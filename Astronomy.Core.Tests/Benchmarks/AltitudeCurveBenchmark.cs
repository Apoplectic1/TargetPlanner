using System;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;
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
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

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

        // Reproduction of AltitudeSeries.BuildDaySeries's minute-loop shape. Only difference
        // is we collect into a double[] instead of Chart.Points so BenchmarkDotNet's iteration
        // isn't polluted by WinForms allocations.
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

        // Body of the reverted AltitudeCurve.Sample. One SiderealTime.Local up front; each
        // subsequent LST is computed as lstStart + i * lstStepHours (rather than accumulating,
        // to stay insensitive to Count). Hour angle goes straight into AltitudeAtHourAngle --
        // no per-iteration Location allocation, no per-iteration SiderealTime recompute.
        [Benchmark]
        public double[] LstAdvance()
        {
            double latSigned  = _location.North ?  _location.Latitude  : -_location.Latitude;
            double decSigned  = _target.North   ?  _target.Declination : -_target.Declination;
            double lonDegEast = _location.West  ? -_location.Longitude :  _location.Longitude;
            double raHours    = _target.RightAscension;

            double lstStart = SiderealTime.Local(_startUtc, lonDegEast);
            double lstStepHours = 1.0 / 60.0 * SiderealHoursPerSolarDay / 24.0;

            double[] altitudes = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                double lst = lstStart + i * lstStepHours;
                double ha = lst - raHours;
                altitudes[i] = TargetGeometry.AltitudeAtHourAngle(ha, latSigned, decSigned);
            }
            return altitudes;
        }
    }
}
