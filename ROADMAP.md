# TargetPlanner — Roadmap

Captured 2026-04-19 for follow-up later.

## Recently shipped

Archived from CLAUDE.md's "Open follow-ups" section so the file stays under the perf-warning threshold; preserve commit hashes for future archaeology.

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

## Open decisions

### Astrometry math: hand-rolled vs external library?

`Support/Astrometry.cs` is currently author-written from white papers. Candidates for replacement, rough order of fit:

- **Keep using `CoordinateSharp`** (already a dependency). It handles sun / moon events end-to-end. Question: does its API cover arbitrary RA/Dec → Alt/Az for stellar targets, or only the named solar-system bodies? A quick spike against its docs / source would settle this. If yes, the hand-rolled `GetAltitudeAzimuth` can go entirely.
- **Astronomy Engine (CosineKitty)** — MIT, active, ~0.1 arcmin accuracy, clean API, NuGet available. Covers RA/Dec → Alt/Az, rise/set/transit, moon phase, planetary positions. Well-matched to amateur astrophotography needs without being overkill.
- **AASharp** — C# port of Jean Meeus's "Astronomical Algorithms". Broader but chattier API.
- **Keep hand-rolled, audited.** Lowest external risk; highest maintenance. After Step 1 we'll know if the math is actually solid.

Recommendation: spike CoordinateSharp first (we already depend on it). If it covers the case, we shrink our footprint. If not, pick Astronomy Engine.

### TS SQLite access — where does it live?

XisfManager already has a working layer for reading/mutating the TS SQLite database. Decide whether to:

- Move it *into* `Astronomy.Core` (widens the library beyond pure astronomy — mild scope creep), or
- Keep it in a separate `NINA.TS.Access` library the plugin and XisfManager both reference.

Second option feels cleaner; the first is tempting only if the coupling between astronomy and TS data is tight enough to warrant it.

## Current state of the code (post commit `24bb4e7`)

Step 1 (correctness audit) fixes already in:

- Julian Day offset corrected (`+ 2415018.0` → `+ 2415018.5`).
- GMST replaced with the USNO one-liner (fixes the incomplete single-subtract mod).
- Latitude sign flip applied in `Astrometry.Location(...)` (was longitude-only).
- Polar `null` safety on astronomical dawn/dusk.
- RA standardised on hours `[0, 24)` project-wide; Target / UI / Parser / Astrometry all agree.
- Latitude / longitude setters coerce only on negative input, so unsigned UI magnitudes don't clobber the hemisphere checkbox.
- `AltAz2RaDec`, `AngularDistance`, and the unused `JulianDay` helper deleted (all broken or dead).

Chart behaviour landed since:

- Year and Optimal series merged into a single pass, then refactored to a fully analytic (non-minute-scan) transit-math implementation (`f07e608`).
- `OptimalFloor` and `OptimalFloorCentered` curves added alongside the peak-altitude `Optimal` (best-placement floor + strict transit-centered floor).
- Cache-backed rebuild (`mYearCache`) makes Horizon/Duration spinner scrubbing effectively instant (`0cc9b57`).
- Per-target AltitudeSeries ownership moved from `Target.mAltitudeSeries` to `Dictionary<Target, AltitudeSeries>` on `AltitudeChart` (`df7731e`), which fixed the multi-target race and was later reinforced by Step 2's extraction.
- `mTargetSeries` instance field eliminated in favour of a `MakeSeries(...)` factory; each build method uses a local `Series`.
- "Now" red vertical line on Day / Year / Optimal, updating on the 5 s timer (timer is now enabled at launch based on `CheckBox_HoldTime`).

Step 2 landed Astronomy.Core with the 10-primitive library surface — see the Step 2 section above.

Known open (heading into Step 3):

- RA text-box range check consistency (spinner path doesn't enforce `[0, 24)`).
- Dusk/dawn hour-rounding duplication between `BuildDaySeries` and `BuildMoonSeries`.
- Chart code duplicates Core's `Session.BestSession.For` math inline in `BuildOptimalSeries` — migrate or annotate.
- `Location` / `Target` still settable properties on `AltitudeSeries` (shared-mutable-state smell).
- `Support/Astrometry.cs` still named `Astrometry` despite being UI-state-only after Step 2; consider renaming to `AstrometryUi`.
- WinForms control `CheckedListBox_SelectedSgpTargets` retains the "Sgp" prefix despite the NINA target loader swap (`ccab2c0`); cosmetic rename due.
