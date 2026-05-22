using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Recursively walks a file or directory and parses every target-bearing file
    // it finds into bare Astronomy.Core Targets. This is the format-agnostic
    // enumeration + dispatch layer: per-file parsing lives in TargetLoader
    // (.json) and ImageLibraryLoader (.xisf), de-duplication lives in
    // TargetIdentity; this class only finds files and routes each to its parser.
    //
    // The walk is error-tolerant -- a directory the process cannot read is logged
    // and skipped, the rest of the tree still scans -- and skips no directory by
    // name (calibration frames are excluded per-file by IMAGETYP instead). The
    // returned list is raw: one entry per accepted file, so an object imaged
    // through several filters appears many times. Collapsing those into one
    // target per object is the caller's job, via TargetIdentity.SelectNewTargets.
    //
    // All file I/O runs off the calling thread; callers await ScanAsync on the UI
    // thread and publish the result to the view-model there -- the await is the
    // marshalling seam, so no control is ever touched off-thread.
    public static class TargetScanner
    {
        // Scans <paramref name="path"/> -- a single file or a directory tree --
        // and returns one bare Target per accepted file, in a stable order.
        // Never throws for I/O problems inside the tree (logged and skipped); a
        // cancelled token still surfaces as OperationCanceledException.
        public static async Task<IReadOnlyList<Target>> ScanAsync(
            string path, TargetFileKinds kinds, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path) || kinds == TargetFileKinds.None)
                return Array.Empty<Target>();

            if (File.Exists(path))
            {
                Target single = MatchesKind(path, kinds)
                    ? await ParseOneAsync(path, ct).ConfigureAwait(false)
                    : null;
                return single == null ? Array.Empty<Target>() : new[] { single };
            }

            if (!Directory.Exists(path))
            {
                Log.Warn($"TargetScanner: path does not exist: '{path}'.");
                return Array.Empty<Target>();
            }

            // Enumeration + sort off-thread: a large library tree is thousands of
            // directory-metadata reads. Sorted so the first-occurrence-wins
            // collapse downstream is deterministic across runs.
            string[] files = await Task.Run(
                () => SafeEnumerateFiles(path, ct)
                        .Where(f => MatchesKind(f, kinds))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                ct).ConfigureAwait(false);

            // Parse in parallel into a fixed-index array so the result keeps the
            // sorted file order regardless of completion order.
            var parsed = new Target[files.Length];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, files.Length), ct,
                async (i, token) =>
                {
                    parsed[i] = await ParseOneAsync(files[i], token).ConfigureAwait(false);
                }).ConfigureAwait(false);

            var result = new List<Target>(files.Length);
            foreach (Target t in parsed)
                if (t != null) result.Add(t);

            Log.Diag("UI", $"TargetScanner: '{path}' kinds={kinds} -> "
                + $"{files.Length} file(s) matched, {result.Count} target(s) parsed.");
            return result;
        }

        // Dispatches one file to its format parser by extension. Both parsers are
        // never-throw (a malformed file logs + yields null); this returns null for
        // any extension that is not a recognised target format.
        private static Task<Target> ParseOneAsync(string file, CancellationToken ct)
        {
            string ext = Path.GetExtension(file);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(TargetLoader.ParseFile(file));
            if (ext.Equals(".xisf", StringComparison.OrdinalIgnoreCase))
                return ImageLibraryLoader.ParseFileAsync(file, ct);
            return Task.FromResult<Target>(null);
        }

        private static bool MatchesKind(string file, TargetFileKinds kinds)
        {
            string ext = Path.GetExtension(file);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return (kinds & TargetFileKinds.Json) != 0;
            if (ext.Equals(".xisf", StringComparison.OrdinalIgnoreCase))
                return (kinds & TargetFileKinds.Xisf) != 0;
            return false;
        }

        // Depth-first file walk that survives unreadable directories. The stock
        // Directory.EnumerateFiles(..., AllDirectories) aborts the whole walk on
        // the first UnauthorizedAccessException; this catches per-directory so one
        // locked folder doesn't lose the rest of the tree. No directory is skipped
        // by name -- per-file IMAGETYP filtering excludes calibration frames.
        private static IEnumerable<string> SafeEnumerateFiles(string root, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                // Push subdirectories in reverse so the walk pops them in name
                // order; the caller's OrderBy fixes the final ordering regardless.
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
