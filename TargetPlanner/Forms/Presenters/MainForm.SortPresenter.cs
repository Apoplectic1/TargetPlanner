using System;
using System.Collections.Generic;
using System.Linq;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Sort-and-populate concern: produces the canonical sorted-targets ordering and
    // pushes that ordering into ComboBox_SelectTarget + CheckedListBox_SelectedTargets,
    // preserving check state across sort changes. Split out of MainForm.cs (PR-7.4a
    // of the architectural-review campaign) so the form file stays navigable; this
    // is a partial-class file split rather than a Presenter-object extraction
    // because the methods orchestrate ~6 MainForm controls + the VM + the
    // coordinator and constructor-injecting all of those would be more ceremony
    // than the move is worth.
    //
    // Wired into MainForm via the Designer event handler ComboBox_SortTargets_
    // SelectedIndexChanged below, and called from the form's lifecycle methods
    // (OnVmKnownTargetsChanged, Button_AddTarget_Click, Button_RemoveTarget_Click,
    // DatePicker / TimePicker ValueChanged when the active mode is time-dependent).
    public partial class MainForm
    {
        // Repopulates ComboBox_SelectTarget.Items in the order produced by
        // SortedTargets(mSelection.KnownTargets). Routing every combo-populate through
        // this one helper is what makes "always apply ComboBox_SortTargets' order to
        // ComboBox_SelectTarget" an invariant -- the combo has Sorted=false in Designer,
        // so the Items order comes entirely from what we push in here, and nowhere else
        // in the form adds combo items.
        //
        // preserveSelection=true snapshots the current Text, clears + repopulates, then
        // restores the selection by looking up the prior name in the new Items (falling
        // back to setting Text if the prior name isn't in the new list -- accepts typed-text
        // survivors). Used by ResortSelectedTargets on sort-combo change or time-picker
        // scrub under Transit/Rise modes.
        //
        // preserveSelection=false auto-selects index 0 -- used by OnVmKnownTargetsChanged
        // after a NINA load to surface a sane selection in the current sort order.
        //
        // Runs under mUpdatingUiFromVm so the per-write SelectedIndexChanged events don't
        // round-trip through OnComboSelectTargetChanged into the VM.
        private void PopulateTargetComboFromTargets(bool preserveSelection)
        {
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try
            {
                string priorName = ComboBox_SelectTarget.Text;
                ComboBox_SelectTarget.Items.Clear();
                foreach (Target t in SortedTargets(mSelection.KnownTargets))
                {
                    ComboBox_SelectTarget.Items.Add(t.Name);
                }

                if (preserveSelection)
                {
                    int idx = ComboBox_SelectTarget.Items.IndexOf(priorName);
                    if (idx >= 0)
                    {
                        ComboBox_SelectTarget.SelectedIndex = idx;
                    }
                    else
                    {
                        // Prior Text wasn't in the new Items (typed-text override from the
                        // user, or a target that got filtered out). Keep it as typed text
                        // so we don't silently overwrite the user's pick.
                        ComboBox_SelectTarget.Text = priorName;
                    }
                }
                else if (ComboBox_SelectTarget.Items.Count > 0)
                {
                    ComboBox_SelectTarget.SelectedIndex = 0;
                    // WinForms DropDown-style ComboBox quirk: after an Items.Clear() + Add()
                    // churn, setting SelectedIndex alone doesn't reliably update Text when the
                    // combo previously had a different item selected (the prior Text survives
                    // the Clear and isn't overwritten by the SelectedIndex setter under these
                    // conditions). Explicitly push Items[0] into Text so the visible textbox
                    // matches Items[0] -- which is what "auto-select the first item" means.
                    ComboBox_SelectTarget.Text = ComboBox_SelectTarget.Items[0].ToString();
                }
            }
            finally
            {
                mUpdatingUiFromVm = wasUpdating;
            }
        }

        // Wraps a Target reference for storage as a CheckedListBox row. ToString
        // returns the target name (which the listbox renders), but the Target
        // property exposes the underlying instance for index-based lookups.
        // This is the only way to disambiguate two targets with the same name
        // (and possibly different coords) -- looking up by Name via FirstOrDefault
        // always returns the first match, so the second row would resolve to the
        // first target's data.
        private sealed class TargetRow
        {
            public Target Target { get; }
            public TargetRow(Target target) { Target = target; }
            public override string ToString() => Target?.Name ?? string.Empty;
        }

        // Resolve a CheckedListBox row's underlying Target. Items are TargetRow
        // wrappers (see PopulateCheckedListBoxFromTargets); cast and read
        // .Target. Returns null for out-of-range indices.
        private Target TargetForRow(int index)
        {
            if (index < 0 || index >= CheckedListBox_SelectedTargets.Items.Count) return null;
            return (CheckedListBox_SelectedTargets.Items[index] as TargetRow)?.Target;
        }

        // Clears CheckedListBox_SelectedTargets and re-adds every target from
        // mSelection.KnownTargets in the currently-selected sort order. Each row is
        // checked when defaultChecked is true OR when the target is currently in
        // mSelection.Checked (the latter applies after re-sorts so prior check state
        // survives a sort-mode change).
        //
        // Runs under mUpdatingUiFromVm so the per-Add ItemCheck events don't
        // round-trip through OnCheckedListBoxItemCheck into the VM.
        private void PopulateCheckedListBoxFromTargets(bool defaultChecked)
        {
            CheckedListBox_SelectedTargets.BeginUpdate();
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try
            {
                CheckedListBox_SelectedTargets.Items.Clear();
                foreach (Target t in SortedTargets(mSelection.KnownTargets))
                {
                    bool isChecked = defaultChecked || mSelection.Checked.Contains(t);
                    CheckedListBox_SelectedTargets.Items.Add(new TargetRow(t), isChecked);
                }
            }
            finally
            {
                mUpdatingUiFromVm = wasUpdating;
                CheckedListBox_SelectedTargets.EndUpdate();
            }

        }

        // Re-order CheckedListBox_SelectedTargets AND ComboBox_SelectTarget in place using the
        // current ComboBox_SortTargets mode. Per-row check state is sourced from
        // mSelection.Checked; PopulateCheckedListBoxFromTargets reads it directly so we don't
        // need an explicit snapshot/restore. Called from the sort-mode ComboBox, from the
        // picker ValueChanged handlers when the active mode is time-dependent, and internally
        // after anything that changes the list's membership. When
        // <paramref name="autoSelectFirstInCombo"/> is true, ComboBox_SelectTarget snaps to
        // the first item of the new order; otherwise its current Text selection is preserved
        // at its new index.
        private void ResortSelectedTargets(bool autoSelectFirstInCombo = false)
        {
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;

            PopulateTargetComboFromTargets(preserveSelection: !autoSelectFirstInCombo);
            PopulateCheckedListBoxFromTargets(defaultChecked: false);

            // Sort change: build a snapshot with the permuted Targets and hand
            // it to the coordinator. The diff sees a Targets reference change
            // and Renders the active sub-chart from cache; the cache's fits
            // are still valid because the target SET is unchanged (the new
            // List instance just has a different order). Inactive sub-charts
            // catch up to the new order the next time the user clicks their
            // radio -- coordinator's stamp records the new order so the diff
            // takes the showOnly fast path after that one re-render.
            IReadOnlyList<Target> lastTargets = mCoordinator?.LastAppliedTargets;
            if (mCoordinator != null && lastTargets != null && lastTargets.Count > 0)
            {
                var sorted = SortedTargets(lastTargets).Where(t => t != null).ToList();
                mCoordinator.Apply(SnapshotCurrent(sorted));
            }

            // Belt-and-suspenders for autoSelectFirstInCombo: re-apply the first item's text
            // at the very end. The CheckedListBox repopulate above can trigger a
            // SelectedIndexChanged whose handler routes through OnCheckedListBoxSelectedIndex-
            // Changed -> mSelection.SetSelectedSingle, which then writes the highlighted
            // row's name back into ComboBox_SelectTarget via OnVmSelectedSingleChanged --
            // overwriting the first-item Text the populate just set. Re-apply under
            // mUpdatingUiFromVm so the write doesn't round-trip back through the VM.
            if (autoSelectFirstInCombo && ComboBox_SelectTarget.Items.Count > 0)
            {
                bool wasUpdating = mUpdatingUiFromVm;
                mUpdatingUiFromVm = true;
                try
                {
                    ComboBox_SelectTarget.Text = ComboBox_SelectTarget.Items[0].ToString();
                }
                finally
                {
                    mUpdatingUiFromVm = wasUpdating;
                }
            }
        }

        // Dispatch on ComboBox_SortTargets.SelectedIndex. Transit and Rise modes fall back to
        // Name sort when mLocation isn't ready yet (form is mid-init), so callers don't need
        // to guard. The picker anchor converts to UTC via SpecifyKind(..., Local).ToUniversalTime()
        // to match Button_VisibleTonight_Click's idiom.
        private IEnumerable<Target> SortedTargets(IEnumerable<Target> targets)
        {
            int mode = ComboBox_SortTargets != null ? ComboBox_SortTargets.SelectedIndex : 0;

            if ((mode == 1 || mode == 2) && mLocation != null)
            {
                DateTime anchorUtc = DateTime.SpecifyKind(
                    DatePicker.Value.Date + TimePicker.Value.TimeOfDay,
                    DateTimeKind.Local).ToUniversalTime();

                if (mode == 1)
                {
                    return Astronomy.Core.Session.TargetOrdering.ByTransit(
                        targets, mLocation, anchorUtc);
                }
                return Astronomy.Core.Session.TargetOrdering.ByRise(
                    targets, mLocation, anchorUtc, mLocation.Horizon);
            }

            return targets.Where(t => t != null)
                .OrderBy(t => t.Name, NaturalStringComparer.OrdinalIgnoreCase);
        }

        // Designer-wired event handler. Routes to ResortSelectedTargets;
        // autoSelectFirstInCombo=true so a deliberate sort change re-anchors the
        // combo text on the new first item.
        private void ComboBox_SortTargets_SelectedIndexChanged(object sender, EventArgs e)
        {
            ResortSelectedTargets(autoSelectFirstInCombo: true);
        }
    }
}
