using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    public static class IntegratedQuality
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        // Integrates altitudeQuality(alt(t)) over [startUtc, startUtc + duration] using
        // composite Simpson's rule. Returns a dimensionless quantity in units of
        // (solar hours) * (quality output) -- i.e. "total quality" accumulated over the
        // session. Simpson over 20 segments is accurate to ~1e-6 for smooth altitude
        // curves; completes in microseconds per call.
        public static double OverSession(
            Target target, Location location,
            DateTime startUtc, TimeSpan duration,
            Func<double, double> altitudeQuality)
        {
            if (altitudeQuality == null) throw new ArgumentNullException(nameof(altitudeQuality));

            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;

            const int n = 20; // even
            double totalHours = duration.TotalHours;
            if (totalHours <= 0) return 0.0;
            double dt = totalHours / n;

            double lstStart = SiderealTime.Local(startUtc, lonDegEast);
            double sum = 0.0;
            for (int i = 0; i <= n; i++)
            {
                double t = i * dt;                                   // UT hours offset
                double lst = lstStart + t * SiderealHoursPerSolarDay / 24.0;
                double ha = lst - raHours;
                double alt = TargetGeometry.AltitudeAtHourAngle(ha, latDeg, decDeg);
                double q = altitudeQuality(alt);
                double w = (i == 0 || i == n) ? 1.0 : (i % 2 == 0 ? 2.0 : 4.0);
                sum += w * q;
            }
            return sum * dt / 3.0;
        }

        // Closed-form evaluation for the common quality function q(alt) = sin(alt). Exact, not
        // numerical. Using the integral:
        //   integral sin(alt(HA)) dHA = sin(phi)*sin(delta)*(HA2 - HA1)
        //                             + cos(phi)*cos(delta) * (12/pi) *
        //                               ( sin(HA2*pi/12) - sin(HA1*pi/12) )
        // with HA in sidereal hours, then converts to solar-hour-denominated result.
        public static double SinAltitudeOverSession(
            Target target, Location location,
            DateTime startUtc, TimeSpan duration)
        {
            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;

            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;

            double lstStart = SiderealTime.Local(startUtc, lonDegEast);
            double haStart = lstStart - raHours;
            double durationLst = duration.TotalHours * SiderealHoursPerSolarDay / 24.0;
            double haEnd = haStart + durationLst;

            double siderealIntegral =
                  Math.Sin(phi) * Math.Sin(delta) * (haEnd - haStart)
                + Math.Cos(phi) * Math.Cos(delta) * (12.0 / Math.PI)
                  * (Math.Sin(haEnd * Math.PI / 12.0) - Math.Sin(haStart * Math.PI / 12.0));

            // Convert sidereal-hour-based integral to solar-hour-based.
            return siderealIntegral * 24.0 / SiderealHoursPerSolarDay;
        }
    }
}
