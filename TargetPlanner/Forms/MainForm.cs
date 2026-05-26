using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Sun;
using Astronomy.Core.Time;
using Astronomy.NINA.Persistence;
using TargetPlanner.Filters;
using TargetPlanner.Forms;
using TargetPlanner.Horizons;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Targets;
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
        // Per-session observation moment (UTC + site zone). Replaces the legacy
        // (DateTime When, TimeZoneInfo Zone) tuple field; matches AL's named-
        // immutable-type convention so consumers thread the (Utc, Zone) pair
        // around without losing the invariant that they travel together.
        private ObservationMoment mObservation;

        // Per-site user planning preferences (target floor degrees, minimum
        // duration). Phase-2 successor to the now-removed Location.Horizon /
        // .Duration scalars. Mutated by NumericUpDown_TargetFloor /
        // NumericUpDown_TargetDuration handlers via `with` syntax; persisted
        // per-NamedSite; flows into PlanningPolicy at SnapshotCurrent.
        private PlanningPreferences mPlanningPreferences;

        // Per-site polyline horizon (NINA `.hrz`) when one is configured for the
        // active NamedSite and loads successfully; null otherwise (the
        // scalar floor from mPlanningPreferences.TargetFloorDeg applies on its
        // own). Reloaded on site-pick and on FileSystemWatcher change events;
        // threaded through PlanningPolicy.LocalHorizon by SnapshotCurrent (via
        // MaxOfHorizonProfile when a polyline + scalar both exist) so the chart
        // and the fits cache pick it up. HdmKey reference-compares this so a
        // swap (different file, hot-reload) invalidates the per-(target, HdmKey)
        // fits cache automatically.
        private IHorizonProfile mLocalHorizon;

        // Phase 2 of the SoC refactor: TargetSelection view-model owns target / list / mode
        // state. UI controls (ComboBox_SelectTarget, CheckedListBox_SelectedTargets, RA/Dec
        // inputs, Select-All / Clear-All / Visible-Tonight buttons) bind to it. The
        // mUpdatingUiFromVm flag protects the echo path: when a VM event fires and the
        // handler programmatically writes back to a UI control, the control's user-input
        // event re-fires and would round-trip through the VM. The flag short-circuits
        // those echoes so the VM stays the single source of truth.
        private TargetSelection mSelection;
        private bool mUpdatingUiFromVm;

        // Active chart-area state (post-PR4e: legacy AltitudeChart deleted; this state
        // used to live on it). MainForm owns:
        //   - mSubCharts: keyed by area ("Day"/"Sky"/"Year"/"Sessions"), each value
        //     implements IAltitudeSubChart so picker/spinner/debounce/Graph-click
        //     traffic dispatches via foreach + dict lookup instead of explicit fields.
        //   - mLC2Sky: typed reference for the Sky-specific quirks not in the
        //     interface (ActiveFilterCenterNm / ActiveFilterBandwidthNm setters +
        //     RefreshSkyBrightness).
        //   - mActiveFilter (declared further down with the filter-presenter state):
        //     single source of truth for the K-S filter inputs (CenterNm +
        //     BandwidthNm) and the Lorentzian moon-clear gate (via ToProfile()).
        //     Threaded into PlanningPolicy.ActiveFilter by SnapshotCurrent.
        // The "what targets is the active chart currently displaying" question is
        // answered by mCoordinator.LastAppliedTargets -- pre-stamped at pipeline
        // entry so concurrent Apply()s see the user's current intent rather than
        // the previous successful render.
        private System.Collections.Generic.Dictionary<string, Charts.IAltitudeSubChart> mSubCharts;

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

        // Targets last tagged "visible tonight" by a Button_VisibleTonight click.
        // Keyed by Target identity so the tint follows the target across sort
        // changes. Replaced (not unioned) on each Visible click; pruned to current
        // KnownTargets on any KnownTargets change (IntersectWith in
        // OnVmKnownTargetsChanged); cleared by right-click on the listbox. Dupe
        // colors take precedence over this tint at paint time so manual dupe-
        // hunting isn't masked by a Visible flag.
        private readonly System.Collections.Generic.HashSet<Astronomy.Core.Targets.Target> mVisibleTaggedTargets =
            new System.Collections.Generic.HashSet<Astronomy.Core.Targets.Target>();

        // Muted-success-green tint painted into the checkbox interior by
        // DupeAwareCheckedListBox for Visible-tonight rows. Independent of
        // the dupe-set row tint (different paint surfaces) so both can show
        // simultaneously on a row that is duped AND visible-tagged.
        private static readonly System.Drawing.Color VisibleTintColor =
            System.Drawing.Color.FromArgb(0x6E, 0xBE, 0x6E);

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

        // Form-lifecycle cancellation. Cancelled on FormClosing so background
        // warmups (GetNinaTargets' PrepareManyAsync / PrepareFitsAsync chain)
        // stop awaiting after the form is on its way out. The cache itself
        // doesn't observe this token -- its surface is CT-free by the
        // c74224f cancellation-removal stance; any in-flight build runs to
        // completion and discards harmlessly via the publish-time stale check.
        // The CTS exists purely so the awaiting Task.Run lambda exits cleanly
        // instead of trying to touch the form after Dispose.
        private readonly CancellationTokenSource mFormClosingCts = new CancellationTokenSource();

        // Suppresses the FormClosing settings.json save so a Defaults > Edit
        // (where the user is about to hand-edit the file) or Defaults > Clear
        // (where we just deleted the file) doesn't get its work clobbered by
        // an exit-time save of TP's in-memory AppSettings.
        private bool mSuppressFormClosingSave;

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
        // IAltitudeSubChart: ActiveFilterCenterNm + ActiveFilterBandwidthNm properties
        // (Rayleigh λ⁻⁴ extinction scaling + linear bandwidth scaling of the three K-S
        // nL contributions) and RefreshSkyBrightness(cache, location) (Bortle /
        // ExtinctionK / Filter scrub).
        private Charts.AltitudeSubChart_Sky mLC2Sky;

        // Day needs a typed reference for the DayChartModeChanged event raised by
        // the Floor / Transit radios overlaid on its plot area. Not part of
        // IAltitudeSubChart since the radios are Day-specific UI.
        private Charts.AltitudeSubChart_Day mLC2Day;

        // Active Day-chart placement-strategy mode (driven by the radios on
        // mLC2Day). SnapshotCurrent projects this into ChartContext.DayMode;
        // a flip flows through the coordinator's Apply pipeline as a normal
        // Render (cache eval surfaces DayModeChanged=true for any future
        // short-circuit consumer). Floor = current behavior (all fit-tonight
        // targets).
        private TargetPlanner.State.DayChartMode mDayChartMode = TargetPlanner.State.DayChartMode.Floor;

        private ToolTip mToolTip;
        private int mToolTipIndex;

        // Dedicated ToolTip instance for the explanatory radio-button tooltips (Sessions,
        // Day). Kept separate from mToolTip because its AutoPopDelay must be much longer
        // (60 s -- the text runs several paragraphs) than mToolTip's 5 s, and one ToolTip
        // instance can only hold a single AutoPopDelay.
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

        private const string VisibleButtonTooltipText =
@"Checks every target above the horizon for at least 15 minutes during
tonight's session, computed from the Date/Time picker forward.

Independent of H/D/M -- scrub the spinners freely; the candidate set
is preserved.";

        private Panel Panel_AltitudeChart;

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

        // Generation that currently owns the bar's visible state. Set when a
        // chart-pipeline closure first claims the bar (its `claimed` flip).
        // The deferred-hide continuation reads this -- if a follow-on pipeline
        // has claimed ownership in the meantime, the older pipeline's hide
        // bails and lets the newer one manage the bar. Without this, the 200 ms
        // hold at 100 % would either clobber a cold follow-on scrub (clearing
        // the bar mid-progress) or leave a warm follow-on staring at a stuck
        // 100 % indefinitely. UI-thread-only access; no synchronization needed.
        private int mBarOwnerGen;

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

        // FileSystemWatcher + 500 ms debounce for hot-reload of the active `.hrz`
        // file. Editor-save patterns (write-tmp + rename) typically fire multiple
        // change events in quick succession; the debounce coalesces them into one
        // reload + Apply. Watcher Changed/Created callbacks run off the UI thread
        // and BeginInvoke onto the form to restart the timer.
        // (Button_BrowseHorizon and Label_HorizonPath are Designer-managed controls;
        // their declarations live in MainForm.Designer.cs.)
        private FileSystemWatcher mHorizonWatcher;
        private System.Windows.Forms.Timer mHorizonReloadDebounce;
        private const int HorizonReloadDebounceMs = 500;

        public MainForm()
        {
            InitializeComponent();
            TimePicker.Format = DateTimePickerFormat.Time;
            TimePicker.ShowUpDown = true;
            TimePicker.Format = DateTimePickerFormat.Custom;
            TimePicker.CustomFormat = "  hh:mm tt";

            // Hidden-when-idle: load paths and the chart pipeline both flip
            // Visible=true at the start of work and back to false after the
            // 1-second hold. Designer leaves the bar Visible by default; this
            // line establishes the boot-idle state. The auto-load image-library
            // scan kicked off later in MainForm_Shown surfaces the bar via
            // BeginScanProgress, so first-paint still shows progress.
            ProgressBar_MultiTargetProcessing.Visible = false;

            // Disable Aero visual styles on the progress bar so Value setter
            // is instant -- under default visual styles, ProgressBar animates
            // smoothly between Value sets over ~500 ms internally, and rapid
            // chart-pipeline ticks (0 -> max in tens of ms) leave the visible
            // bar lagging behind the setter. By the time the hold-then-reset
            // fires, the visible animation has only reached ~40 % of the
            // journey to max, then reverses. SetWindowTheme(handle, " ", " ")
            // is the documented workaround -- the bar renders classic-style
            // with no animation, and the visible state matches Value setter
            // immediately on every tick.
            SetWindowTheme(ProgressBar_MultiTargetProcessing.Handle, " ", " ");

            mAppSettings = SettingsStore.Load();

            mObservation = ObservationMoment.Now(TimeZoneInfo.Local);
            mLocation = PickStartupLocation();
            // Per-site user preferences (target floor + minimum duration) for the
            // boot location. PickStartupPreferences mirrors PickStartupLocation --
            // both resolve from the same NamedSite entry so the spinner
            // values, the chart horizon line, and the fits cache key all start
            // consistent with the persisted shape.
            mPlanningPreferences = PickStartupPreferences();
            // Polyline horizon for the boot location, if the matching NamedSite
            // carries a LocalHorizonPath. Null result falls back to the scalar floor path
            // through SnapshotCurrent. Looked up by name against mAppSettings.NamedLocations
            // since PickStartupLocation returns a Location with no path reference.
            mLocalHorizon = LoadLocalHorizonForCurrentLocation();
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

            UpdateLocalDateTimeEvents();
            InitializeDynamicControls();

            // Show the running version in the title bar so the user can read it without
            // opening About. Stripped of any build-metadata suffix (the +sha that MinVer
            // attaches for dev builds).
            Text = "TargetPlanner v" + GetDisplayVersion();

            // File menu: extend the Designer-resident "File" menu with a Defaults
            // submenu. Edit launches the OS-default editor on settings.json (no
            // auto-reload; user restarts TP to pick up changes). Clear is a
            // factory-reset gesture -- wipes settings.json + filters.json +
            // local-targets.json + Logs/ after confirmation, then exits the app
            // so the next launch boots from PersonalDefaults.BuildSeedSettings.
            var defaultsItem    = new ToolStripMenuItem("&User Defaults");
            var editItem        = new ToolStripMenuItem("&Edit settings.json (add/remove)");
            var clearItem       = new ToolStripMenuItem("&Clear All (reset all defaults)");
            editItem.Click  += (s, e) => HandleEditDefaultsClick();
            clearItem.Click += (s, e) => HandleClearDefaultsClick();
            defaultsItem.DropDownItems.Add(editItem);
            defaultsItem.DropDownItems.Add(clearItem);
            FileToolStripMenuItem_MainForm.DropDownItems.Add(defaultsItem);

            // Help and Filters top-level menu items + the Help children
            // (Check for Updates / About) are now Designer-resident -- see
            // HelpToolStripMenuItem_MainForm + FiltersToolStripMenuItem_MainForm
            // + CheckUpdatesToolStripMenuItem + AboutToolStripMenuItem in
            // MainForm.Designer.cs. Click handlers wire to OnCheckUpdatesClick /
            // OnAboutClick below. Filters children (one per library filter) stay
            // dynamic and get populated by BuildFiltersMenu().

            // Help -> Feedback: appended programmatically rather than via the
            // Designer (which is don't-touch per project convention). Tooltip
            // surfaces the Ctrl+N observation feature for discoverability.
            // Single child opens the Logs folder in Explorer so the user can
            // delete a session's notes (tp.log + screenshots/) with one
            // selection or attach the captured PNGs to a bug report.
            HelpToolStripMenuItem_MainForm.DropDownItems.Add(new ToolStripSeparator());
            var feedbackItem = new ToolStripMenuItem("&Feedback")
            {
                ToolTipText =
                    "Open the Logs folder containing diagnostic logs and screenshots " +
                    "captured via Ctrl+N (the observation dialog). The dialog lets you " +
                    "tick what you observed + write notes + auto-attaches a screenshot " +
                    "and current planner state. One delete of the Logs folder clears " +
                    "every captured note.",
            };
            var openNotesItem = new ToolStripMenuItem("&Open Notes Folder");
            openNotesItem.Click += (s, e) => HandleOpenNotesFolderClick();
            feedbackItem.DropDownItems.Add(openNotesItem);
            HelpToolStripMenuItem_MainForm.DropDownItems.Add(feedbackItem);

            // Filters menu: load the per-filter library (or ship-defaults on first launch)
            // and build a mutually-exclusive radio group of menu items. Disabled is the
            // first-launch default; a click on any preset writes the active filter
            // into mActiveFilter and triggers the coordinator pipeline so the universal
            // hide-on-no-fit refresh propagates the new avoidance regime to every
            // sub-chart.
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
            mToolTip.SetToolTip(Button_VisibleTargets, VisibleButtonTooltipText);

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
            // Defaults > Edit and Defaults > Clear both set mSuppressFormClosingSave
            // before triggering Application.Exit() so the user's hand-edits (Edit)
            // or freshly-deleted file (Clear) aren't overwritten by an exit-time
            // save of TP's in-memory state.
            if (!mSuppressFormClosingSave)
            {
                // Commit any pending NumericUpDown / TextBox edits the user typed
                // but hasn't yet blurred. WinForms NumericUpDown's text-to-Value
                // conversion happens in its OnLeave override -- triggered by focus
                // loss, NOT by Validating. ValidateChildren fires Validating only,
                // so it's not enough; we need actual focus loss on the currently
                // focused control. Setting ActiveControl = null synchronously fires
                // Leave / LostFocus on the previously-active control, which forces
                // NumericUpDown to parse Text -> Value, which fires ValueChanged ->
                // the spinner handler -> PersistPlanningPreferencesToActiveSite.
                // ValidateChildren follows as defence for any custom Validating
                // handlers, but the Active->null is what fixes the typed-not-blurred
                // case.
                ActiveControl = null;
                ValidateChildren();
                SettingsStore.Save(mAppSettings);
            }

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

            mFilterAutoSaveDebounce?.Stop();
            mFilterAutoSaveDebounce?.Dispose();

            mCheckedToggleDebounce?.Stop();
            mCheckedToggleDebounce?.Dispose();

            mHorizonWatcher?.Dispose();
            mHorizonReloadDebounce?.Stop();
            mHorizonReloadDebounce?.Dispose();
        }

        public void InitializeDynamicControls()
        {
            // Wire the dupe-set tint callback. The DupeAwareCheckedListBox owns
            // the paint path (CheckedListBox swallows the standard DrawItem event),
            // so we expose the dupe-color lookup as a Func and the listbox calls
            // it on every row paint.
            CheckedListBox_SelectedTargets.RowBackground = GetDupeRowBackground;
            CheckedListBox_SelectedTargets.CheckboxInteriorTint = GetCheckboxInteriorTint;

            // Right-click on the listbox clears the Visible-tonight tint set.
            CheckedListBox_SelectedTargets.MouseDown += OnSelectedTargetsMouseDown;

            // Accept Explorer drag-drop of .json / .xisf files and target folders
            // (any mix) onto the list -- see GetDroppedTargets.
            CheckedListBox_SelectedTargets.AllowDrop = true;
            CheckedListBox_SelectedTargets.DragEnter += OnTargetListDragEnter;
            CheckedListBox_SelectedTargets.DragDrop += OnTargetListDragDrop;

            // Up/Down on DatePicker = +/-1 day with natural month/year cascade
            // (DateTime.AddDays handles the rollover). Replaces the default
            // WinForms field-wrap behavior where Up on day=31 wraps to day=01
            // of the same month. Use the dropdown calendar for big jumps.
            DatePicker.KeyDown += DatePicker_KeyDown;

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

            // Wire ComboBox_TimeZone: bind the full list of system zones and route the
            // user's pick through OnLocationEdited so the location combo flips to "Custom"
            // and the cache invalidation debounce restarts, matching Bortle/Extinction.
            // DataSource is set ONCE at boot -- a sorted snapshot of TimeZoneInfo.GetSystemTimeZones()
            // (~140 entries on Windows, DisplayName-formatted as "(UTC-05:00) Eastern Time (US & Canada)").
            // The resolved TimeZoneInfo carries DST rules, so consumer-side ConvertTime* calls
            // (chart axes, picker, moon labels) are DST-aware across ST<->DST transitions
            // without any caller-side date arithmetic.
            ComboBox_TimeZone.DisplayMember = "DisplayName";
            ComboBox_TimeZone.ValueMember   = "Id";
            ComboBox_TimeZone.DataSource    = TimeZoneInfo.GetSystemTimeZones().ToList();
            ComboBox_TimeZone.SelectedIndexChanged += ComboBox_TimeZone_SelectedIndexChanged;

            // Populate ComboBox_Location from settings, select the startup location, then
            // push mLocation's values into the lat/lon/N/W/Horizon/Duration inputs.
            ComboBox_Location.SelectedIndexChanged -= ComboBox_Location_SelectionIndexChanged;
            ComboBox_Location.Items.Clear();
            foreach (NamedSite nl in mAppSettings.NamedLocations)
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
            mCache = new TargetPlanner.Caches.ChartCacheStore(mLocation, mObservation.Utc);

            // Phase 4 LC2 sub-charts. Indexed by area name so MainForm dispatches
            // picker / spinner / debounce / Graph-click traffic via foreach + dict
            // lookup. Sky also keeps a typed reference (mLC2Sky) for K-S quirks
            // (ActiveFilterCenterNm + ActiveFilterBandwidthNm properties +
            // RefreshSkyBrightness) that aren't on the IAltitudeSubChart interface.
            mLC2Sky = new Charts.AltitudeSubChart_Sky();
            mLC2Day = new Charts.AltitudeSubChart_Day();
            mSubCharts = new System.Collections.Generic.Dictionary<string, Charts.IAltitudeSubChart>(System.StringComparer.Ordinal)
            {
                ["Day"]      = mLC2Day,
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

            // Day-chart placement-strategy radios (Floor / Meridian / Wall) live
            // inside the Day sub-chart's plot area. The radio CheckedChanged
            // fires this event; route through the coordinator's snapshot pipeline
            // so the new DayMode reaches the active sub-chart through the single
            // Render seam (no cache change -- the mode is a pure visibility
            // filter on top of NightFit.CenteredFloor).
            mLC2Day.DayChartModeChanged += (s, e) =>
            {
                mDayChartMode = mLC2Day.Mode;
                mCoordinator?.Apply(SnapshotCurrent());
            };

            // Initial form sizing so an empty LC2 chart's plot area sits at the
            // ChartLayout.FixedPlotAreaHeight position even before any Graph click.
            // All sub-charts share the same ChartFixedHeight + empty legend at boot,
            // so seeding from any of them is equivalent.
            ResizeAltitudeChartArea(mSubCharts["Day"].IdealHeight);

            // Construct the coordinator after both mCache and mSubCharts exist
            // (it captures references via the resolver delegates). The
            // post-apply hook is the one place that runs side-effects which
            // don't fit Render:
            //   - Astrometry labels (dawn/dusk/sun/moon altitude/phase/illumination).
            //   - Now-line position on every sub-chart (date/time scrubs that
            //     don't trigger a Render still need the red line to move).
            //   - Horizon-line position on every sub-chart (horizon scrubs that
            //     would otherwise need a full Render just to move the green line).
            //   - Sky's K-S brightness re-walk (Bortle/Extinction/Filter scrubs).
            mCoordinator = new TargetPlanner.State.ChartCoordinator(
                cache: mCache,
                renderActiveArea: RenderArea,
                defaultProgressFactory: CreateChartProgress,
                postApplyHook: (ctx, eval) =>
                {
                    RefreshAstrometryLabels();
                    foreach (var sc in mSubCharts.Values)
                    {
                        sc.UpdateNowLine(ctx.Observation.Utc);
                        // Horizon line tracks the user's TargetFloor spinner -- a UI
                        // affordance for the scalar knob, not the LocalHorizon polyline
                        // (which can dip below the floor and drive per-azimuth fit
                        // decisions in the cache instead).
                        sc.UpdateHorizonLine(ctx.Policy.TargetFloorDeg);
                    }
                    // First ChartEvaluation flag consumer: skip the K-S re-walk
                    // when no K-S input changed since the last Apply. Sky's Render
                    // owns the K-S walk inline when Sky is the active sub-chart,
                    // so this hook is only load-bearing for "Sky not active +
                    // BrightnessInputs changed" (keep Sky's series current in the
                    // background so a later switch to Sky doesn't show a stale
                    // flash). Date/Location/Targets/Hdm scrubs without a
                    // BrightnessInputs change either get a fresh walk from Sky's
                    // Render (if Sky is active) or get a fresh walk the next
                    // time Sky activates (Render is the authoritative refresh).
                    if (eval.BrightnessInputsChanged) PushSkyKSInputs(ctx);
                });

            // Baseline paint: fire one Apply with an empty target list so the chart
            // area paints its non-target scaffolding (axis labels, dusk/dawn gradient,
            // moon overlay on Day) instead of staying blank-gray at boot. Empty targets
            // is the key -- the chart's target curves stay absent until the user
            // explicitly checks a target or clicks Button_Graph, which keeps the
            // "rendered targets == user intent" rule intact across every code path.
            // Cheap: EnsureAsync with no targets prepares moon altitudes only, no
            // per-target work.
            mCoordinator.Apply(SnapshotCurrent(Array.Empty<Target>()));

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

            // Fire-and-forget. Startup auto-loads the image library (no fallback
            // browse, no dialog -- a missing/empty root just logs and boots empty).
            _ = GetImageLibraryTargets(offerFallbackBrowse: false);

            // Wire VM bindings after the CoordinateInput helpers and the sub-chart dict exist.
            // The ComboBox starts blank; OnVmKnownTargetsChanged populates it after NINA
            // load completes (~100 ms) and auto-selects the first sorted target.
            WireSelectionVm();

            // Seed the hand-typed local targets (local-targets.json) into the
            // known set up front. Loads now ADD rather than replace, so locals no
            // longer need re-merging on every NINA load -- one seed here makes
            // them visible from boot. AddKnownTargets fires KnownTargetsChanged,
            // which OnVmKnownTargetsChanged turns into a listbox/combo populate.
            if (mLocalTargets.Count > 0)
                mSelection.AddKnownTargets(mLocalTargets);

            // Local-horizon hot-reload + initial label sync. The Button_BrowseHorizon
            // and Label_HorizonPath controls themselves are Designer-managed; this just
            // wires the FileSystemWatcher debounce timer and seeds the path label from
            // the startup site's NamedSite.LocalHorizonPath.
            InitializeLocalHorizonControls();
        }

        private void UpdateLocalDateTimeEvents()
        {
            DateTime local = DatePicker.Value.Date + TimePicker.Value.TimeOfDay;
            TimeZoneInfo zone = mLocation?.TimeZoneInfo ?? TimeZoneInfo.Local;
            mObservation = ObservationMoment.FromLocal(local, zone);
            RefreshAstrometryLabels();
        }

        // Push every astrometry-derived label. Reads NightWindow from the cache
        // (single source of truth for dusk / dawn / illumination); falls back
        // to NightCalculator.ComputeNight when the cache is still cold (early
        // form-init before mCache is constructed). The five remaining values
        // (sun altitude, moon altitude / phase, moon rise / set) are computed
        // inline -- one-shot ~150 us of Meeus per call, cheap enough to fire on
        // every spinner tick. Called from UpdateLocalDateTimeEvents (date/time
        // scrubs), OnLocationEdited (lat/lon/N/W/elevation spinners),
        // ComboBox_Location_SelectionIndexChanged (preset picks), and the
        // coordinator's post-apply hook.
        private void RefreshAstrometryLabels()
        {
            DateTime utc = mObservation.Utc;
            TimeZoneInfo zone = mObservation.Zone;
            NightWindow night = mCache?.LocationNightCache?.Starting
                             ?? NightCalculator.ComputeNight(mLocation, utc);
            double latSigned = mLocation.LatSigned();
            double lonEast   = mLocation.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, mLocation.Elevation);

            double sunAlt = SunPosition.AltAzAt(mLocation, utc).Altitude;
            double moonAlt = AstroUtil.GetMoonAltitude(utc, observer);
            string moonPhase = AstroUtil.GetMoonPhaseName(utc);
            // Bracket-by-night so the displayed rise/set match the chart's
            // dusk->dawn window. GetMoonRiseAndSet (UTC-calendar-day search)
            // returned the prior local evening's set for non-UTC observers --
            // see Library AstroUtil.GetMoonRiseAndSetForNight remarks.
            RiseAndSetEvent moonRs = AstroUtil.GetMoonRiseAndSetForNight(
                night.AstronomicalDusk, night.AstronomicalDawn,
                latSigned, lonEast, mLocation.Elevation);

            Label_AstronomicalDuskValue.Text = FormatZoned(night.AstronomicalDusk, zone);
            Label_AstronomicalDawnValue.Text = FormatZoned(night.AstronomicalDawn, zone);
            Label_SunAltitudeValue.Text = sunAlt.ToString("F0") + "\u00B0";
            Label_LunarAltitudeValue.Text = moonAlt.ToString("F0") + "\u00B0";
            Label_LunarIlluminationFractionValue.Text = (night.LunarIlluminationFraction * 100).ToString("F0") + "%";
            Label_LunarPhaseValue.Text = moonPhase;
            Label_MoonRiseValue.Text = FormatZoned(moonRs.Rise, zone);
            Label_MoonSetValue.Text  = FormatZoned(moonRs.Set,  zone);
        }

        // Format a UTC instant as a wall-clock short time in the observer's zone.
        // "--:--" placeholder for the no-event case (polar summer Sun events;
        // moon below the horizon for the whole bracket-night search window) so
        // the label doesn't silently read "12:00 AM" from a MinValue sentinel.
        private static string FormatZoned(DateTime? utc, TimeZoneInfo zone)
            => utc.HasValue ? FormatZoned(utc.Value, zone) : "--:--";

        private static string FormatZoned(DateTime utc, TimeZoneInfo zone)
            => utc == DateTime.MinValue
                ? "--:--"
                : TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToShortTimeString();

        // Coordinate-input callbacks (OnLatitudeEdited / OnLongitudeEdited /
        // OnRightAscensionEdited / OnDeclinationEdited) plus ComboTextOrFallback
        // helper plus SyncLocationUIFromModel / SyncTargetUIFromModel live in
        // Forms/Presenters/MainForm.CoordinatePresenter.cs as a partial-class
        // file split. See that file for the implementation.

        // Filter-library / moon-avoidance methods (BuildFiltersMenu,
        // BuildFiltersGroupBox, IdentifierSafe, OnFilterDefaultsClick,
        // RefreshFilterMenuLabels, OpenEditFiltersDialog,
        // RefreshActiveFilterAfterDialogSave, SetActiveFilter,
        // OnAvoidanceEnableChanged, OnLorentzianControlChanged,
        // BuildProfileFromControls, RestartFilterAutoSaveDebounce,
        // FilterAutoSaveDebounce_Tick, IndexOfActiveFilter,
        // WriteProfileToControls, SetLorentzianControlsEnabled) live in
        // Forms/Presenters/MainForm.FilterMenuPresenter.cs as a partial-class
        // file split. See that file for the implementation.

        // Help -> Check for Updates... handler. Wired to CheckUpdatesToolStripMenuItem
        // in MainForm.Designer.cs.
        private async void OnCheckUpdatesClick(object sender, EventArgs e)
        {
            Log.Diag("UI", "Menu Help.CheckUpdates.Click");
            await UpdateService.CheckManuallyAsync(this);
        }

        // Help -> About TargetPlanner handler. Wired to AboutToolStripMenuItem in
        // MainForm.Designer.cs.
        private void OnAboutClick(object sender, EventArgs e)
        {
            Log.Diag("UI", "Menu Help.About.Click");
            using (var dlg = new AboutDialog())
                dlg.ShowDialog(this);
        }

        // Help -> Feedback -> Open Notes Folder. Ensures the Logs folder
        // exists (it doesn't until the first Log.Append fires after rotation)
        // so the user always gets a real Explorer window rather than a
        // path-not-found error. Process.Start with UseShellExecute=true
        // hands the path off to the OS shell, which opens the default folder
        // viewer (Explorer on Windows).
        private void HandleOpenNotesFolderClick()
        {
            Log.Diag("UI", "Menu Help.Feedback.OpenNotesFolder.Click");
            try
            {
                string path = Log.NotesFolderPath;
                System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Error("HandleOpenNotesFolderClick failed", ex);
                MessageBox.Show(this,
                    "Couldn't open the notes folder:\n\n" + ex.Message,
                    "Open Notes Folder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // File -> Defaults -> Edit settings.json. Closes TP after launching the
        // OS-default editor so an exit-time SettingsStore.Save can't clobber the
        // user's hand-edits.
        //
        // Sequence:
        //   1. Confirm prompt (cancellable -- user can back out before commitment).
        //   2. Flush current in-memory AppSettings to settings.json so the editor
        //      opens the freshest view of TP's state.
        //   3. Launch the editor (Process.Start UseShellExecute=true so Windows
        //      resolves the .json association).
        //   4. Set mSuppressFormClosingSave + Application.Exit. The user edits at
        //      leisure and relaunches TP to load their changes.
        private void HandleEditDefaultsClick()
        {
            Log.Diag("UI", "Menu File.Defaults.Edit.Click");
            DialogResult confirm = MessageBox.Show(this,
                "Open settings.json in your default editor?\n\n" +
                "TargetPlanner will close so your edits save cleanly.\n" +
                "Relaunch TargetPlanner when you're done editing.",
                "Edit settings.json",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            if (confirm != DialogResult.OK) return;

            try
            {
                // Flush current in-memory state so the editor sees TP's latest
                // view -- e.g. a site swap since boot that hadn't been persisted
                // by a different code path. Defensive; most save call-sites
                // already keep settings.json current.
                SettingsStore.Save(mAppSettings);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsStore.FilePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Error("EditDefaults: failed to open '" + SettingsStore.FilePath + "'", ex);
                MessageBox.Show(this,
                    "Could not open the editor.\n\n" + ex.Message,
                    "Edit settings.json",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            mSuppressFormClosingSave = true;
            Application.Exit();
        }

        // File -> Defaults -> Clear (factory reset)... Confirms via YesNo, deletes
        // settings.json + filters.json + local-targets.json + the Logs/ directory
        // recursively, then exits the application so the next launch boots from
        // PersonalDefaults.BuildSeedSettings(). tp.log is part of Logs/ so it goes
        // last (the per-file deletes log their failures first). Exit is forced --
        // the user explicitly asked for a reset; reading old in-memory state
        // through subsequent saves would partially undo the wipe.
        private void HandleClearDefaultsClick()
        {
            Log.Diag("UI", "Menu File.Defaults.Clear.Click");
            string body =
                "Factory reset TargetPlanner?\n\n" +
                "This deletes:\n" +
                "  - " + SettingsStore.FilePath + "\n" +
                "  - " + FilterLibrary.DefaultPath + "\n" +
                "  - " + LocalTargetStore.FilePath + "\n" +
                "  - " + Log.NotesFolderPath + " (entire folder: tp.log + screenshots + .prev)\n\n" +
                "TargetPlanner will close after the reset; relaunch to boot from defaults.\n\n" +
                "This cannot be undone.";

            DialogResult confirm = MessageBox.Show(this, body, "Defaults: Clear (factory reset)",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            TryDeleteFile(SettingsStore.FilePath);
            TryDeleteFile(FilterLibrary.DefaultPath);
            TryDeleteFile(LocalTargetStore.FilePath);
            TryDeleteDirectory(Log.NotesFolderPath);

            // Confirm prompt above already told the user TP will close; skip a
            // second "Reset complete" dialog and just exit. Suppress flag stops
            // FormClosing from re-saving settings.json over the just-deleted one.
            mSuppressFormClosingSave = true;
            Application.Exit();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Error("ClearDefaults: failed to delete '" + path + "'", ex);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                    System.IO.Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Error("ClearDefaults: failed to delete directory '" + path + "'", ex);
            }
        }


        private void DatePicker_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"DatePicker.ValueChanged value={DatePicker.Value:yyyy-MM-dd}");
            UpdateLocalDateTimeEvents();
            // Immediate now-line update for live feedback during scrub. Coordinator's
            // post-apply hook re-runs UpdateNowLine on settle (cheap; just shifts a
            // section's X position).
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mObservation.Utc);
            // Transit / Rise sort keys are time-dependent; Name is not. Skip the re-sort on
            // Name to avoid a pointless Items.Clear+re-add round-trip on every scrub tick.
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
            // Coordinator: a date change trips the cache's mLastSetUtc diff ->
            // SetLocationAsync -> full cache rebuild -> Render with fresh moon
            // series, dusk/dawn, altitudes, and Tonight fits. Date-unchanged
            // scrubs (TimePicker within the same UTC day) skip the rebuild and
            // just bounce through the post-apply hook for label + now-line sync.
            mCoordinator?.Apply(SnapshotCurrent());
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"TimePicker.ValueChanged value={TimePicker.Value:HH:mm}");
            UpdateLocalDateTimeEvents();
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mObservation.Utc);
            if (ComboBox_SortTargets != null && ComboBox_SortTargets.SelectedIndex > 0)
                ResortSelectedTargets();
            mCoordinator?.Apply(SnapshotCurrent());
        }

        // Plain Up/Down on the DatePicker = +/-1 day with natural cascade across
        // month/year boundaries (DateTime.AddDays). Setting Value programmatically
        // fires ValueChanged which routes through Apply(SnapshotCurrent()) so the
        // chart refreshes with the new date. Modifier keys (Shift/Ctrl/Alt + arrow)
        // pass through to the default WinForms handler.
        private void DatePicker_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.None) return;
            int delta;
            if (e.KeyCode == Keys.Up) delta = 1;
            else if (e.KeyCode == Keys.Down) delta = -1;
            else return;
            Log.Diag("UI", $"DatePicker.KeyDown key={e.KeyCode} delta={delta}d");
            DatePicker.Value = DatePicker.Value.AddDays(delta);
            e.Handled = true;
            e.SuppressKeyPress = true;
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
        // that reads mLocation / mObservation / mPlanningPreferences /
        // mActiveFilter / CheckBox_Moon_AvoidanceEnable / SelectedArea() /
        // mTargetColorsByTarget — adding a new chart input is one record-field
        // addition here plus one additional read here, not a signature break
        // across six files. Caller decides which target list to pass
        // (single-target via SelectedSingle, multi via the checked set, or empty
        // for blanking).
        //
        // PlanningPolicy is synthesized from mPlanningPreferences plus the active
        // filter + moon-avoidance master toggle + mLocalHorizon. The polyline
        // horizon (when configured + loaded for the active named location)
        // composes with the scalar floor via MaxOfHorizonProfile so a target
        // qualifies only when it clears whichever of the two is higher at the
        // target's azimuth. Scalar-only sites skip the combinator and pass a
        // bare ScalarHorizonProfile.
        private ChartContext SnapshotCurrent(IReadOnlyList<Target> targets)
        {
            ScalarHorizonProfile scalar = new ScalarHorizonProfile(mPlanningPreferences.TargetFloorDeg);
            IHorizonProfile horizon = mLocalHorizon == null
                ? scalar
                : new MaxOfHorizonProfile(mLocalHorizon, scalar);
            bool moonAvoidanceEnabled = CheckBox_Moon_AvoidanceEnable != null
                                     && CheckBox_Moon_AvoidanceEnable.Checked;
            PlanningPolicy policy = new PlanningPolicy(
                TargetFloorDeg:        mPlanningPreferences.TargetFloorDeg,
                MinDuration:           mPlanningPreferences.MinDuration,
                ActiveFilter:          mActiveFilter,
                MoonAvoidanceEnabled:  moonAvoidanceEnabled,
                LocalHorizon:          horizon);

            return new ChartContext(
                Location:     mLocation,
                Targets:      targets ?? Array.Empty<Target>(),
                Policy:       policy,
                Observation:  mObservation,
                ActiveArea:   SelectedArea(),
                TargetColors: mTargetColorsByTarget,
                DayMode:      mDayChartMode);
        }

        // Snap the observation moment back to the current wall-clock time. Replaces the
        // prior Now/SetDateTime/Hold trio plus the 5-second polling timer with a single
        // explicit user action: set mLocalDateTime to now, push into the pickers (without
        // re-triggering their ValueChanged), refresh every label via UpdateLocalDateTime-
        // Events, and reposition the chart's red now-line to the current X coordinate.
        private void Button_Now_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_Now.Click");
            TimeZoneInfo zone = mLocation?.TimeZoneInfo ?? TimeZoneInfo.Local;
            mObservation = ObservationMoment.Now(zone);
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(mObservation.Utc, zone);

            DatePicker.ValueChanged -= DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;
            DatePicker.Value = localNow;
            TimePicker.Value = localNow;
            DatePicker.ValueChanged += DatePicker_ValueChanged;
            TimePicker.ValueChanged += TimePicker_ValueChanged;

            UpdateLocalDateTimeEvents();

            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(mObservation.Utc);

            mCoordinator?.Apply(SnapshotCurrent());
        }

        private static decimal ClampToRange(NumericUpDown spinner, decimal value)
        {
            if (value < spinner.Minimum) return spinner.Minimum;
            if (value > spinner.Maximum) return spinner.Maximum;
            return value;
        }
        // Target-loading methods -- the Load/Browse button handlers, the
        // image-library / NINA-.json / type-detecting-browse orchestration, the
        // pure-loader wrappers, the fallback folder pickers, and StartCacheWarmup
        // -- live in Forms/Presenters/MainForm.TargetLoadingPresenter.cs as a
        // partial-class file split. See that file for the implementation.

        // Sort-and-populate methods (SortedTargets, ResortSelectedTargets,
        // PopulateTargetComboFromTargets, PopulateCheckedListBoxFromTargets,
        // TargetRow + TargetForRow, ComboBox_SortTargets_SelectedIndexChanged)
        // live in Forms/Presenters/MainForm.SortPresenter.cs as a partial-class
        // file split. See that file for the implementation.

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
        }

        // Ctrl+N opens (or focuses) the user-observation dialog. Modeless +
        // TopMost so the user can interact with the main UI while the dialog
        // stays open; USER_OBS_START / USER_OBS_END / USER_OBS_CANCEL
        // markers in tp.log bracket the user's actions chronologically.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N))
            {
                Forms.UserObservationDialog.ShowOrFocus(this, GetObservationContext);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Builds the context snapshot string included in the USER_OBS_END
        // line. Called by the observation dialog at OK time so the report
        // carries the planner state without the user having to type it.
        private string GetObservationContext()
        {
            try
            {
                IReadOnlyList<Target> targets =
                    mCoordinator?.LastAppliedTargets ?? Array.Empty<Target>();
                return string.Format(
                    "area={0}, obs={1:yyyy-MM-dd HH:mm}Z, n={2}, H={3:F0}, D={4:F0}m, filter={5}, Bortle={6}, K={7:F2}",
                    SelectedArea() ?? "?",
                    mObservation.Utc,
                    targets.Count,
                    mPlanningPreferences.TargetFloorDeg,
                    mPlanningPreferences.MinDuration.TotalMinutes,
                    mActiveFilter == null
                        ? "(none)"
                        : string.Format("{0}@{1:F0}/{2:F0}nm",
                                        mActiveFilter.Name, mActiveFilter.CenterNm, mActiveFilter.BandwidthNm),
                    mLocation.BortleClass,
                    mLocation.ExtinctionK);
            }
            catch (Exception ex)
            {
                Log.Warn("GetObservationContext threw", ex);
                return "context unavailable";
            }
        }

        // Merge the current SelectedSingle (combo's resolved target -- could be a
        // NINA-known target or a transient one built from RA/Dec spinner edits) into
        // the checked set. Transient targets are added to KnownTargets and persisted
        // to the local-targets.json sidecar so they survive form-close + NINA reload.
        private void Button_AddTarget_Click(object sender, EventArgs e)
        {
            Target t = mSelection?.SelectedSingle;
            Log.Diag("UI", $"Button_AddTarget.Click target={t?.Name ?? "<null>"}");
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
            Log.Diag("UI", $"Button_RemoveTarget.Click target={t?.Name ?? "<null>"}");
            if (t == null) { ShowTransientMessage("No Target"); return; }

            bool wasInLocal = mLocalTargets.Remove(t);
            mSelection.RemoveKnownTarget(t);
            if (wasInLocal) LocalTargetStore.Save(mLocalTargets);

            // Re-sort listbox + combo by the current ComboBox_SortTargets selection
            // so the survivor list stays in canonical order after the deletion.
            ResortSelectedTargets();
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

        // Coordinator-side default progress factory: builds a fresh
        // Progress<T> per Apply with closure-captured (gen, claimed) state.
        // Progress<T> captures SynchronizationContext.Current at construction
        // -- called from the UI-thread coordinator -- so Report callbacks
        // marshal back to the UI thread even when the cache ticks from
        // CacheAxis.PrepareAsync's TaskScheduler.Default ContinueWith
        // (ThreadPool). Shared mChartBuildGeneration with BeginScanProgress
        // so load paths and chart pipelines mutually invalidate stale
        // callbacks -- one operation owns the bar at a time.
        //
        // Behavior: first Report with Total > 0 claims the bar (Value=0,
        // Maximum=Total, Visible=true) and stamps mBarOwnerGen so the
        // deferred hide can tell whether a follow-on pipeline has stolen
        // ownership. Subsequent Reports advance Value monotonically (stale
        // ticks from a slower path can't regress). On Done >= Maximum the
        // closure schedules a 200 ms hold-then-hide via Task.Delay so the
        // bar is visibly at 100 % before disappearing -- without the hold,
        // WinForms doesn't paint between Value=max and Visible=false in the
        // same handler invocation, so the user never sees the full bar.
        // The hide bails if ownership has moved to a newer pipeline, which
        // resolves the two takeover quirks that killed the prior 1 s hold:
        // a cold follow-on can claim the bar mid-hold without being
        // clobbered, and a warm follow-on still gets the hide (since the
        // outgoing pipeline retained ownership through to its delayed hide).
        private const int ProgressBarHoldMs = 1000;

        // Disables Aero visual styles for a single control. Passing " " (a
        // single space) for both subAppName and subIdList is the documented
        // pattern. We use this on the ProgressBar to suppress the smooth-
        // animation behaviour that lags behind rapid Value sets; see the
        // SetWindowTheme call in the MainForm constructor for the rationale.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private IProgress<(int Done, int Total)> CreateChartProgress()
        {
            int gen = ++mChartBuildGeneration;
            bool claimed = false;
            TaskScheduler uiSched = TaskScheduler.FromCurrentSynchronizationContext();
            return new Progress<(int Done, int Total)>(value =>
            {
                if (gen != mChartBuildGeneration) return;   // superseded
                if (value.Total <= 0) return;                // no work signal
                int max = Math.Max(1, value.Total);
                if (!claimed)
                {
                    // Fresh take-over: reset Value/Visible from whatever the
                    // previous pipeline left behind so a stale 100% fill
                    // doesn't ride forward through the monotonic guard.
                    // mBarOwnerGen stamp lets a previous pipeline's deferred
                    // hide notice we've taken over and bail.
                    claimed = true;
                    mBarOwnerGen = gen;
                    ProgressBar_MultiTargetProcessing.Minimum = 0;
                    ProgressBar_MultiTargetProcessing.Maximum = max;
                    ProgressBar_MultiTargetProcessing.Value   = 0;
                    ProgressBar_MultiTargetProcessing.Visible = true;
                }
                else if (ProgressBar_MultiTargetProcessing.Maximum != max)
                {
                    ProgressBar_MultiTargetProcessing.Maximum = max;
                }
                int clamped = Math.Min(Math.Max(0, value.Done), max);
                if (clamped > ProgressBar_MultiTargetProcessing.Value)
                    ProgressBar_MultiTargetProcessing.Value = clamped;
                if (clamped >= max)
                {
                    Task.Delay(ProgressBarHoldMs).ContinueWith(_ =>
                    {
                        // Hide only if we still own the bar; a newer pipeline
                        // that claimed in the meantime will manage its own
                        // hide (or its own takeover reset will already have
                        // happened).
                        if (mBarOwnerGen != gen) return;
                        mBarOwnerGen = 0;
                        ProgressBar_MultiTargetProcessing.Value   = 0;
                        ProgressBar_MultiTargetProcessing.Visible = false;
                    }, uiSched);
                }
            });
        }

        // Load-path progress (Browse / Load / drag-drop). The scanner discovers
        // the Total after file enumeration so the first Report sizes Maximum;
        // pair with FinishScanProgress for the fill + 1-second hold + reset.
        // Shares mChartBuildGeneration with the chart pipeline's progress sink
        // (CreateChartProgress) so a chart click mid-scan -- or a load mid-build
        // -- invalidates the other's stale callbacks.
        private (int generation, IProgress<(int Done, int Total)> progress) BeginScanProgress()
        {
            int thisGeneration = ++mChartBuildGeneration;

            ProgressBar_MultiTargetProcessing.Minimum = 0;
            ProgressBar_MultiTargetProcessing.Maximum = 1;   // resized on first Total
            ProgressBar_MultiTargetProcessing.Value   = 0;
            ProgressBar_MultiTargetProcessing.Visible = true;

            var progress = new Progress<(int Done, int Total)>(t =>
            {
                if (thisGeneration != mChartBuildGeneration) return;  // stale
                int max = Math.Max(1, t.Total);
                if (ProgressBar_MultiTargetProcessing.Maximum != max)
                    ProgressBar_MultiTargetProcessing.Maximum = max;
                int clamped = Math.Min(Math.Max(0, t.Done), max);
                if (clamped > ProgressBar_MultiTargetProcessing.Value)
                    ProgressBar_MultiTargetProcessing.Value = clamped;
            });

            return (thisGeneration, progress);
        }

        // Load-path finish: fill bar to Maximum, hold 1 s, clear + hide.
        // Generation-guarded so a superseding chart pipeline or fresh load
        // doesn't get its bar clobbered by the trailing reset.
        private void FinishScanProgress(int generation)
        {
            if (generation != mChartBuildGeneration) return;
            ProgressBar_MultiTargetProcessing.Value = ProgressBar_MultiTargetProcessing.Maximum;
            Task.Delay(1000).ContinueWith(
                _ =>
                {
                    if (generation != mChartBuildGeneration) return;
                    ProgressBar_MultiTargetProcessing.Value = 0;
                    ProgressBar_MultiTargetProcessing.Visible = false;
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

            Astronomy.Core.Night.NightWindow night =
                Astronomy.Core.Night.NightCalculator.ComputeNight(mLocation, tonightObs.Utc);

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
