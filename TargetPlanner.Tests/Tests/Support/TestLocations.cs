using System;
using Astronomy.Core.Locations;

namespace TargetPlanner.Tests.Tests.Support
{
    // Test-only location fixtures with stable, hand-picked coordinates. Adapted
    // from Library\Astronomy.Core.Tests\Tests\TestLocations.cs -- cross-repo
    // duplication, sync if either drifts. Both copies are static readonly fields
    // (not `=> new Location(...)` properties) so reference identity is stable
    // across multiple test-method accesses; the cache's TryPublish discard and
    // per-target dict keys both depend on that.
    public static class TestLocations
    {
        // The historical Location.Default site -- US east coast, mid-latitude N, suburban
        // Bortle 5.
        public static readonly Location PennsPark = new Location(
            name:         "Penns Park",
            latitude:     40.282835, north: true,
            longitude:    74.997369, west:  true,
            timeZoneInfo: TimeZoneInfo.Local,
            elevation:    80.67,
            bortleClass:  5,
            extinctionK:  0.28);

        // Sydney Opera House. Southern hemisphere, eastern longitude.
        public static readonly Location Sydney = new Location(
            name:         "Sydney",
            latitude:     33.8568, north: false,
            longitude:    151.2153, west: false,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    20.0,
            bortleClass:  7,
            extinctionK:  0.35);

        // Quito, Ecuador. Just south of the equator, western longitude. Stresses
        // the equator-degenerate latitude case (cos(phi) ~ 1).
        public static readonly Location Equator = new Location(
            name:         "Quito",
            latitude:     0.1807, north: false,
            longitude:    78.4678, west: true,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    2850.0,
            bortleClass:  6,
            extinctionK:  0.20);

        // Reykjavik. High northern latitude (~64 N).
        public static readonly Location Reykjavik = new Location(
            name:         "Reykjavik",
            latitude:     64.1466, north: true,
            longitude:    21.9426, west: true,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    10.0,
            bortleClass:  4,
            extinctionK:  0.22);
    }
}
