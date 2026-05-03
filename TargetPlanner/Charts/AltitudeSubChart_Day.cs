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

        public CartesianChart Control { get; }

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

            Control = new CartesianChart
            {
                XAxes = new[] { mXAxis },
                YAxes = new[] { mYAxis },
                Sections = new[] { mDuskSection, mDawnSection, mNowLine, mHorizonLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Bottom,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                BackColor = Color.FromArgb(70, 70, 70),
                Dock = DockStyle.Fill,
            };

            // Per-instance controllers. Hover uses interpolated mode (300 ms)
            // because Day altitude curves are smooth and per-DataPoint snap
            // would feel jittery.
            mOverlay = new OverlayController(
                Control,
                () => mSeriesByTarget.Values,
                series => mTargetWindows.TryGetValue(series, out var w)
                    ? ((double, double, double)?)w
                    : null,
                _ => { });
            mHover = new HoverTooltipController(
                Control,
                () => mSeriesByTarget.Values,
                legendTooltipFormatter: null,
                curveTooltipFormatter: null,
                debounceMs: 300);
            mLegend = new LegendClickHandler(Control, _ => { });

            Control.MouseDown += OnChartMouseDown;
            Control.SizeChanged += OnChartSizeChanged;
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
            Control.Series = seriesList;
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
            Control.Series = Array.Empty<ISeries>();
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
                Control.Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            var clickData = Control.ScalePixelsToData(new LvcPointD(e.X, e.Y));

            // Below the plot area: legend strip.
            if (clickData.Y < MinAltitude)
            {
                mLegend.HandleClick(e.X);
                return;
            }
            // Above the plot area: title / no interactive content.
            if (clickData.Y > MaxAltitude) return;

            // Inside the plot area: HD overlay hit-test.
            mOverlay.TryToggleAt(clickData.X, clickData.Y);
            Control.Invalidate();
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
            Control.MouseDown -= OnChartMouseDown;
            Control.SizeChanged -= OnChartSizeChanged;
            mHover.Dispose();
            Control.Dispose();
        }
    }
}
