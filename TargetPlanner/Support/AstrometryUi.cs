using Astronomy.Core.Astrometry;
using Astronomy.Core.Night;
using Astronomy.Core.Sun;
using System;

using Location = Astronomy.Core.Locations.Location;

namespace TargetPlanner.Support
{
    /// <summary>
    /// Snapshot of UI-bound astrometry values for a given <see cref="Location"/>:
    /// astronomical dawn / dusk, sun + moon altitudes, moon rise / set, lunar
    /// phase, lunar illumination. Every field reflects the same input Location
    /// at one point in time -- snapshot-coherent and immutable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built by <see cref="For(Location)"/> on any thread (pure compute; no
    /// shared state). MainForm holds the most recent instance in a field and
    /// reassigns it on every <c>RefreshAstrometryLabels</c> call (DatePicker /
    /// TimePicker / Button_Now / OnLocationEdited scrubs); the dependent
    /// labels read from the instance, never from static state.
    /// </para>
    /// <para>
    /// The math functions that used to live on this class moved into
    /// Astronomy.Core -- call <c>Astronomy.Core.AltAz</c>,
    /// <c>Astronomy.Core.TargetGeometry</c>,
    /// <c>Astronomy.Core.Time.SiderealTime</c>, and
    /// <c>Astronomy.Core.Night.NightCalculator</c> directly for those.
    /// </para>
    /// </remarks>
    public sealed record AstrometryUi(
        DateTime AstronomicalDawn,
        DateTime AstronomicalDusk,
        double   SunAltitude,
        DateTime LunarRise,
        DateTime LunarSet,
        double   LunarAltitude,
        string   LunarPhase,
        double   LunarIlluminationFraction)
    {
        /// <summary>Empty snapshot used as the initial value before the first
        /// <see cref="For(Location)"/> call lands. Every field is the safe
        /// default (zero / <see cref="DateTime.MinValue"/> / empty string), so
        /// label binding before MainForm boot doesn't crash.</summary>
        public static readonly AstrometryUi Empty = new(
            AstronomicalDawn:          DateTime.MinValue,
            AstronomicalDusk:          DateTime.MinValue,
            SunAltitude:               0.0,
            LunarRise:                 DateTime.MinValue,
            LunarSet:                  DateTime.MinValue,
            LunarAltitude:             0.0,
            LunarPhase:                string.Empty,
            LunarIlluminationFraction: 0.0);

        /// <summary>
        /// Compute the snapshot for <paramref name="location"/>. Pure -- safe to
        /// call from any thread. ~150 µs of Meeus + 0 allocations beyond the
        /// returned record (the closed-form sun / moon paths are allocation-free
        /// post the Astronomy.Core 2026-05-04 perf pass).
        /// </summary>
        public static AstrometryUi For(Location location)
        {
            DateTime utc = location.DateTime.ToUniversalTime();
            double latSigned = location.LatSigned();
            double lonEast   = location.LonEast();
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            // Astronomical-twilight night window bracketing now.
            NightWindow night = NightCalculator.ComputeNight(location);
            DateTime astronomicalDawn = night.AstronomicalDawn != DateTime.MinValue
                ? night.AstronomicalDawn.ToLocalTime() : DateTime.MinValue;
            DateTime astronomicalDusk = night.AstronomicalDusk != DateTime.MinValue
                ? night.AstronomicalDusk.ToLocalTime() : DateTime.MinValue;

            // Per-moment sun / moon altitudes at the observer. Sun goes through the
            // Astronomy.Core.Sun.SunPosition facade; Moon stays on AstroUtil.
            double sunAltitude   = SunPosition.AltAzAt(location, utc).Altitude;
            double lunarAltitude = AstroUtil.GetMoonAltitude(utc, observer);

            // Lunar phase name from synodic-cycle bucket.
            string lunarPhase = AstroUtil.GetMoonPhaseName(utc);

            // Moon rise / set on today's UTC calendar day; elevation-corrected for the
            // observer's horizon dip so high-altitude users see the moon rise earlier and
            // set later (~3.5 min shift at 1000 m, ~11 min at 10000 m).
            RiseAndSetEvent moonRs = AstroUtil.GetMoonRiseAndSet(utc, latSigned, lonEast, location.Elevation);
            DateTime lunarRise = moonRs.Rise.HasValue ? moonRs.Rise.Value.ToLocalTime() : DateTime.MinValue;
            DateTime lunarSet  = moonRs.Set .HasValue ? moonRs.Set .Value.ToLocalTime() : DateTime.MinValue;

            return new AstrometryUi(
                AstronomicalDawn:          astronomicalDawn,
                AstronomicalDusk:          astronomicalDusk,
                SunAltitude:               sunAltitude,
                LunarRise:                 lunarRise,
                LunarSet:                  lunarSet,
                LunarAltitude:             lunarAltitude,
                LunarPhase:                lunarPhase,
                LunarIlluminationFraction: night.LunarIlluminationFraction);
        }
    }
}
