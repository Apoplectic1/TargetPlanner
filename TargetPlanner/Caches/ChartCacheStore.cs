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
        // Moon-sample sweep cadence inside ComputeYearDays. Matches the cadence
        // BestSession.MoonClearIntersect uses for the Day-chart path. 1-minute cadence
        // mirrors what ISP will use, so TP's gate decisions align minute-for-minute with
        // the Sky chart's per-minute K-S compute (no Sky-chart-vs-gate disagreement on
        // narrow moon transitions).
        private static readonly TimeSpan MoonSampleStep = TimeSpan.FromMinutes(1);

        private readonly object mGate = new object();

        private Location mLocation;
        private NightCache mNightCache;
        private Task<NightCache> mNightCacheTask;        // in-flight per-location night-cache build

        // The four cache axes. Each CacheAxis owns its store + in-flight dicts
        // and the get / build / in-flight-dedupe / publish lifecycle; all four
        // share mGate and discard stale builds via the () => mLocation accessor.
        // yearDays is per-target; fits per-(target, HdmKey) -- an HdmKey change
        // invalidates fits but preserves yearDays so H/D/M scrubs don't re-pay
        // the per-(target, location) moon-sample sweep; day is per-(target,
        // NightDate) -- the Day chart's trajectory polyline (AltAz per minute),
        // independent of HdmKey; moon is per-NightDate -- target-independent (the
        // moon is shared across all targets at a location). SetLocationAsync
        // drains + resets all four. Constructed in the ctor after mLocation is
        // seeded.
        private readonly CacheAxis<Target, TargetCacheEntry> mYearDaysAxis;
        private readonly CacheAxis<(Target, HdmKey), TargetFitEntry> mFitsAxis;
        private readonly CacheAxis<(Target, NightDate), TargetTrajectoryEntry> mDayAxis;
        private readonly CacheAxis<NightDate, MoonEphemerisEntry> mMoonAxis;

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

            // Build the axes after mLocation is seeded; the () => mLocation
            // accessor reads it live, so a later SetLocationAsync swap is seen
            // by in-flight builds' stale-discard check.
            mYearDaysAxis = new CacheAxis<Target, TargetCacheEntry>(
                mGate, () => mLocation,
                (key, loc) => BuildEntryAsync(key, loc));
            mFitsAxis = new CacheAxis<(Target, HdmKey), TargetFitEntry>(
                mGate, () => mLocation,
                (key, loc) => BuildFitEntryAsync(key.Item1, key.Item2, loc));
            mDayAxis = new CacheAxis<(Target, NightDate), TargetTrajectoryEntry>(
                mGate, () => mLocation,
                (key, loc) => BuildTrajectoryAsync(key.Item1, key.Item2, loc));
            mMoonAxis = new CacheAxis<NightDate, MoonEphemerisEntry>(
                mGate, () => mLocation,
                (key, loc) => BuildMoonEphemerisAsync(key, loc));
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
            return mYearDaysAxis.GetOrNull(t);
        }

        public Task PrepareManyAsync(IEnumerable<Target> targets,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return Task.CompletedTask;
            return mYearDaysAxis.PrepareAsync(
                targets.Where(t => t != null), targetCompleteProgress);
        }

        public TargetFitEntry GetFitOrNull(Target t, HdmKey key)
        {
            if (t == null) return null;
            return mFitsAxis.GetOrNull((t, key));
        }

        public Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return Task.CompletedTask;
            return mFitsAxis.PrepareAsync(
                targets.Where(t => t != null).Select(t => (t, key)),
                targetCompleteProgress);
        }

        public TargetTrajectoryEntry GetTrajectoryOrNull(Target t, NightDate key)
        {
            if (t == null) return null;
            return mDayAxis.GetOrNull((t, key));
        }

        public Task PrepareTrajectoryAsync(IEnumerable<Target> targets, NightDate key,
            IProgress<int> targetCompleteProgress = null)
        {
            if (targets == null) return Task.CompletedTask;
            return mDayAxis.PrepareAsync(
                targets.Where(t => t != null).Select(t => (t, key)),
                targetCompleteProgress);
        }

        public MoonEphemerisEntry GetMoonOrNull(NightDate key)
        {
            return mMoonAxis.GetOrNull(key);
        }

        public Task PrepareMoonAsync(NightDate key) => mMoonAxis.GetOrBuildAsync(key);

        // -------------- single-entry pipeline --------------

        public async Task<ChartEvaluation> EnsureAsync(ChartContext ctx, NightDate nightDate,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            // Capture the last-applied snapshot + date anchor under one lock so
            // the diff sees a consistent picture; the compute + Prepare paths
            // below run unlocked (each Prepare path is itself locked + idempotent).
            ChartContext prev;
            DateTime prevUtc;
            lock (mGate) { prev = mLastEnsureCtx; prevUtc = mLastSetUtc; }

            CacheDiff diff = ComputeDiff(prev, ctx, prevUtc);

            bool nightValid = nightDate != default;

            if (Log.IsDiagEnabled("Cache"))
            {
                Log.Diag("Cache",
                    $"EnsureAsync enter prevNull={prev == null} locChanged={diff.LocationChanged} " +
                    $"dateChanged={diff.DateChanged} tgtChanged={diff.TargetsChanged} hdmChanged={diff.HdmChanged} " +
                    $"dayModeChanged={diff.DayModeChanged} brightnessChanged={diff.BrightnessChanged} " +
                    $"targets={ctx.Targets?.Count ?? 0} nightDate={nightDate}");
            }

            // Pessimistic work estimate from the diff. Per-axis cost is targets
            // * (axis stale), summed across the three per-target axes plus 1
            // for the moon axis when the night is valid. SetLocationAsync drops
            // every cache entry, so locOrDate forces a full rebuild on all axes;
            // an HdmKey-only change re-keys fits but preserves year + trajectory.
            // Render adds one tick per target so the bar advances smoothly into
            // the sub-chart pass without a Maximum resize.
            int n = ctx.Targets?.Count ?? 0;
            bool locOrDate = diff.LocationChanged || diff.DateChanged;
            int yearWork  = (locOrDate && n > 0) ? n : 0;
            int fitWork   = ((locOrDate || diff.HdmChanged) && n > 0) ? n : 0;
            int dayWork   = (locOrDate && n > 0 && nightValid) ? n : 0;
            int moonWork  = (locOrDate && nightValid) ? 1 : 0;
            int ensureWork = yearWork + fitWork + dayWork + moonWork;
            int renderWork = (n > 0) ? n : 0;
            int totalWork = ensureWork + renderWork;

            // Initial size + visibility flip via the consumer's first-Report
            // semantic. Skipping the Report when ensureWork == 0 keeps the bar
            // hidden for warm scrubs (per-key Prepare fast paths still run for
            // their idempotence but nothing actually ticks).
            if (ensureWork > 0) progress?.Report((0, totalWork));

            int done = 0;
            IProgress<int> SubProgress() =>
                progress == null ? null : new ActionProgress<int>(_ =>
                {
                    int d = Interlocked.Increment(ref done);
                    progress.Report((d, totalWork));
                });

            // 1. Re-key cache on geometry or date change. Skip SetLocationAsync
            //    entirely when both LocationCacheEquivalent and dateChanged report
            //    unchanged -- SetLocationAsync's own ReferenceEquals fast path
            //    doesn't help when the form re-creates Location instances on
            //    every UI tick even for value-unchanged saves.
            if (locOrDate)
            {
                await SetLocationAsync(ctx.Location, ctx.Observation.Utc);
            }

            // 2a. Moon ephemeris is TARGET-INDEPENDENT (function of Location +
            //     night only). Prep unconditionally when the night is valid so
            //     Day's startup Render (with empty targets, before NINA load
            //     completes) hits a warm moon cache instead of the defensive
            //     inline fallback WARN.
            if (nightValid)
            {
                await PrepareMoonAsync(nightDate);
                if (moonWork > 0)
                {
                    int d = Interlocked.Increment(ref done);
                    progress?.Report((d, totalWork));
                }
            }

            // 2b. Per-target prep: yearDays + fits + per-night trajectories.
            //     Each Prepare path is internally idempotent per cache key, so
            //     repeated EnsureAsync calls with the same ctx settle in the
            //     per-key fast paths.
            if (ctx.Targets != null && ctx.Targets.Count > 0)
            {
                await PrepareManyAsync(ctx.Targets, yearWork > 0 ? SubProgress() : null);
                await PrepareFitsAsync(ctx.Targets, ctx.Hdm, fitWork > 0 ? SubProgress() : null);

                // nightDate == default sentinels "no valid night" (polar /
                // sub-horizon-anchor). Day chart's Render handles the
                // blank-chart case from cache.GetTrajectoryOrNull returning
                // null; skip the prep.
                if (nightValid)
                {
                    await PrepareTrajectoryAsync(ctx.Targets, nightDate, dayWork > 0 ? SubProgress() : null);
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
                Log.Diag("Cache",
                    $"EnsureAsync exit mFits.Count={mFitsAxis.Count} mDay.Count={mDayAxis.Count} " +
                    $"mMoon.Count={mMoonAxis.Count}");
            }

            return new ChartEvaluation
            {
                BrightnessInputsChanged = diff.BrightnessChanged,
                EnsureWork = ensureWork,
                RenderWork = renderWork,
            };
        }

        // Tiny IProgress<T> shim so EnsureAsync can hand the per-axis Prepare*
        // calls a lambda-backed sub-progress (each tick increments a shared
        // Interlocked counter and re-reports the outer (Done, Total) tuple).
        // Lives here as a private nested type — single-purpose, no other
        // consumer in the project.
        private sealed class ActionProgress<T> : IProgress<T>
        {
            private readonly Action<T> mAction;
            public ActionProgress(Action<T> action) { mAction = action; }
            public void Report(T value) => mAction(value);
        }

        // Bundles the six staleness flags ComputeDiff produces. LocationChanged
        // + DateChanged gate SetLocationAsync; BrightnessChanged becomes the
        // returned ChartEvaluation; Targets / Hdm / DayMode are diag-only today.
        private readonly record struct CacheDiff(
            bool LocationChanged, bool DateChanged, bool TargetsChanged,
            bool HdmChanged, bool DayModeChanged, bool BrightnessChanged);

        // Pure staleness diff of ctx against the last-applied snapshot (prev)
        // and date anchor (prevUtc). No instance state -- EnsureAsync captures
        // both under the lock and passes them in.
        private static CacheDiff ComputeDiff(ChartContext prev, ChartContext ctx, DateTime prevUtc)
        {
            bool locationChanged = prev == null
                || !LocationCacheEquivalent(prev.Location, ctx.Location);
            bool targetsChanged = prev == null
                || !TargetsEqualByReference(prev.Targets, ctx.Targets);
            bool hdmChanged = prev == null || prev.Hdm != ctx.Hdm;
            bool dayModeChanged = prev == null || prev.DayMode != ctx.DayMode;
            bool brightnessChanged = prev == null
                || ctx.Location.BortleClass != prev.Location.BortleClass
                || ctx.Location.ExtinctionK != prev.Location.ExtinctionK
                || !Equals(ctx.Policy.ActiveFilter, prev.Policy.ActiveFilter);

            // Date-change-without-geometry-change. NightCache's Starting window
            // and YearStartDay both depend on the seed UTC; any cross-day scrub
            // stales the Starting slot, any cross-month scrub stales the
            // YearStartDay anchor (and thus the whole year-cache). Treat both as
            // cache-busting events on par with a true geometry change so
            // SetLocationAsync rebuilds against the new anchor. The first
            // EnsureAsync (prev == null) at the unchanged ctor anchor keeps
            // dateChanged false -- the cache is already keyed via the ctor seed.
            bool dateChanged =
                prevUtc.Date != ctx.Observation.Utc.Date
                || NightCache.ComputeYearStartDay(prevUtc)
                   != NightCache.ComputeYearStartDay(ctx.Observation.Utc);

            return new CacheDiff(locationChanged, dateChanged, targetsChanged,
                hdmChanged, dayModeChanged, brightnessChanged);
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
            List<Task<TargetCacheEntry>> oldInFlight;
            List<Task<TargetFitEntry>> oldInFlightFits;
            List<Task<TargetTrajectoryEntry>> oldInFlightDay;
            List<Task<MoonEphemerisEntry>> oldInFlightMoon;

            lock (mGate)
            {
                // No-op if location is unchanged (reference equality) AND the
                // startingUtc anchor is the same; legitimate for repeated
                // settings-driven calls. A pure date scrub at the same site
                // proceeds with the rebuild because the anchor moved.
                if (object.ReferenceEquals(mLocation, newLocation)
                    && mLastSetUtc == startingUtc) return;

                oldNightTask = mNightCacheTask;

                // Swap mLocation FIRST so any post-swap publish from an old
                // in-flight build fails its ReferenceEquals check and discards.
                // Old in-flight builds keep running and drop themselves; each
                // axis's DrainAndReset runs under this same lock so all four
                // axes + mLocation + the night cache reset atomically.
                mLocation = newLocation;
                mLastSetUtc = startingUtc;
                mNightCache = null;
                mNightCacheTask = null;
                oldInFlight     = mYearDaysAxis.DrainAndReset();
                oldInFlightFits = mFitsAxis.DrainAndReset();
                oldInFlightDay  = mDayAxis.DrainAndReset();
                oldInFlightMoon = mMoonAxis.DrainAndReset();
            }

            // Wait for in-flight tasks (against the old location) to finish so callers
            // who await SetLocationAsync don't continue while stale work is still
            // touching the threadpool. Stale-publish is harmless (each axis's
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
            foreach (Task<TargetTrajectoryEntry> t in oldInFlightDay)
                staleAwaits.Add(SafeAwait(t,
                    "Stale per-(target, DayWindowKey) altitude build threw during SetLocationAsync"));
            foreach (Task<MoonEphemerisEntry> t in oldInFlightMoon)
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

        // The four Build*EntryAsync methods are the per-axis compute bodies,
        // wired as the CacheAxis build delegates in the ctor. They are pure
        // compute: CacheAxis owns the in-flight dedupe, the publish, and the
        // stale-build discard (a build started against a since-swapped location
        // is dropped by the axis's ReferenceEquals check).

        private async Task<TargetCacheEntry> BuildEntryAsync(Target target, Location location)
        {
            NightCache night = await EnsureNightCacheAsync(location);

            IReadOnlyList<NightCacheEntry> yearDays = await Task.Run(
                () => ComputeYearDays(target, location, night));

            return new TargetCacheEntry(target, yearDays);
        }

        // Per-(target, HdmKey) fit build. Awaits yearDays for the target (via the
        // yearDays axis, de-duped there), then walks each night computing the
        // Sessions Ceiling / Floor / CenteredFloor triple plus a single-night
        // Tonight fit from NightCache.Starting (Day's HD-overlay box and Sky's
        // hide-on-no-fit read the same cache). The horizon profile is
        // reconstructed from the key: HdmKey.LocalHorizon carries the polyline /
        // MaxOfHorizonProfile composite verbatim; for the scalar case it is null
        // and HdmKey.HorizonDeg is the exact target floor, so a fresh
        // ScalarHorizonProfile(HorizonDeg) is functionally identical to the live
        // one. (HorizonDeg, LocalHorizon) thus fully determines the profile, so
        // fits dedupe is exact -- no caller-supplied-horizon contract needed.
        private async Task<TargetFitEntry> BuildFitEntryAsync(
            Target target, HdmKey key, Location location)
        {
            TargetCacheEntry yearEntry = await mYearDaysAxis.GetOrBuildAsync(target);
            IReadOnlyList<NightCacheEntry> yearDays = yearEntry.YearDays;
            IHorizonProfile horizon = key.LocalHorizon
                ?? new ScalarHorizonProfile(key.HorizonDeg);
            TimeSpan duration = TimeSpan.FromTicks(key.DurationTicks);
            MoonAvoidanceProfile profile = key.MoonAvoidanceEnabled && key.ActiveFilter != null
                ? key.ActiveFilter.ToProfile()
                : null;
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
                    $"BuildFit target={target.Name} hdmKey=(H={key.HorizonDeg},Dt={key.DurationTicks},F={key.ActiveFilter?.Name ?? "(none)"},MoonOn={key.MoonAvoidanceEnabled}) " +
                    $"durationSec={duration.TotalSeconds:F0} startingValid={starting.IsValid} " +
                    $"tonightHasFloor={tonight.Floor.HasValue}");
            }

            return entry;
        }

        // Per-(target, NightDate) trajectory build. AltitudeCurve.Sample on a
        // threadpool thread; the result is the minute-spaced (Alt, Az) the Day
        // chart paints + the BLUE check / future polyline-gating paths consume.
        // Independent of HdmKey (geometry is policy-free), so the cache hits
        // across HDM scrubs. Samples span the chart's render window for the
        // night (DayWindowKey.Count minutes from DayWindowKey.ChartStartUtc),
        // not just dusk-to-dawn -- consumers reading the night-only range slice
        // the corresponding indices.
        private async Task<TargetTrajectoryEntry> BuildTrajectoryAsync(
            Target target, NightDate key, Location location)
        {
            DayWindowKey window = BuildDayWindowForDate(location, key);
            if (window.Count <= 0)
                return new TargetTrajectoryEntry(target, key, window, Array.Empty<AltAzSample>());

            IReadOnlyList<AltAzSample> samples = await Task.Run(
                () => AltitudeCurve.Sample(
                    target, location, window.ChartStartUtc, TimeSpan.FromMinutes(1), window.Count));

            return new TargetTrajectoryEntry(target, key, window, samples);
        }

        // Per-NightDate moon ephemeris build. Singleton per night (the moon is
        // shared across all targets at a location). MoonEphemeris.Sample on a
        // threadpool thread; the result is the minute-spaced topocentric moon
        // state (AltAz + distance + age + phase + illumination) the Day chart's
        // moon plot, Sky chart's K-S walk, and moon-clear gate all read.
        private async Task<MoonEphemerisEntry> BuildMoonEphemerisAsync(
            NightDate key, Location location)
        {
            DayWindowKey window = BuildDayWindowForDate(location, key);
            if (window.Count <= 0)
                return new MoonEphemerisEntry(key, window, Array.Empty<MoonSample>());

            IReadOnlyList<MoonSample> samples = await Task.Run(
                () => MoonEphemeris.Sample(
                    location, window.ChartStartUtc, TimeSpan.FromMinutes(1), window.Count));

            return new MoonEphemerisEntry(key, window, samples);
        }

        // Resolve a NightDate to the DayWindowKey covering its chart-visible
        // window. Builder helper: receives just (Location, NightDate) from the
        // cache axis pipeline and needs to derive the time range to sample.
        // Returns an empty/zero-Count window for invalid (polar) nights so
        // builders short-circuit cleanly.
        private static DayWindowKey BuildDayWindowForDate(Location location, NightDate key)
        {
            if (key == default) return default;
            TimeZoneInfo zone = location.TimeZoneInfo;
            DateTime localAnchor = key.DuskDate.ToDateTime(new TimeOnly(12, 0));
            DateTime utcAnchor = TimeZoneInfo.ConvertTimeToUtc(localAnchor, zone);
            NightWindow night = NightCalculator.ComputeNight(location, utcAnchor);
            if (!night.IsValid) return default;
            return Charts.ChartLayout.BuildDayWindow(night, zone).Key;
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
                    entry.MoonSamples = new List<MoonSweepSample>(0);
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
                // at 1-minute cadence between Dusk and Dawn so the cache stays profile-
                // independent: the Lorentzian decision is evaluated at render time against
                // these raw samples, not pre-decided per night. ~600 samples per night per
                // target on a typical night. Each is one MoonSeparation.ObserveAt call --
                // now lock-free (Meeus-backed AstroUtil) so the per-target sweeps run in
                // parallel across threadpool cores.
                List<MoonSweepSample> samples = new List<MoonSweepSample>(720);
                DateTime sampleUtc = entry.Dusk;
                while (sampleUtc <= entry.Dawn)
                {
                    var observed = MoonSeparation.ObserveAt(target, location, sampleUtc);
                    samples.Add(new MoonSweepSample(
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

                fits[i] = ComputeOneFit(target, location, nw, horizonProfile, duration, profile);
            }

            return fits;
        }

        // Single-night equivalent of ComputeNightFits' loop body, for the
        // LocationNightCache.Starting slot consumed by Day's HD-overlay and
        // Sky's hide-on-no-fit.
        private static NightFit ComputeTonightFit(
            Target target, Location location, NightWindow starting,
            IHorizonProfile horizonProfile, TimeSpan duration, MoonAvoidanceProfile profile)
        {
            if (duration <= TimeSpan.Zero || !starting.IsValid) return default;
            return ComputeOneFit(target, location, starting, horizonProfile, duration, profile);
        }

        // The per-night fit recipe shared by ComputeNightFits (the per-night
        // loop) and ComputeTonightFit (the single Starting window). One
        // BestSession.ResolveCandidates resolve drives both PlaceBest
        // (Ceiling + Floor) and PlaceCentered (CenteredFloor). No guards --
        // each caller applies its own pre-rejection (geometric for the loop,
        // duration / IsValid for tonight) before calling in.
        private static NightFit ComputeOneFit(
            Target target, Location location, NightWindow nw,
            IHorizonProfile horizonProfile, TimeSpan duration, MoonAvoidanceProfile profile)
        {
            var candidates = BestSession.ResolveCandidates(
                target, location, nw, horizonProfile, profile);
            if (candidates.Count == 0) return default;

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

            // Upper transit at or after dusk. Analytic (constant cost), independent
            // of H/D/M -- carried here so the Day chart's HD-overlay can mark transit
            // X on the floor bar without re-deriving from RA + sidereal time at paint.
            DateTime transit = TransitTime.UtcAtOrAfter(target, location, nw.AstronomicalDusk);

            return new NightFit
            {
                Ceiling = ceiling,
                Floor = floor,
                CenteredFloor = centeredFloor,
                StartUtc = session?.Start,
                EndUtc   = session?.End,
                CenteredStartUtc = centered?.Start,
                CenteredEndUtc   = centered?.End,
                TransitUtc = transit,
            };
        }
    }
}
