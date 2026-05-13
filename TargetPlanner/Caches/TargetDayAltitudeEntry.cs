using System.Collections.Generic;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-(target, <see cref="DayWindowKey"/>) cached minute-by-minute altitude
    /// curve. Drives the Day chart's per-target altitude polyline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AltitudesPerMinute"/> has exactly <see cref="DayWindowKey.Count"/>
    /// entries; entry <c>i</c> is the target's altitude (degrees above the
    /// mathematical horizon) at <c>ChartStartUtcTicks + i minutes</c>. The
    /// caller paints these against minute-spaced X positions starting at the
    /// chart's local time bound.
    /// </para>
    /// <para>
    /// Owned by <see cref="ChartCacheStore"/>; published immutable.
    /// </para>
    /// </remarks>
    public sealed class TargetDayAltitudeEntry
    {
        public Target Target { get; }
        public DayWindowKey Key { get; }
        public IReadOnlyList<double> AltitudesPerMinute { get; }

        public TargetDayAltitudeEntry(Target target, DayWindowKey key, IReadOnlyList<double> altitudesPerMinute)
        {
            Target = target;
            Key = key;
            AltitudesPerMinute = altitudesPerMinute;
        }
    }
}
