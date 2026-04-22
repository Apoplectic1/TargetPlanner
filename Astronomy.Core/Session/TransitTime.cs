using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Computes the next upper transit (local meridian crossing, HA = 0) of a stellar
    /// target.
    /// </summary>
    public static class TransitTime
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        /// <summary>
        /// Returns the first UTC instant at or after <paramref name="searchFromUtc"/> when
        /// the target transits (crosses the local meridian, HA = 0) as seen from the given
        /// location.
        /// </summary>
        /// <remarks>
        /// Assumes stellar fixed RA/Dec. Inverts <c>LST(t) = RA</c> analytically in one step
        /// -- no numerical root finding, constant cost.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="searchFromUtc">
        /// The lower bound for the search. Must be UTC (<see cref="DateTimeKind.Utc"/>).
        /// </param>
        /// <returns>
        /// The next UTC instant at or after <paramref name="searchFromUtc"/> when the target
        /// transits. <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime UtcAtOrAfter(Target target, Location location, DateTime searchFromUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;

            double lstRef = SiderealTime.Local(searchFromUtc, lonDegEast);
            double deltaLst = raHours - lstRef;
            while (deltaLst <   0.0) deltaLst += 24.0;
            while (deltaLst >= 24.0) deltaLst -= 24.0;

            // Advance UT by the solar-hour equivalent of deltaLst sidereal hours.
            double deltaUtHours = deltaLst * 24.0 / SiderealHoursPerSolarDay;
            return searchFromUtc.AddHours(deltaUtHours);
        }
    }
}
