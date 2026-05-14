using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Astronomy.Core.Horizons;
using TargetPlanner.Support;

namespace TargetPlanner.Horizons
{
    /// <summary>
    /// Parses NINA's two-column whitespace <c>.hrz</c> local-horizon format into a
    /// <see cref="PolylineHorizonProfile"/>. Each non-blank, non-comment line carries
    /// <c>azimuth_deg &lt;ws&gt; altitude_deg</c>; comment lines start with <c>#</c>.
    /// </summary>
    /// <remarks>
    /// Best-effort: any parse / IO failure is logged to <c>tp.log</c> and the method
    /// returns <see langword="null"/>, letting the caller fall back to the scalar
    /// <see cref="ScalarHorizonProfile"/> path. Used by the site-pick + FileSystemWatcher
    /// hot-reload flow in MainForm to materialize <c>NamedLocationSetting.LocalHorizonPath</c>
    /// into a profile that flows through <c>PlanningPolicy.LocalHorizon</c>.
    /// </remarks>
    internal static class HrzFileLoader
    {
        public static PolylineHorizonProfile Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (!File.Exists(path))
            {
                Log.Warn("HrzFileLoader.Load: file does not exist: " + path);
                return null;
            }

            try
            {
                var azimuths = new List<double>();
                var altitudes = new List<double>();
                var inv = CultureInfo.InvariantCulture;
                int lineNo = 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    lineNo++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    string[] tokens = line.Split(
                        (char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2)
                    {
                        Log.Warn(
                            "HrzFileLoader.Load: skipping malformed line " + lineNo +
                            " (need two whitespace-separated values): " + raw);
                        continue;
                    }

                    if (!double.TryParse(tokens[0], NumberStyles.Float, inv, out double az) ||
                        !double.TryParse(tokens[1], NumberStyles.Float, inv, out double alt))
                    {
                        Log.Warn(
                            "HrzFileLoader.Load: skipping unparseable line " + lineNo +
                            " in '" + path + "': " + raw);
                        continue;
                    }

                    azimuths.Add(az);
                    altitudes.Add(alt);
                }

                if (azimuths.Count == 0)
                {
                    Log.Warn("HrzFileLoader.Load: no usable samples in: " + path);
                    return null;
                }

                return new PolylineHorizonProfile(azimuths.ToArray(), altitudes.ToArray());
            }
            catch (Exception ex)
            {
                Log.Warn("HrzFileLoader.Load: failed to read '" + path + "'", ex);
                return null;
            }
        }
    }
}
