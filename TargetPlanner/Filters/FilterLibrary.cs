using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

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
        private readonly List<Filter> mFilters;

        /// <summary>Read-only view of the current library contents (in insertion order).</summary>
        public IReadOnlyList<Filter> Filters => mFilters;

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
                        return new FilterLibrary(filters);
                }
            }
            catch
            {
                // Fall through to defaults. JSON corruption / IO error / permission denied.
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
        /// First-launch in-code defaults: <c>H/O/S</c> at narrowband <c>(60°, 7d)</c>;
        /// <c>L/R/G/B</c> at broadband <c>(120°, 14d)</c>. Bandwidth values are typical
        /// for amateur kits; the user is expected to override via Edit Filters.
        /// </summary>
        public static FilterLibrary DefaultLibrary()
        {
            return new FilterLibrary(new[]
            {
                new Filter("H", 60.0,  7.0,  false, -15.0, 5.0, 0.0,   3.0),
                new Filter("O", 60.0,  7.0,  false, -15.0, 5.0, 0.0,   3.0),
                new Filter("S", 60.0,  7.0,  false, -15.0, 5.0, 0.0,   3.0),
                new Filter("L", 120.0, 14.0, false, -15.0, 5.0, 0.0, 300.0),
                new Filter("R", 120.0, 14.0, false, -15.0, 5.0, 0.0, 100.0),
                new Filter("G", 120.0, 14.0, false, -15.0, 5.0, 0.0, 100.0),
                new Filter("B", 120.0, 14.0, false, -15.0, 5.0, 0.0, 100.0),
            });
        }
    }
}
