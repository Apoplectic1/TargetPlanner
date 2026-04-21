using System;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Dusk / dawn pair bracketing a single night, plus the lunar illumination fraction at
    /// that night.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AstronomicalDawn"/> and <see cref="AstronomicalDusk"/> are
    /// <see cref="DateTimeKind.Utc"/> -- absolute UTC instants. Call <c>.ToLocalTime()</c>
    /// when displaying wall-clock time; <c>.ToUniversalTime()</c> on values stamped Utc is
    /// a safe no-op.
    /// </para>
    /// <para>
    /// <see cref="DateTime.MinValue"/> is used as a sentinel when the night window is not
    /// well-defined (polar day / polar night / outside CoordinateSharp's valid range).
    /// Consumers can short-circuit with <see cref="IsValid"/> instead of checking both
    /// fields against <see cref="DateTime.MinValue"/> individually.
    /// </para>
    /// </remarks>
    public struct NightWindow
    {
        /// <summary>UTC instant of astronomical dawn bounding this night.</summary>
        public DateTime AstronomicalDawn;

        /// <summary>UTC instant of astronomical dusk bounding this night.</summary>
        public DateTime AstronomicalDusk;

        /// <summary>Fraction of the moon's disk that is illuminated at this night, in [0, 1].</summary>
        public double   LunarIlluminationFraction;

        /// <summary>
        /// <see langword="true"/> if both <see cref="AstronomicalDawn"/> and
        /// <see cref="AstronomicalDusk"/> are real instants (not the
        /// <see cref="DateTime.MinValue"/> sentinel), i.e. a genuine dark-sky window exists
        /// at the observer's location for the target date.
        /// </summary>
        public bool IsValid => AstronomicalDawn != DateTime.MinValue
                            && AstronomicalDusk != DateTime.MinValue;
    }
}
