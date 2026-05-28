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
        // plot (after inversion); darker sky = higher mag = bottom. Was [16, 22]
        // matching the legacy V-band axis; widened to [16, 26] when bandwidth-
        // aware K-S landed so narrowband filters at any Bortle (and broadband at
        // very dark sites) land inside the axis. Narrowband Hα at Bortle 5
        // predicts ~24 mag; at Bortle 1 ~25.6 mag — both off-axis under the
        // legacy [16, 22] bounds. The 10-mag span compresses the per-mag pixel
        // height vs the legacy 6-mag span — acceptable tradeoff vs the
        // alternative dynamic-axis-per-render plumbing. Future upgrade path:
        // per-render bounds = v0 + 2.5·log10(85/bandwidth) + small buffer.
        public const double SkyAxisMinMag = 16.0;
        public const double SkyAxisMaxMag = 26.0;

        // Low-altitude gate for K-S compute. Below this target altitude the K-S 1991
        // dark-sky baseline goes unphysical for light-polluted sites: the +k*(X-1)
        // extinction term dominates at high airmass (k=0.55 + alt 1° → vDark > 30 mag
        // -- darker than physically possible). Real urban skies brighten toward the
        // horizon from off-axis light-pollution in-scatter, which K-S doesn't model
        // (Garstang/Falchi do; future work). 10° is the conventional amateur-low-alt
        // cutoff and bounds the formula's reliable regime for any Bortle range. Below
        // the gate the per-minute sample plots as null-Y (line break) with a tooltip
        // explaining why.
        private const double KsLowAltitudeGateDeg = 10.0;

        // The Container hosts (top) the CartesianChart at fixed height + (bottom) a
        // FlowLayoutPanel hosting custom legend items that wrap as targets grow.
        // MainForm adds Container to Panel_AltitudeChart and resizes Panel +
        // GroupBox + Form to match Container's IdealHeight on legend changes.
        public Control Control { get; }
        private readonly Panel mContainer;
        private readonly CartesianChart mChart;
        private readonly ChartLegendPanel mLegend;

        // Chart-furniture state preserved across Render calls. Sections /
        // Axes objects are mutated in place; only Series can be re-listed.
        private readonly Axis mXAxis;
        private readonly Axis mYAxis;
        private readonly DuskDawnGradient mGradient;
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

        // "Fit tonight" tracker for the legend filter. Populated by Render
        // per HasFit per target, consulted by BuildLegendItems
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
        private DateTime mLastChartStartUtc;
        private int mLastCount;

        // Site time zone for the current Render. The X axis is UTC-internal
        // (every plotted X is the OADate of a UTC instant); this zone is the
        // single seam where the axis Labeler and the tooltip strings convert a
        // UTC instant to the site's wall clock, so DST transitions resolve
        // per-instant. Null before the first Render.
        private TimeZoneInfo mAxisZone;

        // Moon altitude overlay (filled area) -- mirrors Day's moon, but Y values
        // mapped to Sky's [SkyAxisMinMag, SkyAxisMaxMag] plot range so the curve
        // fits the magnitude axis. Visually overlays the K-S brightness curves
        // with a translucent grey area showing moon-up time + altitude. The
        // moon's K-S contribution is still baked into the per-target curves;
        // this overlay is a presence/intensity indicator only.
        private LineSeries<ObservablePoint> mMoonSeries;

        private readonly HoverTooltipController mHover;

        // Raised when the chart's IdealHeight changes (legend wrap count moved).
        // MainForm subscribes and resizes Panel_AltitudeChart + GroupBox_Altitude +
        // Form by the delta so the plot area stays in a fixed pixel position
        // regardless of target count. Forwarded from mLegend (wired in the ctor).
        public event EventHandler IdealHeightChanged;

        // Fixed chart height + the legend's current wrapped height -- owned by
        // ChartLegendPanel.
        public int IdealHeight => mLegend.IdealHeight;

        // Active filter center wavelength (nm) for Rayleigh λ⁻⁴ extinction
        // scaling via SkyBrightness.ScaleK. Defaults to V-band (550 nm) so K-S
        // produces sensible values before MainForm pushes the user's filter.
        public double ActiveFilterCenterNm { get; set; } = 550.0;

        // Active filter passband width (nm) for K-S bandwidth scaling (each of the
        // three nL contributions scales linearly with BW for continuum sources).
        // Defaults to the V-band reference (BWRefNm = 85 nm) so the predicted
        // brightness is identity-scaled before MainForm pushes the user's filter.
        public double ActiveFilterBandwidthNm { get; set; } = SkyBrightness.BWRefNm;

        public AltitudeSubChart_Sky()
        {
            mXAxis = ChartLayout.MakeTimeXAxis(() => mAxisZone);
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
            mGradient = new DuskDawnGradient();
            mNowLine = new RectangularSection
            {
                Xi = 0, Xj = 0,
                Stroke = new SolidColorPaint(SKColors.Red, 2),
            };

            mChart = new CartesianChart
            {
                XAxes = new[] { mXAxis },
                YAxes = new[] { mYAxis },
                Sections = new[] { mGradient.Dusk, mGradient.Dawn, mNowLine },
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

            mLegend = new ChartLegendPanel(mChart);
            mLegend.IdealHeightChanged += (s, e) => IdealHeightChanged?.Invoke(this, EventArgs.Empty);

            mContainer = new Panel
            {
                BackColor = ChartLayout.ChartBackground,
                Dock = DockStyle.Fill,
            };
            // Order matters for Dock=Top stacking: the LAST control added docks
            // FIRST. Add legend first (lower z-order), then chart (higher z-order)
            // so chart claims the top region and legend docks below it.
            mContainer.Controls.Add(mLegend.Panel);
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

            mGradient.WireSizeChanged(mChart);
        }

        // Update the red now-line position in place. The X axis is UTC-internal
        // so the now instant (already UTC) plots as its own OADate directly.
        public void UpdateNowLine(DateTime nowUtc)
        {
            double oa = nowUtc.ToOADate();
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
            double hoverX,
            double interpY,
            int segmentStart)
        {
            if (!mTooltipText.TryGetValue(series, out var arr)) return string.Empty;
            if (segmentStart < 0 || segmentStart >= arr.Length) return string.Empty;
            return arr[segmentStart] ?? string.Empty;
        }

        public void Render(ChartContext ctx, IChartCacheStore cache,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Location == null) throw new ArgumentException("ctx.Location must not be null", nameof(ctx));
            if (ctx.Policy == null) throw new ArgumentException("ctx.Policy must not be null", nameof(ctx));
            // Phase 7's short-circuit-on-eval-flags was reverted; see
            // AltitudeSubChart_Day.Render for rationale (LC2 visual instability
            // across hidden->visible Control transitions).

            Location location = ctx.Location;
            IReadOnlyList<Target> targets = ctx.Targets;
            DateTime now = ctx.Observation.Utc;

            // Sync ActiveFilterCenterNm + ActiveFilterBandwidthNm from the snapshot
            // before computing K-S. ChartContext.Policy.ActiveFilter is the
            // authoritative input; the property setters still exist for cheap-scrub
            // callers (RefreshSkyBrightness from the SessionsRebuildDebounce_Tick
            // path) which feed them directly. Null filter (empty library / pre-init)
            // falls back to V-band defaults.
            TargetPlanner.Filters.Filter activeFilter = ctx.Policy.ActiveFilter;
            ActiveFilterCenterNm    = activeFilter?.CenterNm    ?? 550.0;
            ActiveFilterBandwidthNm = activeFilter?.BandwidthNm ?? SkyBrightness.BWRefNm;

            NightWindow night = cache?.LocationNightCache?.Starting ?? NightCalculator.ComputeNight(location, now);
            if (!night.IsValid)
            {
                ClearAll();
                return;
            }

            TimeZoneInfo zone = ctx.Observation.Zone;
            mAxisZone = zone;
            // Use ChartLayout.BuildDayWindow so the DayWindowKey we read from
            // the moon cache matches the one EnsureAsync used to build the
            // entry. Day and Sky share the same dayKey for the same night.
            var dayWindow = ChartLayout.BuildDayWindow(night, zone);
            DateTime startUtc = dayWindow.StartUtc;
            DateTime endUtc = dayWindow.EndUtc;
            int count = dayWindow.Count;
            DayWindowKey dayKey = dayWindow.Key;

            mLastChartStartUtc = startUtc;
            mLastCount = count;

            // Lock X axis to the night bounds so the gradient sections render
            // edge-to-edge and the now-line position is well defined even before
            // the user adds targets. The axis is UTC-internal -- bounds are the
            // OADate of the UTC start/end instants. MinLimit/MaxLimit are nudged
            // outward by ChartLayout.LabelEdgeEpsilonDays (1 ms) so LC2's
            // Ceil/Floor edge-tick math reliably places the leftmost/rightmost
            // hour labels -- same fix Day's X axis uses.
            mXAxis.MinLimit = startUtc.ToOADate() - ChartLayout.LabelEdgeEpsilonDays;
            mXAxis.MaxLimit = endUtc.ToOADate() + ChartLayout.LabelEdgeEpsilonDays;

            // Gradient sections are UTC-anchored: dusk gradient [startUtc, dusk],
            // dawn gradient [dawn, endUtc].
            mGradient.Update(startUtc, night.AstronomicalDusk,
                             night.AstronomicalDawn, endUtc);
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
            // also puts it behind the target curves). FetchOrCompute reads the
            // per-NightDate cache entry, falling back to inline compute on
            // a cache miss (defensive only -- EnsureAsync prepares this).
            NightDate nightDate = NightDate.Of(night, ctx.Observation.Zone);
            IReadOnlyList<double> moonAltitudes = MoonOverlay.FetchOrCompute(
                cache, nightDate, location, startUtc, count, "Sky");
            mMoonSeries = MoonOverlay.BuildSeries(
                moonAltitudes, startUtc, count, night.LunarIlluminationFraction,
                alt => SkyAxisMinMag + (alt / 90.0) * (SkyAxisMaxMag - SkyAxisMinMag), "Sky");

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
                progress?.Report((t + 1, targets.Count));
                Target target = targets[t];
                if (target == null) continue;

                Color c = ChartLayout.ResolveTargetColor(ctx.TargetColors, target, t);
                mTargetColors[target] = c;

                var series = GetOrCreateTargetSeries(target, c);
                BuildOrUpdateTargetSeries(series, target, location, startUtc, zone,
                    count, observer, kAtBand, v0,
                    ActiveFilterBandwidthNm, ActiveFilterCenterNm);

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

            ChartLayout.SwapSeriesDict(mSeriesByTarget, newSeriesByTarget);
            mChart.Series = seriesList;
            BuildLegendItems();
        }

        // Cheap path for Bortle / ExtinctionK / ActiveFilter scrubs that don't
        // change the night-window geometry, only the K-S magnitudes. Walks every
        // existing series' ObservablePoint collection in place; no series identity
        // churn. Caller must have run Render(...) at least once -- this method
        // assumes mSeriesByTarget is populated and mLastChartStartUtc / mLastCount
        // / mAxisZone carry the night-grid bounds + display zone.
        public void RefreshSkyBrightness(IChartCacheStore cache, Location location)
        {
            _ = cache;  // unused: night bounds taken from the last Render's snapshot
            if (location == null || mSeriesByTarget.Count == 0 || mLastCount <= 0) return;
            // mAxisZone is set by Render; this cheap-scrub path is documented to
            // run only after a Render. Guard anyway against a future early caller.
            if (mAxisZone == null) return;

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
                    mLastChartStartUtc, mAxisZone, mLastCount,
                    observer, kAtBand, v0,
                    ActiveFilterBandwidthNm, ActiveFilterCenterNm);
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
            mLegend.Clear();
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
            DateTime startUtc,
            TimeZoneInfo zone,
            int count,
            ObserverInfo observer,
            double kAtBand,
            double v0,
            double bandwidthNm,
            double centerNm)
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
                DateTime utc = DateTime.SpecifyKind(startUtc.AddMinutes(i), DateTimeKind.Utc);
                // Wall-clock label for the tooltip -- DST-correct per-instant.
                DateTime localForLabel = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);

                AltAz t = AltAzCalculator.At(target, location, utc);

                // Low-altitude gate: K-S 1991's dark-sky baseline goes unphysical for
                // light-polluted sites at high airmass (extinction term dominates -> sky
                // predicted darker than zenith, which is wrong for urban regimes where
                // off-axis light-pollution in-scatter actually brightens the horizon --
                // a Garstang/Falchi physics K-S doesn't model). Null-Y the sample below
                // the gate threshold with an explanatory tooltip rather than show a
                // misleading prediction.
                double? plotY;
                string tooltip;
                if (t.Altitude < KsLowAltitudeGateDeg)
                {
                    plotY = null;
                    tooltip = string.Format(CultureInfo.InvariantCulture,
                        "{0}\n{1:h:mm tt}\n(low altitude — K-S unreliable)",
                        target.Name, localForLabel);
                }
                else
                {
                    var m = MoonSeparation.ObserveAt(target, location, utc);
                    double phase = SkyBrightness.PhaseAngleDegFromAgeDays(LunarAge.DaysAt(utc));
                    double sunAlt = SunPosition.AltAzAt(location, utc).Altitude;
                    // KsAt's moonAltDeg parameter contract is APPARENT altitude
                    // (geometric + refraction lift). MoonSeparation.ObserveAt returns
                    // geometric — refraction-correct so the K-S cutoff aligns with the
                    // visually-observed moonset (~34' lift, ~2 min later than geometric
                    // moonset depending on the moon's descent rate).
                    double moonAltApparent = m.MoonAltDeg + Refraction.SaemundssonDeg(m.MoonAltDeg);

                    double mag = SkyBrightness.KsAt(
                        t.Altitude, t.Azimuth,
                        moonAltApparent, m.MoonAzDeg,
                        phase, sunAlt, kAtBand, v0, bandwidthNm, centerNm);

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
                            target.Name, localForLabel)
                        : string.Format(CultureInfo.InvariantCulture,
                            "{0}\n{1:h:mm tt}\n{2:0.0} mag/arcsec²",
                            target.Name, localForLabel, mag);
                }

                // UTC-internal X axis: plot the sample at its own UTC OADate.
                var p = new ObservablePoint(utc.ToOADate(), plotY);
                if (i < data.Count) data[i] = p;
                else data.Add(p);

                tooltips[i] = tooltip;
            }
            while (data.Count > count) data.RemoveAt(data.Count - 1);

            mTooltipText[series] = tooltips;
        }

        // Rebuild the external legend from the current target series. Filter:
        // targets not in mFitSeries (no D-hour window fits tonight under the
        // current H/D/M) are excluded, so the legend matches what's visible.
        private void BuildLegendItems()
        {
            var entries = new List<ChartLegendPanel.LegendEntry>();
            foreach (var kv in mSeriesByTarget)
            {
                Target target = kv.Key;
                LineSeries<ObservablePoint> series = kv.Value;
                if (!mFitSeries.Contains(series)) continue;
                Color color = mTargetColors.TryGetValue(target, out var c) ? c : Color.LightGray;
                entries.Add(new ChartLegendPanel.LegendEntry(
                    target.Name, color,
                    () => series.IsVisible,
                    () => series.IsVisible = !series.IsVisible));
            }
            mLegend.SetItems(entries);
        }

        public void Dispose()
        {
            mGradient.Dispose();
            mHover.Dispose();
            mContainer.Dispose();
        }
    }
}
