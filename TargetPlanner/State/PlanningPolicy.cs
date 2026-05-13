using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;

namespace TargetPlanner.State
{
    /// <summary>
    /// Per-session imaging-planning inputs: what altitude threshold counts as
    /// "above the horizon" (<see cref="TargetFloorDeg"/> /
    /// <see cref="LocalHorizon"/>), how long a target must stay above it
    /// (<see cref="MinDuration"/>), and the filter/moon parameters that gate
    /// per-night fit decisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives on <see cref="ChartContext.Policy"/>; sourced from MainForm's
    /// active spinners/filters in <c>SnapshotCurrent(...)</c>. Decoupled from
    /// <see cref="Astronomy.Core.Locations.Location"/> on purpose:
    /// <c>Location</c> is the site description (latitude / longitude /
    /// elevation / time zone / Bortle), while planning policy is what the user
    /// chooses to image *from* that site. The two scrub independently
    /// (changing site doesn't reset your target floor; raising target floor
    /// doesn't relocate you).
    /// </para>
    /// <para>
    /// <see cref="LocalHorizon"/> is the canonical horizon representation for
    /// fit decisions. The common case is <see cref="ScalarHorizonProfile"/>
    /// wrapping <see cref="TargetFloorDeg"/>; a future per-site polyline
    /// (NINA <c>.hrz</c>) loader substitutes a <see cref="PolylineHorizonProfile"/>
    /// here without any call-site changes. <see cref="TargetFloorDeg"/> stays
    /// as the scalar UI surface (the green horizon line, the
    /// <c>NumericUpDown_TargetFloor</c> spinner).
    /// </para>
    /// <para>
    /// <see cref="HdmKey"/> derives its scalar <c>HorizonDeg</c> from
    /// <see cref="TargetFloorDeg"/>. When <see cref="LocalHorizon"/> is
    /// scalar (the default), this matches <see cref="IHorizonProfile.MinAltitude"/>.
    /// For polyline horizons, the cache continues to dedupe on the scalar key
    /// until the PR-5 LocalHorizon work extends <see cref="HdmKey"/> to carry
    /// the profile reference.
    /// </para>
    /// </remarks>
    public sealed record PlanningPolicy(
        double TargetFloorDeg,
        TimeSpan MinDuration,
        MoonAvoidanceProfile MoonProfile,
        double FilterCenterNm,
        IHorizonProfile LocalHorizon)
    {
        /// <summary>
        /// Convenience factory for the scalar-horizon case: the user has no
        /// per-azimuth horizon file, so <see cref="LocalHorizon"/> is a
        /// <see cref="ScalarHorizonProfile"/> wrapping
        /// <paramref name="targetFloorDeg"/>.
        /// </summary>
        public static PlanningPolicy WithScalarHorizon(
            double targetFloorDeg,
            TimeSpan minDuration,
            MoonAvoidanceProfile moonProfile,
            double filterCenterNm)
            => new(targetFloorDeg, minDuration, moonProfile, filterCenterNm,
                   new ScalarHorizonProfile(targetFloorDeg));
    }
}
