using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core.Night;
using TargetPlanner.Caches;
using TargetPlanner.Charts;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.State
{
    /// <summary>
    /// Single funnel for chart-state changes. Takes a <see cref="ChartContext"/>
    /// snapshot, diffs against the last successfully-applied snapshot, and
    /// dispatches the right combination of cache (re)build + chart render +
    /// visibility refresh + post-apply UI sync.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Concurrency model.</b> The coordinator is single-threaded (UI thread).
    /// All public methods are called from UI; the async pipeline yields only at
    /// awaited cache work, which marshals back via the captured SyncContext.
    /// Multiple pipelines may overlap during rapid scrubs — supersession is via
    /// a monotonically increasing <see cref="mGeneration"/> counter. Each
    /// pipeline captures its generation at entry and bails before any
    /// side-effecting write (Render / ShowOnly / stamp) if a newer Apply has
    /// run in the meantime. Cache builds dedupe per-key so overlapping
    /// pipelines don't double-compute.
    /// </para>
    /// </remarks>
    public sealed class ChartCoordinator : IDisposable
    {
        private readonly IChartCacheStore mCache;
        private readonly Action<ChartContext> mRenderActiveArea;
        private readonly Action<ChartContext> mShowOnlyActiveArea;
        private readonly Action<ChartContext> mPostApplyHook;

        private readonly System.Windows.Forms.Timer mDebounce;
        // Per-area "last applied" tracker. Each sub-chart's data-currency is
        // tracked independently so a radio swap to an already-current area
        // (data unchanged since that area's last Render or RefreshVisibility)
        // can skip the expensive Render and just flip visibility.
        //
        // An area gets stamped only after it has been Render'd at least once
        // (membership in mEverRendered). RefreshVisibility on a never-rendered
        // area early-returns (mSeriesByTarget empty), so stamping it would
        // create a phantom-current stamp leading to a ShowOnly-of-empty-chart
        // bug on the next radio click. The mEverRendered gate prevents that.
        private readonly Dictionary<string, ChartContext> mLastAppliedByArea
            = new Dictionary<string, ChartContext>(StringComparer.Ordinal);
        private readonly HashSet<string> mEverRendered
            = new HashSet<string>(StringComparer.Ordinal);
        // Monotonically increasing per-Apply counter. Each RunPipelineAsync captures
        // its generation at entry and bails before any side-effecting write if a
        // newer Apply has run in the meantime. Replaces the prior mPipelineCts.
        private int mGeneration;
        // Most recently applied target set, shared across all areas. Radio swaps
        // re-use the same targets across sub-charts; this is the SoT that
        // SnapshotCurrent() reads on a radio-only Apply (when LastAppliedFor of
        // the newly-active area is null because it's never been rendered).
        // Replaces MainForm's prior mLastRenderedTargets shadow store.
        private IReadOnlyList<Target> mLastAppliedTargets = Array.Empty<Target>();
        private ChartContext mPendingContext;
        private IProgress<int> mPendingProgress;
        private bool mDisposed;

        public ChartCoordinator(
            IChartCacheStore cache,
            Action<ChartContext> renderActiveArea,
            Action<ChartContext> showOnlyActiveArea,
            Action<ChartContext> postApplyHook,
            int debounceMs = 150)
        {
            mCache = cache ?? throw new ArgumentNullException(nameof(cache));
            mRenderActiveArea = renderActiveArea ?? throw new ArgumentNullException(nameof(renderActiveArea));
            mShowOnlyActiveArea = showOnlyActiveArea ?? throw new ArgumentNullException(nameof(showOnlyActiveArea));
            mPostApplyHook = postApplyHook;

            mDebounce = new System.Windows.Forms.Timer { Interval = debounceMs };
            mDebounce.Tick += OnDebounceTick;
        }

        /// <summary>Schedule a pipeline run after the debounce settles. Replaces
        /// any previously-pending context — only the most recent snapshot ever
        /// runs. Returns immediately; the actual pipeline runs on the timer
        /// tick. <paramref name="progress"/> is forwarded to the cache's
        /// <c>PrepareManyAsync</c> for per-target completion ticks (used by
        /// graph-build callers that drive a progress bar).</summary>
        public void Apply(ChartContext ctx, IProgress<int> progress = null)
        {
            if (ctx == null || mDisposed) return;
            mPendingContext = ctx;
            mPendingProgress = progress;
            mDebounce.Stop();
            mDebounce.Start();
        }

        /// <summary>Run the pipeline immediately (no debounce). Drops any
        /// pending debounce. Awaitable. <paramref name="progress"/> is
        /// forwarded to the cache's <c>PrepareManyAsync</c>.</summary>
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

        /// <summary>Returns the last successfully-applied <see cref="ChartContext"/>
        /// for the given chart area, or <see langword="null"/> if the area has
        /// never been rendered.</summary>
        public ChartContext LastAppliedFor(string area)
        {
            if (area == null) return null;
            if (!mEverRendered.Contains(area)) return null;
            mLastAppliedByArea.TryGetValue(area, out ChartContext ctx);
            return ctx;
        }

        /// <summary>Targets list from the most recent <see cref="RunPipelineAsync"/>
        /// that reached the stamping block, regardless of which area was active.
        /// Defaults to <c>Array.Empty&lt;Target&gt;()</c> before any pipeline has
        /// run, and after a deliberate empty-targets reset
        /// (<c>ResetForLocationChange</c>). Use this on radio-only swaps where the
        /// newly-active area's stamp doesn't exist yet -- the targets carry across
        /// sub-charts so the right thing is "what's the user currently viewing,"
        /// not "what's this specific area's last seen target set."</summary>
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
            // Interlocked + Volatile fence the read/write so a stray off-UI-thread
            // Apply doesn't lose its bump (today every caller is UI-thread but the
            // primitive is cheap; defensive in case the contract ever softens).
            int gen = Interlocked.Increment(ref mGeneration);

            // Per-area diff: compare the new ctx against what the *active area*
            // was last brought current with, not against a global last-applied.
            // An area is "ever rendered" iff mEverRendered contains its name --
            // RefreshVisibility on a never-rendered area is a no-op (mSeriesByTarget
            // empty), so we don't trust stamps for areas that haven't been
            // Render'd at least once. mEverRendered.Contains(area) gates whether
            // the area has a meaningful prev stamp.
            string activeArea = ctx.ActiveArea;
            bool activeEverRendered = mEverRendered.Contains(activeArea);
            ChartContext prev = activeEverRendered
                ? mLastAppliedByArea[activeArea]
                : null;

            bool locationKeyChanged = !activeEverRendered
                || !LocationCacheEquivalent(prev.Location, ctx.Location);
            bool targetsChanged = !activeEverRendered
                || !TargetsEqualByReference(prev.Targets, ctx.Targets);
            bool hdmKeyChanged = !activeEverRendered || prev.Hdm != ctx.Hdm;

            // 1. Cache re-key on geometry change. SetLocationAsync is a no-op
            //    when the new Location already matches by reference (identity
            //    comparison happens inside the cache); LocationCacheEquivalent
            //    is the value-comparison gate that prevents an unnecessary
            //    drop+rebuild when only DateTime changed within a year.
            try
            {
                if (locationKeyChanged) await mCache.SetLocationAsync(ctx.Location);

                // 2. Cache pre-population for targets the renderer is about to read.
                //    PrepareManyAsync no-ops on already-warm entries; first call
                //    after a SetLocationAsync rebuilds from scratch.
                if (ctx.Targets != null && ctx.Targets.Count > 0)
                {
                    await mCache.PrepareManyAsync(ctx.Targets, progress);

                    // 2b. Per-(target, HdmKey) fit pre-pop. Internally de-duped per
                    //     (Target, HdmKey); when fits for the current HdmKey are
                    //     already built this is a no-op. The horizon profile from
                    //     PlanningPolicy is passed through to BestSession.ResolveCandidates
                    //     -- scalar today, polyline once PR-5 LocalHorizon lands.
                    await mCache.PrepareFitsAsync(
                        ctx.Targets, ctx.Hdm, ctx.Policy.LocalHorizon, progress);

                    // 2c. Per-(target, DayWindowKey) altitude-curve pre-pop. Independent
                    //     of HdmKey -- altitude depends on geometry + time, not policy --
                    //     so HDM scrubs hit warm cache. The night window is read off the
                    //     cache's LocationNightCache (built by PrepareManyAsync above);
                    //     when the night is invalid (polar etc) we skip the prep and
                    //     Day.Render's IsValid check paints a blank chart.
                    NightWindow night = mCache.LocationNightCache?.Starting
                                     ?? NightCalculator.ComputeNight(ctx.Location);
                    if (night.IsValid)
                    {
                        var dayWindow = ChartLayout.BuildDayWindow(night);
                        await mCache.PrepareDayAsync(ctx.Targets, dayWindow.Key, progress);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ChartCoordinator pipeline await threw", ex);
                return;
            }

            // Generation guard: a newer Apply has come in while we awaited; bail.
            if (gen != Volatile.Read(ref mGeneration)) return;

            // 3. Dispatch one of two paths:
            //    a) Full Render of the active area when its data is stale
            //       (never rendered, or location/targets/HdmKey changed since).
            //       Inactive sub-charts hold no per-area derived state (fits
            //       live in the cache, keyed on (Target, HdmKey)) so they don't
            //       need a "stay current" callback -- a subsequent radio swap
            //       hits the cache via Render.
            //    b) Active area data is current: skip Render, flip visibility.
            //       Instant toggle.
            bool activeNeedsFullRender = !activeEverRendered
                || locationKeyChanged || targetsChanged || hdmKeyChanged;
            if (activeNeedsFullRender)
            {
                // mRenderActiveArea is MainForm.RenderArea -- ShowOnlyAltitudeChart
                // (flips Visible per sub-chart) + Render + ResizeAltitudeChartArea.
                mRenderActiveArea(ctx);
                mEverRendered.Add(activeArea);
            }
            else
            {
                mShowOnlyActiveArea(ctx);
            }

            // 4. Post-apply hook for label refresh / line position updates
            //    that don't fit the Render or RefreshVisibility contracts.
            mPostApplyHook?.Invoke(ctx);

            // 5. Stamping policy is path-dependent.
            //    - full-render: only the active area was actually Render'd
            //      with this ctx. Inactives may be stale w.r.t. targets /
            //      HdmKey; leave their stamps untouched so the next click
            //      triggers a proper full Render.
            //    - showOnly: no work; the diff guarantees every ever-
            //      rendered area is already current with ctx. Safe to
            //      stamp all (often a no-op).
            if (activeNeedsFullRender)
            {
                mLastAppliedByArea[activeArea] = ctx;
            }
            else
            {
                foreach (string area in mEverRendered)
                    mLastAppliedByArea[area] = ctx;
            }

            // Stamp the shared "current targets" SoT used by SnapshotCurrent() (no-arg).
            // Always update -- including to empty after a deliberate reset -- so the
            // next no-arg snapshot reflects the actual current state.
            mLastAppliedTargets = ctx.Targets ?? Array.Empty<Target>();
        }

        // -------------- diff helpers --------------

        // Duplicates MainForm.LocationsCacheEquivalent intentionally during the
        // Phase 2 transition; both forms read from the same Location surface and
        // share the cache-keying contract. Phase 3 promotes one canonical
        // implementation (likely a static on ChartCacheStore since it's a
        // property of the cache's keying) and drops the duplicate.
        private static bool LocationCacheEquivalent(Location a, Location b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Latitude  == b.Latitude
                && a.Longitude == b.Longitude
                && a.North     == b.North
                && a.West      == b.West
                && a.Elevation == b.Elevation
                && NightCache.ComputeYearStartDay(a.DateTime)
                == NightCache.ComputeYearStartDay(b.DateTime);
        }

        // Reference-equality element compare. Targets are immutable instances
        // (Astronomy.Core.Targets.Target.With(...) returns a new instance), so
        // a UI flow that re-builds the same logical target produces a different
        // reference. The chart pipeline re-creates the snapshot's Targets list
        // from the same KnownTargets backing store, so reference identity does
        // hold for "user toggled checkboxes / clicked Graph" — which is the
        // common case Phase 2 cares about. Phase 3 may switch to a content
        // hash if Re-Loading targets becomes a frequent diff trigger.
        private static bool TargetsEqualByReference(IReadOnlyList<Target> a, IReadOnlyList<Target> b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!object.ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

    }
}
