using System.Collections.Generic;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-<see cref="DayWindowKey"/> cached minute-by-minute moon altitude curve
    /// for the cache's current <c>CurrentLocation</c>. Singleton per night (the
    /// moon is not target-keyed) — the cache clears it on
    /// <see cref="IChartCacheStore.SetLocationAsync"/> alongside the per-target
    /// dicts, so a location swap invalidates the moon entry too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AltitudesPerMinute"/> has exactly <see cref="DayWindowKey.Count"/>
    /// entries; entry <c>i</c> is the moon's geometric altitude (degrees, pre-
    /// refraction) at <c>ChartStartUtcTicks + i minutes</c>. Callers that need
    /// apparent altitude (e.g. K-S sky brightness, which gates on visual moonset)
    /// should apply <see cref="Astronomy.Core.Astrometry.Refraction.SaemundssonDeg"/>
    /// to convert from geometric to apparent.
    /// </para>
    /// <para>
    /// Owned by <see cref="ChartCacheStore"/>; published immutable.
    /// </para>
    /// </remarks>
    public sealed class MoonAltitudeEntry
    {
        public DayWindowKey Key { get; }
        public IReadOnlyList<double> AltitudesPerMinute { get; }

        public MoonAltitudeEntry(DayWindowKey key, IReadOnlyList<double> altitudesPerMinute)
        {
            Key = key;
            AltitudesPerMinute = altitudesPerMinute;
        }
    }
}
