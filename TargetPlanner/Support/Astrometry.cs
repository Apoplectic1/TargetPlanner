using Astronomy.Core.Astrometry;
using Astronomy.Core.Night;
using System;

using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Support
{
    // UI state facade: refreshes the static dawn/dusk/moon-phase/etc. properties that the
    // MainForm binds to, from the current Location. The math functions that used to live on
    // this class have moved into Astronomy.Core -- call Astronomy.Core.AltAz,
    // Astronomy.Core.TargetGeometry, Astronomy.Core.Time.SiderealTime, and
    // Astronomy.Core.Night.NightCalculator directly for those.
    public class Astrometry
    {
        public static DateTime AstronomicalDawn          { get; private set; }
        public static DateTime AstronomicalDusk          { get; private set; }
        public static double   SunAltitude              { get; private set; }
        public static DateTime LunarRise                 { get; private set; }
        public static DateTime LunarSet                  { get; private set; }
        public static double   LunarAltitude             { get; private set; }
        public static string   LunarPhase                { get; private set; }
        public static double   LunarIlluminationFraction { get; private set; }

        public Astrometry()
        {
        }

        public static void Location(Location localLocation)
        {
            DateTime utc = localLocation.DateTime.ToUniversalTime();
            double latSigned = localLocation.LatSigned();
            double lonEast   = localLocation.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, localLocation.Elevation);

            // Astronomical-twilight night window bracketing now.
            NightWindow night = NightCalculator.ComputeNight(localLocation);
            AstronomicalDawn = night.AstronomicalDawn != DateTime.MinValue
                ? night.AstronomicalDawn.ToLocalTime() : DateTime.MinValue;
            AstronomicalDusk = night.AstronomicalDusk != DateTime.MinValue
                ? night.AstronomicalDusk.ToLocalTime() : DateTime.MinValue;
            LunarIlluminationFraction = night.LunarIlluminationFraction;

            // Per-moment sun / moon altitudes at the observer.
            SunAltitude   = AstroUtil.GetSunAltitude (utc, observer);
            LunarAltitude = AstroUtil.GetMoonAltitude(utc, observer);

            // Lunar phase name from synodic-cycle bucket.
            LunarPhase = AstroUtil.GetMoonPhaseName(utc);

            // Moon rise / set on today's UTC calendar day; elevation-corrected for the
            // observer's horizon dip so high-altitude users see the moon rise earlier and
            // set later (~3.5 min shift at 1000 m, ~11 min at 10000 m).
            RiseAndSetEvent moonRs = AstroUtil.GetMoonRiseAndSet(utc, latSigned, lonEast, localLocation.Elevation);
            LunarRise = moonRs.Rise.HasValue ? moonRs.Rise.Value.ToLocalTime() : DateTime.MinValue;
            LunarSet  = moonRs.Set .HasValue ? moonRs.Set .Value.ToLocalTime() : DateTime.MinValue;
        }
    }
}
