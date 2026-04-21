using Astronomy.Core;
using CoordinateSharp;
using System;

using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Support
{
    // UI state facade: refreshes the static dawn/dusk/moon-phase/etc. properties that the
    // MainForm binds to, from the current Location. The math functions that used to live on
    // this class have moved into Astronomy.Core -- call Astronomy.Core.AltAz,
    // Astronomy.Core.TargetGeometry, Astronomy.Core.Time.SiderealTime, and
    // Astronomy.Core.Night.NightCalculator directly for those.
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
        public static void Location(Location localLocation)
        {
            double LatSign  = localLocation.North ? 1.0 : -1.0;
            double LongSign = localLocation.West  ? -1.0 : 1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(localLocation.DateTime);

            mLocal = CoordinateSharpGate.Calculate(
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
                mLocal = CoordinateSharpGate.Calculate(
                    LatSign  * localLocation.Latitude,
                    LongSign * localLocation.Longitude,
                    localLocation.DateTime.AddDays(1), mEgagerLoad, utcOffset.Hours);
                mDawnTomorrow = mLocal.AdditionalSolarTimes.AstronomicalDawn;

                AstronomicalDawn = mDawnTomorrow ?? DateTime.MinValue;
                AstronomicalDusk = mDuskToday    ?? DateTime.MinValue;
            }
            else
            {
                mLocal = CoordinateSharpGate.Calculate(
                    LatSign  * localLocation.Latitude,
                    LongSign * localLocation.Longitude,
                    localLocation.DateTime.AddDays(-1), mEgagerLoad, utcOffset.Hours);
                mDuskYesterday = mLocal.AdditionalSolarTimes.AstronomicalDusk;

                AstronomicalDawn = mDawnToday     ?? DateTime.MinValue;
                AstronomicalDusk = mDuskYesterday ?? DateTime.MinValue;
            }
        }
    }
}
