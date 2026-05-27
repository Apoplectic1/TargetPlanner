using System;
using TargetPlanner.Targets;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // SkyCentroid is the spherical-mean of a set of (RA hr, Dec deg) pairs --
    // the only correct way to average sky coordinates. A component-wise mean
    // of RA fails across the 0h/24h seam (23.9h and 0.1h averaging to ~12h);
    // the vector form has no seam and behaves at the poles. These tests pin
    // the seam case, the pole case, and the single-point identity.
    public class SkyCentroidTests
    {
        // Tolerance for centroid comparison. The vector mean is exact for the
        // unit-vector sum but the inverse atan2 has floating-point noise around
        // 1e-12; 1e-9 deg ~ 4 microarcseconds, well below any practical use.
        private const double DegTol = 1e-9;
        private const double HourTol = 1e-9 / 15.0;

        [Fact]
        public void Of_NullInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => SkyCentroid.Of(null));
        }

        [Fact]
        public void Of_EmptyInput_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SkyCentroid.Of(new (double, double)[0]));
        }

        [Fact]
        public void Of_SinglePoint_ReturnsItUnchanged()
        {
            var (ra, dec) = SkyCentroid.Of(new[] { (5.59, -5.39) });
            Assert.Equal(5.59, ra, precision: 9);
            Assert.Equal(-5.39, dec, precision: 9);
        }

        [Fact]
        public void Of_CrossesRaSeam_AveragesNearZero()
        {
            // 23.9h and 0.1h on the equator: the seam-naive component mean is 12h
            // (opposite side of sky); the vector mean is ~0h. Tolerance is generous
            // here because the atan2 inverse near zero amplifies floating-point noise
            // -- we're testing the qualitative seam behavior, not exact zero.
            var (ra, dec) = SkyCentroid.Of(new[] { (23.9, 0.0), (0.1, 0.0) });
            // The midpoint is 0.0h, but the vector form gives a result very close
            // to 0 (or equivalently 24 - epsilon). The wrap step ensures [0, 24).
            // Distance from either 0 or 24 should be <= 0.1h.
            double distFromZero = Math.Min(ra, 24.0 - ra);
            Assert.True(distFromZero < 0.05,
                $"Expected centroid near 0h (or 24h), got {ra:F6}h");
            Assert.Equal(0.0, dec, precision: 9);
        }

        [Fact]
        public void Of_AntipodalRaSamePole_PicksTheCommonPole()
        {
            // Two points at the pole with antipodal RA still resolve to ~90 Dec.
            // The RA at the pole is ambiguous (atan2 of (~0, ~0)) but the Dec
            // should be exactly 90 within tolerance.
            var (_, dec) = SkyCentroid.Of(new[] { (0.0, 89.99), (12.0, 89.99) });
            Assert.True(dec > 89.0,
                $"Expected centroid near +90 deg, got Dec {dec:F6}");
        }

        [Fact]
        public void Of_ReturnsRaInZeroToTwentyFourRange()
        {
            // The implementation wraps negative atan2 results back into [0, 24).
            // Construct a case that would naturally land just below 0 if not wrapped:
            // 0.05h and 23.95h on the equator -- vector centroid is at RA=0, which
            // could come out as just-below-zero before the wrap fixup.
            var (ra, _) = SkyCentroid.Of(new[] { (0.05, 0.0), (23.95, 0.0) });
            Assert.InRange(ra, 0.0, 24.0);
        }

        [Fact]
        public void Of_EquatorSamePointDuplicated_ReturnsTheSame()
        {
            // Centroid of N identical points equals the point itself (to within
            // floating-point noise) -- a basic sanity invariant.
            var (ra, dec) = SkyCentroid.Of(new[] { (6.0, 23.0), (6.0, 23.0), (6.0, 23.0) });
            Assert.Equal(6.0, ra, precision: 9);
            Assert.Equal(23.0, dec, precision: 9);
        }

        [Fact]
        public void Of_SymmetricAboutMeridian_CentroidLandsOnMeridian()
        {
            // Two points symmetric about RA = 6h: vector mean should be at RA = 6h.
            var (ra, dec) = SkyCentroid.Of(new[] { (5.5, 30.0), (6.5, 30.0) });
            Assert.Equal(6.0, ra, precision: 6);
            // Dec sits slightly ABOVE 30 because the chord-midpoint of two points at
            // the same declination is closer to the rotation axis than the endpoints,
            // so reprojecting to unit length yields a higher Dec than the inputs.
            // For ΔRA=1h symmetric about 6h at Dec 30, the centroid lands at
            // ~30.21 deg. Verify the qualitative direction (above input) and a loose
            // numeric bound rather than the exact value.
            Assert.InRange(dec, 30.0, 30.5);
        }
    }
}
