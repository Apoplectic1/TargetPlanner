using TargetPlanner.Caches;

namespace TargetPlanner.State
{
    /// <summary>
    /// Typed output of the cache's pre-render evaluation. Each <see cref="bool"/>
    /// flag says "did this axis change since the last successful pipeline?" so
    /// downstream consumers (sub-chart Render, post-apply hook) can short-circuit
    /// the work they own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Paradigm.</b> This record is the typed enforcement mechanism for the
    /// straight-line chart pipeline. Future code that needs to broadcast a new
    /// staleness signal adds a field here; sub-charts react to it. Don't add
    /// side-paths (extra callbacks, conditional dispatch). The cache populates
    /// fields as it actually rebuilds; render reads them.
    /// </para>
    /// <para>
    /// Phase 4 introduces the type and threads it through
    /// <see cref="TargetPlanner.Charts.IAltitudeSubChart.Render"/>; sub-charts
    /// accept but currently ignore the flags. Phase 5 lands
    /// <c>ChartCacheStore.EnsureAsync(ctx)</c> which populates the flags from
    /// real per-axis diffs. Phase 6 collapses the coordinator's dispatch table
    /// in favour of the straight-line pipeline. Phase 7 wires per-sub-chart
    /// short-circuit logic against the flags.
    /// </para>
    /// </remarks>
    public sealed record ChartEvaluation
    {
        /// <summary>Site geometry or date changed; night, dayKey, moon, fits all invalidate.</summary>
        public required bool LocationChanged { get; init; }

        /// <summary>Per-target series set added or removed.</summary>
        public required bool TargetsChanged { get; init; }

        /// <summary>HDM / filter / horizon-profile changed; fits invalidate, altitude
        /// cache untouched.</summary>
        public required bool HdmChanged { get; init; }

        /// <summary>Day chart placement-strategy radio flipped (Floor / Transit);
        /// per-target visibility filter shifts, no cache state changes.</summary>
        public required bool DayModeChanged { get; init; }

        /// <summary>Sky chart's K-S brightness inputs (Bortle / ExtinctionK / Filter
        /// center) changed; sky re-walks brightness lookup without rebuilding fits.</summary>
        public required bool BrightnessInputsChanged { get; init; }

        /// <summary>Current Day window key; sub-charts pass to
        /// <see cref="IChartCacheStore.GetDayOrNull"/> /
        /// <see cref="IChartCacheStore.GetMoonOrNull"/>.</summary>
        public required DayWindowKey DayKey { get; init; }

        /// <summary>Current HDM key; sub-charts pass to
        /// <see cref="IChartCacheStore.GetFitOrNull"/>.</summary>
        public required HdmKey HdmKey { get; init; }

        /// <summary>Current Day chart placement-strategy mode.</summary>
        public required DayChartMode DayMode { get; init; }

        /// <summary>Convenience: any of the staleness flags set.</summary>
        public bool AnyChange => LocationChanged
                              || TargetsChanged
                              || HdmChanged
                              || DayModeChanged
                              || BrightnessInputsChanged;

        /// <summary>"Worst-case" eval used by callers that haven't yet computed
        /// per-axis diffs (Phase 4 transitional). Every flag is <c>true</c> so
        /// sub-charts that haven't migrated to short-circuit logic still take
        /// their full Render path. Phase 5's <c>EnsureAsync</c> replaces this
        /// with real diffs.</summary>
        public static ChartEvaluation FullChange(DayWindowKey dayKey, HdmKey hdmKey, DayChartMode dayMode)
            => new ChartEvaluation
            {
                LocationChanged = true,
                TargetsChanged = true,
                HdmChanged = true,
                DayModeChanged = true,
                BrightnessInputsChanged = true,
                DayKey = dayKey,
                HdmKey = hdmKey,
                DayMode = dayMode,
            };
    }
}
