using System;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Correctness guard for AltitudeCurve.Sample: its output must match a per-minute
    // AltAzCalculator.Of loop to well below chart pixel resolution at every sample. The two
    // paths share the underlying TargetGeometry.AltitudeAtHourAngle formula; the only place
    // they can diverge is the LST computation (per-sample SiderealTime.Local in the baseline
    // vs one-shot + linear advance in AltitudeCurve). GMST is linear in UT to far below
    // arcsecond precision over a night, so the expected agreement is ~nanodegrees. A loose
    // 1e-6 degree tolerance leaves margin for any future refactor of either side without
    // masking a real divergence.
    public class AltitudeCurveTests
    {
        [Theory]
        [InlineData(600)]   // typical Day-chart night
        [InlineData(1000)]  // long winter night
        [InlineData(6000)]  // stress
        public void Sample_MatchesPerMinuteAltAz(int count)
        {
            Target target = Target.Default;
            Location location = Location.Default;
            DateTime startUtc = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
            TimeSpan step = TimeSpan.FromMinutes(1);

            var batched = AltitudeCurve.Sample(target, location, startUtc, step, count);

            for (int i = 0; i < count; i++)
            {
                DateTime point = startUtc.Add(TimeSpan.FromTicks(step.Ticks * i));
                double expected = AltAzCalculator
                    .Of(target, location.With(dateTime: point)).Altitude;
                double actual = batched[i];
                Assert.True(
                    Math.Abs(expected - actual) < 1e-6,
                    $"sample {i}: expected {expected}, got {actual}, delta {expected - actual}");
            }
        }

        [Fact]
        public void Sample_CountZero_ReturnsEmpty()
        {
            var result = AltitudeCurve.Sample(
                Target.Default, Location.Default,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), count: 0);

            Assert.Empty(result);
        }

        [Fact]
        public void Sample_NegativeCount_Throws()
        {
            Assert.Throws<ArgumentException>(() => AltitudeCurve.Sample(
                Target.Default, Location.Default,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), count: -1));
        }

        [Fact]
        public void Sample_NonPositiveStep_Throws()
        {
            Assert.Throws<ArgumentException>(() => AltitudeCurve.Sample(
                Target.Default, Location.Default,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.Zero, count: 10));
        }
    }
}
