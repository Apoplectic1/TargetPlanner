using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TargetPlanner.Settings;
using TargetPlanner.Support;
using System.Threading.Tasks;
using LocalLib;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    public partial class MainForm : Form
    {
        private OpenFolderDialog mFolder;

        private Location mLocation;
        private (DateTime When, TimeZoneInfo Zone) mLocalDateTime;

        private Target mTarget;
        private List<Target> mTargetList;

        private const string NinaTargetsRootPath = @"E:\Photography\Astro Photography\Captures\Nina\Targets";

        private Charts.AltitudeChart mAltitudeChart;

        private ToolTip mToolTip;
        private int mToolTipIndex;

        // Dedicated ToolTip instance for the Optimal radio button. Kept separate from mToolTip
        // because its AutoPopDelay must be much longer (the explanatory text runs several
        // paragraphs) and mToolTip's ShowCheckBoxObjectToolTip handler resets AutoPopDelay to
        // 5 seconds on every CheckedListBox hover -- globals wouldn't stick.
        private ToolTip mOptimalRadioTooltip;

        private const string OptimalRadioTooltipText =
@"All three curves describe the best imaging session windows available
on each night of the next year bounded by Local Horizon and Duration
hours.  They answer three different planning questions:

Ceiling Window — Answers: ""What is the highest altitude reached for
Duration hours above the Local Horizon?"".

This is the highest altitude reached inside any above-horizon window
that's long enough to image for Duration hours. It's the target's
ceiling for that night.


Floor Window — Answers: ""What is the lowest altitude reached for
Duration hours above the Local Horizon?"".

This is the lowestest altitude reached inside any above-horizon window
that's long enough to image for Duration hours. It's the target's
floor for that night.

This window is transit-centered when duration fits inside the
above-horizon window. If not, the window is pushed against whichever
wall is closest to the Meridian.


Symmetric Floor Window — Answers: ""Can I image this target
symetrically around the Meridian?"".

When possible, this curve is present. When not possible (night too
short, transit too close to dusk/dawn, etc.), the curve is removed.


On an ideal night, all three curves bunch together near zenith; the
Symetric matchies Floor because the best window IS the symetric one.

On a marginal night, the Ceiling is still decent but Floor drops and
Symetric disappears entirely.

When Floor and Symetric coincide, transit falls comfortably inside the
night and a centered Duration hour session fits. When they diverge,
the best-placed session is asymmetric: Floor shows you the practical
achievable floor and Symetric shows the floor you could have with
symmetry at the cost of a lower minimum altitude.";

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
            mTarget = Target.Default;
            mTargetList = new List<Target>();

            mUIState = new UIState();

            UpdateUI();
            UpdateLocalDateTimeEvents();
            InitializeDynamicControls();
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
            Panel_AltitudeChart.Size = new Size(GroupBox_AltitudeChart.Width - 20, GroupBox_AltitudeChart.Height - 50);
            Panel_AltitudeChart.Name = "Panel_Mschart";
            Panel_AltitudeChart.BackColor = Color.FromArgb(255, 128, 128, 128);
            GroupBox_AltitudeChart.Controls.Add(Panel_AltitudeChart);

            // Add actual Altitude Chart to Panel
            mAltitudeChart = new Charts.AltitudeChart(mLocation);
            mAltitudeChart.mChart.Location = new Point(5, 5);
            mAltitudeChart.mChart.Size = new Size(Panel_AltitudeChart.Width - 10, Panel_AltitudeChart.Size.Height - 10);
            mAltitudeChart.mChart.BackColor = Color.FromArgb(255, 239, 235, 233);

            mAltitudeChart.AddChartAreaToList("Day");
            mAltitudeChart.AddChartAreaToList("Year");
            mAltitudeChart.AddChartAreaToList("Optimal");
            mAltitudeChart.AddToTargetList(mTarget);
            mAltitudeChart.BuildTargetSeriesList();
            mAltitudeChart.ShowChartAreaSeries("Day");


            mAltitudeChart.ChartTitle = FormatChartTitle("Day");
            mAltitudeChart.UIState(mUIState);
            mAltitudeChart.AddLegend();
            mAltitudeChart.UpdateNowLine(mLocalDateTime.When);


            mAltitudeChart.Legend = true;

            Panel_AltitudeChart.Controls.Add(mAltitudeChart.mChart);

            // Fire-and-forget; GetNinaTargets owns its own try/catch for diagnostics.
            _ = GetNinaTargets(folderSelectedPaths);

            ComboBox_SelectTarget.Text = "M31";
        }

        private void UpdateUI()
        {
            CheckBox_LocalNorth.Checked = mLocation.North;
            CheckBox_LocalWest.Checked = mLocation.West;
            TextBox_Latitude.Text = mLocation.Latitude.ToString("F6");
            TextBox_Longitude.Text = mLocation.Longitude.ToString("F6");

            CheckBox_TargetNorth.Checked = mTarget.North;
            TextBox_RightAscension.Text = mTarget.RightAscension.ToString("F6");
            TextBox_Declination.Text = mTarget.Declination.ToString("F6");
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
            if (mTarget == null) return;
            mTarget = mTarget.With(rightAscension: Math.Round(mRaInput.Magnitude, 6));
        }

        private void OnDeclinationEdited(object sender, EventArgs e)
        {
            if (mTarget == null) return;
            mTarget = mTarget.With(
                declination: Math.Round(mDecInput.Magnitude, 6),
                north:       mDecInput.Positive);
        }

        private void NumericUpDown_Duration_ValueChanged(object sender, EventArgs e)
        {
            TimeSpan newDuration = TimeSpan.FromMinutes((double)NumericUpDown_Duration.Value * 60.0);
            mLocation = mLocation.With(duration: newDuration);
            if (mAltitudeChart == null) return;
            // Pass the scrubbed value explicitly; the chart's snapshot keeps its Graph-click
            // Horizon / Duration but the rendered curve follows the spinner live.
            mAltitudeChart.RebuildOptimalData(mLocation.Horizon, newDuration);
        }

        private void NumericUpDown_Horizon_ValueChanged(object sender, EventArgs e)
        {
            double newHorizon = (double)NumericUpDown_Horizon.Value;
            mLocation = mLocation.With(horizon: newHorizon);
            if (mAltitudeChart == null) return;
            mAltitudeChart.UpdateHorizonLines(newHorizon);
            mAltitudeChart.RebuildOptimalData(newHorizon, mLocation.Duration);
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

        private void Button_GraphTarget_Click(object sender, EventArgs e)
        {
            foreach (Target target in mTargetList)
            {
                if (target.Name == ComboBox_SelectTarget.Text)
                {
                    mTarget = target;
                    break;
                }
            }

            // mLocation.DateTime is already kept in sync with the pickers via
            // UpdateLocalDateTimeEvents (called from DatePicker/TimePicker ValueChanged and
            // Button_Now_Click). Don't overwrite it with DateTime.Now here -- that was the
            // pre-refactor assumption when the app was always "live now" by default.
            IProgress<string> phaseProgress = BeginChartBuildProgress(targetCount: 1);

            // Reload-in-place: keep the Chart control, its ChartAreas, its Legend, and any
            // user zoom / legend-color-toggle state alive. The chart was constructed once
            // in InitializeDynamicControls. Reload resets only the transient state (series,
            // strip lines, per-target AltitudeSeries cache, target list, Location snapshot).
            mAltitudeChart.ReloadWithTargets(mLocation, new[] { mTarget }, phaseProgress);

            // Snap the radio button state to Day so the UI and the active chart area agree.
            // Setting Checked=true only fires CheckedChanged if the value actually changes,
            // so we unconditionally run ShowChartAreaSeries + ChartTitle after to cover the
            // "radio already on Day" path.
            RadioButton_Day.Checked = true;
            mAltitudeChart.ShowChartAreaSeries("Day");
            mAltitudeChart.ChartTitle = FormatChartTitle("Day");
            mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
        }

        // Graph every checked target from CheckedListBox_SelectedTargets on the embedded
        // chart. Mirrors Button_GraphTarget but with N targets instead of one -- progress-bar
        // Maximum scales with the count, and ReloadWithTargets renders them all at once.
        private void Button_GraphAllTargets_Click(object sender, EventArgs e)
        {
            if (mTargetList == null || mTargetList.Count == 0) return;

            // Walk CheckedItems in display order so mAltitudeChart's target list -- and
            // therefore the chart legend -- inherits the CheckedListBox's NaturalStringComparer
            // sort (see GetNinaTargets). Iterating mTargetList here instead would have used
            // folder-load order, which is effectively arbitrary. CheckedItems is filtered to
            // the checked subset but preserves the full list's ordering.
            var checkedTargets = new List<Target>();
            foreach (object item in CheckedListBox_SelectedTargets.CheckedItems)
            {
                string name = item.ToString();
                Target t = mTargetList.Find(x => x.Name == name);
                if (t != null) checkedTargets.Add(t);
            }
            if (checkedTargets.Count == 0) return;

            IProgress<string> phaseProgress = BeginChartBuildProgress(targetCount: checkedTargets.Count);

            mAltitudeChart.ReloadWithTargets(mLocation, checkedTargets, phaseProgress);

            RadioButton_Day.Checked = true;
            mAltitudeChart.ShowChartAreaSeries("Day");
            mAltitudeChart.ChartTitle = FormatChartTitle("Day");
            mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
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

        private void Button_ClearTarget_Click(object sender, EventArgs e)
        {
            ComboBox_SelectTarget.Text = "M31";
            CheckBox_TargetNorth.Checked = true;
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

                NumericUpDown_Horizon.ValueChanged  -= NumericUpDown_Horizon_ValueChanged;
                NumericUpDown_Duration.ValueChanged -= NumericUpDown_Duration_ValueChanged;
                NumericUpDown_Horizon.Value  = ClampToRange(NumericUpDown_Horizon,  (decimal)mLocation.Horizon);
                NumericUpDown_Duration.Value = ClampToRange(NumericUpDown_Duration, (decimal)mLocation.Duration.TotalHours);
                NumericUpDown_Horizon.ValueChanged  += NumericUpDown_Horizon_ValueChanged;
                NumericUpDown_Duration.ValueChanged += NumericUpDown_Duration_ValueChanged;
            }
            finally { mSyncingLocationUI = false; }
        }

        // Push mTarget into the RA / Dec coordinate inputs. No equivalent guard flag because
        // OnRightAscensionEdited / OnDeclinationEdited don't have the combo-flip side effect
        // that SyncLocationUIFromModel has to suppress; SetProgrammatic already skips
        // ValueChanged.
        private void SyncTargetUIFromModel()
        {
            if (mTarget == null) return;
            mRaInput.SetProgrammatic(mTarget.RightAscension, positive: true);
            mDecInput.SetProgrammatic(mTarget.Declination,    positive: mTarget.North);
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
                _ = GetNinaTargets(mFolder.SelectedPaths);
            }
        }

        private async Task GetNinaTargets(string[] folderSelectedPaths)
        {
            mTargetList.Clear();
            CheckedListBox_SelectedTargets.Items.Clear();
            ComboBox_SelectTarget.Items.Clear();

            var progressHandler = new Progress<(int Current, int Total)>(value =>
            {
                ProgressBar_ProcessObject.Maximum = value.Total;
                ProgressBar_ProcessObject.Value = value.Current;
            });

            var progress = progressHandler as IProgress<(int Current, int Total)>;

            ProgressBar_ProcessObject.Value = 0;

            try
            {
                foreach (string folder in folderSelectedPaths)
                {
                    List<Target> loaded = null;
                    await Task.Run(() =>
                    {
                        loaded = TargetPlanner.Nina.TargetLoader.Load(folder, progress);
                    });

                    if (loaded != null) mTargetList.AddRange(loaded);
                    ProgressBar_ProcessObject.Value = ProgressBar_ProcessObject.Maximum;
                }
            }
            catch (Exception ex)
            {
                // Log the full exception (stack + type) before surfacing a shorter user-facing
                // message; the bare catch used to swallow the stack trace entirely.
                System.Diagnostics.Debug.WriteLine($"GetNinaTargets failed: {ex}");
                MessageBox.Show(ex.Message, "Target load failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ProgressBar_ProcessObject.Value = 0;
            }

            // Designer property Sorted=false; we feed the list in whatever ordering
            // ComboBox_SortTargets currently selects (defaults to Name / NaturalStringComparer).
            PopulateCheckedListBoxFromTargets(defaultChecked: true);

            if (mTargetList.Count == 0) return;

            foreach (Target t in mTargetList)
            {
                ComboBox_SelectTarget.Items.Add(t.Name);
            }
        }

        // Clears CheckedListBox_SelectedTargets and re-adds every target from mTargetList in
        // the currently-selected sort order, with each row set to defaultChecked. Used on a
        // fresh target-list load where there's no prior check state to preserve; the
        // ResortSelectedTargets path handles re-ordering with state preservation.
        private void PopulateCheckedListBoxFromTargets(bool defaultChecked)
        {
            CheckedListBox_SelectedTargets.BeginUpdate();
            try
            {
                CheckedListBox_SelectedTargets.Items.Clear();
                foreach (Target t in SortedTargets(mTargetList))
                {
                    CheckedListBox_SelectedTargets.Items.Add(t.Name, defaultChecked);
                }
            }
            finally
            {
                CheckedListBox_SelectedTargets.EndUpdate();
            }
        }

        // Re-order CheckedListBox_SelectedTargets in place using the current ComboBox_SortTargets
        // mode, preserving each row's check state across the rebuild. Called from the sort-mode
        // ComboBox, from the picker ValueChanged handlers when the active mode is time-dependent,
        // and internally after anything that changes the list's membership. No-ops when the list
        // is empty or mTargetList isn't populated yet (defensive for form-init event ordering).
        private void ResortSelectedTargets()
        {
            if (CheckedListBox_SelectedTargets.Items.Count == 0) return;
            if (mTargetList == null || mTargetList.Count == 0) return;

            // Snapshot check state keyed by name so reordering preserves each row.
            var checkStates = new Dictionary<string, CheckState>(StringComparer.Ordinal);
            for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
            {
                string n = CheckedListBox_SelectedTargets.Items[i].ToString();
                checkStates[n] = CheckedListBox_SelectedTargets.GetItemCheckState(i);
            }

            // Resolve the currently-displayed item names back to Target instances. Unchecked
            // entries stay in the list -- this is reordering, not filtering.
            var displayed = new List<Target>();
            foreach (object item in CheckedListBox_SelectedTargets.Items)
            {
                string n = item.ToString();
                Target t = mTargetList.Find(x => x.Name == n);
                if (t != null) displayed.Add(t);
            }

            CheckedListBox_SelectedTargets.BeginUpdate();
            try
            {
                CheckedListBox_SelectedTargets.Items.Clear();
                foreach (Target t in SortedTargets(displayed))
                {
                    CheckState cs = checkStates.TryGetValue(t.Name, out var state)
                        ? state : CheckState.Unchecked;
                    CheckedListBox_SelectedTargets.Items.Add(t.Name, cs);
                }
            }
            finally
            {
                CheckedListBox_SelectedTargets.EndUpdate();
            }

            // Reorder the chart legend to match, in place -- no replot, no recompute. Series
            // objects (Points, Color, Tag, ToolTip) are preserved; only their index in
            // mChart.Series changes, which drives the legend row order.
            if (mAltitudeChart != null && mAltitudeChart.Targets.Count > 0)
            {
                mAltitudeChart.ReorderTargets(SortedTargets(mAltitudeChart.Targets));
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
            ResortSelectedTargets();
        }

        // Double-click a target row to open its source NINA .json (Target.Directory is the
        // full file path, populated by TargetLoader). Uses ShellExecute via Process.Start so
        // whatever the user has registered for .json (Notepad, VS Code, etc.) handles it.
        private void CheckedListBox_SelectedTargets_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = CheckedListBox_SelectedTargets.IndexFromPoint(e.Location);
            if (index < 0) return;

            string name = CheckedListBox_SelectedTargets.Items[index].ToString();
            Target found = mTargetList.Find(x => x.Name == name);
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
            Target found = mTargetList.Find(x => x.Name == name);
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
        // Generation guarding: each call bumps mChartBuildGeneration and captures its value.
        // If the user clicks Graph again before the prior build's Task.Run completes, the
        // stale callbacks compare-mismatch and no-op -- the new click's bar stays truthful.
        private IProgress<string> BeginChartBuildProgress(int targetCount)
        {
            int thisGeneration = ++mChartBuildGeneration;

            ProgressBar_MultiTargetProcessing.Minimum = 0;
            ProgressBar_MultiTargetProcessing.Maximum = Math.Max(1, targetCount * 3);
            ProgressBar_MultiTargetProcessing.Value   = 0;

            return new Progress<string>(_ =>
            {
                if (thisGeneration != mChartBuildGeneration) return;  // stale -- a newer click superseded us
                if (ProgressBar_MultiTargetProcessing.Value < ProgressBar_MultiTargetProcessing.Maximum)
                    ProgressBar_MultiTargetProcessing.Value += 1;
            });
        }

        private void ComboBox_SelectTarget_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Validate-then-assign: if the Find returns null (combobox text doesn't match any
            // loaded target), leave mTarget pointing at the previous valid target. Assigning
            // null to mTarget and bailing would leak a null through to BuildTargetSeriesList ->
            // SeriesFor(null) -> ArgumentNullException on the next Graph click.
            string selectedTargetName = ComboBox_SelectTarget.Text;
            Target found = mTargetList.Find(t => t.Name == selectedTargetName);
            if (found == null) return;

            mTarget = found;
            SyncTargetUIFromModel();
        }

        private void Button_ClearAllTargets_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
                CheckedListBox_SelectedTargets.SetItemCheckState(i, CheckState.Unchecked);
        }

        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {

            for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
                CheckedListBox_SelectedTargets.SetItemCheckState(i, CheckState.Checked);
        }

        // Check exactly the targets that have a contiguous window of at least
        // NumericUpDown_Duration above NumericUpDown_Horizon during the night bracketing
        // the moment picked in DatePicker + TimePicker. NightCalculator's bracket logic
        // walks forward to tomorrow's dawn or back to yesterday's dusk depending on
        // whether the moment is past today's dawn -- so a 2 AM TimePicker value yields
        // the night that's currently in progress, while a 10 PM value yields the night
        // just starting. Matches the rest of the form's
        // "DatePicker.Value.Date + TimePicker.Value.TimeOfDay" pattern.
        private void Button_VisibleTonight_Click(object sender, EventArgs e)
        {
            if (mTargetList == null || mTargetList.Count == 0) return;
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

            for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
            {
                string name = CheckedListBox_SelectedTargets.Items[i].ToString();
                Target target = mTargetList.Find(t => t.Name == name);
                bool visible = target != null
                    && Astronomy.Core.Session.CoarseVisibility.IsAboveHorizonForAtLeast(
                        target, pickedNightLocation, night, horizon,
                        pickedNightLocation.Duration);
                CheckedListBox_SelectedTargets.SetItemCheckState(
                    i, visible ? CheckState.Checked : CheckState.Unchecked);
            }
        }
    }
}
