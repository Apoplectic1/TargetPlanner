using Astronomy.Core.Night;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // Read-only view of the currently-graphed targets in legend order. Callers that want
        // to reorder the legend should walk Targets, compute the new sequence, and call
        // ReorderTargets(newSequence) -- do not mutate this list directly.
        public IReadOnlyList<Target> Targets { get { return mTargetList; } }

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

            // Horizon strip line placement is NOT done here. ReloadWithTargets seeds the
            // lines on every chart area at the snapshot Horizon; subsequent spinner scrubs
            // drive UpdateHorizonLines which repositions all three in sync. Letting this
            // method also call SetHorizonLine would revert the visible area's line to the
            // stale snapshot Horizon on every radio-button switch, making it look like the
            // spinner got disconnected after a single switch-away-and-back cycle.

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

        // Synchronous per-target build on the already-committed state. Used at startup
        // where InitializeDynamicControls expects the Day series to be populated in
        // TargetSeriesList by the time it calls ShowChartAreaSeries on the next line.
        // User-initiated graph clicks go through ReloadWithTargets instead, which stages
        // the whole build off to the side on a Task.Run thread and swaps atomically after
        // WhenAll completes.
        //
        // Runs on the caller's thread (UI, at startup). For a single seed target (M31 by
        // default) the freeze is short and bounded; the form is still in its constructor
        // when this fires, so the window simply appears a beat later fully rendered rather
        // than appearing blank and populating afterwards.
        //
        // phaseProgress (if non-null) fires "Day" / "Year" / "Optimal" once per target.
        public void BuildTargetSeriesList(IProgress<string> phaseProgress = null,
                                          CancellationToken ct = default)
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                try
                {
                    SeriesFor(target).BuildSeriesListBlocking(phaseProgress, ct);
                }
                catch (OperationCanceledException) { /* expected -- caller cancelled */ }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"AltitudeSeries.BuildSeriesListBlocking (startup path) failed: {ex}");
                }
            }
        }

        private enum BuildResult { Success, Cancelled, Failed }
        private readonly struct BuildOutcome
        {
            public readonly Target Target;
            public readonly BuildResult Result;
            public readonly Exception Error;
            public BuildOutcome(Target t, BuildResult r, Exception e)
            { Target = t; Result = r; Error = e; }
        }

        // Staged, atomic reload. Builds a fresh set of AltitudeSeries off to the side while
        // the prior chart remains visible and interactive. Only after all target builds
        // finish (success, error, or cancellation) does the swap land -- mChart.Series
        // gets cleared, strip lines are reseeded, the committed dictionaries are replaced.
        //
        // Returns true if the swap happened (chart now shows new data, possibly partial),
        // false if nothing committed (prior chart still live). Callers use the return
        // value to decide whether to run the "update display" follow-up (radio button,
        // ShowChartAreaSeries, ChartTitle, UpdateNowLine) or skip it to leave the chart
        // exactly as it was.
        //
        // Cancellation / error semantics:
        //   - Outer ct (e.g. MainForm's mGraphCts) fires -> tasks observe cancellation,
        //     no swap, returns false.
        //   - Per-target exception -> modal dialog offers "Cancel remaining & show what
        //     succeeded" (partial swap, returns true) vs. "Continue (suppress further
        //     dialogs)" (build remaining targets, silently skip failed ones in the swap).
        //   - If every target failed or was cancelled, returns false (prior chart stays).
        public async Task<bool> ReloadWithTargets(Location newLocation, IEnumerable<Target> targets,
                                                  IProgress<string> phaseProgress = null,
                                                  CancellationToken ct = default)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));
            if (targets == null)     throw new ArgumentNullException(nameof(targets));

            // Stage new state -- no visible UI mutation below until the swap at the end.
            var newTargetList = new List<Target>();
            var newSeriesByTarget = new Dictionary<Target, AltitudeSeries>();
            var newTargetColors = new Dictionary<Target, Color>();

            foreach (Target t in targets)
            {
                if (t == null) continue;
                Color color = TargetColorPalette[newTargetList.Count % TargetColorPalette.Length];
                newTargetColors[t] = color;
                newTargetList.Add(t);
                newSeriesByTarget[t] = new AltitudeSeries(newLocation, t, color);
            }

            if (newTargetList.Count == 0) return false;

            // Linked CTS distinguishes "outer Cancel" (propagated from ct -- e.g. the
            // MainForm Cancel button) vs. "error-dialog Cancel" (we Cancel linkedCts
            // locally and set dialogCancelled so the post-await branch knows to do the
            // partial swap instead of returning).
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                bool dialogCancelled = false;
                bool suppressDialogs = false;

                async Task<BuildOutcome> WrapBuild(Target target)
                {
                    try
                    {
                        await newSeriesByTarget[target].BuildSeriesList(phaseProgress, linkedCts.Token);
                        return new BuildOutcome(target, BuildResult.Success, null);
                    }
                    catch (OperationCanceledException)
                    {
                        return new BuildOutcome(target, BuildResult.Cancelled, null);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"AltitudeSeries.BuildSeriesList failed for target '{target?.Name}': {ex}");

                        if (!suppressDialogs && !dialogCancelled)
                        {
                            // Preemptive set: WinForms MessageBox pumps messages while open,
                            // so a parallel target's catch running on the UI thread could
                            // re-enter here and stack a second dialog. Marking suppressDialogs
                            // true up front makes the reentrant check short-circuit. The user
                            // only chooses between "keep building (quietly)" and "stop now",
                            // so preempting doesn't lose anything.
                            suppressDialogs = true;

                            DialogResult dr = MessageBox.Show(
                                $"Could not build chart data for target '{target?.Name}':" +
                                Environment.NewLine + Environment.NewLine +
                                ex.Message + Environment.NewLine + Environment.NewLine +
                                "OK: keep building remaining targets (further failures will be " +
                                "suppressed and skipped silently)." + Environment.NewLine +
                                "Cancel: stop now and display only targets that have finished " +
                                "successfully.",
                                "Target build failed",
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Warning);

                            if (dr == DialogResult.Cancel)
                            {
                                dialogCancelled = true;
                                linkedCts.Cancel();
                            }
                        }
                        return new BuildOutcome(target, BuildResult.Failed, ex);
                    }
                }

                var wrappedTasks = newTargetList.Select(WrapBuild).ToList();
                await Task.WhenAll(wrappedTasks);

                // Outer Cancel (not dialog-cancel) fired during the build -> leave prior
                // chart exactly as it was. Partial success from a dialog-cancel continues
                // to the swap below.
                if (ct.IsCancellationRequested && !dialogCancelled) return false;

                // Commit successful targets only. If nothing succeeded, keep the prior chart.
                var commitTargets = new List<Target>();
                foreach (Task<BuildOutcome> t in wrappedTasks)
                {
                    if (t.Result.Result == BuildResult.Success) commitTargets.Add(t.Result.Target);
                }
                if (commitTargets.Count == 0) return false;

                // Atomic swap -- synchronous on UI thread, no yield points.
                mChart.Series.Clear();
                foreach (ChartArea area in mChartAreaList)
                {
                    area.AxisX.StripLines.Clear();
                    area.AxisY.StripLines.Clear();
                }
                mNowLines.Clear();
                mHorizonLines.Clear();

                Location = newLocation;

                // Preserve the identity of mTargetList / mSeriesByTarget / mTargetColors
                // (Targets property returns a view of mTargetList; callers may hold its
                // reference). Clear+Add instead of reassigning the fields.
                mTargetList.Clear();
                mSeriesByTarget.Clear();
                mTargetColors.Clear();
                foreach (Target t in commitTargets)
                {
                    mTargetList.Add(t);
                    mSeriesByTarget[t] = newSeriesByTarget[t];
                    mTargetColors[t] = newTargetColors[t];
                }

                UpdateHorizonLines(newLocation.Horizon);
            }
            return true;
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
                AltitudeSeries series = SeriesFor(target);
                series.RebuildOptimalSeries(horizon, duration);
                // Day-series hover tooltip summarises the best D-hour window; Horizon /
                // Duration spinner scrubs feed through the same path so the tooltip stays
                // in sync with what the Optimal curves display.
                series.RebuildDayTooltip(horizon, duration);
            }
            mChart.Invalidate();
        }

        // Re-order mTargetList and mChart.Series to match newOrder without recomputing any
        // per-target altitude points. The Series objects themselves stay -- their Points,
        // Color (explicit from TargetColorPalette), Tag (legend toggle stash), and ToolTip
        // are preserved; only their position in mChart.Series changes, which drives the
        // legend row order.
        //
        // Target-independent series like Moon-Day stay in place: the reorder only touches
        // series whose name starts with "{TargetName}-", so a Series named "Moon-Day" is
        // left wherever ShowChartAreaSeries put it.
        //
        // Set-equality is required (same Target instances, same count). A mismatch is a
        // caller bug; the method bails out silently rather than partially rewriting state.
        public void ReorderTargets(IEnumerable<Target> newOrder)
        {
            if (newOrder == null) throw new ArgumentNullException(nameof(newOrder));

            List<Target> newList = new List<Target>();
            foreach (Target t in newOrder)
            {
                if (t != null) newList.Add(t);
            }

            HashSet<Target> existing = new HashSet<Target>(mTargetList);
            if (newList.Count != existing.Count) return;
            foreach (Target t in newList)
            {
                if (!existing.Contains(t)) return;
            }

            mTargetList.Clear();
            mTargetList.AddRange(newList);

            // Push each target's per-target series to the end of mChart.Series in the new
            // order. After the loop, target-keyed series sit at the tail in newList order;
            // anything else (Moon-Day, future target-independent series) retains its prior
            // relative position in the head.
            foreach (Target t in newList)
            {
                string prefix = t.Name + "-";
                AltitudeSeries altSeries = SeriesFor(t);
                foreach (Series s in altSeries.TargetSeriesList.ToList())
                {
                    if (!s.Name.StartsWith(prefix)) continue;
                    int idx = mChart.Series.IndexOf(s);
                    if (idx < 0) continue;
                    mChart.Series.RemoveAt(idx);
                    mChart.Series.Add(s);
                }
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
        // AltitudeChart instance (rather than calling ReloadWithTargets) should Dispose the
        // prior instance before replacing; otherwise repeated swaps leak GDI handles.
        private bool mDisposed;
        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            mChart?.Dispose();
        }
    }
}
