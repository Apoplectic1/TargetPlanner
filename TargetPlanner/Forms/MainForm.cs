using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Astronomy.Core.Horizons;
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
        //   - mMoonAvoidanceProfile / mActiveFilterCenterNm: state pushed into each
        //     sub-chart's Render / RefreshVisibility call. Set by SetActiveFilter
        //     and the Lorentzian / avoidance-checkbox handlers.
        // The "what targets is the active chart currently displaying" question is
        // answered by mCoordinator.LastAppliedFor(SelectedArea())?.Targets -- the
        // coordinator's stamp is the single SoT; the prior mLastRenderedTargets
        // shadow store is gone.
        private System.Collections.Generic.Dictionary<string, Charts.IAltitudeSubChart> mSubCharts;
        private Astronomy.Core.Moon.MoonAvoidanceProfile mMoonAvoidanceProfile;
        private double mActiveFilterCenterNm = 550.0;

        // Single source of truth for per-target curve / legend colors across every
        // sub-chart. Built once per KnownTargets change (NINA load), Name-sorted so
        // the same target lands on the same palette index across reloads of the
        // same folder. Sub-chart Render reads ctx.TargetColors[target] (threaded
        // through ChartContext) rather than computing palette[i % len] per-iteration
        // — the latter diverged between sub-charts whenever the targets list order
        // differed between their Renders (e.g. after Reorder on a sort change),
        // visible as a target's curve flipping color when the user switched radios.
        private System.Collections.Generic.Dictionary<Astronomy.Core.Targets.Target, System.Drawing.Color> mTargetColorsByTarget =
            new System.Collections.Generic.Dictionary<Astronomy.Core.Targets.Target, System.Drawing.Color>();

        // User-added (non-NINA) targets, persisted to %AppData%\TargetPlanner\local-targets.json.
        // Re-merged into KnownTargets after every NINA Load so a re-browse doesn't wipe them.
        // Membership doubles as the "is this target locally-added?" check on Button_RemoveTarget
        // (locally-added removals also drop the target from the sidecar; NINA-loaded removals
        // don't, since the next browse re-adds them).
        private System.Collections.Generic.HashSet<Astronomy.Core.Targets.Target> mLocalTargets =
            new System.Collections.Generic.HashSet<Astronomy.Core.Targets.Target>();

        // Per-target dupe-set background colors for CheckedListBox_SelectedTargets.
        // Targets sharing (RA, Dec, North) form a dupe-set; each set gets a stable
        // pastel from DupeSetPalette so the user can spot multi-coord coincidences
        // at a glance. Recomputed on every KnownTargetsChanged. Targets not in any
        // dupe-set are absent from the dict; the owner-draw handler reads missing as
        // "use default background".
        private System.Collections.Generic.Dictionary<Astronomy.Core.Targets.Target, System.Drawing.Color> mDupeSetColors =
            new System.Collections.Generic.Dictionary<Astronomy.Core.Targets.Target, System.Drawing.Color>();

        // Pastel palette indexed by stable hash of (RoundedRa, RoundedDec, North)
        // so the same coord set lands on the same color across sort changes and
        // re-populates. Alpha is muted so listbox row text stays readable on the
        // OS theme regardless of light / dark.
        // Opaque pastels; GDI+ alpha-blending against a system-themed CheckedListBox
        // background renders inconsistently across Windows themes, so we mix the
        // tints into the OS Window color directly rather than relying on alpha.
        private static readonly System.Drawing.Color[] DupeSetPalette = new[]
        {
            System.Drawing.Color.FromArgb(190, 220, 250),  // soft blue
            System.Drawing.Color.FromArgb(250, 230, 180),  // soft amber
            System.Drawing.Color.FromArgb(240, 210, 240),  // soft magenta
            System.Drawing.Color.FromArgb(200, 240, 220),  // soft teal
            System.Drawing.Color.FromArgb(250, 210, 200),  // soft salmon
            System.Drawing.Color.FromArgb(230, 240, 200),  // soft lime
            System.Drawing.Color.FromArgb(220, 210, 250),  // soft lavender
            System.Drawing.Color.FromArgb(240, 240, 200),  // soft pale yellow-green
        };

        // Phase 3 of the SoC refactor: ChartCacheStore owns the per-(Location, Target)
        // year cache + per-Location NightCache. After GetNinaTargets completes we kick
        // off PrepareManyAsync(KnownTargets) for background pre-population so subsequent
        // Graph clicks find caches already built. On Location change we call
        // SetLocationAsync to drop everything and re-populate at the new location.
        private TargetPlanner.Caches.ChartCacheStore mCache;

        // Latest UI-bound astrometry snapshot, refreshed by RefreshAstrometryLabels
        // on every DatePicker / TimePicker / OnLocationEdited tick. Immutable
        // record -- safe to read from any thread; replaces the prior static
        // mutable AstrometryUi.* properties.
        private TargetPlanner.Support.AstrometryUi mAstrometryUi = TargetPlanner.Support.AstrometryUi.Empty;

        // Form-lifecycle cancellation. Cancelled on FormClosing so background
        // warmups (GetNinaTargets' PrepareManyAsync / PrepareFitsAsync chain)
        // stop awaiting after the form is on its way out. The cache itself
        // doesn't observe this token -- its surface is CT-free by the
        // c74224f cancellation-removal stance; any in-flight build runs to
        // completion and discards harmlessly via the publish-time stale check.
        // The CTS exists purely so the awaiting Task.Run lambda exits cleanly
        // instead of trying to touch the form after Dispose.
        private readonly CancellationTokenSource mFormClosingCts = new CancellationTokenSource();

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

            // Locally-added targets (typed via RA/Dec spinners + Add button) persist to
            // %AppData%\TargetPlanner\local-targets.json. Loaded here so they're available
            // before the startup NINA browse merges them into KnownTargets.
            foreach (Target lt in LocalTargetStore.Load())
                mLocalTargets.Add(lt);
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

            // Help and Filters top-level menu items + the Help children
            // (Check for Updates / About) are now Designer-resident -- see
            // HelpToolStripMenuItem_MainForm + FiltersToolStripMenuItem_MainForm
            // + CheckUpdatesToolStripMenuItem + AboutToolStripMenuItem in
            // MainForm.Designer.cs. Click handlers wire to OnCheckUpdatesClick /
            // OnAboutClick below. Filters children (one per library filter) stay
            // dynamic and get populated by BuildFiltersMenu().

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

            // Signal background warmups to stop awaiting before Dispose'ing the
            // cache they're calling into.
            try { mFormClosingCts.Cancel(); } catch (ObjectDisposedException) { }
            mFormClosingCts.Dispose();

            mCoordinator?.Dispose();
            mCache?.Dispose();

            mSessionsRebuildDebounce?.Stop();
            mSessionsRebuildDebounce?.Dispose();
        }

        public void InitializeDynamicControls()
        {
            string[] folderSelectedPaths = { NinaTargetsRootPath };

            // Wire the dupe-set tint callback. The DupeAwareCheckedListBox owns
            // the paint path (CheckedListBox swallows the standard DrawItem event),
            // so we expose the dupe-color lookup as a Func and the listbox calls
            // it on every row paint.
            CheckedListBox_SelectedTargets.RowBackground = GetDupeRowBackground;

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
            // (it captures references via the resolver delegates). The
            // post-apply hook is the one place that runs side-effects which
            // don't fit Render or RefreshVisibility:
            //   - Astrometry labels (dawn/dusk/sun/moon altitude/phase/illumination).
            //   - Now-line position on every sub-chart (date/time scrubs that
            //     don't trigger a Render still need the red line to move).
            //   - Horizon-line position on every sub-chart (horizon scrubs that
            //     refresh visibility-only need the green line to move).
            //   - Sky's K-S brightness re-walk (Bortle/Extinction/Filter scrubs).
            mCoordinator = new TargetPlanner.State.ChartCoordinator(
                cache: mCache,
                renderActiveArea: ctx => RenderArea(ctx),
                showOnlyActiveArea: ctx =>
                {
                    // Cheap "make this sub-chart visible without re-rendering"
                    // path. Used by the coordinator's skip-Render-on-redundant-
                    // area-change optimization when the new active area is
                    // already current with the snapshot.
                    if (mSubCharts == null) return;
                    if (!mSubCharts.TryGetValue(ctx.ActiveArea, out var sc)) return;
                    ShowOnlyAltitudeChart(sc.Control);
                    ResizeAltitudeChartArea(sc.IdealHeight);
                },
                postApplyHook: ctx =>
                {
                    RefreshAstrometryLabels();
                    foreach (var sc in mSubCharts.Values)
                    {
                        sc.UpdateNowLine(ctx.Location.DateTime);
                        sc.UpdateHorizonLine(ctx.Policy.LocalHorizon.MinAltitude);
                    }
                    PushSkyKSInputs(ctx);
                });

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
            mAstrometryUi = AstrometryUi.For(mLocation);

            Label_AstronomicalDuskValue.Text = mAstrometryUi.AstronomicalDusk.ToShortTimeString();
            Label_AstronomicalDawnValue.Text = mAstrometryUi.AstronomicalDawn.ToShortTimeString();
            Label_SunAltitudeValue.Text = mAstrometryUi.SunAltitude.ToString("F0") + "\u00B0";
            Label_LunarAltitudeValue.Text = mAstrometryUi.LunarAltitude.ToString("F0") + "\u00B0";
            Label_LunarIlluminationFractionValue.Text = (mAstrometryUi.LunarIlluminationFraction * 100).ToString("F0") + "%";
            Label_LunarPhaseValue.Text = mAstrometryUi.LunarPhase;
            Label_MoonRiseValue.Text = mAstrometryUi.LunarRise.ToShortTimeString();
            Label_MoonSetValue.Text = mAstrometryUi.LunarSet.ToShortTimeString();
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
                t.With(name: ComboTextOrFallback(t.Name),
                       rightAscension: Math.Round(mRaInput.Magnitude, 6)));
        }

        private void OnDeclinationEdited(object sender, EventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = mSelection.SelectedSingle ?? Target.Default;
            mSelection.SetSelectedSingle(
                t.With(
                    name:        ComboTextOrFallback(t.Name),
                    declination: Math.Round(mDecInput.Magnitude, 6),
                    north:       mDecInput.Positive));
        }

        // Honor the user's typed name in ComboBox_SelectTarget when building a new
        // SelectedSingle from a spinner edit. Without this, OnVmSelectedSingleChanged
        // overwrites the combo's typed text with the prior target's name on every
        // RA/Dec edit -- making "type a new name + adjust RA/Dec + Add" impossible.
        // Falls back to the prior name when the combo is blank.
        private string ComboTextOrFallback(string fallback)
        {
            string typed = ComboBox_SelectTarget?.Text;
            return string.IsNullOrWhiteSpace(typed) ? fallback : typed;
        }

        private void NumericUpDown_TargetDuration_ValueChanged(object sender, EventArgs e)
        {
            TimeSpan newDuration = TimeSpan.FromMinutes((double)NumericUpDown_TargetDuration.Value * 60.0);
            mLocation = mLocation.With(duration: newDuration);
            if (mCoordinator == null) return;
            // Coordinator's internal debounce coalesces rapid scrub ticks into one
            // pipeline run; pipeline diff catches Duration change as HDM-only and
            // refreshes visibility on every sub-chart.
            mCoordinator.Apply(SnapshotCurrent());
        }

        private void NumericUpDown_TargetFloor_ValueChanged(object sender, EventArgs e)
        {
            double newHorizon = (double)NumericUpDown_TargetFloor.Value;
            mLocation = mLocation.With(horizon: newHorizon);
            if (mCoordinator == null) return;
            // Horizon-line repositioning stays immediate -- it's one strip per chart
            // and the user wants instant feedback as they scrub. The per-target
            // visibility recompute is what's expensive; the coordinator's debounce
            // collapses scrub ticks into one trailing-edge pipeline run.
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateHorizonLine(newHorizon);
            mCoordinator.Apply(SnapshotCurrent());
        }

        // Lazily-constructed shared Timer debouncing OnLocationEdited (lat/lon/elev/
        // N/W/Bortle/Extinction edits). The debounce exists so the keying-change
        // detection (LocationsCacheEquivalent) runs once per scrub burst rather
        // than per spinner tick -- a keying change triggers ResetForLocationChange
        // (clears checked set + blanks chart) which we don't want firing on every
        // intermediate tick of a multi-step scrub.
        //
        // Other handlers (Horizon / Duration / Filter / Moon) don't have a keying-
        // change semantic and call mCoordinator.Apply directly; the coordinator's
        // own internal 150 ms debounce coalesces those.
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

        // Trailing-edge tick for the OnLocationEdited debounce. Branches on whether
        // the just-settled mLocation crossed LocationsCacheEquivalent vs the cache:
        //
        // 1. Keying drift -> ResetForLocationChange (clears checked set, blanks
        //    chart, drops + rebuilds the cache against the new location). Per
        //    spec, scrubs that cross are treated as "I changed sites."
        //
        // 2. Within-equiv scrub (Bortle / ExtinctionK -- the non-keying fields
        //    that ride OnLocationEdited): hand a snapshot to the coordinator
        //    immediately. ApplyImmediateAsync (no further internal debounce
        //    since we've already settled) runs the pipeline; the diff catches
        //    Bortle / ExtinctionK change as HDM-style and refreshes visibility
        //    on every sub-chart. Post-apply hook fires PushSkyKSInputs(ctx)
        //    so Sky's K-S brightness re-walks with the new inputs.
        private async void SessionsRebuildDebounce_Tick(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                mSessionsRebuildDebounce.Stop();

                if (mCache != null && !LocationsCacheEquivalent(mLocation, mCache.CurrentLocation))
                {
                    await ResetForLocationChange();
                    return;
                }

                if (mCoordinator == null) return;
                await mCoordinator.ApplyImmediateAsync(SnapshotCurrent());
            }
            catch (Exception ex)
            {
                Log.Error("SessionsRebuildDebounce_Tick threw", ex);
            }
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
        //   - Hand the coordinator an empty-targets snapshot. The pipeline
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
            // Cancel any in-flight pipeline (chart build or scrub-debounce tick)
            // so its post-await effects can't paint stale geometry.
            mCoordinator?.Cancel();

            mSelection.SetAllChecked(false);
            mCheckedToggleDebounce?.Stop();

            // Explicit empty targets so the active area re-renders blank under the
            // new location. The no-arg SnapshotCurrent() would inherit the prior
            // last-applied target list (i.e., the old location's targets) -- not
            // what we want on a deliberate reset.
            await mCoordinator.ApplyImmediateAsync(SnapshotCurrent(Array.Empty<Target>()));
        }

        // Push the active filter's center wavelength + re-walk the K-S minute grid
        // through the Sky sub-chart's existing series. Called from the coordinator's
        // post-apply hook so Bortle / ExtinctionK / Filter scrubs (and any other
        // pipeline) keep Sky's brightness curves in sync with the just-applied
        // snapshot. ctx-based overload reads the snapshot's filter + location
        // for snapshot-coherence under mid-pipeline drift; the no-arg overload
        // is kept for legacy callers that still read MainForm fields directly.
        // Null-safe; no-op when Sky isn't instantiated yet (early-init paths).
        private void PushSkyKSInputs(ChartContext ctx)
        {
            if (mLC2Sky == null || ctx == null || ctx.Location == null || ctx.Policy == null) return;
            mLC2Sky.ActiveFilterCenterNm = ctx.Policy.FilterCenterNm;
            mLC2Sky.RefreshSkyBrightness(mCache, ctx.Location);
        }

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

            // Trigger the visibility refresh explicitly. BuildFiltersMenu deliberately
            // skips this (the construction-time caller can't safely run it -- see the
            // comment block in BuildFiltersMenu); by the time OpenEditFiltersDialog runs,
            // year caches are populated and the call is cheap.
            if (mSubCharts != null)
            {
                ChartContext refreshCtx = SnapshotCurrent();
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
            // minute-loop scales extinction k via Rayleigh λ⁻⁴ at the band. The
            // coordinator's post-apply hook calls PushSkyKSInputs(ctx) to re-walk
            // the K-S grid; the property is also set immediately here for any
            // sync read that lands before the pipeline settles.
            mActiveFilterCenterNm = filter.CenterNm;
            if (mLC2Sky != null) mLC2Sky.ActiveFilterCenterNm = filter.CenterNm;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Help -> Check for Updates... handler. Wired to CheckUpdatesToolStripMenuItem
        // in MainForm.Designer.cs.
        private async void OnCheckUpdatesClick(object sender, EventArgs e)
        {
            await UpdateService.CheckManuallyAsync(this);
        }

        // Help -> About TargetPlanner handler. Wired to AboutToolStripMenuItem in
        // MainForm.Designer.cs.
        private void OnAboutClick(object sender, EventArgs e)
        {
            using (var dlg = new AboutDialog())
                dlg.ShowDialog(this);
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
                "  • " + LocalTargetStore.FilePath + "\n" +
                "  • " + Log.FilePath + "\n\n" +
                "This cannot be undone.";

            DialogResult confirm = MessageBox.Show(this, body, "Clear All Data",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            TryDeleteFile(SettingsStore.FilePath);
            TryDeleteFile(FilterLibrary.DefaultPath);
            TryDeleteFile(LocalTargetStore.FilePath);
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
            // Coordinator's internal debounce collapses a fast Enable-Disable-
            // Enable click sequence into one trailing-edge pipeline run.
            mCoordinator?.Apply(SnapshotCurrent());
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
            mCoordinator?.Apply(SnapshotCurrent());
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
            UpdateLocalDateTimeEvents();
            // Immediate now-line update for live feedback during scrub. Coordinator's
            // post-apply hook re-runs UpdateNowLine on settle (cheap; just shifts a
            // section's X position).
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);
            // Transit / Rise sort keys are time-dependent; Name is not. Skip the re-sort on
            // Name to avoid a pointless Items.Clear+re-add round-trip on every scrub tick.
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
            // Coordinator: post-apply hook handles RefreshAstrometryLabels + final
            // line sync. DateTime sub-day changes don't trigger any structural diff
            // (year-start unchanged), so the pipeline is no-op except the hook.
            mCoordinator?.Apply(SnapshotCurrent());
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            UpdateLocalDateTimeEvents();
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
            mCoordinator?.Apply(SnapshotCurrent());
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
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
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
            catch (Exception ex)
            {
                Log.Error("Button_Graph_Click threw", ex);
            }
        }

        // Shared graph-build entry. Both Button_Graph_Click (single-target) and
        // CheckedToggleDebounce_Tick (multi-target) call this. The coordinator's
        // pipeline owns supersedence via its internal generation counter: a newer
        // Apply increments the generation; older pipelines bail at their gen check
        // before any side-effecting write.
        //
        // Empty targets is intentional -- ClearAll / fresh-NINA-load / location
        // change all produce empty input; PrepareManyAsync no-ops on empty (the
        // cache for previously-rendered targets stays intact -- only
        // SetLocationAsync ever drops cache entries), and the active sub-chart's
        // Render paints a blank chart per its empty-list contract.
        //
        // Park focus on the form before disabling Button_Graph. Otherwise Win32
        // auto-advances focus from the just-disabled Button_Graph to the next TabStop
        // (ComboBox_SelectTarget), whose focus-gain auto-selects its text and would
        // cascade into the combo's SelectedIndexChanged path.
        private async Task RunGraphBuildAsync(IReadOnlyList<Target> targets)
        {
            // Snapshot full ChartContext at build entry; the coordinator's
            // pipeline is location-coherent against this snapshot.
            ChartContext ctxSnapshot = SnapshotCurrent(targets);

            (int progressGeneration, IProgress<int> targetProgress) =
                BeginChartBuildProgress(targetCount: targets.Count);

            ActiveControl = null;
            Button_GraphTarget.Enabled = false;

            try
            {
                // Coordinator owns the cache-prep + render pipeline. Its
                // generation-counter supersedence ensures only the latest Apply's
                // pipeline writes Render state; older pipelines bail before
                // touching the chart. The progress object forwards to
                // PrepareManyAsync so per-target completion ticks drive
                // ProgressBar_MultiTargetProcessing through BeginChartBuildProgress.
                if (mCoordinator != null)
                {
                    await mCoordinator.ApplyImmediateAsync(ctxSnapshot, targetProgress);
                }
                // The coordinator's mLastAppliedByArea is the single SoT for the
                // rendered target list; no form-side shadow store to update.
            }
            finally
            {
                Button_GraphTarget.Enabled = true;

                // Tick the bar to its Maximum, hold 1 s, reset to 0. Generation-
                // guarded so a superseding Apply doesn't leave the bar stuck.
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
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                mCheckedToggleDebounce.Stop();

                var targets = new List<Target>();
                foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
                {
                    if (item is TargetRow row && row.Target != null) targets.Add(row.Target);
                }

                await RunGraphBuildAsync(targets);
            }
            catch (Exception ex)
            {
                Log.Error("CheckedToggleDebounce_Tick threw", ex);
            }
        }

        // Force-render the currently-checked set immediately, bypassing the 250 ms
        // CheckedToggleDebounce. Convenience for users who want the chart now after
        // a series of checkbox toggles, or who want to re-render the same checked
        // set after an HMD/Location scrub. Walks CheckedListBox_SelectedTargets in
        // display order so the rendered list inherits the listbox's NaturalString-
        // Comparer sort, mirroring CheckedToggleDebounce_Tick.
        private async void Button_CheckedTargets_Click(object sender, EventArgs e)
        {
            mCheckedToggleDebounce?.Stop();

            var targets = new List<Target>();
            foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
            {
                if (item is TargetRow row && row.Target != null) targets.Add(row.Target);
            }

            await RunGraphBuildAsync(targets);
        }

        // Returns the currently-active chart-area name. The radio cluster
        // (Day / Year / Sessions) ensures exactly one is checked at any time;
        // CheckBox_Sky lives inside the Day radio and toggles its sub-mode
        // between altitude (Day) and K-S brightness (Sky). Day↔Sky toggling
        // exercises the coordinator's skip-Render-on-redundant-area-change
        // optimization for instant switching.
        private string SelectedArea()
        {
            if (RadioButton_Sessions.Checked) return "Sessions";
            if (RadioButton_Year.Checked)     return "Year";
            // Day radio active (default). Sub-mode determined by CheckBox_Sky.
            if (CheckBox_Sky != null && CheckBox_Sky.Checked) return "Sky";
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
        private void RenderArea(ChartContext ctx)
        {
            if (mSubCharts == null) return;
            if (ctx == null) return;
            if (!mSubCharts.TryGetValue(ctx.ActiveArea, out var sc)) return;
            ShowOnlyAltitudeChart(sc.Control);
            sc.Render(ctx, mCache);
            ResizeAltitudeChartArea(sc.IdealHeight);
        }

        // No-arg overload: reads the shared "current targets" from the coordinator
        // (any area's stamp would carry the same target set, but using the dedicated
        // LastAppliedTargets property avoids the "Year never rendered, returns
        // empty after a radio swap" bug -- the coordinator stamps this property
        // on every successful pipeline regardless of active area). This is the
        // SoT replacement for the prior mLastRenderedTargets shadow store.
        // RunGraphBuildAsync and ResetForLocationChange still use the
        // explicit-targets overload to set Targets explicitly.
        private ChartContext SnapshotCurrent()
        {
            IReadOnlyList<Target> lastTargets =
                mCoordinator?.LastAppliedTargets ?? Array.Empty<Target>();
            return SnapshotCurrent(lastTargets);
        }

        // Build a ChartContext snapshot from current MainForm state. Single point
        // that reads mLocation / mMoonAvoidanceProfile / mActiveFilterCenterNm /
        // SelectedArea() / mTargetColorsByTarget — adding a new chart input is one
        // record-field addition here plus one additional read here, not a signature
        // break across six files. Caller decides which target list to pass
        // (single-target via SelectedSingle, multi via the checked set, or empty
        // for blanking).
        //
        // PlanningPolicy is synthesized from mLocation.Horizon / .Duration plus the
        // form-level moon profile + filter center. mLocation continues to persist
        // these values via NamedLocationSetting; ChartContext carries only the
        // policy projection (Library APIs never read Location.Horizon/.Duration).
        // Once a per-site `.hrz` file is wired in PR-5, the scalar horizon factory
        // gets swapped for the polyline path here and nothing downstream changes.
        private ChartContext SnapshotCurrent(IReadOnlyList<Target> targets)
        {
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                targetFloorDeg:  mLocation.Horizon,
                minDuration:     mLocation.Duration,
                moonProfile:     mMoonAvoidanceProfile,
                filterCenterNm:  mActiveFilterCenterNm);

            return new ChartContext(
                Location:     mLocation,
                Targets:      targets ?? Array.Empty<Target>(),
                Policy:       policy,
                ActiveArea:   SelectedArea(),
                TargetColors: mTargetColorsByTarget);
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
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mLocalDateTime.When);

            mCoordinator?.Apply(SnapshotCurrent());
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

            // Locally-added targets are additive on top of NINA. Append them so a NINA
            // reload doesn't wipe them. PopulateCheckedListBoxFromTargets re-sorts via
            // SortedTargets, so append order is irrelevant for display.
            foreach (Target lt in mLocalTargets) allLoaded.Add(lt);

            // Push the new known-target list to the VM. KnownTargetsChanged fires once;
            // OnVmKnownTargetsChanged repopulates ComboBox_SelectTarget +
            // CheckedListBox_SelectedTargets via PopulateTargetComboFromTargets +
            // PopulateCheckedListBoxFromTargets, which read the new VM state.
            mSelection.SetKnownTargets(allLoaded);

            // Phase 3: kick off background pre-population of the chart cache so subsequent
            // Graph clicks find caches already built. Fire-and-forget; a re-Browse just
            // starts a new warmup over the same (and possibly larger) target set -- the
            // cache de-dupes per target so already-built entries are no-ops. Errors
            // swallowed -- this is a best-effort warmup, not load-bearing.
            //
            // Two phases of warmup: PrepareManyAsync builds the per-target yearDays
            // (~1-2 sec for 44 targets); PrepareFitsAsync builds per-(target, HdmKey)
            // fits against the current H/D/M (~few sec). Both run in the same Task.Run
            // so the second phase awaits the first naturally; the user's first
            // Sessions / Year click hits a warm cache and renders instantly.
            ChartContext warmupCtx = SnapshotCurrent(allLoaded);
            HdmKey hdm = warmupCtx.Hdm;
            IHorizonProfile horizon = warmupCtx.Policy.LocalHorizon;
            CancellationToken formCt = mFormClosingCts.Token;
            _ = Task.Run(async () =>
            {
                // Race the warmup against the form-closing signal so the awaiter
                // doesn't keep hold of the cache reference after the form has
                // started tearing down. The cache build itself isn't cancellable
                // and runs to completion regardless -- its publish-time stale
                // check makes that safe.
                try
                {
                    Task warmup = WarmupAsync();
                    Task cancelled = Task.Delay(Timeout.Infinite, formCt);
                    if (await Task.WhenAny(warmup, cancelled) == cancelled) return;
                    await warmup;  // observe any fault
                }
                catch (OperationCanceledException) { /* form closed mid-warmup; expected */ }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ChartCacheStore warmup failed: {ex}");
                }

                async Task WarmupAsync()
                {
                    await mCache.PrepareManyAsync(allLoaded);
                    await mCache.PrepareFitsAsync(allLoaded, hdm, horizon);
                }
            });
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

            Target found = TargetForRow(index);
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

            Target found = TargetForRow(mToolTipIndex);
            if (found == null) return;

            mToolTip.SetToolTip(CheckedListBox_SelectedTargets, found.Directory);
            mToolTip.AutoPopDelay = 5000;
            mToolTip.InitialDelay = 2000;
            mToolTip.ReshowDelay = 2000;
        }

        // The three view radio handlers (Day / Year / Sessions) all share the
        // same shape: persist UI state, and if the radio is now checked, hand a
        // snapshot to the coordinator. CheckBox_Sky lives inside the Day radio
        // as a sub-mode toggle (altitude vs K-S brightness); it's enabled only
        // when Day is the active radio. The coordinator's diff sees ActiveArea
        // changed and dispatches Render or ShowOnly depending on whether the
        // new active area's data is current.
        private void RadioButton_Day_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.DayChart = RadioButton_Day.Checked;
            if (CheckBox_Sky != null) CheckBox_Sky.Enabled = RadioButton_Day.Checked;
            if (!RadioButton_Day.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        private void RadioButton_Year_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.YearChart = RadioButton_Year.Checked;
            if (!RadioButton_Year.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        private void RadioButton_Sessions_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.SessionsChart = RadioButton_Sessions.Checked;
            if (!RadioButton_Sessions.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Sub-mode toggle inside the Day radio. Wired in MainForm.Designer.cs.
        // Toggling switches between Day (altitude) and Sky (K-S brightness)
        // chart areas. Enabled only when Day radio is active (gated by
        // RadioButton_Day_CheckedChanged); CheckedChanged firing while
        // disabled would only happen via programmatic state restore at
        // form-load and is harmless (Apply of the unchecked path renders Day,
        // which is what was about to be rendered anyway).
        private void CheckBox_Sky_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.SkyChart = CheckBox_Sky.Checked;
            // Only dispatch when Day is the active radio -- toggling the
            // checkbox while Year or Sessions is selected would otherwise
            // re-render those areas with an unchanged ActiveArea (Year /
            // Sessions don't read CheckBox_Sky). Cheap to gate here.
            if (RadioButton_Day == null || !RadioButton_Day.Checked) return;
            mCoordinator?.Apply(SnapshotCurrent());
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

            // Rebuild the per-target color map. Name-sorted so the same target lands on
            // the same palette index across reloads of the same folder; consumed by every
            // sub-chart's Render via ctx.TargetColors so all charts agree on each
            // target's color regardless of their Render-time iteration order.
            RebuildTargetColors();

            // Compute dupe-set background colors for the listbox owner-draw handler.
            // Targets sharing (RA, Dec, North) get a shared pastel; recomputed any
            // time KnownTargets changes (NINA load, Add, Remove).
            RecomputeDupeSetColors();

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

        // Rebuild mTargetColorsByTarget from the current KnownTargets, Name-sorted.
        // Stable across sort changes (Reorder doesn't touch this), across radio
        // switches (every sub-chart reads the same dict), and across HMD scrubs
        // (RefreshVisibility doesn't reassign). Rebuilds only when KnownTargets
        // changes (NINA load).
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

        // Group KnownTargets by (RoundedRa, RoundedDec, North) and assign each group
        // with size > 1 a stable pastel from DupeSetPalette. The palette index is a
        // deterministic hash of the coord triple so the same coord-set always lands
        // on the same color across sort changes / re-populates. Targets not in any
        // dupe-set are absent from mDupeSetColors -- the listbox owner-draw handler
        // reads "missing" as "use the OS default background".
        // Group targets by Name-OR-coords match. Two targets are in the same dupe
        // set if they share a Name OR they share (RA, Dec, North) -- and the
        // relation is transitive: T1 ~ T2 by name and T2 ~ T3 by coords means
        // {T1, T2, T3} are one group. Implemented via DSU. Each group with size
        // > 1 gets a stable pastel from DupeSetPalette (hash is XOR of member
        // identities so the assignment survives sort changes).
        private void RecomputeDupeSetColors()
        {
            mDupeSetColors.Clear();
            if (mSelection == null || mSelection.KnownTargets.Count == 0) return;

            var targets = mSelection.KnownTargets.Where(t => t != null).ToList();
            int n = targets.Count;
            if (n == 0) return;

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

            // Bucket by Name and by coord-triple, then union members within each
            // bucket to the bucket's first member.
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            var byCoord = new Dictionary<(double Ra, double Dec, bool N), int>();
            for (int i = 0; i < n; i++)
            {
                Target t = targets[i];
                if (byName.TryGetValue(t.Name, out int nameRoot)) Union(nameRoot, i);
                else byName[t.Name] = i;

                var key = (System.Math.Round(t.RightAscension, 6),
                           System.Math.Round(t.Declination, 6),
                           t.North);
                if (byCoord.TryGetValue(key, out int coordRoot)) Union(coordRoot, i);
                else byCoord[key] = i;
            }

            // Collect connected components.
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var bucket))
                {
                    bucket = new List<int>();
                    groups[root] = bucket;
                }
                bucket.Add(i);
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

        // Merge the current SelectedSingle (combo's resolved target -- could be a
        // NINA-known target or a transient one built from RA/Dec spinner edits) into
        // the checked set. Transient targets are added to KnownTargets and persisted
        // to the local-targets.json sidecar so they survive form-close + NINA reload.
        private void Button_AddTarget_Click(object sender, EventArgs e)
        {
            Target t = mSelection?.SelectedSingle;
            if (t == null) { ShowTransientMessage("No Target"); return; }

            bool wasNew = mSelection.AddKnownTarget(t);
            mSelection.SetChecked(t, true);
            if (wasNew)
            {
                mLocalTargets.Add(t);
                LocalTargetStore.Save(mLocalTargets);
            }

            // Re-sort listbox + combo by the current ComboBox_SortTargets selection
            // so the new target lands in its sorted position rather than wherever
            // PopulateCheckedListBoxFromTargets's first repopulate placed it.
            ResortSelectedTargets();

            // Keep the combo focused on the just-added target. ResortSelectedTargets
            // calls PopulateTargetComboFromTargets which preserves the prior text;
            // re-write it here in case the prior text had drifted (e.g. NINA reload
            // path reset combo to first sorted before this Add fired).
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try { ComboBox_SelectTarget.Text = t.Name; }
            finally { mUpdatingUiFromVm = wasUpdating; }
        }

        // Remove the current SelectedSingle from KnownTargets entirely (combo +
        // listbox both lose the entry). NINA-loaded targets re-appear on the next
        // browse; locally-added targets are also dropped from the sidecar so they
        // stay gone across restarts.
        private void Button_RemoveTarget_Click(object sender, EventArgs e)
        {
            Target t = mSelection?.SelectedSingle;
            if (t == null) { ShowTransientMessage("No Target"); return; }

            bool wasInLocal = mLocalTargets.Remove(t);
            mSelection.RemoveKnownTarget(t);
            if (wasInLocal) LocalTargetStore.Save(mLocalTargets);

            // Re-sort listbox + combo by the current ComboBox_SortTargets selection
            // so the survivor list stays in canonical order after the deletion.
            ResortSelectedTargets();
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

        private void OnCheckedListBoxItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (mUpdatingUiFromVm) return;
            Target t = TargetForRow(e.Index);
            if (t == null) return;
            bool isChecked = e.NewValue == CheckState.Checked;
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
            Astronomy.Core.Horizons.IHorizonProfile mathHorizon =
                new Astronomy.Core.Horizons.ScalarHorizonProfile(0.0);
            TimeSpan minDuration = TimeSpan.FromMinutes(15);

            var visible = mSelection.KnownTargets
                .Where(t => Astronomy.Core.Session.CoarseVisibility.IsAboveHorizonForAtLeast(
                    t, pickedNightLocation, night, mathHorizon, minDuration))
                .ToList();
            mSelection.SetCheckedSet(visible);
        }
    }
}
