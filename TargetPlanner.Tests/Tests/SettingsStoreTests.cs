using System.Collections.Generic;
using System.IO;
using Astronomy.NINA.Persistence;
using Newtonsoft.Json;
using TargetPlanner.Settings;
using TargetPlanner.Tests.Tests.Support;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // SettingsStore Load/Save round-trip behaviour. Tests cover the three load paths
    // documented in the source: missing-file -> seed + save, present-and-current ->
    // Pattern C fill + Custom-name strip, and present-but-broken (corrupt JSON /
    // version mismatch) -> fallback seed + save. All tests use TempDirectory so
    // %APPDATA% is never touched.
    public class SettingsStoreTests
    {
        private static string SeedJson(int version, string ninaRoot = null, string imgRoot = null,
            List<NamedSite> sites = null, string lastSelected = null)
        {
            AppSettings s = new AppSettings
            {
                Version                  = version,
                NinaTargetsRoot          = ninaRoot,
                ImageLibraryRoot         = imgRoot,
                NamedLocations           = sites,
                LastSelectedLocationName = lastSelected,
            };
            return JsonConvert.SerializeObject(s, Formatting.Indented);
        }

        [Fact]
        public void Load_MissingFile_ReturnsSeedAndWritesIt()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            AppSettings result = SettingsStore.Load(path);

            // Seed has Penns Park as LastSelected and 4 NamedLocations.
            Assert.Equal("Penns Park", result.LastSelectedLocationName);
            Assert.Equal(4, result.NamedLocations.Count);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Load_PresentAndCurrent_RoundTripsUserState()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            List<NamedSite> sites = new List<NamedSite>
            {
                new NamedSite { Name = "Home", Latitude = 40.0, North = true, Longitude = 75.0, West = true, BortleClass = 6 },
            };
            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: @"C:\Nina", imgRoot: @"C:\Img", sites: sites, lastSelected: "Home"));

            AppSettings result = SettingsStore.Load(path);

            Assert.Equal("Home", result.LastSelectedLocationName);
            Assert.Equal(@"C:\Nina", result.NinaTargetsRoot);
            Assert.Equal(@"C:\Img", result.ImageLibraryRoot);
            Assert.Single(result.NamedLocations);
            Assert.Equal("Home", result.NamedLocations[0].Name);
        }

        [Fact]
        public void Load_NullNinaRoot_PatternCFillsFromSeed()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            // User has a real Home site but NinaTargetsRoot is null (older schema or
            // hand-edited to null). Pattern C must fill from seed.
            List<NamedSite> sites = new List<NamedSite>
            {
                new NamedSite { Name = "Home", Latitude = 40.0, North = true, Longitude = 75.0, West = true },
            };
            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: null, imgRoot: null, sites: sites));

            AppSettings result = SettingsStore.Load(path);

            Assert.False(string.IsNullOrEmpty(result.NinaTargetsRoot));
            Assert.False(string.IsNullOrEmpty(result.ImageLibraryRoot));
            // User's site list is preserved (not overridden by seed).
            Assert.Single(result.NamedLocations);
            Assert.Equal("Home", result.NamedLocations[0].Name);
        }

        [Fact]
        public void Load_EmptyNamedLocations_PatternCReplacesWithSeedList()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: @"C:\Nina", imgRoot: @"C:\Img",
                sites: new List<NamedSite>()));

            AppSettings result = SettingsStore.Load(path);

            // Empty list triggers seed fill -- the 4 personal-default sites land.
            Assert.Equal(4, result.NamedLocations.Count);
        }

        [Fact]
        public void Load_NullNamedLocations_PatternCReplacesWithSeedList()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: @"C:\Nina", imgRoot: @"C:\Img", sites: null));

            AppSettings result = SettingsStore.Load(path);

            Assert.Equal(4, result.NamedLocations.Count);
        }

        [Fact]
        public void Load_CustomSiteInList_IsStripped()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            List<NamedSite> sites = new List<NamedSite>
            {
                new NamedSite { Name = "Home",   Latitude = 40.0, North = true, Longitude = 75.0, West = true },
                new NamedSite { Name = "Custom", Latitude = 40.0, North = true, Longitude = 75.0, West = true },
                new NamedSite { Name = "Office", Latitude = 41.0, North = true, Longitude = 76.0, West = true },
            };
            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: @"C:\Nina", imgRoot: @"C:\Img", sites: sites));

            AppSettings result = SettingsStore.Load(path);

            Assert.Equal(2, result.NamedLocations.Count);
            Assert.DoesNotContain(result.NamedLocations, s =>
                string.Equals(s.Name, "Custom", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Load_CustomSiteCaseInsensitive_IsStripped()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            List<NamedSite> sites = new List<NamedSite>
            {
                new NamedSite { Name = "Home",   Latitude = 40.0, North = true, Longitude = 75.0, West = true },
                new NamedSite { Name = "CUSTOM", Latitude = 40.0, North = true, Longitude = 75.0, West = true },
            };
            File.WriteAllText(path, SeedJson(AppSettings.CurrentVersion,
                ninaRoot: @"C:\Nina", imgRoot: @"C:\Img", sites: sites));

            AppSettings result = SettingsStore.Load(path);

            Assert.Single(result.NamedLocations);
            Assert.Equal("Home", result.NamedLocations[0].Name);
        }

        [Fact]
        public void Load_CorruptJson_FallsBackToSeedAndOverwrites()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");
            File.WriteAllText(path, "{ not valid json at all");

            AppSettings result = SettingsStore.Load(path);

            // Fallback path returns the seed and writes it back, replacing the corrupt
            // contents -- the next launch finds a valid file.
            Assert.Equal("Penns Park", result.LastSelectedLocationName);
            Assert.Equal(4, result.NamedLocations.Count);
            string rewritten = File.ReadAllText(path);
            Assert.Contains("Penns Park", rewritten);
        }

        [Fact]
        public void Load_VersionMismatch_FallsBackToSeed()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("settings.json");

            File.WriteAllText(path, SeedJson(
                version: AppSettings.CurrentVersion + 999,
                ninaRoot: @"C:\HandEdited",
                imgRoot: @"C:\HandEdited",
                sites: new List<NamedSite>
                {
                    new NamedSite { Name = "WillBeLost", Latitude = 1, North = true, Longitude = 1, West = true },
                }));

            AppSettings result = SettingsStore.Load(path);

            // User's hand-edited values are discarded; seed lands.
            Assert.Equal("Penns Park", result.LastSelectedLocationName);
            Assert.DoesNotContain(result.NamedLocations, s => s.Name == "WillBeLost");
        }

        [Fact]
        public void Save_NullPath_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                SettingsStore.Save(null, new AppSettings()));
        }

        [Fact]
        public void Load_NullPath_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => SettingsStore.Load(null));
        }

        [Fact]
        public void Save_CreatesDirectoryIfMissing()
        {
            using TempDirectory dir = new TempDirectory();
            // Path includes a subfolder that doesn't exist yet -- Save must create it.
            string path = Path.Combine(dir.Path, "sub", "nested", "settings.json");

            SettingsStore.Save(path, new AppSettings { LastSelectedLocationName = "Anywhere" });

            Assert.True(File.Exists(path));
            AppSettings loaded = SettingsStore.Load(path);
            Assert.Equal("Anywhere", loaded.LastSelectedLocationName);
        }
    }
}
