# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Windows Forms desktop tool for astrophotography planning. Given a target (RA/Dec) and a location (lat/long, horizon, minimum duration above horizon), it plots altitude over time and ingests NINA sequence files (`.json`, `DeepSkyObjectContainer`) to build a batch target list. Written for the author's own astrophotography workflow — defaults reflect that (default location "Penns Park", default target "M31", hardcoded NINA targets root `E:\Photography\Astro Photography\Captures\Nina\Targets`).

## Build / run

- Solution `TargetPlanner.sln` contains **two projects**, both SDK-style:
  - `TargetPlanner/TargetPlanner.csproj` — WinExe, `TargetFramework = net481` (UseWindowsForms=true), entry point `TargetPlanner.Program.Main`. Configurations: `Debug|AnyCPU`, `Release|AnyCPU`, `Debug|x64`, `Release|x64`. Outputs land in `bin\Debug\net481\`, `bin\x64\Release\net481\`, etc. (the `net481` suffix is the SDK convention).
  - `Astronomy.Core/Astronomy.Core.csproj` — class library, `TargetFramework = netstandard2.0`. Produces `Astronomy.Core.dll` which the WinExe project references via `ProjectReference`.
- Both use `<PackageReference>` for NuGet. F5 in Visual Studio, `msbuild "TargetPlanner.sln" -restore -p:Configuration=Debug`, and `dotnet build "TargetPlanner.sln" -c Debug` all work. Prefer `dotnet build` in scripts (auto-restores, no `-restore` flag needed); `msbuild` is the fallback for anything the `dotnet` CLI doesn't cover (e.g. VS-specific build targets).
- No test project exists — there is no unit test framework wired up. Do not invent build/test commands.

## External dependencies that are easy to miss

- **LocalLib** is a private sibling assembly, not a NuGet package. The WinExe csproj hint path is `..\..\..\Libraries\LocalLib\LocalLib\bin\Release\LocalLib.dll` (i.e. `E:\Projects\VisualStudio\Libraries\LocalLib\...`). It supplies `OpenFolderDialog` used by `MainForm.Button_BrowseTargetList_Click`. If the reference is missing, the build fails; don't try to "fix" it by deleting the reference. Referenced only by TargetPlanner, not by Core.
- **NuGet packages** (both projects on `<PackageReference>`):
  - WinExe project: `CoordinateSharp 3.4.1.1`, `Newtonsoft.Json 13.0.4`. Newtonsoft is consumed by `Nina/TargetLoader.cs` (parses NINA `.json` sequence files) and `Settings/*` (app-settings serialization). CoordinateSharp is pulled in transitively via the Core project reference but the WinExe's own PackageReference remains for paths like `BuildMoonSeries` that consume `MoonAltitude` directly.
  - Core project: `CoordinateSharp 3.4.1.1`. Single dependency. Core is deliberately not coupled to Newtonsoft, WinForms, or System.Drawing.

## Architecture

The codebase is split at the assembly boundary: **`Astronomy.Core`** is pure, UI-free astronomical math plus POCOs; **`TargetPlanner`** is the WinForms chart/host/UI on top. Everything runs on the UI thread except target-list parsing and year-series construction, which are offloaded with `Task.Run`.

### `Astronomy.Core` — pure math, no UI

Shared library targeting `netstandard2.0`. Consumed today by TargetPlanner; designed to be consumed by XisfManager and a future NINA plugin without re-porting. See `SCHEDULER_DESIGN.md` for the full design rationale. Surface by subfolder:

- **`Targets/Target.cs`** — **immutable** POCO. Every property is read-only; mutations produce a new instance via `With(...)`. `RightAscension` is **decimal hours** `[0, 24)`; `RaHours` / `RaMinutes` / `RaSeconds` are get-only DMS-component accessors computed on read (not a duplicate store). `Declination` is stored as a non-negative magnitude; the constructor normalizes a negative value by flipping `North` and storing `abs(value)`. `Target.Default` returns M31 defaults. Does **not** carry any chart / WinForms state.
- **`Locations/Location.cs`** — **immutable** POCO. `With(...)` returns a new instance. Latitude / longitude stored as non-negative magnitudes with direction in `North` / `West` flags; a negative magnitude passed to the ctor flips the corresponding flag. D/M/S accessors (`LatDegrees` etc.) are computed on read. Also carries `Horizon` (degrees), `Duration` (minimum time required above horizon for "Optimal" chart), `DateTime`, `TimeZoneInfo`. `Location.Default` returns Penns Park defaults.
- **`Night/NightWindow.cs`** — struct: `AstronomicalDawn`, `AstronomicalDusk` (both `DateTimeKind.Utc`), `LunarIlluminationFraction`, plus `IsValid` (short-circuits the `DateTime.MinValue` polar-day / polar-night sentinel). See the `Core consumer contract` section below for the Kind=Utc rationale and the DST trap it closes.
- **`Time/JulianDate.cs`, `Time/SiderealTime.cs`** — JD from UTC; GMST from JD; LST from UTC + east-longitude.
- **`AltAz.cs`** — `AltAzCalculator.At(target, location, utc)` and `AltAzCalculator.Of(target, location)` (reads `location.DateTime.ToUniversalTime()`). Returns an `AltAz` readonly struct with `Altitude` and `Azimuth` properties (both degrees; azimuth from North, clockwise). Replaces the old `GetAltitudeAzimuth` / `Tuple<double,double>` pattern.
- **`TargetGeometry.cs`** — `MeridianAltitude`, `LowerCulminationAltitude`, `HourAngleAtAltitude` (returns `NaN` for never-reaches, `+Infinity` for always-above), `AltitudeAtHourAngle`, `AzimuthAtHourAngle`. All take signed-degrees lat/dec.
- **`Night/NightCalculator.cs`, `Night/TwilightCalculator.cs`** — `ComputeNight(location)` (−18° astronomical default) and `TwilightCalculator.ComputeNight(location, sunAltBelowDeg)` for the three standard thresholds (−18 / −12 / −6). Both pull dawn/dusk from CoordinateSharp's `AdditionalSolarTimes`.
- **`Horizons/IHorizonProfile.cs`** + `ScalarHorizonProfile`, `PolylineHorizonProfile`, `ObstructionTableHorizonProfile` — abstraction over a horizon altitude function `AltitudeAt(azimuth)`. Scalar wraps the legacy single-double case.
- **`Session/`** primitives (built for the planned interval scheduler; not yet consumed by the TargetPlanner chart):
  - `TransitTime.UtcAtOrAfter` — analytic LST=RA inverse.
  - `IntegratedQuality.OverSession` (Simpson, 20 points) + `IntegratedQuality.SinAltitudeOverSession` (closed form).
  - `VisibilityWindows.For` — above-horizon ∩ night.
  - `BestSession.For` — transit-centered-or-wall-pushed placement across visibility windows.
  - `QualitySamples.OverNight` — slot-size grid.
  - `RiseSet.NextAtOrAfter` — scalar analytic and `IHorizonProfile`-aware (scalar seed + bisection refine).
- **`Moon/MoonSeparation.cs`** — `DegreesAt` (topocentric target-moon angle) and `IntervalsAboveDeg` (night intervals above a threshold).

### `TargetPlanner` — WinForms host

**UI state facade (`Support/Astrometry.cs`).** Thin static class with **mutable static state** (`AstronomicalDawn`, `AstronomicalDusk`, `SunAltitude`, `LunarAltitude`, `LunarPhase`, etc.) populated by `Astrometry.Location(mLocation)`. MainForm binds its dawn/dusk/moon-phase labels to these. The math that used to live here moved to Core; `Astrometry.Location(...)` remains to populate the static cache and to roll dawn/dusk forward or backward by a day so the pair always brackets the coming night.

**Target ingestion (`Nina/TargetLoader.cs`, namespace `TargetPlanner.Nina`).** `TargetLoader.Load(rootFolder, IProgress)` enumerates every `.json` in the root plus every subfolder except `Calibration` and `Mosaics`, parses each as a NINA `DeepSkyObjectContainer`, and converts its sexagesimal `InputCoordinates` (RAHours/RAMinutes/RASeconds, DecDegrees/DecMinutes/DecSeconds, `NegativeDec`) into `Astronomy.Core.Targets.Target` POCOs. Malformed files are skipped silently. Called from `MainForm.GetNinaTargets` inside `Task.Run` with progress reported to `ProgressBar_ProcessObject`. The root is a single constant `MainForm.NinaTargetsRootPath`, used for both the startup seed and the Browse-Target-List dialog's `InitialDirectory`.

**Charting (`Charts/`):**
- `AltitudeChart` wraps a single `System.Windows.Forms.DataVisualization.Charting.Chart` control and maintains a list of named `ChartArea`s — `"Day"`, `"Year"`, `"Optimal"`. Only one chart area is visible at a time; `ShowChartAreaSeries(name)` swaps it in, re-applies axis formatting via `SetChartAreaAxis`, and enables matching series.
- `AltitudeChart` owns per-target chart state via `private Dictionary<Target, AltitudeSeries> mSeriesByTarget` (Core's `Target` POCO can't carry a WinForms chart reference). `SeriesFor(target)` lazy-creates on first access; lifetime is scoped to a single `AltitudeChart` instance.
- `AltitudeSeries` generates `Series` objects per target. **Series naming convention: `"{TargetName}-{ChartAreaName}"`.** `AltitudeChart.ShowChartAreaSeries` filters by `series.Name.Contains(chartAreaName)`, so never put the chart-area string into a target name.
  - `BuildSeriesList(IProgress<string> phaseProgress = null)` is the orchestrator: sync `BuildMoonSeries` + `BuildDaySeries`, then `await Task.Run(() => ComputeYearCache())`, then UI-thread `RenderYearSeries` / `RenderOptimalSeries`. The optional `IProgress<string>` reports `"Day"` / `"Year"` / `"Optimal"` once each per successful build — consumed by `MainForm`'s progress bar (3 ticks × N targets).
  - `BuildDaySeries` — minute-by-minute altitude from ~1h before dusk to ~1h after dawn. Synchronous, UI thread.
  - `ComputeYearCache` — pure compute of `mYearCache` (per-day intermediates: dusk/dawn/LSTs/AltAtDusk/AltAtDawn/TransitInNight/YearAlt/IsPolar/SentinelX) for 365 days. Called from `Task.Run`; no WinForms Series.Points access. Seeds from `Location.DateTime`, so picking a past/future date shifts the whole year scan.
  - `RenderYearSeries` — UI thread; writes the Year series points from `mYearCache`.
  - `RenderOptimalSeries(horizon, duration)` — UI thread; emits three curves on the Optimal chart area: `"-Optimal"` (peak altitude in a qualifying above-horizon window), `"-OptimalFloor"` (floor altitude of the best D-hour session — transit-centered when it fits, wall-pushed otherwise), `"-OptimalFloorCentered"` (floor of a strictly transit-centered session, `-90` if it doesn't fit). All three read from `mYearCache`; no `ComputeNight` or `AltAzCalculator.Of` on this path.
  - `BuildMoonSeries` — filled area series using CoordinateSharp's `MoonAltitude`; alpha scaled by `LunarIlluminationFraction`. The resulting `"Moon-Day"` series name is target-independent, so when rendering multiple targets the second-and-subsequent copies are skipped in `AltitudeChart.ShowChartAreaSeries` to avoid duplicate-name collisions in `mChart.Series`.
- `RebuildOptimalSeries` is the entry point for Horizon/Duration spinner changes — cheap because it walks the cache (no `Task.Run`).
- `AltitudeChart.ReloadWithTargets(Location, IEnumerable<Target>, IProgress<string> = null)` resets the transient state (series, strip lines, per-target cache, target list, Location snapshot) in place while keeping the `Chart` control, its `ChartArea`s, and legend alive across Graph clicks. Previously the chart was torn down and rebuilt per click; W2-11 flipped that. Don't revert — user zoom / legend-color-toggle state is meant to survive.

**UI flow (`Forms/MainForm.cs`):**
- `InitializeDynamicControls` constructs the embedded chart, registers the three chart areas, seeds the target list from `NinaTargetsRootPath`, and sets the combo box to `M31`. The embedded `mAltitudeChart` is built once here and reused across every Graph click via `ReloadWithTargets` (see above).
- Coordinate inputs are triple-bound (D/M/S `NumericUpDown`, decimal `TextBox`, N/S/E/W checkbox) via `Support/CoordinateInput.cs`. The helper owns all the "update one surface and keep the others in sync" plumbing through a single `mSuppress` flag — callers never unsubscribe / re-subscribe sibling events. Four instances: `mLatitudeInput`, `mLongitudeInput`, `mRaInput`, `mDecInput`. A single `ValueChanged` event per helper funnels into `OnLatitudeEdited` / `OnLongitudeEdited` / `OnRightAscensionEdited` / `OnDeclinationEdited`, each ~5 lines.
- **Time controls are picker-driven.** `DatePicker` / `TimePicker` hold the observation moment and drive `mLocation.DateTime` via `UpdateLocalDateTimeEvents`. `Button_Now` snaps the pickers to `DateTime.Now` and refreshes labels + the chart's red now-line. No 5-second poll timer; no "live now vs. held" radio / checkbox trio — deleted in favor of picker-is-authoritative semantics.
- `Button_GraphTarget_Click` reloads `mAltitudeChart` in place for the currently-selected single target. `Button_GraphAllTargets_Click` does the same for every **checked** item in `CheckedListBox_SelectedTargets`. Both snap the view-area radio to Day, set the title via `FormatChartTitle`, and move the now-line; both drive `ProgressBar_MultiTargetProcessing` via `BeginChartBuildProgress(targetCount)` (3 ticks per target — one per `"Day"` / `"Year"` / `"Optimal"` report from `AltitudeSeries.BuildSeriesList`, marshalled to the UI thread by `Progress<T>`; stale callbacks from in-flight prior builds are neutralized by a generation counter).
- `NumericUpDown_Horizon_ValueChanged` / `NumericUpDown_Duration_ValueChanged` call `mAltitudeChart.RebuildOptimalData()` (and `UpdateHorizonLines()` for Horizon) — surgical, no full teardown. The cache-backed Optimal rebuild makes spinner scrubbing effectively instant.

## Conventions worth knowing before editing

- **RA is decimal hours** (`[0, 24)`) in `Target.RightAscension`. The UI presents D/M/S of arc only for Declination; RA D/M/S are derived hour/minute/second fields. Core's `AltAz` and `TargetGeometry` consume `RightAscension` directly as hours.
- **Signed hemispheres** are an TargetPlanner / Core convention: `Latitude`, `Longitude`, `Declination` are stored as positive magnitudes with a paired `North` / `West` flag. Core helpers that take "signed degrees" (e.g. `TargetGeometry.MeridianAltitude`) expect the caller to resolve the flag — see `AltAz.At` for the canonical resolution idiom.
- **Type-alias usings for Core types.** Files in `TargetPlanner.*` namespaces that reference `Astronomy.Core.Targets.Target` use a `using Target = Astronomy.Core.Targets.Target;` alias at the top. This is defensive: if a sibling `TargetPlanner.Target` namespace ever reappears, C# enclosing-namespace lookup would shadow the type otherwise. No such namespace exists today.
- Default coordinates (`Penns Park`, 40.28°N 74.99°W) live in `Location`'s constructor — they are intentional defaults, not test data.
- The hardcoded NINA targets root (`E:\Photography\Astro Photography\Captures\Nina\Targets`) lives in a single `MainForm.NinaTargetsRootPath` constant used by both the startup seed and the Browse-Target-List dialog's `InitialDirectory`. Change in one place.
- `Forms/MainForm.Designer.cs` is ~1400 lines of generated designer code — edit via the Visual Studio designer, not by hand.
- **Core has no static mutable state.** `Support/Astrometry.cs` (UI state facade) is the one place where static mutable properties live — that pattern is deliberately TargetPlanner-only and must not propagate into Core.

## Core consumer contract

These are the explicit guarantees / expectations Core makes of its consumers (TargetPlanner today, XisfManager and a planned NINA scheduler plugin tomorrow). Preserve them when editing Core public API.

- **Collection returns are `IReadOnlyList<T>`.** Every Session / Moon helper that returns a list (`VisibilityWindows.For`, `QualitySamples.OverNight`, `MoonSeparation.IntervalsAboveDeg`, `BestSession`-style planners) types the return as `IReadOnlyList<T>`. Stick to that for any new Core method — callers are allowed to assume immutability, ordering stability, and no-nulls without checking.
- **DateTime.Kind contract.** `NightWindow.AstronomicalDawn` / `AstronomicalDusk` are `DateTimeKind.Utc` — absolute UTC instants. Core methods that take a `DateTime utc` parameter (`AltAz.At`, `SiderealTime.Local`, `JulianDate.FromUtc`, `MoonSeparation.DegreesAt`) expect UTC; `AltAz.Of` reads `Location.DateTime` and will convert via `.ToUniversalTime()` (no-op if already Utc). Feeding a Kind=Local `Location.DateTime` is fine; Unspecified is treated as Local. The one trap to avoid: don't call `.ToUniversalTime()` on a `DateTimeKind.Unspecified` instant that was originally computed under a different UTC offset (e.g. legacy CoordinateSharp wall-clock-at-offset returns) — that was the source of the 2026-11-01 / 2027-03-14 DST-transition bugs fixed in commit `5eb3d0b`.
- **Signed-degree inputs.** `TargetGeometry.MeridianAltitude`, `HourAngleAtAltitude`, `AltitudeAtHourAngle`, `AzimuthAtHourAngle` take **signed** lat / dec. The caller resolves the hemisphere flag (`latDeg = location.North ? +location.Latitude : -location.Latitude`). `AltAz.At` is the canonical resolution idiom — new helpers should follow that shape rather than re-exposing another `North` bool.
- **No static mutable state** in Core. Lazy-init of `EagerLoad` for CoordinateSharp is fine (read-only after ctor); everything else threads instances through parameters.
