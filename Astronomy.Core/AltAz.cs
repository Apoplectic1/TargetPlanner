using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core
{
    /// <summary>
    /// Topocentric horizontal coordinates: altitude and azimuth.
    /// </summary>
    /// <remarks>
    /// Altitude is degrees above the mathematical horizon (0 = horizon, positive = up);
    /// never adjusted for atmospheric refraction. Azimuth is degrees from North, clockwise,
    /// in <c>[0, 360)</c>. Replaces the previous <c>Tuple&lt;double, double&gt;</c> return
    /// so callers unpack by meaning rather than by position.
    /// </remarks>
    public readonly struct AltAz
    {
        /// <summary>Altitude above the mathematical horizon, in degrees. Unrefracted.</summary>
        public double Altitude { get; }

        /// <summary>Azimuth measured clockwise from North, in degrees, in <c>[0, 360)</c>.</summary>
        public double Azimuth  { get; }

        /// <summary>
        /// Constructs an <see cref="AltAz"/> from explicit altitude and azimuth values.
        /// </summary>
        public AltAz(double altitude, double azimuth)
        {
            Altitude = altitude;
            Azimuth = azimuth;
        }

        /// <summary>
        /// Enables positional deconstruction:
        /// <c>var (alt, az) = AltAzCalculator.Of(target, location);</c>
        /// </summary>
        public void Deconstruct(out double altitude, out double azimuth)
        {
            altitude = Altitude;
            azimuth  = Azimuth;
        }
    }

    /// <summary>
    /// Static helper: produces an <see cref="AltAz"/> for a target seen from a location.
    /// Signed hemispheres are resolved internally from the <see cref="Target.North"/> /
    /// <see cref="Location.North"/> / <see cref="Location.West"/> flags, so callers pass
    /// the unsigned magnitudes stored in <see cref="Target"/> / <see cref="Location"/>.
    /// </summary>
    public static class AltAzCalculator
    {
        /// <summary>
        /// Returns the altitude and azimuth of <paramref name="target"/> as seen from
        /// <paramref name="location"/> at the given UTC instant.
        /// </summary>
        /// <param name="target">Target RA/Dec in the Core convention (unsigned + North flag).</param>
        /// <param name="location">Observer latitude/longitude in the Core convention.</param>
        /// <param name="utc">The instant at which to evaluate. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static AltAz At(Target target, Location location, DateTime utc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            double raHours = target.RightAscension;
            double decDeg = target.North ? target.Declination : -target.Declination;
            double latDeg = location.North ? location.Latitude : -location.Latitude;
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;

            double lst = SiderealTime.Local(utc, lonDegEast);
            double hourAngle = lst - raHours;

            double altitude = TargetGeometry.AltitudeAtHourAngle(hourAngle, latDeg, decDeg);
            double azimuth  = TargetGeometry.AzimuthAtHourAngle(hourAngle, latDeg, decDeg);
            return new AltAz(altitude, azimuth);
        }

        /// <summary>
        /// Overload that reads the UTC instant from
        /// <c>location.DateTime.ToUniversalTime()</c>. Accepts
        /// <see cref="Location.DateTime"/> with any <see cref="DateTimeKind"/>: Local and
        /// Unspecified are treated as local and converted via Windows rules; Utc is a
        /// no-op. Callers that want to evaluate at a <see cref="Night.NightWindow"/>-sourced
        /// instant (Kind=Utc) can pass it through
        /// <c>Location.With(dateTime: night.AstronomicalDusk)</c> without converting first.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static AltAz Of(Target target, Location location)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            return At(target, location, location.DateTime.ToUniversalTime());
        }
    }
}
