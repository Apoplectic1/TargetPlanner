using CoordinateSharp;
using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    // Pure helper: returns the night window (astronomical dusk/dawn bracketing the night at
    // location.DateTime) plus the lunar illumination fraction, without touching any static
    // field. Safe to call from concurrent background tasks.
    //
    // Parameterized sun-altitude threshold (civil / nautical / custom) is not yet implemented
    // -- CoordinateSharp only exposes the three fixed thresholds through its SolarTimes
    // property. Will be generalized in Phase 7 via a direct solar-altitude solve.
    public static class NightCalculator
    {
        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

        public static NightWindow ComputeNight(Location location)
        {
            double LatSign  = location.North ?  1.0 : -1.0;
            double LongSign = location.West  ? -1.0 :  1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(location.DateTime);

            Celestial today = Celestial.CalculateCelestialTimes(
                LatSign  * location.Latitude,
                LongSign * location.Longitude,
                location.DateTime, mEagerLoad, utcOffset.Hours);

            DateTime? dawnToday = today.AdditionalSolarTimes.AstronomicalDawn;
            DateTime? duskToday = today.AdditionalSolarTimes.AstronomicalDusk;
            double illum = today.MoonIllum.Fraction;

            if (location.DateTime >= dawnToday)
            {
                Celestial tomorrow = Celestial.CalculateCelestialTimes(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(1), mEagerLoad, utcOffset.Hours);
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
                    location.DateTime.AddDays(-1), mEagerLoad, utcOffset.Hours);
                return new NightWindow
                {
                    AstronomicalDawn = dawnToday                                       ?? DateTime.MinValue,
                    AstronomicalDusk = yesterday.AdditionalSolarTimes.AstronomicalDusk ?? DateTime.MinValue,
                    LunarIlluminationFraction = illum,
                };
            }
        }
    }
}
