using System;
using System.Collections.Generic;
using TargetPlanner.State;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-(target, <see cref="HdmKey"/>) cached fit data: the per-night decisions
    /// the Year and Sessions sub-charts need to paint without doing any
    /// <see cref="Astronomy.Core.Session.BestSession"/> work themselves, plus
    /// the single-night <see cref="Tonight"/> slot the Day and Sky sub-charts
    /// read for hide-on-no-fit + Day's HD-overlay window box.
    /// </summary>
    /// <remarks>
    /// <see cref="Nights"/> is index-aligned with the corresponding
    /// <see cref="TargetCacheEntry.YearDays"/> — entry <c>i</c> here describes
    /// the night at <c>YearDays[i]</c>. <see cref="Tonight"/> is the fit for
    /// <c>LocationNightCache.Starting</c>; it generally does NOT correspond to
    /// <c>Nights[0]</c> because the year grid is anchored at the 1st of the
    /// current month, not at today. Owned by <see cref="ChartCacheStore"/>;
    /// published immutable.
    /// </remarks>
    public sealed class TargetFitEntry
    {
        public Target Target { get; }
        public HdmKey Key { get; }
        public IReadOnlyList<NightFit> Nights { get; }
        public NightFit Tonight { get; }

        public TargetFitEntry(Target target, HdmKey key, IReadOnlyList<NightFit> nights, NightFit tonight)
        {
            Target = target;
            Key = key;
            Nights = nights;
            Tonight = tonight;
        }
    }

    /// <summary>
    /// Per-night fit decision. Year reads <see cref="Floor"/>; Sessions reads
    /// the three altitude fields (<see cref="Ceiling"/> + <see cref="Floor"/>
    /// for its transit-centered-or-wall-pushed curves, <see cref="CenteredFloor"/>
    /// for the strict-centered "Symmetric" curve); Day reads
    /// <see cref="StartUtc"/> / <see cref="EndUtc"/> / <see cref="Floor"/> off
    /// the entry's <see cref="TargetFitEntry.Tonight"/> slot for its HD-overlay
    /// window box in Floor mode, and <see cref="CenteredStartUtc"/> /
    /// <see cref="CenteredEndUtc"/> / <see cref="CenteredFloor"/> for the same
    /// box in Transit mode; Sky reads <see cref="Floor"/>.HasValue off the same
    /// Tonight slot for hide-on-no-fit. One
    /// <see cref="Astronomy.Core.Session.BestSession.ResolveCandidates"/> resolve
    /// drives both <c>PlaceBest</c> (Ceiling + Floor + Start/End) and
    /// <c>PlaceCentered</c> (CenteredFloor + CenteredStart/End), so the per-night
    /// compute pays one candidate resolve plus two placements regardless of
    /// which sub-charts consume the result.
    /// </summary>
    /// <remarks>
    /// All fields are <see langword="null"/> when no fit exists for the night
    /// under the current H/D/M (polar / sub-horizon / moon-blocked /
    /// transit-doesn't-center). The PlaceBest trio (<see cref="StartUtc"/> /
    /// <see cref="EndUtc"/> / <see cref="Floor"/>) and the PlaceCentered trio
    /// (<see cref="CenteredStartUtc"/> / <see cref="CenteredEndUtc"/> /
    /// <see cref="CenteredFloor"/>) are each populated atomically -- all three
    /// non-null when the placement succeeded, all three null when it didn't.
    /// PlaceBest succeeding does NOT imply PlaceCentered did, so a target can
    /// have a Floor trio but a null CenteredFloor trio (wall-pushed only).
    /// Convert UTC to local at the consumer
    /// (Day does <c>StartUtc.Value.ToLocalTime().ToOADate()</c>). Tooltips are
    /// formatted on hover from these fields plus the index-matched
    /// <see cref="NightCacheEntry"/> rather than pre-formatted into a parallel
    /// string array — saves ~16k strings per target on a 44-target 365-night sweep.
    /// </remarks>
    public readonly struct NightFit
    {
        public double? Ceiling { get; init; }
        public double? Floor { get; init; }
        public double? CenteredFloor { get; init; }
        public DateTime? StartUtc { get; init; }
        public DateTime? EndUtc { get; init; }
        public DateTime? CenteredStartUtc { get; init; }
        public DateTime? CenteredEndUtc { get; init; }
        // Upper-transit UTC for this night (HA = 0 at or after AstronomicalDusk).
        // Independent of H/D/M; carried here because NightFit is the natural
        // per-night slot consumers (Day chart HD-overlay transit tick) read from.
        public DateTime? TransitUtc { get; init; }
    }
}
