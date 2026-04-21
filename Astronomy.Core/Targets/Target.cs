using System;

namespace Astronomy.Core.Targets
{
    // Immutable deep-sky target. Every property is read-only; mutations produce a new
    // instance via With(...). Construction takes a full parameter set; callers use
    // Target.Default for the M31 defaults.
    //
    // RA is stored as decimal hours [0, 24). Declination is stored as a non-negative
    // magnitude with hemisphere in the North flag, matching the Location convention.
    // A negative declination passed to the constructor is normalized into
    // (positive magnitude, flipped North).
    //
    // D/M/S accessors (RaHours/RaMinutes/RaSeconds and DecDegrees/DecMinutes/DecSeconds)
    // are computed on read -- no stored fields, no possibility of drift. The DMS breakdown
    // for Declination is a direct decimal-degree decomposition (degrees + minutes/60 +
    // seconds/3600), consistent with Location's LatDegrees / LatMinutes / LatSeconds;
    // the previous implementation routed through TimeSpan.FromHours and produced values
    // that matched hours-of-declination rather than degrees-of-declination.
    public sealed class Target
    {
        public string  Name           { get; }
        public double  RightAscension { get; }   // decimal hours [0, 24)
        public double  Declination    { get; }   // non-negative magnitude in degrees
        public bool    North          { get; }
        public string  Directory      { get; }
        public bool    Enabled        { get; }

        // Sexagesimal RA components derived from RightAscension. "RaHours" is the whole-hours
        // digit of the DMS breakdown, NOT a synonym for decimal RightAscension -- don't feed
        // RaHours into altitude math that expects hours-as-a-float.
        public double RaHours   => Math.Floor(RightAscension);
        public double RaMinutes => Math.Floor(60.0 * (RightAscension - RaHours));
        public double RaSeconds => 3600.0 * (RightAscension - RaHours - RaMinutes / 60.0);

        // Sexagesimal Dec components derived from the stored magnitude. DecDegrees is always
        // non-negative (hemisphere lives in the North flag), so a southern declination of
        // -30.5 reports DecDegrees = 30, DecMinutes = 30, North = false -- the sign is not
        // re-applied to the D/M/S triple.
        public double DecDegrees => Math.Truncate(Declination);
        public double DecMinutes => Math.Floor(60.0 * (Declination - DecDegrees));
        public double DecSeconds => 3600.0 * (Declination - DecDegrees - DecMinutes / 60.0);

        public Target(
            string name,
            double rightAscension,
            double declination, bool north,
            string directory,
            bool enabled)
        {
            // Negative declination flips the hemisphere flag, matching Location's convention.
            if (declination < 0) { declination = -declination; north = !north; }

            Name           = name ?? "Custom";
            RightAscension = rightAscension;
            Declination    = declination;
            North          = north;
            Directory      = directory ?? string.Empty;
            Enabled        = enabled;
        }

        public Target With(
            string name = null,
            double? rightAscension = null,
            double? declination = null, bool? north = null,
            string directory = null,
            bool? enabled = null)
            => new Target(
                name           ?? this.Name,
                rightAscension ?? this.RightAscension,
                declination    ?? this.Declination,
                north          ?? this.North,
                directory      ?? this.Directory,
                enabled        ?? this.Enabled);

        public static Target Default => new Target(
            name:           "M31",
            rightAscension: 0.712306,
            declination:    41.269167, north: true,
            directory:      string.Empty,
            enabled:        true);
    }
}
