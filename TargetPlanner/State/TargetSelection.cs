using System;
using System.Collections.Generic;
using System.Linq;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.State
{
    /// <summary>
    /// Observable view-model of the user's target selection. Owns:
    ///   - <see cref="KnownTargets"/>  — the loaded NINA target list.
    ///   - <see cref="SelectedSingle"/> — the active single target (also driven by
    ///     RA/Dec edits; may not be a member of <see cref="KnownTargets"/>).
    ///   - <see cref="Checked"/>       — the multi-target set used for the
    ///     auto-debounced multi-graph render.
    /// </summary>
    /// <remarks>
    /// Phase 2 of the SoC refactor introduced this type as the single source of truth
    /// for target-selection state. UI controls bind to it: user input flows into the VM
    /// through <c>SetX</c>/<c>ToggleX</c> mutators; programmatic state changes flow back
    /// to the UI via the three <see cref="SelectedSingleChanged"/>/
    /// <see cref="CheckedSetChanged"/>/<see cref="KnownTargetsChanged"/> events. Callers
    /// guard the echo path with a per-form <c>mUpdatingUiFromVm</c> flag so VM-driven UI
    /// writes don't re-enter the VM.
    /// <para>
    /// All mutators short-circuit when the new value matches the current value (reference
    /// equality for targets; set equality for the checked set). Events fire only on actual
    /// change.
    /// </para>
    /// <para>
    /// There is no <c>Mode</c> property — render dispatch is explicit at the consumer:
    /// <c>Button_Graph</c> renders <see cref="SelectedSingle"/>; multi-graph auto-fires
    /// off <see cref="CheckedSetChanged"/> via a debounced trigger in MainForm.
    /// </para>
    /// </remarks>
    public sealed class TargetSelection
    {
        private List<Target> mKnown = new List<Target>();
        private Target mSelected;
        private HashSet<Target> mChecked = new HashSet<Target>();

        public IReadOnlyList<Target> KnownTargets => mKnown;
        public Target SelectedSingle => mSelected;
        public IReadOnlyCollection<Target> Checked => mChecked;

        public event EventHandler KnownTargetsChanged;
        public event EventHandler SelectedSingleChanged;
        public event EventHandler CheckedSetChanged;

        /// <summary>
        /// Replace the loaded-target catalog. <see cref="Checked"/> is reset to empty
        /// (default-none-checked policy: a fresh load presents the candidate list with
        /// everything unchecked, so the user opts in target-by-target rather than opting
        /// out). <see cref="SelectedSingle"/> is preserved iff it's still a member of the
        /// new known set; otherwise cleared.
        /// </summary>
        public void SetKnownTargets(IEnumerable<Target> targets)
        {
            mKnown = targets?.ToList() ?? new List<Target>();

            bool selectionDropped = false;
            if (mSelected != null && !mKnown.Contains(mSelected))
            {
                mSelected = null;
                selectionDropped = true;
            }

            // Default-none-checked policy. The set always replaces; we always notify, since
            // even a same-equal HashSet here is semantically a "fresh population" event for
            // listeners (and feeds the multi-graph debounce trigger so the chart blanks
            // automatically on a fresh NINA load).
            mChecked = new HashSet<Target>();

            KnownTargetsChanged?.Invoke(this, EventArgs.Empty);
            CheckedSetChanged?.Invoke(this, EventArgs.Empty);
            if (selectionDropped) SelectedSingleChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set the active <see cref="SelectedSingle"/> target. Accepts targets not in
        /// <see cref="KnownTargets"/> (e.g. user-typed coords via RA/Dec inputs). No-op
        /// when the target reference matches the current state.
        /// </summary>
        public void SetSelectedSingle(Target t)
        {
            if (object.ReferenceEquals(mSelected, t)) return;
            mSelected = t;
            SelectedSingleChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Add or remove <paramref name="t"/> from <see cref="Checked"/>. No-op if the
        /// target is null or already in the requested state.
        /// </summary>
        public void SetChecked(Target t, bool isChecked)
        {
            if (t == null) return;

            bool wasChecked = mChecked.Contains(t);
            if (wasChecked == isChecked) return;

            if (isChecked) mChecked.Add(t);
            else           mChecked.Remove(t);

            CheckedSetChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Replace the entire <see cref="Checked"/> set in one shot. Used by
        /// Visible-Tonight, Select-All, Clear-All and similar batch ops.
        /// </summary>
        public void SetCheckedSet(IEnumerable<Target> targets)
        {
            HashSet<Target> newSet = targets != null
                ? new HashSet<Target>(targets)
                : new HashSet<Target>();

            if (mChecked.SetEquals(newSet)) return;

            mChecked = newSet;
            CheckedSetChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Bulk check/uncheck every target in <see cref="KnownTargets"/>.
        /// </summary>
        public void SetAllChecked(bool isChecked)
        {
            SetCheckedSet(isChecked ? mKnown : Enumerable.Empty<Target>());
        }
    }
}
