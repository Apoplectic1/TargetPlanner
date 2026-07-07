using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Caches
{
    // One cache axis: a store dictionary + an in-flight Task dictionary + the
    // get / build / in-flight-dedupe / publish lifecycle, all guarded by a lock
    // shared with the owning ChartCacheStore. Stale builds — ones started
    // against a location that SetLocationAsync has since swapped — discard
    // themselves at publish via the ReferenceEquals(currentLocation, buildLocation)
    // check. Extracted from four byte-identical axes in ChartCacheStore
    // (docs/2026-05-19-code-quality-audit.md Tier 5); the genuinely-distinct part — the actual
    // compute — stays in the store as the injected build delegate.
    internal sealed class CacheAxis<TKey, TVal> where TVal : class
    {
        private readonly object mGate;                          // shared store lock
        private readonly Func<Location> mCurrentLocation;       // reads the store's mLocation, live
        private readonly Func<TKey, Location, Task<TVal>> mBuild;

        private Dictionary<TKey, TVal> mStore = new Dictionary<TKey, TVal>();
        private Dictionary<TKey, Task<TVal>> mInFlight = new Dictionary<TKey, Task<TVal>>();

        public CacheAxis(object gate, Func<Location> currentLocation,
                         Func<TKey, Location, Task<TVal>> build)
        {
            mGate = gate;
            mCurrentLocation = currentLocation;
            mBuild = build;
        }

        // Published-entry count, for diagnostics.
        public int Count { get { lock (mGate) { return mStore.Count; } } }

        // Sync read: the published entry, or null if not yet built.
        public TVal GetOrNull(TKey key)
        {
            lock (mGate) { mStore.TryGetValue(key, out TVal v); return v; }
        }

        // Fast-path published read; otherwise start (or join an in-flight) build.
        public Task<TVal> GetOrBuildAsync(TKey key)
        {
            TVal existing = GetOrNull(key);
            if (existing != null) return Task.FromResult(existing);

            lock (mGate)
            {
                if (mInFlight.TryGetValue(key, out Task<TVal> task)) return task;

                Location location = mCurrentLocation();
                task = RunBuildAsync(key, location);
                mInFlight[key] = task;
                return task;
            }
        }

        // Runs the injected compute, then publishes (or drops on fault). Only
        // TryPublish / DropOnFault re-take mGate, and only after the await — so
        // a build that itself calls into another axis cannot deadlock.
        private async Task<TVal> RunBuildAsync(TKey key, Location buildLocation)
        {
            try
            {
                TVal value = await mBuild(key, buildLocation);
                TryPublish(key, value, buildLocation);
                return value;
            }
            catch
            {
                DropOnFault(key, buildLocation);
                throw;
            }
        }

        // Publish when the build's source location is still current; a
        // SetLocationAsync swap since the build started orphans it (the new
        // store / in-flight dicts don't carry this key anyway).
        private void TryPublish(TKey key, TVal value, Location buildLocation)
        {
            lock (mGate)
            {
                if (!ReferenceEquals(mCurrentLocation(), buildLocation)) return;
                mStore[key] = value;
                mInFlight.Remove(key);
            }
        }

        // Drop a faulted task so the next GetOrBuildAsync starts fresh instead
        // of re-awaiting the broken Task. Skipped when a location swap already
        // replaced the dict.
        private void DropOnFault(TKey key, Location buildLocation)
        {
            lock (mGate)
            {
                if (ReferenceEquals(mCurrentLocation(), buildLocation))
                    mInFlight.Remove(key);
            }
        }

        // Batch warm-up: build every key, ticking completeProgress once per
        // build that runs to completion. The build task itself is awaited (not
        // only the progress continuation) so WhenAll surfaces a faulted build
        // rather than the continuation's swallowed OnlyOnRanToCompletion cancel.
        public async Task PrepareAsync(IEnumerable<TKey> keys,
            IProgress<int> completeProgress = null)
        {
            if (keys == null) return;
            List<Task> tasks = new List<Task>();
            int completed = 0;
            foreach (TKey key in keys)
            {
                Task<TVal> build = GetOrBuildAsync(key);
                tasks.Add(build);
                if (completeProgress != null)
                {
                    tasks.Add(build.ContinueWith(
                        _ => completeProgress.Report(Interlocked.Increment(ref completed)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion
                            | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));
                }
            }
            await Task.WhenAll(tasks);
        }

        // Reset the axis to empty for a location swap, returning the old
        // in-flight tasks so the caller can await them drain.
        //
        // Two preconditions MUST hold (caller is responsible -- this method
        // doesn't enforce them):
        //
        //   1. mGate is held. ChartCacheStore.SetLocationAsync calls this
        //      inside its single lock so all four axes + mLocation reset
        //      atomically.
        //
        //   2. mCurrentLocation() ALREADY returns the new location. The
        //      stale-publish discard at TryPublish is gated by
        //      ReferenceEquals(mCurrentLocation(), buildLocation); if the
        //      location hasn't moved, an in-flight build's TryPublish will
        //      still see ref-equal and land its value in the post-drain
        //      mStore -- silently re-populating what the caller just cleared.
        //      ChartCacheStore.SetLocationAsync sets mLocation FIRST, THEN
        //      calls DrainAndReset on every axis, so the contract holds.
        //
        // A future second caller (or a test author) that drains without
        // swapping the location first will hit this trap.
        public List<Task<TVal>> DrainAndReset()
        {
            List<Task<TVal>> oldInFlight = mInFlight.Values.ToList();
            mStore = new Dictionary<TKey, TVal>();
            mInFlight = new Dictionary<TKey, Task<TVal>>();
            return oldInFlight;
        }
    }
}
