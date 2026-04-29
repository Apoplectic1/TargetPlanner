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
            double latSigned = localLocation.North ?  localLocation.Latitude  : -localLocation.Latitude;
            double lonEast   = localLocation.West  ? -localLocation.Longitude :  localLocation.Longitude;
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, 0.0);

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

            // Moon rise / set on today's UTC calendar day; convert to local for the UI label.
            RiseAndSetEvent moonRs = AstroUtil.GetMoonRiseAndSet(utc, latSigned, lonEast);
            LunarRise = moonRs.Rise.HasValue ? moonRs.Rise.Value.ToLocalTime() : DateTime.MinValue;
            LunarSet  = moonRs.Set .HasValue ? moonRs.Set .Value.ToLocalTime() : DateTime.MinValue;
        }
    }
}
