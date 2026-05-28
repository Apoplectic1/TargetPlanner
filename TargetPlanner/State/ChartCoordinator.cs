using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core.Night;
using TargetPlanner.Caches;
using TargetPlanner.Charts;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.State
{
    /// <summary>
    /// Single funnel for chart-state changes. Takes a <see cref="ChartContext"/>
    /// snapshot, delegates pre-render staleness reasoning to the cache via
    /// <see cref="IChartCacheStore.EnsureAsync"/>, then dispatches Render +
    /// post-apply on the active sub-chart. One path. Same path every time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Paradigm.</b> This is the only path from UI to charts. Do not add
    /// side-paths (extra callbacks, conditional dispatch tables, per-area
    /// stamping). If you need to broadcast a new staleness signal, add a
    /// field to <see cref="ChartEvaluation"/> -- the cache populates it and
    /// the post-apply hook reads it. The straight-line shape is the SoC win;
    /// preserving it is how the architecture defends against erosion.
    /// </para>
    /// <para>
    /// <b>Concurrency model.</b> The coordinator is single-threaded (UI thread).
    /// All public methods are called from UI; the async pipeline yields only at
    /// awaited cache work, which marshals back via the captured SyncContext.
    /// Multiple pipelines may overlap during rapid scrubs -- supersession is via
    /// a monotonically increasing <see cref="mGeneration"/> counter. Each
    /// pipeline captures its generation at entry and bails before any
    /// side-effecting write if a newer Apply has run in the meantime. Cache
    /// builds dedupe per-key so overlapping pipelines don't double-compute.
    /// </para>
    /// </remarks>
    public sealed class ChartCoordinator : IDisposable
    {
        private readonly IChartCacheStore mCache;
        private readonly Action<ChartContext, IProgress<(int Done, int Total)>> mRenderActiveArea;
        private readonly Action<ChartContext, ChartEvaluation> mPostApplyHook;
        private readonly Func<IProgress<(int Done, int Total)>> mDefaultProgressFactory;

        private readonly System.Windows.Forms.Timer mDebounce;
        // Monotonically increasing per-Apply counter. Each RunPipelineAsync captures
        // its generation at entry and bails before any side-effecting write if a
        // newer Apply has run in the meantime.
        private int mGeneration;
        // Most recently applied target set, shared across all areas. Pre-stamped
        // at RunPipelineAsync entry (after the generation bump) so a concurrent
        // Apply()'s no-arg SnapshotCurrent reads the user's CURRENT intent
        // rather than the previous successful render, even during cache-cold
        // await windows. Replaces the prior MainForm.mLastRenderedTargets
        // shadow store and the per-area mLastAppliedByArea stamps.
        private IReadOnlyList<Target> mLastAppliedTargets = Array.Empty<Target>();
        private ChartContext mPendingContext;
        private IProgress<(int Done, int Total)> mPendingProgress;
        private bool mDisposed;

        public ChartCoordinator(
            IChartCacheStore cache,
            Action<ChartContext, IProgress<(int Done, int Total)>> renderActiveArea,
            Action<ChartContext, ChartEvaluation> postApplyHook,
            Func<IProgress<(int Done, int Total)>> defaultProgressFactory = null,
            int debounceMs = 150)
        {
            mCache = cache ?? throw new ArgumentNullException(nameof(cache));
            mRenderActiveArea = renderActiveArea ?? throw new ArgumentNullException(nameof(renderActiveArea));
            mPostApplyHook = postApplyHook;
            mDefaultProgressFactory = defaultProgressFactory;

            mDebounce = new System.Windows.Forms.Timer { Interval = debounceMs };
            mDebounce.Tick += OnDebounceTick;
        }

        /// <summary>Schedule a pipeline run after the debounce settles. Replaces
        /// any previously-pending context — only the most recent snapshot ever
        /// runs. Returns immediately; the actual pipeline runs on the timer
        /// tick. Explicit <paramref name="progress"/> overrides the
        /// coordinator's default progress factory for this Apply (used by
        /// callers that need their own sink — e.g. tests). When omitted the
        /// factory builds a fresh sink at pipeline-start so every Apply path
        /// — scrubs, location edits, graph-build — drives the same bar without
        /// per-callsite wrapping.</summary>
        public void Apply(ChartContext ctx, IProgress<(int Done, int Total)> progress = null)
        {
            if (ctx == null || mDisposed) return;
            mPendingContext = ctx;
            mPendingProgress = progress;
            mDebounce.Stop();
            mDebounce.Start();
        }

        /// <summary>Run the pipeline immediately (no debounce). Drops any
        /// pending debounce. Awaitable.</summary>
        public Task ApplyImmediateAsync(ChartContext ctx, IProgress<(int Done, int Total)> progress = null)
        {
            if (ctx == null || mDisposed) return Task.CompletedTask;
            mDebounce.Stop();
            mPendingContext = null;
            mPendingProgress = null;
            return RunPipelineAsync(ctx, progress);
        }

        /// <summary>Drop any pending debounce. In-flight pipelines are not
        /// interrupted; they bail at their generation check before writing
        /// any state, so any subsequent Apply naturally wins.</summary>
        public void Cancel()
        {
            mDebounce.Stop();
            mPendingContext = null;
        }

        /// <summary>Targets list from the most recent <see cref="RunPipelineAsync"/>
        /// invocation. Pre-stamped at pipeline entry (after the generation bump)
        /// so a concurrent <c>Apply</c>'s no-arg <c>SnapshotCurrent</c> reads the
        /// user's current intent rather than the previous successful render, even
        /// during cache-cold await windows. Defaults to
        /// <c>Array.Empty&lt;Target&gt;()</c> before any pipeline has run, and
        /// after a deliberate empty-targets reset (<c>ResetForLocationChange</c>).
        /// </summary>
        public IReadOnlyList<Target> LastAppliedTargets => mLastAppliedTargets;

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            Cancel();
            mDebounce.Tick -= OnDebounceTick;
            mDebounce.Dispose();
        }

        private async void OnDebounceTick(object sender, EventArgs e)
        {
            // async void: any exception escaping this handler crashes the process.
            // Wrap the entire body (including the synchronous prefix) so a stray
            // NRE in the field reads doesn't blow up the form.
            try
            {
                mDebounce.Stop();
                ChartContext pending = mPendingContext;
                IProgress<(int Done, int Total)> progress = mPendingProgress;
                mPendingContext = null;
                mPendingProgress = null;
                if (pending == null) return;
                await RunPipelineAsync(pending, progress);
            }
            catch (Exception ex)
            {
                Log.Error("ChartCoordinator debounce tick threw", ex);
            }
        }

        private async Task RunPipelineAsync(ChartContext ctx, IProgress<(int Done, int Total)> progress)
        {
            // Capture this pipeline's generation. A newer Apply increments
            // mGeneration; older pipelines that complete their awaits later see
            // gen != mGeneration and bail before any side-effecting write.
            int gen = Interlocked.Increment(ref mGeneration);

            // Pre-stamp intended targets before the awaits so a concurrent Apply()'s
            // no-arg SnapshotCurrent sees the user's CURRENT intent.
            mLastAppliedTargets = ctx.Targets ?? Array.Empty<Target>();

            // No explicit sink? Build one from the factory. Per-Apply call so
            // every pipeline gets a fresh generation stamp on its sink (the
            // factory increments MainForm's shared generation counter on
            // creation). Wiring lives at the coordinator construction site;
            // the coordinator itself stays UI-agnostic.
            if (progress == null && mDefaultProgressFactory != null)
            {
                progress = mDefaultProgressFactory();
            }

            try
            {
                // Caller (this coordinator) computes the dayKey from the current
                // night window so the cache stays Charts-agnostic. Always
                // re-derive from ctx.Location + ctx.Observation.Utc -- using
                // mCache.LocationNightCache here would pick up the PREVIOUS
                // location's night on a date scrub (SetLocationAsync inside
                // EnsureAsync hasn't run yet), and the resulting OLD dayKey
                // would mismatch the sub-chart's post-SetLocation NEW dayKey
                // -> every cache.GetDayOrNull / GetMoonOrNull returns null and
                // targets disappear from Day. NightCalculator.ComputeNight is
                // sub-millisecond Meeus math; recomputing per pipeline is
                // cheap insurance against stale-key bugs.
                DayWindowKey dayKey = default;
                NightWindow night = NightCalculator.ComputeNight(ctx.Location, ctx.Observation.Utc);
                if (night.IsValid)
                {
                    dayKey = ChartLayout.BuildDayWindow(night, ctx.Observation.Zone).Key;
                }

                if (Log.IsDiagEnabled("Coord"))
                {
                    Log.Diag("Coord",
                        $"Pipeline enter activeArea={ctx.ActiveArea} dayKey.Count={dayKey.Count} " +
                        $"obs={ctx.Observation.Utc:yyyy-MM-dd HH:mm}Z zone={ctx.Observation.Zone?.Id ?? "(null)"}");
                }
                ChartEvaluation eval = await mCache.EnsureAsync(ctx, dayKey, progress);

                // Generation guard: a newer Apply has come in while we awaited; bail.
                if (gen != Volatile.Read(ref mGeneration)) return;

                // Bar surfaces only when EnsureAsync did real work (warm-cache
                // scrubs returned EnsureWork == 0 and never issued a Report,
                // so the sink stayed hidden). When the cache ran prep, wrap
                // the sink with an OffsetProgress so Render's per-target ticks
                // continue Done from where EnsureAsync left off — bar advances
                // smoothly across the two phases without a Maximum resize.
                IProgress<(int Done, int Total)> renderProgress =
                    (progress != null && eval.EnsureWork > 0)
                        ? new OffsetProgress(progress, eval.EnsureWork, eval.EnsureWork + eval.RenderWork)
                        : null;

                // Render is unconditional. The active area's ShowOnlyAltitudeChart
                // + Render + Resize sequence is owned by MainForm.RenderArea; this
                // coordinator doesn't conditionally skip any of it -- the sub-chart
                // owns its own idempotency.
                mRenderActiveArea(ctx, renderProgress);

                // Post-apply hook for label refresh / line position updates /
                // Sky K-S re-walk that don't fit the Render contract. The hook
                // receives the cache's ChartEvaluation so it can gate expensive
                // steps (e.g. the Sky K-S re-walk on eval.BrightnessInputsChanged).
                mPostApplyHook?.Invoke(ctx, eval);
            }
            catch (Exception ex)
            {
                Log.Error("ChartCoordinator pipeline await threw", ex);
            }
        }

        // Translates Render's local (Done, Total) ticks into the outer sink's
        // coordinate system. Render reports (0..renderWork, renderWork) from
        // its own loop; this adapter forwards (offset+done, total) so the bar
        // sees cumulative progress across EnsureAsync + Render with a constant
        // Maximum. Single use site (RunPipelineAsync above); kept private.
        private sealed class OffsetProgress : IProgress<(int Done, int Total)>
        {
            private readonly IProgress<(int Done, int Total)> mInner;
            private readonly int mOffset;
            private readonly int mTotal;
            public OffsetProgress(IProgress<(int Done, int Total)> inner, int offset, int total)
            { mInner = inner; mOffset = offset; mTotal = total; }
            public void Report((int Done, int Total) value)
                => mInner.Report((mOffset + value.Done, mTotal));
        }
    }
}
