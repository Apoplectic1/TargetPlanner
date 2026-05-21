using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Astronomy.Core.Horizons;
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
    /// Phase 3 of the SoC refactor introduced this seam. The renderer queries cache
    /// state and renders; it no longer owns its own cache.
    /// </para>
    /// <para>
    /// Threading: implementations run cache builds on the threadpool (<c>Task.Run</c>).
    /// Synchronous read accessors (<see cref="IsReady"/>, <see cref="GetOrNull"/>) are
    /// lock-free for the consumer. Callers awaiting <c>*OrBuildAsync</c> or
    /// <c>PrepareXxx</c> receive published entries on completion — no separate
    /// event surface is required.
    /// </para>
    /// <para>
    /// Cancellation: the cache itself does not cancel in-flight builds; on a
    /// <see cref="SetLocationAsync"/>, stale builds run to completion and drop their
    /// results via a publish-time location check. Compute is short (~1-2 sec for 44
    /// targets); the wasted CPU is bounded and acceptable for the simpler code path.
    /// </para>
    /// </remarks>
    public interface IChartCacheStore
    {
        /// <summary>Single-entrypoint pre-render pipeline. Diffs <paramref name="ctx"/>
        /// against the last successfully-applied ctx, runs the necessary internal
        /// Prepare paths (location swap, per-target year, per-(target, HdmKey)
        /// fits, per-(target, DayWindowKey) day altitudes, per-DayWindowKey moon
        /// altitudes), and returns a <see cref="ChartEvaluation"/> describing
        /// what changed. <paramref name="dayKey"/> identifies the Day chart's
        /// current minute-spaced sampling window (the caller derives this from
        /// the night's <c>NightWindow</c>); pass <c>default(DayWindowKey)</c>
        /// to skip the Day/Moon prep on polar or empty-targets nights.</summary>
        /// <remarks>Idempotent: a call with the same ctx as the previous call
        /// short-circuits via the internal per-key Prepare paths (all already
        /// no-op on warm cache). The returned eval reflects the diff from the
        /// previous EnsureAsync; sub-charts use the flags to decide whether
        /// to short-circuit their own Render work.</remarks>
        Task<ChartEvaluation> EnsureAsync(ChartContext ctx, DayWindowKey dayKey);

        /// <summary>Location all current cache entries are keyed against.</summary>
        Location CurrentLocation { get; }

        /// <summary>Per-location <see cref="NightCache"/>; <see langword="null"/> until the
        /// first build completes for the current location.</summary>
        NightCache LocationNightCache { get; }

        /// <summary>Returns the published entry for <paramref name="t"/> at the current
        /// location, or <see langword="null"/> if not yet built.</summary>
        TargetCacheEntry GetOrNull(Target t);

        /// <summary>Build (or wait for an in-flight build of) the entry for
        /// <paramref name="t"/> at the current location. Idempotent: concurrent calls for
        /// the same target dedupe to one underlying compute.</summary>
        Task<TargetCacheEntry> GetOrBuildAsync(Target t);

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

        /// <summary>Build (or wait for an in-flight build of) the fit entry for
        /// <paramref name="t"/> at <paramref name="key"/>. Idempotent per
        /// (target, key); concurrent calls dedupe to one underlying compute.</summary>
        /// <remarks>Requires the per-target yearDays entry to exist; callers should ensure
        /// <see cref="GetOrBuildAsync"/> / <see cref="PrepareManyAsync"/> has completed
        /// for the same target before calling this. The implementation reads
        /// <see cref="TargetCacheEntry.YearDays"/> off the published yearDays entry to
        /// drive the per-night fit walk. <paramref name="horizon"/> drives
        /// <see cref="Astronomy.Core.Session.BestSession.ResolveCandidates"/>'s per-azimuth
        /// visibility test; for a given <paramref name="key"/> the caller must pass a
        /// functionally-equivalent profile (today the scalar case is the only case in flight,
        /// keyed uniquely by <see cref="HdmKey.HorizonDeg"/>).</remarks>
        Task<TargetFitEntry> GetFitOrBuildAsync(Target t, HdmKey key, IHorizonProfile horizon);

        /// <summary>Pre-build fit entries for many targets at <paramref name="key"/>
        /// in parallel. Awaits the yearDays prepare for missing targets internally, so
        /// callers can fire this immediately after constructing the cache without
        /// pre-awaiting yearDays themselves. <paramref name="horizon"/> is passed through
        /// to each per-target build. Optional progress reports a 1-based completion count
        /// as each target's fit-build finishes.</summary>
        Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
            IHorizonProfile horizon, IProgress<int> targetCompleteProgress = null);

        /// <summary>Returns the published per-night altitude curve for
        /// <paramref name="t"/> at <paramref name="key"/>, or <see langword="null"/>
        /// if not yet built. Synchronous, lock-protected.</summary>
        TargetDayAltitudeEntry GetDayOrNull(Target t, DayWindowKey key);

        /// <summary>Build (or wait for an in-flight build of) the Day altitude curve
        /// for <paramref name="t"/> at <paramref name="key"/>. Idempotent per
        /// (target, key); concurrent calls dedupe to one underlying
        /// <see cref="Astronomy.Core.AltitudeCurve"/>.Sample call.</summary>
        Task<TargetDayAltitudeEntry> GetDayOrBuildAsync(Target t, DayWindowKey key);

        /// <summary>Pre-build Day altitude entries for many targets at <paramref name="key"/>
        /// in parallel. Optional progress reports a 1-based completion count
        /// as each target's altitude build finishes.</summary>
        Task PrepareDayAsync(IEnumerable<Target> targets, DayWindowKey key,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Returns the published per-minute moon altitude entry at
        /// <paramref name="key"/>, or <see langword="null"/> if not yet built.
        /// Singleton per <see cref="DayWindowKey"/> (the moon is not target-keyed).
        /// Synchronous, lock-protected.</summary>
        MoonAltitudeEntry GetMoonOrNull(DayWindowKey key);

        /// <summary>Build (or wait for an in-flight build of) the moon altitude
        /// entry at <paramref name="key"/>. Idempotent per key; concurrent calls
        /// dedupe to one underlying compute.</summary>
        Task<MoonAltitudeEntry> GetMoonOrBuildAsync(DayWindowKey key);

        /// <summary>Pre-build the moon altitude entry at <paramref name="key"/>.
        /// No-op when already published.</summary>
        Task PrepareMoonAsync(DayWindowKey key);

        /// <summary>Drop every cached entry and switch to <paramref name="newLocation"/>,
        /// re-anchoring the NightCache against <paramref name="startingUtc"/>. In-flight
        /// builds against the old (location, utc) pair run to completion and discard
        /// themselves at publish via the cache's internal location check. Subsequent
        /// <see cref="GetOrBuildAsync"/> / <see cref="PrepareManyAsync"/> calls build
        /// against the new state.</summary>
        Task SetLocationAsync(Location newLocation, DateTime startingUtc);
    }
}
