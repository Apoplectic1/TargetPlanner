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
        private System.Timers.Timer mTimer;
        private OpenFolderDialog mFolder;

        private Location mLocation;
        private (DateTime When, TimeZoneInfo Zone) mLocalDateTime;

        private Target mTarget;
        private List<Target> mTargetList;

        private const string NinaTargetsRootPath = @"E:\Photography\Astro Photography\Captures\Nina\Targets";

        private Charts.AltitudeChartForm mAltitudeChartForm;
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


            mTimer = new System.Timers.Timer();
            mTimer.Interval = 5000;
            // Fire OnTimedEvent on the UI thread via this Form as the synchronizing object.
            // Previously the Elapsed handler ran on a thread-pool thread and had to Invoke
            // back to the UI thread for every label update, and its direct writes to
            // mLocation.DateTime / .TimeZoneInfo raced with UI-thread reads.
            mTimer.SynchronizingObject = this;
            mTimer.Enabled = !CheckBox_HoldTime.Checked;
            mTimer.Elapsed += OnTimedEvent;

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

            // Dispose long-lived resources the form owns. Without this, the Timer keeps
            // firing on the thread pool even after the form closes, the ToolTip leaks a
            // native handle, and any still-open target-list popup stays alive in memory.
            mTimer?.Stop();
            mTimer?.Dispose();
            mToolTip?.Dispose();
            mAltitudeChart?.Dispose();
            mAltitudeChartForm?.Dispose();
        }

        public void InitializeDynamicControls()
        {
            string[] folderSelectedPaths = { NinaTargetsRootPath };

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

            // Flip the location combo to "Custom" the moment the user changes a geographic
            // field (lat / lon / N / W). Horizon and Duration are analysis preferences that
            // are independent of location identity -- editing them does not rename the
            // selected location. These handlers sit alongside the per-field handlers already
            // wired in the Designer; the mSyncingLocationUI guard keeps programmatic syncs
            // from tripping them.
            NumericUpDown_LatitudeDegrees.ValueChanged  += OnLocationEdited;
            NumericUpDown_LatitudeMinutes.ValueChanged  += OnLocationEdited;
            NumericUpDown_LatitudeSeconds.ValueChanged  += OnLocationEdited;
            NumericUpDown_LongitudeDegrees.ValueChanged += OnLocationEdited;
            NumericUpDown_LongitudeMinutes.ValueChanged += OnLocationEdited;
            NumericUpDown_LongitudeSeconds.ValueChanged += OnLocationEdited;
            TextBox_Latitude.TextChanged                += OnLocationEdited;
            TextBox_Longitude.TextChanged               += OnLocationEdited;
            CheckBox_LocalNorth.CheckedChanged          += OnLocationEdited;
            CheckBox_LocalWest.CheckedChanged           += OnLocationEdited;

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


            mAltitudeChart.ChartTitle = "Altitude at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
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

        // ---------- Latitude ----------
        private void UpdateLatitudeTextBox(object sender, EventArgs e)
        {
            double latitude;

            ScrollNumericLocationCounters();

            latitude = (double)NumericUpDown_LatitudeDegrees.Value + (double)NumericUpDown_LatitudeMinutes.Value / 60.0 + (double)NumericUpDown_LatitudeSeconds.Value / 3600.0;

            mLocation = mLocation.With(latitude: Math.Round(latitude, 6));

            TextBox_Latitude.TextChanged -= TextBox_Latitude_TextChanged;
            TextBox_Latitude.Text = mLocation.Latitude.ToString("F6");
            TextBox_Latitude.TextChanged += TextBox_Latitude_TextChanged;
        }

        private void TextBox_Latitude_TextChanged(object sender, EventArgs e)
        {
            bool status;
            double latitude;


            if (System.Text.RegularExpressions.Regex.IsMatch(TextBox_Latitude.Text, "  ^ [0-9]"))
            {
                TextBox_Latitude.Text = "";
                return;
            }

            status = Double.TryParse(TextBox_Latitude.Text, out latitude);

            if (status)
            {
                if (latitude <= 180.0)
                {
                    mLocation = mLocation.With(latitude: Math.Round(latitude, 6));
                    TextBox_Latitude.Text = mLocation.Latitude.ToString("F6");

                    CheckBox_LocalNorth.Checked = mLocation.North;

                    NumericUpDown_LatitudeDegrees.ValueChanged -= UpdateLatitudeTextBox;
                    NumericUpDown_LatitudeMinutes.ValueChanged -= UpdateLatitudeTextBox;
                    NumericUpDown_LatitudeSeconds.ValueChanged -= UpdateLatitudeTextBox;

                    NumericUpDown_LatitudeDegrees.Value = (decimal)mLocation.LatDegrees;
                    NumericUpDown_LatitudeMinutes.Value = (decimal)mLocation.LatMinutes;
                    NumericUpDown_LatitudeSeconds.Value = (decimal)mLocation.LatSeconds;

                    NumericUpDown_LatitudeDegrees.ValueChanged += UpdateLatitudeTextBox;
                    NumericUpDown_LatitudeMinutes.ValueChanged += UpdateLatitudeTextBox;
                    NumericUpDown_LatitudeSeconds.ValueChanged += UpdateLatitudeTextBox;

                    mLocation = mLocation.With(north: CheckBox_LocalNorth.Checked, west: CheckBox_LocalWest.Checked);
                    mTarget = mTarget.With(north: CheckBox_TargetNorth.Checked);
                }
            }
        }

        // ---------- Longitude ----------
        private void UpdateLongitudeTextBox(object sender, EventArgs e)
        {
            double longitude;

            ScrollNumericLocationCounters();

            longitude = (double)NumericUpDown_LongitudeDegrees.Value + (double)NumericUpDown_LongitudeMinutes.Value / 60.0 + (double)NumericUpDown_LongitudeSeconds.Value / 3600.0;

            mLocation = mLocation.With(longitude: Math.Round(longitude, 6));

            TextBox_Longitude.TextChanged -= TextBox_Longitude_TextChanged;
            TextBox_Longitude.Text = mLocation.Longitude.ToString("F6");
            TextBox_Longitude.TextChanged += TextBox_Longitude_TextChanged;
        }

        private void TextBox_Longitude_TextChanged(object sender, EventArgs e)
        {
            bool status;
            double longitude;

            if (System.Text.RegularExpressions.Regex.IsMatch(TextBox_Longitude.Text, "  ^ [0-9]"))
            {
                TextBox_Longitude.Text = "";
                return;
            }

            status = Double.TryParse(TextBox_Longitude.Text, out longitude);

            if (status)
            {
                if (longitude <= 90.0)
                {
                    mLocation = mLocation.With(longitude: Math.Round(longitude, 6));
                    TextBox_Longitude.Text = mLocation.Longitude.ToString("F6");

                    CheckBox_LocalWest.Checked = mLocation.West;

                    NumericUpDown_LongitudeDegrees.ValueChanged -= UpdateLongitudeTextBox;
                    NumericUpDown_LongitudeMinutes.ValueChanged -= UpdateLongitudeTextBox;
                    NumericUpDown_LongitudeSeconds.ValueChanged -= UpdateLongitudeTextBox;

                    NumericUpDown_LongitudeDegrees.Value = (decimal)mLocation.LonDegrees;
                    NumericUpDown_LongitudeMinutes.Value = (decimal)mLocation.LonMinutes;
                    NumericUpDown_LongitudeSeconds.Value = (decimal)mLocation.LonSeconds;

                    NumericUpDown_LongitudeDegrees.ValueChanged += UpdateLongitudeTextBox;
                    NumericUpDown_LongitudeMinutes.ValueChanged += UpdateLongitudeTextBox;
                    NumericUpDown_LongitudeSeconds.ValueChanged += UpdateLongitudeTextBox;

                    mLocation = mLocation.With(north: CheckBox_LocalNorth.Checked, west: CheckBox_LocalWest.Checked);
                    mTarget = mTarget.With(north: CheckBox_TargetNorth.Checked);
                }
            }
        }

        // ---------- Right Ascension ----------
        private void UpdateRightAscensionTextBox(object sender, EventArgs e)
        {
            TimeSpan raTimeSpanHours;
            double milliseconds;

            ScrollNumericLocationCounters();

            milliseconds = (int)(1000.0m * (NumericUpDown_RaSeconds.Value - Math.Floor(NumericUpDown_RaSeconds.Value)));
            raTimeSpanHours = new TimeSpan(0, (int)NumericUpDown_RaHours.Value, (int)NumericUpDown_RaMinutes.Value, (int)NumericUpDown_RaSeconds.Value, (int)milliseconds);

            mTarget = mTarget.With(rightAscension: Math.Round(raTimeSpanHours.TotalHours, 6));

            TextBox_RightAscension.TextChanged -= TextBox_RightAscension_TextChanged;
            TextBox_RightAscension.Text = mTarget.RightAscension.ToString("F6");
            TextBox_RightAscension.TextChanged += TextBox_RightAscension_TextChanged;
        }

        private void TextBox_RightAscension_TextChanged(object sender, EventArgs e)
        {
            double raHours;
            bool status;

            if (mTarget == null) return;

            if (System.Text.RegularExpressions.Regex.IsMatch(TextBox_RightAscension.Text, "  ^ [0-9]"))
            {
                TextBox_RightAscension.Text = "";
                return;
            }

            status = Double.TryParse(TextBox_RightAscension.Text, out raHours);

            if (status && raHours >= 0.0 && raHours < 24.0)
            {
                mTarget = mTarget.With(rightAscension: Math.Round(raHours, 6));
                TextBox_RightAscension.Text = mTarget.RightAscension.ToString("F6");

                NumericUpDown_RaHours.ValueChanged   -= UpdateRightAscensionTextBox;
                NumericUpDown_RaMinutes.ValueChanged -= UpdateRightAscensionTextBox;
                NumericUpDown_RaSeconds.ValueChanged -= UpdateRightAscensionTextBox;

                NumericUpDown_RaHours.Value   = (decimal)mTarget.RaHours;
                NumericUpDown_RaMinutes.Value = (decimal)mTarget.RaMinutes;
                NumericUpDown_RaSeconds.Value = (decimal)mTarget.RaSeconds;

                NumericUpDown_RaHours.ValueChanged   += UpdateRightAscensionTextBox;
                NumericUpDown_RaMinutes.ValueChanged += UpdateRightAscensionTextBox;
                NumericUpDown_RaSeconds.ValueChanged += UpdateRightAscensionTextBox;

                mLocation = mLocation.With(north: CheckBox_LocalNorth.Checked, west: CheckBox_LocalWest.Checked);
                mTarget = mTarget.With(north: CheckBox_TargetNorth.Checked);
            }
        }

        // ---------- Declination ----------
        private void UpdateDeclinationTextBox(object sender, EventArgs e)
        {
            double declination;

            if (mTarget == null) return;

            ScrollNumericLocationCounters();

            declination = (double)NumericUpDown_DecDegrees.Value + (double)NumericUpDown_DecMinutes.Value / 60.0 + (double)NumericUpDown_DecSeconds.Value / 3600.0;

            mTarget = mTarget.With(declination: Math.Round(declination, 6));

            TextBox_Declination.TextChanged -= TextBox_Declination_TextChanged;
            TextBox_Declination.Text = mTarget.Declination.ToString("F6");
            TextBox_Declination.TextChanged += TextBox_Declination_TextChanged;
        }

        private void TextBox_Declination_TextChanged(object sender, EventArgs e)
        {
            bool status;
            double declination;

            if (System.Text.RegularExpressions.Regex.IsMatch(TextBox_Declination.Text, "  ^ [0-9]"))
            {
                TextBox_Declination.Text = "";
                return;
            }

            status = Double.TryParse(TextBox_Declination.Text, out declination);

            if (status)
            {
                if ((declination <= 90.0) && (declination >= -90.0))
                {
                    mTarget = mTarget.With(declination: Math.Round(Math.Abs(declination), 6));
                    TextBox_Declination.Text = mTarget.Declination.ToString("F6");

                    CheckBox_TargetNorth.Checked = mTarget.North;

                    NumericUpDown_DecDegrees.ValueChanged -= UpdateDeclinationTextBox;
                    NumericUpDown_DecMinutes.ValueChanged -= UpdateDeclinationTextBox;
                    NumericUpDown_DecSeconds.ValueChanged -= UpdateDeclinationTextBox;

                    NumericUpDown_DecDegrees.Value = (decimal)mTarget.DecDegrees;
                    NumericUpDown_DecMinutes.Value = (decimal)mTarget.DecMinutes;
                    NumericUpDown_DecSeconds.Value = (decimal)mTarget.DecSeconds;

                    NumericUpDown_DecDegrees.ValueChanged += UpdateDeclinationTextBox;
                    NumericUpDown_DecMinutes.ValueChanged += UpdateDeclinationTextBox;
                    NumericUpDown_DecSeconds.ValueChanged += UpdateDeclinationTextBox;

                    mLocation = mLocation.With(north: CheckBox_LocalNorth.Checked, west: CheckBox_LocalWest.Checked);
                    mTarget = mTarget.With(north: CheckBox_TargetNorth.Checked);
                }
            }
        }

        public void ScrollNumericLocationCounters()
        {
            // Latitude
            if (NumericUpDown_LatitudeSeconds.Value == 60.0m)
            {
                decimal minutes;
                NumericUpDown_LatitudeSeconds.Value = 0m;
                minutes = NumericUpDown_LatitudeMinutes.Value + 1.0m;
                NumericUpDown_LatitudeMinutes.Value = minutes;
            }

            if (NumericUpDown_LatitudeSeconds.Value == -0.01m)
            {
                decimal minutes;
                NumericUpDown_LatitudeSeconds.Value = 59.99m;
                minutes = NumericUpDown_LatitudeMinutes.Value - 1.0m;
                NumericUpDown_LatitudeMinutes.Value = minutes;
            }

            if (NumericUpDown_LatitudeMinutes.Value == 60.0m)
            {
                decimal degrees;
                NumericUpDown_LatitudeMinutes.Value = 0;
                degrees = NumericUpDown_LatitudeDegrees.Value + 1.0m;
                NumericUpDown_LatitudeDegrees.Value = degrees;
            }

            if (NumericUpDown_LatitudeMinutes.Value == -1m)
            {
                decimal degrees;
                NumericUpDown_LatitudeMinutes.Value = 59.0m;
                degrees = NumericUpDown_LatitudeDegrees.Value - 1.0m;
                NumericUpDown_LatitudeDegrees.Value = degrees;
            }

            // Longitude
            if (NumericUpDown_LongitudeSeconds.Value == 60.0m)
            {
                decimal minutes;
                NumericUpDown_LongitudeSeconds.Value = 0m;
                minutes = NumericUpDown_LongitudeMinutes.Value + 1.0m;
                NumericUpDown_LongitudeMinutes.Value = minutes;
            }

            if (NumericUpDown_LongitudeSeconds.Value == -0.01m)
            {
                decimal minutes;
                NumericUpDown_LongitudeSeconds.Value = 59.99m;
                minutes = NumericUpDown_LongitudeMinutes.Value - 1.0m;
                NumericUpDown_LongitudeMinutes.Value = minutes;
            }

            if (NumericUpDown_LongitudeMinutes.Value == 60.0m)
            {
                decimal degrees;
                NumericUpDown_LongitudeMinutes.Value = 0;
                degrees = NumericUpDown_LongitudeDegrees.Value + 1.0m;
                NumericUpDown_LongitudeDegrees.Value = degrees;
            }

            if (NumericUpDown_LongitudeMinutes.Value == -1m)
            {
                decimal degrees;
                NumericUpDown_LongitudeMinutes.Value = 59.0m;
                degrees = NumericUpDown_LongitudeDegrees.Value - 1.0m;
                NumericUpDown_LongitudeDegrees.Value = degrees;
            }

            // Right Ascension
            if (NumericUpDown_RaSeconds.Value == 60.0m)
            {
                decimal minutes;
                NumericUpDown_RaSeconds.Value = 0m;
                minutes = NumericUpDown_RaMinutes.Value + 1.0m;
                NumericUpDown_RaMinutes.Value = minutes;
            }

            if (NumericUpDown_RaSeconds.Value == -0.01m)
            {
                decimal minutes;
                NumericUpDown_RaSeconds.Value = 59.99m;
                minutes = NumericUpDown_RaMinutes.Value - 1.0m;
                NumericUpDown_RaMinutes.Value = minutes;
            }

            if (NumericUpDown_RaMinutes.Value == 60.0m)
            {
                decimal degrees;
                NumericUpDown_RaMinutes.Value = 0;
                degrees = NumericUpDown_RaHours.Value + 1.0m;
                NumericUpDown_RaHours.Value = degrees;
            }

            if (NumericUpDown_RaMinutes.Value == -1m)
            {
                decimal degrees;
                NumericUpDown_RaMinutes.Value = 59.0m;
                degrees = NumericUpDown_RaHours.Value - 1.0m;
                NumericUpDown_RaHours.Value = degrees;
            }

            // Declination
            if (NumericUpDown_DecSeconds.Value == 60.0m)
            {
                decimal minutes;
                NumericUpDown_DecSeconds.Value = 0m;
                minutes = NumericUpDown_DecMinutes.Value + 1.0m;
                NumericUpDown_DecMinutes.Value = minutes;
            }

            if (NumericUpDown_DecSeconds.Value == -0.01m)
            {
                decimal minutes;
                NumericUpDown_DecSeconds.Value = 59.99m;
                minutes = NumericUpDown_DecMinutes.Value - 1.0m;
                NumericUpDown_DecMinutes.Value = minutes;
            }

            if (NumericUpDown_DecMinutes.Value == 60.0m)
            {
                decimal degrees;
                NumericUpDown_DecMinutes.Value = 0;
                degrees = NumericUpDown_DecDegrees.Value + 1.0m;
                NumericUpDown_DecDegrees.Value = degrees;
            }

            if (NumericUpDown_DecMinutes.Value == -1m)
            {
                decimal degrees;
                NumericUpDown_DecMinutes.Value = 59.0m;
                degrees = NumericUpDown_DecDegrees.Value - 1.0m;
                NumericUpDown_DecDegrees.Value = degrees;
            }
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
            RadioButton_Now.Checked = false;
            RadioButton_SetDateTime.Checked = true;
            UpdateLocalDateTimeEvents();
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZoneInfo.Local);
            RadioButton_Now.Checked = false;
            RadioButton_SetDateTime.Checked = true;
            UpdateLocalDateTimeEvents();
        }

        private void Button_GraphEphemeride_Click(object sender, EventArgs e)
        {
            // Remove the old chart's Control from its parent, then dispose the AltitudeChart
            // (which disposes the underlying Chart, releasing its GDI handles). Without this,
            // every Graph click leaks a Chart control's native resources.
            Panel_AltitudeChart.Controls.Clear();
            mAltitudeChart?.Dispose();

            foreach (Target target in mTargetList)
            {
                if (target.Name == ComboBox_SelectTarget.Text)
                {
                    mTarget = target;
                    break;
                }
            }

            mLocation = mLocation.With(dateTime: DateTime.Now);

            // Add actual Altitude Chart to Panel. The chart captures mLocation as its
            // frozen snapshot at construction; subsequent spinner edits won't leak into
            // the chart's stored Location.
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

            mAltitudeChart.ChartTitle = "Altitude at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            mAltitudeChart.UIState(mUIState);
            mAltitudeChart.AddLegend();
            mAltitudeChart.UpdateNowLine(DateTime.Now);


            mAltitudeChart.Legend = true;

            Panel_AltitudeChart.Controls.Add(mAltitudeChart.mChart);
        }

        private void CheckBox_LocalWest_CheckedChanged(object sender, EventArgs e)
        {
            mLocation = mLocation.With(west: CheckBox_LocalWest.Checked);
        }

        private void CheckBox_HoldTime_CheckedChanged(object sender, EventArgs e)
        {
            mTimer.Enabled = !CheckBox_HoldTime.Checked;
        }
        private void OnTimedEvent(System.Object source, System.Timers.ElapsedEventArgs e)
        {
            // Now on the UI thread courtesy of Timer.SynchronizingObject = this, so the
            // Invoke indirection is no longer needed. Direct mLocation writes + UI updates
            // are safe.
            mLocalDateTime = (DateTime.Now, TimeZoneInfo.Local);
            mLocation = mLocation.With(dateTime: mLocalDateTime.When, timeZoneInfo: mLocalDateTime.Zone);

            DatePicker.ValueChanged -= DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;

            TimePicker.Value = mLocalDateTime.When;
            DatePicker.Value = mLocalDateTime.When;

            DatePicker.ValueChanged += DatePicker_ValueChanged;
            TimePicker.ValueChanged += TimePicker_ValueChanged;

            Label_SunAltitudeValue.Text = Astrometry.SunAltitude.ToString("F1");
            Label_LunarAltitudeValue.Text = Astrometry.LunarAltitude.ToString("F1");

            if (mAltitudeChart != null)
            {
                mAltitudeChart.UpdateNowLine(mLocalDateTime.When);
            }
        }

        private void RadioButton_Now_CheckedChanged(object sender, EventArgs e)
        {
            mLocalDateTime = (DateTime.Now, TimeZoneInfo.Local);
            UpdateUI();
            UpdateLocalDateTimeEvents();

            DatePicker.ValueChanged -= DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;

            TimePicker.Value = DateTime.Now;
            DatePicker.Value = DateTime.Now;

            DatePicker.ValueChanged += DatePicker_ValueChanged;
            TimePicker.ValueChanged -= TimePicker_ValueChanged;
        }

        private void Button_ClearEphemeride_Click(object sender, EventArgs e)
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

        // Push mLocation into the lat / lon / N / W / Horizon / Duration inputs. Unsubscribes
        // per-field handlers while writing so the existing triple-binding (spinners <-> textbox
        // <-> checkbox) doesn't thrash during the sync, and sets mSyncingLocationUI so
        // OnLocationEdited treats the writes as programmatic.
        private void SyncLocationUIFromModel()
        {
            mSyncingLocationUI = true;
            try
            {
                NumericUpDown_LatitudeDegrees.ValueChanged  -= UpdateLatitudeTextBox;
                NumericUpDown_LatitudeMinutes.ValueChanged  -= UpdateLatitudeTextBox;
                NumericUpDown_LatitudeSeconds.ValueChanged  -= UpdateLatitudeTextBox;
                NumericUpDown_LongitudeDegrees.ValueChanged -= UpdateLongitudeTextBox;
                NumericUpDown_LongitudeMinutes.ValueChanged -= UpdateLongitudeTextBox;
                NumericUpDown_LongitudeSeconds.ValueChanged -= UpdateLongitudeTextBox;
                TextBox_Latitude.TextChanged                -= TextBox_Latitude_TextChanged;
                TextBox_Longitude.TextChanged               -= TextBox_Longitude_TextChanged;
                CheckBox_LocalNorth.CheckedChanged          -= CheckBox_LocalNorth_CheckedChanged;
                CheckBox_LocalWest.CheckedChanged           -= CheckBox_LocalWest_CheckedChanged;
                NumericUpDown_Horizon.ValueChanged          -= NumericUpDown_Horizon_ValueChanged;
                NumericUpDown_Duration.ValueChanged         -= NumericUpDown_Duration_ValueChanged;

                CheckBox_LocalNorth.Checked = mLocation.North;
                CheckBox_LocalWest.Checked  = mLocation.West;
                TextBox_Latitude.Text       = mLocation.Latitude.ToString("F6");
                TextBox_Longitude.Text      = mLocation.Longitude.ToString("F6");

                NumericUpDown_LatitudeDegrees.Value  = ClampToRange(NumericUpDown_LatitudeDegrees,  (decimal)mLocation.LatDegrees);
                NumericUpDown_LatitudeMinutes.Value  = ClampToRange(NumericUpDown_LatitudeMinutes,  (decimal)mLocation.LatMinutes);
                NumericUpDown_LatitudeSeconds.Value  = ClampToRange(NumericUpDown_LatitudeSeconds,  (decimal)Math.Round(mLocation.LatSeconds, 2));
                NumericUpDown_LongitudeDegrees.Value = ClampToRange(NumericUpDown_LongitudeDegrees, (decimal)mLocation.LonDegrees);
                NumericUpDown_LongitudeMinutes.Value = ClampToRange(NumericUpDown_LongitudeMinutes, (decimal)mLocation.LonMinutes);
                NumericUpDown_LongitudeSeconds.Value = ClampToRange(NumericUpDown_LongitudeSeconds, (decimal)Math.Round(mLocation.LonSeconds, 2));
                NumericUpDown_Horizon.Value          = ClampToRange(NumericUpDown_Horizon,          (decimal)mLocation.Horizon);
                NumericUpDown_Duration.Value         = ClampToRange(NumericUpDown_Duration,         (decimal)mLocation.Duration.TotalHours);

                NumericUpDown_LatitudeDegrees.ValueChanged  += UpdateLatitudeTextBox;
                NumericUpDown_LatitudeMinutes.ValueChanged  += UpdateLatitudeTextBox;
                NumericUpDown_LatitudeSeconds.ValueChanged  += UpdateLatitudeTextBox;
                NumericUpDown_LongitudeDegrees.ValueChanged += UpdateLongitudeTextBox;
                NumericUpDown_LongitudeMinutes.ValueChanged += UpdateLongitudeTextBox;
                NumericUpDown_LongitudeSeconds.ValueChanged += UpdateLongitudeTextBox;
                TextBox_Latitude.TextChanged                += TextBox_Latitude_TextChanged;
                TextBox_Longitude.TextChanged               += TextBox_Longitude_TextChanged;
                CheckBox_LocalNorth.CheckedChanged          += CheckBox_LocalNorth_CheckedChanged;
                CheckBox_LocalWest.CheckedChanged           += CheckBox_LocalWest_CheckedChanged;
                NumericUpDown_Horizon.ValueChanged          += NumericUpDown_Horizon_ValueChanged;
                NumericUpDown_Duration.ValueChanged         += NumericUpDown_Duration_ValueChanged;
            }
            finally { mSyncingLocationUI = false; }
        }

        private static decimal ClampToRange(NumericUpDown spinner, decimal value)
        {
            if (value < spinner.Minimum) return spinner.Minimum;
            if (value > spinner.Maximum) return spinner.Maximum;
            return value;
        }

        private void CheckBox_TargetNorth_CheckedChanged(object sender, EventArgs e)
        {
            mTarget = mTarget.With(north: CheckBox_TargetNorth.Checked);
        }

        private void CheckBox_LocalNorth_CheckedChanged(object sender, EventArgs e)
        {
            mLocation = mLocation.With(north: CheckBox_LocalNorth.Checked);
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

        private void Button_GraphTargetList_Click(object sender, EventArgs e)
        {

            if (mTargetList == null || mTargetList.Count == 0)
            {
                return;
            }

            Label_SelectedTargetNumber.Text = mTargetList.Count.ToString();
            // Repeat clicks used to accumulate floating popup forms. Close + dispose the
            // prior instance before spawning a new one.
            mAltitudeChartForm?.Close();
            mAltitudeChartForm?.Dispose();
            mAltitudeChartForm = new Charts.AltitudeChartForm();

            mAltitudeChartForm.ChartTitle = "Altitude at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            mAltitudeChartForm.AstronomicalDawn = Astrometry.AstronomicalDawn;
            mAltitudeChartForm.AstronomicalDusk = Astrometry.AstronomicalDusk;
            mAltitudeChartForm.AddDawnDuskGradient();
            mAltitudeChartForm.AddHorizonLine(mLocation.Horizon);

            mAltitudeChartForm.AddToTargetList(mTargetList);
            mAltitudeChartForm.Show();

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
            }
        }

        private void RadioButton_Year_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.YearChart = RadioButton_Year.Checked;
            if (RadioButton_Year.Checked == true)
            {
                mAltitudeChart.ShowChartAreaSeries("Year");
            }
        }

        private void RadioButton_Optimal_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.OptimalChart = RadioButton_Optimal.Checked;
            if (RadioButton_Optimal.Checked == true)
            {
                mAltitudeChart.ShowChartAreaSeries("Optimal");
            }
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
            TextBox_RightAscension.Text = mTarget.RightAscension.ToString();
            TextBox_Declination.Text = mTarget.Declination.ToString();
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
