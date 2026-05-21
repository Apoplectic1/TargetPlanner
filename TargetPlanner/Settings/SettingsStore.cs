using System;
using System.Collections.Generic;
using System.IO;
using Astronomy.NINA.Persistence;
using Newtonsoft.Json;
using TargetPlanner.Support;

namespace TargetPlanner.Settings
{
    // File-backed per-user settings store. Path: %AppData%\TargetPlanner\settings.json.
    // Single source of truth for user state between sessions.
    //
    // Two paths through Load:
    //   1. settings.json missing -> seed from PersonalDefaults.BuildSeedSettings(),
    //      save, return. First-run / post-factory-reset.
    //   2. settings.json present -> deserialise, apply Pattern C fill on any
    //      null/empty top-level fields, strip the "Custom" sentinel from
    //      NamedLocations if a stale entry crept in.
    //
    // Save is best-effort -- a write failure is logged but the in-memory state
    // is unchanged so the next save attempt picks up the same data.
    public static class SettingsStore
    {
        public static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TargetPlanner");

        public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

        public static AppSettings Load()
        {
            // No file -> first-run path: build from seed, save, return.
            if (!File.Exists(FilePath))
            {
                AppSettings seed = PersonalDefaults.BuildSeedSettings();
                Save(seed);
                return seed;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                AppSettings settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings != null && settings.Version == AppSettings.CurrentVersion)
                {
                    // Pattern C: fill absent top-level fields from the seed. A field
                    // missing from JSON (because the schema didn't have it yet when the
                    // file was last written) is null/empty in the deserialised instance
                    // -- we replace those with the seed value. Present fields, including
                    // those the user has set to empty intentionally via Defaults > Edit,
                    // are preserved.
                    AppSettings seed = PersonalDefaults.BuildSeedSettings();
                    if (string.IsNullOrEmpty(settings.NinaTargetsRoot))
                        settings.NinaTargetsRoot = seed.NinaTargetsRoot;
                    if (string.IsNullOrEmpty(settings.ImageLibraryRoot))
                        settings.ImageLibraryRoot = seed.ImageLibraryRoot;
                    if (settings.NamedLocations == null || settings.NamedLocations.Count == 0)
                        settings.NamedLocations = seed.NamedLocations;
                    else
                    {
                        // "Custom" is a reserved sentinel in ComboBox_Location for the
                        // free-edit mode; saved sites named "Custom" are historical
                        // artifacts (from older Location.Default-fallback code paths) and
                        // would render as a duplicate dropdown entry alongside the
                        // sentinel. Strip on load so the runtime list contains only real
                        // sites.
                        settings.NamedLocations.RemoveAll(s =>
                            string.Equals(s.Name, "Custom", StringComparison.OrdinalIgnoreCase));
                    }
                    return settings;
                }

                // Version mismatch (or null after deserialise): treat as corrupt /
                // incompatible; fall through to seed.
                if (settings != null)
                {
                    Log.Error(
                        "SettingsStore.Load: version mismatch at '" + FilePath +
                        "' (file=" + settings.Version + ", expected=" +
                        AppSettings.CurrentVersion + "); resetting to defaults");
                }
            }
            catch (Exception ex)
            {
                // Corrupt JSON, permission denied, disk error -- log + fall through to
                // seed so the app still launches.
                Log.Error("SettingsStore.Load failed at '" + FilePath + "'", ex);
            }

            AppSettings fallback = PersonalDefaults.BuildSeedSettings();
            Save(fallback);
            return fallback;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                // Disk full, permission denied, antivirus lock -- silent failure for the user,
                // but log to tp.log so the root cause is recoverable.
                Log.Error("SettingsStore.Save failed at '" + FilePath + "'", ex);
            }
        }
    }
}
