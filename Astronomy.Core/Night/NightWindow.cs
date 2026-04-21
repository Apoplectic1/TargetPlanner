using System;

namespace Astronomy.Core.Night
{
    // Dusk / dawn pair bracketing a single night, plus the lunar illumination fraction
    // at that night. DateTime.MinValue is used as a sentinel when the night window is not
    // well-defined (polar day / polar night / outside CoordinateSharp's valid range).
    // Consumers can short-circuit with IsValid instead of checking both DateTimes against
    // MinValue individually.
    //
    // AstronomicalDawn / AstronomicalDusk are DateTimeKind.Utc -- absolute UTC instants.
    // Call .ToLocalTime() when displaying wall-clock time; .ToUniversalTime() is a safe
    // no-op on values stamped Utc.
    public struct NightWindow
    {
        public DateTime AstronomicalDawn;
        public DateTime AstronomicalDusk;
        public double   LunarIlluminationFraction;

        public bool IsValid => AstronomicalDawn != DateTime.MinValue
                            && AstronomicalDusk != DateTime.MinValue;
    }
}
