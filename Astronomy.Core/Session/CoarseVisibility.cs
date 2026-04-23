using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Coarse pre-filter answering the yes/no question "does this target ever clear the local
    /// horizon during this night?" -- intended as the first elimination pass ahead of more
    /// expensive per-target work like per-minute precompute, scoring, or interval scheduling.
    /// </summary>
    public static class CoarseVisibility
    {
        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> rises above
        /// <paramref name="horizon"/>'s <see cref="IHorizonProfile.MinAltitude"/> at any point
        /// between <paramref name="night"/>'s <see cref="NightWindow.AstronomicalDusk"/> and
        /// <see cref="NightWindow.AstronomicalDawn"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Conservative coarse pre-filter: tests against
        /// <see cref="IHorizonProfile.MinAltitude"/> (the lowest horizon altitude across all
        /// azimuths), so no target visible per a more precise per-azimuth horizon check is
        /// wrongly rejected. Any target that fails this test cannot pass a stricter one
        /// either.
        /// </para>
        /// <para>
        /// O(1) per call; closed-form via <see cref="VisibilityWindows.For"/>. Returns
        /// <see langword="false"/> for invalid (polar day / polar night) night windows.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsEverAboveHorizon(
            Target target, Location location, NightWindow night, IHorizonProfile horizon)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));

            if (!night.IsValid) return false;

            return VisibilityWindows.For(target, location, night, horizon).Count > 0;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> has a single contiguous
        /// window of at least <paramref name="minDuration"/> above <paramref name="horizon"/>'s
        /// <see cref="IHorizonProfile.MinAltitude"/> somewhere between
        /// <paramref name="night"/>'s <see cref="NightWindow.AstronomicalDusk"/> and
        /// <see cref="NightWindow.AstronomicalDawn"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Single-window semantics: a target whose total above-horizon time during the night
        /// is split across two shorter windows (rises, sets, rises again) is <b>not</b>
        /// considered visible even if the sum meets <paramref name="minDuration"/> -- a single
        /// imaging session can't span a horizon dip. Matches <see cref="BestSession"/> and the
        /// Optimal-chart series, which both filter by single-window length.
        /// </para>
        /// <para>
        /// Same cost class as <see cref="IsEverAboveHorizon"/>: one closed-form call to
        /// <see cref="VisibilityWindows.For"/> plus an O(windows) length scan (at most two
        /// windows). Returns <see langword="false"/> for invalid nights (polar day / polar
        /// night) and for targets that never clear the horizon during the night.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsAboveHorizonForAtLeast(
            Target target, Location location, NightWindow night,
            IHorizonProfile horizon, TimeSpan minDuration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));

            if (!night.IsValid) return false;

            var windows = VisibilityWindows.For(target, location, night, horizon);
            foreach (var (start, end) in windows)
            {
                if (end - start >= minDuration) return true;
            }
            return false;
        }
    }
}
