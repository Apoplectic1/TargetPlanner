using System;
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
        public double Horizon { get; set; }
        public double DurationMinutes { get; set; }

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

        public static NamedLocationSetting FromLocation(Location loc)
        {
            return new NamedLocationSetting
            {
                Name = loc.Name,
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                North = loc.North,
                West = loc.West,
#pragma warning disable CS0618 // Transitional persistence: NamedLocationSetting serializes Location.Horizon/.Duration scalars until PlanningPolicy owns per-site persistence.
                Horizon = loc.Horizon,
                DurationMinutes = loc.Duration.TotalMinutes,
#pragma warning restore CS0618
                Elevation = loc.Elevation,
                BortleClass = loc.BortleClass,
                ExtinctionK = loc.ExtinctionK,
            };
        }

        public Location ToLocation()
        {
            return new Location(
                name:         Name,
                latitude:     Latitude, north: North,
                longitude:    Longitude, west:  West,
                horizon:      Horizon,
                duration:     TimeSpan.FromMinutes(DurationMinutes),
                dateTime:     DateTime.Now,
                timeZoneInfo: TimeZoneInfo.Local,
                elevation:    Elevation,
                bortleClass:  BortleClass <= 0 ? 5 : BortleClass,
                extinctionK:  ExtinctionK <= 0 ? 0.28 : ExtinctionK);
        }
    }
}
