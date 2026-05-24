using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using TpFilter = TargetPlanner.Filters.Filter;

namespace TargetPlanner.State
{
    /// <summary>
    /// Per-session imaging-planning inputs: what altitude threshold counts as
    /// "above the horizon" (<see cref="TargetFloorDeg"/> /
    /// <see cref="LocalHorizon"/>), how long a target must stay above it
    /// (<see cref="MinDuration"/>), and the active filter + moon-avoidance master
    /// toggle that gate per-night fit decisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives on <see cref="ChartContext.Policy"/>; sourced from MainForm's
    /// active spinners/filter selection in <c>SnapshotCurrent(...)</c>. Decoupled
    /// from <see cref="Astronomy.Core.Locations.Location"/> on purpose:
    /// <c>Location</c> is the site description (latitude / longitude /
    /// elevation / time zone / Bortle), while planning policy is what the user
    /// chooses to image *from* that site. The two scrub independently
    /// (changing site doesn't reset your target floor; raising target floor
    /// doesn't relocate you).
    /// </para>
    /// <para>
    /// <see cref="ActiveFilter"/> is the single source of truth for filter inputs
    /// to both K-S sky-brightness (CenterNm + BandwidthNm) and the Lorentzian
    /// moon-clear gate (Lorentzian + Relax fields, surfaced via <see cref="MoonProfile"/>).
    /// <see cref="MoonAvoidanceEnabled"/> is the master on/off toggle from the UI;
    /// when false the derived <see cref="MoonProfile"/> returns <see langword="null"/>
    /// and the placement-primitive moon gate short-circuits to the visibility result.
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
    /// <see cref="HdmKey"/> keys the fits cache on both <c>HorizonDeg</c>
    /// (derived from <see cref="TargetFloorDeg"/> — the differentiator for the
    /// scalar case) and <see cref="HdmKey.LocalHorizon"/> (the profile reference,
    /// populated for non-scalar polyline / obstruction horizons).
    /// </para>
    /// </remarks>
    public sealed record PlanningPolicy(
        double TargetFloorDeg,
        TimeSpan MinDuration,
        TpFilter ActiveFilter,
        bool MoonAvoidanceEnabled,
        IHorizonProfile LocalHorizon)
    {
        /// <summary>
        /// Derived moon-clear gate profile. Returns <see langword="null"/> when
        /// the master toggle is off or no filter is active — placement primitives
        /// interpret that as "moon-blind" and skip the gate.
        /// </summary>
        public MoonAvoidanceProfile MoonProfile
            => MoonAvoidanceEnabled && ActiveFilter != null
                ? ActiveFilter.ToProfile()
                : null;

        /// <summary>
        /// Convenience factory for the scalar-horizon case: the user has no
        /// per-azimuth horizon file, so <see cref="LocalHorizon"/> is a
        /// <see cref="ScalarHorizonProfile"/> wrapping
        /// <paramref name="targetFloorDeg"/>.
        /// </summary>
        public static PlanningPolicy WithScalarHorizon(
            double targetFloorDeg,
            TimeSpan minDuration,
            TpFilter activeFilter,
            bool moonAvoidanceEnabled)
            => new(targetFloorDeg, minDuration, activeFilter, moonAvoidanceEnabled,
                   new ScalarHorizonProfile(targetFloorDeg));
    }
}
