using System;
using System.Collections.Generic;
using System.Drawing;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Time;
using TargetPlanner.Filters;
using TargetPlanner.State;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // ChartContext is the immutable per-pipeline snapshot. The Hdm derived property
    // is the cache-key projection -- it must pull every field from Policy, and it
    // must null out ScalarHorizonProfile instances so the fits cache doesn't thrash
    // on every Snapshot (the form re-allocates a fresh ScalarHorizonProfile via
    // PlanningPolicy.WithScalarHorizon on each scrub tick).
    public class ChartContextTests
    {
        private static Filter Filter() =>
            new Filter("H", 1.0, 656.3, 3.0);

        private static Location Location() => new Location(
            name: "Test",
            latitude: 40.0, north: true,
            longitude: 75.0, west: true,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation: 0.0,
            bortleClass: 5,
            extinctionK: 0.28);

        private static ChartContext MakeCtx(PlanningPolicy policy) => new ChartContext(
            Location: Location(),
            Targets: Array.Empty<Target>(),
            Policy: policy,
            Observation: ObservationMoment.Now(TimeZoneInfo.Utc),
            ActiveArea: "Day",
            TargetColors: new Dictionary<Target, Color>(),
            DayMode: DayChartMode.Floor);

        [Fact]
        public void Hdm_ScalarHorizon_NullsLocalHorizonField()
        {
            // SnapshotCurrent wraps the scalar floor in a fresh ScalarHorizonProfile;
            // ChartContext.Hdm must drop it so the cache key falls back to HorizonDeg.
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                targetFloorDeg: 30.0,
                minDuration: TimeSpan.FromMinutes(240),
                activeFilter: Filter(),
                moonAvoidanceEnabled: true);

            HdmKey hdm = MakeCtx(policy).Hdm;
            Assert.Null(hdm.LocalHorizon);
            Assert.Equal(30.0, hdm.HorizonDeg);
        }

        [Fact]
        public void Hdm_PolylineHorizon_PassesReferenceThrough()
        {
            IHorizonProfile polyline = new PolylineHorizonProfile(
                new[] { 0.0, 90.0, 180.0, 270.0 },
                new[] { 10.0, 15.0, 12.0, 14.0 });
            PlanningPolicy policy = new PlanningPolicy(
                TargetFloorDeg: 30.0,
                MinDuration: TimeSpan.FromMinutes(240),
                ActiveFilter: Filter(),
                MoonAvoidanceEnabled: true,
                LocalHorizon: polyline);

            HdmKey hdm = MakeCtx(policy).Hdm;
            Assert.Same(polyline, hdm.LocalHorizon);
        }

        [Fact]
        public void Hdm_PullsHorizonDegFromPolicy()
        {
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                25.0, TimeSpan.FromMinutes(240), Filter(), true);
            Assert.Equal(25.0, MakeCtx(policy).Hdm.HorizonDeg);
        }

        [Fact]
        public void Hdm_PullsDurationTicksFromPolicy()
        {
            TimeSpan dur = TimeSpan.FromMinutes(180);
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                30.0, dur, Filter(), true);
            Assert.Equal(dur.Ticks, MakeCtx(policy).Hdm.DurationTicks);
        }

        [Fact]
        public void Hdm_PullsActiveFilterFromPolicy()
        {
            Filter f = new Filter("O", 1.0, 500.7, 3.0);
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), f, true);
            Assert.Equal(f, MakeCtx(policy).Hdm.ActiveFilter);
        }

        [Fact]
        public void Hdm_PullsMoonAvoidanceEnabledFromPolicy()
        {
            PlanningPolicy on = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), Filter(), moonAvoidanceEnabled: true);
            PlanningPolicy off = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), Filter(), moonAvoidanceEnabled: false);
            Assert.True(MakeCtx(on).Hdm.MoonAvoidanceEnabled);
            Assert.False(MakeCtx(off).Hdm.MoonAvoidanceEnabled);
        }

        [Fact]
        public void Record_WithExpression_MutatesSingleFieldOnly()
        {
            ChartContext orig = MakeCtx(PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), Filter(), true));
            ChartContext mut = orig with { ActiveArea = "Sky" };
            Assert.Equal("Sky", mut.ActiveArea);
            Assert.Equal("Day", orig.ActiveArea);
            Assert.Same(orig.Location, mut.Location);
            Assert.Same(orig.Policy, mut.Policy);
        }

        [Fact]
        public void Record_StructuralEquality_OnIdenticalFields()
        {
            // ChartContext is a record -- structural equality flows through. Two
            // snapshots built from identical source state should compare equal.
            PlanningPolicy policy = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), Filter(), true);
            ObservationMoment obs = ObservationMoment.Now(TimeZoneInfo.Utc);
            Location loc = Location();
            IReadOnlyDictionary<Target, Color> colors = new Dictionary<Target, Color>();
            ChartContext a = new ChartContext(loc, Array.Empty<Target>(), policy, obs,
                "Day", colors, DayChartMode.Floor);
            ChartContext b = new ChartContext(loc, Array.Empty<Target>(), policy, obs,
                "Day", colors, DayChartMode.Floor);
            // Targets reference matters (record's default IReadOnlyList equality is by
            // reference). Using the same Array.Empty<Target>() singleton on both sides
            // wouldn't be enough since we passed two separate calls -- Array.Empty
            // returns the same singleton, so they ARE reference-equal.
            Assert.Equal(a, b);
        }
    }
}
