using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinForms;

namespace TargetPlanner.Charts
{
    // HD-overlay state machine. Owns the per-series snapshot/restore
    // dictionary and the apply/restore step-function rewrite logic. A
    // left-click in the chart's plot area dispatches to TryToggleAt(clickX,
    // clickY); right-click dispatches to ToggleAll. Status updates flow
    // through the reportStatus callback (the Day chart wires it to
    // Log.Diag("Overlay", ...)) so the controller doesn't reach into the
    // host form directly.
    //
    // Window source is supplied via a delegate (not a static dictionary)
    // so callers whose target→window mapping changes between Render calls
    // (e.g. TP's Day chart, where the best D-hour window depends on
    // current Horizon / Duration / MoonAvoidance) can supply a live
    // lookup without rebuilding this controller.
    public class OverlayController
    {
        // Click hit-test tolerance: a left-click within 5° (Y) of a curve toggles
        // its overlay. Deliberately more forgiving than HoverTooltipController's
        // MaxHoverDistanceDeg (1.5°) — a click is a committed action, a hover a
        // precise probe.
        public const double MaxClickDistanceDeg = 5.0;

        // Vertical tick height drawn DOWNWARD from the floor bar's top edge at the
        // target's transit X. Approximates "5 px" at the Day chart's fixed plot-area
        // pixel scale (Y axis spans [0, 90]°).
        private const double TickHeightDeg = 3.0;

        private readonly CartesianChart mChart;
        private readonly Func<LineSeries<ObservablePoint>, (double startOA, double endOA, double floor, double? transitOA)?> mWindowFor;
        private readonly Func<IEnumerable<LineSeries<ObservablePoint>>> mTargetSeries;
        private readonly Action<string> mReportStatus;

        // Y is nullable so we preserve below-horizon "no value" gaps across
        // the snapshot/restore cycle — coercing to 0 would lose them.
        private readonly Dictionary<LineSeries<ObservablePoint>, double?[]> mBackups
            = new Dictionary<LineSeries<ObservablePoint>, double?[]>();

        // Tick decoration LineSeries per active overlay. Lifecycle parallel to
        // mBackups: created by ApplyOverlay alongside the line-series mutation,
        // dropped wherever a backup is dropped (restore branches, Prune, no-window
        // RefreshActiveOverlays branch). Each entry is a 2-point LineSeries drawing
        // a short vertical line from (transitX, floor) down to (transitX, floor -
        // TickHeightDeg), in the host series's stroke color. Added to mChart.Series
        // so LC2 paints it; removed by reassigning mChart.Series sans the tick.
        private readonly Dictionary<LineSeries<ObservablePoint>, LineSeries<ObservablePoint>> mTickSeries
            = new Dictionary<LineSeries<ObservablePoint>, LineSeries<ObservablePoint>>();

        // True if the most recent activation was via ToggleAll's apply path
        // (right-click apply-all). Cleared by RestoreAll, ClearAll, by
        // PruneStaleBackups when the prune drains backups to empty, and by
        // TryToggleAt when a per-target toggle-off drains the last backup.
        private bool mWasGlobalApply;

        // Series the user has explicitly opted out of the global overlay via
        // per-target left-click while in global mode. EnsureGlobalApplied
        // skips these so H/D/M scrubs don't re-overlay them. Cleared by
        // ClearAll, RestoreAll, ToggleAll's apply path (fresh global state),
        // PruneStaleBackups when backups drain to empty, and TryToggleAt
        // when a toggle-off drains the last backup.
        private readonly HashSet<LineSeries<ObservablePoint>> mGlobalOptOuts
            = new HashSet<LineSeries<ObservablePoint>>();

        // Sticky-target fast-path state. After a successful per-target toggle,
        // mLastToggled holds the series and mLastClickPxX/Y the pixel coords
        // of that click. The NEXT TryToggleAt that lands at the IDENTICAL
        // pixel (no mouse movement) re-toggles mLastToggled directly without
        // a hit-test, letting the user rapidly flip an overlay on/off without
        // moving the cursor to re-intersect the redrawn curve. Pixel-exact
        // comparison (no tolerance) preserves the ability to target adjacent
        // or overlapping curves with a 1-pixel mouse nudge. Cleared by
        // ClearAll, RestoreAll, ToggleAll, PruneStaleBackups (when the series
        // is no longer active), and the drain-to-empty branch in TryToggleAt.
        private LineSeries<ObservablePoint> mLastToggled;
        private int mLastClickPxX;
        private int mLastClickPxY;

        // True when the overlay state is in "show windows for all visible
        // targets" mode (right-click apply-all was the last activation and at
        // least one backup is still active). Used by the host chart to extend
        // the global intent to newly-added targets after a Render: per-target
        // mode (false) leaves added targets bare; global mode (true) auto-
        // applies overlay to newly-visible targets via EnsureGlobalApplied.
        // Per-target clicks in global mode are allowed and tracked as
        // exceptions in mGlobalOptOuts. Drains naturally when the user clicks
        // all individual overlays off.
        public bool IsGlobalMode => mWasGlobalApply && mBackups.Count > 0;

        public OverlayController(
            CartesianChart chart,
            Func<IEnumerable<LineSeries<ObservablePoint>>> targetSeries,
            Func<LineSeries<ObservablePoint>, (double startOA, double endOA, double floor, double? transitOA)?> windowFor,
            Action<string> reportStatus)
        {
            mChart = chart;
            mTargetSeries = targetSeries;
            mWindowFor = windowFor;
            mReportStatus = reportStatus;
        }

        // Discard ALL backup + opt-out tracking. Caller invokes before a full
        // Render() so stale entries (series whose data collection is being
        // repopulated) don't leak across render cycles. Implemented as a prune
        // against an empty active set — every series is "stale", so the prune
        // wipes everything — keeping the reset field-list in one place
        // (PruneStaleBackups) instead of duplicating it here.
        public void ClearAll() => PruneStaleBackups(Array.Empty<LineSeries<ObservablePoint>>());

        // Walk currently-active overlays and re-render against the latest window
        // returned by mWindowFor. Called after Horizon / Duration / MoonAvoidance
        // scrubs so an overlay rectangle tracks the new best D-hour window without
        // requiring the user to right-click + re-click. For series whose window
        // vanished entirely (no fit at the new spinner state), restores the original
        // altitude data from the backup and releases the snapshot.
        public void RefreshActiveOverlays()
        {
            if (mBackups.Count == 0) return;
            var snapshot = mBackups.Keys.ToList();
            foreach (var series in snapshot)
            {
                if (!(series.Values is ObservableCollection<ObservablePoint> data)) continue;
                if (!mBackups.TryGetValue(series, out var backup)) continue;

                // Restore original altitude data first; the step function is then
                // re-applied with the fresh window if one exists.
                for (int i = 0; i < backup.Length && i < data.Count; i++)
                    data[i] = new ObservablePoint(data[i].X, backup[i]);

                var win = mWindowFor(series);
                if (!win.HasValue)
                {
                    // Target has no D-hour window under the current H/D/M --
                    // restore the altitude data (above) but KEEP the backup so
                    // a subsequent scrub that re-admits the target re-applies
                    // the overlay automatically. Without this, scrubbing across
                    // a fit/no-fit/fit cycle would silently drop the user's
                    // overlay intent on the way through "no fit" and they'd
                    // have to re-click after every such cycle. Right-click
                    // (RestoreAll) is still the explicit way to clear intent.
                    // The tick decoration goes with the bar visually -- drop it
                    // here too; ApplyOverlay will recreate it when the target
                    // re-enters fit.
                    RemoveTickFor(series);
                    continue;
                }
                // Re-apply step function. ApplyOverlay re-snapshots into mBackups
                // (capturing the just-restored altitude data — same Y values it had
                // on the original click); identity preserved.
                ApplyOverlay(series, data, win.Value);
            }
        }

        public void TryToggleAt(double clickX, double clickY, int clickPxX, int clickPxY)
        {
            // Per-target click is allowed in both modes. In per-target mode it
            // toggles the clicked target's overlay. In global mode it carves
            // out an exception: toggle-off adds the series to mGlobalOptOuts
            // so EnsureGlobalApplied won't re-overlay it on the next H/D/M
            // scrub; toggle-on removes the exception. Right-click (ToggleAll)
            // remains the bulk clear / bulk apply gesture.
            LineSeries<ObservablePoint> best = null;
            (double startOA, double endOA, double floor, double? transitOA)? bestWin = null;

            // Sticky fast-path: if the mouse hasn't moved since the last
            // successful toggle, re-toggle the same target without a hit-test.
            // After a toggle, the curve is replaced by the step shape (or vice
            // versa), so the cursor no longer sits over the original curve at
            // the click pixel; without this, the second click would either
            // miss ("no target curve at this position") or hit a neighbour.
            // Pixel-exact (no tolerance) preserves the ability to target an
            // adjacent or overlapping curve with even a 1-pixel mouse nudge.
            // mWindowFor(mLastToggled).HasValue check confirms the target is
            // still in the host's active filter set (Day's mTargetWindows): a
            // mode-filter swap (Floor -> Meridian/Wall) can drop the sticky
            // target from view, in which case we want to fall through to the
            // normal hit-test rather than toggle an invisible series' backup.
            (double, double, double, double?)? stickyWin = mLastToggled != null
                ? mWindowFor(mLastToggled)
                : null;
            if (mLastToggled != null
                && mLastToggled.IsVisible
                && stickyWin.HasValue
                && clickPxX == mLastClickPxX
                && clickPxY == mLastClickPxY)
            {
                best = mLastToggled;
                bestWin = stickyWin;
            }
            else
            {
                // Walk every visible target series, find the closest curve.
                // Hidden series are excluded — clicking empty space where a
                // curve used to be should not trigger an overlay on it.
                var bestDistance = double.MaxValue;
                foreach (var s in mTargetSeries())
                {
                    if (!s.IsVisible) continue;
                    if (!(s.Values is ObservableCollection<ObservablePoint> data)) continue;
                    var probe = CurveHitTester.At(data, p => p.X, p => p.Y, clickX, clickY);
                    if (probe is null) continue;
                    var dy = probe.Value.Distance;
                    if (dy < bestDistance)
                    {
                        bestDistance = dy;
                        best = s;
                        bestWin = mWindowFor(s);
                    }
                }

                if (best is null)
                {
                    mReportStatus?.Invoke("HD overlay: no target curve at this position");
                    return;
                }
                if (bestDistance > MaxClickDistanceDeg)
                {
                    mReportStatus?.Invoke($"HD overlay: nearest='{best.Name}' Δy={bestDistance:F1}° > {MaxClickDistanceDeg}° — no action");
                    return;
                }
            }

            if (!(best.Values is ObservableCollection<ObservablePoint> bdata)) return;
            bool wasGlobal = IsGlobalMode;
            bool didToggle = false;
            if (mBackups.TryGetValue(best, out var backup))
            {
                for (int i = 0; i < backup.Length && i < bdata.Count; i++)
                    bdata[i] = new ObservablePoint(bdata[i].X, backup[i]);
                mBackups.Remove(best);
                RemoveTickFor(best);
                if (wasGlobal) mGlobalOptOuts.Add(best);
                // Draining the last backup exits global mode cleanly; opt-out
                // bookkeeping becomes meaningless once global intent is gone.
                if (mBackups.Count == 0)
                {
                    mWasGlobalApply = false;
                    mGlobalOptOuts.Clear();
                }
                mReportStatus?.Invoke(wasGlobal && mBackups.Count > 0
                    ? $"HD overlay restored: '{best.Name}' (global -- excluded)"
                    : $"HD overlay restored: '{best.Name}'");
                didToggle = true;
            }
            else if (bestWin.HasValue)
            {
                mGlobalOptOuts.Remove(best);
                ApplyOverlay(best, bdata, bestWin.Value);
                mReportStatus?.Invoke(wasGlobal
                    ? $"HD overlay applied: '{best.Name}' (global -- restored)"
                    : $"HD overlay applied: '{best.Name}'");
                didToggle = true;
            }
            else
            {
                mReportStatus?.Invoke($"HD overlay: '{best.Name}' has no D-hour window tonight");
            }

            // Update sticky state only on a successful toggle so the no-window
            // branch doesn't capture a target the user couldn't actually
            // overlay. The pixel coords pin the fast-path to this exact click
            // position; a 1-pixel mouse move on the next click falls through
            // to the hit-test and can pick up an adjacent curve.
            if (didToggle)
            {
                mLastToggled = best;
                mLastClickPxX = clickPxX;
                mLastClickPxY = clickPxY;
            }
        }

        private void RestoreAll()
        {
            if (mBackups.Count == 0) return;
            foreach (var kv in mBackups)
            {
                if (!(kv.Key.Values is ObservableCollection<ObservablePoint> data)) continue;
                var backup = kv.Value;
                for (int i = 0; i < backup.Length && i < data.Count; i++)
                    data[i] = new ObservablePoint(data[i].X, backup[i]);
            }
            // Drop every tick decoration alongside the backups; reassigns
            // mChart.Series once stripping all known ticks (more efficient than
            // per-tick removes).
            if (mTickSeries.Count > 0)
            {
                var ticks = new HashSet<ISeries>(mTickSeries.Values);
                mTickSeries.Clear();
                var existing = mChart.Series;
                if (existing != null)
                    mChart.Series = existing.Where(s => !ticks.Contains(s)).ToList();
            }
            var n = mBackups.Count;
            mBackups.Clear();
            mWasGlobalApply = false;
            mGlobalOptOuts.Clear();
            mLastToggled = null;
            mReportStatus?.Invoke($"HD overlay restored ({n})");
        }

        // Bulk counterpart of TryToggleAt. If any overlay is currently active,
        // revert everything (preserves the legacy right-click "clear" semantic);
        // otherwise apply the overlay to every visible series that has a window.
        // Visibility and has-a-window guards mirror TryToggleAt so bulk apply
        // matches per-target apply exactly.
        public void ToggleAll()
        {
            if (mBackups.Count > 0)
            {
                RestoreAll();
                return;
            }

            // Re-applying globally starts from a fresh state: any prior
            // per-target opt-outs are discarded so the right-click apply-all
            // gesture always covers every visible+fitting target. The sticky
            // fast-path is also reset since bulk apply breaks the "I'm
            // working with this individual target" context.
            mGlobalOptOuts.Clear();
            mLastToggled = null;

            int applied = 0;
            foreach (var s in mTargetSeries())
            {
                if (!s.IsVisible) continue;
                if (!(s.Values is ObservableCollection<ObservablePoint> data)) continue;
                var win = mWindowFor(s);
                if (!win.HasValue) continue;
                ApplyOverlay(s, data, win.Value);
                applied++;
            }
            if (applied > 0) mWasGlobalApply = true;
            mReportStatus?.Invoke(applied > 0
                ? $"HD overlay applied to all ({applied})"
                : "HD overlay: no visible targets with a D-hour window tonight");
        }

        // Drop backup tracking AND opt-out tracking for series not in the
        // active set. Host's Render path calls this after rebuilding
        // mSeriesByTarget so entries for removed targets are released while
        // entries for surviving targets remain valid for RefreshActiveOverlays.
        // If the prune drains backups to empty, also resets mWasGlobalApply
        // and clears the opt-out set so IsGlobalMode goes false naturally
        // and stale opt-outs don't suppress a later EnsureGlobalApplied.
        public void PruneStaleBackups(IEnumerable<LineSeries<ObservablePoint>> activeSeries)
        {
            if (mBackups.Count == 0 && mGlobalOptOuts.Count == 0 && mLastToggled == null
                && mTickSeries.Count == 0) return;
            var active = new HashSet<LineSeries<ObservablePoint>>(activeSeries);
            var staleBackups = mBackups.Keys.Where(s => !active.Contains(s)).ToList();
            foreach (var s in staleBackups) mBackups.Remove(s);
            // Tick dict tracks the same series keys as mBackups -- prune stale
            // ticks in lockstep so a series removed from the active set doesn't
            // leak its tick reference. The actual ISeries in mChart.Series is
            // already gone (Render reassigned mChart.Series with a fresh list).
            var staleTicks = mTickSeries.Keys.Where(s => !active.Contains(s)).ToList();
            foreach (var s in staleTicks) mTickSeries.Remove(s);
            var staleOptOuts = mGlobalOptOuts.Where(s => !active.Contains(s)).ToList();
            foreach (var s in staleOptOuts) mGlobalOptOuts.Remove(s);
            if (mLastToggled != null && !active.Contains(mLastToggled)) mLastToggled = null;
            if (mBackups.Count == 0)
            {
                mWasGlobalApply = false;
                mGlobalOptOuts.Clear();
            }
        }

        // Apply overlay to any visible, fits-tonight series that doesn't currently
        // have a backup AND isn't in the per-target opt-out set. Called by the
        // host's Render path when IsGlobalMode is true so newly-added targets
        // pick up the overlay -- extends "show windows for all visible targets"
        // intent across target add. Visibility + has-a-window guards mirror
        // ToggleAll's apply path exactly; the opt-out skip honours per-target
        // exceptions the user carved out via left-click in global mode.
        public void EnsureGlobalApplied()
        {
            foreach (var s in mTargetSeries())
            {
                if (!s.IsVisible) continue;
                if (mBackups.ContainsKey(s)) continue;
                if (mGlobalOptOuts.Contains(s)) continue;
                if (!(s.Values is ObservableCollection<ObservablePoint> data)) continue;
                var win = mWindowFor(s);
                if (!win.HasValue) continue;
                ApplyOverlay(s, data, win.Value);
            }
        }

        // Two-pass step-function rewrite. Inside the window: Y = floor
        // (horizontal bar). Single points immediately adjacent to the
        // window edges: Y = 0 (anchor for the vertical drop line). All
        // other outside-window points: Y = null (gap).
        //
        // If win.transitOA is non-null, also publishes a 2-point tick LineSeries
        // hanging downward from (transitOA, floor) by TickHeightDeg in the host
        // series's stroke color. Caller clips transitOA to inside-window upstream
        // (AltitudeSubChart_Day derives transitOA from NightFit.TransitUtc and
        // nulls it when transit is outside the window).
        private void ApplyOverlay(
            LineSeries<ObservablePoint> series,
            ObservableCollection<ObservablePoint> bdata,
            (double startOA, double endOA, double floor, double? transitOA) win)
        {
            var snapshot = bdata.Select(p => p.Y).ToArray();
            mBackups[series] = snapshot;

            var insideMask = new bool[bdata.Count];
            for (int i = 0; i < bdata.Count; i++)
            {
                var x = bdata[i].X ?? 0;
                insideMask[i] = x >= win.startOA && x <= win.endOA;
            }
            for (int i = 0; i < bdata.Count; i++)
            {
                var x = bdata[i].X ?? 0;
                double? newY;
                if (insideMask[i])
                {
                    newY = win.floor;
                }
                else
                {
                    var prevInside = i > 0 && insideMask[i - 1];
                    var nextInside = i < bdata.Count - 1 && insideMask[i + 1];
                    newY = (prevInside || nextInside) ? 0 : (double?)null;
                }
                bdata[i] = new ObservablePoint(x, newY);
            }

            // Replace any prior tick for this series, then publish the new one.
            // Sharing series.Stroke ties the tick to the target's overlay color
            // automatically (ApplyTargetVisibility rebuilds the stroke per-render;
            // we hold the same IPaint instance the curve does at apply time).
            RemoveTickFor(series);
            if (!win.transitOA.HasValue) return;
            var tick = new LineSeries<ObservablePoint>
            {
                Name = series.Name + " (transit)",
                Values = new ObservableCollection<ObservablePoint>
                {
                    new ObservablePoint(win.transitOA.Value, win.floor),
                    new ObservablePoint(win.transitOA.Value, win.floor - TickHeightDeg),
                },
                Stroke = series.Stroke,
                Fill = null,
                GeometrySize = 0,
                IsHoverable = false,
                IsVisibleAtLegend = false,
            };
            mTickSeries[series] = tick;
            AddSeriesToChart(tick);
        }

        // Drop a tick from both mTickSeries and mChart.Series. No-op when no tick
        // is currently active for the given main series.
        private void RemoveTickFor(LineSeries<ObservablePoint> series)
        {
            if (!mTickSeries.TryGetValue(series, out var tick)) return;
            mTickSeries.Remove(series);
            RemoveSeriesFromChart(tick);
        }

        // mChart.Series setter triggers an LC2 redraw. The list may be a List
        // (Render's seriesList) or an Array (ClearAll / construction); we re-
        // materialize as a List unconditionally so subsequent removes can locate
        // the entry. Reassignment frequency is bounded by user toggle gestures.
        private void AddSeriesToChart(ISeries deco)
        {
            var list = mChart.Series?.ToList() ?? new List<ISeries>();
            list.Add(deco);
            mChart.Series = list;
        }

        private void RemoveSeriesFromChart(ISeries deco)
        {
            var existing = mChart.Series;
            if (existing == null) return;
            mChart.Series = existing.Where(s => !ReferenceEquals(s, deco)).ToList();
        }
    }
}
