using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Samples a stellar target's altitude at a uniform time grid in a single pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Equivalent to calling <see cref="AltAzCalculator.At"/> once per grid point, but
    /// <see cref="SiderealTime.Local"/> is evaluated only at the grid's start and advanced
    /// linearly by a constant sidereal-per-solar step for each subsequent sample. GMST is
    /// linear in UT to well below arcsecond precision across a single night, so the
    /// linear-advance result matches per-sample re-evaluation to many decimal places; the
    /// difference is far below chart pixel resolution.
    /// </para>
    /// <para>
    /// Also saves the per-sample <c>Location.With(dateTime: ...)</c> allocation that a
    /// caller would otherwise do to feed <see cref="AltAzCalculator.Of"/> in a loop.
    /// Measured (<c>Astronomy.Core.Tests/Benchmarks/AltitudeCurveBenchmark</c>, net10.0,
    /// Release, 2026-04-23): ~2.6x faster than a per-minute
    /// <see cref="AltAzCalculator.Of"/> loop and ~11x less allocation at 600 / 1000 / 6000
    /// sample counts.
    /// </para>
    /// <para>
    /// No atmospheric refraction; the result matches the unrefracted altitude convention
    /// used elsewhere in <see cref="AltAz"/> and <see cref="TargetGeometry"/>.
    /// </para>
    /// </remarks>
    public static class AltitudeCurve
    {
        private const double SiderealHoursPerSolarDay = 24.06570982441908;

        /// <summary>
        /// Returns <paramref name="count"/> altitudes at <paramref name="step"/> spacing,
        /// starting at <paramref name="startUtc"/>. Index 0 is the altitude at
        /// <paramref name="startUtc"/>; index <c>i</c> is the altitude at
        /// <c>startUtc + i * step</c>.
        /// </summary>
        /// <param name="target">Target RA/Dec in the Core convention (unsigned + North flag).</param>
        /// <param name="location">Observer latitude/longitude in the Core convention.</param>
        /// <param name="startUtc">
        /// First sample instant. Must be <see cref="DateTimeKind.Utc"/> per the Core
        /// contract; callers converting from local wall-clock should use
        /// <c>DateTime.SpecifyKind(localDt, DateTimeKind.Local).ToUniversalTime()</c>.
        /// </param>
        /// <param name="step">Spacing between samples. Must be positive.</param>
        /// <param name="count">Number of samples. Must be &gt;= 0.</param>
        /// <returns>
        /// Altitudes in degrees above the mathematical horizon (unrefracted). For
        /// <paramref name="count"/> == 0 returns an empty list.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="step"/> is non-positive or <paramref name="count"/> is negative.
        /// </exception>
        public static IReadOnlyList<double> Sample(
            Target target, Location location, DateTime startUtc, TimeSpan step, int count)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (step <= TimeSpan.Zero)
                throw new ArgumentException("step must be positive", nameof(step));
            if (count < 0)
                throw new ArgumentException("count must be >= 0", nameof(count));
            if (count == 0) return Array.Empty<double>();

            double latSigned  = location.North ?  location.Latitude  : -location.Latitude;
            double decSigned  = target.North   ?  target.Declination : -target.Declination;
            double lonDegEast = location.West  ? -location.Longitude :  location.Longitude;
            double raHours    = target.RightAscension;

            double lstStart = SiderealTime.Local(startUtc, lonDegEast);
            // LST advances at the sidereal rate: one solar hour of UT elapses
            // SiderealHoursPerSolarDay / 24 sidereal hours of LST.
            double lstStepHours = step.TotalHours * SiderealHoursPerSolarDay / 24.0;

            double[] altitudes = new double[count];
            for (int i = 0; i < count; i++)
            {
                // Compute each LST independently from the start rather than accumulating
                // per-step, so the result is insensitive to step count. For ~1000 samples
                // the difference vs accumulation is cosmetic, but it avoids any question
                // about drift for larger grids (e.g. a full-year precompute).
                double lst = lstStart + i * lstStepHours;
                double ha = lst - raHours;
                altitudes[i] = TargetGeometry.AltitudeAtHourAngle(ha, latSigned, decSigned);
            }
            return altitudes;
        }
    }
}
