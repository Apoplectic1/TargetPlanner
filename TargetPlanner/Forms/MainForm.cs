using System;
using System.Collections.Generic;
using System.Drawing;
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

            Label_SelectedTargetNumber.Text = "None";

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
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SettingsStore.Save(mAppSettings);

            // Dispose long-lived resources the form owns. Without this, the ToolTip leaks
            // a native handle.
            mToolTip?.Dispose();
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
            mAltitudeChart.UpdateNowLine(DateTime.Now);


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
            Label_SunAltitudeValue.Text = Astrometry.SunAltitude.ToString("F1");
            Label_LunarAltitudeValue.Text = Astrometry.LunarAltitude.ToString("F1");
            Label_LunarIlluminationFractionValue.Text = (Astrometry.LunarIlluminationFraction * 100).ToString("F0");
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
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            UpdateLocalDateTimeEvents();
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

            mLocation = mLocation.With(dateTime: DateTime.Now);

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
            mAltitudeChart.UpdateNowLine(DateTime.Now);
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

            foreach (Target t in mTargetList)
            {
                CheckedListBox_SelectedTargets.Items.Add(t.Name, true);
            }

            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedTargets.Items.Count.ToString();

            if (mTargetList.Count == 0) return;

            foreach (Target t in mTargetList)
            {
                ComboBox_SelectTarget.Items.Add(t.Name);
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


            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedTargets.Items.Count.ToString();
        }

        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {

            for (int i = 0; i < CheckedListBox_SelectedTargets.Items.Count; i++)
                CheckedListBox_SelectedTargets.SetItemCheckState(i, CheckState.Checked);

            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedTargets.Items.Count.ToString();

        }
    }
}
