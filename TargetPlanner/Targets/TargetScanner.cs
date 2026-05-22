using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TargetPlanner.ImageLibrary;
using TargetPlanner.Nina;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Targets
{
    // Which on-disk target formats a scan should pick up. The Load buttons pass
    // the single format they own; Browse and drag-drop pass All.
    [Flags]
    public enum TargetFileKinds
    {
        None = 0,
        Json = 1,   // NINA .json sequence files
        Xisf = 2,   // .xisf image files
        All  = Json | Xisf,
    }

    // Recursively walks a file or directory and produces one bare Astronomy.Core
    // Target per real sky target. Format-agnostic enumeration + grouping layer:
    // per-file parsing lives in TargetLoader (.json) and ImageLibraryLoader
    // (.xisf), the spherical centroid in SkyCentroid.
    //
    // A target is a GROUP of files collapsed to one Target whose coordinate is
    // the vector centroid of the group. That is what makes a target's dithered
    // capture frames -- whose per-frame plate-solved RA/DEC scatters over many
    // arcminutes -- and a mosaic's panels resolve to a single planning target:
    //
    //   .xisf            -> grouped by target folder (the folder above Captures/);
    //                       centroid over every IMAGETYP=Light frame beneath it.
    //                       A mosaic folder is one target, centroid across all
    //                       panels. .xisf outside a Captures/ tree (processing
    //                       outputs, loose files) is ignored in a directory scan;
    //                       a single-file scan keeps it.
    //   .json mosaic     -> a folder whose files are named "... Panel <n>";
    //                       centroid over the panels' planned coordinates.
    //   .json standalone -> one file, one target, its planned coordinate.
    //
    // Comets are excluded -- every comet folder / target is named "Comet ...".
    //
    // The walk is error-tolerant (an unreadable directory is logged and skipped)
    // and skips no directory by name. All file I/O runs off the calling thread;
    // callers await ScanAsync on the UI thread and publish the result there.
    public static class TargetScanner
    {
        // The capture-frames subtree under a target folder: <Target>/Captures/...
        private const string CapturesSegment = "Captures";

        // A .json file whose name carries this token is one panel of a NINA
        // mosaic (e.g. "Sh2 103 Panel 07.json"); all panels sharing a folder
        // centroid into one target.
        private static readonly Regex MosaicPanelPattern =
            new Regex(@"\bPanel\s*\d+\b", RegexOptions.IgnoreCase);

        // Scans <paramref name="path"/> -- a single file or a directory tree --
        // and returns one bare Target per real sky target. Never throws for I/O
        // problems inside the tree (logged and skipped); a cancelled token still
        // surfaces as OperationCanceledException.
        //
        // <paramref name="progress"/>, when supplied, receives one (Done=0, Total)
        // report after enumeration + per-kind filtering (extension / Captures /
        // comet drop), then one (Done++, Total) per file processed -- .xisf
        // header read or .json parse. The increment is Interlocked, so concurrent
        // parallel-parse ticks are race-free; the receiver should ignore stale
        // (out-of-order) Done values. The single-file branch does not report.
        public static async Task<IReadOnlyList<Target>> ScanAsync(
            string path, TargetFileKinds kinds, CancellationToken ct,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (string.IsNullOrWhiteSpace(path) || kinds == TargetFileKinds.None)
                return Array.Empty<Target>();

            if (File.Exists(path))
                return await ScanSingleFileAsync(path, kinds, ct).ConfigureAwait(false);

            if (!Directory.Exists(path))
            {
                Log.Warn($"TargetScanner: path does not exist: '{path}'.");
                return Array.Empty<Target>();
            }

            string[] files = await Task.Run(
                () => SafeEnumerateFiles(path, ct).ToArray(), ct).ConfigureAwait(false);

            // Pre-filter per kind so the work-unit total is known up front. The
            // .xisf side does its full pairing pass here (extension + Captures/
            // ancestor + comet drop) -- the heavy work (parallel header reads)
            // gets exactly the files that will become Targets. The .json side
            // just filters by extension; standalone/panel/comet decisions happen
            // during parsing.
            List<(string TargetFolder, string File)> xisfPaired =
                (kinds & TargetFileKinds.Xisf) != 0
                    ? await Task.Run(() => PairXisfFiles(files), ct).ConfigureAwait(false)
                    : null;
            string[] jsonFiles =
                (kinds & TargetFileKinds.Json) != 0
                    ? files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                           .ToArray()
                    : null;

            int total = (xisfPaired?.Count ?? 0) + (jsonFiles?.Length ?? 0);
            progress?.Report((0, total));

            int done = 0;
            void OnFileProcessed()
            {
                int d = Interlocked.Increment(ref done);
                progress?.Report((d, total));
            }

            var result = new List<Target>();
            if (xisfPaired != null)
                result.AddRange(await ScanXisfDirectoryAsync(xisfPaired, ct, OnFileProcessed)
                    .ConfigureAwait(false));
            if (jsonFiles != null)
                result.AddRange(await Task.Run(
                    () => ScanJsonFiles(jsonFiles, OnFileProcessed), ct).ConfigureAwait(false));

            Log.Diag("UI", $"TargetScanner: '{path}' kinds={kinds} -> "
                + $"{files.Length} file(s), {result.Count} target(s).");
            return result;
        }

        // A directly-picked file (Browse / drag-drop of one file) is its own
        // target -- there is no folder to group or centroid with. .xisf keeps
        // that one frame's coordinate; .json its planned coordinate. No progress
        // ticking -- the work is too small to be worth reporting.
        private static async Task<IReadOnlyList<Target>> ScanSingleFileAsync(
            string path, TargetFileKinds kinds, CancellationToken ct)
        {
            string ext = Path.GetExtension(path);
            Target t = null;
            if ((kinds & TargetFileKinds.Json) != 0
                && ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                t = TargetLoader.ParseFile(path);
            }
            else if ((kinds & TargetFileKinds.Xisf) != 0
                && ext.Equals(".xisf", StringComparison.OrdinalIgnoreCase))
            {
                t = await ImageLibraryLoader.ParseFileAsync(path, ct).ConfigureAwait(false);
            }
            return t == null || IsComet(t.Name)
                ? Array.Empty<Target>()
                : new[] { t };
        }

        // .xisf directory scan: read every paired frame's header in parallel,
        // then collapse each target folder's light frames to one Target at their
        // vector centroid. Pairing (extension + Captures/-ancestor + comet drop)
        // happens earlier in ScanAsync so the progress-tick total is known.
        private static async Task<IReadOnlyList<Target>> ScanXisfDirectoryAsync(
            IReadOnlyList<(string TargetFolder, string File)> paired,
            CancellationToken ct,
            Action onFileProcessed)
        {
            if (paired.Count == 0) return Array.Empty<Target>();

            var frames = new Target[paired.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, paired.Count), ct,
                async (i, token) =>
                {
                    frames[i] = await ImageLibraryLoader
                        .ParseFileAsync(paired[i].File, token).ConfigureAwait(false);
                    onFileProcessed?.Invoke();
                }).ConfigureAwait(false);

            // Collect each target folder's light-frame coordinates.
            var byTarget = new Dictionary<string, List<(double, double)>>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paired.Count; i++)
            {
                Target frame = frames[i];
                if (frame == null) continue;   // not a light frame, or no RA/DEC
                string tf = paired[i].TargetFolder;
                if (!byTarget.TryGetValue(tf, out var coords))
                    byTarget[tf] = coords = new List<(double, double)>();
                coords.Add((frame.RightAscension, SignedDec(frame)));
            }

            var result = new List<Target>(byTarget.Count);
            foreach (var kv in byTarget)
            {
                (double raHours, double decDeg) = SkyCentroid.Of(kv.Value);
                result.Add(new Target(
                    name:           Path.GetFileName(kv.Key),
                    rightAscension: raHours,
                    declination:    decDeg,
                    north:          true,
                    directory:      kv.Key,
                    enabled:        true));
            }
            return result;
        }

        // .json scan: a standalone .json is one target, unchanged; a folder of
        // "... Panel <n>" files is a NINA mosaic, collapsed to one target at the
        // centroid of the panels' planned coordinates.
        private static IReadOnlyList<Target> ScanJsonFiles(
            IList<string> jsonFiles, Action onFileProcessed)
        {
            var standalone = new List<Target>();
            var mosaicPanels = new Dictionary<string, List<Target>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string f in jsonFiles)
            {
                Target t = TargetLoader.ParseFile(f);
                onFileProcessed?.Invoke();
                if (t == null || IsComet(t.Name)) continue;

                if (MosaicPanelPattern.IsMatch(Path.GetFileNameWithoutExtension(f)))
                {
                    string folder = Path.GetDirectoryName(f) ?? string.Empty;
                    if (!mosaicPanels.TryGetValue(folder, out var panels))
                        mosaicPanels[folder] = panels = new List<Target>();
                    panels.Add(t);
                }
                else
                {
                    standalone.Add(t);
                }
            }

            var result = new List<Target>(standalone);
            foreach (var kv in mosaicPanels)
            {
                var coords = new List<(double, double)>(kv.Value.Count);
                foreach (Target panel in kv.Value)
                    coords.Add((panel.RightAscension, SignedDec(panel)));
                (double raHours, double decDeg) = SkyCentroid.Of(coords);
                result.Add(new Target(
                    name:           Path.GetFileName(kv.Key),
                    rightAscension: raHours,
                    declination:    decDeg,
                    north:          true,
                    directory:      kv.Key,
                    enabled:        true));
            }
            return result;
        }

        // Pairs every .xisf capture frame with its target folder (the path
        // segment above \Captures\). Drops files without a Captures/ ancestor
        // (processing outputs, loose files -- not capture frames) and comet
        // target folders, so their headers are never read. Pure string work --
        // safe to run on a worker thread.
        private static List<(string TargetFolder, string File)> PairXisfFiles(string[] files)
        {
            var p = new List<(string, string)>();
            foreach (string f in files)
            {
                if (!f.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase)) continue;
                string tf = CaptureTargetFolderOf(f);
                if (tf == null) continue;
                if (IsComet(Path.GetFileName(tf))) continue;
                p.Add((tf, f));
            }
            return p;
        }

        // The target folder of a capture frame: the path component immediately
        // above its \Captures\ segment. Null when the file is not inside a
        // Captures/ tree.
        private static string CaptureTargetFolderOf(string filePath)
        {
            string needle = Path.DirectorySeparatorChar + CapturesSegment
                + Path.DirectorySeparatorChar;
            int idx = filePath.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            return idx < 0 ? null : filePath.Substring(0, idx);
        }

        // True for a comet target -- every comet folder / target is named "Comet"
        // plus a designation. The trailing space keeps "Cometary ..." (a real
        // nebula class) from matching.
        private static bool IsComet(string name) =>
            name != null
            && name.TrimStart().StartsWith("Comet ", StringComparison.OrdinalIgnoreCase);

        // Declination as a signed value -- the Target store keeps a magnitude
        // plus a North flag; the centroid math needs the sign.
        private static double SignedDec(Target t) =>
            t.North ? t.Declination : -t.Declination;

        // Depth-first file walk that survives unreadable directories. The stock
        // Directory.EnumerateFiles(..., AllDirectories) aborts the whole walk on
        // the first UnauthorizedAccessException; this catches per-directory so one
        // locked folder doesn't lose the rest of the tree. No directory is skipped
        // by name.
        private static IEnumerable<string> SafeEnumerateFiles(string root, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                // Push subdirectories in reverse so the walk pops them in name
                // order; the result ordering does not otherwise matter (targets
                // are grouped by folder, not by file order).
                string[] subdirs = SafeGetEntries(dir, Directory.GetDirectories);
                for (int i = subdirs.Length - 1; i >= 0; i--)
                    stack.Push(subdirs[i]);

                foreach (string f in SafeGetEntries(dir, Directory.GetFiles))
                    yield return f;
            }
        }

        // One directory's entries (files or subdirectories), or an empty array
        // when the directory can't be read -- logged once, the walk continues.
        private static string[] SafeGetEntries(string dir, Func<string, string[]> getter)
        {
            try
            {
                return getter(dir);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                   or IOException
                   or DirectoryNotFoundException)
            {
                Log.Warn($"TargetScanner: skipping unreadable directory '{dir}': {ex.Message}");
                return Array.Empty<string>();
            }
        }
    }
}
