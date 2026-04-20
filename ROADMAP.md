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

### Step 2 — Extract `Astronomy.Core` (new class library, `netstandard2.0`)

Target `netstandard2.0` so the same DLL works for:

- `SGP Ephemerides` (staying on .NET Framework 4.8.1).
- `XisfManager` (whatever framework it's on today).
- A future **.NET 10** NINA plugin — no re-port.

Candidates to move out of this repo into the library:

- `Support/Astrometry.cs` — `GetAltitudeAzimuth`, the `Location(...)` static facade over CoordinateSharp.
- `Target/Target.cs`, `Location/Location.cs` — POCOs (the signed-hemisphere convention too).
- The per-night altitude loop and horizon-crossing logic currently inside `AltitudeSeries.cs`. Pull into something like `TargetObservability` that returns data (max altitude, above-horizon windows) rather than mutating `System.Windows.Forms.DataVisualization.Charting.Series` objects. The WinForms host keeps the Series mutation; the library stays UI-free.

Open sub-question: keep the hand-rolled altitude math or replace it — see **Open decisions** below.

### Step 3 — Functional cleanup in place (no framework change)

Already queued, independent of goals 2/4:

- **Multi-target race** in `AltitudeChart.BuildTargetSeriesList` + `AltitudeSeries.BuildSeriesList`. The async-void `Task.Run(() => BuildYearAndOptimalSeries())` closes over `this.Target` on the shared `AltitudeSeries` instance; the caller's foreach loop reassigns `mAltitudeSeries.Target = target` before the previous target's background task runs, so target A's Year/Optimal series ends up plotted with target B's RA/Dec. Fix: either snapshot `Target`/`Location` into the lambda, or parameterise `BuildSeriesList(Target, Location)`. `Location` is already safe because it's `Clone()`d at method entry.
- RA text-box range check consistency. Currently only `TextBox_RightAscension_TextChanged` enforces `[0, 24)`; the spinner path doesn't.
- Centralise the dusk/dawn hour-rounding block that's copy-pasted between `BuildDaySeries` and `BuildMoonSeries`.
- Any correctness fixes that fall out of Step 1.
- Optional: delete `Target/Parser.cs` — SGP `.sgf` support is obsolete for the user's current workflow.

### Step 4 — NINA plugin

Thin WPF shell over `Astronomy.Core` plus a TS SQLite access layer. NINA is .NET 10 / WPF, so none of the current WinForms UI transfers — a plugin is a fresh UI over the shared library. The TS access code XisfManager already has working could itself be factored out into the same (or a sibling) library.

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

## Current state of the code (post commit `49f2b4c`)

Recent fixes already in:

- Julian Day offset corrected (`+ 2415018.0` → `+ 2415018.5`).
- GMST replaced with the USNO one-liner (fixes the incomplete single-subtract mod).
- Latitude sign flip applied in `Astrometry.Location(...)` (was longitude-only).
- Polar `null` safety on astronomical dawn/dusk.
- RA standardised on hours `[0, 24)` project-wide; Target / UI / Parser / Astrometry all agree.
- Latitude / longitude setters coerce only on negative input, so unsigned UI magnitudes don't clobber the hemisphere checkbox.
- Year and Optimal series merged into a single day/minute pass — both available simultaneously, ~2× faster.
- `mTargetSeries` instance field eliminated in favour of a `MakeSeries(...)` factory; each build method now uses a local `Series`.
- "Now" red vertical line on Day / Year / Optimal, updating on the 5 s timer (timer is now enabled at launch based on `CheckBox_HoldTime`).
- `AltAz2RaDec`, `AngularDistance`, and the unused `JulianDay` helper deleted (all broken or dead).

Known open (all flagged earlier in this session):

- Multi-target race in `AltitudeChart.BuildTargetSeriesList` → `AltitudeSeries.BuildSeriesList`.
- `aboveHorizonAltitude` semantics in `BuildYearAndOptimalSeries` — likely records altitude at the crossing instant, not the max altitude during the qualifying window.
- `Target/Parser.cs` (SGP `.sgf`) is obsolete for the current workflow.
- `Location` and `Target` are public settable properties on `AltitudeSeries`; the shared-mutable-state smell is still there even after the `mTargetSeries` cleanup.
