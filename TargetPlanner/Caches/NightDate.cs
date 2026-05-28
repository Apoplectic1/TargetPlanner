using System;
using Astronomy.Core.Night;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-night cache key for <see cref="ChartCacheStore"/>'s ephemeris and
    /// trajectory axes. Represents the local calendar date on which the night's
    /// astronomical dusk occurs at the site's <see cref="System.TimeZoneInfo"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Dusk date" rather than "dawn date" matches user mental model: when the
    /// user picks a date in <c>DatePicker</c>, they mean the night starting on
    /// that evening (dusk → next-day dawn). NightDate's value tracks dusk's
    /// local calendar date in the site's zone, which equals
    /// <c>DatePicker.Value.Date</c> for the common case.
    /// </para>
    /// <para>
    /// Stable across DST transitions (DST shifts dusk's wall-clock time but not
    /// its calendar date). Polar edge cases (no astronomical dusk on the
    /// calendar day) are handled by callers via <see cref="NightWindow.IsValid"/>;
    /// NightDate itself just carries the date.
    /// </para>
    /// <para>
    /// Within a Location-scoped cache the zone is implicit (Location swap drops
    /// the cache); a bare <see cref="DateOnly"/> is sufficient. The <c>Of</c>
    /// factory is the single derivation seam from a built
    /// <see cref="NightWindow"/> + zone.
    /// </para>
    /// </remarks>
    public readonly record struct NightDate(DateOnly DuskDate)
    {
        /// <summary>
        /// Derive a <see cref="NightDate"/> from a built <see cref="NightWindow"/>
        /// by converting its astronomical-dusk instant to <paramref name="zone"/>
        /// and taking the local calendar date. Returns <c>default</c> for invalid
        /// windows.
        /// </summary>
        public static NightDate Of(NightWindow night, TimeZoneInfo zone)
        {
            if (!night.IsValid || zone == null) return default;
            DateTime localDusk = TimeZoneInfo.ConvertTimeFromUtc(night.AstronomicalDusk, zone);
            return new NightDate(DateOnly.FromDateTime(localDusk));
        }

        public override string ToString() => DuskDate.ToString("yyyy-MM-dd");
    }
}
