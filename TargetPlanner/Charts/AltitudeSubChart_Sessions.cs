using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;
using TargetPlanner.Caches;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    // LiveCharts2 implementation of TP's Sessions chart area. Three curves per
    // target across the 12-month night-cache window:
    //   - Ceiling  ({TargetName}-Sessions)              -- best transit-centered-or-wall-pushed altitude (high endpoint).
    //   - Floor    ({TargetName}-SessionsFloor)         -- low endpoint of the same session.
    //   - Symmetric ({TargetName}-SessionsFloorCentered)-- strict transit-centered placement (either endpoint, since centered).
    //
    // Per-night fit decision uses the SAME candidates list (visibility ∩ moon-clear)
    // for both PlaceBest and PlaceCentered -- the public BestSession.ResolveCandidates
    // helper exists exactly so we don't run the moon mask twice per night.
    //
    // Owns one controller wired to its CartesianChart instance:
    //   - HoverTooltipController: per-DataPoint snap tooltip (30 ms debounce);
    //     custom formatter reads pre-formatted text from a parallel string[]
    //     populated during the visibility task. Same tooltip string is assigned
    //     to all three of a night's DataPoints so hovering any of the curves
    //     surfaces the full triple (Ceiling / Floor / Symmetric).
    //
    // Legend: ONE item per target, click toggles all three of that target's
    // series IsVisible together. Mirrors the legacy MS Charts behavior where
    // ShowChartAreaSeries collapsed the three sub-series under a single
    // {TargetName} legend label.
    //
    // No OverlayController (no HD overlay on Sessions). No moon series, no
    // dusk/dawn gradient (12-month axis has no single twilight context).
    //
    public class AltitudeSubChart_Sessions : IAltitudeSubChart
    {
        // Y axis bounds (altitude, degrees). 0-90 to match Day / Year so the
        // plot template stays uniform across radio swaps. Legacy MS Charts
        // Sessions used 10-90; the user's preference per PR4c is 0-90 across
        // all altitude charts.
        public const double MinAltitude = 0.0;
        public const double MaxAltitude = 90.0;

        // sin(altitude) airmass-weighted quality metric -- same probe Day / Sky
        // / Year use. PlaceBest ranks candidates by this when multiple sub-
        // intervals exist on the same night.
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly FlowLayoutPanel mLegendPanel;

        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly RectangularSection mNowLine;
        private readonly RectangularSection mHorizonLine;

        // Per-target series triples. All three keyed on the same Target so the
        // legend toggle / fit-result-application loops can iterate one dict and
        // touch all three companions.
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mCeilingByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mFloorByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mCenteredByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // Pre-formatted per-night unified tooltip text, keyed by Target. The
        // SAME text is read by all three series' formatters for a given night
        // so hovering any curve surfaces the full triple.
        private readonly Dictionary<Target, string[]> mTooltipTextByTarget
            = new Dictionary<Target, string[]>();

        // Per-target year-day cache snapshot stashed at Render so the
        // background visibility task can read it without going through the
        // cache store again.
        private readonly Dictionary<Target, IReadOnlyList<NightCacheEntry>> mYearDaysByTarget
            = new Dictionary<Target, IReadOnlyList<NightCacheEntry>>();

        // Cancellation for the in-flight visibility task. Replaced (cancelling
        // the prior) on every RefreshVisibility call.
        private CancellationTokenSource mVisibilityCts;

        private readonly HoverTooltipController mHover;

        private int mLastIdealHeight = -1;
        public event EventHandler IdealHeightChanged;

        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Sessions()
        {
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("MMM", CultureInfo.InvariantCulture),
                UnitWidth = TimeSpan.FromDays(30).TotalDays,
                MinStep = TimeSpan.FromDays(30).TotalDays,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
            };
            mYAxis = new Axis
            {
                Name = "Altitude at Minimum Duration (°)",
                MinLimit = MinAltitude,
                MaxLimit = MaxAltitude,
                MinStep = 10,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

            mNowLine = new RectangularSection
            {
                Xi = 0, Xj = 0,
                Stroke = new SolidColorPaint(SKColors.Red, 2),
            };
            mHorizonLine = new RectangularSection
            {
                Yi = 30, Yj = 30,
                Stroke = new SolidColorPaint(SKColors.Green, 2),
            };

            mChart = new CartesianChart
            {
                XAxes = new[] { mXAxis },
                YAxes = new[] { mYAxis },
                Sections = new[] { mNowLine, mHorizonLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Hidden,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Top,
                Height = ChartLayout.ChartFixedHeight,
            };

            mChart.DrawMargin = new LiveChartsCore.Measure.Margin(
                ChartLayout.LeftChromePx, ChartLayout.TopChromePx,
                ChartLayout.RightChromePx, ChartLayout.XAxisLabelHeightPx);

            mLegendPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = ChartLayout.ChartBackground,
                Padding = new Padding(
                    ChartLayout.LeftChromePx, ChartLayout.LegendTopPaddingPx,
                    ChartLayout.RightChromePx, ChartLayout.LegendBottomPaddingPx),
            };

            mContainer = new Panel
            {
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Fill,
            };
            mContainer.Controls.Add(mLegendPanel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-DataPoint snap tooltip: each night is a discrete data point,
            // 30 ms debounce, custom formatter reads unified per-target text
            // by segmentStart so hovering any of the three curves shows
            // Ceiling / Floor / Symmetric together.
            mHover = new HoverTooltipController(
                mChart,
                () => AllSeries(),
                curveTooltipFormatter: SessionsTooltipFormatter,
                debounceMs: 30);
        }

        // All three series, all targets, in legend / render order. Used by the
        // tooltip controller's "find all series" callback.
        private IEnumerable<LineSeries<ObservablePoint>> AllSeries()
        {
            foreach (var s in mCeilingByTarget.Values) yield return s;
            foreach (var s in mFloorByTarget.Values) yield return s;
            foreach (var s in mCenteredByTarget.Values) yield return s;
        }

        public void UpdateHorizonLine(double horizon)
        {
            mHorizonLine.Yi = horizon;
            mHorizonLine.Yj = horizon;
        }

        public void UpdateNowLine(DateTime now)
        {
            double oa = now.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
        }

        // Custom formatter: reverse-lookup the Target that owns this series so
        // we can index into the unified per-target tooltip array. The same
        // string is returned regardless of which of the three series is hit --
        // hovering any one shows Ceiling / Floor / Symmetric for the night.
        private string SessionsTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX, double hoverY,
            double interpY,
            int segmentStart)
        {
            Target target = TargetFor(series);
            if (target == null) return string.Empty;
            if (!mTooltipTextByTarget.TryGetValue(target, out var arr)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= arr.Length) return string.Empty;
            return arr[segmentStart] ?? string.Empty;
        }

        private Target TargetFor(LineSeries<ObservablePoint> series)
        {
            foreach (var kv in mCeilingByTarget)
                if (ReferenceEquals(kv.Value, series)) return kv.Key;
            foreach (var kv in mFloorByTarget)
                if (ReferenceEquals(kv.Value, series)) return kv.Key;
            foreach (var kv in mCenteredByTarget)
                if (ReferenceEquals(kv.Value, series)) return kv.Key;
            return null;
        }

        public void Render(
            IReadOnlyList<Target> targets,
            IChartCacheStore cache,
            MoonAvoidanceProfile profile,
            Location location,
            double horizon,
            TimeSpan duration,
            DateTime now,
            CancellationToken ct = default)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            ct.ThrowIfCancellationRequested();

            UpdateHorizonLine(horizon);
            UpdateNowLine(now);

            mTargetColors.Clear();
            mYearDaysByTarget.Clear();

            DateTime? gridStart = null;
            DateTime? gridEnd   = null;

            var newCeiling  = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var newFloor    = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var newCentered = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList  = new List<ISeries>();

            for (int t = 0; t < targets.Count; t++)
            {
                ct.ThrowIfCancellationRequested();
                Target target = targets[t];
                if (target == null) continue;

                TargetCacheEntry cacheEntry = cache?.GetOrNull(target);
                if (cacheEntry == null) continue;
                IReadOnlyList<NightCacheEntry> yearDays = cacheEntry.YearDays;
                if (yearDays == null || yearDays.Count == 0) continue;

                if (gridStart == null)
                {
                    gridStart = yearDays[0].SentinelX;
                    gridEnd   = yearDays[yearDays.Count - 1].SentinelX;
                }

                Color c = ChartLayout.TargetColorPalette[t % ChartLayout.TargetColorPalette.Length];
                mTargetColors[target] = c;
                mYearDaysByTarget[target] = yearDays;

                LineSeries<ObservablePoint> ceiling  = GetOrCreateSeries(mCeilingByTarget,  target, c, "-Sessions");
                LineSeries<ObservablePoint> floor    = GetOrCreateSeries(mFloorByTarget,    target, c, "-SessionsFloor");
                LineSeries<ObservablePoint> centered = GetOrCreateSeries(mCenteredByTarget, target, c, "-SessionsFloorCentered");

                // Initialize all three series to null Y across the year. The
                // background visibility task will fill in fitted nights.
                // Geometric early-out (polar / sub-horizon / duration<=0) is
                // handled by the bg task too, since it has the same predicate.
                InitializeNullSeriesData(ceiling,  yearDays);
                InitializeNullSeriesData(floor,    yearDays);
                InitializeNullSeriesData(centered, yearDays);

                // Reset the per-night tooltip array; bg task will overwrite.
                mTooltipTextByTarget[target] = new string[yearDays.Count];

                newCeiling[target]  = ceiling;
                newFloor[target]    = floor;
                newCentered[target] = centered;
                // Series order in mChart.Series: legend reads first match by
                // name, so adding ceiling first means the legend (if it ever
                // comes back from LC2's hidden mode) would pick the canonical
                // -Sessions series. The custom legend below does its own
                // ordering anyway.
                seriesList.Add(ceiling);
                seriesList.Add(floor);
                seriesList.Add(centered);
            }

            if (gridStart.HasValue && gridEnd.HasValue)
            {
                mXAxis.MinLimit = gridStart.Value.ToOADate();
                mXAxis.MaxLimit = gridEnd.Value.ToOADate();
            }

            // Drop tooltip arrays for targets dropped from the render list.
            var droppedTargets = mCeilingByTarget.Keys
                .Where(k => !newCeiling.ContainsKey(k))
                .ToList();
            foreach (var k in droppedTargets) mTooltipTextByTarget.Remove(k);

            mCeilingByTarget.Clear();
            mFloorByTarget.Clear();
            mCenteredByTarget.Clear();
            foreach (var kv in newCeiling)  mCeilingByTarget[kv.Key]  = kv.Value;
            foreach (var kv in newFloor)    mFloorByTarget[kv.Key]    = kv.Value;
            foreach (var kv in newCentered) mCenteredByTarget[kv.Key] = kv.Value;

            mChart.Series = seriesList;
            BuildLegendItems();

            // Kick off the per-night fit pass on a background thread. First
            // paint shows empty curves; the bg task fills in fitted nights.
            // For 44 targets × 365 nights × (PlaceBest + PlaceCentered) the
            // moon-aware case takes ~10-60 sec, which is why this is async.
            RefreshVisibility(cache, profile, location, horizon, duration);

            RecomputeLayout();
        }

        // Recompute per-night fit under the current Horizon / Duration / Moon
        // profile and apply line breaks / fitted altitudes on every series'
        // ObservablePoint.Y. Runs the BestSession.ResolveCandidates +
        // PlaceBest + PlaceCentered probe per (target, night) on a background
        // thread so the UI stays responsive during a scrub. Replaces any
        // in-flight task (cancellation token) so a rapid spinner scrub doesn't
        // queue stale work behind in-flight stale work.
        public void RefreshVisibility(
            IChartCacheStore cache,
            MoonAvoidanceProfile profile,
            Location location,
            double horizon,
            TimeSpan duration)
        {
            // cache is part of the IAltitudeSubChart contract for uniform
            // call sites; Sessions snapshots YearDays at Render via
            // mYearDaysByTarget and doesn't need to re-read it here.
            _ = cache;
            if (location == null || mCeilingByTarget.Count == 0) return;

            mVisibilityCts?.Cancel();
            mVisibilityCts = new CancellationTokenSource();
            CancellationToken ct = mVisibilityCts.Token;

            // Snapshot per-target work onto a stack-local list so the bg task
            // captures stable references even if the UI thread keeps mutating
            // mCeilingByTarget / mYearDaysByTarget for a subsequent Render.
            var snapshot = new List<(Target Target,
                                     LineSeries<ObservablePoint> Ceiling,
                                     LineSeries<ObservablePoint> Floor,
                                     LineSeries<ObservablePoint> Centered,
                                     IReadOnlyList<NightCacheEntry> Days)>();
            foreach (var kv in mCeilingByTarget)
            {
                if (!mFloorByTarget.TryGetValue(kv.Key, out var floor)) continue;
                if (!mCenteredByTarget.TryGetValue(kv.Key, out var centered)) continue;
                if (!mYearDaysByTarget.TryGetValue(kv.Key, out var days)) continue;
                snapshot.Add((kv.Key, kv.Value, floor, centered, days));
            }
            if (snapshot.Count == 0) return;

            IHorizonProfile horizonProfile = new ScalarHorizonProfile(horizon);
            TimeSpan dur = duration;
            MoonAvoidanceProfile profileCapture = profile;
            Location locationCapture = location;
            double horizonCapture = horizon;

            Task.Run(() =>
            {
                // Per-target buffers: three nullable-double arrays for the three
                // curves' Y values + a string[] for the unified tooltip per night.
                var perTarget = new List<(Target Target,
                                         LineSeries<ObservablePoint> Ceiling,
                                         LineSeries<ObservablePoint> Floor,
                                         LineSeries<ObservablePoint> Centered,
                                         IReadOnlyList<NightCacheEntry> Days,
                                         double?[] CeilY,
                                         double?[] FloorY,
                                         double?[] CenteredY,
                                         string[] Tooltips)>();
                foreach (var (target, ceil, fl, cen, days) in snapshot)
                {
                    if (ct.IsCancellationRequested) return;

                    int n = days.Count;
                    double?[] ceilY  = new double?[n];
                    double?[] floorY = new double?[n];
                    double?[] cenY   = new double?[n];
                    string[]  tips   = new string[n];

                    for (int i = 0; i < n; i++)
                    {
                        if (ct.IsCancellationRequested) return;
                        NightCacheEntry night = days[i];

                        if (night.IsPolar
                            || night.YearAlt < horizonCapture
                            || dur <= TimeSpan.Zero)
                        {
                            // Geometric pre-rejection: line break on all three.
                            tips[i] = FormatTooltip(target, night, null, null, null);
                            continue;
                        }

                        NightWindow nw = new NightWindow
                        {
                            AstronomicalDusk = night.Dusk,
                            AstronomicalDawn = night.Dawn,
                            LunarIlluminationFraction = 0,
                        };

                        var candidates = BestSession.ResolveCandidates(
                            target, locationCapture, nw, horizonProfile,
                            profileCapture);
                        if (candidates.Count == 0)
                        {
                            tips[i] = FormatTooltip(target, night, null, null, null);
                            continue;
                        }

                        // Ceiling / Floor: best transit-centered-or-wall-pushed
                        // placement; sin(alt) ranks candidates when multiple.
                        var session = BestSession.PlaceBest(
                            target, locationCapture, candidates,
                            dur, dur, SinAltQuality);
                        if (session != null)
                        {
                            floorY[i] = SessionAltitude.Floor(target, locationCapture,
                                session.Value.Start, session.Value.End);
                            ceilY[i]  = SessionAltitude.Ceiling(target, locationCapture,
                                session.Value.Start, session.Value.End);
                        }

                        // Symmetric: strict-centered placement; null when
                        // transit doesn't fit with positive room on both sides.
                        var centered = BestSession.PlaceCentered(
                            target, locationCapture, candidates, dur);
                        if (centered != null)
                        {
                            cenY[i] = SessionAltitude.Floor(target, locationCapture,
                                centered.Value.Start, centered.Value.End);
                        }

                        tips[i] = FormatTooltip(target, night, ceilY[i], floorY[i], cenY[i]);
                    }

                    perTarget.Add((target, ceil, fl, cen, days, ceilY, floorY, cenY, tips));
                }

                if (ct.IsCancellationRequested) return;

                if (mChart.IsHandleCreated)
                {
                    mChart.BeginInvoke(new Action(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        foreach (var (target, ceil, fl, cen, days, ceilY, floorY, cenY, tips)
                            in perTarget)
                        {
                            ApplyTargetFit(target, ceil, fl, cen, days, ceilY, floorY, cenY, tips);
                        }
                    }));
                }
            }, ct);
        }

        // UI-thread continuation: rewrite each series' ObservablePoint.Y in
        // place + the unified tooltip text. Mutates the existing
        // ObservableCollection so series identity (and the user's legend
        // toggle state) survives the refresh.
        private void ApplyTargetFit(
            Target target,
            LineSeries<ObservablePoint> ceiling,
            LineSeries<ObservablePoint> floor,
            LineSeries<ObservablePoint> centered,
            IReadOnlyList<NightCacheEntry> days,
            double?[] ceilY, double?[] floorY, double?[] cenY,
            string[] tooltips)
        {
            ObservableCollection<ObservablePoint> ceilData  = ceiling.Values  as ObservableCollection<ObservablePoint>;
            ObservableCollection<ObservablePoint> floorData = floor.Values    as ObservableCollection<ObservablePoint>;
            ObservableCollection<ObservablePoint> cenData   = centered.Values as ObservableCollection<ObservablePoint>;
            if (ceilData == null || floorData == null || cenData == null) return;

            int n = Math.Min(days.Count,
                    Math.Min(ceilData.Count,
                    Math.Min(floorData.Count,
                    Math.Min(cenData.Count,
                    Math.Min(ceilY.Length,
                    Math.Min(floorY.Length,
                    Math.Min(cenY.Length, tooltips.Length)))))));
            for (int i = 0; i < n; i++)
            {
                double oa = days[i].SentinelX.ToOADate();
                ceilData[i]  = new ObservablePoint(oa, ceilY[i]);
                floorData[i] = new ObservablePoint(oa, floorY[i]);
                cenData[i]   = new ObservablePoint(oa, cenY[i]);
            }

            mTooltipTextByTarget[target] = tooltips;
        }

        // Unified per-night tooltip text. Format mirrors the legacy
        // AssignSessionsTooltip output (CLAUDE.md): target name + date header,
        // then Ceiling / Floor / Symmetric lines.
        private static string FormatTooltip(
            Target target, NightCacheEntry night,
            double? ceilAlt, double? floorAlt, double? centeredAlt)
        {
            string ceilLine  = "Ceiling: "    + FormatAlt(ceilAlt);
            string floorLine = "Floor: "      + FormatAlt(floorAlt);
            string symLine   = "Symmetric: "  + FormatAlt(centeredAlt);
            string statusLine = ceilAlt.HasValue
                ? string.Empty
                : (night.IsPolar ? "(polar period)" : "(no fit at current Horizon / Duration / Moon)");

            if (string.IsNullOrEmpty(statusLine))
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} — {1:MMM dd, yyyy}\n{2}\n{3}\n{4}",
                    target.Name, night.SentinelX, ceilLine, floorLine, symLine);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} — {1:MMM dd, yyyy}\n{2}\n{3}\n{4}\n{5}",
                target.Name, night.SentinelX, statusLine, ceilLine, floorLine, symLine);
        }

        private static string FormatAlt(double? alt)
            => alt.HasValue
                ? alt.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°"
                : "—";

        // Build the external legend FlowLayoutPanel: ONE item per target.
        // Click toggles all three of that target's series IsVisible together,
        // mirroring the legacy MS Charts behavior where ShowChartAreaSeries
        // collapsed the three sub-series under a single {TargetName} legend.
        private void BuildLegendItems()
        {
            mLegendPanel.SuspendLayout();
            mLegendPanel.Controls.Clear();
            foreach (var kv in mCeilingByTarget)
            {
                Target target = kv.Key;
                Color color = mTargetColors.TryGetValue(target, out var c) ? c : Color.LightGray;
                LineSeries<ObservablePoint> ceiling  = kv.Value;
                LineSeries<ObservablePoint> floor    = mFloorByTarget.TryGetValue(target, out var f) ? f : null;
                LineSeries<ObservablePoint> centered = mCenteredByTarget.TryGetValue(target, out var ce) ? ce : null;
                mLegendPanel.Controls.Add(MakeLegendItem(target, color, ceiling, floor, centered));
            }
            mLegendPanel.ResumeLayout(performLayout: true);
        }

        private Control MakeLegendItem(
            Target target, Color color,
            LineSeries<ObservablePoint> ceiling,
            LineSeries<ObservablePoint> floor,
            LineSeries<ObservablePoint> centered)
        {
            const int markerWidth = 18;
            const int markerHeight = 4;
            const int markerLabelGap = 6;

            // Initial label state matches the ceiling series' visibility -- the
            // three companions share a single toggle so the ceiling's IsVisible
            // is the canonical "shown" indicator.
            bool initialVisible = ceiling.IsVisible;
            var label = new Label
            {
                AutoSize = true,
                ForeColor = initialVisible ? Color.LightGray : Color.DimGray,
                BackColor = ChartLayout.ChartBackground,
                Padding = new Padding(markerWidth + markerLabelGap, 2, 12, 2),
                Margin = new Padding(0, 0, 4, 2),
                Text = target.Name,
                Cursor = Cursors.Hand,
            };
            label.Paint += (s, e) =>
            {
                int y = (label.Height - markerHeight) / 2;
                using (var brush = new SolidBrush(color))
                {
                    e.Graphics.FillRectangle(brush, 0, y, markerWidth, markerHeight);
                }
            };
            label.Click += (s, e) =>
            {
                bool nextVisible = !ceiling.IsVisible;
                ceiling.IsVisible = nextVisible;
                if (floor != null)    floor.IsVisible    = nextVisible;
                if (centered != null) centered.IsVisible = nextVisible;
                label.ForeColor = nextVisible ? Color.LightGray : Color.DimGray;
                // Reassigning Series forces LC2 to re-iterate and re-evaluate
                // IsVisible on every series. Plain Invalidate() would repaint
                // the cached layout but skip the visibility re-check.
                mChart.Series = mChart.Series.ToList();
                mChart.Invalidate();
            };
            return label;
        }

        // Cheap path for Sort changes -- rebuild the three dicts' iteration
        // order + mChart.Series + legend without recomputing data and
        // without restarting the background fit task. The cached fit results
        // (already painted as Y values on each series) stay valid because
        // the target SET is unchanged.
        public void Reorder(IReadOnlyList<Target> newOrder)
        {
            if (newOrder == null || mCeilingByTarget.Count == 0) return;
            var reCeil  = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var reFloor = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var reCen   = new Dictionary<Target, LineSeries<ObservablePoint>>();
            foreach (var target in newOrder)
            {
                if (target == null) continue;
                if (mCeilingByTarget.TryGetValue(target, out var c))   reCeil[target]  = c;
                if (mFloorByTarget.TryGetValue(target, out var f))     reFloor[target] = f;
                if (mCenteredByTarget.TryGetValue(target, out var ce)) reCen[target]   = ce;
            }
            mCeilingByTarget.Clear();
            mFloorByTarget.Clear();
            mCenteredByTarget.Clear();
            foreach (var kv in reCeil)  mCeilingByTarget[kv.Key]  = kv.Value;
            foreach (var kv in reFloor) mFloorByTarget[kv.Key]    = kv.Value;
            foreach (var kv in reCen)   mCenteredByTarget[kv.Key] = kv.Value;

            var seriesList = new List<ISeries>();
            foreach (var target in newOrder)
            {
                if (target == null) continue;
                if (mCeilingByTarget.TryGetValue(target, out var c))   seriesList.Add(c);
                if (mFloorByTarget.TryGetValue(target, out var f))     seriesList.Add(f);
                if (mCenteredByTarget.TryGetValue(target, out var ce)) seriesList.Add(ce);
            }
            mChart.Series = seriesList;
            BuildLegendItems();
        }

        private LineSeries<ObservablePoint> GetOrCreateSeries(
            Dictionary<Target, LineSeries<ObservablePoint>> dict,
            Target target, Color c, string nameSuffix)
        {
            if (dict.TryGetValue(target, out var existing)) return existing;
            return new LineSeries<ObservablePoint>
            {
                // Suffixed name preserved from legacy ("-Sessions" /
                // "-SessionsFloor" / "-SessionsFloorCentered"). LC2 doesn't
                // route by name for visibility, but a couple of legacy
                // helpers (FindReferenceSeries, ShowChartAreaSeries) used to
                // -- the suffix stays for parity until PR4e cleanup.
                Name = target.Name + nameSuffix,
                Values = new ObservableCollection<ObservablePoint>(),
                Stroke = new SolidColorPaint(new SKColor(c.R, c.G, c.B, c.A), 2),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
            };
        }

        // Initialize a series' Values to all-null-Y points indexed by yearDays.
        // The bg fit task will overwrite specific indices; nights that stay
        // null Y render as line breaks (visible "no fit / not yet computed").
        private static void InitializeNullSeriesData(
            LineSeries<ObservablePoint> series,
            IReadOnlyList<NightCacheEntry> yearDays)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }
            int n = yearDays.Count;
            for (int i = 0; i < n; i++)
            {
                var p = new ObservablePoint(yearDays[i].SentinelX.ToOADate(), null);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > n) data.RemoveAt(data.Count - 1);
        }

        private void RecomputeLayout()
        {
            int idealHeight = IdealHeight;
            if (idealHeight != mLastIdealHeight)
            {
                mLastIdealHeight = idealHeight;
                IdealHeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            mVisibilityCts?.Cancel();
            mVisibilityCts?.Dispose();
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
