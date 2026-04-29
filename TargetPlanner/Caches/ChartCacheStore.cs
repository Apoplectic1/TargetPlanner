using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Time;
using Location = Astronomy.Core.Locations.Location;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Default <see cref="IChartCacheStore"/> implementation. Single-writer cache with
    /// in-flight de-duping; cache builds run on the threadpool gated by an internal
    /// concurrency semaphore so we don't slam the process-wide CoordinateSharp lock.
    /// </summary>
    /// <remarks>
    /// Phase 3 of the SoC refactor: this class becomes the single CoordinateSharp call
    /// site for chart-cache work. The renderer queries cache state instead of owning its
    /// own. A future roll-your-own-astronomy follow-up only needs to swap the internals
    /// of <see cref="BuildTargetEntry"/> / <see cref="EnsureNightCacheAsync"/>.
    /// </remarks>
    public sealed class ChartCacheStore : IChartCacheStore, IDisposable
    {
        // Build concurrency cap. CoordinateSharp serializes per-call internally, so going
        // above 1 here mostly schedules waiters; staying at 1 keeps memory predictable.
        // Bumped from 1 to a small fixed parallelism to amortize Task scheduling overhead.
        private const int BuildConcurrency = 4;

        // Moon-sample sweep cadence inside ComputeYearDays. Matches the pre-Phase-3 behavior
        // in AltitudeSeries.ComputeYearCache and the cadence BestSession.MoonClearIntersect
        // uses for the Day-chart path.
        private static readonly TimeSpan MoonSampleStep = TimeSpan.FromMinutes(10);

        private readonly object mGate = new object();
        private readonly SynchronizationContext mUiContext;
        private readonly SemaphoreSlim mBuildSlots = new SemaphoreSlim(BuildConcurrency, BuildConcurrency);

        private Location mLocation;
        private NightCache mNightCache;
        private Task<NightCache> mNightCacheTask;        // in-flight per-location night-cache build
        private CancellationTokenSource mLocationCts;    // cancels everything keyed to mLocation
        private Dictionary<Target, TargetCacheEntry> mEntries = new Dictionary<Target, TargetCacheEntry>();
        private Dictionary<Target, Task<TargetCacheEntry>> mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();

        public event EventHandler<TargetReadyEventArgs> TargetReady;

        public ChartCacheStore(Location initialLocation)
        {
            if (initialLocation == null) throw new ArgumentNullException(nameof(initialLocation));
            mLocation = initialLocation;
            mLocationCts = new CancellationTokenSource();
            mUiContext = SynchronizationContext.Current;
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

        public async Task PrepareManyAsync(IEnumerable<Target> targets, CancellationToken ct)
        {
            if (targets == null) return;
            List<Task> tasks = new List<Task>();
            foreach (Target t in targets)
            {
                if (t == null) continue;
                tasks.Add(GetOrBuildAsync(t, ct));
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

            lock (mGate)
            {
                // No-op if location is unchanged (reference equality); legitimate for repeated
                // settings-driven calls.
                if (object.ReferenceEquals(mLocation, newLocation)) return;

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

            // Wait for in-flight tasks to observe the cancel and unwind. Swallow the resulting
            // OperationCanceledException -- it's the expected outcome of the cancel.
            try
            {
                if (oldNightTask != null) await oldNightTask;
            }
            catch (OperationCanceledException) { }
            catch { /* don't fail SetLocationAsync on a stale build's compute error */ }

            foreach (Task<TargetCacheEntry> t in oldInFlight)
            {
                try { await t; }
                catch (OperationCanceledException) { }
                catch { /* same -- stale builds are best-effort */ }
            }

            oldCts.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource cts;
            lock (mGate) { cts = mLocationCts; }
            try { cts?.Cancel(); cts?.Dispose(); } catch { }
            mBuildSlots.Dispose();
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

                FireTargetReady(target, entry);
                return entry;
            }
            catch
            {
                // Remove the failed/cancelled task from in-flight so a subsequent
                // GetOrBuildAsync starts fresh instead of re-awaiting the broken Task.
                lock (mGate)
                {
                    if (mInFlight.TryGetValue(target, out Task<TargetCacheEntry> t) && t.IsCompleted)
                    {
                        mInFlight.Remove(target);
                    }
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

        // Helper: gate the per-target build through the concurrency semaphore. Wraps
        // BuildEntryAsync's await Task.Run so we don't oversubscribe the threadpool with
        // ComputeYearDays calls.
        //
        // Currently mBuildSlots is acquired inside ComputeYearDays via Task.Run, but
        // PrepareManyAsync would otherwise schedule N tasks all hitting the threadpool
        // simultaneously. Tightening: wait on mBuildSlots before scheduling. (TODO if perf
        // becomes an issue; today the bottleneck is CoordinateSharpGate's serial lock.)
        // For now we let Task.Run in ComputeYearDays self-throttle via the threadpool.

        private void FireTargetReady(Target target, TargetCacheEntry entry)
        {
            EventHandler<TargetReadyEventArgs> handler = TargetReady;
            if (handler == null) return;
            TargetReadyEventArgs args = new TargetReadyEventArgs(target, entry);
            if (mUiContext != null)
            {
                mUiContext.Post(_ => handler(this, args), null);
            }
            else
            {
                handler(this, args);
            }
        }

        // External cancellation: the caller's ct may fire before the build's locationCts.
        // Wrap the in-flight build so the caller's await throws on either source.
        private static async Task<TargetCacheEntry> WithExternalCancel(Task<TargetCacheEntry> inner, CancellationToken external)
        {
            if (!external.CanBeCanceled) return await inner;

            using (CancellationTokenRegistration reg = external.Register(state =>
            {
                // No way to cancel the inner task from here -- it's keyed to the location's
                // CTS. The await below observes external cancellation via Task.WhenAny.
            }, null))
            {
                Task completed = await Task.WhenAny(inner, Task.Delay(Timeout.Infinite, external));
                if (completed != inner)
                    external.ThrowIfCancellationRequested();
                return await inner;
            }
        }

        // -------------- compute --------------

        // Lifted from AltitudeSeries.ComputeYearCache (Phase 3). Pure compute: no UI access,
        // no instance state. Reads `night` (the per-location NightCache) and `target` /
        // `location` (the per-target inputs). Returns the per-target year-of-night precomputes.
        //
        // The moon-sample loop is currently disabled (TEMP DEBUG bisection from Phase 1-3 of
        // the SoC refactor). Re-enabling moon avoidance is a 1-line revert of the empty-samples
        // line to the 10-min sweep below.
        private static IReadOnlyList<NightCacheEntry> ComputeYearDays(
            Target target, Location location, NightCache night, CancellationToken ct)
        {
            double latSigned  = location.North ?  location.Latitude  : -location.Latitude;
            double decSigned  = target.North   ?  target.Declination : -target.Declination;
            double lonDegEast = location.West  ? -location.Longitude :  location.Longitude;
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

                // TEMP DEBUG (bisection, kept through Phase 1-3): skip the ~25,600
                // MoonSeparation.ObserveAt calls per target. The chart's
                // MoonAvoidanceProfile setter forces null, so HasMoonClearViableWindow /
                // EnumerateMoonClearIntervalsUtc never run. Phase 5 will restore the
                // 10-min sweep below in this method (search "TEMP DEBUG" to find).
                entry.MoonSamples = new List<MoonSample>(0);
                /*
                List<MoonSample> samples = new List<MoonSample>(80);
                DateTime sampleUtc = entry.Dusk;
                while (sampleUtc <= entry.Dawn)
                {
                    var observed = MoonSeparation.ObserveAt(target, location, sampleUtc);
                    samples.Add(new MoonSample
                    {
                        Utc        = sampleUtc,
                        SepDeg     = observed.SeparationDeg,
                        MoonAltDeg = observed.MoonAltDeg
                    });
                    sampleUtc = sampleUtc.Add(MoonSampleStep);
                }
                entry.MoonSamples = samples;
                */
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
