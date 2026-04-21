using System.Collections.Generic;

namespace TargetPlanner.Settings
{
    // JSON-persisted per-user settings, loaded on startup and saved on change / form close.
    // New fields are additive: old files missing new keys will load with defaults; files with
    // unknown keys will round-trip lossily (Newtonsoft ignores unknown properties by default).
    public class AppSettings
    {
        public int Version { get; set; } = 1;
        public List<NamedLocationSetting> NamedLocations { get; set; } = new List<NamedLocationSetting>();
        public string LastSelectedLocationName { get; set; }
    }
}
