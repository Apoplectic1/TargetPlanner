using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Integrates a caller-supplied altitude-quality function over a session window. Used by
    /// schedulers that want to rank candidate sessions by "total quality accumulated" rather
    /// than by a single instantaneous altitude.
    /// </summary>
    public static class IntegratedQuality
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        /// <summary>
        /// Integrates <paramref name="altitudeQuality"/><c>(alt(t))</c> over
        /// <c>[startUtc, startUtc + duration]</c> using composite Simpson's rule.
        /// </summary>
        /// <remarks>
        /// Returns a dimensionless quantity in units of <c>(solar hours) * (quality output)</c>
        /// -- i.e. "total quality" accumulated over the session. Simpson over 20 segments is
        /// accurate to ~1e-6 for smooth altitude curves; completes in microseconds per call.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="startUtc">Session start, UTC.</param>
        /// <param name="duration">Session length. Non-positive durations return 0.</param>
        /// <param name="altitudeQuality">
        /// Maps altitude (degrees) to a dimensionless "quality" score. Caller-owned semantics
        /// -- e.g. <c>alt =&gt; Math.Sin(alt * Math.PI / 180)</c> for airmass-weighted quality.
        /// Must be finite for altitudes in [-90, 90]; NaN / infinite values corrupt the
        /// integral silently.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="altitudeQuality"/> is <see langword="null"/>.
        /// </exception>
        public static double OverSession(
            Target target, Location location,
            DateTime startUtc, TimeSpan duration,
            Func<double, double> altitudeQuality)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
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

        /// <summary>
        /// Closed-form evaluation of <see cref="OverSession"/> for the common quality
        /// function <c>q(alt) = sin(alt)</c>. Exact (not numerical).
        /// </summary>
        /// <remarks>
        /// Uses the analytic integral
        /// <c>integral sin(alt(HA)) dHA = sin(phi)*sin(delta)*(HA2 - HA1) + cos(phi)*cos(delta)*(12/pi)*(sin(HA2*pi/12) - sin(HA1*pi/12))</c>,
        /// with <c>HA</c> in sidereal hours, then converts to solar-hour-denominated result.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double SinAltitudeOverSession(
            Target target, Location location,
            DateTime startUtc, TimeSpan duration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

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
