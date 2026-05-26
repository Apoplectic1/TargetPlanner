using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TargetPlanner.Support;
using TargetPlanner.Targets;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Selection-VM <-> UI sync concern: every method that mirrors TargetSelection
    // state into / out of the target controls (CheckedListBox_SelectedTargets,
    // ComboBox_SelectTarget, the RA/Dec inputs) and the per-target visual state
    // the listbox paints (color palette, duplicate-set tint, Visible-tonight
    // tint). Split out of MainForm.cs (ROADMAP item 8) -- partial-class file
    // split, same pattern as SortPresenter / CoordinatePresenter /
    // TargetLoadingPresenter: the methods orchestrate the VM + several controls
    // + the visual state, and constructor-injecting all of that is heavier
    // ceremony than the relocation.
    //
    // Wiring topology:
    //   * VM -> UI: WireSelectionVm subscribes the three TargetSelection events
    //     (KnownTargetsChanged, SelectedSingleChanged, CheckedSetChanged) to the
    //     OnVm* handlers; CheckedSetChanged additionally fires
    //     OnVmCheckedSetChanged_TriggerGraph for the multi-graph debounce.
    //     mUpdatingUiFromVm guards the echo path so VM-driven UI writes don't
    //     re-enter the VM.
    //   * UI -> VM: OnCheckedListBoxItemCheck / OnCheckedListBoxSelectedIndex-
    //     Changed route listbox events into VM mutators. Other UI -> VM paths
    //     (Button_*Click handlers, RA/Dec CoordinateInput callbacks,
    //     ComboBox_SortTargets) live with their respective concerns.
    //   * Listbox paint: GetDupeRowBackground / GetCheckboxInteriorTint are
    //     Func<int, Color?> callbacks DupeAwareCheckedListBox calls per row
    //     paint; they read mDupeSetColors / mVisibleTaggedTargets, populated
    //     by RecomputeDupeSetColors + Button_VisibleTonight_Click.
    //
    // Fields stay in MainForm.cs (the partial-class-split pattern); only the
    // methods relocate.
    public partial class MainForm
    {
        // Wire two-way bindings between TargetSelection and the UI controls. User input
        // flows into the VM via mutator calls; VM events flow back to UI controls. The
        // mUpdatingUiFromVm flag short-circuits VM-driven UI writes so they don't re-enter
        // the VM (a UI control's user-input event still fires when the value is set
        // programmatically; without the guard the write would round-trip).
        //
        // Render dispatch is explicit, not inferred:
        //   - Button_Graph_Click renders SelectedSingle (single-target only).
        //   - CheckedSetChanged drives a 250 ms-debounced multi-graph through
        //     OnVmCheckedSetChanged_TriggerGraph -> CheckedToggleDebounce_Tick.
        // The two paths are independent views; switching between them is the user's
        // explicit action (chart and checked-set may diverge after Button_Graph; the
        // next checkbox toggle re-renders the full checked set).
        //
        // CheckedListBox events: ItemCheck routes to SetChecked which fires
        // CheckedSetChanged. SelectedIndexChanged routes to SetSelectedSingle which
        // fires SelectedSingleChanged. Highlighting a row updates the combo / RA /
        // Dec via SetSelectedSingle; the multi-graph debounce never triggers because
        // CheckedSetChanged isn't raised. No latch needed -- the two events fire
        // independently and route to independent VM mutators.
        private void WireSelectionVm()
        {
            // VM -> UI bindings.
            mSelection.KnownTargetsChanged   += OnVmKnownTargetsChanged;
            mSelection.SelectedSingleChanged += OnVmSelectedSingleChanged;
            mSelection.CheckedSetChanged     += OnVmCheckedSetChanged;
            mSelection.CheckedSetChanged     += OnVmCheckedSetChanged_TriggerGraph;

            // UI -> VM bindings. ComboBox_SelectTarget.SelectedIndexChanged is Designer-wired
            // to ComboBox_SelectTarget_SelectedIndexChanged which routes to the VM. Browse /
            // Sort / VisibleTonight / Select-All / Clear-All buttons are Designer-wired to
            // their own click handlers which talk to the VM directly. RA/Dec CoordinateInput
            // events are subscribed in InitializeDynamicControls and route through
            // OnRightAscensionEdited / OnDeclinationEdited. CheckedListBox needs programmatic
            // wiring for ItemCheck / SelectedIndexChanged because it has no Designer-wired
            // handlers for those events today.
            CheckedListBox_SelectedTargets.ItemCheck      += OnCheckedListBoxItemCheck;
            CheckedListBox_SelectedTargets.SelectedIndexChanged += OnCheckedListBoxSelectedIndexChanged;
        }

        private void OnVmKnownTargetsChanged(object sender, EventArgs e)
        {
            // Repopulate ComboBox_SelectTarget and CheckedListBox_SelectedTargets from the
            // current known-target list (in the current sort order). New rows arrive
            // unchecked -- they are not in the VM's checked set -- so the user opts in
            // target-by-target. preserveSelection:true keeps the combo on the user's
            // current pick: loads now ADD to the catalog, and an add must not yank the
            // combo (and the RA/Dec spinners it drives) off whatever the user selected.
            // The first populate, with nothing selected yet, falls through to the
            // first-sorted auto-pick below.
            PopulateCheckedListBoxFromTargets(defaultChecked: false);
            PopulateTargetComboFromTargets(preserveSelection: true);

            // Rebuild the per-target color map. Name-sorted so the same target lands on
            // the same palette index across reloads of the same folder; consumed by every
            // sub-chart's Render via ctx.TargetColors so all charts agree on each
            // target's color regardless of their Render-time iteration order.
            RebuildTargetColors();

            // Compute duplicate-set tint colors for the listbox owner-draw handler.
            // Recomputed any time KnownTargets changes (load, Add, Remove); see
            // RecomputeDupeSetColors for the TargetIdentity-based grouping.
            RecomputeDupeSetColors();

            // Prune the Visible-tonight tint set to current KnownTargets. A load
            // adds, so tags survive it; Clear All empties the catalog and the tag
            // set with it; Remove drops just the removed target. Listbox repaint
            // follows from PopulateCheckedListBoxFromTargets above.
            mVisibleTaggedTargets.IntersectWith(mSelection.KnownTargets);

            // When nothing is selected yet -- the first populate, or after Clear All
            // emptied the catalog -- establish a default by picking the *first sorted*
            // known target. SetSelectedSingle fires SelectedSingleChanged, which writes
            // the name into the ComboBox and the coords into the RA/Dec inputs, so all
            // three agree. Using KnownTargets[0] would pick load order, which only
            // coincides with sorted order under Name sort. A load that adds onto an
            // already-populated catalog leaves SelectedSingle intact and skips this.
            //
            // NOTE: this seeds the combo's default value only -- it does NOT auto-paint
            // the chart against the seeded target. Rendering the chart with an implicit
            // single target the user didn't ask for created a special case where the
            // combo target was always plotted regardless of checked-state, which
            // confused the "unchecked = nothing rendered" mental model. The chart's
            // baseline paint (axes, dusk/dawn, moon) happens once at boot via the
            // coordinator's initial empty-targets Apply in MainForm's constructor;
            // target curves appear only when the user explicitly checks or clicks
            // Button_Graph.
            if (mSelection.SelectedSingle == null && mSelection.KnownTargets.Count > 0)
            {
                Target firstSorted = SortedTargets(mSelection.KnownTargets).FirstOrDefault();
                if (firstSorted != null) mSelection.SetSelectedSingle(firstSorted);
            }
        }

        private void OnVmSelectedSingleChanged(object sender, EventArgs e)
        {
            Target t = mSelection.SelectedSingle;
            if (t == null) return;

            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try
            {
                mRaInput.SetProgrammatic(t.RightAscension, positive: true);
                mDecInput.SetProgrammatic(t.Declination,   positive: t.North);
                if (ComboBox_SelectTarget.Text != t.Name)
                    ComboBox_SelectTarget.Text = t.Name;
            }
            finally
            {
                mUpdatingUiFromVm = wasUpdating;
            }
        }

        private void OnVmCheckedSetChanged(object sender, EventArgs e)
        {
            // Push VM.Checked state into the listbox row check states. Walks the listbox
            // in display order; reads each row's underlying Target via TargetForRow,
            // then checks/unchecks based on whether VM.Checked contains it.
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try
            {
                for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
                {
                    Target row = TargetForRow(i);
                    bool shouldBeChecked = row != null && mSelection.Checked.Contains(row);
                    CheckState desired = shouldBeChecked ? CheckState.Checked : CheckState.Unchecked;
                    if (CheckedListBox_SelectedTargets.GetItemCheckState(i) != desired)
                        CheckedListBox_SelectedTargets.SetItemCheckState(i, desired);
                }
            }
            finally
            {
                mUpdatingUiFromVm = wasUpdating;
            }
        }

        // CheckedSetChanged handler that arms the multi-graph debounce. A stop+start
        // collapses rapid mutations (toggle three checkboxes in <250 ms succession)
        // into one trailing-edge tick. Constructed lazily on first arm so the timer
        // exists by the time Tick subscribers fire.
        private void OnVmCheckedSetChanged_TriggerGraph(object sender, EventArgs e)
        {
            if (mCheckedToggleDebounce == null)
            {
                mCheckedToggleDebounce = new System.Windows.Forms.Timer
                {
                    Interval = CheckedToggleDebounceMs
                };
                mCheckedToggleDebounce.Tick += CheckedToggleDebounce_Tick;
            }
            mCheckedToggleDebounce.Stop();
            mCheckedToggleDebounce.Start();
        }

        private void OnCheckedListBoxItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = TargetForRow(e.Index);
            if (t == null) return;
            bool isChecked = e.NewValue == CheckState.Checked;
            Log.Diag("UI", $"CheckedListBox_SelectedTargets.ItemCheck target={t.Name} checked={isChecked}");
            mSelection.SetChecked(t, isChecked);
        }

        private void OnCheckedListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;

            // De-selection (no row highlighted): nothing to push to the VM.
            int idx = CheckedListBox_SelectedTargets.SelectedIndex;
            if (idx < 0) return;

            // Highlighting a row updates the single-target combo + RA/Dec inputs via
            // SetSelectedSingle. This fires SelectedSingleChanged only -- the
            // multi-graph debounce subscribes to CheckedSetChanged, so the chart is
            // not re-rendered when the user merely highlights a row. Index-based
            // lookup picks the correct Target instance even when multiple rows
            // share the same name.
            Target t = TargetForRow(idx);
            if (t != null) mSelection.SetSelectedSingle(t);
        }

        // Rebuild mTargetColorsByTarget from the current KnownTargets, Name-sorted.
        // Stable across sort changes (Reorder doesn't touch this), across radio
        // switches (every sub-chart reads the same dict), and across HDM scrubs
        // (Render doesn't reassign). Rebuilds only when KnownTargets changes
        // (NINA load).
        private void RebuildTargetColors()
        {
            mTargetColorsByTarget.Clear();
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;

            var nameSorted = mSelection.KnownTargets
                .Where(t => t != null)
                .OrderBy(t => t.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .ToList();
            int paletteSize = Charts.ChartLayout.TargetColorPalette.Length;
            for (int i = 0; i < nameSorted.Count; i++)
            {
                mTargetColorsByTarget[nameSorted[i]] =
                    Charts.ChartLayout.TargetColorPalette[i % paletteSize];
            }
        }

        // Group KnownTargets into duplicate-sets and give each set of two or more
        // a stable pastel from DupeSetPalette. "Duplicate" is the app-wide
        // definition in TargetIdentity.AreSameTarget -- equal stars-stripped names
        // AND coordinates within ~1 arcminute. Membership is transitive (T1~T2 and
        // T2~T3 puts all three in one set), resolved with a disjoint-set union.
        //
        // Loads collapse duplicates as they arrive (TargetIdentity.SelectNewTargets),
        // so in practice this tints what manual Add / RA-Dec entry has created --
        // the same "spot a target you typed twice" cue the listbox has always
        // given. Targets in no duplicate-set are absent from mDupeSetColors; the
        // listbox owner-draw handler reads "missing" as "use the OS background".
        private void RecomputeDupeSetColors()
        {
            mDupeSetColors.Clear();
            var targets = mSelection?.KnownTargets.Where(t => t != null).ToList()
                          ?? new List<Target>();
            int n = targets.Count;
            if (n == 0)
            {
                CheckedListBox_SelectedTargets?.Invalidate();
                return;
            }

            // DSU.
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            // Two targets can only be duplicates when their normalized names
            // match, so bucket indices by name and run the coordinate test only
            // within a bucket -- O(n) tiny buckets instead of an O(n^2) sweep.
            var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string key = TargetIdentity.NormalizeName(targets[i].Name);
                if (!byName.TryGetValue(key, out var indices))
                {
                    indices = new List<int>();
                    byName[key] = indices;
                }
                indices.Add(i);
            }
            foreach (var indices in byName.Values)
            {
                for (int a = 0; a < indices.Count; a++)
                    for (int b = a + 1; b < indices.Count; b++)
                        if (TargetIdentity.AreSameTarget(targets[indices[a]], targets[indices[b]]))
                            Union(indices[a], indices[b]);
            }

            // Collect connected components.
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var members))
                {
                    members = new List<int>();
                    groups[root] = members;
                }
                members.Add(i);
            }

            int paletteSize = DupeSetPalette.Length;
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                // Order-independent hash of group members, so the same set of
                // targets always lands on the same palette index regardless of
                // KnownTargets insertion order.
                int hash = 0;
                foreach (int idx in kv.Value)
                {
                    Target t = targets[idx];
                    hash ^= System.HashCode.Combine(
                        t.Name,
                        System.Math.Round(t.RightAscension, 6),
                        System.Math.Round(t.Declination, 6),
                        t.North);
                }
                int colorIdx = (hash & 0x7FFFFFFF) % paletteSize;
                System.Drawing.Color c = DupeSetPalette[colorIdx];
                foreach (int idx in kv.Value) mDupeSetColors[targets[idx]] = c;
            }
            CheckedListBox_SelectedTargets?.Invalidate();
        }

        // RowBackground callback wired into DupeAwareCheckedListBox. Returns the
        // dupe-set tint for a row, or null when the row's target isn't in any
        // dupe-set. The listbox subclass owns the actual painting; we just look
        // up by row index -> Items[idx].ToString() (the target name) -> Target.
        private System.Drawing.Color? GetDupeRowBackground(int rowIndex)
        {
            Target row = TargetForRow(rowIndex);
            if (row == null) return null;
            return mDupeSetColors.TryGetValue(row, out var c) ? (System.Drawing.Color?)c : null;
        }

        // Checkbox-interior tint callback for the Visible-tonight tag. Returns
        // VisibleTintColor for tagged rows; the listbox subclass paints a
        // custom checkbox with that interior color so the tag shows on the
        // glyph without touching the row background. Independent from the
        // dupe-set row tint (different paint surfaces), so both signals are
        // visible simultaneously on a row that's duped AND visible-tagged.
        private System.Drawing.Color? GetCheckboxInteriorTint(int rowIndex)
        {
            Target row = TargetForRow(rowIndex);
            if (row == null) return null;
            return mVisibleTaggedTargets.Contains(row) ? (System.Drawing.Color?)VisibleTintColor : null;
        }

        // Right-click on the listbox: explicit "clear the Visible-tonight tint
        // set" gesture. Left-click delegates to standard CheckedListBox check /
        // selection behavior (this handler returns early on non-right).
        private void OnSelectedTargetsMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (mVisibleTaggedTargets.Count == 0) return;
            mVisibleTaggedTargets.Clear();
            CheckedListBox_SelectedTargets?.Invalidate();
        }
    }
}
