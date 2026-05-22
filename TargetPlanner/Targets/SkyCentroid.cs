using System;
using System.Collections.Generic;

namespace TargetPlanner.Targets
{
    // Averages a set of sky coordinates the only way that is correct on a sphere:
    // convert each (RA, Dec) to a unit vector, sum the vectors, and convert the
    // mean direction back. A component-wise mean (or median) of RA fails across
    // the 0h/24h seam -- 23.9h and 0.1h would average to ~12h, the opposite side
    // of the sky -- because it treats an angle as a plain number. The vector form
    // has no seam and is well-behaved at the poles.
    //
    // Used to derive one stable target coordinate from the many dithered capture
    // frames of an .xisf target (whose per-frame plate-solved RA/DEC scatters
    // over many arcminutes), and from the panel coordinates of a NINA mosaic.
    public static class SkyCentroid
    {
        private const double DegToRad   = Math.PI / 180.0;
        private const double HoursToRad = Math.PI / 12.0;   // 15 deg per hour
        private const double RadToDeg   = 180.0 / Math.PI;
        private const double RadToHours = 12.0 / Math.PI;

        // The vector mean of <paramref name="coords"/> -- each element is
        // (right ascension in decimal hours, declination in signed decimal
        // degrees). Returns RA in [0, 24) hours and Dec in [-90, 90] degrees.
        // The list must be non-empty.
        public static (double RaHours, double DecDeg) Of(
            IReadOnlyList<(double RaHours, double DecDeg)> coords)
        {
            if (coords == null || coords.Count == 0)
                throw new ArgumentException("coords must be non-empty.", nameof(coords));

            double sx = 0.0, sy = 0.0, sz = 0.0;
            foreach ((double raHours, double decDeg) in coords)
            {
                double ra = raHours * HoursToRad;
                double dec = decDeg * DegToRad;
                double cosDec = Math.Cos(dec);
                sx += cosDec * Math.Cos(ra);
                sy += cosDec * Math.Sin(ra);
                sz += Math.Sin(dec);
            }

            // atan2 is scale-invariant, so summing the vectors is enough --
            // dividing by Count to get the true mean would not change the angles.
            double raRad = Math.Atan2(sy, sx);
            double decRad = Math.Atan2(sz, Math.Sqrt(sx * sx + sy * sy));

            double raHoursOut = raRad * RadToHours;
            if (raHoursOut < 0.0) raHoursOut += 24.0;
            return (raHoursOut, decRad * RadToDeg);
        }
    }
}
