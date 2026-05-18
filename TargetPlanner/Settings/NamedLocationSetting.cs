using System;
using TargetPlanner.State;
using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Settings
{
    // Serializable DTO for a named location. Deliberately flat and Newtonsoft-friendly so
    // Astronomy.Core.Locations.Location stays uncoupled from JSON (per CLAUDE.md: Core has no
    // Newtonsoft dependency). Latitude/Longitude are stored as positive magnitudes, hemisphere
    // carried by North/West bool flags -- matching the Core convention.
    public class NamedLocationSetting
    {
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool North { get; set; }
        public bool West { get; set; }

        // Per-site user planning preferences (target floor, minimum duration).
        // Serialised as a nested JSON object so the in-memory record shape and
        // the on-disk shape match. Null on a never-seen site triggers
        // PlanningPreferences.Default at load.
        public PlanningPreferencesDto Preferences { get; set; }

        // Observer ground elevation, meters above geoid. Default 0 keeps existing
        // settings.json files (which predate the field) deserializing without error.
        public double Elevation { get; set; }

        // Bortle dark-sky class for this site (1 = excellent dark, 9 = inner-city).
        // Default 0 is the C# default for missing JSON; the SettingsStore.MergeBuiltins
        // step auto-fills 5 (suburban) on any name-matched builtin whose persisted value
        // is still 0 (typical when upgrading from a settings.json predating the field).
        public int BortleClass { get; set; }

        // Atmospheric extinction coefficient k at 500 nm (mag/airmass), sea level.
        // Default 0.0 triggers the same MergeBuiltins auto-fill as BortleClass.
        public double ExtinctionK { get; set; }

        // Optional path to a NINA-format `.hrz` local-horizon polyline file for this site.
        // Null/empty -> the chart and scheduling fall back to the scalar TargetFloorDeg path
        // (ScalarHorizonProfile). Persisted here rather than on Location because Location
        // is Library-side and stays format-agnostic; HrzFileLoader (TP-side) reads the path
        // at site-pick time and builds a PolylineHorizonProfile that flows through
        // PlanningPolicy.LocalHorizon.
        public string LocalHorizonPath { get; set; }

        // UTC offset for this site, in hours. Null = not configured (older settings.json
        // files); load falls back to TimeZoneInfo.Local. Whole hours by current spinner
        // convention; double leaves room for fractional zones (Newfoundland UTC-3:30,
        // India UTC+5:30) if the spinner DecimalPlaces is ever bumped. The site-bound
        // simplification: a constant offset, no DST. The TP user picks "I'm at UTC-5",
        // not "I'm in Eastern Time" -- DST transitions don't auto-shift this value.
        public double? UtcOffsetHours { get; set; }

        public static NamedLocationSetting FromState(
            Location loc, PlanningPreferences preferences, string localHorizonPath)
        {
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (preferences == null) throw new ArgumentNullException(nameof(preferences));
            return new NamedLocationSetting
            {
                Name             = loc.Name,
                Latitude         = loc.Latitude,
                Longitude        = loc.Longitude,
                North            = loc.North,
                West             = loc.West,
                Preferences      = PlanningPreferencesDto.From(preferences),
                Elevation        = loc.Elevation,
                BortleClass      = loc.BortleClass,
                ExtinctionK      = loc.ExtinctionK,
                LocalHorizonPath = localHorizonPath,
                UtcOffsetHours   = loc.TimeZoneInfo?.BaseUtcOffset.TotalHours,
            };
        }

        public Location ToLocation()
        {
            return new Location(
                name:         Name,
                latitude:     Latitude, north: North,
                longitude:    Longitude, west:  West,
                timeZoneInfo: UtcOffsetHours.HasValue
                                  ? TimeZoneFromUtcOffsetHours(UtcOffsetHours.Value)
                                  : TimeZoneInfo.Local,
                elevation:    Elevation,
                bortleClass:  BortleClass <= 0 ? 5 : BortleClass,
                extinctionK:  ExtinctionK <= 0 ? 0.28 : ExtinctionK);
        }

        // Hoist the per-site PlanningPreferences out of the persisted shape into
        // the in-memory record. A null Preferences DTO (never-seen site)
        // resolves to PlanningPreferences.Default so the spinners boot with
        // sensible values rather than 0/0.
        public PlanningPreferences ToPreferences()
        {
            return Preferences?.ToRecord() ?? PlanningPreferences.Default;
        }

        // Build a no-DST custom TimeZoneInfo for the given UTC offset in hours.
        // Used by the NumericUpDown_TimeZone spinner handler and by ToLocation();
        // the resulting TZ has a constant BaseUtcOffset year-round so a saved
        // offset round-trips through Location.TimeZoneInfo.BaseUtcOffset.TotalHours
        // without DST shifting the persisted value across boots.
        internal static TimeZoneInfo TimeZoneFromUtcOffsetHours(double hours)
        {
            TimeSpan offset = TimeSpan.FromHours(hours);
            TimeSpan abs = offset.Duration();
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            string label = "UTC" + sign + abs.Hours.ToString("D2") + ":" + abs.Minutes.ToString("D2");
            return TimeZoneInfo.CreateCustomTimeZone(label, offset, label, label);
        }
    }

    // Nested DTO mirroring PlanningPreferences. Kept separate from the record
    // so Newtonsoft.Json can round-trip without needing a custom converter for
    // the TimeSpan field -- TotalMinutes is a plain double, much friendlier to
    // a human reading settings.json than the "00:04:00:00.0000000" TimeSpan
    // default format.
    public class PlanningPreferencesDto
    {
        public double TargetFloorDeg { get; set; }
        public double MinDurationMinutes { get; set; }

        public static PlanningPreferencesDto From(PlanningPreferences p) =>
            new PlanningPreferencesDto
            {
                TargetFloorDeg     = p.TargetFloorDeg,
                MinDurationMinutes = p.MinDuration.TotalMinutes,
            };

        public PlanningPreferences ToRecord() =>
            new PlanningPreferences(TargetFloorDeg, TimeSpan.FromMinutes(MinDurationMinutes));
    }
}
