using System;
using System.Collections.Generic;
using System.Drawing;
using Astronomy.Core.Night;
using SkiaSharp;
using TargetPlanner.Caches;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    // Shared plot-area template + theme for the four LC2 sub-charts (Day / Sky /
    // Year / Sessions). One source of truth so sub-charts agree on plot-area
    // pixel position, legend chrome, and per-target colors -- toggling radios
    // therefore does not shift the plot area.
    //
    // Phase 4 of the chart migration consumes these values from every
    // AltitudeSubChart_*.cs renderer. The legacy MS Charts AltitudeChart also
    // pulls TargetColorPalette from here so per-target colors stay stable across
    // the migration; once PR4e drops MS Charts entirely, the legacy file goes
    // and this class becomes LC2-only.
    public static class ChartLayout
    {
        // Plot-area template. The plot rectangle is locked to these pixel
        // dimensions on every sub-chart; the chart's total height is
        // ChartFixedHeight (top chrome + plot + x-axis labels). Legend lives
        // outside the chart in a sibling FlowLayoutPanel and grows the
        // container's IdealHeight as rows wrap.
        public const int FixedPlotAreaHeight = 420;

        // Left chrome holds the rotated Y-axis Name + tick labels + breathing
        // room. Bottom chrome holds the X-axis tick labels only -- the legend
        // is external. Right chrome is sized so half of the rightmost X-axis
        // label fits past the last tick without clipping at the chart edge --
        // most visible on Day / Sky where the rightmost time label (e.g.
        // "5:00 AM") would otherwise truncate.
        public const int LeftChromePx = 96;
        public const int RightChromePx = 40;
        public const int TopChromePx = 20;
        public const int XAxisLabelHeightPx = 44;

        // Total chart height that keeps the plot area at FixedPlotAreaHeight,
        // with axis chrome top and bottom. Constant -- the legend lives outside
        // the chart so chart total height never changes; only the container's
        // IdealHeight grows when legend rows wrap.
        public const int ChartFixedHeight =
            TopChromePx + FixedPlotAreaHeight + XAxisLabelHeightPx;

        // Sub-second nudge applied to time-axis MinLimit / MaxLimit (Day, Sky)
        // so LC2's Ceil/Floor-based edge-tick math reliably places the leftmost
        // and rightmost hour labels. Without it, floating-point precision in
        // DateTime.ToOADate() occasionally tips Ceil(MinLimit/step) up by one
        // full step (silently dropping the leftmost hour label) or Floor(MaxLimit
        // /step) down (silently dropping the rightmost). 1 ms is far below any
        // human-visible precision -- the chart still appears to start/end on the
        // whole hour. Gradient sections stay anchored at the exact hour bounds.
        public const double LabelEdgeEpsilonDays = 1.0 / 86400000.0; // 1 millisecond

        // Legend (external, below chart in a FlowLayoutPanel) styling.
        public const int LegendRowHeightPx = 22;
        public const int LegendTopPaddingPx = 6;
        public const int LegendBottomPaddingPx = 6;

        // Dark grey chart background. Matches the legacy MS Charts ChartArea
        // background so toggling between legacy + LC2 areas during the
        // migration does not flash a different colour.
        public static readonly Color ChartBackground = Color.FromArgb(70, 70, 70);

        // Light grid lines that read against the ChartBackground without
        // competing with the per-target curves. SkiaSharp colour because LC2
        // axes consume SolidColorPaint(SKColor).
        public static readonly SKColor GridLineColor = new SKColor(180, 180, 180, 90);

        // Stable colour palette for target series. Picked for readability
        // against the dark grey background and distinctness from the red
        // now-line, green horizon line, and yellow dusk/dawn gradient.
        // Assignment is by target index (positions 0..N-1 map to palette[i % N]);
        // with 12 entries, wrap-around only hits on very large target sets.
        //
        // Explicit colours opt out of the legacy framework's auto-palette,
        // which assigned a colour to every Color.Empty series in the order they
        // appear in mChart.Series and skipped series with explicit colours --
        // toggling one to Transparent (the hide-via-legend behaviour) shifted
        // every remaining Empty series one slot down the palette, visibly
        // reshuffling colours. Concrete per-target colours opt out of that
        // entirely. LC2 has no auto-palette so the same array is consumed
        // directly there.
        public static readonly Color[] TargetColorPalette = new[]
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

        // Minimum gradient width in minutes (= 0.1 hour). When the natural
        // dusk/dawn gradient between the integer-hour edge and dusk/dawn would
        // be thinner than this, the chart bound steps another hour outward so
        // the gradient band remains visible. Lower values keep the chart tight
        // around the actual night at the cost of occasionally-thin gradients;
        // higher values widen the chart more often.
        private const int MinGradientMinutes = 6;

        // Night-grid bounds for the single-night charts (Day, Sky). Both pad
        // outward from dusk/dawn to the nearest enclosing integer hour so dusk
        // and dawn never coincide with an X-axis edge label.
        //
        // Start = the integer hour mark strictly before duskLocal.
        // Stop  = the integer hour mark strictly past dawnLocal.
        // If dusk/dawn lands exactly on an hour the bound steps one full hour
        // outward.
        //
        // Visual minimum: if the resulting gradient (start->dusk or dawn->stop)
        // would be < MinGradientMinutes, step out one more hour so the
        // dusk-to-fully-dark (and dawn-from-fully-dark) gradient remains
        // readable. Each side is evaluated independently -- both ends can
        // expand on the same night if both gradients fall below the threshold.
        public static DateTime DayChartStart(DateTime duskLocal)
        {
            // Floor to the top of the current hour, preserving DateTimeKind.
            DateTime topOfHour = new DateTime(duskLocal.Year, duskLocal.Month, duskLocal.Day,
                                              duskLocal.Hour, 0, 0, duskLocal.Kind);

            // duskLocal.Minute is the gradient width on the dusk side: keep the
            // current hour as start when the gradient is at least the threshold.
            if (duskLocal.Minute >= MinGradientMinutes) return topOfHour;

            // Gradient too thin, roll back an hour to widen it.
            return topOfHour.AddHours(-1);
        }

        public static DateTime DayChartStop(DateTime dawnLocal)
        {
            // Floor to the top of the current hour, preserving DateTimeKind.
            DateTime topOfHour = new DateTime(dawnLocal.Year, dawnLocal.Month, dawnLocal.Day,
                                              dawnLocal.Hour, 0, 0, dawnLocal.Kind);
            DateTime nextHour = topOfHour.AddHours(1);

            // (60 - dawnLocal.Minute) is the gradient width on the dawn side:
            // use nextHour as stop when the gradient meets the threshold.
            if (dawnLocal.Minute <= 60 - MinGradientMinutes) return nextHour;

            // Gradient too thin, roll forward an hour to widen it.
            return nextHour.AddHours(1);
        }

        /// <summary>
        /// Convert a <see cref="NightWindow"/> to the Day chart's minute-spaced
        /// sampling window: rounded chart bounds in the location's
        /// <paramref name="zone"/> + the UTC start + total minute count +
        /// a cache key that uniquely identifies the resulting altitude curve.
        /// The coordinator's pipeline and <c>AltitudeSubChart_Day.Render</c>
        /// both call this so the <see cref="DayWindowKey"/> they pass to the
        /// cache is guaranteed identical.
        /// </summary>
        public static (DayWindowKey Key, DateTime ChartStart, DateTime ChartStop,
                       DateTime StartUtc, int Count)
            BuildDayWindow(NightWindow night, TimeZoneInfo zone)
        {
            DateTime duskLocal = TimeZoneInfo.ConvertTimeFromUtc(night.AstronomicalDusk, zone);
            DateTime dawnLocal = TimeZoneInfo.ConvertTimeFromUtc(night.AstronomicalDawn, zone);
            DateTime chartStart = DayChartStart(duskLocal);
            DateTime chartStop = DayChartStop(dawnLocal);
            int totalMins = Convert.ToInt32(Math.Round((chartStop - chartStart).TotalMinutes));
            int count = totalMins + 1;
            DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(chartStart, DateTimeKind.Unspecified), zone);
            DayWindowKey key = new DayWindowKey
            {
                ChartStartUtcTicks = startUtc.Ticks,
                Count = count,
            };
            return (key, chartStart, chartStop, startUtc, count);
        }

        // Look up <paramref name="target"/>'s color from <paramref name="colorMap"/>
        // (the MainForm-owned KnownTargets-keyed dict threaded through ChartContext);
        // fall back to <c>palette[fallbackIndex % len]</c> when the map is null
        // (early-init before NINA load) or the target isn't in it (transient
        // RA/Dec-typed targets that aren't part of KnownTargets). Used by every
        // sub-chart's Render so colors stay consistent across charts even when
        // their iteration order diverges (sort change between Renders).
        public static Color ResolveTargetColor(
            IReadOnlyDictionary<Target, Color> colorMap,
            Target target,
            int fallbackIndex)
        {
            if (target != null && colorMap != null
                && colorMap.TryGetValue(target, out Color c)) return c;
            return TargetColorPalette[fallbackIndex % TargetColorPalette.Length];
        }

        // Year / Sessions x-axis tick positions: one per first-of-month boundary
        // covering monthCount months starting at startMonth (which must already be a
        // first-of-month at midnight; NightCache.ComputeYearStartDay returns that
        // shape today). Returns monthCount + 1 OADate values so the axis can show
        // both the leading and trailing month boundary (Jan 1 ... Jan 1 next year
        // for monthCount = 12). LiveCharts2 consumes these directly via
        // Axis.CustomSeparators -- exact 1st-of-month placement, no drift over the
        // year regardless of variable month length.
        public static double[] MonthBoundaryOADates(DateTime startMonth, int monthCount)
        {
            if (monthCount < 0)
                throw new ArgumentOutOfRangeException(nameof(monthCount));
            double[] separators = new double[monthCount + 1];
            for (int i = 0; i <= monthCount; i++)
                separators[i] = startMonth.AddMonths(i).ToOADate();
            return separators;
        }
    }
}
