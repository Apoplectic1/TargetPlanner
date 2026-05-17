using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Astronomy.Core;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Brightness;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Sun;
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
    // LiveCharts2 implementation of TP's Sky chart area. Stateless renderer:
    // every Render(...) call refreshes the chart from the supplied inputs,
    // preserving Series identity across calls (ObservableCollection mutation
    // triggers reactive redraw without rebuilding the series object).
    //
    // Y axis is inverted-data, NOT inverted-axis: brighter sky (lower mag)
    // renders HIGHER, but the X axis stays at the visual bottom (no
    // IsReversed). The plot Y range stays canonical [SkyAxisMinMag,
    // SkyAxisMaxMag] = [16, 22], and the Labeler maps each plot value back to
    // its actual magnitude via mag = min + max - plotY. This is the LC2
    // translation of the legacy MS Charts CustomLabels trick in
    // AltitudeChart.ConfigureSkyYAxis.
    //
    // Owns one controller wired to its CartesianChart instance:
    //   - HoverTooltipController: per-DataPoint snap tooltip (30 ms debounce);
    //     custom formatter reads pre-formatted text from a parallel string[]
    //     populated during Render -- the user sees the actual minute-resolution
    //     K-S value rather than an interpolated number that doesn't correspond
    //     to any real measurement.
    //
    // No OverlayController (no HD click-to-toggle on Sky) and no MoonSeries
    // (the moon's contribution is baked into the K-S brightness, so a separate
    // moon curve adds clutter without information).
    //
    public class AltitudeSubChart_Sky : IAltitudeSubChart
    {
        // K-S magnitude bounds in mag/arcsec². Brighter sky = lower mag = top of
        // plot (after inversion); darker sky = higher mag = bottom. Matches the
        // legacy AltitudeSeries.SkyAxisMinMag / SkyAxisMaxMag constants byte-
        // for-byte so per-target curves overlap legacy positions exactly.
        public const double SkyAxisMinMag = 16.0;
        public const double SkyAxisMaxMag = 22.0;

        // Yellow gradient endpoints for dusk/dawn sections (matches Day side
        // and the legacy MS Charts Sky area).
        private static readonly SKColor YellowOpaque = new SKColor(255, 238, 88, 145);
        private static readonly SKColor YellowFaded  = new SKColor(255, 238, 88,   0);

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

        // Per-target series keyed by Target. Series identity preserved across
        // renders (via GetOrCreateTargetSeries) so legend toggle state survives
        // a Render call -- e.g. when the user toggles off a target then scrubs
        // Bortle, the toggled-off curve must stay hidden.
        private readonly Dictionary<Target, LineSeries<ObservablePoint>> mSeriesByTarget
            = new Dictionary<Target, LineSeries<ObservablePoint>>();

        // Per-target stable color from ChartLayout.TargetColorPalette.
        private readonly Dictionary<Target, Color> mTargetColors
            = new Dictionary<Target, Color>();

        // "Fit tonight" tracker for the legend filter. Populated by
        // RefreshVisibility per HasFit per target, consulted by BuildLegendItems
        // to skip unfit targets so the legend only lists curves the user can
        // actually see (alpha-0 unfit curves stay in mChart.Series, but their
        // legend entries don't render). Mirrors Day's mTargetWindows.ContainsKey
        // shape; Sky needs only a presence bit (no window endpoints since there's
        // no HD overlay on Sky).
        private readonly HashSet<LineSeries<ObservablePoint>> mFitSeries
            = new HashSet<LineSeries<ObservablePoint>>();

        // Pre-formatted per-minute tooltip text, keyed by series. The custom
        // CurveTooltipFormatter reads mTooltipText[series][segmentStart] to
        // surface the actual K-S magnitude rather than the inverted plot Y.
        // Same length as the series' Values collection.
        private readonly Dictionary<LineSeries<ObservablePoint>, string[]> mTooltipText
            = new Dictionary<LineSeries<ObservablePoint>, string[]>();

        // Per-render minute-grid bounds. Captured in Render(...) and reused by
        // RefreshSkyBrightness(...) so the cheap rebuild path doesn't have to
        // recompute night bounds (Bortle / ExtinctionK / ActiveFilter changes
        // don't shift the night).
        private DateTime mLastChartStart;
        private DateTime mLastChartStartUtc;
        private int mLastCount;

        // Astronomical-night bounds (UTC) snapshotted from the last Render. K-S
        // compute is gated to this window because the model's twilight component
        // is filter-blind and produces unreliable results at high airmass during
        // twilight (see ROADMAP.md "wavelength-dependent twilight in K-S sky
        // brightness"). Outside [AstronomicalDusk, AstronomicalDawn] the curve
        // gets null-Y -- the dusk/dawn yellow gradient sections become "K-S not
        // shown here" zones, which is self-documenting against the chart's
        // existing visual cue. RefreshSkyBrightness reads these fields to apply
        // the same gate during cheap-scrub rebuilds.
        private DateTime mLastAstronomicalDuskUtc;
        private DateTime mLastAstronomicalDawnUtc;

        // Moon altitude overlay (filled area) -- mirrors Day's moon, but Y values
        // mapped to Sky's [SkyAxisMinMag, SkyAxisMaxMag] plot range so the curve
        // fits the magnitude axis. Visually overlays the K-S brightness curves
        // with a translucent grey area showing moon-up time + altitude. The
        // moon's K-S contribution is still baked into the per-target curves;
        // this overlay is a presence/intensity indicator only.
        private LineSeries<ObservablePoint> mMoonSeries;

        private readonly HoverTooltipController mHover;

        // Cached IdealHeight from the last layout pass; used to detect changes so
        // the IdealHeightChanged event only fires when the form actually needs to
        // resize.
        private int mLastIdealHeight = -1;

        // Raised when the chart's IdealHeight changes (legend wrap count moved).
        // MainForm subscribes and resizes Panel_AltitudeChart + GroupBox_Altitude +
        // Form by the delta so the plot area stays in a fixed pixel position
        // regardless of target count.
        public event EventHandler IdealHeightChanged;

        public int IdealHeight => ChartLayout.ChartFixedHeight + mLegendPanel.Height;

        // Active filter center wavelength (nm) for Rayleigh λ⁻⁴ extinction
        // scaling via SkyBrightness.ScaleK. Defaults to V-band (550 nm) so K-S
        // produces sensible values before MainForm pushes the user's filter.
        // MainForm mirrors the legacy AltitudeChart.ActiveFilterCenterNm setter
        // pattern: SetActiveFilter pushes to both this and the legacy chart so
        // they stay in sync mid-migration.
        public double ActiveFilterCenterNm { get; set; } = 550.0;

        public AltitudeSubChart_Sky()
        {
            mXAxis = new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("h:mm tt"),
                UnitWidth = TimeSpan.FromHours(1).TotalDays,
                MinStep = TimeSpan.FromHours(1).TotalDays,
                // ForceStepToMin disables LC2's adaptive label-skip density logic.
                // Mirrors Day's X-axis config; both charts use the same hour-tick
                // labeling scheme over the same night bounds.
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
            };
            mYAxis = new Axis
            {
                Name = "Sky brightness (mag/arcsec²)",
                MinLimit = SkyAxisMinMag,
                MaxLimit = SkyAxisMaxMag,
                MinStep = 1,
                ForceStepToMin = true,
                // Inverted-data Labeler: plot Y stays in [16, 22], but the tick
                // label shows the actual magnitude (16 at top, 22 at bottom).
                // mag = min + max - plotY -- matches the data inversion in
                // BuildOrUpdateTargetSeries and the legacy CustomLabels trick.
                Labeler = v => ((int)Math.Round(SkyAxisMinMag + SkyAxisMaxMag - v))
                    .ToString(CultureInfo.InvariantCulture),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(ChartLayout.GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

            // Initialize section objects with placeholder bounds; Render() rewrites
            // Xi/Xj per the actual night window.
            mDuskSection = new RectangularSection { Xi = 0, Xj = 0 };
            mDawnSection = new RectangularSection { Xi = 0, Xj = 0 };
            mNowLine = new RectangularSection
            {
                Xi = 0, Xj = 0,
                Stroke = new SolidColorPaint(SKColors.Red, 2),
            };

            mChart = new CartesianChart
            {
                XAxes = new[] { mXAxis },
                YAxes = new[] { mYAxis },
                Sections = new[] { mDuskSection, mDawnSection, mNowLine },
                Series = Array.Empty<ISeries>(),
                LegendPosition = LegendPosition.Hidden,
                FindingStrategy = FindingStrategy.ExactMatch,
                TooltipPosition = TooltipPosition.Hidden,
                AnimationsSpeed = TimeSpan.Zero,
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Top,
                Height = ChartLayout.ChartFixedHeight,
            };

            // Lock the plot area to a fixed pixel rectangle. Same template as Day
            // so the plot pixel position is identical when the user swaps radios.
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
            // Order matters for Dock=Top stacking: the LAST control added docks
            // FIRST. Add legend first (lower z-order), then chart (higher z-order)
            // so chart claims the top region and legend docks below it.
            mContainer.Controls.Add(mLegendPanel);
            mContainer.Controls.Add(mChart);
            Control = mContainer;

            // Per-DataPoint snap tooltip: 30 ms debounce, custom formatter reads
            // pre-formatted text by segmentStart so the user sees the actual
            // mag/arcsec² value computed at that minute.
            mHover = new HoverTooltipController(
                mChart,
                () => mSeriesByTarget.Values,
                curveTooltipFormatter: SkyTooltipFormatter,
                debounceMs: 30);

            mChart.SizeChanged += OnChartSizeChanged;
        }

        // Update the red now-line position in place.
        public void UpdateNowLine(DateTime now)
        {
            double oa = now.ToOADate();
            mNowLine.Xi = oa;
            mNowLine.Xj = oa;
        }

        // Sky has no horizon line (its Y axis is K-S magnitude, not altitude).
        // The interface contract still requires the method; it's a no-op here.
        public void UpdateHorizonLine(double horizon) { }

        // Custom formatter: per-DataPoint snap. segmentStart is the i in the
        // bracketing segment [data[i], data[i+1]]; we surface the left-edge's
        // pre-formatted text. Same minute resolution as the underlying K-S
        // sweep -- no interpolated values that don't correspond to real samples.
        private string SkyTooltipFormatter(
            LineSeries<ObservablePoint> series,
            IList<ObservablePoint> data,
            double hoverX, double hoverY,
            double interpY,
            int segmentStart)
        {
            if (!mTooltipText.TryGetValue(series, out var arr)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= arr.Length) return string.Empty;
            return arr[segmentStart] ?? string.Empty;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache, ChartEvaluation eval)
        {
            _ = eval; // Phase 4: accept but ignore; Phase 7 will wire short-circuit.
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Location == null) throw new ArgumentException("ctx.Location must not be null", nameof(ctx));
            if (ctx.Policy == null) throw new ArgumentException("ctx.Policy must not be null", nameof(ctx));

            Location location = ctx.Location;
            IReadOnlyList<Target> targets = ctx.Targets;
            DateTime now = location.DateTime;

            // Sync ActiveFilterCenterNm from the snapshot before computing K-S.
            // ChartContext.Policy is the authoritative input; the property setter still
            // exists for cheap-scrub callers (RefreshSkyBrightness from the
            // SessionsRebuildDebounce_Tick path) which feed it directly.
            ActiveFilterCenterNm = ctx.Policy.FilterCenterNm;

            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location);
            if (!night.IsValid)
            {
                ClearAll();
                return;
            }

            DateTime duskLocal = night.AstronomicalDusk.ToLocalTime();
            DateTime dawnLocal = night.AstronomicalDawn.ToLocalTime();
            // Use ChartLayout.BuildDayWindow so the DayWindowKey we read from
            // the moon cache matches the one EnsureAsync used to build the
            // entry. Day and Sky share the same dayKey for the same night.
            var dayWindow = ChartLayout.BuildDayWindow(night);
            DateTime chartStart = dayWindow.ChartStart;
            DateTime chartStop = dayWindow.ChartStop;
            DateTime startUtc = dayWindow.StartUtc;
            int count = dayWindow.Count;
            DayWindowKey dayKey = dayWindow.Key;

            mLastChartStart = chartStart;
            mLastChartStartUtc = startUtc;
            mLastCount = count;
            mLastAstronomicalDuskUtc = night.AstronomicalDusk;
            mLastAstronomicalDawnUtc = night.AstronomicalDawn;

            // Lock X axis to the night bounds so the gradient sections render
            // edge-to-edge and the now-line position is well defined even before
            // the user adds targets. MinLimit/MaxLimit are nudged outward by
            // ChartLayout.LabelEdgeEpsilonDays (1 ms) so LC2's Ceil/Floor edge-
            // tick math reliably places the leftmost/rightmost hour labels --
            // same fix Day's X axis uses.
            mXAxis.MinLimit = chartStart.ToOADate() - ChartLayout.LabelEdgeEpsilonDays;
            mXAxis.MaxLimit = chartStop.ToOADate() + ChartLayout.LabelEdgeEpsilonDays;

            UpdateGradientSections(chartStart, duskLocal, dawnLocal, chartStop);
            UpdateNowLine(now);

            mTargetColors.Clear();
            mFitSeries.Clear();

            // K-S inputs that depend only on Location + filter -- compute once
            // per Render and reuse across the per-target loop.
            double v0 = Bortle.DefaultZenithMag(location.BortleClass);
            double kAtBand = SkyBrightness.ScaleK(location.ExtinctionK, ActiveFilterCenterNm);
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            // Moon altitude overlay -- shared across all targets; built before
            // the per-target loop so it lands first in mChart.Series (ZIndex=-1
            // also puts it behind the target curves). Read altitudes from the
            // per-DayWindowKey cache entry; fall back to inline compute on
            // cache miss (defensive only -- EnsureAsync prepares this).
            IReadOnlyList<double> moonAltitudes = cache?.GetMoonOrNull(dayKey)?.AltitudesPerMinute;
            if (moonAltitudes == null || moonAltitudes.Count != count)
            {
                Log.Warn($"Sky moon cache miss; inline fallback (dayKey.Count={count}, cached={moonAltitudes?.Count ?? -1})");
                moonAltitudes = ComputeMoonAltitudesInline(location, startUtc, count);
            }
            BuildOrUpdateMoonSeries(moonAltitudes, chartStart, count, night.LunarIlluminationFraction);

            // Compute K-S data for ALL passed targets so a future H/D/M scrub
            // that brings an unfit target back into fit can re-add its series
            // without recomputing K-S. Fit-tonight filter is applied to
            // mChart.Series (and the legend via mFitSeries) -- mirrors Day's
            // "compute everything, filter display" pattern. Same fit decision
            // as Day (TargetFitEntry.Tonight.Floor.HasValue) so Day and Sky
            // always agree on which targets are visible tonight; zero
            // BestSession.For calls in the Sky render path.
            var newSeriesByTarget = new Dictionary<Target, LineSeries<ObservablePoint>>();
            var seriesList = new List<ISeries>();
            if (mMoonSeries != null) seriesList.Add(mMoonSeries);
            for (int t = 0; t < targets.Count; t++)
            {
                Target target = targets[t];
                if (target == null) continue;

                Color c = ChartLayout.ResolveTargetColor(ctx.TargetColors, target, t);
                mTargetColors[target] = c;

                var series = GetOrCreateTargetSeries(target, c);
                BuildOrUpdateTargetSeries(series, target, location, chartStart, startUtc,
                    count, observer, kAtBand, v0,
                    night.AstronomicalDusk, night.AstronomicalDawn);

                bool fits = cache?.GetFitOrNull(target, ctx.Hdm)?.Tonight.Floor.HasValue ?? false;
                if (fits)
                {
                    ApplyTargetVisibility(series, c, true);
                    mFitSeries.Add(series);
                    seriesList.Add(series);
                }

                newSeriesByTarget[target] = series;
            }

            // Drop tooltip arrays for targets no longer in the render list.
            var droppedSeries = mSeriesByTarget
                .Where(kv => !newSeriesByTarget.ContainsKey(kv.Key))
                .Select(kv => kv.Value)
                .ToList();
            foreach (var s in droppedSeries) mTooltipText.Remove(s);

            mSeriesByTarget.Clear();
            foreach (var kv in newSeriesByTarget) mSeriesByTarget[kv.Key] = kv.Value;
            mChart.Series = seriesList;
            BuildLegendItems();

            RecomputeLayout();
        }

        // Hide-on-no-fit visibility refresh. Mirrors AltitudeSubChart_Day's
        // RefreshDayWindowsAndVisibility -- per-target BestSession.For tonight
        // with the current Horizon / Duration / MoonAvoidanceProfile; if no
        // D-hour window fits, the target's Sky stroke goes alpha 0 (invisible).
        // The K-S magnitudes themselves are NOT cleared -- only the stroke
        // alpha toggles -- so a subsequent scrub that re-admits the target
        // restores the curve at its current K-S values without recomputation.
        //
        // Mirrors the legacy AltitudeSeries.RebuildDayTooltip behaviour:
        // "The Sky brightness companion curve mirrors the hide-on-no-fit
        //  (consistency: a target hidden by the Lorentzian fit-check shouldn't
        //  have its sky-brightness curve still visible on the Sky area)."
        public void RefreshVisibility(ChartContext ctx, IChartCacheStore cache)
        {
            if (ctx == null || ctx.Location == null || ctx.Policy == null || mSeriesByTarget.Count == 0) return;
            Location location = ctx.Location;

            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location);
            if (!night.IsValid) return;

            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mTargetColors.TryGetValue(target, out Color c)) c = Color.White;

                bool fits = cache?.GetFitOrNull(target, ctx.Hdm)?.Tonight.Floor.HasValue ?? false;
                if (fits)
                {
                    ApplyTargetVisibility(series, c, true);
                    mFitSeries.Add(series);
                }
                else
                {
                    mFitSeries.Remove(series);
                }
            }

            // Rebuild mChart.Series + legend from mSeriesByTarget filtered on
            // mFitSeries -- Day-style fit-tonight filter, no alpha-0 toggling.
            // H/D/M scrubs that change a target's fit status add or remove its
            // curve and legend entry together; Sky and Day stay in lockstep on
            // which targets are visible.
            var seriesList = new List<ISeries>();
            foreach (var kv in mSeriesByTarget)
            {
                if (mFitSeries.Contains(kv.Value)) seriesList.Add(kv.Value);
            }
            mChart.Series = seriesList;
            BuildLegendItems();
        }

        // Cheap path for Bortle / ExtinctionK / ActiveFilter scrubs that don't
        // change the night-window geometry, only the K-S magnitudes. Walks every
        // existing series' ObservablePoint collection in place; no series identity
        // churn. Caller must have run Render(...) at least once -- this method
        // assumes mSeriesByTarget is populated and mLastChartStart / mLastChartStartUtc
        // / mLastCount carry the night-grid bounds.
        public void RefreshSkyBrightness(IChartCacheStore cache, Location location)
        {
            _ = cache;  // unused: night bounds taken from the last Render's snapshot
            if (location == null || mSeriesByTarget.Count == 0 || mLastCount <= 0) return;

            double v0 = Bortle.DefaultZenithMag(location.BortleClass);
            double kAtBand = SkyBrightness.ScaleK(location.ExtinctionK, ActiveFilterCenterNm);
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                BuildOrUpdateTargetSeries(series, target, location,
                    mLastChartStart, mLastChartStartUtc, mLastCount,
                    observer, kAtBand, v0,
                    mLastAstronomicalDuskUtc, mLastAstronomicalDawnUtc);
            }
        }

        private void ClearAll()
        {
            mSeriesByTarget.Clear();
            mTargetColors.Clear();
            mTooltipText.Clear();
            mFitSeries.Clear();
            mMoonSeries = null;
            mLastCount = 0;
            mChart.Series = Array.Empty<ISeries>();
            mLegendPanel.Controls.Clear();
        }

        // Build the moon overlay for Sky from a pre-computed altitude array
        // (sourced from the per-DayWindowKey moon cache). Y values mapped to
        // Sky's [SkyAxisMinMag, SkyAxisMaxMag] plot range: altitude=0 ->
        // y_plot=SkyAxisMinMag (bottom = darkest mag), altitude=90 ->
        // y_plot=SkyAxisMaxMag (top = brightest mag). Below-horizon points get
        // null Y so the fill gaps where the moon is down.
        private void BuildOrUpdateMoonSeries(
            IReadOnlyList<double> altitudes,
            DateTime chartStart,
            int count,
            double lunarIllumination)
        {
            byte alpha = (byte)Math.Min(250, Math.Max(0, (int)(lunarIllumination * 250.0)));

            int aboveHorizon = 0;
            const double yRange = SkyAxisMaxMag - SkyAxisMinMag;
            var data = new ObservableCollection<ObservablePoint>();
            for (int i = 0; i < count; i++)
            {
                double moonAlt = altitudes[i];
                if (moonAlt > 0) aboveHorizon++;
                double? plotY = moonAlt < 0
                    ? (double?)null
                    : SkyAxisMinMag + (moonAlt / 90.0) * yRange;
                DateTime point = chartStart.AddMinutes(i);
                data.Add(new ObservablePoint(point.ToOADate(), plotY));
            }

            mMoonSeries = new LineSeries<ObservablePoint>
            {
                Name = "Moon",
                Values = data,
                Stroke = null,
                Fill = new SolidColorPaint(new SKColor(209, 209, 209, alpha)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
                IsVisibleAtLegend = false,
                ZIndex = -1,
            };

            if (Log.IsDiagEnabled("Sky"))
            {
                Log.Diag("Sky",
                    $"BuildMoon illum={lunarIllumination:F3} alpha={alpha} count={count} " +
                    $"aboveHorizon={aboveHorizon} chartStart={chartStart:yyyy-MM-dd HH:mm}");
            }
        }

        // Defensive fallback when the moon cache misses. Matches
        // ChartCacheStore.BuildMoonEntryAsync's compute path.
        private static IReadOnlyList<double> ComputeMoonAltitudesInline(
            Location location, DateTime startUtc, int count)
        {
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);
            double[] altitudes = new double[count];
            for (int i = 0; i < count; i++)
            {
                DateTime pointUtc = DateTime.SpecifyKind(
                    startUtc.AddMinutes(i), DateTimeKind.Utc);
                altitudes[i] = AstroUtil.GetMoonAltitude(pointUtc, observer);
            }
            return altitudes;
        }


        // Hide via fully-transparent stroke (zero alpha) when no D-hour window
        // fits tonight; restore the palette stroke when one fits. Mirrors Day's
        // ApplyTargetVisibility -- same alpha toggle, same stroke width.
        private static void ApplyTargetVisibility(
            LineSeries<ObservablePoint> series, Color color, bool hasWindow)
        {
            byte a = hasWindow ? color.A : (byte)0;
            series.Stroke = new SolidColorPaint(new SKColor(color.R, color.G, color.B, a), 2);
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

        // Per-minute K-S sweep. Mutates the series' ObservableCollection in
        // place to preserve identity across renders + refreshes (legend toggle
        // state lives on the LineSeries instance via IsVisible). Also rebuilds
        // the parallel mTooltipText[series] array so the snap formatter has
        // text aligned with each minute.
        private void BuildOrUpdateTargetSeries(
            LineSeries<ObservablePoint> series,
            Target target,
            Location location,
            DateTime chartStart,
            DateTime startUtc,
            int count,
            ObserverInfo observer,
            double kAtBand,
            double v0,
            DateTime astronomicalDuskUtc,
            DateTime astronomicalDawnUtc)
        {
            var data = series.Values as ObservableCollection<ObservablePoint>;
            if (data == null)
            {
                data = new ObservableCollection<ObservablePoint>();
                series.Values = data;
            }

            string[] tooltips = new string[count];
            for (int i = 0; i < count; i++)
            {
                DateTime point = chartStart.AddMinutes(i);
                DateTime utc = DateTime.SpecifyKind(startUtc.AddMinutes(i), DateTimeKind.Utc);

                // Gate K-S compute to astronomical night. The model's twilight
                // component is filter-blind, producing unreliable curves at
                // high airmass during civil/nautical twilight (see ROADMAP.md
                // for the wavelength-dependent twilight + bandwidth fixes
                // queued at the Library level). Outside [AstronomicalDusk,
                // AstronomicalDawn] we null-Y the data point; the dusk/dawn
                // yellow gradient sections then double as "K-S not shown here"
                // zones, self-documenting against the chart's existing visual
                // cue. Once the Library fixes land, drop this gate.
                bool inAstronomicalNight = utc >= astronomicalDuskUtc
                                        && utc <= astronomicalDawnUtc;

                double? plotY;
                string tooltip;
                if (!inAstronomicalNight)
                {
                    plotY = null;
                    tooltip = string.Format(CultureInfo.InvariantCulture,
                        "{0}\n{1:h:mm tt}\n(twilight — K-S not shown)",
                        target.Name, point);
                }
                else
                {
                    AltAz t = AltAzCalculator.At(target, location, utc);
                    var m = MoonSeparation.ObserveAt(target, location, utc);
                    double phase = SkyBrightness.PhaseAngleDegFromAgeDays(LunarAge.DaysAt(utc));
                    double sunAlt = SunPosition.AltAzAt(location, utc).Altitude;

                    double mag = SkyBrightness.KsAt(
                        t.Altitude, t.Azimuth,
                        m.MoonAltDeg, m.MoonAzDeg,
                        phase, sunAlt, kAtBand, v0);

                    // null Y = "no data" gap (LC2 renders nullable points as breaks
                    // in the line). Cleaner than the legacy -90 sentinel because
                    // Sky's plot range is [16, 22]; a -90 spike would clip but
                    // could still leave a stray pixel artifact.
                    plotY = double.IsNaN(mag)
                        ? (double?)null
                        : (SkyAxisMinMag + SkyAxisMaxMag - mag);

                    tooltip = double.IsNaN(mag)
                        ? string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:h:mm tt}\n(target below horizon)",
                            target.Name, point)
                        : string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:h:mm tt}\n{2:0.0} mag/arcsec²",
                            target.Name, point, mag);
                }

                var p = new ObservablePoint(point.ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);

                tooltips[i] = tooltip;
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);

            mTooltipText[series] = tooltips;
        }

        // Rebuild the external legend FlowLayoutPanel from the current target
        // series collection. Each item is a small Label with a color marker +
        // target-name; click toggles the corresponding LineSeries.IsVisible.
        // FlowLayoutPanel auto-wraps to multiple rows as the legend grows.
        //
        // Filter: targets without an entry in mFitSeries (HasFit returned false
        // under current H/D/M) are excluded from the legend. Their alpha-0
        // curves stay in mChart.Series but the legend matches what's actually
        // visible. Day's BuildLegendItems uses the same shape via
        // mTargetWindows.ContainsKey(series); Sky's mFitSeries is the parallel
        // (Sky has no window endpoints to store, just a fit bit).
        private void BuildLegendItems()
        {
            mLegendPanel.SuspendLayout();
            mLegendPanel.Controls.Clear();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mFitSeries.Contains(series)) continue;
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

        public void Dispose()
        {
            mChart.SizeChanged -= OnChartSizeChanged;
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
