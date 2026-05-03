using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Astronomy.Core.Astrometry;
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
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;
using LvcPointD = LiveChartsCore.Drawing.LvcPointD;

namespace TargetPlanner.Charts
{
    // LiveCharts2 implementation of TP's Day chart area. Stateless renderer:
    // every Render(...) call refreshes the chart from the supplied inputs,
    // preserving Series identity across calls (ObservableCollection mutation
    // triggers reactive redraw without rebuilding the series object).
    //
    // Owns three controllers wired to its CartesianChart instance:
    //   - OverlayController: HD click-to-toggle best-window step rectangle.
    //   - HoverTooltipController: smooth-curve interpolated tooltip
    //     (300 ms debounce — Day altitude is continuous).
    //   - LegendClickHandler: manual legend toggle (LC2 v2.1.0-dev-365's
    //     bottom legend doesn't auto-toggle on click).
    //
    // Phase 4 incremental migration: this class replaces the legacy MS Charts
    // Day chart area. PR4a hosts both side-by-side; the radio handler routes
    // to whichever owns the selected area.
    public class AltitudeSubChart_Day : IDisposable
    {
        // Y axis bounds for Day (altitude, degrees). MaxAltitude stays at 90 so
        // hover tests can use the same [0, 90] plot-area gate as the prototype.
        public const double MinAltitude = 0.0;
        public const double MaxAltitude = 90.0;

        // Plot-area template. The Day chart's plot area is locked to these
        // dimensions; Sky / Year / Sessions sub-charts (PR4b..d) inherit the same
        // values so toggling radios doesn't shift the plot's pixel position.
        // Chart total height grows as the legend wraps to additional rows; Panel +
        // GroupBox + Form follow the chart's IdealHeight (raised via event).
        public const int FixedPlotAreaHeight = 420;
        // Left chrome holds the rotated Y-axis Name + tick labels + breathing room.
        // Bottom chrome holds the X-axis tick labels only (no legend — that lives in
        // a sibling FlowLayoutPanel below the chart, not inside the chart's surface).
        private const int LeftChromePx = 96;          // Y-axis: Name (rotated) + ticks + pad
        private const int RightChromePx = 24;         // right padding
        private const int TopChromePx = 20;           // padding above plot
        private const int XAxisLabelHeightPx = 44;    // X-axis tick labels + pad

        // Total chart height that keeps the plot area at FixedPlotAreaHeight, with
        // axis chrome top and bottom. Constant — the legend lives outside the chart
        // so chart total height never changes.
        private const int ChartFixedHeight =
            TopChromePx + FixedPlotAreaHeight + XAxisLabelHeightPx;

        // Legend (external, below chart in a FlowLayoutPanel) styling.
        private const int LegendRowHeightPx = 22;     // single-line legend item height
        private const int LegendTopPaddingPx = 6;
        private const int LegendBottomPaddingPx = 6;

        // Same palette as legacy AltitudeChart so per-target colors stay stable
        // across the migration. Phase 4e drops the legacy file; this becomes the
        // single source.
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

        // Yellow gradient endpoints for dusk/dawn sections (matches MS Charts side).
        private static readonly SKColor YellowOpaque = new SKColor(255, 238, 88, 145);
        private static readonly SKColor YellowFaded  = new SKColor(255, 238, 88,   0);

        // Light grid lines that read against the dark grey (70,70,70) chart background
        // without competing with the per-target curves.
        private static readonly SKColor GridLineColor = new SKColor(180, 180, 180, 90);

        // Quality metric for BestSession.For — sin(altitude) is the standard
        // airmass-weighted proxy. Same as AltitudeSeries.SinAltQuality.
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        // The Container hosts (top) the CartesianChart at fixed height + (bottom) a
        // FlowLayoutPanel hosting custom legend items that wrap as targets grow.
        // MainForm adds Container to Panel_AltitudeChart and resizes Panel +
        // GroupBox + Form to match Container's IdealHeight on legend changes.
        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly FlowLayoutPanel mLegendPanel;

        // Chart-furniture state preserved across Render calls. Sections /
        // Axes objects are mutated in place; only Series can be re-listed.
        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly RectangularSection mDuskSection;
        private readonly RectangularSection mDawnSection;
        private readonly RectangularSection mNowLine;
        private readonly RectangularSection mHorizonLine;

        // Per-target series keyed by Target. Series identity preserved across
        // renders; ObservableCollection mutation drives the reactive redraw.
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mSeriesByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        // Per-target stable color from the palette. Stashed during Render so the
        // hide-on-no-fit refresh path can restore the original color when a curve
        // becomes visible again. Mirrors AltitudeChart.mTargetColors.
        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // Per-target best D-hour window for the HD overlay click handler.
        // Keyed by LineSeries (not Target) because OverlayController operates on
        // the LineSeries it found via hit-test.
        private readonly Dictionary<LineSeries<ObservablePoint>, (double startOA, double endOA, double floor)>
            mTargetWindows = new Dictionary<LineSeries<ObservablePoint>, (double, double, double)>();

        private LineSeries<ObservablePoint> mMoonSeries;

        private readonly OverlayController mOverlay;
        private readonly HoverTooltipController mHover;
        private readonly LegendClickHandler mLegend;

        // Cached IdealHeight from the last layout pass; used to detect changes so
        // the IdealHeightChanged event only fires when the form actually needs to
        // resize.
        private int mLastIdealHeight = -1;

        // Raised when the chart's IdealHeight changes (legend wrap count moved).
        // MainForm subscribes and resizes Panel_AltitudeChart + GroupBox_Altitude +
        // Form by the delta so the plot area stays in a fixed pixel position
        // regardless of target count.
        public event EventHandler IdealHeightChanged;

        // Total Container height = fixed chart height + the FlowLayoutPanel's
        // preferred height for its current legend items. Legend panel grows in
        // height as targets are added (FlowLayoutPanel auto-wraps).
        // With FlowLayoutPanel.Dock=Top + AutoSize=true, the panel's Height
        // auto-tracks its content after each layout pass. Container.IdealHeight
        // is just chart fixed height + that current Height.
        public int IdealHeight => ChartFixedHeight + mLegendPanel.Height;

        public AltitudeSubChart_Day()
        {
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("h:mm tt"),
                UnitWidth = TimeSpan.FromHours(1).TotalDays,
                MinStep = TimeSpan.FromHours(1).TotalDays,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(GridLineColor),
            };
            mYAxis = new Axis
            {
                Name = "Altitude (°)",
                MinLimit = MinAltitude,
                MaxLimit = MaxAltitude,
                MinStep = 10,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

            // Initialize section objects with placeholder bounds; Render() rewrites
            // Xi/Xj/Yi/Yj per the actual night window.
            mDuskSection = new RectangularSection { Xi = 0, Xj = 0 };
            mDawnSection = new RectangularSection { Xi = 0, Xj = 0 };
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
                Sections = new[] { mDuskSection, mDawnSection, mNowLine, mHorizonLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Hidden,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                BackColor = Color.FromArgb(70, 70, 70),
                Dock = DockStyle.Top,
                Height = ChartFixedHeight,
            };

            // Lock the plot area to a fixed pixel rectangle. Bottom margin is just
            // X-axis label space — the legend lives outside the chart in a sibling
            // FlowLayoutPanel, so chart height is constant and X-axis labels sit at
            // a fixed pixel position relative to the plot area.
            mChart.DrawMargin = new LiveChartsCore.Measure.Margin(
                LeftChromePx, TopChromePx, RightChromePx, XAxisLabelHeightPx);

            mLegendPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(70, 70, 70),
                Padding = new Padding(LeftChromePx, LegendTopPaddingPx, RightChromePx, LegendBottomPaddingPx),
            };

            mContainer = new Panel
            {
                BackColor = Color.FromArgb(70, 70, 70),
                Dock = DockStyle.Fill,
            };
            // Order matters for Dock=Top stacking: the LAST control added docks
            // FIRST. Add legend first (lower z-order), then chart (higher z-order)
            // so chart claims the top region and legend docks below it.
            mContainer.Controls.Add(mLegendPanel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-instance controllers wired to mChart (the CartesianChart inside
            // the container). Hover uses interpolated mode (300 ms) because Day
            // altitude curves are smooth.
            mOverlay = new OverlayController(
                mChart,
                () => mSeriesByTarget.Values,
                series => mTargetWindows.TryGetValue(series, out var w)
                    ? ((double, double, double)?)w
                    : null,
                _ => { });
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                legendTooltipFormatter: null,
                curveTooltipFormatter: null,
                debounceMs: 300);
            mLegend = new LegendClickHandler(mChart, _ => { });

            mChart.MouseDown += OnChartMouseDown;
            mChart.SizeChanged += OnChartSizeChanged;
        }

        // Update the green horizon line in place. Cheap; called from spinner ticks.
        public void UpdateHorizonLine(double horizon)
        {
            mHorizonLine.Yi = horizon;
            mHorizonLine.Yj = horizon;
        }

        // Update the red now-line position in place.
        public void UpdateNowLine(DateTime now)
        {
            double oa = now.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
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

            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location);
            if (!night.IsValid)
            {
                ClearAll();
                return;
            }

            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();
            DateTime chartStart = AltitudeSeries.DayChartStart(duskLocal);
            DateTime chartStop  = AltitudeSeries.DayChartStop(dawnLocal);
            int totalMins = Convert.ToInt32(Math.Round((chartStop - chartStart).TotalMinutes));
            int count = totalMins + 1;
            DateTime startUtc = DateTime.SpecifyKind(chartStart, DateTimeKind.Local).ToUniversalTime();

            // Lock X axis to the night bounds so the HD overlay's null Y values
            // can't trigger LC2's auto-zoom-to-non-null-span behavior.
            mXAxis.MinLimit = chartStart.ToOADate();
            mXAxis.MaxLimit = chartStop.ToOADate();

            UpdateGradientSections(chartStart, duskLocal, dawnLocal, chartStop);
            UpdateNowLine(now);
            UpdateHorizonLine(horizon);

            // Reset HD overlay state -- the underlying ObservableCollections are
            // about to be repopulated with fresh altitude data; any pending
            // backups belong to the prior render cycle.
            mOverlay.ClearAll();
            mTargetWindows.Clear();
            mTargetColors.Clear();

            BuildOrUpdateMoonSeries(location, chartStart, startUtc, count, night.LunarIlluminationFraction, ct);

            var newSeriesByTarget = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList = new List<ISeries>();
            if (mMoonSeries != null) seriesList.Add(mMoonSeries);

            for (int t = 0; t < targets.Count; t++)
            {
                ct.ThrowIfCancellationRequested();
                Target target = targets[t];
                if (target == null) continue;

                Color c = TargetColorPalette[t % TargetColorPalette.Length];
                mTargetColors[target] = c;

                IReadOnlyList<double> altitudes = AltitudeCurve.Sample(
                    target, location, startUtc, TimeSpan.FromMinutes(1), count);

                var series = GetOrCreateTargetSeries(target, c);
                FillTargetSeriesData(series, chartStart, count, altitudes);

                var window = ComputeBestDayWindow(target, location, night, profile, horizon, duration);
                ApplyTargetVisibility(series, c, window.HasValue);
                if (window.HasValue)
                {
                    mTargetWindows[series] = (
                        window.Value.Start.ToOADate(),
                        window.Value.End.ToOADate(),
                        window.Value.Floor);
                }

                newSeriesByTarget[target] = series;
                seriesList.Add(series);
            }

            mSeriesByTarget.Clear();
            foreach (var kv in newSeriesByTarget) mSeriesByTarget[kv.Key] = kv.Value;
            mChart.Series = seriesList;
            BuildLegendItems();

            RecomputeLayout();
        }

        // Rebuild the external legend FlowLayoutPanel from the current target
        // series collection. Each item is a small Panel with a color marker +
        // target-name Label; click toggles the corresponding LineSeries.IsVisible.
        // FlowLayoutPanel auto-wraps to multiple rows as the legend grows.
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
                ForeColor = Color.LightGray,
                BackColor = Color.FromArgb(70, 70, 70),
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
                // Reassigning Series forces LC2 to re-iterate and re-evaluate
                // IsVisible. Plain Invalidate() repaints the cached layout but
                // doesn't pick up IsVisible changes on existing series.
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

        // Recompute per-target BestSession windows and apply hide-on-no-fit to the
        // existing series collection. Cheap path for Horizon / Duration / MoonAvoidance
        // scrubs that don't change altitude geometry — only the per-target visibility
        // and any active HD overlay rectangles. Caller must have run Render first;
        // this method assumes mSeriesByTarget is already populated for the current
        // target list.
        public void RefreshDayWindowsAndVisibility(
            IChartCacheStore cache,
            MoonAvoidanceProfile profile,
            Location location,
            double horizon,
            TimeSpan duration)
        {
            if (location == null || mSeriesByTarget.Count == 0) return;
            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location);
            if (!night.IsValid) return;

            UpdateHorizonLine(horizon);

            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mTargetColors.TryGetValue(target, out Color c)) c = Color.White;

                var window = ComputeBestDayWindow(target, location, night, profile, horizon, duration);
                ApplyTargetVisibility(series, c, window.HasValue);
                if (window.HasValue)
                {
                    mTargetWindows[series] = (
                        window.Value.Start.ToOADate(),
                        window.Value.End.ToOADate(),
                        window.Value.Floor);
                }
                else
                {
                    mTargetWindows.Remove(series);
                }
            }

            // Re-apply any active HD overlay rectangles against the refreshed windows.
            // For series whose window vanished, the overlay restores from backup and
            // releases the snapshot; series whose window shifted get the rectangle
            // re-rendered against the new bounds.
            mOverlay.RefreshActiveOverlays();
        }

        // Hide via fully-transparent stroke (zero alpha) when no D-hour window fits
        // tonight; restore the palette stroke when a window fits. Mirrors the legacy
        // RebuildDayTooltip's Color.Transparent / mSeriesColor toggle. Stroke width 2
        // matches the visible-curve build path.
        private static void ApplyTargetVisibility(
            LineSeries<ObservablePoint> series, Color color, bool hasWindow)
        {
            byte a = hasWindow ? color.A : (byte)0;
            series.Stroke = new SolidColorPaint(new SKColor(color.R, color.G, color.B, a), 2);
        }

        private void ClearAll()
        {
            mOverlay.ClearAll();
            mTargetWindows.Clear();
            mTargetColors.Clear();
            mSeriesByTarget.Clear();
            mMoonSeries = null;
            mChart.Series = Array.Empty<ISeries>();
            mLegendPanel.Controls.Clear();
        }

        // Recreate gradient Fills sized to the actual dusk/dawn widths. LC2 caches
        // shaders per-Section; calling this every Render keeps the gradient correctly
        // sized when the night window changes (Location / DateTime edits).
        private void UpdateGradientSections(
            DateTime chartStart, DateTime duskLocal, DateTime dawnLocal, DateTime chartStop)
        {
            mDuskSection.Xi = chartStart.ToOADate();
            mDuskSection.Xj = duskLocal.ToOADate();
            mDawnSection.Xi = dawnLocal.ToOADate();
            mDawnSection.Xj = chartStop.ToOADate();

            // SKPoint coords for RectangularSection.Fill gradients are normalized
            // to the chart's plot area (NOT the section's bounds). So a section
            // of width W out of total night width T gets gradient endpoints from
            // 0 to W/T (dusk: opaque-left → faded-right) or 1-W/T to 1 (dawn).
            double total = (chartStop - chartStart).TotalMinutes;
            float duskFrac = (float)((duskLocal - chartStart).TotalMinutes / total);
            float dawnFrac = (float)((chartStop - dawnLocal).TotalMinutes / total);
            mDuskSection.Fill = new LinearGradientPaint(
                new[] { YellowOpaque, YellowFaded },
                new SKPoint(0f, 0.5f),
                new SKPoint(duskFrac, 0.5f));
            mDawnSection.Fill = new LinearGradientPaint(
                new[] { YellowFaded, YellowOpaque },
                new SKPoint(1f - dawnFrac, 0.5f),
                new SKPoint(1f, 0.5f));
        }

        private void OnChartSizeChanged(object sender, EventArgs e)
        {
            // LC2 caches the gradient shader at first paint; horizontal resize
            // would otherwise leave the dawn gradient progressively cut off.
            // Re-assigning Fill forces a fresh shader resolve.
            if (!mDuskSection.Xi.HasValue || !mDuskSection.Xj.HasValue
                || !mDawnSection.Xi.HasValue || !mDawnSection.Xj.HasValue) return;
            if (mDuskSection.Xi.Value == 0 && mDuskSection.Xj.Value == 0) return;  // pre-render
            DateTime chartStart = DateTime.FromOADate(mDuskSection.Xi.Value);
            DateTime duskLocal  = DateTime.FromOADate(mDuskSection.Xj.Value);
            DateTime dawnLocal  = DateTime.FromOADate(mDawnSection.Xi.Value);
            DateTime chartStop  = DateTime.FromOADate(mDawnSection.Xj.Value);
            UpdateGradientSections(chartStart, duskLocal, dawnLocal, chartStop);
        }

        private void OnChartMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                mOverlay.RestoreAll();
                mChart.Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            var clickData = mChart.ScalePixelsToData(new LvcPointD(e.X, e.Y));
            if (clickData.Y < MinAltitude || clickData.Y > MaxAltitude) return;

            // Inside the plot area: HD overlay hit-test. Legend clicks are
            // handled per-item in the external FlowLayoutPanel, not here.
            mOverlay.TryToggleAt(clickData.X, clickData.Y);
            mChart.Invalidate();
        }

        // Build (or refresh) the shared Moon-Day filled area series. Moon altitude
        // depends only on Location + time; alpha-scaled by the night's lunar
        // illumination fraction.
        private void BuildOrUpdateMoonSeries(
            Location location,
            DateTime chartStart,
            DateTime startUtc,
            int count,
            double lunarIllumination,
            CancellationToken ct)
        {
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            ObservableCollection<ObservablePoint> data;
            if (mMoonSeries == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                mMoonSeries = new LineSeries<ObservablePoint>
                {
                    Name = "Moon",
                    Values = data,
                    Stroke = null,
                    GeometrySize = 0,
                    LineSmoothness = 0.4,
                    IsVisibleAtLegend = false,
                    ZIndex = -1,
                };
            }
            else
            {
                data = mMoonSeries.Values as ObservableCollection<ObservablePoint>
                    ?? new ObservableCollection<ObservablePoint>();
                if (!ReferenceEquals(data, mMoonSeries.Values)) mMoonSeries.Values = data;
            }

            byte alpha = (byte)Math.Min(250, Math.Max(0, (int)(lunarIllumination * 250.0)));
            mMoonSeries.Fill = new SolidColorPaint(new SKColor(209, 209, 209, alpha));

            // Mutate in place to preserve the ObservableCollection identity. Length
            // can shift when night length changes (different date / latitude); add
            // / remove to fit count.
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                DateTime point = chartStart.AddMinutes(i);
                DateTime pointUtc = DateTime.SpecifyKind(
                    startUtc.AddMinutes(i), DateTimeKind.Utc);
                double moonAlt = AstroUtil.GetMoonAltitude(pointUtc, observer);
                double? plotY = moonAlt < 0 ? (double?)null : moonAlt;
                var p = new ObservablePoint(point.ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);
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

        private static void FillTargetSeriesData(
            LineSeries<ObservablePoint> series,
            DateTime chartStart,
            int count,
            IReadOnlyList<double> altitudes)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }
            for (int i = 0; i < count; i++)
            {
                double alt = altitudes[i];
                double? plotY = alt < 0 ? (double?)null : alt;
                var p = new ObservablePoint(chartStart.AddMinutes(i).ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);
        }

        // Mirrors AltitudeSeries.ComputeBestDayWindow. Shared NightCache (when
        // present) is used; otherwise the gated NightCalculator.ComputeNight is
        // called inline.
        private static (DateTime Start, DateTime End, double Floor)? ComputeBestDayWindow(
            Target target, Location location, NightWindow night, MoonAvoidanceProfile profile,
            double horizon, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return null;
            IHorizonProfile horizonProfile = new ScalarHorizonProfile(horizon);
            var best = BestSession.For(
                target, location, night, horizonProfile,
                duration, duration,
                SinAltQuality,
                profile: profile);
            if (best == null) return null;
            double floor = SessionAltitude.Floor(target, location, best.Value.Start, best.Value.End);
            return (best.Value.Start.ToLocalTime(), best.Value.End.ToLocalTime(), floor);
        }

        public void Dispose()
        {
            mChart.MouseDown -= OnChartMouseDown;
            mChart.SizeChanged -= OnChartSizeChanged;
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
