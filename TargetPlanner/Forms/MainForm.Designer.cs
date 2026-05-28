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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            GroupBox_Local = new System.Windows.Forms.GroupBox();
            GroupBox_Location = new System.Windows.Forms.GroupBox();
            ComboBox_TimeZone = new System.Windows.Forms.ComboBox();
            Label_TimeZone = new System.Windows.Forms.Label();
            Button_BrowseLocalHorizon = new System.Windows.Forms.Button();
            Label_HorizonPath = new System.Windows.Forms.Label();
            Label_Extinction = new System.Windows.Forms.Label();
            Label_Bortle = new System.Windows.Forms.Label();
            ComboBox_Bortle = new System.Windows.Forms.ComboBox();
            NumericUpDown_Extinction = new System.Windows.Forms.NumericUpDown();
            Label_LocalMeters = new System.Windows.Forms.Label();
            NumericUpDown_LocalElevation = new System.Windows.Forms.NumericUpDown();
            Label_LocalElevation = new System.Windows.Forms.Label();
            CheckBox_LocalNorth = new System.Windows.Forms.CheckBox();
            Label_Location = new System.Windows.Forms.Label();
            ComboBox_Location = new System.Windows.Forms.ComboBox();
            NumericUpDown_LatitudeMinutes = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_LatitudeDegrees = new System.Windows.Forms.NumericUpDown();
            Label_LocalLatitudeText = new System.Windows.Forms.Label();
            Label_LocalLongitudeText = new System.Windows.Forms.Label();
            NumericUpDown_LongitudeDegrees = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_LongitudeMinutes = new System.Windows.Forms.NumericUpDown();
            Label_LocalLatDegreeColon = new System.Windows.Forms.Label();
            CheckBox_LocalWest = new System.Windows.Forms.CheckBox();
            Label_LocalLonDegreeColon = new System.Windows.Forms.Label();
            TextBox_Longitude = new System.Windows.Forms.TextBox();
            Label_LocalLatMinuteColon = new System.Windows.Forms.Label();
            TextBox_Latitude = new System.Windows.Forms.TextBox();
            Label_LocalLonMinuteColon = new System.Windows.Forms.Label();
            NumericUpDown_LongitudeSeconds = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_LatitudeSeconds = new System.Windows.Forms.NumericUpDown();
            GroupBox_LocalDateTime = new System.Windows.Forms.GroupBox();
            Label_Phase = new System.Windows.Forms.Label();
            DatePicker = new System.Windows.Forms.DateTimePicker();
            Label_LunarPhaseValue = new System.Windows.Forms.Label();
            Button_Now = new System.Windows.Forms.Button();
            Label_SunAltitudeValue = new System.Windows.Forms.Label();
            Label_AstronomicalDusk = new System.Windows.Forms.Label();
            Label_SunAltitude = new System.Windows.Forms.Label();
            Label_AstronomicalDawnValue = new System.Windows.Forms.Label();
            Label_MoonSetValue = new System.Windows.Forms.Label();
            Label_AstronomicalDuskValue = new System.Windows.Forms.Label();
            Label_MoonSetTimeText = new System.Windows.Forms.Label();
            Label_AstronomicalDawn = new System.Windows.Forms.Label();
            Label_MoonRiseValue = new System.Windows.Forms.Label();
            Label_MoonAltitude = new System.Windows.Forms.Label();
            Label_MoonRise = new System.Windows.Forms.Label();
            Label_LunarAltitudeValue = new System.Windows.Forms.Label();
            Label_LunarIlluminationFractionValue = new System.Windows.Forms.Label();
            Label_LunarIlluminationFraction = new System.Windows.Forms.Label();
            Label_TargetHours = new System.Windows.Forms.Label();
            Label_TargetDuration = new System.Windows.Forms.Label();
            Label_TargetFloor = new System.Windows.Forms.Label();
            NumericUpDown_TargetFloor = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_TargetDuration = new System.Windows.Forms.NumericUpDown();
            Label_LocalHorizon = new System.Windows.Forms.Label();
            ComboBox_SelectTarget = new System.Windows.Forms.ComboBox();
            Label_TargetName = new System.Windows.Forms.Label();
            CheckBox_TargetNorth = new System.Windows.Forms.CheckBox();
            TextBox_RightAscension = new System.Windows.Forms.TextBox();
            NumericUpDown_RaMinutes = new System.Windows.Forms.NumericUpDown();
            TextBox_Declination = new System.Windows.Forms.TextBox();
            Label_DecMinuteColon = new System.Windows.Forms.Label();
            Button_GraphTarget = new System.Windows.Forms.Button();
            NumericUpDown_RaHours = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_RaSeconds = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_DecMinutes = new System.Windows.Forms.NumericUpDown();
            Label_TargetDeclinationText = new System.Windows.Forms.Label();
            Label_RaHourColon = new System.Windows.Forms.Label();
            Label_RaMinuteColon = new System.Windows.Forms.Label();
            Label_DecDegreeColon = new System.Windows.Forms.Label();
            Label_TargetRightAscensionText = new System.Windows.Forms.Label();
            NumericUpDown_DecSeconds = new System.Windows.Forms.NumericUpDown();
            NumericUpDown_DecDegrees = new System.Windows.Forms.NumericUpDown();
            RadioButton_Sessions = new System.Windows.Forms.RadioButton();
            RadioButton_Year = new System.Windows.Forms.RadioButton();
            RadioButton_Day = new System.Windows.Forms.RadioButton();
            Button_VisibleTargets = new System.Windows.Forms.Button();
            Button_CheckAllTargets = new System.Windows.Forms.Button();
            Button_UnCheckAllTargets = new System.Windows.Forms.Button();
            Button_ClearAllTargets = new System.Windows.Forms.Button();
            Label_SortBy = new System.Windows.Forms.Label();
            ComboBox_SortTargets = new System.Windows.Forms.ComboBox();
            CheckedListBox_SelectedTargets = new TargetPlanner.Forms.DupeAwareCheckedListBox();
            Button_BrowseTargetList = new System.Windows.Forms.Button();
            Button_LoadImageLibrary = new System.Windows.Forms.Button();
            Button_LoadJsonTargets = new System.Windows.Forms.Button();
            GroupBox_Target = new System.Windows.Forms.GroupBox();
            Button_RemoveTarget = new System.Windows.Forms.Button();
            Button_AddTarget = new System.Windows.Forms.Button();
            Button_CheckedTargets = new System.Windows.Forms.Button();
            GroupBox_MoonAvoidance = new System.Windows.Forms.GroupBox();
            Label_Moon_WidthDays = new System.Windows.Forms.Label();
            CheckBox_Moon_AvoidanceEnable = new System.Windows.Forms.CheckBox();
            Label_Moon_Separation = new System.Windows.Forms.Label();
            NumericUpDown_Moon_Separation = new System.Windows.Forms.NumericUpDown();
            GroupBox_Moon_Filters = new System.Windows.Forms.GroupBox();
            Label_Moon_Width = new System.Windows.Forms.Label();
            NumericUpDown_Moon_Width = new System.Windows.Forms.NumericUpDown();
            CheckBox_Moon_RelaxEnabled = new System.Windows.Forms.CheckBox();
            Label_Moon_RelaxMin = new System.Windows.Forms.Label();
            NumericUpDown_Moon_RelaxMin = new System.Windows.Forms.NumericUpDown();
            Label_Moon_RelaxMax = new System.Windows.Forms.Label();
            NumericUpDown_Moon_RelaxMax = new System.Windows.Forms.NumericUpDown();
            Label_Moon_RelaxScale = new System.Windows.Forms.Label();
            NumericUpDown_Moon_RelaxScale = new System.Windows.Forms.NumericUpDown();
            MenuStrip_MainForm = new System.Windows.Forms.MenuStrip();
            FileToolStripMenuItem_MainForm = new System.Windows.Forms.ToolStripMenuItem();
            FiltersToolStripMenuItem_MainForm = new System.Windows.Forms.ToolStripMenuItem();
            HelpToolStripMenuItem_MainForm = new System.Windows.Forms.ToolStripMenuItem();
            CheckUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            AboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            GroupBox_Altitude = new System.Windows.Forms.GroupBox();
            CheckBox_Sky = new System.Windows.Forms.CheckBox();
            ProgressBar_Processing = new System.Windows.Forms.ProgressBar();
            GroupBox_Local.SuspendLayout();
            GroupBox_Location.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Extinction).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LocalElevation).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeMinutes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeDegrees).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeDegrees).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeMinutes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeSeconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeSeconds).BeginInit();
            GroupBox_LocalDateTime.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_TargetFloor).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_TargetDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaMinutes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaHours).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaSeconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecMinutes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecSeconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecDegrees).BeginInit();
            GroupBox_Target.SuspendLayout();
            GroupBox_MoonAvoidance.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_Separation).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_Width).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxScale).BeginInit();
            MenuStrip_MainForm.SuspendLayout();
            GroupBox_Altitude.SuspendLayout();
            SuspendLayout();
            // 
            // GroupBox_Local
            // 
            GroupBox_Local.Controls.Add(GroupBox_Location);
            GroupBox_Local.Controls.Add(GroupBox_LocalDateTime);
            GroupBox_Local.Location = new System.Drawing.Point(15, 35);
            GroupBox_Local.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Local.Name = "GroupBox_Local";
            GroupBox_Local.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Local.Size = new System.Drawing.Size(588, 389);
            GroupBox_Local.TabIndex = 0;
            GroupBox_Local.TabStop = false;
            GroupBox_Local.Text = "Local";
            // 
            // GroupBox_Location
            // 
            GroupBox_Location.Controls.Add(ComboBox_TimeZone);
            GroupBox_Location.Controls.Add(Label_TimeZone);
            GroupBox_Location.Controls.Add(Button_BrowseLocalHorizon);
            GroupBox_Location.Controls.Add(Label_HorizonPath);
            GroupBox_Location.Controls.Add(Label_Extinction);
            GroupBox_Location.Controls.Add(Label_Bortle);
            GroupBox_Location.Controls.Add(ComboBox_Bortle);
            GroupBox_Location.Controls.Add(NumericUpDown_Extinction);
            GroupBox_Location.Controls.Add(Label_LocalMeters);
            GroupBox_Location.Controls.Add(NumericUpDown_LocalElevation);
            GroupBox_Location.Controls.Add(Label_LocalElevation);
            GroupBox_Location.Controls.Add(CheckBox_LocalNorth);
            GroupBox_Location.Controls.Add(Label_Location);
            GroupBox_Location.Controls.Add(ComboBox_Location);
            GroupBox_Location.Controls.Add(NumericUpDown_LatitudeMinutes);
            GroupBox_Location.Controls.Add(NumericUpDown_LatitudeDegrees);
            GroupBox_Location.Controls.Add(Label_LocalLatitudeText);
            GroupBox_Location.Controls.Add(Label_LocalLongitudeText);
            GroupBox_Location.Controls.Add(NumericUpDown_LongitudeDegrees);
            GroupBox_Location.Controls.Add(NumericUpDown_LongitudeMinutes);
            GroupBox_Location.Controls.Add(Label_LocalLatDegreeColon);
            GroupBox_Location.Controls.Add(CheckBox_LocalWest);
            GroupBox_Location.Controls.Add(Label_LocalLonDegreeColon);
            GroupBox_Location.Controls.Add(TextBox_Longitude);
            GroupBox_Location.Controls.Add(Label_LocalLatMinuteColon);
            GroupBox_Location.Controls.Add(TextBox_Latitude);
            GroupBox_Location.Controls.Add(Label_LocalLonMinuteColon);
            GroupBox_Location.Controls.Add(NumericUpDown_LongitudeSeconds);
            GroupBox_Location.Controls.Add(NumericUpDown_LatitudeSeconds);
            GroupBox_Location.Location = new System.Drawing.Point(21, 22);
            GroupBox_Location.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Location.Name = "GroupBox_Location";
            GroupBox_Location.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Location.Size = new System.Drawing.Size(548, 182);
            GroupBox_Location.TabIndex = 30;
            GroupBox_Location.TabStop = false;
            GroupBox_Location.Text = "Location";
            // 
            // ComboBox_TimeZone
            // 
            ComboBox_TimeZone.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            ComboBox_TimeZone.FormattingEnabled = true;
            ComboBox_TimeZone.Location = new System.Drawing.Point(291, 19);
            ComboBox_TimeZone.Name = "ComboBox_TimeZone";
            ComboBox_TimeZone.Size = new System.Drawing.Size(242, 23);
            ComboBox_TimeZone.TabIndex = 48;
            // 
            // Label_TimeZone
            // 
            Label_TimeZone.AutoSize = true;
            Label_TimeZone.Location = new System.Drawing.Point(221, 23);
            Label_TimeZone.Name = "Label_TimeZone";
            Label_TimeZone.Size = new System.Drawing.Size(67, 15);
            Label_TimeZone.TabIndex = 45;
            Label_TimeZone.Text = "Time Zone:";
            // 
            // Button_BrowseLocalHorizon
            // 
            Button_BrowseLocalHorizon.Location = new System.Drawing.Point(148, 145);
            Button_BrowseLocalHorizon.Name = "Button_BrowseLocalHorizon";
            Button_BrowseLocalHorizon.Size = new System.Drawing.Size(130, 23);
            Button_BrowseLocalHorizon.TabIndex = 46;
            Button_BrowseLocalHorizon.Text = "Local Horizon";
            Button_BrowseLocalHorizon.UseVisualStyleBackColor = true;
            Button_BrowseLocalHorizon.Click += Button_BrowseHorizon_Click;
            // 
            // Label_HorizonPath
            // 
            Label_HorizonPath.AutoEllipsis = true;
            Label_HorizonPath.Location = new System.Drawing.Point(284, 147);
            Label_HorizonPath.Name = "Label_HorizonPath";
            Label_HorizonPath.Size = new System.Drawing.Size(163, 18);
            Label_HorizonPath.TabIndex = 47;
            Label_HorizonPath.Text = "(no local horizon)";
            Label_HorizonPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_Extinction
            // 
            Label_Extinction.AutoSize = true;
            Label_Extinction.Location = new System.Drawing.Point(436, 115);
            Label_Extinction.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Extinction.Name = "Label_Extinction";
            Label_Extinction.Size = new System.Drawing.Size(80, 15);
            Label_Extinction.TabIndex = 43;
            Label_Extinction.Text = "Sky Extinction";
            // 
            // Label_Bortle
            // 
            Label_Bortle.AutoSize = true;
            Label_Bortle.Location = new System.Drawing.Point(258, 115);
            Label_Bortle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Bortle.Name = "Label_Bortle";
            Label_Bortle.Size = new System.Drawing.Size(38, 15);
            Label_Bortle.TabIndex = 42;
            Label_Bortle.Text = "Bortle";
            // 
            // ComboBox_Bortle
            // 
            ComboBox_Bortle.FormattingEnabled = true;
            ComboBox_Bortle.Location = new System.Drawing.Point(301, 111);
            ComboBox_Bortle.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            ComboBox_Bortle.Name = "ComboBox_Bortle";
            ComboBox_Bortle.Size = new System.Drawing.Size(48, 23);
            ComboBox_Bortle.TabIndex = 41;
            // 
            // NumericUpDown_Extinction
            // 
            NumericUpDown_Extinction.DecimalPlaces = 2;
            NumericUpDown_Extinction.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            NumericUpDown_Extinction.Location = new System.Drawing.Point(357, 111);
            NumericUpDown_Extinction.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Extinction.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            NumericUpDown_Extinction.Name = "NumericUpDown_Extinction";
            NumericUpDown_Extinction.Size = new System.Drawing.Size(76, 23);
            NumericUpDown_Extinction.TabIndex = 29;
            NumericUpDown_Extinction.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_LocalMeters
            // 
            Label_LocalMeters.AutoSize = true;
            Label_LocalMeters.Location = new System.Drawing.Point(167, 115);
            Label_LocalMeters.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalMeters.Name = "Label_LocalMeters";
            Label_LocalMeters.Size = new System.Drawing.Size(43, 15);
            Label_LocalMeters.TabIndex = 28;
            Label_LocalMeters.Text = "Meters";
            // 
            // NumericUpDown_LocalElevation
            // 
            NumericUpDown_LocalElevation.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            NumericUpDown_LocalElevation.Location = new System.Drawing.Point(99, 111);
            NumericUpDown_LocalElevation.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LocalElevation.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            NumericUpDown_LocalElevation.Minimum = new decimal(new int[] { 100, 0, 0, int.MinValue });
            NumericUpDown_LocalElevation.Name = "NumericUpDown_LocalElevation";
            NumericUpDown_LocalElevation.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_LocalElevation.TabIndex = 27;
            NumericUpDown_LocalElevation.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_LocalElevation.ValueChanged += NumericUpDown_LocalElevation_ValueChanged;
            // 
            // Label_LocalElevation
            // 
            Label_LocalElevation.AutoSize = true;
            Label_LocalElevation.Location = new System.Drawing.Point(37, 115);
            Label_LocalElevation.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalElevation.Name = "Label_LocalElevation";
            Label_LocalElevation.Size = new System.Drawing.Size(55, 15);
            Label_LocalElevation.TabIndex = 26;
            Label_LocalElevation.Text = "Elevation";
            // 
            // CheckBox_LocalNorth
            // 
            CheckBox_LocalNorth.AutoSize = true;
            CheckBox_LocalNorth.Checked = true;
            CheckBox_LocalNorth.CheckState = System.Windows.Forms.CheckState.Checked;
            CheckBox_LocalNorth.Location = new System.Drawing.Point(451, 60);
            CheckBox_LocalNorth.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_LocalNorth.Name = "CheckBox_LocalNorth";
            CheckBox_LocalNorth.Size = new System.Drawing.Size(57, 19);
            CheckBox_LocalNorth.TabIndex = 25;
            CheckBox_LocalNorth.Text = "North";
            CheckBox_LocalNorth.UseVisualStyleBackColor = true;
            // 
            // Label_Location
            // 
            Label_Location.AutoSize = true;
            Label_Location.Location = new System.Drawing.Point(14, 23);
            Label_Location.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Location.Name = "Label_Location";
            Label_Location.Size = new System.Drawing.Size(56, 15);
            Label_Location.TabIndex = 20;
            Label_Location.Text = "Location:";
            // 
            // ComboBox_Location
            // 
            ComboBox_Location.FormattingEnabled = true;
            ComboBox_Location.Location = new System.Drawing.Point(73, 19);
            ComboBox_Location.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            ComboBox_Location.Name = "ComboBox_Location";
            ComboBox_Location.Size = new System.Drawing.Size(140, 23);
            ComboBox_Location.TabIndex = 1;
            ComboBox_Location.DropDown += ComboBox_Location_DropDown;
            ComboBox_Location.SelectedIndexChanged += ComboBox_Location_SelectionIndexChanged;
            // 
            // NumericUpDown_LatitudeMinutes
            // 
            NumericUpDown_LatitudeMinutes.AllowDrop = true;
            NumericUpDown_LatitudeMinutes.Location = new System.Drawing.Point(187, 58);
            NumericUpDown_LatitudeMinutes.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LatitudeMinutes.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_LatitudeMinutes.Minimum = new decimal(new int[] { 1, 0, 0, int.MinValue });
            NumericUpDown_LatitudeMinutes.Name = "NumericUpDown_LatitudeMinutes";
            NumericUpDown_LatitudeMinutes.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_LatitudeMinutes.TabIndex = 3;
            NumericUpDown_LatitudeMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_LatitudeDegrees
            // 
            NumericUpDown_LatitudeDegrees.AllowDrop = true;
            NumericUpDown_LatitudeDegrees.Location = new System.Drawing.Point(99, 58);
            NumericUpDown_LatitudeDegrees.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LatitudeDegrees.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            NumericUpDown_LatitudeDegrees.Minimum = new decimal(new int[] { 90, 0, 0, int.MinValue });
            NumericUpDown_LatitudeDegrees.Name = "NumericUpDown_LatitudeDegrees";
            NumericUpDown_LatitudeDegrees.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_LatitudeDegrees.TabIndex = 2;
            NumericUpDown_LatitudeDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_LocalLatitudeText
            // 
            Label_LocalLatitudeText.AutoSize = true;
            Label_LocalLatitudeText.Location = new System.Drawing.Point(44, 62);
            Label_LocalLatitudeText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLatitudeText.Name = "Label_LocalLatitudeText";
            Label_LocalLatitudeText.Size = new System.Drawing.Size(50, 15);
            Label_LocalLatitudeText.TabIndex = 3;
            Label_LocalLatitudeText.Text = "Latitude";
            // 
            // Label_LocalLongitudeText
            // 
            Label_LocalLongitudeText.AutoSize = true;
            Label_LocalLongitudeText.Location = new System.Drawing.Point(34, 89);
            Label_LocalLongitudeText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLongitudeText.Name = "Label_LocalLongitudeText";
            Label_LocalLongitudeText.Size = new System.Drawing.Size(61, 15);
            Label_LocalLongitudeText.TabIndex = 4;
            Label_LocalLongitudeText.Text = "Longitude";
            // 
            // NumericUpDown_LongitudeDegrees
            // 
            NumericUpDown_LongitudeDegrees.AllowDrop = true;
            NumericUpDown_LongitudeDegrees.Location = new System.Drawing.Point(99, 84);
            NumericUpDown_LongitudeDegrees.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LongitudeDegrees.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            NumericUpDown_LongitudeDegrees.Name = "NumericUpDown_LongitudeDegrees";
            NumericUpDown_LongitudeDegrees.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_LongitudeDegrees.TabIndex = 6;
            NumericUpDown_LongitudeDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_LongitudeMinutes
            // 
            NumericUpDown_LongitudeMinutes.AllowDrop = true;
            NumericUpDown_LongitudeMinutes.Location = new System.Drawing.Point(187, 84);
            NumericUpDown_LongitudeMinutes.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LongitudeMinutes.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_LongitudeMinutes.Minimum = new decimal(new int[] { 1, 0, 0, int.MinValue });
            NumericUpDown_LongitudeMinutes.Name = "NumericUpDown_LongitudeMinutes";
            NumericUpDown_LongitudeMinutes.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_LongitudeMinutes.TabIndex = 7;
            NumericUpDown_LongitudeMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_LocalLatDegreeColon
            // 
            Label_LocalLatDegreeColon.AutoSize = true;
            Label_LocalLatDegreeColon.Location = new System.Drawing.Point(169, 62);
            Label_LocalLatDegreeColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLatDegreeColon.Name = "Label_LocalLatDegreeColon";
            Label_LocalLatDegreeColon.Size = new System.Drawing.Size(10, 15);
            Label_LocalLatDegreeColon.TabIndex = 8;
            Label_LocalLatDegreeColon.Text = ":";
            // 
            // CheckBox_LocalWest
            // 
            CheckBox_LocalWest.AutoSize = true;
            CheckBox_LocalWest.Checked = true;
            CheckBox_LocalWest.CheckState = System.Windows.Forms.CheckState.Checked;
            CheckBox_LocalWest.Location = new System.Drawing.Point(451, 87);
            CheckBox_LocalWest.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_LocalWest.Name = "CheckBox_LocalWest";
            CheckBox_LocalWest.Size = new System.Drawing.Size(52, 19);
            CheckBox_LocalWest.TabIndex = 10;
            CheckBox_LocalWest.Text = "West";
            CheckBox_LocalWest.UseVisualStyleBackColor = true;
            // 
            // Label_LocalLonDegreeColon
            // 
            Label_LocalLonDegreeColon.AutoSize = true;
            Label_LocalLonDegreeColon.Location = new System.Drawing.Point(169, 89);
            Label_LocalLonDegreeColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLonDegreeColon.Name = "Label_LocalLonDegreeColon";
            Label_LocalLonDegreeColon.Size = new System.Drawing.Size(10, 15);
            Label_LocalLonDegreeColon.TabIndex = 9;
            Label_LocalLonDegreeColon.Text = ":";
            // 
            // TextBox_Longitude
            // 
            TextBox_Longitude.Location = new System.Drawing.Point(357, 84);
            TextBox_Longitude.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            TextBox_Longitude.Name = "TextBox_Longitude";
            TextBox_Longitude.Size = new System.Drawing.Size(86, 23);
            TextBox_Longitude.TabIndex = 9;
            TextBox_Longitude.Text = " ";
            TextBox_Longitude.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_LocalLatMinuteColon
            // 
            Label_LocalLatMinuteColon.AutoSize = true;
            Label_LocalLatMinuteColon.Location = new System.Drawing.Point(257, 62);
            Label_LocalLatMinuteColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLatMinuteColon.Name = "Label_LocalLatMinuteColon";
            Label_LocalLatMinuteColon.Size = new System.Drawing.Size(10, 15);
            Label_LocalLatMinuteColon.TabIndex = 10;
            Label_LocalLatMinuteColon.Text = ":";
            // 
            // TextBox_Latitude
            // 
            TextBox_Latitude.AcceptsReturn = true;
            TextBox_Latitude.AllowDrop = true;
            TextBox_Latitude.Location = new System.Drawing.Point(357, 58);
            TextBox_Latitude.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            TextBox_Latitude.MaxLength = 20;
            TextBox_Latitude.Name = "TextBox_Latitude";
            TextBox_Latitude.Size = new System.Drawing.Size(86, 23);
            TextBox_Latitude.TabIndex = 5;
            TextBox_Latitude.Text = " ";
            TextBox_Latitude.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_LocalLonMinuteColon
            // 
            Label_LocalLonMinuteColon.AutoSize = true;
            Label_LocalLonMinuteColon.Location = new System.Drawing.Point(257, 89);
            Label_LocalLonMinuteColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalLonMinuteColon.Name = "Label_LocalLonMinuteColon";
            Label_LocalLonMinuteColon.Size = new System.Drawing.Size(10, 15);
            Label_LocalLonMinuteColon.TabIndex = 11;
            Label_LocalLonMinuteColon.Text = ":";
            // 
            // NumericUpDown_LongitudeSeconds
            // 
            NumericUpDown_LongitudeSeconds.AllowDrop = true;
            NumericUpDown_LongitudeSeconds.DecimalPlaces = 2;
            NumericUpDown_LongitudeSeconds.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            NumericUpDown_LongitudeSeconds.Location = new System.Drawing.Point(274, 84);
            NumericUpDown_LongitudeSeconds.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LongitudeSeconds.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_LongitudeSeconds.Minimum = new decimal(new int[] { 1, 0, 0, -2147352576 });
            NumericUpDown_LongitudeSeconds.Name = "NumericUpDown_LongitudeSeconds";
            NumericUpDown_LongitudeSeconds.Size = new System.Drawing.Size(76, 23);
            NumericUpDown_LongitudeSeconds.TabIndex = 8;
            NumericUpDown_LongitudeSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_LatitudeSeconds
            // 
            NumericUpDown_LatitudeSeconds.AllowDrop = true;
            NumericUpDown_LatitudeSeconds.DecimalPlaces = 2;
            NumericUpDown_LatitudeSeconds.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            NumericUpDown_LatitudeSeconds.Location = new System.Drawing.Point(274, 58);
            NumericUpDown_LatitudeSeconds.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_LatitudeSeconds.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_LatitudeSeconds.Minimum = new decimal(new int[] { 1, 0, 0, -2147352576 });
            NumericUpDown_LatitudeSeconds.Name = "NumericUpDown_LatitudeSeconds";
            NumericUpDown_LatitudeSeconds.Size = new System.Drawing.Size(76, 23);
            NumericUpDown_LatitudeSeconds.TabIndex = 4;
            NumericUpDown_LatitudeSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // GroupBox_LocalDateTime
            // 
            GroupBox_LocalDateTime.Controls.Add(Label_Phase);
            GroupBox_LocalDateTime.Controls.Add(DatePicker);
            GroupBox_LocalDateTime.Controls.Add(Label_LunarPhaseValue);
            GroupBox_LocalDateTime.Controls.Add(Button_Now);
            GroupBox_LocalDateTime.Controls.Add(Label_SunAltitudeValue);
            GroupBox_LocalDateTime.Controls.Add(Label_AstronomicalDusk);
            GroupBox_LocalDateTime.Controls.Add(Label_SunAltitude);
            GroupBox_LocalDateTime.Controls.Add(Label_AstronomicalDawnValue);
            GroupBox_LocalDateTime.Controls.Add(Label_MoonSetValue);
            GroupBox_LocalDateTime.Controls.Add(Label_AstronomicalDuskValue);
            GroupBox_LocalDateTime.Controls.Add(Label_MoonSetTimeText);
            GroupBox_LocalDateTime.Controls.Add(Label_AstronomicalDawn);
            GroupBox_LocalDateTime.Controls.Add(Label_MoonRiseValue);
            GroupBox_LocalDateTime.Controls.Add(Label_MoonAltitude);
            GroupBox_LocalDateTime.Controls.Add(Label_MoonRise);
            GroupBox_LocalDateTime.Controls.Add(Label_LunarAltitudeValue);
            GroupBox_LocalDateTime.Controls.Add(Label_LunarIlluminationFractionValue);
            GroupBox_LocalDateTime.Controls.Add(Label_LunarIlluminationFraction);
            GroupBox_LocalDateTime.Location = new System.Drawing.Point(21, 211);
            GroupBox_LocalDateTime.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_LocalDateTime.Name = "GroupBox_LocalDateTime";
            GroupBox_LocalDateTime.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_LocalDateTime.Size = new System.Drawing.Size(547, 153);
            GroupBox_LocalDateTime.TabIndex = 29;
            GroupBox_LocalDateTime.TabStop = false;
            GroupBox_LocalDateTime.Text = "Date and Time";
            // 
            // Label_Phase
            // 
            Label_Phase.AutoSize = true;
            Label_Phase.Location = new System.Drawing.Point(294, 129);
            Label_Phase.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Phase.Name = "Label_Phase";
            Label_Phase.Size = new System.Drawing.Size(74, 15);
            Label_Phase.TabIndex = 41;
            Label_Phase.Text = "Lunar Phase:";
            // 
            // DatePicker
            // 
            DatePicker.Location = new System.Drawing.Point(204, 31);
            DatePicker.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            DatePicker.Name = "DatePicker";
            DatePicker.Size = new System.Drawing.Size(243, 23);
            DatePicker.TabIndex = 0;
            DatePicker.ValueChanged += DatePicker_ValueChanged;
            // 
            // Label_LunarPhaseValue
            // 
            Label_LunarPhaseValue.AutoSize = true;
            Label_LunarPhaseValue.Location = new System.Drawing.Point(372, 129);
            Label_LunarPhaseValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LunarPhaseValue.Name = "Label_LunarPhaseValue";
            Label_LunarPhaseValue.Size = new System.Drawing.Size(14, 15);
            Label_LunarPhaseValue.TabIndex = 39;
            Label_LunarPhaseValue.Text = "V";
            // 
            // Button_Now
            // 
            Button_Now.Location = new System.Drawing.Point(101, 30);
            Button_Now.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_Now.Name = "Button_Now";
            Button_Now.Size = new System.Drawing.Size(77, 27);
            Button_Now.TabIndex = 0;
            Button_Now.Text = "Now";
            Button_Now.UseVisualStyleBackColor = true;
            Button_Now.Click += Button_Now_Click;
            // 
            // Label_SunAltitudeValue
            // 
            Label_SunAltitudeValue.AutoSize = true;
            Label_SunAltitudeValue.Location = new System.Drawing.Point(498, 80);
            Label_SunAltitudeValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_SunAltitudeValue.Name = "Label_SunAltitudeValue";
            Label_SunAltitudeValue.Size = new System.Drawing.Size(14, 15);
            Label_SunAltitudeValue.TabIndex = 38;
            Label_SunAltitudeValue.Text = "V";
            Label_SunAltitudeValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_AstronomicalDusk
            // 
            Label_AstronomicalDusk.AutoSize = true;
            Label_AstronomicalDusk.Location = new System.Drawing.Point(27, 80);
            Label_AstronomicalDusk.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_AstronomicalDusk.Name = "Label_AstronomicalDusk";
            Label_AstronomicalDusk.Size = new System.Drawing.Size(110, 15);
            Label_AstronomicalDusk.TabIndex = 24;
            Label_AstronomicalDusk.Text = "Astronomical Dusk:";
            // 
            // Label_SunAltitude
            // 
            Label_SunAltitude.AutoSize = true;
            Label_SunAltitude.Location = new System.Drawing.Point(408, 80);
            Label_SunAltitude.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_SunAltitude.Name = "Label_SunAltitude";
            Label_SunAltitude.Size = new System.Drawing.Size(75, 15);
            Label_SunAltitude.TabIndex = 37;
            Label_SunAltitude.Text = "Sun Altitude:";
            // 
            // Label_AstronomicalDawnValue
            // 
            Label_AstronomicalDawnValue.AutoSize = true;
            Label_AstronomicalDawnValue.Location = new System.Drawing.Point(342, 80);
            Label_AstronomicalDawnValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_AstronomicalDawnValue.Name = "Label_AstronomicalDawnValue";
            Label_AstronomicalDawnValue.Size = new System.Drawing.Size(14, 15);
            Label_AstronomicalDawnValue.TabIndex = 27;
            Label_AstronomicalDawnValue.Text = "V";
            Label_AstronomicalDawnValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_MoonSetValue
            // 
            Label_MoonSetValue.AutoSize = true;
            Label_MoonSetValue.Location = new System.Drawing.Point(342, 103);
            Label_MoonSetValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_MoonSetValue.Name = "Label_MoonSetValue";
            Label_MoonSetValue.Size = new System.Drawing.Size(14, 15);
            Label_MoonSetValue.TabIndex = 36;
            Label_MoonSetValue.Text = "V";
            Label_MoonSetValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_AstronomicalDuskValue
            // 
            Label_AstronomicalDuskValue.AutoSize = true;
            Label_AstronomicalDuskValue.Location = new System.Drawing.Point(145, 80);
            Label_AstronomicalDuskValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_AstronomicalDuskValue.Name = "Label_AstronomicalDuskValue";
            Label_AstronomicalDuskValue.Size = new System.Drawing.Size(14, 15);
            Label_AstronomicalDuskValue.TabIndex = 25;
            Label_AstronomicalDuskValue.Text = "V";
            Label_AstronomicalDuskValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_MoonSetTimeText
            // 
            Label_MoonSetTimeText.AutoSize = true;
            Label_MoonSetTimeText.Location = new System.Drawing.Point(219, 103);
            Label_MoonSetTimeText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_MoonSetTimeText.Name = "Label_MoonSetTimeText";
            Label_MoonSetTimeText.Size = new System.Drawing.Size(61, 15);
            Label_MoonSetTimeText.TabIndex = 35;
            Label_MoonSetTimeText.Text = "Moon Set:";
            // 
            // Label_AstronomicalDawn
            // 
            Label_AstronomicalDawn.AutoSize = true;
            Label_AstronomicalDawn.Location = new System.Drawing.Point(219, 80);
            Label_AstronomicalDawn.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_AstronomicalDawn.Name = "Label_AstronomicalDawn";
            Label_AstronomicalDawn.Size = new System.Drawing.Size(114, 15);
            Label_AstronomicalDawn.TabIndex = 26;
            Label_AstronomicalDawn.Text = "Astronomical Dawn:";
            // 
            // Label_MoonRiseValue
            // 
            Label_MoonRiseValue.AutoSize = true;
            Label_MoonRiseValue.Location = new System.Drawing.Point(145, 103);
            Label_MoonRiseValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_MoonRiseValue.Name = "Label_MoonRiseValue";
            Label_MoonRiseValue.Size = new System.Drawing.Size(14, 15);
            Label_MoonRiseValue.TabIndex = 34;
            Label_MoonRiseValue.Text = "V";
            Label_MoonRiseValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_MoonAltitude
            // 
            Label_MoonAltitude.AutoSize = true;
            Label_MoonAltitude.Location = new System.Drawing.Point(408, 103);
            Label_MoonAltitude.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_MoonAltitude.Name = "Label_MoonAltitude";
            Label_MoonAltitude.Size = new System.Drawing.Size(87, 15);
            Label_MoonAltitude.TabIndex = 29;
            Label_MoonAltitude.Text = "Moon Altitude:";
            // 
            // Label_MoonRise
            // 
            Label_MoonRise.AutoSize = true;
            Label_MoonRise.Location = new System.Drawing.Point(27, 103);
            Label_MoonRise.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_MoonRise.Name = "Label_MoonRise";
            Label_MoonRise.Size = new System.Drawing.Size(66, 15);
            Label_MoonRise.TabIndex = 33;
            Label_MoonRise.Text = "Moon Rise:";
            // 
            // Label_LunarAltitudeValue
            // 
            Label_LunarAltitudeValue.AutoSize = true;
            Label_LunarAltitudeValue.Location = new System.Drawing.Point(498, 103);
            Label_LunarAltitudeValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LunarAltitudeValue.Name = "Label_LunarAltitudeValue";
            Label_LunarAltitudeValue.Size = new System.Drawing.Size(14, 15);
            Label_LunarAltitudeValue.TabIndex = 30;
            Label_LunarAltitudeValue.Text = "V";
            Label_LunarAltitudeValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // Label_LunarIlluminationFractionValue
            // 
            Label_LunarIlluminationFractionValue.AutoSize = true;
            Label_LunarIlluminationFractionValue.Location = new System.Drawing.Point(196, 129);
            Label_LunarIlluminationFractionValue.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LunarIlluminationFractionValue.Name = "Label_LunarIlluminationFractionValue";
            Label_LunarIlluminationFractionValue.Size = new System.Drawing.Size(14, 15);
            Label_LunarIlluminationFractionValue.TabIndex = 32;
            Label_LunarIlluminationFractionValue.Text = "V";
            Label_LunarIlluminationFractionValue.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // Label_LunarIlluminationFraction
            // 
            Label_LunarIlluminationFraction.AutoSize = true;
            Label_LunarIlluminationFraction.Location = new System.Drawing.Point(89, 129);
            Label_LunarIlluminationFraction.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LunarIlluminationFraction.Name = "Label_LunarIlluminationFraction";
            Label_LunarIlluminationFraction.Size = new System.Drawing.Size(107, 15);
            Label_LunarIlluminationFraction.TabIndex = 31;
            Label_LunarIlluminationFraction.Text = "Lunar Illumination:";
            // 
            // Label_TargetHours
            // 
            Label_TargetHours.AutoSize = true;
            Label_TargetHours.Location = new System.Drawing.Point(500, 213);
            Label_TargetHours.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetHours.Name = "Label_TargetHours";
            Label_TargetHours.Size = new System.Drawing.Size(39, 15);
            Label_TargetHours.TabIndex = 24;
            Label_TargetHours.Text = "Hours";
            // 
            // Label_TargetDuration
            // 
            Label_TargetDuration.AutoSize = true;
            Label_TargetDuration.Location = new System.Drawing.Point(323, 213);
            Label_TargetDuration.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetDuration.Name = "Label_TargetDuration";
            Label_TargetDuration.Size = new System.Drawing.Size(92, 15);
            Label_TargetDuration.TabIndex = 23;
            Label_TargetDuration.Text = "Target Duration:";
            Label_TargetDuration.UseWaitCursor = true;
            // 
            // Label_TargetFloor
            // 
            Label_TargetFloor.AutoSize = true;
            Label_TargetFloor.Location = new System.Drawing.Point(245, 213);
            Label_TargetFloor.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetFloor.Name = "Label_TargetFloor";
            Label_TargetFloor.Size = new System.Drawing.Size(49, 15);
            Label_TargetFloor.TabIndex = 22;
            Label_TargetFloor.Text = "Degrees";
            // 
            // NumericUpDown_TargetFloor
            // 
            NumericUpDown_TargetFloor.AllowDrop = true;
            NumericUpDown_TargetFloor.Location = new System.Drawing.Point(178, 209);
            NumericUpDown_TargetFloor.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_TargetFloor.Maximum = new decimal(new int[] { 89, 0, 0, 0 });
            NumericUpDown_TargetFloor.Name = "NumericUpDown_TargetFloor";
            NumericUpDown_TargetFloor.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_TargetFloor.TabIndex = 11;
            NumericUpDown_TargetFloor.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_TargetFloor.Value = new decimal(new int[] { 30, 0, 0, 0 });
            NumericUpDown_TargetFloor.ValueChanged += NumericUpDown_TargetFloor_ValueChanged;
            // 
            // NumericUpDown_TargetDuration
            // 
            NumericUpDown_TargetDuration.AllowDrop = true;
            NumericUpDown_TargetDuration.DecimalPlaces = 2;
            NumericUpDown_TargetDuration.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            NumericUpDown_TargetDuration.Location = new System.Drawing.Point(434, 209);
            NumericUpDown_TargetDuration.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_TargetDuration.Maximum = new decimal(new int[] { 24, 0, 0, 0 });
            NumericUpDown_TargetDuration.Name = "NumericUpDown_TargetDuration";
            NumericUpDown_TargetDuration.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_TargetDuration.TabIndex = 12;
            NumericUpDown_TargetDuration.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_TargetDuration.Value = new decimal(new int[] { 4, 0, 0, 0 });
            NumericUpDown_TargetDuration.ValueChanged += NumericUpDown_TargetDuration_ValueChanged;
            // 
            // Label_LocalHorizon
            // 
            Label_LocalHorizon.AutoSize = true;
            Label_LocalHorizon.Location = new System.Drawing.Point(88, 213);
            Label_LocalHorizon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_LocalHorizon.Name = "Label_LocalHorizon";
            Label_LocalHorizon.Size = new System.Drawing.Size(73, 15);
            Label_LocalHorizon.TabIndex = 21;
            Label_LocalHorizon.Text = "Target Floor:";
            // 
            // ComboBox_SelectTarget
            // 
            ComboBox_SelectTarget.FormattingEnabled = true;
            ComboBox_SelectTarget.Location = new System.Drawing.Point(174, 91);
            ComboBox_SelectTarget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            ComboBox_SelectTarget.Name = "ComboBox_SelectTarget";
            ComboBox_SelectTarget.Size = new System.Drawing.Size(319, 23);
            ComboBox_SelectTarget.TabIndex = 40;
            ComboBox_SelectTarget.SelectedIndexChanged += ComboBox_SelectTarget_SelectedIndexChanged;
            ComboBox_SelectTarget.MouseLeave += ComboBox_SelectTarget_SelectedIndexChanged;
            // 
            // Label_TargetName
            // 
            Label_TargetName.AutoSize = true;
            Label_TargetName.Location = new System.Drawing.Point(83, 96);
            Label_TargetName.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetName.Name = "Label_TargetName";
            Label_TargetName.Size = new System.Drawing.Size(78, 15);
            Label_TargetName.TabIndex = 39;
            Label_TargetName.Text = "Target Name:";
            // 
            // CheckBox_TargetNorth
            // 
            CheckBox_TargetNorth.AutoSize = true;
            CheckBox_TargetNorth.Checked = true;
            CheckBox_TargetNorth.CheckState = System.Windows.Forms.CheckState.Checked;
            CheckBox_TargetNorth.Location = new System.Drawing.Point(503, 155);
            CheckBox_TargetNorth.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_TargetNorth.Name = "CheckBox_TargetNorth";
            CheckBox_TargetNorth.Size = new System.Drawing.Size(57, 19);
            CheckBox_TargetNorth.TabIndex = 26;
            CheckBox_TargetNorth.Text = "North";
            CheckBox_TargetNorth.UseVisualStyleBackColor = true;
            // 
            // TextBox_RightAscension
            // 
            TextBox_RightAscension.AllowDrop = true;
            TextBox_RightAscension.Location = new System.Drawing.Point(407, 126);
            TextBox_RightAscension.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            TextBox_RightAscension.MaxLength = 20;
            TextBox_RightAscension.Name = "TextBox_RightAscension";
            TextBox_RightAscension.Size = new System.Drawing.Size(86, 23);
            TextBox_RightAscension.TabIndex = 16;
            TextBox_RightAscension.Text = " ";
            TextBox_RightAscension.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            TextBox_RightAscension.WordWrap = false;
            // 
            // NumericUpDown_RaMinutes
            // 
            NumericUpDown_RaMinutes.AllowDrop = true;
            NumericUpDown_RaMinutes.Location = new System.Drawing.Point(237, 126);
            NumericUpDown_RaMinutes.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_RaMinutes.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_RaMinutes.Minimum = new decimal(new int[] { 1, 0, 0, int.MinValue });
            NumericUpDown_RaMinutes.Name = "NumericUpDown_RaMinutes";
            NumericUpDown_RaMinutes.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_RaMinutes.TabIndex = 14;
            NumericUpDown_RaMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // TextBox_Declination
            // 
            TextBox_Declination.Location = new System.Drawing.Point(407, 152);
            TextBox_Declination.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            TextBox_Declination.Name = "TextBox_Declination";
            TextBox_Declination.Size = new System.Drawing.Size(86, 23);
            TextBox_Declination.TabIndex = 20;
            TextBox_Declination.Text = " ";
            TextBox_Declination.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_DecMinuteColon
            // 
            Label_DecMinuteColon.AutoSize = true;
            Label_DecMinuteColon.Location = new System.Drawing.Point(307, 157);
            Label_DecMinuteColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_DecMinuteColon.Name = "Label_DecMinuteColon";
            Label_DecMinuteColon.Size = new System.Drawing.Size(10, 15);
            Label_DecMinuteColon.TabIndex = 38;
            Label_DecMinuteColon.Text = ":";
            // 
            // Button_GraphTarget
            // 
            Button_GraphTarget.Location = new System.Drawing.Point(509, 67);
            Button_GraphTarget.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_GraphTarget.Name = "Button_GraphTarget";
            Button_GraphTarget.Size = new System.Drawing.Size(75, 23);
            Button_GraphTarget.TabIndex = 34;
            Button_GraphTarget.Text = "Graph";
            Button_GraphTarget.UseVisualStyleBackColor = true;
            Button_GraphTarget.Click += Button_Graph_Click;
            // 
            // NumericUpDown_RaHours
            // 
            NumericUpDown_RaHours.AllowDrop = true;
            NumericUpDown_RaHours.Location = new System.Drawing.Point(149, 126);
            NumericUpDown_RaHours.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_RaHours.Maximum = new decimal(new int[] { 23, 0, 0, 0 });
            NumericUpDown_RaHours.Name = "NumericUpDown_RaHours";
            NumericUpDown_RaHours.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_RaHours.TabIndex = 13;
            NumericUpDown_RaHours.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_RaSeconds
            // 
            NumericUpDown_RaSeconds.AllowDrop = true;
            NumericUpDown_RaSeconds.DecimalPlaces = 2;
            NumericUpDown_RaSeconds.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            NumericUpDown_RaSeconds.Location = new System.Drawing.Point(324, 126);
            NumericUpDown_RaSeconds.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_RaSeconds.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_RaSeconds.Minimum = new decimal(new int[] { 1, 0, 0, -2147352576 });
            NumericUpDown_RaSeconds.Name = "NumericUpDown_RaSeconds";
            NumericUpDown_RaSeconds.Size = new System.Drawing.Size(76, 23);
            NumericUpDown_RaSeconds.TabIndex = 15;
            NumericUpDown_RaSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_DecMinutes
            // 
            NumericUpDown_DecMinutes.AllowDrop = true;
            NumericUpDown_DecMinutes.Location = new System.Drawing.Point(237, 152);
            NumericUpDown_DecMinutes.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_DecMinutes.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_DecMinutes.Minimum = new decimal(new int[] { 1, 0, 0, int.MinValue });
            NumericUpDown_DecMinutes.Name = "NumericUpDown_DecMinutes";
            NumericUpDown_DecMinutes.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_DecMinutes.TabIndex = 18;
            NumericUpDown_DecMinutes.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // Label_TargetDeclinationText
            // 
            Label_TargetDeclinationText.AutoSize = true;
            Label_TargetDeclinationText.Location = new System.Drawing.Point(114, 157);
            Label_TargetDeclinationText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetDeclinationText.Name = "Label_TargetDeclinationText";
            Label_TargetDeclinationText.Size = new System.Drawing.Size(29, 15);
            Label_TargetDeclinationText.TabIndex = 26;
            Label_TargetDeclinationText.Text = "DEC";
            // 
            // Label_RaHourColon
            // 
            Label_RaHourColon.AutoSize = true;
            Label_RaHourColon.Location = new System.Drawing.Point(219, 130);
            Label_RaHourColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_RaHourColon.Name = "Label_RaHourColon";
            Label_RaHourColon.Size = new System.Drawing.Size(10, 15);
            Label_RaHourColon.TabIndex = 25;
            Label_RaHourColon.Text = ":";
            // 
            // Label_RaMinuteColon
            // 
            Label_RaMinuteColon.AutoSize = true;
            Label_RaMinuteColon.Location = new System.Drawing.Point(307, 130);
            Label_RaMinuteColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_RaMinuteColon.Name = "Label_RaMinuteColon";
            Label_RaMinuteColon.Size = new System.Drawing.Size(10, 15);
            Label_RaMinuteColon.TabIndex = 37;
            Label_RaMinuteColon.Text = ":";
            // 
            // Label_DecDegreeColon
            // 
            Label_DecDegreeColon.AutoSize = true;
            Label_DecDegreeColon.Location = new System.Drawing.Point(219, 157);
            Label_DecDegreeColon.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_DecDegreeColon.Name = "Label_DecDegreeColon";
            Label_DecDegreeColon.Size = new System.Drawing.Size(10, 15);
            Label_DecDegreeColon.TabIndex = 36;
            Label_DecDegreeColon.Text = ":";
            // 
            // Label_TargetRightAscensionText
            // 
            Label_TargetRightAscensionText.AutoSize = true;
            Label_TargetRightAscensionText.Location = new System.Drawing.Point(122, 130);
            Label_TargetRightAscensionText.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_TargetRightAscensionText.Name = "Label_TargetRightAscensionText";
            Label_TargetRightAscensionText.Size = new System.Drawing.Size(22, 15);
            Label_TargetRightAscensionText.TabIndex = 25;
            Label_TargetRightAscensionText.Text = "RA";
            // 
            // NumericUpDown_DecSeconds
            // 
            NumericUpDown_DecSeconds.AllowDrop = true;
            NumericUpDown_DecSeconds.DecimalPlaces = 2;
            NumericUpDown_DecSeconds.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            NumericUpDown_DecSeconds.Location = new System.Drawing.Point(324, 152);
            NumericUpDown_DecSeconds.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_DecSeconds.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_DecSeconds.Minimum = new decimal(new int[] { 1, 0, 0, -2147352576 });
            NumericUpDown_DecSeconds.Name = "NumericUpDown_DecSeconds";
            NumericUpDown_DecSeconds.Size = new System.Drawing.Size(76, 23);
            NumericUpDown_DecSeconds.TabIndex = 19;
            NumericUpDown_DecSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // NumericUpDown_DecDegrees
            // 
            NumericUpDown_DecDegrees.AllowDrop = true;
            NumericUpDown_DecDegrees.Location = new System.Drawing.Point(149, 152);
            NumericUpDown_DecDegrees.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_DecDegrees.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            NumericUpDown_DecDegrees.Minimum = new decimal(new int[] { 90, 0, 0, int.MinValue });
            NumericUpDown_DecDegrees.Name = "NumericUpDown_DecDegrees";
            NumericUpDown_DecDegrees.Size = new System.Drawing.Size(64, 23);
            NumericUpDown_DecDegrees.TabIndex = 17;
            NumericUpDown_DecDegrees.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // RadioButton_Sessions
            // 
            RadioButton_Sessions.AutoSize = true;
            RadioButton_Sessions.Location = new System.Drawing.Point(117, 20);
            RadioButton_Sessions.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            RadioButton_Sessions.Name = "RadioButton_Sessions";
            RadioButton_Sessions.Size = new System.Drawing.Size(69, 19);
            RadioButton_Sessions.TabIndex = 38;
            RadioButton_Sessions.Text = "Sessions";
            RadioButton_Sessions.UseVisualStyleBackColor = true;
            RadioButton_Sessions.CheckedChanged += RadioButton_Sessions_CheckedChanged;
            // 
            // RadioButton_Year
            // 
            RadioButton_Year.AutoSize = true;
            RadioButton_Year.Location = new System.Drawing.Point(196, 20);
            RadioButton_Year.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            RadioButton_Year.Name = "RadioButton_Year";
            RadioButton_Year.Size = new System.Drawing.Size(47, 19);
            RadioButton_Year.TabIndex = 37;
            RadioButton_Year.TabStop = true;
            RadioButton_Year.Text = "Year";
            RadioButton_Year.UseVisualStyleBackColor = true;
            RadioButton_Year.CheckedChanged += RadioButton_Year_CheckedChanged;
            // 
            // RadioButton_Day
            // 
            RadioButton_Day.AutoSize = true;
            RadioButton_Day.Checked = true;
            RadioButton_Day.Location = new System.Drawing.Point(13, 20);
            RadioButton_Day.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            RadioButton_Day.Name = "RadioButton_Day";
            RadioButton_Day.Size = new System.Drawing.Size(45, 19);
            RadioButton_Day.TabIndex = 36;
            RadioButton_Day.TabStop = true;
            RadioButton_Day.Text = "Day";
            RadioButton_Day.UseVisualStyleBackColor = true;
            RadioButton_Day.CheckedChanged += RadioButton_Day_CheckedChanged;
            // 
            // Button_VisibleTargets
            // 
            Button_VisibleTargets.Location = new System.Drawing.Point(1072, 22);
            Button_VisibleTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_VisibleTargets.Name = "Button_VisibleTargets";
            Button_VisibleTargets.Size = new System.Drawing.Size(75, 27);
            Button_VisibleTargets.TabIndex = 7;
            Button_VisibleTargets.Text = "Visible";
            Button_VisibleTargets.UseVisualStyleBackColor = true;
            Button_VisibleTargets.Click += Button_VisibleTonight_Click;
            // 
            // Button_CheckAllTargets
            // 
            Button_CheckAllTargets.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
            Button_CheckAllTargets.Location = new System.Drawing.Point(992, 22);
            Button_CheckAllTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_CheckAllTargets.Name = "Button_CheckAllTargets";
            Button_CheckAllTargets.Size = new System.Drawing.Size(75, 27);
            Button_CheckAllTargets.TabIndex = 9;
            Button_CheckAllTargets.Text = "Check All";
            Button_CheckAllTargets.UseVisualStyleBackColor = true;
            Button_CheckAllTargets.Click += Button_SelectAllTargets_Click;
            // 
            // Button_UnCheckAllTargets
            // 
            Button_UnCheckAllTargets.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
            Button_UnCheckAllTargets.Location = new System.Drawing.Point(912, 22);
            Button_UnCheckAllTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_UnCheckAllTargets.Name = "Button_UnCheckAllTargets";
            Button_UnCheckAllTargets.Size = new System.Drawing.Size(75, 27);
            Button_UnCheckAllTargets.TabIndex = 8;
            Button_UnCheckAllTargets.Text = "UnCheck All";
            Button_UnCheckAllTargets.UseVisualStyleBackColor = true;
            Button_UnCheckAllTargets.Click += Button_UncheckAll_Click;
            // 
            // Button_ClearAllTargets
            // 
            Button_ClearAllTargets.Location = new System.Drawing.Point(489, 24);
            Button_ClearAllTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_ClearAllTargets.Name = "Button_ClearAllTargets";
            Button_ClearAllTargets.Size = new System.Drawing.Size(122, 27);
            Button_ClearAllTargets.TabIndex = 10;
            Button_ClearAllTargets.Text = "Clear All Targets";
            Button_ClearAllTargets.UseVisualStyleBackColor = true;
            Button_ClearAllTargets.Click += Button_ClearAllTargets_Click;
            // 
            // Label_SortBy
            // 
            Label_SortBy.AutoSize = true;
            Label_SortBy.Location = new System.Drawing.Point(629, 28);
            Label_SortBy.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_SortBy.Name = "Label_SortBy";
            Label_SortBy.Size = new System.Drawing.Size(47, 15);
            Label_SortBy.TabIndex = 10;
            Label_SortBy.Text = "Sort by:";
            // 
            // ComboBox_SortTargets
            // 
            ComboBox_SortTargets.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            ComboBox_SortTargets.FormattingEnabled = true;
            ComboBox_SortTargets.Items.AddRange(new object[] { "Name", "Transit", "Rise", "Longest", "Highest" });
            ComboBox_SortTargets.Location = new System.Drawing.Point(685, 24);
            ComboBox_SortTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            ComboBox_SortTargets.Name = "ComboBox_SortTargets";
            ComboBox_SortTargets.Size = new System.Drawing.Size(139, 23);
            ComboBox_SortTargets.TabIndex = 11;
            ComboBox_SortTargets.SelectedIndexChanged += ComboBox_SortTargets_SelectedIndexChanged;
            // 
            // CheckedListBox_SelectedTargets
            // 
            CheckedListBox_SelectedTargets.FormattingEnabled = true;
            CheckedListBox_SelectedTargets.Location = new System.Drawing.Point(629, 54);
            CheckedListBox_SelectedTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckedListBox_SelectedTargets.MultiColumn = true;
            CheckedListBox_SelectedTargets.Name = "CheckedListBox_SelectedTargets";
            CheckedListBox_SelectedTargets.ScrollAlwaysVisible = true;
            CheckedListBox_SelectedTargets.Size = new System.Drawing.Size(598, 310);
            CheckedListBox_SelectedTargets.TabIndex = 4;
            CheckedListBox_SelectedTargets.ThreeDCheckBoxes = true;
            CheckedListBox_SelectedTargets.MouseDoubleClick += CheckedListBox_SelectedTargets_MouseDoubleClick;
            CheckedListBox_SelectedTargets.MouseMove += ShowCheckBoxObjectToolTip;
            // 
            // Button_BrowseTargetList
            // 
            Button_BrowseTargetList.Location = new System.Drawing.Point(379, 24);
            Button_BrowseTargetList.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_BrowseTargetList.Name = "Button_BrowseTargetList";
            Button_BrowseTargetList.Size = new System.Drawing.Size(98, 27);
            Button_BrowseTargetList.TabIndex = 0;
            Button_BrowseTargetList.Text = "Browse";
            Button_BrowseTargetList.UseVisualStyleBackColor = true;
            Button_BrowseTargetList.Click += Button_BrowseTargetList_Click;
            // 
            // Button_LoadImageLibrary
            // 
            Button_LoadImageLibrary.Location = new System.Drawing.Point(10, 24);
            Button_LoadImageLibrary.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_LoadImageLibrary.Name = "Button_LoadImageLibrary";
            Button_LoadImageLibrary.Size = new System.Drawing.Size(171, 27);
            Button_LoadImageLibrary.TabIndex = 1;
            Button_LoadImageLibrary.Text = "Load Image Library Targets";
            Button_LoadImageLibrary.UseVisualStyleBackColor = true;
            Button_LoadImageLibrary.Click += Button_LoadImageLibrary_Click;
            // 
            // Button_LoadJsonTargets
            // 
            Button_LoadJsonTargets.Location = new System.Drawing.Point(193, 24);
            Button_LoadJsonTargets.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Button_LoadJsonTargets.Name = "Button_LoadJsonTargets";
            Button_LoadJsonTargets.Size = new System.Drawing.Size(174, 27);
            Button_LoadJsonTargets.TabIndex = 2;
            Button_LoadJsonTargets.Text = "Load NINA Sequencer Targets";
            Button_LoadJsonTargets.UseVisualStyleBackColor = true;
            Button_LoadJsonTargets.Click += Button_LoadJsonTargets_Click;
            // 
            // GroupBox_Target
            // 
            GroupBox_Target.Controls.Add(Button_RemoveTarget);
            GroupBox_Target.Controls.Add(Button_AddTarget);
            GroupBox_Target.Controls.Add(Button_CheckedTargets);
            GroupBox_Target.Controls.Add(GroupBox_MoonAvoidance);
            GroupBox_Target.Controls.Add(Button_VisibleTargets);
            GroupBox_Target.Controls.Add(Label_TargetHours);
            GroupBox_Target.Controls.Add(Label_RaMinuteColon);
            GroupBox_Target.Controls.Add(Button_GraphTarget);
            GroupBox_Target.Controls.Add(Label_DecDegreeColon);
            GroupBox_Target.Controls.Add(ComboBox_SelectTarget);
            GroupBox_Target.Controls.Add(Label_RaHourColon);
            GroupBox_Target.Controls.Add(Label_TargetName);
            GroupBox_Target.Controls.Add(Label_TargetRightAscensionText);
            GroupBox_Target.Controls.Add(Label_TargetDuration);
            GroupBox_Target.Controls.Add(Label_TargetDeclinationText);
            GroupBox_Target.Controls.Add(NumericUpDown_DecSeconds);
            GroupBox_Target.Controls.Add(CheckBox_TargetNorth);
            GroupBox_Target.Controls.Add(NumericUpDown_DecMinutes);
            GroupBox_Target.Controls.Add(Label_TargetFloor);
            GroupBox_Target.Controls.Add(NumericUpDown_DecDegrees);
            GroupBox_Target.Controls.Add(NumericUpDown_RaSeconds);
            GroupBox_Target.Controls.Add(TextBox_RightAscension);
            GroupBox_Target.Controls.Add(Button_BrowseTargetList);
            GroupBox_Target.Controls.Add(Button_LoadImageLibrary);
            GroupBox_Target.Controls.Add(Button_LoadJsonTargets);
            GroupBox_Target.Controls.Add(Label_LocalHorizon);
            GroupBox_Target.Controls.Add(NumericUpDown_RaHours);
            GroupBox_Target.Controls.Add(Button_CheckAllTargets);
            GroupBox_Target.Controls.Add(CheckedListBox_SelectedTargets);
            GroupBox_Target.Controls.Add(NumericUpDown_TargetFloor);
            GroupBox_Target.Controls.Add(ComboBox_SortTargets);
            GroupBox_Target.Controls.Add(NumericUpDown_RaMinutes);
            GroupBox_Target.Controls.Add(Label_DecMinuteColon);
            GroupBox_Target.Controls.Add(Button_UnCheckAllTargets);
            GroupBox_Target.Controls.Add(Button_ClearAllTargets);
            GroupBox_Target.Controls.Add(Label_SortBy);
            GroupBox_Target.Controls.Add(NumericUpDown_TargetDuration);
            GroupBox_Target.Controls.Add(TextBox_Declination);
            GroupBox_Target.Location = new System.Drawing.Point(623, 35);
            GroupBox_Target.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Target.Name = "GroupBox_Target";
            GroupBox_Target.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Target.Size = new System.Drawing.Size(1254, 389);
            GroupBox_Target.TabIndex = 3;
            GroupBox_Target.TabStop = false;
            GroupBox_Target.Text = "Target";
            // 
            // Button_RemoveTarget
            // 
            Button_RemoveTarget.Location = new System.Drawing.Point(509, 115);
            Button_RemoveTarget.Name = "Button_RemoveTarget";
            Button_RemoveTarget.Size = new System.Drawing.Size(75, 23);
            Button_RemoveTarget.TabIndex = 45;
            Button_RemoveTarget.Text = "Remove";
            Button_RemoveTarget.UseVisualStyleBackColor = true;
            Button_RemoveTarget.Click += Button_RemoveTarget_Click;
            // 
            // Button_AddTarget
            // 
            Button_AddTarget.Location = new System.Drawing.Point(509, 91);
            Button_AddTarget.Name = "Button_AddTarget";
            Button_AddTarget.Size = new System.Drawing.Size(75, 23);
            Button_AddTarget.TabIndex = 44;
            Button_AddTarget.Text = "Add";
            Button_AddTarget.UseVisualStyleBackColor = true;
            Button_AddTarget.Click += Button_AddTarget_Click;
            // 
            // Button_CheckedTargets
            // 
            Button_CheckedTargets.Location = new System.Drawing.Point(1152, 22);
            Button_CheckedTargets.Name = "Button_CheckedTargets";
            Button_CheckedTargets.Size = new System.Drawing.Size(75, 27);
            Button_CheckedTargets.TabIndex = 43;
            Button_CheckedTargets.Text = "Checked";
            Button_CheckedTargets.UseVisualStyleBackColor = true;
            Button_CheckedTargets.Click += Button_CheckedTargets_Click;
            // 
            // GroupBox_MoonAvoidance
            // 
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_WidthDays);
            GroupBox_MoonAvoidance.Controls.Add(CheckBox_Moon_AvoidanceEnable);
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_Separation);
            GroupBox_MoonAvoidance.Controls.Add(NumericUpDown_Moon_Separation);
            GroupBox_MoonAvoidance.Controls.Add(GroupBox_Moon_Filters);
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_Width);
            GroupBox_MoonAvoidance.Controls.Add(NumericUpDown_Moon_Width);
            GroupBox_MoonAvoidance.Controls.Add(CheckBox_Moon_RelaxEnabled);
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_RelaxMin);
            GroupBox_MoonAvoidance.Controls.Add(NumericUpDown_Moon_RelaxMin);
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_RelaxMax);
            GroupBox_MoonAvoidance.Controls.Add(NumericUpDown_Moon_RelaxMax);
            GroupBox_MoonAvoidance.Controls.Add(Label_Moon_RelaxScale);
            GroupBox_MoonAvoidance.Controls.Add(NumericUpDown_Moon_RelaxScale);
            GroupBox_MoonAvoidance.Location = new System.Drawing.Point(26, 244);
            GroupBox_MoonAvoidance.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_MoonAvoidance.Name = "GroupBox_MoonAvoidance";
            GroupBox_MoonAvoidance.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_MoonAvoidance.Size = new System.Drawing.Size(580, 139);
            GroupBox_MoonAvoidance.TabIndex = 42;
            GroupBox_MoonAvoidance.TabStop = false;
            GroupBox_MoonAvoidance.Text = "Moon Avoidance";
            // 
            // Label_Moon_WidthDays
            // 
            Label_Moon_WidthDays.AutoSize = true;
            Label_Moon_WidthDays.Location = new System.Drawing.Point(237, 51);
            Label_Moon_WidthDays.Name = "Label_Moon_WidthDays";
            Label_Moon_WidthDays.Size = new System.Drawing.Size(32, 15);
            Label_Moon_WidthDays.TabIndex = 13;
            Label_Moon_WidthDays.Text = "Days";
            // 
            // CheckBox_Moon_AvoidanceEnable
            // 
            CheckBox_Moon_AvoidanceEnable.AutoSize = true;
            CheckBox_Moon_AvoidanceEnable.Location = new System.Drawing.Point(19, 21);
            CheckBox_Moon_AvoidanceEnable.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_Moon_AvoidanceEnable.Name = "CheckBox_Moon_AvoidanceEnable";
            CheckBox_Moon_AvoidanceEnable.Size = new System.Drawing.Size(61, 19);
            CheckBox_Moon_AvoidanceEnable.TabIndex = 11;
            CheckBox_Moon_AvoidanceEnable.Text = "Enable";
            CheckBox_Moon_AvoidanceEnable.UseVisualStyleBackColor = true;
            CheckBox_Moon_AvoidanceEnable.CheckedChanged += OnAvoidanceEnableChanged;
            // 
            // Label_Moon_Separation
            // 
            Label_Moon_Separation.AutoSize = true;
            Label_Moon_Separation.Location = new System.Drawing.Point(19, 51);
            Label_Moon_Separation.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Moon_Separation.Name = "Label_Moon_Separation";
            Label_Moon_Separation.Size = new System.Drawing.Size(69, 15);
            Label_Moon_Separation.TabIndex = 6;
            Label_Moon_Separation.Text = "Separation: ";
            // 
            // NumericUpDown_Moon_Separation
            // 
            NumericUpDown_Moon_Separation.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            NumericUpDown_Moon_Separation.Location = new System.Drawing.Point(90, 47);
            NumericUpDown_Moon_Separation.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Moon_Separation.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            NumericUpDown_Moon_Separation.Name = "NumericUpDown_Moon_Separation";
            NumericUpDown_Moon_Separation.Size = new System.Drawing.Size(47, 23);
            NumericUpDown_Moon_Separation.TabIndex = 0;
            NumericUpDown_Moon_Separation.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_Moon_Separation.Value = new decimal(new int[] { 60, 0, 0, 0 });
            NumericUpDown_Moon_Separation.ValueChanged += OnLorentzianControlChanged;
            // 
            // GroupBox_Moon_Filters
            // 
            GroupBox_Moon_Filters.Location = new System.Drawing.Point(19, 83);
            GroupBox_Moon_Filters.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Moon_Filters.Name = "GroupBox_Moon_Filters";
            GroupBox_Moon_Filters.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Moon_Filters.Size = new System.Drawing.Size(542, 50);
            GroupBox_Moon_Filters.TabIndex = 12;
            GroupBox_Moon_Filters.TabStop = false;
            GroupBox_Moon_Filters.Text = "Filters";
            // 
            // Label_Moon_Width
            // 
            Label_Moon_Width.AutoSize = true;
            Label_Moon_Width.Location = new System.Drawing.Point(144, 51);
            Label_Moon_Width.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Moon_Width.Name = "Label_Moon_Width";
            Label_Moon_Width.Size = new System.Drawing.Size(42, 15);
            Label_Moon_Width.TabIndex = 7;
            Label_Moon_Width.Text = "Width:";
            // 
            // NumericUpDown_Moon_Width
            // 
            NumericUpDown_Moon_Width.Location = new System.Drawing.Point(188, 47);
            NumericUpDown_Moon_Width.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Moon_Width.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            NumericUpDown_Moon_Width.Name = "NumericUpDown_Moon_Width";
            NumericUpDown_Moon_Width.Size = new System.Drawing.Size(47, 23);
            NumericUpDown_Moon_Width.TabIndex = 1;
            NumericUpDown_Moon_Width.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_Moon_Width.Value = new decimal(new int[] { 7, 0, 0, 0 });
            NumericUpDown_Moon_Width.ValueChanged += OnLorentzianControlChanged;
            // 
            // CheckBox_Moon_RelaxEnabled
            // 
            CheckBox_Moon_RelaxEnabled.AutoSize = true;
            CheckBox_Moon_RelaxEnabled.Location = new System.Drawing.Point(285, 21);
            CheckBox_Moon_RelaxEnabled.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_Moon_RelaxEnabled.Name = "CheckBox_Moon_RelaxEnabled";
            CheckBox_Moon_RelaxEnabled.Size = new System.Drawing.Size(118, 19);
            CheckBox_Moon_RelaxEnabled.TabIndex = 2;
            CheckBox_Moon_RelaxEnabled.Text = "Relaxation Enable";
            CheckBox_Moon_RelaxEnabled.UseVisualStyleBackColor = true;
            CheckBox_Moon_RelaxEnabled.CheckedChanged += OnRelaxEnabledChanged;
            // 
            // Label_Moon_RelaxMin
            // 
            Label_Moon_RelaxMin.AutoSize = true;
            Label_Moon_RelaxMin.Location = new System.Drawing.Point(285, 51);
            Label_Moon_RelaxMin.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Moon_RelaxMin.Name = "Label_Moon_RelaxMin";
            Label_Moon_RelaxMin.Size = new System.Drawing.Size(31, 15);
            Label_Moon_RelaxMin.TabIndex = 8;
            Label_Moon_RelaxMin.Text = "Min:";
            // 
            // NumericUpDown_Moon_RelaxMin
            // 
            NumericUpDown_Moon_RelaxMin.Location = new System.Drawing.Point(318, 47);
            NumericUpDown_Moon_RelaxMin.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Moon_RelaxMin.Maximum = new decimal(new int[] { 45, 0, 0, 0 });
            NumericUpDown_Moon_RelaxMin.Minimum = new decimal(new int[] { 45, 0, 0, int.MinValue });
            NumericUpDown_Moon_RelaxMin.Name = "NumericUpDown_Moon_RelaxMin";
            NumericUpDown_Moon_RelaxMin.Size = new System.Drawing.Size(47, 23);
            NumericUpDown_Moon_RelaxMin.TabIndex = 3;
            NumericUpDown_Moon_RelaxMin.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_Moon_RelaxMin.Value = new decimal(new int[] { 15, 0, 0, int.MinValue });
            NumericUpDown_Moon_RelaxMin.ValueChanged += OnLorentzianControlChanged;
            // 
            // Label_Moon_RelaxMax
            // 
            Label_Moon_RelaxMax.AutoSize = true;
            Label_Moon_RelaxMax.Location = new System.Drawing.Point(372, 51);
            Label_Moon_RelaxMax.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Moon_RelaxMax.Name = "Label_Moon_RelaxMax";
            Label_Moon_RelaxMax.Size = new System.Drawing.Size(32, 15);
            Label_Moon_RelaxMax.TabIndex = 9;
            Label_Moon_RelaxMax.Text = "Max:";
            // 
            // NumericUpDown_Moon_RelaxMax
            // 
            NumericUpDown_Moon_RelaxMax.Location = new System.Drawing.Point(406, 47);
            NumericUpDown_Moon_RelaxMax.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Moon_RelaxMax.Maximum = new decimal(new int[] { 45, 0, 0, 0 });
            NumericUpDown_Moon_RelaxMax.Minimum = new decimal(new int[] { 45, 0, 0, int.MinValue });
            NumericUpDown_Moon_RelaxMax.Name = "NumericUpDown_Moon_RelaxMax";
            NumericUpDown_Moon_RelaxMax.Size = new System.Drawing.Size(47, 23);
            NumericUpDown_Moon_RelaxMax.TabIndex = 4;
            NumericUpDown_Moon_RelaxMax.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_Moon_RelaxMax.Value = new decimal(new int[] { 5, 0, 0, 0 });
            NumericUpDown_Moon_RelaxMax.ValueChanged += OnLorentzianControlChanged;
            // 
            // Label_Moon_RelaxScale
            // 
            Label_Moon_RelaxScale.AutoSize = true;
            Label_Moon_RelaxScale.Location = new System.Drawing.Point(461, 51);
            Label_Moon_RelaxScale.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            Label_Moon_RelaxScale.Name = "Label_Moon_RelaxScale";
            Label_Moon_RelaxScale.Size = new System.Drawing.Size(37, 15);
            Label_Moon_RelaxScale.TabIndex = 10;
            Label_Moon_RelaxScale.Text = "Scale:";
            // 
            // NumericUpDown_Moon_RelaxScale
            // 
            NumericUpDown_Moon_RelaxScale.DecimalPlaces = 2;
            NumericUpDown_Moon_RelaxScale.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            NumericUpDown_Moon_RelaxScale.Location = new System.Drawing.Point(500, 47);
            NumericUpDown_Moon_RelaxScale.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            NumericUpDown_Moon_RelaxScale.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            NumericUpDown_Moon_RelaxScale.Name = "NumericUpDown_Moon_RelaxScale";
            NumericUpDown_Moon_RelaxScale.Size = new System.Drawing.Size(61, 23);
            NumericUpDown_Moon_RelaxScale.TabIndex = 5;
            NumericUpDown_Moon_RelaxScale.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            NumericUpDown_Moon_RelaxScale.ValueChanged += OnLorentzianControlChanged;
            // 
            // MenuStrip_MainForm
            // 
            MenuStrip_MainForm.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { FileToolStripMenuItem_MainForm, FiltersToolStripMenuItem_MainForm, HelpToolStripMenuItem_MainForm });
            MenuStrip_MainForm.Location = new System.Drawing.Point(0, 0);
            MenuStrip_MainForm.Name = "MenuStrip_MainForm";
            MenuStrip_MainForm.Padding = new System.Windows.Forms.Padding(7, 2, 0, 2);
            MenuStrip_MainForm.ShowItemToolTips = true;
            MenuStrip_MainForm.Size = new System.Drawing.Size(1899, 24);
            MenuStrip_MainForm.TabIndex = 5;
            MenuStrip_MainForm.Text = "menuStrip1";
            // 
            // FileToolStripMenuItem_MainForm
            // 
            FileToolStripMenuItem_MainForm.Name = "FileToolStripMenuItem_MainForm";
            FileToolStripMenuItem_MainForm.Size = new System.Drawing.Size(37, 20);
            FileToolStripMenuItem_MainForm.Text = "File";
            // 
            // FiltersToolStripMenuItem_MainForm
            // 
            FiltersToolStripMenuItem_MainForm.Name = "FiltersToolStripMenuItem_MainForm";
            FiltersToolStripMenuItem_MainForm.Size = new System.Drawing.Size(50, 20);
            FiltersToolStripMenuItem_MainForm.Text = "&Filters";
            FiltersToolStripMenuItem_MainForm.ToolTipText = "Click a filter to activate it for moon avoidance. Right-click any filter to open the Edit Filters dialog.";
            // 
            // HelpToolStripMenuItem_MainForm
            // 
            HelpToolStripMenuItem_MainForm.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { CheckUpdatesToolStripMenuItem, AboutToolStripMenuItem });
            HelpToolStripMenuItem_MainForm.Name = "HelpToolStripMenuItem_MainForm";
            HelpToolStripMenuItem_MainForm.Size = new System.Drawing.Size(44, 20);
            HelpToolStripMenuItem_MainForm.Text = "&Help";
            // 
            // CheckUpdatesToolStripMenuItem
            // 
            CheckUpdatesToolStripMenuItem.Name = "CheckUpdatesToolStripMenuItem";
            CheckUpdatesToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            CheckUpdatesToolStripMenuItem.Text = "Check for &Updates...";
            CheckUpdatesToolStripMenuItem.Click += OnCheckUpdatesClick;
            // 
            // AboutToolStripMenuItem
            // 
            AboutToolStripMenuItem.Name = "AboutToolStripMenuItem";
            AboutToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            AboutToolStripMenuItem.Text = "&About TargetPlanner";
            AboutToolStripMenuItem.Click += OnAboutClick;
            // 
            // GroupBox_Altitude
            // 
            GroupBox_Altitude.Controls.Add(CheckBox_Sky);
            GroupBox_Altitude.Controls.Add(ProgressBar_Processing);
            GroupBox_Altitude.Controls.Add(RadioButton_Year);
            GroupBox_Altitude.Controls.Add(RadioButton_Sessions);
            GroupBox_Altitude.Controls.Add(RadioButton_Day);
            GroupBox_Altitude.Location = new System.Drawing.Point(15, 430);
            GroupBox_Altitude.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Altitude.Name = "GroupBox_Altitude";
            GroupBox_Altitude.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            GroupBox_Altitude.Size = new System.Drawing.Size(1860, 640);
            GroupBox_Altitude.TabIndex = 6;
            GroupBox_Altitude.TabStop = false;
            GroupBox_Altitude.Text = "Altitude";
            // 
            // CheckBox_Sky
            // 
            CheckBox_Sky.AutoSize = true;
            CheckBox_Sky.Location = new System.Drawing.Point(65, 21);
            CheckBox_Sky.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            CheckBox_Sky.Name = "CheckBox_Sky";
            CheckBox_Sky.Size = new System.Drawing.Size(44, 19);
            CheckBox_Sky.TabIndex = 41;
            CheckBox_Sky.Text = "Sky";
            CheckBox_Sky.UseVisualStyleBackColor = true;
            CheckBox_Sky.CheckedChanged += CheckBox_Sky_CheckedChanged;
            // 
            // ProgressBar_Processing
            // 
            ProgressBar_Processing.Location = new System.Drawing.Point(280, 19);
            ProgressBar_Processing.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            ProgressBar_Processing.Name = "ProgressBar_Processing";
            ProgressBar_Processing.Size = new System.Drawing.Size(1570, 21);
            ProgressBar_Processing.TabIndex = 40;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1899, 1097);
            Controls.Add(GroupBox_Altitude);
            Controls.Add(GroupBox_Target);
            Controls.Add(GroupBox_Local);
            Controls.Add(MenuStrip_MainForm);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = MenuStrip_MainForm;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            Name = "MainForm";
            Text = "TargetPlanner";
            FormClosing += MainForm_FormClosing;
            Load += MainForm_Load;
            GroupBox_Local.ResumeLayout(false);
            GroupBox_Location.ResumeLayout(false);
            GroupBox_Location.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Extinction).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LocalElevation).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeMinutes).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeDegrees).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeDegrees).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeMinutes).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LongitudeSeconds).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_LatitudeSeconds).EndInit();
            GroupBox_LocalDateTime.ResumeLayout(false);
            GroupBox_LocalDateTime.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_TargetFloor).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_TargetDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaMinutes).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaHours).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_RaSeconds).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecMinutes).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecSeconds).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_DecDegrees).EndInit();
            GroupBox_Target.ResumeLayout(false);
            GroupBox_Target.PerformLayout();
            GroupBox_MoonAvoidance.ResumeLayout(false);
            GroupBox_MoonAvoidance.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_Separation).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_Width).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_Moon_RelaxScale).EndInit();
            MenuStrip_MainForm.ResumeLayout(false);
            MenuStrip_MainForm.PerformLayout();
            GroupBox_Altitude.ResumeLayout(false);
            GroupBox_Altitude.PerformLayout();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox GroupBox_Local;
        private System.Windows.Forms.DateTimePicker DatePicker;
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
        private System.Windows.Forms.NumericUpDown NumericUpDown_TargetDuration;
        private System.Windows.Forms.NumericUpDown NumericUpDown_TargetFloor;
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
        private System.Windows.Forms.Button Button_GraphTarget;
        private System.Windows.Forms.Label Label_AstronomicalDawnValue;
        private System.Windows.Forms.Label Label_AstronomicalDawn;
        private System.Windows.Forms.Label Label_AstronomicalDuskValue;
        private System.Windows.Forms.Label Label_AstronomicalDusk;
        private System.Windows.Forms.GroupBox GroupBox_Location;
        private System.Windows.Forms.GroupBox GroupBox_LocalDateTime;
        private System.Windows.Forms.Label Label_LunarAltitudeValue;
        private System.Windows.Forms.Label Label_MoonAltitude;
        private System.Windows.Forms.Label Label_LunarIlluminationFractionValue;
        private System.Windows.Forms.Label Label_LunarIlluminationFraction;
        private System.Windows.Forms.Label Label_MoonSetValue;
        private System.Windows.Forms.Label Label_MoonSetTimeText;
        private System.Windows.Forms.Label Label_MoonRiseValue;
        private System.Windows.Forms.Label Label_MoonRise;
        private System.Windows.Forms.Button Button_Now;
        private System.Windows.Forms.Label Label_SunAltitudeValue;
        private System.Windows.Forms.Label Label_SunAltitude;
        private System.Windows.Forms.Label Label_LunarPhaseValue;
        private System.Windows.Forms.Label Label_Location;
        private System.Windows.Forms.ComboBox ComboBox_Location;
        private System.Windows.Forms.Button Button_BrowseTargetList;
        private System.Windows.Forms.Button Button_LoadImageLibrary;
        private System.Windows.Forms.Button Button_LoadJsonTargets;
        private TargetPlanner.Forms.DupeAwareCheckedListBox CheckedListBox_SelectedTargets;
        private System.Windows.Forms.ComboBox ComboBox_SortTargets;
        private System.Windows.Forms.Label Label_SortBy;
        private System.Windows.Forms.Label Label_TargetDuration;
        private System.Windows.Forms.Label Label_TargetFloor;
        private System.Windows.Forms.Label Label_TargetHours;
        private System.Windows.Forms.Label Label_DecMinuteColon;
        private System.Windows.Forms.Label Label_RaMinuteColon;
        private System.Windows.Forms.Label Label_DecDegreeColon;
        private System.Windows.Forms.Label Label_RaHourColon;
        private System.Windows.Forms.CheckBox CheckBox_LocalNorth;
        private System.Windows.Forms.CheckBox CheckBox_TargetNorth;
        private System.Windows.Forms.RadioButton RadioButton_Sessions;
        private System.Windows.Forms.RadioButton RadioButton_Year;
        private System.Windows.Forms.RadioButton RadioButton_Day;
        private System.Windows.Forms.GroupBox GroupBox_Target;
        private System.Windows.Forms.MenuStrip MenuStrip_MainForm;
        private System.Windows.Forms.ToolStripMenuItem FileToolStripMenuItem_MainForm;
        private System.Windows.Forms.ToolStripMenuItem FiltersToolStripMenuItem_MainForm;
        private System.Windows.Forms.ToolStripMenuItem HelpToolStripMenuItem_MainForm;
        private System.Windows.Forms.ToolStripMenuItem CheckUpdatesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem AboutToolStripMenuItem;
        private System.Windows.Forms.GroupBox GroupBox_Altitude;
        private System.Windows.Forms.Label Label_TargetName;
        private System.Windows.Forms.Label Label_Phase;
        private System.Windows.Forms.ComboBox ComboBox_SelectTarget;
        private System.Windows.Forms.Button Button_CheckAllTargets;
        private System.Windows.Forms.Button Button_UnCheckAllTargets;
        private System.Windows.Forms.Button Button_ClearAllTargets;
        private System.Windows.Forms.ProgressBar ProgressBar_Processing;
        private System.Windows.Forms.Button Button_VisibleTargets;
        private System.Windows.Forms.GroupBox GroupBox_MoonAvoidance;
        private System.Windows.Forms.CheckBox CheckBox_Moon_AvoidanceEnable;
        private System.Windows.Forms.Label Label_Moon_Separation;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Moon_Separation;
        private System.Windows.Forms.Label Label_Moon_Width;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Moon_Width;
        private System.Windows.Forms.CheckBox CheckBox_Moon_RelaxEnabled;
        private System.Windows.Forms.Label Label_Moon_RelaxMin;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Moon_RelaxMin;
        private System.Windows.Forms.Label Label_Moon_RelaxMax;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Moon_RelaxMax;
        private System.Windows.Forms.Label Label_Moon_RelaxScale;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Moon_RelaxScale;
        private System.Windows.Forms.Label Label_LocalMeters;
        private System.Windows.Forms.NumericUpDown NumericUpDown_LocalElevation;
        private System.Windows.Forms.Label Label_LocalElevation;
        private System.Windows.Forms.GroupBox GroupBox_Moon_Filters;
        private System.Windows.Forms.Label Label_Extinction;
        private System.Windows.Forms.Label Label_Bortle;
        private System.Windows.Forms.ComboBox ComboBox_Bortle;
        private System.Windows.Forms.NumericUpDown NumericUpDown_Extinction;
        private System.Windows.Forms.CheckBox CheckBox_Sky;
        private System.Windows.Forms.Button Button_CheckedTargets;
        private System.Windows.Forms.Button Button_AddTarget;
        private System.Windows.Forms.Button Button_RemoveTarget;
        private System.Windows.Forms.Label Label_TimeZone;
        private System.Windows.Forms.Button Button_BrowseLocalHorizon;
        private System.Windows.Forms.Label Label_HorizonPath;
        private System.Windows.Forms.ComboBox ComboBox_TimeZone;
        public System.Windows.Forms.Label Label_Moon_WidthDays;
    }
}

