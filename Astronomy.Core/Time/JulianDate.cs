using System;

namespace Astronomy.Core.Time
{
    public static class JulianDate
    {
        // Julian Date of the given UTC instant. Uses the OADate idiom (days since 1899-12-30
        // 00:00 UT plus the offset to JD 2415018.5) which is accurate to sub-millisecond for
        // all dates representable by System.DateTime.
        public static double FromUtc(DateTime utc)
        {
            return utc.ToOADate() + 2415018.5;
        }
    }
}
