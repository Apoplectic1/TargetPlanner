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
        // Phase 4: render callback now receives the typed ChartEvaluation so
        // the sub-chart can short-circuit (Phase 7) once eval is populated by
        // EnsureAsync (Phase 5). Refresh / showOnly stay (ctx)-only -- they're
        // collapsing in Phase 6 when the dispatch table goes away.
        private readonly Action<ChartContext, ChartEvaluation> mRenderActiveArea;
        private readonly Action<ChartContext> mRefreshActiveArea;
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
        //
        // Pre-stamped at RunPipelineAsync entry (after the generation bump), not
        // at end-of-success, so a concurrent Apply()'s no-arg SnapshotCurrent
        // sees the user's CURRENT intent rather than the previous successful
        // render -- matters during the ~2 sec cache-cold pipeline-await window
        // when checkbox-toggle and radio-click races would otherwise stamp a
        // stale target set.
        private IReadOnlyList<Target> mLastAppliedTargets = Array.Empty<Target>();
        private ChartContext mPendingContext;
        private IProgress<int> mPendingProgress;
        private bool mDisposed;

        public ChartCoordinator(
            IChartCacheStore cache,
            Action<ChartContext, ChartEvaluation> renderActiveArea,
            Action<ChartContext> refreshActiveArea,
            Action<ChartContext> showOnlyActiveArea,
            Action<ChartContext> postApplyHook,
            int debounceMs = 150)
        {
            mCache = cache ?? throw new ArgumentNullException(nameof(cache));
            mRenderActiveArea = renderActiveArea ?? throw new ArgumentNullException(nameof(renderActiveArea));
            mRefreshActiveArea = refreshActiveArea ?? throw new ArgumentNullException(nameof(refreshActiveArea));
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
        /// invocation, regardless of which area was active. Pre-stamped at pipeline
        /// entry (after the generation bump) so a concurrent <c>Apply</c>'s no-arg
        /// <c>SnapshotCurrent</c> reads the user's current intent rather than the
        /// previous successful render, even during cache-cold await windows.
        /// Defaults to <c>Array.Empty&lt;Target&gt;()</c> before any pipeline has
        /// run, and after a deliberate empty-targets reset
        /// (<c>ResetForLocationChange</c>). Use this on radio-only swaps where the
        /// newly-active area's stamp doesn't exist yet -- the targets carry across
        /// sub-charts so the right thing is "what's the user currently viewing,"
        /// not "what's this specific area's last seen target set."
        /// <para>
        /// Semantic: "last intended to render" (may be in-flight, may have bailed),
        /// not "last successfully rendered." Generation-bail leaves the stamp at
        /// the bailed pipeline's intent, but a newer pipeline's own pre-stamp has
        /// already overwritten by then. Exception-bail in the cache awaits leaves
        /// the stamp at unrendered intent; same chart-stuck outcome as the prior
        /// "stamp at end of success" path, no regression.
        /// </para></summary>
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

            // Pre-stamp intended targets: a concurrent Apply()'s no-arg
            // SnapshotCurrent reads mLastAppliedTargets during this pipeline's
            // ~2 sec cache-cold await window. Stamping BEFORE the awaits ensures
            // the second Apply sees the user's CURRENT intent, not the previous
            // render. Without this, a checkbox toggle (which routes through
            // mCheckedToggleDebounce -> RunGraphBuildAsync -> ApplyImmediateAsync,
            // taking ~2 sec to complete cold) followed by a radio click captures
            // the pre-toggle targets via the no-arg snapshot and persists the
            // stale render until user-driven recovery (Button_CheckedTargets).
            // Bail-safe: a generation-bailed pipeline leaves the stamp pointing
            // at intent that was already superseded by the newer pipeline's own
            // pre-stamp; an exception-bailed pipeline leaves the stamp pointing
            // at unrendered intent (same outcome as the prior "stamp at end of
            // success" path -- no regression).
            mLastAppliedTargets = ctx.Targets ?? Array.Empty<Target>();

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

            // Phase 4: dayKey is hoisted so the ChartEvaluation constructed at
            // dispatch time has a real DayWindowKey when the night is valid.
            // Falls back to default(DayWindowKey) on the empty-targets or polar-
            // night branches (sub-charts ignore eval keys in Phase 4 anyway).
            DayWindowKey dayKey = default;

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
                        dayKey = dayWindow.Key;
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

            // 3. Dispatch one of three paths:
            //    a) Full Render of the active area when its altitude geometry is
            //       stale (never rendered, or location/targets changed since).
            //       Inactive sub-charts hold no per-area derived state (fits
            //       live in the cache, keyed on (Target, HdmKey)) so they don't
            //       need a "stay current" callback -- a subsequent radio swap
            //       hits the cache via Render.
            //    b) Hdm-only changed on an already-rendered area: dispatch to
            //       the lightweight RefreshVisibility path. Altitude geometry is
            //       unchanged, only fits + per-target visibility shift -- and
            //       crucially, the Day chart's HD overlay backups stay valid
            //       across the refresh. Routing H/D/M and filter-wavelength
            //       scrubs through here keeps active overlays sticky.
            //    c) Active area data is fully current: skip work, flip
            //       visibility only. Instant toggle.
            bool activeNeedsFullRender = !activeEverRendered
                || locationKeyChanged || targetsChanged;
            // DayMode is a Day-chart-only target-filter knob (Floor / Meridian /
            // Wall). It doesn't enter HdmKey -- no cache state changes -- so a
            // mode flip routes through RefreshVisibility (re-evaluates the per-
            // target filter + re-renders any active HD overlays against the
            // refreshed mTargetWindows). Bundled into the hdm-only branch so a
            // mode change while Day is active triggers the lightweight refresh
            // path; other areas ignore DayMode in their Render/RefreshVisibility
            // so a stray mode flip while Year/Sessions/Sky is active is a cheap
            // no-op refresh.
            bool dayModeChanged = activeEverRendered && prev.DayMode != ctx.DayMode;
            bool hdmOnlyChanged = activeEverRendered
                && !locationKeyChanged
                && !targetsChanged
                && (hdmKeyChanged || dayModeChanged);
            if (activeNeedsFullRender)
            {
                // mRenderActiveArea is MainForm.RenderArea -- ShowOnlyAltitudeChart
                // (flips Visible per sub-chart) + Render + ResizeAltitudeChartArea.
                // Phase 4: hand the sub-chart a ChartEvaluation. FullChange flags
                // everything stale so sub-charts that haven't migrated to
                // short-circuit logic still take their full Render path. Phase 5's
                // EnsureAsync replaces this construction with real per-axis diffs.
                ChartEvaluation eval = ChartEvaluation.FullChange(dayKey, ctx.Hdm, ctx.DayMode);
                mRenderActiveArea(ctx, eval);
                mEverRendered.Add(activeArea);
            }
            else if (hdmOnlyChanged)
            {
                // mRefreshActiveArea is MainForm.RefreshArea -- ShowOnlyAltitudeChart
                // + sub-chart.RefreshVisibility + ResizeAltitudeChartArea. The Hdm
                // cache prep above (PrepareFitsAsync) ran before this dispatch, so
                // RefreshVisibility reads warm fits for the new HdmKey.
                mRefreshActiveArea(ctx);
            }
            else
            {
                mShowOnlyActiveArea(ctx);
            }

            // 4. Post-apply hook for label refresh / line position updates
            //    that don't fit the Render or RefreshVisibility contracts.
            mPostApplyHook?.Invoke(ctx);

            // 5. Stamping policy: only stamp the active area, only when it was
            //    actually Render'd (or RefreshVisibility'd) with this ctx.
            //    - full-render: active rendered, stamp it.
            //    - hdm-refresh: active refreshed, stamp it.
            //    - showOnly: no work done. Don't stamp anything. The active
            //      area's existing stamp is already cache-equivalent to ctx
            //      (that's WHY showOnly fired); restamping is a no-op for
            //      diff purposes. Inactive areas' stamps stay at whatever ctx
            //      they were last actually rendered with -- crucial because
            //      stamping them now would LIE about their on-screen state,
            //      causing a subsequent activation to take the showOnly fast
            //      path and show stale chart bounds (e.g. Sky's MinLimit /
            //      gradient frozen at a previous date). The prior "stamp all
            //      on showOnly" behavior conflated cache-state currency with
            //      rendered-state currency, which only coincide for the
            //      active area.
            if (activeNeedsFullRender || hdmOnlyChanged)
            {
                mLastAppliedByArea[activeArea] = ctx;
            }

            // (mLastAppliedTargets is pre-stamped at pipeline entry above; no
            // end-of-pipeline stamp is needed because ChartContext is immutable
            // and ctx.Targets cannot change between entry and here.)
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
            // DateTime.Date is included so a DatePicker scrub (date change within
            // the same month) invalidates the cache. Year-start-day alone misses
            // sub-month date changes, which leaves mNightCache.Starting stale --
            // moon series, dusk/dawn gradients, per-target altitudes, and
            // Tonight fits all date-sensitive. Cost is full cache rebuild on
            // every date pick (~2-4 sec for 44 targets per the perf budget);
            // acceptable because DatePicker is click-driven, not scrubbed.
            // TimePicker-only scrubs (date unchanged) still skip cache rebuild.
            return a.Latitude  == b.Latitude
                && a.Longitude == b.Longitude
                && a.North     == b.North
                && a.West      == b.West
                && a.Elevation == b.Elevation
                && a.DateTime.Date == b.DateTime.Date
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
