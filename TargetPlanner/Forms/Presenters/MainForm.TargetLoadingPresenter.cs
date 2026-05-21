using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Target-loading concern: every path that brings targets into the form --
    // the three Load/Browse button handlers, the image-library / NINA-.json /
    // type-detecting-browse orchestration, the never-throw pure-loader wrappers,
    // the fallback folder pickers, and the post-load chart-cache warmup. Split
    // out of MainForm.cs so the form file stays navigable; this is a partial-
    // class file split rather than a Presenter-object extraction -- same
    // rationale as SortPresenter / CoordinatePresenter: the methods orchestrate
    // the VM, the cache, several controls and mAppSettings, and constructor-
    // injecting all of that is more ceremony than the move is worth.
    //
    // Entry points: startup calls GetImageLibraryTargets(offerFallbackBrowse:
    // false) from InitializeDynamicControls; the three Button_*_Click handlers
    // are Designer-wired.
    public partial class MainForm
    {
        // Root folder the NINA target loader walks at startup and the Browse-Target-List
        // dialog opens to. Sourced from mAppSettings.NinaTargetsRoot (settings.json),
        // seeded from PersonalDefaults on first run. User can edit via Defaults > Edit.
        private string NinaTargetsRootPath => mAppSettings?.NinaTargetsRoot;

        // Root folder the image-library scanner walks on "Load Image Library".
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

        // One-off type-detecting browse: opens a folder-capable file dialog, works
        // out whether the chosen path is a .json/.xisf file or a NINA/image-library
        // directory, loads accordingly, and replaces the known-target set. Unlike
        // the Load buttons it does NOT persist the path and does NOT append the
        // local-targets sidecar -- a clean one-off view of exactly what was browsed.
        private async Task GetBrowsedTargets()
        {
            string path = PromptForFileOrFolder();
            if (string.IsNullOrEmpty(path)) return;
            Log.Diag("UI", $"Browse selected: {path}");

            UseWaitCursor = true;
            try
            {
                List<Target> loaded = await LoadBrowsedPathAsync(path);
                if (loaded.Count == 0)
                {
                    MessageBox.Show(
                        "No NINA .json or image-library targets were found at:\n\n" + path,
                        "Nothing to load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                mSelection.SetKnownTargets(loaded);
                StartCacheWarmup(loaded);
            }
            catch (OperationCanceledException) { /* form closing mid-load; expected */ }
            catch (Exception ex) { Log.Error("Browse load failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // Classifies a browsed path and loads targets from it. A file dispatches by
        // extension (.json -> one NINA target, .xisf -> one image-library target);
        // a directory is an image-library root when LooksLikeImageLibraryRoot
        // matches the <Catalog>/Captures/ convention, otherwise a NINA .json folder.
        private async Task<List<Target>> LoadBrowsedPathAsync(string path)
        {
            if (File.Exists(path))
            {
                string ext = Path.GetExtension(path);
                if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
                    return TargetPlanner.Nina.TargetLoader.LoadFile(path);
                if (string.Equals(ext, ".xisf", StringComparison.OrdinalIgnoreCase))
                    return await TargetPlanner.ImageLibrary.ImageLibraryLoader.LoadFileAsync(
                        path, mFormClosingCts.Token);
                return new List<Target>();
            }
            if (Directory.Exists(path))
            {
                return LooksLikeImageLibraryRoot(path)
                    ? await LoadImageLibraryAsync(path)
                    : await LoadNinaTargetsAsync(path);
            }
            return new List<Target>();
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

        // True when a directory looks like an image-library root: at least one
        // immediate child directory has a "Captures" subfolder -- the convention
        // ImageLibraryScanner walks (<Catalog>/Captures/<Camera>/<Filter>/).
        private static bool LooksLikeImageLibraryRoot(string dir)
        {
            try
            {
                foreach (string child in Directory.EnumerateDirectories(dir))
                {
                    if (Directory.Exists(Path.Combine(child, "Captures")))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"LooksLikeImageLibraryRoot: enumerate failed at '{dir}': {ex.Message}");
            }
            return false;
        }

        // Loads targets from the image library and replaces the known-target set.
        // offerFallbackBrowse=false on startup (a missing/empty/failed root logs
        // and boots empty); =true for the button press (fall back to a folder
        // browse whose result is persisted as the new ImageLibraryRoot).
        private async Task GetImageLibraryTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetImageLibraryTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            try
            {
                List<Target> loaded = await LoadImageLibraryAsync(ImageLibraryRootPath);
                if (loaded.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "Image library not found -- locate your image-library folder",
                        ImageLibraryRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        loaded = await LoadImageLibraryAsync(picked);
                        if (loaded.Count > 0)
                        {
                            mAppSettings.ImageLibraryRoot = picked;
                            SettingsStore.Save(mAppSettings);
                        }
                    }
                }
                mSelection.SetKnownTargets(loaded);
                StartCacheWarmup(loaded);
            }
            catch (OperationCanceledException) { /* form closing mid-scan; expected */ }
            catch (Exception ex) { Log.Error("GetImageLibraryTargets failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // Image-library scan that never throws (cancellation aside): returns an
        // empty list, logged, when the root is unset/missing or the scan fails.
        private async Task<List<Target>> LoadImageLibraryAsync(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                Log.Warn("LoadImageLibraryAsync: image library root is not set.");
                return new List<Target>();
            }
            try
            {
                return await TargetPlanner.ImageLibrary.ImageLibraryLoader.LoadAsync(
                    root, mFormClosingCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Image library scan failed at '{root}'", ex);
                return new List<Target>();
            }
        }

        // Loads NINA .json targets from NinaTargetsRoot and replaces the known-
        // target set -- the NINA-lens counterpart of GetImageLibraryTargets.
        // offerFallbackBrowse falls back to a folder browse whose result is
        // persisted as the new NinaTargetsRoot. Always a button action.
        private async Task GetJsonTargets(bool offerFallbackBrowse)
        {
            Log.Diag("UI", $"GetJsonTargets offerFallback={offerFallbackBrowse}");
            UseWaitCursor = true;
            try
            {
                List<Target> loaded = await LoadNinaTargetsAsync(NinaTargetsRootPath);
                if (loaded.Count == 0 && offerFallbackBrowse)
                {
                    string picked = PromptForFolder(
                        "No NINA targets found -- locate your NINA sequence-files folder",
                        NinaTargetsRootPath);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        loaded = await LoadNinaTargetsAsync(picked);
                        if (loaded.Count > 0)
                        {
                            mAppSettings.NinaTargetsRoot = picked;
                            SettingsStore.Save(mAppSettings);
                        }
                    }
                }
                // Locally-added targets are additive on top of NINA -- append them
                // so a reload doesn't wipe them (existing NINA-load behavior).
                foreach (Target lt in mLocalTargets) loaded.Add(lt);
                mSelection.SetKnownTargets(loaded);
                StartCacheWarmup(loaded);
            }
            catch (Exception ex) { Log.Error("GetJsonTargets failed", ex); }
            finally { UseWaitCursor = false; }
        }

        // NINA .json folder walk that never throws: returns an empty list, logged,
        // when the folder is unset/missing or the walk fails. The walk (file
        // enumeration + JSON parse) runs on a background thread.
        private async Task<List<Target>> LoadNinaTargetsAsync(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                Log.Warn("LoadNinaTargetsAsync: NINA targets root is not set.");
                return new List<Target>();
            }
            try
            {
                return await Task.Run(
                    () => TargetPlanner.Nina.TargetLoader.Load(folder, null));
            }
            catch (Exception ex)
            {
                Log.Error($"NINA target load failed at '{folder}'", ex);
                return new List<Target>();
            }
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
        // is best-effort warmup, not load-bearing. Shared by both target-load
        // paths (NINA .json via GetNinaTargets + image library).
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
