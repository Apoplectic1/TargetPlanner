using Astronomy.Core.Moon;

namespace TargetPlanner.Filters
{
    /// <summary>
    /// Photographic filter with persisted moon-avoidance defaults and bandwidth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each user-configured filter (typically <c>H</c>, <c>O</c>, <c>S</c>, <c>L</c>,
    /// <c>R</c>, <c>G</c>, <c>B</c>) carries its own Lorentzian / relaxation parameters
    /// plus a center wavelength and a bandwidth in nanometres. Center + bandwidth
    /// feed the K-S sky-brightness model via <c>PlanningPolicy.ActiveFilter</c>;
    /// <see cref="ToProfile"/> extracts the Lorentzian slice for the moon-clear gate.
    /// </para>
    /// <para>
    /// Library persists as JSON via <see cref="FilterLibrary"/>. The record's positional
    /// constructor is the deserialization contract; Newtonsoft.Json maps parameter
    /// names to property names case-insensitively.
    /// </para>
    /// <para>
    /// Promoted from <c>sealed class</c> to <c>sealed record</c> so structural equality
    /// flows into <c>HdmKey.ActiveFilter</c> for cache invalidation, and so Lorentzian
    /// scrubs can mutate via <c>with</c> expressions instead of the prior hand-written
    /// <c>With</c> builder.
    /// </para>
    /// </remarks>
    public sealed record Filter(
        string Name,
        double SeparationDeg,
        double WidthDays,
        bool RelaxEnabled,
        double RelaxMinAltDeg,
        double RelaxMaxAltDeg,
        double RelaxScale,
        double CenterNm,
        double BandwidthNm)
    {
        /// <summary>
        /// Convert to a moon-aware avoidance profile. Drops <see cref="Name"/>,
        /// <see cref="CenterNm"/>, and <see cref="BandwidthNm"/> (Filter metadata not
        /// consumed by the wavelength-agnostic Lorentzian).
        /// </summary>
        public MoonAvoidanceProfile ToProfile()
            => new MoonAvoidanceProfile(
                enabled:        true,
                separationDeg:  SeparationDeg,
                widthDays:      WidthDays,
                relaxEnabled:   RelaxEnabled,
                relaxMinAltDeg: RelaxMinAltDeg,
                relaxMaxAltDeg: RelaxMaxAltDeg,
                relaxScale:     RelaxScale);
    }
}
