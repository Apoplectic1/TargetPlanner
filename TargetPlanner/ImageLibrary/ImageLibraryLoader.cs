using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.XISF;
using TargetPlanner.Support;
using TargetPlanner.Targets;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.ImageLibrary
{
    // Parses a single .xisf image file into one bare Astronomy.Core Target by
    // reading its FITS-keyword header (OBJECT / RA / DEC / IMAGETYP).
    //
    // Parsing only -- the recursive directory walk that feeds it lives in
    // TargetPlanner.Targets.TargetScanner, and collapsing the many per-filter /
    // stars / per-frame .xisf files of one object into a single target lives in
    // TargetIdentity. TP consumes targets as bare geometry (name + RA + Dec), so
    // the rich per-frame data (exposure, filter, camera) is dropped at this
    // boundary; surfacing it is deferred scheduler-era work.
    public static class ImageLibraryLoader
    {
        // Reads one .xisf header and builds a Target, or null when the file is
        // missing, not a light frame, lacks RA/DEC keywords, or fails to parse
        // (each logged). Never throws (cancellation aside) -- TargetScanner reads
        // thousands of headers and one bad file must not abort the batch.
        //
        // Only IMAGETYP=Light frames yield a target; darks / flats / bias are
        // skipped here rather than by directory name, so calibration frames are
        // excluded wherever they sit in the tree. The OBJECT keyword is the
        // complete target name (e.g. "M101 P1 Stars"); TargetIdentity.NormalizeName
        // strips the imaging-only " Stars" designation so a stars frame collapses
        // onto its parent target.
        public static async Task<Target> ParseFileAsync(string xisfPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(xisfPath) || !File.Exists(xisfPath))
                return null;
            try
            {
                XisfHeader header =
                    await XisfHeaderReader.ReadAsync(xisfPath, ct).ConfigureAwait(false);

                if (!IsLightFrame(header))
                    return null;

                double? raDeg = header.RaDegrees;
                double? decDeg = header.DecDegrees;
                if (raDeg is null || decDeg is null)
                {
                    Log.Warn($"ImageLibraryLoader: '{xisfPath}' has no RA/DEC header "
                        + "keyword; skipped.");
                    return null;
                }

                double raHours = (raDeg.Value / 15.0) % 24.0;
                if (raHours < 0) raHours += 24.0;

                string rawName = string.IsNullOrWhiteSpace(header.ObjectName)
                    ? Path.GetFileNameWithoutExtension(xisfPath)
                    : header.ObjectName;

                return new Target(
                    name:           TargetIdentity.NormalizeName(rawName),
                    rightAscension: Math.Round(raHours, 6),
                    declination:    Math.Round(decDeg.Value, 6),
                    north:          true,
                    directory:      xisfPath,
                    enabled:        true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"ImageLibraryLoader.ParseFileAsync: skipping '{xisfPath}'", ex);
                return null;
            }
        }

        // True when the header's IMAGETYP marks a light frame. An absent IMAGETYP
        // is treated as not-a-light-frame: the user's processed library always
        // stamps it, so a missing value means a file outside that pipeline, which
        // is safer to skip than to admit as a target.
        private static bool IsLightFrame(XisfHeader header)
        {
            string imageType = header.ImageType;
            return !string.IsNullOrWhiteSpace(imageType)
                && imageType.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
