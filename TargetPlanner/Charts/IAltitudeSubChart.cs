using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TargetPlanner.Caches;
using TargetPlanner.State;

using Target = Astronomy.Core.Targets.Target;

namespace TargetPlanner.Charts
{
    // Common shape implemented by every chart-area sub-chart in the LC2-based
    // Charts/ directory. MainForm holds a Dictionary<string, IAltitudeSubChart>
    // keyed by area name ("Day" / "Sky" / "Year" / "Sessions") and dispatches
    // picker / spinner / debounce / Graph-click traffic via foreach iteration
    // instead of explicit per-chart wiring.
    //
    // The universal chart behavior contract (CLAUDE.md) is enforced at the
    // type level here -- forgetting UpdateNowLine or RefreshVisibility on a
    // new sub-chart is a compiler error, not a behavior drift.
    //
    // Per-chart specifics that don't fit the shared contract stay on the
    // concrete class and are accessed via a typed reference (currently only
    // AltitudeSubChart_Sky.ActiveFilterCenterNm and RefreshSkyBrightness).
    public interface IAltitudeSubChart : IDisposable
    {
        // The WinForms Container hosting the CartesianChart + custom legend.
        // MainForm adds this to Panel_AltitudeChart and flips Visible via
        // ShowOnlyAltitudeChart so only the active sub-chart paints.
        Control Control { get; }

        // Total preferred height of the Container (chart + legend's current
        // wrap-row count). MainForm grows Panel / GroupBox / Form to match.
        int IdealHeight { get; }

        // Raised when IdealHeight changes (e.g. legend rows wrap to an extra
        // line). MainForm subscribes and resizes the panel + form by the delta.
        event EventHandler IdealHeightChanged;

        // Live update -- mutates the red now-line's X position in place.
        // Wired to fire from DatePicker / TimePicker / Button_Now without
        // debounce; data series do NOT recompute.
        void UpdateNowLine(DateTime now);

        // Live update -- mutates the green horizon line's Y position in place.
        // Wired from the horizon spinner without debounce. No-op on charts
        // without a horizon line (Sky).
        void UpdateHorizonLine(double horizon);

        // Synchronous full re-render. Each sub-chart preserves series identity
        // across calls via internal GetOrCreate paths so legend-toggle state
        // survives a re-render. Render's tail calls RefreshVisibility(...) so
        // the first paint already reflects the H/D/M state.
        //
        // Phase 1 of the orchestration-layer refactor: consolidates the prior
        // 8-parameter signature behind a single ChartContext snapshot. The
        // sub-chart reads ctx.Targets / ctx.MoonProfile / ctx.Location and
        // derives Horizon / Duration / now from ctx.Location.
        void Render(ChartContext ctx, IChartCacheStore cache);

        // Cheap path for Sort changes -- reorders the existing series in
        // mChart.Series without recomputing data and without restarting the
        // visibility refresh task. Caller must pass the SAME target set as
        // the most recent Render, just permuted; targets not in the chart's
        // current state are silently skipped.
        //
        // Without this, sort changes would fire a full Render which kicks the
        // background visibility task on Year / Sessions (wasted work for an
        // order change that doesn't invalidate the cached fit results).
        void Reorder(IReadOnlyList<Target> newOrder);

        // H/D/M-aware visibility refresh per the universal contract. Day / Sky
        // run synchronous BestSession.For probes and toggle stroke alpha;
        // Year / Sessions delegate to Render (their fits live in the cache
        // keyed on HdmKey, so the synchronous re-render reads the new HdmKey's
        // fits directly from cache without any bg work). Cache argument is
        // consumed by Day / Sky for NightCache.Starting and by Year / Sessions
        // for the GetFitOrNull(target, ctx.Hdm) lookup.
        void RefreshVisibility(ChartContext ctx, IChartCacheStore cache);
    }
}
