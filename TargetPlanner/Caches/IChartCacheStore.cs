using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Astronomy.Core.Night;
using TargetPlanner.State;
using Location = Astronomy.Core.Locations.Location;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-(<see cref="Location"/>, <see cref="Target"/>) chart cache. Caches the
    /// 365-day NightWindow series (target-independent, shared across targets at the
    /// same location) and the per-target year-of-night precomputes (altitudes, moon
    /// samples, transit-in-night flags).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Full caller contract — invariants, threading, no-CT design, EnsureAsync semantics —
    /// at <c>docs/design/cache-contract.md</c>.
    /// </para>
    /// <para>
    /// Phase 3 of the SoC refactor introduced this seam. The renderer queries cache
    /// state and renders; it no longer owns its own cache.
    /// </para>
    /// <para>
    /// Threading: implementations run cache builds on the threadpool (<c>Task.Run</c>).
    /// Synchronous read accessors (<see cref="GetOrNull"/> and the per-axis
    /// <c>Get*OrNull</c> siblings) are lock-protected for the consumer. Callers
    /// awaiting the <c>PrepareXxx</c> methods receive published entries on
    /// completion — no separate event surface is required.
    /// </para>
    /// <para>
    /// Cancellation: the cache itself does not cancel in-flight builds; on a
    /// <see cref="SetLocationAsync"/>, stale builds run to completion and drop their
    /// results via a publish-time location check. Compute is short (~2 sec for a
    /// ~77-target library; measured 2026-07-07); the wasted CPU is bounded and acceptable for the simpler code path.
    /// </para>
    /// </remarks>
    public interface IChartCacheStore
    {
        /// <summary>Single-entrypoint pre-render pipeline. Diffs <paramref name="ctx"/>
        /// against the last successfully-applied ctx, runs the necessary internal
        /// Prepare paths (location swap, per-target year, per-(target, HdmKey)
        /// fits, per-(target, NightDate) trajectories, per-NightDate moon
        /// ephemeris), and returns a <see cref="ChartEvaluation"/> describing
        /// what changed. <paramref name="nightDate"/> identifies the current
        /// night (the caller derives this via <see cref="NightDate.Of"/> from
        /// the night's <c>NightWindow</c> + zone); pass <c>default(NightDate)</c>
        /// to skip the trajectory/moon prep on polar or empty-targets nights.
        /// <paramref name="progress"/> receives <c>(Done, Total)</c> ticks for
        /// cache-prep + sub-chart Render work combined. The cache sizes
        /// <c>Total</c> from its staleness diff (pessimistic upper bound) and
        /// ticks <c>Done</c> per-target-per-axis as the prepare paths complete.
        /// When the diff predicts zero work, no Report is issued so the caller's
        /// progress UI (if any) stays inert. Pass <see langword="null"/> to opt
        /// out of progress reporting entirely.</summary>
        /// <remarks>Idempotent: a call with the same ctx as the previous call
        /// short-circuits via the internal per-key Prepare paths (all already
        /// no-op on warm cache). The returned eval reflects the diff from the
        /// previous EnsureAsync; sub-charts use the flags to decide whether
        /// to short-circuit their own Render work.</remarks>
        Task<ChartEvaluation> EnsureAsync(ChartContext ctx, NightDate nightDate,
            IProgress<(int Done, int Total)> progress = null);

        /// <summary>Location all current cache entries are keyed against.</summary>
        Location CurrentLocation { get; }

        /// <summary>Per-location <see cref="NightCache"/>; <see langword="null"/> until the
        /// first build completes for the current location.</summary>
        NightCache LocationNightCache { get; }

        /// <summary>Returns the published entry for <paramref name="t"/> at the current
        /// location, or <see langword="null"/> if not yet built.</summary>
        TargetCacheEntry GetOrNull(Target t);

        /// <summary>Pre-build entries for many targets in parallel. Returns when all builds
        /// have completed (or one has faulted). Optional <paramref name="targetCompleteProgress"/>
        /// receives a 1-based completion count as each target finishes (order matches
        /// completion order, not input order); pass <see langword="null"/> to skip
        /// progress reporting.</summary>
        Task PrepareManyAsync(IEnumerable<Target> targets,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Returns the published fit entry for <paramref name="t"/> at
        /// <paramref name="key"/>, or <see langword="null"/> if not yet built.</summary>
        /// <remarks>Synchronous, lock-protected; safe to call from the UI thread on every
        /// Render. Returns null is the expected "fits not ready" sentinel — sub-chart
        /// Render loops skip the target on null and the coordinator awaits
        /// <see cref="PrepareFitsAsync"/> before dispatch.</remarks>
        TargetFitEntry GetFitOrNull(Target t, HdmKey key);

        /// <summary>Pre-build fit entries for many targets at <paramref name="key"/>
        /// in parallel. Awaits the yearDays prepare for missing targets internally, so
        /// callers can fire this immediately after constructing the cache without
        /// pre-awaiting yearDays themselves. The horizon profile is reconstructed from
        /// <paramref name="key"/> per build. Optional progress reports a 1-based
        /// completion count as each target's fit-build finishes.</summary>
        Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Returns the published per-night trajectory for
        /// <paramref name="t"/> at <paramref name="key"/>, or <see langword="null"/>
        /// if not yet built. Synchronous, lock-protected.</summary>
        TargetTrajectoryEntry GetTrajectoryOrNull(Target t, NightDate key);

        /// <summary>Pre-build trajectory entries for many targets at <paramref name="key"/>
        /// in parallel. Optional progress reports a 1-based completion count
        /// as each target's trajectory build finishes.</summary>
        Task PrepareTrajectoryAsync(IEnumerable<Target> targets, NightDate key,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Returns the published per-minute moon ephemeris entry at
        /// <paramref name="key"/>, or <see langword="null"/> if not yet built.
        /// Singleton per <see cref="NightDate"/> (the moon is not target-keyed).
        /// Synchronous, lock-protected.</summary>
        MoonEphemerisEntry GetMoonOrNull(NightDate key);

        /// <summary>Pre-build the moon ephemeris entry at <paramref name="key"/>.
        /// No-op when already published.</summary>
        Task PrepareMoonAsync(NightDate key);

        /// <summary>Drop every cached entry and switch to <paramref name="newLocation"/>,
        /// re-anchoring the NightCache against <paramref name="startingUtc"/>. In-flight
        /// builds against the old (location, utc) pair run to completion and discard
        /// themselves at publish via the cache's internal location check. Subsequent
        /// <see cref="PrepareManyAsync"/> calls build against the new state.</summary>
        Task SetLocationAsync(Location newLocation, DateTime startingUtc);
    }
}
