using System;
using System.Collections.Generic;
using System.IO;
using Astronomy.NINA.Persistence;
using Newtonsoft.Json;
using TargetPlanner.State;
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
                        // Schema-version gate. Today only Version == CurrentVersion is
                        // accepted; future versions add a switch on settings.Version with
                        // per-version migration transforms and fall through to here.
                        // Any mismatch logs and falls through to BuiltDefaults so the user
                        // gets a working app rather than a crash on an incompatible file.
                        if (settings.Version != AppSettings.CurrentVersion)
                        {
                            Log.Error(
                                "SettingsStore.Load: version mismatch at '" + FilePath +
                                "' (file=" + settings.Version + ", expected=" +
                                AppSettings.CurrentVersion + "); resetting to defaults");
                        }
                        else
                        {
                            if (settings.NamedLocations == null || settings.NamedLocations.Count == 0)
                                settings.NamedLocations = BuildDefaultNamedLocations();
                            else
                                MergeBuiltins(settings.NamedLocations);
                            return settings;
                        }
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
                Version = AppSettings.CurrentVersion,
                NamedLocations = BuildDefaultNamedLocations(),
                LastSelectedLocationName = PersonalDefaults.LocationName,
            };
        }

        // Merge built-in default named-locations into the user's saved list. Idempotent;
        // matched by Name (case-insensitive). Four responsibilities:
        //
        // 1. Append: a built-in not present in the existing list is appended so adding a
        //    new preset in a release (or in the developer's personal-defaults.json)
        //    doesn't require deleting settings.json.
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
        //
        // 4. Auto-fill Preferences: forward-flow seed of the per-site planning prefs
        //    DTO. A null Preferences on an existing entry adopts the builtin's
        //    Preferences (which itself defaults to PlanningPreferences.Default when the
        //    builtin doesn't carry one). User-set non-null Preferences are preserved.
        //    Not a migration path -- per the no-backwards-compat rule, this is forward
        //    extension of new presets into a settings.json that may pre-date the field.
        //
        // NOTE: TimeZoneId is deliberately NOT auto-filled here. The named-TZ refactor
        // (2026-05-19) made TZ a user-managed per-site choice; the user re-picks via
        // ComboBox_TimeZone once per existing site. PersonalDefaults seeds the canonical
        // TZ on PP / Hillsborough for fresh installs, but a stale settings.json's null
        // TimeZoneId resolves to TimeZoneInfo.Local until the user picks deliberately.
        private static void MergeBuiltins(List<NamedSite> existing)
        {
            foreach (NamedSite builtin in BuildDefaultNamedLocations())
            {
                NamedSite match = existing.Find(e =>
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
                    if (match.Preferences == null && builtin.Preferences != null)
                        match.Preferences = builtin.Preferences;
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

        // Seeds the named-locations list a fresh install (or a settings.json reset) starts
        // with. PersonalDefaults.NamedLocations -- loaded from the developer's gitignored
        // %LocalAppData%\TargetPlanner\personal-defaults.json -- supplies the presets when
        // present; otherwise we fall back to a single neutral Location.Default entry so the
        // public-binary case still has something selectable in the location dropdown.
        private static List<NamedSite> BuildDefaultNamedLocations()
        {
            var list = new List<NamedSite>();
            foreach (NamedSite preset in PersonalDefaults.NamedLocations)
                list.Add(Clone(preset));
            if (list.Count == 0)
                list.Add(NamedSite.FromLocation(
                    Location.Default, PlanningPreferences.Default.ToDto(), localHorizonPath: null));
            return list;
        }

        // PersonalDefaults exposes its presets as IReadOnlyList<NamedSite>; we copy each
        // entry so the settings.json round-trip can mutate freely without bleeding back
        // into the static personal-defaults snapshot.
        private static NamedSite Clone(NamedSite src)
        {
            return new NamedSite
            {
                Name             = src.Name,
                Latitude         = src.Latitude,
                Longitude        = src.Longitude,
                North            = src.North,
                West             = src.West,
                Preferences      = src.Preferences == null
                    ? null
                    : new PlanningPreferencesDto
                    {
                        TargetFloorDeg     = src.Preferences.TargetFloorDeg,
                        MinDurationMinutes = src.Preferences.MinDurationMinutes,
                    },
                Elevation        = src.Elevation,
                BortleClass      = src.BortleClass,
                ExtinctionK      = src.ExtinctionK,
                LocalHorizonPath = src.LocalHorizonPath,
                TimeZoneId       = src.TimeZoneId,
            };
        }
    }
}
