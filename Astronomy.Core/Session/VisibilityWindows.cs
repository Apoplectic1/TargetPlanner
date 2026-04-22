using System;
using System.Collections.Generic;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Intersects a stellar target's above-horizon arcs with the night window, returning the
    /// contiguous UTC intervals where the target is both above the horizon profile and
    /// between astronomical dusk and dawn.
    /// </summary>
    public static class VisibilityWindows
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        /// <summary>
        /// Returns 0-2 contiguous UTC intervals where the target is visible during the given
        /// night. Zero windows means never above horizon during the night; one is the usual
        /// case; two arises when the target rises, sets, and rises again before dawn (shifted
        /// transits <c>k = -1</c> and <c>k = +1</c>).
        /// </summary>
        /// <remarks>
        /// Currently uses <see cref="IHorizonProfile.MinAltitude"/> as a scalar fast-path --
        /// treating the profile as flat at its minimum. An azimuth-aware refinement is
        /// pending. For targets whose visibility is determined by ridges, trees, or
        /// buildings with sharp azimuth features, the current result is a conservative lower
        /// bound on visible time (the target is at least above <c>MinAltitude</c> during the
        /// reported windows; it may clear the full profile for longer or shorter, depending
        /// on azimuth variation).
        /// </remarks>
        /// <returns>
        /// Intervals as <c>(Start, End)</c> tuples, both <see cref="DateTimeKind.Utc"/>.
        /// Empty list if the target never clears the horizon, never rises, or if the night
        /// window is invalid (polar day / polar night).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<(DateTime Start, DateTime End)> For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));

            var result = new List<(DateTime Start, DateTime End)>();

            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;
            double horizonDeg = horizon.MinAltitude;

            double haHorizon = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, horizonDeg);
            if (double.IsNaN(haHorizon)) return result; // never reaches horizon

            if (!night.IsValid) return result;

            // NightWindow exposes AstronomicalDusk / AstronomicalDawn as Kind=Utc. No
            // conversion needed here -- see NightCalculator for the offset-recovery rationale.
            DateTime duskUtc = night.AstronomicalDusk;
            DateTime dawnUtc = night.AstronomicalDawn;

            double lstDusk = SiderealTime.Local(duskUtc, lonDegEast);
            double lstDawn = SiderealTime.Local(dawnUtc, lonDegEast);
            if (lstDawn < lstDusk) lstDawn += 24.0;

            if (double.IsPositiveInfinity(haHorizon))
            {
                // Circumpolar above horizon: full night is one visibility window.
                result.Add((duskUtc, dawnUtc));
                return result;
            }

            double solarPerSidereal = 24.0 / SiderealHoursPerSolarDay;
            for (int k = -1; k <= 1; k++)
            {
                double center  = raHours + 24.0 * k;
                double ahStart = center - haHorizon;
                double ahEnd   = center + haHorizon;
                double s = Math.Max(lstDusk, ahStart);
                double e = Math.Min(lstDawn, ahEnd);
                if (s >= e) continue;

                DateTime startUtc = duskUtc.AddHours((s - lstDusk) * solarPerSidereal);
                DateTime endUtc   = duskUtc.AddHours((e - lstDusk) * solarPerSidereal);
                result.Add((startUtc, endUtc));
            }

            return result;
        }
    }
}
