using System.Collections.Generic;
using Astronomy.NINA.Persistence;

namespace TargetPlanner.Settings
{
    // JSON-persisted per-user settings. Loaded on startup and saved on change /
    // form close. settings.json is the single source of truth for everything TP
    // remembers between sessions: site list, last-active site, NINA targets root.
    //
    // Schema migration policy: when a new field is added, existing settings.json
    // files load with the field at its C# default (null/empty/0). SettingsStore
    // applies "Pattern C" fill -- on load, any top-level field that's null/empty
    // is replaced by the value from PersonalDefaults.BuildSeedSettings(). User-
    // set non-null/non-empty values are preserved.
    public class AppSettings
    {
        // Schema version of settings.json. Bumped when an incompatible field
        // change ships (rename, semantic shift, removal). Additive changes that
        // load cleanly with default-valued missing fields don't need a bump.
        // SettingsStore.Load compares this against the persisted value and
        // resets to defaults when they disagree (see "version mismatch" path).
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public List<NamedSite> NamedLocations { get; set; } = new List<NamedSite>();
        public string LastSelectedLocationName { get; set; }

        // Filesystem root the Browse Target List dialog scans for NINA target
        // JSONs. Null on a stale settings.json predating this field; Pattern C
        // fills from PersonalDefaults on load.
        public string NinaTargetsRoot { get; set; }
    }
}
