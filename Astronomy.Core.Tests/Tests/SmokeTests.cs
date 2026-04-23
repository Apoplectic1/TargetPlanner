using System;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Scaffolding smoke tests -- prove the xUnit runner, the ProjectReference to
    // Astronomy.Core, and the Core public surface are all wired. Not the Step-1 correctness
    // audit against Stellarium / NINA TS (ROADMAP.md Step 1); that's a larger follow-up. The
    // "AltitudeAtTransitMatchesMeridianAltitude" fact cross-checks three primitives
    // (TransitTime.UtcAtOrAfter, AltAzCalculator.At, TargetGeometry.MeridianAltitude)
    // against each other without needing an external reference value.
    public class SmokeTests
    {
        [Fact]
        public void TargetDefault_IsM31()
        {
            Target m31 = Target.Default;

            Assert.Equal("M31", m31.Name);
            Assert.True(m31.North);
            Assert.InRange(m31.RightAscension, 0.0, 24.0);
            Assert.True(m31.Declination > 0.0);
        }

        [Fact]
        public void LocationDefault_IsPennsPark()
        {
            Location loc = Location.Default;

            Assert.Equal("Penns Park", loc.Name);
            Assert.True(loc.North);
            Assert.True(loc.West);
            Assert.InRange(loc.Latitude, 0.0, 90.0);
            Assert.InRange(loc.Longitude, 0.0, 180.0);
        }

        [Fact]
        public void AltitudeAtTransit_MatchesMeridianAltitude()
        {
            // Pick a stable UTC instant (no DST dance, unambiguous).
            DateTime searchFromUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            Target target = Target.Default;
            Location location = Location.Default;

            DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, searchFromUtc);
            AltAz altaz = AltAzCalculator.At(target, location, transitUtc);

            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double decSigned = target.North   ?  target.Declination : -target.Declination;
            double expectedMeridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);

            // At transit (hour angle = 0), altitude equals the meridian altitude exactly per
            // the closed-form identity. Tolerance is generous to absorb floating-point noise
            // from the LST=RA inversion in TransitTime.
            Assert.Equal(expectedMeridianAlt, altaz.Altitude, precision: 6);
        }
    }
}
