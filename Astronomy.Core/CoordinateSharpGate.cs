using System;
using CoordinateSharp;

namespace Astronomy.Core
{
    // Serializes CoordinateSharp's Celestial.CalculateCelestialTimes across threads.
    // CoordinateSharp 3.4.1.1 has internal state accessed during Celestial computation that
    // is not safe under concurrent calls -- two calls racing can produce occasional results
    // with null AdditionalSolarTimes entries (AstronomicalDawn / Dusk etc.) that would
    // otherwise have been valid. Routing every call in Astronomy.Core (and its consumers)
    // through this gate eliminates that failure mode.
    //
    // The lock is held only around CalculateCelestialTimes itself; the returned Celestial is
    // constructed with EagerLoadType.Celestial, so reads of AdditionalSolarTimes, MoonIllum,
    // SunAltitude, MoonAltitude, etc. are field lookups on the locally-owned instance and do
    // not need to be under the lock.
    //
    // THREAD-SAFETY: the returned Celestial is not itself thread-safe; callers must not share
    // a single returned instance across threads.
    public static class CoordinateSharpGate
    {
        private static readonly object sLock = new object();

        public static Celestial Calculate(
            double latitude, double longitude, DateTime dateTime,
            EagerLoad eagerLoad, double utcOffsetHours)
        {
            if (eagerLoad == null) throw new ArgumentNullException(nameof(eagerLoad));
            lock (sLock)
            {
                return Celestial.CalculateCelestialTimes(
                    latitude, longitude, dateTime, eagerLoad, utcOffsetHours);
            }
        }

        public static Celestial Calculate(
            double latitude, double longitude, DateTime dateTime, double utcOffsetHours)
        {
            lock (sLock)
            {
                return Celestial.CalculateCelestialTimes(
                    latitude, longitude, dateTime, utcOffsetHours);
            }
        }
    }
}
