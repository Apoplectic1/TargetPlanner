using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Astronomy.Core.Horizons;
using Astronomy.NINA.Persistence;
using TargetPlanner.Horizons;
using TargetPlanner.Settings;
using TargetPlanner.State;
using TargetPlanner.Support;

using Location = Astronomy.Core.Locations.Location;
using Target   = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // Location + per-site preferences + local-horizon concern: every method
    // that observes or mutates mLocation, mPlanningPreferences,
    // mAppSettings.NamedLocations, or the per-site polyline horizon. Lifted
    // out of MainForm.cs as the seventh partial-class file split (after
    // Sort / Coordinate / FilterMenu / TargetLoading / SelectionVm /
    // ChartBuild).
    //
    // What's here:
    //   * Site-characteristic spinners -- LocalElevation / Bortle / Extinction
    //     / TimeZone / TargetDuration / TargetFloor handlers + the
    //     PersistPlanningPreferencesToActiveSite mirror back onto NamedSite.
    //   * Edit funnel + debounce -- OnLocationEdited (single attach point
    //     for lat/lon/elev/N/W/Bortle/Extinction edits) +
    //     RestartSessionsRebuildDebounce / SessionsRebuildDebounce_Tick (the
    //     250 ms scrub debounce that triggers either ResetForLocationChange
    //     or a coordinator Apply, based on LocationsCacheEquivalent).
    //   * Reset -- ResetForLocationChange clears the checked set, blanks the
    //     chart, drops + rebuilds the cache against the new location.
    //   * Location combo -- ComboBox_Location_SelectionIndexChanged +
    //     ComboBox_Location_DropDown (re-fire trick for re-picking the same
    //     item after a Custom auto-switch).
    //   * Startup pickers -- PickStartupLocation + PickStartupPreferences
    //     resolve initial state from LastSelectedLocationName.
    //   * Local-horizon polyline plumbing -- ApplySiteHorizon (entry from the
    //     location combo + OnLocationEdited's Custom flip), LoadLocalHorizon-
    //     ForCurrentLocation / GetCurrentHorizonPath, UpdateHorizonPathLabel,
    //     ConfigureHorizonWatcher + HorizonWatcher_FileChanged +
    //     HorizonReloadDebounce_Tick (the FileSystemWatcher hot-reload
    //     pipeline), Button_BrowseHorizon_Click (file picker for assigning a
    //     .hrz path to the active NamedSite), InitializeLocalHorizonControls
    //     (wires the lot at form init).
    //
    // Stays in MainForm.cs:
    //   * Fields (mLocation, mPlanningPreferences, mAppSettings, mLocalHorizon,
    //     mSessionsRebuildDebounce, mHorizonReloadDebounce, mHorizonWatcher,
    //     mSyncingLocationUI, and the debounce-interval consts).
    //   * SyncLocationUIFromModel + the lat/lon CoordinateInput callbacks
    //     (in MainForm.CoordinatePresenter.cs).
    //   * SnapshotCurrent (used here for the coordinator Apply calls).
    //   * RefreshAstrometryLabels (called from OnLocationEdited).
    //   * ClampToRange (shared across CoordinatePresenter + FilterMenu-
    //     Presenter + this presenter).
    public partial class MainForm
    {
        // Single-spinner control (no D/M/S triple) so we don't go through CoordinateInput;
        // route directly to OnLocationEdited so the combo flips to "Custom" and the cache
        // invalidation debounce restarts -- same path the lat/lon handlers take.
        private void NumericUpDown_LocalElevation_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"NumericUpDown_LocalElevation.ValueChanged value={NumericUpDown_LocalElevation.Value}");
            mLocation = mLocation.With(elevation: (double)NumericUpDown_LocalElevation.Value);
            OnLocationEdited(sender, e);
        }

        // Bortle dropdown change: update Location.BortleClass and overwrite the extinction
        // spinner with the typical k for the new class. The class also drives V₀ inside
        // K-S via Bortle.DefaultZenithMag at render time, so a Bortle change triggers a
        // Day-Sky rebuild via the OnLocationEdited debounce path.
        private void ComboBox_Bortle_SelectedIndexChanged(object sender, EventArgs e)
        {
            int b = ComboBox_Bortle.SelectedIndex + 1;  // index 0 -> Bortle 1
            if (b < 1 || b > 9) return;
            Log.Diag("UI", $"ComboBox_Bortle.SelectedIndexChanged class={b}");
            double k = Astronomy.Core.Brightness.Bortle.DefaultExtinctionK500(b);
            mLocation = mLocation.With(bortleClass: b, extinctionK: k);

            // Push the auto-filled k into the spinner without re-firing its handler.
            NumericUpDown_Extinction.ValueChanged -= NumericUpDown_Extinction_ValueChanged;
            NumericUpDown_Extinction.Value = ClampToRange(NumericUpDown_Extinction, (decimal)k);
            NumericUpDown_Extinction.ValueChanged += NumericUpDown_Extinction_ValueChanged;

            OnLocationEdited(sender, e);
        }

        // Extinction spinner edit: update Location.ExtinctionK directly. Bortle stays
        // wherever the user left it (manual override -- the user knows their site's k
        // better than the Bortle table does).
        private void NumericUpDown_Extinction_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"NumericUpDown_Extinction.ValueChanged value={NumericUpDown_Extinction.Value}");
            mLocation = mLocation.With(extinctionK: (double)NumericUpDown_Extinction.Value);
            OnLocationEdited(sender, e);
        }

        // TimeZone combo change: pull the resolved TimeZoneInfo straight from the
        // selected item and push it onto mLocation + mObservation. The combo's items
        // are TimeZoneInfo instances (bound at boot from TimeZoneInfo.GetSystemTimeZones)
        // so SelectedItem already carries DST-aware AdjustmentRules -- no resolver call
        // here. Like the other site-characteristic edits (Bortle / Extinction), routes
        // through OnLocationEdited so the location combo flips to "Custom" and the
        // cache invalidation debounce restarts. Programmatic syncs are gated by
        // mSyncingLocationUI.
        private void ComboBox_TimeZone_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mSyncingLocationUI) return;
            if (ComboBox_TimeZone.SelectedItem is not TimeZoneInfo zone) return;
            Log.Diag("UI", $"ComboBox_TimeZone.SelectedIndexChanged id={zone.Id}");
            mLocation = mLocation.With(timeZoneInfo: zone);
            // mObservation's Zone field is the same logical TZ as Location.TimeZoneInfo --
            // keep them in lockstep so downstream consumers reading either see a
            // consistent view. with-syntax preserves Utc; the wall-clock moment the
            // user picked under the OLD zone is reinterpreted under the NEW zone, which
            // for DST-aware zones means the picker label shifts by the offset delta on
            // either side of a DST boundary.
            mObservation = mObservation with { Zone = zone };
            OnLocationEdited(sender, e);
        }

        private void NumericUpDown_TargetDuration_ValueChanged(object sender, EventArgs e)
        {
            Log.Diag("UI", $"NumericUpDown_TargetDuration.ValueChanged hours={NumericUpDown_TargetDuration.Value}");
            TimeSpan newDuration = TimeSpan.FromMinutes((double)NumericUpDown_TargetDuration.Value * 60.0);
            mPlanningPreferences = mPlanningPreferences with { MinDuration = newDuration };
            PersistPlanningPreferencesToActiveSite();
            if (mCoordinator == null) return;
            // Coordinator's internal debounce coalesces rapid scrub ticks into one
            // pipeline run; pipeline diff catches Duration change as HDM-only and
            // refreshes visibility on every sub-chart.
            mCoordinator.Apply(SnapshotCurrent());
        }

        private void NumericUpDown_TargetFloor_ValueChanged(object sender, EventArgs e)
        {
            double newHorizon = (double)NumericUpDown_TargetFloor.Value;
            Log.Diag("UI", $"NumericUpDown_TargetFloor.ValueChanged deg={newHorizon}");
            mPlanningPreferences = mPlanningPreferences with { TargetFloorDeg = newHorizon };
            PersistPlanningPreferencesToActiveSite();
            if (mCoordinator == null) return;
            // Horizon-line repositioning stays immediate -- it's one strip per chart
            // and the user wants instant feedback as they scrub. The per-target
            // visibility recompute is what's expensive; the coordinator's debounce
            // collapses scrub ticks into one trailing-edge pipeline run.
            if (mSubCharts != null)
                foreach (var sc in mSubCharts.Values) sc.UpdateHorizonLine(newHorizon);
            mCoordinator.Apply(SnapshotCurrent());
        }

        // Mirror mPlanningPreferences back onto the active NamedSite's Preferences
        // DTO so SettingsStore.Save (FormClosing or any other path) persists the
        // user's spinner edit. mPlanningPreferences alone is in-memory state that
        // isn't part of mAppSettings.NamedLocations, so without this mirror the
        // saved settings.json carries stale per-site defaults. Skips when the
        // user is on "Custom" or any name not in mAppSettings (preference edits
        // outside a named site have nowhere to persist; the user is in free-edit
        // mode and would need to add the site first).
        private void PersistPlanningPreferencesToActiveSite()
        {
            if (mLocation == null || mAppSettings?.NamedLocations == null) return;
            NamedSite active = mAppSettings.NamedLocations.Find(x =>
                string.Equals(x.Name, mLocation.Name, StringComparison.OrdinalIgnoreCase));
            if (active == null) return;
            active.Preferences = mPlanningPreferences.ToDto();
        }

        // Lazily-constructed shared Timer debouncing OnLocationEdited (lat/lon/elev/
        // N/W/Bortle/Extinction edits). The debounce exists so the keying-change
        // detection (LocationsCacheEquivalent) runs once per scrub burst rather
        // than per spinner tick -- a keying change triggers ResetForLocationChange
        // (clears checked set + blanks chart) which we don't want firing on every
        // intermediate tick of a multi-step scrub.
        //
        // Other handlers (Horizon / Duration / Filter / Moon) don't have a keying-
        // change semantic and call mCoordinator.Apply directly; the coordinator's
        // own internal 150 ms debounce coalesces those.
        private void RestartSessionsRebuildDebounce()
        {
            if (mSessionsRebuildDebounce == null)
            {
                mSessionsRebuildDebounce = new System.Windows.Forms.Timer { Interval = SessionsRebuildDebounceMs };
                mSessionsRebuildDebounce.Tick += SessionsRebuildDebounce_Tick;
            }
            mSessionsRebuildDebounce.Stop();
            mSessionsRebuildDebounce.Start();
        }

        // Trailing-edge tick for the OnLocationEdited debounce. Branches on whether
        // the just-settled mLocation crossed LocationsCacheEquivalent vs the cache:
        //
        // 1. Keying drift -> ResetForLocationChange (clears checked set, blanks
        //    chart, drops + rebuilds the cache against the new location). Per
        //    spec, scrubs that cross are treated as "I changed sites."
        //
        // 2. Within-equiv scrub (Bortle / ExtinctionK -- the non-keying fields
        //    that ride OnLocationEdited): hand a snapshot to the coordinator
        //    immediately. ApplyImmediateAsync (no further internal debounce
        //    since we've already settled) runs the pipeline; the diff catches
        //    Bortle / ExtinctionK change as HDM-style and refreshes visibility
        //    on every sub-chart. Post-apply hook fires PushSkyKSInputs(ctx)
        //    so Sky's K-S brightness re-walks with the new inputs.
        private async void SessionsRebuildDebounce_Tick(object sender, EventArgs e)
        {
            // async void: wrap entire body so a synchronous throw doesn't crash the process.
            try
            {
                mSessionsRebuildDebounce.Stop();

                if (mCache != null && !LocationsCacheEquivalent(mLocation, mCache.CurrentLocation))
                {
                    await ResetForLocationChange();
                    return;
                }

                if (mCoordinator == null) return;
                await mCoordinator.ApplyImmediateAsync(SnapshotCurrent());
            }
            catch (Exception ex)
            {
                Log.Error("SessionsRebuildDebounce_Tick threw", ex);
            }
        }

        // Symmetric clean-slate response to any user action that re-keys the
        // chart cache (combo location pick, lat/lon/elev scrub once it crosses
        // LocationsCacheEquivalent). Per spec:
        //   - Cancel any in-flight chart build so its post-await render can't
        //     paint stale geometry from the old location.
        //   - Clear the checked set via the VM. CheckedListBox row checks blank
        //     via the OnVmCheckedSetChanged binding; the multi-graph debounce's
        //     re-arm on CheckedSetChanged is then explicitly stopped because we
        //     don't want a 250-ms-late blank-render to fight our explicit one
        //     (and harmless when the set was already empty -- VM short-circuits,
        //     no event, Stop() no-ops).
        //   - Hand the coordinator an empty-targets snapshot. The pipeline
        //     drops the cache (gated by LocationCacheEquivalent), runs
        //     PrepareManyAsync(empty) as a no-op, Renders the active chart
        //     with the empty target list (-> blank chart), and refreshes
        //     visibility on inactive charts (no-op on empty mSeriesByTarget).
        //     Post-apply hook refreshes the dependent dawn/dusk/sun/moon
        //     labels for the new location.
        // No auto re-render: the user re-engages a control (Button_Graph,
        // CheckedListBox toggles, etc.) to draw fresh series.
        private async Task ResetForLocationChange()
        {
            // Cancel any in-flight pipeline (chart build or scrub-debounce tick)
            // so its post-await effects can't paint stale geometry.
            mCoordinator?.Cancel();

            mSelection.SetAllChecked(false);
            mCheckedToggleDebounce?.Stop();

            // Drop the Visible-Tonight tint set too. Those tags were computed against
            // the prior site's visibility window; the new site's "visible tonight" is
            // a different set entirely, so the tinted checkbox interiors would be
            // misleading until the user re-clicks Button_VisibleTonight. Same gesture
            // as the listbox's right-click clear, just tied to the location swap.
            if (mVisibleTaggedTargets.Count > 0)
            {
                mVisibleTaggedTargets.Clear();
                CheckedListBox_SelectedTargets?.Invalidate();
            }

            // Explicit empty targets so the active area re-renders blank under the
            // new location. The no-arg SnapshotCurrent() would inherit the prior
            // last-applied target list (i.e., the old location's targets) -- not
            // what we want on a deliberate reset.
            await mCoordinator.ApplyImmediateAsync(SnapshotCurrent(Array.Empty<Target>()));
        }

        // Compare the two locations on the fields that key the chart cache: pure
        // geometry (lat/lon/N/W/elevation). Post-Phase-2 the date axis lives on
        // ObservationMoment and is tracked separately by ChartCacheStore via its
        // mLastSetUtc shadow; this helper stays geometry-only so a site-keying
        // scrub vs a date scrub vs a TZ scrub each take their own code path.
        private static bool LocationsCacheEquivalent(Location a, Location b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Latitude  == b.Latitude
                && a.Longitude == b.Longitude
                && a.North     == b.North
                && a.West      == b.West
                && a.Elevation == b.Elevation;
        }

        // ---------- ComboBox_Location ----------
        private async void ComboBox_Location_SelectionIndexChanged(object sender, EventArgs e)
        {
            if (ComboBox_Location.SelectedItem == null) return;
            string name = ComboBox_Location.SelectedItem.ToString();
            Log.Diag("UI", $"ComboBox_Location.SelectionIndexChanged name={name}");

            if (name == "Custom")
            {
                // User explicitly chose "Custom" -- clear lat/lon so they can type fresh
                // values. Preserve Horizon / Duration / N / W: those are independent of the
                // location name and the user may have deliberately tuned them.
                mLocation = mLocation.With(name: "Custom", latitude: 0, longitude: 0);
                // Polyline horizon is per-named-site; Custom has no associated NamedSite
                // to look up a LocalHorizonPath against, so clear the loaded profile and
                // fall back to the scalar Horizon path until the user picks a named site.
                ApplySiteHorizon(null);
                SyncLocationUIFromModel();
            }
            else
            {
                NamedSite named = mAppSettings.NamedLocations.Find(x => x.Name == name);
                if (named == null) return;
                // The user's observation moment is independent of site -- mObservation
                // stays put across the swap. The picked site's own TimeZoneInfo
                // becomes the new Location.TimeZoneInfo; if the user wants the
                // picker to reinterpret the wall-clock time against the new zone
                // they'll click Button_Now or re-pick the date/time.
                mLocation = named.ToLocation();
                // Per-site planning preferences come over with the site -- the
                // Horizon/Duration spinners snap to the new site's values.
                mPlanningPreferences = PlanningPreferences.FromDto(named.Preferences);
                // Load the polyline horizon for the picked site, if configured. Null result
                // (no path, missing file, parse failure) falls back through SnapshotCurrent
                // to the scalar ScalarHorizonProfile(mLocation.Horizon) path; the loader
                // logs to tp.log on failure.
                ApplySiteHorizon(named.LocalHorizonPath);
                SyncLocationUIFromModel();
            }

            mAppSettings.LastSelectedLocationName = name;
            SettingsStore.Save(mAppSettings);

            // Symmetric reset path: clear checked set, blank chart, drop+rekey cache.
            // The coordinator's post-apply hook (RefreshAstrometryLabels) refreshes
            // the dependent dusk/dawn/sun/moon labels once the pipeline settles.
            await ResetForLocationChange();
        }

        // DropDown nulls the current selection so re-picking the same item (e.g. the
        // user's home location after a manual edit auto-switched the combo to "Custom")
        // still fires SelectedIndexChanged.
        private void ComboBox_Location_DropDown(object sender, EventArgs e)
        {
            ComboBox_Location.SelectedItem = null;
        }

        // Fired by every location-input event (lat/lon spinners, textboxes, N/W checkboxes,
        // Horizon, Duration). If the user edited a field by hand, flip the combo to "Custom"
        // so the combo label always matches the currently-displayed values, and restart the
        // debounce so the cache invalidates and the Sessions chart rebuilds (the Tick handler
        // does the cache-equivalency check, so a no-op edit -- e.g., flipping N then back --
        // ultimately doesn't drop the cache).
        private void OnLocationEdited(object sender, EventArgs e)
        {
            if (mSyncingLocationUI) return;

            // Per-edit label refresh. Cheap (~150 us); fires on every spinner tick so
            // the dependent readouts (dusk/dawn, sun/moon altitude, moon rise/set,
            // illumination, phase) track the live mLocation values without lag.
            RefreshAstrometryLabels();

            // Debounce-restart fires for every user-driven location edit, regardless of
            // whether the combo is already "Custom". The cache check inside the Tick
            // determines whether the cache actually drops; the chart's RebuildSessionsData
            // is harmless when the cache is empty (no-ops per series).
            RestartSessionsRebuildDebounce();

            if (ComboBox_Location.SelectedItem != null &&
                ComboBox_Location.SelectedItem.ToString() == "Custom") return;

            ComboBox_Location.SelectedIndexChanged -= ComboBox_Location_SelectionIndexChanged;
            ComboBox_Location.SelectedItem = "Custom";
            ComboBox_Location.SelectedIndexChanged += ComboBox_Location_SelectionIndexChanged;

            mLocation = mLocation.With(name: "Custom");
            // Clear the polyline -- the user has edited away from the named site that
            // owned the LocalHorizonPath, so the loaded polyline no longer corresponds
            // to the displayed coordinates. Scalar Horizon takes over until the user
            // re-picks a named location (or stays on Custom, where there's no polyline).
            ApplySiteHorizon(null);
            mAppSettings.LastSelectedLocationName = "Custom";
            // Not saving on every edit -- settings are persisted on form close.
        }

        // Apply a named site's polyline horizon: load the .hrz at localHorizonPath
        // (null/empty -> no polyline, the scalar Horizon takes over), refresh the
        // path label, and re-point the file watcher. Shared by the ComboBox_Location
        // site-pick branches and OnLocationEdited's switch-to-Custom path.
        private void ApplySiteHorizon(string localHorizonPath)
        {
            mLocalHorizon = string.IsNullOrEmpty(localHorizonPath)
                ? null
                : HrzFileLoader.Load(localHorizonPath);
            UpdateHorizonPathLabel();
            ConfigureHorizonWatcher(localHorizonPath);
        }

        // PickStartupPreferences -- companion to PickStartupLocation. Resolves
        // the per-site PlanningPreferences from the same NamedSite entry
        // PickStartupLocation picked so the spinner values, the chart horizon
        // line, and the fits cache key all start consistent with the persisted
        // shape. Falls through to PlanningPreferences.Default when no settings
        // entry matches the last-selected name (e.g. the user renamed the site
        // out from under settings.json).
        private PlanningPreferences PickStartupPreferences()
        {
            NamedSite lastPicked = mAppSettings.NamedLocations.Find(x =>
                string.Equals(x.Name, mAppSettings.LastSelectedLocationName, StringComparison.OrdinalIgnoreCase));
            if (lastPicked != null) return PlanningPreferences.FromDto(lastPicked.Preferences);

            if (mAppSettings.NamedLocations.Count > 0)
                return PlanningPreferences.FromDto(mAppSettings.NamedLocations[0].Preferences);

            return PlanningPreferences.Default;
        }

        private Location PickStartupLocation()
        {
            // Boot default: the LastSelectedLocationName from settings.json.
            // PersonalDefaults seeds this to "Penns Park" on first run, then
            // ComboBox_Location_SelectionIndexChanged overwrites it each pick
            // so the next launch lands on the user's most recently-active site.
            NamedSite lastPicked = mAppSettings.NamedLocations.Find(x =>
                string.Equals(x.Name, mAppSettings.LastSelectedLocationName, StringComparison.OrdinalIgnoreCase));
            if (lastPicked != null) return lastPicked.ToLocation();

            if (mAppSettings.NamedLocations.Count > 0)
                return mAppSettings.NamedLocations[0].ToLocation();

            // Fully qualify: MainForm inherits Control.Location (type Point), which shadows
            // the `using Location = ...` alias in member-access context.
            return Astronomy.Core.Locations.Location.Default;
        }

        // Look up the active named-location's LocalHorizonPath (if any) and return the
        // loaded polyline profile. Null if mLocation is null/Custom, no matching named
        // setting exists, no path is configured, or the loader fails (logs to tp.log).
        // Used by the startup flow + FileSystemWatcher Tick to refresh mLocalHorizon
        // against the currently-active site without going through the combo handler.
        private IHorizonProfile LoadLocalHorizonForCurrentLocation()
        {
            if (mLocation == null || string.Equals(mLocation.Name, "Custom", StringComparison.Ordinal))
                return null;
            NamedSite named = mAppSettings?.NamedLocations?.Find(x =>
                string.Equals(x.Name, mLocation.Name, StringComparison.OrdinalIgnoreCase));
            return HrzFileLoader.Load(named?.LocalHorizonPath);
        }

        // Returns the LocalHorizonPath configured for the active named location, or
        // null when the active location is Custom / unknown / has no path.
        private string GetCurrentHorizonPath()
        {
            if (mLocation == null || string.Equals(mLocation.Name, "Custom", StringComparison.Ordinal))
                return null;
            NamedSite named = mAppSettings?.NamedLocations?.Find(x =>
                string.Equals(x.Name, mLocation.Name, StringComparison.OrdinalIgnoreCase));
            return named?.LocalHorizonPath;
        }

        // Drives Label_HorizonPath's text from GetCurrentHorizonPath(). Safe to call
        // before InitializeLocalHorizonControls (label may still be null) -- guard
        // checks for that.
        private void UpdateHorizonPathLabel()
        {
            if (Label_HorizonPath == null) return;
            string path = GetCurrentHorizonPath();
            Label_HorizonPath.Text = string.IsNullOrEmpty(path)
                ? "(no local horizon)"
                : Path.GetFileName(path);
        }

        // Rebuild the FileSystemWatcher for the given path. Disposing the previous
        // watcher cleanly drops its event subscriptions; a null/missing path leaves
        // the watcher disposed and unconfigured.
        private void ConfigureHorizonWatcher(string path)
        {
            mHorizonWatcher?.Dispose();
            mHorizonWatcher = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string dir = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

            try
            {
                mHorizonWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                mHorizonWatcher.Changed += HorizonWatcher_FileChanged;
                mHorizonWatcher.Created += HorizonWatcher_FileChanged;
            }
            catch (Exception ex)
            {
                Log.Warn("ConfigureHorizonWatcher failed for '" + path + "'", ex);
                mHorizonWatcher = null;
            }
        }

        // Watcher callback runs off the UI thread; marshal to the UI thread and
        // restart the debounce timer. The Tick handler does the actual reload.
        private void HorizonWatcher_FileChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    mHorizonReloadDebounce?.Stop();
                    mHorizonReloadDebounce?.Start();
                }));
            }
            catch (ObjectDisposedException)
            {
                // Form closed between IsHandleCreated check and BeginInvoke; safe to ignore.
            }
        }

        // Trailing-edge debounce tick for FileSystemWatcher coalescing. Reloads the
        // polyline from the active path and re-renders via the coordinator.
        private void HorizonReloadDebounce_Tick(object sender, EventArgs e)
        {
            try
            {
                mHorizonReloadDebounce.Stop();
                string path = GetCurrentHorizonPath();
                if (string.IsNullOrWhiteSpace(path)) return;
                mLocalHorizon = HrzFileLoader.Load(path);
                UpdateHorizonPathLabel();
                mCoordinator?.Apply(SnapshotCurrent());
            }
            catch (Exception ex)
            {
                Log.Error("HorizonReloadDebounce_Tick threw", ex);
            }
        }

        // Browse button click handler. Opens an OpenFileDialog filtered to *.hrz,
        // persists the selected path to NamedSite for the active named
        // location, reloads the polyline + re-renders via the coordinator. No-op
        // (informational message) when the active location is Custom -- the
        // polyline is per-named-site so Custom has nowhere to persist the path.
        private void Button_BrowseHorizon_Click(object sender, EventArgs e)
        {
            Log.Diag("UI", "Button_BrowseHorizon.Click");
            if (mLocation == null || string.Equals(mLocation.Name, "Custom", StringComparison.Ordinal))
            {
                MessageBox.Show(this,
                    "Pick a named location first; the local horizon file is associated with a specific site.",
                    "Local horizon",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new OpenFileDialog
            {
                Title = "Select local horizon file (.hrz)",
                Filter = "NINA horizon files (*.hrz)|*.hrz|All files (*.*)|*.*",
                DefaultExt = "hrz",
            })
            {
                string current = GetCurrentHorizonPath();
                if (!string.IsNullOrEmpty(current))
                {
                    string dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dlg.InitialDirectory = dir;
                    dlg.FileName = Path.GetFileName(current);
                }

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                NamedSite named = mAppSettings.NamedLocations.Find(x =>
                    string.Equals(x.Name, mLocation.Name, StringComparison.OrdinalIgnoreCase));
                if (named == null) return;
                named.LocalHorizonPath = dlg.FileName;
                SettingsStore.Save(mAppSettings);

                mLocalHorizon = HrzFileLoader.Load(dlg.FileName);
                UpdateHorizonPathLabel();
                ConfigureHorizonWatcher(dlg.FileName);
                mCoordinator?.Apply(SnapshotCurrent());
            }
        }

        // Wire up the local-horizon hot-reload pipeline: debounce timer for the
        // FileSystemWatcher coalescing, initial label text from the current path,
        // and watcher configured for the startup site (if one is set). The Browse
        // button + path label themselves live in MainForm.Designer.cs; their
        // Click handler is wired there too.
        private void InitializeLocalHorizonControls()
        {
            mHorizonReloadDebounce = new System.Windows.Forms.Timer { Interval = HorizonReloadDebounceMs };
            mHorizonReloadDebounce.Tick += HorizonReloadDebounce_Tick;

            UpdateHorizonPathLabel();
            ConfigureHorizonWatcher(GetCurrentHorizonPath());
        }
    }
}
