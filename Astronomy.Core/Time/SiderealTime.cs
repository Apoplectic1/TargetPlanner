using System;

namespace Astronomy.Core.Time
{
    public static class SiderealTime
    {
        // Greenwich Mean Sidereal Time in hours [0, 24) at the given Julian Date.
        // USNO one-liner form: GMST(0h UT) + 1.00273790935 * (elapsed UT hours).
        public static double Greenwich(double julianDate)
        {
            double D = julianDate - 2451545.0;
            double gmst = 18.697374558 + 24.06570982441908 * D;
            return gmst - 24.0 * Math.Floor(gmst / 24.0);
        }

        // Local Sidereal Time in hours [0, 24) at the given UTC instant and east-positive
        // longitude in degrees.
        public static double Local(DateTime utc, double longitudeDegEast)
        {
            double jd = JulianDate.FromUtc(utc);
            double lst = Greenwich(jd) + longitudeDegEast / 15.0;
            return lst - 24.0 * Math.Floor(lst / 24.0);
        }
    }
}
