using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Astronomy.Core.Horizons;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using TargetPlanner.Caches;
using TargetPlanner.State;
using TargetPlanner.Support;
using TargetPlanner.Targets;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner
{
    // CheckedListBox_SelectedTargets paint concern: owns the two independent
    // paint surfaces the listbox exposes via DupeAwareCheckedListBox callbacks.
    //
    //   - Row background (the GetDupeRowBackground callback): per-target
    //     dupe-set pastel from DupeSetPalette. Marks "two or more rows refer
    //     to the same sky target" (TargetIdentity.AreSameTarget). Rebuilt on
    //     KnownTargetsChanged from RecomputeDupeSetColors.
    //
    //   - Checkbox interior (the GetCheckboxInteriorTint callback): three-
    //     state filter readout.
    //
    //         GREEN (VisibleTintColor) -- cache has a non-null Tonight.Floor
    //         under the current Hdm: target passes the active H/D/M/F.
    //
    //         BLUE (null tint -> OS-default Win11 glyph) -- target is
    //         geometrically below the site's polyline horizon (or 0 deg math
    //         horizon when no polyline) at every azimuth during the night.
    //         Filter-independent; only changes when site / TZ / date / polyline
    //         changes. Tracked in mGeoVisCache, rebuilt in
    //         RefreshAfterPostApply via CoarseVisibility.IsEverAboveHorizon.
    //
    //         SLATE (SlateInteriorColor) -- target is visible above the
    //         polyline at some point but has no D-hour fit at the current
    //         H/D/M/F (no Tonight.Floor). Fails the planning filter, not the
    //         physical horizon.
    //
    // Right-click on the listbox clears the checked set entirely
    // (OnSelectedTargetsMouseDown). The tints themselves are cache-driven
    // and not user-clearable.
    //
    // Fields stay on MainForm (the partial-class-file-split pattern); only
    // the methods + constants live here. The post-apply hook in
    // ChartCoordinatorPresenter calls RefreshAfterPostApply with the live
    // ctx; the painter then reads off the captured state per row paint.
    public partial class MainForm
    {
        // Muted-success green for the GREEN state (target passes H/D/M/F).
        private static readonly Color VisibleTintColor =
            Color.FromArgb(0x6E, 0xBE, 0x6E);

        // Slate-blue for the SLATE state (above polyline but no D-hour fit).
        private static readonly Color SlateInteriorColor =
            Color.FromArgb(0x6C, 0x7C, 0xB2);

        // Pastel palette indexed by stable hash of (RoundedRa, RoundedDec,
        // North) so the same coord set lands on the same color across sort
        // changes and re-populates. Opaque pastels (not alpha) -- GDI+
        // alpha-blending against a system-themed CheckedListBox background
        // renders inconsistently across Windows themes, so we mix the tints
        // into the OS Window color directly rather than relying on alpha.
        private static readonly Color[] DupeSetPalette = new[]
        {
            Color.FromArgb(190, 220, 250),  // soft blue
            Color.FromArgb(250, 230, 180),  // soft amber
            Color.FromArgb(240, 210, 240),  // soft magenta
            Color.FromArgb(200, 240, 220),  // soft teal
            Color.FromArgb(250, 210, 200),  // soft salmon
            Color.FromArgb(230, 240, 200),  // soft lime
            Color.FromArgb(220, 210, 250),  // soft lavender
            Color.FromArgb(240, 240, 200),  // soft pale yellow-green
        };

        // Per-target dupe-set background colors. Targets sharing (RA, Dec,
        // North) form a dupe-set; each set gets a stable pastel from
        // DupeSetPalette. Recomputed on every KnownTargetsChanged via
        // RecomputeDupeSetColors. Targets not in any dupe-set are absent
        // from the dict -- the owner-draw handler reads missing as "use the
        // OS background".
        private readonly Dictionary<Target, Color> mDupeSetColors =
            new Dictionary<Target, Color>();

        // Per-target geometric-visibility cache for the BLUE checkbox-interior
        // state. True iff the target rises above the site's polyline horizon
        // (or 0 deg refracted math horizon when no polyline) at SOME azimuth
        // during tonight's astronomical night. Rebuilt in the coordinator's
        // post-apply hook from CoarseVisibility.IsEverAboveHorizon -- O(1)
        // closed-form + 1-min polyline scan per target, sub-millisecond for
        // 77-target sets.
        private readonly Dictionary<Target, bool> mGeoVisCache =
            new Dictionary<Target, bool>();

        // Latest ChartContext + DayWindowKey successfully applied through the
        // coordinator. Stamped in RefreshAfterPostApply so the painter can
        // resolve GREEN / SLATE / BLUE without re-running SnapshotCurrent per
        // paint. Also consumed by Button_VisibleTonight_Click to bulk-check
        // every GREEN target under the same ctx.Hdm.
        private ChartContext mLastAppliedCtx;
        private DayWindowKey mLastAppliedDayKey;

        // Called by the ChartCoordinator's post-apply hook. Stamps the last-
        // applied snapshot for the painter, rebuilds mGeoVisCache against
        // the new ctx, and invalidates the listbox so the new tints paint.
        private void RefreshAfterPostApply(ChartContext ctx)
        {
            mLastAppliedCtx = ctx;
            NightWindow night = mCache?.LocationNightCache?.Starting
                              ?? NightCalculator.ComputeNight(mLocation, ctx.Observation.Utc);
            mLastAppliedDayKey = Charts.ChartLayout
                .BuildDayWindow(night, ctx.Observation.Zone).Key;

            // BLUE check uses the raw polyline only (not the user's
            // TargetFloorDeg scalar) so "below polyline" is the physical-
            // obstruction story; the TargetFloorDeg knob is the SLATE/GREEN
            // distinction inside H/D/M.
            IHorizonProfile polyline = ctx.Policy.PolylineHorizon
                                       ?? new ScalarHorizonProfile(0);
            mGeoVisCache.Clear();
            foreach (Target t in mSelection?.KnownTargets ?? Enumerable.Empty<Target>())
            {
                if (t == null) continue;
                mGeoVisCache[t] = CoarseVisibility.IsEverAboveHorizon(
                    t, ctx.Location, night, polyline);
            }

            CheckedListBox_SelectedTargets?.Invalidate();
        }

        // Checkbox-interior tint callback. Three states resolved off the last-
        // applied ChartContext + cache fits + mGeoVisCache.
        private Color? GetCheckboxInteriorTint(int rowIndex)
        {
            Target row = TargetForRow(rowIndex);
            if (row == null || mCache == null || mLastAppliedCtx == null) return null;
            // GREEN: passes current H/D/M/F.
            var fit = mCache.GetFitOrNull(row, mLastAppliedCtx.Hdm)?.Tonight;
            if (fit?.Floor != null) return VisibleTintColor;
            // BLUE: geometrically below polyline. Defensive default -- treat
            // unknown / not-yet-computed targets as visible (SLATE) rather
            // than risk a false BLUE on a real target.
            if (mGeoVisCache.TryGetValue(row, out bool visible) && !visible) return null;
            // SLATE.
            return SlateInteriorColor;
        }

        // RowBackground callback. Returns the dupe-set tint for a row, or null
        // when the row's target isn't in any dupe-set.
        private Color? GetDupeRowBackground(int rowIndex)
        {
            Target row = TargetForRow(rowIndex);
            if (row == null) return null;
            return mDupeSetColors.TryGetValue(row, out var c) ? (Color?)c : null;
        }

        // Right-click on the listbox: clear the checked set. Replaces the
        // pre-2026-05-28 "clear Visible-tonight tint" gesture (tints are now
        // cache-driven, not user-clearable). The check-set clear is the
        // closest matching "undo what just got picked" affordance.
        private void OnSelectedTargetsMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            mSelection?.SetAllChecked(false);
        }

        // Group KnownTargets into duplicate-sets and give each set of two or
        // more a stable pastel from DupeSetPalette. "Duplicate" is
        // TargetIdentity.AreSameTarget -- equal stars-stripped names AND
        // coordinates within ~1 arcminute. Membership is transitive (T1~T2
        // and T2~T3 puts all three in one set), resolved with a disjoint-set
        // union. Loads collapse duplicates as they arrive
        // (TargetIdentity.SelectNewTargets), so in practice this tints what
        // manual Add / RA-Dec entry has created -- the "spot a target you
        // typed twice" cue the listbox has always given. Targets in no
        // duplicate-set are absent from mDupeSetColors; the listbox owner-
        // draw handler reads missing as "use the OS background".
        private void RecomputeDupeSetColors()
        {
            mDupeSetColors.Clear();
            var targets = mSelection?.KnownTargets.Where(t => t != null).ToList()
                          ?? new List<Target>();
            int n = targets.Count;
            if (n == 0)
            {
                CheckedListBox_SelectedTargets?.Invalidate();
                return;
            }

            // DSU.
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            // Two targets can only be duplicates when their normalized names
            // match, so bucket indices by name and run the coordinate test
            // only within a bucket -- O(n) tiny buckets instead of O(n^2).
            var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string key = TargetIdentity.NormalizeName(targets[i].Name);
                if (!byName.TryGetValue(key, out var indices))
                {
                    indices = new List<int>();
                    byName[key] = indices;
                }
                indices.Add(i);
            }
            foreach (var indices in byName.Values)
            {
                for (int a = 0; a < indices.Count; a++)
                    for (int b = a + 1; b < indices.Count; b++)
                        if (TargetIdentity.AreSameTarget(targets[indices[a]], targets[indices[b]]))
                            Union(indices[a], indices[b]);
            }

            // Collect connected components.
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var members))
                {
                    members = new List<int>();
                    groups[root] = members;
                }
                members.Add(i);
            }

            int paletteSize = DupeSetPalette.Length;
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                // Order-independent hash of group members, so the same set
                // of targets always lands on the same palette index regardless
                // of KnownTargets insertion order.
                int hash = 0;
                foreach (int idx in kv.Value)
                {
                    Target t = targets[idx];
                    hash ^= HashCode.Combine(
                        t.Name,
                        Math.Round(t.RightAscension, 6),
                        Math.Round(t.Declination, 6),
                        t.North);
                }
                int colorIdx = (hash & 0x7FFFFFFF) % paletteSize;
                Color c = DupeSetPalette[colorIdx];
                foreach (int idx in kv.Value) mDupeSetColors[targets[idx]] = c;
            }
            CheckedListBox_SelectedTargets?.Invalidate();
        }
    }
}
