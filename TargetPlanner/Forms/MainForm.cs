using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Astronomy.Core.Horizons;
using Astronomy.Core.Time;
using Astronomy.NINA.Persistence;
using TargetPlanner.Filters;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Updates;
using System.Threading;

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
            ProgressBar_Processing.Visible = false;

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
            // Defensive wrap regardless so any bug in the call path can't escape into the
            // WinForms unhandled-exception filter and terminate the process.
            Shown += async (s, e) =>
            {
                try { await UpdateService.CheckOnStartupAsync(this); }
                catch (Exception ex) { Log.Error("Shown update-check threw", ex); }
            };
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

            // Chart coordinator boot wiring -- bodies live in
            // Forms/Presenters/MainForm.ChartCoordinatorPresenter.cs. Call order
            // is load-bearing: wire DayChartModeChanged onto mLC2Day, then seed
            // the chart panel size, then construct mCoordinator (needs mCache +
            // mSubCharts), then fire the empty-targets baseline paint.
            WireDayChartModeChanged();
            ResizeAltitudeChartArea(mSubCharts["Day"].IdealHeight);
            ConstructCoordinator();
            FireBaselinePaint();

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

        // RefreshAstrometryLabels + the FormatZoned formatters live in
        // Forms/Presenters/MainForm.AstrometryLabelsPresenter.cs.

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

        // App-meta menu handlers (Help: Updates / About / Feedback; File >
        // Defaults: Edit / Clear) plus the TryDelete* statics live in
        // Forms/Presenters/MainForm.AppMenuPresenter.cs.

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

        // Add/Remove target click handlers live in
        // Forms/Presenters/MainForm.TargetLoadingPresenter.cs alongside the
        // other target-lifecycle paths (Load / Browse / drag-drop).

        // Transient notice popup (mTransientNotice / mTransientLabel /
        // mTransientTimer fields + ShowTransientMessage) lives in
        // Forms/Presenters/MainForm.TransientNoticePresenter.cs.

        // Progress-bar plumbing (mChartBuildGeneration / mBarOwnerGen fields,
        // ProgressBarHoldMs const, CreateChartProgress / BeginScanProgress /
        // FinishScanProgress) lives in Forms/Presenters/MainForm.ProgressBarPresenter.cs.

        // Selection-command handlers (ComboBox_SelectTarget + the three "all"
        // buttons + Visible-Tonight) live in
        // Forms/Presenters/MainForm.SelectionCommandsPresenter.cs. Distinct
        // from SelectionVmPresenter, which owns the bidirectional VM <-> UI
        // sync only.
    }
}
