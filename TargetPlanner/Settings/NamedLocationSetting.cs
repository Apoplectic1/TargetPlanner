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

        public static NamedLocationSetting FromLocation(Location loc)
        {
            return new NamedLocationSetting
            {
                Name = loc.Name,
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                North = loc.North,
                West = loc.West,
                Horizon = loc.Horizon,
                DurationMinutes = loc.Duration.TotalMinutes,
            };
        }

        public Location ToLocation()
        {
            return new Location
            {
                Name = Name,
                Latitude = Latitude,
                Longitude = Longitude,
                North = North,
                West = West,
                Horizon = Horizon,
                Duration = TimeSpan.FromMinutes(DurationMinutes),
            };
        }
    }
}
