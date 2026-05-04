# TargetPlanner — Roadmap

Last updated 2026-05-04 (Phase 4 LC2 chart migration shipped). Originally captured 2026-04-19.

## Recently shipped

Archived from CLAUDE.md's "Open follow-ups" section so the file stays under the perf-warning threshold; preserve commit hashes for future archaeology.

- **Phase 4 — Chart migration to LiveCharts2 SHIPPED.** All four chart areas (Day / Sky / Year / Sessions) ported off `System.Windows.Forms.DataVisualization.Charting` to LC2 v2.1.0-dev-365. Each area is a sub-chart class implementing `Charts.IAltitudeSubChart` (common: Control, IdealHeight, IdealHeightChanged, UpdateNowLine, UpdateHorizonLine, Render, Reorder, RefreshVisibility, Dispose). MainForm holds `Dictionary<string, IAltitudeSubChart> mSubCharts` keyed by area; picker / spinner / debounce / Graph-click traffic dispatches via foreach + dict lookup. Sky keeps a typed `mLC2Sky` reference for `ActiveFilterCenterNm` + `RefreshSkyBrightness` (K-S quirks outside the interface). `BestSession.ResolveCandidates(...)` (Library) added to expose visibility ∩ moon-clear so Sessions's PlaceBest + PlaceCentered see identical inputs. Year switched from night-max `YearAlt` to session-floor altitude (more actionable planning metric). The legacy `AltitudeChart.cs` (~1400 lines) + `AltitudeSeries.cs` (~900 lines) + `LegendClickHandler.cs` + `LegendHitTester.cs` (dead after the custom-legend pivot) deleted; DataVisualization package reference dropped. Phase 4 commits: `bebf909` PR4a Day · `582b4fb` PR4a Day plot-area lock · `edc2c9b` PR4b prep (ChartLayout hoist) · `5763bbc` PR4b Sky · `7d5d3b2` PR4c Year + universal hide rule · `99f2fc3` Year → floor metric · `46dcd4f` PR4d Sessions · plus PR4e (this commit). Companion Library commits: `6fce6b0` (BestSession non-positive-duration → null) · `a251524` (BestSession.ResolveCandidates public). Detailed plan / lessons-learned at `~/.claude/plans/i-thought-i-d-take-valiant-neumann.md` and the per-PR plans `pr4c-year-to-lc2.md` / `pr4d-sessions-to-lc2.md` / `pr4e-drop-ms-charts.md`.

- **Chart-package investigation — LiveCharts2 chosen** — design doc at `docs/design/chart-package-investigation.md` captures the comparison (OxyPlot / ScottPlot / LiveCharts2) against the four representative tests + the dual-target (`net481` today, `.NET 10` post-migration) requirement. Working prototype at `Prototypes/LiveCharts2Prototype/` (own git repo on `dev` branch, last commit `6613629`) was the pattern playbook for the Phase 4 migration above.
- **Core solvers for parameter exploration** — Library `c2c1f5e` + `9504461` add `Astronomy.Core.Session.SessionSolvers` with the full six-method surface: `LongestDuration` / `LongestDurationIn` / `LowestHorizon` (transit-centered-or-wall-pushed placement, parallel to `BestSession.PlaceBest`) and `LongestDurationCentered` / `LongestDurationCenteredIn` / `LowestHorizonCentered` (strict-centered placement, parallel to `BestSession.PlaceCentered` / Symmetric-curve UI semantics). Foundation for downstream "what's possible tonight?" UI surfaces in TP and plan-relaxation paths in IS / ISP. `MoonClearIntersect` promoted from `private` → `internal` to let `SessionSolvers` reuse the existing moon sweep. 23 new tests, 164/164 in suite. TP UI consumers are a separate follow-up.
- **Per-sub-interval moon-aware Optimal placement** — `RenderOptimalSeries` no longer runs placement against moon-blind visibility windows on partially-moon-impacted nights. Library `3737cfa` promoted `BestSession.PlaceBest` to public, added `BestSession.PlaceCentered`, and added the new `SessionAltitude` class with `Floor` / `Ceiling` evaluation helpers. TP follow-up commit migrates the Optimal-chart per-night loop to call `BestSession.PlaceBest` (Floor / Ceiling via `SessionAltitude`) and `BestSession.PlaceCentered` (Symmetric) over `(visibility ∩ moon-clear)` candidates derived chart-side from cached `MoonSamples`. `ComputeBestDayWindow` (Day overlay) also moved to `SessionAltitude.Floor` for SoC consolidation. Retires Step-3 cleanup item "chart-side `BuildOptimalSeries` math". `HasMoonClearViableWindow` short-circuit dropped (PlaceBest/PlaceCentered returning null is the new sentinel).
- **CoordinateSharp roll-your-own** — pure-C# Meeus replacement landed in `e602bdb` (Library) + `2249834` (TP). Cache pre-population dropped from ~17 min to 2-4 sec on 44 targets; Astronomy.Core is now lock-free and managed-only.
- **Moon-avoidance re-enable** — committed alongside the CS removal; bisection disables removed.
- **Cache invalidation on Location change** — `LocationsCacheEquivalent` gates `mCache.SetLocationAsync`; lat/lon edits ride the debounce, combo picks fire immediately (commit `56269db`).
- **Year-chart visibility** — `RebuildDayTooltip` no longer hides Year-curve series for targets with no D-hour fit tonight (Day-only filter). Commit `24e3213`.
- **Location.Elevation end-to-end** — `Location` POCO gains `Elevation` (Library `2df74c1`); spinner wired into `MainForm.Designer.cs` + `SyncLocationUIFromModel` + `OnLocationEdited` + `LocationsCacheEquivalent` (TP `8b2a6d7`). Hillsborough preset (40.459456°N, 74.612921°W, 28.16 m) added; `MergeBuiltins` auto-fills existing settings by name match.
- **Elevation-dip on rise/set** — `MeeusUtility.HorizonDipDeg(elevationM)` + elevation-aware thresholds in `AstroUtil.GetSunRiseAndSet` / `GetMoonRiseAndSet` (Library `65ca166`); TP `Astrometry.cs` passes `localLocation.Elevation` (TP `8484ed8`).
- **Refresh dependent labels on Location edits** — extracted `RefreshAstrometryLabels()` from `UpdateLocalDateTimeEvents`; called from `OnLocationEdited` and `ComboBox_Location_SelectionIndexChanged` so dusk/dawn/altitude/illumination/phase/moon-rise-set track lat/lon/elevation/combo edits in real time (TP `8484ed8`).
- **Penns-Park-on-boot default** — `PickStartupLocation` always prefers Penns Park when present (commit `43fc931`); `LastSelectedLocationName` still tracks user's combo pick for persistence but no longer drives start-up.

## Why this project is still open

The app was originally built around Sequence Generator Pro's `.sgf` sequence files. The user has since moved to NINA + the Target Scheduler plugin (TS, backed by a local SQLite database) and has a separate C# app, **XisfManager**, that already has a tab for reading/mutating that TS database. The goals for *this* project going forward:

1. Make sure the altitude/visibility math is actually correct (the recent Astrometry fixes landed real bugs — there may be more).
2. Expose the reusable astronomy routines in a form **XisfManager** can consume directly, so the same code isn't hand-ported twice.
3. Keep this app as a standalone tool, cleaned up in place. .NET Framework 4.8.1 is fine — migration to .NET 10 would be cosmetic, not load-bearing.
4. Eventually wrap the shared library in a **NINA plugin** so planning against the TS database can happen inside NINA itself.

## Sequencing

### Step 1 — Correctness audit (on 4.8.1, before any structural change)

Three concrete checks, using the live app plus Stellarium / NINA TS as references:

- **Transit time and max altitude** for ~5 targets at Penns Park, one night each. Compare to Stellarium to sub-minute precision.
- **M31 max daily altitude over a full year.** Shape compared to NINA Target Scheduler's own altitude curve.
- **Horizon-crossing / Optimal plot** for a low-declination target (e.g. M8). The current code records `aboveHorizonAltitude` at the *instant* the target clears horizon, which is ≈ `Horizon` by definition and makes the Optimal chart look like a step function. The old commit message `"Fixing OptimalPlot series to show min and max optimal altitude for duration"` hints the intent was **max altitude while continuously above horizon for ≥ Duration** — closer to usable-for-imaging semantics. Verify against reality and decide what Optimal's Y should be.

Land any fixes here *before* restructuring — debugging on the current code with known-good reference tools is easier than during a migration.

### Step 2 — Extract `Astronomy.Core` (new class library, `netstandard2.0`) ✅ **DONE** (commit `24bb4e7`)

Landed the full library surface in one pass: POCOs (`Targets/Target`, `Locations/Location`, `Night/NightWindow`), time / sidereal / alt-az / target-geometry primitives, night + twilight calculators, horizon profile abstraction (`IHorizonProfile` + scalar / polyline / obstruction-table impls), session-level primitives (`TransitTime`, `IntegratedQuality` incl. closed-form sin(alt), `VisibilityWindows`, `BestSession`, `QualitySamples`, `RiseSet` scalar + profile-aware), `MoonSeparation`. Design rationale for every primitive lives in `SCHEDULER_DESIGN.md`.

Notable follow-through from the extraction:

- `Target.mAltitudeSeries` field removed from the POCO (WinForms chart state can't live in netstandard2.0). Per-target AltitudeSeries ownership now lives in `Dictionary<Target, AltitudeSeries>` on `AltitudeChart`, preserving the recent multi-target correctness fix.
- `Support/Astrometry.cs` slimmed to a UI state facade — math methods moved to Core; the static dawn/dusk/moon-phase properties and the `Location(...)` populator stay because MainForm binds to them.
- Parser's namespace was renamed `TargetPlanner.Target` → `TargetPlanner.Sgf` to unblock `using Target = Astronomy.Core.Targets.Target;` aliases in files that would otherwise hit enclosing-namespace lookup. (Parser.cs itself was subsequently retired entirely in commit `ccab2c0` when the NINA target loader replaced it.)
- ~~Chart code still contains its own inline versions of some Core primitives~~ — **Done.** `RenderOptimalSeries`'s inline transit-centered / wall-pushed math retired in favour of `BestSession.PlaceBest` / `PlaceCentered` + `SessionAltitude.Floor` / `Ceiling` (see "Recently shipped" above for commit refs).

### Step 3 — Functional cleanup in place (no framework change) — **Closed**

All originally-listed cleanup items are resolved:

- ~~RA text-box range check consistency~~ — Both spinner and textbox paths share `CoordinateInput`'s `Math.Abs(v) > mMaxMagnitude` enforcement (the original "spinner doesn't enforce" inconsistency went away with the `CoordinateInput` extraction); `NumericUpDown_RaHours.Maximum` tightened from 24 to 23 for strict `[0, 24)` semantics.
- ~~Centralise the dusk/dawn hour-rounding block~~ — Moot: `BuildMoonSeries` was retired; the hour-rounding logic lives in static helpers `DayChartStart` / `DayChartStop` (`AltitudeSeries.cs`) which both `BuildDaySeries` and `AltitudeChart.BuildSharedMoonSeries` consume.
- ~~Retire or consolidate `BuildOptimalSeries`' inline transit-centered / wall-pushed math~~ — Migrated to `BestSession.PlaceBest` / `PlaceCentered` + `SessionAltitude.Floor` / `Ceiling`; chart-side math is gone.
- ~~Any correctness fixes that fall out of Step 1~~ — Step-1 fixes landed in commits before the Step-2 Astronomy.Core extraction; subsequent correctness work (CS removal, moon avoidance, per-sub-interval placement, descending-arc fix) covered the post-Step-2 fall-out.
- ~~`Location` and `Target` settable properties on `AltitudeSeries`~~ — Moot: both are `{ get; }` only with init-only semantics; class comment makes the contract explicit.
- ~~`Astrometry` UI state facade rename~~ — Renamed to `AstrometryUi` to disambiguate from `Astronomy.Core.Astrometry` namespace.

### Step 4 — NINA plugin

Thin WPF shell over `Astronomy.Core` plus a TS SQLite access layer. NINA is .NET 10 / WPF, so none of the current WinForms UI transfers — a plugin is a fresh UI over the shared library. The TS access code XisfManager already has working could itself be factored out into the same (or a sibling) library.

`SCHEDULER_DESIGN.md` captures the plugin's intended architecture in detail: interval-scheduling (not score-at-decision), three policy modes (meridian-chase, narrow-window, keep-busy) that all reduce to the same weighted-interval-scheduling solver, a quality function `q(alt)` defaulting to `sin(alt)`, and a clear library/scheduler/plugin seam. Read that before starting.

Reference sources already present locally (see memory for paths): full NINA codebase on `develop` at `E:\Projects\VisualStudio\Astronomy\NINA`, Target Scheduler clone at `E:\Projects\VisualStudio\Astronomy\TargetScheduler_Clone\nina.plugin.targetscheduler`. The dossier's delta pass against NINA 3.2.x `develop` is still current as of the Step 2 commit — plugin API shifts (Ninject → MS.DI, new `IMessageBroker`, `StartAdvancedSequence(skipValidation)`) are captured there.

## Open code-quality items (CODE_REVIEW.md residual)

The 2026-04-21 whole-repo audit (archived at `docs/archive/CODE_REVIEW-2026-04-21.md`) flagged 14 P1 / 43 P2 / 18 P3 findings. ~75% closed by the work since: Phase 4 LC2 chart migration (deletes legacy `AltitudeChart` / `AltitudeSeries`, the bulk of the threading + null-safety + chart-rebuild findings); the SoC refactors (Phases 1-3 in TP — selection VM, cache store, render decoupling); the Library extraction with immutable `Target` / `Location` POCOs + `NightWindow.IsValid` + `AltAz` struct + `RiseSet.RiseSetState` enum; `CoordinateInput` helper for triple-bound RA/Dec/lat/lon; `Log.Error` plumbing through every former-bare catch site; `<GenerateDocumentationFile>true</GenerateDocumentationFile>`; `System.TimeZone` → `TimeZoneInfo` migration; typo sweep.

Residual still-open items (verified 2026-05-04):

- **P2-2.5 — `Location.DateTime` defaults to `DateTime.Now` in the immutable POCO ctor** (`Astronomy.Core/Locations/Location.cs:189`). Nondeterministic for unit tests / library consumers. Either drop the default (require caller to supply) or use `DateTime.MinValue` as a sentinel and document the contract.
- **P2-3.4 — `SettingsStore.Version` is written but never read.** No schema migration when `AppSettings` fields are added/removed in a future release. Read on `Load`, compare to current `Version`, apply transforms or reset to defaults.
- **P2-9.5 — `BestSession.For` boundary semantics undocumented.** Transit-at-dusk-exactly is included in the visibility window; transit-at-dawn-exactly is excluded (per the survey). XML doc on `For` / `PlaceBest` / `ResolveCandidates` should call this out so consumers don't write off-by-one logic.
- **P2-10.2 — Signed-degree convention is in CLAUDE.md but not at the API XML doc level.** `<GenerateDocumentationFile>` is on, but per-method `///` comments on `TargetGeometry.MeridianAltitude` / `HourAngleAtAltitude` / etc. don't restate the "caller resolves the hemisphere flag" expectation. A NINA plugin author reading IntelliSense cold won't see the convention.
- **P2-5.3 — `IntegratedQuality.OverSession` doesn't document NaN behaviour.** If the caller's `altitudeQuality` lambda returns NaN/∞ on a boundary altitude, the integral silently corrupts. Add a `///` remarks note + optional `Debug.Assert`.
- **🔄 P2-5.4 — INVERTED.** Original audit flagged `BestSession.For` for not throwing on `minDuration <= 0`. We deliberately reversed that (Library `6fce6b0`): non-positive duration now returns null (the user-reachable degenerate case), making consumers' "no fit" handling uniform. The audit finding is moot; documenting here so future spelunkers don't try to re-add the throw.

Each item is small. None are crash-class. P2-2.5 + P2-3.4 are the most user-visible; the rest are documentation polish on Library public surface.
