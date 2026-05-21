using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Astronomy.NINA.Xisf;
using Astronomy.XISF;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.ImageLibrary
{
    // Loads Astronomy.Core Target objects from the user's image library by
    // scanning .xisf FITS headers via Astronomy.NINA's ImageLibraryScanner.
    //
    // TP today consumes targets as bare geometry (name + RA + Dec), exactly as it
    // does NINA .json targets, so this loader down-converts each scanned
    // TargetReport to a bare Astronomy.Core.Targets.Target at the boundary and
    // drops the rich per-filter imaging history. Surfacing that richness
    // (filters, integration time) is deferred TPP/TPS work -- see the Phase C
    // plan. The image library and NINA .json are independent target lenses;
    // each load wholesale-replaces the known-target set.
    public static class ImageLibraryLoader
    {
        // Scans the image library rooted at <paramref name="root"/> and returns
        // one bare Target per top-level target directory found. The scan reads
        // every .xisf header (~1.4 s for a 14k-frame library on the dev machine).
        public static async Task<List<Target>> LoadAsync(string root, CancellationToken ct)
        {
            ImageLibraryReport report =
                await ImageLibraryScanner.ScanAsync(root, ct).ConfigureAwait(false);

            var result = new List<Target>(report.Targets.Count);
            foreach (TargetReport tr in report.Targets)
            {
                // DecDegrees is signed; pass north:true and let the Target ctor
                // normalize a negative declination by flipping the flag (matches
                // the convention TargetLoader and ReportToTargetAdapter use). The
                // directory name ("M51 - Whirlpool") is the user's canonical
                // identity -- use it for both the display name and Directory.
                result.Add(new Target(
                    name:           tr.DirectoryName,
                    rightAscension: tr.RaHours,
                    declination:    tr.DecDegrees,
                    north:          true,
                    directory:      tr.DirectoryName,
                    enabled:        true));
            }

            if (report.SkippedFiles.Count > 0)
            {
                Log.Warn($"ImageLibraryLoader: {report.SkippedFiles.Count} file(s) "
                    + $"skipped during scan of '{root}' (XISF parse failures).");
            }

            return result;
        }

        // Reads a single .xisf file's FITS header and builds one bare Target from
        // it. Returns a 0-or-1-element list -- empty when the file is missing or
        // its header lacks RA/DEC (logged). The rich per-frame data is dropped at
        // the boundary, same as LoadAsync.
        public static async Task<List<Target>> LoadFileAsync(string xisfPath, CancellationToken ct)
        {
            var result = new List<Target>();
            if (string.IsNullOrWhiteSpace(xisfPath) || !File.Exists(xisfPath))
                return result;
            try
            {
                XisfHeader header =
                    await XisfHeaderReader.ReadAsync(xisfPath, ct).ConfigureAwait(false);
                double? raDeg = header.RaDegrees;
                double? decDeg = header.DecDegrees;
                if (raDeg is null || decDeg is null)
                {
                    Log.Warn($"ImageLibraryLoader.LoadFileAsync: '{xisfPath}' "
                        + "has no RA/DEC header keyword; skipped.");
                    return result;
                }
                double raHours = (raDeg.Value / 15.0) % 24.0;
                if (raHours < 0) raHours += 24.0;
                string name = string.IsNullOrWhiteSpace(header.ObjectName)
                    ? Path.GetFileNameWithoutExtension(xisfPath)
                    : header.ObjectName;
                result.Add(new Target(
                    name:           name,
                    rightAscension: raHours,
                    declination:    decDeg.Value,
                    north:          true,
                    directory:      xisfPath,
                    enabled:        true));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"ImageLibraryLoader.LoadFileAsync failed at '{xisfPath}'", ex);
            }
            return result;
        }
    }
}
