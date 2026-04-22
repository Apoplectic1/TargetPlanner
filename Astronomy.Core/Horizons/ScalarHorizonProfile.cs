namespace Astronomy.Core.Horizons
{
    /// <summary>
    /// Constant horizon altitude regardless of azimuth. Wraps the legacy "just a single
    /// <see cref="Astronomy.Core.Locations.Location.Horizon"/> number" case in the
    /// <see cref="IHorizonProfile"/> abstraction so callers can ignore the scalar-vs-profile
    /// distinction entirely.
    /// </summary>
    public sealed class ScalarHorizonProfile : IHorizonProfile
    {
        private readonly double mAltitudeDeg;

        /// <summary>Constructs a profile that returns <paramref name="altitudeDeg"/> at every azimuth.</summary>
        public ScalarHorizonProfile(double altitudeDeg)
        {
            mAltitudeDeg = altitudeDeg;
        }

        /// <inheritdoc />
        public double AltitudeAt(double azimuthDeg) => mAltitudeDeg;

        /// <inheritdoc />
        public double MinAltitude => mAltitudeDeg;
    }
}
