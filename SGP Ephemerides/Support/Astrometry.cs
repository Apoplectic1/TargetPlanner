using CoordinateSharp;
using System;

namespace SGP_Ephemerides.Support
{
    public struct NightWindow
    {
        public DateTime AstronomicalDawn;
        public DateTime AstronomicalDusk;
        public double   LunarIlluminationFraction;
    }

    public class Astrometry
    {
        public static Celestial mLocal { get; private set; }
        public static DateTime AstronomicalDawn { get; private set; }
        public static DateTime AstronomicalDusk { get; private set; }
        public static double SunAltitude { get; private set; }
        public static DateTime LunarRise { get; private set; }
        public static DateTime LunarSet { get; private set; }
        public static double LunarAltitude { get; private set; }
        public static string LunarPhase { get; private set; }
        public static double LunarIlluminationFraction { get; private set; }

        private static EagerLoad mEgagerLoad = EagerLoad.Create(EagerLoadType.Celestial);
        private static DateTime? mDuskToday;
        private static DateTime? mDawnToday;
        private static DateTime? mDuskYesterday;
        private static DateTime? mDawnTomorrow;

        public Astrometry()
        {
        }

        // ################################################################################################################################
        // ################################################################################################################################
        public static void Location(Location.Location localLocation)
        {
            double LatSign  = localLocation.North ? 1.0 : -1.0;
            double LongSign = localLocation.West  ? -1.0 : 1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(localLocation.DateTime);

            mLocal = Celestial.CalculateCelestialTimes(
                LatSign  * localLocation.Latitude,
                LongSign * localLocation.Longitude,
                localLocation.DateTime, mEgagerLoad, utcOffset.Hours);

            SunAltitude = mLocal.SunAltitude;
            LunarAltitude = mLocal.MoonAltitude;
            LunarRise = mLocal.MoonRise.HasValue ? mLocal.MoonRise.Value : DateTime.MinValue;
            LunarSet = mLocal.MoonSet.HasValue ? mLocal.MoonSet.Value : DateTime.MinValue;
            LunarPhase = mLocal.MoonIllum.PhaseName;
            LunarIlluminationFraction = mLocal.MoonIllum.Fraction;

            mDawnToday = mLocal.AdditionalSolarTimes.AstronomicalDawn;
            mDuskToday = mLocal.AdditionalSolarTimes.AstronomicalDusk;

            if (localLocation.DateTime >= mDawnToday)
            {
                mLocal = Celestial.CalculateCelestialTimes(
                    LatSign  * localLocation.Latitude,
                    LongSign * localLocation.Longitude,
                    localLocation.DateTime.AddDays(1), mEgagerLoad, utcOffset.Hours);
                mDawnTomorrow = mLocal.AdditionalSolarTimes.AstronomicalDawn;

                AstronomicalDawn = mDawnTomorrow ?? DateTime.MinValue;
                AstronomicalDusk = mDuskToday    ?? DateTime.MinValue;
            }
            else
            {
                mLocal = Celestial.CalculateCelestialTimes(
                    LatSign  * localLocation.Latitude,
                    LongSign * localLocation.Longitude,
                    localLocation.DateTime.AddDays(-1), mEgagerLoad, utcOffset.Hours);
                mDuskYesterday = mLocal.AdditionalSolarTimes.AstronomicalDusk;

                AstronomicalDawn = mDawnToday     ?? DateTime.MinValue;
                AstronomicalDusk = mDuskYesterday ?? DateTime.MinValue;
            }
        }

        // ####################################################################################################################################
        // ####################################################################################################################################

        // Pure helper: returns the night window (astronomical dusk/dawn bracketing the night at
        // location.DateTime) plus the lunar illumination fraction, without touching any static
        // field. Safe to call from concurrent background tasks. Use this from per-day loops; the
        // static-mutating Location(...) above is reserved for the UI display path.
        public static NightWindow ComputeNight(Location.Location location)
        {
            double LatSign  = location.North ? 1.0 : -1.0;
            double LongSign = location.West  ? -1.0 : 1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(location.DateTime);

            Celestial today = Celestial.CalculateCelestialTimes(
                LatSign  * location.Latitude,
                LongSign * location.Longitude,
                location.DateTime, mEgagerLoad, utcOffset.Hours);

            DateTime? dawnToday = today.AdditionalSolarTimes.AstronomicalDawn;
            DateTime? duskToday = today.AdditionalSolarTimes.AstronomicalDusk;
            double illum = today.MoonIllum.Fraction;

            if (location.DateTime >= dawnToday)
            {
                Celestial tomorrow = Celestial.CalculateCelestialTimes(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(1), mEgagerLoad, utcOffset.Hours);
                return new NightWindow
                {
                    AstronomicalDawn = tomorrow.AdditionalSolarTimes.AstronomicalDawn ?? DateTime.MinValue,
                    AstronomicalDusk = duskToday                                      ?? DateTime.MinValue,
                    LunarIlluminationFraction = illum,
                };
            }
            else
            {
                Celestial yesterday = Celestial.CalculateCelestialTimes(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(-1), mEgagerLoad, utcOffset.Hours);
                return new NightWindow
                {
                    AstronomicalDawn = dawnToday                                       ?? DateTime.MinValue,
                    AstronomicalDusk = yesterday.AdditionalSolarTimes.AstronomicalDusk ?? DateTime.MinValue,
                    LunarIlluminationFraction = illum,
                };
            }
        }

        // Greenwich Mean Sidereal Time in hours [0, 24), at the instant whose Julian Date is JD.
        // USNO one-liner form: equivalent to GMST(0h UT) + 1.00273790935 * (elapsed UT hours).
        private static double Greenwich(double JD)
        {
            double D = JD - 2451545.0;
            double GMST = 18.697374558 + 24.06570982441908 * D;
            GMST = GMST - 24.0 * Math.Floor(GMST / 24.0);
            return GMST;
        }


        public static Tuple<double, double> GetAltitudeAzimuth(Target.Target target, Location.Location location)
        {
            double raHours = target.RightAscension;                              // hours in [0, 24)
            double decRadian = target.Declination * Math.PI / 180.0;
            decRadian = target.North ? decRadian : -decRadian;

            DateTime gmt = location.DateTime.ToUniversalTime();

            double latRadian = location.Latitude * Math.PI / 180.0;
            latRadian = location.North ? latRadian : -latRadian;
            double longDegree = location.West ? -location.Longitude : location.Longitude;

            double julianDay = gmt.ToOADate() + 2415018.5;                       // true JD of gmt

            // Local Sidereal Time in hours, differs from GMST by longitude.
            double lst = Greenwich(julianDay) + longDegree / 15.0;
            if (lst <   0) lst += 24.0;
            if (lst >= 24) lst -= 24.0;

            double hourAngle = lst - raHours;                                    // hours east/west of meridian
            if (hourAngle < 0) hourAngle += 24.0;
            hourAngle = hourAngle * Math.PI / 12.0;                              // -> radians

            double sinAlt = Math.Cos(hourAngle) * Math.Cos(decRadian) * Math.Cos(latRadian)
                          + Math.Sin(decRadian) * Math.Sin(latRadian);
            double altitude = Math.Asin(sinAlt);

            // Azimuth from North, clockwise. Acos gives [0, 180]; flip to the western half when HA < pi.
            double cosAz = (Math.Sin(decRadian) - Math.Sin(latRadian) * sinAlt)
                         / (Math.Cos(latRadian) * Math.Cos(altitude));
            double azimuth = Math.Acos(cosAz) * 180.0 / Math.PI;
            if (hourAngle < Math.PI) azimuth = 360.0 - azimuth;

            return Tuple.Create(altitude * 180.0 / Math.PI, azimuth);
        }
    }
}
