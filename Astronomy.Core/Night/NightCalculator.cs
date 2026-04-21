using CoordinateSharp;
using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Computes the astronomical night window (sun at or below -18&#176;) bracketing
    /// <see cref="Location.DateTime"/>, plus the lunar illumination fraction. Pure; no
    /// static mutable state; safe to call from concurrent background tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="TwilightCalculator.ComputeNight"/> for nautical (-12&#176;) or civil
    /// (-6&#176;) thresholds. Arbitrary thresholds are not yet supported -- CoordinateSharp
    /// only exposes the three fixed bands through its <c>SolarTimes</c> property. A direct
    /// solar-altitude solve would lift that restriction.
    /// </para>
    /// <para>
    /// The returned <see cref="NightWindow.AstronomicalDawn"/> and
    /// <see cref="NightWindow.AstronomicalDusk"/> are <see cref="DateTimeKind.Utc"/> --
    /// absolute UTC instants. Consumers who want local wall-clock values call
    /// <c>.ToLocalTime()</c> at the display site.
    /// </para>
    /// <para>
    /// Implementation note on DST: CoordinateSharp is called in the "local frame" form
    /// (<see cref="Location.DateTime"/> passed with the observer's local UTC offset) so the
    /// returned solar events key to the local calendar day the observer experiences. Those
    /// returns are <see cref="DateTimeKind.Unspecified"/> wall-clock times "at that
    /// offset"; we recover true UTC via
    /// <c>new DateTimeOffset(value, offset).UtcDateTime</c>, which undoes the same fixed
    /// offset we handed in. Using <c>.ToUniversalTime()</c> here would re-derive the offset
    /// from Windows DST rules for each returned instant and get a different answer on
    /// DST-transition nights -- the ~1 h LST error that produced the OptimalFloor spike on
    /// 2026-11-01.
    /// </para>
    /// </remarks>
    public static class NightCalculator
    {
        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

        /// <summary>
        /// Returns the astronomical night window bracketing <paramref name="location"/>'s
        /// moment, rolled forward (to tomorrow's dawn) if the observer is already past
        /// today's dawn or back (to yesterday's dusk) if before it.
        /// </summary>
        /// <param name="location">Observer position and local moment. Non-null.</param>
        /// <returns>
        /// A <see cref="NightWindow"/> with <see cref="DateTimeKind.Utc"/> dusk/dawn
        /// instants. If the location is in polar day / polar night (no dusk or no dawn on
        /// either the today/yesterday/tomorrow queries), the missing field falls back to
        /// <see cref="DateTime.MinValue"/> and <see cref="NightWindow.IsValid"/> will
        /// report false.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static NightWindow ComputeNight(Location location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));

            double LatSign  = location.North ?  1.0 : -1.0;
            double LongSign = location.West  ? -1.0 :  1.0;
            TimeSpan utcOffset = TimeZoneInfo.Local.GetUtcOffset(location.DateTime);

            Celestial today = CoordinateSharpGate.Calculate(
                LatSign  * location.Latitude,
                LongSign * location.Longitude,
                location.DateTime, mEagerLoad, utcOffset.Hours);

            DateTime? dawnToday = today.AdditionalSolarTimes.AstronomicalDawn;
            DateTime? duskToday = today.AdditionalSolarTimes.AstronomicalDusk;
            double illum = today.MoonIllum.Fraction;

            // Bracketing comparison is in the local frame: both sides are wall-clock at the
            // same offset, so the compare is well-defined without touching UTC.
            if (dawnToday.HasValue && location.DateTime >= dawnToday.Value)
            {
                Celestial tomorrow = CoordinateSharpGate.Calculate(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(1), mEagerLoad, utcOffset.Hours);
                return new NightWindow
                {
                    AstronomicalDawn = ToUtc(tomorrow.AdditionalSolarTimes.AstronomicalDawn, utcOffset),
                    AstronomicalDusk = ToUtc(duskToday,                                      utcOffset),
                    LunarIlluminationFraction = illum,
                };
            }
            else
            {
                Celestial yesterday = CoordinateSharpGate.Calculate(
                    LatSign  * location.Latitude,
                    LongSign * location.Longitude,
                    location.DateTime.AddDays(-1), mEagerLoad, utcOffset.Hours);
                return new NightWindow
                {
                    AstronomicalDawn = ToUtc(dawnToday,                                       utcOffset),
                    AstronomicalDusk = ToUtc(yesterday.AdditionalSolarTimes.AstronomicalDusk, utcOffset),
                    LunarIlluminationFraction = illum,
                };
            }
        }

        // Convert a CoordinateSharp "wall-clock at utcOffset" DateTime to absolute UTC by
        // undoing the same offset we handed in. DateTimeOffset with Kind=Unspecified accepts
        // any offset verbatim (no Windows DST re-derivation), which is the whole point.
        private static DateTime ToUtc(DateTime? maybe, TimeSpan offset)
        {
            if (!maybe.HasValue) return DateTime.MinValue;
            DateTime asUnspec = DateTime.SpecifyKind(maybe.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(asUnspec, offset).UtcDateTime;
        }
    }
}
