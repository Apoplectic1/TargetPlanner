using System;
using System.Collections.Generic;
using System.Threading;
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
    /// Phase 3 of the SoC refactor introduced this seam. The renderer queries cache
    /// state and renders; it no longer owns its own cache.
    /// </para>
    /// <para>
    /// Threading: implementations run cache builds on the threadpool (<c>Task.Run</c>).
    /// Synchronous read accessors (<see cref="IsReady"/>, <see cref="GetOrNull"/>) are
    /// lock-free for the consumer. The <see cref="TargetReady"/> event is marshalled
    /// to the UI thread via the <see cref="SynchronizationContext"/> captured at store
    /// construction.
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
        /// <summary>Location all current cache entries are keyed against.</summary>
        Location CurrentLocation { get; }

        /// <summary>Per-location <see cref="NightCache"/>; <see langword="null"/> until the
        /// first build completes for the current location.</summary>
        NightCache LocationNightCache { get; }

        /// <summary>True iff the cache contains a published entry for <paramref name="t"/>
        /// at the current location.</summary>
        bool IsReady(Target t);

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
        /// drive the per-night fit walk.</remarks>
        Task<TargetFitEntry> GetFitOrBuildAsync(Target t, HdmKey key);

        /// <summary>Pre-build fit entries for many targets at <paramref name="key"/>
        /// in parallel. Awaits the yearDays prepare for missing targets internally, so
        /// callers can fire this immediately after constructing the cache without
        /// pre-awaiting yearDays themselves. Optional progress reports a 1-based
        /// completion count as each target's fit-build finishes.</summary>
        Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Drop every cached entry and switch to <paramref name="newLocation"/>.
        /// In-flight builds against the old location run to completion and discard
        /// themselves at publish via the cache's internal location check. Subsequent
        /// <see cref="GetOrBuildAsync"/> / <see cref="PrepareManyAsync"/> calls build
        /// against the new location. Fires <see cref="LocationChanged"/> after the swap
        /// commits.</summary>
        Task SetLocationAsync(Location newLocation);

        /// <summary>Fires after a <see cref="TargetCacheEntry"/> has been published.
        /// Marshalled to the UI thread. <see cref="TargetReadyEventArgs.Location"/>
        /// identifies the location the entry was built against — subscribers should
        /// filter against <see cref="CurrentLocation"/> to skip stale-location ticks.
        /// </summary>
        event EventHandler<TargetReadyEventArgs> TargetReady;

        /// <summary>Fires after <see cref="SetLocationAsync"/> commits the swap (and
        /// before in-flight stale builds finish unwinding). Marshalled to the UI
        /// thread. Subscribers can blank rendered state, drop pre-render decisions
        /// keyed to the old location, or schedule a fresh render against the new
        /// one.</summary>
        event EventHandler<LocationChangedEventArgs> LocationChanged;
    }

    public sealed class TargetReadyEventArgs : EventArgs
    {
        public Location Location { get; }
        public Target Target { get; }
        public TargetCacheEntry Entry { get; }

        public TargetReadyEventArgs(Location location, Target target, TargetCacheEntry entry)
        {
            Location = location;
            Target   = target;
            Entry    = entry;
        }
    }

    public sealed class LocationChangedEventArgs : EventArgs
    {
        public Location OldLocation { get; }
        public Location NewLocation { get; }

        public LocationChangedEventArgs(Location oldLocation, Location newLocation)
        {
            OldLocation = oldLocation;
            NewLocation = newLocation;
        }
    }
}
