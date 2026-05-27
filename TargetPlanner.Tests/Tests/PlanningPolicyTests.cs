using System;
using Astronomy.Core.Horizons;
using TargetPlanner.Filters;
using TargetPlanner.State;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // PlanningPolicy aggregates the per-session imaging inputs that gate fit
    // decisions. The MoonProfile derived property is the single source of truth
    // for the Lorentzian gate -- if either the master toggle is off or no filter
    // is active, fits run moon-blind. WithScalarHorizon is the convenience
    // factory the form uses for the no-polyline case.
    public class PlanningPolicyTests
    {
        private static Filter MakeFilter() =>
            new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);

        [Fact]
        public void WithScalarHorizon_WrapsFloorInScalarProfile()
        {
            PlanningPolicy p = PlanningPolicy.WithScalarHorizon(
                targetFloorDeg: 25.0,
                minDuration: TimeSpan.FromMinutes(180),
                activeFilter: MakeFilter(),
                moonAvoidanceEnabled: true);
            ScalarHorizonProfile s = Assert.IsType<ScalarHorizonProfile>(p.LocalHorizon);
            Assert.Equal(25.0, s.MinAltitude);
            Assert.Equal(25.0, p.TargetFloorDeg);
            Assert.Equal(TimeSpan.FromMinutes(180), p.MinDuration);
        }

        [Fact]
        public void MoonProfile_WhenMasterToggleOff_ReturnsNull()
        {
            PlanningPolicy p = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), MakeFilter(), moonAvoidanceEnabled: false);
            Assert.Null(p.MoonProfile);
        }

        [Fact]
        public void MoonProfile_WhenFilterNull_ReturnsNull()
        {
            PlanningPolicy p = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), activeFilter: null, moonAvoidanceEnabled: true);
            Assert.Null(p.MoonProfile);
        }

        [Fact]
        public void MoonProfile_WhenEnabled_ReturnsFilterToProfileResult()
        {
            Filter f = MakeFilter();
            PlanningPolicy p = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), f, moonAvoidanceEnabled: true);
            var profile = p.MoonProfile;
            Assert.NotNull(profile);
            Assert.Equal(f.SeparationDeg, profile.SeparationDeg);
            Assert.Equal(f.WidthDays, profile.WidthDays);
            Assert.True(profile.Enabled);
        }

        [Fact]
        public void Record_StructuralEquality_OnIdenticalFields()
        {
            Filter f = MakeFilter();
            PlanningPolicy a = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), f, true);
            PlanningPolicy b = new PlanningPolicy(
                TargetFloorDeg: 30.0,
                MinDuration: TimeSpan.FromMinutes(240),
                ActiveFilter: f,
                MoonAvoidanceEnabled: true,
                LocalHorizon: a.LocalHorizon);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Record_WithExpression_FlipsMoonAvoidanceWithoutTouchingFloor()
        {
            PlanningPolicy orig = PlanningPolicy.WithScalarHorizon(
                30.0, TimeSpan.FromMinutes(240), MakeFilter(), true);
            PlanningPolicy mut = orig with { MoonAvoidanceEnabled = false };
            Assert.False(mut.MoonAvoidanceEnabled);
            Assert.Equal(30.0, mut.TargetFloorDeg);
            Assert.Same(orig.LocalHorizon, mut.LocalHorizon);
            Assert.Null(mut.MoonProfile);
            Assert.NotNull(orig.MoonProfile);
        }
    }
}
