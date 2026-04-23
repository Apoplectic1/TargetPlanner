using System;
using System.Collections.Generic;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using BenchmarkDotNet.Attributes;

namespace Astronomy.Core.Tests.Benchmarks
{
    // Two side-by-side implementations of "sample a stellar target's altitude at a uniform
    // time grid" to settle the keep-or-revert question on AltitudeCurve.cs (the Core helper
    // that was added and then reverted -- see git log around commit 9c94612).
    //
    //   PerMinuteAltAz : today's BuildDaySeries loop -- one AltAzCalculator.Of call per
    //                    sample, with a Location.With(dateTime: point) allocation each
    //                    iteration. Per-sample SiderealTime.Local recomputation.
    //
    //   LstAdvance     : the proposed Core helper's body -- one SiderealTime.Local at the
    //                    start, then linear LST advance (i * lstStepHours) per sample,
    //                    feeding TargetGeometry.AltitudeAtHourAngle directly. No per-sample
    //                    allocations past the result array.
    //
    // Both produce the same altitudes (GMST is linear in UT to well below chart resolution
    // over a single night); the benchmark answers which is faster in wall-clock and how much
    // less allocation the batched form does.
    //
    // [Params(Count)] covers a typical Day-chart night (~600 min), a longer winter night
    // (~1000 min), and a stress case (~6000 min / 100 hours) to amplify any per-iteration
    // overhead trend.
    [MemoryDiagnoser]
    public class AltitudeCurveBenchmark
    {
        [Params(600, 1000, 6000)]
        public int Count;

        private Target _target;
        private Location _location;
        private DateTime _startUtc;

        [GlobalSetup]
        public void Setup()
        {
            _target = Target.Default;
            _location = Location.Default;
            // Fixed UTC anchor -- keeps iterations comparable run-to-run.
            _startUtc = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
        }

        // Reproduction of AltitudeSeries.BuildDaySeries's previous minute-loop shape. Only
        // difference is we collect into a double[] instead of Chart.Points so BenchmarkDotNet's
        // iteration isn't polluted by WinForms allocations.
        [Benchmark(Baseline = true)]
        public double[] PerMinuteAltAz()
        {
            double[] altitudes = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                DateTime point = _startUtc.AddMinutes(i);
                AltAz pos = AltAzCalculator.Of(_target, _location.With(dateTime: point));
                altitudes[i] = pos.Altitude;
            }
            return altitudes;
        }

        // Exercises the actual Core production code path -- if a future refactor slows
        // AltitudeCurve.Sample down, this column surfaces the regression instead of an
        // inline copy that can drift from the shipped implementation.
        [Benchmark]
        public IReadOnlyList<double> LstAdvance()
        {
            return AltitudeCurve.Sample(
                _target, _location, _startUtc, TimeSpan.FromMinutes(1), Count);
        }
    }
}
