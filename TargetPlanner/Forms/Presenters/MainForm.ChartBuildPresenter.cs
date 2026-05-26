using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TargetPlanner.State;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Chart-build concern: every method that turns a user gesture (Graph /
    // CheckedTargets / a radio click) into a rendered chart. Lifted out of
    // MainForm.cs -- partial-class file split, same pattern as the other
    // presenter partials.
    //
    // What's here:
    //   * Build entry points: Button_Graph_Click / Button_CheckedTargets_Click +
    //     the shared RunGraphBuildAsync (single AND multi);
    //     CheckedToggleDebounce_Tick is the trailing-edge debounce tick wired by
    //     SelectionVmPresenter's OnVmCheckedSetChanged_TriggerGraph.
    //   * Active-area + render dispatch: SelectedArea (Day / Sky / Year /
    //     Sessions resolver) and RenderArea (the coordinator's post-await
    //     render delegate).
    //   * Chart-area UI: ShowOnlyAltitudeChart / OnSubChartIdealHeightChanged /
    //     ResizeAltitudeChartArea -- sub-chart visibility + the elastic resize.
    //   * Radio handlers: the four CheckedChanged handlers that Apply a fresh
    //     SnapshotCurrent() to the coordinator when the active area changes.
    //   * Sky-side push: PushSkyKSInputs is called from the coordinator's
    //     post-apply hook to keep Sky's K-S brightness curves in sync with the
    //     just-applied snapshot.
    //
    // What stays in MainForm.cs:
    //   * CreateChartProgress -- the coordinator's default progress factory.
    //     Returns a fresh Progress<(int Done, int Total)> per Apply with a
    //     closure that owns the bar state for that pipeline. Used by every
    //     Apply path (scrubs, location edits, graph-build) through the
    //     coordinator's dispatch funnel.
    //   * BeginScanProgress / FinishScanProgress -- load-path progress (Browse /
    //     Load / drag-drop). Shares mChartBuildGeneration with CreateChartProgress
    //     so a chart click mid-scan invalidates the scan and vice versa.
    //   * SnapshotCurrent (both overloads) -- builds the ChartContext used by
    //     every Apply, called from too many places (loads, picker handlers,
    //     location edits) to live in any single presenter.
    public partial class MainForm
    {
        // Button_Graph is single-target only. Always graphs mSelection.SelectedSingle
        // (the combo + RA/Dec inputs); on null SelectedSingle a 2-second
        // ShowTransientMessage("No Targets") notice fires. Multi-target rendering is
        // owned by the CheckedSetChanged debounce path (CheckedToggleDebounce_Tick),
        // not by this button.
        //
        // Chart-vs-checkbox divergence is intentional and unmanaged: clicking
        // Button_Graph after checking targets renders just the combo target while the
        // checkboxes stay ticked; the next checkbox toggle re-renders the full checked
        // set, producing a visible jump from single to multi. That's the documented
        // rule -- Button_Graph and the checked-set are independent views, switching
        // between them is the user's explicit action.
        //
        // mObservation is kept in sync with the pickers via UpdateLocalDateTimeEvents
        // (called from DatePicker/TimePicker ValueChanged and Button_Now_Click).
        // Don't overwrite with ObservationMoment.Now here -- that was the pre-refactor
        // assumption when the app was always "live now" by default.
        private async void Button_Graph_Click(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                Log.Diag("UI", $"Button_Graph.Click selected={mSelection?.SelectedSingle?.Name ?? "<null>"}");
                // Cancel any pending multi-graph trigger. A user click on Button_Graph is an
                // explicit "I want single-target now" intent; without this stop, a checkbox
                // toggle 200 ms ago would still tick the debounce 50 ms later and clobber
                // the just-rendered single-graph with a multi re-render.
                if (mCheckedToggleDebounce != null) mCheckedToggleDebounce.Stop();

                // Resolve combo text to a Target. Covers the edge case where the user typed
                // into ComboBox_SelectTarget without triggering SelectedIndexChanged /
                // MouseLeave; without this, SelectedSingle would lag the combo by one edit.
                // If the text doesn't match a loaded target, fall through and use whatever
                // SelectedSingle currently is.
                Target found = mSelection.KnownTargets.FirstOrDefault(t => t.Name == ComboBox_SelectTarget.Text);
                if (found != null) mSelection.SetSelectedSingle(found);

                Target current = mSelection.SelectedSingle;
                if (current == null)
                {
                    // No SelectedSingle, no resolvable combo text. Surface a brief
                    // auto-dismissing notice instead of silently doing nothing (the silent
                    // path was confusing -- the user clicked Graph and saw no feedback).
                    ShowTransientMessage("No Targets");
                    return;
                }

                await RunGraphBuildAsync(new[] { current });
            }
            catch (Exception ex)
            {
                Log.Error("Button_Graph_Click threw", ex);
            }
        }

        // Shared graph-build entry. Both Button_Graph_Click (single-target) and
        // CheckedToggleDebounce_Tick (multi-target) call this. The coordinator's
        // pipeline owns supersedence via its internal generation counter: a newer
        // Apply increments the generation; older pipelines bail at their gen check
        // before any side-effecting write.
        //
        // Empty targets is intentional -- ClearAll / fresh-NINA-load / location
        // change all produce empty input; PrepareManyAsync no-ops on empty (the
        // cache for previously-rendered targets stays intact -- only
        // SetLocationAsync ever drops cache entries), and the active sub-chart's
        // Render paints a blank chart per its empty-list contract.
        //
        // Park focus on the form before disabling Button_Graph. Otherwise Win32
        // auto-advances focus from the just-disabled Button_Graph to the next TabStop
        // (ComboBox_SelectTarget), whose focus-gain auto-selects its text and would
        // cascade into the combo's SelectedIndexChanged path.
        private async Task RunGraphBuildAsync(IReadOnlyList<Target> targets)
        {
            // Snapshot full ChartContext at build entry; the coordinator's
            // pipeline is location-coherent against this snapshot.
            ChartContext ctxSnapshot = SnapshotCurrent(targets);

            ActiveControl = null;
            Button_GraphTarget.Enabled = false;

            try
            {
                // Coordinator owns the cache-prep + render pipeline AND the
                // progress bar (via the defaultProgressFactory wired at
                // construction). Every Apply path -- this graph-build, scrubs,
                // location edits -- drives ProgressBar_MultiTargetProcessing
                // through one funnel; no per-callsite Begin/Finish wrapping.
                // Generation-counter supersedence ensures only the latest
                // Apply's pipeline writes Render state; older pipelines bail
                // before touching the chart, and their sinks bail at the
                // mChartBuildGeneration check before touching the bar.
                if (mCoordinator != null)
                {
                    await mCoordinator.ApplyImmediateAsync(ctxSnapshot);
                }
                // The coordinator's mLastAppliedByArea is the single SoT for the
                // rendered target list; no form-side shadow store to update.
            }
            finally
            {
                Button_GraphTarget.Enabled = true;
            }
        }

        // Trailing-edge debounce tick. Walks CheckedListBox_SelectedTargets.CheckedItems
        // in display order so the rendered target list -- and therefore the chart
        // legend -- inherits the listbox's NaturalStringComparer sort (see
        // GetNinaTargets). Iterating mSelection.Checked here would be set-order, not
        // sort-order. Empty CheckedItems -> empty targets -> blank chart, intentionally.
        private async void CheckedToggleDebounce_Tick(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                mCheckedToggleDebounce.Stop();
                await RunGraphBuildAsync(HarvestCheckedTargets());
            }
            catch (Exception ex)
            {
                Log.Error("CheckedToggleDebounce_Tick threw", ex);
            }
        }

        // Force-render the currently-checked set immediately, bypassing the 250 ms
        // CheckedToggleDebounce. Convenience for users who want the chart now after
        // a series of checkbox toggles, or who want to re-render the same checked
        // set after an HMD/Location scrub. Walks CheckedListBox_SelectedTargets in
        // display order so the rendered list inherits the listbox's NaturalString-
        // Comparer sort, mirroring CheckedToggleDebounce_Tick.
        private async void Button_CheckedTargets_Click(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                Log.Diag("UI", $"Button_CheckedTargets.Click checkedCount={CheckedListBox_SelectedTargets.CheckedItems.Count}");
                mCheckedToggleDebounce?.Stop();
                await RunGraphBuildAsync(HarvestCheckedTargets());
            }
            catch (Exception ex)
            {
                Log.Error("Button_CheckedTargets_Click threw", ex);
            }
        }

        // Walk CheckedListBox_SelectedTargets in display order, collecting the
        // checked targets. Display order so the rendered list inherits the
        // listbox's NaturalStringComparer sort. Shared by Button_CheckedTargets_Click
        // and the CheckedToggleDebounce_Tick multi-graph path.
        private List<Target> HarvestCheckedTargets()
        {
            var targets = new List<Target>();
            foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
                if (item is TargetRow row && row.Target != null)
                    targets.Add(row.Target);
            return targets;
        }

        // Returns the currently-active chart-area name. The radio cluster
        // (Day / Year / Sessions) ensures exactly one is checked at any time;
        // CheckBox_Sky lives inside the Day radio and toggles its sub-mode
        // between altitude (Day) and K-S brightness (Sky). Day↔Sky toggling
        // exercises the coordinator's skip-Render-on-redundant-area-change
        // optimization for instant switching.
        private string SelectedArea()
        {
            if (RadioButton_Sessions.Checked) return "Sessions";
            if (RadioButton_Year.Checked)     return "Year";
            // Day radio active (default). Sub-mode determined by CheckBox_Sky.
            if (CheckBox_Sky != null && CheckBox_Sky.Checked) return "Sky";
            return "Day";
        }

        // Dispatch a synchronous Render to the sub-chart named by <paramref name="ctx.ActiveArea"/>.
        // Resizes the panel to match the newly-active sub-chart's IdealHeight.
        //
        // <paramref name="ctx"/> is the immutable input snapshot — Phase 1 of the
        // orchestration-layer refactor. Callers either build a snapshot via
        // <see cref="SnapshotCurrent(IReadOnlyList{Target})"/> (radio toggles, etc.)
        // or capture the snapshot at the start of an async build (RunGraphBuildAsync)
        // so the paint is location-coherent even if mLocation has drifted since.
        // <paramref name="progress"/> is the coordinator's render sub-progress
        // (OffsetProgress wrapping the active sink); null on warm-cache scrubs
        // where EnsureAsync reported zero work and the bar should stay hidden.
        private void RenderArea(ChartContext ctx,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (mSubCharts == null) return;
            if (ctx == null) return;
            if (!mSubCharts.TryGetValue(ctx.ActiveArea, out var sc)) return;
            // Render BEFORE ShowOnly so the sub-chart's Series state is fully
            // current at the moment WinForms fires the Visible=true paint cycle.
            sc.Render(ctx, mCache, progress);
            ShowOnlyAltitudeChart(sc.Control);
            ResizeAltitudeChartArea(sc.IdealHeight);
            // Force synchronous repaint. LC2's SKControl first-paint after
            // Visible=true uses stale internal state from when the control
            // was hidden -- specifically misses Fill-only LineSeries (the
            // moon overlay), leaving it invisible until the next Visible
            // cycle. Diagnosed by the Sky chart's moon overlay rendering
            // fine across the same scrub sequence (it stays visible during
            // Sky scrubs so LC2's cache is hot). Control.Refresh() forces
            // a synchronous repaint after Visible=true which re-reads the
            // Series state. Workaround at the dispatch layer rather than
            // inside any individual sub-chart -- every sub-chart benefits
            // from the same kick when it goes hidden -> visible.
            sc.Control.Refresh();
        }

        // Hide every control in Panel_AltitudeChart except `target`. Used to
        // multiplex the legacy MS Charts control with the LC2 sub-charts being
        // ported per Phase 4 PR. Both controls are added to the panel at startup
        // (Dock=Fill); ShowOnly flips Visible so only one paints.
        private void ShowOnlyAltitudeChart(Control target)
        {
            if (Panel_AltitudeChart == null) return;
            foreach (Control c in Panel_AltitudeChart.Controls)
            {
                c.Visible = ReferenceEquals(c, target);
            }
        }

        // Keep the chart's plot area at a fixed pixel height. As legend rows wrap,
        // the firing sub-chart's IdealHeight grows; this handler grows the Panel /
        // GroupBox / Form by the delta so the plot area stays put.
        private void OnSubChartIdealHeightChanged(object sender, EventArgs e)
        {
            if (sender is Charts.IAltitudeSubChart sc)
            {
                ResizeAltitudeChartArea(sc.IdealHeight);
            }
        }

        // Resize Panel_AltitudeChart, GroupBox_Altitude, and the form's ClientSize
        // so the chart's plot area sits at ChartLayout.FixedPlotAreaHeight.
        // Width is unchanged. Idempotent: a no-delta call is a cheap no-op.
        private void ResizeAltitudeChartArea(int targetPanelHeight)
        {
            if (Panel_AltitudeChart == null || GroupBox_Altitude == null) return;
            int delta = targetPanelHeight - Panel_AltitudeChart.Height;
            if (delta == 0) return;

            Panel_AltitudeChart.Height = targetPanelHeight;
            GroupBox_Altitude.Height += delta;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);
        }

        // The three view radio handlers (Day / Year / Sessions) all share the
        // same shape: if the radio is now checked, hand a snapshot to the
        // coordinator. CheckBox_Sky lives inside the Day radio as a sub-mode
        // toggle (altitude vs K-S brightness); it's enabled only when Day is
        // the active radio. The coordinator's diff sees ActiveArea changed and
        // dispatches Render on the new active sub-chart.
        private void RadioButton_Day_CheckedChanged(object sender, EventArgs e)
        {
            // Sky-mode toggle is meaningful only while Day is the active radio --
            // enable/disable in lockstep with the radio's checked state (this line
            // fires on BOTH the check and uncheck side of the toggle).
            if (CheckBox_Sky != null) CheckBox_Sky.Enabled = RadioButton_Day.Checked;
            OnViewRadioCheckedChanged(RadioButton_Day);
        }

        private void RadioButton_Year_CheckedChanged(object sender, EventArgs e)
            => OnViewRadioCheckedChanged(RadioButton_Year);

        private void RadioButton_Sessions_CheckedChanged(object sender, EventArgs e)
            => OnViewRadioCheckedChanged(RadioButton_Sessions);

        // Shared body of the three view-radio handlers. CheckedChanged fires on
        // both the radio-being-unchecked and the radio-being-checked sides of a
        // radio-group toggle; we only Apply on the checked side (the unchecked
        // side's neighbor will fire its own checked event and Apply for us).
        private void OnViewRadioCheckedChanged(System.Windows.Forms.RadioButton radio)
        {
            Log.Diag("UI", $"{radio.Name}.CheckedChanged checked={radio.Checked}");
            if (!radio.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Sub-mode toggle inside the Day radio. Wired in MainForm.Designer.cs.
        // Toggling switches between Day (altitude) and Sky (K-S brightness)
        // chart areas. Enabled only when Day radio is active (gated by
        // RadioButton_Day_CheckedChanged); CheckedChanged firing while
        // disabled would only happen via programmatic state restore at
        // form-load and is harmless (Apply of the unchecked path renders Day,
        // which is what was about to be rendered anyway).
        private void CheckBox_Sky_CheckedChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"CheckBox_Sky.CheckedChanged checked={CheckBox_Sky.Checked}");
            // Only dispatch when Day is the active radio -- toggling the
            // checkbox while Year or Sessions is selected would otherwise
            // re-render those areas with an unchanged ActiveArea (Year /
            // Sessions don't read CheckBox_Sky). Cheap to gate here.
            if (RadioButton_Day == null || !RadioButton_Day.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Push the active filter's center wavelength + bandwidth + re-walk the K-S
        // minute grid through the Sky sub-chart's existing series. Called from the
        // coordinator's post-apply hook so Bortle / ExtinctionK / Filter scrubs (and
        // any other pipeline) keep Sky's brightness curves in sync with the
        // just-applied snapshot. Reads the snapshot's filter + location for
        // snapshot-coherence under mid-pipeline drift. Null-safe; no-op when Sky
        // isn't instantiated yet (early-init paths). When no filter is active (empty
        // library / pre-init) Sky falls back to V-band defaults (550 nm / 85 nm).
        private void PushSkyKSInputs(ChartContext ctx)
        {
            if (mLC2Sky == null || ctx == null || ctx.Location == null || ctx.Policy == null) return;
            TargetPlanner.Filters.Filter active = ctx.Policy.ActiveFilter;
            mLC2Sky.ActiveFilterCenterNm    = active?.CenterNm    ?? 550.0;
            mLC2Sky.ActiveFilterBandwidthNm = active?.BandwidthNm ?? Astronomy.Core.Brightness.SkyBrightness.BWRefNm;
            mLC2Sky.RefreshSkyBrightness(mCache, ctx.Location);
        }
    }
}
