using System;

namespace Astronomy.Core.Horizons
{
    // Horizon altitude interpolated linearly between (azimuth, altitude) sample points.
    // Samples are sorted by azimuth on construction and wrapped cyclically at 360 degrees, so
    // AltitudeAt behaves correctly for any real azimuth input.
    //
    // Pairs well with NINA's CustomHorizon polyline and with manually-entered obstruction
    // sketches that aren't dense enough to warrant a full 360-sample table.
    public sealed class PolylineHorizonProfile : IHorizonProfile
    {
        private readonly double[] mAzimuths;     // sorted ascending, all in [0, 360)
        private readonly double[] mAltitudes;    // parallel to mAzimuths
        private readonly double   mMinAltitude;

        // Takes two parallel arrays. Azimuths are normalized into [0, 360) and the combined
        // list is sorted by azimuth; duplicates are tolerated and the last one wins. Must
        // contain at least one sample.
        public PolylineHorizonProfile(double[] azimuthsDeg, double[] altitudesDeg)
        {
            if (azimuthsDeg == null) throw new ArgumentNullException(nameof(azimuthsDeg));
            if (altitudesDeg == null) throw new ArgumentNullException(nameof(altitudesDeg));
            if (azimuthsDeg.Length != altitudesDeg.Length)
                throw new ArgumentException("azimuthsDeg and altitudesDeg must have the same length");
            if (azimuthsDeg.Length == 0)
                throw new ArgumentException("at least one sample required");

            int n = azimuthsDeg.Length;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            double[] normalizedAz = new double[n];
            for (int i = 0; i < n; i++) normalizedAz[i] = Wrap360(azimuthsDeg[i]);

            Array.Sort(order, (a, b) => normalizedAz[a].CompareTo(normalizedAz[b]));

            mAzimuths  = new double[n];
            mAltitudes = new double[n];
            double minAlt = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                mAzimuths[i]  = normalizedAz[order[i]];
                mAltitudes[i] = altitudesDeg[order[i]];
                if (mAltitudes[i] < minAlt) minAlt = mAltitudes[i];
            }
            mMinAltitude = minAlt;
        }

        public double AltitudeAt(double azimuthDeg)
        {
            double az = Wrap360(azimuthDeg);
            int n = mAzimuths.Length;
            if (n == 1) return mAltitudes[0];

            // Find the pair bracketing az, wrapping through 360 if needed.
            int hi = 0;
            while (hi < n && mAzimuths[hi] <= az) hi++;

            int aIdx, bIdx;
            double aAz, bAz;
            if (hi == 0)
            {
                // az is below the smallest sample -- wrap from the last sample around 360 back to the first.
                aIdx = n - 1;
                bIdx = 0;
                aAz  = mAzimuths[aIdx] - 360.0;
                bAz  = mAzimuths[bIdx];
            }
            else if (hi == n)
            {
                // az is at or above the largest sample -- wrap from the last sample forward through 360.
                aIdx = n - 1;
                bIdx = 0;
                aAz  = mAzimuths[aIdx];
                bAz  = mAzimuths[bIdx] + 360.0;
            }
            else
            {
                aIdx = hi - 1;
                bIdx = hi;
                aAz  = mAzimuths[aIdx];
                bAz  = mAzimuths[bIdx];
            }

            if (bAz == aAz) return mAltitudes[aIdx];
            double t = (az - aAz) / (bAz - aAz);
            return mAltitudes[aIdx] + t * (mAltitudes[bIdx] - mAltitudes[aIdx]);
        }

        public double MinAltitude => mMinAltitude;

        private static double Wrap360(double deg)
        {
            double w = deg % 360.0;
            if (w < 0) w += 360.0;
            return w;
        }
    }
}
