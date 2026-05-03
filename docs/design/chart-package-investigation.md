# Chart-package investigation

Captured 2026-05-03 by claude-opus-4-7 against the open follow-up
*"Chart-package investigation — prototype OxyPlot / ScottPlot /
LiveCharts2 against a representative test (Day chart + strip lines +
click-toggle legend + click-overlay rectangle). Document the input
shape that fits the chosen package's API. Informs the next item."*

This is a survey + recommendation. **No prototype code was written.**
Once a candidate is confirmed, the next pass writes a small standalone
WinForms harness exercising the four representative interactions.

---

## Why swap at all

**The chart-package swap is a precursor to migrating TargetPlanner from
`net481` to `.NET 10`.** The current chart layer
(`System.Windows.Forms.DataVisualization.Charting`) is .NET Framework-only,
unmaintained (last meaningful update 2018), and has no first-class .NET
5+ port. The community-maintained `System.Windows.Forms.DataVisualization`
NuGet (1.0.0-prerelease) exists but is unofficial and pre-release — not
something to bet a TP migration on.

The migration sequencing this investigation assumes:

1. **Now:** swap chart layer on `net481` to a package that *also* supports
   `.NET 10` WinForms. TP keeps running on `net481`; chart code is the
   moving piece.
2. **Later:** migrate TP from `net481` to `.NET 10`. The chart code
   carries over unchanged because the chosen package already supports
   `.NET 10`.

This means the chosen package must be **dual-target**: clean build on
both `net481` (today) and `.NET 10 WinForms` (after migration).

Phase 4 of the SoC refactor (demote `AltitudeChart` to a stateless
renderer that takes `(SelectionState, CacheStore, Profile, Horizon,
Duration)` and emits a plot) layers cleanly on top — easier to write
the renderer once for a modern API than to rewrite it during the .NET
10 migration.

The investigation cost is low (this doc + a small prototype). The
migration cost is real, so the picking step matters.

---

## Requirements (current `AltitudeChart` + `AltitudeSeries`)

Per the inventory pass against `Charts/AltitudeChart.cs` and
`Charts/AltitudeSeries.cs`, any candidate must support:

**Core chart structure:**
- Multiple swappable chart areas (`Day` / `Year` / `Sessions`) sharing one
  control surface, each with its own axis configuration.
- Per-area X axis: minute / month / month grids respectively;
  DateTime-based label formats.
- Per-area Y axis: altitude in degrees, range 10–90° on Year/Sessions,
  data-inversion + `CustomLabels` for the Sky sub-mode on Day.

**Series:**
- Per-target line series with explicit colors from a 12-entry palette
  (modulo wrap). Many targets, each with up to 5–6 named series.
- One shared `Moon-Day` *area* series with alpha-from-illumination grey fill.
- Per-point tooltips on Year / Sessions; per-series tooltip on Day.

**The IsXValueIndexed=true count invariant.** Every Day series must have
identical point count on every paint or the chart throws. The HD overlay
relies on this — it overwrites Y values in place; X is immutable.

**Interactivity (the four representative tests):**

1. **Strip lines** — red vertical "now" line, green horizontal horizon line,
   yellow→grey→yellow dawn/dusk gradient on Day. Strip-line equivalents
   that update at runtime (now-line every 5s, horizon on spinner).
2. **Legend hit toggles series visibility** — clicking a legend entry hides
   the matching series. Implemented today by swapping `Series.Color` with
   `Color.Transparent` and stashing the original in `Series.Tag`.
3. **Day-curve hit overwrites Y values with a step function** (the HD
   Overlay). Click a Day curve and its altitude becomes
   `BestDayWindow.Floor` inside the window, `0` outside. Right-click
   restores all replaced curves. Backup snapshot in
   `Dictionary<Series, double[]>`.
4. **Per-point tooltips** with formatted multi-line text (transit,
   sub-interval context, integrated quality, etc.).

**Forward platform target:** must build for *both* `net481` (current TP)
*and* `.NET 10 WinForms` (post-migration TP). This is a hard requirement —
the chart swap is the precursor step to the .NET 10 migration; the
chosen package's chart code must survive that transition without rewrite.

---

## Candidates surveyed

### OxyPlot

- **Latest:** `OxyPlot.WindowsForms 2.2.0`, `OxyPlot.Wpf 2.2.0`,
  `OxyPlot.Core 2.2.0`. Released across 2024–2025; active maintenance
  with periodic patch releases.
- **Targets (verified on NuGet):** Core is `netstandard2.0`.
  WindowsForms 2.2.0 directly targets `net462`, `net6.0-windows7.0`,
  `net8.0-windows7.0`. `.NET 10 WinForms` works via backward-compat —
  no explicit `net10.0-windows` TFM yet, but the `net8.0-windows7.0`
  build runs on the `.NET 10` runtime without modification. WPF package
  additionally targets `net6.0-windows7.0` / `net8.0-windows7.0` — same
  forward path.
- **Interaction model:** `PlotView` control + `PlotModel` (data) +
  `PlotController` (input bindings). Series derive from `Series` base
  class (`LineSeries`, `AreaSeries`, `ScatterSeries`, etc.). `Annotation`
  hierarchy gives `LineAnnotation` (horizontal / vertical / general line),
  `RectangleAnnotation`, `PolygonAnnotation`, `TextAnnotation`,
  `ArrowAnnotation`, `EllipseAnnotation`, `PolylineAnnotation`,
  `FunctionAnnotation`. All annotations support `MouseDown` / `MouseMove`
  events plus per-element hit-testing.
- **Mouse handling:** `PlotController` binds gestures
  (`OxyMouseDownGesture`) to commands. `HitTest(args)` on PlotModel
  returns a `HitTestResult` identifying series / annotation / legend /
  axis. Identical pattern to TP's current `HitTest` usage.

### ScottPlot

- **Latest:** `ScottPlot.WinForms 5.1.58` (v5 line) and `4.1.74` (v4 line,
  still maintained). v5 is the newer architecture; v4 the original.
- **Targets (verified on NuGet):** v5.1.58 directly targets `net462`,
  `net8.0-windows7.0`, **`net10.0-windows7.0`** (explicitly listed —
  fresher .NET 10 story than OxyPlot). `net481` is computed-compatible
  via the `net462` target.
- **Known caveats:**
  - Issue [#3526](https://github.com/ScottPlot/ScottPlot/issues/3526)
    reports v5.0 WinForms charts not working on `net481` (March 2024,
    closed without detail in our fetch). Whether this was fixed by
    v5.1.x is unverified — would need a 30-line spike to confirm.
  - Issue [#2491](https://github.com/ScottPlot/ScottPlot/issues/2491)
    reports v5 crashes the WinForms Designer (significant for TP's
    `MainForm.Designer.cs` workflow, though TP's chart is hand-instantiated
    in code so the designer crash may not bite).
  - v4 is stable on `net481` but has an older API and is the deprecation
    track once v5 stabilizes.
- **Interaction model:** `FormsPlot` control wrapping a `Plot`. Add data
  with `plot.Add.Scatter(...)` / `plot.Add.Signal(...)` / similar;
  annotations include `plot.Add.HorizontalLine(...)` /
  `VerticalLine(...)` / `HorizontalSpan(...)`. v5's interactivity model
  is being expanded but is less mature than OxyPlot's; legend-click-toggle
  is not built in (must be added via custom mouse handler).

### LiveCharts2

- **Latest:** `LiveChartsCore.SkiaSharpView.WinForms 2.0.2` (still in 2.0
  release-candidate phase as recently as 2.0.0-rc5).
- **Targets (verified on NuGet):** 2.0.2 targets `net462`,
  `net8.0-windows7.0`, `net8.0-windows10.0.19041`. No explicit
  `net10.0-windows` TFM, but per the maintainers' guidance the
  `net8.0-windows*` builds run correctly on the `.NET 10` runtime; the
  `LiveChartsCore` package is `netstandard2.0` + `net8.0` and inherits
  backward-compat. v2.0.2 specifically addressed reported `.NET 10`
  layout / NRE issues (WPF TabControls). `.NET 10`-specific edge cases
  may surface — track GitHub Discussions during migration.
- **Interaction model:** `CartesianChart` control with a `Series`
  collection of `ISeries` (`LineSeries`, `ColumnSeries`, etc.). Sections
  via `RectangularSection { Xi, Xj, Yi, Yj }`. `DataPointerDown` event
  for click-on-point. Custom legend behaviour requires implementing
  `IChartLegend<T>`. Strong on animations and visual polish; weaker on
  fine-grained interactivity ergonomics. SkiaSharp dependency.

---

## TFM compatibility note

For all three candidates, *"no explicit `net10.0-windows` TFM"* is a
labeling gap, not a functional risk. The .NET runtime is roll-forward
compatible:

- Assemblies targeting `netstandard2.0`, `net6.0-windows7.0`, or
  `net8.0-windows7.0` load and execute on the `.NET 10` runtime
  unchanged. Unless a low-level primitive (e.g. `System.Object`, core
  collection interfaces) breaks, the IL remains valid.
- NuGet TFM-precedence walks back to the closest compatible version on
  resolution — `.NET 10` consumers transparently pick up an older-TFM
  package.
- For LiveCharts2 specifically, the SkiaSharp rendering layer decouples
  drawing from the .NET UI stack, so chart geometry is a `.NET 10`
  non-event by construction.

The actual narrow risks of an older TFM are:
1. **Compiler warning noise** about dependency version mismatches
   (typically auto-suppressed by the SDK).
2. **Missing `.NET 10`-specific JIT optimizations** (AVX-512 loop
   inversion etc.) that older-TFM builds can't access. Minor perf left
   on the table; not relevant at TP's chart scale.
3. **Subtle layout / measurement regressions** that .NET 10 introduces
   without breaking the public API. LiveCharts2 v2.0.2 specifically
   fixed early-2026 TabControl layout distortion — and shipped that
   fix without bumping its TFM. A package author can patch `.NET 10`
   issues against the existing TFM.

So ScottPlot's explicit `net10.0-windows7.0` target signals *"tested on
.NET 10"* more than *"works only on .NET 10"*. Real but small.

## Comparison

| Requirement | OxyPlot | ScottPlot v5 | LiveCharts2 |
|---|---|---|---|
| `net481` build (today) | ✅ direct (`net462` target) | ⚠️ known issue #3526 from v5.0; unverified on v5.1.58 | ✅ direct (`net462` target) |
| `.NET 10` WinForms (post-migration) | ✅ via `net8.0-windows7.0` backward-compat | ✅ **explicit `net10.0-windows7.0` target** | ✅ via `net8.0-windows7.0` backward-compat (v2.0.2 fixed early `.NET 10` layout issues) |
| Single chosen package survives the `net481`→`.NET 10` migration | ✅ low risk | ✅ if #3526 is fixed | ✅ low risk; track RC→stable + edge cases |
| Multiple chart areas in one control | ✅ swap `PlotModel` on `PlotView` | ⚠️ generally one Plot per FormsPlot; v5 multi-axis support evolving | ⚠️ typically one CartesianChart; multi-chart needs multiple controls |
| Vertical / horizontal strip lines | ✅ `LineAnnotation` (Type=Horizontal/Vertical) | ✅ `HorizontalLine` / `VerticalLine` | ✅ `Section` w/ X-only or Y-only bounds |
| Filled rectangle (dawn/dusk gradient) | ✅ `RectangleAnnotation` (Fill, gradient via custom render) | ✅ `HorizontalSpan` / `Rectangle` | ✅ `RectangularSection` (Fill) |
| Legend hit → toggle series | ✅ `PlotController` + HitTest | ⚠️ custom mouse handler required | ⚠️ custom `IChartLegend<T>` required |
| Click on series curve | ✅ HitTest returns Series + nearest point | ✅ `plot.GetCoordinates` + closest-point search | ✅ `DataPointerDown` event |
| Right-click handling | ✅ `OxyMouseDownGesture(MouseButton.Right)` | ✅ standard `MouseClick` event | ✅ standard `MouseClick` event |
| Per-point tooltip | ✅ Series.TrackerFormatString + custom formatters | ✅ tooltips configurable | ✅ Tooltip with template |
| Y-value rewrite preserving X (HD overlay) | ✅ `LineSeries.Points[i] = new DataPoint(x, newY)` | ✅ data array mutation + `Refresh()` | ✅ `Values[i] = newY` |
| Mature, widely used | ✅ ~10 yr in market | ⚠️ v5 is recent; v4 mature | ⚠️ v2 still RC |
| Active maintenance | ✅ regular releases | ✅ active | ✅ active |
| Designer compatibility | ✅ WinForms toolbox | ⚠️ v5 designer crash (#2491); v4 OK | ✅ |

✅ = direct support / known good. ⚠️ = workaround needed or known caveat.

---

## Recommendation

**Pick OxyPlot, with a small ScottPlot v5.1.58 spike to confirm
`net481` works.** With `.NET 10` compatibility no longer a meaningful
differentiator (all three candidates work via direct or backward-compat
TFMs), the decision rests on **interaction-API maturity** and **known
compat regressions**:

1. **Both `net481` (today) and `.NET 10` (post-migration) are covered.**
   OxyPlot.WindowsForms 2.2.0 has solid `net462` for the transition;
   `.NET 10` works via the `net8.0-windows7.0` build's backward-compat.
   This is the same shape as LiveCharts2 — neither has an explicit
   `net10.0-windows` TFM yet, both run fine on `.NET 10`. ScottPlot
   does have an explicit `net10.0-windows7.0` TFM (the freshest
   forward-looking signal of the three), but that advantage is small
   given the others Just Work via backward-compat.
2. **The four representative interactions map directly** to existing
   OxyPlot primitives (annotations, PlotController, HitTest, in-place
   point mutation). Legend-click-toggle, Day-curve hit, right-click
   restore-all are all built-in patterns; no custom legend
   infrastructure or workarounds required. **This is the strongest
   differentiator** — ScottPlot's interactivity model is younger and
   would force more glue code; LiveCharts2 requires implementing
   `IChartLegend<T>` for legend-toggle behaviour.
3. **API maturity matches TP's current chart code's complexity.** TP's
   chart layer is interaction-heavy (legend toggle, Day-curve overlay,
   right-click restore-all, multi-series tooltips). OxyPlot's
   PlotController + HitTest pattern is a near-1:1 translation of TP's
   current `Chart_MouseClick` + `HitTest` code path.
4. **No known compatibility regressions.** Unlike ScottPlot v5's
   `net481` issue #3526 and Designer crash #2491.
5. **Stable v2.x API** — no RC churn risk like LiveCharts2's still-RC
   v2 line.

**Defensible alternatives** if a spike reveals a blocker:

- **ScottPlot v5.1.58** — if the verification spike confirms #3526 is
  no longer reproducible on v5.1.58 + `net481`, ScottPlot's explicit
  `net10.0-windows7.0` target becomes attractive enough to reconsider.
  The catch: more glue code for legend-toggle and HD-overlay
  interactions, plus Designer crash risk (#2491) — though TP
  hand-instantiates the chart, so the Designer crash may not bite.
- **ScottPlot v4** — older but stable on `net481`. Drops the .NET 10
  explicit target story (v4 only goes to `net8.0`); not a fit for the
  precursor framing.
- **LiveCharts2** — viable on `.NET 10` (v2.0.2 addressed early
  layout / NRE issues; runs via `net8.0-windows7.0` backward-compat),
  but its interaction model would force more glue code than OxyPlot's
  for legend-toggle / HD-overlay patterns. Reconsider if animation
  polish or SkiaSharp-rendered visuals become a goal, or if v2 stable
  ships with cleaner interactivity primitives.

---

## Input shape for OxyPlot

How TP's current chart data maps to OxyPlot's API. This isn't a migration
plan; it's the contract a Phase 4 stateless renderer would satisfy when
emitting an OxyPlot `PlotModel`.

### Chart-area swap

Instead of one `Chart` control with three `ChartArea`s, keep one `PlotView`
control. Maintain three `PlotModel` instances (`mDayModel`, `mYearModel`,
`mSessionsModel`). `ShowChartAreaSeries(name)` becomes
`plotView.Model = mModelsByName[name]; plotView.InvalidatePlot(true);`.

```csharp
// Once at startup:
mDayModel = new PlotModel { Title = "Day" };
ConfigureDayAxes(mDayModel);
// ...same for Year, Sessions

// On show:
plotView.Model = mDayModel;
```

### Series

Each TP `Series` becomes an OxyPlot `LineSeries` (or `AreaSeries` for the
shared Moon-Day fill):

```csharp
var daySeries = new LineSeries
{
    Title = $"{target.Name}",
    Color = OxyColor.FromArgb(255, palette.R, palette.G, palette.B),
    StrokeThickness = 1.5,
    LineStyle = LineStyle.Solid,
    TrackerFormatString = "{Tag}", // tooltip per-point via DataPoint.Tag
};
for (int i = 0; i < count; i++)
{
    daySeries.Points.Add(new DataPoint(
        DateTimeAxis.ToDouble(start.AddMinutes(i)),
        altitudes[i]));
}
mDayModel.Series.Add(daySeries);
```

The `IsXValueIndexed=true` count invariant translates to "all Day
LineSeries share the same X-axis grid"; OxyPlot doesn't enforce it but
the HD overlay rewrite still relies on the count being stable. Same
discipline applies.

### Strip lines / annotations

```csharp
// Now-line:
mDayModel.Annotations.Add(new LineAnnotation {
    Type = LineAnnotationType.Vertical,
    X = DateTimeAxis.ToDouble(DateTime.Now),
    Color = OxyColors.Red, StrokeThickness = 1.0,
});

// Horizon line:
mDayModel.Annotations.Add(new LineAnnotation {
    Type = LineAnnotationType.Horizontal,
    Y = horizonDeg,
    Color = OxyColors.Green, StrokeThickness = 2.0,
});

// Dawn/dusk gradient:
mDayModel.Annotations.Add(new RectangleAnnotation {
    MinimumX = DateTimeAxis.ToDouble(start),
    MaximumX = DateTimeAxis.ToDouble(duskLocal),
    MinimumY = double.MinValue, MaximumY = double.MaxValue,
    Fill = OxyColor.FromAColor(96, OxyColors.Yellow),
    Stroke = OxyColors.Transparent,
});
// Repeat for dawn-side gradient + tail.
```

Strip-line *update* (now-line every 5 s) becomes:

```csharp
var nowAnno = mDayModel.Annotations.OfType<LineAnnotation>()
    .First(a => a.Color == OxyColors.Red);
nowAnno.X = DateTimeAxis.ToDouble(DateTime.Now);
plotView.InvalidatePlot(false);
```

### Legend hit → toggle series

`PlotController` binds the left-click gesture to a custom command:

```csharp
var controller = new PlotController();
controller.UnbindAll();
controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.SnapTrack);
controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.None, 1, /*click count*/
    new DelegatePlotCommand<OxyMouseDownEventArgs>((view, _, args) => {
        var hit = view.ActualModel.HitTest(new HitTestArguments(args.Position, 10));
        if (hit?.Element is LineSeries series) {
            // Day-curve hit: HD overlay toggle
            ToggleDayCurveWindow(series);
            args.Handled = true;
        }
        else if (hit?.Element is Legend) {
            // Legend hit: toggle visibility
            ToggleLegendItem(view.ActualModel, args.Position);
            args.Handled = true;
        }
    }));
controller.BindMouseDown(OxyMouseButton.Right, /* ... */
    new DelegatePlotCommand<OxyMouseDownEventArgs>((view, _, args) => {
        RestoreAllReplacedCurves(); args.Handled = true;
    }));
plotView.Controller = controller;
```

`ToggleLegendItem` swaps `series.Color` with `OxyColors.Transparent` and
stashes the original in `series.Tag` — same pattern as today. (OxyPlot's
`Series.Tag` is `object`, identical use.)

### HD overlay (Day-curve hit)

`LineSeries.Points` is `List<DataPoint>` (a struct), so in-place rewrite
works:

```csharp
void ApplyOverlayStepFunction(LineSeries series, BestDayWindow window)
{
    for (int i = 0; i < series.Points.Count; i++) {
        var p = series.Points[i];
        var t = DateTimeAxis.ToDateTime(p.X);
        double newY = (t >= window.Start && t <= window.End) ? window.Floor : 0;
        series.Points[i] = new DataPoint(p.X, newY);   // in place
    }
    plotView.InvalidatePlot(false);
}
```

Backup snapshot stays as `Dictionary<LineSeries, double[]>`. Right-click
restoration walks it.

### Per-point tooltip

Set `TrackerFormatString` on the series and stash per-point text in
`DataPoint.Tag`-equivalent — actually OxyPlot's `DataPoint` is a struct
without a `Tag`. The cleaner pattern for TP is `LineSeries.TrackerKey`
plus a custom `IPlotView.Tracker` formatter, OR subclass `LineSeries` to
hold a parallel `string[]` of tooltips and override `GetNearestPoint`.

Per-series tooltip (the Day series's "best D-hour window" summary in TP's
`ComposeDayTooltip`) is just a single `TrackerFormatString` on that series.

### Color palette

`OxyColor` instead of `System.Drawing.Color`. Same 12-entry palette, just
re-typed. No semantic change.

---

## Open questions / next steps

1. **Per-point tooltip in OxyPlot.** The `DataPoint`-is-a-struct issue
   means TP can't use the `series.Points[i].ToolTip = ...` pattern
   directly. The cleanest workarounds are: (a) subclass `LineSeries` to
   carry parallel `string[]` tooltips, or (b) use `TrackerFormatString`
   with a custom formatter that looks up by index. Prototype will
   confirm which is cleaner.

2. **Sky sub-mode Y-axis inversion.** Today `BuildDaySkySeries` does
   `plotY = (SkyAxisMinMag + SkyAxisMaxMag - mag)` and uses
   `CustomLabels` to relabel ticks. OxyPlot has `Axis.IsAxisVisible = true,
   StartPosition = 1, EndPosition = 0` for reversed direction, *or* the
   same data-inversion trick. The data-inversion path keeps the X axis
   at the bottom (the only reason TP avoids `IsReversed`). Both work.

3. **Per-area axis re-application.** TP's `SetChartAreaAxis` reapplies
   axis config every time the area is shown. With one `PlotModel` per
   area held permanently, axes are configured once at construction and
   never need re-applying. Slight simplification.

4. **Designer integration.** Verify `OxyPlot.WindowsForms.PlotView`
   appears in the WinForms Toolbox after NuGet install; the current
   chart is hand-instantiated in `InitializeDynamicControls` so this is
   a non-issue, but worth eyes-on.

5. **Prototype scope.** A small standalone WinForms harness with one
   `PlotView` and a button cycling through Day / Year / Sessions, plus
   the four representative interactions (now-line, legend toggle,
   click-overlay, right-click restore). ~200 LOC. Confirms ergonomics
   before committing to the Phase 4 refactor. Build the prototype
   csproj as multi-target (`<TargetFrameworks>net481;net10.0-windows</TargetFrameworks>`)
   so we exercise the dual-target story up front — if OxyPlot.WindowsForms
   2.2.0's computed-compat `.NET 10` story breaks, this surfaces it
   before TP migration time.

6. **ScottPlot v5.1.58 verification spike.** Independent 30-line
   harness — drop a `FormsPlot` on a Form, add a `Scatter` series, build
   on `net481`. Confirms whether issue #3526 is fixed in v5.1.58. If
   yes, ScottPlot becomes a defensible alternative; if no, OxyPlot is
   the only candidate.

---

## Future: extract a shared `Astronomy.Charts` library

The LC2 prototype's `CurveHitTester` (and likely `LegendHitTester`) are
already library-ready: pure-logic, generic over the point type via
`Func<T, double?>` selectors, no UI / chart-library dependency, full
XML doc, sentinel-filter parameter, segment-index in return, defensible
bracket-vs-interp semantics.

**Deferred** until a second consumer materializes. Promotion trigger:
when XisfManager (image preview hover), IS, ISP, or ISS needs chart
hit-testing — at that point each would otherwise copy the file, which
is exactly the deduplication-vs-overhead tipping point. Until then it
lives in TP (introduced during Phase 4 of the SoC refactor when the LC2
patterns from the prototype get ported into TP's actual chart layer).

**Out of scope for the library**: `OverlayController`,
`HoverTooltipController`, `LegendClickHandler`. These are
WinForms-coupled (System.Windows.Forms.ToolTip / Timer / TextRenderer)
and LiveCharts2-coupled (CartesianChart, ObservablePoint), so they're
patterns to copy per-consumer rather than library code. Only the pure
hit-test math promotes cleanly.

**Path when extracting:** new `Astronomy.Charts` library (peer of
`Astronomy.Core`, also `netstandard2.0`), single file
`HitTesting/CurveHitTester.cs` initially, ~60 LOC. Consumers add a
`ProjectReference`. Sibling helpers (`PolylineDistance`, axis-range
math, etc.) accumulate over time; the library starts small and
self-evident, just like `Astronomy.Core` did.

---

## Sources

- [OxyPlot homepage](https://oxyplot.github.io/)
- [OxyPlot.WindowsForms 2.2.0 NuGet](https://www.nuget.org/packages/OxyPlot.WindowsForms)
- [OxyPlot annotations doc](https://oxyplot.readthedocs.io/en/latest/models/annotations/)
- [OxyPlot PlotControllerExamples.cs](https://github.com/oxyplot/oxyplot/blob/develop/Source/Examples/ExampleLibrary/Examples/PlotControllerExamples.cs)
- [ScottPlot 5 .NET Framework 4.8 issue #3526](https://github.com/ScottPlot/ScottPlot/issues/3526)
- [ScottPlot 5 WinForms Designer crash #2491](https://github.com/ScottPlot/ScottPlot/issues/2491)
- [LiveCharts2 WinForms CartesianChart docs](https://livecharts.dev/docs/WinForms/2.0.0-rc1/CartesianChart.Cartesian%20chart%20control)
- [LiveCharts2 click events discussion](https://github.com/beto-rodriguez/LiveCharts2/discussions/260)
