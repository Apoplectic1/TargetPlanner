using System;
using System.Collections.Generic;

namespace Astronomy.Core.Horizons
{
    /// <summary>
    /// Obstruction table: dense set of <c>(azimuth, altitude)</c> readings that describe
    /// the user's local horizon as a full 360-degree sweep. Stepped interpretation -- each
    /// sample is the horizon altitude over the azimuth sector up to the next sample's
    /// azimuth, with wrap at 360.
    /// </summary>
    /// <remarks>
    /// Use <see cref="PolylineHorizonProfile"/> when linear interpolation between samples is
    /// more appropriate (e.g. smooth ridgelines); this one is better for discrete
    /// obstructions like trees and buildings whose edges are sharp.
    /// </remarks>
    public sealed class ObstructionTableHorizonProfile : IHorizonProfile
    {
        private readonly double[] mAzimuths;
        private readonly double[] mAltitudes;
        private readonly double   mMinAltitude;

        /// <summary>
        /// Builds a profile from a list of <c>(azimuth, altitude)</c> samples. Azimuths are
        /// normalized into <c>[0, 360)</c> and sorted on construction.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="samples"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="samples"/> is empty.
        /// </exception>
        public ObstructionTableHorizonProfile(IReadOnlyList<(double AzimuthDeg, double AltitudeDeg)> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0) throw new ArgumentException("at least one sample required");

            int n = samples.Count;
            double[] az = new double[n];
            double[] alt = new double[n];
            for (int i = 0; i < n; i++)
            {
                az[i] = Wrap360(samples[i].AzimuthDeg);
                alt[i] = samples[i].AltitudeDeg;
            }

            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => az[a].CompareTo(az[b]));

            mAzimuths  = new double[n];
            mAltitudes = new double[n];
            double minAlt = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                mAzimuths[i]  = az[order[i]];
                mAltitudes[i] = alt[order[i]];
                if (mAltitudes[i] < minAlt) minAlt = mAltitudes[i];
            }
            mMinAltitude = minAlt;
        }

        /// <inheritdoc />
        public double AltitudeAt(double azimuthDeg)
        {
            double a = Wrap360(azimuthDeg);
            int n = mAzimuths.Length;

            // Stepped: the sample at the largest azimuth <= a wins; if a < smallest sample,
            // wrap to the last sample's altitude (it covers the sector that crosses 0).
            int idx = -1;
            for (int i = 0; i < n; i++)
            {
                if (mAzimuths[i] <= a) idx = i;
                else break;
            }
            if (idx < 0) idx = n - 1;
            return mAltitudes[idx];
        }

        /// <inheritdoc />
        public double MinAltitude => mMinAltitude;

        private static double Wrap360(double deg)
        {
            double w = deg % 360.0;
            if (w < 0) w += 360.0;
            return w;
        }
    }
}
