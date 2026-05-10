# TargetPlanner — Roadmap

Last updated 2026-05-04 (TP migrated to .NET 10). Originally captured 2026-04-19.

## Currently open (priority order)

Migrated from CLAUDE.md so the agent-facing reference stays lean. Order is rough recommendation, not a commitment.

1. **TP UI surfaces for `SessionSolvers`** — Library API fully shipped: `LongestDuration` / `LongestDurationIn` / `LowestHorizon` for transit-centered-or-wall-pushed placement, plus `LongestDurationCentered` / `LongestDurationCenteredIn` / `LowestHorizonCentered` for strict-centered (Symmetric-curve) placement. TP needs to surface them somewhere user-visible — tooltips, right-click menu, info panel? Needs UX design before implementation.
2. **IS / ISP work** — current major thrust per memory; the four-phase IntervalScheduler pipeline is the strategic next axis.
3. **Velopack version bump** — `0.0.1298` → `0.0.1589+`. Dry-run a release cycle (`vpk pack` + `vpk release` + self-update install) on net10 before shipping; the auto-update flow hasn't been smoke-tested at the current pinned version on net10.
4. **Lower-priority perf chasing** if anyone wants to push further — `GetSunAltitude` / `GetMoonAltitude` per-call allocations (144 B / 56 B; root cause not obvious without an allocation profiler), and `Math.FusedMultiplyAdd` in the `MoonPosition` periodic-term loops (~1-5%, hardware-FMA-dependent). The big wins from the 2026-05-04 session (`MoonSeparation.ObserveAt` -54%, `BestSession_For_Narrowband` -53%) already exhausted the easy lifts; remaining items are diminishing returns.

**Future-flagged for Core API shape:** **partial-moon-impact tolerance** — allowing a session to span moon-blocked time at a quality penalty rather than rejecting outright. Deferred until much later, but the placement primitives are designed so they don't preclude it (moon profile is optional everywhere; mask computation is behind an internal helper).

**Future-flagged UX/Core split — Local Horizon vs Target Floor.** Today the H in HMD (`NumericUpDown_TargetFloor` → `Location.Horizon`) conflates two distinct concepts:

- **Target Floor** — user preference: "I don't want to image targets below this altitude tonight." A scrubbable filter knob that affects chart visibility but doesn't change the physics of what's observable.
- **Local Horizon** — site reality: a polyline of `[Alt, Az]` pairs describing where the sky is actually blocked by terrain / trees / buildings. Both NINA and TS support this; `Astronomy.Core.Horizons.IHorizonProfile` already has the abstraction (`PolylineHorizonProfile`, `ObstructionTableHorizonProfile`).

The Library half is mostly ready — `IHorizonProfile` is plumbed through every visibility / placement primitive (`CoarseVisibility.IsAboveHorizonForAtLeast`, `BestSession.For`, etc.). The TP-side work needed: a separate `Location.LocalHorizon` field + UI to import/edit a `[Alt, Az]` table (file-load from a NINA/TS shared format, or graphical scrub), plus consumer plumbing so the cache + render pipeline reads from the polyline profile rather than `ScalarHorizonProfile(Location.Horizon)` everywhere. `Button_VisibleTonight` (and the universal hide-on-no-fit rule) would intersect Local Horizon with Target Floor — a target must clear both the user's floor AND not be terrain-blocked.

Warrants a separate design discussion before implementation. Ties into BIRDWATCHER if the local horizon definition lives in NINA's preferences and TP needs to read it.

## Recently shipped

Archived from CLAUDE.md's "Open follow-ups" and "What shipped" sections so the file stays under the perf-warning threshold; preserve commit hashes for future archaeology.

### 2026-05-04 — .NET 10 migration + Library perf wave

Long session covering, in commit order:

- **Astronomy.Core review** — closed all 5 small findings + 6 missing test files + profile-aware `VisibilityWindows.For` refinement + 4 ROADMAP residuals (Library `d38fed9` `629e37b` `d11a6dc` `319e4df`; TP `a98a45e`).
- **Portfolio framework bump to .NET 10** — TP `net481` → `net10.0-windows10.0.19041` (TP `85bc590`); Astronomy.Core → `net10.0` (Library `b834f52`); Astronomy.PCL → `net10.0` (Library `c7eeff9`); Astronomy.Core.Tests pinned `LangVersion latest` (Library `6d66881`); Astronomy.Core nullable + LangVersion latest (Library `2bd3c20`); LocalLib reference dropped (`OpenFolderDialog` → stock `FolderBrowserDialog`, single-select).
- **Library perf opts** — BDN baseline (Library `6d9f402`); `MoonSeparation.ObserveAt` single-pass alt+az dedup (Library `adfdd5f`, −49% time, −100% alloc); `ObserverInfo` class → readonly struct (Library `8ca5b37`); `MoonPosition` periodic tables `int[,]` → `int[]` flat (Library `383c38c`, −10% on `GetMoonAltitude`); `BestSession` + `SessionSolvers` accept null altitudeQuality → closed-form `SinAltitudeOverSession` fast path (Library `e83a110`); TP charts drop their `SinAltQuality` lambdas (TP `14a87ea`). Cumulative `BestSession_For_Narrowband` 177 µs → 83 µs (-53%, -60% alloc).
- **TP UX fixes** — progress-bar wired (TP `0cec432`); 8 px gap above `Panel_AltitudeChart` (TP `4fdd479`); `NightCache.ComputeYearStartDay` off-by-one fix (Library `0d4ef83`); Year + Sessions exact 1st-of-month CustomSeparators (TP `bcd148a`); `RightChromePx` 24 → 40 (TP `a5d6171`); MainForm Designer VS-regen cleanup (TP `7b81158`).
- **Memory + framework_stance memory** — rewritten 2026-05-04 to reflect uniformly-net10 portfolio (NINA migrated upstream too, verified at `E:\Projects\VisualStudio\Astronomy\NINA\NINA\NINA.csproj:462`).

- **TP migrated `net481` → `net10.0-windows10.0.19041`.** Single TP commit. csproj: `TargetFramework` bumped, `LangVersion` 10 → `latest`, `AutoGenerateBindingRedirects` removed (irrelevant on modern .NET), `<ServerGarbageCollection>` + `<ConcurrentGarbageCollection>` MSBuild properties added (replaced the deleted App.config `<runtime>` block). The `Win10 2004` Windows API contract version is needed because `SkiaSharp.Views.WindowsForms 3.119.0` (transitive via LiveCharts2) only ships modern-.NET assets at `net8.0-windows10.0.19041` — the default `net10.0-windows7.0` would fall all the way back to the package's `net462` lib, which doesn't load on .NET 10. LocalLib reference dropped: its reflection-based `OpenFolderDialog` multi-select hack relied on `System.Windows.Forms.FileDialogNative+IFileDialog` internals that don't survive into modern WinForms. `MainForm.Button_BrowseTargetList_Click` now uses stock `FolderBrowserDialog` (single-select; multi-select was a nice-to-have). `App.config` deleted (modern .NET ignores `<startup>` and `<runtime>` blocks). Astronomy.Core (`netstandard2.0`) and Astronomy.PCL (`net8.0`) sibling assemblies unchanged — both forward-compat with the new TP. **Velopack 0.0.1298** is forward-compat through netstandard2.0 fallback; bump to 0.0.1589+ is queued as a follow-up below.

- **Phase 4 — Chart migration to LiveCharts2 SHIPPED.** All four chart areas (Day / Sky / Year / Sessions) ported off `System.Windows.Forms.DataVisualization.Charting` to LC2 v2.1.0-dev-365. Each area is a sub-chart class implementing `Charts.IAltitudeSubChart` (common: Control, IdealHeight, IdealHeightChanged, UpdateNowLine, UpdateHorizonLine, Render, Reorder, RefreshVisibility, Dispose). MainForm holds `Dictionary<string, IAltitudeSubChart> mSubCharts` keyed by area; picker / spinner / debounce / Graph-click traffic dispatches via foreach + dict lookup. Sky keeps a typed `mLC2Sky` reference for `ActiveFilterCenterNm` + `RefreshSkyBrightness` (K-S quirks outside the interface). `BestSession.ResolveCandidates(...)` (Library) added to expose visibility ∩ moon-clear so Sessions's PlaceBest + PlaceCentered see identical inputs. Year switched from night-max `YearAlt` to session-floor altitude (more actionable planning metric). The legacy `AltitudeChart.cs` (~1400 lines) + `AltitudeSeries.cs` (~900 lines) + `LegendClickHandler.cs` + `LegendHitTester.cs` (dead after the custom-legend pivot) deleted; DataVisualization package reference dropped. Phase 4 commits: `bebf909` PR4a Day · `582b4fb` PR4a Day plot-area lock · `edc2c9b` PR4b prep (ChartLayout hoist) · `5763bbc` PR4b Sky · `7d5d3b2` PR4c Year + universal hide rule · `99f2fc3` Year → floor metric · `46dcd4f` PR4d Sessions · plus PR4e (this commit). Companion Library commits: `6fce6b0` (BestSession non-positive-duration → null) · `a251524` (BestSession.ResolveCandidates public). Detailed plan / lessons-learned at `~/.claude/plans/i-thought-i-d-take-valiant-neumann.md` and the per-PR plans `pr4c-year-to-lc2.md` / `pr4d-sessions-to-lc2.md` / `pr4e-drop-ms-charts.md`.

- **Chart-package investigation — LiveCharts2 chosen** — comparison (OxyPlot / ScottPlot / LiveCharts2) against the four representative tests + the dual-target (`net481` today, `.NET 10` post-migration) requirement, plus the MS-Charts→LC2 migration findings, now live in the prototype's `CLAUDE.md` at `E:\Projects\VisualStudio\LiveCharts2Prototype\` (relocated out of TP's `Prototypes/` 2026-05-08 once the investigation finished — own git repo, no longer a TP submodule). The prototype was the pattern playbook for the Phase 4 migration above.
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
3. Keep this app as a standalone tool, cleaned up in place. (Originally said ".NET Framework 4.8.1 is fine — migration to .NET 10 would be cosmetic, not load-bearing." Migrated anyway 2026-05-04 for portfolio consistency with IS / ISP / ISS / XisfManager and the perf wins on the per-target Meeus + LiveCharts paint loops.)
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

- **Velopack 0.0.1298 → newer prerelease bump.** Current pinned version is from 2025-06; 0.0.1589+ exists (2026-04). Velopack 0.0.1298 forward-compats to .NET 10 via netstandard2.0 fallback (build-time fine; runtime self-update path not explicitly tested at this version on net10). Plan: bump version, dry-run a release cycle (`vpk pack` + `vpk release` + self-update install/upgrade smoke), confirm the auto-update flow works against a Velopack-hosted release feed. Separate commit; not gating any active work.
- **🔄 P2-5.4 — INVERTED.** Original audit flagged `BestSession.For` for not throwing on `minDuration <= 0`. We deliberately reversed that (Library `6fce6b0`): non-positive duration now returns null (the user-reachable degenerate case), making consumers' "no fit" handling uniform. The audit finding is moot; documenting here so future spelunkers don't try to re-add the throw.

Closed in Library `d38fed9` (2026-05-04, "Astronomy.Core cleanup: 5 small findings from code review"):
- ~~P2-9.5 — `BestSession.For` boundary semantics now documented in `For` / `PlaceBest` / `ResolveCandidates` `<remarks>`. Both dusk and dawn boundaries are inclusive (verified against `VisibilityWindows.For`'s `Max(lstDusk, riseHA)` / `Min(lstDawn, setHA)` idiom — the original survey claim that dawn was exclusive was incorrect).~~
- ~~P2-10.2 — Per-method `<remarks>` on `TargetGeometry.MeridianAltitude` / `LowerCulminationAltitude` / `HourAngleAtAltitude` / `AltitudeAtHourAngle` / `AzimuthAtHourAngle` now restate the "caller resolves North/West flag" convention; IntelliSense surfaces it on every callsite.~~

Closed 2026-05-04 (final residual sweep):
- ~~P2-2.5 — `Location.Default` now carries an explicit `<remarks>` block documenting the `DateTime.Now` nondeterminism contract: convenience for interactive callers, override via `.With(dateTime: ...)` for deterministic library / test consumption. The `Default` factory's behavior is unchanged; the doc closes the audit's "or document the contract" branch.~~
- ~~P2-3.4 — `SettingsStore.Load` now reads the persisted `Version` and falls through to defaults when it doesn't match `AppSettings.CurrentVersion` (logged to `tp.log`). Foundational stub for future schema migrations: the version-mismatch branch is the seam where per-version transforms will hang.~~
- ~~P2-5.3 — `IntegratedQuality.OverSession` `<remarks>` now contains an explicit `<para>` "NaN contract" block: `altitudeQuality` must return finite values across `[-90, 90]`; below-horizon rejection should return `0`, not `double.NaN`. Promoted from the `<param>` tag's already-existing one-liner.~~

The Astronomy.Core review residual list is empty. Inverted P2-5.4 is documented and intentionally not actioned.
