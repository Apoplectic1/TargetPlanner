using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core
{
    public static class AltAz
    {
        // Altitude (degrees) and azimuth (degrees from North, clockwise) of target seen from
        // location at the given UTC instant. Signed hemispheres are resolved internally from
        // the target.North / location.North / location.West flags.
        public static Tuple<double, double> At(Target target, Location location, DateTime utc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            double raHours = target.RightAscension;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;

            double lst = SiderealTime.Local(utc, lonDegEast);
            double hourAngle = lst - raHours;

            double altitude = TargetGeometry.AltitudeAtHourAngle(hourAngle, latDeg, decDeg);
            double azimuth  = TargetGeometry.AzimuthAtHourAngle(hourAngle, latDeg, decDeg);
            return Tuple.Create(altitude, azimuth);
        }

        // Overload that reads the UTC instant from location.DateTime.ToUniversalTime().
        // Preserves the call pattern from the pre-extraction code that mutates
        // locationClone.DateTime between calls.
        public static Tuple<double, double> Of(Target target, Location location)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            return At(target, location, location.DateTime.ToUniversalTime());
        }
    }
}
