using Newtonsoft.Json.Linq;
using System;
using System.IO;
using TargetPlanner.Support;
using TargetPlanner.Targets;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Nina
{
    // Parses a single NINA sequence file (.json) into one bare Astronomy.Core
    // Target. A NINA target file serializes a DeepSkyObjectContainer whose
    // Target.InputCoordinates carries sexagesimal RA/Dec plus a NegativeDec flag.
    //
    // Parsing only -- this class finds nothing on its own. The recursive
    // directory walk that feeds it lives in TargetPlanner.Targets.TargetScanner;
    // collapsing the duplicates a walk turns up lives in TargetIdentity.
    public static class TargetLoader
    {
        // Parses one NINA .json sequence file into a Target, or null when the
        // file is missing, unparseable, or carries no target (each logged).
        // Never throws -- TargetScanner parses many files and one bad file must
        // not abort the batch. The target name is canonicalized via
        // TargetIdentity.NormalizeName so a " Stars" sequence collapses onto its
        // parent target downstream.
        public static Target ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                return ParseTargetFile(path);
            }
            catch (Exception ex)
            {
                // Skip unparseable / malformed files but leave a diagnostic trail
                // in tp.log. A bare swallow drops both the path and the reason,
                // which makes "target X missing from the list" an unsolvable
                // mystery.
                Log.Warn("TargetLoader.ParseFile: skipping '" + path + "'", ex);
                return null;
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
                name:           TargetIdentity.NormalizeName(name),
                rightAscension: Math.Round(raHours, 6),
                declination:    Math.Round(decDegrees, 6),
                north:          true,
                directory:      path,
                enabled:        true);
        }
    }
}
