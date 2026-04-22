using System;

namespace Astronomy.Core.Targets
{
    /// <summary>
    /// Immutable deep-sky target: name plus equatorial coordinates (RA in decimal hours, Dec
    /// in decimal degrees) in the Core magnitude-plus-flag convention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every property is read-only; mutations produce a new instance via <see cref="With"/>.
    /// Construction takes a full parameter set; callers use <see cref="Default"/> for the M31
    /// defaults.
    /// </para>
    /// <para>
    /// <b>RA is stored as decimal hours in [0, 24).</b> <see cref="Declination"/> is stored
    /// as a non-negative magnitude with hemisphere in the <see cref="North"/> flag, matching
    /// the <see cref="Astronomy.Core.Locations.Location"/> convention. A negative declination
    /// passed to the constructor is normalized into <c>(positive magnitude, flipped North)</c>.
    /// </para>
    /// <para>
    /// D/M/S accessors (<see cref="RaHours"/> / <see cref="RaMinutes"/> /
    /// <see cref="RaSeconds"/> and <see cref="DecDegrees"/> / <see cref="DecMinutes"/> /
    /// <see cref="DecSeconds"/>) are computed on read -- no stored fields, no possibility of
    /// drift. The DMS breakdown for Declination is a direct decimal-degree decomposition
    /// (<c>degrees + minutes/60 + seconds/3600</c>), consistent with
    /// <see cref="Astronomy.Core.Locations.Location.LatDegrees"/> etc.; the previous
    /// implementation routed through <see cref="TimeSpan.FromHours"/> and produced values
    /// that matched hours-of-declination rather than degrees-of-declination.
    /// </para>
    /// </remarks>
    public sealed class Target
    {
        /// <summary>Human-readable name (e.g. "M31", "Abell 21"). Defaults to "Custom".</summary>
        public string  Name           { get; }

        /// <summary>Right ascension in decimal hours, in <c>[0, 24)</c>. NOT degrees.</summary>
        public double  RightAscension { get; }

        /// <summary>Declination magnitude in decimal degrees, non-negative. Hemisphere lives in <see cref="North"/>.</summary>
        public double  Declination    { get; }

        /// <summary><see langword="true"/> for Northern hemisphere declination, <see langword="false"/> for Southern.</summary>
        public bool    North          { get; }

        /// <summary>
        /// Filesystem path the target was loaded from (e.g. the NINA .json file). Empty string
        /// if the target was not loaded from disk.
        /// </summary>
        public string  Directory      { get; }

        /// <summary>
        /// Whether this target is currently selected for multi-target operations. Defaults to
        /// <see langword="true"/>. Not meaningful for single-target paths (e.g.
        /// <see cref="AltAzCalculator.At"/>).
        /// </summary>
        public bool    Enabled        { get; }

        /// <summary>Sexagesimal RA whole-hours component (0-23). NOT a synonym for decimal <see cref="RightAscension"/>.</summary>
        public double RaHours   => Math.Floor(RightAscension);
        /// <summary>Sexagesimal RA whole-minutes component (0-59).</summary>
        public double RaMinutes => Math.Floor(60.0 * (RightAscension - RaHours));
        /// <summary>Sexagesimal RA fractional-seconds component.</summary>
        public double RaSeconds => 3600.0 * (RightAscension - RaHours - RaMinutes / 60.0);

        /// <summary>Sexagesimal Dec whole-degrees component; always non-negative (hemisphere in <see cref="North"/>).</summary>
        public double DecDegrees => Math.Truncate(Declination);
        /// <summary>Sexagesimal Dec whole-arcminutes component.</summary>
        public double DecMinutes => Math.Floor(60.0 * (Declination - DecDegrees));
        /// <summary>Sexagesimal Dec fractional-arcseconds component.</summary>
        public double DecSeconds => 3600.0 * (Declination - DecDegrees - DecMinutes / 60.0);

        /// <summary>
        /// Constructs a fully-specified <see cref="Target"/>. A negative
        /// <paramref name="declination"/> flips <paramref name="north"/> and is stored as a
        /// positive magnitude. A <see langword="null"/> <paramref name="name"/> defaults to
        /// "Custom"; a <see langword="null"/> <paramref name="directory"/> defaults to
        /// <see cref="string.Empty"/>.
        /// </summary>
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

        /// <summary>
        /// Named-argument builder. Any omitted argument inherits from the current instance.
        /// </summary>
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

        /// <summary>
        /// M31 (Andromeda) defaults. Freshly instantiated on each access.
        /// </summary>
        public static Target Default => new Target(
            name:           "M31",
            rightAscension: 0.712306,
            declination:    41.269167, north: true,
            directory:      string.Empty,
            enabled:        true);
    }
}
