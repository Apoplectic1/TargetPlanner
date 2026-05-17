using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TargetPlanner.Support;

namespace TargetPlanner.Settings
{
    // Layer between hardcoded ship-safe constants and the user's runtime SettingsStore. Read
    // once at static-ctor time from %LocalAppData%\TargetPlanner\personal-defaults.json --
    // a per-developer file that never enters the repo. The same compiled binary works for
    // everyone: if the file exists, the dev's preferred location / NINA targets root /
    // built-in named locations override the public placeholders below; if it's absent (the
    // public-binary case), the placeholders take over.
    //
    // Do NOT add author-specific values here. Public-safe placeholders only -- the design
    // doc in docs/design/personal-defaults-and-settings.md lays out the resolution model.
    public static class PersonalDefaults
    {
        public static string LocationName    { get; private set; } = "Custom";
        public static double Latitude        { get; private set; } = 40.0;
        public static double Longitude       { get; private set; } = 75.0;   // West-positive
        public static double Elevation       { get; private set; } = 0.0;
        public static int    BortleClass     { get; private set; } = 5;
        public static double ExtinctionK     { get; private set; } = 0.28;
        public static double Horizon         { get; private set; } = 30.0;
        public static double DurationMinutes { get; private set; } = 240.0;
        public static string NinaTargetsRoot { get; private set; } =
            @"C:\Users\Public\Documents\NINA\Targets";

        // Per-developer named-location preset list (e.g. their imaging sites). Empty by
        // default; SettingsStore.BuildDefaultNamedLocations seeds from this when present
        // and falls back to a single Location.Default entry when empty so a fresh public
        // install still has at least one (neutral) location in the dropdown.
        public static IReadOnlyList<NamedLocationSetting> NamedLocations { get; private set; } =
            Array.Empty<NamedLocationSetting>();

        // Pre-seeded checklist for the right-click-title-bar user-observation
        // dialog (Forms/UserObservationDialog). The seed list captures recurring
        // observation patterns we've hit during debugging; edit via
        // personal-defaults.json to add project-specific items. Items appear in
        // the same order in the dialog's CheckedListBox.
        public static IReadOnlyList<string> UserObservationChecklist { get; private set; } =
            new[]
            {
                "All okay (checkpoint)",
                "Moon missing from Day chart",
                "Moon missing from Sky chart",
                "Targets disappear when they shouldn't",
                "Chart shows wrong/stale state",
                "Chart blank when it shouldn't be",
                "Wrong axis bounds (labels missing, gradient cut off)",
                "Spinner or button doesn't respond",
                "HD overlay stuck/broken",
                "Form labels disagree with chart",
                "Performance issue (slow scrub/render)",
                "Other (see notes)",
            };

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TargetPlanner", "personal-defaults.json");

        static PersonalDefaults() { TryLoad(); }

        private static void TryLoad()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string json = File.ReadAllText(FilePath);
                Dto d = JsonConvert.DeserializeObject<Dto>(json);
                if (d == null) return;

                if (!string.IsNullOrWhiteSpace(d.LocationName))    LocationName    = d.LocationName;
                if (d.Latitude.HasValue)                           Latitude        = d.Latitude.Value;
                if (d.Longitude.HasValue)                          Longitude       = d.Longitude.Value;
                if (d.Elevation.HasValue)                          Elevation       = d.Elevation.Value;
                if (d.BortleClass.HasValue && d.BortleClass.Value > 0)
                                                                   BortleClass     = d.BortleClass.Value;
                if (d.ExtinctionK.HasValue && d.ExtinctionK.Value > 0)
                                                                   ExtinctionK     = d.ExtinctionK.Value;
                if (d.Horizon.HasValue)                            Horizon         = d.Horizon.Value;
                if (d.DurationMinutes.HasValue)                    DurationMinutes = d.DurationMinutes.Value;
                if (!string.IsNullOrWhiteSpace(d.NinaTargetsRoot)) NinaTargetsRoot = d.NinaTargetsRoot;
                if (d.NamedLocations != null && d.NamedLocations.Count > 0)
                    NamedLocations = d.NamedLocations.AsReadOnly();
                if (d.UserObservationChecklist != null && d.UserObservationChecklist.Count > 0)
                    UserObservationChecklist = d.UserObservationChecklist.AsReadOnly();
            }
            catch (Exception ex)
            {
                // Malformed file, permission denied, disk error -- silently fall through to
                // public-safe defaults so the app still launches. Leave a diagnostic trail
                // in tp.log so "why didn't my personal defaults apply?" is recoverable.
                Log.Error("PersonalDefaults.TryLoad failed at '" + FilePath + "'", ex);
            }
        }

        // Newtonsoft DTO. Nullable scalars distinguish "field omitted" from "field set to 0",
        // so a partial JSON file overrides exactly the fields it specifies and inherits the
        // public-safe values for the rest. NamedLocations reuses the existing
        // NamedLocationSetting shape so the DTO matches settings.json's structure 1:1.
        private class Dto
        {
            public string LocationName    { get; set; }
            public double? Latitude       { get; set; }
            public double? Longitude      { get; set; }
            public double? Elevation      { get; set; }
            public int?    BortleClass    { get; set; }
            public double? ExtinctionK    { get; set; }
            public double? Horizon        { get; set; }
            public double? DurationMinutes{ get; set; }
            public string NinaTargetsRoot { get; set; }
            public List<NamedLocationSetting> NamedLocations { get; set; }
            public List<string> UserObservationChecklist { get; set; }
        }
    }
}
