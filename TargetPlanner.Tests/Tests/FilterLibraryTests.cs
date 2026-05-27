using System.Linq;
using TargetPlanner.Filters;
using Xunit;

namespace TargetPlanner.Tests.Tests
{
    // FilterLibrary in-memory behaviour: Find / mutation primitives, BuiltinDefaults
    // factory identity, DiffersFromBuiltinDefault drift detection, and FindBuiltinDefault
    // case-insensitivity. Persistence (Save/Load round-trip, MigrateLegacyFields) is
    // Phase 2 -- those tests need temp-directory plumbing and live in
    // FilterLibraryPersistenceTests.
    public class FilterLibraryTests
    {
        private static Filter MakeFilter(string name) =>
            new Filter(name, 30.0, 5.0, false, -15.0, 5.0, 0.0, 656.3, 3.0);

        [Fact]
        public void Ctor_NullEnumerable_TreatedAsEmpty()
        {
            FilterLibrary lib = new FilterLibrary(null);
            Assert.Empty(lib.Filters);
        }

        [Fact]
        public void Find_ExistingName_ReturnsTheFilter()
        {
            Filter h = MakeFilter("H");
            FilterLibrary lib = new FilterLibrary(new[] { h, MakeFilter("O") });
            Assert.Same(h, lib.Find("H"));
        }

        [Fact]
        public void Find_MissingName_ReturnsNull()
        {
            FilterLibrary lib = new FilterLibrary(new[] { MakeFilter("H") });
            Assert.Null(lib.Find("Nonexistent"));
        }

        [Fact]
        public void Find_NullOrEmpty_ReturnsNull()
        {
            FilterLibrary lib = new FilterLibrary(new[] { MakeFilter("H") });
            Assert.Null(lib.Find(null));
            Assert.Null(lib.Find(string.Empty));
        }

        [Fact]
        public void Add_RemoveAt_Replace_MutateInPlace()
        {
            FilterLibrary lib = new FilterLibrary(new[] { MakeFilter("H") });

            Filter o = MakeFilter("O");
            lib.Add(o);
            Assert.Equal(2, lib.Filters.Count);
            Assert.Same(o, lib.Filters[1]);

            Filter s = MakeFilter("S");
            lib.Replace(0, s);
            Assert.Same(s, lib.Filters[0]);

            lib.RemoveAt(1);
            Assert.Single(lib.Filters);
            Assert.Same(s, lib.Filters[0]);
        }

        [Fact]
        public void ReplaceAll_SwapsEntireList()
        {
            FilterLibrary lib = new FilterLibrary(new[] { MakeFilter("H"), MakeFilter("O") });
            lib.ReplaceAll(new[] { MakeFilter("L"), MakeFilter("R"), MakeFilter("G") });
            Assert.Equal(3, lib.Filters.Count);
            Assert.Equal(new[] { "L", "R", "G" }, lib.Filters.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void ReplaceAll_NullEnumerable_ClearsList()
        {
            FilterLibrary lib = new FilterLibrary(new[] { MakeFilter("H") });
            lib.ReplaceAll(null);
            Assert.Empty(lib.Filters);
        }

        [Fact]
        public void BuiltinDefaults_Contains_HOSLRGB()
        {
            // Pin the seven shipped builtins so a future edit that drops or renames
            // one trips this test loudly. The values themselves are calibrated to a
            // specific filter kit; let the per-filter values change without trip if
            // the user changes their kit (don't test exact Lorentzian numbers here).
            string[] expected = { "H", "O", "S", "L", "R", "G", "B" };
            Assert.Equal(expected, FilterLibrary.BuiltinDefaults.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void FindBuiltinDefault_CaseInsensitive_ReturnsMatch()
        {
            Filter h = FilterLibrary.FindBuiltinDefault("h");
            Assert.NotNull(h);
            Assert.Equal("H", h.Name);
        }

        [Fact]
        public void FindBuiltinDefault_MissingName_ReturnsNull()
        {
            Assert.Null(FilterLibrary.FindBuiltinDefault("CustomNarrowband"));
            Assert.Null(FilterLibrary.FindBuiltinDefault(null));
            Assert.Null(FilterLibrary.FindBuiltinDefault(string.Empty));
        }

        [Fact]
        public void DiffersFromBuiltinDefault_NoBuiltin_ReturnsFalse()
        {
            // User-created filter (no factory baseline) is never "modified" --
            // there's nothing to differ from.
            Filter custom = new Filter("CustomNarrowband", 45.0, 7.0, false, -10.0, 5.0, 0.0, 489.0, 3.0);
            Assert.False(FilterLibrary.DiffersFromBuiltinDefault(custom));
        }

        [Fact]
        public void DiffersFromBuiltinDefault_FieldByField()
        {
            Filter h = FilterLibrary.FindBuiltinDefault("H");
            Assert.False(FilterLibrary.DiffersFromBuiltinDefault(h));

            // Each value field flips DiffersFromBuiltinDefault to true independently.
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { SeparationDeg  = h.SeparationDeg  + 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { WidthDays      = h.WidthDays      + 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { RelaxEnabled   = !h.RelaxEnabled }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { RelaxMinAltDeg = h.RelaxMinAltDeg - 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { RelaxMaxAltDeg = h.RelaxMaxAltDeg + 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { RelaxScale     = h.RelaxScale     + 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { CenterNm       = h.CenterNm       + 1 }));
            Assert.True(FilterLibrary.DiffersFromBuiltinDefault(h with { BandwidthNm    = h.BandwidthNm    + 1 }));
        }

        [Fact]
        public void DiffersFromBuiltinDefault_Null_ReturnsFalse()
        {
            Assert.False(FilterLibrary.DiffersFromBuiltinDefault(null));
        }

        [Fact]
        public void DefaultLibrary_MatchesBuiltinDefaults()
        {
            FilterLibrary def = FilterLibrary.DefaultLibrary();
            Assert.Equal(FilterLibrary.BuiltinDefaults.Count, def.Filters.Count);
            for (int i = 0; i < def.Filters.Count; i++)
                Assert.Equal(FilterLibrary.BuiltinDefaults[i], def.Filters[i]);
        }
    }
}
