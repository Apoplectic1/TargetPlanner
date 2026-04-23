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
    }
}
