using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TargetPlanner.ImageLibrary;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // ImageLibraryLoader.ParseFileAsync wraps Astronomy.XISF.XisfHeaderReader and
    // produces a bare Target from a light-frame XISF header. Tests use synthetic
    // XISF files generated per-test via SyntheticXisf so fixtures are dynamic.
    public class ImageLibraryLoaderTests
    {
        [Fact]
        public async Task ParseFileAsync_NullOrWhitespacePath_ReturnsNull()
        {
            Assert.Null(await ImageLibraryLoader.ParseFileAsync(null, CancellationToken.None));
            Assert.Null(await ImageLibraryLoader.ParseFileAsync("", CancellationToken.None));
            Assert.Null(await ImageLibraryLoader.ParseFileAsync("   ", CancellationToken.None));
        }

        [Fact]
        public async Task ParseFileAsync_NonExistentFile_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            Assert.Null(await ImageLibraryLoader.ParseFileAsync(
                dir.FilePath("nope.xisf"), CancellationToken.None));
        }

        [Fact]
        public async Task ParseFileAsync_LightFrame_ReturnsTargetWithRaInHours()
        {
            // M51 (Whirlpool): RA 13h 29m 52.7s ≈ 13.498 hours = 202.469625 degrees.
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M51_light.xisf");
            SyntheticXisf.Write(path, SyntheticXisf.LightFrameKeywords("M51", 202.469625, 47.195));

            Target t = await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None);

            Assert.NotNull(t);
            Assert.Equal("M51", t.Name);
            // FITS RA is degrees; loader divides by 15 for hours.
            Assert.Equal(13.498, t.RightAscension, precision: 3);
            Assert.Equal(47.195, t.Declination, precision: 3);
            Assert.True(t.North);
        }

        [Fact]
        public async Task ParseFileAsync_DarkFrame_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("dark.xisf");
            SyntheticXisf.Write(path, new Dictionary<string, string>
            {
                ["OBJECT"]   = "Dark",
                ["RA"]       = "0.0",
                ["DEC"]      = "0.0",
                ["IMAGETYP"] = "DARK",
            });

            // Non-light frames are skipped here rather than by directory name --
            // calibration frames anywhere in the tree don't become targets.
            Assert.Null(await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None));
        }

        [Fact]
        public async Task ParseFileAsync_MissingImageType_ReturnsNull()
        {
            // Absent IMAGETYP is treated as not-a-light-frame (defensive default;
            // the user's pipeline always stamps IMAGETYP on light captures).
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("no_imagetyp.xisf");
            SyntheticXisf.Write(path, new Dictionary<string, string>
            {
                ["OBJECT"] = "M51",
                ["RA"]     = "202.469",
                ["DEC"]    = "47.195",
            });

            Assert.Null(await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None));
        }

        [Fact]
        public async Task ParseFileAsync_MissingRA_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("no_ra.xisf");
            SyntheticXisf.Write(path, new Dictionary<string, string>
            {
                ["OBJECT"]   = "M51",
                ["DEC"]      = "47.195",
                ["IMAGETYP"] = "LIGHT",
            });

            Assert.Null(await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None));
        }

        [Fact]
        public async Task ParseFileAsync_StarsSuffix_StrippedFromObjectName()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M101_Stars.xisf");
            SyntheticXisf.Write(path,
                SyntheticXisf.LightFrameKeywords("M101 P1 Stars", 210.0, 54.0));

            Target t = await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None);

            Assert.NotNull(t);
            // TargetIdentity.NormalizeName strips " Stars" so the stars frame
            // collapses onto its parent.
            Assert.Equal("M101 P1", t.Name);
        }

        [Fact]
        public async Task ParseFileAsync_EmptyObjectName_FallsBackToFileName()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("Sh2-126.xisf");
            SyntheticXisf.Write(path, new Dictionary<string, string>
            {
                ["OBJECT"]   = "",
                ["RA"]       = "330.0",
                ["DEC"]      = "55.0",
                ["IMAGETYP"] = "LIGHT",
            });

            Target t = await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None);

            Assert.NotNull(t);
            // Filename stem fallback (without extension).
            Assert.Equal("Sh2-126", t.Name);
        }

        [Fact]
        public async Task ParseFileAsync_RaInRangeZeroToTwentyFour()
        {
            // RA conversion is (deg / 15) % 24, then wrap negatives. Verify a
            // wraparound case to be sure.
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("highRA.xisf");
            // 360 degrees = 24 hours -> wraps to 0
            SyntheticXisf.Write(path, SyntheticXisf.LightFrameKeywords("Wrap", 360.0, 0.0));

            Target t = await ImageLibraryLoader.ParseFileAsync(path, CancellationToken.None);

            Assert.NotNull(t);
            Assert.InRange(t.RightAscension, 0.0, 24.0);
        }
    }
}
