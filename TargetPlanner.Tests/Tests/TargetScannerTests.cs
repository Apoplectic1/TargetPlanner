using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TargetPlanner.Targets;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // TargetScanner.ScanAsync walks a path, applies per-kind filtering, parses
    // each surviving file (JSON / XISF), and centroids groups of frames /
    // mosaic panels into one Target per real sky target. Tests build a tree
    // under TempDirectory and assert on the returned Target list.
    public class TargetScannerTests
    {
        // Minimum NINA-shaped target JSON (same shape as TargetLoaderTests
        // helper -- duplicated here so the test class is self-contained).
        private static string BuildTargetJson(
            string name,
            double raHours = 0, double raMinutes = 0,
            double decDegrees = 0, bool negativeDec = false) =>
            $@"{{
                ""Target"": {{
                    ""TargetName"": ""{name}"",
                    ""InputCoordinates"": {{
                        ""RAHours"":     {raHours},
                        ""RAMinutes"":   {raMinutes},
                        ""RASeconds"":   0,
                        ""DecDegrees"":  {decDegrees},
                        ""DecMinutes"":  0,
                        ""DecSeconds"":  0,
                        ""NegativeDec"": {(negativeDec ? "true" : "false")}
                    }}
                }}
            }}";

        // -------- Edge inputs --------

        [Fact]
        public async Task ScanAsync_NullOrWhitespacePath_ReturnsEmpty()
        {
            Assert.Empty(await TargetScanner.ScanAsync(
                null, TargetFileKinds.All, CancellationToken.None));
            Assert.Empty(await TargetScanner.ScanAsync(
                "", TargetFileKinds.All, CancellationToken.None));
            Assert.Empty(await TargetScanner.ScanAsync(
                "   ", TargetFileKinds.All, CancellationToken.None));
        }

        [Fact]
        public async Task ScanAsync_KindsNone_ReturnsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            // Even if there are files in the dir, TargetFileKinds.None is a no-op.
            File.WriteAllText(dir.FilePath("any.json"), BuildTargetJson("M31"));

            Assert.Empty(await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.None, CancellationToken.None));
        }

        [Fact]
        public async Task ScanAsync_NonExistentDirectory_ReturnsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            string missing = Path.Combine(dir.Path, "no-such-subdir");

            // Non-existent path logs a warn but doesn't throw.
            Assert.Empty(await TargetScanner.ScanAsync(
                missing, TargetFileKinds.All, CancellationToken.None));
        }

        [Fact]
        public async Task ScanAsync_EmptyDirectory_ReturnsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            Assert.Empty(await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.All, CancellationToken.None));
        }

        // -------- JSON paths --------

        [Fact]
        public async Task ScanAsync_StandaloneJson_OneTarget()
        {
            using TempDirectory dir = new TempDirectory();
            File.WriteAllText(dir.FilePath("M31.json"),
                BuildTargetJson("M31", raHours: 0, raMinutes: 42, decDegrees: 41));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Json, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M31", targets[0].Name);
        }

        [Fact]
        public async Task ScanAsync_MosaicPanels_CollapseToOneTargetAtCentroid()
        {
            // A folder of "... Panel <n>" files is collapsed to one target at
            // the centroid of the panels' planned coordinates.
            using TempDirectory dir = new TempDirectory();
            string mosaicDir = Path.Combine(dir.Path, "Sh2-126 Mosaic");
            Directory.CreateDirectory(mosaicDir);
            File.WriteAllText(Path.Combine(mosaicDir, "Sh2-126 Panel 1.json"),
                BuildTargetJson("Sh2-126 Panel 1", raHours: 21, raMinutes: 30, decDegrees: 55));
            File.WriteAllText(Path.Combine(mosaicDir, "Sh2-126 Panel 2.json"),
                BuildTargetJson("Sh2-126 Panel 2", raHours: 21, raMinutes: 40, decDegrees: 55));
            File.WriteAllText(Path.Combine(mosaicDir, "Sh2-126 Panel 3.json"),
                BuildTargetJson("Sh2-126 Panel 3", raHours: 21, raMinutes: 50, decDegrees: 55));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Json, CancellationToken.None);

            // Three panel files -> ONE target named after the folder.
            Assert.Single(targets);
            Assert.Equal("Sh2-126 Mosaic", targets[0].Name);
            // Centroid should sit roughly at the middle panel's RA (21h 40m).
            Assert.InRange(targets[0].RightAscension, 21.6, 21.8);
        }

        [Fact]
        public async Task ScanAsync_CometJson_Excluded()
        {
            // Comet targets are excluded -- the name starts with "Comet ".
            using TempDirectory dir = new TempDirectory();
            File.WriteAllText(dir.FilePath("M31.json"),
                BuildTargetJson("M31", raHours: 0, raMinutes: 42, decDegrees: 41));
            File.WriteAllText(dir.FilePath("Comet 12P.json"),
                BuildTargetJson("Comet 12P Pons-Brooks", raHours: 1, decDegrees: 30));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Json, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M31", targets[0].Name);
        }

        [Fact]
        public async Task ScanAsync_RecursiveWalk_FindsNestedJson()
        {
            // Scanner walks subdirectories depth-first; depth doesn't matter.
            using TempDirectory dir = new TempDirectory();
            string nested = Path.Combine(dir.Path, "Catalog", "Messier", "M31");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "M31.json"),
                BuildTargetJson("M31", raHours: 0, raMinutes: 42, decDegrees: 41));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Json, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M31", targets[0].Name);
        }

        // -------- XISF paths --------

        [Fact]
        public async Task ScanAsync_XisfInCaptures_CollapseToOneTargetAtCentroid()
        {
            // A target folder's Captures/ subtree of light frames collapses to
            // one target whose coordinate is the centroid of every frame.
            using TempDirectory dir = new TempDirectory();
            string targetDir = Path.Combine(dir.Path, "M51");
            string capturesDir = Path.Combine(targetDir, "Captures");
            Directory.CreateDirectory(capturesDir);

            // Two frames slightly offset (plate-solve dither across frames).
            SyntheticXisf.Write(Path.Combine(capturesDir, "frame_001.xisf"),
                SyntheticXisf.LightFrameKeywords("M51", 202.4, 47.2));
            SyntheticXisf.Write(Path.Combine(capturesDir, "frame_002.xisf"),
                SyntheticXisf.LightFrameKeywords("M51", 202.6, 47.1));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Xisf, CancellationToken.None);

            Assert.Single(targets);
            // Target name is the parent folder name (not the OBJECT keyword);
            // the centroid sits between the two frame coords.
            Assert.Equal("M51", targets[0].Name);
            Assert.InRange(targets[0].RightAscension, 13.49, 13.51);
            Assert.InRange(targets[0].Declination, 47.1, 47.2);
        }

        [Fact]
        public async Task ScanAsync_XisfOutsideCaptures_Ignored()
        {
            // .xisf files outside a Captures/ subtree (processing outputs,
            // master frames, loose files) are dropped before parsing.
            using TempDirectory dir = new TempDirectory();
            SyntheticXisf.Write(dir.FilePath("loose.xisf"),
                SyntheticXisf.LightFrameKeywords("Loose", 100.0, 30.0));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Xisf, CancellationToken.None);

            Assert.Empty(targets);
        }

        [Fact]
        public async Task ScanAsync_CometXisfFolder_Excluded()
        {
            // A "Comet ..." target folder is dropped before its headers are read.
            using TempDirectory dir = new TempDirectory();
            string cometDir = Path.Combine(dir.Path, "Comet 12P", "Captures");
            Directory.CreateDirectory(cometDir);
            SyntheticXisf.Write(Path.Combine(cometDir, "frame.xisf"),
                SyntheticXisf.LightFrameKeywords("Comet 12P", 50.0, 25.0));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Xisf, CancellationToken.None);

            Assert.Empty(targets);
        }

        // -------- Single-file branch --------

        [Fact]
        public async Task ScanAsync_SingleJsonFile_ReturnsThatTarget()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M31.json");
            File.WriteAllText(path, BuildTargetJson("M31", raHours: 0, raMinutes: 42, decDegrees: 41));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                path, TargetFileKinds.Json, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M31", targets[0].Name);
        }

        [Fact]
        public async Task ScanAsync_SingleXisfFile_ReturnsThatTarget()
        {
            // A directly-picked .xisf is its own target (no Captures/ ancestry
            // required for the single-file branch).
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M51.xisf");
            SyntheticXisf.Write(path, SyntheticXisf.LightFrameKeywords("M51", 202.5, 47.2));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                path, TargetFileKinds.Xisf, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M51", targets[0].Name);
        }

        // -------- Kind filtering --------

        [Fact]
        public async Task ScanAsync_KindsJsonOnly_IgnoresXisf()
        {
            using TempDirectory dir = new TempDirectory();
            File.WriteAllText(dir.FilePath("M31.json"),
                BuildTargetJson("M31", raHours: 0, decDegrees: 41));

            string capturesDir = Path.Combine(dir.Path, "M51", "Captures");
            Directory.CreateDirectory(capturesDir);
            SyntheticXisf.Write(Path.Combine(capturesDir, "frame.xisf"),
                SyntheticXisf.LightFrameKeywords("M51", 202.5, 47.2));

            IReadOnlyList<Target> targets = await TargetScanner.ScanAsync(
                dir.Path, TargetFileKinds.Json, CancellationToken.None);

            Assert.Single(targets);
            Assert.Equal("M31", targets[0].Name);
        }

        // -------- Cancellation --------

        [Fact]
        public async Task ScanAsync_CancelledToken_Throws()
        {
            using TempDirectory dir = new TempDirectory();
            File.WriteAllText(dir.FilePath("M31.json"), BuildTargetJson("M31"));

            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            // OperationCanceledException or TaskCanceledException (its subclass);
            // ThrowsAnyAsync accepts either.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TargetScanner.ScanAsync(dir.Path, TargetFileKinds.All, cts.Token));
        }
    }
}
