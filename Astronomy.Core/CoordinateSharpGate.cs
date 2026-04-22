using System;
using CoordinateSharp;

namespace Astronomy.Core
{
    /// <summary>
    /// Serializes <c>CoordinateSharp.Celestial.CalculateCelestialTimes</c> across threads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CoordinateSharp 3.4.1.1 has internal state accessed during Celestial computation that
    /// is not safe under concurrent calls -- two calls racing can produce occasional results
    /// with null <c>AdditionalSolarTimes</c> entries (AstronomicalDawn / Dusk etc.) that
    /// would otherwise have been valid. Routing every call in Astronomy.Core (and its
    /// consumers) through this gate eliminates that failure mode.
    /// </para>
    /// <para>
    /// The lock is held only around <c>CalculateCelestialTimes</c> itself; the returned
    /// <c>Celestial</c> is constructed with <c>EagerLoadType.Celestial</c>, so reads of
    /// <c>AdditionalSolarTimes</c>, <c>MoonIllum</c>, <c>SunAltitude</c>,
    /// <c>MoonAltitude</c>, etc. are field lookups on the locally-owned instance and do not
    /// need to be under the lock.
    /// </para>
    /// <para>
    /// <b>Thread-safety:</b> the returned <c>Celestial</c> is not itself thread-safe;
    /// callers must not share a single returned instance across threads.
    /// </para>
    /// </remarks>
    public static class CoordinateSharpGate
    {
        private static readonly object sLock = new object();

        /// <summary>
        /// Thread-safe wrapper for
        /// <c>Celestial.CalculateCelestialTimes(lat, lon, dateTime, eagerLoad, offsetHours)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="eagerLoad"/> is <see langword="null"/>.
        /// </exception>
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

        /// <summary>
        /// Thread-safe wrapper for
        /// <c>Celestial.CalculateCelestialTimes(lat, lon, dateTime, offsetHours)</c> --
        /// default eager-load set.
        /// </summary>
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
