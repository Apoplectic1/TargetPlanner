namespace Astronomy.Core.Horizons
{
    // Azimuth-dependent horizon altitude, wrapped so 360 -> 0.
    //
    // Consumers take IHorizonProfile rather than a scalar double so a single call site can
    // handle everything from "flat 30-degree minimum" up to a 360-sample user-entered
    // obstruction table. MinAltitude is exposed as a fast-path lower bound for rise/set
    // solvers that want to bracket with a scalar before refining against the profile.
    public interface IHorizonProfile
    {
        // Horizon altitude (degrees) at the given azimuth (degrees, 0 = North, clockwise).
        // Implementations must tolerate any real azimuth and treat it modulo 360.
        double AltitudeAt(double azimuthDeg);

        // Minimum altitude over the full 0..360 azimuth range. Used by rise/set solvers as
        // a scalar lower bound: if the target is below MinAltitude it is guaranteed below the
        // profile too, so the scalar fast-path gives a safe seed for Newton refinement.
        double MinAltitude { get; }
    }
}
