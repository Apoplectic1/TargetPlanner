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
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    // LiveCharts2 implementation of TP's Year chart area. 12-month per-night
    // session-floor sweep -- one DataPoint per night plotting the worst-case
    // altitude of the best D-hour session that fits under the current Horizon
    // / Duration / Moon profile. Null Y for nights where no D-hour window
    // fits.
    //
    // **Render-only.** Sub-chart paints synchronously from
    // <see cref="IChartCacheStore.GetFitOrNull"/>; the heavy
    // <c>BestSession.ResolveCandidates</c> + <c>PlaceBest</c> +
    // <c>SessionAltitude.Floor</c> walk lives in <see cref="ChartCacheStore.BuildFitEntryAsync"/>
    // keyed on (Target, <see cref="HdmKey"/>). The coordinator awaits
    // <see cref="IChartCacheStore.PrepareFitsAsync"/> before dispatching Render
    // so the fits-by-HdmKey are guaranteed present (modulo a build that raced
    // a location swap -- GetFitOrNull returns null in that case and the target
    // is skipped).
    //
    // Owns one controller wired to its CartesianChart instance:
    //   - HoverTooltipController: per-DataPoint snap tooltip (30 ms debounce);
    //     custom formatter reads cached NightFit + yearDays at hover time so
    //     the user sees the actual session floor altitude / date pair.
    //
    // No OverlayController, no Moon series, no dusk/dawn gradient.
    public class AltitudeSubChart_Year : IAltitudeSubChart
    {
        // Y axis bounds (altitude, degrees). 0-90 to match Day so the plot area
        // template stays uniform across radio swaps.
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

        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mSeriesByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // Per-target yearDays snapshot stashed at Render so the tooltip
        // formatter can look up SentinelX + IsPolar at hover time without
        // round-tripping through the cache (which would need the current
        // location's identity). IReadOnlyList<NightCacheEntry> is published
        // immutable per the cache store's contract.
        private readonly Dictionary<Target, IReadOnlyList<NightCacheEntry>> mYearDaysByTarget
            = new Dictionary<Target, IReadOnlyList<NightCacheEntry>>();

        // Snapshot of the full ChartContext at Render so the tooltip formatter
        // can read per-night NightFit (via cache.GetFitOrNull(target, ctx.Hdm))
        // plus any future per-target solver results without growing more
        // mLastFoo fields per input source.
        private ChartContext mLastCtx;
        private IChartCacheStore mLastCache;

        // Reverse lookup: series → target. Populated alongside mSeriesByTarget at
        // Render time so the tooltip hit-test resolves the hovered series in O(1)
        // instead of an O(N) scan over mSeriesByTarget on every mouse motion.
        private readonly Dictionary<LineSeries<ObservablePoint>, Target> mTargetBySeries
            = new Dictionary<LineSeries<ObservablePoint>, Target>();

        private readonly HoverTooltipController mHover;

        private int mLastIdealHeight = -1;
        public event EventHandler IdealHeightChanged;

        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Year()
        {
            // Tick positions are driven by Axis.CustomSeparators (set in Render once
            // the year-grid start is known) so labels sit on real month boundaries
            // and the 12 ticks span exactly 12 calendar months. UnitWidth = 1 day
            // matches the per-night data spacing.
            mXAxis = ChartLayout.MakeMonthXAxis();
            mYAxis = ChartLayout.MakeAltitudeYAxis("Session floor altitude (°)");

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
            // 30 ms debounce, custom formatter formats per-night text on hover
            // from the cached NightFit + yearDays.
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                curveTooltipFormatter: YearTooltipFormatter,
                debounceMs: 30);
        }

        // Update the green horizon line in place. Cheap; called from horizon
        // spinner ticks on the MainForm.
        public void UpdateHorizonLine(double horizon)
        {
            mHorizonLine.Yi = horizon;
            mHorizonLine.Yj = horizon;
        }

        // Update the red now-line position in place. The X axis is UTC-internal
        // -- per-night points plot at SentinelX (a UTC instant) ToOADate -- so
        // the now instant (already UTC) plots as its own OADate directly.
        public void UpdateNowLine(DateTime nowUtc)
        {
            double oa = nowUtc.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
        }

        private string YearTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX,
            double interpY,
            int segmentStart)
        {
            Target target = TargetForSeries(series);
            if (target == null) return string.Empty;
            if (!mYearDaysByTarget.TryGetValue(target, out var days)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= days.Count) return string.Empty;

            NightCacheEntry night = days[segmentStart];
            HdmKey hdm = mLastCtx?.Hdm ?? default;
            TargetFitEntry fitEntry = mLastCache?.GetFitOrNull(target, hdm);
            NightFit fit = fitEntry != null && segmentStart < fitEntry.Nights.Count
                ? fitEntry.Nights[segmentStart]
                : default;

            if (fit.Floor.HasValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}\n{1:MMM dd, yyyy}\nFloor: {2:0.0}°",
                    target.Name, night.SentinelX, fit.Floor.Value);
            }
            if (night.IsPolar)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}\n{1:MMM dd, yyyy}\n(polar period)",
                    target.Name, night.SentinelX);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0}\n{1:MMM dd, yyyy}\n(no fit at current Horizon / Duration / Moon)",
                target.Name, night.SentinelX);
        }

        private Target TargetForSeries(LineSeries<ObservablePoint> series)
        {
            if (series == null) return null;
            mTargetBySeries.TryGetValue(series, out Target target);
            return target;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Location == null) throw new ArgumentException("ctx.Location must not be null", nameof(ctx));
            if (ctx.Policy == null) throw new ArgumentException("ctx.Policy must not be null", nameof(ctx));
            // Phase 7's short-circuit-on-eval-flags was reverted; see
            // AltitudeSubChart_Day.Render for rationale.

            Location location = ctx.Location;
            IReadOnlyList<Target> targets = ctx.Targets;
            // Green horizon line follows the scalar TargetFloor spinner; LocalHorizon's
            // polyline drives per-azimuth fit decisions in the cache, not the chart line.
            double horizonAlt = ctx.Policy.TargetFloorDeg;
            DateTime now = ctx.Observation.Utc;
            HdmKey hdm = ctx.Hdm;

            UpdateHorizonLine(horizonAlt);
            UpdateNowLine(now);

            mLastCtx = ctx;
            mLastCache = cache;

            mTargetColors.Clear();
            mYearDaysByTarget.Clear();

            // X axis bounds locked to the first / last SentinelX of any target's
            // year cache. All targets share the same year-day grid (cache is
            // keyed by Location, not by Target), so the first non-empty cache
            // entry is sufficient. If no targets have a cache yet, leave bounds
            // unset and LC2 auto-fits to the data.
            DateTime? gridStart = null;
            DateTime? gridEnd   = null;

            var newSeriesByTarget = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList = new List<ISeries>();
            for (int t = 0; t < targets.Count; t++)
            {
                Target target = targets[t];
                if (target == null) continue;

                TargetCacheEntry yearEntry = cache?.GetOrNull(target);
                TargetFitEntry fitEntry = cache?.GetFitOrNull(target, hdm);
                if (yearEntry == null || fitEntry == null) continue;   // raced; skip
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

                var series = GetOrCreateTargetSeries(target, c);
                ApplyFitsToSeries(series, yearDays, fits);

                newSeriesByTarget[target] = series;
                seriesList.Add(series);
            }

            if (gridStart.HasValue && gridEnd.HasValue)
            {
                // Snap chart bounds to the start-of-month midnights so columns
                // align with the CustomSeparators ticks. gridStart's SentinelX is
                // mid-day on the first cached night (May 1 12:00 if start month is
                // May); back it up to midnight for the visible left edge. Right
                // edge = first-of-(start+12 months) so 12 full month columns fit
                // exactly between the 13 ticks.
                DateTime startMonth = gridStart.Value.Date.AddDays(1 - gridStart.Value.Day);
                DateTime endMonth = startMonth.AddYears(1);
                mXAxis.MinLimit = startMonth.ToOADate();
                mXAxis.MaxLimit = endMonth.ToOADate();
                mXAxis.CustomSeparators = ChartLayout.MonthBoundaryOADates(startMonth, 12);
            }

            mSeriesByTarget.Clear();
            mTargetBySeries.Clear();
            foreach (var kv in newSeriesByTarget)
            {
                mSeriesByTarget[kv.Key] = kv.Value;
                mTargetBySeries[kv.Value] = kv.Key;
            }
            mChart.Series = seriesList;
            BuildLegendItems();

            RecomputeLayout();
        }

        // Apply per-night Floor altitudes from the cached NightFit array to the
        // series' ObservablePoint Y values. Mutates the existing ObservableCollection
        // in place so series identity -- and the user's legend toggle state --
        // survives the refresh.
        private static void ApplyFitsToSeries(
            LineSeries<ObservablePoint> series,
            IReadOnlyList<NightCacheEntry> yearDays,
            IReadOnlyList<NightFit> fits)
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
                var p = new ObservablePoint(oa, fits[i].Floor);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > n) data.RemoveAt(data.Count - 1);
        }

        private LineSeries<ObservablePoint> GetOrCreateTargetSeries(Target target, Color c)
        {
            if (mSeriesByTarget.TryGetValue(target, out var existing)) return existing;
            return new LineSeries<ObservablePoint>
            {
                Name = target.Name,
                Values = new ObservableCollection<ObservablePoint>(),
                Stroke = new SolidColorPaint(new SKColor(c.R, c.G, c.B, c.A), 2),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
            };
        }

        private void BuildLegendItems()
        {
            mLegendPanel.SuspendLayout();
            mLegendPanel.Controls.Clear();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                Color color = mTargetColors.TryGetValue(target, out var c) ? c : Color.LightGray;
                mLegendPanel.Controls.Add(MakeLegendItem(series, target, color));
            }
            mLegendPanel.ResumeLayout(performLayout: true);
        }

        private Control MakeLegendItem(
            LineSeries<ObservablePoint> series, Target target, Color color)
        {
            const int markerWidth = 18;
            const int markerHeight = 4;
            const int markerLabelGap = 6;

            var label = new Label
            {
                AutoSize = true,
                ForeColor = series.IsVisible ? Color.LightGray : Color.DimGray,
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
                series.IsVisible = !series.IsVisible;
                label.ForeColor = series.IsVisible ? Color.LightGray : Color.DimGray;
                mChart.Series = mChart.Series.ToList();
                mChart.Invalidate();
            };
            return label;
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
