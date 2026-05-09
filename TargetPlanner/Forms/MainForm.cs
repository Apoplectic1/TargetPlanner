using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using TargetPlanner.Filters;
using TargetPlanner.Forms;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Updates;
using System.Threading;
using System.Threading.Tasks;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;
using TpFilter = TargetPlanner.Filters.Filter;

namespace TargetPlanner
{
    public partial class MainForm : Form
    {
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

        // Root folder the NINA target loader walks at startup and the Browse-Target-List
        // dialog opens to. Sourced from PersonalDefaults so the public binary ships with
        // a neutral fallback (%PUBLIC%\Documents\NINA\Targets) and the developer's actual
        // imaging-PC path lives in their gitignored personal-defaults.json.
        private static string NinaTargetsRootPath => PersonalDefaults.NinaTargetsRoot;

        // Active chart-area state (post-PR4e: legacy AltitudeChart deleted; this state
        // used to live on it). MainForm owns:
        //   - mSubCharts: keyed by area ("Day"/"Sky"/"Year"/"Sessions"), each value
        //     implements IAltitudeSubChart so picker/spinner/debounce/Graph-click
        //     traffic dispatches via foreach + dict lookup instead of explicit fields.
        //   - mLC2Sky: typed reference for the Sky-specific quirks not in the
        //     interface (ActiveFilterCenterNm setter + RefreshSkyBrightness).
        //   - mLastRenderedTargets: snapshot of the most recent Graph-click target
        //     list. Sort callbacks read this for the Reorder fast path; radio
        //     handlers read it to re-Render when toggling between areas.
        //   - mMoonAvoidanceProfile / mActiveFilterCenterNm: state pushed into each
        //     sub-chart's Render / RefreshVisibility call. Set by SetActiveFilter
        //     and the Lorentzian / avoidance-checkbox handlers.
        private System.Collections.Generic.Dictionary<string, Charts.IAltitudeSubChart> mSubCharts;
        private System.Collections.Generic.List<Astronomy.Core.Targets.Target> mLastRenderedTargets =
            new System.Collections.Generic.List<Astronomy.Core.Targets.Target>();
        private Astronomy.Core.Moon.MoonAvoidanceProfile mMoonAvoidanceProfile;
        private double mActiveFilterCenterNm = 550.0;

        // Phase 3 of the SoC refactor: ChartCacheStore owns the per-(Location, Target)
        // year cache + per-Location NightCache. After GetNinaTargets completes we kick
        // off PrepareManyAsync(KnownTargets) for background pre-population so subsequent
        // Graph clicks find caches already built. On Location change we call
        // SetLocationAsync to drop everything and re-populate at the new location.
        private TargetPlanner.Caches.ChartCacheStore mCache;
        private CancellationTokenSource mCachePrepCts;

        // Phase 2 of the orchestration-layer refactor: ChartCoordinator centralizes
        // the diff-and-dispatch pipeline. UI handlers in this form build a
        // ChartContext snapshot via SnapshotCurrent(...) and hand it to the
        // coordinator's Apply (debounced) or ApplyImmediateAsync (no-debounce);
        // the coordinator decides cache (re)build vs render vs visibility refresh.
        // Phase 2 routes only the location-pipe (combo location pick + lat/lon/elev
        // scrubs that cross LocationsCacheEquivalent) through it; non-keying scrubs
        // stay on the legacy SessionsRebuildDebounce path until Phase 3.
        private TargetPlanner.State.ChartCoordinator mCoordinator;

        // Filter library + the Filters-menu items. The library persists in
        // %APPDATA%\TargetPlanner\filters.json and ships with H/O/S/L/R/G/B defaults if
        // no file exists yet. mFilterMenuItems is the radio group -- one item per
        // filter; SetActiveFilter walks the list to enforce single-checked. mActiveFilter
        // is the auto-save target: scrubbing the Lorentzian controls mutates this filter
        // in-place via the FilterAutoSaveDebounce_Tick path, persisting the change to
        // filters.json and refreshing the menu's '*' modified-indicator.
        private FilterLibrary mFilterLibrary;
        private ToolStripMenuItem mFiltersMenu;
        private List<ToolStripMenuItem> mFilterMenuItems;
        private TpFilter mActiveFilter;

        // Raised while WriteProfileToControls writes profile values into the Lorentzian
        // controls so OnLorentzianControlChanged returns early -- those writes aren't
        // user edits and shouldn't trigger an auto-save tick.
        private bool mSuppressFilterEvents;

        // Set true while the EditFiltersForm modal is showing. The Lorentzian-scrub
        // auto-save tick consults this so dialog-time edits commit only via the dialog's
        // own Save button (transactional shadow), not via the main-form auto-save path.
        private bool mEditFiltersDialogOpen;

        // Debounce timer for the Lorentzian-scrub auto-save into mActiveFilter. Mirrors
        // the SessionsRebuildDebounce pattern (Stop+Start collapses rapid edits into one
        // trailing-edge tick). Tick handler replaces mActiveFilter in mFilterLibrary,
        // saves to filters.json, and refreshes menu '*' labels.
        private System.Windows.Forms.Timer mFilterAutoSaveDebounce;
        private const int FilterAutoSaveDebounceMs = 500;

        // GroupBox_Moon_Filters surface: one RadioButton per library filter plus a
        // single "Defaults" button. Built programmatically by BuildFiltersGroupBox
        // alongside BuildFiltersMenu, so the menu and the strip stay in sync.
        // Parallel-indexed with mFilterLibrary.Filters and mFilterMenuItems.
        private List<RadioButton> mFilterRadios;
        private Button mFilterDefaultsButton;

        // Sky needs a typed reference for K-S/filter-specific calls that aren't on
        // IAltitudeSubChart: ActiveFilterCenterNm property (Rayleigh λ⁻⁴ scaling) and
        // RefreshSkyBrightness(cache, location) (Bortle / ExtinctionK / Filter scrub).
        private Charts.AltitudeSubChart_Sky mLC2Sky;

        private ToolTip mToolTip;
        private int mToolTipIndex;

        // Dedicated ToolTip instance for the explanatory radio-button tooltips (Sessions,
        // Day). Kept separate from mToolTip because AutoPopDelay must be much longer (text
        // runs several paragraphs) and mToolTip's ShowCheckBoxObjectToolTip handler resets
        // AutoPopDelay to 5 seconds on every CheckedListBox hover -- globals wouldn't stick.
        private ToolTip mSessionsRadioTooltip;

        private const string SessionsRadioTooltipText =
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

        // Incremented on every Graph click so stale Progress<int> callbacks from a prior
        // (still in-flight) PrepareManyAsync don't tick ProgressBar_MultiTargetProcessing
        // after the user has already launched a new chart build. Captured by value in the
        // Progress<int> closure, so each click's callbacks are stamped and can be
        // identified as stale later.
        private int mChartBuildGeneration;

        // Parallel generation counter for GetNinaTargets. Incremented on each target-list load
        // so that a stale "reset-to-zero" continuation from a prior (still-holding-at-100%)
        // run doesn't wipe the new run's partial bar when Browse is triggered again during
        // the 1-second hold.
        private int mProcessObjectGeneration;

        // Cancellation handle for the current chart build. RunGraphBuildAsync creates
        // a new CTS per build (cancelling any prior in-flight build for supersedence);
        // Button_Cancel_Click signals it; the token is observed inside
        // ChartCacheStore.PrepareManyAsync's per-target Task.WhenAll. Disposed in
        // MainForm_FormClosing.
        private CancellationTokenSource mGraphCts;

        // Ignore-second-click guard for Button_Graph_Click. A Graph click while a prior
        // build is still awaiting its Task.WhenAll just returns -- the user has to Cancel
        // the in-flight build before a new one can start.
        private bool mGraphBuildInProgress;

        // Debounce for the Horizon / Duration spinners. Each ValueChanged restarts the
        // timer (stop + start), so rapid scrubs coalesce into one trailing-edge
        // RebuildSessionsData on the Tick. Horizon-line positioning stays immediate in the
        // ValueChanged handlers because that's cheap (one strip line per chart area) and
        // gives instant visual feedback during the scrub; only the per-target Sessions
        // recompute is deferred.
        private System.Windows.Forms.Timer mSessionsRebuildDebounce;
        private const int SessionsRebuildDebounceMs = 150;

        // Trailing-edge debounce for multi-graph auto-rendering. Subscribed off
        // mSelection.CheckedSetChanged in WireSelectionVm: each Set/Clear/toggle bumps
        // Stop+Start; CheckedToggleDebounce_Tick fires after 250 ms of quiet and runs
        // RunGraphBuildAsync over the current Checked set. Button_Graph_Click also
        // calls Stop() so a still-pending tick can't clobber a just-rendered single
        // graph (see plan: mode-removal-against-current-dev.md, Edge case 4).
        private System.Windows.Forms.Timer mCheckedToggleDebounce;
        private const int CheckedToggleDebounceMs = 250;

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

            // File menu: extend the Designer-resident "File" menu with a Clear All Data
            // entry. Wipes the three persistent files in %APPDATA%\TargetPlanner (settings,
            // filters, log) and offers a restart so the next launch boots from defaults.
            var clearDataItem = new ToolStripMenuItem("&Clear All Data...");
            clearDataItem.Click += (s, e) => HandleClearAllDataClick();
            FileToolStripMenuItem_MainForm.DropDownItems.Add(clearDataItem);

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
            // mMoonAvoidanceProfile and triggers RestartSessionsRebuildDebounce so the
            // universal hide-on-no-fit refresh propagates the new avoidance regime
            // to every sub-chart.
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

            // Long-lived explanatory tooltip for the Sessions radio button. InitialDelay is
            // 5 seconds so only a deliberate hover reveals it (casual mouse-overs don't
            // trigger the paragraph-length popup); AutoPopDelay stays long so the full text
            // is readable once it does appear.
            mSessionsRadioTooltip = new ToolTip();
            mSessionsRadioTooltip.AutoPopDelay = 60000;
            mSessionsRadioTooltip.InitialDelay = 5000;
            mSessionsRadioTooltip.ReshowDelay  = 500;
            mSessionsRadioTooltip.SetToolTip(RadioButton_Sessions, SessionsRadioTooltipText);
            mSessionsRadioTooltip.SetToolTip(RadioButton_Day, DayRadioTooltipText);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SettingsStore.Save(mAppSettings);

            // Dispose long-lived resources the form owns. Without this, the ToolTip leaks
            // a native handle.
            mToolTip?.Dispose();
            mSessionsRadioTooltip?.Dispose();
            if (mSubCharts != null)
            {
                foreach (var sc in mSubCharts.Values) sc.Dispose();
            }
            mLatitudeInput?.Dispose();
            mLongitudeInput?.Dispose();
            mRaInput?.Dispose();
            mDecInput?.Dispose();

            mGraphCts?.Cancel();
            mGraphCts?.Dispose();

            mCachePrepCts?.Cancel();
            mCachePrepCts?.Dispose();
            mCoordinator?.Dispose();
            mCache?.Dispose();

            mSessionsRebuildDebounce?.Stop();
            mSessionsRebuildDebounce?.Dispose();
        }

        public void InitializeDynamicControls()
        {
            string[] folderSelectedPaths = { NinaTargetsRootPath };

            // Duration spinner: bump Minimum off the Designer default of 0. The
            // library tolerates non-positive duration (BestSession.For etc. now
            // return null in that case rather than throwing), but a UI state
            // where every target is hidden on every chart at zero duration is a
            // user-confusion trap, not a useful view. 0.25 h = 15 min is the
            // smallest practical imaging session.
            NumericUpDown_TargetDuration.Minimum = 0.25M;

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

            // Populate Bortle combo with the nine class numbers (1=excellent dark,
            // 9=inner-city). Wired programmatically so the Designer doesn't have to
            // know about the K-S model. SelectedIndexChanged auto-fills the extinction
            // spinner with the typical k for that class; both feed Location and route
            // through OnLocationEdited.
            ComboBox_Bortle.Items.Clear();
            for (int i = 1; i <= 9; i++) ComboBox_Bortle.Items.Add(i.ToString());
            ComboBox_Bortle.DropDownStyle      = ComboBoxStyle.DropDownList;
            ComboBox_Bortle.SelectedIndexChanged += ComboBox_Bortle_SelectedIndexChanged;
            NumericUpDown_Extinction.ValueChanged += NumericUpDown_Extinction_ValueChanged;

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

            // Add Panel that the chart sub-charts will appear in to GroupBox.
            // Top = 48 leaves an 8 px gap between the radio cluster above and the
            // chart panel below, so the chart's dark-grey background does not run
            // flush against the controls (visible bug pre-2026-05-04). Height
            // compensates so the bottom edge stays where it was.
            Panel_AltitudeChart = new Panel();
            Panel_AltitudeChart.Location = new Point(10, 48);
            Panel_AltitudeChart.Size = new Size(GroupBox_Altitude.Width - 20, GroupBox_Altitude.Height - 58);
            Panel_AltitudeChart.Name = "Panel_Mschart";
            Panel_AltitudeChart.BackColor = Color.FromArgb(255, 128, 128, 128);
            GroupBox_Altitude.Controls.Add(Panel_AltitudeChart);

            // Add actual Altitude Chart to Panel
            // Phase 3: instantiate the cache store first; the chart takes a reference so
            // every AltitudeSeries it spawns reads from the shared store.
            mCache = new TargetPlanner.Caches.ChartCacheStore(mLocation, SynchronizationContext.Current);

            // Phase 4 LC2 sub-charts. Indexed by area name so MainForm dispatches
            // picker / spinner / debounce / Graph-click traffic via foreach + dict
            // lookup. Sky also keeps a typed reference (mLC2Sky) for K-S quirks
            // (ActiveFilterCenterNm property + RefreshSkyBrightness) that aren't
            // on the IAltitudeSubChart interface.
            mLC2Sky = new Charts.AltitudeSubChart_Sky();
            mSubCharts = new System.Collections.Generic.Dictionary<string, Charts.IAltitudeSubChart>(System.StringComparer.Ordinal)
            {
                ["Day"]      = new Charts.AltitudeSubChart_Day(),
                ["Sky"]      = mLC2Sky,
                ["Year"]     = new Charts.AltitudeSubChart_Year(),
                ["Sessions"] = new Charts.AltitudeSubChart_Sessions(),
            };
            foreach (var sc in mSubCharts.Values)
            {
                sc.Control.Visible = false;
                sc.IdealHeightChanged += OnSubChartIdealHeightChanged;
                Panel_AltitudeChart.Controls.Add(sc.Control);
            }

            // Initial form sizing so an empty LC2 chart's plot area sits at the
            // ChartLayout.FixedPlotAreaHeight position even before any Graph click.
            // All sub-charts share the same ChartFixedHeight + empty legend at boot,
            // so seeding from any of them is equivalent.
            ResizeAltitudeChartArea(mSubCharts["Day"].IdealHeight);

            // Construct the coordinator after both mCache and mSubCharts exist
            // (it captures references via the resolver delegates). Post-apply
            // hook keeps location-dependent labels in sync with the just-applied
            // snapshot. Phase 2 only routes the location-pipe through it; other
            // handlers migrate in Phase 3.
            mCoordinator = new TargetPlanner.State.ChartCoordinator(
                cache: mCache,
                resolveSubChart: name => mSubCharts.TryGetValue(name, out var sc) ? sc : null,
                resolveAllSubCharts: () => mSubCharts.Values,
                postApplyHook: _ => RefreshAstrometryLabels());

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

            // Wire VM bindings after the CoordinateInput helpers and the sub-chart dict exist.
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
            RefreshAstrometryLabels();
        }

        // Re-populate the AstrometryUi static cache from mLocation and push every dependent
        // label. ~150 us of Meeus math + 8 string assignments; cheap enough to fire on
        // every Lat/Lon/Elevation spinner tick without debouncing. Called from
        // UpdateLocalDateTimeEvents (date/time scrubs), OnLocationEdited (lat/lon/N/W/
        // elevation spinners), and ComboBox_Location_SelectionIndexChanged (preset picks).
        private void RefreshAstrometryLabels()
        {
            AstrometryUi.Location(mLocation);

            Label_AstronomicalDuskValue.Text = AstrometryUi.AstronomicalDusk.ToShortTimeString();
            Label_AstronomicalDawnValue.Text = AstrometryUi.AstronomicalDawn.ToShortTimeString();
            Label_SunAltitudeValue.Text = AstrometryUi.SunAltitude.ToString("F0") + "\u00B0";
            Label_LunarAltitudeValue.Text = AstrometryUi.LunarAltitude.ToString("F0") + "\u00B0";
            Label_LunarIlluminationFractionValue.Text = (AstrometryUi.LunarIlluminationFraction * 100).ToString("F0") + "%";
            Label_LunarPhaseValue.Text = AstrometryUi.LunarPhase;
            Label_MoonRiseValue.Text = AstrometryUi.LunarRise.ToShortTimeString();
            Label_MoonSetValue.Text = AstrometryUi.LunarSet.ToShortTimeString();
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

        // Single-spinner control (no D/M/S triple) so we don't go through CoordinateInput;
        // route directly to OnLocationEdited so the combo flips to "Custom" and the cache
        // invalidation debounce restarts -- same path the lat/lon handlers take.
        private void NumericUpDown_LocalElevation_ValueChanged(object sender, EventArgs e)
        {
            mLocation = mLocation.With(elevation: (double)NumericUpDown_LocalElevation.Value);
            OnLocationEdited(sender, e);
        }

        // Bortle dropdown change: update Location.BortleClass and overwrite the extinction
        // spinner with the typical k for the new class. The class also drives V₀ inside
        // K-S via Bortle.DefaultZenithMag at render time, so a Bortle change triggers a
        // Day-Sky rebuild via the OnLocationEdited debounce path.
        private void ComboBox_Bortle_SelectedIndexChanged(object sender, EventArgs e)
        {
            int b = ComboBox_Bortle.SelectedIndex + 1;  // index 0 -> Bortle 1
            if (b < 1 || b > 9) return;
            double k = Astronomy.Core.Brightness.Bortle.DefaultExtinctionK500(b);
            mLocation = mLocation.With(bortleClass: b, extinctionK: k);

            // Push the auto-filled k into the spinner without re-firing its handler.
            NumericUpDown_Extinction.ValueChanged -= NumericUpDown_Extinction_ValueChanged;
            NumericUpDown_Extinction.Value = ClampToRange(NumericUpDown_Extinction, (decimal)k);
            NumericUpDown_Extinction.ValueChanged += NumericUpDown_Extinction_ValueChanged;

            OnLocationEdited(sender, e);
        }

        // Extinction spinner edit: update Location.ExtinctionK directly. Bortle stays
        // wherever the user left it (manual override -- the user knows their site's k
        // better than the Bortle table does).
        private void NumericUpDown_Extinction_ValueChanged(object sender, EventArgs e)
        {
            mLocation = mLocation.With(extinctionK: (double)NumericUpDown_Extinction.Value);
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
            if (mSubCharts == null) return;
            // RefreshVisibility iterates every target -- on large target sets the
            // moon-aware path that's heavy on the bg task on Year / Sessions adds
            // up quickly during live scrubbing. Debounce.
            RestartSessionsRebuildDebounce();
        }

        private void NumericUpDown_TargetFloor_ValueChanged(object sender, EventArgs e)
        {
            double newHorizon = (double)NumericUpDown_TargetFloor.Value;
            mLocation = mLocation.With(horizon: newHorizon);
            if (mSubCharts == null) return;
            // Horizon-line repositioning stays immediate -- it's one strip per chart
            // and the user wants instant feedback as they scrub. The per-target
            // visibility recompute is what's expensive; debounce that.
            foreach (var sc in mSubCharts.Values) sc.UpdateHorizonLine(newHorizon);
            RestartSessionsRebuildDebounce();
        }

        // Lazily-constructed shared Timer. ValueChanged calls Stop()+Start() to reset the
        // interval, so rapid fire events collapse to one trailing-edge Tick. Tick reads the
        // latest mLocation.Horizon / Duration / Lat / Lon (already set by the ValueChanged
        // handlers) so no per-event state needs to be latched.
        private void RestartSessionsRebuildDebounce()
        {
            if (mSessionsRebuildDebounce == null)
            {
                mSessionsRebuildDebounce = new System.Windows.Forms.Timer { Interval = SessionsRebuildDebounceMs };
                mSessionsRebuildDebounce.Tick += SessionsRebuildDebounce_Tick;
            }
            mSessionsRebuildDebounce.Stop();
            mSessionsRebuildDebounce.Start();
        }

        // Trailing-edge debounce tick. Branches on whether any cache-keying field
        // (Lat / Lon / N / W / Elev / year-start) drifted vs the cache:
        //
        // 1. Keying drift -> ResetForLocationChange: blank the chart, clear the
        //    checked set, drop + rebuild the cache against the new location. Per
        //    spec, scrubs are treated as "I changed sites" the same way an
        //    explicit combo pick is -- the user re-engages the controls to pick
        //    fresh targets at the new geometry.
        //
        // 2. Non-keying scrub (Horizon / Duration / Bortle / Extinction / Filter):
        //    rerun the universal hide-on-no-fit visibility pass (CLAUDE.md
        //    "Universal chart behavior contract") on every sub-chart, then re-walk
        //    Sky's minute-grid K-S brightness with the new inputs. Cache stays
        //    intact -- those fields don't key the cache.
        private async void SessionsRebuildDebounce_Tick(object sender, EventArgs e)
        {
            mSessionsRebuildDebounce.Stop();

            if (mCache != null && !LocationsCacheEquivalent(mLocation, mCache.CurrentLocation))
            {
                await ResetForLocationChange();
                return;
            }

            if (mSubCharts == null) return;
            ChartContext refreshCtx = SnapshotCurrent(mLastRenderedTargets);
            foreach (var sc in mSubCharts.Values)
            {
                sc.RefreshVisibility(refreshCtx, mCache);
            }
            PushSkyKSInputs();
        }

        // Symmetric clean-slate response to any user action that re-keys the
        // chart cache (combo location pick, lat/lon/elev scrub once it crosses
        // LocationsCacheEquivalent). Per spec:
        //   - Cancel any in-flight chart build so its post-await render can't
        //     paint stale geometry from the old location.
        //   - Clear the checked set via the VM. CheckedListBox row checks blank
        //     via the OnVmCheckedSetChanged binding; the multi-graph debounce's
        //     re-arm on CheckedSetChanged is then explicitly stopped because we
        //     don't want a 250-ms-late blank-render to fight our explicit one
        //     (and harmless when the set was already empty -- VM short-circuits,
        //     no event, Stop() no-ops).
        //   - Reset mLastRenderedTargets to empty. The coordinator's pipeline
        //     drops the cache (gated by LocationCacheEquivalent), runs
        //     PrepareManyAsync(empty) as a no-op, Renders the active chart
        //     with the empty target list (-> blank chart), and refreshes
        //     visibility on inactive charts (no-op on empty mSeriesByTarget).
        //     Post-apply hook refreshes the dependent dawn/dusk/sun/moon
        //     labels for the new location.
        // No auto re-render: the user re-engages a control (Button_Graph,
        // CheckedListBox toggles, etc.) to draw fresh series.
        private async Task ResetForLocationChange()
        {
            mGraphCts?.Cancel();

            mSelection.SetAllChecked(false);
            mCheckedToggleDebounce?.Stop();

            mLastRenderedTargets = new List<Target>();
            await mCoordinator.ApplyImmediateAsync(SnapshotCurrent(mLastRenderedTargets));
        }

        // Push the active filter's center wavelength + re-walk the K-S minute grid
        // through the Sky sub-chart's existing series. Called from the debounce
        // tick where Bortle / ExtinctionK / Filter scrubs need their effect to
        // reach Sky's brightness curves. NOT called from RenderArea (Render
        // rebuilds the K-S grid inline) or SetActiveFilter (the debounce tick
        // 150 ms later runs this; calling here would cause a redundant
        // K-S re-walk on filter click). Null-safe; no-op when Sky isn't
        // instantiated yet (early-init paths).
        private void PushSkyKSInputs()
        {
            if (mLC2Sky == null) return;
            mLC2Sky.ActiveFilterCenterNm = mActiveFilterCenterNm;
            mLC2Sky.RefreshSkyBrightness(mCache, mLocation);
        }

        // Compare the two locations on the fields that key the chart cache: Lat / Lon /
        // hemisphere flags (geometry) plus the year-start-day (NightCache horizon). Horizon
        // and Duration are scrub-only inputs to RenderSessionsSeries and don't invalidate the
        // cache. DateTime within a single year-window is fine; only the year-start
        // anchor matters (NightCache.ComputeYearStartDay drops the day-of-month and rounds
        // to the start of the seed's month).
        private static bool LocationsCacheEquivalent(Location a, Location b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Latitude  == b.Latitude
                && a.Longitude == b.Longitude
                && a.North     == b.North
                && a.West      == b.West
                && a.Elevation == b.Elevation
                && NightCache.ComputeYearStartDay(a.DateTime) == NightCache.ComputeYearStartDay(b.DateTime);
        }

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
                mFiltersMenu.DropDownItems.Add(item);
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
            // restarts the SessionsRebuildDebounce, whose tick can run while the chart's
            // year caches are still being built (the M31 seed's async BuildSeriesList
            // is still mid-flight at construction time). Setting MoonAvoidanceProfile
            // alone is enough -- the setter propagates to every AltitudeSeries in
            // mSeriesByTarget, and the in-flight async builder picks it up when it
            // reaches RenderSessionsSeries. The post-Edit-Filters caller in
            // OpenEditFiltersDialog explicitly calls RebuildSessionsData afterward,
            // when caches are guaranteed populated.
            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            if (firstFilter != null)
            {
                firstFilterItem.Checked = true;
                MoonAvoidanceProfile firstProfile = firstFilter.ToProfile();
                WriteProfileToControls(firstProfile);
                mActiveFilter = firstFilter;
                mMoonAvoidanceProfile = avoidanceOn ? firstProfile : null;
                mActiveFilterCenterNm = firstFilter.CenterNm;
            }
            else
            {
                // Empty library: nothing checked, no profile.
                mActiveFilter = null;
                mMoonAvoidanceProfile = null;
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
                    name:           current.Name,
                    separationDeg:  builtin.SeparationDeg,
                    widthDays:      builtin.WidthDays,
                    relaxEnabled:   builtin.RelaxEnabled,
                    relaxMinAltDeg: builtin.RelaxMinAltDeg,
                    relaxMaxAltDeg: builtin.RelaxMaxAltDeg,
                    relaxScale:     builtin.RelaxScale,
                    centerNm:       builtin.CenterNm,
                    bandwidthNm:    builtin.BandwidthNm);
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
        // top-level mFiltersMenu.Text also gets ' *' iff any filter is modified. User-
        // created filters (no built-in baseline) always show no '*'. Called after
        // BuildFiltersMenu/BuildFiltersGroupBox initial setup and after every filter
        // auto-save tick.
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

            if (mFiltersMenu != null)
                mFiltersMenu.Text = anyModified ? "&Filters *" : "&Filters";
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

            // Trigger the visibility refresh explicitly. BuildFiltersMenu deliberately
            // skips this (the construction-time caller can't safely run it -- see the
            // comment block in BuildFiltersMenu); by the time OpenEditFiltersDialog runs,
            // year caches are populated and the call is cheap.
            if (mSubCharts != null)
            {
                ChartContext refreshCtx = SnapshotCurrent(mLastRenderedTargets);
                foreach (var sc in mSubCharts.Values)
                {
                    sc.RefreshVisibility(refreshCtx, mCache);
                }
            }
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

            mMoonAvoidanceProfile = avoidanceOn ? profile : null;
            // Push the active filter's center wavelength so the K-S sky-brightness
            // minute-loop scales extinction k via Rayleigh λ⁻⁴ at the band. Sky
            // re-walks the K-S grid via SessionsRebuildDebounce_Tick (which calls
            // mLC2Sky.RefreshSkyBrightness after pushing this value).
            mActiveFilterCenterNm = filter.CenterNm;
            if (mLC2Sky != null) mLC2Sky.ActiveFilterCenterNm = filter.CenterNm;
            RestartSessionsRebuildDebounce();
        }

        // File -> Clear All Data... handler. Confirms via YesNo MessageBox, deletes the
        // three persistent files in %APPDATA%\TargetPlanner, then offers a restart so the
        // next launch boots from defaults. tp.log is deleted last so any per-file delete
        // errors get captured before the log file itself goes away. If the user declines
        // the restart, in-memory state is unchanged and any subsequent SettingsStore.Save /
        // FilterLibrary.Save call will recreate the corresponding file with current state.
        private void HandleClearAllDataClick()
        {
            string body =
                "Clear all TargetPlanner data?\n\n" +
                "This deletes:\n" +
                "  • " + SettingsStore.FilePath + "\n" +
                "  • " + FilterLibrary.DefaultPath + "\n" +
                "  • " + Log.FilePath + "\n\n" +
                "This cannot be undone.";

            DialogResult confirm = MessageBox.Show(this, body, "Clear All Data",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            TryDeleteFile(SettingsStore.FilePath);
            TryDeleteFile(FilterLibrary.DefaultPath);
            TryDeleteFile(Log.FilePath);

            DialogResult restart = MessageBox.Show(this,
                "Data cleared.\n\nRestart TargetPlanner now to load defaults?",
                "Clear All Data",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            if (restart == DialogResult.Yes) Application.Restart();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Error("ClearAllData: failed to delete '" + path + "'", ex);
            }
        }

        // Master on/off for moon avoidance. When checked, the active filter's profile
        // (read live from the Lorentzian controls -- they always reflect either a named
        // filter's values or the user's Custom scrubs) is pushed to the chart. When
        // unchecked, the chart sees null and skips moon-aware work entirely.
        private void OnAvoidanceEnableChanged(object sender, EventArgs e)
        {
            if (mSubCharts == null) return;

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
            mMoonAvoidanceProfile = profile;
            // Debounce so a fast Enable-Disable-Enable click sequence collapses to one
            // rebuild and the master toggle shares the Lorentzian-scrub debounce path.
            RestartSessionsRebuildDebounce();
        }

        // User scrubbed a Lorentzian control. Push the live values to the chart (gated
        // on master Enable) and start the auto-save debounce -- after 500 ms idle, the
        // tick handler commits the live values into mActiveFilter and persists. Returns
        // early under mSuppressFilterEvents (WriteProfileToControls is the writer; its
        // writes aren't user edits).
        private void OnLorentzianControlChanged(object sender, EventArgs e)
        {
            if (mSuppressFilterEvents) return;
            if (NumericUpDown_Moon_Separation == null) return;

            bool avoidanceOn = CheckBox_Moon_AvoidanceEnable != null
                            && CheckBox_Moon_AvoidanceEnable.Checked;
            mMoonAvoidanceProfile = avoidanceOn ? BuildProfileFromControls() : null;
            RestartFilterAutoSaveDebounce();
            RestartSessionsRebuildDebounce();
        }

        // Read the live Lorentzian control values into a MoonAvoidanceProfile. Used by
        // OnLorentzianControlChanged for the live chart push and (indirectly, via the
        // same control reads) by FilterAutoSaveDebounce_Tick when building the
        // replacement Filter.
        private MoonAvoidanceProfile BuildProfileFromControls()
            => MoonAvoidanceProfile.Custom(
                separationDeg:  (double)NumericUpDown_Moon_Separation.Value,
                widthDays:      (double)NumericUpDown_Moon_Width.Value,
                relaxEnabled:   CheckBox_Moon_RelaxEnabled.Checked,
                relaxMinAltDeg: (double)NumericUpDown_Moon_RelaxMin.Value,
                relaxMaxAltDeg: (double)NumericUpDown_Moon_RelaxMax.Value,
                relaxScale:     (double)NumericUpDown_Moon_RelaxScale.Value);

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

        // Trailing-edge tick for the Lorentzian-scrub auto-save. Builds a replacement
        // Filter from the live control values (preserving Name and BandwidthNm from
        // the active filter -- those aren't editable from the main form), replaces the
        // entry in mFilterLibrary, persists, and refreshes the menu '*' labels.
        // Suppressed while the EditFiltersForm modal is open; the dialog has its own
        // Save semantics against a transactional shadow.
        private void FilterAutoSaveDebounce_Tick(object sender, EventArgs e)
        {
            mFilterAutoSaveDebounce.Stop();
            if (mEditFiltersDialogOpen) return;
            if (mActiveFilter == null) return;

            int idx = IndexOfActiveFilter();
            if (idx < 0) return;

            TpFilter updated = new TpFilter(
                name:           mActiveFilter.Name,
                separationDeg:  (double)NumericUpDown_Moon_Separation.Value,
                widthDays:      (double)NumericUpDown_Moon_Width.Value,
                relaxEnabled:   CheckBox_Moon_RelaxEnabled.Checked,
                relaxMinAltDeg: (double)NumericUpDown_Moon_RelaxMin.Value,
                relaxMaxAltDeg: (double)NumericUpDown_Moon_RelaxMax.Value,
                relaxScale:     (double)NumericUpDown_Moon_RelaxScale.Value,
                centerNm:       mActiveFilter.CenterNm,
                bandwidthNm:    mActiveFilter.BandwidthNm);

            mFilterLibrary.Replace(idx, updated);
            mActiveFilter = updated;

            try { mFilterLibrary.Save(); }
            catch (Exception ex) { Log.Error("FilterLibrary.Save (auto-save) failed", ex); }

            RefreshFilterMenuLabels();
        }

        // Locate the active filter's index in mFilterLibrary by reference equality.
        // Returns -1 when mActiveFilter has been replaced (post-dialog-Save) before a
        // refresh, or when the library is empty.
        private int IndexOfActiveFilter()
        {
            if (mActiveFilter == null || mFilterLibrary == null) return -1;
            for (int i = 0; i < mFilterLibrary.Filters.Count; i++)
            {
                if (object.ReferenceEquals(mFilterLibrary.Filters[i], mActiveFilter)) return i;
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
            if (mSubCharts != null)
            {
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);
            }
            // Transit / Rise sort keys are time-dependent; Name is not. Skip the re-sort on
            // Name to avoid a pointless Items.Clear+re-add round-trip on every scrub tick.
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            UpdateLocalDateTimeEvents();
            if (mSubCharts != null)
            {
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);
            }
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
        }

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
        // mLocation.DateTime is already kept in sync with the pickers via
        // UpdateLocalDateTimeEvents (called from DatePicker/TimePicker ValueChanged and
        // Button_Now_Click). Don't overwrite it with DateTime.Now here -- that was the
        // pre-refactor assumption when the app was always "live now" by default.
        private async void Button_Graph_Click(object sender, EventArgs e)
        {
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

        // Shared graph-build pipeline. Cancels any in-flight build + replaces
        // mGraphCts before starting; both Button_Graph_Click (single-target) and
        // CheckedToggleDebounce_Tick (multi-target) call this. Whoever triggers a
        // new build supersedes the prior one -- the stale build's CT trips and the
        // catch (OperationCanceledException) lets it unwind cleanly without leaving
        // the form gating flags wedged.
        //
        // Async: PrepareManyAsync warms the per-target year-cache + per-Location
        // NightCache; sub-chart Render calls assume the cache is ready, so we
        // await first, then RenderArea dispatches into the active radio's sub-chart.
        // Empty targets is intentional -- ClearAll / fresh-NINA-load both produce
        // empty input; PrepareManyAsync no-ops on empty (the cache for previously-
        // rendered targets stays intact -- only SetLocationAsync ever drops cache),
        // RenderArea paints a blank chart per each sub-chart's empty-list contract.
        //
        // Park focus on the form before disabling Button_Graph. Otherwise Win32
        // auto-advances focus from the just-disabled Button_Graph to the next TabStop
        // (ComboBox_SelectTarget), whose focus-gain auto-selects its text and would
        // cascade into the combo's SelectedIndexChanged path.
        private async Task RunGraphBuildAsync(IReadOnlyList<Target> targets)
        {
            // Snapshot full ChartContext at build entry. RenderArea is called
            // against the snapshot, not against live MainForm fields -- if a
            // SetLocationAsync ran mid-build, the snapshot pins the build to its
            // original inputs and the post-await drift check abandons the render
            // so a stale series can't paint against new geometry.
            ChartContext ctxSnapshot = SnapshotCurrent(targets);

            (int progressGeneration, IProgress<int> targetProgress) =
                BeginChartBuildProgress(targetCount: targets.Count);

            // Latch the build's CTS into locals immediately after assignment so the
            // post-await render can't observe a successor build's CTS. Without the
            // latch, two RRGB calls racing would let the older build's RenderArea
            // read the newer build's mGraphCts and paint with the wrong token /
            // miss the cancel.
            mGraphCts?.Cancel();
            CancellationTokenSource buildCts = new CancellationTokenSource();
            mGraphCts = buildCts;
            CancellationToken buildCt = buildCts.Token;

            ActiveControl = null;
            Button_Graph.Enabled = false;
            mGraphBuildInProgress = true;

            try
            {
                if (mCache != null)
                {
                    await mCache.PrepareManyAsync(targets, buildCt, targetProgress);
                }

                // Drift check: if a location change ran during PrepareManyAsync
                // (combo pick / scrub debounce), the cache was reset and the build
                // we just awaited operated against the *new* location's empty
                // cache -- entries are missing and the snapshot-keyed render
                // would paint partial data. Abandon and let the location-change
                // path drive the next render.
                if (!object.ReferenceEquals(mLocation, ctxSnapshot.Location)) return;

                mLastRenderedTargets = new List<Target>(targets);

                // Paint on whichever area the user had selected when the build
                // started (carried in ctxSnapshot.ActiveArea). Day is the default
                // at form construction (Designer sets RadioButton_Day.Checked =
                // true) so a fresh launch lands on Day.
                RenderArea(ctxSnapshot, buildCt);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer build (single->multi or multi->single) or
                // explicit Cancel. mLastRenderedTargets is left intact -- the newer
                // build will overwrite it, or the user cancelled and the prior
                // mLastRenderedTargets is still the on-screen truth.
            }
            finally
            {
                mGraphBuildInProgress = false;
                Button_Graph.Enabled = true;
                Button_Cancel.Enabled = true;

                // Tick the bar to its Maximum, hold 1 s, reset to 0. Generation-
                // guarded so the success path is observed cleanly while a cancel
                // (which bumped mChartBuildGeneration in Button_Cancel_Click)
                // no-ops here -- Cancel_Click already reset the bar to 0.
                FinishChartBuildProgress(progressGeneration);
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

        // Trailing-edge debounce tick. Walks CheckedListBox_SelectedTargets.CheckedItems
        // in display order so the rendered target list -- and therefore the chart
        // legend -- inherits the listbox's NaturalStringComparer sort (see
        // GetNinaTargets). Iterating mSelection.Checked here would be set-order, not
        // sort-order. Empty CheckedItems -> empty targets -> blank chart, intentionally.
        private async void CheckedToggleDebounce_Tick(object sender, EventArgs e)
        {
            mCheckedToggleDebounce.Stop();

            var targets = new List<Target>();
            foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
            {
                string name = item.ToString();
                Target t = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
                if (t != null) targets.Add(t);
            }

            await RunGraphBuildAsync(targets);
        }

        // Returns the currently-checked view radio's area name. Day is the default
        // since the Designer sets RadioButton_Day.Checked = true; the radio cluster
        // ensures exactly one is checked at any time.
        private string SelectedArea()
        {
            if (RadioButton_Sky.Checked)      return "Sky";
            if (RadioButton_Sessions.Checked) return "Sessions";
            if (RadioButton_Year.Checked)     return "Year";
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
        private void RenderArea(ChartContext ctx, CancellationToken ct = default)
        {
            if (mSubCharts == null) return;
            if (ctx == null) return;
            if (!mSubCharts.TryGetValue(ctx.ActiveArea, out var sc)) return;
            ShowOnlyAltitudeChart(sc.Control);
            sc.Render(ctx, mCache, ct);
            ResizeAltitudeChartArea(sc.IdealHeight);
        }

        // Build a ChartContext snapshot from current MainForm state. Single point
        // that reads mLocation / mMoonAvoidanceProfile / mActiveFilterCenterNm /
        // SelectedArea() — adding a new chart input is one record-field addition
        // here plus one additional read here, not a signature break across six
        // files. Caller decides which target list to pass (single-target via
        // SelectedSingle, multi via the checked set, or empty for blanking).
        private ChartContext SnapshotCurrent(IReadOnlyList<Target> targets)
        {
            return new ChartContext(
                Location:             mLocation,
                Targets:              targets ?? Array.Empty<Target>(),
                MoonProfile:          mMoonAvoidanceProfile,
                ActiveFilterCenterNm: mActiveFilterCenterNm,
                ActiveArea:           SelectedArea());
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

            if (mSubCharts != null)
            {
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);
            }
        }

        // Signal the in-flight chart build to unwind. The Day / Moon phase is synchronous
        // and completes before Button_Graph_Click returns, so it can't be cancelled -- only
        // the Year + Sessions background compute is interruptible. The progress bar is reset
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
        private async void ComboBox_Location_SelectionIndexChanged(object sender, EventArgs e)
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

            // Symmetric reset path: clear checked set, blank chart, drop+rekey cache.
            // The coordinator's post-apply hook (RefreshAstrometryLabels) refreshes
            // the dependent dusk/dawn/sun/moon labels once the pipeline settles.
            await ResetForLocationChange();
        }

        // DropDown nulls the current selection so re-picking the same item (e.g. the
        // user's home location after a manual edit auto-switched the combo to "Custom")
        // still fires SelectedIndexChanged.
        private void ComboBox_Location_DropDown(object sender, EventArgs e)
        {
            ComboBox_Location.SelectedItem = null;
        }

        // Fired by every location-input event (lat/lon spinners, textboxes, N/W checkboxes,
        // Horizon, Duration). If the user edited a field by hand, flip the combo to "Custom"
        // so the combo label always matches the currently-displayed values, and restart the
        // debounce so the cache invalidates and the Sessions chart rebuilds (the Tick handler
        // does the cache-equivalency check, so a no-op edit -- e.g., flipping N then back --
        // ultimately doesn't drop the cache).
        private void OnLocationEdited(object sender, EventArgs e)
        {
            if (mSyncingLocationUI) return;

            // Per-edit label refresh. Cheap (~150 us); fires on every spinner tick so
            // the dependent readouts (dusk/dawn, sun/moon altitude, moon rise/set,
            // illumination, phase) track the live mLocation values without lag.
            RefreshAstrometryLabels();

            // Debounce-restart fires for every user-driven location edit, regardless of
            // whether the combo is already "Custom". The cache check inside the Tick
            // determines whether the cache actually drops; the chart's RebuildSessionsData
            // is harmless when the cache is empty (no-ops per series).
            RestartSessionsRebuildDebounce();

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
            // Boot default: the named location from PersonalDefaults when present in the
            // saved list, regardless of the last in-session selection.
            // mAppSettings.LastSelectedLocationName still tracks the most recent combo
            // pick (so we can persist it), but it no longer drives the start-up state --
            // a fresh launch always lands on the personal-default location unless it's
            // missing from settings.
            NamedLocationSetting personalDefault = mAppSettings.NamedLocations.Find(x =>
                string.Equals(x.Name, PersonalDefaults.LocationName, StringComparison.OrdinalIgnoreCase));
            if (personalDefault != null) return personalDefault.ToLocation();

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

                NumericUpDown_TargetFloor.ValueChanged    -= NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged -= NumericUpDown_TargetDuration_ValueChanged;
                NumericUpDown_LocalElevation.ValueChanged -= NumericUpDown_LocalElevation_ValueChanged;
                NumericUpDown_Extinction.ValueChanged     -= NumericUpDown_Extinction_ValueChanged;
                ComboBox_Bortle.SelectedIndexChanged      -= ComboBox_Bortle_SelectedIndexChanged;
                NumericUpDown_TargetFloor.Value    = ClampToRange(NumericUpDown_TargetFloor,    (decimal)mLocation.Horizon);
                NumericUpDown_TargetDuration.Value = ClampToRange(NumericUpDown_TargetDuration, (decimal)mLocation.Duration.TotalHours);
                NumericUpDown_LocalElevation.Value = ClampToRange(NumericUpDown_LocalElevation, (decimal)mLocation.Elevation);
                NumericUpDown_Extinction.Value     = ClampToRange(NumericUpDown_Extinction,     (decimal)mLocation.ExtinctionK);
                int bortleIdx = mLocation.BortleClass - 1;
                if (bortleIdx >= 0 && bortleIdx < ComboBox_Bortle.Items.Count)
                    ComboBox_Bortle.SelectedIndex = bortleIdx;
                NumericUpDown_TargetFloor.ValueChanged    += NumericUpDown_TargetFloor_ValueChanged;
                NumericUpDown_TargetDuration.ValueChanged += NumericUpDown_TargetDuration_ValueChanged;
                NumericUpDown_LocalElevation.ValueChanged += NumericUpDown_LocalElevation_ValueChanged;
                NumericUpDown_Extinction.ValueChanged     += NumericUpDown_Extinction_ValueChanged;
                ComboBox_Bortle.SelectedIndexChanged      += ComboBox_Bortle_SelectedIndexChanged;
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
            // Stock WinForms FolderBrowserDialog already uses the Vista IFileDialog UI
            // by default on .NET 6+. Single-folder selection only -- the multi-select
            // hack the legacy LocalLib.OpenFolderDialog provided was a nice-to-have that
            // didn't survive the .NET-Framework -> .NET 10 migration (relied on
            // reflection over System.Windows.Forms internal types). GetNinaTargets
            // accepts a string[] and iterates; passing a single-element array works.
            using var dialog = new FolderBrowserDialog
            {
                Description = "NINA Target Folder Browser",
                UseDescriptionForTitle = true,
                InitialDirectory = NinaTargetsRootPath,
                ShowNewFolderButton = false,
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                // SetKnownTargets resets Checked to empty (default-none-checked), which
                // fires CheckedSetChanged -> debounce -> blank multi-graph after 250 ms.
                // The user opts in target-by-target via the listbox; no Mode setup
                // needed.
                _ = GetNinaTargets(new[] { dialog.SelectedPath });
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
                // Log the full exception (stack + type) to tp.log before surfacing a shorter
                // user-facing MessageBox; the bare catch used to swallow the stack entirely.
                Log.Error("GetNinaTargets failed", ex);
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

            // Reorder every sub-chart's series + legend to match, in place -- no
            // replot, no recompute, no visibility refresh. The cached fit data
            // stays valid because the target SET is unchanged.
            if (mSubCharts != null && mLastRenderedTargets != null && mLastRenderedTargets.Count > 0)
            {
                var sorted = SortedTargets(mLastRenderedTargets).Where(t => t != null).ToList();
                mLastRenderedTargets = sorted;
                foreach (var sc in mSubCharts.Values) sc.Reorder(sorted);
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

        // The four view radio handlers all share the same shape: persist UI state,
        // and if the radio is now checked, dispatch a Render to the corresponding
        // sub-chart with the most recent target list. Day's handler also refreshes
        // the static AstrometryUi cache (legacy quirk -- kept for parity with the
        // dawn/dusk/moon labels).
        private void RadioButton_Day_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.DayChart = RadioButton_Day.Checked;
            if (!RadioButton_Day.Checked) return;
            AstrometryUi.Location(mLocation);
            RenderArea(SnapshotCurrent(mLastRenderedTargets));
        }

        private void RadioButton_Year_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.YearChart = RadioButton_Year.Checked;
            if (!RadioButton_Year.Checked) return;
            RenderArea(SnapshotCurrent(mLastRenderedTargets));
        }

        private void RadioButton_Sessions_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.SessionsChart = RadioButton_Sessions.Checked;
            if (!RadioButton_Sessions.Checked) return;
            RenderArea(SnapshotCurrent(mLastRenderedTargets));
        }

        private void RadioButton_Sky_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.SkyChart = RadioButton_Sky.Checked;
            if (!RadioButton_Sky.Checked) return;
            RenderArea(SnapshotCurrent(mLastRenderedTargets));
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


        // Reset ProgressBar_MultiTargetProcessing and return an IProgress<string> that ticks
        // it once per phase ("Day" / "Year" / "Sessions") for each of targetCount targets.
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
            // new known-target list (in the current sort order). Default checked = false
            // for every loaded target -- the user opts in target-by-target rather than
            // opting out via Clear-All. Matches TargetSelection's default-none-checked
            // policy in SetKnownTargets.
            PopulateCheckedListBoxFromTargets(defaultChecked: false);
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
        }

        private void OnCheckedListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;

            // De-selection (no row highlighted): nothing to push to the VM.
            if (CheckedListBox_SelectedTargets.SelectedItem == null) return;

            // Highlighting a row updates the single-target combo + RA/Dec inputs via
            // SetSelectedSingle. This fires SelectedSingleChanged only -- the
            // multi-graph debounce subscribes to CheckedSetChanged, so the chart is
            // not re-rendered when the user merely highlights a row.
            string name = CheckedListBox_SelectedTargets.SelectedItem.ToString();
            Target t = mSelection.KnownTargets.FirstOrDefault(x => x.Name == name);
            if (t != null) mSelection.SetSelectedSingle(t);
        }

        // Pooled instances for the transient-notice popup. Allocated lazily on first
        // ShowTransientMessage call and reused across subsequent invocations -- prior
        // implementation built a fresh Form + Label + Timer per call (GDI handle churn
        // for a notice that fires once every few minutes at most).
        private Form mTransientNotice;
        private Label mTransientLabel;
        private System.Windows.Forms.Timer mTransientTimer;

        // Show a small auto-dismissing notice centered on the main form. Used by
        // Button_Graph_Click when no targets are picked / checked / typed -- a silent
        // no-op was confusing. Non-modal: the main form stays interactive while the
        // notice is on screen. The pooled Form is hidden (not disposed) on Tick so the
        // next call reuses it.
        private void ShowTransientMessage(string text, int durationMs = 2000)
        {
            if (mTransientNotice == null || mTransientNotice.IsDisposed)
            {
                mTransientLabel = new Label
                {
                    Dock      = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 12F, FontStyle.Bold),
                };
                mTransientNotice = new Form
                {
                    // Manual positioning: FormStartPosition.CenterParent only fires on the
                    // first Show(); the pooled instance's subsequent Show() calls would
                    // keep the original position even if the main form has moved. We
                    // re-center against the main form's live bounds on every Show below.
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    StartPosition   = FormStartPosition.Manual,
                    ShowInTaskbar   = false,
                    ControlBox      = false,
                    Text            = string.Empty,
                    Size            = new Size(220, 80),
                    BackColor       = SystemColors.Info,
                };
                mTransientNotice.Controls.Add(mTransientLabel);
                mTransientTimer = new System.Windows.Forms.Timer();
                mTransientTimer.Tick += (s, e) =>
                {
                    mTransientTimer.Stop();
                    if (mTransientNotice != null && !mTransientNotice.IsDisposed && mTransientNotice.Visible)
                        mTransientNotice.Hide();
                };
            }

            mTransientLabel.Text = text;
            mTransientTimer.Stop();
            mTransientTimer.Interval = durationMs;

            // Center on the main form's live bounds (screen coords). Recomputed on every
            // Show so the notice tracks the user moving / resizing the main form between
            // displays.
            mTransientNotice.Location = new Point(
                this.Left + (this.Width  - mTransientNotice.Width)  / 2,
                this.Top  + (this.Height - mTransientNotice.Height) / 2);

            if (!mTransientNotice.Visible) mTransientNotice.Show(this);
            mTransientTimer.Start();
        }

        // Sets up ProgressBar_MultiTargetProcessing for a fresh chart build. Returns:
        //   * generation: stamp captured in the Progress<int> closure + the post-build
        //     finishing path, so a stale callback / hold-then-reset from a prior build
        //     no-ops instead of clobbering the new build's bar.
        //   * progress: per-target-completion handler from PrepareManyAsync. Each
        //     report increments the bar by 1 (counter is the 1-based completion count
        //     so we just assign Value directly).
        // The bar's Maximum is `targetCount + 1` -- one tick per target completion plus
        // one final tick for the post-cache Render. Synchronous Value=0 reset paints
        // before the first await, so a fresh click clears any stale fill before the
        // build starts.
        private (int generation, IProgress<int> progress) BeginChartBuildProgress(int targetCount)
        {
            int thisGeneration = ++mChartBuildGeneration;

            ProgressBar_MultiTargetProcessing.Minimum = 0;
            ProgressBar_MultiTargetProcessing.Maximum = Math.Max(1, targetCount + 1);
            ProgressBar_MultiTargetProcessing.Value   = 0;

            // Progress<T> captures SynchronizationContext.Current; constructed on the UI
            // thread, so Report() callbacks marshal back to the UI thread automatically.
            var progress = new Progress<int>(completed =>
            {
                if (thisGeneration != mChartBuildGeneration) return;  // stale
                int clamped = Math.Min(completed, ProgressBar_MultiTargetProcessing.Maximum);
                if (clamped > ProgressBar_MultiTargetProcessing.Value)
                    ProgressBar_MultiTargetProcessing.Value = clamped;
            });

            return (thisGeneration, progress);
        }

        // Final tick + 1-second hold + reset, executed after PrepareManyAsync + Render
        // complete. Generation-guarded so a Cancel or new Graph click during the hold
        // no-ops the reset. Marshalled to the UI thread via FromCurrentSynchronizationContext.
        private void FinishChartBuildProgress(int generation)
        {
            if (generation != mChartBuildGeneration) return;
            ProgressBar_MultiTargetProcessing.Value = ProgressBar_MultiTargetProcessing.Maximum;
            Task.Delay(1000).ContinueWith(
                _ =>
                {
                    if (generation != mChartBuildGeneration) return;
                    ProgressBar_MultiTargetProcessing.Value = 0;
                },
                TaskScheduler.FromCurrentSynchronizationContext());
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

        // VM mutator. SetAllChecked fires CheckedSetChanged; OnVmCheckedSetChanged
        // updates the listbox row check states, and OnVmCheckedSetChanged_TriggerGraph
        // arms the multi-graph debounce -> chart blanks ~250 ms later. The cache for
        // previously-rendered targets is preserved (PrepareManyAsync(empty) is a
        // no-op; only SetLocationAsync ever drops cache entries), so re-checking
        // those targets later hits the warm cache instantly.
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

            // Push the visible-tonight set into the VM. SetCheckedSet fires
            // CheckedSetChanged -> debounce -> multi-graph: the visible-tonight chart
            // appears automatically ~250 ms after the click. The combo / RA / Dec
            // inputs stay pointing at whatever single target the user had selected
            // before the click -- they describe the single-target view, not the
            // visible-set view, and the two paradigms are independent (see
            // Button_Graph_Click + WireSelectionVm).
            var visible = mSelection.KnownTargets.Where(t =>
                useEverVisible
                    ? Astronomy.Core.Session.CoarseVisibility.IsEverVisible(t, pickedNightLocation, night)
                    : Astronomy.Core.Session.CoarseVisibility.IsAboveHorizonForAtLeast(
                          t, pickedNightLocation, night, horizon, pickedNightLocation.Duration))
                .ToList();
            mSelection.SetCheckedSet(visible);
        }
    }
}
