using System;
using CoordinateSharp;

namespace Astronomy.Core
{
    // Serializes CoordinateSharp's CalculateCelestialTimes across threads. CoordinateSharp
    // 3.4.1.1 has internal state accessed during Celestial computation that is not safe under
    // concurrent calls: when the chart's background Task.Run hammers it for a 365-day year
    // scan while the UI thread also calls it (AltitudeSeries.BuildMoonSeries,
    // AltitudeChart.AddDawnDuskGradient, Astrometry.Location, ...), occasional days come back
    // with a null AdditionalSolarTimes.AstronomicalDawn or AstronomicalDusk. NightCalculator
    // maps that to DateTime.MinValue, BuildYearSeries reads it as "polar", and
    // BuildOptimalSeries emits -90 for that single day -- producing the random-position
    // spikes on the Optimal chart that shift from run to run. Every CoordinateSharp call in
    // this codebase routes through this gate.
    //
    // The lock is held only around CalculateCelestialTimes itself; the returned Celestial
    // object is constructed with EagerLoadType.Celestial, so reads of AdditionalSolarTimes,
    // MoonIllum, SunAltitude, MoonAltitude, etc. are field lookups on the locally-owned
    // instance and do not need to be under the lock.
    public static class CoordinateSharpGate
    {
        private static readonly object sLock = new object();

        public static Celestial Calculate(
            double latitude, double longitude, DateTime dateTime,
            EagerLoad eagerLoad, double utcOffsetHours)
        {
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
