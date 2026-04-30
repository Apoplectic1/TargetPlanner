using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Support
{
    // Resolves the magnitude+hemisphere flag stored on Location / Target into the
    // signed-degrees inputs that Core's TargetGeometry / AltAz / SiderealTime
    // helpers expect. Encodes the convention CLAUDE.md documents in code rather
    // than as repeated three-line idioms at every call site.
    internal static class CoordinateExtensions
    {
        public static double LatSigned(this Location l) => l.North ?  l.Latitude  : -l.Latitude;
        public static double LonEast(this Location l)   => l.West  ? -l.Longitude :  l.Longitude;
        public static double DecSigned(this Target t)   => t.North ?  t.Declination : -t.Declination;
    }
}
