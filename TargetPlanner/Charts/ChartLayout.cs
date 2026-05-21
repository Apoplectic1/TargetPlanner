using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Astronomy.Core.Night;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
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
        /// <paramref name="zone"/> + the UTC start/end instants + total minute
        /// count + a cache key that uniquely identifies the resulting altitude
        /// curve. The coordinator's pipeline and <c>AltitudeSubChart_Day.Render</c>
        /// both call this so the <see cref="DayWindowKey"/> they pass to the
        /// cache is guaranteed identical.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Count</c> is derived from the UTC span (<c>EndUtc - StartUtc</c>),
        /// NOT the wall-clock span (<c>ChartStop - ChartStart</c>). On a night
        /// that crosses a DST transition the two differ by 60 minutes -- a
        /// fall-back night is one real hour longer than its wall-clock face
        /// suggests, a spring-forward night one hour shorter. The Day/Sky charts
        /// plot per-minute samples at UTC instants, so the UTC span is the
        /// correct sample count; the wall-clock span would drop (or duplicate)
        /// the transition hour. On non-DST nights the two spans are identical so
        /// <c>Count</c> -- and therefore the <see cref="DayWindowKey"/> -- is
        /// unchanged from the pre-DST-fix behaviour.
        /// </para>
        /// </remarks>
        public static (DayWindowKey Key, DateTime ChartStart, DateTime ChartStop,
                       DateTime StartUtc, DateTime EndUtc, int Count)
            BuildDayWindow(NightWindow night, TimeZoneInfo zone)
        {
            DateTime duskLocal = TimeZoneInfo.ConvertTimeFromUtc(night.AstronomicalDusk, zone);
            DateTime dawnLocal = TimeZoneInfo.ConvertTimeFromUtc(night.AstronomicalDawn, zone);
            DateTime chartStart = DayChartStart(duskLocal);
            DateTime chartStop = DayChartStop(dawnLocal);
            DateTime startUtc = LocalChartHourToUtc(chartStart, zone);
            DateTime endUtc = LocalChartHourToUtc(chartStop, zone);
            int count = Convert.ToInt32(Math.Round((endUtc - startUtc).TotalMinutes)) + 1;
            DayWindowKey key = new DayWindowKey
            {
                ChartStartUtcTicks = startUtc.Ticks,
                Count = count,
            };
            return (key, chartStart, chartStop, startUtc, endUtc, count);
        }

        // Convert a whole-hour local chart bound to its UTC instant. chartStart /
        // chartStop are evening / morning hours, so in practice they never land
        // inside the spring-forward gap (02:00-02:59 local) -- but ConvertTimeToUtc
        // throws ArgumentException on an invalid local time, and BuildDayWindow is
        // on the render hot path, so the IsInvalidTime nudge is cheap insurance.
        // Fall-back ambiguous times do NOT throw (ConvertTimeToUtc resolves them
        // deterministically to standard time), so no ambiguity guard is needed.
        private static DateTime LocalChartHourToUtc(DateTime localHour, TimeZoneInfo zone)
        {
            DateTime unspec = DateTime.SpecifyKind(localHour, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspec))
                unspec = unspec.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(unspec, zone);
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

        // --- Shared Axis factories (code-quality-audit.md Tier 4) -------------
        // The four sub-charts built near-identical Axis objects inline; these
        // factories are the single source of truth.

        // Format a UTC-OADate axis value as the site's wall-clock "h:mm tt" --
        // the shared body of the Day/Sky time-axis Labeler. The X axis is
        // UTC-internal, so ConvertTimeFromUtc evaluates DST rules per-instant.
        // A null zone (a Labeler call before the first Render) falls back to a
        // zone-blind format.
        public static string FormatZonedAxisLabel(double oaDate, TimeZoneInfo zone)
        {
            if (zone == null) return DateTime.FromOADate(oaDate).ToString("h:mm tt");
            DateTime utc = DateTime.SpecifyKind(DateTime.FromOADate(oaDate), DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("h:mm tt");
        }

        // The Day/Sky single-night time X axis: whole-hour ticks over a
        // UTC-internal OADate axis, labelled in the site's wall clock.
        // zoneAccessor is read live by the Labeler (the host updates its zone
        // field each Render). ForceStepToMin disables LC2's adaptive
        // label-skip so every whole-hour tick is labelled.
        public static Axis MakeTimeXAxis(Func<TimeZoneInfo> zoneAccessor)
            => new Axis
            {
                Labeler = v => FormatZonedAxisLabel(v, zoneAccessor()),
                UnitWidth = TimeSpan.FromHours(1).TotalDays,
                MinStep = TimeSpan.FromHours(1).TotalDays,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(GridLineColor),
            };

        // The Year/Sessions 12-month X axis: one-day UnitWidth, month-
        // abbreviation labels. Tick positions come from MonthBoundaryOADates
        // via Axis.CustomSeparators, set per Render.
        public static Axis MakeMonthXAxis()
            => new Axis
            {
                Labeler = v => DateTime.FromOADate(v).ToString("MMM", CultureInfo.InvariantCulture),
                UnitWidth = TimeSpan.FromDays(1).TotalDays,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(GridLineColor),
            };

        // The 0-90° altitude Y axis shared by Day / Year / Sessions,
        // parameterised on the axis name (the only per-chart difference).
        public static Axis MakeAltitudeYAxis(string name)
            => new Axis
            {
                Name = name,
                MinLimit = 0,
                MaxLimit = 90,
                MinStep = 10,
                ForceStepToMin = true,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(GridLineColor),
                NamePaint = new SolidColorPaint(SKColors.LightGray),
            };

        // --- Render-body shared helpers (code-quality-audit.md Tier 6) --------

        // The series-dictionary swap every sub-chart's Render does at commit:
        // replace `persistent` wholesale with the freshly-built `fresh`. When
        // `reverse` is non-null it is also populated [value] = key (NOT cleared
        // here -- the caller clears it once, so Sessions can call this three
        // times into one shared reverse map). Series identity is preserved by
        // the caller's GetOrCreate path; this is pure dictionary plumbing.
        public static void SwapSeriesDict<TVal>(
            Dictionary<Target, TVal> persistent,
            Dictionary<Target, TVal> fresh,
            Dictionary<TVal, Target> reverse = null)
        {
            persistent.Clear();
            foreach (var kv in fresh)
            {
                persistent[kv.Key] = kv.Value;
                if (reverse != null) reverse[kv.Value] = kv.Key;
            }
        }

        // The Year/Sessions month-grid bounds: snap gridStart down to its
        // first-of-month, span exactly 12 months, and lock the X axis to that
        // range with 1st-of-month CustomSeparators. Both charts ran this block
        // verbatim once the first target's yearDays gave a grid anchor.
        public static void ApplyMonthGrid(Axis xAxis, DateTime gridStart)
        {
            DateTime startMonth = gridStart.Date.AddDays(1 - gridStart.Day);
            DateTime endMonth = startMonth.AddYears(1);
            xAxis.MinLimit = startMonth.ToOADate();
            xAxis.MaxLimit = endMonth.ToOADate();
            xAxis.CustomSeparators = MonthBoundaryOADates(startMonth, 12);
        }
    }
}
