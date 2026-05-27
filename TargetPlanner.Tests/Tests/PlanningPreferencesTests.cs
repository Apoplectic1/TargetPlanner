using System;
using Astronomy.NINA.Persistence;
using TargetPlanner.State;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // PlanningPreferences is the per-site persisted scalar floor + min-duration
    // pair. Default mirrors the pre-Phase-2 hardcoded Location.Default values
    // (30 deg, 240 min); a null DTO must resolve to those rather than throw.
    public class PlanningPreferencesTests
    {
        [Fact]
        public void Default_HasShipSafeValues()
        {
            PlanningPreferences d = PlanningPreferences.Default;
            Assert.Equal(30.0, d.TargetFloorDeg);
            Assert.Equal(TimeSpan.FromMinutes(240), d.MinDuration);
        }

        [Fact]
        public void FromDto_NullDto_ReturnsDefault()
        {
            Assert.Equal(PlanningPreferences.Default, PlanningPreferences.FromDto(null));
        }

        [Fact]
        public void FromDto_NonNullDto_ProjectsFields()
        {
            PlanningPreferencesDto dto = new PlanningPreferencesDto
            {
                TargetFloorDeg = 35.0,
                MinDurationMinutes = 90.0,
            };
            PlanningPreferences p = PlanningPreferences.FromDto(dto);
            Assert.Equal(35.0, p.TargetFloorDeg);
            Assert.Equal(TimeSpan.FromMinutes(90), p.MinDuration);
        }

        [Fact]
        public void ToDto_FromDto_RoundTrip()
        {
            PlanningPreferences orig = new PlanningPreferences(28.5, TimeSpan.FromMinutes(150));
            PlanningPreferencesDto dto = orig.ToDto();
            PlanningPreferences roundTripped = PlanningPreferences.FromDto(dto);
            Assert.Equal(orig, roundTripped);
        }

        [Fact]
        public void Record_WithExpression_MutatesFloorOnly()
        {
            PlanningPreferences orig = PlanningPreferences.Default;
            PlanningPreferences mut = orig with { TargetFloorDeg = 20.0 };
            Assert.Equal(20.0, mut.TargetFloorDeg);
            Assert.Equal(orig.MinDuration, mut.MinDuration);
        }
    }
}
