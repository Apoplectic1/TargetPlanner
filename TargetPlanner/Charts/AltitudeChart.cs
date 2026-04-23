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

        // Explicit per-target color, assigned once by ReloadWithTargets from TargetColorPalette
        // by the target's index in mTargetList. Every Series the target produces (Day / Year /
        // Optimal / OptimalFloor / OptimalFloorCentered) picks up this color at construction.
        // See TargetColorPalette for the rationale on explicit vs auto-palette.
        private Dictionary<Target, Color> mTargetColors;

        // Stable color palette for target series. Picked for readability against the dark gray
        // chart background (70,70,70) and distinctness from the red now-line, green horizon
        // line, and yellow dusk/dawn gradient. Assignment is by target index in mTargetList
        // (so positions 0..N-1 map to palette[i % N]); with 12 entries, wrap-around only hits
        // on very large target sets.
        //
        // Explicit colors here replace the framework's implicit auto-palette, which assigns a
        // color to every Color.Empty series in the order they appear in mChart.Series and
        // skips series with explicit colors. That meant toggling one series to Transparent
        // (the hide-via-legend-click behavior) shifted every remaining Empty series one slot
        // down the palette, visibly reshuffling colors on the chart. Concrete per-target
        // colors opt out of that auto-assignment entirely.
        private static readonly Color[] TargetColorPalette = new[]
        {
            Color.FromArgb( 65, 140, 240),  // blue
            Color.FromArgb(252, 180,  65),  // gold
            Color.FromArgb(220, 100, 220),  // magenta
            Color.FromArgb(100, 220, 180),  // teal
            Color.FromArgb(255, 138, 128),  // salmon
            Color.FromArgb(180, 220, 100),  // lime
            Color.FromArgb(180, 150, 255),  // lavender
            Color.FromArgb(100, 200, 255),  // sky blue
            Color.FromArgb(255, 200, 100),  // peach
            Color.FromArgb(220, 220, 120),  // pale yellow-green
            Color.FromArgb(255, 150, 200),  // pink
            Color.FromArgb(150, 220, 150),  // sage
        };

        private Dictionary<string, StripLine> mNowLines;
        private Dictionary<string, StripLine> mHorizonLines;

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
            mTargetColors = new Dictionary<Target, Color>();
            mNowLines = new Dictionary<string, StripLine>();
            mHorizonLines = new Dictionary<string, StripLine>();

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
                // AltitudeSeries is an immutable snapshot: Location, Target, and the series
                // color are captured here and cannot be reassigned later. Colors are normally
                // pre-populated in mTargetColors by ReloadWithTargets; if a caller ever adds
                // a target through another path the lookup lazy-fills a palette slot here so
                // no series ever ends up with Color.Empty (which would re-enable the auto-
                // palette reshuffle we're explicitly avoiding).
                if (!mTargetColors.TryGetValue(target, out Color color))
                {
                    color = TargetColorPalette[mTargetColors.Count % TargetColorPalette.Length];
                    mTargetColors[target] = color;
                }
                series = new AltitudeSeries(Location, target, color);
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

                // Toggle hide / show by swapping Series.Color with Color.Transparent while
                // preserving the assigned color in Series.Tag. Prior behavior toggled between
                // Color.Empty (show, palette auto-assigns) and Color.Transparent (hide), which
                // caused the remaining visible series to re-index through the palette and
                // visibly shift colors on every click. Stashing the assigned color in Tag
                // means each series restores exactly the color it was built with, and
                // toggling one never perturbs another.
                if (series.Color == Color.Transparent)
                {
                    if (series.Tag is Color stashed) series.Color = stashed;
                }
                else
                {
                    series.Tag = series.Color;
                    series.Color = Color.Transparent;
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

            SetHorizonLine(chartAreaName, Location.Horizon);

            ClearSeries();

            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                foreach (Series series in SeriesFor(target).TargetSeriesList.ToList())
                {
                    if (series.Name.Contains(chartAreaName))
                    {
                        // Target-independent series (currently "Moon-Day" -- BuildMoonSeries
                        // uses that literal name for every target) get built once per
                        // target's TargetSeriesList. Adding the 2nd+ copy to mChart.Series
                        // throws on duplicate name. The curves would be identical anyway
                        // (moon altitude depends on location, not on the target), so keep
                        // the first instance and disable the rest.
                        if (mChart.Series.IndexOf(series.Name) >= 0)
                        {
                            series.Enabled = false;
                            continue;
                        }
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
                        // Strip the trailing "-{ChartAreaName}" suffix only -- LastIndexOf,
                        // not IndexOf, because target names like "M27 - Dumbbell" contain
                        // their own dashes and IndexOf("-") would chop the legend at the
                        // first one ("M27 " instead of "M27 - Dumbbell").
                        series.LegendText = series.Name.Remove(series.Name.LastIndexOf("-"));
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
            mHorizonLines.Clear();
            mSeriesByTarget.Clear();
            mTargetColors.Clear();
            mTargetList.Clear();

            Location = newLocation;

            // mTargetList order == legend order (ShowChartAreaSeries adds to mChart.Series in
            // this order, and the Chart legend mirrors mChart.Series). Color assignment uses
            // this same index so the color a target gets is the color its legend row displays.
            foreach (Target t in targets)
            {
                if (t == null) continue;
                mTargetColors[t] = TargetColorPalette[mTargetList.Count % TargetColorPalette.Length];
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

        // Place or reposition the green horizon strip line on the given chart area. Tracks
        // one StripLine per area in mHorizonLines so repeated calls reposition the same
        // instance instead of adding new ones. Replaces a prior color-equality scheme that
        // failed to detect stale lines (Color.Green round-trips through ARGB inside
        // StripLine.BackColor, breaking the `sl.BackColor == Color.Green` filter and
        // causing one line to accumulate per spinner change).
        private void SetHorizonLine(string chartAreaName, double horizon)
        {
            StripLine line;
            if (!mHorizonLines.TryGetValue(chartAreaName, out line))
            {
                line = new StripLine
                {
                    Interval = 0,
                    StripWidth = 2,
                    BackColor = Color.Green,
                };
                mHorizonLines[chartAreaName] = line;
                mChart.ChartAreas[chartAreaName].AxisY.StripLines.Add(line);
            }
            line.IntervalOffset = horizon - 1;
        }

        // Reposition the green horizon strip line on every registered chart area. Horizon is
        // passed in rather than read from the snapshot so spinner-scrub updates the line live.
        public void UpdateHorizonLines(double horizon)
        {
            foreach (ChartArea area in mChartAreaList)
                SetHorizonLine(area.Name, horizon);
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
                    mChart.ChartAreas[chartAreaName].AxisY.Minimum = 0.0;
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

            // The Chart's auto-fit logic on AxisX drops the last label when it decides the
            // label text would clip against the chart-area edge. Force the end label visible
            // and disable auto-fit so the rightmost hourly / monthly label always renders.
            mChart.ChartAreas[chartAreaName].AxisX.IsLabelAutoFit = false;
            mChart.ChartAreas[chartAreaName].AxisX.LabelStyle.IsEndLabelVisible = true;

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

        public void AddDawnDuskGradient(string chartAreaName)
        {
            NightWindow night = NightCalculator.ComputeNight(Location);
            // NightWindow fields are UTC as of the Core DST fix; convert to local here because
            // the X axis is plotted in wall-clock time and the stripline positions are computed
            // by wall-clock minute/hour.
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();

            // Anchor stripe positions to the same Day-chart bounds the data series uses so the
            // gradients align with the X axis. Index 0 = chartStart; everything else is in
            // wall-clock minutes off chartStart.
            DateTime chartStart = AltitudeSeries.DayChartStart(duskLocal);
            DateTime chartStop  = AltitudeSeries.DayChartStop(dawnLocal);

            // Dusk gradient: chartStart -> dusk (yellow into gray).
            StripLine duskStripe = new StripLine();
            duskStripe.BackColor             = Color.FromArgb(145, 255, 238, 88);
            duskStripe.BackSecondaryColor    = Color.FromArgb(255,  90,  90, 90);
            duskStripe.BackGradientStyle     = GradientStyle.LeftRight;
            duskStripe.IntervalOffsetType    = DateTimeIntervalType.Minutes;
            duskStripe.Interval              = 0;
            duskStripe.IntervalType          = DateTimeIntervalType.Minutes;
            duskStripe.IntervalOffset        = 0;
            duskStripe.StripWidth            = duskLocal.Subtract(chartStart).TotalMinutes;
            mChart.ChartAreas[chartAreaName].AxisX.StripLines.Add(duskStripe);

            // Dawn gradient: dawn -> chartStop (gray into yellow).
            StripLine dawnStripe = new StripLine();
            dawnStripe.BackColor             = Color.FromArgb(255,  90,  90, 90);
            dawnStripe.BackSecondaryColor    = Color.FromArgb(145, 255, 238, 88);
            dawnStripe.BackGradientStyle     = GradientStyle.LeftRight;
            dawnStripe.IntervalOffsetType    = DateTimeIntervalType.Minutes;
            dawnStripe.Interval              = 0;
            dawnStripe.IntervalType          = DateTimeIntervalType.Minutes;
            dawnStripe.IntervalOffset        = dawnLocal.Subtract(chartStart).TotalMinutes;
            dawnStripe.StripWidth            = chartStop.Subtract(dawnLocal).TotalMinutes;
            mChart.ChartAreas[chartAreaName].AxisX.StripLines.Add(dawnStripe);
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
