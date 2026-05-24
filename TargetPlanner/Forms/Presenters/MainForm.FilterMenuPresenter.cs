using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Astronomy.Core.Moon;
using TargetPlanner.Filters;
using TargetPlanner.Forms;
using TargetPlanner.State;
using TargetPlanner.Support;

using TpFilter = TargetPlanner.Filters.Filter;

namespace TargetPlanner
{
    // Filter-library / moon-avoidance concern: Filters menu + GroupBox_Moon_Filters
    // radio strip + Lorentzian control scrub auto-save + Edit Filters dialog
    // wiring. Largest of the three presenter file splits (~625 lines) -- the
    // cluster covers initial menu build, radio-strip sync, auto-save debounce,
    // master enable-checkbox, defaults restore, and the dialog round-trip.
    //
    // Same partial-class file-split rationale as 7.4a/7.4b: heavy MainForm
    // coupling (mFilterLibrary, mActiveFilter, mFilterMenuItems, mFilterRadios,
    // mFilterAutoSaveDebounce, mSuppressFilterEvents, mEditFiltersDialogOpen,
    // CheckBox_Moon_AvoidanceEnable, GroupBox_Moon_Filters, every NumericUpDown_Moon_*,
    // mCoordinator + SnapshotCurrent + mLC2Sky) makes constructor-injection
    // ceremony heavier than the relocation is worth.
    public partial class MainForm
    {
        // Loads the filter library (or ships defaults on first launch) and builds the
        // Filters menu's flat radio group -- one item per library filter. Modified
        // filters (values differ from FilterLibrary.BuiltinDefaults) get a trailing ' *'
        // in their menu label. Right-click on a filter opens the modal EditFiltersForm
        // pre-selected on that filter. The CheckBox_Moon_AvoidanceEnable on
        // GroupBox_Moon_Avoidance is the master on/off switch -- the menu is
        // filter-selection only. The menu is appended to MenuStrip_MainForm at
        // construction time, after File and Help.
        private void BuildFiltersMenu()
        {
            mFilterLibrary = FilterLibrary.LoadOrDefault();

            // Top-level "&Filters" menu is Designer-resident (FiltersToolStripMenuItem_MainForm
            // in MainForm.Designer.cs). Children are populated dynamically per-call:
            // first call after form load + every Edit Filters dialog Save. Clearing on
            // each call keeps the menu in sync with the live library; no first-call
            // special case needed.
            FiltersToolStripMenuItem_MainForm.DropDownItems.Clear();

            mFilterMenuItems = new List<ToolStripMenuItem>();

            TpFilter firstFilter = null;
            ToolStripMenuItem firstFilterItem = null;
            foreach (TpFilter filter in mFilterLibrary.Filters)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(filter.Name);
                // Capture by Name (stable across auto-save) -- the auto-save tick
                // replaces the Filter instance in the library, so a captured-by-reference
                // filter would go stale and SetActiveFilter would fail to find it.
                string capturedName = filter.Name;
                ToolStripMenuItem capturedItem = item;
                item.Click += (s, e) =>
                {
                    Log.Diag("UI", $"Menu Filters.{capturedName}.Click");
                    TpFilter live = mFilterLibrary != null ? mFilterLibrary.Find(capturedName) : null;
                    if (live != null) SetActiveFilter(live);
                };
                // Right-click: dismiss the dropdown and open the modal Edit Filters
                // dialog pre-selected on this filter. ToolStripMenuItem doesn't have a
                // dedicated right-click event; MouseDown is the standard hook.
                item.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Right) return;
                    ToolStripDropDown owner = capturedItem.Owner as ToolStripDropDown;
                    if (owner != null) owner.Close();
                    OpenEditFiltersDialog(capturedName);
                };
                FiltersToolStripMenuItem_MainForm.DropDownItems.Add(item);
                mFilterMenuItems.Add(item);
                if (firstFilter == null)
                {
                    firstFilter = filter;
                    firstFilterItem = item;
                }
            }

            // Pre-select the first library filter visually and write its values into
            // the Lorentzian controls so the GroupBox has sensible defaults from the
            // start. Whether avoidance actually applies is gated on
            // CheckBox_Moon_AvoidanceEnable.
            //
            // Critical: do NOT route through SetActiveFilter here -- SetActiveFilter
            // calls mCoordinator?.Apply, whose pipeline can run while the chart's
            // year caches are still being built (the M31 seed's async BuildSeriesList
            // is still mid-flight at construction time). Just stash mActiveFilter and
            // mirror its Lorentzian into the controls; SnapshotCurrent will pick it up
            // on the next legitimate Apply. The post-Edit-Filters caller in
            // OpenEditFiltersDialog explicitly fires Apply afterward, when caches are
            // guaranteed populated.
            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            if (firstFilter != null)
            {
                firstFilterItem.Checked = true;
                WriteProfileToControls(firstFilter.ToProfile());
                mActiveFilter = firstFilter;
            }
            else
            {
                // Empty library: nothing checked, no active filter.
                mActiveFilter = null;
            }
            SetLorentzianControlsEnabled(avoidanceOn);

            // Build the parallel radio strip in GroupBox_Moon_Filters and stamp the
            // initial '*' state across all surfaces (menu items, radios, top-level title).
            BuildFiltersGroupBox();
            RefreshFilterMenuLabels();
        }

        // Populate GroupBox_Moon_Filters with one RadioButton per library filter plus a
        // Defaults button. Defaults takes a fixed 70 px on the right; radios share the
        // remaining width equally. Idempotent rebuild -- called by BuildFiltersMenu so
        // the strip stays in sync with the menu after every Edit Filters Save.
        //
        // Click on a radio activates that filter (mirrors the menu); right-click opens
        // the Edit Filters dialog pre-selected on that filter (also mirrors the menu).
        // The Defaults button restores the active filter to factory defaults via
        // OnFilterDefaultsClick.
        private void BuildFiltersGroupBox()
        {
            if (GroupBox_Moon_Filters == null) return;

            GroupBox_Moon_Filters.SuspendLayout();
            try
            {
                GroupBox_Moon_Filters.Controls.Clear();
                if (mFilterRadios == null) mFilterRadios = new List<RadioButton>();
                mFilterRadios.Clear();

                Rectangle area = GroupBox_Moon_Filters.DisplayRectangle;
                const int DefaultsWidth = 70;
                const int Gap = 4;
                const int Padding = 6;
                int top = area.Top + 2;
                int rowH = Math.Max(16, area.Height - 4);

                int defaultsX = area.Right - DefaultsWidth - Padding;
                mFilterDefaultsButton = new Button
                {
                    Name     = "Button_Moon_Defaults",
                    Text     = "Defaults",
                    Location = new Point(defaultsX, top - 2),
                    Size     = new Size(DefaultsWidth, rowH + 2),
                    TabStop  = false,
                };
                mFilterDefaultsButton.Click += OnFilterDefaultsClick;
                GroupBox_Moon_Filters.Controls.Add(mFilterDefaultsButton);

                int radioLeft = area.Left + Padding;
                int radioAreaWidth = (defaultsX - Gap) - radioLeft;
                int n = mFilterLibrary != null ? mFilterLibrary.Filters.Count : 0;
                if (n == 0) return;
                int radioWidth = radioAreaWidth / n;

                int x = radioLeft;
                for (int i = 0; i < n; i++)
                {
                    TpFilter filter = mFilterLibrary.Filters[i];
                    bool modified = FilterLibrary.DiffersFromBuiltinDefault(filter);
                    // Capture by Name (stable across auto-save) -- the auto-save tick
                    // replaces the Filter instance in the library, so a captured-by-
                    // reference filter would go stale (clicking the radio would try to
                    // activate an instance no longer in the library and SetActiveFilter
                    // would fail to find an index match, un-checking every radio).
                    string capturedName = filter.Name;
                    RadioButton radio = new RadioButton
                    {
                        Name     = "RadioButton_Moon_" + IdentifierSafe(filter.Name),
                        Text     = filter.Name + (modified ? " *" : ""),
                        Location = new Point(x, top),
                        Size     = new Size(radioWidth, rowH),
                        TabStop  = false,
                        Checked  = object.ReferenceEquals(mActiveFilter, filter),
                    };
                    radio.Click += (s, e) =>
                    {
                        if (mSuppressFilterEvents) return;
                        TpFilter live = mFilterLibrary != null ? mFilterLibrary.Find(capturedName) : null;
                        if (live != null) SetActiveFilter(live);
                    };
                    radio.MouseDown += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Right) OpenEditFiltersDialog(capturedName);
                    };
                    GroupBox_Moon_Filters.Controls.Add(radio);
                    mFilterRadios.Add(radio);
                    x += radioWidth;
                }
            }
            finally
            {
                GroupBox_Moon_Filters.ResumeLayout();
            }
        }

        // Strip non-identifier characters from a filter name so the generated control
        // Name field stays diagnosable in the VS debugger ("Hα-7nm" -> "H7nm").
        private static string IdentifierSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unnamed";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "Unnamed";
        }

        // Defaults button click: restore EVERY library filter that has a factory
        // built-in baseline to its factory values. User-created filters (no baseline
        // via FilterLibrary.FindBuiltinDefault) are skipped silently. Persists once
        // at the end, then refreshes both UI surfaces, re-applies the active filter
        // (its values may have just been reset), and surfaces a 1-second transient
        // message so the user sees what happened.
        private void OnFilterDefaultsClick(object sender, EventArgs e)
        {
            if (mFilterLibrary == null || mFilterLibrary.Filters.Count == 0) return;

            string activeName = mActiveFilter != null ? mActiveFilter.Name : null;
            bool anyChanged = false;

            for (int i = 0; i < mFilterLibrary.Filters.Count; i++)
            {
                TpFilter current = mFilterLibrary.Filters[i];
                TpFilter builtin = FilterLibrary.FindBuiltinDefault(current.Name);
                if (builtin == null) continue;  // user-created -- no factory baseline

                // Adopt the builtin's values; preserve the current Name's casing
                // (FindBuiltinDefault matched case-insensitively).
                TpFilter restored = new TpFilter(
                    Name:           current.Name,
                    SeparationDeg:  builtin.SeparationDeg,
                    WidthDays:      builtin.WidthDays,
                    RelaxEnabled:   builtin.RelaxEnabled,
                    RelaxMinAltDeg: builtin.RelaxMinAltDeg,
                    RelaxMaxAltDeg: builtin.RelaxMaxAltDeg,
                    RelaxScale:     builtin.RelaxScale,
                    CenterNm:       builtin.CenterNm,
                    BandwidthNm:    builtin.BandwidthNm);
                mFilterLibrary.Replace(i, restored);
                anyChanged = true;
            }

            if (!anyChanged) return;  // every filter was user-created

            try { mFilterLibrary.Save(); }
            catch (Exception ex) { Log.Error("FilterLibrary.Save (Defaults click) failed", ex); }

            // Re-resolve mActiveFilter by name -- if it was a builtin, the Replace
            // above swapped its instance; if user-created it's unchanged. Either way
            // SetActiveFilter pushes the (possibly-reset) values into the Lorentzian
            // controls + chart.
            TpFilter newActive = activeName != null ? mFilterLibrary.Find(activeName) : null;
            if (newActive != null) SetActiveFilter(newActive);
            RefreshFilterMenuLabels();

            ShowTransientMessage("Filters reset to defaults", 1000);
        }

        // Walk both UI surfaces (menu items + groupbox radios) updating each label from
        // the corresponding library filter's modified-vs-default state. Filters whose
        // values differ from FilterLibrary.BuiltinDefaults get a trailing ' *'; the
        // top-level FiltersToolStripMenuItem_MainForm.Text also gets ' *' iff any
        // filter is modified. User-created filters (no built-in baseline) always show
        // no '*'. Called after BuildFiltersMenu/BuildFiltersGroupBox initial setup and
        // after every filter auto-save tick.
        private void RefreshFilterMenuLabels()
        {
            if (mFilterLibrary == null) return;
            int filterCount = mFilterLibrary.Filters.Count;
            int menuN  = mFilterMenuItems != null ? Math.Min(mFilterMenuItems.Count, filterCount) : 0;
            int radioN = mFilterRadios    != null ? Math.Min(mFilterRadios.Count,    filterCount) : 0;
            bool anyModified = false;

            for (int i = 0; i < filterCount; i++)
            {
                TpFilter f = mFilterLibrary.Filters[i];
                bool modified = FilterLibrary.DiffersFromBuiltinDefault(f);
                if (modified) anyModified = true;
                string label = f.Name + (modified ? " *" : "");
                if (i < menuN)  mFilterMenuItems[i].Text = label;
                if (i < radioN) mFilterRadios[i].Text    = label;
            }

            FiltersToolStripMenuItem_MainForm.Text = anyModified ? "&Filters *" : "&Filters";
        }

        // Open the modal Edit Filters dialog. Suspends the main-form auto-save while
        // the dialog is showing (the dialog has its own transactional Save against a
        // shadow BindingList). After Save: rebuild the menu, re-resolve mActiveFilter
        // to the prior-active by name (BuildFiltersMenu's early-init points it at the
        // first filter), refresh the chart.
        private void OpenEditFiltersDialog(string preSelectName = null)
        {
            mEditFiltersDialogOpen = true;
            if (mFilterAutoSaveDebounce != null) mFilterAutoSaveDebounce.Stop();

            // Capture the prior-active name BEFORE BuildFiltersMenu (it overwrites
            // mActiveFilter via the early-init pre-select). After Save, we want to keep
            // the user on whatever filter they had active before opening the dialog,
            // not jump to whichever happens to be first in the library.
            string priorActiveName = mActiveFilter != null ? mActiveFilter.Name : null;

            try
            {
                using (EditFiltersForm dlg = new EditFiltersForm(mFilterLibrary, preSelectName))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                }
            }
            finally
            {
                mEditFiltersDialogOpen = false;
            }

            // The library was mutated in place + persisted by the dialog. Rebuild the
            // Filters menu so renamed / added / removed entries show up, then re-resolve
            // mActiveFilter back to the prior-active by name when possible. The master
            // CheckBox_Moon_AvoidanceEnable state is preserved by BuildFiltersMenu.
            BuildFiltersMenu();
            RefreshActiveFilterAfterDialogSave(priorActiveName);

            // Route the post-save refresh through the single-seam coordinator
            // pipeline. RefreshActiveFilterAfterDialogSave's tail call to
            // SetActiveFilter fires a coordinator Apply on the common path; the
            // belt-and-suspenders Apply here covers the empty-library edge case
            // (every filter deleted in the dialog) where RefreshActiveFilter
            // takes the early null-return branch. The 150 ms internal debounce
            // collapses any double-Apply into a single trailing-edge pipeline run.
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Re-resolve mActiveFilter to a Filter instance in the post-dialog mFilterLibrary
        // by case-insensitive name match against priorActiveName. Falls back to the
        // first library filter when priorActiveName has been renamed or removed.
        // Routes through SetActiveFilter so the menu radio + Lorentzian controls + chart
        // pick up the (possibly modified) values.
        private void RefreshActiveFilterAfterDialogSave(string priorActiveName)
        {
            TpFilter found = null;
            if (priorActiveName != null)
            {
                foreach (TpFilter f in mFilterLibrary.Filters)
                {
                    if (string.Equals(f.Name, priorActiveName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = f;
                        break;
                    }
                }
            }
            if (found == null && mFilterLibrary.Filters.Count > 0)
                found = mFilterLibrary.Filters[0];

            if (found == null)
            {
                mActiveFilter = null;
                return;
            }

            SetActiveFilter(found);
        }

        // Activate the named filter on both UI surfaces (menu items + groupbox radios):
        // resolve the index once, then walk each surface setting Checked = (i == idx).
        // Populates the Lorentzian controls from the filter's profile, pushes the profile
        // to the chart (gated on the master Enable), and sets mActiveFilter -- the
        // auto-save target that scrubbing the controls will mutate via
        // FilterAutoSaveDebounce_Tick.
        private void SetActiveFilter(TpFilter filter)
        {
            if (filter == null) return;

            mActiveFilter = filter;
            MoonAvoidanceProfile profile = filter.ToProfile();

            int idx = -1;
            for (int i = 0; i < mFilterLibrary.Filters.Count; i++)
            {
                if (object.ReferenceEquals(mFilterLibrary.Filters[i], filter)) { idx = i; break; }
            }

            if (mFilterMenuItems != null)
            {
                for (int i = 0; i < mFilterMenuItems.Count; i++)
                    mFilterMenuItems[i].Checked = (i == idx);
            }

            if (mFilterRadios != null)
            {
                // Programmatic Checked changes don't fire RadioButton.Click (we use Click,
                // not CheckedChanged, so there's no recursion); the suppress flag is a
                // belt-and-suspenders guard against any future CheckedChanged subscriber.
                bool wasSuppressed = mSuppressFilterEvents;
                mSuppressFilterEvents = true;
                try
                {
                    for (int i = 0; i < mFilterRadios.Count; i++)
                        mFilterRadios[i].Checked = (i == idx);
                }
                finally { mSuppressFilterEvents = wasSuppressed; }
            }

            // WriteProfileToControls raises mSuppressFilterEvents internally so its
            // writes don't trigger OnLorentzianControlChanged's auto-save debounce.
            WriteProfileToControls(profile);

            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            SetLorentzianControlsEnabled(avoidanceOn);

            // mActiveFilter is the single source of truth for both K-S inputs
            // (CenterNm + BandwidthNm) and the Lorentzian moon-clear gate
            // (via ToProfile()). Apply funnels through SnapshotCurrent which reads
            // mActiveFilter into PlanningPolicy.ActiveFilter; the coordinator's
            // post-apply hook calls PushSkyKSInputs(ctx) to push center+bandwidth
            // into the Sky chart's per-minute K-S walk.
            mCoordinator?.Apply(SnapshotCurrent());
            // SessionSolvers modes consume Policy.MoonProfile (and indirectly the active
            // filter via the moon-avoidance Lorentzian) -- re-rank when the active
            // filter changes. Helper short-circuits when sort mode isn't Longest/Highest.
            MaybeResortForSessionSolversInputChange();
        }

        // Master on/off for moon avoidance. SnapshotCurrent reads CheckBox_Moon_-
        // AvoidanceEnable directly into PlanningPolicy.MoonAvoidanceEnabled; the
        // derived Policy.MoonProfile returns null when the toggle is off, so the
        // placement-primitive moon gate short-circuits to visibility-only.
        private void OnAvoidanceEnableChanged(object sender, EventArgs e)
        {
            if (mSubCharts == null) return;

            bool enabled = CheckBox_Moon_AvoidanceEnable.Checked;
            SetLorentzianControlsEnabled(enabled);
            // Coordinator's internal debounce collapses a fast Enable-Disable-
            // Enable click sequence into one trailing-edge pipeline run.
            mCoordinator?.Apply(SnapshotCurrent());
            // SessionSolvers ranking respects MoonProfile gating, so a master-toggle
            // change affects the listbox ordering. Helper short-circuits when sort
            // mode isn't Longest/Highest.
            MaybeResortForSessionSolversInputChange();
        }

        // User scrubbed a Lorentzian control. Eagerly mutate mActiveFilter (via record
        // `with`) so SnapshotCurrent sees the live values, then start the auto-save
        // debounce -- after 500 ms idle, the tick handler persists mActiveFilter to
        // filters.json. Returns early under mSuppressFilterEvents
        // (WriteProfileToControls is the writer; its writes aren't user edits).
        private void OnLorentzianControlChanged(object sender, EventArgs e)
        {
            if (mSuppressFilterEvents) return;
            if (NumericUpDown_Moon_Separation == null) return;
            if (mActiveFilter == null) return;

            mActiveFilter = mActiveFilter with
            {
                SeparationDeg  = (double)NumericUpDown_Moon_Separation.Value,
                WidthDays      = (double)NumericUpDown_Moon_Width.Value,
                RelaxEnabled   = CheckBox_Moon_RelaxEnabled.Checked,
                RelaxMinAltDeg = (double)NumericUpDown_Moon_RelaxMin.Value,
                RelaxMaxAltDeg = (double)NumericUpDown_Moon_RelaxMax.Value,
                RelaxScale     = (double)NumericUpDown_Moon_RelaxScale.Value,
            };
            RestartFilterAutoSaveDebounce();
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // CheckBox_Moon_RelaxEnabled toggled: re-gate the relaxation Min/Max/Scale
        // params (enabled only while this is checked), then run the standard
        // Lorentzian-changed path -- the avoidance profile carries RelaxEnabled, so
        // the chart must rebuild.
        private void OnRelaxEnabledChanged(object sender, EventArgs e)
        {
            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            SetLorentzianControlsEnabled(avoidanceOn);
            OnLorentzianControlChanged(sender, e);
        }

        // Lazily-constructed shared Timer for the Lorentzian-scrub auto-save. Same
        // restart-on-edit pattern as RestartSessionsRebuildDebounce: ValueChanged calls
        // Stop()+Start() to reset the interval, so rapid edits collapse to one
        // trailing-edge Tick.
        private void RestartFilterAutoSaveDebounce()
        {
            if (mFilterAutoSaveDebounce == null)
            {
                mFilterAutoSaveDebounce = new System.Windows.Forms.Timer { Interval = FilterAutoSaveDebounceMs };
                mFilterAutoSaveDebounce.Tick += FilterAutoSaveDebounce_Tick;
            }
            mFilterAutoSaveDebounce.Stop();
            mFilterAutoSaveDebounce.Start();
        }

        // Trailing-edge tick for the Lorentzian-scrub auto-save. mActiveFilter already
        // holds the live values (OnLorentzianControlChanged eagerly mutates it via
        // record `with`); the tick just commits the current mActiveFilter into
        // mFilterLibrary + persists to disk + refreshes the menu '*' labels.
        // Suppressed while the EditFiltersForm modal is open; the dialog has its own
        // Save semantics against a transactional shadow.
        private void FilterAutoSaveDebounce_Tick(object sender, EventArgs e)
        {
            mFilterAutoSaveDebounce.Stop();
            if (mEditFiltersDialogOpen) return;
            if (mActiveFilter == null) return;

            int idx = IndexOfActiveFilter();
            if (idx < 0) return;

            mFilterLibrary.Replace(idx, mActiveFilter);

            try { mFilterLibrary.Save(); }
            catch (Exception ex) { Log.Error("FilterLibrary.Save (auto-save) failed", ex); }

            RefreshFilterMenuLabels();
        }

        // Locate the active filter's index in mFilterLibrary by Name. Reference equality
        // would fail because OnLorentzianControlChanged constructs new Filter instances
        // via record `with` on each scrub -- mActiveFilter and the library entry share
        // a Name but not an instance until the next FilterAutoSaveDebounce_Tick syncs
        // them. Returns -1 when mActiveFilter has been removed from the library
        // (post-dialog Save with delete) or when the library is empty.
        private int IndexOfActiveFilter()
        {
            if (mActiveFilter == null || mFilterLibrary == null) return -1;
            for (int i = 0; i < mFilterLibrary.Filters.Count; i++)
            {
                if (string.Equals(mFilterLibrary.Filters[i].Name, mActiveFilter.Name,
                                  StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // Push the profile's parameters into the Lorentzian controls. Raises
        // mSuppressFilterEvents so the writes don't fire OnLorentzianControlChanged
        // (which would recursively flip the menu to Custom). No-op when profile is null
        // -- "Disabled" doesn't have values to push, the controls just go grey.
        private void WriteProfileToControls(MoonAvoidanceProfile profile)
        {
            if (profile == null) return;
            if (NumericUpDown_Moon_Separation == null) return;

            bool wasSuppressed = mSuppressFilterEvents;
            mSuppressFilterEvents = true;
            try
            {
                NumericUpDown_Moon_Separation.Value =
                    ClampToRange(NumericUpDown_Moon_Separation, (decimal)profile.SeparationDeg);
                NumericUpDown_Moon_Width.Value =
                    ClampToRange(NumericUpDown_Moon_Width, (decimal)profile.WidthDays);
                CheckBox_Moon_RelaxEnabled.Checked = profile.RelaxEnabled;
                NumericUpDown_Moon_RelaxMin.Value =
                    ClampToRange(NumericUpDown_Moon_RelaxMin, (decimal)profile.RelaxMinAltDeg);
                NumericUpDown_Moon_RelaxMax.Value =
                    ClampToRange(NumericUpDown_Moon_RelaxMax, (decimal)profile.RelaxMaxAltDeg);
                NumericUpDown_Moon_RelaxScale.Value =
                    ClampToRange(NumericUpDown_Moon_RelaxScale, (decimal)profile.RelaxScale);
            }
            finally
            {
                mSuppressFilterEvents = wasSuppressed;
            }
        }

        // Enable/disable the moon-avoidance controls in a two-level hierarchy.
        // The master CheckBox_Moon_AvoidanceEnable (passed as avoidanceEnabled)
        // gates the Separation / Width controls and the Relaxation-Enable toggle.
        // The relaxation Min/Max/Scale params (labels + spinners) sit one level
        // deeper -- enabled only when avoidance is on AND CheckBox_Moon_RelaxEnabled
        // is checked. CheckBox_Moon_AvoidanceEnable itself is never disabled here.
        private void SetLorentzianControlsEnabled(bool avoidanceEnabled)
        {
            if (NumericUpDown_Moon_Separation == null) return;

            Label_Moon_Separation.Enabled         = avoidanceEnabled;
            NumericUpDown_Moon_Separation.Enabled  = avoidanceEnabled;
            Label_Moon_Width.Enabled              = avoidanceEnabled;
            NumericUpDown_Moon_Width.Enabled       = avoidanceEnabled;
            CheckBox_Moon_RelaxEnabled.Enabled    = avoidanceEnabled;

            bool relaxParamsEnabled = avoidanceEnabled && CheckBox_Moon_RelaxEnabled.Checked;
            Label_Moon_RelaxMin.Enabled           = relaxParamsEnabled;
            NumericUpDown_Moon_RelaxMin.Enabled   = relaxParamsEnabled;
            Label_Moon_RelaxMax.Enabled           = relaxParamsEnabled;
            NumericUpDown_Moon_RelaxMax.Enabled   = relaxParamsEnabled;
            Label_Moon_RelaxScale.Enabled         = relaxParamsEnabled;
            NumericUpDown_Moon_RelaxScale.Enabled = relaxParamsEnabled;
        }
    }
}
