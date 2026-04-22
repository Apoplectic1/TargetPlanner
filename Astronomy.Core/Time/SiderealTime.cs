using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Greenwich and Local Sidereal Time derivations from a UTC instant.
    /// </summary>
    public static class SiderealTime
    {
        /// <summary>
        /// Greenwich Mean Sidereal Time in hours <c>[0, 24)</c> at the given Julian Date.
        /// USNO one-liner form: <c>GMST(0h UT) + 1.00273790935 * (elapsed UT hours)</c>.
        /// </summary>
        public static double Greenwich(double julianDate)
        {
            double D = julianDate - 2451545.0;
            double gmst = 18.697374558 + 24.06570982441908 * D;
            return gmst - 24.0 * Math.Floor(gmst / 24.0);
        }

        /// <summary>
        /// Local Sidereal Time in hours <c>[0, 24)</c> at the given UTC instant and
        /// east-positive longitude in degrees.
        /// </summary>
        /// <param name="utc">Instant to evaluate. Must be UTC -- callers that hold a
        /// <see cref="Astronomy.Core.Locations.Location"/> with a non-UTC
        /// <see cref="Astronomy.Core.Locations.Location.DateTime"/> should
        /// <c>.ToUniversalTime()</c> first.</param>
        /// <param name="longitudeDegEast">Longitude in decimal degrees, east-positive (so a
        /// western-hemisphere longitude is negative).</param>
        public static double Local(DateTime utc, double longitudeDegEast)
        {
            double jd = JulianDate.FromUtc(utc);
            double lst = Greenwich(jd) + longitudeDegEast / 15.0;
            return lst - 24.0 * Math.Floor(lst / 24.0);
        }
    }
}
