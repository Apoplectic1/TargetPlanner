using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Builds a slot-by-slot quality grid across the night, feeding a scheduler's interval
    /// solver.
    /// </summary>
    public static class QualitySamples
    {
        /// <summary>
        /// Splits the night into contiguous slots of approximately <paramref name="slotSize"/>
        /// (the last slot is truncated if it would extend past dawn) and returns the average
        /// quality per solar hour for each slot.
        /// </summary>
        /// <remarks>
        /// <c>QualityPerHour = integrated-quality-over-slot / slot-duration-in-solar-hours</c>.
        /// A slot with quality=1 means the target spent that hour at a weighted altitude
        /// whose <paramref name="altitudeQuality"/> evaluated to 1 (e.g. zenith under
        /// <c>q = sin(alt)</c>).
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Night-window bounds (Kind=Utc). Must be <see cref="NightWindow.IsValid"/>; an invalid window returns an empty list.</param>
        /// <param name="slotSize">Slot width. Must be positive.</param>
        /// <param name="altitudeQuality">See <see cref="IntegratedQuality.OverSession"/>.</param>
        /// <returns>
        /// One tuple per slot. <c>Start</c> / <c>End</c> are <see cref="DateTimeKind.Utc"/>.
        /// Empty list if the night window is invalid (polar day / polar night).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="altitudeQuality"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="slotSize"/> is not positive.
        /// </exception>
        public static IReadOnlyList<(DateTime Start, DateTime End, double QualityPerHour)> OverNight(
            Target target, Location location, NightWindow night,
            TimeSpan slotSize, Func<double, double> altitudeQuality)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (altitudeQuality == null) throw new ArgumentNullException(nameof(altitudeQuality));
            if (slotSize <= TimeSpan.Zero) throw new ArgumentException("slotSize must be positive");

            var result = new List<(DateTime Start, DateTime End, double QualityPerHour)>();

            if (!night.IsValid) return result;

            // NightWindow exposes AstronomicalDusk / AstronomicalDawn as Kind=Utc. See
            // NightCalculator for the offset-recovery rationale.
            DateTime startUtc = night.AstronomicalDusk;
            DateTime endUtc   = night.AstronomicalDawn;

            DateTime cursor = startUtc;
            while (cursor < endUtc)
            {
                DateTime slotEnd = cursor.Add(slotSize);
                if (slotEnd > endUtc) slotEnd = endUtc;
                TimeSpan slotLen = slotEnd - cursor;

                double integrated = IntegratedQuality.OverSession(
                    target, location, cursor, slotLen, altitudeQuality);
                double perHour = slotLen.TotalHours > 0 ? integrated / slotLen.TotalHours : 0.0;
                result.Add((cursor, slotEnd, perHour));

                cursor = slotEnd;
            }

            return result;
        }
    }
}
