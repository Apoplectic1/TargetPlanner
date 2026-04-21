using Astronomy.Core.Night;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    public class AltitudeChart
    {
        public Chart mChart { get; set; }
        private List<ChartArea> mChartAreaList;

        public Location Location { get; set; }
        public bool Legend { set { mLegend.Enabled = value; } }

        private ChartArea mChartArea;
        private List<Target> mTargetList;
        private Target mTarget;
        private List<Series> mSeriesList;
        private Series mSeries;
        private Legend mLegend;
        private UIState mUIState;

        // Per-target AltitudeSeries state. Target POCO no longer carries its own (it lives in
        // Astronomy.Core which can't depend on WinForms charts), so the chart layer owns the
        // per-target mapping here. Lifetime tied to this AltitudeChart instance; a fresh chart
        // on Graph-Ephemeride click starts empty, same as the old Target.mAltitudeSeries pattern.
        private Dictionary<Target, AltitudeSeries> mSeriesByTarget;

        private Dictionary<string, StripLine> mNowLines;

        //################################################################################################################
        //################################################################################################################

        public AltitudeChart()
        {
            mChart = new Chart();
            mChartAreaList = new List<ChartArea>();
            //mChart.ChartAreas.Add(mChartArea);
            mTargetList = new List<Target>();
            mTarget = new Target();
            mSeriesList = new List<Series>();
            mSeries = new Series();
            mLegend = new Legend();
            mUIState = new Support.UIState();
            mSeriesByTarget = new Dictionary<Target, AltitudeSeries>();
            mNowLines = new Dictionary<string, StripLine>();

            mChart.MouseClick += new MouseEventHandler(this.Chart_MouseClick);
        }

        private AltitudeSeries SeriesFor(Target target)
        {
            // Fail-fast on null. Previously the Dictionary.TryGetValue call threw
            // ArgumentNullException from deep in mscorlib; surfacing the check here makes
            // callers' responsibility explicit. Callers should skip null entries when
            // iterating mTargetList rather than rely on this guard.
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (!mSeriesByTarget.TryGetValue(target, out AltitudeSeries series))
            {
                series = new AltitudeSeries();
                mSeriesByTarget[target] = series;
            }
            return series;
        }

        // Draws (or repositions) a red vertical strip line at the current time on every chart
        // area. The target series uses IsXValueIndexed = true, so the X axis is indexed: each
        // data point sits at integer index 0..N-1, with the point's DateTime shown only as a
        // label. StripLine.IntervalOffset is therefore expressed in those same index units --
        // computed as the offset (in minutes for Day, days for Year / Optimal) from the first
        // target data point to "now".
        public void UpdateNowLine(DateTime now)
        {
            foreach (ChartArea area in mChartAreaList)
            {
                Series reference = null;
                foreach (Target target in mTargetList.ToList())
                {
                    if (target == null) continue;
                    foreach (Series s in SeriesFor(target).TargetSeriesList.ToList())
                    {
                        if (s.Name.EndsWith("-" + area.Name) && s.Points.Count > 0)
                        {
                            reference = s;
                            break;
                        }
                    }
                    if (reference != null) break;
                }
                if (reference == null) continue;

                DateTime firstX = DateTime.FromOADate(reference.Points[0].XValue);
                double nowIndex = area.Name == "Day"
                    ? (now - firstX).TotalMinutes
                    : (now - firstX).TotalDays;

                StripLine line;
                if (!mNowLines.TryGetValue(area.Name, out line))
                {
                    line = new StripLine
                    {
                        BackColor = Color.Red,
                        Interval = 0,
                        IntervalOffsetType = DateTimeIntervalType.Number,
                        StripWidthType = DateTimeIntervalType.Number,
                        StripWidth = 1.0,  // 1 index unit; ~1 min on Day, ~1 day on Year / Optimal
                    };
                    mNowLines[area.Name] = line;
                    area.AxisX.StripLines.Add(line);
                }
                line.IntervalOffset = nowIndex - line.StripWidth / 2.0;
            }
        }

        private void Chart_MouseClick(object sender, MouseEventArgs e)
        {
            HitTestResult result = mChart.HitTest(e.X, e.Y);

            if (result != null && result.Object != null && result.Object is LegendItem && e.Button == MouseButtons.Left)
            {
                LegendItem legendItem = (LegendItem)result.Object;

                // Indexer throws ArgumentException if the legend label doesn't map to a current
                // Series; that can happen if the click arrives during a chart rebuild, or if
                // the legend was populated from a Series that was since removed.
                int idx = mChart.Series.IndexOf(legendItem.SeriesName);
                if (idx < 0) return;
                Series series = mChart.Series[idx];

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

        //################################################################################################################
        //################################################################################################################
        public void UIState(Support.UIState state)
        {
            mUIState = state;
        }

        public void ClearChartAreaList()
        {
            mChartAreaList.Clear();
        }

        public void AddChartAreaToList(string chartAreaName)
        {
            mChartArea = new ChartArea(chartAreaName);
            mChartArea.BackColor = Color.FromArgb(255, 70, 70, 70);

            if (mChartAreaList.Count >= 1)
            {
                mChartArea.AlignmentOrientation = mChartAreaList[0].AlignmentOrientation;
                mChartArea.Visible = false;
            }
            mChartAreaList.Add(mChartArea);
        }

        private void AddChartAreaToChart(string chartAreaName)
        {
            mChart.ChartAreas.Clear();

            foreach (ChartArea chartArea in mChartAreaList)
            {
                chartArea.Visible = false;

                if (chartArea.Name == chartAreaName)
                {
                    mChartArea = chartArea;
                }
            }

            mChart.ChartAreas.Add(mChartArea);
        }

        public void ShowChartAreaSeries(string chartAreaName)
        {
            // Guard the ChartAreas indexer at :214 (and the ones in AddHorizonLine /
            // SetChartAreaAxis / AddDawnDuskGradient further down) against an unknown area
            // name. An unknown name would throw ArgumentException from the indexer; early
            // return is safer than propagating to a UI event handler.
            if (mChartAreaList.All(ca => ca.Name != chartAreaName)) return;

            AddChartAreaToChart(chartAreaName);

            AddHorizonLine(chartAreaName);

            ClearSeries();

            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                foreach (Series series in SeriesFor(target).TargetSeriesList.ToList())
                {
                    if (series.Name.Contains(chartAreaName))
                    {
                        series.Enabled = true;
                        series.LegendText = series.Name.Remove(series.Name.IndexOf("-"));
                        AddSeries(series);
                    }
                    else
                    {
                        series.Enabled = false;
                    }
                }
            }

            SetChartAreaAxis(chartAreaName);
            mChart.ChartAreas[chartAreaName].Visible = true;
        }

        public void BuildTargetSeriesList()
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                SeriesFor(target).Location = Location;
                SeriesFor(target).Target   = target;
                // Fire-and-forget: BuildSeriesList owns its own try/catch for diagnostics.
                // The discard suppresses CS4014 and documents the intent explicitly.
                _ = SeriesFor(target).BuildSeriesList();
            }
        }

        // Regenerate only the Optimal series in place for every target in the target list.
        // Day, Moon, and Year do not depend on Horizon or Duration so they stay untouched;
        // the Optimal recomputation walks the cache populated during the initial build and
        // avoids any ComputeNight / GetAltitudeAzimuth calls. Series object identity is
        // preserved (FindOrCreateSeries reuses them), so mChart.Series references stay valid
        // and the chart picks up the new points automatically.
        public void RebuildOptimalData()
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                SeriesFor(target).Location = Location;
                SeriesFor(target).Target   = target;
                SeriesFor(target).RebuildOptimalSeries();
            }
            mChart.Invalidate();
        }

        // Move the green horizon strip line to the current Location.Horizon on every chart
        // area. Clears any prior horizon line (identified by its green color) before adding a
        // fresh one so repeated calls don't accumulate strip lines.
        public void UpdateHorizonLines()
        {
            foreach (ChartArea area in mChartAreaList)
            {
                List<StripLine> stale = new List<StripLine>();
                foreach (StripLine sl in area.AxisY.StripLines)
                {
                    if (sl.BackColor == Color.Green) stale.Add(sl);
                }
                foreach (StripLine sl in stale) area.AxisY.StripLines.Remove(sl);

                StripLine replacement = new StripLine();
                replacement.Interval = 0;
                replacement.IntervalOffset = Location.Horizon - 1;
                replacement.StripWidth = 2;
                replacement.BackColor = Color.Green;
                area.AxisY.StripLines.Add(replacement);
            }
            mChart.Invalidate();
        }

        public string ChartTitle
        {
            set
            {
                mChart.Titles.Clear();
                mChart.Titles.Add(value);
            }
        }

        public void ClearSeries()
        {
            mChart.Series.Clear();
        }

        public void RemoveSeries(Series series)
        {
            mChart.Series.Remove(series);
        }

        public void AddSeries(Series series)
        {
            mChart.Series.Add(series);
        }

        public void ClearTargetList()
        {
            ClearSeries();
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                SeriesFor(target).ClearTargetList();
            }
            mTargetList.Clear();
        }

        public void RemoveFromTargetList(Target target)
        {

        }

        public void AddToTargetList(Target target)
        {
            mTargetList.Add(target);
        }

        public void AddToTargetList(List<Target> targetList)
        {
            foreach (Target target in targetList)
            {
                AddToTargetList(target);
            }
        }

        public void SetChartAreaAxis(string chartAreaName)
        {
            foreach (Series series in mChart.Series)
            {
                series.Enabled = false;
            }

            // Every case below indexes mChart.ChartAreas[chartAreaName]; bail out up front if
            // the area isn't present so the indexer never throws ArgumentException.
            if (mChart.ChartAreas.IndexOf(chartAreaName) < 0) return;

            switch (chartAreaName)
            {
                case "Day":
                case "Moon":
                    mChart.ChartAreas[chartAreaName].AxisX.Interval = 60.0;
                    mChart.ChartAreas[chartAreaName].AxisX.IntervalType = DateTimeIntervalType.Minutes;
                    mChart.ChartAreas[chartAreaName].AxisX.LabelStyle.Format = "h:mm tt";
                    mChart.ChartAreas[chartAreaName].AxisX.Title = "";

                    mChart.ChartAreas[chartAreaName].AxisY.Interval = 10;
                    mChart.ChartAreas[chartAreaName].AxisY.Maximum = 90.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Minimum = 10.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Title = "Altitude";

                    AddDawnDuskGradient(chartAreaName);
                    break;

                case "Year":
                    mChart.ChartAreas[chartAreaName].AxisX.Interval = 1.0;
                    mChart.ChartAreas[chartAreaName].AxisX.IntervalType = DateTimeIntervalType.Months;
                    mChart.ChartAreas[chartAreaName].AxisX.LabelStyle.Format = "MMMM";
                    mChart.ChartAreas[chartAreaName].AxisX.Title = "";

                    mChart.ChartAreas[chartAreaName].AxisY.Interval = 10;
                    mChart.ChartAreas[chartAreaName].AxisY.Maximum = 90.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Minimum = 10.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Title = "Maximum Daily Altitude";
                    break;

                case "Optimal":
                    mChart.ChartAreas[chartAreaName].AxisX.Interval = 1.0;
                    mChart.ChartAreas[chartAreaName].AxisX.IntervalType = DateTimeIntervalType.Months;
                    mChart.ChartAreas[chartAreaName].AxisX.LabelStyle.Format = "MMMM";
                    mChart.ChartAreas[chartAreaName].AxisX.Title = "";

                    mChart.ChartAreas[chartAreaName].AxisY.Interval = 10;
                    mChart.ChartAreas[chartAreaName].AxisY.Maximum = 90.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Minimum = 10.0;
                    mChart.ChartAreas[chartAreaName].AxisY.Title = "Altitude at Minimum Duration";
                    break;

                default:
                    break;
            }

            foreach (Series series in mChart.Series)
            {
                if (series.Name.Contains(chartAreaName))
                {
                    series.Enabled = true;
                }
                else
                {
                    series.Enabled = false;
                }
            }

            mChart.Invalidate();
        }

        public void AddHorizonLine(string chartAreaName)
        {
            StripLine stripline = new StripLine();
            stripline.Interval = 0;
            stripline.IntervalOffset = Location.Horizon - 1;
            stripline.StripWidth = 2;
            stripline.BackColor = Color.Green;
            mChart.ChartAreas[chartAreaName].AxisY.StripLines.Add(stripline);
        }

        public void AddDawnDuskGradient(string chartAreaName)
        {
            double duskOffset;
            double dawnOffset;
            TimeSpan delta;
            StripLine stripLine;
            DateTime start;
            DateTime stop;

            NightWindow night = NightCalculator.ComputeNight(Location);

            stripLine = new StripLine();

            duskOffset = (night.AstronomicalDusk.Minute > 30.0) ? 0.0 : -1.0;
            start = night.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(night.AstronomicalDusk.AddHours(duskOffset).Hour);
            stop = night.AstronomicalDusk;

            delta = stop.Subtract(start);

            stripLine.BackColor = Color.FromArgb(125, 80, 230, 210);
            stripLine.BackColor = Color.FromArgb(145, 255, 238, 88);
            stripLine.BackSecondaryColor = Color.FromArgb(255, 90, 90, 90);
            stripLine.BackGradientStyle = GradientStyle.LeftRight;
            stripLine.IntervalOffset = start.Minute;
            stripLine.IntervalOffsetType = DateTimeIntervalType.Minutes;
            stripLine.Interval = 0;
            stripLine.IntervalType = DateTimeIntervalType.Minutes;

            stripLine.StripWidth = delta.TotalMinutes;

            mChart.ChartAreas[chartAreaName].AxisX.StripLines.Add(stripLine);



            stripLine = new StripLine();

            dawnOffset = (night.AstronomicalDawn.Minute > 30.0) ? 2.0 : 1.0;
            start = night.AstronomicalDawn;
            stop = night.AstronomicalDawn.AddHours(dawnOffset).Date.AddHours(night.AstronomicalDawn.AddHours(dawnOffset).Hour);
            delta = stop.Subtract(start);

            stripLine.BackSecondaryColor = Color.FromArgb(145, 255, 238, 88);
            stripLine.BackColor = Color.FromArgb(255, 90, 90, 90);
            stripLine.BackGradientStyle = GradientStyle.LeftRight;
            stripLine.IntervalOffsetType = DateTimeIntervalType.Minutes;
            stripLine.Interval = 0;
            stripLine.IntervalType = DateTimeIntervalType.Minutes;
            stripLine.StripWidth = delta.TotalMinutes;

            start = night.AstronomicalDusk.AddHours(duskOffset).Date.AddHours(night.AstronomicalDusk.AddHours(duskOffset).Hour);
            delta = night.AstronomicalDawn.Subtract(start);

            stripLine.IntervalOffset = delta.TotalMinutes + 2;

            mChart.ChartAreas[chartAreaName].AxisX.StripLines.Add(stripLine);
        }

        public void AddLegend()
        {
            mChart.Legends.Add(mLegend);
        }
        public void ClearLegend()
        {
            mChart.Legends.Clear();
        }
    }
}
