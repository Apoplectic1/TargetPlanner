using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Time;
using TargetPlanner.Support;
using Location = Astronomy.Core.Locations.Location;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Default <see cref="IChartCacheStore"/> implementation. Single-writer cache with
    /// in-flight de-duping; cache builds run on the threadpool and self-throttle.
    /// </summary>
    /// <remarks>
    /// Phase 3 of the SoC refactor: the renderer queries cache state instead of owning
    /// its own. Astronomy.Core's Meeus implementation is lock-free, so per-target year
    /// builds run in parallel across threadpool cores.
    /// </remarks>
    public sealed class ChartCacheStore : IChartCacheStore, IDisposable
    {
        // Moon-sample sweep cadence inside ComputeYearDays. Matches the pre-Phase-3 behavior
        // in AltitudeSeries.ComputeYearCache and the cadence BestSession.MoonClearIntersect
        // uses for the Day-chart path.
        private static readonly TimeSpan MoonSampleStep = TimeSpan.FromMinutes(10);

        private readonly object mGate = new object();
        private readonly SynchronizationContext mUiContext;

        private Location mLocation;
        private NightCache mNightCache;
        private Task<NightCache> mNightCacheTask;        // in-flight per-location night-cache build
        private CancellationTokenSource mLocationCts;    // cancels everything keyed to mLocation
        private Dictionary<Target, TargetCacheEntry> mEntries = new Dictionary<Target, TargetCacheEntry>();
        private Dictionary<Target, Task<TargetCacheEntry>> mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();

        public event EventHandler<TargetReadyEventArgs> TargetReady;
        public event EventHandler<LocationChangedEventArgs> LocationChanged;

        public ChartCacheStore(Location initialLocation, SynchronizationContext uiContext)
        {
            if (initialLocation == null) throw new ArgumentNullException(nameof(initialLocation));
            if (uiContext == null) throw new ArgumentNullException(
                nameof(uiContext),
                "ChartCacheStore must be constructed on a UI thread; pass SynchronizationContext.Current "
                + "from the form constructor / InitializeDynamicControls. A null context would silently "
                + "fall back to firing TargetReady on the build thread, breaking subscribers' UI marshalling.");
            mLocation = initialLocation;
            mLocationCts = new CancellationTokenSource();
            mUiContext = uiContext;
        }

        public Location CurrentLocation
        {
            get { lock (mGate) { return mLocation; } }
        }

        public NightCache LocationNightCache
        {
            get { lock (mGate) { return mNightCache; } }
        }

        public bool IsReady(Target t)
        {
            if (t == null) return false;
            lock (mGate) { return mEntries.ContainsKey(t); }
        }

        public TargetCacheEntry GetOrNull(Target t)
        {
            if (t == null) return null;
            lock (mGate)
            {
                mEntries.TryGetValue(t, out TargetCacheEntry entry);
                return entry;
            }
        }

        public Task<TargetCacheEntry> GetOrBuildAsync(Target t, CancellationToken ct)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            // Fast path: already published.
            TargetCacheEntry existing = GetOrNull(t);
            if (existing != null) return Task.FromResult(existing);

            CancellationTokenSource locationCts;
            Task<TargetCacheEntry> task;
            lock (mGate)
            {
                // In-flight de-dupe.
                if (mInFlight.TryGetValue(t, out task)) return WithExternalCancel(task, ct);

                locationCts = mLocationCts;
                Location location = mLocation;
                task = BuildEntryAsync(t, location, locationCts.Token);
                mInFlight[t] = task;
            }
            return WithExternalCancel(task, ct);
        }

        public async Task PrepareManyAsync(IEnumerable<Target> targets, CancellationToken ct,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return;
            List<Task> tasks = new List<Task>();
            int completed = 0;
            foreach (Target t in targets)
            {
                if (t == null) continue;
                Task<TargetCacheEntry> build = GetOrBuildAsync(t, ct);
                if (targetCompleteProgress == null)
                {
                    tasks.Add(build);
                }
                else
                {
                    // ContinueWith on RanToCompletion only -- cancelled / faulted tasks
                    // skip the tick (they propagate via Task.WhenAll below). Synchronous
                    // continuation keeps the increment cheap; Progress<T>.Report internally
                    // marshals the callback to the captured SyncContext (UI thread).
                    tasks.Add(build.ContinueWith(
                        _ => targetCompleteProgress.Report(Interlocked.Increment(ref completed)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));
                }
            }
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { /* expected on cancel; surface only one */ throw; }
        }

        public async Task SetLocationAsync(Location newLocation)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));

            CancellationTokenSource oldCts;
            Task<NightCache> oldNightTask;
            ICollection<Task<TargetCacheEntry>> oldInFlight;
            Location oldLocation;

            lock (mGate)
            {
                // No-op if location is unchanged (reference equality); legitimate for repeated
                // settings-driven calls.
                if (object.ReferenceEquals(mLocation, newLocation)) return;

                oldLocation = mLocation;
                oldCts = mLocationCts;
                oldNightTask = mNightCacheTask;
                oldInFlight = mInFlight.Values.ToList();

                // Reset state for the new location. Cancel + drop everything tied to the old
                // location; new GetOrBuildAsync calls will build against the new location.
                mLocation = newLocation;
                mLocationCts = new CancellationTokenSource();
                mNightCache = null;
                mNightCacheTask = null;
                mEntries = new Dictionary<Target, TargetCacheEntry>();
                mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();
            }

            oldCts.Cancel();

            // Fire LocationChanged immediately after the swap commits, before awaiting
            // the in-flight unwind. Subscribers reading CurrentLocation see the new
            // location and can blank UI / schedule re-renders without waiting on stale
            // builds to settle.
            FireLocationChanged(oldLocation, newLocation);

            // Wait for in-flight tasks to observe the cancel and unwind. OperationCanceled
            // is the expected outcome of the cancel; other exceptions are stale-build
            // compute errors -- log them but don't fail SetLocationAsync.
            try
            {
                if (oldNightTask != null) await oldNightTask;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warn("Stale NightCache build threw during SetLocationAsync", ex); }

            foreach (Task<TargetCacheEntry> t in oldInFlight)
            {
                try { await t; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Warn("Stale per-target build threw during SetLocationAsync", ex); }
            }

            oldCts.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource cts;
            lock (mGate) { cts = mLocationCts; }
            try { cts?.Cancel(); cts?.Dispose(); }
            catch (Exception ex) { Log.Warn("ChartCacheStore.Dispose: cancel/dispose threw", ex); }
        }

        // -------------- internals --------------

        private async Task<TargetCacheEntry> BuildEntryAsync(Target target, Location location, CancellationToken ct)
        {
            try
            {
                NightCache night = await EnsureNightCacheAsync(location, ct);
                ct.ThrowIfCancellationRequested();

                IReadOnlyList<NightCacheEntry> yearDays = await Task.Run(
                    () => ComputeYearDays(target, location, night, ct), ct);

                TargetCacheEntry entry = new TargetCacheEntry(target, yearDays);

                lock (mGate)
                {
                    // Discard if location changed mid-build (the publish would corrupt the new
                    // location's cache).
                    if (!object.ReferenceEquals(mLocation, location)) return entry;
                    mEntries[target] = entry;
                    mInFlight.Remove(target);
                }

                FireTargetReady(location, target, entry);
                return entry;
            }
            catch
            {
                // Remove the failed/cancelled task from in-flight so a subsequent
                // GetOrBuildAsync starts fresh instead of re-awaiting the broken Task.
                // Mirrors the success-path location guard above: if SetLocationAsync
                // swapped mInFlight while we were building, the new dict doesn't
                // contain us anyway -- leave it alone.
                lock (mGate)
                {
                    if (object.ReferenceEquals(mLocation, location))
                        mInFlight.Remove(target);
                }
                throw;
            }
        }

        private Task<NightCache> EnsureNightCacheAsync(Location location, CancellationToken ct)
        {
            lock (mGate)
            {
                if (mNightCache != null && object.ReferenceEquals(mLocation, location))
                    return Task.FromResult(mNightCache);

                if (mNightCacheTask != null && object.ReferenceEquals(mLocation, location))
                    return mNightCacheTask;

                Location loc = location;
                Task<NightCache> task = Task.Run(() =>
                {
                    DateTime seed = loc.DateTime;
                    DateTime startDay = NightCache.ComputeYearStartDay(seed);
                    int days = NightCache.ComputeYearDaysCount(seed);
                    return new NightCache(loc, startDay, days, ct);
                }, ct);

                mNightCacheTask = task.ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled) return null;
                    NightCache nc = t.Result;
                    lock (mGate)
                    {
                        if (object.ReferenceEquals(mLocation, location))
                            mNightCache = nc;
                    }
                    return nc;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                return mNightCacheTask;
            }
        }

        private void FireTargetReady(Location location, Target target, TargetCacheEntry entry)
        {
            EventHandler<TargetReadyEventArgs> handler = TargetReady;
            if (handler == null) return;
            TargetReadyEventArgs args = new TargetReadyEventArgs(location, target, entry);
            // mUiContext is non-null by ctor invariant -- subscribers always get UI marshalling.
            mUiContext.Post(_ => handler(this, args), null);
        }

        private void FireLocationChanged(Location oldLocation, Location newLocation)
        {
            EventHandler<LocationChangedEventArgs> handler = LocationChanged;
            if (handler == null) return;
            LocationChangedEventArgs args = new LocationChangedEventArgs(oldLocation, newLocation);
            mUiContext.Post(_ => handler(this, args), null);
        }

        // External cancellation: the caller's ct may fire before the build's locationCts.
        // Wrap the in-flight build so the caller's await throws on either source. The inner
        // task is keyed to the location's CTS and isn't cancelled from here -- WhenAny just
        // gives the caller's await a path to observe external cancellation.
        private static async Task<TargetCacheEntry> WithExternalCancel(Task<TargetCacheEntry> inner, CancellationToken external)
        {
            if (!external.CanBeCanceled) return await inner;
            Task completed = await Task.WhenAny(inner, Task.Delay(Timeout.Infinite, external));
            if (completed != inner) external.ThrowIfCancellationRequested();
            return await inner;
        }

        // -------------- compute --------------

        // Lifted from AltitudeSeries.ComputeYearCache (Phase 3). Pure compute: no UI access,
        // no instance state. Reads `night` (the per-location NightCache) and `target` /
        // `location` (the per-target inputs). Returns the per-target year-of-night precomputes.
        private static IReadOnlyList<NightCacheEntry> ComputeYearDays(
            Target target, Location location, NightCache night, CancellationToken ct)
        {
            double latSigned  = location.LatSigned();
            double decSigned  = target.DecSigned();
            double lonDegEast = location.LonEast();
            double raHours    = target.RightAscension;

            double meridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);

            DateTime startDay = night.YearStartDay;
            int totalDays = night.YearDays.Count;

            List<NightCacheEntry> cache = new List<NightCacheEntry>(totalDays);

            for (int day = 0; day < totalDays; day++)
            {
                ct.ThrowIfCancellationRequested();

                NightWindow nw = night.YearDays[day];
                NightCacheEntry entry = new NightCacheEntry();

                if (!nw.IsValid)
                {
                    entry.IsPolar   = true;
                    entry.SentinelX = startDay.AddDays(day).AddHours(12);
                    entry.YearAlt   = -90.0;
                    entry.MoonSamples = new List<MoonSample>(0);
                    cache.Add(entry);
                    continue;
                }

                entry.Dusk      = nw.AstronomicalDusk;
                entry.Dawn      = nw.AstronomicalDawn;
                entry.SentinelX = entry.Dawn.AddMinutes(-1);

                entry.AltDusk = AltAzCalculator.Of(target, location.With(dateTime: entry.Dusk)).Altitude;
                entry.AltDawn = AltAzCalculator.Of(target, location.With(dateTime: entry.Dawn)).Altitude;

                entry.LstDusk = SiderealTime.Local(entry.Dusk.ToUniversalTime(), lonDegEast);
                entry.LstDawn = SiderealTime.Local(entry.Dawn.ToUniversalTime(), lonDegEast);
                if (entry.LstDawn < entry.LstDusk) entry.LstDawn += 24.0;

                // Moon-aware Sessions-chart rebuild path needs per-night moon state. Sampled
                // at 10-minute cadence between Dusk and Dawn so the cache stays profile-
                // independent: the Lorentzian decision is evaluated at render time against
                // these raw samples, not pre-decided per night. ~70 samples per night per
                // target on a typical night. Each is one MoonSeparation.ObserveAt call --
                // now lock-free (Meeus-backed AstroUtil) so the per-target sweeps run in
                // parallel across threadpool cores.
                List<MoonSample> samples = new List<MoonSample>(80);
                DateTime sampleUtc = entry.Dusk;
                while (sampleUtc <= entry.Dawn)
                {
                    var observed = MoonSeparation.ObserveAt(target, location, sampleUtc);
                    samples.Add(new MoonSample(
                        utc:        sampleUtc,
                        sepDeg:     observed.SeparationDeg,
                        moonAltDeg: observed.MoonAltDeg));
                    sampleUtc = sampleUtc.Add(MoonSampleStep);
                }
                entry.MoonSamples = samples;
                DateTime midUtc = entry.Dusk.AddTicks((entry.Dawn - entry.Dusk).Ticks / 2);
                entry.MoonAgeDays = LunarAge.DaysAt(midUtc);

                entry.TransitInNight = false;
                for (int k = -1; k <= 1; k++)
                {
                    double t = raHours + 24.0 * k;
                    if (t >= entry.LstDusk && t <= entry.LstDawn)
                    {
                        entry.TransitInNight = true;
                        break;
                    }
                }

                double yearAlt = Math.Max(entry.AltDusk, entry.AltDawn);
                if (entry.TransitInNight && meridianAlt > yearAlt) yearAlt = meridianAlt;
                entry.YearAlt = yearAlt;

                cache.Add(entry);
            }

            return cache;
        }
    }
}
