using System;
using System.Threading.Tasks;

namespace TargetPlanner
{
    // Progress-bar plumbing for ProgressBar_Processing. Two producers feed it:
    //   * CreateChartProgress -- the ChartCoordinator's defaultProgressFactory.
    //     Builds a fresh Progress<T> per Apply with a closure-captured (gen,
    //     claimed) pair. The closure marshals callbacks back to the UI thread
    //     via the Progress<T> captured SynchronizationContext.
    //   * BeginScanProgress + FinishScanProgress -- the load paths
    //     (Browse / Load / drag-drop). Scanner discovers Total during file
    //     enumeration; FinishScanProgress fills + holds + hides.
    //
    // Both producers share mChartBuildGeneration so a chart click mid-scan --
    // or a load mid-build -- invalidates the other's stale callbacks. One
    // operation owns the bar at a time; mBarOwnerGen tracks which pipeline's
    // deferred hide is allowed to clear the visible state.
    //
    // Lifted out of MainForm.cs -- partial-class file split, same pattern as
    // the other presenter partials.
    public partial class MainForm
    {
        // Incremented on every Graph click so stale Progress<int> callbacks from a prior
        // (still in-flight) PrepareManyAsync don't tick ProgressBar_Processing
        // after the user has already launched a new chart build. Captured by value in the
        // Progress<int> closure, so each click's callbacks are stamped and can be
        // identified as stale later.
        private int mChartBuildGeneration;

        // Generation that currently owns the bar's visible state. Set when a
        // chart-pipeline closure first claims the bar (its `claimed` flip).
        // The deferred-hide continuation reads this -- if a follow-on pipeline
        // has claimed ownership in the meantime, the older pipeline's hide
        // bails and lets the newer one manage the bar. Without this, the 200 ms
        // hold at 100 % would either clobber a cold follow-on scrub (clearing
        // the bar mid-progress) or leave a warm follow-on staring at a stuck
        // 100 % indefinitely. UI-thread-only access; no synchronization needed.
        private int mBarOwnerGen;

        private const int ProgressBarHoldMs = 1000;

        // Coordinator-side default progress factory: builds a fresh
        // Progress<T> per Apply with closure-captured (gen, claimed) state.
        // Progress<T> captures SynchronizationContext.Current at construction
        // -- called from the UI-thread coordinator -- so Report callbacks
        // marshal back to the UI thread even when the cache ticks from
        // CacheAxis.PrepareAsync's TaskScheduler.Default ContinueWith
        // (ThreadPool). Shared mChartBuildGeneration with BeginScanProgress
        // so load paths and chart pipelines mutually invalidate stale
        // callbacks -- one operation owns the bar at a time.
        //
        // Behavior: first Report with Total > 0 claims the bar (Value=0,
        // Maximum=Total, Visible=true) and stamps mBarOwnerGen so the
        // deferred hide can tell whether a follow-on pipeline has stolen
        // ownership. Subsequent Reports advance Value monotonically (stale
        // ticks from a slower path can't regress). On Done >= Maximum the
        // closure schedules a 200 ms hold-then-hide via Task.Delay so the
        // bar is visibly at 100 % before disappearing -- without the hold,
        // WinForms doesn't paint between Value=max and Visible=false in the
        // same handler invocation, so the user never sees the full bar.
        // The hide bails if ownership has moved to a newer pipeline, which
        // resolves the two takeover quirks that killed the prior 1 s hold:
        // a cold follow-on can claim the bar mid-hold without being
        // clobbered, and a warm follow-on still gets the hide (since the
        // outgoing pipeline retained ownership through to its delayed hide).
        private IProgress<(int Done, int Total)> CreateChartProgress()
        {
            int gen = ++mChartBuildGeneration;
            bool claimed = false;
            TaskScheduler uiSched = TaskScheduler.FromCurrentSynchronizationContext();
            return new Progress<(int Done, int Total)>(value =>
            {
                if (gen != mChartBuildGeneration) return;   // superseded
                if (value.Total <= 0) return;                // no work signal
                int max = Math.Max(1, value.Total);
                if (!claimed)
                {
                    // Fresh take-over: reset Value/Visible from whatever the
                    // previous pipeline left behind so a stale 100% fill
                    // doesn't ride forward through the monotonic guard.
                    // mBarOwnerGen stamp lets a previous pipeline's deferred
                    // hide notice we've taken over and bail.
                    claimed = true;
                    mBarOwnerGen = gen;
                    ProgressBar_Processing.Minimum = 0;
                    ProgressBar_Processing.Maximum = max;
                    ProgressBar_Processing.Value   = 0;
                    ProgressBar_Processing.Visible = true;
                }
                else if (ProgressBar_Processing.Maximum != max)
                {
                    ProgressBar_Processing.Maximum = max;
                }
                int clamped = Math.Min(Math.Max(0, value.Done), max);
                if (clamped > ProgressBar_Processing.Value)
                    ProgressBar_Processing.Value = clamped;
                if (clamped >= max)
                {
                    Task.Delay(ProgressBarHoldMs).ContinueWith(_ =>
                    {
                        // Hide only if we still own the bar; a newer pipeline
                        // that claimed in the meantime will manage its own
                        // hide (or its own takeover reset will already have
                        // happened).
                        if (mBarOwnerGen != gen) return;
                        mBarOwnerGen = 0;
                        ProgressBar_Processing.Value   = 0;
                        ProgressBar_Processing.Visible = false;
                    }, uiSched);
                }
            });
        }

        // Load-path progress (Browse / Load / drag-drop). The scanner discovers
        // the Total after file enumeration so the first Report sizes Maximum;
        // pair with FinishScanProgress for the fill + 1-second hold + reset.
        // Shares mChartBuildGeneration with the chart pipeline's progress sink
        // (CreateChartProgress) so a chart click mid-scan -- or a load mid-build
        // -- invalidates the other's stale callbacks.
        private (int generation, IProgress<(int Done, int Total)> progress) BeginScanProgress()
        {
            int thisGeneration = ++mChartBuildGeneration;

            ProgressBar_Processing.Minimum = 0;
            ProgressBar_Processing.Maximum = 1;   // resized on first Total
            ProgressBar_Processing.Value   = 0;
            ProgressBar_Processing.Visible = true;

            var progress = new Progress<(int Done, int Total)>(t =>
            {
                if (thisGeneration != mChartBuildGeneration) return;  // stale
                int max = Math.Max(1, t.Total);
                if (ProgressBar_Processing.Maximum != max)
                    ProgressBar_Processing.Maximum = max;
                int clamped = Math.Min(Math.Max(0, t.Done), max);
                if (clamped > ProgressBar_Processing.Value)
                    ProgressBar_Processing.Value = clamped;
            });

            return (thisGeneration, progress);
        }

        // Load-path finish: fill bar to Maximum, hold 1 s, clear + hide.
        // Generation-guarded so a superseding chart pipeline or fresh load
        // doesn't get its bar clobbered by the trailing reset.
        private void FinishScanProgress(int generation)
        {
            if (generation != mChartBuildGeneration) return;
            ProgressBar_Processing.Value = ProgressBar_Processing.Maximum;
            Task.Delay(1000).ContinueWith(
                _ =>
                {
                    if (generation != mChartBuildGeneration) return;
                    ProgressBar_Processing.Value = 0;
                    ProgressBar_Processing.Visible = false;
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
