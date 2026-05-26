using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using TargetPlanner.Support;
using Velopack;
using Velopack.Sources;

namespace TargetPlanner.Updates
{
    // Thin facade around Velopack's UpdateManager keyed at the project's GitHub Releases.
    // Owns two entry points:
    //   - CheckOnStartupAsync: silent on no-update / network failure; prompts on hit.
    //   - CheckManuallyAsync:  always reports a result (Help -> Check for Updates...).
    //
    // Both swallow exceptions and report via MessageBox so a transient network failure can't
    // crash the app. Manual path surfaces the failure to the user; startup path keeps quiet.
    internal static class UpdateService
    {
        private const string RepoUrl = "https://github.com/Apoplectic1/TargetPlanner";

        private static readonly UpdateManager Manager = new UpdateManager(
            new GithubSource(RepoUrl, accessToken: null, prerelease: false));

        public static async Task CheckOnStartupAsync(IWin32Window owner)
        {
            try
            {
                if (!Manager.IsInstalled) return;  // running from F5 / dev build, not via Setup.exe

                UpdateInfo updateInfo = await Manager.CheckForUpdatesAsync();
                if (updateInfo == null) return;

                if (PromptToInstall(owner, updateInfo) != DialogResult.Yes) return;

                await Manager.DownloadUpdatesAsync(updateInfo);
                Manager.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                // Startup path: silent to the user on any failure -- we don't want to greet
                // them with an error dialog every launch when their network is down. The
                // manual menu path surfaces failures explicitly when the user asks. Diagnostic
                // trail in tp.log so "update prompts never appear" is one grep away from a
                // root cause; user-facing silence requirement preserved.
                Log.Warn("UpdateService.CheckOnStartupAsync swallowed exception (silent by design)", ex);
            }
        }

        public static async Task CheckManuallyAsync(IWin32Window owner)
        {
            try
            {
                if (!Manager.IsInstalled)
                {
                    MessageBox.Show(owner,
                        "Update checks only run on the installed app (not when launched from Visual Studio).",
                        "TargetPlanner",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                UpdateInfo updateInfo = await Manager.CheckForUpdatesAsync();
                if (updateInfo == null)
                {
                    MessageBox.Show(owner,
                        "TargetPlanner is up to date.",
                        "TargetPlanner",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (PromptToInstall(owner, updateInfo) != DialogResult.Yes) return;

                await Manager.DownloadUpdatesAsync(updateInfo);
                Manager.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner,
                    "Update check failed:\n\n" + ex.Message,
                    "TargetPlanner",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static DialogResult PromptToInstall(IWin32Window owner, UpdateInfo updateInfo)
        {
            string version = updateInfo.TargetFullRelease.Version.ToString();
            return MessageBox.Show(owner,
                "TargetPlanner " + version + " is available.\n\nInstall now and restart?",
                "Update available",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }
    }
}
