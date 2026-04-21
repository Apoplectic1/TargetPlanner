using System;

namespace Astronomy.Core.Locations
{
    // Immutable observer location. Every property is read-only; mutations produce a new
    // instance via With(...). Construction takes a full parameter set so every caller is
    // explicit about what it means; use Location.Default for the Penns Park defaults.
    //
    // Hemisphere convention: Latitude and Longitude are stored as non-negative magnitudes,
    // with direction carried by the North / West bool flags. A negative magnitude passed to
    // the constructor is normalized (flipped to positive) and the corresponding hemisphere
    // flag is inverted, so `new Location(..., latitude: -40, north: true, ...)` produces
    // { Latitude: 40, North: false } -- the sign takes precedence over the flag.
    //
    // D/M/S derivations (LatDegrees/LatMinutes/LatSeconds and the Longitude equivalents) are
    // computed on read instead of stored as fields. No possibility of the derived values
    // falling out of sync with the decimal value.
    public sealed class Location
    {
        public string        Name         { get; }
        public double        Latitude     { get; }
        public bool          North        { get; }
        public double        Longitude    { get; }
        public bool          West         { get; }
        public double        Horizon      { get; }
        public TimeSpan      Duration     { get; }
        public DateTime      DateTime     { get; }
        public TimeZoneInfo  TimeZoneInfo { get; }

        // Sexagesimal latitude components derived from the stored magnitude. LatDegrees is the
        // whole-degrees digit of the DMS breakdown (always non-negative), NOT a synonym for
        // decimal Latitude. Hemisphere lives in the North flag, not in the sign of LatDegrees.
        public double LatDegrees => Math.Truncate(Latitude);
        public double LatMinutes => Math.Floor(60.0 * (Latitude - LatDegrees));
        public double LatSeconds => 3600.0 * (Latitude - LatDegrees - LatMinutes / 60.0);

        // Sexagesimal longitude components. LonDegrees is non-negative; direction lives in the
        // West flag.
        public double LonDegrees => Math.Truncate(Longitude);
        public double LonMinutes => Math.Floor(60.0 * (Longitude - LonDegrees));
        public double LonSeconds => 3600.0 * (Longitude - LonDegrees - LonMinutes / 60.0);

        public double MinutesAboveHorizon => Duration.TotalMinutes;

        public Location(
            string name,
            double latitude, bool north,
            double longitude, bool west,
            double horizon,
            TimeSpan duration,
            DateTime dateTime,
            TimeZoneInfo timeZoneInfo)
        {
            // Sign normalization: negative magnitude flips the hemisphere flag so the stored
            // state is always (non-negative magnitude, explicit hemisphere).
            if (latitude < 0) { latitude = -latitude; north = !north; }
            if (longitude < 0) { longitude = -longitude; west = !west; }

            Name         = name ?? "Custom";
            Latitude     = latitude;
            North        = north;
            Longitude    = longitude;
            West         = west;
            Horizon      = horizon;
            Duration     = duration;
            DateTime     = dateTime;
            TimeZoneInfo = timeZoneInfo ?? TimeZoneInfo.Local;
        }

        // Named-argument builder. Callers pass only the fields they want to change:
        //     mLocation = mLocation.With(horizon: 35.0);
        //     mLocation = mLocation.With(latitude: 40.3, north: true);
        public Location With(
            string name = null,
            double? latitude = null, bool? north = null,
            double? longitude = null, bool? west = null,
            double? horizon = null,
            TimeSpan? duration = null,
            DateTime? dateTime = null,
            TimeZoneInfo timeZoneInfo = null)
            => new Location(
                name         ?? this.Name,
                latitude     ?? this.Latitude,
                north        ?? this.North,
                longitude    ?? this.Longitude,
                west         ?? this.West,
                horizon      ?? this.Horizon,
                duration     ?? this.Duration,
                dateTime     ?? this.DateTime,
                timeZoneInfo ?? this.TimeZoneInfo);

        // Penns Park defaults, freshly instantiated on each access (DateTime = now).
        public static Location Default => new Location(
            name:         "Penns Park",
            latitude:     40.282835, north: true,
            longitude:    74.997369, west:  true,
            horizon:      30,
            duration:     TimeSpan.FromMinutes(240),
            dateTime:     DateTime.Now,
            timeZoneInfo: TimeZoneInfo.Local);
    }
}
