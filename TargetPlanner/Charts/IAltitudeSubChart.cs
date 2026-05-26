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
    // type level here -- forgetting UpdateNowLine on a new sub-chart is a
    // compiler error, not a behavior drift.
    //
    // Per-chart specifics that don't fit the shared contract stay on the
    // concrete class and are accessed via a typed reference (currently only
    // AltitudeSubChart_Sky.ActiveFilterCenterNm + ActiveFilterBandwidthNm
    // and RefreshSkyBrightness).
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
        // debounce; data series do NOT recompute. All four sub-charts use a
        // UTC-internal X axis (every plotted X is the OADate of a UTC instant),
        // so the caller passes ObservationMoment.Utc and the implementation
        // plots nowUtc.ToOADate() directly -- no zone conversion needed.
        void UpdateNowLine(DateTime nowUtc);

        // Live update -- mutates the green horizon line's Y position in place.
        // Wired from the horizon spinner without debounce. No-op on charts
        // without a horizon line (Sky).
        void UpdateHorizonLine(double horizon);

        // Synchronous full re-render. Each sub-chart preserves series identity
        // across calls via internal GetOrCreate paths so legend-toggle state
        // survives a re-render. Render reads H/D/M-aware fit state from the
        // cache (cache.GetFitOrNull(target, ctx.Hdm)) and applies
        // hide-on-no-fit / overlay reconciliation inline, so the first paint
        // already reflects the current H/D/M without a follow-up call.
        //
        // Phase 1 of the orchestration-layer refactor consolidated the prior
        // 8-parameter signature behind a single ChartContext snapshot. The
        // sub-chart reads ctx.Targets / ctx.Policy (active filter (Lorentzian +
        // center + bandwidth), moon-avoidance toggle, target floor, min duration,
        // local horizon) / ctx.Location and derives DateTime from ctx.Observation.Utc.
        //
        // The optional progress sink ticks once per target as the per-target
        // outer loop iterates. The coordinator wraps the underlying sink in
        // an offset adapter so Done is cumulative across EnsureAsync + Render
        // (the bar advances smoothly from cache-prep into render without a
        // reset). Pass null to opt out (single-target / hidden-chart paths).
        void Render(ChartContext ctx, IChartCacheStore cache,
            IProgress<(int Done, int Total)> progress = null);
    }
}
