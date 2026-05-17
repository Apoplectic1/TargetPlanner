using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Astronomy.Core.Night;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinForms;
using SkiaSharp;
using TargetPlanner.Caches;
using TargetPlanner.State;

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
    // **Render-only.** Sub-chart paints synchronously from
    // <see cref="IChartCacheStore.GetFitOrNull"/>; the heavy
    // <c>BestSession.ResolveCandidates</c> + <c>PlaceBest</c> + <c>PlaceCentered</c>
    // + <c>SessionAltitude.{Floor, Ceiling}</c> walk lives in
    // <see cref="ChartCacheStore.BuildFitEntryAsync"/> keyed on (Target,
    // <see cref="HdmKey"/>). The coordinator awaits
    // <see cref="IChartCacheStore.PrepareFitsAsync"/> before dispatching Render
    // so all three fields of <see cref="NightFit"/> are guaranteed populated
    // for every (target, night) we render.
    //
    // Owns one controller wired to its CartesianChart instance:
    //   - HoverTooltipController: per-DataPoint snap tooltip (30 ms debounce);
    //     custom formatter assembles the unified Ceiling / Floor / Symmetric
    //     triple text on hover from the cached NightFit. Same tooltip string
    //     applies regardless of which of the three curves the user is hovering.
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

        // Per-target yearDays snapshot stashed at Render so the tooltip
        // formatter can look up SentinelX + IsPolar at hover time.
        private readonly Dictionary<Target, IReadOnlyList<NightCacheEntry>> mYearDaysByTarget
            = new Dictionary<Target, IReadOnlyList<NightCacheEntry>>();

        // Snapshot of the full ChartContext at Render so the tooltip formatter
        // can read per-night NightFit (via cache.GetFitOrNull(target, ctx.Hdm))
        // plus any future per-target solver results without growing more
        // mLastFoo fields per input source.
        private ChartContext mLastCtx;
        private IChartCacheStore mLastCache;

        // Reverse lookup: series → target, spanning all three of a target's series
        // (Ceiling / Floor / Centered). Populated at Render time so the tooltip
        // hit-test resolves in O(1) instead of three sequential O(N) foreach
        // scans over the three per-target dicts on every mouse motion.
        private readonly Dictionary<LineSeries<ObservablePoint>, Target> mTargetBySeries
            = new Dictionary<LineSeries<ObservablePoint>, Target>();

        private readonly HoverTooltipController mHover;

        private int mLastIdealHeight = -1;
        public event EventHandler IdealHeightChanged;

        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Sessions()
        {
            // Tick positions are driven by Axis.CustomSeparators (set in Render once
            // the year-grid start is known) so labels sit on real month boundaries
            // and the 12 ticks span exactly 12 calendar months. UnitWidth = 1 day
            // matches the per-night data spacing.
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("MMM", CultureInfo.InvariantCulture),
                UnitWidth = TimeSpan.FromDays(1).TotalDays,
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
                AnimationsSpeed = TimeSpan.Zero,
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
            // 30 ms debounce, custom formatter assembles the unified
            // Ceiling / Floor / Symmetric text on hover. Hovering any of the
            // three curves surfaces the same triple.
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
        // we can fetch its cached NightFit. The same string is returned regardless
        // of which of the three series is hit -- hovering any one shows Ceiling /
        // Floor / Symmetric for the night.
        private string SessionsTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX, double hoverY,
            double interpY,
            int segmentStart)
        {
            Target target = TargetFor(series);
            if (target == null) return string.Empty;
            if (!mYearDaysByTarget.TryGetValue(target, out var days)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= days.Count) return string.Empty;

            NightCacheEntry night = days[segmentStart];
            HdmKey hdm = mLastCtx?.Hdm ?? default;
            TargetFitEntry fitEntry = mLastCache?.GetFitOrNull(target, hdm);
            NightFit fit = fitEntry != null && segmentStart < fitEntry.Nights.Count
                ? fitEntry.Nights[segmentStart]
                : default;

            return FormatTooltip(target, night, fit);
        }

        private Target TargetFor(LineSeries<ObservablePoint> series)
        {
            if (series == null) return null;
            if (mTargetBySeries.TryGetValue(series, out Target target)) return target;
            return null;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache, ChartEvaluation eval)
        {
            _ = eval; // Phase 4: accept but ignore; Phase 7 will wire short-circuit.
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Location == null) throw new ArgumentException("ctx.Location must not be null", nameof(ctx));
            if (ctx.Policy == null) throw new ArgumentException("ctx.Policy must not be null", nameof(ctx));

            Location location = ctx.Location;
            IReadOnlyList<Target> targets = ctx.Targets;
            // Green horizon line follows the scalar TargetFloor spinner; LocalHorizon's
            // polyline drives per-azimuth fit decisions in the cache, not the chart line.
            double horizonAlt = ctx.Policy.TargetFloorDeg;
            DateTime now = location.DateTime;
            HdmKey hdm = ctx.Hdm;

            UpdateHorizonLine(horizonAlt);
            UpdateNowLine(now);

            mLastCtx = ctx;
            mLastCache = cache;

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
                Target target = targets[t];
                if (target == null) continue;

                TargetCacheEntry yearEntry = cache?.GetOrNull(target);
                TargetFitEntry fitEntry = cache?.GetFitOrNull(target, hdm);
                if (yearEntry == null || fitEntry == null) continue;
                IReadOnlyList<NightCacheEntry> yearDays = yearEntry.YearDays;
                IReadOnlyList<NightFit> fits = fitEntry.Nights;
                if (yearDays == null || yearDays.Count == 0) continue;

                if (gridStart == null)
                {
                    gridStart = yearDays[0].SentinelX;
                    gridEnd   = yearDays[yearDays.Count - 1].SentinelX;
                }

                Color c = ChartLayout.ResolveTargetColor(ctx.TargetColors, target, t);
                mTargetColors[target] = c;
                mYearDaysByTarget[target] = yearDays;

                LineSeries<ObservablePoint> ceiling  = GetOrCreateSeries(mCeilingByTarget,  target, c, "-Sessions");
                LineSeries<ObservablePoint> floor    = GetOrCreateSeries(mFloorByTarget,    target, c, "-SessionsFloor");
                LineSeries<ObservablePoint> centered = GetOrCreateSeries(mCenteredByTarget, target, c, "-SessionsFloorCentered");

                ApplyFitsToSeries(ceiling,  yearDays, fits, NightFitField.Ceiling);
                ApplyFitsToSeries(floor,    yearDays, fits, NightFitField.Floor);
                ApplyFitsToSeries(centered, yearDays, fits, NightFitField.CenteredFloor);

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
                // Snap chart bounds to the start-of-month midnights so columns
                // align with the CustomSeparators ticks. gridStart's SentinelX is
                // mid-day on the first cached night; back it up to midnight for
                // the visible left edge. Right edge = first-of-(start+12 months)
                // so 12 full month columns fit exactly between the 13 ticks.
                DateTime startMonth = gridStart.Value.Date.AddDays(1 - gridStart.Value.Day);
                DateTime endMonth = startMonth.AddYears(1);
                mXAxis.MinLimit = startMonth.ToOADate();
                mXAxis.MaxLimit = endMonth.ToOADate();
                mXAxis.CustomSeparators = ChartLayout.MonthBoundaryOADates(startMonth, 12);
            }

            mCeilingByTarget.Clear();
            mFloorByTarget.Clear();
            mCenteredByTarget.Clear();
            mTargetBySeries.Clear();
            foreach (var kv in newCeiling)
            {
                mCeilingByTarget[kv.Key]  = kv.Value;
                mTargetBySeries[kv.Value] = kv.Key;
            }
            foreach (var kv in newFloor)
            {
                mFloorByTarget[kv.Key]    = kv.Value;
                mTargetBySeries[kv.Value] = kv.Key;
            }
            foreach (var kv in newCentered)
            {
                mCenteredByTarget[kv.Key] = kv.Value;
                mTargetBySeries[kv.Value] = kv.Key;
            }

            mChart.Series = seriesList;
            BuildLegendItems();

            RecomputeLayout();
        }

        // H/D/M-aware "refresh". Synchronous re-render from the cache; coordinator's
        // PrepareFitsAsync await guarantees the new HdmKey's fits are built before
        // this fires. Contract shape stays uniform with Day/Sky.
        public void RefreshVisibility(ChartContext ctx, IChartCacheStore cache)
        {
            if (ctx == null || ctx.Location == null || mCeilingByTarget.Count == 0) return;
            // Sessions ignores eval; pass FullChange so the Render path proceeds
            // unchanged. RefreshVisibility itself is going away in Phase 6.
            Render(ctx, cache, ChartEvaluation.FullChange(default, ctx.Hdm, ctx.DayMode));
        }

        private enum NightFitField { Ceiling, Floor, CenteredFloor }

        private static double? GetField(in NightFit fit, NightFitField field) => field switch
        {
            NightFitField.Ceiling => fit.Ceiling,
            NightFitField.Floor => fit.Floor,
            NightFitField.CenteredFloor => fit.CenteredFloor,
            _ => null,
        };

        private static void ApplyFitsToSeries(
            LineSeries<ObservablePoint> series,
            IReadOnlyList<NightCacheEntry> yearDays,
            IReadOnlyList<NightFit> fits,
            NightFitField field)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }
            int n = Math.Min(yearDays.Count, fits.Count);
            for (int i = 0; i < n; i++)
            {
                double oa = yearDays[i].SentinelX.ToOADate();
                var p = new ObservablePoint(oa, GetField(fits[i], field));
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > n) data.RemoveAt(data.Count - 1);
        }

        // Unified per-night tooltip text. Format mirrors the legacy
        // AssignSessionsTooltip output (CLAUDE.md): target name + date header,
        // then Ceiling / Floor / Symmetric lines.
        private static string FormatTooltip(
            Target target, NightCacheEntry night, NightFit fit)
        {
            string ceilLine  = "Ceiling: "    + FormatAlt(fit.Ceiling);
            string floorLine = "Floor: "      + FormatAlt(fit.Floor);
            string symLine   = "Symmetric: "  + FormatAlt(fit.CenteredFloor);
            string statusLine = fit.Ceiling.HasValue
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
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
