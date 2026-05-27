using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using TargetPlanner.Filters;
using TargetPlanner.Forms;
using TargetPlanner.Settings;
using TargetPlanner.Support;
using TargetPlanner.Updates;

namespace TargetPlanner
{
    // App-meta menus concern: Help (Check for Updates / About / Open Notes
    // Folder) + File > Defaults (Edit settings.json / Clear factory reset).
    // One-shot click handlers with no chart state, no VM coupling -- they touch
    // Log, MessageBox, the file system, mAppSettings, and (for both Defaults
    // commands) mSuppressFormClosingSave + Application.Exit. Lifted out of
    // MainForm.cs -- partial-class file split, same pattern as the other
    // presenter partials.
    //
    // TryDeleteFile / TryDeleteDirectory are static helpers used only by
    // HandleClearDefaultsClick; they stay co-located with their single caller.
    public partial class MainForm
    {
        // Help -> Check for Updates... handler. Wired to CheckUpdatesToolStripMenuItem
        // in MainForm.Designer.cs.
        private async void OnCheckUpdatesClick(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                Log.Diag("UI", "Menu Help.CheckUpdates.Click");
                await UpdateService.CheckManuallyAsync(this);
            }
            catch (Exception ex)
            {
                Log.Error("OnCheckUpdatesClick threw", ex);
            }
        }

        // Help -> About TargetPlanner handler. Wired to AboutToolStripMenuItem in
        // MainForm.Designer.cs.
        private void OnAboutClick(object sender, EventArgs e)
        {
            Log.Diag("UI", "Menu Help.About.Click");
            using (var dlg = new AboutDialog())
                dlg.ShowDialog(this);
        }

        // Help -> Feedback -> Open Notes Folder. Ensures the Logs folder
        // exists (it doesn't until the first Log.Append fires after rotation)
        // so the user always gets a real Explorer window rather than a
        // path-not-found error. Process.Start with UseShellExecute=true
        // hands the path off to the OS shell, which opens the default folder
        // viewer (Explorer on Windows).
        private void HandleOpenNotesFolderClick()
        {
            Log.Diag("UI", "Menu Help.Feedback.OpenNotesFolder.Click");
            try
            {
                string path = Log.NotesFolderPath;
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Error("HandleOpenNotesFolderClick failed", ex);
                MessageBox.Show(this,
                    "Couldn't open the notes folder:\n\n" + ex.Message,
                    "Open Notes Folder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // File -> Defaults -> Edit settings.json. Closes TP after launching the
        // OS-default editor so an exit-time SettingsStore.Save can't clobber the
        // user's hand-edits.
        //
        // Sequence:
        //   1. Confirm prompt (cancellable -- user can back out before commitment).
        //   2. Flush current in-memory AppSettings to settings.json so the editor
        //      opens the freshest view of TP's state.
        //   3. Launch the editor (Process.Start UseShellExecute=true so Windows
        //      resolves the .json association).
        //   4. Set mSuppressFormClosingSave + Application.Exit. The user edits at
        //      leisure and relaunches TP to load their changes.
        private void HandleEditDefaultsClick()
        {
            Log.Diag("UI", "Menu File.Defaults.Edit.Click");
            DialogResult confirm = MessageBox.Show(this,
                "Open settings.json in your default editor?\n\n" +
                "TargetPlanner will close so your edits save cleanly.\n" +
                "Relaunch TargetPlanner when you're done editing.",
                "Edit settings.json",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
            if (confirm != DialogResult.OK) return;

            try
            {
                // Flush current in-memory state so the editor sees TP's latest
                // view -- e.g. a site swap since boot that hadn't been persisted
                // by a different code path. Defensive; most save call-sites
                // already keep settings.json current.
                SettingsStore.Save(mAppSettings);

                Process.Start(new ProcessStartInfo
                {
                    FileName = SettingsStore.FilePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Error("EditDefaults: failed to open '" + SettingsStore.FilePath + "'", ex);
                MessageBox.Show(this,
                    "Could not open the editor.\n\n" + ex.Message,
                    "Edit settings.json",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            mSuppressFormClosingSave = true;
            Application.Exit();
        }

        // File -> Defaults -> Clear (factory reset)... Confirms via YesNo, deletes
        // settings.json + filters.json + local-targets.json + the Logs/ directory
        // recursively, then exits the application so the next launch boots from
        // PersonalDefaults.BuildSeedSettings(). tp.log is part of Logs/ so it goes
        // last (the per-file deletes log their failures first). Exit is forced --
        // the user explicitly asked for a reset; reading old in-memory state
        // through subsequent saves would partially undo the wipe.
        private void HandleClearDefaultsClick()
        {
            Log.Diag("UI", "Menu File.Defaults.Clear.Click");
            string body =
                "Factory reset TargetPlanner?\n\n" +
                "This deletes:\n" +
                "  - " + SettingsStore.FilePath + "\n" +
                "  - " + FilterLibrary.DefaultPath + "\n" +
                "  - " + LocalTargetStore.FilePath + "\n" +
                "  - " + Log.NotesFolderPath + " (entire folder: tp.log + screenshots + .prev)\n\n" +
                "TargetPlanner will close after the reset; relaunch to boot from defaults.\n\n" +
                "This cannot be undone.";

            DialogResult confirm = MessageBox.Show(this, body, "Defaults: Clear (factory reset)",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            TryDeleteFile(SettingsStore.FilePath);
            TryDeleteFile(FilterLibrary.DefaultPath);
            TryDeleteFile(LocalTargetStore.FilePath);
            TryDeleteDirectory(Log.NotesFolderPath);

            // Confirm prompt above already told the user TP will close; skip a
            // second "Reset complete" dialog and just exit. Suppress flag stops
            // FormClosing from re-saving settings.json over the just-deleted one.
            mSuppressFormClosingSave = true;
            Application.Exit();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Error("ClearDefaults: failed to delete file '" + path + "'", ex);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Error("ClearDefaults: failed to delete directory '" + path + "'", ex);
            }
        }
    }
}
