using System.Collections.Generic;
using System.IO;
using TargetPlanner.Settings;
using TargetPlanner.Tests.Tests.Support;
using Xunit;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Tests.Tests
{
    // LocalTargetStore Save/Load round-trip behaviour. The DTO carries only
    // Name/RA/Dec/North; Directory and Enabled default on load. Tests use
    // TempDirectory so %APPDATA% is never touched.
    public class LocalTargetStorePersistenceTests
    {
        private static Target Make(string name, double ra, double decMag, bool north = true) =>
            new Target(
                name: name,
                rightAscension: ra,
                declination: decMag, north: north,
                directory: string.Empty,
                enabled: true);

        [Fact]
        public void Save_Load_EmptyEnumerable_RoundTripsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            LocalTargetStore.Save(path, new Target[0]);
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Empty(loaded);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Save_Load_SingleTarget_RoundTripsFields()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            Target orig = Make("M31", 0.712306, 41.269167);
            LocalTargetStore.Save(path, new[] { orig });
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Single(loaded);
            Assert.Equal("M31", loaded[0].Name);
            Assert.Equal(0.712306, loaded[0].RightAscension, precision: 6);
            Assert.Equal(41.269167, loaded[0].Declination, precision: 6);
            Assert.True(loaded[0].North);
        }

        [Fact]
        public void Save_Load_MultipleTargets_RoundTripsAll()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            Target[] originals =
            {
                Make("M31", 0.71, 41.27),
                Make("M42", 5.59,  5.39, north: false),   // Orion, southern Dec
                Make("M81", 9.93, 69.07),
            };
            LocalTargetStore.Save(path, originals);
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Equal(3, loaded.Count);
            Assert.Equal("M31", loaded[0].Name);
            Assert.Equal("M42", loaded[1].Name);
            Assert.Equal("M81", loaded[2].Name);
        }

        [Fact]
        public void Save_Load_SignedHemisphere_Preserved()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            // Southern target: magnitude stored positive, North flag false.
            Target orig = Make("M42", 5.59, 5.39, north: false);
            LocalTargetStore.Save(path, new[] { orig });
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Single(loaded);
            Assert.False(loaded[0].North);
            Assert.Equal(5.39, loaded[0].Declination, precision: 6);   // magnitude
        }

        [Fact]
        public void Save_NullEnumerable_WritesEmptyArray()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            LocalTargetStore.Save(path, null);
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Empty(loaded);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Save_NullTargetInList_IsSkipped()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            Target real = Make("M31", 0.71, 41.27);
            LocalTargetStore.Save(path, new[] { null, real, null });
            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Single(loaded);
            Assert.Equal("M31", loaded[0].Name);
        }

        [Fact]
        public void Load_WhitespaceNameInDto_IsSkipped()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");

            // Hand-craft JSON with two whitespace-name entries surrounding a real one.
            File.WriteAllText(path, @"[
                { ""Name"": ""   "", ""RightAscension"": 1.0, ""Declination"": 10.0, ""North"": true },
                { ""Name"": ""M31"", ""RightAscension"": 0.71, ""Declination"": 41.27, ""North"": true },
                { ""Name"": """",    ""RightAscension"": 2.0, ""Declination"": 20.0, ""North"": true }
            ]");

            List<Target> loaded = LocalTargetStore.Load(path);

            Assert.Single(loaded);
            Assert.Equal("M31", loaded[0].Name);
        }

        [Fact]
        public void Load_CorruptJson_ReturnsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("local-targets.json");
            File.WriteAllText(path, "{ this is not a list");

            List<Target> loaded = LocalTargetStore.Load(path);
            Assert.Empty(loaded);
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmpty()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("does-not-exist.json");

            List<Target> loaded = LocalTargetStore.Load(path);
            Assert.Empty(loaded);
            // Load doesn't create the file when missing (unlike SettingsStore).
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Load_NullPath_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => LocalTargetStore.Load(null));
        }

        [Fact]
        public void Save_NullPath_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                LocalTargetStore.Save(null, new Target[0]));
        }
    }
}
