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
    /// Phase 2 of the orchestration-layer refactor (see plan
    /// <c>~/.claude/plans/high-level-refactoring-goals-separation-moonlit-clarke.md</c>).
    /// Replaces the per-handler decision trees previously scattered across
    /// MainForm event handlers — every handler now reduces to "build a snapshot
    /// delta and call <see cref="Apply"/> or <see cref="ApplyImmediateAsync"/>";
    /// the coordinator decides what's affected downstream.
    ///
    /// <para>
    /// <b>Concurrency model.</b> The coordinator is single-threaded (UI thread).
    /// All public methods are called from UI; the async pipeline yields only at
    /// awaited cache work, which marshals back via the captured SyncContext.
    /// One pipeline runs at a time — a new <see cref="ApplyImmediateAsync"/>
    /// cancels the in-flight one's CTS so its <c>await</c>s throw OCE and the
    /// cleanup catch swallows it. <see cref="Apply"/> debounces the same
    /// channel: rapid Apply calls only ever turn into one pipeline run after
    /// the debounce settles.
    /// </para>
    ///
    /// <para>
    /// <b>Phase 2 scope.</b> Only the location-pipe (combo location pick + lat/
    /// lon/elev scrubs that cross <c>LocationsCacheEquivalent</c>) routes
    /// through the coordinator. Non-keying scrubs (Bortle / Extinction /
    /// Horizon / Duration / Filter / Moon) still go through MainForm's legacy
    /// SessionsRebuildDebounce until Phase 3 finishes the migration.
    /// </para>
    /// </remarks>
    public sealed class ChartCoordinator : IDisposable
    {
        private readonly IChartCacheStore mCache;
        private readonly Action<ChartContext, CancellationToken> mRenderActiveArea;
        private readonly Action<ChartContext> mShowOnlyActiveArea;
        private readonly Func<string, IAltitudeSubChart> mResolveSubChart;
        private readonly Func<IEnumerable<IAltitudeSubChart>> mResolveAllSubCharts;
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
        private CancellationTokenSource mPipelineCts;
        private ChartContext mPendingContext;
        private IProgress<int> mPendingProgress;
        private bool mDisposed;

        public ChartCoordinator(
            IChartCacheStore cache,
            Action<ChartContext, CancellationToken> renderActiveArea,
            Action<ChartContext> showOnlyActiveArea,
            Func<string, IAltitudeSubChart> resolveSubChart,
            Func<IEnumerable<IAltitudeSubChart>> resolveAllSubCharts,
            Action<ChartContext> postApplyHook,
            int debounceMs = 150)
        {
            mCache = cache ?? throw new ArgumentNullException(nameof(cache));
            mRenderActiveArea = renderActiveArea ?? throw new ArgumentNullException(nameof(renderActiveArea));
            mShowOnlyActiveArea = showOnlyActiveArea ?? throw new ArgumentNullException(nameof(showOnlyActiveArea));
            mResolveSubChart = resolveSubChart ?? throw new ArgumentNullException(nameof(resolveSubChart));
            mResolveAllSubCharts = resolveAllSubCharts ?? throw new ArgumentNullException(nameof(resolveAllSubCharts));
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

        /// <summary>Run the pipeline immediately (no debounce). Cancels any
        /// in-flight pipeline and any pending debounce. Awaitable. <paramref
        /// name="progress"/> is forwarded to the cache's <c>PrepareManyAsync</c>.
        /// </summary>
        public Task ApplyImmediateAsync(ChartContext ctx, IProgress<int> progress = null)
        {
            if (ctx == null || mDisposed) return Task.CompletedTask;
            mDebounce.Stop();
            mPendingContext = null;
            mPendingProgress = null;
            return SupersedeAndRunAsync(ctx, progress);
        }

        /// <summary>Cancel any in-flight pipeline and drop any pending context
        /// without running.</summary>
        public void Cancel()
        {
            mDebounce.Stop();
            mPendingContext = null;
            try { mPipelineCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            Cancel();
            mDebounce.Tick -= OnDebounceTick;
            mDebounce.Dispose();
            try { mPipelineCts?.Dispose(); }
            catch (ObjectDisposedException) { }
        }

        private async void OnDebounceTick(object sender, EventArgs e)
        {
            mDebounce.Stop();
            ChartContext pending = mPendingContext;
            IProgress<int> progress = mPendingProgress;
            mPendingContext = null;
            mPendingProgress = null;
            if (pending == null) return;
            try { await SupersedeAndRunAsync(pending, progress); }
            catch (Exception ex) { Log.Error("ChartCoordinator debounce tick threw", ex); }
        }

        private async Task SupersedeAndRunAsync(ChartContext ctx, IProgress<int> progress)
        {
            try { mPipelineCts?.Cancel(); }
            catch (ObjectDisposedException) { }

            CancellationTokenSource cts = new CancellationTokenSource();
            mPipelineCts = cts;
            CancellationToken ct = cts.Token;

            try { await RunPipelineAsync(ctx, progress, ct); }
            catch (OperationCanceledException) { /* superseded; expected */ }
            catch (Exception ex) { Log.Error("ChartCoordinator pipeline threw", ex); }
            finally
            {
                if (object.ReferenceEquals(mPipelineCts, cts))
                {
                    try { cts.Dispose(); } catch (ObjectDisposedException) { }
                    mPipelineCts = null;
                }
            }
        }

        private async Task RunPipelineAsync(ChartContext ctx, IProgress<int> progress, CancellationToken ct)
        {
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
            bool hdmChanged = activeEverRendered && HdmChanged(prev, ctx);

            // 1. Cache re-key on geometry change. SetLocationAsync is a no-op
            //    when the new Location already matches by reference (identity
            //    comparison happens inside the cache); LocationCacheEquivalent
            //    is the value-comparison gate that prevents an unnecessary
            //    drop+rebuild when only DateTime changed within a year.
            if (locationKeyChanged)
            {
                await mCache.SetLocationAsync(ctx.Location);
                ct.ThrowIfCancellationRequested();
            }

            // 2. Cache pre-population for targets the renderer is about to read.
            //    PrepareManyAsync no-ops on already-warm entries; first call
            //    after a SetLocationAsync rebuilds from scratch.
            if (ctx.Targets != null && ctx.Targets.Count > 0)
            {
                await mCache.PrepareManyAsync(ctx.Targets, ct, progress);
                ct.ThrowIfCancellationRequested();
            }

            // 3. Dispatch one of three paths:
            //    a) Full Render of the active area when its data is stale
            //       (never rendered, or location/targets changed since).
            //       Inactive areas get RefreshVisibility so they stay current.
            //    b) HDM-only: refresh fit on every sub-chart (active + inactive),
            //       then ShowOnly the active area. Covers same-area HMD scrub
            //       (ShowOnly is a no-op) and HMD scrub + radio swap in the
            //       same Apply (ShowOnly flips to the newly-active area whose
            //       data was just refreshed in the foreach).
            //    c) Only ActiveArea changed (data current): skip Render, just
            //       flip visibility to the new active area. Instant toggle.
            bool activeNeedsFullRender = !activeEverRendered || locationKeyChanged || targetsChanged;
            if (activeNeedsFullRender)
            {
                // mRenderActiveArea is MainForm.RenderArea -- ShowOnlyAltitudeChart
                // (flips Visible per sub-chart) + Render + ResizeAltitudeChartArea.
                mRenderActiveArea(ctx, ct);
                ct.ThrowIfCancellationRequested();
                mEverRendered.Add(activeArea);

                // Inactive charts: refresh visibility so they stay current when
                // the user switches to them. Sub-charts with empty mSeriesByTarget
                // (never rendered yet) early-return cheaply -- they don't get
                // added to mEverRendered here, only via their own activeNeedsFullRender
                // path when the user clicks their radio.
                IAltitudeSubChart active = mResolveSubChart(activeArea);
                foreach (IAltitudeSubChart sc in mResolveAllSubCharts())
                {
                    if (sc != null && !object.ReferenceEquals(sc, active))
                        sc.RefreshVisibility(ctx, mCache);
                }
            }
            else if (hdmChanged)
            {
                foreach (IAltitudeSubChart sc in mResolveAllSubCharts())
                {
                    sc?.RefreshVisibility(ctx, mCache);
                }
                mShowOnlyActiveArea(ctx);
            }
            else
            {
                mShowOnlyActiveArea(ctx);
            }

            // 4. Post-apply hook for label refresh / line position updates
            //    that don't fit the Render or RefreshVisibility contracts.
            mPostApplyHook?.Invoke(ctx);

            // 5. Stamping policy is path-dependent. RefreshVisibility on a
            //    sub-chart with empty mSeriesByTarget early-returns (no work),
            //    so when the active area's targets change, the inactive
            //    foreach-RefreshVisibility loop does NOT bring an empty-state
            //    inactive current with the new targets. Stamping it anyway
            //    would falsely mark it "current with new targets", causing the
            //    next radio click to take the showOnly fast path and reveal an
            //    empty chart -- the original bug repro:
            //      form-load Day Render with [] -> stamp Day, [] ->
            //      check Sky -> Sky Render with [] -> only Sky stamped ->
            //      SelectAll -> Sky Render with [44] (Day still empty) ->
            //      stamp ALL with [44] (BUG: Day's stamp is now [44] but
            //      Day's mSeriesByTarget is still []) -> uncheck Sky ->
            //      Day diff against stamp [44] vs ctx [44] -> showOnly ->
            //      Day chart shows nothing.
            //
            //    Path-by-path correctness:
            //    - full-render: only the active area was actually Render'd
            //      with this ctx. Inactives may be stale w.r.t. targets;
            //      leave their stamps untouched so the next click triggers
            //      a proper full Render.
            //    - hdmChanged: foreach RefreshVisibility brought every
            //      non-empty area current with the new HDM. Empty-state
            //      inactives are vacuously current (their nothing-to-show
            //      output is HDM-invariant). Safe to stamp all.
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

        // Horizon / Duration / MoonProfile / FilterCenter / Bortle / ExtinctionK
        // — non-keying inputs that change visibility or sky-brightness without
        // dropping the cache. Phase 2 only flags HDM differences for completeness;
        // the actual non-keying scrub handlers don't route through the
        // coordinator yet (Phase 3).
        private static bool HdmChanged(ChartContext prev, ChartContext now)
        {
            return prev.Location.Horizon       != now.Location.Horizon
                || prev.Location.Duration      != now.Location.Duration
                || !object.ReferenceEquals(prev.MoonProfile, now.MoonProfile)
                || prev.ActiveFilterCenterNm   != now.ActiveFilterCenterNm
                || prev.Location.BortleClass   != now.Location.BortleClass
                || prev.Location.ExtinctionK   != now.Location.ExtinctionK;
        }
    }
}
