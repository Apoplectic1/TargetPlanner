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
    public class AltitudeChart : IDisposable
    {
        public Chart mChart { get; set; }
        private List<ChartArea> mChartAreaList;

        // Snapshot set at construction and at each ReloadWithTargets call. Horizon /
        // Duration live-scrub paths pass the current values through
        // RebuildOptimalData(horizon, duration) / UpdateHorizonLines(horizon) instead of
        // mutating the snapshot. The immutable-mid-flight invariant the background
        // AltitudeSeries Tasks rely on is preserved: Reload discards mSeriesByTarget so
        // every subsequent SeriesFor(target) call produces a fresh AltitudeSeries against
        // the new Location, and in-flight Tasks from the prior cycle still reference their
        // own frozen Location.
        public Location Location { get; private set; }
        public bool Legend { set { mLegend.Enabled = value; } }

        private ChartArea mChartArea;
        private List<Target> mTargetList;
        private Legend mLegend;
        private UIState mUIState;

        // Per-target AltitudeSeries state. Target POCO no longer carries its own (it lives in
        // Astronomy.Core which can't depend on WinForms charts), so the chart layer owns the
        // per-target mapping here. Lifetime tied to this AltitudeChart instance; a fresh chart
        // on Graph-Target click starts empty, same as the old Target.mAltitudeSeries pattern.
        private Dictionary<Target, AltitudeSeries> mSeriesByTarget;

        private Dictionary<string, StripLine> mNowLines;

        public AltitudeChart(Location location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            Location = location;

            mChart = new Chart();
            mChartAreaList = new List<ChartArea>();
            mTargetList = new List<Target>();
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
                // AltitudeSeries is an immutable snapshot: Location and Target are captured
                // here and cannot be reassigned later.
                series = new AltitudeSeries(Location, target);
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

        // Populate mChart.ChartAreas once (on first call) from the registered mChartAreaList,
        // then on every subsequent call just flip each area's Visible flag. The previous
        // implementation cleared mChart.ChartAreas and re-added the selected area on every
        // radio-button switch, which also nuked the user's zoom and legend-color-toggle state.
        // Keeping the ChartArea instances resident preserves that state across switches.
        private void AddChartAreaToChart(string chartAreaName)
        {
            if (mChart.ChartAreas.Count == 0)
            {
                foreach (ChartArea area in mChartAreaList)
                    mChart.ChartAreas.Add(area);
            }

            foreach (ChartArea area in mChartAreaList)
            {
                bool active = area.Name == chartAreaName;
                area.Visible = active;
                if (active) mChartArea = area;
            }
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
                        // Bind the Series to its ChartArea by name. Previously mChart.ChartAreas
                        // held only the active area at a time, so a Series with no explicit
                        // binding defaulted to that single area and always rendered correctly.
                        // Post-W2-11 all three areas live in mChart.ChartAreas simultaneously
                        // (flipped via Visible); a Series without an explicit ChartArea falls
                        // back to the first one in the collection (Day), so Year / Optimal
                        // series would render into the hidden Day area and the active area
                        // would appear empty.
                        series.ChartArea = chartAreaName;
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

        // phaseProgress (if non-null) is propagated through to AltitudeSeries.BuildSeriesList;
        // it fires "Day" / "Year" / "Optimal" once per target. Subscribers that want a per-tick
        // count should multiply by mTargetList.Count to know the Maximum.
        public void BuildTargetSeriesList(IProgress<string> phaseProgress = null)
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                // SeriesFor(target) captures Location and target into the new AltitudeSeries
                // on first access; no per-call property assignment needed.
                // Fire-and-forget: BuildSeriesList owns its own try/catch for diagnostics.
                // The discard suppresses CS4014 and documents the intent explicitly.
                _ = SeriesFor(target).BuildSeriesList(phaseProgress);
            }
        }

        // Reset all transient state on this chart for a fresh Graph-click cycle, swap in a new
        // Location / target list, and kick off BuildTargetSeriesList. Keeps the Chart control,
        // its ChartArea instances, its Legend, and any user zoom / legend-color-toggle state
        // alive across reloads -- only series, strip lines, per-target AltitudeSeries cache,
        // target list, and the Location snapshot actually change.
        //
        // Safe even if a prior cycle's Task.Run continuations are still in flight: those still
        // reference their own frozen Location via the old AltitudeSeries instances captured
        // before mSeriesByTarget.Clear(). They write to their own TargetSeriesList's Series
        // objects, which are no longer in mChart.Series -- so the writes land on disconnected
        // data and do not cross-contaminate the new cycle.
        public void ReloadWithTargets(Location newLocation, IEnumerable<Target> targets,
                                      IProgress<string> phaseProgress = null)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));
            if (targets == null)     throw new ArgumentNullException(nameof(targets));

            mChart.Series.Clear();
            foreach (ChartArea area in mChartAreaList)
            {
                area.AxisX.StripLines.Clear();  // dawn/dusk gradient + now line
                area.AxisY.StripLines.Clear();  // horizon line
            }
            mNowLines.Clear();
            mSeriesByTarget.Clear();
            mTargetList.Clear();

            Location = newLocation;

            foreach (Target t in targets)
            {
                if (t == null) continue;
                mTargetList.Add(t);
            }

            BuildTargetSeriesList(phaseProgress);
        }

        // Regenerate only the Optimal series in place for every target in the target list.
        // Day, Moon, and Year do not depend on Horizon or Duration so they stay untouched;
        // the Optimal recomputation walks the cache populated during the initial build and
        // avoids any ComputeNight / GetAltitudeAzimuth calls. Series object identity is
        // preserved (FindOrCreateSeries reuses them), so mChart.Series references stay valid
        // and the chart picks up the new points automatically.
        public void RebuildOptimalData(double horizon, TimeSpan duration)
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                SeriesFor(target).RebuildOptimalSeries(horizon, duration);
            }
            mChart.Invalidate();
        }

        // Move the green horizon strip line to the given horizon altitude on every chart
        // area. Clears any prior horizon line (identified by its green color) before adding
        // a fresh one so repeated calls don't accumulate strip lines. Horizon is passed in
        // rather than read from the snapshot so spinner-scrub updates the line live.
        public void UpdateHorizonLines(double horizon)
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
                replacement.IntervalOffset = horizon - 1;
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
            // NightWindow fields are UTC as of the Core DST fix; convert to local here because
            // the X axis is plotted in wall-clock time and the stripline positions are computed
            // by wall-clock minute/hour.
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();

            stripLine = new StripLine();

            duskOffset = (duskLocal.Minute > 30.0) ? 0.0 : -1.0;
            start = duskLocal.AddHours(duskOffset).Date.AddHours(duskLocal.AddHours(duskOffset).Hour);
            stop = duskLocal;

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

            dawnOffset = (dawnLocal.Minute > 30.0) ? 2.0 : 1.0;
            start = dawnLocal;
            stop = dawnLocal.AddHours(dawnOffset).Date.AddHours(dawnLocal.AddHours(dawnOffset).Hour);
            delta = stop.Subtract(start);

            stripLine.BackSecondaryColor = Color.FromArgb(145, 255, 238, 88);
            stripLine.BackColor = Color.FromArgb(255, 90, 90, 90);
            stripLine.BackGradientStyle = GradientStyle.LeftRight;
            stripLine.IntervalOffsetType = DateTimeIntervalType.Minutes;
            stripLine.Interval = 0;
            stripLine.IntervalType = DateTimeIntervalType.Minutes;
            stripLine.StripWidth = delta.TotalMinutes;

            start = duskLocal.AddHours(duskOffset).Date.AddHours(duskLocal.AddHours(duskOffset).Hour);
            delta = dawnLocal.Subtract(start);

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

        // Dispose the underlying Chart control. Series / ChartAreas / StripLines owned by the
        // Chart are disposed transitively. Safe to call more than once. Callers that swap the
        // AltitudeChart (Button_GraphTarget_Click's tear-and-rebuild) should Dispose the
        // prior instance before replacing; repeated clicks otherwise leak GDI handles.
        private bool mDisposed;
        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            mChart?.Dispose();
        }
    }
}
