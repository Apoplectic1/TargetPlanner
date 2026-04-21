using CoordinateSharp;
using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    // Pure helper: returns the night window (astronomical dusk/dawn bracketing the night at
    // location.DateTime) plus the lunar illumination fraction, without touching any static
    // field. Safe to call from concurrent background tasks.
    //
    // Returned AstronomicalDawn / AstronomicalDusk are DateTimeKind.Utc -- absolute UTC
    // instants, not wall-clock times in any particular offset. Consumers that need local
    // wall-clock values must call .ToLocalTime() explicitly.
    //
    // CoordinateSharp is called in the original "local frame" form -- we pass
    // location.DateTime with location's UTC offset, so it returns solar events keyed to the
    // local calendar day (the same day/night pairing the observer experiences). Those return
    // values are Kind=Unspecified wall-clock times "at that offset". We then recover true UTC
    // by using DateTimeOffset(value, offset).UtcDateTime -- this undoes the same fixed offset
    // we handed in, which is the crux of the fix. Calling .ToUniversalTime() instead would
    // re-derive the offset from Windows DST rules for each returned instant and get a
    // different answer on DST-transition nights (the ~1 h LST error that produced the
    // OptimalFloor spike on Nov 1, 2026).
    //
    // Parameterized sun-altitude threshold (civil / nautical / custom) is not yet implemented
    // -- CoordinateSharp only exposes the three fixed thresholds through its SolarTimes
    // property. A direct solar-altitude solve would generalize this; see TwilightCalculator
    // for the bounded-choice variant in the meantime.
    public static class NightCalculator
    {
        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

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
