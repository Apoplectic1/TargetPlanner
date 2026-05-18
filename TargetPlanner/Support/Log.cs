using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TargetPlanner.Support
{
    // Append-only diagnostic log at %APPDATA%\TargetPlanner\tp.log. Used to surface
    // silent-failure paths (filter auto-save, settings save, JSON corruption recovery,
    // stale-build catches) that would otherwise only emit Debug.WriteLine -- invisible in
    // shipped builds. Best-effort: any exception writing the log is itself swallowed so
    // logging failures cannot cascade into hard errors in the caller.
    //
    // Diagnostic channels (Diag) ride the same file but use category prefixes so log
    // consumers can grep / filter. Categories are toggled via the TP_DIAG environment
    // variable: comma-separated list of enabled categories, or "*" for all. In Debug
    // builds the default is "*"; in Release the default is empty (no diag overhead).
    // Common categories: "Coord" (ChartCoordinator pipeline), "Cache" (ChartCacheStore
    // EnsureAsync + Prepare paths), "Day" / "Sky" / "Year" / "Sessions" (per sub-chart
    // Render), "Render" (cross-sub-chart shared concerns).
    internal static class Log
    {
        private static readonly string sPath = ComputePath();
        private static readonly object sGate = new object();
        // null sentinel = "all categories enabled"; empty set = "none enabled".
        private static readonly HashSet<string> sEnabledCategories = ResolveEnabledCategories();

        // Exposed so clear-all-data can delete it alongside settings.json / filters.json.
        public static string FilePath => sPath;

        // Notes folder = the user-visible directory containing tp.log,
        // tp.log.prev, screenshots/, screenshots.prev/. Single delete cleans
        // every captured-observation artifact. Exposed for the Help -> Feedback
        // -> Open Notes Folder menu item and as the parent dir for the
        // UserObservationDialog screenshot save path.
        public static string NotesFolderPath => Path.GetDirectoryName(sPath);

        /// <summary>Rotate the current tp.log to tp.log.prev (overwriting any
        /// previous rotation) and start a new empty log. Also rotates the
        /// observation-screenshot directory: screenshots/ -> screenshots.prev/
        /// so prev-run PNG paths referenced in tp.log.prev still resolve, and
        /// the disk footprint stays bounded at one session back. Called once
        /// at app startup so each run's diag trail + screenshots are self-
        /// contained. Best-effort: any IO failure is silently swallowed
        /// (logging must not cascade into hard errors).</summary>
        public static void StartNewSession()
        {
            try
            {
                lock (sGate)
                {
                    string dir = Path.GetDirectoryName(sPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string prevPath = sPath + ".prev";
                    if (File.Exists(sPath))
                    {
                        if (File.Exists(prevPath)) File.Delete(prevPath);
                        File.Move(sPath, prevPath);
                    }
                    File.WriteAllText(sPath,
                        string.Format("{0:o} INFO new session{1}",
                            DateTime.UtcNow, Environment.NewLine));

                    // Rotate observation screenshots in the same shape as the
                    // log: screenshots/ -> screenshots.prev/. References in
                    // tp.log.prev still resolve via the .prev subdir; the
                    // active session writes to a fresh screenshots/ created
                    // on first save by UserObservationDialog.
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string shotsDir = Path.Combine(dir, "screenshots");
                        string shotsPrev = Path.Combine(dir, "screenshots.prev");
                        if (Directory.Exists(shotsDir))
                        {
                            if (Directory.Exists(shotsPrev))
                                Directory.Delete(shotsPrev, recursive: true);
                            Directory.Move(shotsDir, shotsPrev);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort -- a logging failure must never escalate.
            }
        }

        public static void Warn(string message)        => Append("WARN",  message, null);
        public static void Warn(string message, Exception ex)  => Append("WARN",  message, ex);
        public static void Error(string message)       => Append("ERROR", message, null);
        public static void Error(string message, Exception ex) => Append("ERROR", message, ex);

        /// <summary>True when <paramref name="category"/> is enabled. Cheap; call
        /// before constructing expensive log messages (string interpolation).</summary>
        public static bool IsDiagEnabled(string category)
            => sEnabledCategories == null
            || (category != null && sEnabledCategories.Contains(category));

        /// <summary>Append a diag-level line tagged with <paramref name="category"/>.
        /// No-op when the category isn't in the enabled set. Keep <paramref name="message"/>
        /// short and structured (key=value pairs) so grep filtering stays useful.</summary>
        public static void Diag(string category, string message)
        {
            if (!IsDiagEnabled(category)) return;
            Append("DIAG/" + category, message, null);
        }

        /// <summary>Write a USER_OBS_START line marking the moment the user
        /// opened the observation dialog. The dialog is modeless so the user
        /// can interact with the main UI while it stays open; the matching
        /// USER_OBS_END (or USER_OBS_CANCEL) line carries the same id so
        /// grep id=&lt;short&gt; surfaces the full investigation window
        /// (intervening UI / Coord / Cache / chart diag lines are
        /// chronologically bracketed by start/end).</summary>
        public static void UserObservationStart(string id)
        {
            string build = TryGetBuildVersion();
            Append("USER_OBS_START", string.Format("id={0} build={1}",
                id ?? string.Empty, build), null);
        }

/// <summary>Write the end of an observation window with the user's
        /// checklist + notes + auto-captured context + screenshot path.
        /// <paramref name="id"/> matches the prior USER_OBS_START.
        /// <paramref name="ctx"/> is the formatted state snapshot
        /// ("area=Day, date=..., n=44, H=30, ...") -- a free-form string the
        /// caller assembles. <paramref name="screenshotPath"/> is the absolute
        /// path to the saved screenshot PNG (empty when capture failed).
        /// Newlines / quotes in notes are escaped so the line stays
        /// grep-friendly (one observation = one line).</summary>
        public static void UserObservationEnd(string id, string ctx, string checkedItems,
            string notes, string screenshotPath)
        {
            string escapedNotes = (notes ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\"", "\\\"");
            string body = string.Format(
                "id={0} ctx=({1}) screenshot={2} checked=[{3}] notes=\"{4}\"",
                id ?? string.Empty, ctx ?? string.Empty,
                screenshotPath ?? string.Empty, checkedItems ?? string.Empty,
                escapedNotes);
            Append("USER_OBS_END", body, null);
        }

        /// <summary>Write a USER_OBS_CANCEL line for an observation window
        /// the user abandoned (Cancel button or close-X). Every START gets
        /// a matching END or CANCEL so the log is symmetrical.</summary>
        public static void UserObservationCancel(string id)
        {
            Append("USER_OBS_CANCEL", "id=" + (id ?? string.Empty), null);
        }

        private static void Append(string level, string message, Exception ex)
        {
            try
            {
                lock (sGate)
                {
                    string dir = Path.GetDirectoryName(sPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string body = ex == null
                        ? string.Format("{0:o} {1} {2}{3}", DateTime.UtcNow, level, message, Environment.NewLine)
                        : string.Format("{0:o} {1} {2}: {3}{4}", DateTime.UtcNow, level, message, ex, Environment.NewLine);
                    File.AppendAllText(sPath, body);
                }
            }
            catch
            {
                // Best-effort -- a logging failure must never escalate.
            }
        }

        private static string ComputePath()
        {
            // Place tp.log + screenshots/ under a Logs/ subfolder so a single
            // delete clears every captured-observation artifact. settings.json
            // and other per-app state stay at the TargetPlanner/ root and are
            // unaffected.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TargetPlanner", "Logs", "tp.log");
        }

        // Reads AssemblyInformationalVersion (MinVer-stamped tag + git hash)
        // from the entry assembly. Falls back to AssemblyVersion or "unknown"
        // so logging never throws when the attribute isn't present (e.g. in
        // unit tests or design-time scenarios).
        private static string TryGetBuildVersion()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly();
                string infoVer = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVer)) return infoVer;
                return asm?.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static HashSet<string> ResolveEnabledCategories()
        {
            string env = Environment.GetEnvironmentVariable("TP_DIAG");
            if (env != null)
            {
                string trimmed = env.Trim();
                if (trimmed == "*") return null;  // null sentinel = all
                if (trimmed.Length == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return new HashSet<string>(
                    trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
            }
#if DEBUG
            return null;  // default in Debug: all categories enabled
#else
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);  // default in Release: off
#endif
        }
    }
}
