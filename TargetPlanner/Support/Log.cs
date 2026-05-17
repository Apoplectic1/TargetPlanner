using System;
using System.Collections.Generic;
using System.IO;

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

        /// <summary>Rotate the current tp.log to tp.log.prev (overwriting any
        /// previous rotation) and start a new empty log. Called once at app
        /// startup so each run's diag trail is self-contained. One run back is
        /// preserved in tp.log.prev for postmortem on the previous session.
        /// Best-effort: any IO failure is silently swallowed (logging must not
        /// cascade into hard errors).</summary>
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
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "TargetPlanner", "tp.log");
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
