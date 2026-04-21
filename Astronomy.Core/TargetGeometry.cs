using System;

namespace Astronomy.Core
{
    /// <summary>
    /// Pure-geometry functions for a stellar target's altitude and azimuth at arbitrary hour
    /// angle, plus the transit / lower-culmination / horizon-crossing primitives that are
    /// exact analytic consequences of those two coordinates.
    /// </summary>
    /// <remarks>
    /// All inputs are <em>signed</em> degrees -- the caller resolves the hemisphere flag
    /// (<c>latDeg = location.North ? +location.Latitude : -location.Latitude</c>, same for
    /// declination). <see cref="AltAzCalculator.At"/> is the canonical resolution idiom.
    /// </remarks>
    public static class TargetGeometry
    {
        /// <summary>
        /// Upper-transit altitude (hour angle = 0) in degrees. Equal to
        /// <c>90 &#8722; |latDeg &#8722; decDeg|</c> under common conditions (both signed).
        /// </summary>
        public static double MeridianAltitude(double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Lower-culmination altitude (hour angle = 12h = 180&#176;) in degrees. Equal to
        /// <c>|latDeg + decDeg| &#8722; 90</c> under common conditions. Negative for targets
        /// that dip below the horizon.
        /// </summary>
        public static double LowerCulminationAltitude(double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) - Math.Cos(phi) * Math.Cos(delta);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Hour-angle magnitude (hours, in <c>(0, 12)</c>) at which a stellar target at
        /// signed declination <paramref name="decDeg"/> seen from signed latitude
        /// <paramref name="latDeg"/> reaches altitude <paramref name="altDeg"/>.
        /// </summary>
        /// <returns>
        /// <list type="bullet">
        /// <item><see cref="double.NaN"/> -- target's maximum altitude is below
        /// <paramref name="altDeg"/> (never reaches it).</item>
        /// <item><see cref="double.PositiveInfinity"/> -- target's minimum altitude is above
        /// <paramref name="altDeg"/> (always above it, i.e. circumpolar-above).</item>
        /// <item>Otherwise the crossing hour angle in hours.</item>
        /// </list>
        /// Callers that stitch rise / set arcs must branch on these sentinels explicitly;
        /// treating <see cref="double.NaN"/> as a real value silently produces wrong
        /// windows.
        /// </returns>
        public static double HourAngleAtAltitude(double latDeg, double decDeg, double altDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double h = altDeg * Math.PI / 180.0;
            double rhs = (Math.Sin(h) - Math.Sin(phi) * Math.Sin(delta)) / (Math.Cos(phi) * Math.Cos(delta));
            if (rhs >  1.0) return double.NaN;
            if (rhs < -1.0) return double.PositiveInfinity;
            return Math.Acos(rhs) * 12.0 / Math.PI;
        }

        /// <summary>
        /// Altitude in degrees at hour angle <paramref name="haHours"/> (sidereal hours;
        /// sign irrelevant since <c>cos</c> is even) for a target at signed latitude and
        /// declination.
        /// </summary>
        public static double AltitudeAtHourAngle(double haHours, double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double haRad = haHours * Math.PI / 12.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(haRad);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Azimuth in degrees (from North, clockwise) at hour angle
        /// <paramref name="haHours"/> (sidereal hours).
        /// </summary>
        /// <remarks>
        /// Hour-angle convention: 0 at upper transit (meridian south in the northern
        /// hemisphere), increasing westward. The input is wrapped into <c>[0, 24)</c>, and
        /// the result is flipped to <c>360 &#8722; az</c> when HA is in the eastern half
        /// (HA &lt; &#960; radians &#8596; target is east of the meridian).
        /// </remarks>
        public static double AzimuthAtHourAngle(double haHours, double latDeg, double decDeg)
        {
            double ha = haHours;
            while (ha <   0.0) ha += 24.0;
            while (ha >= 24.0) ha -= 24.0;
            double haRad = ha * Math.PI / 12.0;

            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;

            double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(haRad);
            double altitude = Math.Asin(sinAlt);

            double cosAz = (Math.Sin(delta) - Math.Sin(phi) * sinAlt) / (Math.Cos(phi) * Math.Cos(altitude));
            if (cosAz >  1.0) cosAz =  1.0;
            if (cosAz < -1.0) cosAz = -1.0;
            double azimuth = Math.Acos(cosAz) * 180.0 / Math.PI;
            if (haRad < Math.PI) azimuth = 360.0 - azimuth;
            return azimuth;
        }
    }
}
