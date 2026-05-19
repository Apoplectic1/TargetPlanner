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

        // CheckBox_WallClock state -- "Use Site Wall Clock". True means the
        // selected location's TimeZoneInfo drives picker / chart-axis / label
        // display; false falls back to the machine's local zone (TimeZoneInfo.Local).
        // Defaults to true so a fresh install matches the post-2026-05-18 chart-
        // pipeline-on-Location-zone shipping behavior. Additive field: a missing
        // value in settings.json deserialises as default(bool) = false, which would
        // INVERT the intended default -- callers therefore initialise via the
        // CheckBox's Designer-set Checked state at boot rather than trusting the
        // deserialised value when the key was absent (see MainForm boot path).
        public bool UseSiteWallClock { get; set; } = true;
    }
}
