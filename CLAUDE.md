# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Windows Forms desktop tool for astrophotography planning. Given a target (RA/Dec) and a location (lat/long, horizon, minimum duration above horizon), it plots altitude over time and ingests Sequence Generator Pro `.sgf` sequence files to build a batch target list. Written for the author's own astrophotography workflow — defaults reflect that (default location "Penns Park", default target "M31", hardcoded SGP capture root `E:\Photography\Astro Photography\Captures\SGP`).

## Build / run

- Solution `TargetPlanner.sln` contains **two projects**:
  - `TargetPlanner/TargetPlanner.csproj` — WinExe, `TargetFrameworkVersion = v4.8.1`, legacy-style (packages.config), entry point `TargetPlanner.Program.Main`. Configurations: `Debug|AnyCPU`, `Release|AnyCPU`, `Debug|x64`, `Release|x64`.
  - `Astronomy.Core/Astronomy.Core.csproj` — SDK-style class library, `TargetFramework = netstandard2.0`, PackageReference. Produces `Astronomy.Core.dll` which the WinExe project references via `ProjectReference`.
- F5 in Visual Studio to run; otherwise `msbuild "TargetPlanner.sln" -restore /p:Configuration=Debug`. The `-restore` flag is required because the Core project's NuGet dependency (CoordinateSharp) is declared as a PackageReference — a plain `nuget restore` only covers the WinExe project's packages.config. `dotnet restore` / `dotnet build` still won't work because the WinExe side is legacy-style.
- No test project exists — there is no unit test framework wired up. Do not invent build/test commands.

## External dependencies that are easy to miss

- **LocalLib** is a private sibling assembly, not a NuGet package. The WinExe csproj hint path is `..\..\..\General\LocalLib\LocalLib\bin\Release\LocalLib.dll` (i.e. `E:\Projects\VisualStudio\General\LocalLib\...`). It supplies `OpenFolderDialog` used by `MainForm.Button_BrowseTargetList_Click`. If the reference is missing, the build fails; don't try to "fix" it by deleting the reference. Referenced only by TargetPlanner, not by Core.
- **NuGet packages**:
  - WinExe project (`packages.config`): `CoordinateSharp 3.4.1.1`, `Newtonsoft.Json 13.0.4`. Newtonsoft is used only by `AltitudeSeries.Clone<T>`; CoordinateSharp is pulled in transitively via the Core project reference but the `packages.config` entry remains for the WinExe's own consumption paths (MoonAltitude in `BuildMoonSeries`).
  - Core project (PackageReference): `CoordinateSharp 3.4.1.1`. Single dependency. Core is deliberately not coupled to Newtonsoft, WinForms, or System.Drawing.

## Architecture

The codebase is split at the assembly boundary: **`Astronomy.Core`** is pure, UI-free astronomical math plus POCOs; **`TargetPlanner`** is the WinForms chart/host/UI on top. Everything runs on the UI thread except target-list parsing and year-series construction, which are offloaded with `Task.Run`.

### `Astronomy.Core` — pure math, no UI

Shared library targeting `netstandard2.0`. Consumed today by TargetPlanner; designed to be consumed by XisfManager and a future NINA plugin without re-porting. See `SCHEDULER_DESIGN.md` for the full design rationale. Surface by subfolder:

- **`Targets/Target.cs`** — POCO. `RightAscension` is **decimal hours** `[0, 24)`; the setter derives `RaHours/RaMinutes/RaSeconds` from it. `Declination` setter coerces sign into the `North` bool (absolute value kept in the numeric field). Does **not** carry any chart / WinForms state.
- **`Locations/Location.cs`** — POCO. Latitude/longitude stored as decimal degrees with synthesized D/M/S accessors via property setter side-effects. Also carries `Horizon` (degrees), `Duration` (minimum time required above horizon for "Optimal" chart), `DateTime`, `TimeZone`.
- **`Night/NightWindow.cs`** — struct: `AstronomicalDawn`, `AstronomicalDusk`, `LunarIlluminationFraction`.
- **`Time/JulianDate.cs`, `Time/SiderealTime.cs`** — JD from UTC; GMST from JD; LST from UTC + east-longitude.
- **`AltAz.cs`** — `AltAz.At(target, location, utc)` and `AltAz.Of(target, location)` (reads `location.DateTime.ToUniversalTime()`). Returns `Tuple<altitude, azimuth>` in degrees. Replaces the old `GetAltitudeAzimuth`.
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
  - `BuildDaySeries` — minute-by-minute altitude from ~1h before dusk to ~1h after dawn.
  - `BuildYearSeries` — for each day of the coming year, the max nightly altitude between dusk and dawn. Populates `mYearCache` (per-day intermediates that do not depend on Horizon or Duration) so rebuild paths only touch the Optimal series.
  - `BuildOptimalSeries` — emits three curves on the Optimal chart area: `"-Optimal"` (peak altitude in a qualifying above-horizon window), `"-OptimalFloor"` (floor altitude of the best D-hour session — transit-centered when it fits, wall-pushed otherwise), `"-OptimalFloorCentered"` (floor of a strictly transit-centered session, `-90` if it doesn't fit). All three read from `mYearCache`; no `ComputeNight` or `GetAltitudeAzimuth` on rebuild.
  - `BuildMoonSeries` — filled area series using CoordinateSharp's `MoonAltitude`; alpha scaled by `LunarIlluminationFraction`.
- `RebuildOptimalSeries` is the entry point for Horizon/Duration spinner changes — cheap because it walks the cache.
- `AltitudeSeries.Clone<T>` round-trips an object through `JsonConvert` to deep-copy `Location` before mutating `DateTime` in the inner loops. This matters: the series builders run on background tasks and must not scribble on the shared `Location` the UI is bound to.
- `Forms/AltitudeChartForm` is a separate popup form used only by `Button_GraphTargetList_Click`, showing the entire ingested target list at once. It is distinct from the embedded `AltitudeChart` inside `MainForm`.

**UI flow (`Forms/MainForm.cs`):**
- `InitializeDynamicControls` constructs the embedded chart, registers the three chart areas, seeds the target list from `NinaTargetsRootPath`, and sets the combo box to `M31`.
- Coordinate inputs are triple-bound (D/M/S `NumericUpDown`, decimal `TextBox`, N/S/E/W checkbox). Every handler unsubscribes the sibling handlers before writing values back to avoid feedback loops — preserve that pattern when adding inputs.
- `mTimer` (5 s) refreshes "now" only while `CheckBox_HoldTime` is unchecked; `RadioButton_Now` vs `RadioButton_SetDateTime` governs whether date/time pickers drive the model.
- `Button_GraphEphemeride_Click` **tears down and rebuilds** `mAltitudeChart` every click rather than mutating the existing one. Don't try to "optimize" this into an in-place update without understanding the chart-area/series lifecycle.
- `NumericUpDown_Horizon_ValueChanged` / `NumericUpDown_Duration_ValueChanged` call `mAltitudeChart.RebuildOptimalData()` (and `UpdateHorizonLines()` for Horizon) — surgical, no full teardown. The cache-backed Optimal rebuild makes spinner scrubbing effectively instant.

## Conventions worth knowing before editing

- **RA is decimal hours** (`[0, 24)`) in `Target.RightAscension`. The UI presents D/M/S of arc only for Declination; RA D/M/S are derived hour/minute/second fields. Core's `AltAz` and `TargetGeometry` consume `RightAscension` directly as hours.
- **Signed hemispheres** are an TargetPlanner / Core convention: `Latitude`, `Longitude`, `Declination` are stored as positive magnitudes with a paired `North` / `West` flag. Core helpers that take "signed degrees" (e.g. `TargetGeometry.MeridianAltitude`) expect the caller to resolve the flag — see `AltAz.At` for the canonical resolution idiom.
- **Type-alias usings for Core types.** Files in `TargetPlanner.*` namespaces that reference `Astronomy.Core.Targets.Target` use a `using Target = Astronomy.Core.Targets.Target;` alias at the top. This is defensive: if a sibling `TargetPlanner.Target` namespace ever reappears, C# enclosing-namespace lookup would shadow the type otherwise. No such namespace exists today.
- Default coordinates (`Penns Park`, 40.28°N 74.99°W) live in `Location`'s constructor — they are intentional defaults, not test data.
- The hardcoded NINA targets root (`E:\Photography\Astro Photography\Captures\Nina\Targets`) lives in a single `MainForm.NinaTargetsRootPath` constant used by both the startup seed and the Browse-Target-List dialog's `InitialDirectory`. Change in one place.
- `Forms/MainForm.Designer.cs` is ~1400 lines of generated designer code — edit via the Visual Studio designer, not by hand.
- **Core has no static mutable state.** `Support/Astrometry.cs` (UI state facade) is the one place where static mutable properties live — that pattern is deliberately TargetPlanner-only and must not propagate into Core.
