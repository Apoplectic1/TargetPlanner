using System;
using System.IO;

namespace TargetPlanner.Support
{
    // Append-only diagnostic log at %APPDATA%\TargetPlanner\tp.log. Used to surface
    // silent-failure paths (filter auto-save, settings save, JSON corruption recovery,
    // stale-build catches) that would otherwise only emit Debug.WriteLine -- invisible in
    // shipped builds. Best-effort: any exception writing the log is itself swallowed so
    // logging failures cannot cascade into hard errors in the caller.
    internal static class Log
    {
        private static readonly string sPath = ComputePath();
        private static readonly object sGate = new object();

        public static void Warn(string message)        => Append("WARN",  message, null);
        public static void Warn(string message, Exception ex)  => Append("WARN",  message, ex);
        public static void Error(string message)       => Append("ERROR", message, null);
        public static void Error(string message, Exception ex) => Append("ERROR", message, ex);

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
    }
}
