using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TargetPlanner.Filters;
using TargetPlanner.Tests.Tests.Support;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // FilterLibrary Save/LoadOrDefault round-trip + MigrateLegacyFields fill on
    // legacy CenterNm=0 entries. Tests use TempDirectory so %APPDATA% is never
    // touched.
    public class FilterLibraryPersistenceTests
    {
        [Fact]
        public void Save_LoadOrDefault_RoundTripsFilters()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");

            FilterLibrary orig = new FilterLibrary(new[]
            {
                new Filter("H",   30.0, 5.0,  false, -15.0, 5.0, 0.0, 656.3, 3.0),
                new Filter("O",   60.0, 5.0,  false, -15.0, 5.0, 0.0, 500.7, 3.0),
                new Filter("L",   90.0, 10.0, true,  -10.0, 3.0, 1.5, 550.0, 300.0),
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
        public void LoadOrDefault_LegacyZeroCenterNm_FilledFromBuiltin()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");

            // Pre-CenterNm filters.json: deserialised entries have CenterNm = 0
            // (the C# default for the missing JSON field). MigrateLegacyFields
            // must auto-fill from the matching builtin's CenterNm.
            Filter h = new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, /*centerNm*/ 0.0, /*bandwidthNm*/ 0.0);
            File.WriteAllText(path, JsonConvert.SerializeObject(new[] { h }));

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            Filter builtinH = FilterLibrary.FindBuiltinDefault("H");
            Assert.Single(loaded.Filters);
            Assert.Equal(builtinH.CenterNm, loaded.Filters[0].CenterNm);
        }

        [Fact]
        public void LoadOrDefault_UserRenamedFilter_KeepsZeroCenterNm()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");

            // Custom filter name (no builtin baseline): MigrateLegacyFields can't
            // fill CenterNm, so it stays 0. User repairs via Edit Filters UI.
            Filter custom = new Filter("MyCustomNarrowband", 30.0, 5.0, false, -15.0, 5.0, 0.0, 0.0, 0.0);
            File.WriteAllText(path, JsonConvert.SerializeObject(new[] { custom }));

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            Assert.Single(loaded.Filters);
            Assert.Equal(0.0, loaded.Filters[0].CenterNm);
        }

        [Fact]
        public void LoadOrDefault_NonZeroCenterNm_PassedThroughUnchanged()
        {
            using TempDirectory dir = new TempDirectory();
            string path = dir.FilePath("filters.json");

            // User-set CenterNm on the H name -- migration must NOT overwrite a
            // legitimately-non-zero user value with the builtin (only the legacy
            // 0 sentinel triggers fill).
            Filter h = new Filter("H", 30.0, 5.0, false, -15.0, 5.0, 0.0, /*centerNm*/ 657.0, 3.0);
            File.WriteAllText(path, JsonConvert.SerializeObject(new[] { h }));

            FilterLibrary loaded = FilterLibrary.LoadOrDefault(path);

            Assert.Equal(657.0, loaded.Filters[0].CenterNm);
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
