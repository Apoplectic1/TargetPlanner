using System.Collections.Generic;
using TargetPlanner.State;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-(target, <see cref="HdmKey"/>) cached fit data: the per-night decisions
    /// the Year and Sessions sub-charts need to paint without doing any
    /// <see cref="Astronomy.Core.Session.BestSession"/> work themselves.
    /// </summary>
    /// <remarks>
    /// Index-aligned with the corresponding <see cref="TargetCacheEntry.YearDays"/>
    /// — entry <c>i</c> here describes the night at <c>YearDays[i]</c>.
    /// Owned by <see cref="ChartCacheStore"/>; published immutable.
    /// </remarks>
    public sealed class TargetFitEntry
    {
        public Target Target { get; }
        public HdmKey Key { get; }
        public IReadOnlyList<NightFit> Nights { get; }

        public TargetFitEntry(Target target, HdmKey key, IReadOnlyList<NightFit> nights)
        {
            Target = target;
            Key = key;
            Nights = nights;
        }
    }

    /// <summary>
    /// Per-night fit decision. Year reads <see cref="Floor"/>; Sessions reads
    /// all three (<see cref="Ceiling"/> + <see cref="Floor"/> for its
    /// transit-centered-or-wall-pushed curves, <see cref="CenteredFloor"/> for
    /// the strict-centered "Symmetric" curve). One
    /// <see cref="Astronomy.Core.Session.BestSession.ResolveCandidates"/> resolve
    /// drives both <c>PlaceBest</c> (Ceiling + Floor) and <c>PlaceCentered</c>
    /// (CenteredFloor), so the per-night compute pays one candidate resolve
    /// plus two placements regardless of which sub-charts consume the result.
    /// </summary>
    /// <remarks>
    /// All three fields are <see langword="null"/> when no fit exists for the
    /// night under the current H/D/M (polar / sub-horizon / moon-blocked /
    /// transit-doesn't-center). Tooltips are formatted on hover from these
    /// fields plus the index-matched <see cref="NightCacheEntry"/> rather than
    /// pre-formatted into a parallel string array — saves ~16k strings per
    /// target on a 44-target 365-night sweep.
    /// </remarks>
    public readonly struct NightFit
    {
        public double? Ceiling { get; init; }
        public double? Floor { get; init; }
        public double? CenteredFloor { get; init; }
    }
}
