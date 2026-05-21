namespace TargetPlanner.State
{
    /// <summary>
    /// Result of the cache's pre-render evaluation, produced by
    /// <c>ChartCacheStore.EnsureAsync</c> and consumed by the coordinator's
    /// post-apply hook. Currently a single staleness gate — whether the Sky
    /// chart's K-S brightness inputs changed since the last pipeline. NOT
    /// threaded through sub-chart <c>Render</c>.
    /// </summary>
    /// <remarks>
    /// The four per-axis change flags this record once carried
    /// (Location / Targets / Hdm / DayMode) plus the unread DayKey / HdmKey /
    /// DayMode keys were Phase-7 short-circuit scaffolding. Phase 7 was reverted
    /// and nothing ever branched on the flags, so they were dropped. If a future
    /// change needs a sub-chart to react to a staleness signal, add the field
    /// here and re-thread the parameter onto <c>Render</c> then — with a real
    /// consumer, not as speculative scaffolding.
    /// </remarks>
    public sealed record ChartEvaluation
    {
        /// <summary>Sky chart's K-S brightness inputs (Bortle / ExtinctionK /
        /// filter centre) changed since the last pipeline. The coordinator's
        /// post-apply hook gates the K-S brightness re-walk on this so a
        /// brightness-only scrub doesn't rebuild fits.</summary>
        public required bool BrightnessInputsChanged { get; init; }
    }
}
