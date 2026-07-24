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
        // Calibrated to a specific Astrodon Gen 2 E-Series LRGB + Astrodon 3nm Hα/OIII +
        // Chroma 3nm SII filter set (~$3K of premium glass, 2020-vintage). Center/bandwidth
        // per manufacturer datasheets; Chroma SII centered between the 671.6 / 673.1 doublet
        // lines (not on the 671.6 spectroscopic line).
        // ToleranceMag = the K-S Δmag moon gate's acceptance budget (Library calibration
        // 2026-07-24, cycle-median anchored): narrowband 1.0 (sky ×2.5 — line signal
        // doesn't scale with sky continuum), broadband 0.30 (sky ×1.32). Family-level is
        // enough: the per-filter distinctions the old Lorentzian table hand-encoded (OIII
        // stricter than Hα/SII because moonlight brightens the 500.7nm airglow) now emerge
        // from the physics — K-S scales extinction by center wavelength, so O's blue
        // center sees ~3× Hα's scatter for the same moon. Tune per-filter via Edit Filters.
        private static readonly Filter[] sBuiltinDefaults = new[]
        {
            //          name  tolMag  centerNm  bandwidthNm
            new Filter("H",   1.0,    656.3,      3.0),     // Astrodon 3nm Hα
            new Filter("O",   1.0,    500.7,      3.0),     // Astrodon 3nm [O III]
            new Filter("S",   1.0,    672.4,      3.0),     // Chroma 3nm SII (671.6/673.1 doublet)
            new Filter("L",   0.30,   550.0,    300.0),     // Astrodon E-Series Luminance
            new Filter("R",   0.30,   650.0,     60.0),     // Astrodon E-Series Red
            new Filter("G",   0.30,   525.0,     65.0),     // Astrodon E-Series Green
            new Filter("B",   0.30,   450.0,    100.0),     // Astrodon E-Series Blue
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
        public static FilterLibrary LoadOrDefault() => LoadOrDefault(DefaultPath);

        /// <summary>
        /// Loads from an arbitrary path (testing / migration). Same semantics as the
        /// parameterless overload: missing / malformed / unreadable -> defaults.
        /// </summary>
        public static FilterLibrary LoadOrDefault(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Filter[] filters = JsonConvert.DeserializeObject<Filter[]>(json);
                    if (filters != null && filters.Length > 0)
                        return new FilterLibrary(filters);
                }
            }
            catch (Exception ex)
            {
                // JSON corruption / IO error / permission denied. Fall through to defaults
                // silently from the user's perspective, but log to tp.log so the root cause
                // is recoverable.
                Log.Error("FilterLibrary.LoadOrDefault failed at '" + path + "'", ex);
            }
            return DefaultLibrary();
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
        /// First-launch in-code defaults: narrowband filters (<c>H/O/S</c>) at moon
        /// tolerance <c>1.0</c> mag; broadband filters (<c>L/R/G/B</c>) at <c>0.30</c>.
        /// See <see cref="sBuiltinDefaults"/> for the authoritative per-filter values.
        /// The user is expected to override via Edit Filters.
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
        /// its value fields (moon tolerance, center, bandwidth) differ from that baseline.
        /// User-created filters (no factory baseline) always return false.
        /// </summary>
        public static bool DiffersFromBuiltinDefault(Filter f)
        {
            if (f == null) return false;
            Filter b = FindBuiltinDefault(f.Name);
            if (b == null) return false;
            return f.ToleranceMag != b.ToleranceMag
                || f.CenterNm     != b.CenterNm
                || f.BandwidthNm  != b.BandwidthNm;
        }
    }
}
