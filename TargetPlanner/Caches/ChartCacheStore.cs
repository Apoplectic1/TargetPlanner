using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.Core;
using Astronomy.Core.Astrometry;
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

        // Per-(target, DayWindowKey) altitude-curve cache. Third axis alongside
        // yearDays (per-target) and fits (per-(target, HdmKey)). Drives the Day
        // chart's per-target altitude polyline -- AltitudeCurve.Sample is the
        // heavy call (44 targets × 1440 minutes ≈ 63k Meeus calls) and was the
        // last UI-thread hot path before this axis. DayWindowKey is independent
        // of HdmKey (altitude depends on target geometry + time, not on the
        // user's planning policy), so HDM scrubs hit warm cache and only re-run
        // the per-target ComputeBestDayWindow fit-tonight filter.
        private Dictionary<(Target, DayWindowKey), TargetDayAltitudeEntry> mDay
            = new Dictionary<(Target, DayWindowKey), TargetDayAltitudeEntry>();
        private Dictionary<(Target, DayWindowKey), Task<TargetDayAltitudeEntry>> mInFlightDay
            = new Dictionary<(Target, DayWindowKey), Task<TargetDayAltitudeEntry>>();

        // Per-DayWindowKey moon altitude curves. Singleton-style (not target-keyed)
        // since the moon is shared across all targets. SetLocationAsync clears
        // these alongside the per-target dicts. The Day chart's render reads
        // GetMoonOrNull(dayKey) instead of computing AstroUtil.GetMoonAltitude
        // per-minute inline -- same SoC win as TargetDayAltitudeEntry was for
        // per-target altitudes.
        private Dictionary<DayWindowKey, MoonAltitudeEntry> mMoon
            = new Dictionary<DayWindowKey, MoonAltitudeEntry>();
        private Dictionary<DayWindowKey, Task<MoonAltitudeEntry>> mInFlightMoon
            = new Dictionary<DayWindowKey, Task<MoonAltitudeEntry>>();

        // Last ChartContext successfully applied via EnsureAsync; drives the
        // per-axis diff flags returned in the next ChartEvaluation. Null until
        // the first EnsureAsync completes (first call's eval flags all set true
        // so sub-charts take their full Render path). Set under mGate.
        private ChartContext mLastEnsureCtx;

        // ObservationMoment.Utc most recently passed through SetLocationAsync.
        // Used by EnsureAsync to detect a date-change-without-geometry-change
        // (the user scrubbed the date picker but the named location is the
        // same): NightCache's Starting window depends on the seed UTC and
        // becomes stale on any cross-day scrub; YearStartDay can flip on a
        // cross-month scrub. Both trigger SetLocationAsync(loc, utc) so the
        // night cache rebuilds against the new anchor. Seeded at construction
        // from the caller-supplied initialUtc so the warmup path
        // (PrepareManyAsync called before any EnsureAsync) gets a usable seed
        // for EnsureNightCacheAsync.
        private DateTime mLastSetUtc;

        public ChartCacheStore(Location initialLocation, DateTime initialUtc)
        {
            if (initialLocation == null) throw new ArgumentNullException(nameof(initialLocation));
            mLocation = initialLocation;
            mLastSetUtc = initialUtc;
        }

        public Location CurrentLocation
        {
            get { lock (mGate) { return mLocation; } }
        }

        public NightCache LocationNightCache
        {
            get { lock (mGate) { return mNightCache; } }
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

        public TargetDayAltitudeEntry GetDayOrNull(Target t, DayWindowKey key)
        {
            if (t == null) return null;
            lock (mGate)
            {
                mDay.TryGetValue((t, key), out TargetDayAltitudeEntry entry);
                return entry;
            }
        }

        public Task<TargetDayAltitudeEntry> GetDayOrBuildAsync(Target t, DayWindowKey key)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            // Fast path: already published.
            TargetDayAltitudeEntry existing = GetDayOrNull(t, key);
            if (existing != null) return Task.FromResult(existing);

            lock (mGate)
            {
                if (mInFlightDay.TryGetValue((t, key), out Task<TargetDayAltitudeEntry> task))
                    return task;

                Location location = mLocation;
                task = BuildDayEntryAsync(t, key, location);
                mInFlightDay[(t, key)] = task;
                return task;
            }
        }

        public async Task PrepareDayAsync(IEnumerable<Target> targets, DayWindowKey key,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return;
            List<Task> tasks = new List<Task>();
            int completed = 0;
            foreach (Target t in targets)
            {
                if (t == null) continue;
                Task<TargetDayAltitudeEntry> build = GetDayOrBuildAsync(t, key);
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

        public MoonAltitudeEntry GetMoonOrNull(DayWindowKey key)
        {
            lock (mGate)
            {
                mMoon.TryGetValue(key, out MoonAltitudeEntry entry);
                return entry;
            }
        }

        public Task<MoonAltitudeEntry> GetMoonOrBuildAsync(DayWindowKey key)
        {
            // Fast path: already published.
            MoonAltitudeEntry existing = GetMoonOrNull(key);
            if (existing != null) return Task.FromResult(existing);

            lock (mGate)
            {
                if (mInFlightMoon.TryGetValue(key, out Task<MoonAltitudeEntry> task))
                    return task;

                Location location = mLocation;
                task = BuildMoonEntryAsync(key, location);
                mInFlightMoon[key] = task;
                return task;
            }
        }

        public Task PrepareMoonAsync(DayWindowKey key) => GetMoonOrBuildAsync(key);

        // -------------- single-entry pipeline --------------

        public async Task<ChartEvaluation> EnsureAsync(ChartContext ctx, DayWindowKey dayKey)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // Capture prev under the lock so the diff sees a consistent snapshot.
            // The compute below runs unlocked (await), which is fine because every
            // downstream Prepare path is itself locked + idempotent.
            ChartContext prev;
            lock (mGate) { prev = mLastEnsureCtx; }

            bool locationChanged = prev == null
                || !LocationCacheEquivalent(prev.Location, ctx.Location);
            bool targetsChanged = prev == null
                || !TargetsEqualByReference(prev.Targets, ctx.Targets);
            bool hdmChanged = prev == null || prev.Hdm != ctx.Hdm;
            bool dayModeChanged = prev == null || prev.DayMode != ctx.DayMode;
            bool brightnessChanged = prev == null
                || ctx.Location.BortleClass != prev.Location.BortleClass
                || ctx.Location.ExtinctionK != prev.Location.ExtinctionK
                || ctx.Policy.FilterCenterNm != prev.Policy.FilterCenterNm;

            // Detect date-change-without-geometry-change. NightCache's Starting
            // window and YearStartDay both depend on the seed UTC; any cross-day
            // scrub stales the Starting slot, any cross-month scrub stales the
            // YearStartDay anchor (and thus the whole year-cache). Treat both
            // as cache-busting events on par with a true geometry change so
            // SetLocationAsync rebuilds against the new anchor.
            DateTime prevUtc;
            lock (mGate) { prevUtc = mLastSetUtc; }
            // First EnsureAsync at a same-date ctor anchor doesn't need to
            // SetLocationAsync (the cache is already keyed correctly via the
            // ctor's seed). Only mark dateChanged when the date axis actually
            // moved off the ctor anchor or a previous SetLocationAsync's anchor.
            bool dateChanged =
                prevUtc.Date != ctx.Observation.Utc.Date
                || NightCache.ComputeYearStartDay(prevUtc)
                   != NightCache.ComputeYearStartDay(ctx.Observation.Utc);

            if (Log.IsDiagEnabled("Cache"))
            {
                Log.Diag("Cache",
                    $"EnsureAsync enter prevNull={prev == null} locChanged={locationChanged} " +
                    $"dateChanged={dateChanged} tgtChanged={targetsChanged} hdmChanged={hdmChanged} " +
                    $"dayModeChanged={dayModeChanged} brightnessChanged={brightnessChanged} " +
                    $"targets={ctx.Targets?.Count ?? 0} dayKey.Count={dayKey.Count}");
            }

            // 1. Re-key cache on geometry or date change. Skip SetLocationAsync
            //    entirely when both LocationCacheEquivalent and dateChanged report
            //    unchanged -- SetLocationAsync's own ReferenceEquals fast path
            //    doesn't help when the form re-creates Location instances on
            //    every UI tick even for value-unchanged saves.
            if (locationChanged || dateChanged)
            {
                await SetLocationAsync(ctx.Location, ctx.Observation.Utc);
            }

            // Surface date-change to downstream diff: a date-only scrub at the
            // same site should still report LocationChanged=true to consumers
            // (sub-charts and post-apply hooks) because the cache contents
            // genuinely cleared. Keeps the public eval semantics aligned with
            // what actually happened underneath.
            locationChanged = locationChanged || dateChanged;

            // 2a. Moon altitudes are TARGET-INDEPENDENT (function of Location +
            //     night only). Prep unconditionally when dayKey is valid so
            //     Day's startup Render (with empty targets, before NINA load
            //     completes) hits a warm moon cache instead of the defensive
            //     inline fallback WARN.
            if (dayKey.Count > 0)
            {
                await PrepareMoonAsync(dayKey);
            }

            // 2b. Per-target prep: yearDays + fits + per-night Day altitudes.
            //     Each Prepare path is internally idempotent per cache key, so
            //     repeated EnsureAsync calls with the same ctx settle in the
            //     per-key fast paths.
            if (ctx.Targets != null && ctx.Targets.Count > 0)
            {
                await PrepareManyAsync(ctx.Targets);
                await PrepareFitsAsync(ctx.Targets, ctx.Hdm, ctx.Policy.LocalHorizon);

                // dayKey.Count == 0 sentinels "no valid Day window" (polar
                // night). Day chart's Render handles the blank-chart case from
                // cache.GetDayOrNull returning null; skip the prep.
                if (dayKey.Count > 0)
                {
                    await PrepareDayAsync(ctx.Targets, dayKey);
                }
            }

            // 3. Stamp ctx as last-applied for the NEXT EnsureAsync's diff.
            //    CAS-style: only stamp if no concurrent EnsureAsync stamped a
            //    different ctx while we awaited above. Without this guard, two
            //    overlapping pipelines (coordinator generations N and N+1 both
            //    in flight when the debounce tick fires twice) can race at the
            //    stamp -- if N+1 stamps first then N stamps second, the cache
            //    would advertise ctxN as the latest even though ctxN+1 is what
            //    actually rendered (generation guard in the coordinator gates
            //    render to the newest gen, but the cache stamp has no such
            //    ordering). The CAS prevents the stale-clobbers-fresh
            //    direction; the reverse (N stamps first, N+1's CAS then fails)
            //    leaves the older stamp in place, which is still safe because
            //    cache contents are correct either way (per-key Prepare is
            //    idempotent) and the next EnsureAsync's eval flags will be
            //    over-invalidating, not under-invalidating -- the caller does
            //    extra work, never wrong work. A fully-ordered fix would pipe
            //    coordinator generation into the cache; deferred since today's
            //    sub-charts don't consume eval flags (Phase 7 reverted) and
            //    over-invalidation is benign for any future flag consumer too.
            //
            //    If any await above threw, mLastEnsureCtx stays at the prior
            //    value -- next call's eval reflects the bailed pipeline's
            //    intended-but-incomplete state as "still stale", matching
            //    the cache's actual contents.
            lock (mGate)
            {
                if (ReferenceEquals(mLastEnsureCtx, prev)) mLastEnsureCtx = ctx;
            }

            if (Log.IsDiagEnabled("Cache"))
            {
                int fitCount, dayCount, moonCount;
                lock (mGate) { fitCount = mFits.Count; dayCount = mDay.Count; moonCount = mMoon.Count; }
                Log.Diag("Cache",
                    $"EnsureAsync exit mFits.Count={fitCount} mDay.Count={dayCount} mMoon.Count={moonCount}");
            }

            return new ChartEvaluation
            {
                LocationChanged = locationChanged,
                TargetsChanged = targetsChanged,
                HdmChanged = hdmChanged,
                DayModeChanged = dayModeChanged,
                BrightnessInputsChanged = brightnessChanged,
                DayKey = dayKey,
                HdmKey = ctx.Hdm,
                DayMode = ctx.DayMode,
            };
        }

        private static bool TargetsEqualByReference(IReadOnlyList<Target> a, IReadOnlyList<Target> b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!object.ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        // Value-equivalent comparison on the Location fields the cache itself
        // keys against -- pure geometry (lat/lon/N/W/elevation) post-Phase-2.
        // Date-anchored axes (NightCache.Starting / YearStartDay) are tracked
        // separately via mLastSetUtc in EnsureAsync. Bortle / ExtinctionK /
        // TimeZoneInfo are NOT included here -- brightness inputs ride the
        // separate BrightnessInputsChanged flag; TZ identity doesn't affect
        // cache contents either way. Used by EnsureAsync so a ref-different
        // / value-equivalent ctx (form re-creating Location on every
        // NumericUpDown tick) doesn't trash the cache.
        private static bool LocationCacheEquivalent(Location a, Location b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Latitude == b.Latitude
                && a.Longitude == b.Longitude
                && a.North == b.North
                && a.West == b.West
                && a.Elevation == b.Elevation;
        }

        public async Task SetLocationAsync(Location newLocation, DateTime startingUtc)
        {
            if (newLocation == null) throw new ArgumentNullException(nameof(newLocation));

            Task<NightCache> oldNightTask;
            ICollection<Task<TargetCacheEntry>> oldInFlight;
            ICollection<Task<TargetFitEntry>> oldInFlightFits;
            ICollection<Task<TargetDayAltitudeEntry>> oldInFlightDay;
            ICollection<Task<MoonAltitudeEntry>> oldInFlightMoon;

            lock (mGate)
            {
                // No-op if location is unchanged (reference equality) AND the
                // startingUtc anchor is the same; legitimate for repeated
                // settings-driven calls. A pure date scrub at the same site
                // proceeds with the rebuild because the anchor moved.
                if (object.ReferenceEquals(mLocation, newLocation)
                    && mLastSetUtc == startingUtc) return;

                oldNightTask = mNightCacheTask;
                oldInFlight = mInFlight.Values.ToList();
                oldInFlightFits = mInFlightFits.Values.ToList();
                oldInFlightDay = mInFlightDay.Values.ToList();
                oldInFlightMoon = mInFlightMoon.Values.ToList();

                // Reset state for the new location. Old in-flight builds keep running and
                // discard themselves at publish via the ReferenceEquals(mLocation, location)
                // check in BuildEntryAsync / BuildFitEntryAsync / BuildDayEntryAsync /
                // BuildMoonEntryAsync.
                mLocation = newLocation;
                mLastSetUtc = startingUtc;
                mNightCache = null;
                mNightCacheTask = null;
                mEntries = new Dictionary<Target, TargetCacheEntry>();
                mInFlight = new Dictionary<Target, Task<TargetCacheEntry>>();
                mFits = new Dictionary<(Target, HdmKey), TargetFitEntry>();
                mInFlightFits = new Dictionary<(Target, HdmKey), Task<TargetFitEntry>>();
                mDay = new Dictionary<(Target, DayWindowKey), TargetDayAltitudeEntry>();
                mInFlightDay = new Dictionary<(Target, DayWindowKey), Task<TargetDayAltitudeEntry>>();
                mMoon = new Dictionary<DayWindowKey, MoonAltitudeEntry>();
                mInFlightMoon = new Dictionary<DayWindowKey, Task<MoonAltitudeEntry>>();
            }

            // Wait for in-flight tasks (against the old location) to finish so callers
            // who await SetLocationAsync don't continue while stale work is still
            // touching the threadpool. Stale-publish is harmless (TryPublish's
            // ReferenceEquals check drops them); we just want the wait for hygiene.
            // Exceptions thrown by stale builds are logged via the local SafeAwait
            // helper and don't fail SetLocationAsync. Concurrent vs sequential
            // await doesn't matter -- the tasks are already running; we're only
            // awaiting their completion.
            static async Task SafeAwait(Task task, string warnContext)
            {
                try { await task; }
                catch (Exception ex) { Log.Warn(warnContext, ex); }
            }

            List<Task> staleAwaits = new List<Task>();
            if (oldNightTask != null)
                staleAwaits.Add(SafeAwait(oldNightTask,
                    "Stale NightCache build threw during SetLocationAsync"));
            foreach (Task<TargetCacheEntry> t in oldInFlight)
                staleAwaits.Add(SafeAwait(t,
                    "Stale per-target build threw during SetLocationAsync"));
            foreach (Task<TargetFitEntry> t in oldInFlightFits)
                staleAwaits.Add(SafeAwait(t,
                    "Stale per-(target, HdmKey) fit build threw during SetLocationAsync"));
            foreach (Task<TargetDayAltitudeEntry> t in oldInFlightDay)
                staleAwaits.Add(SafeAwait(t,
                    "Stale per-(target, DayWindowKey) altitude build threw during SetLocationAsync"));
            foreach (Task<MoonAltitudeEntry> t in oldInFlightMoon)
                staleAwaits.Add(SafeAwait(t,
                    "Stale per-DayWindowKey moon altitude build threw during SetLocationAsync"));
            await Task.WhenAll(staleAwaits);
        }

        public void Dispose()
        {
            // No cancellation-related state to clean up since the cancellation removal pass
            // (Phase 1 of the SoC-completion refactor). In-flight tasks are orphaned on
            // Dispose; their publish-time stale check ensures they don't write into a
            // disposed store's state.
        }

        // -------------- internals --------------

        // Publish a successfully-built entry into <paramref name="store"/> when the
        // build's source location is still current. Removes from
        // <paramref name="inFlight"/> on a match. Returns true on publish; false
        // when a SetLocationAsync swap orphaned the build (caller discards the
        // entry by simply not using the published copy -- the local entry is
        // still returned to the immediate caller). Used by every BuildXxxAsync
        // method so the lock + ReferenceEquals + publish + in-flight-remove
        // pattern lives in exactly one place.
        private bool TryPublish<TKey, TVal>(
            Dictionary<TKey, TVal> store,
            Dictionary<TKey, Task<TVal>> inFlight,
            TKey key, TVal value, Location buildLocation)
        {
            lock (mGate)
            {
                if (!object.ReferenceEquals(mLocation, buildLocation)) return false;
                store[key] = value;
                inFlight.Remove(key);
                return true;
            }
        }

        // Drop a faulted task from <paramref name="inFlight"/> so the next
        // GetOrBuildAsync starts fresh instead of re-awaiting the broken Task.
        // Skipped when a SetLocationAsync swap already discarded our dict
        // (the new dict doesn't contain our key anyway). Mirrors TryPublish's
        // location guard.
        private void DropOnFault<TKey, TVal>(
            Dictionary<TKey, Task<TVal>> inFlight, TKey key, Location buildLocation)
        {
            lock (mGate)
            {
                if (object.ReferenceEquals(mLocation, buildLocation))
                    inFlight.Remove(key);
            }
        }

        private async Task<TargetCacheEntry> BuildEntryAsync(Target target, Location location)
        {
            try
            {
                NightCache night = await EnsureNightCacheAsync(location);

                IReadOnlyList<NightCacheEntry> yearDays = await Task.Run(
                    () => ComputeYearDays(target, location, night));

                TargetCacheEntry entry = new TargetCacheEntry(target, yearDays);
                TryPublish(mEntries, mInFlight, target, entry, location);
                return entry;
            }
            catch
            {
                DropOnFault(mInFlight, target, location);
                throw;
            }
        }

        // Per-(target, HdmKey) fit build. Awaits yearDays for the target (de-duped via
        // the existing mInFlight path), then walks each night computing the Sessions
        // Ceiling / Floor / CenteredFloor triple. Year reads Floor; both share the
        // upstream BestSession.ResolveCandidates resolve so one resolve drives both
        // placements. The tail also computes a single-night Tonight fit from the
        // NightCache.Starting window so Day's HD-overlay box and Sky's
        // hide-on-no-fit read from the same cache (one source of truth for the
        // fit decision; zero UI-thread Library calls in render).
        private async Task<TargetFitEntry> BuildFitEntryAsync(Target target, HdmKey key,
            Location location, IHorizonProfile horizon)
        {
            try
            {
                TargetCacheEntry yearEntry = await GetOrBuildAsync(target);
                IReadOnlyList<NightCacheEntry> yearDays = yearEntry.YearDays;
                TimeSpan duration = TimeSpan.FromTicks(key.DurationTicks);
                MoonAvoidanceProfile profile = key.Profile;
                NightCache nightCache = await EnsureNightCacheAsync(location);
                NightWindow starting = nightCache.Starting;

                (IReadOnlyList<NightFit> nights, NightFit tonight) = await Task.Run(
                    () => (
                        ComputeNightFits(target, location, yearDays, horizon, duration, profile),
                        ComputeTonightFit(target, location, starting, horizon, duration, profile)));

                TargetFitEntry entry = new TargetFitEntry(target, key, nights, tonight);

                if (Log.IsDiagEnabled("Cache"))
                {
                    Log.Diag("Cache",
                        $"BuildFit target={target.Name} hdmKey=(H={key.HorizonDeg},Dt={key.DurationTicks},FNm={key.FilterCenterNm}) " +
                        $"durationSec={duration.TotalSeconds:F0} startingValid={starting.IsValid} " +
                        $"tonightHasFloor={tonight.Floor.HasValue}");
                }

                TryPublish(mFits, mInFlightFits, (target, key), entry, location);
                return entry;
            }
            catch
            {
                DropOnFault(mInFlightFits, (target, key), location);
                throw;
            }
        }

        // Per-(target, DayWindowKey) altitude-curve build. AltitudeCurve.Sample on a
        // threadpool thread; the result is the minute-spaced altitudes the Day chart
        // paints. Independent of HdmKey (altitude is a function of geometry + time,
        // not user policy), so the cache hits across HDM scrubs.
        private async Task<TargetDayAltitudeEntry> BuildDayEntryAsync(Target target, DayWindowKey key, Location location)
        {
            try
            {
                DateTime startUtc = key.ChartStartUtc;
                int count = key.Count;

                IReadOnlyList<double> altitudes = await Task.Run(
                    () => AltitudeCurve.Sample(target, location, startUtc, TimeSpan.FromMinutes(1), count));

                TargetDayAltitudeEntry entry = new TargetDayAltitudeEntry(target, key, altitudes);
                TryPublish(mDay, mInFlightDay, (target, key), entry, location);
                return entry;
            }
            catch
            {
                DropOnFault(mInFlightDay, (target, key), location);
                throw;
            }
        }

        // Per-DayWindowKey moon altitude-curve build. Singleton per night-window
        // (the moon is shared across all targets at a location). AstroUtil.GetMoonAltitude
        // on a threadpool thread; the result is the minute-spaced geometric altitudes
        // the Day chart's moon plot reads. Stored geometric -- callers needing
        // apparent altitude (K-S brightness gate) apply Saemundsson refraction.
        private async Task<MoonAltitudeEntry> BuildMoonEntryAsync(DayWindowKey key, Location location)
        {
            try
            {
                DateTime startUtc = key.ChartStartUtc;
                int count = key.Count;
                double latSigned = location.LatSigned();
                double lonEast = location.LonEast();
                ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

                IReadOnlyList<double> altitudes = await Task.Run(() =>
                {
                    double[] arr = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        DateTime pointUtc = DateTime.SpecifyKind(
                            startUtc.AddMinutes(i), DateTimeKind.Utc);
                        arr[i] = AstroUtil.GetMoonAltitude(pointUtc, observer);
                    }
                    return arr;
                });

                MoonAltitudeEntry entry = new MoonAltitudeEntry(key, altitudes);
                TryPublish(mMoon, mInFlightMoon, key, entry, location);
                return entry;
            }
            catch
            {
                DropOnFault(mInFlightMoon, key, location);
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
                DateTime seed = mLastSetUtc;
                Task<NightCache> task = Task.Run(() =>
                {
                    DateTime startDay = NightCache.ComputeYearStartDay(seed);
                    int days = NightCache.ComputeYearDaysCount(seed);
                    return new NightCache(loc, seed, startDay, days);
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

                entry.AltDusk = AltAzCalculator.At(target, location, entry.Dusk).Altitude;
                entry.AltDawn = AltAzCalculator.At(target, location, entry.Dawn).Altitude;

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
                    StartUtc = session?.Start,
                    EndUtc   = session?.End,
                    CenteredStartUtc = centered?.Start,
                    CenteredEndUtc   = centered?.End,
                };
            }

            return fits;
        }

        // Single-night equivalent of ComputeNightFits' loop body, for the
        // LocationNightCache.Starting slot consumed by Day's HD-overlay and
        // Sky's hide-on-no-fit. Same recipe (ResolveCandidates + PlaceBest +
        // SessionAltitude.Floor / Ceiling + PlaceCentered) -- so the cached
        // Tonight fit is byte-identical to what Day's ad-hoc BestSession.For
        // produced pre-consolidation.
        private static NightFit ComputeTonightFit(
            Target target, Location location, NightWindow starting,
            IHorizonProfile horizonProfile, TimeSpan duration, MoonAvoidanceProfile profile)
        {
            if (duration <= TimeSpan.Zero || !starting.IsValid) return default;

            var candidates = BestSession.ResolveCandidates(
                target, location, starting, horizonProfile, profile);
            if (candidates.Count == 0) return default;

            double? floor = null, ceiling = null;
            DateTime? startUtc = null, endUtc = null;
            var session = BestSession.PlaceBest(target, location, candidates, duration, duration);
            if (session != null)
            {
                floor    = SessionAltitude.Floor(target, location, session.Value.Start, session.Value.End);
                ceiling  = SessionAltitude.Ceiling(target, location, session.Value.Start, session.Value.End);
                startUtc = session.Value.Start;
                endUtc   = session.Value.End;
            }

            double? centeredFloor = null;
            var centered = BestSession.PlaceCentered(target, location, candidates, duration);
            if (centered != null)
            {
                centeredFloor = SessionAltitude.Floor(target, location,
                    centered.Value.Start, centered.Value.End);
            }

            return new NightFit
            {
                Ceiling = ceiling,
                Floor = floor,
                CenteredFloor = centeredFloor,
                StartUtc = startUtc,
                EndUtc   = endUtc,
                CenteredStartUtc = centered?.Start,
                CenteredEndUtc   = centered?.End,
            };
        }
    }
}
