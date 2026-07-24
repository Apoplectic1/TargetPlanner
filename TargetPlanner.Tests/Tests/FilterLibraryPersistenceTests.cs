using System.IO;
using System.Linq;
using TargetPlanner.Filters;
using TargetPlanner.Tests.Tests.Support;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // FilterLibrary Save/LoadOrDefault round-trip. Tests use TempDirectory so
    // %APPDATA% is never touched. (The legacy CenterNm=0 migration fill was
    // deleted 2026-07-24 with the K-S Δmag gate migration -- no back-compat;
    // an old filters.json is deleted and re-seeded from builtins.)
    public class FilterLibraryPersistenceTests
    {
        [Fact]
        public void Save_LoadOrDefault_RoundTripsFilters()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");

            FilterLibrary orig = new FilterLibrary(new[]
            {
                new Filter("H", 1.0,  656.3,   3.0),
                new Filter("O", 0.85, 500.7,   3.0),
                new Filter("L", 0.30, 550.0, 300.0),
            });
            orig.Save(path);
            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            Assert.Equal(3, loaded.Filters.Count);
            for (int i = 0; i < 3; i++)
                Assert.Equal(orig.Filters[i], loaded.Filters[i]);
        }

        [Fact]
        public void LoadOrDefault_MissingFile_ReturnsDefaultLibrary()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("does-not-exist.json");

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            // DefaultLibrary returns the builtin set; missing-file path does NOT
            // create the file (unlike SettingsStore).
            Assert.False(File.Exists(path));
            Assert.Equal(FilterLibrary.BuiltinDefaults.Count, loaded.Filters.Count);
            Assert.Equal(
                FilterLibrary.BuiltinDefaults.Select(f => f.Name).ToArray(),
                loaded.Filters.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void LoadOrDefault_CorruptJson_ReturnsDefaultLibrary()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");
            File.WriteAllText(path, "{ not a json array");

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            Assert.Equal(FilterLibrary.BuiltinDefaults.Count, loaded.Filters.Count);
        }

        [Fact]
        public void LoadOrDefault_EmptyArray_ReturnsDefaultLibrary()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");
            File.WriteAllText(path, "[]");

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            // Empty file means "user explicitly cleared" -- but the source treats
            // it the same as missing: fall through to defaults. (DefaultLibrary
            // ensures the H/O/S/L/R/G/B set is always available.)
            Assert.Equal(FilterLibrary.BuiltinDefaults.Count, loaded.Filters.Count);
        }

        [Fact]
        public void LoadOrDefault_NullPath_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => FilterLibrary.LoadOrDefault(null));
        }

        [Fact]
        public void Save_CreatesParentDirectoryIfMissing()
        {
            using TempDirectory dir = new TempDirectory();
            string path = Path.Combine(dir.Path, "sub", "filters.json");

            new FilterLibrary(FilterLibrary.BuiltinDefaults).Save(path);

            Assert.True(File.Exists(path));
        }
    }
}
