using System.Collections.Generic;
using System.Drawing;
using Astronomy.Core.Horizons;

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
    /// <para>
    /// Composition: <see cref="Location"/> is pure site geometry (lat / lon /
    /// elevation / time zone / Bortle / ExtinctionK / DateTime); <see cref="Policy"/>
    /// is the per-session planning input (target floor / minimum duration / moon
    /// profile / filter center / local horizon). The split lets a user scrub
    /// imaging policy independently of site, and lets a future XISF / IS consumer
    /// reuse <c>Location</c> without dragging UI-only fields with it.
    /// </para>
    /// <para>
    /// C# <c>record</c> gives structural equality and the <c>with</c> expression
    /// for non-destructive mutation (<c>newCtx = oldCtx with { Location = newLoc };</c>) —
    /// the same shape Core's <c>Target.With(...)</c> / <c>Location.With(...)</c>
    /// already use.
    /// </para>
    /// <para>
    /// <see cref="TargetColors"/> is the single source of truth for per-target
    /// curve / legend colors across every sub-chart. MainForm rebuilds the dict
    /// once per <c>KnownTargets</c> change (NINA load), Name-sorted so the same
    /// target lands on the same palette index across reloads of the same folder.
    /// Sub-chart <c>Render</c> implementations look up <c>TargetColors[target]</c>
    /// rather than computing <c>palette[i % len]</c> per-iteration; the latter
    /// produces inconsistent colors across charts whenever the targets list
    /// order diverges between sub-chart Renders (e.g. after <c>Reorder</c> on
    /// a sort change). May be <see langword="null"/> on early-init code paths
    /// before the first NINA load completes; sub-charts fall back to the first
    /// palette entry as a safe default.
    /// </para>
    /// </remarks>
    public sealed record ChartContext(
        Location Location,
        IReadOnlyList<Target> Targets,
        PlanningPolicy Policy,
        string ActiveArea,
        IReadOnlyDictionary<Target, Color> TargetColors
    )
    {
        /// <summary>
        /// Derived cache key for per-(target, H/D/M) fit data. All fields
        /// source from <see cref="Policy"/>; flips on any TargetFloor /
        /// Duration / MoonProfile / FilterCenter / LocalHorizon-reference
        /// change. Bortle / ExtinctionK are excluded — they affect Sky's K-S
        /// brightness path, not fit decisions. <see cref="HdmKey.LocalHorizon"/>
        /// is populated only for non-scalar profiles; scalar lives under
        /// <c>HorizonDeg</c> to avoid cache thrash on every snapshot (the
        /// <see cref="PlanningPolicy.WithScalarHorizon"/> factory creates a
        /// fresh <see cref="ScalarHorizonProfile"/> instance per call).
        /// </summary>
        public HdmKey Hdm => new HdmKey
        {
            HorizonDeg     = Policy.TargetFloorDeg,
            DurationTicks  = Policy.MinDuration.Ticks,
            Profile        = Policy.MoonProfile,
            FilterCenterNm = Policy.FilterCenterNm,
            LocalHorizon   = Policy.LocalHorizon is ScalarHorizonProfile ? null : Policy.LocalHorizon,
        };
    }
}
