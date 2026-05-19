using System.Collections.Generic;
using Astronomy.NINA.Persistence;

namespace TargetPlanner.Settings
{
    // Ship-time seed for settings.json's first creation. Single public surface:
    // BuildSeedSettings() returns a fresh AppSettings with all per-user defaults.
    // Used by SettingsStore.Load when settings.json doesn't exist (fresh install
    // or post-factory-reset) and by Pattern C field-fill on a loaded settings.json
    // missing a top-level field (additive-schema migration self-heal).
    //
    // No disk side here -- settings.json is the only on-disk file TP touches for
    // user state. Previously paired with a gitignored personal-defaults.json that
    // could override these values; the dual-file model was collapsed when its
    // sync semantics proved confusing (an edit to personal-defaults didn't
    // propagate to existing entries in settings.json).
    //
    // The author's personal site presets (Penns Park, Hillsborough, Cherry
    // Springs, Denver) are intentionally checked in here. The trade-off: this
    // file ships with the public binary; site coordinates and the target floor
    // values are public. Acceptable for a solo-consumer app today; if TP later
    // ships to other users, split the four into a gitignored partial class or
    // re-introduce a personal-overrides.json that AppSettings consumes.
    public static class PersonalDefaults
    {
        public static AppSettings BuildSeedSettings()
        {
            return new AppSettings
            {
                Version                  = AppSettings.CurrentVersion,
                LastSelectedLocationName = "Penns Park",
                NinaTargetsRoot          = @"E:\Photography\Astro Photography\Captures\Nina\Targets",
                NamedLocations           = new List<NamedSite>
                {
                    new NamedSite
                    {
                        Name        = "Penns Park",
                        Latitude    = 40.282835, North = true,
                        Longitude   = 74.997369, West  = true,
                        Elevation   = 80.67,
                        BortleClass = 5,
                        ExtinctionK = 0.28,
                        TimeZoneId  = "Eastern Standard Time",
                        Preferences = new PlanningPreferencesDto
                        {
                            TargetFloorDeg     = 45.0,
                            MinDurationMinutes = 240.0,
                        },
                    },
                    new NamedSite
                    {
                        Name        = "Hillsborough",
                        Latitude    = 40.459456, North = true,
                        Longitude   = 74.612921, West  = true,
                        Elevation   = 28.16,
                        BortleClass = 5,
                        ExtinctionK = 0.28,
                        TimeZoneId  = "Eastern Standard Time",
                        Preferences = new PlanningPreferencesDto
                        {
                            TargetFloorDeg     = 45.0,
                            MinDurationMinutes = 240.0,
                        },
                    },
                    new NamedSite
                    {
                        Name        = "Cherry Springs",
                        Latitude    = 41.66,    North = true,
                        Longitude   = 77.82,    West  = true,
                        Elevation   = 690.0,
                        BortleClass = 1,
                        ExtinctionK = 0.20,
                        TimeZoneId  = "Eastern Standard Time",
                        Preferences = new PlanningPreferencesDto
                        {
                            TargetFloorDeg     = 30.0,
                            MinDurationMinutes = 240.0,
                        },
                    },
                    new NamedSite
                    {
                        Name        = "Denver",
                        Latitude    = 39.740459, North = true,
                        Longitude   = 105.025215, West = true,
                        Elevation   = 1609,
                        BortleClass = 9,
                        ExtinctionK = 0.55,
                        TimeZoneId  = "Mountain Standard Time",
                        Preferences = new PlanningPreferencesDto
                        {
                            TargetFloorDeg     = 60.0,
                            MinDurationMinutes = 240.0,
                        },
                    },
                },
            };
        }
    }
}
