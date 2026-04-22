using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Distinguishes the three outcomes of a rise/set lookup. Previously the API returned
    /// <c>(null, null)</c> for both <see cref="Circumpolar"/> and <see cref="NeverRises"/>
    /// and the consumer had to probe the current altitude to disambiguate. This enum lets a
    /// scheduler branch on the shape directly without an extra <c>AltAz</c> call.
    /// </summary>
    public enum RiseSetState
    {
        /// <summary>Rise and / or Set are populated with valid UTC DateTimes.</summary>
        Found,
        /// <summary>Target is always above the horizon at this location. Rise and Set are both <see langword="null"/>.</summary>
        Circumpolar,
        /// <summary>Target never reaches the horizon at this location. Rise and Set are both <see langword="null"/>.</summary>
        NeverRises
    }

    /// <summary>
    /// Next UTC rise / set of a stellar target. Scalar-horizon overload is analytic; the
    /// <see cref="IHorizonProfile"/> overload uses the scalar fast-path as a seed and
    /// refines against the profile via bisection.
    /// </summary>
    public static class RiseSet
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;
        private const double SiderealDayInSolarHours  = 24.0 * 24.0 / SiderealHoursPerSolarDay;

        /// <summary>
        /// Next UTC rise and set of <paramref name="target"/> at or after
        /// <paramref name="searchFromUtc"/> against a scalar horizon
        /// <paramref name="horizonDeg"/>.
        /// </summary>
        /// <remarks>
        /// Analytic: derives both times from the next transit plus/minus the hour angle at
        /// which the target is at <paramref name="horizonDeg"/>.
        /// </remarks>
        /// <returns>
        /// A tuple whose <c>State</c> is <see cref="RiseSetState.Found"/> (Rise and Set are
        /// non-null UTC DateTimes), <see cref="RiseSetState.Circumpolar"/> (both null), or
        /// <see cref="RiseSetState.NeverRises"/> (both null).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static (RiseSetState State, DateTime? Rise, DateTime? Set) NextAtOrAfter(
            Target target, Location location, DateTime searchFromUtc, double horizonDeg)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double haHorizon = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, horizonDeg);

            if (double.IsNaN(haHorizon))               return (RiseSetState.NeverRises, null, null);
            if (double.IsPositiveInfinity(haHorizon))  return (RiseSetState.Circumpolar, null, null);

            double haSolarHours = haHorizon * 24.0 / SiderealHoursPerSolarDay;

            DateTime nextTransit = TransitTime.UtcAtOrAfter(target, location, searchFromUtc);
            DateTime candidateRise = nextTransit.AddHours(-haSolarHours);
            DateTime candidateSet  = nextTransit.AddHours( haSolarHours);

            // Rise belonging to this transit cycle may already be in the past. If so, use the
            // rise from the NEXT transit cycle, which is one sidereal day later.
            DateTime nextRise = candidateRise >= searchFromUtc
                ? candidateRise
                : candidateRise.AddHours(SiderealDayInSolarHours);

            // Set for this transit cycle is >= nextTransit >= searchFromUtc, so always valid.
            return (RiseSetState.Found, nextRise, candidateSet);
        }

        /// <summary>
        /// Same as the scalar-horizon overload but against an
        /// <see cref="IHorizonProfile"/>. The scalar fast-path (<see cref="IHorizonProfile.MinAltitude"/>)
        /// seeds a bisection refinement, sampling the profile at the target's current
        /// azimuth each iteration.
        /// </summary>
        /// <remarks>
        /// For profiles that are close to flat the result converges in 2-3 iterations and
        /// matches the scalar case exactly; for heavily non-monotonic profiles (ridges /
        /// buildings) bisection is the safer choice than Newton, so we use that here.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static (RiseSetState State, DateTime? Rise, DateTime? Set) NextAtOrAfter(
            Target target, Location location, DateTime searchFromUtc, IHorizonProfile horizon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));

            // Seed from the scalar lower-bound fast-path. Circumpolar / NeverRises pass
            // straight through with Rise / Set null.
            var seed = NextAtOrAfter(target, location, searchFromUtc, horizon.MinAltitude);
            if (seed.State != RiseSetState.Found) return seed;

            DateTime? rise = seed.Rise.HasValue
                ? Refine(target, location, horizon, seed.Rise.Value, isRise: true)
                : (DateTime?)null;
            DateTime? set = seed.Set.HasValue
                ? Refine(target, location, horizon, seed.Set.Value, isRise: false)
                : (DateTime?)null;

            return (RiseSetState.Found, rise, set);
        }

        // Bisection-refine a scalar-seed candidate crossing time against the profile. The
        // crossing we want is where target_altitude(t) equals profile.AltitudeAt(target_az(t)).
        // For a "rise" crossing the function target_alt - profile_alt transitions from
        // negative to positive as t increases; for "set" it transitions from positive to
        // negative. We bracket around the seed by +/-2 hours which is safely larger than the
        // expected scalar-vs-profile disagreement (a 90-degree profile variation from MinAlt
        // shifts the crossing by at most ~15-20 minutes).
        private static DateTime Refine(
            Target target, Location location, IHorizonProfile horizon,
            DateTime seed, bool isRise)
        {
            DateTime lo = seed.AddHours(-2.0);
            DateTime hi = seed.AddHours( 2.0);

            for (int i = 0; i < 30; i++)  // 2^-30 of a 4-hour span = sub-second precision
            {
                DateTime mid = new DateTime((lo.Ticks + hi.Ticks) / 2, DateTimeKind.Utc);
                AltAz coords = AltAzCalculator.At(target, location, mid);
                double targetAlt = coords.Altitude;
                double targetAz  = coords.Azimuth;
                double profileAlt = horizon.AltitudeAt(targetAz);

                bool targetAbove = targetAlt > profileAlt;

                if (isRise)
                {
                    // Rise: below before crossing, above after.
                    if (targetAbove) hi = mid;
                    else             lo = mid;
                }
                else
                {
                    // Set: above before crossing, below after.
                    if (targetAbove) lo = mid;
                    else             hi = mid;
                }
            }

            return new DateTime((lo.Ticks + hi.Ticks) / 2, DateTimeKind.Utc);
        }
    }
}
