using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    public static class TransitTime
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        // Returns the first UTC instant at or after searchFromUtc when the target transits
        // (crosses the local meridian, HA = 0) as seen from the given location. Assumes stellar
        // fixed RA/Dec. Inverts LST(t) = RA analytically in one step -- no numerical root
        // finding, constant cost.
        public static DateTime UtcAtOrAfter(Target target, Location location, DateTime searchFromUtc)
        {
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;

            double lstRef = SiderealTime.Local(searchFromUtc, lonDegEast);
            double deltaLst = raHours - lstRef;
            while (deltaLst <   0.0) deltaLst += 24.0;
            while (deltaLst >= 24.0) deltaLst -= 24.0;

            // Advance UT by the solar-hour equivalent of deltaLst sidereal hours.
            double deltaUtHours = deltaLst * 24.0 / SiderealHoursPerSolarDay;
            return searchFromUtc.AddHours(deltaUtHours);
        }
    }
}
