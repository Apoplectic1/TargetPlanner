namespace Astronomy.Core.Horizons
{
    // Constant horizon altitude regardless of azimuth. Wraps the legacy "just a single
    // Location.Horizon number" case in the IHorizonProfile abstraction so callers can ignore
    // the scalar-vs-profile distinction entirely.
    public sealed class ScalarHorizonProfile : IHorizonProfile
    {
        private readonly double mAltitudeDeg;

        public ScalarHorizonProfile(double altitudeDeg)
        {
            mAltitudeDeg = altitudeDeg;
        }

        public double AltitudeAt(double azimuthDeg) => mAltitudeDeg;
        public double MinAltitude => mAltitudeDeg;
    }
}
