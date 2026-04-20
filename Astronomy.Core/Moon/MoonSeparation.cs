using CoordinateSharp;
using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Moon
{
    public static class MoonSeparation
    {
        private static readonly EagerLoad mEagerLoad = EagerLoad.Create(EagerLoadType.Celestial);

        // Topocentric angular separation (degrees) between the target and the Moon at the given
        // UTC instant, as seen from the observer location. This is the number that actually
        // governs moon-contamination in an image -- geocentric separation is only a proxy.
        //
        // Composes Core's target Alt/Az with CoordinateSharp's moon Alt/Az via the spherical
        // law of cosines. Result is always in [0, 180].
        public static double DegreesAt(Target target, Location location, DateTime utc)
        {
            var targetAltAz = AltAz.At(target, location, utc);
            double tAlt = targetAltAz.Item1;
            double tAz  = targetAltAz.Item2;

            double LatSign  = location.North ?  1.0 : -1.0;
            double LongSign = location.West  ? -1.0 :  1.0;

            // Pass the UTC instant straight through with utcOffset = 0 hours; CoordinateSharp
            // treats the DateTime argument as local to the offset so this effectively asks for
            // "celestial times at this UTC".
            Celestial c = Celestial.CalculateCelestialTimes(
                LatSign  * location.Latitude,
                LongSign * location.Longitude,
                utc, mEagerLoad, 0.0);

            double mAlt = c.MoonAltitude;
            double mAz  = c.MoonAzimuth;

            double t1  = tAlt * Math.PI / 180.0;
            double t2  = mAlt * Math.PI / 180.0;
            double da  = (tAz - mAz) * Math.PI / 180.0;

            double cosSep = Math.Sin(t1) * Math.Sin(t2) + Math.Cos(t1) * Math.Cos(t2) * Math.Cos(da);
            if (cosSep >  1.0) cosSep =  1.0;
            if (cosSep < -1.0) cosSep = -1.0;
            return Math.Acos(cosSep) * 180.0 / Math.PI;
        }

        // Contiguous UTC intervals during the night when the target-moon separation is at or
        // above minSepDeg. Samples at 10-minute granularity then linearly interpolates threshold
        // crossings between adjacent samples for a ~1-minute-accurate boundary. Returns an empty
        // list if the moon is below the threshold for the entire night (or the night itself is
        // empty / polar).
        public static IReadOnlyList<(DateTime Start, DateTime End)> IntervalsAboveDeg(
            Target target, Location location, NightWindow night, double minSepDeg)
        {
            var result = new List<(DateTime Start, DateTime End)>();
            if (night.AstronomicalDusk == DateTime.MinValue ||
                night.AstronomicalDawn == DateTime.MinValue) return result;

            DateTime startUtc = night.AstronomicalDusk.ToUniversalTime();
            DateTime endUtc   = night.AstronomicalDawn.ToUniversalTime();
            TimeSpan sampleSize = TimeSpan.FromMinutes(10);

            DateTime tPrev = startUtc;
            double sepPrev = DegreesAt(target, location, tPrev);
            bool abovePrev = sepPrev >= minSepDeg;
            DateTime? currentStart = abovePrev ? (DateTime?)tPrev : null;

            DateTime tCur = startUtc.Add(sampleSize);
            while (tCur <= endUtc)
            {
                double sepCur = DegreesAt(target, location, tCur);
                bool aboveCur = sepCur >= minSepDeg;

                if (abovePrev != aboveCur)
                {
                    // Interpolate the exact crossing between tPrev and tCur.
                    double frac = (minSepDeg - sepPrev) / (sepCur - sepPrev);
                    DateTime crossing = tPrev.AddTicks((long)(frac * (tCur - tPrev).Ticks));
                    if (aboveCur)
                    {
                        currentStart = crossing;
                    }
                    else if (currentStart.HasValue)
                    {
                        result.Add((currentStart.Value, crossing));
                        currentStart = null;
                    }
                }

                tPrev = tCur;
                sepPrev = sepCur;
                abovePrev = aboveCur;
                tCur = tCur.Add(sampleSize);
            }

            if (currentStart.HasValue)
            {
                result.Add((currentStart.Value, endUtc));
            }

            return result;
        }
    }
}
