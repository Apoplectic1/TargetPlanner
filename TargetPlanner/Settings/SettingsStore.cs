using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
                            AppendMissingBuiltins(settings.NamedLocations);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                // Corrupt file, permission denied, disk error, malformed JSON -- fall back to
                // defaults silently from the user's perspective, but leave a diagnostic trail
                // so "why did my saved locations disappear?" is traceable.
                System.Diagnostics.Debug.WriteLine(
                    $"SettingsStore.Load failed at '{FilePath}': {ex.GetType().Name}: {ex.Message}");
            }

            return new AppSettings
            {
                Version = 1,
                NamedLocations = BuildDefaultNamedLocations(),
                LastSelectedLocationName = "Penns Park",
            };
        }

        // Append any built-in default named-locations that aren't already in the user's saved
        // list. Idempotent: matched by Name (case-insensitive). Lets us add new defaults
        // (e.g., "Hillsborough") in a release without forcing existing users to delete
        // settings.json. User-edited Lat/Lon/etc. for an existing entry are preserved
        // (matching by Name skips the re-add).
        private static void AppendMissingBuiltins(List<NamedLocationSetting> existing)
        {
            foreach (NamedLocationSetting builtin in BuildDefaultNamedLocations())
            {
                bool present = existing.Exists(e =>
                    string.Equals(e.Name, builtin.Name, StringComparison.OrdinalIgnoreCase));
                if (!present) existing.Add(builtin);
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
                // but log so the root cause is recoverable.
                System.Diagnostics.Debug.WriteLine(
                    $"SettingsStore.Save failed at '{FilePath}': {ex.GetType().Name}: {ex.Message}");
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
                },
            };
        }
    }
}
