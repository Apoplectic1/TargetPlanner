using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Julian Date conversions for UTC instants.
    /// </summary>
    public static class JulianDate
    {
        /// <summary>
        /// Julian Date of the given UTC instant.
        /// </summary>
        /// <remarks>
        /// Uses the OADate idiom (days since 1899-12-30 00:00 UT plus the offset to
        /// JD 2415018.5), accurate to sub-millisecond for all dates representable by
        /// <see cref="DateTime"/>.
        /// </remarks>
        /// <param name="utc">Instant to convert. Must be UTC.</param>
        public static double FromUtc(DateTime utc)
        {
            return utc.ToOADate() + 2415018.5;
        }
    }
}
