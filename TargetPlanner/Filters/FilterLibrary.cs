using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TargetPlanner.Support;

namespace TargetPlanner.Filters
{
    /// <summary>
    /// Persisted collection of named photographic filters. Loaded from
    /// <see cref="DefaultPath"/> on demand; ships with sensible defaults when no library
    /// file exists yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutable: Edit Filters dialog modifies the in-memory list directly via
    /// <see cref="Add"/> / <see cref="RemoveAt"/> / <see cref="Replace"/> /
    /// <see cref="ReplaceAll"/>, then calls <see cref="Save"/> to persist.
    /// </para>
    /// <para>
    /// Persistence format is the JSON-serialized array of <see cref="Filter"/> objects;
    /// Newtonsoft.Json's constructor mapping handles deserialization without explicit
    /// schema metadata.
    /// </para>
    /// </remarks>
    public sealed class FilterLibrary
    {
        // Factory built-in defaults. Filter is immutable so the array is safe to share;
        // FilterLibrary's ctor takes a snapshot via .ToList() so library mutations never
        // touch this array.
        private static readonly Filter[] sBuiltinDefaults = new[]
        {
            //          name  sep    width  relax  rMin   rMax  rScl  centerNm  bandwidthNm
            new Filter("H",   60.0,  7.0,   false, -15.0, 5.0,  0.0,  656.3,    3.0),    // Hα line
            new Filter("O",   60.0,  7.0,   false, -15.0, 5.0,  0.0,  500.7,    3.0),    // [O III] line
            new Filter("S",   60.0,  7.0,   false, -15.0, 5.0,  0.0,  671.6,    3.0),    // [S II] line
            new Filter("L",   120.0, 14.0,  false, -15.0, 5.0,  0.0,  540.0,  300.0),    // luminance mid
            new Filter("R",   120.0, 14.0,  false, -15.0, 5.0,  0.0,  650.0,  100.0),
            new Filter("G",   120.0, 14.0,  false, -15.0, 5.0,  0.0,  550.0,  100.0),
            new Filter("B",   120.0, 14.0,  false, -15.0, 5.0,  0.0,  445.0,  100.0),
        };

        private readonly List<Filter> mFilters;

        /// <summary>Read-only view of the current library contents (in insertion order).</summary>
        public IReadOnlyList<Filter> Filters => mFilters;

        /// <summary>
        /// The shipped factory defaults. Used by the Filters menu's "*" modified-indicator
        /// (see <see cref="DiffersFromBuiltinDefault"/>) and by the EditFiltersForm Defaults
        /// per-row button to restore a row to its factory values.
        /// </summary>
        public static IReadOnlyList<Filter> BuiltinDefaults => sBuiltinDefaults;

        /// <summary>Constructs a library from an enumerable of filters. <see langword="null"/> is treated as empty.</summary>
        public FilterLibrary(IEnumerable<Filter> filters)
        {
            mFilters = filters?.ToList() ?? new List<Filter>();
        }

        /// <summary>Returns the filter with the matching <paramref name="name"/>, or <see langword="null"/>.</summary>
        public Filter Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Filter f in mFilters)
            {
                if (f.Name == name) return f;
            }
            return null;
        }

        /// <summary>Append a new filter at the end of the library.</summary>
        public void Add(Filter f) => mFilters.Add(f);

        /// <summary>Remove the filter at <paramref name="index"/>.</summary>
        public void RemoveAt(int index) => mFilters.RemoveAt(index);

        /// <summary>Replace the filter at <paramref name="index"/> with <paramref name="f"/>.</summary>
        public void Replace(int index, Filter f) => mFilters[index] = f;

        /// <summary>Replace the entire list (Edit Filters dialog uses this on Save).</summary>
        public void ReplaceAll(IEnumerable<Filter> filters)
        {
            mFilters.Clear();
            if (filters != null) mFilters.AddRange(filters);
        }

        /// <summary>
        /// Loads from <see cref="DefaultPath"/>. If the file is missing, malformed, or
        /// unreadable, returns the in-code defaults (<see cref="DefaultLibrary"/>).
        /// Errors are silenced -- a corrupted user file should not block app launch.
        /// </summary>
        public static FilterLibrary LoadOrDefault()
        {
            try
            {
                if (File.Exists(DefaultPath))
                {
                    string json = File.ReadAllText(DefaultPath);
                    Filter[] filters = JsonConvert.DeserializeObject<Filter[]>(json);
                    if (filters != null && filters.Length > 0)
                        return new FilterLibrary(MigrateLegacyFields(filters));
                }
            }
            catch (Exception ex)
            {
                // JSON corruption / IO error / permission denied. Fall through to defaults
                // silently from the user's perspective, but log to tp.log so the root cause
                // is recoverable.
                Log.Error("FilterLibrary.LoadOrDefault failed at '" + DefaultPath + "'", ex);
            }
            return DefaultLibrary();
        }

        // Mirrors SettingsStore.MergeBuiltins's auto-fill pattern. Filters loaded from
        // older filters.json files predating CenterNm deserialize with CenterNm = 0.0
        // (the C# default for missing JSON fields). Walk the deserialized array and for
        // each filter whose Name matches a builtin AND whose CenterNm is 0, fill in the
        // builtin's CenterNm. Negative wavelengths are unphysical and 0 is the
        // unmistakable "field was missing" tell, so the heuristic is safe -- a user
        // can't legitimately set CenterNm = 0. User-renamed builtins or user-created
        // filters land at 0 and the user can fix via Edit Filters.
        private static Filter[] MigrateLegacyFields(Filter[] filters)
        {
            Filter[] result = new Filter[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                Filter f = filters[i];
                if (f.CenterNm == 0.0)
                {
                    Filter b = FindBuiltinDefault(f.Name);
                    if (b != null) f = f with { CenterNm = b.CenterNm };
                }
                result[i] = f;
            }
            return result;
        }

        /// <summary>Save the library to <see cref="DefaultPath"/>, creating directories as needed.</summary>
        public void Save() => Save(DefaultPath);

        /// <summary>Save the library to an arbitrary path (testing / migration).</summary>
        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonConvert.SerializeObject(mFilters, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Default persistence path: <c>%APPDATA%\TargetPlanner\filters.json</c>.
        /// </summary>
        public static string DefaultPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "TargetPlanner", "filters.json");
            }
        }

        /// <summary>
        /// First-launch in-code defaults: <c>H/O/S</c> at narrowband <c>(60°, 7d)</c>;
        /// <c>L/R/G/B</c> at broadband <c>(120°, 14d)</c>. Bandwidth values are typical
        /// for amateur kits; the user is expected to override via Edit Filters.
        /// </summary>
        public static FilterLibrary DefaultLibrary() => new FilterLibrary(sBuiltinDefaults);

        /// <summary>
        /// Returns the built-in factory default with a matching <paramref name="name"/>
        /// (case-insensitive), or <see langword="null"/> when the name has no factory
        /// baseline (i.e., user-created filter).
        /// </summary>
        public static Filter FindBuiltinDefault(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Filter f in sBuiltinDefaults)
            {
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        /// <summary>
        /// True iff <paramref name="f"/> has a built-in factory default by name AND any of
        /// its value fields (separation, width, relaxation params, bandwidth) differ from
        /// that baseline. User-created filters (no factory baseline) always return false.
        /// </summary>
        public static bool DiffersFromBuiltinDefault(Filter f)
        {
            if (f == null) return false;
            Filter b = FindBuiltinDefault(f.Name);
            if (b == null) return false;
            return f.SeparationDeg  != b.SeparationDeg
                || f.WidthDays      != b.WidthDays
                || f.RelaxEnabled   != b.RelaxEnabled
                || f.RelaxMinAltDeg != b.RelaxMinAltDeg
                || f.RelaxMaxAltDeg != b.RelaxMaxAltDeg
                || f.RelaxScale     != b.RelaxScale
                || f.CenterNm       != b.CenterNm
                || f.BandwidthNm    != b.BandwidthNm;
        }
    }
}
