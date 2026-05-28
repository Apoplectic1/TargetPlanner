using System;
using System.Linq;
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

        // Check every target that isn't geometrically BLUE (below the local
        // horizon polyline at every Az during tonight's night). BLUE targets
        // can't be observed at this site/date by definition; including them in
        // the check-set would add them to ctx.Targets, force a no-op per-target
        // cache fit walk, and produce hide-on-no-fit chart series -- all with
        // no user-visible result. Defensive default: a target absent from
        // mGeoVisCache (e.g., during the boot window before the first
        // post-apply hook fires) counts as visible to avoid hiding a real
        // target the painter just hasn't classified yet.
        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_SelectAllTargets.Click");
            var nonBlue = mSelection.KnownTargets
                .Where(t => t != null && (!mGeoVisCache.TryGetValue(t, out var v) || v))
                .ToList();
            mSelection.SetCheckedSet(nonBlue);
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
        // Bulk-check every target that currently passes the H/D/M/F filter
        // (a non-null Tonight.Floor in the cache under the last-applied Hdm).
        // "Visible tonight" under this model means "GREEN-painted in the
        // listbox" -- targets that would render under the current planning
        // policy. Targets that are SLATE (above polyline but no D-hour fit)
        // and BLUE (below polyline) are deliberately excluded; if the user
        // wants those, they can relax H or D and click again.
        //
        // SetCheckedSet fires CheckedSetChanged -> multi-graph debounce, so
        // the chart auto-renders the new set after the standard delay. The
        // checkbox-interior tints already reflect this set's GREEN-ness; the
        // user gesture is the bulk-check shortcut, not a new tint.
        private void Button_VisibleTonight_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_VisibleTonight.Click");
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;
            if (mCache == null || mLastAppliedCtx == null) return;

            var green = mSelection.KnownTargets
                .Where(t => t != null
                         && mCache.GetFitOrNull(t, mLastAppliedCtx.Hdm)?.Tonight.Floor != null)
                .ToList();
            mSelection.SetCheckedSet(green);
        }
    }
}
