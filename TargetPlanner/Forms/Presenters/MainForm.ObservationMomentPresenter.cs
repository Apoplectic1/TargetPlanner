using System;
using System.Windows.Forms;
using Astronomy.Core.Time;
using TargetPlanner.Support;

namespace TargetPlanner
{
    // Observation-moment concern: every handler that mutates mObservation
    // (DatePicker / TimePicker scrubs, Button_Now snap-to-now, DatePicker
    // arrow-key nav). Lifted out of MainForm.cs -- partial-class file split,
    // same pattern as the other presenter partials.
    //
    // The three "moment changed" handlers share a common tail (refresh labels,
    // shift now-lines on every sub-chart, conditionally re-sort the listbox if
    // a time-dependent sort key is active, hand a snapshot to the coordinator).
    // OnObservationMomentChanged folds that tail. DatePicker_KeyDown is a
    // UI-delta handler (Up/Down = +/-1 day); it mutates DatePicker.Value and
    // lets DatePicker_ValueChanged do the actual work.
    public partial class MainForm
    {
        private void DatePicker_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"DatePicker.ValueChanged value={DatePicker.Value:yyyy-MM-dd}");
            // A date change trips the cache's mLastSetUtc diff -> SetLocationAsync
            // -> full cache rebuild -> Render with fresh moon series, dusk/dawn,
            // altitudes, and Tonight fits. Date-unchanged scrubs (TimePicker within
            // the same UTC day) skip the rebuild and just bounce through the
            // post-apply hook for label + now-line sync.
            OnObservationMomentChanged(resortIfTimeKeyed: true);
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"TimePicker.ValueChanged value={TimePicker.Value:HH:mm}");
            OnObservationMomentChanged(resortIfTimeKeyed: true);
        }

        // Plain Up/Down on the DatePicker = +/-1 day with natural cascade across
        // month/year boundaries (DateTime.AddDays). Setting Value programmatically
        // fires ValueChanged which routes through OnObservationMomentChanged so
        // the chart refreshes with the new date. Modifier keys (Shift/Ctrl/Alt
        // + arrow) pass through to the default WinForms handler.
        private void DatePicker_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.None) return;
            int delta;
            if (e.KeyCode == Keys.Up) delta = 1;
            else if (e.KeyCode == Keys.Down) delta = -1;
            else return;
            Log.Diag("UI", $"DatePicker.KeyDown key={e.KeyCode} delta={delta}d");
            DatePicker.Value = DatePicker.Value.AddDays(delta);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // Snap the observation moment back to the current wall-clock time. Replaces
        // the prior Now/SetDateTime/Hold trio plus the 5-second polling timer with
        // a single explicit user action: set mObservation to now, push into the
        // pickers (without re-triggering their ValueChanged), refresh every label
        // via UpdateLocalDateTimeEvents, and reposition the chart's red now-line
        // to the current X coordinate. Passes resortIfTimeKeyed: true so a
        // Transit / Rise-sorted listbox re-ranks against the new "now" -- the
        // pre-extraction code skipped this, leaving the listbox showing pre-snap
        // ordering on a meaningful time change.
        private void Button_Now_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_Now.Click");
            TimeZoneInfo zone = mLocation?.TimeZoneInfo ?? TimeZoneInfo.Local;
            mObservation = ObservationMoment.Now(zone);
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(mObservation.Utc, zone);

            DatePicker.ValueChanged -= DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;
            DatePicker.Value = localNow;
            TimePicker.Value = localNow;
            DatePicker.ValueChanged += DatePicker_ValueChanged;
            TimePicker.ValueChanged += TimePicker_ValueChanged;

            OnObservationMomentChanged(resortIfTimeKeyed: true);
        }

        // Common tail for every handler that mutates mObservation: refresh
        // dawn/dusk/sun/moon labels, shift the now-line on every sub-chart,
        // optionally re-run a time-dependent sort (Transit / Rise; Name is
        // time-independent so a Name sort is left alone), then hand a snapshot
        // to the coordinator.
        private void OnObservationMomentChanged(bool resortIfTimeKeyed)
        {
            UpdateLocalDateTimeEvents();
            // Immediate now-line update for live feedback during scrub. Coordinator's
            // post-apply hook re-runs UpdateNowLine on settle (cheap; just shifts a
            // section's X position).
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mObservation.Utc);
            // Transit / Rise sort keys are time-dependent; Name is not. Skip the
            // re-sort on Name to avoid a pointless Items.Clear+re-add round-trip
            // on every scrub tick.
            if (resortIfTimeKeyed
                && ComboBox_SortTargets != null
                && ComboBox_SortTargets.SelectedIndex > 0)
            {
                ResortSelectedTargets();
            }
            mCoordinator?.Apply(SnapshotCurrent());
        }
    }
}
