namespace Astronomy.Core.Horizons
{
    /// <summary>
    /// Azimuth-dependent horizon altitude, wrapped modulo 360.
    /// </summary>
    /// <remarks>
    /// Consumers take <see cref="IHorizonProfile"/> rather than a scalar so a single call
    /// site can handle everything from "flat 30-degree minimum" (see
    /// <see cref="ScalarHorizonProfile"/>) up to a 360-sample user-entered obstruction table
    /// (see <see cref="ObstructionTableHorizonProfile"/>). <see cref="MinAltitude"/> is
    /// exposed as a fast-path lower bound for rise/set solvers that want to bracket with a
    /// scalar before refining against the profile.
    /// </remarks>
    public interface IHorizonProfile
    {
        /// <summary>
        /// Horizon altitude (degrees) at the given azimuth (degrees, 0 = North, clockwise).
        /// Implementations must tolerate any real azimuth and treat it modulo 360.
        /// </summary>
        double AltitudeAt(double azimuthDeg);

        /// <summary>
        /// Minimum altitude over the full <c>[0, 360)</c> azimuth range. Used by rise/set
        /// solvers as a scalar lower bound: if the target is below <see cref="MinAltitude"/>
        /// it is guaranteed below the profile too, so the scalar fast-path gives a safe
        /// seed for profile-aware refinement.
        /// </summary>
        double MinAltitude { get; }
    }
}
