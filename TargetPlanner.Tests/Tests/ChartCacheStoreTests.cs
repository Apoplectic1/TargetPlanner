using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Astronomy.Core.Time;
using TargetPlanner.Caches;
using TargetPlanner.Filters;
using TargetPlanner.State;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Location = Astronomy.Core.Locations.Location;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // ChartCacheStore tests via the public IChartCacheStore surface. Direct map
    // from docs/design/cache-contract.md -- each lifecycle invariant + diff-matrix
    // row gets a test by the same name.
    //
    // Uses TestLocations.PennsPark and a shared M31 Target instance as canonical
    // fixtures to keep per-test compute bounded. The yearDays sweep for a single
    // target is ~50-200ms; full Phase 3 settles in well under 30 sec for ~20 tests.
    //
    // M31 is held as a class-level static so every test in the file uses the
    // same Target reference. The cache uses reference identity on Target
    // (per-(target, key) dict keys) and Location (TryPublish's ReferenceEquals
    // discard), so threading one shared instance through Prepare and GetOrNull
    // is the cleanest pattern -- and a class-level field makes the canonical-
    // fixture intent explicit even though Target.Default is now itself a
    // static readonly singleton on the Library side.
    public class ChartCacheStoreTests
    {
        private static readonly DateTime SeedUtc =
            new DateTime(2026, 5, 27, 22, 0, 0, DateTimeKind.Utc);

        // Shared canonical target -- captured once at class init so every Get/Prepare
        // pair sees the same instance.
        private static readonly Target M31 = Target.Default;

        // Southern-hemisphere companion -- only used in monotonic-growth tests.
        private static readonly Target M42 = new Target(
            name: "M42", rightAscension: 5.591, declination: 5.39, north: false,
            directory: string.Empty, enabled: true);

        private static Filter MakeFilter(string name = "H") =>
            new Filter(name, 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);

        private static PlanningPolicy MakePolicy(double floorDeg = 30.0, Filter filter = null,
            bool moonOn = true) =>
            PlanningPolicy.WithScalarHorizon(
                targetFloorDeg: floorDeg,
                minDuration: TimeSpan.FromMinutes(240),
                activeFilter: filter ?? MakeFilter(),
                moonAvoidanceEnabled: moonOn);

        private static ChartContext MakeCtx(
            Location location,
            IReadOnlyList<Target> targets,
            PlanningPolicy policy,
            DateTime? observationUtc = null) =>
            new ChartContext(
                Location: location,
                Targets: targets,
                Policy: policy,
                Observation: new ObservationMoment(
                    observationUtc ?? SeedUtc, TimeZoneInfo.Utc),
                ActiveArea: "Day",
                TargetColors: new Dictionary<Target, Color>(),
                DayMode: DayChartMode.Floor);

        // Day window for SeedUtc as a synthetic key -- the cache treats this as an
        // opaque identifier, so it doesn't need to correspond to a real dusk-dawn
        // pair (we're not exercising the per-minute altitude semantics, only that
        // the day axis dedupes / drops by key).
        private static DayWindowKey MakeDayKey(int offsetMinutes = 0) =>
            new DayWindowKey
            {
                ChartStartUtcTicks = SeedUtc.AddMinutes(offsetMinutes).Ticks,
                Count = 720,
            };

        // -------- Construction --------

        [Fact]
        public void Ctor_NullInitialLocation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ChartCacheStore(null, SeedUtc));
        }

        [Fact]
        public void Ctor_StoresInitialLocation_NightCacheStartsNull()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            Assert.Same(TestLocations.PennsPark, store.CurrentLocation);
            Assert.Null(store.LocationNightCache);
        }

        // -------- Per-axis Prepare/Get round-trips --------

        [Fact]
        public async Task PrepareManyAsync_PublishesYearDays_AndBuildsNightCache()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);

            await store.PrepareManyAsync(new[] { M31 });

            TargetCacheEntry entry = store.GetOrNull(M31);
            Assert.NotNull(entry);
            Assert.NotNull(entry.YearDays);
            Assert.NotEmpty(entry.YearDays);

            // BuildEntryAsync awaits EnsureNightCacheAsync, so by the time
            // PrepareManyAsync returns the night cache is published.
            Assert.NotNull(store.LocationNightCache);
        }

        [Fact]
        public async Task PrepareFitsAsync_PublishesFitsForKey()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            HdmKey key = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy()).Hdm;

            await store.PrepareFitsAsync(new[] { M31 }, key);

            TargetFitEntry fits = store.GetFitOrNull(M31, key);
            Assert.NotNull(fits);
            Assert.NotNull(fits.Nights);
        }

        [Fact]
        public async Task PrepareDayAsync_PublishesDayAltitudes()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            DayWindowKey dayKey = MakeDayKey();

            await store.PrepareDayAsync(new[] { M31 }, dayKey);

            TargetDayAltitudeEntry day = store.GetDayOrNull(M31, dayKey);
            Assert.NotNull(day);
            Assert.Equal(720, day.AltitudesPerMinute.Count);
        }

        [Fact]
        public async Task PrepareMoonAsync_PublishesSingletonForKey()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            DayWindowKey dayKey = MakeDayKey();

            await store.PrepareMoonAsync(dayKey);

            MoonAltitudeEntry moon = store.GetMoonOrNull(dayKey);
            Assert.NotNull(moon);
            Assert.Equal(720, moon.AltitudesPerMinute.Count);
        }

        [Fact]
        public async Task PrepareMoonAsync_DifferentKey_DifferentEntry()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            DayWindowKey k1 = MakeDayKey(offsetMinutes: 0);
            DayWindowKey k2 = MakeDayKey(offsetMinutes: 60);

            await store.PrepareMoonAsync(k1);
            await store.PrepareMoonAsync(k2);

            Assert.NotNull(store.GetMoonOrNull(k1));
            Assert.NotNull(store.GetMoonOrNull(k2));
            Assert.NotSame(store.GetMoonOrNull(k1), store.GetMoonOrNull(k2));
        }

        [Fact]
        public void GetOrNull_NullTarget_ReturnsNull()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            Assert.Null(store.GetOrNull(null));
            Assert.Null(store.GetFitOrNull(null, default));
            Assert.Null(store.GetDayOrNull(null, default));
        }

        // -------- Lifecycle invariants (cache-contract.md §Lifecycle invariants) --------

        [Fact]
        public async Task SetLocationAsync_DropsEveryAxis()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            HdmKey hdm = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy()).Hdm;
            DayWindowKey dayKey = MakeDayKey();

            // Warm every axis.
            await store.PrepareManyAsync(new[] { M31 });
            await store.PrepareFitsAsync(new[] { M31 }, hdm);
            await store.PrepareDayAsync(new[] { M31 }, dayKey);
            await store.PrepareMoonAsync(dayKey);

            await store.SetLocationAsync(TestLocations.Sydney, SeedUtc);

            // Single-location invariant: every axis empties atomically on swap.
            Assert.Same(TestLocations.Sydney, store.CurrentLocation);
            Assert.Null(store.GetOrNull(M31));
            Assert.Null(store.GetFitOrNull(M31, hdm));
            Assert.Null(store.GetDayOrNull(M31, dayKey));
            Assert.Null(store.GetMoonOrNull(dayKey));
            Assert.Null(store.LocationNightCache);
        }

        [Fact]
        public async Task SetLocationAsync_RefEqualAndSameUtc_IsNoOp()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            await store.PrepareManyAsync(new[] { M31 });
            Assert.NotNull(store.GetOrNull(M31));

            await store.SetLocationAsync(TestLocations.PennsPark, SeedUtc);

            // Reference-equal location + same Utc anchor short-circuits in
            // SetLocationAsync (the form sometimes re-resolves the same NamedSite
            // -> same Location instance; the cache must not trash itself).
            Assert.NotNull(store.GetOrNull(M31));
        }

        [Fact]
        public async Task PrepareManyAsync_Idempotent_SecondCallReturnsSameEntry()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);

            await store.PrepareManyAsync(new[] { M31 });
            TargetCacheEntry first = store.GetOrNull(M31);
            await store.PrepareManyAsync(new[] { M31 });
            TargetCacheEntry second = store.GetOrNull(M31);

            // Idempotence at the cache-contract level: a second call with the same
            // key fast-paths to the published entry.
            Assert.Same(first, second);
        }

        [Fact]
        public async Task PrepareManyAsync_MonotonicGrowth_AcrossMultipleTargets()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);

            await store.PrepareManyAsync(new[] { M31 });
            await store.PrepareManyAsync(new[] { M42 });

            Assert.NotNull(store.GetOrNull(M31));
            Assert.NotNull(store.GetOrNull(M42));
        }

        // -------- EnsureAsync diff matrix (cache-contract.md §EnsureAsync semantics) --------

        [Fact]
        public async Task EnsureAsync_NullCtx_Throws()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                store.EnsureAsync(null, default));
        }

        [Fact]
        public async Task EnsureAsync_FirstColdCall_ReportsNonZeroWork()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());

            ChartEvaluation eval = await store.EnsureAsync(ctx, MakeDayKey());

            Assert.True(eval.EnsureWork > 0,
                $"Expected EnsureWork > 0 on first call, got {eval.EnsureWork}");
            Assert.Equal(1, eval.RenderWork);   // one target
        }

        [Fact]
        public async Task EnsureAsync_WarmCall_ReportsZeroEnsureWork()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());
            DayWindowKey dayKey = MakeDayKey();

            await store.EnsureAsync(ctx, dayKey);
            ChartEvaluation second = await store.EnsureAsync(ctx, dayKey);

            // Warm-cache fast path: every axis's diff says no work needed.
            Assert.Equal(0, second.EnsureWork);
            // Render work still scales with target count (RenderWork is for the
            // sub-chart paint pass, not cache prep).
            Assert.Equal(1, second.RenderWork);
        }

        [Fact]
        public async Task EnsureAsync_LocationChange_DropsAllAxes()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx1 = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());
            await store.EnsureAsync(ctx1, MakeDayKey());

            // New Location instance (different geometry) -> all axes drop.
            ChartContext ctx2 = ctx1 with { Location = TestLocations.Sydney };
            await store.EnsureAsync(ctx2, MakeDayKey());

            // Verify by ref-equal CurrentLocation -- the store atomically swapped.
            Assert.Same(TestLocations.Sydney, store.CurrentLocation);
        }

        [Fact]
        public async Task EnsureAsync_HdmKeyChange_PreservesYearDays_RebuildsFits()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);

            ChartContext ctx1 = MakeCtx(
                TestLocations.PennsPark, new[] { M31 }, MakePolicy(floorDeg: 30.0));
            DayWindowKey dayKey = MakeDayKey();
            await store.EnsureAsync(ctx1, dayKey);

            TargetCacheEntry yearBefore = store.GetOrNull(M31);
            TargetDayAltitudeEntry dayBefore = store.GetDayOrNull(M31, dayKey);
            HdmKey hdm1 = ctx1.Hdm;

            // Change only the floor (the H of H/D/M) -> different HdmKey, but
            // Location + Date + DayWindowKey identical.
            ChartContext ctx2 = ctx1 with { Policy = MakePolicy(floorDeg: 35.0) };
            await store.EnsureAsync(ctx2, dayKey);
            HdmKey hdm2 = ctx2.Hdm;
            Assert.NotEqual(hdm1, hdm2);

            // yearDays + day axis preserved (year compute is per-(target, location),
            // independent of HdmKey; day altitudes are also independent of HdmKey).
            Assert.Same(yearBefore, store.GetOrNull(M31));
            Assert.Same(dayBefore, store.GetDayOrNull(M31, dayKey));

            // Fits keyed under both old and new HdmKey are present (monotonic growth).
            Assert.NotNull(store.GetFitOrNull(M31, hdm1));
            Assert.NotNull(store.GetFitOrNull(M31, hdm2));
        }

        [Fact]
        public async Task EnsureAsync_DayWindowKeyChange_RebuildsDayAndMoon_PreservesYearAndFits()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());
            DayWindowKey k1 = MakeDayKey(offsetMinutes: 0);
            DayWindowKey k2 = MakeDayKey(offsetMinutes: 60);

            await store.EnsureAsync(ctx, k1);
            TargetCacheEntry yearBefore = store.GetOrNull(M31);
            TargetFitEntry fitsBefore = store.GetFitOrNull(M31, ctx.Hdm);
            await store.EnsureAsync(ctx, k2);

            Assert.Same(yearBefore, store.GetOrNull(M31));
            Assert.Same(fitsBefore, store.GetFitOrNull(M31, ctx.Hdm));
            Assert.NotNull(store.GetDayOrNull(M31, k1));
            Assert.NotNull(store.GetDayOrNull(M31, k2));
            Assert.NotNull(store.GetMoonOrNull(k1));
            Assert.NotNull(store.GetMoonOrNull(k2));
        }

        [Fact]
        public async Task EnsureAsync_BrightnessInputsChange_FlaggedButNoAxisFlip()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);

            // Build a sibling Location with the same geometry but different Bortle
            // so the brightness diff fires without tripping the geometry-keyed
            // SetLocationAsync drop.
            Location dimmer = new Location(
                name:         TestLocations.PennsPark.Name,
                latitude:     TestLocations.PennsPark.Latitude, north: TestLocations.PennsPark.North,
                longitude:    TestLocations.PennsPark.Longitude, west: TestLocations.PennsPark.West,
                timeZoneInfo: TestLocations.PennsPark.TimeZoneInfo,
                elevation:    TestLocations.PennsPark.Elevation,
                bortleClass:  TestLocations.PennsPark.BortleClass + 1,   // brightness diff
                extinctionK:  TestLocations.PennsPark.ExtinctionK);

            ChartContext ctx1 = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());
            DayWindowKey dayKey = MakeDayKey();
            await store.EnsureAsync(ctx1, dayKey);
            TargetCacheEntry yearBefore = store.GetOrNull(M31);
            TargetFitEntry fitsBefore = store.GetFitOrNull(M31, ctx1.Hdm);

            ChartContext ctx2 = ctx1 with { Location = dimmer };
            ChartEvaluation eval = await store.EnsureAsync(ctx2, dayKey);

            // Brightness moved -> flag set.
            Assert.True(eval.BrightnessInputsChanged);
            // Geometry didn't move (lat/lon/N/W/elev unchanged), so no axis drops.
            Assert.Same(yearBefore, store.GetOrNull(M31));
            Assert.Same(fitsBefore, store.GetFitOrNull(M31, ctx1.Hdm));
        }

        [Fact]
        public async Task EnsureAsync_EmptyTargets_NoPerTargetPrep_RunsMoonPrep()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx = MakeCtx(
                TestLocations.PennsPark, Array.Empty<Target>(), MakePolicy());
            DayWindowKey dayKey = MakeDayKey();

            ChartEvaluation eval = await store.EnsureAsync(ctx, dayKey);

            // Boot-baseline path: chart paints scaffolding (moon overlay on Day)
            // without any targets. Moon axis is target-independent, so it preps
            // even with zero targets.
            Assert.NotNull(store.GetMoonOrNull(dayKey));
            Assert.Equal(0, eval.RenderWork);
        }

        [Fact]
        public async Task EnsureAsync_PolarDaySentinel_SkipsDayAndMoonPrep()
        {
            using ChartCacheStore store = new ChartCacheStore(TestLocations.PennsPark, SeedUtc);
            ChartContext ctx = MakeCtx(TestLocations.PennsPark, new[] { M31 }, MakePolicy());

            // default(DayWindowKey).Count == 0 is the polar-night / empty-targets
            // sentinel; EnsureAsync must skip Day + Moon prep entirely.
            await store.EnsureAsync(ctx, default);

            Assert.NotNull(store.GetOrNull(M31));                  // year still built
            Assert.NotNull(store.GetFitOrNull(M31, ctx.Hdm));      // fits still built
            Assert.Null(store.GetDayOrNull(M31, default));         // day skipped
            Assert.Null(store.GetMoonOrNull(default));             // moon skipped
        }
    }
}
