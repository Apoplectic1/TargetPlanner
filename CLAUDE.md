# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Windows Forms desktop tool for astrophotography planning. Given a target (RA/Dec) and a location (lat/long, horizon, minimum duration above horizon), it plots altitude over time and ingests Sequence Generator Pro `.sgf` sequence files to build a batch target list. Written for the author's own astrophotography workflow — defaults reflect that (default location "Penns Park", default target "M31", hardcoded SGP capture root `E:\Photography\Astro Photography\Captures\SGP`).

## Build / run

- Single solution: `SGP Ephemerides.sln`, single project `SGP Ephemerides/SGP Ephemerides.csproj`.
- `TargetFrameworkVersion = v4.8.1`, `OutputType = WinExe`, entry point `SGP_Ephemerides.Program.Main`.
- Configurations: `Debug|AnyCPU`, `Release|AnyCPU`, `Debug|x64`, `Release|x64`. F5 in Visual Studio to run; otherwise `msbuild "SGP Ephemerides.sln" /p:Configuration=Debug` after `nuget restore` (this project uses `packages.config`, not PackageReference, so `dotnet restore` / `dotnet build` will not work).
- No test project exists — there is no unit test framework wired up. Do not invent build/test commands.

## External dependencies that are easy to miss

- **LocalLib** is a private sibling assembly, not a NuGet package. The csproj hint path is `..\..\..\General\LocalLib\LocalLib\bin\Release\LocalLib.dll` (i.e. `E:\Projects\VisualStudio\General\LocalLib\...`). It supplies `OpenFolderDialog` used by `MainForm.Button_BrowseTargetList_Click`. If the reference is missing, the build fails; don't try to "fix" it by deleting the reference.
- NuGet packages (`packages.config`): `CoordinateSharp 3.4.1.1`, `Newtonsoft.Json 13.0.4`. CoordinateSharp supplies all solar/lunar event calculations; the hand-rolled sidereal math in `Support/Astrometry.cs` is only used for target alt/az.

## Architecture

Everything runs on the UI thread except target-list parsing and year-series construction, which are offloaded with `Task.Run`.

**Data model (plain POCOs):**
- `Location/Location.cs` — latitude/longitude stored as decimal degrees with synthesized D/M/S accessors via property setter side-effects. Also carries `Horizon` (degrees), `Duration` (minimum time required above horizon for "Optimal" chart), `DateTime`, `TimeZone`.
- `Target/Target.cs` — `RightAscension` is stored as **decimal degrees, not hours**; the setter derives `RaHours/RaMinutes/RaSeconds` from it. `Declination` setter coerces sign into the `North` bool (absolute value kept in the numeric field).

**Astronomy (`Support/Astrometry.cs`):**
- Static class with **mutable static state** (`AstronomicalDawn`, `AstronomicalDusk`, `SunAltitude`, `LunarAltitude`, `LunarPhase`, etc.). `Astrometry.Location(location)` must be called before any consumer reads those fields. It also rolls dawn/dusk forward or backward by a day depending on whether the supplied `DateTime` is before or after today's dawn, so the pair always brackets the coming night.
- `GetAltitudeAzimuth(target, location)` is a hand-rolled GMST/hour-angle calculation, independent of CoordinateSharp. It returns `Tuple<altitude, azimuth, maxAltitude>` in degrees.

**Target ingestion (`Target/Parser.cs`):**
- `BuildObjectList(folder, IProgress)` recursively globs `*.sgf` (SGP sequence files — JSON), reads `arEventGroups[0].sName` as the target name and `arEventGroups[0].siReference.nRightAscension` / `nDeclination` as coordinates. It silently skips files whose name contains `flat` or `calibrat` (case-insensitive). Corrupt / unparseable files are skipped silently via try/catch. Called from `MainForm.GetSGPTargets` inside `Task.Run` with progress reported to `ProgressBar_ProcessObject`.

**Charting (`Charts/`):**
- `AltitudeChart` wraps a single `System.Windows.Forms.DataVisualization.Charting.Chart` control and maintains a list of named `ChartArea`s — `"Day"`, `"Year"`, `"Optimal"`. Only one chart area is visible at a time; `ShowChartAreaSeries(name)` swaps it in, re-applies axis formatting via `SetChartAreaAxis`, and enables matching series.
- `AltitudeSeries` generates `Series` objects per target. **Series naming convention: `"{TargetName}-{ChartAreaName}"`.** `AltitudeChart.ShowChartAreaSeries` filters by `series.Name.Contains(chartAreaName)`, so never put the chart-area string into a target name.
  - `BuildDaySeries` — minute-by-minute altitude from ~1h before dusk to ~1h after dawn.
  - `BuildYearSeries` — for each day of the coming year, the max nightly altitude between dusk and dawn (line chart by month).
  - `BuildYearOptimalSeries` — like `BuildYearSeries`, but the plotted altitude is the max altitude reached while the target was continuously above `Horizon` for at least `Duration`. Days that never clear that bar plot as `-90`.
  - `BuildMoonSeries` — filled area series using CoordinateSharp's `MoonAltitude`; alpha scaled by `LunarIlluminationFraction`.
- `AltitudeSeries.Clone<T>` round-trips an object through `JsonConvert` to deep-copy `Location` before mutating `DateTime` in the inner loops. This matters: the series builders run on background tasks and must not scribble on the shared `Location` the UI is bound to.
- `Forms/AltitudeChartForm` is a separate popup form used only by `Button_GraphTargetList_Click`, showing the entire ingested target list at once. It is distinct from the embedded `AltitudeChart` inside `MainForm`.

**UI flow (`Forms/MainForm.cs`):**
- `InitializeDynamicControls` constructs the embedded chart, registers the three chart areas, seeds the target list from the default SGP folder, and sets the combo box to `M31`.
- Coordinate inputs are triple-bound (D/M/S `NumericUpDown`, decimal `TextBox`, N/S/E/W checkbox). Every handler unsubscribes the sibling handlers before writing values back to avoid feedback loops — preserve that pattern when adding inputs.
- `mTimer` (5 s) refreshes "now" only while `CheckBox_HoldTime` is unchecked; `RadioButton_Now` vs `RadioButton_SetDateTime` governs whether date/time pickers drive the model.
- `Button_GraphEphemeride_Click` **tears down and rebuilds** `mAltitudeChart` every click rather than mutating the existing one. Don't try to "optimize" this into an in-place update without understanding the chart-area/series lifecycle.

## Conventions worth knowing before editing

- **RA is degrees, not hours** throughout the model. The UI converts. A lot of astronomy code assumes hours, so be deliberate.
- Default coordinates (`Penns Park`, 40.28°N 74.99°W) live in `Location`'s constructor — they are intentional defaults, not test data.
- The hardcoded path `E:\Photography\Astro Photography\Captures\SGP` appears in two places in `MainForm.cs` (`InitializeDynamicControls` seed, `Button_BrowseTargetList_Click` dialog initial directory). Change both or neither.
- `Forms/MainForm.Designer.cs` is ~1400 lines of generated designer code — edit via the Visual Studio designer, not by hand.
