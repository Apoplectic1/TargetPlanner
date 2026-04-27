using Astronomy.Core.Moon;
using Newtonsoft.Json;

namespace TargetPlanner.Filters
{
    /// <summary>
    /// Photographic filter with persisted moon-avoidance defaults and bandwidth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each user-configured filter (typically <c>H</c>, <c>O</c>, <c>S</c>, <c>L</c>,
    /// <c>R</c>, <c>G</c>, <c>B</c>) carries its own Lorentzian / relaxation parameters
    /// plus a bandwidth in nanometres. The bandwidth is captured for the future
    /// IntervalScheduler plugin's K-S sky-brightness work; <see cref="ToProfile"/> drops
    /// it because TargetPlanner's Lorentzian doesn't consume it.
    /// </para>
    /// <para>
    /// Library persists as JSON via <see cref="FilterLibrary"/>. Newtonsoft.Json maps
    /// constructor parameters to property names by case-insensitive match, so the
    /// constructor signature is the deserialization contract.
    /// </para>
    /// </remarks>
    public sealed class Filter
    {
        /// <summary>Short user-facing name (e.g. "H", "O", "S", "L", "R", "G", "B").</summary>
        public string Name           { get; }

        /// <summary>Required separation (degrees) at full moon. Lorentzian "distance" parameter.</summary>
        public double SeparationDeg  { get; }

        /// <summary>Width parameter (days) of the Lorentzian.</summary>
        public double WidthDays      { get; }

        /// <summary>Whether the TS-style altitude relaxation extension is applied.</summary>
        public bool   RelaxEnabled   { get; }

        /// <summary>Lower altitude bound (degrees) of the relaxation zone.</summary>
        public double RelaxMinAltDeg { get; }

        /// <summary>Upper altitude bound (degrees) of the relaxation zone.</summary>
        public double RelaxMaxAltDeg { get; }

        /// <summary>Distance-ramp coefficient inside the relaxation zone.</summary>
        public double RelaxScale     { get; }

        /// <summary>
        /// Filter passband (nm). Captured for the future IntervalScheduler plugin's K-S
        /// sky-brightness model; ignored by TargetPlanner's Lorentzian.
        /// </summary>
        public double BandwidthNm    { get; }

        /// <summary>
        /// Constructs a fully-specified filter. Serializable via Newtonsoft.Json's
        /// constructor mapping (parameter names match property names).
        /// </summary>
        [JsonConstructor]
        public Filter(
            string name,
            double separationDeg, double widthDays,
            bool   relaxEnabled,
            double relaxMinAltDeg, double relaxMaxAltDeg, double relaxScale,
            double bandwidthNm)
        {
            Name           = name ?? "Custom";
            SeparationDeg  = separationDeg;
            WidthDays      = widthDays;
            RelaxEnabled   = relaxEnabled;
            RelaxMinAltDeg = relaxMinAltDeg;
            RelaxMaxAltDeg = relaxMaxAltDeg;
            RelaxScale     = relaxScale;
            BandwidthNm    = bandwidthNm;
        }

        /// <summary>
        /// Named-argument builder. Any omitted argument inherits from the current instance.
        /// </summary>
        public Filter With(
            string name = null,
            double? separationDeg = null,
            double? widthDays = null,
            bool?   relaxEnabled = null,
            double? relaxMinAltDeg = null,
            double? relaxMaxAltDeg = null,
            double? relaxScale = null,
            double? bandwidthNm = null)
            => new Filter(
                name           ?? this.Name,
                separationDeg  ?? this.SeparationDeg,
                widthDays      ?? this.WidthDays,
                relaxEnabled   ?? this.RelaxEnabled,
                relaxMinAltDeg ?? this.RelaxMinAltDeg,
                relaxMaxAltDeg ?? this.RelaxMaxAltDeg,
                relaxScale     ?? this.RelaxScale,
                bandwidthNm    ?? this.BandwidthNm);

        /// <summary>
        /// Convert to a moon-aware avoidance profile. Drops <see cref="Name"/> and
        /// <see cref="BandwidthNm"/> (both TP-only metadata).
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
