using System;
using System.Collections.Generic;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// One per-target per-night cache entry. Holds the geometric and lunar precomputes
    /// that <see cref="TargetPlanner.Charts.AltitudeSeries"/> reads at render time
    /// instead of recomputing per spinner scrub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="TargetCacheEntry"/> contains an array of these spanning the
    /// 365-day window seeded from the target's <see cref="Astronomy.Core.Locations.Location"/>.
    /// Index <c>i</c> corresponds to <c>NightCache.YearStartDay.AddDays(i)</c>.
    /// </para>
    /// <para>
    /// Promoted from a private nested struct in <c>AltitudeSeries</c> to a public class in
    /// the <c>TargetPlanner.Caches</c> namespace as part of Phase 3 of the SoC refactor.
    /// Class (not struct) so members can be mutated during cache build before the entry
    /// is published; once the parent <see cref="TargetCacheEntry"/> is in the cache
    /// dictionary, treat it as immutable.
    /// </para>
    /// </remarks>
    public sealed class NightCacheEntry
    {
        /// <summary>Astronomical dusk for this night, <see cref="DateTimeKind.Utc"/>.</summary>
        public DateTime Dusk;

        /// <summary>Astronomical dawn for this night, <see cref="DateTimeKind.Utc"/>.</summary>
        public DateTime Dawn;

        /// <summary>Local Sidereal Time at dusk, hours.</summary>
        public double LstDusk;

        /// <summary>Local Sidereal Time at dawn, hours. Always &gt;= <see cref="LstDusk"/>.</summary>
        public double LstDawn;

        /// <summary>Target altitude at dusk, degrees.</summary>
        public double AltDusk;

        /// <summary>Target altitude at dawn, degrees.</summary>
        public double AltDawn;

        /// <summary>True if the target's transit (LST = RA) falls within the night window.</summary>
        public bool TransitInNight;

        /// <summary>Maximum altitude reached during the night, degrees.</summary>
        public double YearAlt;

        /// <summary>True for polar day / polar night nights where <see cref="Astronomy.Core.Night.NightWindow.IsValid"/> was false.</summary>
        public bool IsPolar;

        /// <summary>X-axis coordinate used for Year/Optimal series points (DateTime ticks).</summary>
        public DateTime SentinelX;

        /// <summary>Per-night moon samples at 10-minute cadence between Dusk and Dawn.</summary>
        public IReadOnlyList<MoonSample> MoonSamples;

        /// <summary>Lunar age (days since most recent new moon) at the night's midpoint.</summary>
        public double MoonAgeDays;
    }
}
