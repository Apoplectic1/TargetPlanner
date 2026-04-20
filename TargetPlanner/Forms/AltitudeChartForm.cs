using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    public partial class AltitudeChartForm : Form
    {
        private System.ComponentModel.IContainer mComponents = null;
        private Chart ChartForm;
        private ChartArea mChartArea;
        private Legend mLegend;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;

        public DateTime AstronomicalDusk { get; set; }
        public DateTime AstronomicalDawn { get; set; }
        
        public AltitudeChartForm()
        {
            mChartArea = new ChartArea();
            mLegend = new Legend();
            InitializeComponent();
            AddXYAxis();
        }

        public void AddStripline (StripLine stripLine)
        {
            ChartForm.ChartAreas[0].AxisX.StripLines.Add(stripLine);
        }

        private void EphemeridesChart_Load(object sender, EventArgs e)
        {
            ChartForm.Invalidate();
        }

        public void AddSeries(Target target)
        {
            //ChartForm.Series.Add(series);
        }

        public void AddToTargetList(List<Target> targetList)
        {
            foreach (Target target in targetList)
            {
                AddSeries(target);
            }
        }

        public void RemoveSeries(Series series)
        {
            ChartForm.Series.Remove(series);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (mComponents != null))
            {
                mComponents.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            ChartArea chartArea1 = new ChartArea();
            Legend legend1 = new Legend();
            this.ChartForm = new Chart();
            this.menuStrip1 = new MenuStrip();
            this.fileToolStripMenuItem = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.ChartForm)).BeginInit();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // ChartForm
            // 
            chartArea1.Name = "ChartArea";
            this.ChartForm.ChartAreas.Add(chartArea1);
            this.ChartForm.Dock = System.Windows.Forms.DockStyle.Fill;
            legend1.Name = "Legend";
            this.ChartForm.Legends.Add(legend1);
            this.ChartForm.Location = new Point(0, 24);
            this.ChartForm.Name = "ChartForm";
            this.ChartForm.Size = new Size(1248, 369);
            this.ChartForm.TabIndex = 0;
            this.ChartForm.Text = "Ephemerides Chart";
            this.ChartForm.MouseClick += new MouseEventHandler(this.ChartForm_MouseClick);
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new ToolStripItem[] {this.fileToolStripMenuItem});
            this.menuStrip1.Location = new Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new Size(1248, 24);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // EphemeridesChartForm
            // 
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new Size(1248, 393);
            this.Controls.Add(this.ChartForm);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "EphemeridesChartForm";
            this.Text = "Target Ephemerides";
            this.Load += new EventHandler(this.EphemeridesChart_Load);
            ((System.ComponentModel.ISupportInitialize)(this.ChartForm)).EndInit();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void ChartForm_MouseClick(object sender, MouseEventArgs e)
        {
            HitTestResult result = ChartForm.HitTest(e.X, e.Y);

            if (result != null && result.Object != null && result.Object is LegendItem && e.Button == MouseButtons.Left)
            {
                LegendItem legendItem = (LegendItem)result.Object;

                Series series = ChartForm.Series[legendItem.SeriesName];

                if (series.Color == Color.Empty)
                {
                    series.Color = Color.Transparent; 
                }
                else
                {
                    series.Color = Color.Empty;
                }
            }
        }
        public void AddDawnDuskGradient()
        {
            double duskOffset;
            double dawnOffset;
            TimeSpan delta;
            StripLine stripLine;
            DateTime start;
            DateTime stop;

            stripLine = new StripLine();

            duskOffset = (AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            start = AstronomicalDusk.AddHours(duskOffset).Date.AddHours(AstronomicalDusk.AddHours(duskOffset).Hour);
            stop = AstronomicalDusk;

            delta = stop.Subtract(start);

            stripLine.BackColor = Color.FromArgb(125, 80, 230, 210);
            stripLine.BackSecondaryColor = Color.FromArgb(125, 110, 110, 110);
            stripLine.BackGradientStyle = GradientStyle.LeftRight;
            stripLine.IntervalOffset = start.Minute;
            stripLine.IntervalOffsetType = DateTimeIntervalType.Minutes;
            stripLine.Interval = 0;
            stripLine.IntervalType = DateTimeIntervalType.Minutes;

            stripLine.StripWidth = delta.TotalMinutes;
            
            ChartForm.ChartAreas[0].AxisX.StripLines.Add(stripLine);



            stripLine = new StripLine();

            dawnOffset = (AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            start = AstronomicalDawn;
            stop = AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(AstronomicalDawn.AddHours(dawnOffset).Hour);
            delta = stop.Subtract(start);

            stripLine.BackSecondaryColor = Color.FromArgb(125, 80, 230, 210);
            stripLine.BackColor = Color.FromArgb(125, 110, 110, 110);
            stripLine.BackGradientStyle = GradientStyle.LeftRight;
            stripLine.IntervalOffsetType = DateTimeIntervalType.Minutes;
            stripLine.Interval = 0;
            stripLine.IntervalType = DateTimeIntervalType.Minutes;
            stripLine.StripWidth = delta.TotalMinutes;

            start = AstronomicalDusk.AddHours(duskOffset).Date.AddHours(AstronomicalDusk.AddHours(duskOffset).Hour);
            delta = AstronomicalDawn.Subtract(start);

            stripLine.IntervalOffset = delta.TotalMinutes + 2;

            ChartForm.ChartAreas[0].AxisX.StripLines.Add(stripLine);
        }
        public string ChartTitle 
        {
            set
            { 
                ChartForm.Titles.Add(value); 
            } 
        }
        public void AddHorizonLine(double horizon)
        {
            StripLine stripline = new StripLine();
            stripline.Interval = 0;
            stripline.IntervalOffset = horizon - 0.5;
            stripline.StripWidth = 1;
            stripline.BackColor = Color.Green;
            ChartForm.ChartAreas[0].AxisY.StripLines.Add(stripline);
        }
        private void AddXYAxis()
        {
            this.ChartForm.ChartAreas[0].AxisX.Interval = 60.0;
            this.ChartForm.ChartAreas[0].AxisX.IntervalType = DateTimeIntervalType.Minutes;
            this.ChartForm.ChartAreas[0].AxisX.LabelStyle.Format = "hh:mm tt";
            this.ChartForm.ChartAreas[0].AxisY.Maximum = 100.0;
            this.ChartForm.ChartAreas[0].AxisY.Title = "Altitude";
        }

    }

}
