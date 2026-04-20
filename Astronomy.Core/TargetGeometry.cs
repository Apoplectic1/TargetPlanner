using System;

namespace Astronomy.Core
{
    // Pure-geometry functions for a stellar target's altitude and azimuth at arbitrary hour angle,
    // plus the transit / lower culmination / horizon-crossing primitives that are exact analytic
    // consequences of those two coordinates. All inputs are signed degrees (caller resolves the
    // North/West hemisphere flags).
    public static class TargetGeometry
    {
        // Upper transit altitude (HA = 0). Equal to 90 - |lat - dec| under common conditions.
        public static double MeridianAltitude(double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        // Lower culmination altitude (HA = 12h = 180 degrees). Equal to |lat + dec| - 90
        // under common conditions. Can be negative for targets that dip below the horizon.
        public static double LowerCulminationAltitude(double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) - Math.Cos(phi) * Math.Cos(delta);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        // Hour angle magnitude (hours, in (0, 12)) at which a stellar target at declination decDeg
        // seen from latitude latDeg reaches altitude altDeg.
        //   NaN              -> target's max altitude is below altDeg (never reaches it)
        //   PositiveInfinity -> target's min altitude is above altDeg (always above it)
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

        // Altitude in degrees at hour angle haHours (sidereal hours; sign irrelevant since cos
        // is even) for a target at signed latDeg, decDeg.
        public static double AltitudeAtHourAngle(double haHours, double latDeg, double decDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double delta = decDeg * Math.PI / 180.0;
            double haRad = haHours * Math.PI / 12.0;
            double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(haRad);
            return Math.Asin(sinAlt) * 180.0 / Math.PI;
        }

        // Azimuth in degrees (from North, clockwise) at hour angle haHours (sidereal hours).
        // HA convention: 0 at upper transit (meridian south in N-hemi), increasing westward.
        // Wraps haHours into [0, 24) and flips to 360 - az when HA is in the eastern half,
        // matching the convention in GetAltitudeAzimuth (HA < pi -> eastern -> flip).
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
