using System.Collections.Generic;
using Astronomy.Core.Moon;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.State
{
    /// <summary>
    /// Immutable snapshot of every input the chart pipeline reads. Built by
    /// <c>MainForm.SnapshotCurrent(...)</c> at one point in time and threaded
    /// through the cache trigger logic and every sub-chart's <c>Render</c> /
    /// <c>RefreshVisibility</c> call so downstream code can't observe state
    /// drifting mid-render.
    /// </summary>
    /// <remarks>
    /// Phase 1 of the orchestration-layer refactor (see plan
    /// <c>~/.claude/plans/high-level-refactoring-goals-separation-moonlit-clarke.md</c>).
    /// Replaces the loose primitive parameter lists previously passed into
    /// <c>IAltitudeSubChart.Render(8 params)</c> and
    /// <c>IAltitudeSubChart.RefreshVisibility(5 params)</c> — same fields,
    /// structurally bundled so adding a new chart input (e.g. BIRDWATCHER
    /// connection state) is one record-field addition rather than a signature
    /// break across six files.
    /// <para>
    /// C# <c>record</c> gives structural equality and the <c>with</c> expression
    /// for non-destructive mutation (<c>newCtx = oldCtx with { Location = newLoc };</c>) —
    /// the same shape Core's <c>Target.With(...)</c> / <c>Location.With(...)</c>
    /// already use.
    /// </para>
    /// <para>
    /// <c>Horizon</c> and <c>Duration</c> aren't separate fields here because
    /// they already live on <c>Location</c>; downstream code reads
    /// <c>ctx.Location.Horizon</c> / <c>ctx.Location.Duration</c>.
    /// <c>LocalDateTime</c> for the now-line is <c>ctx.Location.DateTime</c>
    /// (kept in sync by <c>MainForm.UpdateLocalDateTimeEvents</c>).
    /// <c>Filter</c> is represented as its two chart-relevant projections —
    /// <see cref="MoonProfile"/> (Lorentzian / hide-on-no-fit) and
    /// <see cref="ActiveFilterCenterNm"/> (Rayleigh λ⁻⁴ for K-S extinction);
    /// the full <c>Filters.Filter</c> instance carries Name / BandwidthNm /
    /// persistence concerns the chart pipeline doesn't read.
    /// </para>
    /// </remarks>
    public sealed record ChartContext(
        Location Location,
        IReadOnlyList<Target> Targets,
        MoonAvoidanceProfile MoonProfile,
        double ActiveFilterCenterNm,
        string ActiveArea
    );
}
