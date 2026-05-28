using System;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// One per-minute moon sample captured during the year-day moon-clear sweep
    /// (target-moon separation + moon altitude) inside <see cref="NightCacheEntry"/>.
    /// Distinct from the canonical <c>Astronomy.Core.Moon.MoonSample</c> which
    /// carries the full moon state (AltAz + age + phase + illumination) for
    /// downstream brightness / gate consumers.
    /// </summary>
    /// <remarks>
    /// Sampled at 1-minute cadence between Dusk and Dawn for each per-target
    /// <see cref="NightCacheEntry"/>. Profile-independent: consumers walk the samples
    /// at render time and apply the active <see cref="Astronomy.Core.Moon.MoonAvoidanceProfile"/>
    /// to compute moon-clear intervals. Storing raw samples (instead of pre-evaluated
    /// rejection booleans) keeps the cache profile-independent so profile scrubs avoid
    /// re-hitting the underlying primitives.
    /// </remarks>
    public readonly struct MoonSweepSample
    {
        /// <summary>Sample instant. <see cref="DateTimeKind.Utc"/>.</summary>
        public DateTime Utc { get; }

        /// <summary>Topocentric target-moon separation in degrees, range [0, 180].</summary>
        public double SepDeg { get; }

        /// <summary>Moon altitude in degrees, range [-90, +90].</summary>
        public double MoonAltDeg { get; }

        public MoonSweepSample(DateTime utc, double sepDeg, double moonAltDeg)
        {
            Utc = utc;
            SepDeg = sepDeg;
            MoonAltDeg = moonAltDeg;
        }
    }
}
