# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Windows Forms desktop tool for astrophotography planning, plotting target altitude over time with multi-target overlay from NINA sequence files. **User-facing behaviour, defaults, and chart UX live in [README.md](README.md)** — this file focuses on code organisation, conventions, and gotchas for a coding agent. Hardcoded NINA root: `MainForm.NinaTargetsRootPath`. Single render entry point: `Button_Graph_Click` → `RenderArea(SelectedArea(), targets, ct)` which dispatches into the active sub-chart's `Render(...)` (no startup auto-build). NINA load is async; the post-load handler seeds `mSelection.SelectedSingle` to the first sorted target.

## Glossary

Acronyms used throughout this file and adjacent plans / memory files.

**Apps & plugins (the user's portfolio):**
- **TP** — TargetPlanner. *This* app.
- **NINA** — Nighttime Imaging 'N' Astronomy. The imaging-PC orchestrator the user has standardised on. Local clone path in memory `reference_nina_local_sources.md`.
- **SGP** — Sequence Generator Pro. NINA's predecessor in the user's workflow; TP was originally written around SGP `.sgf` files. Now historical.
- **TS / TSP** — Target Scheduler / TargetScheduler Plugin. Tom Palmer's existing NINA plugin. Reference for schema compatibility; the Lorentzian formula in `Astronomy.Core/Moon/MoonAvoidance.cs` matches TS's `AstrometryUtils.cs:126` to 1e-12. TSP's sync, scoring, image grading, and file-path tracking are out of scope for the user's IS family.
- **IS** — IntervalScheduler. User's new .NET 10 desktop app. Heavy lifting: editing projects / targets / exposure plans / templates, 5-minute precompute, plan review, "Replan". Owns the authoritative `scheduler.db` on the desktop.
- **ISP** — IntervalScheduler Plugin. User's new NINA plugin (`net10.0-windows`, NINA-hosted; NINA itself migrated to net10 as of 2026-05). Runtime executor + in-night editing UI. Reads the deployed plan from `scheduler.db`.
- **ISS** — ISSimulator. User's new .NET 10 ISP simulator. May evolve from the existing TP standalone app.
- **XisfManager** — User's existing .NET 10 image-management app. Performs post-night grading; updates `exposure_plan.accepted_count` via the shared `scheduler.db`.

**Architecture / refactor terms:**
- **VM** — view-model. Specifically `TargetSelection` (`State/TargetSelection.cs`) post-Phase-2.
- **SoC** — separation of concerns. The three-phase refactor (commits `0f6c81c` / `1e1986d` / `3425f8e`) is referred to as "the SoC refactor" throughout.

**Domain terms in the chart code** (user-facing definitions live in [README.md](README.md#domain-terms); the mapping below is for locating implementation):
- **HD Overlay** — Day-chart click-toggled best-window step function. Computed via `BestSession.For`; the LC2 `OverlayController` (sibling to `AltitudeSubChart_Day`) owns the state machine, swapping each `LineSeries<ObservablePoint>`'s data array between the original altitude curve and the `floor`-inside-window / `0`-outside-window step shape. Right-click anywhere on the chart calls `mOverlay.RestoreAll()`.
- **D-hour window** — `Astronomy.Core.Session.BestSession.For`. `Location.Duration` is the floor.
- **Ceiling / Floor / Symmetric** — three `LineSeries<ObservablePoint>` per target on `AltitudeSubChart_Sessions`. ONE legend item per target — clicking it toggles all three series' `IsVisible` together. `BestSession.ResolveCandidates` resolves visibility ∩ moon-clear once per night so `PlaceBest` (Ceiling/Floor) and `PlaceCentered` (Symmetric) see identical inputs.

## Build / run

- Solution `TargetPlanner.sln` contains **one project** authored here plus a `ProjectReference` to a sibling library:
  - `TargetPlanner/TargetPlanner.csproj` — WinExe, `TargetFramework = net10.0-windows10.0.19041` (UseWindowsForms=true), entry point `TargetPlanner.Program.Main`. The Win10 2004 contract version is needed because `SkiaSharp.Views.WindowsForms 3.119.0` (transitive via LiveCharts2) only ships modern-.NET assets at `net8.0-windows10.0.19041`. Configurations: `Debug|AnyCPU`, `Release|AnyCPU`, `Debug|x64`, `Release|x64`. Outputs land in `bin\Debug\net10.0-windows10.0.19041\`, etc. GC tuning (Server + Concurrent) lives in csproj `<ServerGarbageCollection>` / `<ConcurrentGarbageCollection>` properties — App.config was deleted during the .NET 10 migration since modern .NET ignores its `<runtime>` and `<startup>` sections.
  - `..\Library\Astronomy.Core\Astronomy.Core.csproj` — external sibling (see "External dependencies" below). Listed in `TargetPlanner.sln` as a project entry via its relative path so VS Solution Explorer shows it alongside TargetPlanner; the actual source tree lives in a separate git repo.
- F5 in Visual Studio, `msbuild "TargetPlanner.sln" -restore -p:Configuration=Debug`, and `dotnet build "TargetPlanner.sln" -c Debug` all work. Prefer `dotnet build` in scripts (auto-restores, no `-restore` flag needed); `msbuild` is the fallback for anything the `dotnet` CLI doesn't cover (e.g. VS-specific build targets).
- **Tests live in the Library repo, not here.** `Astronomy.Core.Tests` (xUnit + BenchmarkDotNet) is part of `..\Library\Astronomy.sln` and runs via `dotnet test ..\Library\Astronomy.sln` or `dotnet run -c Release --project ..\Library\Astronomy.Core.Tests -- --filter "*"` for the benchmark. Do not add a test project under this repo.

## External dependencies that are easy to miss

- **Astronomy library** is an external sibling at `E:\Projects\VisualStudio\Astronomy\Library\` (its own git repo). The WinExe csproj `ProjectReference` path is `..\..\Library\Astronomy.Core\Astronomy.Core.csproj`. Keep the Library repo cloned next to this one -- a missing sibling breaks the build. Extracted from this repo in commit `b28ef9e` (2026-04-23); history for the extracted files is still accessible via `git log -- Astronomy.Core/` here.
- **LocalLib was dropped** in the 2026-05-04 .NET 10 migration. The library's `OpenFolderDialog` (multi-select-folder picker via reflection on `System.Windows.Forms.FileDialogNative+IFileDialog` internals) didn't survive the modern WinForms rewrite — those internal types changed shape. `MainForm.Button_BrowseTargetList_Click` now uses the stock `FolderBrowserDialog` (single-folder selection only; multi-select was nice-to-have). The DLL hint path and `<Reference>` element are gone from the csproj.
- **NuGet packages** on the WinExe project: `Newtonsoft.Json 13.0.4`, `MinVer 7.0.0` (PrivateAssets=all; tag-derived `AssemblyVersion`), `Velopack 0.0.1298`. Newtonsoft is consumed by `Nina/TargetLoader.cs` (parses NINA `.json` sequence files) and `Settings/*` (app-settings serialization). CoordinateSharp was dropped in commit `2249834` (Phase 5–6 of the CS-removal effort) — Library is now pure-managed Meeus.

## Architecture

The codebase is split at the repo boundary: **`Astronomy.Core`** lives in the sibling `Library\` repo and provides pure, UI-free astronomical math plus POCOs; **`TargetPlanner`** (this repo) is the WinForms chart/host/UI on top, consuming Core via `ProjectReference`. The TP-side architecture has been through a three-phase SoC refactor (commits `0f6c81c` / `1e1986d` / `3425f8e`): chart construction is decoupled from cache construction, cache state is decoupled from selection state, and selection state is decoupled from UI controls. Concretely:

- **Selection state** lives in `TargetPlanner/State/TargetSelection.cs` — observable VM owning `KnownTargets`, `SelectedSingle`, `Checked`, `Mode`. UI controls (combo, listbox, buttons, RA/Dec inputs) are views: user input flows into the VM via mutators; VM events flow back to UI under a `mUpdatingUiFromVm` echo guard.
- **Cache state** lives in `TargetPlanner/Caches/ChartCacheStore.cs` — single-writer cache keyed by `(Location, Target)`. Renderer queries `IsReady` / `GetOrNull` / `GetOrBuildAsync`. The store owns the per-target year-cache build loops (the heaviest Meeus path in the chart pipeline).
- **Rendering** lives in `TargetPlanner/Charts/`. Phase 4 (LC2 chart migration) **shipped**: all four chart areas (Day / Sky / Year / Sessions) are LiveCharts2 v2.1.0-dev-365 sub-charts implementing `IAltitudeSubChart`. MS Charts (`System.Windows.Forms.DataVisualization`) is gone. MainForm holds `Dictionary<string, IAltitudeSubChart> mSubCharts` keyed by area; `MainForm.ShowOnlyAltitudeChart` flips `Visible` per radio. Detailed lessons-learned archive at `~/.claude/plans/i-thought-i-d-take-valiant-neumann.md`.

Threading model is now explicit:
- **UI thread**: form events, VM mutations, render passes, chart paint.
- **Cache worker** (`Task.Run` inside `ChartCacheStore.GetOrBuildAsync`, gated by an internal `SemaphoreSlim` cap of 4): per-target year-cache builds, per-Location `NightCache` build. Library is lock-free / managed-only since the `2249834` CS-removal — semaphore now caps actual parallel CPU work rather than a process-wide gate.
- **NINA loader** (`Task.Run` inside `MainForm.GetNinaTargets`): file enumeration + JSON parse. Result published into the VM via `mSelection.SetKnownTargets`, which fires `KnownTargetsChanged` on the UI thread; the VM event handler kicks off background `mCache.PrepareManyAsync` for cache pre-population.

**Deep architecture detail** (Core surface summary, cache store internals, sub-chart wiring, universal chart-behaviour contract, moon avoidance, K-S sky brightness, MainForm UI flow) lives in [ARCHITECTURE.md](ARCHITECTURE.md). Open it when editing `Charts/`, `Caches/`, `State/`, `Filters/`, or any of the chart-pipeline paths in `Forms/MainForm.cs`.

## Conventions worth knowing before editing

- **RA is decimal hours** (`[0, 24)`) in `Target.RightAscension`. The UI presents D/M/S of arc only for Declination; RA D/M/S are derived hour/minute/second fields. Core's `AltAz` and `TargetGeometry` consume `RightAscension` directly as hours.
- **Signed hemispheres** are an TargetPlanner / Core convention: `Latitude`, `Longitude`, `Declination` are stored as positive magnitudes with a paired `North` / `West` flag. Core helpers that take "signed degrees" (e.g. `TargetGeometry.MeridianAltitude`) expect the caller to resolve the flag — see `AltAz.At` for the canonical resolution idiom.
- **Type-alias usings for Core types.** Files in `TargetPlanner.*` namespaces that reference `Astronomy.Core.Targets.Target` use a `using Target = Astronomy.Core.Targets.Target;` alias at the top. This is defensive: if a sibling `TargetPlanner.Target` namespace ever reappears, C# enclosing-namespace lookup would shadow the type otherwise. No such namespace exists today.
- Default coordinates (`Penns Park`, 40.28°N 74.99°W, 80.67 m) live in `Location.Default`'s constructor call — they are intentional defaults, not test data. `SettingsStore.BuildDefaultNamedLocations` ships Penns Park (from `Location.Default`) and Hillsborough (40.459456°N, 74.612921°W, 28.16 m); `SettingsStore.MergeBuiltins` (called from `Load`) appends missing built-ins to a user's saved `settings.json` and auto-fills `Elevation = 0` entries by name match so existing settings pick up new values painlessly.
- **`Location.Elevation`** (meters above geoid, default 0) is honored by `MoonSeparation.ObserveAt` (parallax via `MoonPosition.Topocentric`, ~0.5″ per 1000 m) and by `AstroUtil.GetSunRiseAndSet` / `GetMoonRiseAndSet` (refracted horizon dip via `MeeusUtility.HorizonDipDeg`, ~0.26° at 80 m / ~2.93° at 10000 m, shifting moon-rise/set by ~25 sec / ~11 min respectively). Civil/Nautical/Astronomical twilight thresholds (-6/-12/-18) are by convention NOT elevation-corrected -- they reference the celestial horizontal plane.
- The hardcoded NINA targets root (`E:\Photography\Astro Photography\Captures\Nina\Targets`) lives in a single `MainForm.NinaTargetsRootPath` constant used by both the startup seed and the Browse-Target-List dialog's `InitialDirectory`. Change in one place.
- `Forms/MainForm.Designer.cs` is ~1400 lines of generated designer code — edit via the Visual Studio designer, not by hand.
- **Core has no static mutable state.** `Support/AstrometryUi.cs` (UI state facade) is the one place where static mutable properties live — that pattern is deliberately TargetPlanner-only and must not propagate into Core.

## Roadmap

Recently shipped work and currently-open follow-ups (priority order, including TP `SessionSolvers` UI surfacing, IS/ISP work, Velopack bump, perf chasing) live in [ROADMAP.md](ROADMAP.md).

## Core consumer contract

These are the explicit guarantees / expectations Core makes of its consumers (TargetPlanner today, XisfManager and a planned NINA scheduler plugin tomorrow). Preserve them when editing Core public API.

- **Collection returns are `IReadOnlyList<T>`.** Every Session / Moon helper that returns a list (`VisibilityWindows.For`, `QualitySamples.OverNight`, `MoonSeparation.IntervalsAboveDeg`, `BestSession`-style planners) types the return as `IReadOnlyList<T>`. Stick to that for any new Core method — callers are allowed to assume immutability, ordering stability, and no-nulls without checking.
- **DateTime.Kind contract.** `NightWindow.AstronomicalDawn` / `AstronomicalDusk` are `DateTimeKind.Utc` — absolute UTC instants. Core methods that take a `DateTime utc` parameter (`AltAz.At`, `SiderealTime.Local`, `JulianDate.FromUtc`, `MoonSeparation.DegreesAt`) expect UTC; `AltAz.Of` reads `Location.DateTime` and will convert via `.ToUniversalTime()` (no-op if already Utc). Feeding a Kind=Local `Location.DateTime` is fine; Unspecified is treated as Local. The one trap to avoid: don't call `.ToUniversalTime()` on a `DateTimeKind.Unspecified` instant that was originally computed under a different UTC offset (e.g. legacy CoordinateSharp wall-clock-at-offset returns) — that was the source of the 2026-11-01 / 2027-03-14 DST-transition bugs fixed in commit `5eb3d0b`.
- **Signed-degree inputs.** `TargetGeometry.MeridianAltitude`, `HourAngleAtAltitude`, `AltitudeAtHourAngle`, `AzimuthAtHourAngle` take **signed** lat / dec. The caller resolves the hemisphere flag (`latDeg = location.North ? +location.Latitude : -location.Latitude`). `AltAz.At` is the canonical resolution idiom — new helpers should follow that shape rather than re-exposing another `North` bool.
- **No static mutable state** in Core. Pure-Meeus path since `2249834` — every helper threads instances through parameters; consumers are free to call from any thread without external synchronization.
