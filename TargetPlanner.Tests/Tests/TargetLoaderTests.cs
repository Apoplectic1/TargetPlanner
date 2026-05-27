using System.IO;
using TargetPlanner.Nina;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // TargetLoader.ParseFile takes a NINA .json sequence file and returns one
    // Astronomy.Core Target (or null). Tests use TempDirectory + hand-written
    // JSON strings so the fixture is the shape, not a committed file.
    public class TargetLoaderTests
    {
        // Minimum NINA-shaped target JSON: Target.TargetName + Target.InputCoordinates
        // with optional D/M/S components and NegativeDec flag.
        private static string BuildTargetJson(
            string name,
            double raHours = 0, double raMinutes = 0, double raSeconds = 0,
            double decDegrees = 0, double decMinutes = 0, double decSeconds = 0,
            bool negativeDec = false)
        {
            return $@"{{
                ""Target"": {{
                    ""TargetName"": ""{name}"",
                    ""InputCoordinates"": {{
                        ""RAHours"":     {raHours},
                        ""RAMinutes"":   {raMinutes},
                        ""RASeconds"":   {raSeconds},
                        ""DecDegrees"":  {decDegrees},
                        ""DecMinutes"":  {decMinutes},
                        ""DecSeconds"":  {decSeconds},
                        ""NegativeDec"": {(negativeDec ? "true" : "false")}
                    }}
                }}
            }}";
        }

        [Fact]
        public void ParseFile_NullOrWhitespacePath_ReturnsNull()
        {
            Assert.Null(TargetLoader.ParseFile(null));
            Assert.Null(TargetLoader.ParseFile(""));
            Assert.Null(TargetLoader.ParseFile("   "));
        }

        [Fact]
        public void ParseFile_NonExistentFile_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            Assert.Null(TargetLoader.ParseFile(dir.FilePath("does-not-exist.json")));
        }

        [Fact]
        public void ParseFile_M31_PositiveDecPositiveNorth()
        {
            // M31 (Andromeda): RA 0h 42m 44.3s, Dec +41° 16' 9.0"
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M31.json");
            File.WriteAllText(path,
                BuildTargetJson("M31", raHours: 0, raMinutes: 42, raSeconds: 44.3,
                    decDegrees: 41, decMinutes: 16, decSeconds: 9.0));

            Target t = TargetLoader.ParseFile(path);

            Assert.NotNull(t);
            Assert.Equal("M31", t.Name);
            // 0h + 42m + 44.3s → 0.7122 hours
            Assert.Equal(0.712306, t.RightAscension, precision: 4);
            Assert.True(t.North);
            // 41° 16' 9.0" → 41.2692 deg (positive magnitude, north=true)
            Assert.Equal(41.269167, t.Declination, precision: 4);
            Assert.True(t.Enabled);
        }

        [Fact]
        public void ParseFile_NegativeDecFlag_NormalizesToSouthernHemisphere()
        {
            // M42 (Orion Nebula): RA 5h 35m 17.3s, Dec -5° 23' 28"
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M42.json");
            File.WriteAllText(path,
                BuildTargetJson("M42", raHours: 5, raMinutes: 35, raSeconds: 17.3,
                    decDegrees: 5, decMinutes: 23, decSeconds: 28,
                    negativeDec: true));

            Target t = TargetLoader.ParseFile(path);

            Assert.NotNull(t);
            // Target ctor flips negative-magnitude declination to (positive, North=false).
            Assert.False(t.North);
            Assert.Equal(5.391111, t.Declination, precision: 4);
        }

        [Fact]
        public void ParseFile_SexagesimalRA_AssemblesCorrectly()
        {
            // 12h + 30m + 30s = 12 + 0.5 + 0.008333... = 12.50833... hours
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("RAtest.json");
            File.WriteAllText(path,
                BuildTargetJson("RA test", raHours: 12, raMinutes: 30, raSeconds: 30,
                    decDegrees: 30));

            Target t = TargetLoader.ParseFile(path);

            Assert.NotNull(t);
            Assert.Equal(12.508333, t.RightAscension, precision: 5);
        }

        [Fact]
        public void ParseFile_StarsSuffix_StrippedFromName()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M101 Stars.json");
            File.WriteAllText(path, BuildTargetJson("M101 Stars", raHours: 14, decDegrees: 54));

            Target t = TargetLoader.ParseFile(path);

            Assert.NotNull(t);
            // TargetIdentity.NormalizeName trims " Stars" so the imaging-stars
            // capture collapses onto its parent target downstream.
            Assert.Equal("M101", t.Name);
        }

        [Fact]
        public void ParseFile_MissingTargetNode_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("notarget.json");
            File.WriteAllText(path, @"{ ""SomeOtherField"": 42 }");

            Assert.Null(TargetLoader.ParseFile(path));
        }

        [Fact]
        public void ParseFile_MissingTargetName_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("noname.json");
            File.WriteAllText(path, @"{ ""Target"": { ""InputCoordinates"": {} } }");

            Assert.Null(TargetLoader.ParseFile(path));
        }

        [Fact]
        public void ParseFile_MissingInputCoordinates_ReturnsNull()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("nocoords.json");
            File.WriteAllText(path, @"{ ""Target"": { ""TargetName"": ""M31"" } }");

            Assert.Null(TargetLoader.ParseFile(path));
        }

        [Fact]
        public void ParseFile_MalformedJson_ReturnsNull_NoThrow()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("bad.json");
            File.WriteAllText(path, "{ this is not valid json");

            // TargetScanner parses many files in a directory walk; a single bad
            // file must not abort the batch. Null + tp.log warning is the contract.
            Assert.Null(TargetLoader.ParseFile(path));
        }

        [Fact]
        public void ParseFile_DirectoryFieldCarriesSourcePath()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("M31.json");
            File.WriteAllText(path, BuildTargetJson("M31", raHours: 0, decDegrees: 41));

            Target t = TargetLoader.ParseFile(path);

            Assert.NotNull(t);
            Assert.Equal(path, t.Directory);
        }
    }
}
