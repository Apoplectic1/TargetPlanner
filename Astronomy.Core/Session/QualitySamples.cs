using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    public static class QualitySamples
    {
        // Splits the night into contiguous slots of approximately slotSize (the last slot is
        // truncated if it would extend past dawn) and returns the average quality per solar
        // hour for each. The scheduler's interval solver consumes this grid to build its
        // assignment.
        //
        // QualityPerHour = integrated-quality-over-slot / slot-duration-in-solar-hours. So a
        // slot with quality=1 means the target spent that hour at a weighted altitude whose
        // altitudeQuality evaluated to 1 (e.g. zenith under q=sin(alt)).
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

            DateTime startUtc = night.AstronomicalDusk.ToUniversalTime();
            DateTime endUtc   = night.AstronomicalDawn.ToUniversalTime();

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
