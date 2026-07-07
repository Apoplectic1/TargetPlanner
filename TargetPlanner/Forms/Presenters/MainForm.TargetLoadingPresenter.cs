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

        // Browse: an OpenFileDialog that returns either a multi-selection of
        // .json/.xisf files OR a single folder (navigate into it and click Open)
        // -- one OK yields files xor a folder, never both. Both go through
        // LoadFromPathsAsync. One-off -- the path is not persisted.
        private async Task GetBrowsedTargets()
        {
            string[] paths = PromptForFilesOrFolder();
            if (paths.Length == 0) return;
            Log.Diag("UI", $"Browse selected {paths.Length} path(s)");
            await LoadFromPathsAsync(paths);
        }

        // Shared core of Browse and drag-and-drop: scan each picked file or
        // folder (a folder recursively, both formats), then add the new targets.
        // A single shared IProgress threads through every path; if the user
        // selected multiple paths the bar resizes per path (each one reports its
        // own Total) -- acceptable since the per-path work is either a fast
        // single file or one folder scan.
        private async Task LoadFromPathsAsync(string[] paths)
        {
            UseWaitCursor = true;
            (int progGen, IProgress<(int Done, int Total)> prog) = BeginScanProgress();
            try
            {
                var combined = new List<Target>();
                foreach (string path in paths)
                    combined.AddRange(await ScanPathAsync(path, TargetFileKinds.All, prog));

                if (combined.Count == 0)
                {
                    MessageBox.Show(
                        "No NINA .json or .xisf targets were found in the "
                        + "selected file(s) / folder.",
                        "Nothing to load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddScannedTargets(combined);
            }
            catch (OperationCanceledException) { /* form closing mid-load; expected */ }
            catch (Exception ex) { Log.Error("Browse / drop load failed", ex); }
            finally
            {
                UseWaitCursor = false;
                FinishScanProgress(progGen);
            }
        }

        // Explorer file-drop onto the target list -- the same operation as
        // Browse, just a different way to hand over the files / folders.
        private async Task GetDroppedTargets(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            Log.Diag("UI", $"Targets dropped: {paths.Length} path(s)");
            await LoadFromPathsAsync(paths);
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
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                    await GetDroppedTargets(paths);
            }
            catch (Exception ex)
            {
                Log.Error("OnTargetListDragDrop threw", ex);
            }
        }

        // Scans the configured image-library root for .xisf targets and adds the
        // new ones. offerFallbackBrowse=false on startup (a missing/empty/failed
        // root just logs and adds nothing); =true for the button press (fall back
        // to a folder browse whose result is persisted as the new root).
        private async Task GetImageLibraryTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetImageLibraryTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            (int progGen, IProgress<(int Done, int Total)> prog) = BeginScanProgress();
            try
            {
                IReadOnlyList<Target> scanned =
                    await ScanPathAsync(ImageLibraryRootPath, TargetFileKinds.Xisf, prog);
                if (scanned.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "Image library not found -- locate your image-library folder",
                        ImageLibraryRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        scanned = await ScanPathAsync(picked, TargetFileKinds.Xisf, prog);
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
            finally
            {
                UseWaitCursor = false;
                FinishScanProgress(progGen);
            }
        }

        // Scans the configured NINA targets root for .json targets and adds the
        // new ones -- the NINA-lens counterpart of GetImageLibraryTargets.
        // offerFallbackBrowse falls back to a folder browse whose result is
        // persisted as the new NinaTargetsRoot.
        private async Task GetJsonTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetJsonTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            (int progGen, IProgress<(int Done, int Total)> prog) = BeginScanProgress();
            try
            {
                IReadOnlyList<Target> scanned =
                    await ScanPathAsync(NinaTargetsRootPath, TargetFileKinds.Json, prog);
                if (scanned.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "No NINA targets found -- locate your NINA sequence-files folder",
                        NinaTargetsRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        scanned = await ScanPathAsync(picked, TargetFileKinds.Json, prog);
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
            finally
            {
                UseWaitCursor = false;
                FinishScanProgress(progGen);
            }
        }

        // Scan that never throws (cancellation aside): returns an empty list,
        // logged, when the path is unset or the scan fails. TargetScanner already
        // tolerates per-directory I/O errors inside the tree; this guards the
        // outer call. <paramref name="progress"/> is forwarded to the scanner
        // for per-file ticking on ProgressBar_Processing.
        private async Task<IReadOnlyList<Target>> ScanPathAsync(
            string path, TargetFileKinds kinds,
            IProgress<(int Done, int Total)> progress = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Log.Warn($"ScanPathAsync: no path supplied (kinds={kinds}).");
                return Array.Empty<Target>();
            }
            try
            {
                return await TargetScanner.ScanAsync(
                    path, kinds, mFormClosingCts.Token, progress);
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

        // Browse picker: a multi-select OpenFileDialog. The user picks one or
        // many .json/.xisf files, XOR navigates into a folder and clicks Open to
        // take that folder -- a single OK yields files or a folder, never both.
        // Returns the picked paths, or an empty array if cancelled.
        private string[] PromptForFilesOrFolder()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select target files, or open a folder to scan",
                Filter = "Target files (*.json;*.xisf)|*.json;*.xisf|All files (*.*)|*.*",
                InitialDirectory = ImageLibraryRootPath ?? string.Empty,
                Multiselect = true,
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "(select files, or open a folder)",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return Array.Empty<string>();

            string[] names = dialog.FileNames;
            // A single non-existent entry is the open-a-folder gesture: the dialog
            // hands back <folder>\<sentinel filename>, so the folder is what was
            // meant. Real file selections come back as themselves.
            if (names.Length == 1 && !File.Exists(names[0]) && !Directory.Exists(names[0]))
            {
                string dir = Path.GetDirectoryName(names[0]) ?? string.Empty;
                return Directory.Exists(dir) ? new[] { dir } : Array.Empty<string>();
            }
            return names;
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
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    await mCache.PrepareManyAsync(targets);
                    long yearDaysMs = sw.ElapsedMilliseconds;
                    await mCache.PrepareFitsAsync(targets, hdm);
                    long totalMs = sw.ElapsedMilliseconds;
                    if (Log.IsDiagEnabled("Cache"))
                    {
                        Log.Diag("Cache",
                            $"Warmup complete targets={targets.Count} " +
                            $"yearDaysMs={yearDaysMs} fitsMs={totalMs - yearDaysMs} totalMs={totalMs}");
                    }
                    // The warmup bypasses the coordinator's EnsureAsync seam, so
                    // the post-apply hook didn't fire. Marshal back to the UI
                    // thread and re-stamp the listbox painter's last-applied
                    // snapshot against the Hdm we actually built fits for --
                    // without this the 3-state checkbox tint stays SLATE for
                    // every row at boot because the painter looks up
                    // GetFitOrNull with the stale boot-Apply Hdm (F=(none)).
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke((Action)(() => RefreshAfterPostApply(warmupCtx)));
                    }
                }
            });
        }

        // Merge the current SelectedSingle (combo's resolved target -- could be a
        // NINA-known target or a transient one built from RA/Dec spinner edits) into
        // the checked set. Transient targets are added to KnownTargets and persisted
        // to the local-targets.json sidecar so they survive form-close + NINA reload.
        private void Button_AddTarget_Click(object sender, EventArgs e)
        {
            Target t = mSelection?.SelectedSingle;
            Log.Diag("UI", $"Button_AddTarget.Click target={t?.Name ?? "<null>"}");
            if (t == null) { ShowTransientMessage("No Target"); return; }

            bool wasNew = mSelection.AddKnownTarget(t);
            mSelection.SetChecked(t, true);
            if (wasNew)
            {
                mLocalTargets.Add(t);
                LocalTargetStore.Save(mLocalTargets);
            }

            // Re-sort listbox + combo by the current ComboBox_SortTargets selection
            // so the new target lands in its sorted position rather than wherever
            // PopulateCheckedListBoxFromTargets's first repopulate placed it.
            ResortSelectedTargets();

            // Keep the combo focused on the just-added target. ResortSelectedTargets
            // calls PopulateTargetComboFromTargets which preserves the prior text;
            // re-write it here in case the prior text had drifted (e.g. NINA reload
            // path reset combo to first sorted before this Add fired).
            bool wasUpdating = mUpdatingUiFromVm;
            mUpdatingUiFromVm = true;
            try { ComboBox_SelectTarget.Text = t.Name; }
            finally { mUpdatingUiFromVm = wasUpdating; }
        }

        // Remove the current SelectedSingle from KnownTargets entirely (combo +
        // listbox both lose the entry). NINA-loaded targets re-appear on the next
        // browse; locally-added targets are also dropped from the sidecar so they
        // stay gone across restarts.
        private void Button_RemoveTarget_Click(object sender, EventArgs e)
        {
            Target t = mSelection?.SelectedSingle;
            Log.Diag("UI", $"Button_RemoveTarget.Click target={t?.Name ?? "<null>"}");
            if (t == null) { ShowTransientMessage("No Target"); return; }

            bool wasInLocal = mLocalTargets.Remove(t);
            mSelection.RemoveKnownTarget(t);
            if (wasInLocal) LocalTargetStore.Save(mLocalTargets);

            // Re-sort listbox + combo by the current ComboBox_SortTargets selection
            // so the survivor list stays in canonical order after the deletion.
            ResortSelectedTargets();
        }
    }
}
