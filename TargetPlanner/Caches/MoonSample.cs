using System;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// One moon-position observation captured during cache build, used at render time
    /// by the moon-aware curves (Day-chart HD overlay, Sessions-chart placement).
    /// </summary>
    /// <remarks>
    /// Sampled at 10-minute cadence between Dusk and Dawn for each per-target
    /// <see cref="NightCacheEntry"/>. Profile-independent: consumers walk the samples
    /// at render time and apply the active <see cref="Astronomy.Core.Moon.MoonAvoidanceProfile"/>
    /// to compute moon-clear intervals. Storing raw samples (instead of pre-evaluated
    /// rejection booleans) keeps the cache profile-independent so profile scrubs avoid
    /// re-hitting CoordinateSharp.
    /// </remarks>
    public readonly struct MoonSample
    {
        /// <summary>Sample instant. <see cref="DateTimeKind.Utc"/>.</summary>
        public DateTime Utc { get; }

        /// <summary>Topocentric target-moon separation in degrees, range [0, 180].</summary>
        public double SepDeg { get; }

        /// <summary>Moon altitude in degrees, range [-90, +90].</summary>
        public double MoonAltDeg { get; }

        public MoonSample(DateTime utc, double sepDeg, double moonAltDeg)
        {
            Utc = utc;
            SepDeg = sepDeg;
            MoonAltDeg = moonAltDeg;
        }
    }
}
