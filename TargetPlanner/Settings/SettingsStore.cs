using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TargetPlanner.Support;
using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Settings
{
    // File-backed per-user settings store. Path: %AppData%\TargetPlanner\settings.json.
    // Load/Save are best-effort -- a missing, empty, or corrupt file falls back to built-in
    // defaults rather than crashing the app.
    public static class SettingsStore
    {
        public static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TargetPlanner");

        public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    AppSettings settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        if (settings.NamedLocations == null || settings.NamedLocations.Count == 0)
                            settings.NamedLocations = BuildDefaultNamedLocations();
                        else
                            MergeBuiltins(settings.NamedLocations);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                // Corrupt file, permission denied, disk error, malformed JSON -- fall back to
                // defaults silently from the user's perspective, but leave a diagnostic trail
                // in tp.log so "why did my saved locations disappear?" is traceable.
                Log.Error("SettingsStore.Load failed at '" + FilePath + "'", ex);
            }

            return new AppSettings
            {
                Version = 1,
                NamedLocations = BuildDefaultNamedLocations(),
                LastSelectedLocationName = "Penns Park",
            };
        }

        // Merge built-in default named-locations into the user's saved list. Idempotent;
        // matched by Name (case-insensitive). Three responsibilities:
        //
        // 1. Append: a built-in not present in the existing list is appended so adding a
        //    new preset in a release ("Hillsborough") doesn't require deleting settings.json.
        //
        // 2. Auto-fill Elevation: when a built-in name MATCHES an existing entry whose
        //    Elevation is 0 (the back-compat default for settings written before the field
        //    existed), copy the built-in's Elevation onto the existing entry. User-set
        //    non-zero elevations are preserved (the merge only fills the zero case).
        //
        // 3. Auto-fill BortleClass + ExtinctionK on the same zero-detection rule. K-S
        //    sky-brightness needs both fields; users upgrading from older settings.json
        //    files would otherwise see Bortle = 0 / k = 0 (physically nonsensical) on
        //    every name-matched builtin. User-set non-zero values are preserved.
        private static void MergeBuiltins(List<NamedLocationSetting> existing)
        {
            foreach (NamedLocationSetting builtin in BuildDefaultNamedLocations())
            {
                NamedLocationSetting match = existing.Find(e =>
                    string.Equals(e.Name, builtin.Name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    existing.Add(builtin);
                }
                else
                {
                    if (match.Elevation == 0.0 && builtin.Elevation != 0.0)
                        match.Elevation = builtin.Elevation;
                    if (match.BortleClass == 0 && builtin.BortleClass != 0)
                        match.BortleClass = builtin.BortleClass;
                    if (match.ExtinctionK == 0.0 && builtin.ExtinctionK != 0.0)
                        match.ExtinctionK = builtin.ExtinctionK;
                }
            }
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

        private static List<NamedLocationSetting> BuildDefaultNamedLocations()
        {
            return new List<NamedLocationSetting>
            {
                NamedLocationSetting.FromLocation(Location.Default),
                new NamedLocationSetting
                {
                    Name = "Hillsborough",
                    Latitude = 40.459456,
                    North = true,
                    Longitude = 74.612921,
                    West = true,
                    Horizon = 30.0,
                    DurationMinutes = 240.0,
                    Elevation = 28.16,
                    BortleClass = 5,
                    ExtinctionK = 0.28,
                },
            };
        }
    }
}
