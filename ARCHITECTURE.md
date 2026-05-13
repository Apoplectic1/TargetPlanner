# TargetPlanner architecture

Deep architecture reference for `TargetPlanner`. Read this when editing `Charts/`, `Caches/`, `State/`, `Filters/`, or any of the chart-pipeline paths in `Forms/MainForm.cs`. For the high-level overview (repo split, SoC phases, threading model), the **Glossary**, the **Conventions worth knowing before editing**, and the **Core consumer contract**, see [CLAUDE.md](CLAUDE.md). User-facing behaviour, defaults, and chart UX live in [README.md](README.md).

The codebase is split at the repo boundary: **`Astronomy.Core`** lives in the sibling `Library\` repo and provides pure, UI-free astronomical math plus POCOs; **`TargetPlanner`** (this repo) is the WinForms chart/host/UI on top, consuming Core via `ProjectReference`. The TP-side architecture has been through a three-phase SoC refactor (commits `0f6c81c` / `1e1986d` / `3425f8e`) plus a follow-on cache-completion pass (lifts per-(target, H/D/M) fit compute out of the sub-charts and into `ChartCacheStore` — design rationale at [`docs/design/chart-fits-cache.md`](docs/design/chart-fits-cache.md)). Chart construction is decoupled from cache construction, cache state is decoupled from selection state, selection state is decoupled from UI controls, and **sub-charts are now render-only synchronous painters** that read pre-built fits from the cache. Threading model: UI thread owns form events / VM mutations / render passes / chart paint; cache worker (`Task.Run` inside `ChartCacheStore.GetOrBuildAsync` and `GetFitOrBuildAsync`) owns per-target year-cache builds, per-Location `NightCache` build, and per-(target, HdmKey) fit builds; NINA loader (`Task.Run` inside `MainForm.GetNinaTargets`) owns file enumeration + JSON parse plus the post-load `PrepareManyAsync` + `PrepareFitsAsync` warmup.

## `Astronomy.Core` — pure math, no UI (source in `..\Library\Astronomy.Core\`)

Shared library targeting `net10.0` with `<Nullable>enable</Nullable>` and `LangVersion latest` (C# 14 today), authored in the sibling `Library\` repo at `E:\Projects\VisualStudio\Astronomy\Library\Astronomy.Core\`. Consumed today by TargetPlanner via `ProjectReference`; designed to be consumed by XisfManager / IS / ISP / ISS without re-porting. The whole portfolio (TP, Astronomy.PCL, Astronomy.Core.Tests, NINA upstream, planned ISP) is uniformly net10 as of 2026-05-04 — netstandard2.0 was retired once NINA migrated and the cross-runtime bridge was no longer load-bearing. See `SCHEDULER_DESIGN.md` (still in this repo) for the full design rationale. The surface summary below is a convenience for readers working on TargetPlanner chart code -- the Library repo is the source of truth for API specifics. Surface by subfolder:

- **`Targets/Target.cs`** — **immutable** POCO. Every property is read-only; mutations produce a new instance via `With(...)`. `RightAscension` is **decimal hours** `[0, 24)`; `RaHours` / `RaMinutes` / `RaSeconds` are get-only DMS-component accessors computed on read (not a duplicate store). `Declination` is stored as a non-negative magnitude; the constructor normalizes a negative value by flipping `North` and storing `abs(value)`. `Target.Default` returns M31 defaults. Does **not** carry any chart / WinForms state.
- **`Locations/Location.cs`** — **immutable** POCO. `With(...)` returns a new instance. Latitude / longitude stored as non-negative magnitudes with direction in `North` / `West` flags; a negative magnitude passed to the ctor flips the corresponding flag. D/M/S accessors (`LatDegrees` etc.) are computed on read. Also carries `Horizon` (degrees), `Duration` (minimum time required above horizon for "Sessions" chart), `DateTime`, `TimeZoneInfo`. `Location.Default` returns neutral, ship-safe placeholder values (40°N/75°W); consumer apps resolve their actual site via their own configuration layers (e.g. TargetPlanner's `PersonalDefaults` + `SettingsStore`).
- **`Night/NightWindow.cs`** — struct: `AstronomicalDawn`, `AstronomicalDusk` (both `DateTimeKind.Utc`), `LunarIlluminationFraction`, plus `IsValid` (short-circuits the `DateTime.MinValue` polar-day / polar-night sentinel). See the `Core consumer contract` section in CLAUDE.md for the Kind=Utc rationale and the DST trap it closes.
- **`Time/JulianDate.cs`, `Time/SiderealTime.cs`** — JD from UTC; GMST from JD; LST from UTC + east-longitude.
- **`AltAz.cs`** — `AltAzCalculator.At(target, location, utc)` and `AltAzCalculator.Of(target, location)` (reads `location.DateTime.ToUniversalTime()`). Returns an `AltAz` readonly struct with `Altitude` and `Azimuth` properties (both degrees; azimuth from North, clockwise). Replaces the old `GetAltitudeAzimuth` / `Tuple<double,double>` pattern.
- **`TargetGeometry.cs`** — `MeridianAltitude`, `LowerCulminationAltitude`, `HourAngleAtAltitude` (returns `NaN` for never-reaches, `+Infinity` for always-above), `AltitudeAtHourAngle`, `AzimuthAtHourAngle`. All take signed-degrees lat/dec.
- **`Night/NightCalculator.cs`, `Night/TwilightCalculator.cs`** — `ComputeNight(location)` (−18° astronomical default) and `TwilightCalculator.ComputeNight(location, sunAltBelowDeg)` for the three standard thresholds (−18 / −12 / −6). Both compute dawn/dusk via in-house Meeus solver in `AstroUtil.GetSunRiseAndSet` — UTC instants (`DateTimeKind.Utc`), no DST-transition trap.
- **`Horizons/IHorizonProfile.cs`** + `ScalarHorizonProfile`, `PolylineHorizonProfile`, `ObstructionTableHorizonProfile` — abstraction over a horizon altitude function `AltitudeAt(azimuth)`. Scalar wraps the legacy single-double case.
- **`Session/`** primitives (built for the planned interval scheduler; not yet consumed by the TargetPlanner chart):
  - `TransitTime.UtcAtOrAfter` — analytic LST=RA inverse.
  - `IntegratedQuality.OverSession` (Simpson, 20 points) + `IntegratedQuality.SinAltitudeOverSession` (closed form).
  - `VisibilityWindows.For` — above-horizon ∩ night.
  - `BestSession.For` — transit-centered-or-wall-pushed placement across visibility windows. Optional `MoonAvoidanceProfile profile = null` parameter; when non-null and enabled, the candidate windows are intersected with moon-clear sub-intervals via a 10-min Lorentzian sweep before placement. `null` / `Disabled` falls through to the legacy moon-blind code path byte-for-byte (the backwards-compat hinge for the Day-chart slice).
  - `QualitySamples.OverNight` — slot-size grid.
  - `RiseSet.NextAtOrAfter` — scalar analytic and `IHorizonProfile`-aware (scalar seed + bisection refine).
- **`Moon/MoonSeparation.cs`** — `DegreesAt` (topocentric target-moon angle), `IntervalsAboveDeg` (night intervals above a fixed threshold), and `ObserveAt` (returns `(SeparationDeg, MoonAltDeg, MoonAzDeg)` in one Meeus pass — uses the new `AstroUtil.GetMoonAltAz` to avoid the `MoonPosition.Topocentric`-twice penalty the chart-cache prepare loop used to pay; `DegreesAt` is now a thin wrapper around `ObserveAt`).
- **`Moon/MoonAvoidance.cs`** — `MoonAvoidanceProfile` POCO (immutable, `With(...)` mutators following the `Target` / `Location` pattern; `Disabled` / `Narrowband` 60°/7d / `Broadband` 120°/14d / `Custom(...)` factories) plus pure decision primitives: `LorentzianRequiredSep` (matches the NINA Target Scheduler reference at `AstrometryUtils.cs:126` to 1e-12), `IsRejected` (full decision incl. TS-style relaxation-zone -- MoonDown override intentionally omitted), `RequiredSepWithRelax` (introspection helper).
- **`Moon/LunarAge.cs`** — `DaysAt(utc)` returns days since the most recent new moon via JD math (reference epoch 2000-01-06 18:14 UT, JD 2451550.2597222). Closed-form synodic-cycle estimate; sub-second accurate at the reference, well within Lorentzian tolerance.

## `TargetPlanner` — WinForms host

### UI state facade (`Support/AstrometryUi.cs`)

Thin static class with **mutable static state** (`AstronomicalDawn`, `AstronomicalDusk`, `SunAltitude`, `LunarAltitude`, `LunarPhase`, etc.) populated by `AstrometryUi.Location(mLocation)`. MainForm binds its dawn/dusk/moon-phase labels to these. The math that used to live here moved to Core; `AstrometryUi.Location(...)` remains to populate the static cache and to roll dawn/dusk forward or backward by a day so the pair always brackets the coming night.

### Target ingestion (`Nina/TargetLoader.cs`, namespace `TargetPlanner.Nina`)

`TargetLoader.Load(rootFolder, IProgress)` enumerates every `.json` in the root plus every subfolder except `Calibration` and `Mosaics`, parses each as a NINA `DeepSkyObjectContainer`, and converts its sexagesimal `InputCoordinates` (RAHours/RAMinutes/RASeconds, DecDegrees/DecMinutes/DecSeconds, `NegativeDec`) into `Astronomy.Core.Targets.Target` POCOs. Malformed files are skipped silently. Called from `MainForm.GetNinaTargets` inside `Task.Run` with progress reported to `ProgressBar_ProcessObject`. The root is a single constant `MainForm.NinaTargetsRootPath`, used for both the startup seed and the Browse-Target-List dialog's `InitialDirectory`.

### Selection VM (`State/TargetSelection.cs`)

Phase 2 of the SoC refactor (commit `1e1986d`). Observable view-model with four event-bearing properties: `KnownTargets`, `SelectedSingle`, `Checked` (HashSet), `Mode` (`Single` / `Multi`). Mutators imply `Mode` as a side effect: `SetSelectedSingle` → Single, `SetChecked` / `SetCheckedSet` / `SetAllChecked` → Multi. `SetKnownTargets` resets `Checked` to empty (default-none-checked policy: the user opts in target-by-target rather than opting out via Clear-All) and preserves `Mode` (load doesn't flip the user's last-touched mode). Events fire only on actual change (reference equality for `SelectedSingle`, set equality for `Checked`).

The form's `WireSelectionVm` subscribes the VM events to UI-update handlers (`OnVm*Changed`) and routes UI events into VM mutators (`OnCheckedListBoxItemCheck` / `OnCheckedListBoxSelectedIndexChanged` / `OnRightAscensionEdited` / `OnDeclinationEdited` / `Button_*Click`). The single `mUpdatingUiFromVm` flag short-circuits VM-driven UI writes so they don't re-enter the VM. **The legacy `mSuppressGraphModeEvents` / `mCheckedListBoxClickFiredHandler` flags + `WireGraphModeEvents` / `WireSingleMode` / `WireMultiMode` / `MarkSingleMode` / `MarkMultiMode` / `SyncTargetComboFromCheckedListBoxHighlight` helpers are gone.** The one remaining latch (`mCheckedListBoxJustToggled`) disambiguates ItemCheck-then-SelectedIndexChanged on the same user click — required because both fire when toggling a checkbox.

### Cache store (`Caches/`)

Phase 3 of the SoC refactor (commit `3425f8e`) plus the follow-on fit-cache completion. Seven files:
- `MoonSample` — public struct `(Utc, SepDeg, MoonAltDeg)`. Promoted from a private nested type in `AltitudeSeries`.
- `NightCacheEntry` — public class holding per-target per-night precomputes (`Dusk` / `Dawn` / `LstDusk` / `LstDawn` / `AltDusk` / `AltDawn` / `TransitInNight` / `YearAlt` / `IsPolar` / `SentinelX` / `MoonSamples` / `MoonAgeDays`). Promoted to public.
- `TargetCacheEntry` — owns `Target` + `IReadOnlyList<NightCacheEntry>` (the 365-day series).
- `TargetFitEntry` — owns `Target` + `HdmKey` + `IReadOnlyList<NightFit>` (per-night Ceiling / Floor / CenteredFloor triple, all `double?`). Index-aligned with `TargetCacheEntry.YearDays`.
- `IChartCacheStore` — interface. **yearDays axis**: `IsReady(t)` / `GetOrNull(t)` (sync, lock-free reads) / `GetOrBuildAsync(t)` (in-flight de-duping; one underlying compute per target) / `PrepareManyAsync(targets, IProgress<int>? = null)` (parallel bulk build with optional 1-based per-target completion ticks). **Fits axis**: `GetFitOrNull(t, HdmKey)` / `GetFitOrBuildAsync(t, HdmKey)` / `PrepareFitsAsync(targets, HdmKey, IProgress<int>? = null)`. Lifecycle: `SetLocationAsync(loc)` (drop both caches + switch). UI-thread-marshalled `TargetReady` event. **No `CancellationToken` parameters on any method** — stale work runs to completion and discards via a publish-time location check (see Cancellation policy below).
- `ChartCacheStore` — default implementation. `Task.Run`-backed per-(target) and per-(target, HdmKey) builds. Owns the heavy chart-cache compute paths: `ComputeYearDays` (the ~25,600-call-per-target moon-sample sweep, builds `mEntries[Target]`) and `ComputeNightFits` (per-night `BestSession.ResolveCandidates` + `PlaceBest` + `PlaceCentered` + `SessionAltitude.{Floor, Ceiling}`, builds `mFits[(Target, HdmKey)]`). One `ResolveCandidates` resolve drives both placements per night (~25% fewer Meeus calls than the old separate Year / Sessions paths). **No cancellation infrastructure** — `mLocationCts` / `WithExternalCancel` removed in the cache-completion pass; in-flight stale builds are detected at publish via `ReferenceEquals(mLocation, location)` and silently discarded.

`HdmKey` (`State/HdmKey.cs`) — readonly struct keying the fits cache. Fields: `HorizonDeg`, `DurationTicks`, `Profile` (reference identity, since `MoonAvoidanceProfile` is immutable), `FilterCenterNm`. Surfaced as `ChartContext.Hdm` (derived property). Bortle / ExtinctionK are intentionally excluded — they affect Sky's K-S brightness path, not fit decisions.

`MainForm` instantiates `mCache = new ChartCacheStore(mLocation, SynchronizationContext.Current)` before the sub-charts (the SyncContext lets the cache marshal `TargetReady` to the UI thread), threads the cache reference into each sub-chart's `Render(...)` call, and after each NINA load fires `_ = Task.Run(async () => { await mCache.PrepareManyAsync(allLoaded); await mCache.PrepareFitsAsync(allLoaded, currentHdm); })` for background warmup. `Button_Graph_Click` calls `ApplyImmediateAsync` with the progress handler returned by `BeginChartBuildProgress` so the per-target completion ticks (yearDays then fits) drive `ProgressBar_MultiTargetProcessing`. Location-edit paths route through `LocationsCacheEquivalent` (compares Lat / Lon / North / West / Elevation / `NightCache.ComputeYearStartDay`) and call `mCache.SetLocationAsync` only when geometry actually changed — Horizon / Duration scrubs preserve the year-cache but invalidate the fits cache via a new `HdmKey`.

### Orchestration layer (`State/ChartContext.cs`, `State/ChartCoordinator.cs`)

Phase 1 + Phase 2 of the orchestration-layer refactor (commits `b98912e` / `a267716` /
follow-on Phase 3). MainForm used to be the orchestrator-by-accident — every UI handler
embedded its own decision tree about what to invalidate, which debounce to restart,
which suppression flag to set, which sub-chart to refresh. Adding a control meant
re-deriving all of that.

The orchestration layer factors that out:

- **`ChartContext`** — sealed `record` snapshotting all chart-pipeline inputs:
  `Location` (which carries `Horizon`, `Duration`, `BortleClass`, `ExtinctionK`, `DateTime`,
  `TimeZoneInfo`), `Targets` (the effective render set), `MoonProfile`,
  `ActiveFilterCenterNm`, `ActiveArea`. Built by `MainForm.SnapshotCurrent(targets)`
  at one point in time and threaded through every Render / RefreshVisibility call so
  downstream code can't observe state drifting mid-render.

- **`ChartCoordinator`** — single funnel. Public surface: `Apply(ctx, progress=null)`
  (debounced 150 ms internally), `ApplyImmediateAsync(ctx, progress=null)` (no-debounce,
  awaitable), `Cancel` (stops the debounce; no in-flight cancellation — see below),
  `Dispose`. Holds `mLastAppliedByArea` (per-area last-successfully-applied snapshot,
  for diffing radio swaps), `mEverRendered` (areas that have been Render'd at least
  once), `mGeneration` (monotonic counter that drives supersession), `mPendingContext`
  + `mPendingProgress` (most recent debounced Apply, drained on tick).

  Pipeline flow (`RunPipelineAsync`):
  1. Capture `gen = ++mGeneration` at entry. Diff `prev` vs new for the active area:
     location-key (`LocationCacheEquivalent`), targets (reference-equality), HdmKey
     (`prev.Hdm != ctx.Hdm` — Horizon / Duration / MoonProfile / FilterCenterNm).
  2. If location-key changed → `await mCache.SetLocationAsync(ctx.Location)`.
  3. If `ctx.Targets` non-empty → `await mCache.PrepareManyAsync(ctx.Targets, progress)`
     then `await mCache.PrepareFitsAsync(ctx.Targets, ctx.Hdm, progress)`. Both are
     no-ops when their cache is warm; the second blocks the synchronous Render below
     until fits for the current `HdmKey` are built.
  4. **Generation guard**: if `gen != mGeneration` (a newer Apply has come in while
     we awaited) → return without writing any state.
  5. If `activeNeedsFullRender` (never rendered, or location/targets/HdmKey changed)
     → `Render` active sub-chart (synchronous; reads cache).
     Else → `ShowOnly` (flip Visible on the active sub-chart's Control).
  6. Post-apply hook: `RefreshAstrometryLabels` + `UpdateNowLine` + `UpdateHorizonLine`
     on every sub-chart + `PushSkyKSInputs(ctx)` for Sky's K-S walk (Bortle / ExtinctionK
     / Filter changes ride here without forcing a fit recompute).
  7. Stamp `mLastAppliedByArea[activeArea]` (full-render path) or every ever-rendered
     area (showOnly path).

  Supersession: instead of CTS-based cancellation, the coordinator uses the generation
  counter. Multiple pipelines may overlap during rapid scrubs (each captures its own
  `gen` at entry); the cache's per-(target, key) in-flight dedupe ensures the heavy
  compute isn't duplicated. When the older pipeline's awaits resolve, its `gen !=
  mGeneration` check causes it to bail before any side-effecting Render / ShowOnly /
  stamp call. Only the latest Apply's pipeline ever writes chart state. No exceptions
  thrown, no try/catch needed for supersession.

- **`MainForm.SnapshotCurrent(targets)`** — single point that reads `mLocation` /
  `mMoonAvoidanceProfile` / `mActiveFilterCenterNm` / `SelectedArea()` into a
  `ChartContext`. Adding a new chart input is one record-field addition + one read here.

UI handlers reduce to: build snapshot, hand to coordinator. A handful of paths still
have ancillary work (e.g. `Button_Now_Click` snaps the date/time pickers; `OnLocationEdited`
flips combo to "Custom"; `RunGraphBuildAsync` wraps the coordinator's `ApplyImmediateAsync`
in `BeginChartBuildProgress` / `FinishChartBuildProgress` for the progress-bar handler),
but the downstream cache + render dispatch is all coordinator-owned.

**Remaining legacy debounce:** `mSessionsRebuildDebounce` / `RestartSessionsRebuildDebounce`
/ `SessionsRebuildDebounce_Tick` exist solely for the `OnLocationEdited` keying-change
detection. Per the user spec, lat/lon/elev scrubs that cross `LocationsCacheEquivalent`
should clear the checked set + blank the chart (`ResetForLocationChange`), not auto-render
at the new geometry. Doing that decision per spinner tick is wrong (each intermediate
tick would clear checkboxes); doing it on the coordinator's debounce tick is also wrong
(the coordinator doesn't know about checkbox semantics). So `OnLocationEdited` keeps a
narrow 150 ms debounce whose tick decides between `ResetForLocationChange` (keying drift)
and `mCoordinator.ApplyImmediateAsync` (within-equiv scrubs that ride OnLocationEdited
— Bortle / ExtinctionK).

### Charting (`Charts/`)

Each chart area lives in its own LC2 sub-chart class implementing `IAltitudeSubChart`:
- `AltitudeSubChart_Day.cs` — single-night altitude curves; HD-overlay click-toggle via `OverlayController`; smooth-curve interpolated tooltip (300 ms debounce); shared moon series filled below the per-target altitude curves with alpha scaled by lunar illumination. **Fit-tonight-only filter:** targets without a D-hour window tonight are excluded from `mChart.Series` and the legend entirely (their altitude data is still computed into `mSeriesByTarget` so a subsequent H/D/M scrub that brings them back into fit can re-add them via `RefreshVisibility` without recomputing). Day's legend therefore reflects "what can I image tonight," independent of which boxes are checked in `CheckedListBox_SelectedTargets`. Sky / Year / Sessions stay alpha-0-toggle (no filter).
- `AltitudeSubChart_Sky.cs` — single-night K-S sky brightness curves (mag/arcsec², inverted-data Y axis 16–22 with `Labeler` mapping); per-DataPoint snap tooltip (30 ms). Owns `ActiveFilterCenterNm` (Rayleigh λ⁻⁴ scaling for K-S extinction) and `RefreshSkyBrightness(cache, location)` separate from the universal `RefreshVisibility` since Bortle / ExtinctionK / Filter scrubs change brightness without touching fit decisions.
- `AltitudeSubChart_Year.cs` — 12-month per-night sweep; **Y is session-floor altitude under current H/D/M** (worst-case in the placed D-hour window), not night-max. **Render-only**: reads `cache.GetFitOrNull(target, ctx.Hdm).Nights[i].Floor` per night and paints synchronously. No bg task, no `CancellationTokenSource`. Tooltips formatted on hover from cached `NightFit` + `yearDays[i].SentinelX`.
- `AltitudeSubChart_Sessions.cs` — 12-month per-night sweep with three curves per target: Ceiling / Floor / Symmetric. Same render-only pattern as Year, reading `Ceiling` / `Floor` / `CenteredFloor` fields off the cached `NightFit`. ONE legend item per target — click toggles all three series together. The shared-resolve optimization (`BestSession.ResolveCandidates` once driving both `PlaceBest` and `PlaceCentered`) lives in `ChartCacheStore.ComputeNightFits` rather than the sub-chart.
- `IAltitudeSubChart.cs` — common interface (`Control`, `IdealHeight`, `IdealHeightChanged`, `UpdateNowLine`, `UpdateHorizonLine`, `Render(ChartContext, IChartCacheStore)`, `Reorder`, `RefreshVisibility(ChartContext, IChartCacheStore)`). Snapshot-taking methods replaced the prior loose primitive parameter lists during the orchestration-layer refactor; `Render` lost its `CancellationToken` parameter in the cancellation-removal pass. MainForm holds `Dictionary<string, IAltitudeSubChart> mSubCharts` keyed by area ("Day" / "Sky" / "Year" / "Sessions"); coordinator dispatch resolves the active sub-chart by name. Forgetting any contract method on a new sub-chart is a compile error.
- `ChartLayout.cs` — shared template: `FixedPlotAreaHeight = 420`, chrome dimensions, legend padding, `ChartBackground`, `GridLineColor`, `TargetColorPalette` (12 colors). `DayChartStart(duskLocal)` / `DayChartStop(dawnLocal)` round dusk/dawn to the nearest enclosing integer hour for the Day / Sky minute-grid bounds.
- Controllers (`CurveHitTester`, `OverlayController`, `HoverTooltipController`) sit alongside the sub-charts. `HoverTooltipController` accepts an optional `CurveTooltipFormatter` delegate; per-DataPoint snap formatters use `segmentStart` to read pre-formatted text from a parallel `string[]` (the Sky / Year / Sessions pattern).

**Series identity preservation across renders.** Each sub-chart's `Render(...)` calls a per-target `GetOrCreateTargetSeries(...)` (or three-dict equivalent on Sessions) so `LineSeries<ObservablePoint>` instances persist across renders. This keeps `IsVisible` toggle state — the user's legend clicks — alive when the chart is re-rendered for a Graph-click, a sort change, or an H/D/M scrub. `Reorder(newOrder)` is the cheap path for sort changes: rebuilds `mChart.Series` + the legend in the new order without touching data (the cached fits already painted as Y values stay valid because the target SET is unchanged).

**Day chart click semantics** — three-mode handler (legend toggle / Day-curve overlay toggle / right-click restore-all). User-facing description in [README.md](README.md#chart-interactions). Implementation now lives in `OverlayController` (HD overlay state machine) + the custom legend's `Click` handler. Right-click fires `mOverlay.RestoreAll()`.

### MainForm chart wiring

Lives mostly in foreach loops over `mSubCharts.Values`:
- Live now-line: `foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(when)` from `DatePicker_ValueChanged` / `TimePicker_ValueChanged` / `Button_Now_Click`.
- Live horizon: same shape from `NumericUpDown_TargetFloor_ValueChanged` (Sky's `UpdateHorizonLine` is a no-op).
- Debounce tick: `foreach RefreshVisibility(...)` + `mLC2Sky.RefreshSkyBrightness(...)` + `mLC2Sky.ActiveFilterCenterNm = mActiveFilterCenterNm`.
- Graph-click + radio handlers: shared `RenderArea(ChartContext ctx)` helper that does dict lookup on `ctx.ActiveArea`, `ShowOnlyAltitudeChart(sc.Control)`, `sc.Render(ctx, mCache)`, and `ResizeAltitudeChartArea(sc.IdealHeight)`. Post-orchestration-refactor, `RenderArea` is called only from inside the coordinator's pipeline; the radio handlers reduce to `mCoordinator?.Apply(SnapshotCurrent(mLastRenderedTargets))` and the coordinator's diff sees `ActiveArea` changed → dispatches `Render` on the new active sub-chart.
- Sort callback: `foreach (var sc in mSubCharts.Values) sc.Reorder(sorted)` — no replot, no fit recompute.
- State that legacy `AltitudeChart` used to own now lives on MainForm: `mLastRenderedTargets`, `mMoonAvoidanceProfile`, `mActiveFilterCenterNm`. `SetActiveFilter` / `OnLorentzianControlChanged` / `OnAvoidanceEnableChanged` write these and call `mCoordinator?.Apply(SnapshotCurrent(mLastRenderedTargets))`. The coordinator's diff catches `MoonProfile` / `ActiveFilterCenterNm` change as HDM-style and refreshes visibility on every sub-chart; the post-apply hook re-walks Sky's K-S grid.

### Universal chart behavior contract

Every chart sub-area (Day / Sky / Year / Sessions; any future chart) MUST implement `IAltitudeSubChart`. The interface enforces the contract at compile time — forgetting any member on a new sub-chart is an error. Behaviors:

1. **Live now-line update.** `UpdateNowLine(DateTime now)` mutates the red section's X position in place (no data recompute). MainForm's three picker handlers (`DatePicker_ValueChanged`, `TimePicker_ValueChanged`, `Button_Now_Click`) iterate `foreach (var sc in mSubCharts.Values) sc.UpdateNowLine(when)`. No debounce — instant feedback.

2. **Live horizon-line scrub.** `UpdateHorizonLine(double horizon)` mutates the green section's Y position in place. `NumericUpDown_TargetFloor_ValueChanged` iterates the same way. Sky has no horizon line and the method is a no-op there; every other sub-chart implements it.

3. **Hide-on-no-fit on H/D/M scrub** (the "universal" hide rule). When the active Horizon, Duration, or MoonAvoidanceProfile leaves a target with no D-hour fit, the target's curve is hidden on every chart simultaneously. Each chart hides differently, and where the fit decision lives has moved post-cache-completion:
   - **Day** (single-night view, fit-tonight-only): per-target. Targets without a D-hour window tonight are excluded from `mChart.Series` and the legend entirely (not just alpha-0). `Render` re-evaluates the fit-tonight filter via one `BestSession.For` call per target on the live night (~10 ms total for 44 targets, synchronous). H/D/M scrubs hit the coordinator → `hdmKeyChanged` → full Render.
   - **Sky** (single-night view): per-target. Stroke alpha → 0 when no fit; restore palette alpha when fit. One `BestSession.For` call per target tonight in `Render`, same shape as Day.
   - **Year** (12-month view): per-target × per-night. `ObservablePoint.Y` → null (line break) for unfit nights; for fit nights, Y is `NightFit.Floor` read directly from the cached `TargetFitEntry`. **No compute in the sub-chart** — `ChartCacheStore.ComputeNightFits` runs the `BestSession.ResolveCandidates` + `PlaceBest` + `SessionAltitude.Floor` walk under each `HdmKey`, the coordinator awaits `PrepareFitsAsync(targets, ctx.Hdm)` before dispatching Render, and the sub-chart paints synchronously.
   - **Sessions** (12-month view, three curves): per-target × per-night × three. `NightFit.Ceiling` / `Floor` / `CenteredFloor` read straight off the cached entry. The shared-resolve optimization (one `ResolveCandidates` drives both `PlaceBest` and `PlaceCentered`) lives in `ChartCacheStore.ComputeNightFits`, not the sub-chart.

   M (the Moon component) covers EVERY input that mutates `mMoonAvoidanceProfile`:
   - Active filter selection (`SetActiveFilter` calls `Filter.ToProfile()` which builds a fresh `MoonAvoidanceProfile`).
   - Lorentzian relaxation parameters (`OnLorentzianControlChanged` reads `BuildProfileFromControls()`).
   - Moon avoidance enable checkbox (`CheckBox_Moon_AvoidanceEnable` toggles between profile and `null`).

   All three converge through `mCoordinator.Apply(SnapshotCurrent(mLastRenderedTargets))` (150 ms internal debounce). The coordinator's pipeline detects the change as an `HdmKey` flip, awaits `PrepareFitsAsync` for the new key, then renders the active sub-chart synchronously from the just-built cache entries. The post-apply hook calls `PushSkyKSInputs(ctx)` to re-walk Sky's K-S grid. Bortle / ExtinctionK changes don't move `HdmKey` (they're Sky-K-S inputs, not fit inputs) and ride the post-apply hook only — no fit recompute fires.

4. **First-paint visibility.** `Render(...)` reads `cache.GetFitOrNull(target, ctx.Hdm)` directly — the initial paint already reflects current H/D/M with no "show all, then hide" flicker. The coordinator's `PrepareFitsAsync` await before Render guarantees fits for the current `HdmKey` are built (or in-flight; an in-flight build for the same key is awaited via the cache's per-(target, key) dedupe).

5. **Cheap reorder for Sort changes.** `Reorder(IReadOnlyList<Target> newOrder)` rebuilds `mChart.Series` + legend in the new order without re-reading the cache. The series' painted Y values (from the most recent Render) stay valid because the target SET is unchanged. ResortSelectedTargets iterates `foreach (var sc in mSubCharts.Values) sc.Reorder(sorted)` instead of doing a full Render.

6. **No sub-chart cancellation infrastructure.** Sub-charts are render-only synchronous painters — there is no `CancellationTokenSource`, no `Task.Run`, no `BeginInvoke`. The cache owns all the async compute and dedupes per (target, key); a closing form orphans any in-flight cache builds, which discard themselves at publish via the `ReferenceEquals(mLocation, location)` check.

When adding a new sub-chart, the pattern is: implement `IAltitudeSubChart`, add an entry to `mSubCharts` in `InitializeDynamicControls`. If the sub-chart needs H/D/M-dependent precompute, extend `NightFit` / `ChartCacheStore.ComputeNightFits` rather than introducing a per-sub-chart bg task. The foreach loops in MainForm pick the new sub-chart up automatically. The interface IS the contract.

### Moon avoidance

Per-night Lorentzian-driven moon-clear evaluation gates the Day-chart HD Overlay, the Sessions placement candidates, and the universal hide-on-no-fit rule across every chart. User-facing UX (Enable checkbox, filter library, Edit Filters dialog, `*` indicator, right-click-to-edit) in [README.md](README.md#filters--moon-avoidance). MoonAvoidanceProfile is owned by MainForm (`mMoonAvoidanceProfile`) and threaded into each sub-chart's `Render(...)` / `RefreshVisibility(...)` call. `BestSession.ResolveCandidates(...)` (Library) returns visibility ∩ moon-clear in one call so Sessions's PlaceBest + PlaceCentered see identical inputs.

Moon avoidance is committed and active end-to-end. Post-NINA-load warmup runs in **~1-2 sec for 44 targets** (yearDays via `PrepareManyAsync`) plus a few seconds for the per-`HdmKey` fit build (`PrepareFitsAsync`) chained behind it; both fire in a single `Task.Run` so the user's first Sessions / Year click hits a fully warm cache. Down from ~17 min pre-CoordinateSharp-removal; halved again 2026-05-04 by the `MoonSeparation.ObserveAt` single-pass dedup, the `ObserverInfo` class→struct conversion, and the `MoonPosition` periodic-table flatten — see `Astronomy.Core.Tests/Benchmarks/HotPathBenchmarks.cs` for the BDN baseline. The 10-min `MoonSeparation.ObserveAt` sweep populates `entry.MoonSamples` per night per target.

### K-S sky brightness (Sky chart area)

User-facing UX in [README.md](README.md#sky-brightness-day-sky-sub-mode). Implementation: `Astronomy.Core.Brightness.SkyBrightness.KsAt(...)` (closed-form Krisciunas–Schaefer 1991, mag/arcsec², lower = brighter) composes dark-sky baseline V₀ from `Brightness.Bortle.DefaultZenithMag(BortleClass)`, solar twilight from `Brightness.Twilight.ZenithBrightening(sunAlt)`, and moon contribution. Per-Location `BortleClass` + `ExtinctionK` drive baseline + extinction; the surfacing `ComboBox_Bortle` + `NumericUpDown_LocalExtinction` are designer-resident inside `GroupBox_Location`. Active filter's `CenterNm` scales k via Rayleigh λ⁻⁴ in `SkyBrightness.ScaleK`. The Sky sub-chart's Y axis uses data-inversion (`plotY = SkyAxisMinMag + SkyAxisMaxMag - mag`) plus a custom `Labeler` that maps each plot value back to its actual magnitude — so brighter sky renders higher while the X axis stays at the visual bottom (no `IsReversed`). K-S is additive on top of the universal hide rule (Sky targets hidden by hide-on-no-fit get alpha 0 stroke; the K-S magnitudes are still computed but invisible). Bortle / ExtinctionK / ActiveFilter changes ride `SessionsRebuildDebounce_Tick` which calls `mLC2Sky.RefreshSkyBrightness(...)` after pushing `mActiveFilterCenterNm`. `LocationsCacheEquivalent` deliberately does NOT include Bortle / ExtinctionK so a sky-brightness scrub doesn't drop the year-cache. Tests in `Astronomy.Core.Tests/Tests/SkyBrightnessTests.cs`.

### Filter / moon-avoidance implementation

Filter / moon-avoidance implementation lives at the call sites. Class docs on `Filters/Filter.cs` (immutable POCO, `ToProfile()` drops Name/CenterNm/BandwidthNm — Lorentzian is wavelength-agnostic, CenterNm reserved for K-S, BandwidthNm reserved for IS), `Filters/FilterLibrary.cs` (persistence at `%APPDATA%\TargetPlanner\filters.json`, `BuiltinDefaults` for the H/O/S/L/R/G/B factory set, `MigrateLegacyFields` auto-fills CenterNm=0 entries, `DiffersFromBuiltinDefault` drives the `*` indicator), and `Forms/EditFiltersForm.cs` (modal, no Designer file, `BindingList<FilterRow>` shadow, per-row Defaults button, validates name on Save). MainForm field comments at `mFilterLibrary` / `mSuppressFilterEvents` / `mEditFiltersDialogOpen` / `mFilterAutoSaveDebounce` / `mFilterRadios` explain the auto-save target, the WriteProfileToControls suppression, the dialog-mode suppression, and the parallel-indexed sync invariant. Method comments on `BuildFiltersMenu` / `BuildFiltersGroupBox` / `SetActiveFilter` / `OpenEditFiltersDialog` / `OnLorentzianControlChanged` / `RefreshFilterMenuLabels` cover the early-init bypass (avoids restarting SessionsRebuildDebounce while year caches are mid-flight), the right-click-to-edit hook, and the post-dialog re-resolve. **Layout fact (Designer-only):** `GroupBox_Moon_Filters` is a sibling of `GroupBox_MoonAvoidance` directly inside `GroupBox_Target` since commit `9c13e43`; `CheckBox_Moon_AvoidanceEnable` (the master gate) lives inside `GroupBox_MoonAvoidance`.

### UI flow (`Forms/MainForm.cs`)

- `InitializeDynamicControls` instantiates `mCache = new ChartCacheStore(mLocation)`, then builds `mSubCharts = {"Day": new AltitudeSubChart_Day(), "Sky": mLC2Sky = new AltitudeSubChart_Sky(), "Year": new AltitudeSubChart_Year(), "Sessions": new AltitudeSubChart_Sessions()}`. A foreach loop adds each sub-chart's `Control` to `Panel_AltitudeChart` and subscribes `IdealHeightChanged`. Fires `_ = GetNinaTargets(...)`, calls `WireSelectionVm`, and returns. **No startup chart auto-build** — `Button_Graph_Click` is the only render path. The chart panel paints empty sub-charts until the user clicks Graph.
- Coordinate inputs are triple-bound (D/M/S `NumericUpDown`, decimal `TextBox`, N/S/E/W checkbox) via `Support/CoordinateInput.cs`. The helper owns all the "update one surface and keep the others in sync" plumbing through a single `mSuppress` flag — callers never unsubscribe / re-subscribe sibling events. Four instances: `mLatitudeInput`, `mLongitudeInput`, `mRaInput`, `mDecInput`. RA/Dec edits route through `OnRightAscensionEdited` / `OnDeclinationEdited`, which call `mSelection.SetSelectedSingle(currentTarget.With(...))` — the VM is the source of truth for the active target.
- **Time controls are picker-driven.** `DatePicker` / `TimePicker` drive `mLocation.DateTime` via `UpdateLocalDateTimeEvents`; `Button_Now` snaps to `DateTime.Now` and triggers `RefreshAstrometryLabels` + the chart's red now-line refresh. No 5-second poll timer; no "live now vs. held" radio / checkbox trio — deleted in favour of picker-is-authoritative semantics.
- `Button_Graph_Click` reads `mSelection.Mode` to decide between Multi (walks `CheckedListBox_SelectedTargets.CheckedItems`) and Single (uses `mSelection.SelectedSingle`). When neither produces targets it shows a 2-second `ShowTransientMessage("No Targets")`. Then awaits `mCache.PrepareManyAsync(targets, ct)` (cache warmup), stashes the list in `mLastRenderedTargets`, and dispatches via `RenderArea(SelectedArea(), mLastRenderedTargets, ct)`. `RenderArea` is the shared helper that does dict lookup → `ShowOnlyAltitudeChart(sc.Control)` → push `mActiveFilterCenterNm` to Sky → `sc.Render(...)` → resize panel. The four radio handlers each call the same helper.
- `Button_BrowseTargetList_Click` opens the folder dialog and on OK calls `mSelection.SetMode(GraphMode.Multi)` then `_ = GetNinaTargets(...)` (replaces the old `WireMultiMode(Button_BrowseTargetList)` Click hook).
- `NumericUpDown_TargetFloor_ValueChanged` (Horizon) / `NumericUpDown_TargetDuration_ValueChanged` update `mLocation`, push the immediate horizon-line position to every sub-chart (Horizon only — instant feedback during scrub), and call `mCoordinator?.Apply(SnapshotCurrent(mLastRenderedTargets))`. The coordinator's 150 ms debounce coalesces rapid scrubs; the pipeline's HDM-only path does `foreach RefreshVisibility` on every sub-chart — cache walk only, no Meeus work since it walks the cached year-days.
- **Location-edit funnel.** `OnLocationEdited` is the single attachment point for every user-driven location field edit (lat/lon spinner via `OnLatitudeEdited`/`OnLongitudeEdited`, the elevation spinner via `NumericUpDown_LocalElevation_ValueChanged`, the N/W flip checkboxes). It (1) calls `RefreshAstrometryLabels()` so the dawn/dusk/sun-altitude/moon-altitude/moon-rise-set/illumination labels update in real time, and (2) calls `RestartSessionsRebuildDebounce()` so a cache-equivalency check + `mCache.SetLocationAsync` + per-sub-chart `RefreshVisibility` fire after 150 ms idle. `ComboBox_Location_SelectionIndexChanged` invalidates the cache + refreshes labels immediately (single-shot user intent). `LocationsCacheEquivalent` compares Lat/Lon/North/West/Elevation/`ComputeYearStartDay` so Horizon/Duration scrubs don't drop the cache.
- **`RefreshAstrometryLabels()`.** Extracted from the body of `UpdateLocalDateTimeEvents`; calls `AstrometryUi.Location(mLocation)` then writes the eight static-info labels. ~150 µs of Meeus work + 8 string assignments, fast enough to fire on every spinner tick without debouncing. Called from `UpdateLocalDateTimeEvents` (DatePicker/TimePicker/Button_Now), `OnLocationEdited`, and `ComboBox_Location_SelectionIndexChanged`.
- **`PickStartupLocation()` always returns the personal-default location** (named by `PersonalDefaults.LocationName`) when present in `mAppSettings.NamedLocations` (else first preset, else `Location.Default`). `mAppSettings.LastSelectedLocationName` is still tracked / persisted but no longer drives boot.
- **`File → Clear All Data…` (`HandleClearAllDataClick` in `Forms/MainForm.cs`).** Confirmation dialog + best-effort delete of `settings.json`, `filters.json`, `tp.log` from `%APPDATA%\TargetPlanner` in that order (log file last so per-file failures get logged), then offers `Application.Restart()`. If a new persisted file is added, extend the wipe list here and in `HandleClearAllDataClick`'s body — `Log.FilePath`, `SettingsStore.FilePath`, `FilterLibrary.DefaultPath` are the three current public path properties.
