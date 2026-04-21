using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core
{
    // Topocentric horizontal coordinates: altitude (degrees above the mathematical horizon,
    // 0 = horizon, positive = up; never adjusted for refraction) and azimuth (degrees from
    // North, clockwise, [0, 360)). Replaces the previous `Tuple<double, double>` return
    // from AltAz.At/Of so callers unpack by meaning (altitude / azimuth) instead of by
    // position (Item1 / Item2).
    public readonly struct AltAz
    {
        public double Altitude { get; }
        public double Azimuth  { get; }

        public AltAz(double altitude, double azimuth)
        {
            Altitude = altitude;
            Azimuth = azimuth;
        }

        public void Deconstruct(out double altitude, out double azimuth)
        {
            altitude = Altitude;
            azimuth  = Azimuth;
        }
    }

    // Static helper: produces an AltAz for a target seen from a location at a given UTC
    // instant. Signed hemispheres are resolved internally from the target.North /
    // location.North / location.West flags so callers pass the unsigned magnitudes.
    public static class AltAzCalculator
    {
        public static AltAz At(Target target, Location location, DateTime utc)
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
            return new AltAz(altitude, azimuth);
        }

        // Overload that reads the UTC instant from location.DateTime.ToUniversalTime().
        // Accepts location.DateTime with any DateTimeKind: Local and Unspecified are treated
        // as local and converted via Windows rules; Utc is a no-op. So callers that want to
        // evaluate at a NightWindow-sourced instant (Kind=Utc, see NightCalculator) can pass
        // it through Location.With(dateTime: night.AstronomicalDusk) without first converting.
        public static AltAz Of(Target target, Location location)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            return At(target, location, location.DateTime.ToUniversalTime());
        }
    }
}
