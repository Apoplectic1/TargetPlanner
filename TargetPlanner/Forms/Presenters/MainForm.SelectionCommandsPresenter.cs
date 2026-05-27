using System;
using System.Linq;
using Astronomy.Core.Horizons;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Time;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Selection-command concern: user-initiated commands that mutate
    // TargetSelection (the combobox single-target pick + the three "all"
    // buttons + Visible-Tonight). Distinct from SelectionVmPresenter which
    // owns the bidirectional VM <-> UI sync; that file's header explicitly
    // notes "Other UI -> VM paths (Button_*Click handlers, ...) live with
    // their respective concerns." These five live here.
    //
    // Lifted out of MainForm.cs -- partial-class file split, same pattern as
    // the other presenter partials.
    public partial class MainForm
    {
        private void ComboBox_SelectTarget_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Validate-then-route: if the Find returns null (combobox text doesn't match any
            // loaded target), leave mSelection.SelectedSingle pointing at the previous valid
            // target. The OnComboSelectTargetChanged binding registered in WireSelectionVm
            // also handles this path; this Designer-wired handler is kept as a no-op forwarder
            // so the VM is the single source of truth for resolution.
            if (mUpdatingUiFromVm) return;
            if (mSelection == null) return;
            string selectedTargetName = ComboBox_SelectTarget.Text;
            Target found = mSelection.KnownTargets.FirstOrDefault(t => t.Name == selectedTargetName);
            if (found == null) return;
            Log.Diag("UI", $"ComboBox_SelectTarget.SelectedIndexChanged target={found.Name}");
            mSelection.SetSelectedSingle(found);
        }

        // VM mutator. SetAllChecked fires CheckedSetChanged; OnVmCheckedSetChanged
        // updates the listbox row check states, and OnVmCheckedSetChanged_TriggerGraph
        // arms the multi-graph debounce -> chart blanks ~250 ms later. The cache for
        // previously-rendered targets is preserved (PrepareManyAsync(empty) is a
        // no-op; only SetLocationAsync ever drops cache entries), so re-checking
        // those targets later hits the warm cache instantly.
        private void Button_UncheckAll_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_UncheckAll.Click");
            mSelection.SetAllChecked(false);
        }

        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_SelectAllTargets.Click");
            mSelection.SetAllChecked(true);
        }

        // Empties the known-target list entirely (combo + listbox cleared, charts
        // blanked). Distinct from Button_UncheckAll, which only clears the *checked*
        // set. SetKnownTargets with an empty list also clears Checked + SelectedSingle;
        // the VM events repopulate the now-empty UI and blank the charts.
        private void Button_ClearAllTargets_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_ClearAllTargets.Click");
            mSelection.SetKnownTargets(Array.Empty<Target>());
        }

        // Check exactly the targets that have a contiguous window of at least
        // NumericUpDown_TargetDuration above NumericUpDown_TargetFloor during the night bracketing
        // the moment picked in DatePicker + TimePicker. NightCalculator's bracket logic
        // walks forward to tomorrow's dawn or back to yesterday's dusk depending on
        // whether the moment is past today's dawn -- so a 2 AM TimePicker value yields
        // the night that's currently in progress, while a 10 PM value yields the night
        // just starting. Matches the rest of the form's
        // "DatePicker.Value.Date + TimePicker.Value.TimeOfDay" pattern.
        private void Button_VisibleTonight_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_VisibleTonight.Click");
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;
            if (mLocation == null) return;

            DateTime tonightAnchor = DatePicker.Value.Date + TimePicker.Value.TimeOfDay;
            ObservationMoment tonightObs = ObservationMoment.FromLocal(
                tonightAnchor, mLocation?.TimeZoneInfo ?? TimeZoneInfo.Local);

            NightWindow night = NightCalculator.ComputeNight(mLocation, tonightObs.Utc);

            // Clip the night window to "picker-time forward". A target that was above the
            // horizon early in the night but is already (and remains) below it by the picker
            // time should not count as visible from this point on. If the picker is past dawn
            // there's no remaining night -- invalidate so the visibility check returns false
            // for every target.
            if (night.IsValid)
            {
                DateTime anchorUtc = DateTime.SpecifyKind(tonightAnchor, DateTimeKind.Local).ToUniversalTime();
                if (anchorUtc >= night.AstronomicalDawn)
                {
                    night = night with { AstronomicalDusk = DateTime.MinValue, AstronomicalDawn = DateTime.MinValue };
                }
                else if (anchorUtc > night.AstronomicalDusk)
                {
                    night = night with { AstronomicalDusk = anchorUtc };
                }
            }

            // "Visible tonight" = above mathematical horizon (0°) for at least the smallest
            // practical imaging session (15 min = 0.25 h), independent of HMD spinners.
            // The button populates the maximum candidate pool ("what's potentially observable
            // tonight"); HMD scrubs filter the rendered charts visually via the universal
            // hide-on-no-fit rule -- changing H/D/M after this click never re-keys the
            // checked set, so the user can iterate H/D/M without losing their candidate pool.
            //
            // The 0° + 15-min thresholds are intentionally hard-coded here: H=0 because the
            // user's NumericUpDown_TargetFloor is the H of HMD (a render filter, not a
            // visibility gate), D=15 min because shorter visibility is fleeting and not
            // worth flagging as a candidate. See ROADMAP.md "Local Horizon vs Target Floor"
            // for the future polyline-of-(Alt, Az) physical-obstruction support that would
            // sit alongside Target Floor as a second gate.
            //
            // SetCheckedSet fires CheckedSetChanged -> debounce -> multi-graph: the
            // visible-tonight chart appears automatically ~250 ms after the click. The
            // combo / RA / Dec inputs stay pointing at whatever single target the user
            // had selected before the click -- they describe the single-target view, not
            // the visible-set view, and the two paradigms are independent (see
            // Button_Graph_Click + WireSelectionVm).
            IHorizonProfile mathHorizon = new ScalarHorizonProfile(0.0);
            TimeSpan minDuration = TimeSpan.FromMinutes(15);

            var visible = mSelection.KnownTargets
                .Where(t => CoarseVisibility.IsAboveHorizonForAtLeast(
                    t, mLocation, night, mathHorizon, minDuration))
                .ToList();
            mSelection.SetCheckedSet(visible);

            // Replace (not union) the Visible-tonight tint set. Persists across
            // Clear All by design; cleared by another Visible click (this same
            // path), KnownTargets change (IntersectWith in OnVmKnownTargetsChanged),
            // or right-click on the listbox (OnSelectedTargetsMouseDown).
            mVisibleTaggedTargets.Clear();
            mVisibleTaggedTargets.UnionWith(visible);
            CheckedListBox_SelectedTargets?.Invalidate();
        }
    }
}
