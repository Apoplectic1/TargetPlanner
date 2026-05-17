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
    /// field to <see cref="ChartEvaluation"/> -- the cache populates it,
    /// the sub-chart reads it. The straight-line shape is the SoC win;
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
        private readonly Action<ChartContext, ChartEvaluation> mRenderActiveArea;
        private readonly Action<ChartContext> mPostApplyHook;

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
        private IProgress<int> mPendingProgress;
        private bool mDisposed;

        public ChartCoordinator(
            IChartCacheStore cache,
            Action<ChartContext, ChartEvaluation> renderActiveArea,
            Action<ChartContext> postApplyHook,
            int debounceMs = 150)
        {
            mCache = cache ?? throw new ArgumentNullException(nameof(cache));
            mRenderActiveArea = renderActiveArea ?? throw new ArgumentNullException(nameof(renderActiveArea));
            mPostApplyHook = postApplyHook;

            mDebounce = new System.Windows.Forms.Timer { Interval = debounceMs };
            mDebounce.Tick += OnDebounceTick;
        }

        /// <summary>Schedule a pipeline run after the debounce settles. Replaces
        /// any previously-pending context — only the most recent snapshot ever
        /// runs. Returns immediately; the actual pipeline runs on the timer
        /// tick. <paramref name="progress"/> is forwarded to the cache for
        /// per-target completion ticks (used by graph-build callers that drive
        /// a progress bar).</summary>
        public void Apply(ChartContext ctx, IProgress<int> progress = null)
        {
            if (ctx == null || mDisposed) return;
            mPendingContext = ctx;
            mPendingProgress = progress;
            mDebounce.Stop();
            mDebounce.Start();
        }

        /// <summary>Run the pipeline immediately (no debounce). Drops any
        /// pending debounce. Awaitable.</summary>
        public Task ApplyImmediateAsync(ChartContext ctx, IProgress<int> progress = null)
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
                IProgress<int> progress = mPendingProgress;
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

        private async Task RunPipelineAsync(ChartContext ctx, IProgress<int> progress)
        {
            // Capture this pipeline's generation. A newer Apply increments
            // mGeneration; older pipelines that complete their awaits later see
            // gen != mGeneration and bail before any side-effecting write.
            int gen = Interlocked.Increment(ref mGeneration);

            // Pre-stamp intended targets before the awaits so a concurrent Apply()'s
            // no-arg SnapshotCurrent sees the user's CURRENT intent.
            mLastAppliedTargets = ctx.Targets ?? Array.Empty<Target>();

            try
            {
                // Caller (this coordinator) computes the dayKey from the current
                // night window so the cache stays Charts-agnostic. NightCache
                // is built by EnsureAsync's downstream PrepareManyAsync; on first
                // call mCache.LocationNightCache is null and NightCalculator
                // computes a one-shot fallback so dayKey is real on first invocation.
                DayWindowKey dayKey = default;
                NightWindow night = mCache.LocationNightCache?.Starting
                                 ?? NightCalculator.ComputeNight(ctx.Location);
                if (night.IsValid)
                {
                    dayKey = ChartLayout.BuildDayWindow(night).Key;
                }

                if (Log.IsDiagEnabled("Coord"))
                {
                    Log.Diag("Coord",
                        $"Pipeline enter activeArea={ctx.ActiveArea} dayKey.Count={dayKey.Count} " +
                        $"date={ctx.Location.DateTime:yyyy-MM-dd HH:mm}");
                }
                ChartEvaluation eval = await mCache.EnsureAsync(ctx, dayKey);
                if (Log.IsDiagEnabled("Coord"))
                {
                    Log.Diag("Coord",
                        $"Pipeline eval LocationChanged={eval.LocationChanged} " +
                        $"TargetsChanged={eval.TargetsChanged} HdmChanged={eval.HdmChanged} " +
                        $"DayModeChanged={eval.DayModeChanged} Brightness={eval.BrightnessInputsChanged}");
                }

                // Generation guard: a newer Apply has come in while we awaited; bail.
                if (gen != Volatile.Read(ref mGeneration)) return;

                // Render is unconditional. Sub-charts read eval flags to decide
                // whether to short-circuit (Phase 7). The active area's
                // ShowOnlyAltitudeChart + Render + Resize sequence is owned by
                // MainForm.RenderArea; this coordinator doesn't conditionally
                // skip any of it -- the sub-chart owns its own idempotency.
                mRenderActiveArea(ctx, eval);

                // Post-apply hook for label refresh / line position updates /
                // Sky K-S re-walk that don't fit the Render contract.
                mPostApplyHook?.Invoke(ctx);
            }
            catch (Exception ex)
            {
                Log.Error("ChartCoordinator pipeline await threw", ex);
            }
        }
    }
}
