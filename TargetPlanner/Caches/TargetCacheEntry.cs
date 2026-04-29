using System;
using System.Collections.Generic;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-target year-of-night precomputes, owned by <see cref="ChartCacheStore"/>.
    /// </summary>
    /// <remarks>
    /// One entry per target at the current <see cref="Astronomy.Core.Locations.Location"/>.
    /// Built by <see cref="ChartCacheStore.GetOrBuildAsync"/> on a background thread;
    /// rendered by <see cref="TargetPlanner.Charts.AltitudeSeries"/> on the UI thread.
    /// Immutable from the consumer's perspective once published into the cache dictionary.
    /// </remarks>
    public sealed class TargetCacheEntry
    {
        /// <summary>The target this entry was built for.</summary>
        public Target Target { get; }

        /// <summary>The 365-day per-night precomputes; index aligns with the parent
        /// <see cref="Astronomy.Core.Night.NightCache.YearDays"/> indexing.</summary>
        public IReadOnlyList<NightCacheEntry> YearDays { get; }

        public TargetCacheEntry(Target target, IReadOnlyList<NightCacheEntry> yearDays)
        {
            Target   = target ?? throw new ArgumentNullException(nameof(target));
            YearDays = yearDays ?? throw new ArgumentNullException(nameof(yearDays));
        }
    }
}
