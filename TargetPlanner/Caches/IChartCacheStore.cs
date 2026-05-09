using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core.Night;
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
    /// Phase 3 of the SoC refactor introduces this type as the cache seam. Becomes the
    /// single CoordinateSharp call site for chart-cache work, which means a future
    /// roll-your-own-astronomy effort only has to swap one class's internals. The
    /// renderer (<see cref="TargetPlanner.Charts.AltitudeSeries"/>) queries cache state
    /// and renders; it no longer owns its own cache.
    /// </para>
    /// <para>
    /// Threading: implementations run cache builds on the threadpool (<c>Task.Run</c>),
    /// gated by an internal <see cref="SemaphoreSlim"/> to limit parallel
    /// CoordinateSharpGate hits. Synchronous read accessors (<see cref="IsReady"/>,
    /// <see cref="GetOrNull"/>) are lock-free for the consumer. The
    /// <see cref="TargetReady"/> event is marshalled to the UI thread via the
    /// <see cref="SynchronizationContext"/> captured at store construction.
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
        Task<TargetCacheEntry> GetOrBuildAsync(Target t, CancellationToken ct);

        /// <summary>Pre-build entries for many targets in parallel (gated by the store's
        /// internal concurrency semaphore). Returns when all builds have completed
        /// (or one has cancelled / faulted). Optional <paramref name="targetCompleteProgress"/>
        /// receives a 1-based completion count as each target finishes (order matches
        /// completion order, not input order); pass <see langword="null"/> to skip
        /// progress reporting.</summary>
        Task PrepareManyAsync(IEnumerable<Target> targets, CancellationToken ct,
            IProgress<int> targetCompleteProgress = null);

        /// <summary>Cancel all in-flight builds, drop every cached entry, switch to
        /// <paramref name="newLocation"/>. Subsequent <see cref="GetOrBuildAsync"/> /
        /// <see cref="PrepareManyAsync"/> calls build against the new location. Fires
        /// <see cref="LocationChanged"/> after the swap commits.</summary>
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
