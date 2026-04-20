namespace TargetPlanner
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.GroupBox_LocalConditions = new System.Windows.Forms.GroupBox();
            this.GroupBox_SunMoon = new System.Windows.Forms.GroupBox();
            this.Label_Phase = new System.Windows.Forms.Label();
            this.Label_Percent = new System.Windows.Forms.Label();
            this.Label_LunarPhaseValue = new System.Windows.Forms.Label();
            this.Label_SunAltitudeValue = new System.Windows.Forms.Label();
            this.Label_SunAltitude = new System.Windows.Forms.Label();
            this.Label_MoonSetValue = new System.Windows.Forms.Label();
            this.Label_MoonSetTimeText = new System.Windows.Forms.Label();
            this.Label_MoonRiseValue = new System.Windows.Forms.Label();
            this.Label_MoonRise = new System.Windows.Forms.Label();
            this.Label_LunarIlluminationFractionValue = new System.Windows.Forms.Label();
            this.Label_LunarIlluminationFraction = new System.Windows.Forms.Label();
            this.Label_LunarAltitudeValue = new System.Windows.Forms.Label();
            this.Label_MoonAltitude = new System.Windows.Forms.Label();
            this.Label_AstronomicalDawn = new System.Windows.Forms.Label();
            this.Label_AstronomicalDusk = new System.Windows.Forms.Label();
            this.Label_AstronomicalDuskValue = new System.Windows.Forms.Label();
            this.Label_MoonPhaseName = new System.Windows.Forms.Label();
            this.Label_AstronomicalDawnValue = new System.Windows.Forms.Label();
            this.GroupBox_LocalLocation = new System.Windows.Forms.GroupBox();
            this.CheckBox_LocalNorth = new System.Windows.Forms.CheckBox();
            this.Label_Hours = new System.Windows.Forms.Label();
            this.Label_MinDuration = new System.Windows.Forms.Label();
            this.Label_Degrees = new System.Windows.Forms.Label();
            this.NumericUpDown_Horizon = new System.Windows.Forms.NumericUpDown();
            this.Label_Location = new System.Windows.Forms.Label();
            this.NumericUpDown_Duration = new System.Windows.Forms.NumericUpDown();
            this.ComboBox_Location = new System.Windows.Forms.ComboBox();
            this.NumericUpDown_LatitudeMinutes = new System.Windows.Forms.NumericUpDown();
            this.Label_LocalHorizon = new System.Windows.Forms.Label();
            this.NumericUpDown_LatitudeDegrees = new System.Windows.Forms.NumericUpDown();
            this.Label_LocalLatitudeText = new System.Windows.Forms.Label();
            this.Label_LocalLongitudeText = new System.Windows.Forms.Label();
            this.NumericUpDown_LongitudeDegrees = new System.Windows.Forms.NumericUpDown();
            this.NumericUpDown_LongitudeMinutes = new System.Windows.Forms.NumericUpDown();
            this.Label_LocalLatDegreeColon = new System.Windows.Forms.Label();
            this.CheckBox_LocalWest = new System.Windows.Forms.CheckBox();
            this.Label_LocalLonDegreeColon = new System.Windows.Forms.Label();
            this.TextBox_Longitude = new System.Windows.Forms.TextBox();
            this.Label_LocalLatMinuteColon = new System.Windows.Forms.Label();
            this.TextBox_Latitude = new System.Windows.Forms.TextBox();
            this.Label_LocalLonMinuteColon = new System.Windows.Forms.Label();
            this.NumericUpDown_LongitudeSeconds = new System.Windows.Forms.NumericUpDown();
            this.NumericUpDown_LatitudeSeconds = new System.Windows.Forms.NumericUpDown();
            this.GroupBox_LocalDateTime = new System.Windows.Forms.GroupBox();
            this.GroupBox_LocalDateTimeControls = new System.Windows.Forms.GroupBox();
            this.DatePicker = new System.Windows.Forms.DateTimePicker();
            this.TimePicker = new System.Windows.Forms.DateTimePicker();
            this.GroupBox_TimeModeControls = new System.Windows.Forms.GroupBox();
            this.CheckBox_HoldTime = new System.Windows.Forms.CheckBox();
            this.RadioButton_SetDateTime = new System.Windows.Forms.RadioButton();
            this.RadioButton_Now = new System.Windows.Forms.RadioButton();
            this.GroupBox_CoordinateSelection = new System.Windows.Forms.GroupBox();
            this.ComboBox_SelectTarget = new System.Windows.Forms.ComboBox();
            this.Label_TargetName = new System.Windows.Forms.Label();
            this.CheckBox_TargetNorth = new System.Windows.Forms.CheckBox();
            this.TextBox_RightAscension = new System.Windows.Forms.TextBox();
            this.NumericUpDown_RaMinutes = new System.Windows.Forms.NumericUpDown();
            this.TextBox_Declination = new System.Windows.Forms.TextBox();
            this.Button_ClearEphemride = new System.Windows.Forms.Button();
            this.Label_SgpDecMinuteColon = new System.Windows.Forms.Label();
            this.Button_GraphEphemeride = new System.Windows.Forms.Button();
            this.NumericUpDown_RaHours = new System.Windows.Forms.NumericUpDown();
            this.NumericUpDown_RaSeconds = new System.Windows.Forms.NumericUpDown();
            this.NumericUpDown_DecMinutes = new System.Windows.Forms.NumericUpDown();
            this.Label_TargetDeclinationText = new System.Windows.Forms.Label();
            this.Label_SgpRaHourColon = new System.Windows.Forms.Label();
            this.Label_SgpRaMinuteColon = new System.Windows.Forms.Label();
            this.Label_SgpDecDegreeColon = new System.Windows.Forms.Label();
            this.Label_TargetRightAscensionText = new System.Windows.Forms.Label();
            this.NumericUpDown_DecSeconds = new System.Windows.Forms.NumericUpDown();
            this.NumericUpDown_DecDegrees = new System.Windows.Forms.NumericUpDown();
            this.RadioButton_Optimal = new System.Windows.Forms.RadioButton();
            this.RadioButton_Year = new System.Windows.Forms.RadioButton();
            this.RadioButton_Day = new System.Windows.Forms.RadioButton();
            this.GroupBox_SgpSequence = new System.Windows.Forms.GroupBox();
            this.Button_SelectAllTargets = new System.Windows.Forms.Button();
            this.Button_ClearAllTargets = new System.Windows.Forms.Button();
            this.ProgressBar_ProcessObject = new System.Windows.Forms.ProgressBar();
            this.CheckedListBox_SelectedSgpTargets = new System.Windows.Forms.CheckedListBox();
            this.Label_SgpTargets = new System.Windows.Forms.Label();
            this.Button_GraphTargetList = new System.Windows.Forms.Button();
            this.Button_BrowseTargetList = new System.Windows.Forms.Button();
            this.Label_SelectedTargetNumber = new System.Windows.Forms.Label();
            this.GroupBox_TargetSelection = new System.Windows.Forms.GroupBox();
            this.MenuStrip_MainForm = new System.Windows.Forms.MenuStrip();
            this.FileToolStripMenuItem_MainForm = new System.Windows.Forms.ToolStripMenuItem();
            this.GroupBox_AltitudeChart = new System.Windows.Forms.GroupBox();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.CheckBox_ChartDuration = new System.Windows.Forms.CheckBox();
            this.GroupBox_LocalConditions.SuspendLayout();
            this.GroupBox_SunMoon.SuspendLayout();
            this.GroupBox_LocalLocation.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_Horizon)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_Duration)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeDegrees)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeDegrees)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeSeconds)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeSeconds)).BeginInit();
            this.GroupBox_LocalDateTime.SuspendLayout();
            this.GroupBox_LocalDateTimeControls.SuspendLayout();
            this.GroupBox_TimeModeControls.SuspendLayout();
            this.GroupBox_CoordinateSelection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaHours)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaSeconds)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecSeconds)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecDegrees)).BeginInit();
            this.GroupBox_SgpSequence.SuspendLayout();
            this.GroupBox_TargetSelection.SuspendLayout();
            this.MenuStrip_MainForm.SuspendLayout();
            this.GroupBox_AltitudeChart.SuspendLayout();
            this.SuspendLayout();
            // 
            // GroupBox_LocalConditions
            // 
            this.GroupBox_LocalConditions.Controls.Add(this.GroupBox_SunMoon);
            this.GroupBox_LocalConditions.Controls.Add(this.GroupBox_LocalLocation);
            this.GroupBox_LocalConditions.Controls.Add(this.GroupBox_LocalDateTime);
            this.GroupBox_LocalConditions.Location = new System.Drawing.Point(42, 30);
            this.GroupBox_LocalConditions.Name = "GroupBox_LocalConditions";
            this.GroupBox_LocalConditions.Size = new System.Drawing.Size(529, 430);
            this.GroupBox_LocalConditions.TabIndex = 0;
            this.GroupBox_LocalConditions.TabStop = false;
            this.GroupBox_LocalConditions.Text = "Local Conditions";
            // 
            // GroupBox_SunMoon
            // 
            this.GroupBox_SunMoon.Controls.Add(this.Label_Phase);
            this.GroupBox_SunMoon.Controls.Add(this.Label_Percent);
            this.GroupBox_SunMoon.Controls.Add(this.Label_LunarPhaseValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_SunAltitudeValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_SunAltitude);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonSetValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonSetTimeText);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonRiseValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonRise);
            this.GroupBox_SunMoon.Controls.Add(this.Label_LunarIlluminationFractionValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_LunarIlluminationFraction);
            this.GroupBox_SunMoon.Controls.Add(this.Label_LunarAltitudeValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonAltitude);
            this.GroupBox_SunMoon.Controls.Add(this.Label_AstronomicalDawn);
            this.GroupBox_SunMoon.Controls.Add(this.Label_AstronomicalDusk);
            this.GroupBox_SunMoon.Controls.Add(this.Label_AstronomicalDuskValue);
            this.GroupBox_SunMoon.Controls.Add(this.Label_MoonPhaseName);
            this.GroupBox_SunMoon.Controls.Add(this.Label_AstronomicalDawnValue);
            this.GroupBox_SunMoon.Location = new System.Drawing.Point(28, 315);
            this.GroupBox_SunMoon.Name = "GroupBox_SunMoon";
            this.GroupBox_SunMoon.Size = new System.Drawing.Size(470, 100);
            this.GroupBox_SunMoon.TabIndex = 31;
            this.GroupBox_SunMoon.TabStop = false;
            this.GroupBox_SunMoon.Text = "Sun and Moon";
            // 
            // Label_Phase
            // 
            this.Label_Phase.AutoSize = true;
            this.Label_Phase.Location = new System.Drawing.Point(244, 69);
            this.Label_Phase.Name = "Label_Phase";
            this.Label_Phase.Size = new System.Drawing.Size(70, 13);
            this.Label_Phase.TabIndex = 41;
            this.Label_Phase.Text = "Lunar Phase:";
            // 
            // Label_Percent
            // 
            this.Label_Percent.AutoSize = true;
            this.Label_Percent.Location = new System.Drawing.Point(175, 69);
            this.Label_Percent.Name = "Label_Percent";
            this.Label_Percent.Size = new System.Drawing.Size(15, 13);
            this.Label_Percent.TabIndex = 40;
            this.Label_Percent.Text = "%";
            // 
            // Label_LunarPhaseValue
            // 
            this.Label_LunarPhaseValue.AutoSize = true;
            this.Label_LunarPhaseValue.Location = new System.Drawing.Point(311, 69);
            this.Label_LunarPhaseValue.Name = "Label_LunarPhaseValue";
            this.Label_LunarPhaseValue.Size = new System.Drawing.Size(14, 13);
            this.Label_LunarPhaseValue.TabIndex = 39;
            this.Label_LunarPhaseValue.Text = "V";
            // 
            // Label_SunAltitudeValue
            // 
            this.Label_SunAltitudeValue.AutoSize = true;
            this.Label_SunAltitudeValue.Location = new System.Drawing.Point(424, 26);
            this.Label_SunAltitudeValue.Name = "Label_SunAltitudeValue";
            this.Label_SunAltitudeValue.Size = new System.Drawing.Size(14, 13);
            this.Label_SunAltitudeValue.TabIndex = 38;
            this.Label_SunAltitudeValue.Text = "V";
            this.Label_SunAltitudeValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_SunAltitude
            // 
            this.Label_SunAltitude.AutoSize = true;
            this.Label_SunAltitude.Location = new System.Drawing.Point(348, 26);
            this.Label_SunAltitude.Name = "Label_SunAltitude";
            this.Label_SunAltitude.Size = new System.Drawing.Size(67, 13);
            this.Label_SunAltitude.TabIndex = 37;
            this.Label_SunAltitude.Text = "Sun Altitude:";
            // 
            // Label_MoonSetValue
            // 
            this.Label_MoonSetValue.AutoSize = true;
            this.Label_MoonSetValue.Location = new System.Drawing.Point(288, 46);
            this.Label_MoonSetValue.Name = "Label_MoonSetValue";
            this.Label_MoonSetValue.Size = new System.Drawing.Size(14, 13);
            this.Label_MoonSetValue.TabIndex = 36;
            this.Label_MoonSetValue.Text = "V";
            // 
            // Label_MoonSetTimeText
            // 
            this.Label_MoonSetTimeText.AutoSize = true;
            this.Label_MoonSetTimeText.Location = new System.Drawing.Point(188, 46);
            this.Label_MoonSetTimeText.Name = "Label_MoonSetTimeText";
            this.Label_MoonSetTimeText.Size = new System.Drawing.Size(56, 13);
            this.Label_MoonSetTimeText.TabIndex = 35;
            this.Label_MoonSetTimeText.Text = "Moon Set:";
            // 
            // Label_MoonRiseValue
            // 
            this.Label_MoonRiseValue.AutoSize = true;
            this.Label_MoonRiseValue.Location = new System.Drawing.Point(120, 46);
            this.Label_MoonRiseValue.Name = "Label_MoonRiseValue";
            this.Label_MoonRiseValue.Size = new System.Drawing.Size(14, 13);
            this.Label_MoonRiseValue.TabIndex = 34;
            this.Label_MoonRiseValue.Text = "V";
            this.Label_MoonRiseValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_MoonRise
            // 
            this.Label_MoonRise.AutoSize = true;
            this.Label_MoonRise.Location = new System.Drawing.Point(12, 46);
            this.Label_MoonRise.Name = "Label_MoonRise";
            this.Label_MoonRise.Size = new System.Drawing.Size(61, 13);
            this.Label_MoonRise.TabIndex = 33;
            this.Label_MoonRise.Text = "Moon Rise:";
            // 
            // Label_LunarIlluminationFractionValue
            // 
            this.Label_LunarIlluminationFractionValue.AutoSize = true;
            this.Label_LunarIlluminationFractionValue.Location = new System.Drawing.Point(160, 69);
            this.Label_LunarIlluminationFractionValue.Name = "Label_LunarIlluminationFractionValue";
            this.Label_LunarIlluminationFractionValue.Size = new System.Drawing.Size(14, 13);
            this.Label_LunarIlluminationFractionValue.TabIndex = 32;
            this.Label_LunarIlluminationFractionValue.Text = "V";
            this.Label_LunarIlluminationFractionValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_LunarIlluminationFraction
            // 
            this.Label_LunarIlluminationFraction.AutoSize = true;
            this.Label_LunarIlluminationFraction.Location = new System.Drawing.Point(68, 69);
            this.Label_LunarIlluminationFraction.Name = "Label_LunarIlluminationFraction";
            this.Label_LunarIlluminationFraction.Size = new System.Drawing.Size(92, 13);
            this.Label_LunarIlluminationFraction.TabIndex = 31;
            this.Label_LunarIlluminationFraction.Text = "Lunar Illumination:";
            // 
            // Label_LunarAltitudeValue
            // 
            this.Label_LunarAltitudeValue.AutoSize = true;
            this.Label_LunarAltitudeValue.Location = new System.Drawing.Point(427, 46);
            this.Label_LunarAltitudeValue.Name = "Label_LunarAltitudeValue";
            this.Label_LunarAltitudeValue.Size = new System.Drawing.Size(14, 13);
            this.Label_LunarAltitudeValue.TabIndex = 30;
            this.Label_LunarAltitudeValue.Text = "V";
            this.Label_LunarAltitudeValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_MoonAltitude
            // 
            this.Label_MoonAltitude.AutoSize = true;
            this.Label_MoonAltitude.Location = new System.Drawing.Point(348, 46);
            this.Label_MoonAltitude.Name = "Label_MoonAltitude";
            this.Label_MoonAltitude.Size = new System.Drawing.Size(75, 13);
            this.Label_MoonAltitude.TabIndex = 29;
            this.Label_MoonAltitude.Text = "Moon Altitude:";
            // 
            // Label_AstronomicalDawn
            // 
            this.Label_AstronomicalDawn.AutoSize = true;
            this.Label_AstronomicalDawn.Location = new System.Drawing.Point(188, 26);
            this.Label_AstronomicalDawn.Name = "Label_AstronomicalDawn";
            this.Label_AstronomicalDawn.Size = new System.Drawing.Size(101, 13);
            this.Label_AstronomicalDawn.TabIndex = 26;
            this.Label_AstronomicalDawn.Text = "Astronomical Dawn:";
            // 
            // Label_AstronomicalDusk
            // 
            this.Label_AstronomicalDusk.AutoSize = true;
            this.Label_AstronomicalDusk.Location = new System.Drawing.Point(12, 26);
            this.Label_AstronomicalDusk.Name = "Label_AstronomicalDusk";
            this.Label_AstronomicalDusk.Size = new System.Drawing.Size(98, 13);
            this.Label_AstronomicalDusk.TabIndex = 24;
            this.Label_AstronomicalDusk.Text = "Astronomical Dusk:";
            // 
            // Label_AstronomicalDuskValue
            // 
            this.Label_AstronomicalDuskValue.AutoSize = true;
            this.Label_AstronomicalDuskValue.Location = new System.Drawing.Point(120, 26);
            this.Label_AstronomicalDuskValue.Name = "Label_AstronomicalDuskValue";
            this.Label_AstronomicalDuskValue.Size = new System.Drawing.Size(14, 13);
            this.Label_AstronomicalDuskValue.TabIndex = 25;
            this.Label_AstronomicalDuskValue.Text = "V";
            this.Label_AstronomicalDuskValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_MoonPhaseName
            // 
            this.Label_MoonPhaseName.AutoSize = true;
            this.Label_MoonPhaseName.Location = new System.Drawing.Point(330, 26);
            this.Label_MoonPhaseName.Name = "Label_MoonPhaseName";
            this.Label_MoonPhaseName.Size = new System.Drawing.Size(0, 13);
            this.Label_MoonPhaseName.TabIndex = 28;
            // 
            // Label_AstronomicalDawnValue
            // 
            this.Label_AstronomicalDawnValue.AutoSize = true;
            this.Label_AstronomicalDawnValue.Location = new System.Drawing.Point(288, 26);
            this.Label_AstronomicalDawnValue.Name = "Label_AstronomicalDawnValue";
            this.Label_AstronomicalDawnValue.Size = new System.Drawing.Size(14, 13);
            this.Label_AstronomicalDawnValue.TabIndex = 27;
            this.Label_AstronomicalDawnValue.Text = "V";
            // 
            // GroupBox_LocalLocation
            // 
            this.GroupBox_LocalLocation.Controls.Add(this.CheckBox_LocalNorth);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_Hours);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_MinDuration);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_Degrees);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_Horizon);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_Location);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_Duration);
            this.GroupBox_LocalLocation.Controls.Add(this.ComboBox_Location);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LatitudeMinutes);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalHorizon);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LatitudeDegrees);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLatitudeText);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLongitudeText);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LongitudeDegrees);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LongitudeMinutes);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLatDegreeColon);
            this.GroupBox_LocalLocation.Controls.Add(this.CheckBox_LocalWest);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLonDegreeColon);
            this.GroupBox_LocalLocation.Controls.Add(this.TextBox_Longitude);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLatMinuteColon);
            this.GroupBox_LocalLocation.Controls.Add(this.TextBox_Latitude);
            this.GroupBox_LocalLocation.Controls.Add(this.Label_LocalLonMinuteColon);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LongitudeSeconds);
            this.GroupBox_LocalLocation.Controls.Add(this.NumericUpDown_LatitudeSeconds);
            this.GroupBox_LocalLocation.Location = new System.Drawing.Point(28, 19);
            this.GroupBox_LocalLocation.Name = "GroupBox_LocalLocation";
            this.GroupBox_LocalLocation.Size = new System.Drawing.Size(470, 145);
            this.GroupBox_LocalLocation.TabIndex = 30;
            this.GroupBox_LocalLocation.TabStop = false;
            this.GroupBox_LocalLocation.Text = "Local Location";
            // 
            // CheckBox_LocalNorth
            // 
            this.CheckBox_LocalNorth.AutoSize = true;
            this.CheckBox_LocalNorth.Checked = true;
            this.CheckBox_LocalNorth.CheckState = System.Windows.Forms.CheckState.Checked;
            this.CheckBox_LocalNorth.Location = new System.Drawing.Point(387, 48);
            this.CheckBox_LocalNorth.Name = "CheckBox_LocalNorth";
            this.CheckBox_LocalNorth.Size = new System.Drawing.Size(52, 17);
            this.CheckBox_LocalNorth.TabIndex = 25;
            this.CheckBox_LocalNorth.Text = "North";
            this.CheckBox_LocalNorth.UseVisualStyleBackColor = true;
            this.CheckBox_LocalNorth.CheckedChanged += new System.EventHandler(this.CheckBox_LocalNorth_CheckedChanged);
            // 
            // Label_Hours
            // 
            this.Label_Hours.AutoSize = true;
            this.Label_Hours.Location = new System.Drawing.Point(394, 112);
            this.Label_Hours.Name = "Label_Hours";
            this.Label_Hours.Size = new System.Drawing.Size(35, 13);
            this.Label_Hours.TabIndex = 24;
            this.Label_Hours.Text = "Hours";
            // 
            // Label_MinDuration
            // 
            this.Label_MinDuration.AutoSize = true;
            this.Label_MinDuration.Location = new System.Drawing.Point(242, 112);
            this.Label_MinDuration.Name = "Label_MinDuration";
            this.Label_MinDuration.Size = new System.Drawing.Size(94, 13);
            this.Label_MinDuration.TabIndex = 23;
            this.Label_MinDuration.Text = "Minimum Duration:";
            // 
            // Label_Degrees
            // 
            this.Label_Degrees.AutoSize = true;
            this.Label_Degrees.Location = new System.Drawing.Point(175, 112);
            this.Label_Degrees.Name = "Label_Degrees";
            this.Label_Degrees.Size = new System.Drawing.Size(47, 13);
            this.Label_Degrees.TabIndex = 22;
            this.Label_Degrees.Text = "Degrees";
            // 
            // NumericUpDown_Horizon
            // 
            this.NumericUpDown_Horizon.AllowDrop = true;
            this.NumericUpDown_Horizon.Location = new System.Drawing.Point(118, 108);
            this.NumericUpDown_Horizon.Maximum = new decimal(new int[] {
            89,
            0,
            0,
            0});
            this.NumericUpDown_Horizon.Name = "NumericUpDown_Horizon";
            this.NumericUpDown_Horizon.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_Horizon.TabIndex = 11;
            this.NumericUpDown_Horizon.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_Horizon.Value = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.NumericUpDown_Horizon.ValueChanged += new System.EventHandler(this.NumericUpDown_Horizon_ValueChanged);
            // 
            // Label_Location
            // 
            this.Label_Location.AutoSize = true;
            this.Label_Location.Location = new System.Drawing.Point(47, 20);
            this.Label_Location.Name = "Label_Location";
            this.Label_Location.Size = new System.Drawing.Size(51, 13);
            this.Label_Location.TabIndex = 20;
            this.Label_Location.Text = "Location:";
            // 
            // NumericUpDown_Duration
            // 
            this.NumericUpDown_Duration.AllowDrop = true;
            this.NumericUpDown_Duration.DecimalPlaces = 2;
            this.NumericUpDown_Duration.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.NumericUpDown_Duration.Location = new System.Drawing.Point(337, 108);
            this.NumericUpDown_Duration.Maximum = new decimal(new int[] {
            24,
            0,
            0,
            0});
            this.NumericUpDown_Duration.Name = "NumericUpDown_Duration";
            this.NumericUpDown_Duration.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_Duration.TabIndex = 12;
            this.NumericUpDown_Duration.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_Duration.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.NumericUpDown_Duration.ValueChanged += new System.EventHandler(this.NumericUpDown_Duration_ValueChanged);
            // 
            // ComboBox_Location
            // 
            this.ComboBox_Location.FormattingEnabled = true;
            this.ComboBox_Location.Items.AddRange(new object[] {
            "Penns Park",
            "SGP Sequence"});
            this.ComboBox_Location.Location = new System.Drawing.Point(108, 16);
            this.ComboBox_Location.Name = "ComboBox_Location";
            this.ComboBox_Location.Size = new System.Drawing.Size(121, 21);
            this.ComboBox_Location.TabIndex = 1;
            this.ComboBox_Location.Text = "Penns Park";
            this.ComboBox_Location.DropDown += new System.EventHandler(this.ComboBox_Location_DropDown);
            this.ComboBox_Location.SelectedIndexChanged += new System.EventHandler(this.ComboBox_Location_SelectionIndexChanged);
            // 
            // NumericUpDown_LatitudeMinutes
            // 
            this.NumericUpDown_LatitudeMinutes.AllowDrop = true;
            this.NumericUpDown_LatitudeMinutes.Location = new System.Drawing.Point(160, 46);
            this.NumericUpDown_LatitudeMinutes.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_LatitudeMinutes.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147483648});
            this.NumericUpDown_LatitudeMinutes.Name = "NumericUpDown_LatitudeMinutes";
            this.NumericUpDown_LatitudeMinutes.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_LatitudeMinutes.TabIndex = 3;
            this.NumericUpDown_LatitudeMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LatitudeMinutes.ValueChanged += new System.EventHandler(this.UpdateLatitudeTextBox);
            // 
            // Label_LocalHorizon
            // 
            this.Label_LocalHorizon.AutoSize = true;
            this.Label_LocalHorizon.Location = new System.Drawing.Point(40, 112);
            this.Label_LocalHorizon.Name = "Label_LocalHorizon";
            this.Label_LocalHorizon.Size = new System.Drawing.Size(75, 13);
            this.Label_LocalHorizon.TabIndex = 21;
            this.Label_LocalHorizon.Text = "Local Horizon:";
            // 
            // NumericUpDown_LatitudeDegrees
            // 
            this.NumericUpDown_LatitudeDegrees.AllowDrop = true;
            this.NumericUpDown_LatitudeDegrees.Location = new System.Drawing.Point(85, 46);
            this.NumericUpDown_LatitudeDegrees.Maximum = new decimal(new int[] {
            90,
            0,
            0,
            0});
            this.NumericUpDown_LatitudeDegrees.Minimum = new decimal(new int[] {
            90,
            0,
            0,
            -2147483648});
            this.NumericUpDown_LatitudeDegrees.Name = "NumericUpDown_LatitudeDegrees";
            this.NumericUpDown_LatitudeDegrees.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_LatitudeDegrees.TabIndex = 2;
            this.NumericUpDown_LatitudeDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LatitudeDegrees.ValueChanged += new System.EventHandler(this.UpdateLatitudeTextBox);
            // 
            // Label_LocalLatitudeText
            // 
            this.Label_LocalLatitudeText.AutoSize = true;
            this.Label_LocalLatitudeText.Location = new System.Drawing.Point(38, 50);
            this.Label_LocalLatitudeText.Name = "Label_LocalLatitudeText";
            this.Label_LocalLatitudeText.Size = new System.Drawing.Size(45, 13);
            this.Label_LocalLatitudeText.TabIndex = 3;
            this.Label_LocalLatitudeText.Text = "Latitude";
            // 
            // Label_LocalLongitudeText
            // 
            this.Label_LocalLongitudeText.AutoSize = true;
            this.Label_LocalLongitudeText.Location = new System.Drawing.Point(29, 73);
            this.Label_LocalLongitudeText.Name = "Label_LocalLongitudeText";
            this.Label_LocalLongitudeText.Size = new System.Drawing.Size(54, 13);
            this.Label_LocalLongitudeText.TabIndex = 4;
            this.Label_LocalLongitudeText.Text = "Longitude";
            // 
            // NumericUpDown_LongitudeDegrees
            // 
            this.NumericUpDown_LongitudeDegrees.AllowDrop = true;
            this.NumericUpDown_LongitudeDegrees.Location = new System.Drawing.Point(85, 69);
            this.NumericUpDown_LongitudeDegrees.Maximum = new decimal(new int[] {
            180,
            0,
            0,
            0});
            this.NumericUpDown_LongitudeDegrees.Name = "NumericUpDown_LongitudeDegrees";
            this.NumericUpDown_LongitudeDegrees.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_LongitudeDegrees.TabIndex = 6;
            this.NumericUpDown_LongitudeDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LongitudeDegrees.ValueChanged += new System.EventHandler(this.UpdateLongitudeTextBox);
            // 
            // NumericUpDown_LongitudeMinutes
            // 
            this.NumericUpDown_LongitudeMinutes.AllowDrop = true;
            this.NumericUpDown_LongitudeMinutes.Location = new System.Drawing.Point(160, 69);
            this.NumericUpDown_LongitudeMinutes.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_LongitudeMinutes.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147483648});
            this.NumericUpDown_LongitudeMinutes.Name = "NumericUpDown_LongitudeMinutes";
            this.NumericUpDown_LongitudeMinutes.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_LongitudeMinutes.TabIndex = 7;
            this.NumericUpDown_LongitudeMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LongitudeMinutes.ValueChanged += new System.EventHandler(this.UpdateLongitudeTextBox);
            // 
            // Label_LocalLatDegreeColon
            // 
            this.Label_LocalLatDegreeColon.AutoSize = true;
            this.Label_LocalLatDegreeColon.Location = new System.Drawing.Point(145, 50);
            this.Label_LocalLatDegreeColon.Name = "Label_LocalLatDegreeColon";
            this.Label_LocalLatDegreeColon.Size = new System.Drawing.Size(10, 13);
            this.Label_LocalLatDegreeColon.TabIndex = 8;
            this.Label_LocalLatDegreeColon.Text = ":";
            // 
            // CheckBox_LocalWest
            // 
            this.CheckBox_LocalWest.AutoSize = true;
            this.CheckBox_LocalWest.Checked = true;
            this.CheckBox_LocalWest.CheckState = System.Windows.Forms.CheckState.Checked;
            this.CheckBox_LocalWest.Location = new System.Drawing.Point(387, 71);
            this.CheckBox_LocalWest.Name = "CheckBox_LocalWest";
            this.CheckBox_LocalWest.Size = new System.Drawing.Size(51, 17);
            this.CheckBox_LocalWest.TabIndex = 10;
            this.CheckBox_LocalWest.Text = "West";
            this.CheckBox_LocalWest.UseVisualStyleBackColor = true;
            this.CheckBox_LocalWest.CheckedChanged += new System.EventHandler(this.CheckBox_LocalWest_CheckedChanged);
            // 
            // Label_LocalLonDegreeColon
            // 
            this.Label_LocalLonDegreeColon.AutoSize = true;
            this.Label_LocalLonDegreeColon.Location = new System.Drawing.Point(145, 73);
            this.Label_LocalLonDegreeColon.Name = "Label_LocalLonDegreeColon";
            this.Label_LocalLonDegreeColon.Size = new System.Drawing.Size(10, 13);
            this.Label_LocalLonDegreeColon.TabIndex = 9;
            this.Label_LocalLonDegreeColon.Text = ":";
            // 
            // TextBox_Longitude
            // 
            this.TextBox_Longitude.Location = new System.Drawing.Point(306, 69);
            this.TextBox_Longitude.Name = "TextBox_Longitude";
            this.TextBox_Longitude.Size = new System.Drawing.Size(74, 20);
            this.TextBox_Longitude.TabIndex = 9;
            this.TextBox_Longitude.Text = " ";
            this.TextBox_Longitude.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.TextBox_Longitude.TextChanged += new System.EventHandler(this.TextBox_Longitude_TextChanged);
            // 
            // Label_LocalLatMinuteColon
            // 
            this.Label_LocalLatMinuteColon.AutoSize = true;
            this.Label_LocalLatMinuteColon.Location = new System.Drawing.Point(220, 50);
            this.Label_LocalLatMinuteColon.Name = "Label_LocalLatMinuteColon";
            this.Label_LocalLatMinuteColon.Size = new System.Drawing.Size(10, 13);
            this.Label_LocalLatMinuteColon.TabIndex = 10;
            this.Label_LocalLatMinuteColon.Text = ":";
            // 
            // TextBox_Latitude
            // 
            this.TextBox_Latitude.AcceptsReturn = true;
            this.TextBox_Latitude.AllowDrop = true;
            this.TextBox_Latitude.Location = new System.Drawing.Point(306, 46);
            this.TextBox_Latitude.MaxLength = 20;
            this.TextBox_Latitude.Name = "TextBox_Latitude";
            this.TextBox_Latitude.Size = new System.Drawing.Size(74, 20);
            this.TextBox_Latitude.TabIndex = 5;
            this.TextBox_Latitude.Text = " ";
            this.TextBox_Latitude.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.TextBox_Latitude.TextChanged += new System.EventHandler(this.TextBox_Latitude_TextChanged);
            // 
            // Label_LocalLonMinuteColon
            // 
            this.Label_LocalLonMinuteColon.AutoSize = true;
            this.Label_LocalLonMinuteColon.Location = new System.Drawing.Point(220, 73);
            this.Label_LocalLonMinuteColon.Name = "Label_LocalLonMinuteColon";
            this.Label_LocalLonMinuteColon.Size = new System.Drawing.Size(10, 13);
            this.Label_LocalLonMinuteColon.TabIndex = 11;
            this.Label_LocalLonMinuteColon.Text = ":";
            // 
            // NumericUpDown_LongitudeSeconds
            // 
            this.NumericUpDown_LongitudeSeconds.AllowDrop = true;
            this.NumericUpDown_LongitudeSeconds.DecimalPlaces = 2;
            this.NumericUpDown_LongitudeSeconds.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.NumericUpDown_LongitudeSeconds.Location = new System.Drawing.Point(235, 69);
            this.NumericUpDown_LongitudeSeconds.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_LongitudeSeconds.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147352576});
            this.NumericUpDown_LongitudeSeconds.Name = "NumericUpDown_LongitudeSeconds";
            this.NumericUpDown_LongitudeSeconds.Size = new System.Drawing.Size(65, 20);
            this.NumericUpDown_LongitudeSeconds.TabIndex = 8;
            this.NumericUpDown_LongitudeSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LongitudeSeconds.ValueChanged += new System.EventHandler(this.UpdateLongitudeTextBox);
            // 
            // NumericUpDown_LatitudeSeconds
            // 
            this.NumericUpDown_LatitudeSeconds.AllowDrop = true;
            this.NumericUpDown_LatitudeSeconds.DecimalPlaces = 2;
            this.NumericUpDown_LatitudeSeconds.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.NumericUpDown_LatitudeSeconds.Location = new System.Drawing.Point(235, 46);
            this.NumericUpDown_LatitudeSeconds.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_LatitudeSeconds.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147352576});
            this.NumericUpDown_LatitudeSeconds.Name = "NumericUpDown_LatitudeSeconds";
            this.NumericUpDown_LatitudeSeconds.Size = new System.Drawing.Size(65, 20);
            this.NumericUpDown_LatitudeSeconds.TabIndex = 4;
            this.NumericUpDown_LatitudeSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_LatitudeSeconds.ValueChanged += new System.EventHandler(this.UpdateLatitudeTextBox);
            // 
            // GroupBox_LocalDateTime
            // 
            this.GroupBox_LocalDateTime.Controls.Add(this.GroupBox_LocalDateTimeControls);
            this.GroupBox_LocalDateTime.Controls.Add(this.GroupBox_TimeModeControls);
            this.GroupBox_LocalDateTime.Location = new System.Drawing.Point(28, 170);
            this.GroupBox_LocalDateTime.Name = "GroupBox_LocalDateTime";
            this.GroupBox_LocalDateTime.Size = new System.Drawing.Size(469, 129);
            this.GroupBox_LocalDateTime.TabIndex = 29;
            this.GroupBox_LocalDateTime.TabStop = false;
            this.GroupBox_LocalDateTime.Text = "Local Date and Time";
            // 
            // GroupBox_LocalDateTimeControls
            // 
            this.GroupBox_LocalDateTimeControls.Controls.Add(this.DatePicker);
            this.GroupBox_LocalDateTimeControls.Controls.Add(this.TimePicker);
            this.GroupBox_LocalDateTimeControls.Location = new System.Drawing.Point(58, 66);
            this.GroupBox_LocalDateTimeControls.Name = "GroupBox_LocalDateTimeControls";
            this.GroupBox_LocalDateTimeControls.Size = new System.Drawing.Size(347, 51);
            this.GroupBox_LocalDateTimeControls.TabIndex = 18;
            this.GroupBox_LocalDateTimeControls.TabStop = false;
            this.GroupBox_LocalDateTimeControls.Text = "Date and Time";
            // 
            // DatePicker
            // 
            this.DatePicker.Location = new System.Drawing.Point(20, 21);
            this.DatePicker.Name = "DatePicker";
            this.DatePicker.Size = new System.Drawing.Size(209, 20);
            this.DatePicker.TabIndex = 0;
            this.DatePicker.ValueChanged += new System.EventHandler(this.DatePicker_ValueChanged);
            // 
            // TimePicker
            // 
            this.TimePicker.CustomFormat = "";
            this.TimePicker.Location = new System.Drawing.Point(245, 21);
            this.TimePicker.Name = "TimePicker";
            this.TimePicker.Size = new System.Drawing.Size(83, 20);
            this.TimePicker.TabIndex = 1;
            this.TimePicker.ValueChanged += new System.EventHandler(this.TimePicker_ValueChanged);
            // 
            // GroupBox_TimeModeControls
            // 
            this.GroupBox_TimeModeControls.Controls.Add(this.CheckBox_HoldTime);
            this.GroupBox_TimeModeControls.Controls.Add(this.RadioButton_SetDateTime);
            this.GroupBox_TimeModeControls.Controls.Add(this.RadioButton_Now);
            this.GroupBox_TimeModeControls.Location = new System.Drawing.Point(105, 19);
            this.GroupBox_TimeModeControls.Name = "GroupBox_TimeModeControls";
            this.GroupBox_TimeModeControls.Size = new System.Drawing.Size(253, 41);
            this.GroupBox_TimeModeControls.TabIndex = 17;
            this.GroupBox_TimeModeControls.TabStop = false;
            this.GroupBox_TimeModeControls.Text = "Time Mode";
            // 
            // CheckBox_HoldTime
            // 
            this.CheckBox_HoldTime.AutoSize = true;
            this.CheckBox_HoldTime.Location = new System.Drawing.Point(189, 19);
            this.CheckBox_HoldTime.Name = "CheckBox_HoldTime";
            this.CheckBox_HoldTime.Size = new System.Drawing.Size(48, 17);
            this.CheckBox_HoldTime.TabIndex = 2;
            this.CheckBox_HoldTime.Text = "Hold";
            this.CheckBox_HoldTime.UseVisualStyleBackColor = true;
            this.CheckBox_HoldTime.CheckedChanged += new System.EventHandler(this.CheckBox_HoldTime_CheckedChanged);
            // 
            // RadioButton_SetDateTime
            // 
            this.RadioButton_SetDateTime.AutoSize = true;
            this.RadioButton_SetDateTime.Location = new System.Drawing.Point(71, 18);
            this.RadioButton_SetDateTime.Name = "RadioButton_SetDateTime";
            this.RadioButton_SetDateTime.Size = new System.Drawing.Size(114, 17);
            this.RadioButton_SetDateTime.TabIndex = 1;
            this.RadioButton_SetDateTime.Text = "Set Time and Date";
            this.RadioButton_SetDateTime.UseVisualStyleBackColor = true;
            // 
            // RadioButton_Now
            // 
            this.RadioButton_Now.AutoSize = true;
            this.RadioButton_Now.Checked = true;
            this.RadioButton_Now.Location = new System.Drawing.Point(18, 18);
            this.RadioButton_Now.Name = "RadioButton_Now";
            this.RadioButton_Now.Size = new System.Drawing.Size(47, 17);
            this.RadioButton_Now.TabIndex = 0;
            this.RadioButton_Now.TabStop = true;
            this.RadioButton_Now.Text = "Now";
            this.RadioButton_Now.UseVisualStyleBackColor = true;
            this.RadioButton_Now.CheckedChanged += new System.EventHandler(this.RadioButton_Now_CheckedChanged);
            // 
            // GroupBox_CoordinateSelection
            // 
            this.GroupBox_CoordinateSelection.Controls.Add(this.ComboBox_SelectTarget);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_TargetName);
            this.GroupBox_CoordinateSelection.Controls.Add(this.CheckBox_TargetNorth);
            this.GroupBox_CoordinateSelection.Controls.Add(this.TextBox_RightAscension);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_RaMinutes);
            this.GroupBox_CoordinateSelection.Controls.Add(this.TextBox_Declination);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Button_ClearEphemride);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_SgpDecMinuteColon);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Button_GraphEphemeride);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_RaHours);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_RaSeconds);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_DecMinutes);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_TargetDeclinationText);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_SgpRaHourColon);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_SgpRaMinuteColon);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_SgpDecDegreeColon);
            this.GroupBox_CoordinateSelection.Controls.Add(this.Label_TargetRightAscensionText);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_DecSeconds);
            this.GroupBox_CoordinateSelection.Controls.Add(this.NumericUpDown_DecDegrees);
            this.GroupBox_CoordinateSelection.Location = new System.Drawing.Point(33, 19);
            this.GroupBox_CoordinateSelection.Name = "GroupBox_CoordinateSelection";
            this.GroupBox_CoordinateSelection.Size = new System.Drawing.Size(469, 145);
            this.GroupBox_CoordinateSelection.TabIndex = 1;
            this.GroupBox_CoordinateSelection.TabStop = false;
            this.GroupBox_CoordinateSelection.Text = "Target Coordinates";
            // 
            // ComboBox_SelectTarget
            // 
            this.ComboBox_SelectTarget.FormattingEnabled = true;
            this.ComboBox_SelectTarget.Location = new System.Drawing.Point(107, 16);
            this.ComboBox_SelectTarget.Name = "ComboBox_SelectTarget";
            this.ComboBox_SelectTarget.Size = new System.Drawing.Size(274, 21);
            this.ComboBox_SelectTarget.TabIndex = 40;
            this.ComboBox_SelectTarget.SelectedIndexChanged += new System.EventHandler(this.ComboBox_SelectTarget_SelectedIndexChanged);
            this.ComboBox_SelectTarget.MouseLeave += new System.EventHandler(this.ComboBox_SelectTarget_SelectedIndexChanged);
            // 
            // Label_TargetName
            // 
            this.Label_TargetName.AutoSize = true;
            this.Label_TargetName.Location = new System.Drawing.Point(29, 20);
            this.Label_TargetName.Name = "Label_TargetName";
            this.Label_TargetName.Size = new System.Drawing.Size(72, 13);
            this.Label_TargetName.TabIndex = 39;
            this.Label_TargetName.Text = "Target Name:";
            // 
            // CheckBox_TargetNorth
            // 
            this.CheckBox_TargetNorth.AutoSize = true;
            this.CheckBox_TargetNorth.Checked = true;
            this.CheckBox_TargetNorth.CheckState = System.Windows.Forms.CheckState.Checked;
            this.CheckBox_TargetNorth.Location = new System.Drawing.Point(389, 71);
            this.CheckBox_TargetNorth.Name = "CheckBox_TargetNorth";
            this.CheckBox_TargetNorth.Size = new System.Drawing.Size(52, 17);
            this.CheckBox_TargetNorth.TabIndex = 26;
            this.CheckBox_TargetNorth.Text = "North";
            this.CheckBox_TargetNorth.UseVisualStyleBackColor = true;
            this.CheckBox_TargetNorth.CheckedChanged += new System.EventHandler(this.CheckBox_TargetNorth_CheckedChanged);
            // 
            // TextBox_RightAscension
            // 
            this.TextBox_RightAscension.AllowDrop = true;
            this.TextBox_RightAscension.Location = new System.Drawing.Point(307, 46);
            this.TextBox_RightAscension.MaxLength = 20;
            this.TextBox_RightAscension.Name = "TextBox_RightAscension";
            this.TextBox_RightAscension.Size = new System.Drawing.Size(74, 20);
            this.TextBox_RightAscension.TabIndex = 16;
            this.TextBox_RightAscension.Text = " ";
            this.TextBox_RightAscension.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.TextBox_RightAscension.WordWrap = false;
            this.TextBox_RightAscension.TextChanged += new System.EventHandler(this.TextBox_RightAscension_TextChanged);
            this.TextBox_RightAscension.MouseLeave += new System.EventHandler(this.TextBox_RightAscension_TextChanged);
            // 
            // NumericUpDown_RaMinutes
            // 
            this.NumericUpDown_RaMinutes.AllowDrop = true;
            this.NumericUpDown_RaMinutes.Location = new System.Drawing.Point(161, 46);
            this.NumericUpDown_RaMinutes.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_RaMinutes.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147483648});
            this.NumericUpDown_RaMinutes.Name = "NumericUpDown_RaMinutes";
            this.NumericUpDown_RaMinutes.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_RaMinutes.TabIndex = 14;
            this.NumericUpDown_RaMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_RaMinutes.ValueChanged += new System.EventHandler(this.UpdateRightAscensionTextBox);
            // 
            // TextBox_Declination
            // 
            this.TextBox_Declination.Location = new System.Drawing.Point(307, 69);
            this.TextBox_Declination.Name = "TextBox_Declination";
            this.TextBox_Declination.Size = new System.Drawing.Size(74, 20);
            this.TextBox_Declination.TabIndex = 20;
            this.TextBox_Declination.Text = " ";
            this.TextBox_Declination.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.TextBox_Declination.TextChanged += new System.EventHandler(this.TextBox_Declination_TextChanged);
            // 
            // Button_ClearEphemride
            // 
            this.Button_ClearEphemride.Location = new System.Drawing.Point(245, 107);
            this.Button_ClearEphemride.Name = "Button_ClearEphemride";
            this.Button_ClearEphemride.Size = new System.Drawing.Size(66, 23);
            this.Button_ClearEphemride.TabIndex = 35;
            this.Button_ClearEphemride.Text = "Clear";
            this.Button_ClearEphemride.UseVisualStyleBackColor = true;
            this.Button_ClearEphemride.Click += new System.EventHandler(this.Button_ClearEphemeride_Click);
            // 
            // Label_SgpDecMinuteColon
            // 
            this.Label_SgpDecMinuteColon.AutoSize = true;
            this.Label_SgpDecMinuteColon.Location = new System.Drawing.Point(221, 73);
            this.Label_SgpDecMinuteColon.Name = "Label_SgpDecMinuteColon";
            this.Label_SgpDecMinuteColon.Size = new System.Drawing.Size(10, 13);
            this.Label_SgpDecMinuteColon.TabIndex = 38;
            this.Label_SgpDecMinuteColon.Text = ":";
            // 
            // Button_GraphEphemeride
            // 
            this.Button_GraphEphemeride.Location = new System.Drawing.Point(170, 107);
            this.Button_GraphEphemeride.Name = "Button_GraphEphemeride";
            this.Button_GraphEphemeride.Size = new System.Drawing.Size(66, 23);
            this.Button_GraphEphemeride.TabIndex = 34;
            this.Button_GraphEphemeride.Text = "Graph";
            this.Button_GraphEphemeride.UseVisualStyleBackColor = true;
            this.Button_GraphEphemeride.Click += new System.EventHandler(this.Button_GraphEphemeride_Click);
            // 
            // NumericUpDown_RaHours
            // 
            this.NumericUpDown_RaHours.AllowDrop = true;
            this.NumericUpDown_RaHours.Location = new System.Drawing.Point(86, 46);
            this.NumericUpDown_RaHours.Maximum = new decimal(new int[] {
            24,
            0,
            0,
            0});
            this.NumericUpDown_RaHours.Name = "NumericUpDown_RaHours";
            this.NumericUpDown_RaHours.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_RaHours.TabIndex = 13;
            this.NumericUpDown_RaHours.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_RaHours.ValueChanged += new System.EventHandler(this.UpdateRightAscensionTextBox);
            // 
            // NumericUpDown_RaSeconds
            // 
            this.NumericUpDown_RaSeconds.AllowDrop = true;
            this.NumericUpDown_RaSeconds.DecimalPlaces = 2;
            this.NumericUpDown_RaSeconds.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.NumericUpDown_RaSeconds.Location = new System.Drawing.Point(236, 46);
            this.NumericUpDown_RaSeconds.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_RaSeconds.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147352576});
            this.NumericUpDown_RaSeconds.Name = "NumericUpDown_RaSeconds";
            this.NumericUpDown_RaSeconds.Size = new System.Drawing.Size(65, 20);
            this.NumericUpDown_RaSeconds.TabIndex = 15;
            this.NumericUpDown_RaSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_RaSeconds.ValueChanged += new System.EventHandler(this.UpdateRightAscensionTextBox);
            // 
            // NumericUpDown_DecMinutes
            // 
            this.NumericUpDown_DecMinutes.AllowDrop = true;
            this.NumericUpDown_DecMinutes.Location = new System.Drawing.Point(161, 69);
            this.NumericUpDown_DecMinutes.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_DecMinutes.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147483648});
            this.NumericUpDown_DecMinutes.Name = "NumericUpDown_DecMinutes";
            this.NumericUpDown_DecMinutes.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_DecMinutes.TabIndex = 18;
            this.NumericUpDown_DecMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_DecMinutes.ValueChanged += new System.EventHandler(this.UpdateDeclinationTextBox);
            // 
            // Label_TargetDeclinationText
            // 
            this.Label_TargetDeclinationText.AutoSize = true;
            this.Label_TargetDeclinationText.Location = new System.Drawing.Point(56, 73);
            this.Label_TargetDeclinationText.Name = "Label_TargetDeclinationText";
            this.Label_TargetDeclinationText.Size = new System.Drawing.Size(29, 13);
            this.Label_TargetDeclinationText.TabIndex = 26;
            this.Label_TargetDeclinationText.Text = "DEC";
            // 
            // Label_SgpRaHourColon
            // 
            this.Label_SgpRaHourColon.AutoSize = true;
            this.Label_SgpRaHourColon.Location = new System.Drawing.Point(146, 50);
            this.Label_SgpRaHourColon.Name = "Label_SgpRaHourColon";
            this.Label_SgpRaHourColon.Size = new System.Drawing.Size(10, 13);
            this.Label_SgpRaHourColon.TabIndex = 25;
            this.Label_SgpRaHourColon.Text = ":";
            // 
            // Label_SgpRaMinuteColon
            // 
            this.Label_SgpRaMinuteColon.AutoSize = true;
            this.Label_SgpRaMinuteColon.Location = new System.Drawing.Point(221, 50);
            this.Label_SgpRaMinuteColon.Name = "Label_SgpRaMinuteColon";
            this.Label_SgpRaMinuteColon.Size = new System.Drawing.Size(10, 13);
            this.Label_SgpRaMinuteColon.TabIndex = 37;
            this.Label_SgpRaMinuteColon.Text = ":";
            // 
            // Label_SgpDecDegreeColon
            // 
            this.Label_SgpDecDegreeColon.AutoSize = true;
            this.Label_SgpDecDegreeColon.Location = new System.Drawing.Point(146, 73);
            this.Label_SgpDecDegreeColon.Name = "Label_SgpDecDegreeColon";
            this.Label_SgpDecDegreeColon.Size = new System.Drawing.Size(10, 13);
            this.Label_SgpDecDegreeColon.TabIndex = 36;
            this.Label_SgpDecDegreeColon.Text = ":";
            // 
            // Label_TargetRightAscensionText
            // 
            this.Label_TargetRightAscensionText.AutoSize = true;
            this.Label_TargetRightAscensionText.Location = new System.Drawing.Point(63, 50);
            this.Label_TargetRightAscensionText.Name = "Label_TargetRightAscensionText";
            this.Label_TargetRightAscensionText.Size = new System.Drawing.Size(22, 13);
            this.Label_TargetRightAscensionText.TabIndex = 25;
            this.Label_TargetRightAscensionText.Text = "RA";
            // 
            // NumericUpDown_DecSeconds
            // 
            this.NumericUpDown_DecSeconds.AllowDrop = true;
            this.NumericUpDown_DecSeconds.DecimalPlaces = 2;
            this.NumericUpDown_DecSeconds.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.NumericUpDown_DecSeconds.Location = new System.Drawing.Point(236, 69);
            this.NumericUpDown_DecSeconds.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NumericUpDown_DecSeconds.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147352576});
            this.NumericUpDown_DecSeconds.Name = "NumericUpDown_DecSeconds";
            this.NumericUpDown_DecSeconds.Size = new System.Drawing.Size(65, 20);
            this.NumericUpDown_DecSeconds.TabIndex = 19;
            this.NumericUpDown_DecSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_DecSeconds.ValueChanged += new System.EventHandler(this.UpdateDeclinationTextBox);
            // 
            // NumericUpDown_DecDegrees
            // 
            this.NumericUpDown_DecDegrees.AllowDrop = true;
            this.NumericUpDown_DecDegrees.Location = new System.Drawing.Point(86, 69);
            this.NumericUpDown_DecDegrees.Maximum = new decimal(new int[] {
            90,
            0,
            0,
            0});
            this.NumericUpDown_DecDegrees.Minimum = new decimal(new int[] {
            90,
            0,
            0,
            -2147483648});
            this.NumericUpDown_DecDegrees.Name = "NumericUpDown_DecDegrees";
            this.NumericUpDown_DecDegrees.Size = new System.Drawing.Size(55, 20);
            this.NumericUpDown_DecDegrees.TabIndex = 17;
            this.NumericUpDown_DecDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.NumericUpDown_DecDegrees.ValueChanged += new System.EventHandler(this.UpdateDeclinationTextBox);
            // 
            // RadioButton_Optimal
            // 
            this.RadioButton_Optimal.AutoSize = true;
            this.RadioButton_Optimal.Location = new System.Drawing.Point(112, 19);
            this.RadioButton_Optimal.Name = "RadioButton_Optimal";
            this.RadioButton_Optimal.Size = new System.Drawing.Size(60, 17);
            this.RadioButton_Optimal.TabIndex = 38;
            this.RadioButton_Optimal.Text = "Optimal";
            this.RadioButton_Optimal.UseVisualStyleBackColor = true;
            this.RadioButton_Optimal.CheckedChanged += new System.EventHandler(this.RadioButton_Optimal_CheckedChanged);
            // 
            // RadioButton_Year
            // 
            this.RadioButton_Year.AutoSize = true;
            this.RadioButton_Year.Location = new System.Drawing.Point(60, 19);
            this.RadioButton_Year.Name = "RadioButton_Year";
            this.RadioButton_Year.Size = new System.Drawing.Size(47, 17);
            this.RadioButton_Year.TabIndex = 37;
            this.RadioButton_Year.TabStop = true;
            this.RadioButton_Year.Text = "Year";
            this.RadioButton_Year.UseVisualStyleBackColor = true;
            this.RadioButton_Year.CheckedChanged += new System.EventHandler(this.RadioButton_Year_CheckedChanged);
            // 
            // RadioButton_Day
            // 
            this.RadioButton_Day.AutoSize = true;
            this.RadioButton_Day.Checked = true;
            this.RadioButton_Day.Location = new System.Drawing.Point(11, 19);
            this.RadioButton_Day.Name = "RadioButton_Day";
            this.RadioButton_Day.Size = new System.Drawing.Size(44, 17);
            this.RadioButton_Day.TabIndex = 36;
            this.RadioButton_Day.TabStop = true;
            this.RadioButton_Day.Text = "Day";
            this.RadioButton_Day.UseVisualStyleBackColor = true;
            this.RadioButton_Day.CheckedChanged += new System.EventHandler(this.RadioButton_Day_CheckedChanged);
            // 
            // GroupBox_SgpSequence
            // 
            this.GroupBox_SgpSequence.Controls.Add(this.Button_SelectAllTargets);
            this.GroupBox_SgpSequence.Controls.Add(this.Button_ClearAllTargets);
            this.GroupBox_SgpSequence.Controls.Add(this.ProgressBar_ProcessObject);
            this.GroupBox_SgpSequence.Controls.Add(this.CheckedListBox_SelectedSgpTargets);
            this.GroupBox_SgpSequence.Controls.Add(this.Label_SgpTargets);
            this.GroupBox_SgpSequence.Controls.Add(this.Button_GraphTargetList);
            this.GroupBox_SgpSequence.Controls.Add(this.Button_BrowseTargetList);
            this.GroupBox_SgpSequence.Controls.Add(this.Label_SelectedTargetNumber);
            this.GroupBox_SgpSequence.Location = new System.Drawing.Point(33, 170);
            this.GroupBox_SgpSequence.Name = "GroupBox_SgpSequence";
            this.GroupBox_SgpSequence.Size = new System.Drawing.Size(472, 241);
            this.GroupBox_SgpSequence.TabIndex = 2;
            this.GroupBox_SgpSequence.TabStop = false;
            this.GroupBox_SgpSequence.Text = "SGPro Sequence Selection";
            // 
            // Button_SelectAllTargets
            // 
            this.Button_SelectAllTargets.Font = new System.Drawing.Font("Microsoft Sans Serif", 6F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Button_SelectAllTargets.Location = new System.Drawing.Point(388, 22);
            this.Button_SelectAllTargets.Name = "Button_SelectAllTargets";
            this.Button_SelectAllTargets.Size = new System.Drawing.Size(64, 18);
            this.Button_SelectAllTargets.TabIndex = 9;
            this.Button_SelectAllTargets.Text = "Select All";
            this.Button_SelectAllTargets.UseVisualStyleBackColor = true;
            this.Button_SelectAllTargets.Click += new System.EventHandler(this.Button_SelectAllTargets_Click);
            // 
            // Button_ClearAllTargets
            // 
            this.Button_ClearAllTargets.Font = new System.Drawing.Font("Microsoft Sans Serif", 6F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Button_ClearAllTargets.Location = new System.Drawing.Point(318, 22);
            this.Button_ClearAllTargets.Name = "Button_ClearAllTargets";
            this.Button_ClearAllTargets.Size = new System.Drawing.Size(64, 18);
            this.Button_ClearAllTargets.TabIndex = 8;
            this.Button_ClearAllTargets.Text = "Clear All";
            this.Button_ClearAllTargets.UseVisualStyleBackColor = true;
            this.Button_ClearAllTargets.Click += new System.EventHandler(this.Button_ClearAllTargets_Click);
            // 
            // ProgressBar_ProcessObject
            // 
            this.ProgressBar_ProcessObject.Location = new System.Drawing.Point(19, 49);
            this.ProgressBar_ProcessObject.Name = "ProgressBar_ProcessObject";
            this.ProgressBar_ProcessObject.Size = new System.Drawing.Size(433, 18);
            this.ProgressBar_ProcessObject.TabIndex = 7;
            // 
            // CheckedListBox_SelectedSgpTargets
            // 
            this.CheckedListBox_SelectedSgpTargets.FormattingEnabled = true;
            this.CheckedListBox_SelectedSgpTargets.Location = new System.Drawing.Point(18, 73);
            this.CheckedListBox_SelectedSgpTargets.MultiColumn = true;
            this.CheckedListBox_SelectedSgpTargets.Name = "CheckedListBox_SelectedSgpTargets";
            this.CheckedListBox_SelectedSgpTargets.ScrollAlwaysVisible = true;
            this.CheckedListBox_SelectedSgpTargets.Size = new System.Drawing.Size(434, 154);
            this.CheckedListBox_SelectedSgpTargets.Sorted = true;
            this.CheckedListBox_SelectedSgpTargets.TabIndex = 4;
            this.CheckedListBox_SelectedSgpTargets.ThreeDCheckBoxes = true;
            this.CheckedListBox_SelectedSgpTargets.MouseMove += new System.Windows.Forms.MouseEventHandler(this.ShowCheckBoxObjectToolTip);
            // 
            // Label_SgpTargets
            // 
            this.Label_SgpTargets.AutoSize = true;
            this.Label_SgpTargets.Location = new System.Drawing.Point(170, 25);
            this.Label_SgpTargets.Name = "Label_SgpTargets";
            this.Label_SgpTargets.Size = new System.Drawing.Size(91, 13);
            this.Label_SgpTargets.TabIndex = 3;
            this.Label_SgpTargets.Text = "Selected Targets:";
            // 
            // Button_GraphTargetList
            // 
            this.Button_GraphTargetList.Location = new System.Drawing.Point(95, 20);
            this.Button_GraphTargetList.Name = "Button_GraphTargetList";
            this.Button_GraphTargetList.Size = new System.Drawing.Size(66, 23);
            this.Button_GraphTargetList.TabIndex = 1;
            this.Button_GraphTargetList.Text = "Grpah";
            this.Button_GraphTargetList.UseVisualStyleBackColor = true;
            this.Button_GraphTargetList.Click += new System.EventHandler(this.Button_GraphTargetList_Click);
            // 
            // Button_BrowseTargetList
            // 
            this.Button_BrowseTargetList.Location = new System.Drawing.Point(18, 20);
            this.Button_BrowseTargetList.Name = "Button_BrowseTargetList";
            this.Button_BrowseTargetList.Size = new System.Drawing.Size(66, 23);
            this.Button_BrowseTargetList.TabIndex = 0;
            this.Button_BrowseTargetList.Text = "Browse";
            this.Button_BrowseTargetList.UseVisualStyleBackColor = true;
            this.Button_BrowseTargetList.Click += new System.EventHandler(this.Button_BrowseTargetList_Click);
            // 
            // Label_SelectedTargetNumber
            // 
            this.Label_SelectedTargetNumber.AutoSize = true;
            this.Label_SelectedTargetNumber.Location = new System.Drawing.Point(259, 26);
            this.Label_SelectedTargetNumber.Name = "Label_SelectedTargetNumber";
            this.Label_SelectedTargetNumber.Size = new System.Drawing.Size(35, 13);
            this.Label_SelectedTargetNumber.TabIndex = 6;
            this.Label_SelectedTargetNumber.Text = "LSTN";
            // 
            // GroupBox_TargetSelection
            // 
            this.GroupBox_TargetSelection.Controls.Add(this.GroupBox_CoordinateSelection);
            this.GroupBox_TargetSelection.Controls.Add(this.GroupBox_SgpSequence);
            this.GroupBox_TargetSelection.Location = new System.Drawing.Point(614, 30);
            this.GroupBox_TargetSelection.Name = "GroupBox_TargetSelection";
            this.GroupBox_TargetSelection.Size = new System.Drawing.Size(529, 430);
            this.GroupBox_TargetSelection.TabIndex = 3;
            this.GroupBox_TargetSelection.TabStop = false;
            this.GroupBox_TargetSelection.Text = "Target Selection";
            // 
            // MenuStrip_MainForm
            // 
            this.MenuStrip_MainForm.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileToolStripMenuItem_MainForm});
            this.MenuStrip_MainForm.Location = new System.Drawing.Point(0, 0);
            this.MenuStrip_MainForm.Name = "MenuStrip_MainForm";
            this.MenuStrip_MainForm.Size = new System.Drawing.Size(1182, 24);
            this.MenuStrip_MainForm.TabIndex = 5;
            this.MenuStrip_MainForm.Text = "menuStrip1";
            // 
            // FileToolStripMenuItem_MainForm
            // 
            this.FileToolStripMenuItem_MainForm.Name = "FileToolStripMenuItem_MainForm";
            this.FileToolStripMenuItem_MainForm.Size = new System.Drawing.Size(37, 20);
            this.FileToolStripMenuItem_MainForm.Text = "File";
            // 
            // GroupBox_AltitudeChart
            // 
            this.GroupBox_AltitudeChart.Controls.Add(this.progressBar1);
            this.GroupBox_AltitudeChart.Controls.Add(this.CheckBox_ChartDuration);
            this.GroupBox_AltitudeChart.Controls.Add(this.RadioButton_Year);
            this.GroupBox_AltitudeChart.Controls.Add(this.RadioButton_Optimal);
            this.GroupBox_AltitudeChart.Controls.Add(this.RadioButton_Day);
            this.GroupBox_AltitudeChart.Location = new System.Drawing.Point(13, 476);
            this.GroupBox_AltitudeChart.Name = "GroupBox_AltitudeChart";
            this.GroupBox_AltitudeChart.Size = new System.Drawing.Size(1157, 379);
            this.GroupBox_AltitudeChart.TabIndex = 6;
            this.GroupBox_AltitudeChart.TabStop = false;
            this.GroupBox_AltitudeChart.Text = "Target Altitude";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(298, 18);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(841, 17);
            this.progressBar1.TabIndex = 40;
            // 
            // CheckBox_ChartDuration
            // 
            this.CheckBox_ChartDuration.AutoSize = true;
            this.CheckBox_ChartDuration.Location = new System.Drawing.Point(177, 19);
            this.CheckBox_ChartDuration.Name = "CheckBox_ChartDuration";
            this.CheckBox_ChartDuration.Size = new System.Drawing.Size(66, 17);
            this.CheckBox_ChartDuration.TabIndex = 39;
            this.CheckBox_ChartDuration.Text = "Duration";
            this.CheckBox_ChartDuration.UseVisualStyleBackColor = true;
            this.CheckBox_ChartDuration.CheckedChanged += new System.EventHandler(this.CheckBox_ChartDuration_CheckedChanged);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1182, 878);
            this.Controls.Add(this.GroupBox_AltitudeChart);
            this.Controls.Add(this.GroupBox_TargetSelection);
            this.Controls.Add(this.GroupBox_LocalConditions);
            this.Controls.Add(this.MenuStrip_MainForm);
            this.MainMenuStrip = this.MenuStrip_MainForm;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "TargetPlanner";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.GroupBox_LocalConditions.ResumeLayout(false);
            this.GroupBox_SunMoon.ResumeLayout(false);
            this.GroupBox_SunMoon.PerformLayout();
            this.GroupBox_LocalLocation.ResumeLayout(false);
            this.GroupBox_LocalLocation.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_Horizon)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_Duration)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeDegrees)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeDegrees)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LongitudeSeconds)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_LatitudeSeconds)).EndInit();
            this.GroupBox_LocalDateTime.ResumeLayout(false);
            this.GroupBox_LocalDateTimeControls.ResumeLayout(false);
            this.GroupBox_TimeModeControls.ResumeLayout(false);
            this.GroupBox_TimeModeControls.PerformLayout();
            this.GroupBox_CoordinateSelection.ResumeLayout(false);
            this.GroupBox_CoordinateSelection.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaHours)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_RaSeconds)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecSeconds)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NumericUpDown_DecDegrees)).EndInit();
            this.GroupBox_SgpSequence.ResumeLayout(false);
            this.GroupBox_SgpSequence.PerformLayout();
            this.GroupBox_TargetSelection.ResumeLayout(false);
            this.MenuStrip_MainForm.ResumeLayout(false);
            this.MenuStrip_MainForm.PerformLayout();
            this.GroupBox_AltitudeChart.ResumeLayout(false);
            this.GroupBox_AltitudeChart.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox GroupBox_LocalConditions;
        private System.Windows.Forms.DateTimePicker TimePicker;
        private System.Windows.Forms.DateTimePicker DatePicker;
        private System.Windows.Forms.GroupBox GroupBox_CoordinateSelection;
        private System.Windows.Forms.TextBox TextBox_Longitude;
        private System.Windows.Forms.TextBox TextBox_Latitude;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LongitudeSeconds;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LatitudeSeconds;
        private System.Windows.Forms.Label Label_LocalLonMinuteColon;
        private System.Windows.Forms.Label Label_LocalLatMinuteColon;
        private System.Windows.Forms.Label Label_LocalLonDegreeColon;
        private System.Windows.Forms.Label Label_LocalLatDegreeColon;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LongitudeMinutes;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LatitudeMinutes;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LongitudeDegrees;
        private System.Windows.Forms.Label Label_LocalLongitudeText;
        private System.Windows.Forms.Label Label_LocalLatitudeText;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LatitudeDegrees;
        private System.Windows.Forms.CheckBox CheckBox_LocalWest;
        private System.Windows.Forms.Label Label_LocalHorizon;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Duration;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Horizon;
        private System.Windows.Forms.NumericUpDown NumericUpDown_RaSeconds;
        private System.Windows.Forms.TextBox TextBox_Declination;
        private System.Windows.Forms.NumericUpDown NumericUpDown_RaHours;
        private System.Windows.Forms.TextBox TextBox_RightAscension;
        private System.Windows.Forms.Label Label_TargetRightAscensionText;
        private System.Windows.Forms.NumericUpDown NumericUpDown_DecSeconds;
        private System.Windows.Forms.Label Label_TargetDeclinationText;
        private System.Windows.Forms.NumericUpDown NumericUpDown_DecDegrees;
        private System.Windows.Forms.NumericUpDown NumericUpDown_DecMinutes;
        private System.Windows.Forms.NumericUpDown NumericUpDown_RaMinutes;
        private System.Windows.Forms.Button Button_GraphEphemeride;
        private System.Windows.Forms.Label Label_AstronomicalDawnValue;
        private System.Windows.Forms.Label Label_AstronomicalDawn;
        private System.Windows.Forms.Label Label_AstronomicalDuskValue;
        private System.Windows.Forms.Label Label_AstronomicalDusk;
        private System.Windows.Forms.Label Label_MoonPhaseName;
        private System.Windows.Forms.GroupBox GroupBox_SunMoon;
        private System.Windows.Forms.GroupBox GroupBox_LocalLocation;
        private System.Windows.Forms.GroupBox GroupBox_LocalDateTime;
        private System.Windows.Forms.Label Label_LunarAltitudeValue;
        private System.Windows.Forms.Label Label_MoonAltitude;
        private System.Windows.Forms.Label Label_LunarIlluminationFractionValue;
        private System.Windows.Forms.Label Label_LunarIlluminationFraction;
        private System.Windows.Forms.Label Label_MoonSetValue;
        private System.Windows.Forms.Label Label_MoonSetTimeText;
        private System.Windows.Forms.Label Label_MoonRiseValue;
        private System.Windows.Forms.Label Label_MoonRise;
        private System.Windows.Forms.GroupBox GroupBox_TimeModeControls;
        private System.Windows.Forms.RadioButton RadioButton_SetDateTime;
        private System.Windows.Forms.RadioButton RadioButton_Now;
        private System.Windows.Forms.GroupBox GroupBox_LocalDateTimeControls;
        private System.Windows.Forms.CheckBox CheckBox_HoldTime;
        private System.Windows.Forms.Label Label_SunAltitudeValue;
        private System.Windows.Forms.Label Label_SunAltitude;
        private System.Windows.Forms.Label Label_LunarPhaseValue;
        private System.Windows.Forms.Label Label_Location;
        private System.Windows.Forms.ComboBox ComboBox_Location;
        private System.Windows.Forms.GroupBox GroupBox_SgpSequence;
        private System.Windows.Forms.Button Button_BrowseTargetList;
        private System.Windows.Forms.Label Label_SgpTargets;
        private System.Windows.Forms.Button Button_GraphTargetList;
        private System.Windows.Forms.CheckedListBox CheckedListBox_SelectedSgpTargets;
        private System.Windows.Forms.Label Label_SelectedTargetNumber;
        private System.Windows.Forms.Button Button_ClearEphemride;
        private System.Windows.Forms.Label Label_MinDuration;
        private System.Windows.Forms.Label Label_Degrees;
        private System.Windows.Forms.Label Label_Hours;
        private System.Windows.Forms.Label Label_SgpDecMinuteColon;
        private System.Windows.Forms.Label Label_SgpRaMinuteColon;
        private System.Windows.Forms.Label Label_SgpDecDegreeColon;
        private System.Windows.Forms.Label Label_SgpRaHourColon;
        private System.Windows.Forms.CheckBox CheckBox_LocalNorth;
        private System.Windows.Forms.CheckBox CheckBox_TargetNorth;
        private System.Windows.Forms.ProgressBar ProgressBar_ProcessObject;
        private System.Windows.Forms.RadioButton RadioButton_Optimal;
        private System.Windows.Forms.RadioButton RadioButton_Year;
        private System.Windows.Forms.RadioButton RadioButton_Day;
        private System.Windows.Forms.GroupBox GroupBox_TargetSelection;
        private System.Windows.Forms.MenuStrip MenuStrip_MainForm;
        private System.Windows.Forms.ToolStripMenuItem FileToolStripMenuItem_MainForm;
        private System.Windows.Forms.GroupBox GroupBox_AltitudeChart;
        private System.Windows.Forms.Label Label_TargetName;
        private System.Windows.Forms.Label Label_Percent;
        private System.Windows.Forms.Label Label_Phase;
        private System.Windows.Forms.CheckBox CheckBox_ChartDuration;
        private System.Windows.Forms.ComboBox ComboBox_SelectTarget;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Button Button_SelectAllTargets;
        private System.Windows.Forms.Button Button_ClearAllTargets;
    }
}

