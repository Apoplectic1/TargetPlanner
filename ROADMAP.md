# SGP Ephemerides — Roadmap

Captured 2026-04-19 for follow-up later.

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
- Parser's namespace renamed `SGP_Ephemerides.Target` → `SGP_Ephemerides.Sgf` to unblock `using Target = Astronomy.Core.Targets.Target;` aliases in files that would otherwise hit enclosing-namespace lookup.
- Chart code still contains its own inline versions of some Core primitives (`BuildOptimalSeries`' transit-centered / wall-pushed placement is duplicated with `Session.BestSession.For`). Deferred: refactor the chart to consume Core's versions directly (separate plan).

### Step 3 — Functional cleanup in place (no framework change)

Remaining items (the multi-target race was fixed as a prerequisite to Step 2; see `df7731e`):

- RA text-box range check consistency. Currently only `TextBox_RightAscension_TextChanged` enforces `[0, 24)`; the spinner path doesn't.
- Centralise the dusk/dawn hour-rounding block that's copy-pasted between `BuildDaySeries` and `BuildMoonSeries`.
- Retire or consolidate `BuildOptimalSeries`' inline transit-centered / wall-pushed math — now duplicated with `Astronomy.Core.Session.BestSession.For`. Either migrate the chart to call Core's version, or explicitly annotate the duplication as intentional (there's an argument for keeping the chart's version narrowly tuned to the `NightCacheEntry` shape).
- Any correctness fixes that fall out of Step 1.
- Optional: delete `Target/Parser.cs` — SGP `.sgf` support is obsolete for the user's current workflow. The namespace rename to `SGP_Ephemerides.Sgf` makes this easier to carve out cleanly.
- `Location` and `Target` are public settable properties on `AltitudeSeries`; the shared-mutable-state smell hasn't fully gone away. Consider constructor injection or per-build parameters.
- `Astrometry` UI state facade could stand to be renamed (`AstrometryUi` or similar) to reduce confusion now that the "math" half of Astrometry has moved to Core.

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
- `Target/Parser.cs` (SGP `.sgf`) is obsolete for the current workflow; ripe for deletion.
- `Support/Astrometry.cs` still named `Astrometry` despite being UI-state-only after Step 2; consider renaming to `AstrometryUi`.
