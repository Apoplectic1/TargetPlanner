using Astronomy.Core.Astrometry;
using Astronomy.Core.Moon;
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

        // Active moon-avoidance profile for every target rendered in this chart. Setter
        // pushes the new value to every AltitudeSeries currently in mSeriesByTarget so
        // their next RebuildDayTooltip / RebuildOptimalSeries pass picks it up. Caller
        // is responsible for triggering RebuildOptimalData(horizon, duration) after a
        // change -- mirrors the Horizon / Duration pattern.
        //
        // Null is the backwards-compatible default and means "no moon avoidance":
        // BestSession.For's overload short-circuits to the moon-blind path.
        private MoonAvoidanceProfile mMoonAvoidanceProfile;
        public MoonAvoidanceProfile MoonAvoidanceProfile
        {
            get { return mMoonAvoidanceProfile; }
            set
            {
                mMoonAvoidanceProfile = value;
                if (mSeriesByTarget == null) return;
                foreach (AltitudeSeries s in mSeriesByTarget.Values)
                {
                    s.MoonAvoidanceProfile = value;
                }
            }
        }

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

        // Shared Moon-Day series, built once per Graph click (not once per target). Moon
        // altitude depends only on Location and time, not on the observed target, so the
        // per-target build path that used to produce N identical Moon-Day Series (one per
        // AltitudeSeries, of which N-1 were silently dropped by ShowChartAreaSeries' dedup)
        // is pure waste -- and worse, each one was 720 gated CoordinateSharp calls.
        // ReloadWithTargets computes one moon curve on the same background task that builds
        // the NightCache; ShowChartAreaSeries splices it into mChart.Series when the Day
        // area is selected. Null on the startup fire-and-forget path (AltitudeSeries still
        // builds its own inline when no NightCache is provided -- one target at startup,
        // so the per-target cost is bounded).
        private Series mSharedMoonSeries;

        // Day-chart "best-window" click state. On left-click over a target's Day curve, the
        // handler snapshots the series' current Y values into this dictionary and overwrites
        // them with a step function (0 outside the window, floor altitude inside) tracing
        // that target's best D-hour session. Right-click anywhere on the chart walks the
        // dictionary and restores every series' original Y values. Reloaded on each Graph
        // click (stale Series refs don't survive mChart.Series.Clear() in ReloadWithTargets).
        //
        // Stored as double[] (not DataPoint[]) because the chart uses IsXValueIndexed=true
        // on the Day area -- all Day series must have the SAME point count on every paint
        // (Moon-Day alignment invariant), so we can't swap in a sparse 4-point rectangle.
        // Only Y values change; X values (the minute grid) are preserved.
        private Dictionary<Series, double[]> mReplacedDayBackup;

        // Cache store reference (Phase 3 of the SoC refactor). Constructor-injected by
        // MainForm. AltitudeChart hands it through to every AltitudeSeries it creates,
        // and uses it in ReloadWithTargets to drive the per-(Location, Target) cache
        // build instead of calling the gated NightCalculator directly.
        private readonly TargetPlanner.Caches.IChartCacheStore mCache;

        public AltitudeChart(Location location, TargetPlanner.Caches.IChartCacheStore cache = null)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            Location = location;
            mCache = cache;

            mChart = new Chart();
            mChartAreaList = new List<ChartArea>();
            mTargetList = new List<Target>();
            mLegend = new Legend();
            mUIState = new Support.UIState();
            mSeriesByTarget = new Dictionary<Target, AltitudeSeries>();
            mTargetColors = new Dictionary<Target, Color>();
            mNowLines = new Dictionary<string, StripLine>();
            mHorizonLines = new Dictionary<string, StripLine>();
            mReplacedDayBackup = new Dictionary<Series, double[]>();

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
                series = new AltitudeSeries(Location, target, color, mCache);
                series.MoonAvoidanceProfile = mMoonAvoidanceProfile;
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
                Series reference = FindReferenceSeries(area.Name);
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

        // First non-empty Series in any target whose name ends with "-{areaName}". Used by
        // UpdateNowLine to anchor the strip-line offset against. UI-thread-only so we
        // iterate the live lists directly (no defensive ToList copies).
        private Series FindReferenceSeries(string areaName)
        {
            string suffix = "-" + areaName;
            foreach (Target target in mTargetList)
            {
                if (target == null) continue;
                foreach (Series s in SeriesFor(target).TargetSeriesList)
                {
                    if (s.Points.Count > 0 && s.Name.EndsWith(suffix)) return s;
                }
            }
            return null;
        }

        private void Chart_MouseClick(object sender, MouseEventArgs e)
        {
            // Right-click anywhere on the chart restores every Day-curve that was replaced
            // with a best-window rectangle. Doesn't require a specific HitTest -- the user's
            // model is "I'm done with the overlay, take me back to the real curves", not
            // "restore the one I clicked".
            if (e.Button == MouseButtons.Right)
            {
                RestoreAllReplacedCurves();
                return;
            }

            HitTestResult result = mChart.HitTest(e.X, e.Y);
            if (result == null || result.Object == null) return;

            if (result.Object is LegendItem && e.Button == MouseButtons.Left)
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
                return;
            }

            // Left-click on a target's Day-chart curve toggles the best-window overlay:
            // - Unreplaced curve -> overwrite with the best D-hour window step.
            // - Already-replaced curve -> restore just this one target's original altitude
            //   curve (right-click anywhere still clears every replacement in one shot).
            // Gated on the Day chart area so Year / Optimal stay unaffected. The Moon-Day
            // series is target-independent and skipped.
            //
            // Don't gate on `result.Object is DataPoint`: HitTest's DataPoint classification
            // is stricter than the chart's tooltip proximity check, so clicking on a visibly-
            // over-the-line pixel can report a non-DataPoint object even when Series is
            // correctly set. `result.Series != null` with a "-Day" name suffix is the right
            // signal.
            if (e.Button == MouseButtons.Left
                && result.ChartArea != null && result.ChartArea.Name == "Day"
                && result.Series != null)
            {
                string seriesName = result.Series.Name ?? "";
                if (seriesName == "Moon-Day") return;
                if (!seriesName.EndsWith("-Day", StringComparison.Ordinal)) return;

                ToggleDayCurveWindow(result.Series);
            }
        }

        // Locate the AltitudeSeries whose TargetSeriesList owns the given chart Series. Used
        // by the Day-click handler to read the cached best-window triple. Returns null if no
        // match -- caller treats that as "skip".
        private AltitudeSeries FindOwnerOfSeries(Series s)
        {
            foreach (AltitudeSeries owner in mSeriesByTarget.Values)
            {
                if (owner == null) continue;
                foreach (Series candidate in owner.TargetSeriesList)
                {
                    if (ReferenceEquals(candidate, s)) return owner;
                }
            }
            return null;
        }

        // Toggle the best-window overlay for a single Day-chart Series:
        // - Not currently replaced -> overwrite Y values in place with a step function
        //   (floor altitude inside the best D-hour window, 0 outside). Preserves point
        //   count and X values so IsXValueIndexed alignment with Moon-Day (and sibling
        //   target Day series) is maintained -- the chart throws on paint if Day series
        //   go out of alignment. Visually: flat at y=0 before the window, a near-vertical
        //   step up at window start (one-minute diagonal), flat at y=floor across the
        //   window, near-vertical step down at window end, flat at y=0 after.
        // - Currently replaced -> restore this one series' original Y values from the
        //   backup dictionary. Right-click still restores everything in one shot; this
        //   single-curve path is for per-target undo.
        //
        // Bails silently if the owning AltitudeSeries has no best-window for tonight.
        private void ToggleDayCurveWindow(Series s)
        {
            if (mReplacedDayBackup.TryGetValue(s, out double[] savedBackup))
            {
                RestoreOneReplacedCurve(s, savedBackup);
                mReplacedDayBackup.Remove(s);
                mChart.Invalidate();
                return;
            }

            AltitudeSeries owner = FindOwnerOfSeries(s);
            if (owner == null) return;

            var window = owner.BestDayWindow;
            if (window == null) return;

            int count = s.Points.Count;
            if (count == 0) return;

            // Snapshot Y values before overwrite. X values are grid-driven and immutable,
            // so the Y array is a complete restore record.
            double[] backup = new double[count];
            for (int i = 0; i < count; i++)
            {
                backup[i] = s.Points[i].YValues[0];
            }

            ApplyOverlayStepFunction(s, window);
            mReplacedDayBackup[s] = backup;

            // Series.Points in-place YValues updates don't always auto-invalidate the
            // chart; force a repaint so the rectangle appears without waiting for the
            // next unrelated invalidation.
            mChart.Invalidate();
        }

        // Overwrite a Day-chart Series' Y values in place with a step function: floor altitude
        // inside the best D-hour window, 0 outside. Preserves point count and X values so
        // IsXValueIndexed alignment with Moon-Day (and sibling target Day series) is
        // maintained. Window null -> flatten Y to 0 across the whole curve (overlay toggle
        // stays active in mReplacedDayBackup; when the user scrubs back to a qualifying
        // state, the next call will re-render the step properly).
        //
        // Shared by ToggleDayCurveWindow (initial click) and RebuildOptimalData (per-target
        // refresh on Horizon/Duration spinner debounce ticks).
        private static void ApplyOverlayStepFunction(Series s,
            (DateTime Start, DateTime End, double Floor)? window)
        {
            int count = s.Points.Count;
            if (count == 0) return;

            if (window == null)
            {
                for (int i = 0; i < count; i++) s.Points[i].YValues[0] = 0.0;
                return;
            }

            double startOa = window.Value.Start.ToOADate();
            double endOa   = window.Value.End.ToOADate();
            for (int i = 0; i < count; i++)
            {
                double xOa = s.Points[i].XValue;
                s.Points[i].YValues[0] = (xOa >= startOa && xOa <= endOa) ? window.Value.Floor : 0.0;
            }
        }

        // Write a previously-snapshotted Y-value array back onto the given Series in place.
        // Shared by the single-curve toggle (left-click on a replaced curve) and the
        // restore-all path (right-click). The Min() guard defends against a mid-flight
        // rebuild that shortened the series between snapshot and restore.
        private static void RestoreOneReplacedCurve(Series s, double[] backup)
        {
            int n = Math.Min(backup.Length, s.Points.Count);
            for (int i = 0; i < n; i++)
            {
                s.Points[i].YValues[0] = backup[i];
            }
        }

        // Restore every Series whose Y values were overwritten with a best-window step.
        // Walks the dictionary so multi-target replacements all unwind at once. No-op when
        // nothing was replaced.
        private void RestoreAllReplacedCurves()
        {
            if (mReplacedDayBackup.Count == 0) return;

            foreach (var kv in mReplacedDayBackup)
            {
                RestoreOneReplacedCurve(kv.Key, kv.Value);
            }
            mReplacedDayBackup.Clear();
            mChart.Invalidate();
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

        // Register every entry in mChartAreaList with mChart.ChartAreas. Idempotent: skips
        // when already registered. All areas start hidden (Visible = false); ShowChartAreaSeries
        // flips one to active. Splitting registration from visibility lets startup callers
        // (InitializeDynamicControls) register areas eagerly so UpdateHorizonLines / other
        // pre-render setup can index mChart.ChartAreas before the first ShowChartAreaSeries.
        public void RegisterChartAreas()
        {
            if (mChart.ChartAreas.Count != 0) return;
            foreach (ChartArea area in mChartAreaList)
            {
                area.Visible = false;
                mChart.ChartAreas.Add(area);
            }
        }

        // Make chartAreaName the active (visible) area; hide the others. Keeping every
        // ChartArea resident in mChart.ChartAreas (rather than clear+re-add per switch)
        // preserves the user's zoom and legend-color-toggle state across radio-button flips.
        private void SetActiveChartArea(string chartAreaName)
        {
            RegisterChartAreas();
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

            SetActiveChartArea(chartAreaName);

            // Horizon strip line placement is NOT done here. ReloadWithTargets seeds the
            // lines on every chart area at the snapshot Horizon; subsequent spinner scrubs
            // drive UpdateHorizonLines which repositions all three in sync. Letting this
            // method also call SetHorizonLine would revert the visible area's line to the
            // stale snapshot Horizon on every radio-button switch, making it look like the
            // spinner got disconnected after a single switch-away-and-back cycle.

            ClearSeries();

            // Shared Moon-Day series (hoisted off the per-target build) goes in first so
            // target Day lines render on top of the moon area fill. Null when Reload didn't
            // produce one (polar day / cancelled mid-build); also null on the startup path
            // where AltitudeSeries still builds its own per-target Moon-Day inline and the
            // existing dedup loop below handles it.
            if (chartAreaName == "Day" && mSharedMoonSeries != null
                && mChart.Series.IndexOf(mSharedMoonSeries.Name) < 0)
            {
                mSharedMoonSeries.ChartArea = chartAreaName;
                mSharedMoonSeries.Enabled = true;
                AddSeries(mSharedMoonSeries);
            }

            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                foreach (Series series in SeriesFor(target).TargetSeriesList.ToList())
                {
                    if (series.Name.Contains(chartAreaName))
                    {
                        // Target-independent series (currently "Moon-Day" -- the startup
                        // fallback path still builds per-target; Reload builds once into
                        // mSharedMoonSeries) get built once per target's TargetSeriesList.
                        // Adding the 2nd+ copy to mChart.Series throws on duplicate name, so
                        // the first instance wins and the rest are disabled.
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

        // Fire-and-forget per-target build on the already-committed state. Used at startup
        // where there's no prior chart to preserve; the Moon / Day / FindOrCreateSeries
        // phase of BuildSeriesList runs synchronously on the caller's thread before its
        // first await, so TargetSeriesList is populated with the Day Series by the time
        // InitializeDynamicControls' following ShowChartAreaSeries("Day") line runs. The
        // Year + Optimal phases continue in the background and populate the same
        // AltitudeSeries; the chart picks them up when the user switches to those radios.
        //
        // User-initiated graph clicks go through ReloadWithTargets instead, which stages
        // the whole build off to the side (new AltitudeSeries instances in a local dict),
        // waits for all targets to finish via Task.WhenAll, and swaps atomically.
        //
        // phaseProgress (if non-null) fires "Day" / "Year" / "Optimal" once per target.
        public void BuildTargetSeriesList(IProgress<string> phaseProgress = null,
                                          CancellationToken ct = default)
        {
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                // BuildSeriesList no longer swallows exceptions, so a bare `_ = .BuildSeries-
                // List(...)` would lose OperationCanceledException / compute failures to
                // TaskScheduler.UnobservedTaskException. Wrap to at least get a debug log.
                _ = BuildSeriesListWithLogging(SeriesFor(target), phaseProgress, ct);
            }
        }

        private static async Task BuildSeriesListWithLogging(
            AltitudeSeries series, IProgress<string> phaseProgress, CancellationToken ct)
        {
            try { await series.BuildSeriesList(phaseProgress, ct); }
            catch (OperationCanceledException) { /* expected -- caller cancelled */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"AltitudeSeries.BuildSeriesList (startup path) failed: {ex}");
            }
        }

        // Builds the target-independent Moon-Day series once per Graph click. The minute-loop
        // calls AstroUtil.GetMoonAltitude (Meeus, lock-free, ~25 us/call) for every minute of
        // the night window (~720 calls for a typical 12-hour night). Running this once instead
        // of per-target is the other half of the multi-target speedup; the lock-free Meeus
        // backend made the gate-amortization concern go away, but the once-per-Graph-click
        // hoist remains useful (no point in N copies of identical work).
        //
        // Returns null on polar day / polar night (no valid dusk/dawn to bracket); ShowChart-
        // AreaSeries tolerates a null mSharedMoonSeries and simply omits the moon curve.
        private static Series BuildSharedMoonSeries(Location location, NightWindow night, CancellationToken ct)
        {
            if (!night.IsValid) return null;

            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(location.DateTime);
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            // Day-chart start/stop rounded to hour boundaries; matches BuildDaySeries.
            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();
            DateTime start = AltitudeSeries.DayChartStart(duskLocal);
            DateTime stop  = AltitudeSeries.DayChartStop(dawnLocal);

            Series moon = new Series
            {
                Name = "Moon-Day",
                Color = Color.FromArgb((int)(night.LunarIlluminationFraction * 250.0), 209, 209, 209),
                IsXValueIndexed = true,
                XValueType = ChartValueType.DateTime,
                ChartType = SeriesChartType.Area,
            };
            moon.IsVisibleInLegend = false;

            TimeSpan delta = stop.Subtract(start);
            int totalMinutes = Convert.ToInt32(Math.Round(delta.TotalMinutes, 0));
            for (int minutes = 0; minutes <= totalMinutes; minutes++)
            {
                ct.ThrowIfCancellationRequested();
                DateTime point = start.AddMinutes(minutes);
                DateTime pointUtc = new DateTimeOffset(
                    DateTime.SpecifyKind(point, DateTimeKind.Unspecified), utcOffset).UtcDateTime;
                double moonAlt = AstroUtil.GetMoonAltitude(pointUtc, observer);
                moon.Points.AddXY(point, moonAlt);
            }

            return moon;
        }

        // Reset all transient state on this chart for a fresh Graph-click cycle, swap in a
        // new Location / target list, pre-compute the shared NightCache on a background
        // thread, and kick off per-target BuildSeriesList. Keeps the Chart control, its
        // ChartArea instances, its Legend, and any user zoom / legend-color-toggle state
        // alive across reloads -- only series, strip lines, per-target AltitudeSeries cache,
        // target list, and the Location snapshot actually change.
        //
        // Shared NightCache: NightCalculator.ComputeNight depends only on lat/lon/date (not
        // on the observed target), but multi-target builds prior to this refactor had each
        // target's ComputeYearCache re-derive the same 365-day NightWindow set independently,
        // each call serialised on CoordinateSharpGate's process-wide lock. For 44 targets
        // that's ~32000 gated calls. Pre-computing one NightCache and handing it to every
        // AltitudeSeries reduces the gated Year work to a single ~730-call pass on a
        // threadpool thread; per-target Year work then becomes pure-math AltAz that
        // parallelizes freely.
        //
        // Returns Task.WhenAll of the per-target builds so the caller can await the
        // "all phases complete" signal. Because the cache build is behind the first await,
        // the caller must also wait for this method before running ShowChartAreaSeries --
        // mSeriesByTarget is not populated until after the cache is ready.
        public async Task ReloadWithTargets(Location newLocation, IEnumerable<Target> targets,
                                            IProgress<string> phaseProgress = null,
                                            CancellationToken ct = default)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));
            if (targets == null)     throw new ArgumentNullException(nameof(targets));

            mChart.Series.Clear();
            foreach (ChartArea area in mChartAreaList)
            {
                area.AxisX.StripLines.Clear();
                area.AxisY.StripLines.Clear();
            }
            mNowLines.Clear();
            mHorizonLines.Clear();
            mSeriesByTarget.Clear();
            mTargetColors.Clear();
            mTargetList.Clear();
            mSharedMoonSeries = null;
            mReplacedDayBackup.Clear();

            Location = newLocation;

            UpdateHorizonLines(newLocation.Horizon);

            // mTargetList order == legend order. Color assignment uses this same index so
            // the color a target gets is the color its legend row displays.
            foreach (Target t in targets)
            {
                if (t == null) continue;
                mTargetColors[t] = TargetColorPalette[mTargetList.Count % TargetColorPalette.Length];
                mTargetList.Add(t);
            }

            // Phase 3: ChartCacheStore owns the per-Location NightCache + per-target year
            // cache. We delegate the per-target build by calling PrepareManyAsync; once the
            // store has the per-Location NightCache published, BuildSharedMoonSeries can
            // use its Starting NightWindow.
            //
            // If MainForm has been kicking off pre-population in the background after each
            // NINA load, most targets will already be in the cache and PrepareManyAsync's
            // Task.WhenAll completes near-instantly. New targets (or new location) build
            // here. Cancellation surfaces as OperationCanceledException -- caller catches.
            Series moonSeries;
            try
            {
                if (mCache != null)
                {
                    // Drives the per-Location NightCache (one CoordinateSharp gate hit) +
                    // per-target year cache builds (gated by the store's concurrency cap).
                    await mCache.PrepareManyAsync(mTargetList, ct);
                }

                Astronomy.Core.Night.NightWindow startingNight =
                    mCache?.LocationNightCache?.Starting
                    ?? Astronomy.Core.Night.NightCalculator.ComputeNight(newLocation);

                moonSeries = await Task.Run(() => BuildSharedMoonSeries(newLocation, startingNight, ct), ct);
            }
            catch (OperationCanceledException)
            {
                // User cancelled before the cache finished building; leave mSeriesByTarget
                // empty. Button_Graph_Click's post-await ShowChartAreaSeries will find
                // nothing to add, which is the right behavior for a mid-build cancel.
                return;
            }

            mSharedMoonSeries = moonSeries;
            phaseProgress?.Report("SharedCache");

            // Eagerly construct AltitudeSeries instances, populating mSeriesByTarget up
            // front. Each series reads its per-target cache entry from mCache during
            // BuildSeriesList. SeriesFor's lazy-init branch (used by non-reload callers,
            // e.g. UpdateNowLine before a Graph click) is bypassed here.
            foreach (Target target in mTargetList)
            {
                if (target == null) continue;
                AltitudeSeries seriesForTarget = new AltitudeSeries(
                    Location, target, mTargetColors[target], mCache);
                seriesForTarget.MoonAvoidanceProfile = mMoonAvoidanceProfile;
                mSeriesByTarget[target] = seriesForTarget;
            }

            // Kick off per-target BuildSeriesList. Each runs its Moon / Day sync preamble
            // on THIS (UI) thread before its first await; Day now reads from nightCache.
            // Starting, Year reads from nightCache.YearDays[i]. Moon still calls the gate
            // per minute (Commit 2 hoists BuildMoonSeries out to AltitudeChart to amortize
            // that across targets too).
            var tasks = new List<Task>(mTargetList.Count);
            foreach (Target target in mTargetList)
            {
                if (target == null) continue;
                tasks.Add(BuildSeriesListWithLogging(mSeriesByTarget[target], phaseProgress, ct));
            }
            await Task.WhenAll(tasks);
        }

        // Regenerate only the Optimal series in place for every target in the target list.
        // Day, Moon, and Year do not depend on Horizon or Duration so they stay untouched;
        // the Optimal recomputation walks the cache populated during the initial build and
        // avoids any ComputeNight / GetAltitudeAzimuth calls. Series object identity is
        // preserved (FindOrCreateSeries reuses them), so mChart.Series references stay valid
        // and the chart picks up the new points automatically.
        public void RebuildOptimalData(double horizon, TimeSpan duration)
        {
            string daySuffix = "-Day";
            foreach (Target target in mTargetList.ToList())
            {
                if (target == null) continue;
                AltitudeSeries series = SeriesFor(target);
                series.RebuildOptimalSeries(horizon, duration);
                // Day-series hover tooltip summarises the best D-hour window; Horizon /
                // Duration spinner scrubs feed through the same path so the tooltip stays
                // in sync with what the Optimal curves display. RebuildDayTooltip also
                // refreshes the target's mBestDayWindow as a side effect (consumed below).
                series.RebuildDayTooltip(horizon, duration);

                // Re-render any active best-window overlay for this target's Day curve so
                // the step rectangle reflects the just-updated BestDayWindow. Without this
                // the points carry stale values from the click-toggle moment, and the user
                // sees no change in the overlay despite scrubbing the spinners.
                string dayName = target.Name + daySuffix;
                foreach (Series s in series.TargetSeriesList)
                {
                    if (s.Name == dayName && mReplacedDayBackup.ContainsKey(s))
                    {
                        ApplyOverlayStepFunction(s, series.BestDayWindow);
                        break;
                    }
                }
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
