using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Targets;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Target-loading concern: every path that brings targets into the form --
    // the three Load/Browse button handlers, drag-and-drop onto the target list,
    // and the post-load chart-cache warmup. Each path recursively scans a file or
    // folder (TargetScanner), collapses the raw per-file rows into one entry per
    // object and drops anything already loaded (TargetIdentity.SelectNewTargets),
    // then ADDS the survivors to the known-target set -- loads accumulate, they
    // do not replace.
    //
    // Split out of MainForm.cs so the form file stays navigable; a partial-class
    // file split rather than a Presenter-object extraction -- the methods
    // orchestrate the VM, the cache, several controls and mAppSettings, and
    // constructor-injecting all of that is more ceremony than the move is worth.
    //
    // Entry points: startup calls GetImageLibraryTargets(offerFallbackBrowse:
    // false) from InitializeDynamicControls; the three Button_*_Click handlers
    // are Designer-wired; the target-list drag-drop events are wired in
    // InitializeDynamicControls.
    //
    // Threading: TargetScanner does all file enumeration + parsing off the UI
    // thread; the await resumes here on the UI thread, so the VM mutation
    // (AddKnownTargets) and the CheckedListBox repopulation it triggers are
    // always single-threaded -- no control is ever touched off-thread.
    public partial class MainForm
    {
        // Root folder the "Load NINA Sequencer Targets" button scans (.json only).
        // Sourced from mAppSettings.NinaTargetsRoot (settings.json), seeded from
        // PersonalDefaults on first run. User can edit via Defaults > Edit.
        private string NinaTargetsRootPath => mAppSettings?.NinaTargetsRoot;

        // Root folder the "Load Image Library Targets" button scans (.xisf only).
        // Sourced from mAppSettings.ImageLibraryRoot (settings.json), seeded from
        // PersonalDefaults on first run. User can edit via Defaults > Edit.
        private string ImageLibraryRootPath => mAppSettings?.ImageLibraryRoot;

        private void Button_BrowseTargetList_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_BrowseTargetList.Click");
            _ = GetBrowsedTargets();
        }

        private void Button_LoadImageLibrary_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_LoadImageLibrary.Click");
            // Scans the configured ImageLibraryRoot; if it yields nothing, falls
            // back to a folder browse whose result is persisted as the new root.
            _ = GetImageLibraryTargets(offerFallbackBrowse: true);
        }

        private void Button_LoadJsonTargets_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_LoadJsonTargets.Click");
            // Scans the configured NinaTargetsRoot; same fallback-browse-and-persist
            // behavior as the image-library button.
            _ = GetJsonTargets(offerFallbackBrowse: true);
        }

        // Browse: opens a folder-capable file dialog, recursively scans the chosen
        // file or directory for BOTH .json and .xisf, and adds whatever is new.
        // One-off -- it does not persist the path.
        private async Task GetBrowsedTargets()
        {
            string path = PromptForFileOrFolder();
            if (string.IsNullOrEmpty(path)) return;
            Log.Diag("UI", $"Browse selected: {path}");

            UseWaitCursor = true;
            try
            {
                IReadOnlyList<Target> scanned =
                    await ScanPathAsync(path, TargetFileKinds.All);
                if (scanned.Count == 0)
                {
                    MessageBox.Show(
                        "No NINA .json or .xisf targets were found at:\n\n" + path,
                        "Nothing to load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddScannedTargets(scanned);
            }
            catch (OperationCanceledException) { /* form closing mid-load; expected */ }
            catch (Exception ex) { Log.Error("Browse load failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // Loads targets from a set of dropped paths (Explorer file-drop onto the
        // target list). Behaves identically to Browse: each path is recursively
        // scanned for both .json and .xisf, the results combine, and only targets
        // not already loaded are added. One-off -- no persist.
        private async Task GetDroppedTargets(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            Log.Diag("UI", $"Targets dropped: {paths.Length} path(s)");

            UseWaitCursor = true;
            try
            {
                var combined = new List<Target>();
                foreach (string path in paths)
                    combined.AddRange(await ScanPathAsync(path, TargetFileKinds.All));

                if (combined.Count == 0)
                {
                    MessageBox.Show(
                        "Nothing loadable in the dropped item(s) -- expected NINA "
                        + ".json or .xisf files, or folders of them.",
                        "Nothing to load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddScannedTargets(combined);
            }
            catch (OperationCanceledException) { /* form closing mid-load; expected */ }
            catch (Exception ex) { Log.Error("Dropped-targets load failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // DragEnter on the target list: accept Explorer file-drops (the Copy
        // cursor is the drop affordance), reject anything else.
        private void OnTargetListDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        // DragDrop on the target list: pull the dropped file/folder paths and
        // load them.
        private async void OnTargetListDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                await GetDroppedTargets(paths);
        }

        // Scans the configured image-library root for .xisf targets and adds the
        // new ones. offerFallbackBrowse=false on startup (a missing/empty/failed
        // root just logs and adds nothing); =true for the button press (fall back
        // to a folder browse whose result is persisted as the new root).
        private async Task GetImageLibraryTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetImageLibraryTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            try
            {
                IReadOnlyList<Target> scanned =
                    await ScanPathAsync(ImageLibraryRootPath, TargetFileKinds.Xisf);
                if (scanned.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "Image library not found -- locate your image-library folder",
                        ImageLibraryRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        scanned = await ScanPathAsync(picked, TargetFileKinds.Xisf);
                        if (scanned.Count > 0)
                        {
                            mAppSettings.ImageLibraryRoot = picked;
                            SettingsStore.Save(mAppSettings);
                        }
                    }
                }
                AddScannedTargets(scanned);
            }
            catch (OperationCanceledException) { /* form closing mid-scan; expected */ }
            catch (Exception ex) { Log.Error("GetImageLibraryTargets failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // Scans the configured NINA targets root for .json targets and adds the
        // new ones -- the NINA-lens counterpart of GetImageLibraryTargets.
        // offerFallbackBrowse falls back to a folder browse whose result is
        // persisted as the new NinaTargetsRoot.
        private async Task GetJsonTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetJsonTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            try
            {
                IReadOnlyList<Target> scanned =
                    await ScanPathAsync(NinaTargetsRootPath, TargetFileKinds.Json);
                if (scanned.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "No NINA targets found -- locate your NINA sequence-files folder",
                        NinaTargetsRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        scanned = await ScanPathAsync(picked, TargetFileKinds.Json);
                        if (scanned.Count > 0)
                        {
                            mAppSettings.NinaTargetsRoot = picked;
                            SettingsStore.Save(mAppSettings);
                        }
                    }
                }
                AddScannedTargets(scanned);
            }
            catch (Exception ex) { Log.Error("GetJsonTargets failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // Scan that never throws (cancellation aside): returns an empty list,
        // logged, when the path is unset or the scan fails. TargetScanner already
        // tolerates per-directory I/O errors inside the tree; this guards the
        // outer call.
        private async Task<IReadOnlyList<Target>> ScanPathAsync(string path, TargetFileKinds kinds)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Log.Warn($"ScanPathAsync: no path supplied (kinds={kinds}).");
                return Array.Empty<Target>();
            }
            try
            {
                return await TargetScanner.ScanAsync(path, kinds, mFormClosingCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Target scan failed at '{path}'", ex);
                return Array.Empty<Target>();
            }
        }

        // The single funnel every load path ends in. Collapses the raw per-file
        // rows into one entry per object and drops anything KnownTargets already
        // holds (TargetIdentity.SelectNewTargets) -- the "don't add obvious
        // duplicates" rule -- then adds the survivors to the VM and warms the
        // chart cache for just those new targets. Runs on the UI thread (callers
        // await the scan first), so the AddKnownTargets -> OnVmKnownTargetsChanged
        // -> CheckedListBox repopulation is single-threaded.
        private void AddScannedTargets(IReadOnlyList<Target> scanned)
        {
            List<Target> toAdd =
                TargetIdentity.SelectNewTargets(scanned, mSelection.KnownTargets);
            if (toAdd.Count == 0) return;

            mSelection.AddKnownTargets(toAdd);
            StartCacheWarmup(toAdd);
        }

        // Folder-capable file picker: the user selects a .json/.xisf file, or
        // navigates into a directory and clicks Open (the dialog's relaxed
        // validation lets a folder come back as the path). Returns a real file or
        // directory path, or string.Empty if cancelled / unresolvable.
        private string PromptForFileOrFolder()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Browse to a target file or folder",
                Filter = "Target files (*.json;*.xisf)|*.json;*.xisf|All files (*.*)|*.*",
                InitialDirectory = ImageLibraryRootPath ?? string.Empty,
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "(pick a file, or open a folder)",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return string.Empty;

            string raw = dialog.FileName;
            if (File.Exists(raw) || Directory.Exists(raw)) return raw;
            // Folder-pick path: the user navigated into a folder, so FileName is
            // <folder>\<sentinel>; the containing directory is what they meant.
            string dir = Path.GetDirectoryName(raw) ?? string.Empty;
            return Directory.Exists(dir) ? dir : string.Empty;
        }

        // Folder picker shared by the Load buttons' fallback browse. Returns the
        // chosen directory, or string.Empty if the user cancelled.
        private string PromptForFolder(string description, string initialDir)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                InitialDirectory = initialDir ?? string.Empty,
                ShowNewFolderButton = false,
            };
            return dialog.ShowDialog(this) == DialogResult.OK
                ? dialog.SelectedPath
                : string.Empty;
        }

        // Kicks off background pre-population of the chart cache so subsequent
        // Graph clicks find caches already built. Fire-and-forget; a re-load just
        // starts a new warmup over the same target set -- the cache de-dupes per
        // target so already-built entries are no-ops. Errors are swallowed: this
        // is best-effort warmup, not load-bearing. Shared by every target-load
        // path -- image library, NINA, Browse, and drag-drop.
        //
        // Two phases: PrepareManyAsync builds the per-target yearDays, then
        // PrepareFitsAsync builds per-(target, HdmKey) fits against the current
        // H/D/M. Both run in one Task.Run so the second awaits the first; the
        // user's first Sessions / Year click hits a warm cache.
        private void StartCacheWarmup(List<Target> targets)
        {
            ChartContext warmupCtx = SnapshotCurrent(targets);
            HdmKey hdm = warmupCtx.Hdm;
            CancellationToken formCt = mFormClosingCts.Token;
            _ = Task.Run(async () =>
            {
                // Race the warmup against the form-closing signal so the awaiter
                // doesn't keep hold of the cache reference after the form has
                // started tearing down. The cache build itself isn't cancellable
                // and runs to completion regardless -- its publish-time stale
                // check makes that safe.
                try
                {
                    Task warmup = WarmupAsync();
                    Task cancelled = Task.Delay(Timeout.Infinite, formCt);
                    if (await Task.WhenAny(warmup, cancelled) == cancelled) return;
                    await warmup;  // observe any fault
                }
                catch (OperationCanceledException) { /* form closed mid-warmup; expected */ }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ChartCacheStore warmup failed: {ex}");
                }

                async Task WarmupAsync()
                {
                    await mCache.PrepareManyAsync(targets);
                    await mCache.PrepareFitsAsync(targets, hdm);
                }
            });
        }
    }
}
