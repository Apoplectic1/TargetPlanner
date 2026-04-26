# TargetPlanner

Windows Forms desktop tool for astrophotography target planning. Plots a deep-sky target's altitude across a single night, scans a year for the best dates, and overlays multiple targets from a NINA sequence library.

![TargetPlanner v1.0.0 showing the Day altitude chart for Abell 21](docs/screenshot.png)

## What it does

- **Three altitude views** — *Day* (minute-by-minute altitude across the coming night, with twilight shading and a live "now" line), *Year* (peak altitude per night across 365 days), *Optimal* (best continuous window per night that meets your Horizon + Duration filter).
- **Multi-target overlay** — graph many targets at once. Filter via *Visible Tonight* / *Select All* / *Clear All*; sort by name, RA, declination, or transit time.
- **NINA sequence ingestion** — auto-loads every `DeepSkyObjectContainer` `.json` under your NINA Targets root and turns them into selectable targets. Skips `Calibration` and `Mosaics` folders.
- **Picker-driven moment** — Date / Time pickers drive the observation moment; *Now* snaps back to the current instant and moves the red now-line on the chart.
- **Click a Day curve** to overlay that target's best continuous window on the chart; right-click to clear.
- **Live Horizon / Duration scrubbing** — adjusting the spinners rebuilds the Optimal chart and re-evaluates each target's best window in place.

## Install

Download `TargetPlanner-win-Setup.exe` from the [latest release](https://github.com/Apoplectic1/TargetPlanner/releases/latest). The installer drops the app into `%LocalAppData%\TargetPlanner` with Start Menu / Desktop shortcuts and an Apps & Features entry.

The installer is unsigned, so Windows SmartScreen will warn on first launch. Click *More info → Run anyway*.

## Updates

The app checks for updates on startup and prompts before downloading. You can also trigger a check manually via *Help → Check for Updates...*. Updates are delivered as Velopack delta packages from GitHub Releases.

## Build from source

Requires Visual Studio 2022 (or the .NET Framework 4.8.1 targeting pack + MSBuild) plus two sibling dependencies that aren't on NuGet:

- The **Astronomy.Core** library at `..\..\Library\Astronomy.Core\` relative to this repo (referenced via `ProjectReference`).
- A private **LocalLib.dll** at `..\..\..\Libraries\LocalLib\LocalLib\bin\Release\LocalLib.dll` (supplies `OpenFolderDialog` used by the Browse-Target-List button).

Without either, the build fails. See [`CLAUDE.md`](CLAUDE.md) for the full architecture overview.

```powershell
dotnet build TargetPlanner.sln -c Debug
```

Or open `TargetPlanner.sln` in Visual Studio and F5.

## Defaults

This is a personal tool — the defaults reflect the author's setup:

- Default target: *M31*.
- NINA targets root: `E:\Photography\Astro Photography\Captures\Nina\Targets` (constant at `MainForm.NinaTargetsRootPath`).

## More documentation

- [`CLAUDE.md`](CLAUDE.md) — architecture, conventions, build details.
- [`RELEASING.md`](RELEASING.md) — how to cut a new release.
- [`ROADMAP.md`](ROADMAP.md) — planned work.
- [`SCHEDULER_DESIGN.md`](SCHEDULER_DESIGN.md) — design notes for the upcoming interval scheduler.
