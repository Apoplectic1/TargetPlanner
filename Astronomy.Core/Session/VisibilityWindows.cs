using System;
using System.Collections.Generic;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    public static class VisibilityWindows
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        // Intersects the target's above-horizon arcs with the night window and returns the 0-2
        // contiguous UTC intervals where the target is both above the horizon profile and
        // between astronomical dusk and dawn.
        //
        // Phase 5 uses horizon.MinAltitude as a scalar fast-path -- treating the profile as flat
        // at its minimum. Phase 6 will introduce an azimuth-aware refinement. For targets whose
        // visibility is determined by ridges, trees, or buildings with sharp azimuth features,
        // the current result is a conservative lower bound on visible time (the target is at
        // least above MinAltitude during the reported windows; it may clear the full profile
        // for longer or shorter, depending on azimuth variation).
        public static IReadOnlyList<(DateTime Start, DateTime End)> For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon)
        {
            var result = new List<(DateTime Start, DateTime End)>();

            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;
            double horizonDeg = horizon.MinAltitude;

            double haHorizon = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, horizonDeg);
            if (double.IsNaN(haHorizon)) return result; // never reaches horizon

            if (night.AstronomicalDusk == DateTime.MinValue ||
                night.AstronomicalDawn == DateTime.MinValue) return result;

            DateTime duskUtc = night.AstronomicalDusk.ToUniversalTime();
            DateTime dawnUtc = night.AstronomicalDawn.ToUniversalTime();

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
