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

## Tier 4 — Charts mirror-pair dedup (the dominant theme; M–L)

Day↔Sky and Year↔Sessions carry large copy-paste blocks. None of these need the
separately-future-flagged Day+Sky chart merge — each is a clean helper extraction.

- [ ] **Extract `DuskDawnGradient` helper** (M, low) — the `YellowOpaque/Faded`
  constants, `mDusk/DawnSection` fields + ctor init, `UpdateGradientSections` (the
  24-line normalized-fraction math), and `OnChartSizeChanged` shader-kick are
  byte-identical Day↔Sky (`AltitudeSubChart_Day.cs:46-47,672-711` ↔ `_Sky.cs` equiv).
- [ ] **Extract `MoonOverlay` helper** (M, low) — `BuildOrUpdateMoonSeries` +
  `ComputeMoonAltitudesInline` are identical Day↔Sky except one Y-mapping line. Helper
  takes a `Func<double,double?> altitudeToPlotY`.
- [ ] **`ChartLayout` axis factories** (M, low) — `MakeUtcHourAxis` (Day↔Sky time axis
  + the shared `AxisTimeLabel`), `MakeMonthAxis` (Year↔Sessions), `MakeAltitudeYAxis`.
- [ ] **Extract `ChartLegendPanel`** (L, med — user-visible, needs visual verify) —
  `MakeLegendItem` + `BuildLegendItems` + `RecomputeLayout` + the `FlowLayoutPanel`
  ctor block + `IdealHeight`/`IdealHeightChanged` are duplicated across **all four**
  sub-charts and have **already drifted** (Day seeds legend `ForeColor` unconditionally
  LightGray; Sky/Year seed it conditionally on `IsVisible`). Biggest line-count win.

## Tier 5 — Cache dedup (M–L)

- [ ] **Extract `ComputeOneFit`** (M, low) — `ComputeTonightFit` (`ChartCacheStore.cs:961-1000`)
  is, by its own comment, the single-night equivalent of `ComputeNightFits`' loop body
  (`888-953`). Same `ResolveCandidates → PlaceBest → Floor/Ceiling → PlaceCentered`
  recipe; a placement-strategy change must currently be made twice.
- [ ] **Generic `CacheAxis<TKey,TVal>`** (L, med — biggest structural win, do when a 5th
  axis looms) — the four `Get*OrBuildAsync` blocks and three `Prepare*Async` methods
  (`ChartCacheStore.cs` yearDays/fits/day/moon) are near-identical dedupe + progress-tick
  boilerplate (~120 lines). A `CacheAxis` holding `store` + `inFlight` + a `build` func
  collapses them; the four `BuildXxxEntryAsync` compute bodies stay distinct. Moon is
  keyed by `DayWindowKey` not `(Target,*)`, so the generic keys on `TKey`.

## Tier 6 — SoC restructure (do after Tier 4 helpers land)

- [ ] **Extract `ComputeDiff` from `EnsureAsync`** (M, low) — `EnsureAsync`
  (`ChartCacheStore.cs:333-473`) does staleness-diffing AND prep-orchestration in one
  140-line method. Pull out a pure `ComputeDiff(prev, ctx, prevUtc, dayKey) →
  ChartEvaluation`; `EnsureAsync` stays the orchestrator. Pairs with Tier 3.
- [ ] **Day's `mLastDayKey`/`dayKeyChanged` → consume a cache-provided flag** (M, med) —
  Day (`AltitudeSubChart_Day.cs:130,460-461,567`) shadows `mLastDayKey` to re-derive
  "did the altitude data change?" — staleness the cache already owns. Have the cache
  set a `DayKeyChanged` bool on `ChartEvaluation`; Day reads it. This is a *legitimate*
  `eval`-flag consumer (gates HD-overlay backup bookkeeping, not paint — sidesteps the
  LC2 paint-instability that killed Phase 7), and it makes the `eval` param genuinely
  live, retroactively justifying keeping it on `Render`.
- [ ] **Slim the `Render` bodies** (L, med — do last) — each sub-chart's `Render` is a
  60-178-line procedure mixing 6+ concerns. Once Tier-4 helpers exist, each `Render`
  shrinks to: resolve window → update furniture → per-target loop → commit. Don't
  attempt a shared `RenderBase` template-method (the four per-target loops genuinely
  differ); just extract the non-looping scaffolding. Add a `SwapSeriesDict` helper for
  the repeated `newDict → Clear → copy` dance.
- [ ] **`RestartSessionsRebuildDebounce`** (M, med — documented wart, lower priority) —
  `MainForm.cs:994-1039` is a second debounce timer in front of the coordinator's own
  150 ms debounce; it exists only to gate one `LocationsCacheEquivalent` keying
  decision. Consider moving the keying check to a coordinator post-apply callback.

## Lower-priority / cross-file

- [ ] `OnGridCellClick` / `OnFilterDefaultsClick` both inline the filter-from-builtin
  field copy (`EditFiltersForm.cs:177-183` / `FilterMenuPresenter.cs:241-250`) — already
  drifted (one copies `CenterNm`, the other doesn't). M, low — cross-file.
- [ ] Year/Sessions `mLastCtx` + `mLastCache` shadow fields — benign; fold into a shared
  tooltip base if one ever materialises. S, low.
- [ ] View-radio handler boilerplate (`MainForm.SortPresenter.cs:2157-2199`) — mostly
  dissolves once Tier-1 removes the `mUIState` writes.

## Doc drift caught in passing

- [x] **Fixed 2026-05-19.** `ARCHITECTURE.md` lines 46 + 262 + 263 described a
  `TargetSelection.Mode` property / `SetMode(GraphMode.Multi)` that does not exist
  — the VM has no `Mode`; `Button_Graph` renders `SelectedSingle` and the checked-set
  is an independent debounce-driven view. Corrected in commit alongside this audit.
