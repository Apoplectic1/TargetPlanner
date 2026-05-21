using System;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Cache key for per-(target, single-night) altitude curves. Identifies the
    /// minute-spaced sampling window that <see cref="Astronomy.Core.AltitudeCurve"/>
    /// would produce, so a single (Target, DayWindowKey) entry uniquely names
    /// the altitude data shown on the Day chart for one night.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built by <c>ChartLayout.BuildDayWindow(NightWindow)</c>, which is the
    /// single place the dusk/dawn → chart-bounds rounding lives. Both the
    /// coordinator's pipeline (when it calls <see cref="IChartCacheStore.PrepareDayAsync"/>)
    /// and <c>AltitudeSubChart_Day.Render</c> (when it calls <see cref="IChartCacheStore.GetDayOrNull"/>)
    /// go through that helper, so the build-time key and the render-time key
    /// can't diverge.
    /// </para>
    /// <para>
    /// Comparing by ticks + count rather than by DateTime keeps the equality
    /// fence cheap (two longs) and avoids the DateTime-Kind comparison trap.
    /// </para>
    /// </remarks>
    public readonly struct DayWindowKey : IEquatable<DayWindowKey>
    {
        public long ChartStartUtcTicks { get; init; }
        public int Count { get; init; }

        /// <summary>The chart-window start instant as a UTC <see cref="DateTime"/>.
        /// The key stores ticks (cheap equality); consumers that need the
        /// <see cref="DateTime"/> read this rather than re-wrapping with the
        /// <see cref="DateTimeKind.Utc"/> argument each time.</summary>
        public DateTime ChartStartUtc => new DateTime(ChartStartUtcTicks, DateTimeKind.Utc);

        public bool Equals(DayWindowKey other) =>
            ChartStartUtcTicks == other.ChartStartUtcTicks
            && Count == other.Count;

        public override bool Equals(object obj) => obj is DayWindowKey k && Equals(k);

        public override int GetHashCode() => HashCode.Combine(ChartStartUtcTicks, Count);

        public static bool operator ==(DayWindowKey a, DayWindowKey b) => a.Equals(b);
        public static bool operator !=(DayWindowKey a, DayWindowKey b) => !a.Equals(b);
    }
}
