using System;
using System.Collections.Generic;
using System.Linq;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.State
{
    public enum GraphMode { Single, Multi }

    /// <summary>
    /// Observable view-model of the user's target selection. Owns:
    ///   - <see cref="KnownTargets"/>  — the loaded NINA target list.
    ///   - <see cref="SelectedSingle"/> — the active target in Single mode (also driven by
    ///     RA/Dec edits; may not be a member of <see cref="KnownTargets"/>).
    ///   - <see cref="Checked"/>       — the multi-target set used in Multi mode.
    ///   - <see cref="Mode"/>          — last-touched mode dispatch for <c>Button_Graph_Click</c>.
    /// </summary>
    /// <remarks>
    /// Phase 2 of the SoC refactor introduces this type as the single source of truth for
    /// target-selection state. UI controls bind to it: user input flows into the VM through
    /// <c>SetX</c>/<c>ToggleX</c> mutators; programmatic state changes flow back to the UI
    /// via the four <see cref="SelectedSingleChanged"/>/<see cref="CheckedSetChanged"/>/
    /// <see cref="KnownTargetsChanged"/>/<see cref="ModeChanged"/> events. Callers guard the
    /// echo path with a per-form <c>mUpdatingUiFromVm</c> flag so VM-driven UI writes don't
    /// re-enter the VM.
    /// <para>
    /// All mutators short-circuit when the new value matches the current value (reference
    /// equality for targets; set equality for the checked set). Events fire only on actual
    /// change.
    /// </para>
    /// </remarks>
    public sealed class TargetSelection
    {
        private List<Target> mKnown = new List<Target>();
        private Target mSelected;
        private HashSet<Target> mChecked = new HashSet<Target>();
        private GraphMode mMode = GraphMode.Single;

        public IReadOnlyList<Target> KnownTargets => mKnown;
        public Target SelectedSingle => mSelected;
        public IReadOnlyCollection<Target> Checked => mChecked;
        public GraphMode Mode => mMode;

        public event EventHandler KnownTargetsChanged;
        public event EventHandler SelectedSingleChanged;
        public event EventHandler CheckedSetChanged;
        public event EventHandler ModeChanged;

        /// <summary>
        /// Replace the loaded-target catalog. <see cref="Checked"/> is reset to the new
        /// known-target set (default-all-checked policy: a fresh load primes the Multi-mode
        /// candidate list to "everything"). <see cref="SelectedSingle"/> is preserved iff
        /// it's still a member of the new known set; otherwise cleared.
        /// <see cref="Mode"/> is NOT changed -- the user's last-touched mode survives a load.
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

            // Default-all-checked policy. The set always replaces; we always notify, since
            // even a same-equal HashSet here is semantically a "fresh population" event for
            // listeners (the underlying Target instances are likely new even if names match).
            mChecked = new HashSet<Target>(mKnown);

            KnownTargetsChanged?.Invoke(this, EventArgs.Empty);
            CheckedSetChanged?.Invoke(this, EventArgs.Empty);
            if (selectionDropped) SelectedSingleChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set the active Single-mode target. Sets <see cref="Mode"/> to
        /// <see cref="GraphMode.Single"/>. Accepts targets not in <see cref="KnownTargets"/>
        /// (e.g. user-typed coords via RA/Dec inputs). No-op when both target reference and
        /// mode match the current state.
        /// </summary>
        public void SetSelectedSingle(Target t)
        {
            bool targetChanged = !object.ReferenceEquals(mSelected, t);
            bool modeChanged   = mMode != GraphMode.Single;
            if (!targetChanged && !modeChanged) return;

            mSelected = t;
            mMode     = GraphMode.Single;

            if (targetChanged) SelectedSingleChanged?.Invoke(this, EventArgs.Empty);
            if (modeChanged)   ModeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Add or remove <paramref name="t"/> from <see cref="Checked"/>. Sets
        /// <see cref="Mode"/> to <see cref="GraphMode.Multi"/>. No-op if the target is null
        /// or already in the requested state when mode is already Multi.
        /// </summary>
        public void SetChecked(Target t, bool isChecked)
        {
            if (t == null) return;

            bool wasChecked = mChecked.Contains(t);
            bool checkedChanged = wasChecked != isChecked;
            bool modeChanged    = mMode != GraphMode.Multi;
            if (!checkedChanged && !modeChanged) return;

            if (checkedChanged)
            {
                if (isChecked) mChecked.Add(t);
                else           mChecked.Remove(t);
            }
            mMode = GraphMode.Multi;

            if (checkedChanged) CheckedSetChanged?.Invoke(this, EventArgs.Empty);
            if (modeChanged)    ModeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Replace the entire <see cref="Checked"/> set in one shot. Sets <see cref="Mode"/>
        /// to <see cref="GraphMode.Multi"/>. Used by Visible-Tonight and similar batch ops.
        /// </summary>
        public void SetCheckedSet(IEnumerable<Target> targets)
        {
            HashSet<Target> newSet = targets != null
                ? new HashSet<Target>(targets)
                : new HashSet<Target>();

            bool checkedChanged = !mChecked.SetEquals(newSet);
            bool modeChanged    = mMode != GraphMode.Multi;
            if (!checkedChanged && !modeChanged) return;

            mChecked = newSet;
            mMode    = GraphMode.Multi;

            if (checkedChanged) CheckedSetChanged?.Invoke(this, EventArgs.Empty);
            if (modeChanged)    ModeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Bulk check/uncheck every target in <see cref="KnownTargets"/>. Sets
        /// <see cref="Mode"/> to <see cref="GraphMode.Multi"/>.
        /// </summary>
        public void SetAllChecked(bool isChecked)
        {
            SetCheckedSet(isChecked ? mKnown : Enumerable.Empty<Target>());
        }

        /// <summary>
        /// Force <see cref="Mode"/> without altering selection. Rare; the typical mutators
        /// ( <see cref="SetSelectedSingle"/>, <see cref="SetChecked"/>,
        /// <see cref="SetCheckedSet"/>, <see cref="SetAllChecked"/> ) imply mode.
        /// </summary>
        public void SetMode(GraphMode m)
        {
            if (mMode == m) return;
            mMode = m;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
