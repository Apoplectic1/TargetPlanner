using System;
using System.Collections.Generic;
using System.Linq;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Observer-context orderings for collections of <see cref="Target"/> -- "what comes up
    /// when" sorts that a scheduler UI or night-planning tool can use in place of a pure
    /// alphabetical / catalogue-number sort.
    /// </summary>
    /// <remarks>
    /// Both methods are analytic (constant cost per target via <see cref="TransitTime"/> and
    /// <see cref="RiseSet"/>) and stable (ties preserve input order). They do not mutate the
    /// input sequence; the returned list is a fresh <see cref="IReadOnlyList{T}"/>.
    /// </remarks>
    public static class TargetOrdering
    {
        /// <summary>
        /// Orders <paramref name="targets"/> by the UTC instant of each target's next upper
        /// transit at or after <paramref name="searchFromUtc"/>, ascending.
        /// </summary>
        /// <remarks>
        /// Wraps <see cref="TransitTime.UtcAtOrAfter"/>. Every stellar target transits once
        /// per sidereal day, so the key is always a valid UTC instant and no sentinels are
        /// needed. Null entries in <paramref name="targets"/> are dropped silently (the
        /// caller's job is to skip them, but this keeps the ordering robust against a mixed
        /// list).
        /// </remarks>
        /// <param name="targets">Targets to sort. Non-null; may contain null entries (skipped).</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="searchFromUtc">
        /// Lower bound for each target's transit search. Must be
        /// <see cref="DateTimeKind.Utc"/>.
        /// </param>
        /// <returns>
        /// A new list of the non-null input targets, ordered by ascending next-transit UTC.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targets"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<Target> ByTransit(
            IEnumerable<Target> targets, Location location, DateTime searchFromUtc)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (location == null) throw new ArgumentNullException(nameof(location));

            return targets
                .Where(t => t != null)
                .OrderBy(t => TransitTime.UtcAtOrAfter(t, location, searchFromUtc))
                .ToList();
        }

        /// <summary>
        /// Orders <paramref name="targets"/> by the UTC instant of each target's next rise
        /// above <paramref name="horizonDeg"/> at or after <paramref name="searchFromUtc"/>,
        /// ascending. Circumpolar targets bubble to the front; targets that never rise sink
        /// to the back.
        /// </summary>
        /// <remarks>
        /// Wraps the scalar overload of
        /// <see cref="RiseSet.NextAtOrAfter(Target, Location, DateTime, double)"/>. The tri-state
        /// result is mapped to a sortable <see cref="DateTime"/> key: <see cref="DateTime.MinValue"/>
        /// for <see cref="RiseSetState.Circumpolar"/> (always observable -- user usually wants
        /// these first when planning a night), <see cref="DateTime.MaxValue"/> for
        /// <see cref="RiseSetState.NeverRises"/> (not visible from this location -- pushed to
        /// the end), and the <c>Rise</c> UTC for <see cref="RiseSetState.Found"/>.
        /// </remarks>
        /// <param name="targets">Targets to sort. Non-null; may contain null entries (skipped).</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="searchFromUtc">
        /// Lower bound for each target's rise search. Must be <see cref="DateTimeKind.Utc"/>.
        /// </param>
        /// <param name="horizonDeg">
        /// Horizon altitude in degrees. A target is considered "risen" when its altitude
        /// reaches this value.
        /// </param>
        /// <returns>
        /// A new list of the non-null input targets, ordered by ascending next-rise UTC with
        /// Circumpolar / NeverRises at the extremes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="targets"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<Target> ByRise(
            IEnumerable<Target> targets, Location location,
            DateTime searchFromUtc, double horizonDeg)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (location == null) throw new ArgumentNullException(nameof(location));

            return targets
                .Where(t => t != null)
                .OrderBy(t => RiseKey(t, location, searchFromUtc, horizonDeg))
                .ToList();
        }

        private static DateTime RiseKey(
            Target target, Location location, DateTime searchFromUtc, double horizonDeg)
        {
            var result = RiseSet.NextAtOrAfter(target, location, searchFromUtc, horizonDeg);
            switch (result.State)
            {
                case RiseSetState.Circumpolar: return DateTime.MinValue;
                case RiseSetState.NeverRises:  return DateTime.MaxValue;
                case RiseSetState.Found:       return result.Rise ?? DateTime.MaxValue;
                default:                       return DateTime.MaxValue;
            }
        }
    }
}
