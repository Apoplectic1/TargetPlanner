using System;
using System.Runtime.CompilerServices;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;

namespace TargetPlanner.State
{
    /// <summary>
    /// Cache key for per-(target, H/D/M) fit data. Captures every input that
    /// changes the per-night fit decision (visibility window placement, moon
    /// mask) downstream of the per-Location yearDays cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="TargetPlanner.Caches.ChartCacheStore"/> as the second-
    /// axis cache key (alongside per-target <c>YearDays</c>). A <see cref="HdmKey"/>
    /// change invalidates fits but preserves the yearDays cache, so H/D/M scrubs
    /// don't re-pay the per-(target, location) moon-sample sweep.
    /// </para>
    /// <para>
    /// <c>Profile</c> uses reference identity because <see cref="MoonAvoidanceProfile"/>
    /// is an immutable POCO produced by <c>Filter.ToProfile()</c> / Lorentzian
    /// scrub handlers — the same logical profile reuses the same instance for
    /// the duration of a session. Two structurally-equal profiles created via
    /// different code paths would technically rebuild fits unnecessarily; we
    /// accept that trade for the cheap-equality fast path.
    /// </para>
    /// <para>
    /// <see cref="LocalHorizon"/> is populated only for non-scalar profiles
    /// (polyline / obstruction-table). For the scalar case the field stays
    /// <see langword="null"/> and <see cref="HorizonDeg"/> is the differentiator;
    /// this avoids cache thrash on every <c>SnapshotCurrent</c> call (which
    /// creates a fresh <see cref="ScalarHorizonProfile"/> instance each time
    /// via <see cref="PlanningPolicy.WithScalarHorizon"/>). Reference identity
    /// on the polyline case: the form-level <c>mLocalHorizon</c> caches the
    /// loaded profile for the active location, so the same site reuses the
    /// same instance until a hot-reload swaps it out.
    /// </para>
    /// </remarks>
    public readonly struct HdmKey : IEquatable<HdmKey>
    {
        public double HorizonDeg { get; init; }
        public long DurationTicks { get; init; }
        public MoonAvoidanceProfile Profile { get; init; }
        public double FilterCenterNm { get; init; }
        public IHorizonProfile LocalHorizon { get; init; }

        public bool Equals(HdmKey other) =>
            HorizonDeg == other.HorizonDeg
            && DurationTicks == other.DurationTicks
            && ReferenceEquals(Profile, other.Profile)
            && FilterCenterNm == other.FilterCenterNm
            && ReferenceEquals(LocalHorizon, other.LocalHorizon);

        public override bool Equals(object obj) => obj is HdmKey k && Equals(k);

        public override int GetHashCode() => HashCode.Combine(
            HorizonDeg, DurationTicks, RuntimeHelpers.GetHashCode(Profile), FilterCenterNm,
            RuntimeHelpers.GetHashCode(LocalHorizon));

        public static bool operator ==(HdmKey a, HdmKey b) => a.Equals(b);
        public static bool operator !=(HdmKey a, HdmKey b) => !a.Equals(b);
    }
}
