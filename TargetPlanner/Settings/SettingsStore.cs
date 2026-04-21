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
            };
        }
    }
}
