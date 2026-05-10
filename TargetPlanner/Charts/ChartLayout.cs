using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

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

        // Night-grid bounds for the single-night charts (Day, Sky). Both pad
        // outward from dusk/dawn to the nearest enclosing integer hour so dusk
        // and dawn never coincide with an X-axis edge label.
        //
        // Start = the integer hour mark strictly before duskLocal.
        // Stop  = the integer hour mark strictly past dawnLocal.
        // If dusk/dawn lands exactly on an hour the bound steps one full hour
        // outward.
        public static DateTime DayChartStart(DateTime duskLocal)
        {
            DateTime start = duskLocal.Date.AddHours(duskLocal.Hour);
            if (start >= duskLocal) start = start.AddHours(-1);
            return start;
        }

        public static DateTime DayChartStop(DateTime dawnLocal)
        {
            DateTime stop = dawnLocal.Date.AddHours(dawnLocal.Hour);
            if (stop <= dawnLocal) stop = stop.AddHours(1);
            return stop;
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
