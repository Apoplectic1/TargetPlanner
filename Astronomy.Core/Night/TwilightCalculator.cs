using CoordinateSharp;
using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Night window at parameterized sun-altitude thresholds (astronomical -18&#176;,
    /// nautical -12&#176;, or civil -6&#176;). Sibling of
    /// <see cref="NightCalculator.ComputeNight"/>, which hard-codes the astronomical
    /// threshold.
    /// </summary>
    /// <remarks>
    /// Dusk/dawn instants are returned as <see cref="DateTimeKind.Utc"/>. See
    /// <see cref="NightCalculator"/> for the DST-safe offset-recovery rationale.
    /// </remarks>
    public static class TwilightCalculator
    {
        /// <summary>Sun altitude threshold for astronomical twilight (&#8722;18&#176;).</summary>
        public const double AstronomicalTwilightSunAlt = -18.0;

        /// <summary>Sun altitude threshold for nautical twilight (&#8722;12&#176;).</summary>
        public const double NauticalTwilightSunAlt     = -12.0;

        /// <summary>Sun altitude threshold for civil twilight (&#8722;6&#176;).</summary>
        public const double CivilTwilightSunAlt        =  -6.0;

        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

        /// <summary>
        /// Returns the night window (dusk -> dawn bracketing <paramref name="location"/>'s
        /// moment) where the sun is at or below <paramref name="sunAltBelowDeg"/>. Matches
        /// <see cref="NightCalculator.ComputeNight"/> when <paramref name="sunAltBelowDeg"/>
        /// is <see cref="AstronomicalTwilightSunAlt"/>, but lets callers (e.g. narrowband /
        /// broadband schedulers) pick nautical or civil instead.
        /// </summary>
        /// <param name="location">Observer position and local moment. Non-null.</param>
        /// <param name="sunAltBelowDeg">
        /// One of <see cref="AstronomicalTwilightSunAlt"/>, <see cref="NauticalTwilightSunAlt"/>,
        /// or <see cref="CivilTwilightSunAlt"/>. Matching is tolerant to &#177;0.01&#176;.
        /// </param>
        /// <returns>
        /// A <see cref="NightWindow"/> with <see cref="DateTimeKind.Utc"/> dusk/dawn
        /// instants. Polar day / polar night falls back to
        /// <see cref="DateTime.MinValue"/> sentinels; use <see cref="NightWindow.IsValid"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// <paramref name="sunAltBelowDeg"/> is not one of the three CoordinateSharp-backed
        /// thresholds. Arbitrary sun-altitude thresholds will be added in a future revision
        /// (generalized bisection solve against sun altitude).
        /// </exception>
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
