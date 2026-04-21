using CoordinateSharp;
using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    public static class TwilightCalculator
    {
        public const double AstronomicalTwilightSunAlt = -18.0;
        public const double NauticalTwilightSunAlt     = -12.0;
        public const double CivilTwilightSunAlt        =  -6.0;

        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

        // Night window (dusk -> dawn bracketing the night at location.DateTime) where the sun
        // is at or below sunAltBelowDeg. Matches NightCalculator.ComputeNight for the -18
        // default but lets callers (e.g. narrowband broadband schedulers) pick nautical
        // (-12) or civil (-6) twilight instead.
        //
        // Returned AstronomicalDawn / AstronomicalDusk are DateTimeKind.Utc. See
        // NightCalculator.ComputeNight for the offset-recovery rationale (DateTimeOffset with
        // the same fixed offset we passed to CoordinateSharp, not .ToUniversalTime() which
        // would re-derive via Windows DST rules).
        //
        // Only the three standard thresholds (-18, -12, -6) are supported, courtesy of
        // CoordinateSharp's prebuilt AdditionalSolarTimes. Arbitrary sunAltBelowDeg will throw
        // NotSupportedException -- a generalized bisection solve against sun altitude could
        // lift that restriction.
        public static NightWindow ComputeNight(Location location, double sunAltBelowDeg)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));

            const double tol = 0.01;
            bool isAstro = Math.Abs(sunAltBelowDeg - AstronomicalTwilightSunAlt) < tol;
            bool isNaut  = Math.Abs(sunAltBelowDeg - NauticalTwilightSunAlt)     < tol;
            bool isCivil = Math.Abs(sunAltBelowDeg - CivilTwilightSunAlt)        < tol;

            if (!isAstro && !isNaut && !isCivil)
            {
                throw new NotSupportedException(
                    "Only astronomical (-18), nautical (-12), and civil (-6) twilight thresholds are " +
                    "supported. Arbitrary sun-altitude thresholds will be added in a future revision.");
            }

            double LatSign  = location.North ?  1.0 : -1.0;
            double LongSign = location.West  ? -1.0 :  1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(location.DateTime);

            Celestial today = CoordinateSharpGate.Calculate(
                LatSign  * location.Latitude,
                LongSign * location.Longitude,
                location.DateTime, mEagerLoad, utcOffset.Hours);

            (DateTime? dawn, DateTime? dusk) = GetTimes(today, isAstro, isNaut);
            double illum = today.MoonIllum.Fraction;

            if (dawn.HasValue && location.DateTime >= dawn.Value)
            {
                Celestial tomorrow = CoordinateSharpGate.Calculate(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(1), mEagerLoad, utcOffset.Hours);
                (DateTime? tomorrowDawn, _) = GetTimes(tomorrow, isAstro, isNaut);
                return new NightWindow
                {
                    AstronomicalDawn = ToUtc(tomorrowDawn, utcOffset),
                    AstronomicalDusk = ToUtc(dusk,         utcOffset),
                    LunarIlluminationFraction = illum,
                };
            }
            else
            {
                Celestial yesterday = CoordinateSharpGate.Calculate(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(-1), mEagerLoad, utcOffset.Hours);
                (_, DateTime? yesterdayDusk) = GetTimes(yesterday, isAstro, isNaut);
                return new NightWindow
                {
                    AstronomicalDawn = ToUtc(dawn,           utcOffset),
                    AstronomicalDusk = ToUtc(yesterdayDusk,  utcOffset),
                    LunarIlluminationFraction = illum,
                };
            }
        }

        private static (DateTime? Dawn, DateTime? Dusk) GetTimes(Celestial c, bool isAstro, bool isNaut)
        {
            if (isAstro) return (c.AdditionalSolarTimes.AstronomicalDawn, c.AdditionalSolarTimes.AstronomicalDusk);
            if (isNaut)  return (c.AdditionalSolarTimes.NauticalDawn,     c.AdditionalSolarTimes.NauticalDusk);
            return (c.AdditionalSolarTimes.CivilDawn, c.AdditionalSolarTimes.CivilDusk);
        }

        private static DateTime ToUtc(DateTime? maybe, TimeSpan offset)
        {
            if (!maybe.HasValue) return DateTime.MinValue;
            DateTime asUnspec = DateTime.SpecifyKind(maybe.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(asUnspec, offset).UtcDateTime;
        }
    }
}
