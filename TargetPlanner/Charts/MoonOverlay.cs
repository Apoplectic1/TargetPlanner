using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Moon;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TargetPlanner.Caches;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Charts
{
    // The translucent grey moon-altitude overlay shared by the Day and Sky
    // single-night charts. The only Day/Sky difference is the Y mapping — Day
    // plots raw altitude on its 0–90° axis, Sky remaps it into its inverted
    // magnitude range — captured by the altitudeToPlotY delegate. Extracted
    // from near-identical copies in AltitudeSubChart_Day/_Sky
    // (code-quality-audit.md Tier 4).
    public static class MoonOverlay
    {
        // Build the moon overlay series fresh: a translucent grey filled area
        // whose alpha scales with lunar illumination, one point per minute
        // from startUtc (UTC-internal X axis). A below-horizon sample
        // (moonAlt < 0) gets null Y so the fill gaps; altitudeToPlotY maps a
        // non-negative altitude onto the host chart's Y axis.
        public static LineSeries<ObservablePoint> BuildSeries(
            IReadOnlyList<double> altitudes,
            DateTime startUtc,
            int count,
            double lunarIllumination,
            Func<double, double> altitudeToPlotY,
            string diagCategory)
        {
            byte alpha = (byte)Math.Min(250, Math.Max(0, (int)(lunarIllumination * 250.0)));

            int aboveHorizon = 0;
            double minAlt = double.PositiveInfinity, maxAlt = double.NegativeInfinity;
            var data = new ObservableCollection<ObservablePoint>();
            for (int i = 0; i < count; i++)
            {
                double moonAlt = altitudes[i];
                if (moonAlt > 0) aboveHorizon++;
                if (moonAlt < minAlt) minAlt = moonAlt;
                if (moonAlt > maxAlt) maxAlt = moonAlt;
                double? plotY = moonAlt < 0 ? (double?)null : altitudeToPlotY(moonAlt);
                // UTC-internal X axis: sample i is at startUtc + i minutes.
                DateTime pointUtc = startUtc.AddMinutes(i);
                data.Add(new ObservablePoint(pointUtc.ToOADate(), plotY));
            }

            var series = new LineSeries<ObservablePoint>
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

            if (Log.IsDiagEnabled(diagCategory))
            {
                Log.Diag(diagCategory,
                    $"BuildMoon illum={lunarIllumination:F3} alpha={alpha} count={count} " +
                    $"aboveHorizon={aboveHorizon} minAlt={minAlt:F2} maxAlt={maxAlt:F2} " +
                    $"startUtc={startUtc:yyyy-MM-dd HH:mm}Z");
            }
            return series;
        }

        // Day/Sky moon-altitude source: the per-NightDate cache entry, or a
        // defensive inline recompute via MoonEphemeris.Sample when the cache
        // misses (a race where Render runs before PrepareMoonAsync's await
        // settled -- logs a WARN). The caller passes the result to BuildSeries;
        // diagLabel ("Day" / "Sky") tags the cache-miss WARN. Pulls geometric
        // moon altitudes from MoonSample to match the legacy double-altitude
        // shape consumers expect.
        public static IReadOnlyList<double> FetchOrCompute(
            IChartCacheStore cache, NightDate nightDate, Location location,
            DateTime startUtc, int count, string diagLabel)
        {
            IReadOnlyList<MoonSample> samples = cache?.GetMoonOrNull(nightDate)?.Samples;
            if (samples != null && samples.Count == count)
            {
                double[] altitudes = new double[count];
                for (int i = 0; i < count; i++) altitudes[i] = samples[i].AltDegGeometric;
                return altitudes;
            }
            Log.Warn($"{diagLabel} moon cache miss; inline fallback " +
                $"(count={count}, cached={samples?.Count ?? -1})");
            return ComputeAltitudesInline(location, startUtc, count);
        }

        // Defensive fallback when the moon cache misses. Calls the same
        // MoonEphemeris.Sample primitive that ChartCacheStore.BuildMoonEphemerisAsync
        // uses so the result is byte-identical to the cached version. Returns
        // geometric altitudes (drops the rest of the MoonSample fields the
        // legacy double-altitude caller doesn't read).
        private static IReadOnlyList<double> ComputeAltitudesInline(
            Location location, DateTime startUtc, int count)
        {
            IReadOnlyList<MoonSample> samples = MoonEphemeris.Sample(
                location, startUtc, TimeSpan.FromMinutes(1), count);
            double[] altitudes = new double[count];
            for (int i = 0; i < count; i++) altitudes[i] = samples[i].AltDegGeometric;
            return altitudes;
        }
    }
}
