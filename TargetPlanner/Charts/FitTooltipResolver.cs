using TargetPlanner.Caches;
using TargetPlanner.State;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    /// <summary>
    /// Shared per-night fit lookup pattern for the Year + Sessions chart tooltips.
    /// Both sub-charts hold per-Render snapshots of the <see cref="ChartContext"/>
    /// + <see cref="IChartCacheStore"/> (because tooltips fire asynchronously on
    /// mouse motion, after Render has returned) and resolve a hovered segment to
    /// its cached <see cref="NightFit"/> via the same <c>Hdm</c>-keyed lookup.
    /// </summary>
    /// <remarks>
    /// Audit cleanup (item 2 from <c>docs/2026-05-19-code-quality-audit.md</c>): the two
    /// tooltip formatters previously inlined identical 4-line lookups. Extracting
    /// here names the pattern and prevents future drift between the Year and
    /// Sessions implementations.
    /// </remarks>
    internal static class FitTooltipResolver
    {
        /// <summary>
        /// Resolve the per-night <see cref="NightFit"/> for the hovered segment.
        /// Null-safe across all three nullable inputs (<paramref name="lastCtx"/>,
        /// <paramref name="lastCache"/>, and the cache's per-(target, Hdm) miss
        /// case) — returns <see langword="default"/> when any are unavailable.
        /// Caller is responsible for the segment-bounds check against the
        /// per-target days list (different per sub-chart).
        /// </summary>
        public static NightFit ResolveFit(
            Target target, int segmentIndex,
            ChartContext lastCtx, IChartCacheStore lastCache)
        {
            HdmKey hdm = lastCtx?.Hdm ?? default;
            TargetFitEntry fitEntry = lastCache?.GetFitOrNull(target, hdm);
            return fitEntry != null && segmentIndex < fitEntry.Nights.Count
                ? fitEntry.Nights[segmentIndex]
                : default;
        }
    }
}
