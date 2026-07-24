using Astronomy.Core.Moon;

namespace TargetPlanner.Filters
{
    /// <summary>
    /// Photographic filter with persisted moon-gate tolerance and band parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each user-configured filter (typically <c>H</c>, <c>O</c>, <c>S</c>, <c>L</c>,
    /// <c>R</c>, <c>G</c>, <c>B</c>) carries a K-S Δmag moon tolerance plus a center
    /// wavelength and a bandwidth in nanometres. Center + bandwidth feed the K-S
    /// sky-brightness chart via <c>PlanningPolicy.ActiveFilter</c>;
    /// <see cref="ToProfile"/> extracts the moon-gate slice (tolerance + center — the
    /// gate is bandwidth-independent by construction, so <see cref="BandwidthNm"/>
    /// deliberately stays chart-only).
    /// </para>
    /// <para>
    /// Library persists as JSON via <see cref="FilterLibrary"/>. The record's positional
    /// constructor is the deserialization contract; Newtonsoft.Json maps parameter
    /// names to property names case-insensitively.
    /// </para>
    /// <para>
    /// Promoted from <c>sealed class</c> to <c>sealed record</c> so structural equality
    /// flows into <c>HdmKey.ActiveFilter</c> for cache invalidation, and so tolerance
    /// scrubs can mutate via <c>with</c> expressions instead of the prior hand-written
    /// <c>With</c> builder.
    /// </para>
    /// </remarks>
    public sealed record Filter(
        string Name,
        double ToleranceMag,
        double CenterNm,
        double BandwidthNm)
    {
        /// <summary>
        /// Convert to the K-S moon-gate profile: accept a session minute iff the
        /// moon-driven sky brightening at the target is within
        /// <see cref="ToleranceMag"/> of the moonless baseline. Drops
        /// <see cref="Name"/> and <see cref="BandwidthNm"/> (the bandwidth scale
        /// cancels exactly in the Δmag ratio — Library assumption #24).
        /// </summary>
        public MoonLimitProfile ToProfile()
            => new MoonLimitProfile(
                enabled:      true,
                toleranceMag: ToleranceMag,
                centerNm:     CenterNm);
    }
}
