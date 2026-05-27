using System.Collections.Generic;
using TargetPlanner.Targets;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // TargetIdentity is the single source of truth for "same imaging target" used
    // by the scanner (collapsing per-frame rows into one Target) and the listbox
    // duplicate tint. NormalizeName strips the " Stars" suffix that light-vs-stars
    // capture pairs use; AreSameTarget anchors on normalized-name PLUS angular
    // separation (~1 arcmin) to absorb plate-solve drift.
    public class TargetIdentityTests
    {
        private static Target Make(string name, double ra, double decDeg, bool north = true) =>
            new Target(
                name: name,
                rightAscension: ra,
                declination: decDeg, north: north,
                directory: string.Empty,
                enabled: true);

        // -------- NormalizeName --------

        [Theory]
        [InlineData("M31",          "M31")]
        [InlineData("  M31 ",       "M31")]
        [InlineData("M31 Stars",    "M31")]
        [InlineData("M31 stars",    "M31")]      // case-insensitive
        [InlineData("M31 STARS",    "M31")]
        [InlineData("Sh2-126 Stars","Sh2-126")]
        public void NormalizeName_StripsStarsSuffixAndTrims(string input, string expected)
        {
            Assert.Equal(expected, TargetIdentity.NormalizeName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeName_NullOrWhitespace_ReturnsEmpty(string input)
        {
            Assert.Equal(string.Empty, TargetIdentity.NormalizeName(input));
        }

        // -------- AreSameTarget --------

        [Fact]
        public void AreSameTarget_NullEitherSide_ReturnsFalse()
        {
            Target t = Make("M31", 0.71, 41.27);
            Assert.False(TargetIdentity.AreSameTarget(null, t));
            Assert.False(TargetIdentity.AreSameTarget(t, null));
            Assert.False(TargetIdentity.AreSameTarget(null, null));
        }

        [Fact]
        public void AreSameTarget_DifferentNamesAtSameCoords_ReturnsFalse()
        {
            // Coordinate-close mosaic panels share a base name (M101 Pn) but their
            // panel suffix keeps them distinct. The name guard catches the case.
            Target a = Make("M101 P1", 14.05, 54.35);
            Target b = Make("M101 P2", 14.05, 54.35);
            Assert.False(TargetIdentity.AreSameTarget(a, b));
        }

        [Fact]
        public void AreSameTarget_LightAndStarsFrameOfSameTarget_ReturnsTrue()
        {
            Target light = Make("M101 P1", 14.05, 54.35);
            Target stars = Make("M101 P1 Stars", 14.05, 54.35);
            Assert.True(TargetIdentity.AreSameTarget(light, stars));
        }

        [Fact]
        public void AreSameTarget_SameNameBeyondToleranceArcmin_ReturnsFalse()
        {
            // Two M101 rows 5 arcmin apart on Dec -- far beyond the ~1 arcmin tolerance.
            Target a = Make("M101", 14.05, 54.35);
            Target b = Make("M101", 14.05, 54.35 + 5.0 / 60.0);
            Assert.False(TargetIdentity.AreSameTarget(a, b));
        }

        [Fact]
        public void AreSameTarget_AcrossRaSeam_TreatsZeroAnd24hAsAdjacent()
        {
            // RA delta should wrap. 23.99h and 0.01h on the same Dec are ~6 arcmin
            // apart on-sky (15 deg/hr * 0.02 hr * cos(0) ≈ 0.3 deg = 18 arcmin)
            // -- still beyond tolerance, so AreSameTarget returns false but for the
            // RIGHT reason. The bug we're guarding against is the seam-naive form
            // returning a near-full-circle separation that would treat it as far away.
            Target a = Make("M31", 23.99, 0.0);
            Target b = Make("M31", 0.01, 0.0);
            // Without seam handling, this would compute as ~360 deg -- way over. With
            // seam handling it computes as 0.3 deg, still over the 1 arcmin tol.
            // What we really want to check: when the points are within 1 arcmin
            // across the seam, AreSameTarget returns TRUE. Easier to construct: pick
            // RA = 23.999h vs 0.001h (delta 0.002h ~ 0.03 deg = 1.8 arcmin -- close
            // but slightly over). For a definitively-within case, use 23.9999h /
            // 0.0001h (delta 0.0002h = 0.003 deg = 11 arcsec, well within).
            Assert.False(TargetIdentity.AreSameTarget(a, b));

            Target a2 = Make("M31", 23.9999, 0.0);
            Target b2 = Make("M31", 0.0001,  0.0);
            Assert.True(TargetIdentity.AreSameTarget(a2, b2));
        }

        [Fact]
        public void AreSameTarget_HighDec_CosDecScalesRaSpan()
        {
            // RA degrees converge toward the poles, so the same RA delta corresponds
            // to a smaller on-sky angle at Dec 80 than at the equator. Two M31 rows
            // 0.05 hours apart (= 0.75 RA-deg) at Dec 80 collapse to a sky distance
            // of 0.75 * cos(80 deg) ≈ 0.13 deg = 7.8 arcmin. At the equator the same
            // 0.05 hr is 0.75 deg = 45 arcmin -- definitively over tolerance.
            // Pick a smaller delta so the high-dec case lands within tolerance.
            // 0.001 hr = 0.015 RA-deg. At Dec 89: 0.015 * cos(89) = 0.00026 deg ≈ 1
            // arcsec, within. At equator: 0.015 deg = 54 arcsec, within. So both are
            // "same" -- not a great test. Reverse: pick a delta that's within at
            // high dec but over at equator. 0.05 RA hr at Dec 89: 0.75 * cos(89) =
            // 0.013 deg = 47 arcsec, within. At equator: 0.75 deg = 45 arcmin, over.
            Target hiNorth_a = Make("X", 0.0,  89.0);
            Target hiNorth_b = Make("X", 0.05, 89.0);
            Assert.True(TargetIdentity.AreSameTarget(hiNorth_a, hiNorth_b));

            Target eq_a = Make("X", 0.0,  0.0);
            Target eq_b = Make("X", 0.05, 0.0);
            Assert.False(TargetIdentity.AreSameTarget(eq_a, eq_b));
        }

        [Fact]
        public void AreSameTarget_OppositeHemispheres_ReturnsFalse()
        {
            // Same magnitude declination but opposite hemispheres -- 82 deg apart
            // on Dec. Vastly beyond tolerance.
            Target a = Make("X", 6.0, 41.0, north: true);
            Target b = Make("X", 6.0, 41.0, north: false);
            Assert.False(TargetIdentity.AreSameTarget(a, b));
        }

        // -------- SelectNewTargets --------

        [Fact]
        public void SelectNewTargets_NullCandidates_ReturnsEmpty()
        {
            List<Target> result = TargetIdentity.SelectNewTargets(null, new Target[0]);
            Assert.Empty(result);
        }

        [Fact]
        public void SelectNewTargets_FiltersDuplicatesAgainstExisting()
        {
            Target existing = Make("M31", 0.71, 41.27);
            Target dupe     = Make("M31", 0.71, 41.27);
            Target stars    = Make("M31 Stars", 0.71, 41.27);
            Target novel    = Make("M42", 5.59, -5.39, north: false);

            List<Target> result = TargetIdentity.SelectNewTargets(
                new[] { dupe, stars, novel }, new[] { existing });

            Assert.Single(result);
            Assert.Same(novel, result[0]);
        }

        [Fact]
        public void SelectNewTargets_DedupesWithinCandidates_FirstOccurrenceWins()
        {
            Target a = Make("M31", 0.71, 41.27);
            Target b = Make("M31", 0.71, 41.27);   // same target

            List<Target> result = TargetIdentity.SelectNewTargets(
                new[] { a, b }, new Target[0]);

            Assert.Single(result);
            Assert.Same(a, result[0]);
        }

        [Fact]
        public void SelectNewTargets_PreservesInputOrder()
        {
            Target a = Make("Alpha", 1.0, 10.0);
            Target b = Make("Bravo", 2.0, 20.0);
            Target c = Make("Charlie", 3.0, 30.0);

            List<Target> result = TargetIdentity.SelectNewTargets(
                new[] { c, a, b }, new Target[0]);

            Assert.Equal(3, result.Count);
            Assert.Same(c, result[0]);
            Assert.Same(a, result[1]);
            Assert.Same(b, result[2]);
        }

        [Fact]
        public void SelectNewTargets_NullElements_AreSkipped()
        {
            Target a = Make("Alpha", 1.0, 10.0);
            List<Target> result = TargetIdentity.SelectNewTargets(
                new[] { null, a, null }, new Target[0]);
            Assert.Single(result);
            Assert.Same(a, result[0]);
        }
    }
}
