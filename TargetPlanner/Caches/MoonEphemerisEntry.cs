using System.Collections.Generic;
using Astronomy.Core.Moon;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-<see cref="NightDate"/> cached moon ephemeris: minute-by-minute moon
    /// state (topocentric AltAz + distance + age + phase + illumination) for the
    /// chart-visible window of one night. Target-independent — shared across all
    /// targets at the same site on the same night.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Index <c>i</c> is the sample at
    /// <c>Window.ChartStartUtc + i * 1 minute</c>; <see cref="Samples"/> has
    /// exactly <c>Window.Count</c> entries.
    /// </para>
    /// <para>
    /// Owned by <see cref="ChartCacheStore"/>; published immutable.
    /// </para>
    /// </remarks>
    public sealed class MoonEphemerisEntry
    {
        public NightDate Key { get; }
        public DayWindowKey Window { get; }
        public IReadOnlyList<MoonSample> Samples { get; }

        public MoonEphemerisEntry(NightDate key, DayWindowKey window, IReadOnlyList<MoonSample> samples)
        {
            Key = key;
            Window = window;
            Samples = samples;
        }
    }
}
