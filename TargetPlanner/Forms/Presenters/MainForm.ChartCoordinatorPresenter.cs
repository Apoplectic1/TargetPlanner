using System;
using TargetPlanner.State;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Chart-coordinator boot-wiring concern: construction of mCoordinator plus
    // the two adjacent boot steps that depend on it. Lifted out of the MainForm
    // constructor -- partial-class file split, same pattern as the other
    // presenter partials.
    //
    // What's here:
    //   * WireDayChartModeChanged -- routes the Day-chart placement-strategy
    //     radios (Floor / Meridian / Wall) through the coordinator's single
    //     Render seam.
    //   * ConstructCoordinator -- builds mCoordinator with its post-apply hook
    //     (the one place that runs Render-adjacent side effects: astrometry
    //     labels, now-line / horizon-line repositioning, Sky's K-S re-walk).
    //     The hook is the load-bearing piece -- when a new side effect needs
    //     to ride the coordinator's pipeline, it lands here.
    //   * FireBaselinePaint -- hands the coordinator an empty-targets snapshot
    //     so the chart paints its non-target scaffolding at boot instead of
    //     staying blank-gray until the user gestures.
    //
    // Call order in the constructor is load-bearing:
    //   1. WireDayChartModeChanged after mLC2Day is constructed.
    //   2. ConstructCoordinator after mCache + mSubCharts exist (it captures
    //      both via the resolver delegates).
    //   3. FireBaselinePaint after the coordinator exists.
    public partial class MainForm
    {
        // Day-chart placement-strategy radios (Floor / Meridian / Wall) live
        // inside the Day sub-chart's plot area. The radio CheckedChanged fires
        // this event; route through the coordinator's snapshot pipeline so the
        // new DayMode reaches the active sub-chart through the single Render
        // seam (no cache change -- the mode is a pure visibility filter on top
        // of NightFit.CenteredFloor).
        private void WireDayChartModeChanged()
        {
            mLC2Day.DayChartModeChanged += (s, e) =>
            {
                mDayChartMode = mLC2Day.Mode;
                mCoordinator?.Apply(SnapshotCurrent());
            };
        }

        // Construct the coordinator after both mCache and mSubCharts exist
        // (it captures references via the resolver delegates). The post-apply
        // hook is the one place that runs side-effects which don't fit Render:
        //   - Astrometry labels (dawn/dusk/sun/moon altitude/phase/illumination).
        //   - Now-line position on every sub-chart (date/time scrubs that
        //     don't trigger a Render still need the red line to move).
        //   - Horizon-line position on every sub-chart (horizon scrubs that
        //     would otherwise need a full Render just to move the green line).
        //   - Sky's K-S brightness re-walk (Bortle/Extinction/Filter scrubs).
        private void ConstructCoordinator()
        {
            mCoordinator = new ChartCoordinator(
                cache: mCache,
                renderActiveArea: RenderArea,
                defaultProgressFactory: CreateChartProgress,
                postApplyHook: (ctx, eval) =>
                {
                    // Listbox painter state -- stamps mLastAppliedCtx / DayKey,
                    // rebuilds mGeoVisCache, and Invalidates the listbox.
                    RefreshAfterPostApply(ctx);

                    RefreshAstrometryLabels();
                    foreach (var sc in mSubCharts.Values)
                    {
                        sc.UpdateNowLine(ctx.Observation.Utc);
                        // Horizon line tracks the user's TargetFloor spinner -- a UI
                        // affordance for the scalar knob, not the LocalHorizon polyline
                        // (which can dip below the floor and drive per-azimuth fit
                        // decisions in the cache instead).
                        sc.UpdateHorizonLine(ctx.Policy.TargetFloorDeg);
                    }
                    // First ChartEvaluation flag consumer: skip the K-S re-walk
                    // when no K-S input changed since the last Apply. Sky's Render
                    // owns the K-S walk inline when Sky is the active sub-chart,
                    // so this hook is only load-bearing for "Sky not active +
                    // BrightnessInputs changed" (keep Sky's series current in the
                    // background so a later switch to Sky doesn't show a stale
                    // flash). Date/Location/Targets/Hdm scrubs without a
                    // BrightnessInputs change either get a fresh walk from Sky's
                    // Render (if Sky is active) or get a fresh walk the next
                    // time Sky activates (Render is the authoritative refresh).
                    if (eval.BrightnessInputsChanged) PushSkyKSInputs(ctx);

                    // Refresh the listbox so the tints reflect the new fit state.
                    CheckedListBox_SelectedTargets?.Invalidate();
                });
        }

        // Baseline paint: fire one Apply with an empty target list so the chart
        // area paints its non-target scaffolding (axis labels, dusk/dawn gradient,
        // moon overlay on Day) instead of staying blank-gray at boot. Empty targets
        // is the key -- the chart's target curves stay absent until the user
        // explicitly checks a target or clicks Button_Graph, which keeps the
        // "rendered targets == user intent" rule intact across every code path.
        // Cheap: EnsureAsync with no targets prepares moon altitudes only, no
        // per-target work.
        private void FireBaselinePaint()
        {
            mCoordinator.Apply(SnapshotCurrent(Array.Empty<Target>()));
        }
    }
}
