using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Astronomy.Core.Moon;
using TargetPlanner.Filters;
using TargetPlanner.Forms;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Updates;
using System.Threading;
using System.Threading.Tasks;
using LocalLib;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;
using TpFilter = TargetPlanner.Filters.Filter;

namespace TargetPlanner
{
    public partial class MainForm : Form
    {
        private OpenFolderDialog mFolder;

        private Location mLocation;
        private (DateTime When, TimeZoneInfo Zone) mLocalDateTime;

        // Phase 2 of the SoC refactor: TargetSelection view-model owns target / list / mode
        // state. UI controls (ComboBox_SelectTarget, CheckedListBox_SelectedTargets, RA/Dec
        // inputs, Select-All / Clear-All / Visible-Tonight buttons) bind to it. The
        // mUpdatingUiFromVm flag protects the echo path: when a VM event fires and the
        // handler programmatically writes back to a UI control, the control's user-input
        // event re-fires and would round-trip through the VM. The flag short-circuits
        // those echoes so the VM stays the single source of truth.
        private TargetSelection mSelection;
        private bool mUpdatingUiFromVm;

        private const string NinaTargetsRootPath = @"E:\Photography\Astro Photography\Captures\Nina\Targets";

        private Charts.AltitudeChart mAltitudeChart;

        // Phase 3 of the SoC refactor: ChartCacheStore owns the per-(Location, Target)
        // year cache + per-Location NightCache. After GetNinaTargets completes we kick
        // off PrepareManyAsync(KnownTargets) for background pre-population so subsequent
        // Graph clicks find caches already built. On Location change we call
        // SetLocationAsync to drop everything and re-populate at the new location.
        private TargetPlanner.Caches.ChartCacheStore mCache;
        private CancellationTokenSource mCachePrepCts;

        // Filter library + the radio-grouped Filters-menu items. The library persists in
        // %APPDATA%\TargetPlanner\filters.json and ships with H/O/S/L/R/G/B defaults if
        // no file exists yet. mFilterMenuItems is the mutually-exclusive radio group that
        // the Filters menu populates at construction time; SetActiveFilter walks the list
        // to enforce single-checked.
        private FilterLibrary mFilterLibrary;
        private ToolStripMenuItem mFiltersMenu;
        private List<ToolStripMenuItem> mFilterMenuItems;
        private ToolStripMenuItem mFilterMenuItem_Custom;

        // Mirror of CoordinateInput.mSuppress: raised while preset-load writes values
        // into the Lorentzian controls so OnLorentzianControlChanged returns early
        // instead of recursively flipping the menu back to Custom.
        private bool mSuppressFilterEvents;

        private ToolTip mToolTip;
        private int mToolTipIndex;

        // Dedicated ToolTip instance for the explanatory radio-button tooltips (Optimal,
        // Day). Kept separate from mToolTip because AutoPopDelay must be much longer (text
        // runs several paragraphs) and mToolTip's ShowCheckBoxObjectToolTip handler resets
        // AutoPopDelay to 5 seconds on every CheckedListBox hover -- globals wouldn't stick.
        private ToolTip mOptimalRadioTooltip;

        private const string OptimalRadioTooltipText =
@"These curves present the best imaging windows available on each 
night of the next year, all bounded by a minimum Target Floor (degrees) 
and Duration (hours). They each answer different planning questions:


Ceiling — Answers: ""What is the highest altitude reached for Duration 
hours above the Target Floor?""

    This is the target's highest altitude while above Target Floor 
    for Duration hours. It's the target's highest imaging altitude 
    for that night.


Floor — Answers: ""What is the lowest target altitude required to 
image for Duration hours above the Target Floor?""

    This is the target's best, lowest altitude above Target Floor for 
    Duration hours. It's the target's lowest imaging altitude for 
    that night.

    Floor is transit centered when duration fits inside a target floor 
    window. If not, the window is pushed against  whichever wall 
    is closest to the meridian.


Symmetric — Answers: ""Can I image this target symmetrically 
about the meridian?""

    This is the target's best meridian centered floor, while above 
    Target Floor for Duration hours. This is the lowest altitude 
    required to image equally around the meridian for that night.

    When possible, this curve is present. When a night is too short,
    transit is too close to dusk/dawn or if the symmetrical duration 
    would dip below the Target Floor, the curve is removed.


On an ideal night, all three curves bunch together near zenith;
Symmetric matches Floor because the best window is the symmetric one.

On a marginal night, the Ceiling is still decent but Floor drops and
Symmetric disappears entirely.

When Floor and Symmetric coincide, transit falls comfortably inside a
best-placed Duration window. When they diverge, the best-placed window 
is asymmetric. Floor shows the practical achievable floor and Symmetric 
shows the floor you could have with symmetry at the cost of a lower 
minimum altitude.";

        private const string DayRadioTooltipText =
@"Day View — Altitude curves for tonight, dusk through dawn, for every
selected target.

Left-click a target's curve to overlay its best imaging window for
tonight. Click multiple curves to see best imaging windows
for several targets at once to compare and sequence windows.

Left-click the same curve again to remove that overlay.
Right-click anywhere on the chart to clear all overlays.";

        private Panel Panel_AltitudeChart;

        private UIState mUIState;
        private AppSettings mAppSettings;

        // Guard flag: set while SyncLocationUIFromModel is programmatically updating location
        // inputs so OnLocationEdited doesn't mistake a sync for a user edit and flip the combo
        // to "Custom".
        private bool mSyncingLocationUI;

        // Four triple-bound coordinate inputs. Each wraps three NumericUpDowns (degrees /
        // hours + minutes + seconds), a decimal TextBox, and an optional hemisphere CheckBox,
        // owning the "update one of the four surfaces and keep the others in sync" plumbing
        // that previously lived in four near-identical handler pairs directly on MainForm.
        private CoordinateInput mLatitudeInput;
        private CoordinateInput mLongitudeInput;
        private CoordinateInput mRaInput;
        private CoordinateInput mDecInput;

        // Incremented on every Graph click so stale Progress<string> callbacks from a prior
        // (still in-flight) AltitudeSeries.BuildSeriesList don't tick ProgressBar_MultiTarget-
        // Processing after the user has already launched a new chart build. Captured by value
        // in the Progress<string> closure, so each click's callbacks are stamped and can be
        // identified as stale later.
        private int mChartBuildGeneration;

        // Parallel generation counter for GetNinaTargets. Incremented on each target-list load
        // so that a stale "reset-to-zero" continuation from a prior (still-holding-at-100%)
        // run doesn't wipe the new run's partial bar when Browse is triggered again during
        // the 1-second hold.
        private int mProcessObjectGeneration;

        // mGraphMode is now mSelection.Mode (Phase 2 of the SoC refactor). Mutators on the
        // VM (SetSelectedSingle / SetChecked / SetCheckedSet / SetAllChecked) imply the
        // mode, so callers don't need to update mode explicitly.

        // Cancellation handle for the current chart build. Button_Graph_Click creates a new
        // CTS per build; Button_Cancel_Click signals it; the token is observed inside
        // AltitudeSeries.ComputeYearCache's 365-day loop and AltitudeChart.ReloadWithTargets'
        // Task.WhenAll. Disposed in MainForm_FormClosing.
        private CancellationTokenSource mGraphCts;

        // Ignore-second-click guard for Button_Graph_Click. A Graph click while a prior
        // build is still awaiting its Task.WhenAll just returns -- the user has to Cancel
        // the in-flight build before a new one can start.
        private bool mGraphBuildInProgress;

        // Debounce for the Horizon / Duration spinners. Each ValueChanged restarts the
        // timer (stop + start), so rapid scrubs coalesce into one trailing-edge
        // RebuildOptimalData on the Tick. Horizon-line positioning stays immediate in the
        // ValueChanged handlers because that's cheap (one strip line per chart area) and
        // gives instant visual feedback during the scrub; only the per-target Optimal
        // recompute is deferred.
        private System.Windows.Forms.Timer mOptimalRebuildDebounce;
        private const int OptimalRebuildDebounceMs = 150;

        // Latch used by the CheckedListBox VM binding. ItemCheck sets it true; the
        // subsequent SelectedIndexChanged consumes + clears it to decide "this highlight
        // change came with a checkbox toggle, leave Mode=Multi alone" vs. "pure highlight,
        // flip Mode=Single + select the highlighted target". MouseUp / KeyUp also clear it
        // to cover the case where ItemCheck fired but SelectedIndexChanged did not (e.g.
        // toggling the checkbox of an already-selected row).
        private bool mCheckedListBoxJustToggled;

        public MainForm()
        {
            InitializeComponent();
            TimePicker.Format = DateTimePickerFormat.Time;
            TimePicker.ShowUpDown = true;
            TimePicker.Format = DateTimePickerFormat.Custom;
            TimePicker.CustomFormat = "  hh:mm tt";

            mAppSettings = SettingsStore.Load();

            mLocalDateTime = (DateTime.Now, TimeZoneInfo.Local);
            mLocation = PickStartupLocation();
            mSelection = new TargetSelection();
            // No M31 default seed: SelectedSingle stays null until NINA load completes
            // (auto-picks first sorted target via OnVmKnownTargetsChanged) or the user
            // types coordinates / picks from the ComboBox. The RA/Dec edit handlers fall
            // back to Target.Default if the user types before any selection exists.

            mUIState = new UIState();

            UpdateUI();
            UpdateLocalDateTimeEvents();
            InitializeDynamicControls();

            // Show the running version in the title bar so the user can read it without
            // opening About. Stripped of any build-metadata suffix (the +sha that MinVer
            // attaches for dev builds).
            Text = "TargetPlanner v" + GetDisplayVersion();

            // Help menu: extends the existing MenuStrip_MainForm (which already has File).
            // Two items: Check for Updates... (manual UpdateService entry) and About.
            var helpMenu = new ToolStripMenuItem("&Help");
            var checkUpdatesItem = new ToolStripMenuItem("Check for &Updates...");
            checkUpdatesItem.Click += async (s, e) => await UpdateService.CheckManuallyAsync(this);
            var aboutItem = new ToolStripMenuItem("&About TargetPlanner");
            aboutItem.Click += (s, e) => { using (var dlg = new AboutDialog()) dlg.ShowDialog(this); };
            helpMenu.DropDownItems.Add(checkUpdatesItem);
            helpMenu.DropDownItems.Add(aboutItem);
            MenuStrip_MainForm.Items.Add(helpMenu);

            // Filters menu: load the per-filter library (or ship-defaults on first launch)
            // and build a mutually-exclusive radio group of menu items. Disabled is the
            // first-launch default; a click on any preset writes its values into
            // mAltitudeChart.MoonAvoidanceProfile and triggers a RebuildOptimalData so the
            // Day chart's HD Overlay reflects the new avoidance regime immediately.
            BuildFiltersMenu();

            // BuildFiltersMenu's preselect-first-filter call already wrote starting
            // values into the Lorentzian controls and greyed them per the master
            // CheckBox_Moon_AvoidanceEnable state (unchecked at first launch).

            // Run the silent startup update check after the form is visible so the user sees
            // the UI immediately; the prompt (if any) lands a moment later. Fire-and-forget --
            // UpdateService swallows exceptions internally so a network failure can't crash here.
            Shown += async (s, e) => await UpdateService.CheckOnStartupAsync(this);
        }

        // MinVer stamps AssemblyInformationalVersion ("1.0.0" for tagged releases,
        // "0.0.0-alpha.0.107+sha" for dev builds). Drop the +sha suffix for display.
        private static string GetDisplayVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string raw = info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "unknown";
            int plus = raw.IndexOf('+');
            return plus >= 0 ? raw.Substring(0, plus) : raw;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            mToolTip = new ToolTip();
            mToolTip.AutoPopDelay = 5000;
            mToolTip.InitialDelay = 2000;
            mToolTip.ReshowDelay = 2000;

            // Long-lived explanatory tooltip for the Optimal radio button. InitialDelay is
            // 5 seconds so only a deliberate hover reveals it (casual mouse-overs don't
            // trigger the paragraph-length popup); AutoPopDelay stays long so the full text
            // is readable once it does appear.
            mOptimalRadioTooltip = new ToolTip();
            mOptimalRadioTooltip.AutoPopDelay = 60000;
            mOptimalRadioTooltip.InitialDelay = 5000;
            mOptimalRadioTooltip.ReshowDelay  = 500;
            mOptimalRadioTooltip.SetToolTip(RadioButton_Optimal, OptimalRadioTooltipText);
            mOptimalRadioTooltip.SetToolTip(RadioButton_Day, DayRadioTooltipText);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SettingsStore.Save(mAppSettings);

            // Dispose long-lived resources the form owns. Without this, the ToolTip leaks
            // a native handle.
            mToolTip?.Dispose();
            mOptimalRadioTooltip?.Dispose();
            mAltitudeChart?.Dispose();
            mLatitudeInput?.Dispose();
            mLongitudeInput?.Dispose();
            mRaInput?.Dispose();
            mDecInput?.Dispose();

            mGraphCts?.Cancel();
            mGraphCts?.Dispose();

            mCachePrepCts?.Cancel();
            mCachePrepCts?.Dispose();
            mCache?.Dispose();

            mOptimalRebuildDebounce?.Stop();
            mOptimalRebuildDebounce?.Dispose();
        }

        public void InitializeDynamicControls()
        {
            string[] folderSelectedPaths = { NinaTargetsRootPath };

            // Construct the four triple-bound coordinate helpers before Sync... pushes values
            // into them. Each helper owns the per-field wiring that used to live in the
            // UpdateXxxTextBox / TextBox_Xxx_TextChanged / CheckBox_Xxx_CheckedChanged
            // handlers directly on this form.
            mLatitudeInput = new CoordinateInput(
                NumericUpDown_LatitudeDegrees, NumericUpDown_LatitudeMinutes, NumericUpDown_LatitudeSeconds,
                TextBox_Latitude, CheckBox_LocalNorth, maxMagnitude: 90.0);
            mLongitudeInput = new CoordinateInput(
                NumericUpDown_LongitudeDegrees, NumericUpDown_LongitudeMinutes, NumericUpDown_LongitudeSeconds,
                TextBox_Longitude, CheckBox_LocalWest, maxMagnitude: 180.0);
            mRaInput = new CoordinateInput(
                NumericUpDown_RaHours, NumericUpDown_RaMinutes, NumericUpDown_RaSeconds,
                TextBox_RightAscension, hemisphere: null, maxMagnitude: 24.0);
            mDecInput = new CoordinateInput(
                NumericUpDown_DecDegrees, NumericUpDown_DecMinutes, NumericUpDown_DecSeconds,
                TextBox_Declination, CheckBox_TargetNorth, maxMagnitude: 90.0);

            mLatitudeInput.ValueChanged  += OnLatitudeEdited;
            mLongitudeInput.ValueChanged += OnLongitudeEdited;
            mRaInput.ValueChanged        += OnRightAscensionEdited;
            mDecInput.ValueChanged       += OnDeclinationEdited;

            // Populate ComboBox_Location from settings, select the startup location, then
            // push mLocation's values into the lat/lon/N/W/Horizon/Duration inputs.
            ComboBox_Location.SelectedIndexChanged -= ComboBox_Location_SelectionIndexChanged;
            ComboBox_Location.Items.Clear();
            foreach (NamedLocationSetting nl in mAppSettings.NamedLocations)
                ComboBox_Location.Items.Add(nl.Name);
            ComboBox_Location.Items.Add("Custom");
            if (ComboBox_Location.Items.Contains(mLocation.Name))
                ComboBox_Location.SelectedItem = mLocation.Name;
            else if (ComboBox_Location.Items.Count > 0)
                ComboBox_Location.SelectedIndex = 0;
            ComboBox_Location.SelectedIndexChanged += ComboBox_Location_SelectionIndexChanged;

            SyncLocationUIFromModel();
            SyncTargetUIFromModel();

            // Add Panel that MSChart will appear in to GroupBox
            Panel_AltitudeChart = new Panel();
            Panel_AltitudeChart.Location = new Point(10, 40);
            Panel_AltitudeChart.Size = new Size(GroupBox_Altitude.Width - 20, GroupBox_Altitude.Height - 50);
            Panel_AltitudeChart.Name = "Panel_Mschart";
            Panel_AltitudeChart.BackColor = Color.FromArgb(255, 128, 128, 128);
            GroupBox_Altitude.Controls.Add(Panel_AltitudeChart);

            // Add actual Altitude Chart to Panel
            // Phase 3: instantiate the cache store first; the chart takes a reference so
            // every AltitudeSeries it spawns reads from the shared store.
            mCache = new TargetPlanner.Caches.ChartCacheStore(mLocation);
            mAltitudeChart = new Charts.AltitudeChart(mLocation, mCache);
            mAltitudeChart.mChart.Location = new Point(5, 5);
            mAltitudeChart.mChart.Size = new Size(Panel_AltitudeChart.Width - 10, Panel_AltitudeChart.Size.Height - 10);
            mAltitudeChart.mChart.BackColor = Color.FromArgb(255, 239, 235, 233);

            mAltitudeChart.AddChartAreaToList("Day");
            mAltitudeChart.AddChartAreaToList("Year");
            mAltitudeChart.AddChartAreaToList("Optimal");

            // Phase 1: no startup chart. The chart control / chart areas / legend are
            // wired up here so the empty chart panel renders cleanly, but no targets
            // are added and no series are built. The user clicks Button_Graph to
            // populate the chart; Button_Graph_Click owns the full ReloadWithTargets
            // path (including UpdateHorizonLines + UpdateNowLine) for the populated
            // chart. RegisterChartAreas eagerly registers all three chart areas with
            // mChart.ChartAreas (each Visible = false until ShowChartAreaSeries flips
            // one) so ReloadWithTargets' early UpdateHorizonLines call can index the
            // chart areas before any series exist.
            mAltitudeChart.RegisterChartAreas();
            mAltitudeChart.UIState(mUIState);
            mAltitudeChart.AddLegend();
            mAltitudeChart.Legend = true;

            Panel_AltitudeChart.Controls.Add(mAltitudeChart.mChart);

            // Establish a default sort mode authoritatively from code. The VS Designer has a
            // recurring habit of silently dropping ComboBox_SortTargets.SelectedIndex = 0 from
            // MainForm.Designer.cs; when that happens the initial SelectedIndex is -1 (no
            // selection), which leaves SortedTargets falling through to Name order but also
            // leaves the first Name->Transit sort change not propagating Items[0] into
            // ComboBox_SelectTarget.Text. Owning the default here makes the behavior
            // independent of whatever the Designer file looks like at any given commit. The
            // guard keeps this a no-op if a future Designer edit does preserve the line (so
            // we don't fire a spurious SelectedIndexChanged on startup).
            if (ComboBox_SortTargets.SelectedIndex < 0)
            {
                ComboBox_SortTargets.SelectedIndex = 0;
            }

            // Fire-and-forget; GetNinaTargets owns its own try/catch for diagnostics.
            _ = GetNinaTargets(folderSelectedPaths);

            // Wire VM bindings after the CoordinateInput helpers and mAltitudeChart exist.
            // The ComboBox starts blank; OnVmKnownTargetsChanged populates it after NINA
            // load completes (~100 ms) and auto-selects the first sorted target.
            WireSelectionVm();
        }

        private void UpdateUI()
        {
            CheckBox_LocalNorth.Checked = mLocation.North;
            CheckBox_LocalWest.Checked = mLocation.West;
            TextBox_Latitude.Text = mLocation.Latitude.ToString("F6");
            TextBox_Longitude.Text = mLocation.Longitude.ToString("F6");

            Target t = mSelection?.SelectedSingle;
            if (t != null)
            {
                CheckBox_TargetNorth.Checked = t.North;
                TextBox_RightAscension.Text = t.RightAscension.ToString("F6");
                TextBox_Declination.Text = t.Declination.ToString("F6");
            }
        }

        private void UpdateLocalDateTimeEvents()
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            mLocation = mLocation.With(dateTime: mLocalDateTime.When, timeZoneInfo: mLocalDateTime.Zone);
            Astrometry.Location(mLocation);

            Label_AstronomicalDuskValue.Text = Astrometry.AstronomicalDusk.ToShortTimeString();
            Label_AstronomicalDawnValue.Text = Astrometry.AstronomicalDawn.ToShortTimeString();
            Label_SunAltitudeValue.Text = Astrometry.SunAltitude.ToString("F0") + "\u00B0";
            Label_LunarAltitudeValue.Text = Astrometry.LunarAltitude.ToString("F0") + "\u00B0";
            Label_LunarIlluminationFractionValue.Text = (Astrometry.LunarIlluminationFraction * 100).ToString("F0") + "%";
            Label_LunarPhaseValue.Text = Astrometry.LunarPhase;
            Label_MoonRiseValue.Text = Astrometry.LunarRise.ToShortTimeString();
            Label_MoonSetValue.Text = Astrometry.LunarSet.ToShortTimeString();
        }

        // ---------- Coordinate-input callbacks (ValueChanged from CoordinateInput) ----------
        //
        // Each of these fires only for user-driven edits (spinner tick, textbox edit, or
        // hemisphere checkbox flip). Programmatic writes via SetProgrammatic during
        // SyncLocationUIFromModel / SyncTargetUIFromModel suppress the event, so these
        // callbacks are only ever invoked with the helper's Magnitude / Positive already
        // reflecting the user's intent.
        //
        // Every location-side callback funnels into OnLocationEdited so the combo flips to
        // "Custom" on any geographic change; RA/Dec do not affect the location combo.
        private void OnLatitudeEdited(object sender, EventArgs e)
        {
            mLocation = mLocation.With(
                latitude: Math.Round(mLatitudeInput.Magnitude, 6),
                north:    mLatitudeInput.Positive);
            OnLocationEdited(sender, e);
        }

        private void OnLongitudeEdited(object sender, EventArgs e)
        {
            mLocation = mLocation.With(
                longitude: Math.Round(mLongitudeInput.Magnitude, 6),
                west:      mLongitudeInput.Positive);
            OnLocationEdited(sender, e);
        }

        private void OnRightAscensionEdited(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = mSelection.SelectedSingle ?? Target.Default;
            mSelection.SetSelectedSingle(
                t.With(rightAscension: Math.Round(mRaInput.Magnitude, 6)));
        }

        private void OnDeclinationEdited(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = mSelection.SelectedSingle ?? Target.Default;
            mSelection.SetSelectedSingle(
                t.With(
                    declination: Math.Round(mDecInput.Magnitude, 6),
                    north:       mDecInput.Positive));
        }

        private void NumericUpDown_TargetDuration_ValueChanged(object sender, EventArgs e)
        {
            TimeSpan newDuration = TimeSpan.FromMinutes((double)NumericUpDown_TargetDuration.Value * 60.0);
            mLocation = mLocation.With(duration: newDuration);
            if (mAltitudeChart == null) return;
            // RebuildOptimalData iterates every target's RebuildOptimalSeries; on large
            // target sets that adds up quickly during live scrubbing. Debounce.
            RestartOptimalRebuildDebounce();
        }

        private void NumericUpDown_TargetFloor_ValueChanged(object sender, EventArgs e)
        {
            double newHorizon = (double)NumericUpDown_TargetFloor.Value;
            mLocation = mLocation.With(horizon: newHorizon);
            if (mAltitudeChart == null) return;
            // Horizon-line repositioning stays immediate -- it's one strip line per chart
            // area and the user wants instant feedback as they scrub. The per-target
            // Optimal recompute is what's expensive; debounce that.
            mAltitudeChart.UpdateHorizonLines(newHorizon);
            RestartOptimalRebuildDebounce();
        }

        // Lazily-constructed shared Timer. ValueChanged calls Stop()+Start() to reset the
        // interval, so rapid fire events collapse to one trailing-edge Tick. Tick reads the
        // latest mLocation.Horizon / Duration (already set by the ValueChanged handlers) so
        // no per-event state needs to be latched.
        private void RestartOptimalRebuildDebounce()
        {
            if (mOptimalRebuildDebounce == null)
            {
                mOptimalRebuildDebounce = new System.Windows.Forms.Timer { Interval = OptimalRebuildDebounceMs };
                mOptimalRebuildDebounce.Tick += OptimalRebuildDebounce_Tick;
            }
            mOptimalRebuildDebounce.Stop();
            mOptimalRebuildDebounce.Start();
        }

        private void OptimalRebuildDebounce_Tick(object sender, EventArgs e)
        {
            mOptimalRebuildDebounce.Stop();
            if (mAltitudeChart == null) return;
            mAltitudeChart.RebuildOptimalData(mLocation.Horizon, mLocation.Duration);
        }

        // Loads the filter library (or ships defaults on first launch) and builds the
        // Filters menu's mutually-exclusive radio group: one item per filter, plus a
        // Custom slot. The CheckBox_Moon_AvoidanceEnable on GroupBox_Moon_Avoidance is the
        // master on/off switch -- the menu is filter-selection only. mFilterMenuItems
        // holds the group so SetActiveFilter can enforce single-checked. The menu is
        // appended to MenuStrip_MainForm at construction time, after File and Help.
        private void BuildFiltersMenu()
        {
            mFilterLibrary = FilterLibrary.LoadOrDefault();

            // Idempotent: on first call, create the top-level menu and add it to the
            // strip. On subsequent calls (after EditFiltersForm save), reuse it and
            // wipe its items. Avoids appending a second "Filters" menu to the strip.
            if (mFiltersMenu == null)
            {
                mFiltersMenu = new ToolStripMenuItem("&Filters");
                MenuStrip_MainForm.Items.Add(mFiltersMenu);
            }
            else
            {
                mFiltersMenu.DropDownItems.Clear();
            }

            mFilterMenuItems = new List<ToolStripMenuItem>();

            ToolStripMenuItem firstFilterItem = null;
            MoonAvoidanceProfile firstFilterProfile = null;
            foreach (TpFilter filter in mFilterLibrary.Filters)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(filter.Name);
                TpFilter captured = filter;
                ToolStripMenuItem capturedItem = item;
                item.Click += (s, e) => SetActiveFilter(captured.ToProfile(), capturedItem);
                mFiltersMenu.DropDownItems.Add(item);
                mFilterMenuItems.Add(item);
                if (firstFilterItem == null)
                {
                    firstFilterItem = item;
                    firstFilterProfile = captured.ToProfile();
                }
            }

            mFiltersMenu.DropDownItems.Add(new ToolStripSeparator());

            // Custom slot. Direct user click is a no-op -- Custom is meaningfully active
            // only as a side effect of editing the GroupBox controls (which auto-flips
            // the menu via OnLorentzianControlChanged). Clicking it directly leaves the
            // active profile and control values exactly where they were.
            mFilterMenuItem_Custom = new ToolStripMenuItem("&Custom");
            mFilterMenuItem_Custom.Click += (s, e) => { /* no-op; live values unchanged */ };
            mFiltersMenu.DropDownItems.Add(mFilterMenuItem_Custom);
            mFilterMenuItems.Add(mFilterMenuItem_Custom);

            mFiltersMenu.DropDownItems.Add(new ToolStripSeparator());

            // Edit Filters... opens a modal dialog that mutates mFilterLibrary and
            // persists to JSON. On OK we rebuild this menu so renamed / added /
            // removed filters appear; the active filter falls back to the first
            // library entry.
            ToolStripMenuItem editItem = new ToolStripMenuItem("&Edit Filters...");
            editItem.Click += (s, e) => OpenEditFiltersDialog();
            mFiltersMenu.DropDownItems.Add(editItem);

            // Pre-select the first library filter visually and write its values into
            // the Lorentzian controls so the GroupBox has sensible defaults from the
            // start. Whether avoidance actually applies is gated on
            // CheckBox_Moon_AvoidanceEnable.
            //
            // Critical: do NOT route through SetActiveFilter here. SetActiveFilter
            // calls RebuildOptimalData, which calls AltitudeSeries.RebuildOptimalSeries
            // for every target in the chart's target list; the lazy-init branch in
            // RebuildOptimalSeries (AltitudeSeries.cs:264) synchronously runs
            // ComputeYearCache on the UI thread when the year cache hasn't been built
            // yet. At construction time the M31 seed's async BuildSeriesList is still
            // mid-flight, so the synchronous fallback either races the async builder
            // or hangs the UI for tens of seconds. Setting MoonAvoidanceProfile alone
            // is enough -- the setter propagates to every AltitudeSeries in
            // mSeriesByTarget, and the in-flight async builder picks it up when it
            // reaches RenderOptimalSeries. The post-Edit-Filters caller in
            // OpenEditFiltersDialog explicitly calls RebuildOptimalData afterward,
            // when caches are guaranteed populated.
            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            if (firstFilterItem != null)
            {
                firstFilterItem.Checked = true;
                WriteProfileToControls(firstFilterProfile);
                if (mAltitudeChart != null)
                {
                    mAltitudeChart.MoonAvoidanceProfile = avoidanceOn ? firstFilterProfile : null;
                }
            }
            else if (mAltitudeChart != null)
            {
                // Empty library: nothing checked, no profile.
                mAltitudeChart.MoonAvoidanceProfile = null;
            }
            SetLorentzianControlsEnabled(avoidanceOn);
        }

        private void OpenEditFiltersDialog()
        {
            using (EditFiltersForm dlg = new EditFiltersForm(mFilterLibrary))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }

            // The library was mutated in place + persisted by the dialog. Rebuild the
            // Filters menu so renamed / added / removed entries show up. The active
            // filter falls back to the first library entry (handled inside
            // BuildFiltersMenu); the master CheckBox_Moon_AvoidanceEnable state is
            // preserved.
            BuildFiltersMenu();

            // Trigger the chart redraw explicitly. BuildFiltersMenu deliberately
            // skips RebuildOptimalData (the construction-time caller can't safely
            // run it -- see the comment block in BuildFiltersMenu); by the time
            // OpenEditFiltersDialog runs, year caches are populated and the call
            // is cheap.
            if (mAltitudeChart != null)
            {
                mAltitudeChart.RebuildOptimalData(mLocation.Horizon, mLocation.Duration);
            }
        }

        // Update the active moon-avoidance profile and re-render every target's
        // moon-aware curves. Walks mFilterMenuItems to enforce mutually-exclusive
        // checked state (only the just-clicked item stays checked). The master
        // CheckBox_Moon_AvoidanceEnable gates whether the profile is actually pushed to
        // the chart -- when unchecked, the menu/controls update visibly but the chart
        // sees null (no avoidance).
        private void SetActiveFilter(MoonAvoidanceProfile profile, ToolStripMenuItem clickedItem)
        {
            if (mFilterMenuItems != null)
            {
                foreach (ToolStripMenuItem item in mFilterMenuItems)
                {
                    item.Checked = (item == clickedItem);
                }
            }

            // When clickedItem is a named preset, write the profile's values into the
            // Lorentzian controls. When clickedItem is Custom, the controls ARE the
            // source -- don't overwrite. WriteProfileToControls raises
            // mSuppressFilterEvents internally so its writes don't recursively flip the
            // menu back to Custom via OnLorentzianControlChanged.
            if (clickedItem != mFilterMenuItem_Custom)
            {
                WriteProfileToControls(profile);
            }

            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            SetLorentzianControlsEnabled(avoidanceOn);

            if (mAltitudeChart == null) return;
            mAltitudeChart.MoonAvoidanceProfile = avoidanceOn ? profile : null;
            mAltitudeChart.RebuildOptimalData(mLocation.Horizon, mLocation.Duration);
        }

        // Master on/off for moon avoidance. When checked, the active filter's profile
        // (read live from the Lorentzian controls -- they always reflect either a named
        // filter's values or the user's Custom scrubs) is pushed to the chart. When
        // unchecked, the chart sees null and skips moon-aware work entirely.
        private void OnAvoidanceEnableChanged(object sender, EventArgs e)
        {
            if (mAltitudeChart == null) return;

            bool enabled = CheckBox_Moon_AvoidanceEnable.Checked;
            MoonAvoidanceProfile profile = null;
            if (enabled)
            {
                profile = MoonAvoidanceProfile.Custom(
                    separationDeg:  (double)NumericUpDown_Moon_Separation.Value,
                    widthDays:      (double)NumericUpDown_Moon_Width.Value,
                    relaxEnabled:   CheckBox_Moon_RelaxEnabled.Checked,
                    relaxMinAltDeg: (double)NumericUpDown_Moon_RelaxMin.Value,
                    relaxMaxAltDeg: (double)NumericUpDown_Moon_RelaxMax.Value,
                    relaxScale:     (double)NumericUpDown_Moon_RelaxScale.Value);
            }

            SetLorentzianControlsEnabled(enabled);
            mAltitudeChart.MoonAvoidanceProfile = profile;
            mAltitudeChart.RebuildOptimalData(mLocation.Horizon, mLocation.Duration);
        }

        // User scrubbed a Lorentzian control. Build a Custom profile from the live values
        // and route through SetActiveFilter so the menu radio flips to Custom and the
        // chart re-renders. Returns early under mSuppressFilterEvents (preset-load is
        // writing to the controls and the change isn't a user edit).
        private void OnLorentzianControlChanged(object sender, EventArgs e)
        {
            if (mSuppressFilterEvents) return;
            if (NumericUpDown_Moon_Separation == null) return;

            MoonAvoidanceProfile custom = MoonAvoidanceProfile.Custom(
                separationDeg:  (double)NumericUpDown_Moon_Separation.Value,
                widthDays:      (double)NumericUpDown_Moon_Width.Value,
                relaxEnabled:   CheckBox_Moon_RelaxEnabled.Checked,
                relaxMinAltDeg: (double)NumericUpDown_Moon_RelaxMin.Value,
                relaxMaxAltDeg: (double)NumericUpDown_Moon_RelaxMax.Value,
                relaxScale:     (double)NumericUpDown_Moon_RelaxScale.Value);

            SetActiveFilter(custom, mFilterMenuItem_Custom);
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

        // Enable/disable the Lorentzian controls. Used to grey them when the active
        // filter is Disabled (avoidance off entirely); enables them otherwise.
        private void SetLorentzianControlsEnabled(bool enabled)
        {
            if (NumericUpDown_Moon_Separation == null) return;
            NumericUpDown_Moon_Separation.Enabled  = enabled;
            NumericUpDown_Moon_Width.Enabled       = enabled;
            CheckBox_Moon_RelaxEnabled.Enabled     = enabled;
            NumericUpDown_Moon_RelaxMin.Enabled    = enabled;
            NumericUpDown_Moon_RelaxMax.Enabled    = enabled;
            NumericUpDown_Moon_RelaxScale.Enabled  = enabled;
        }

        private void DatePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            UpdateLocalDateTimeEvents();
            if (mAltitudeChart != null) mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
            // Transit / Rise sort keys are time-dependent; Name is not. Skip the re-sort on
            // Name to avoid a pointless Items.Clear+re-add round-trip on every scrub tick.
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            UpdateLocalDateTimeEvents();
            if (mAltitudeChart != null) mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
        }

        // Graph the targets indicated by mSelection.Mode (last-touched). Multi walks the
        // VM's Checked set; Single falls back to mSelection.SelectedSingle (RA/Dec inputs +
        // combo). If Multi is active but Checked is empty (e.g. user just clicked Clear All),
        // fall back to SelectedSingle so the button always produces a chart.
        //
        // Async: ReloadWithTargets stages every target's Day / Moon / Year / Optimal build
        // off to the side and only swaps into mChart.Series after all of them finish. That
        // means (a) the prior chart stays fully stable during the compute and (b) Cancel
        // leaves the prior chart untouched instead of half-updating.
        //
        // mLocation.DateTime is already kept in sync with the pickers via
        // UpdateLocalDateTimeEvents (called from DatePicker/TimePicker ValueChanged and
        // Button_Now_Click). Don't overwrite it with DateTime.Now here -- that was the
        // pre-refactor assumption when the app was always "live now" by default.
        private async void Button_Graph_Click(object sender, EventArgs e)
        {
            // Ignore second click while a build is in flight. User must Cancel the running
            // build before starting a new one.
            if (mGraphBuildInProgress) return;

            var targets = new List<Target>();

            if (mSelection.Mode == GraphMode.Multi)
            {
                // Walk CheckedItems in display order so mAltitudeChart's target list -- and
                // therefore the chart legend -- inherits the CheckedListBox's NaturalStringComparer
                // sort (see GetNinaTargets). Iterating mSelection.KnownTargets here would have
                // used folder-load order, which is effectively arbitrary.
                foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
                {
                    string name = item.ToString();
                    Target t = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
                    if (t != null) targets.Add(t);
                }
            }

            // Fall back to Single when Multi yields nothing (or when Mode is Single).
            if (targets.Count == 0)
            {
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
                    // No checked targets, no SelectedSingle, no resolvable combo text.
                    // Surface a brief auto-dismissing notice instead of silently doing
                    // nothing (the silent path was confusing -- the user clicked Graph and
                    // saw no feedback).
                    ShowTransientMessage("No Targets");
                    return;
                }
                targets.Add(current);
            }

            IProgress<string> phaseProgress = BeginChartBuildProgress(targetCount: targets.Count);

            mGraphCts?.Cancel();
            mGraphCts = new CancellationTokenSource();

            // Disable Button_Graph for the duration of the full build so the user can't
            // stack clicks. Button_GraphCancel stays enabled so a cancel can still be
            // requested; it disables itself when clicked and re-enables alongside
            // Button_Graph in the finally block below.
            //
            // Park focus on the form before the disable. Otherwise Win32 auto-advances
            // focus from the just-disabled Button_Graph to the next TabStop
            // (ComboBox_SelectTarget), whose focus-gain auto-selects its text and
            // cascades mode-flip side effects into the combo's SelectedIndexChanged path.
            ActiveControl = null;
            Button_Graph.Enabled = false;
            mGraphBuildInProgress = true;

            try
            {
                // ReloadWithTargets now pre-computes a shared NightCache behind its first
                // await; mSeriesByTarget isn't populated (and per-target sync preambles
                // aren't run) until after that await returns. That means the "Day series
                // in TargetSeriesList" precondition ShowChartAreaSeries relies on isn't
                // met until the full build completes -- so we await first, THEN paint.
                await mAltitudeChart.ReloadWithTargets(
                    mLocation, targets, phaseProgress, mGraphCts.Token);

                // Snap the radio to Day and paint the finished chart. Setting Checked=true
                // only fires CheckedChanged if the value actually changes, so we
                // unconditionally run ShowChartAreaSeries + ChartTitle after to cover the
                // "radio already on Day" path.
                RadioButton_Day.Checked = true;
                mAltitudeChart.ShowChartAreaSeries("Day");
                mAltitudeChart.ChartTitle = FormatChartTitle("Day");
                mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
            }
            finally
            {
                mGraphBuildInProgress = false;
                Button_Graph.Enabled = true;
                Button_Cancel.Enabled = true;
            }
        }

        // Snap the observation moment back to the current wall-clock time. Replaces the
        // prior Now/SetDateTime/Hold trio plus the 5-second polling timer with a single
        // explicit user action: set mLocalDateTime to now, push into the pickers (without
        // re-triggering their ValueChanged), refresh every label via UpdateLocalDateTime-
        // Events, and reposition the chart's red now-line to the current X coordinate.
        private void Button_Now_Click(object sender, EventArgs e)
        {
            mLocalDateTime = (DateTime.Now, TimeZoneInfo.Local);

            DatePicker.ValueChanged -= DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;
            DatePicker.Value = mLocalDateTime.When;
            TimePicker.Value = mLocalDateTime.When;
            DatePicker.ValueChanged += DatePicker_ValueChanged;
            TimePicker.ValueChanged += TimePicker_ValueChanged;

            UpdateLocalDateTimeEvents();

            if (mAltitudeChart != null)
            {
                mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
            }
        }

        // Signal the in-flight chart build to unwind. The Day / Moon phase is synchronous
        // and completes before Button_Graph_Click returns, so it can't be cancelled -- only
        // the Year + Optimal background compute is interruptible. The progress bar is reset
        // because partial Day ticks would otherwise leave it stuck at ~1/3 full.
        //
        // Disables itself to prevent a second click from firing while cancellation
        // propagates; Button_Graph_Click's finally re-enables it when the build fully
        // unwinds. If no build is in flight (mGraphBuildInProgress false), early-return
        // without disabling -- the button has nothing to cancel and should stay clickable
        // for the next build.
        private void Button_Cancel_Click(object sender, EventArgs e)
        {
            if (!mGraphBuildInProgress) return;

            Button_Cancel.Enabled = false;

            mGraphCts?.Cancel();

            // Bump the build generation so any late Progress<string> callbacks from the
            // unwinding tasks no-op instead of re-ticking a zeroed bar.
            mChartBuildGeneration++;
            ProgressBar_MultiTargetProcessing.Value = 0;
        }

        // ---------- ComboBox_Location ----------
        private void ComboBox_Location_SelectionIndexChanged(object sender, EventArgs e)
        {
            if (ComboBox_Location.SelectedItem == null) return;
            string name = ComboBox_Location.SelectedItem.ToString();

            if (name == "Custom")
            {
                // User explicitly chose "Custom" -- clear lat/lon so they can type fresh
                // values. Preserve Horizon / Duration / N / W: those are independent of the
                // location name and the user may have deliberately tuned them.
                mLocation = mLocation.With(name: "Custom", latitude: 0, longitude: 0);
                SyncLocationUIFromModel();
            }
            else
            {
                NamedLocationSetting named = mAppSettings.NamedLocations.Find(x => x.Name == name);
                if (named == null) return;
                // Preserve the current DateTime / TimeZoneInfo across the switch -- the user's
                // date/time selection shouldn't reset when they swap locations.
                Location loaded = named.ToLocation();
                mLocation = loaded.With(dateTime: mLocation.DateTime, timeZoneInfo: mLocation.TimeZoneInfo);
                SyncLocationUIFromModel();
            }

            mAppSettings.LastSelectedLocationName = name;
            SettingsStore.Save(mAppSettings);
        }

        // DropDown nulls the current selection so re-picking the same item (e.g. "Penns Park"
        // after a manual edit auto-switched us to "Custom") still fires SelectedIndexChanged.
        private void ComboBox_Location_DropDown(object sender, EventArgs e)
        {
            ComboBox_Location.SelectedItem = null;
        }

        // Fired by every location-input event (lat/lon spinners, textboxes, N/W checkboxes,
        // Horizon, Duration). If the user edited a field by hand, flip the combo to "Custom"
        // so the combo label always matches the currently-displayed values.
        private void OnLocationEdited(object sender, EventArgs e)
        {
            if (mSyncingLocationUI) return;
            if (ComboBox_Location.SelectedItem != null &&
                ComboBox_Location.SelectedItem.ToString() == "Custom") return;

            ComboBox_Location.SelectedIndexChanged -= ComboBox_Location_SelectionIndexChanged;
            ComboBox_Location.SelectedItem = "Custom";
            ComboBox_Location.SelectedIndexChanged += ComboBox_Location_SelectionIndexChanged;

            mLocation = mLocation.With(name: "Custom");
            mAppSettings.LastSelectedLocationName = "Custom";
            // Not saving on every edit -- settings are persisted on form close.
        }

        private Location PickStartupLocation()
        {
            string preferred = mAppSettings.LastSelectedLocationName;
            if (!string.IsNullOrEmpty(preferred) && preferred != "Custom")
            {
                NamedLocationSetting match = mAppSettings.NamedLocations.Find(x => x.Name == preferred);
                if (match != null) return match.ToLocation();
            }
            if (mAppSettings.NamedLocations.Count > 0)
                return mAppSettings.NamedLocations[0].ToLocation();
            // Fully qualify: MainForm inherits Control.Location (type Point), which shadows
            // the `using Location = ...` alias in member-access context.
            return Astronomy.Core.Locations.Location.Default;
        }

        // Push mLocation into the lat / lon / N / W / Horizon / Duration inputs. The two
        // CoordinateInput helpers handle all their own suppress-during-write plumbing; for
        // Horizon / Duration (single-spinner, not triple-bound) we unsubscribe/re-subscribe
        // around the write directly. mSyncingLocationUI still gates OnLocationEdited so the
        // sync itself doesn't flip the combo to "Custom".
        private void SyncLocationUIFromModel()
        {
            mSyncingLocationUI = true;
            try
            {
                mLatitudeInput.SetProgrammatic(mLocation.Latitude,  positive: mLocation.North);
                mLongitudeInput.SetProgrammatic(mLocation.Longitude, positive: mLocation.West);

                NumericUpDown_TargetFloor.ValueChanged  -= NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged -= NumericUpDown_TargetDuration_ValueChanged;
                NumericUpDown_TargetFloor.Value  = ClampToRange(NumericUpDown_TargetFloor,  (decimal)mLocation.Horizon);
                NumericUpDown_TargetDuration.Value = ClampToRange(NumericUpDown_TargetDuration, (decimal)mLocation.Duration.TotalHours);
                NumericUpDown_TargetFloor.ValueChanged  += NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged += NumericUpDown_TargetDuration_ValueChanged;
            }
            finally { mSyncingLocationUI = false; }
        }

        // Push mSelection.SelectedSingle into the RA / Dec coordinate inputs. No equivalent guard flag because
        // OnRightAscensionEdited / OnDeclinationEdited don't have the combo-flip side effect
        // that SyncLocationUIFromModel has to suppress; SetProgrammatic already skips
        // ValueChanged.
        private void SyncTargetUIFromModel()
        {
            Target t = mSelection?.SelectedSingle;
            if (t == null) return;
            mRaInput.SetProgrammatic(t.RightAscension, positive: true);
            mDecInput.SetProgrammatic(t.Declination,   positive: t.North);
        }

        private static decimal ClampToRange(NumericUpDown spinner, decimal value)
        {
            if (value < spinner.Minimum) return spinner.Minimum;
            if (value > spinner.Maximum) return spinner.Maximum;
            return value;
        }
        private void Button_BrowseTargetList_Click(object sender, EventArgs e)
        {
            mFolder = new OpenFolderDialog()
            {
                Title = "NINA Target Folder Browser",
                AutoUpgradeEnabled = true,
                CheckPathExists = false,
                InitialDirectory = NinaTargetsRootPath,
                Multiselect = true,
                RestoreDirectory = true
            };

            DialogResult result = mFolder.ShowDialog(IntPtr.Zero);

            if (result.Equals(DialogResult.OK))
            {
                // Click-Browse implies "I'm about to graph many of these". Flip Mode to
                // Multi so the post-load Graph click uses the checked set without an
                // intermediate explicit user action. Pre-VM this was a WireMultiMode hook
                // on the button's Click event.
                mSelection.SetMode(GraphMode.Multi);
                _ = GetNinaTargets(mFolder.SelectedPaths);
            }
        }

        private async Task GetNinaTargets(string[] folderSelectedPaths)
        {
            // The previous KnownTargets is replaced wholesale at the end of this method
            // via mSelection.SetKnownTargets(allLoaded). The VM event handlers
            // (OnVmKnownTargetsChanged) repopulate ComboBox_SelectTarget +
            // CheckedListBox_SelectedTargets atomically. We don't manually clear those
            // controls here -- doing so would fire spurious SelectedIndexChanged /
            // ItemCheck events that round-trip through the VM with stale state.

            int thisGeneration = ++mProcessObjectGeneration;

            var progressHandler = new Progress<(int Current, int Total)>(value =>
            {
                ProgressBar_ProcessObject.Maximum = value.Total;
                ProgressBar_ProcessObject.Value = value.Current;
            });

            var progress = progressHandler as IProgress<(int Current, int Total)>;

            ProgressBar_ProcessObject.Value = 0;

            var allLoaded = new List<Target>();
            try
            {
                foreach (string folder in folderSelectedPaths)
                {
                    List<Target> loaded = null;
                    await Task.Run(() =>
                    {
                        loaded = TargetPlanner.Nina.TargetLoader.Load(folder, progress);
                    });

                    if (loaded != null) allLoaded.AddRange(loaded);
                    ProgressBar_ProcessObject.Value = ProgressBar_ProcessObject.Maximum;
                }

                // Success path: hold the filled bar for 1 s, then clear to zero. Instant clear
                // on error stays (see catch below) so failure feels snappy rather than animated.
                // Generation-guarded so a re-browse during the hold doesn't wipe the new run.
                // Discard suppresses CS4014 -- the continuation is intentionally fire-and-forget
                // (marshaled to the UI thread via FromCurrentSynchronizationContext).
                _ = Task.Delay(1000).ContinueWith(
                    _2 =>
                    {
                        if (thisGeneration != mProcessObjectGeneration) return;
                        ProgressBar_ProcessObject.Value = 0;
                    },
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                // Log the full exception (stack + type) before surfacing a shorter user-facing
                // message; the bare catch used to swallow the stack trace entirely.
                System.Diagnostics.Debug.WriteLine($"GetNinaTargets failed: {ex}");
                MessageBox.Show(ex.Message, "Target load failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ProgressBar_ProcessObject.Value = 0;
            }

            // Push the new known-target list to the VM. KnownTargetsChanged fires once;
            // OnVmKnownTargetsChanged repopulates ComboBox_SelectTarget +
            // CheckedListBox_SelectedTargets via PopulateTargetComboFromTargets +
            // PopulateCheckedListBoxFromTargets, which read the new VM state.
            mSelection.SetKnownTargets(allLoaded);

            // Phase 3: kick off background pre-population of the chart cache so subsequent
            // Graph clicks find caches already built. Fire-and-forget; cancellation comes
            // from mCachePrepCts (cancelled on Form close + replaced on each new load so
            // a re-Browse aborts the prior load's prep). Errors swallowed -- this is a
            // best-effort warmup, not load-bearing.
            mCachePrepCts?.Cancel();
            mCachePrepCts?.Dispose();
            mCachePrepCts = new CancellationTokenSource();
            CancellationToken prepToken = mCachePrepCts.Token;
            _ = Task.Run(async () =>
            {
                try { await mCache.PrepareManyAsync(allLoaded, prepToken); }
                catch (OperationCanceledException) { /* expected on re-load / form close */ }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ChartCacheStore.PrepareManyAsync warmup failed: {ex}");
                }
            }, prepToken);
        }

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
                    CheckedListBox_SelectedTargets.Items.Add(t.Name, isChecked);
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

            // Reorder the chart legend to match, in place -- no replot, no recompute. Series
            // objects (Points, Color, Tag, ToolTip) are preserved; only their index in
            // mChart.Series changes, which drives the legend row order.
            if (mAltitudeChart != null && mAltitudeChart.Targets.Count > 0)
            {
                mAltitudeChart.ReorderTargets(SortedTargets(mAltitudeChart.Targets));
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

        private void ComboBox_SortTargets_SelectedIndexChanged(object sender, EventArgs e)
        {
            ResortSelectedTargets(autoSelectFirstInCombo: true);
        }

        // Double-click a target row to open its source NINA .json (Target.Directory is the
        // full file path, populated by TargetLoader). Uses ShellExecute via Process.Start so
        // whatever the user has registered for .json (Notepad, VS Code, etc.) handles it.
        private void CheckedListBox_SelectedTargets_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = CheckedListBox_SelectedTargets.IndexFromPoint(e.Location);
            if (index < 0) return;

            string name = CheckedListBox_SelectedTargets.Items[index].ToString();
            Target found = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
            if (found == null) return;

            string path = found.Directory;
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show("File no longer exists:\n" + path, "Open target file",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open target file '{path}': {ex}");
                MessageBox.Show("Could not open file:\n" + path + "\n\n" + ex.Message,
                    "Open target file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowCheckBoxObjectToolTip(object sender, MouseEventArgs e)
        {
            if (mToolTipIndex == this.CheckedListBox_SelectedTargets.IndexFromPoint(e.Location)) return;

            mToolTipIndex = CheckedListBox_SelectedTargets.IndexFromPoint(CheckedListBox_SelectedTargets.PointToClient(MousePosition));
            if (mToolTipIndex < 0) return;

            string name = CheckedListBox_SelectedTargets.Items[mToolTipIndex].ToString();
            Target found = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
            if (found == null) return;

            mToolTip.SetToolTip(CheckedListBox_SelectedTargets, found.Directory);
            mToolTip.AutoPopDelay = 5000;
            mToolTip.InitialDelay = 2000;
            mToolTip.ReshowDelay = 2000;
        }

        private void RadioButton_Day_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.DayChart = RadioButton_Day.Checked;
            if (RadioButton_Day.Checked == true)
            {
                Astrometry.Location(mLocation);
                mAltitudeChart.ShowChartAreaSeries("Day");
                mAltitudeChart.ChartTitle = FormatChartTitle("Day");
            }
        }

        private void RadioButton_Year_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.YearChart = RadioButton_Year.Checked;
            if (RadioButton_Year.Checked == true)
            {
                mAltitudeChart.ShowChartAreaSeries("Year");
                mAltitudeChart.ChartTitle = FormatChartTitle("Year");
            }
        }

        private void RadioButton_Optimal_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.OptimalChart = RadioButton_Optimal.Checked;
            if (RadioButton_Optimal.Checked == true)
            {
                mAltitudeChart.ShowChartAreaSeries("Optimal");
                mAltitudeChart.ChartTitle = FormatChartTitle("Optimal");
            }
        }

        // The Day chart renders one night's altitude curve, so its title includes the
        // calendar date of the evening. Year and Optimal both render a 365-day sweep starting
        // from the current month; their title uses the month name instead of a specific day
        // so the axis label and the title agree.
        private string FormatChartTitle(string areaName)
        {
            if (areaName == "Day")
            {
                return "Altitude at " + mLocation.Name
                     + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            }
            return "Altitude at " + mLocation.Name
                 + " for Year beginning " + mLocation.DateTime.ToString("MMMM yyyy");
        }

        // Reset ProgressBar_MultiTargetProcessing and return an IProgress<string> that ticks
        // it once per phase ("Day" / "Year" / "Optimal") for each of targetCount targets.
        // Progress<T> marshals Report callbacks back to the creation thread (here, the UI
        // thread), so the Value increment is safe even though AltitudeSeries.BuildSeriesList
        // fires the first phase synchronously and the next two from a Task.Run continuation.
        //
        // Wire two-way bindings between TargetSelection and the UI controls. User input
        // flows into the VM via mutator calls; VM events flow back to UI controls. The
        // mUpdatingUiFromVm flag short-circuits VM-driven UI writes so they don't re-enter
        // the VM (a UI control's user-input event still fires when the value is set
        // programmatically; without the guard the write would round-trip).
        //
        // Mode flips are implicit: SetSelectedSingle / SetChecked / SetCheckedSet /
        // SetAllChecked all set Mode as a side effect, so callers don't need to track it.
        //
        // CheckedListBox disambiguation: WinForms fires ItemCheck and SelectedIndexChanged
        // on the same user click when the user toggles a checkbox. ItemCheck routes to
        // SetChecked (Mode = Multi); we then need to suppress the immediately-following
        // SelectedIndexChanged path (which would otherwise route to SetSelectedSingle and
        // flip Mode = Single, undoing the toggle's intent). The mCheckedListBoxJustToggled
        // latch is set by ItemCheck and consumed by SelectedIndexChanged for that purpose.
        private void WireSelectionVm()
        {
            // VM -> UI bindings.
            mSelection.KnownTargetsChanged   += OnVmKnownTargetsChanged;
            mSelection.SelectedSingleChanged += OnVmSelectedSingleChanged;
            mSelection.CheckedSetChanged     += OnVmCheckedSetChanged;

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
            CheckedListBox_SelectedTargets.MouseUp += (s, e) => mCheckedListBoxJustToggled = false;
            CheckedListBox_SelectedTargets.KeyUp   += (s, e) => mCheckedListBoxJustToggled = false;
        }

        private void OnVmKnownTargetsChanged(object sender, EventArgs e)
        {
            // Repopulate ComboBox_SelectTarget and CheckedListBox_SelectedTargets from the
            // new known-target list (in the current sort order). Default checked = true for
            // every loaded target -- the user can Clear All / pick a subset afterward.
            PopulateCheckedListBoxFromTargets(defaultChecked: true);
            PopulateTargetComboFromTargets(preserveSelection: false);

            // SetKnownTargets clears SelectedSingle when the prior selection isn't in the
            // new catalog (different Target instance equality across reloads). Re-establish
            // a default by picking the *first sorted* known target (matching what the
            // ComboBox auto-selected via PopulateTargetComboFromTargets above) so RA/Dec
            // inputs and ComboBox text reflect the same VM state after a load. Using
            // KnownTargets[0] would pick load-order first, which only coincides with sort
            // order under Name sort.
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
            // in display order; resolves each row's name to a Target via VM.KnownTargets,
            // then checks/unchecks based on whether VM.Checked contains it.
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try
            {
                for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
                {
                    string name = CheckedListBox_SelectedTargets.Items[i].ToString();
                    Target row = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
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

        private void OnCheckedListBoxItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            string name = CheckedListBox_SelectedTargets.Items[e.Index].ToString();
            Target t = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
            if (t == null) return;
            bool isChecked = e.NewValue == CheckState.Checked;
            mSelection.SetChecked(t, isChecked);
            mCheckedListBoxJustToggled = true;
        }

        private void OnCheckedListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;

            // De-selection (no row highlighted) -- consume the toggle latch and bail.
            if (CheckedListBox_SelectedTargets.SelectedItem == null)
            {
                mCheckedListBoxJustToggled = false;
                return;
            }

            // Consume the toggle latch: it's valid for exactly one SelectedIndexChanged.
            // If a checkbox was just toggled (ItemCheck fired moments ago), don't route
            // through SetSelectedSingle here -- ItemCheck already established Mode = Multi
            // and SetSelectedSingle would reset Mode = Single, undoing the toggle's intent.
            bool wasJustToggled = mCheckedListBoxJustToggled;
            mCheckedListBoxJustToggled = false;
            if (wasJustToggled) return;

            string name = CheckedListBox_SelectedTargets.SelectedItem.ToString();
            Target t = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
            if (t != null) mSelection.SetSelectedSingle(t);
        }

        // Show a small auto-dismissing notice centered on the main form. Used by
        // Button_Graph_Click when no targets are picked / checked / typed -- a silent
        // no-op was confusing. Non-modal: the main form stays interactive while the
        // notice is on screen.
        private void ShowTransientMessage(string text, int durationMs = 2000)
        {
            var notice = new Form
            {
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition   = FormStartPosition.CenterParent,
                ShowInTaskbar   = false,
                ControlBox      = false,
                Text            = string.Empty,
                Size            = new Size(220, 80),
                BackColor       = SystemColors.Info,
            };
            var label = new Label
            {
                Text      = text,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 12F, FontStyle.Bold),
            };
            notice.Controls.Add(label);

            var timer = new System.Windows.Forms.Timer { Interval = durationMs };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!notice.IsDisposed)
                {
                    notice.Close();
                    notice.Dispose();
                }
            };
            notice.Shown += (s, e) => timer.Start();
            notice.Show(this);
        }

        private IProgress<string> BeginChartBuildProgress(int targetCount)
        {
            int thisGeneration = ++mChartBuildGeneration;

            // Tick budget: 1 (Click) + 1 (SharedCache) + 4 per target (Moon/Day/Year/Optimal).
            ProgressBar_MultiTargetProcessing.Minimum = 0;
            ProgressBar_MultiTargetProcessing.Maximum = Math.Max(1, 2 + targetCount * 4);
            // Synchronous Click tick: paints before the first await so the user sees immediate
            // feedback. Routing through the Progress<string> callback would queue a SyncContext
            // post that may not paint until after the next await yields.
            ProgressBar_MultiTargetProcessing.Value   = 1;

            return new Progress<string>(_ =>
            {
                if (thisGeneration != mChartBuildGeneration) return;  // stale -- a newer click superseded us
                if (ProgressBar_MultiTargetProcessing.Value < ProgressBar_MultiTargetProcessing.Maximum)
                {
                    ProgressBar_MultiTargetProcessing.Value += 1;
                    // Final tick completes the chart build. Hold the filled bar for 1 s so
                    // the "done" state is visible, then clear to zero. Generation-guarded so
                    // if the user clicks Graph again during the hold, the stale reset no-ops
                    // and the new build's bar isn't clobbered.
                    if (ProgressBar_MultiTargetProcessing.Value >= ProgressBar_MultiTargetProcessing.Maximum)
                    {
                        Task.Delay(1000).ContinueWith(
                            _2 =>
                            {
                                if (thisGeneration != mChartBuildGeneration) return;
                                ProgressBar_MultiTargetProcessing.Value = 0;
                            },
                            TaskScheduler.FromCurrentSynchronizationContext());
                    }
                }
            });
        }

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
            mSelection.SetSelectedSingle(found);
        }

        // VM mutator. SetAllChecked fires CheckedSetChanged + ModeChanged (Multi);
        // OnVmCheckedSetChanged updates the listbox row check states.
        private void Button_ClearAllTargets_Click(object sender, EventArgs e)
        {
            mSelection.SetAllChecked(false);
        }

        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {
            mSelection.SetAllChecked(true);
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
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;
            if (mLocation == null) return;

            DateTime tonightAnchor = DatePicker.Value.Date + TimePicker.Value.TimeOfDay;
            Location pickedNightLocation = mLocation.With(dateTime: tonightAnchor);

            Astronomy.Core.Night.NightWindow night =
                Astronomy.Core.Night.NightCalculator.ComputeNight(pickedNightLocation);

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
                    night.AstronomicalDusk = DateTime.MinValue;
                    night.AstronomicalDawn = DateTime.MinValue;
                }
                else if (anchorUtc > night.AstronomicalDusk)
                {
                    night.AstronomicalDusk = anchorUtc;
                }
            }

            Astronomy.Core.Horizons.IHorizonProfile horizon =
                new Astronomy.Core.Horizons.ScalarHorizonProfile(pickedNightLocation.Horizon);

            // Only the 0/0 case uses the parameterless IsEverVisible. It hardcodes the
            // mathematical horizon (0 deg) and has no duration requirement, which matches
            // the literal "no altitude threshold, no duration minimum" meaning of 0/0.
            // Any other (Horizon, Duration) combination -- including partial zeros --
            // still goes through IsAboveHorizonForAtLeast so the non-zero knob is honored.
            bool useEverVisible =
                pickedNightLocation.Horizon <= 0.0
                && pickedNightLocation.Duration <= TimeSpan.Zero;

            // Compute the visible-tonight set, then push to the VM in two steps:
            //   1. SetSelectedSingle to the first sorted visible target. This updates
            //      ComboBox_SelectTarget.Text + RA/Dec inputs to that target. Implies
            //      Mode = Single transiently.
            //   2. SetCheckedSet(visible). This fills the multi-set + flips Mode back
            //      to Multi (the right end-state for Button_Graph).
            // Order matters: doing SetCheckedSet first then SetSelectedSingle would
            // leave Mode = Single. Reversing the order ensures Mode = Multi at exit.
            var visible = mSelection.KnownTargets.Where(t =>
                useEverVisible
                    ? Astronomy.Core.Session.CoarseVisibility.IsEverVisible(t, pickedNightLocation, night)
                    : Astronomy.Core.Session.CoarseVisibility.IsAboveHorizonForAtLeast(
                          t, pickedNightLocation, night, horizon, pickedNightLocation.Duration))
                .ToList();
            Target firstSorted = SortedTargets(visible).FirstOrDefault();
            if (firstSorted != null) mSelection.SetSelectedSingle(firstSorted);
            mSelection.SetCheckedSet(visible);
        }
    }
}
