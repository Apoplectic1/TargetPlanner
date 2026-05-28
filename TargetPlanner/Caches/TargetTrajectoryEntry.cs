using System.Collections.Generic;
using Astronomy.Core.Session;
using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Caches
{
    /// <summary>
    /// Per-(target, <see cref="NightDate"/>) cached trajectory: minute-by-minute
    /// (Alt, Az) of a stellar target for the chart-visible window of one night.
    /// Independent of the H/D/M filter (geometry is policy-free), so cache hits
    /// across HDM scrubs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Index <c>i</c> is the sample at <c>Window.ChartStartUtc + i * 1 minute</c>;
    /// <see cref="Samples"/> has exactly <c>Window.Count</c> entries.
    /// </para>
    /// <para>
    /// Owned by <see cref="ChartCacheStore"/>; published immutable.
    /// </para>
    /// </remarks>
    public sealed class TargetTrajectoryEntry
    {
        public Target Target { get; }
        public NightDate Key { get; }
        public DayWindowKey Window { get; }
        public IReadOnlyList<AltAzSample> Samples { get; }

        public TargetTrajectoryEntry(
            Target target, NightDate key, DayWindowKey window,
            IReadOnlyList<AltAzSample> samples)
        {
            Target = target;
            Key = key;
            Window = window;
            Samples = samples;
        }
    }
}
