using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
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
        private Tuple<DateTime, TimeZone> mLocalDateTime;

        private Target mTarget;
        private List<Target> mTargetList;

        private const string NinaTargetsRootPath = @"E:\Photography\Astro Photography\Captures\Nina\Targets";

        private Charts.AltitudeChartForm mAltitudeChartForm;
        private Charts.AltitudeChart mAltitudeChart;

        private ToolTip mToolTip;
        private int mToolTipIndex;

        private Panel Panel_AltitudeChart;

        private UIState mUIState;

        public MainForm()
        {
            InitializeComponent();
            TimePicker.Format = DateTimePickerFormat.Time;
            TimePicker.ShowUpDown = true;
            TimePicker.Format = DateTimePickerFormat.Custom;
            TimePicker.CustomFormat = "  hh:mm tt";

            mLocalDateTime = Tuple.Create(DateTime.Now, TimeZone.CurrentTimeZone);
            mLocation = new Location();
            mTarget = new Target();
            mTargetList = new List<Target>();

            mUIState = new UIState();


            mTimer = new System.Timers.Timer();
            mTimer.Interval = 5000;
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

        public void InitializeDynamicControls()
        {
            string[] folderSelectedPaths = { NinaTargetsRootPath };

            // Add Panel that MSChart will appear in to GroupBox
            Panel_AltitudeChart = new Panel();
            Panel_AltitudeChart.Location = new Point(10, 40);
            Panel_AltitudeChart.Size = new Size(GroupBox_AltitudeChart.Width - 20, GroupBox_AltitudeChart.Height - 50);
            Panel_AltitudeChart.Name = "Panel_Mschart";
            Panel_AltitudeChart.BackColor = Color.FromArgb(255, 128, 128, 128);
            GroupBox_AltitudeChart.Controls.Add(Panel_AltitudeChart);

            // Add actual Altitude Chart to Panel
            mAltitudeChart = new Charts.AltitudeChart();
            mAltitudeChart.mChart.Location = new Point(5, 5);
            mAltitudeChart.mChart.Size = new Size(Panel_AltitudeChart.Width - 10, Panel_AltitudeChart.Size.Height - 10);
            mAltitudeChart.mChart.BackColor = Color.FromArgb(255, 239, 235, 233);

            mAltitudeChart.ClearTargetList();
            mAltitudeChart.ClearChartAreaList();

            mAltitudeChart.Location = mLocation;
            mAltitudeChart.AddChartAreaToList("Day");
            mAltitudeChart.AddChartAreaToList("Year");
            mAltitudeChart.AddChartAreaToList("Optimal");
            mAltitudeChart.AddToTargetList(mTarget);
            mAltitudeChart.BuildTargetSeriesList();
            mAltitudeChart.ShowChartAreaSeries("Day");


            mAltitudeChart.ChartTitle = "Proper Motion at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            mAltitudeChart.UIState(mUIState);
            mAltitudeChart.AddLegend();
            mAltitudeChart.UpdateNowLine(DateTime.Now);


            mAltitudeChart.Legend = true;

            Panel_AltitudeChart.Controls.Add(mAltitudeChart.mChart);

            GetNinaTargets(folderSelectedPaths);

            ComboBox_SelectTarget.Text = "M31";
        }

        // ************************************************************************************************************************************* *//

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
            mLocalDateTime = Tuple.Create(DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZone.CurrentTimeZone);
            mLocation.DateTime = mLocalDateTime.Item1;
            mLocation.TimeZone = mLocalDateTime.Item2;
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

        // ************************************************************************************************************************************* *//
        // ****************** Latitude ********************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
        private void UpdateLatitudeTextBox(object sender, EventArgs e)
        {
            double latitude;

            ScrollNumericLocationCounters();

            latitude = (double)NumericUpDown_LatitudeDegrees.Value + (double)NumericUpDown_LatitudeMinutes.Value / 60.0 + (double)NumericUpDown_LatitudeSeconds.Value / 3600.0;

            mLocation.Latitude = Math.Round(latitude, 6);

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
                    mLocation.Latitude = Math.Round(latitude, 6);
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

                    mLocation.North = CheckBox_LocalNorth.Checked;
                    mLocation.West = CheckBox_LocalWest.Checked;
                    mTarget.North = CheckBox_TargetNorth.Checked;
                }
            }
        }

        // ************************************************************************************************************************************* *//
        // ****************** Longitude ******************************************************************************************************** *//
        // ************************************************************************************************************************************* *//
        private void UpdateLongitudeTextBox(object sender, EventArgs e)
        {
            double longitude;

            ScrollNumericLocationCounters();

            longitude = (double)NumericUpDown_LongitudeDegrees.Value + (double)NumericUpDown_LongitudeMinutes.Value / 60.0 + (double)NumericUpDown_LongitudeSeconds.Value / 3600.0;

            mLocation.Longitude = Math.Round(longitude, 6);

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
                    mLocation.Longitude = Math.Round(longitude, 6);
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

                    mLocation.North = CheckBox_LocalNorth.Checked;
                    mLocation.West = CheckBox_LocalWest.Checked;
                    mTarget.North = CheckBox_TargetNorth.Checked;
                }
            }
        }

        // ************************************************************************************************************************************* *//
        // ****************** Right Ascention ************************************************************************************************** *//
        // ************************************************************************************************************************************* *//
        private void UpdateRightAscensionTextBox(object sender, EventArgs e)
        {
            TimeSpan raTimeSpanHours;
            double milliseconds;

            ScrollNumericLocationCounters();

            milliseconds = (int)(1000.0m * (NumericUpDown_RaSeconds.Value - Math.Floor(NumericUpDown_RaSeconds.Value)));
            raTimeSpanHours = new TimeSpan(0, (int)NumericUpDown_RaHours.Value, (int)NumericUpDown_RaMinutes.Value, (int)NumericUpDown_RaSeconds.Value, (int)milliseconds);

            mTarget.RightAscension = Math.Round(raTimeSpanHours.TotalHours, 6);

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
                mTarget.RightAscension = Math.Round(raHours, 6);
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

                mLocation.North = CheckBox_LocalNorth.Checked;
                mLocation.West = CheckBox_LocalWest.Checked;
                mTarget.North = CheckBox_TargetNorth.Checked;
            }
        }

        // ************************************************************************************************************************************* *//
        // ************** Declination ********************************************************************************************************** *//
        // ************************************************************************************************************************************* *//
        private void UpdateDeclinationTextBox(object sender, EventArgs e)
        {
            double declination;

            if (mTarget == null) return;

            ScrollNumericLocationCounters();

            declination = (double)NumericUpDown_DecDegrees.Value + (double)NumericUpDown_DecMinutes.Value / 60.0 + (double)NumericUpDown_DecSeconds.Value / 3600.0;

            mTarget.Declination = Math.Round(declination, 6);

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
                    mTarget.Declination = Math.Round(Math.Abs(declination), 6);
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

                    mLocation.North = CheckBox_LocalNorth.Checked;
                    mLocation.West = CheckBox_LocalWest.Checked;
                    mTarget.North = CheckBox_TargetNorth.Checked;
                }
            }
        }

        // ************************************************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
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

        // ************************************************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
        private void NumericUpDown_Duration_ValueChanged(object sender, EventArgs e)
        {
            mLocation.MinutesAboveHorizon = (double)NumericUpDown_Duration.Value * 60.0;
            if (mAltitudeChart == null) return;
            mAltitudeChart.RebuildOptimalData();
        }

        private void NumericUpDown_Horizon_ValueChanged(object sender, EventArgs e)
        {
            mLocation.Horizon = (double)NumericUpDown_Horizon.Value;
            if (mAltitudeChart == null) return;
            mAltitudeChart.UpdateHorizonLines();
            mAltitudeChart.RebuildOptimalData();
        }

        private void DatePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = Tuple.Create(DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZone.CurrentTimeZone);
            RadioButton_Now.Checked = false;
            RadioButton_SetDateTime.Checked = true;
            UpdateLocalDateTimeEvents();
        }

        private void TimePicker_ValueChanged(object sender, EventArgs e)
        {
            mLocalDateTime = Tuple.Create(DatePicker.Value.Date + TimePicker.Value.TimeOfDay, TimeZone.CurrentTimeZone);
            RadioButton_Now.Checked = false;
            RadioButton_SetDateTime.Checked = true;
            UpdateLocalDateTimeEvents();
        }

        private void Button_GraphEphemeride_Click(object sender, EventArgs e)
        {
            Panel_AltitudeChart.Controls.Clear();

            foreach (Target target in mTargetList)
            {
                if (target.Name == ComboBox_SelectTarget.Text)
                {
                    mTarget = target;
                    break;
                }
            }

            // Add actual Altitude Chart to Panel
            mAltitudeChart = new Charts.AltitudeChart();
            mAltitudeChart.mChart.Location = new Point(5, 5);
            mAltitudeChart.mChart.Size = new Size(Panel_AltitudeChart.Width - 10, Panel_AltitudeChart.Size.Height - 10);
            mAltitudeChart.mChart.BackColor = Color.FromArgb(255, 239, 235, 233);


            mLocation.DateTime = DateTime.Now;

            mAltitudeChart.Location = mLocation;
            mAltitudeChart.AddChartAreaToList("Day");
            mAltitudeChart.AddChartAreaToList("Year");
            mAltitudeChart.AddChartAreaToList("Optimal");
            mAltitudeChart.AddToTargetList(mTarget);
            mAltitudeChart.BuildTargetSeriesList();
            mAltitudeChart.ShowChartAreaSeries("Day");

            mAltitudeChart.ChartTitle = "Proper Motion at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            mAltitudeChart.UIState(mUIState);
            mAltitudeChart.AddLegend();
            mAltitudeChart.UpdateNowLine(DateTime.Now);


            mAltitudeChart.Legend = true;

            Panel_AltitudeChart.Controls.Add(mAltitudeChart.mChart);
        }

        private void CheckBox_LocalWest_CheckedChanged(object sender, EventArgs e)
        {
            mLocation.West = CheckBox_LocalWest.Checked;
        }

        private void CheckBox_HoldTime_CheckedChanged(object sender, EventArgs e)
        {
            mTimer.Enabled = !CheckBox_HoldTime.Checked;
        }
        private void OnTimedEvent(System.Object source, System.Timers.ElapsedEventArgs e)
        {
            mLocalDateTime = Tuple.Create(DateTime.Now, TimeZone.CurrentTimeZone);
            mLocation.DateTime = mLocalDateTime.Item1;
            mLocation.TimeZone = mLocalDateTime.Item2;

            Invoke(new Action(() =>
            {
                DatePicker.ValueChanged -= DatePicker_ValueChanged;
                TimePicker.ValueChanged -= TimePicker_ValueChanged;

                TimePicker.Value = mLocalDateTime.Item1;// DateTime.Now;
                DatePicker.Value = mLocalDateTime.Item1; //DateTime.Now;

                DatePicker.ValueChanged += DatePicker_ValueChanged;
                TimePicker.ValueChanged -= TimePicker_ValueChanged;

                Label_SunAltitudeValue.Text = Astrometry.SunAltitude.ToString("F1");
                Label_LunarAltitudeValue.Text = Astrometry.LunarAltitude.ToString("F1");

                if (mAltitudeChart != null)
                {
                    mAltitudeChart.UpdateNowLine(mLocalDateTime.Item1);
                }
            }));

        }

        private void RadioButton_Now_CheckedChanged(object sender, EventArgs e)
        {
            mLocalDateTime = Tuple.Create(DateTime.Now, TimeZone.CurrentTimeZone);
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
        // ************************************************************************************************************************************* *//
        // *********** ComboBox_Location ******************************************************************************************************* *//
        // ************************************************************************************************************************************* *//
        private void ComboBox_Location_SelectionIndexChanged(object sender, EventArgs e)
        {
            if (ComboBox_Location.SelectedItem != null)
            {
                if (ComboBox_Location.SelectedItem.ToString() == "Penns Park")
                {
                    GroupBox_CoordinateSelection.Enabled = true;
                }

                if (ComboBox_Location.SelectedItem.ToString() == "SGP Sequence")
                {
                    GroupBox_SgpSequence.Enabled = true;
                }
            }
        }

        private void ComboBox_Location_DropDown(object sender, EventArgs e)
        {
            ComboBox_Location.SelectedItem = null;
        }

        // ************************************************************************************************************************************* *//
        // *********** ComboBox_Location ******************************************************************************************************* *//
        // ************************************************************************************************************************************* *//

        private void CheckBox_TargetNorth_CheckedChanged(object sender, EventArgs e)
        {
            mTarget.North = CheckBox_TargetNorth.Checked;
        }

        private void CheckBox_LocalNorth_CheckedChanged(object sender, EventArgs e)
        {
            mLocation.North = CheckBox_LocalNorth.Checked;
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
                GetNinaTargets(mFolder.SelectedPaths);
            }
        }

        private async void GetNinaTargets(string[] folderSelectedPaths)
        {
            mTargetList.Clear();
            CheckedListBox_SelectedSgpTargets.Items.Clear();
            ComboBox_SelectTarget.Items.Clear();

            var progressHandler = new Progress<Tuple<int, int>>(value =>
            {
                ProgressBar_ProcessObject.Maximum = value.Item2;
                ProgressBar_ProcessObject.Value = value.Item1;
            });

            var progress = progressHandler as IProgress<Tuple<int, int>>;

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
                MessageBox.Show(ex.Message);
                ProgressBar_ProcessObject.Value = 0;
            }

            foreach (Target t in mTargetList)
            {
                CheckedListBox_SelectedSgpTargets.Items.Add(t.Name, true);
            }

            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedSgpTargets.Items.Count.ToString();

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
            mAltitudeChartForm = new Charts.AltitudeChartForm();

            mAltitudeChartForm.ChartTitle = "Poper Motion at " + mLocation.Name + " for evening beginning " + mLocation.DateTime.Date.ToShortDateString();
            mAltitudeChartForm.AstronomicalDawn = Astrometry.AstronomicalDawn;
            mAltitudeChartForm.AstronomicalDusk = Astrometry.AstronomicalDusk;
            mAltitudeChartForm.AddDawnDuskGradient();
            mAltitudeChartForm.AddHorizonLine(mLocation.Horizon);

            mAltitudeChartForm.AddToTargetList(mTargetList);
            mAltitudeChartForm.Show();

        }

        private void ShowCheckBoxObjectToolTip(object sender, MouseEventArgs e)
        {
            string name;
            Target found;

            if (mToolTipIndex != this.CheckedListBox_SelectedSgpTargets.IndexFromPoint(e.Location))
            {
                mToolTipIndex = CheckedListBox_SelectedSgpTargets.IndexFromPoint(CheckedListBox_SelectedSgpTargets.PointToClient(MousePosition));
                if (mToolTipIndex > -1)
                {
                    name = CheckedListBox_SelectedSgpTargets.Items[mToolTipIndex].ToString();
                    found = mTargetList.Find(x => x.Name == name);
                    mToolTip.SetToolTip(CheckedListBox_SelectedSgpTargets, found.Directory);
                    mToolTip.AutoPopDelay = 5000;
                    mToolTip.InitialDelay = 2000;
                    mToolTip.ReshowDelay = 2000;
                }
            }
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
        private void CheckBox_ChartDuration_CheckedChanged(object sender, EventArgs e)
        {
            mUIState.DurationChart = CheckBox_ChartDuration.Checked;
        }

        private void ComboBox_SelectTarget_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                string selectedTargetName = ComboBox_SelectTarget.Text;

                mTarget = mTargetList.Find(target => target.Name == selectedTargetName);

                if (mTarget == null)
                {
                    return;
                }

                TextBox_RightAscension.Text = mTarget.RightAscension.ToString();
                TextBox_Declination.Text = mTarget.Declination.ToString();
            }
            catch
            {
                return;
            }
        }

        private void Button_ClearAllTargets_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < CheckedListBox_SelectedSgpTargets.Items.Count; i++)
                CheckedListBox_SelectedSgpTargets.SetItemCheckState(i, CheckState.Unchecked);


            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedSgpTargets.Items.Count.ToString();
        }

        private void Button_SelectAllTargets_Click(object sender, EventArgs e)
        {

            for (int i = 0; i < CheckedListBox_SelectedSgpTargets.Items.Count; i++)
                CheckedListBox_SelectedSgpTargets.SetItemCheckState(i, CheckState.Checked);

            Label_SelectedTargetNumber.Text = CheckedListBox_SelectedSgpTargets.Items.Count.ToString();

        }
    }
}
