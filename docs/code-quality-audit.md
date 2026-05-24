# Code-quality audit — SoC / straight-line / dead-code

**Created 2026-05-19** by a three-agent read-only sweep of the architecturally-central
layers (`Forms/`, `State/`, `Caches/`, `Charts/`). Prompted by the standing directive:
promote SoC + reuse + straight-line code, kill dead params/branches, keep the
dispatch pipeline central.

**Headline:** the dispatch pipeline is clean — all three sweeps confirmed **zero
dispatch-bypass paths**; every UI handler funnels through `ChartCoordinator.Apply` /
`ApplyImmediateAsync`, and `EnsureAsync` is the sole staleness seam. No competing
diff path, no special-case dispatch branching. The findings below are **dead code**
(mostly Phase-7-revert residue) and **duplication** (mostly Charts mirror-pairs).

Re-grep each "zero call sites" claim at fix time before deleting. Leaf namespaces
(`Settings/`, `Support/`, `Filters/`, `Nina/`, `Horizons/`) were out of scope.

---

## Tier 1 — Dead-code sweep ✅ SHIPPED 2026-05-19 (commits `e64c9e0` + `54bc9f6`)

- [x] **Deleted the `UIState` class** — `Support/UIState.cs` removed; `mUIState` field,
  ctor init, the 4 view-radio-handler writes, and the `UpdateUI()` call + method all
  removed. The write-only class's boot textbox writes were already re-done by
  `SyncLocationUIFromModel` / `SyncTargetUIFromModel` (verified).
- [x] **Deleted `ChartEvaluation.FullChange`** — Phase-4 transitional scaffold.
- [x] **Deleted `ChartEvaluation.AnyChange`** — zero call sites.
- [x] **Deleted `IChartCacheStore.IsReady`** — interface + `ChartCacheStore` impl.
- [x] **Deleted the no-arg `PushSkyKSInputs()` overload** — zero callers.
- [x] **`OverlayController`: `RestoreAll` → private + status callback wired live.**
  `RestoreAll` is now private; the Day chart wires `reportStatus` to
  `Log.Diag("Overlay", ...)` so the 7 HD-overlay diagnostic strings land in `tp.log`;
  the stale class doc (right-click → `ToggleAll`, not `RestoreAll`) is fixed.
- [x] **`HoverTooltipController`: dropped the dead `hoverY` delegate param** (6 → 5 args)
  and replaced `mShownSeries` with a plain `bool mTooltipVisible`.
- [x] **`ShowCheckBoxObjectToolTip`: dropped the per-mouse-move ToolTip-delay
  re-assignment** — the values are set once in `MainForm_Load`.

## Tier 2 — Small dedup ✅ SHIPPED 2026-05-19 (commit `b60ef97`)

- [x] **`HarvestCheckedTargets()` helper** — extracted; `CheckedToggleDebounce_Tick`
  and `Button_CheckedTargets_Click` both call it instead of inlining the loop.
- [x] **`OnAvoidanceEnableChanged` → `BuildProfileFromControls()`** — the inline
  6-control-read copy replaced with the existing helper call.
- [x] **`ApplySiteHorizon(path)` helper** — folded the `mLocalHorizon` /
  `UpdateHorizonPathLabel` / `ConfigureHorizonWatcher` triple from all three
  horizon sites (2 clear + 1 load) into one helper. Broader than the originally
  scoped `ClearSiteHorizonState` — covers the named-site load path too.
- [x] **`DayWindowKey.ChartStartUtc` computed property** — added; the two
  `ChartCacheStore` consumers read it instead of re-wrapping the ticks.
- [x] **`OverlayController.ClearAll` → delegates to `PruneStaleBackups(empty)`** —
  one source of truth for the reset field-list. The two distance constants got
  cross-reference comments (deliberately different tolerances: 5° click vs 1.5°
  hover) rather than a physical move to `ChartLayout`, which would have split
  each constant from its owner class.

## Tier 3 — Slim `ChartEvaluation` ✅ SHIPPED 2026-05-19 (commit `3fbd772`)

- [x] **Slimmed `ChartEvaluation` to its one live field + dropped the dead `eval`
  parameter from `Render`.** A consumer audit found the four axis flags
  (`LocationChanged` / `TargetsChanged` / `HdmChanged` / `DayModeChanged`) were
  diag-only and the three keys (`DayKey` / `HdmKey` / `DayMode`) had zero reads.
  Only `BrightnessInputsChanged` is load-bearing — and its consumer is the
  coordinator's post-apply hook, not `Render`. So: `ChartEvaluation` collapsed to
  `{ BrightnessInputsChanged }`, and `IAltitudeSubChart.Render(ctx, cache, eval)` →
  `Render(ctx, cache)` across the interface + four sub-charts + `MainForm.RenderArea`
  + the coordinator delegate. Tier 6's Day-overlay work re-introduces a staleness
  param to `Render` when it has a real consumer.

## Tier 4 — Charts mirror-pair dedup ✅ SHIPPED 2026-05-19 (`4b4981c` / `53227f0` / `cfbd45c` / `b03fe3b`)

Day↔Sky and Year↔Sessions carried large copy-paste blocks; four clean helper
extractions removed them without the separately-future-flagged Day+Sky merge.

- [x] **`DuskDawnGradient`** — new `Charts/DuskDawnGradient.cs` owns the dusk/dawn
  gradient (constants, the two `RectangularSection`s, the fraction math, the resize
  shader re-kick); Day + Sky route through it.
- [x] **`MoonOverlay`** — new static `Charts/MoonOverlay.cs`; `BuildSeries` takes a
  `Func<double,double>` Y-mapper for the one Day/Sky difference, `ComputeAltitudesInline`
  is the shared cache-miss fallback.
- [x] **`ChartLayout` axis factories** — `MakeTimeXAxis` / `MakeMonthXAxis` /
  `MakeAltitudeYAxis` + `FormatZonedAxisLabel`; all four sub-charts route through them
  (Sky's inverted-magnitude Y axis stays inline — single consumer).
- [x] **`ChartLegendPanel`** — new `Charts/ChartLegendPanel.cs` owns the external
  clickable legend across all four sub-charts; the `LegendEntry` struct carries the
  per-chart 1-vs-3-series toggle. Fixed the drift — Day's legend `ForeColor` is now
  conditional on visibility like the other three.

## Tier 5 — Cache dedup ✅ SHIPPED 2026-05-19 (`f912e71` / `7973dca`)

- [x] **`ComputeOneFit`** — extracted the shared `ResolveCandidates → PlaceBest →
  Floor/Ceiling → PlaceCentered` recipe; `ComputeNightFits` (per-night loop) and
  `ComputeTonightFit` (single Starting window) both call it. A placement-strategy
  change is now edited once.
- [x] **Generic `CacheAxis<TKey,TVal>`** — new `Caches/CacheAxis.cs` collapses the
  four near-identical axes (yearDays / fits / day / moon) into one generic owning
  `store` + `inFlight` + the get / build / dedupe / publish lifecycle on the shared
  `mGate`; the four `BuildXxxEntryAsync` compute bodies stay distinct, wired in as
  `build` delegates. All four axes went generic — `BuildFitEntryAsync` reconstructs
  its horizon profile from the `HdmKey` (`LocalHorizon ?? new ScalarHorizonProfile(
  HorizonDeg)`), so the fits axis needs no extra parameter; that retired the
  `IHorizonProfile horizon` arg from `PrepareFitsAsync` and the four now-internal
  `Get*OrBuildAsync` methods from `IChartCacheStore`. Net ~120 lines removed.

## Tier 6 — SoC restructure ✅ SHIPPED 2026-05-19 (`7aa941d` / `fa85123`)

Two of the four items shipped; two were re-evaluated against the post-Tier-3/4/5
code and **declined** — both would have added code + coupling, not removed it.

- [x] **`ComputeDiff` extracted from `EnsureAsync`** — new pure
  `ComputeDiff(prev, ctx, prevUtc) → CacheDiff` static + a `CacheDiff` record
  struct; `EnsureAsync` is now a clean orchestrator (capture snapshot → diff →
  SetLocation/Prepare → CAS-stamp → return).
- [x] **`Render`-body cross-chart dedup** — extracted the three byte-identical
  scaffolding blocks: `ChartLayout.SwapSeriesDict<TVal>` (the persistent-dict
  swap, all four sub-charts), `MoonOverlay.FetchOrCompute` (the Day/Sky moon
  cache-fetch + fallback), `ChartLayout.ApplyMonthGrid` (the Year/Sessions
  month-grid block). The per-target loop bodies stay per-chart — no `RenderBase`.
- [~] **Day's `mLastDayKey` → cache `eval` flag — DECLINED.** The premise went
  stale: Tier 3 deliberately removed `ChartEvaluation` from `IAltitudeSubChart.
  Render`. Re-threading it onto all four sub-charts to serve Day's self-contained
  4-line `mLastDayKey` shadow is net *more* code and re-special-cases `Render` —
  against the SoC directive. The shadow stays where it is: local to its one
  consumer, correct, self-documented.
- [~] **`RestartSessionsRebuildDebounce` → coordinator callback — DECLINED.**
  Inspection showed it can't move cleanly: the debounce is a legitimate
  trailing-edge gate for a *UI-level* reset (`ResetForLocationChange` clears the
  target selection), and relocating it needs either a global coordinator-debounce
  bump or a debounce-inside-the-hook — no net simplification. Left as-is.

## Lower-priority / cross-file

- [x] **Fixed 2026-05-24.** `OnGridCellClick` (`EditFiltersForm.cs`) gained the missing
  `CenterNm` field copy to align with `OnFilterDefaultsClick`'s coverage; the latter
  simplified to use the record `with` expression (`builtin with { Name = current.Name }`)
  now that `Filter` is a positional record. Drift fixed + both callsites at their
  simplest local pattern.
- [x] **Fixed 2026-05-24.** `Charts/FitTooltipResolver.cs` extracted; the shared
  5-line lookup pattern (`HdmKey` from `mLastCtx`, `GetFitOrNull(target, hdm)`,
  null-safe + segment-bound NightFit pick) now lives in one place. Year + Sessions
  tooltip formatters call `FitTooltipResolver.ResolveFit(...)` instead of inlining.
  Per-class `mLastCtx` + `mLastCache` fields stay (legitimate per-instance Render
  snapshot state, not the part that was duplicated).
- [x] **Fixed 2026-05-24.** View-radio handler boilerplate dissolved in
  `MainForm.ChartBuildPresenter.cs` (which absorbed the handlers from
  `MainForm.SortPresenter.cs` during the 2026-05-22 extraction). New private helper
  `OnViewRadioCheckedChanged(RadioButton)` owns the Log.Diag + uncheck-side-no-op +
  `mCoordinator.Apply(SnapshotCurrent())` shape. Day keeps its own pre-helper line
  for the `CheckBox_Sky.Enabled` sub-mode gate; Year + Sessions are one-line
  delegates.

## Doc drift caught in passing

- [x] **Fixed 2026-05-19.** `ARCHITECTURE.md` lines 46 + 262 + 263 described a
  `TargetSelection.Mode` property / `SetMode(GraphMode.Multi)` that does not exist
  — the VM has no `Mode`; `Button_Graph` renders `SelectedSingle` and the checked-set
  is an independent debounce-driven view. Corrected in commit alongside this audit.
