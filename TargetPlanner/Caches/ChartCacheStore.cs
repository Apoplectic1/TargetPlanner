using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Time;
using TargetPlanner.State;
using TargetPlanner.Support;
using Location = Astronomy.Core.Locations.Location;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Default <see cref="IChartCacheStore"/> implementation. Single-writer cache with
    /// in-flight de-duping; cache builds run on the threadpool.
    /// </summary>
    /// <remarks>
    /// Phase 3 of the SoC refactor: the renderer queries cache state instead of owning
    /// its own. Astronomy.Core's Meeus implementation is lock-free, so per-target year
    /// builds run in parallel across threadpool cores.
    ///
    /// <para><b>Cancellation policy.</b> The cache itself does not cancel in-flight
    /// builds on a Location swap; <see cref="SetLocationAsync"/> just drops the cache
    /// dicts under the lock and starts fresh. Builds that were running against the
    /// old location keep going on the threadpool and discard themselves at publish
    /// time via the <c>ReferenceEquals(mLocation, location)</c> check inside
    /// <see cref="BuildEntryAsync"/>. Compute is short (~1-2 sec for 44 targets
    /// post-CS-removal); the wasted CPU is bounded by per-(target, location) build
    /// dedupe and by the fact that location swaps are user-initiated and infrequent.
    /// </para>
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
        private Dictionary<Target, TargetCacheEntry> mEntries = new Dictionary<Target, TargetCacheEntry>();
        private Dictionary<Target, Task<TargetCacheEntry>> mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();

        // Per-(target, HdmKey) fit cache. Sibling axis to mEntries (per-target yearDays).
        // SetLocationAsync clears both. HdmKey changes invalidate fits but preserve yearDays
        // so H/D/M scrubs don't re-pay the per-(target, location) moon-sample sweep.
        private Dictionary<(Target, HdmKey), TargetFitEntry> mFits
            = new Dictionary<(Target, HdmKey), TargetFitEntry>();
        private Dictionary<(Target, HdmKey), Task<TargetFitEntry>> mInFlightFits
            = new Dictionary<(Target, HdmKey), Task<TargetFitEntry>>();

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

        public Task<TargetCacheEntry> GetOrBuildAsync(Target t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            // Fast path: already published.
            TargetCacheEntry existing = GetOrNull(t);
            if (existing != null) return Task.FromResult(existing);

            lock (mGate)
            {
                // In-flight de-dupe.
                if (mInFlight.TryGetValue(t, out Task<TargetCacheEntry> task)) return task;

                Location location = mLocation;
                task = BuildEntryAsync(t, location);
                mInFlight[t] = task;
                return task;
            }
        }

        public async Task PrepareManyAsync(IEnumerable<Target> targets,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return;
            List<Task> tasks = new List<Task>();
            int completed = 0;
            foreach (Target t in targets)
            {
                if (t == null) continue;
                Task<TargetCacheEntry> build = GetOrBuildAsync(t);
                // Always include the build task itself so WhenAll observes its
                // original fault (if any) rather than only the continuation's
                // OnlyOnRanToCompletion cancellation. The continuation is a
                // best-effort progress tick; faulted builds skip the tick but
                // their exception still propagates via the build entry below.
                tasks.Add(build);
                if (targetCompleteProgress != null)
                {
                    // Synchronous continuation keeps the increment cheap; Progress<T>.Report
                    // internally marshals the callback to the captured SyncContext (UI thread).
                    tasks.Add(build.ContinueWith(
                        _ => targetCompleteProgress.Report(Interlocked.Increment(ref completed)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));
                }
            }
            await Task.WhenAll(tasks);
        }

        public TargetFitEntry GetFitOrNull(Target t, HdmKey key)
        {
            if (t == null) return null;
            lock (mGate)
            {
                mFits.TryGetValue((t, key), out TargetFitEntry entry);
                return entry;
            }
        }

        public Task<TargetFitEntry> GetFitOrBuildAsync(Target t, HdmKey key, IHorizonProfile horizon)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));

            // Fast path: already published.
            TargetFitEntry existing = GetFitOrNull(t, key);
            if (existing != null) return Task.FromResult(existing);

            lock (mGate)
            {
                // In-flight de-dupe. We trust that callers obey the contract that for a
                // given HdmKey the IHorizonProfile is functionally equivalent across calls
                // (today HdmKey.HorizonDeg uniquely identifies the scalar profile; the PR-5
                // local-horizon work will extend the key with the profile reference).
                if (mInFlightFits.TryGetValue((t, key), out Task<TargetFitEntry> task))
                    return task;

                Location location = mLocation;
                task = BuildFitEntryAsync(t, key, location, horizon);
                mInFlightFits[(t, key)] = task;
                return task;
            }
        }

        public async Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
            IHorizonProfile horizon, IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return;
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));
            List<Task> tasks = new List<Task>();
            int completed = 0;
            foreach (Target t in targets)
            {
                if (t == null) continue;
                Task<TargetFitEntry> build = GetFitOrBuildAsync(t, key, horizon);
                // Always include the build task itself -- see PrepareManyAsync for
                // the rationale (the continuation's OnlyOnRanToCompletion would
                // otherwise mask the original build fault on a WhenAll).
                tasks.Add(build);
                if (targetCompleteProgress != null)
                {
                    tasks.Add(build.ContinueWith(
                        _ => targetCompleteProgress.Report(Interlocked.Increment(ref completed)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default));
                }
            }
            await Task.WhenAll(tasks);
        }

        public async Task SetLocationAsync(Location newLocation)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));

            Task<NightCache> oldNightTask;
            ICollection<Task<TargetCacheEntry>> oldInFlight;
            ICollection<Task<TargetFitEntry>> oldInFlightFits;
            Location oldLocation;

            lock (mGate)
            {
                // No-op if location is unchanged (reference equality); legitimate for repeated
                // settings-driven calls.
                if (object.ReferenceEquals(mLocation, newLocation)) return;

                oldLocation = mLocation;
                oldNightTask = mNightCacheTask;
                oldInFlight = mInFlight.Values.ToList();
                oldInFlightFits = mInFlightFits.Values.ToList();

                // Reset state for the new location. Old in-flight builds keep running and
                // discard themselves at publish via the ReferenceEquals(mLocation, location)
                // check in BuildEntryAsync / BuildFitEntryAsync.
                mLocation = newLocation;
                mNightCache = null;
                mNightCacheTask = null;
                mEntries = new Dictionary<Target, TargetCacheEntry>();
                mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();
                mFits = new Dictionary<(Target, HdmKey), TargetFitEntry>();
                mInFlightFits = new Dictionary<(Target, HdmKey), Task<TargetFitEntry>>();
            }

            // Fire LocationChanged immediately after the swap commits, before awaiting
            // the in-flight unwind. Subscribers reading CurrentLocation see the new
            // location and can blank UI / schedule re-renders without waiting on stale
            // builds to settle.
            FireLocationChanged(oldLocation, newLocation);

            // Wait for in-flight tasks (against the old location) to finish so callers
            // who await SetLocationAsync don't continue while stale work is still
            // touching the threadpool. Stale-publish is harmless (the ReferenceEquals
            // check drops them); we just want the wait for hygiene. Exceptions thrown
            // by stale builds are logged but don't fail SetLocationAsync.
            try
            {
                if (oldNightTask != null) await oldNightTask;
            }
            catch (Exception ex) { Log.Warn("Stale NightCache build threw during SetLocationAsync", ex); }

            foreach (Task<TargetCacheEntry> t in oldInFlight)
            {
                try { await t; }
                catch (Exception ex) { Log.Warn("Stale per-target build threw during SetLocationAsync", ex); }
            }

            foreach (Task<TargetFitEntry> t in oldInFlightFits)
            {
                try { await t; }
                catch (Exception ex) { Log.Warn("Stale per-(target, HdmKey) fit build threw during SetLocationAsync", ex); }
            }
        }

        public void Dispose()
        {
            // No cancellation-related state to clean up since the cancellation removal pass
            // (Phase 1 of the SoC-completion refactor). In-flight tasks are orphaned on
            // Dispose; their publish-time stale check ensures they don't write into a
            // disposed store's state.
        }

        // -------------- internals --------------

        private async Task<TargetCacheEntry> BuildEntryAsync(Target target, Location location)
        {
            try
            {
                NightCache night = await EnsureNightCacheAsync(location);

                IReadOnlyList<NightCacheEntry> yearDays = await Task.Run(
                    () => ComputeYearDays(target, location, night));

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
                // Remove the failed task from in-flight so a subsequent GetOrBuildAsync starts
                // fresh instead of re-awaiting the broken Task. Mirrors the success-path
                // location guard above: if SetLocationAsync swapped mInFlight while we were
                // building, the new dict doesn't contain us anyway -- leave it alone.
                lock (mGate)
                {
                    if (object.ReferenceEquals(mLocation, location))
                        mInFlight.Remove(target);
                }
                throw;
            }
        }

        // Per-(target, HdmKey) fit build. Awaits yearDays for the target (de-duped via
        // the existing mInFlight path), then walks each night computing the Sessions
        // Ceiling / Floor / CenteredFloor triple. Year reads Floor; both share the
        // upstream BestSession.ResolveCandidates resolve so one resolve drives both
        // placements.
        private async Task<TargetFitEntry> BuildFitEntryAsync(Target target, HdmKey key,
            Location location, IHorizonProfile horizon)
        {
            try
            {
                TargetCacheEntry yearEntry = await GetOrBuildAsync(target);
                IReadOnlyList<NightCacheEntry> yearDays = yearEntry.YearDays;
                TimeSpan duration = TimeSpan.FromTicks(key.DurationTicks);
                MoonAvoidanceProfile profile = key.Profile;

                IReadOnlyList<NightFit> nights = await Task.Run(
                    () => ComputeNightFits(target, location, yearDays, horizon, duration, profile));

                TargetFitEntry entry = new TargetFitEntry(target, key, nights);

                lock (mGate)
                {
                    // Discard if location changed mid-build (the publish would corrupt the new
                    // location's cache).
                    if (!object.ReferenceEquals(mLocation, location)) return entry;
                    mFits[(target, key)] = entry;
                    mInFlightFits.Remove((target, key));
                }
                return entry;
            }
            catch
            {
                lock (mGate)
                {
                    if (object.ReferenceEquals(mLocation, location))
                        mInFlightFits.Remove((target, key));
                }
                throw;
            }
        }

        private Task<NightCache> EnsureNightCacheAsync(Location location)
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
                    return new NightCache(loc, startDay, days);
                });

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

        // -------------- compute --------------

        // Lifted from AltitudeSeries.ComputeYearCache (Phase 3). Pure compute: no UI access,
        // no instance state. Reads `night` (the per-location NightCache) and `target` /
        // `location` (the per-target inputs). Returns the per-target year-of-night precomputes.
        private static IReadOnlyList<NightCacheEntry> ComputeYearDays(
            Target target, Location location, NightCache night)
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

        // Per-(target, HdmKey) fit walk. Lifted from the pre-cache Year + Sessions
        // sub-chart bg tasks. One BestSession.ResolveCandidates resolve per night
        // drives both PlaceBest (Ceiling + Floor) and PlaceCentered (CenteredFloor) --
        // the net cost is ~25% lower than today's separate Year / Sessions paths,
        // which each re-resolved internally via BestSession.For.
        //
        // Geometric pre-rejection (polar / sub-horizon / duration<=0) returns the
        // null-fit row directly without touching BestSession at all.
        private static IReadOnlyList<NightFit> ComputeNightFits(
            Target target, Location location, IReadOnlyList<NightCacheEntry> yearDays,
            IHorizonProfile horizonProfile, TimeSpan duration, MoonAvoidanceProfile profile)
        {
            // Scalar lower bound for the geometric pre-rejection. For a scalar
            // profile this equals TargetFloorDeg; for a polyline profile it's the
            // lowest sample. A target whose YearAlt is below this is below the
            // profile everywhere, so the pre-rejection is conservative-safe.
            double minHorizonDeg = horizonProfile.MinAltitude;
            int n = yearDays.Count;
            NightFit[] fits = new NightFit[n];

            for (int i = 0; i < n; i++)
            {
                NightCacheEntry night = yearDays[i];
                if (night.IsPolar || night.YearAlt < minHorizonDeg || duration <= TimeSpan.Zero)
                {
                    continue;        // default(NightFit) — all nullable doubles null
                }

                NightWindow nw = new NightWindow
                {
                    AstronomicalDusk = night.Dusk,
                    AstronomicalDawn = night.Dawn,
                    LunarIlluminationFraction = 0,
                };

                var candidates = BestSession.ResolveCandidates(
                    target, location, nw, horizonProfile, profile);
                if (candidates.Count == 0) continue;

                // PlaceBest: transit-centered-or-wall-pushed placement. altitudeQuality
                // default (null) dispatches to the sin(alt) closed-form path inside
                // BestSession.PlaceBestInternal -- ~25x faster than the Simpson lambda.
                double? floor = null, ceiling = null;
                var session = BestSession.PlaceBest(target, location, candidates, duration, duration);
                if (session != null)
                {
                    floor   = SessionAltitude.Floor(target, location, session.Value.Start, session.Value.End);
                    ceiling = SessionAltitude.Ceiling(target, location, session.Value.Start, session.Value.End);
                }

                // PlaceCentered: strict-centered placement; null when transit doesn't
                // fit with positive room on both sides.
                double? centeredFloor = null;
                var centered = BestSession.PlaceCentered(target, location, candidates, duration);
                if (centered != null)
                {
                    centeredFloor = SessionAltitude.Floor(target, location,
                        centered.Value.Start, centered.Value.End);
                }

                fits[i] = new NightFit
                {
                    Ceiling = ceiling,
                    Floor = floor,
                    CenteredFloor = centeredFloor,
                };
            }

            return fits;
        }
    }
}
