using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Nina
{
    // Loads Astronomy.Core Target objects from NINA sequence files (.json). A NINA target file
    // serializes a DeepSkyObjectContainer whose Target.InputCoordinates carries sexagesimal
    // RA/Dec plus a NegativeDec flag.
    //
    // Scope of discovery:
    //   - every .json in the root of the folder;
    //   - every .json recursively under subfolders EXCEPT those named "Calibration" (flats /
    //     darks holders, not imaging targets) or "Mosaics" (multi-pane composites that can't be
    //     represented as a single point target).
    public static class TargetLoader
    {
        private static readonly HashSet<string> ExcludedSubfolders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { "Calibration", "Mosaics" };

        public static List<Target> Load(string rootFolder, IProgress<(int Current, int Total)> progress)
        {
            var result = new List<Target>();
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                return result;

            List<string> files = EnumerateTargetFiles(rootFolder).ToList();
            progress?.Report((0, files.Count));

            int i = 0;
            foreach (string file in files)
            {
                i++;
                try
                {
                    Target t = ParseTargetFile(file);
                    if (t != null) result.Add(t);
                }
                catch (Exception ex)
                {
                    // Skip unparseable / malformed files but leave a diagnostic trail in
                    // tp.log. The previous bare catch dropped both the path and the reason
                    // on the floor, which made "target X missing from the list" an
                    // unsolvable mystery.
                    Log.Warn("TargetLoader: skipping '" + file + "'", ex);
                }
                progress?.Report((i, files.Count));
            }

            return result;
        }

        private static IEnumerable<string> EnumerateTargetFiles(string rootFolder)
        {
            foreach (string f in Directory.EnumerateFiles(rootFolder, "*.json", SearchOption.TopDirectoryOnly))
                yield return f;

            foreach (string dir in Directory.EnumerateDirectories(rootFolder))
            {
                string name = Path.GetFileName(dir);
                if (ExcludedSubfolders.Contains(name)) continue;

                foreach (string f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
                    yield return f;
            }
        }

        private static Target ParseTargetFile(string path)
        {
            string text = File.ReadAllText(path);
            JObject root = JObject.Parse(text);

            JToken targetObj = root["Target"];
            if (targetObj == null) return null;

            string name = (string)targetObj["TargetName"];
            if (string.IsNullOrWhiteSpace(name)) return null;

            JToken coords = targetObj["InputCoordinates"];
            if (coords == null) return null;

            double raHours = 0.0;
            if (coords["RAHours"]   != null) raHours += (double)coords["RAHours"];
            if (coords["RAMinutes"] != null) raHours += (double)coords["RAMinutes"] / 60.0;
            if (coords["RASeconds"] != null) raHours += (double)coords["RASeconds"] / 3600.0;

            double decDegrees = 0.0;
            if (coords["DecDegrees"] != null) decDegrees += (double)coords["DecDegrees"];
            if (coords["DecMinutes"] != null) decDegrees += (double)coords["DecMinutes"] / 60.0;
            if (coords["DecSeconds"] != null) decDegrees += (double)coords["DecSeconds"] / 3600.0;

            bool negative = coords["NegativeDec"] != null && (bool)coords["NegativeDec"];
            if (negative) decDegrees = -decDegrees;

            // The Target ctor normalizes a negative declination into (magnitude, north=false).
            return new Target(
                name:           name,
                rightAscension: Math.Round(raHours, 6),
                declination:    Math.Round(decDegrees, 6),
                north:          true,
                directory:      path,
                enabled:        true);
        }
    }
}
