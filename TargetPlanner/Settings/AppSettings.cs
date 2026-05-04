using System.Collections.Generic;

namespace TargetPlanner.Settings
{
    // JSON-persisted per-user settings, loaded on startup and saved on change / form close.
    // New fields are additive: old files missing new keys will load with defaults; files with
    // unknown keys will round-trip lossily (Newtonsoft ignores unknown properties by default).
    public class AppSettings
    {
        // Schema version of settings.json. Bumped when an incompatible field
        // change ships (rename, semantic shift, removal). Additive changes that
        // load cleanly with default-valued missing fields don't need a bump.
        // SettingsStore.Load compares this against the persisted value and
        // resets to defaults when they disagree (see "version mismatch" path).
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public List<NamedLocationSetting> NamedLocations { get; set; } = new List<NamedLocationSetting>();
        public string LastSelectedLocationName { get; set; }
    }
}
