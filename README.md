# TargetPlanner

Windows Forms desktop tool for astrophotography target planning. Plots a deep-sky target's altitude across a single night, scans a year for the best dates, and overlays multiple targets from a NINA sequence library.

![TargetPlanner v1.0.0 showing the Day altitude chart for Abell 21](docs/screenshot.png)

## What it does

- **Four chart areas** — *Day* (minute-by-minute altitude across the coming night, with twilight shading and a live "now" line), *Sky* (Krisciunas–Schaefer sky brightness in mag/arcsec² across the same night), *Year* (per-night altitude across 12 months), *Sessions* (Ceiling / Floor / Symmetric placement curves per night).
- **Multi-target overlay** — graph many targets at once. Filter via *Visible Tonight* / *Select All* / *Clear All*; sort by name, RA, declination, or transit time.
- **NINA sequence ingestion** — auto-loads every `DeepSkyObjectContainer` `.json` under your NINA Targets root and turns them into selectable targets. Skips `Calibration` and `Mosaics` folders.
- **Picker-driven moment** — Date / Time pickers drive the observation moment; *Now* snaps back to the current instant and moves the red now-line on the chart.
- **Sky brightness** — its own chart area (peer of Day / Year / Sessions). Krisciunas–Schaefer sky brightness in mag/arcsec², with per-Bortle baseline, atmospheric extinction, and per-filter wavelength scaling.
- **Moon avoidance** — per-filter Lorentzian moon-avoidance gates the Sessions-chart curves and Day-chart best-window overlay.

## Charts

Four chart areas swap behind the **Day / Sky / Year / Sessions** radios beside the chart.

**Day chart.** Minute-by-minute altitude through the coming night. Left edge is the hour boundary before astronomical dusk; right edge is the hour boundary after astronomical dawn. Yellow→gray gradient at left marks dusk twilight; gray→yellow gradient at right marks dawn. A red vertical line shows the current moment, refreshed by the **Now** button. A shared gray filled area shows moon altitude across the night. Click a curve to overlay its best window for tonight (see *HD Overlay* below).

**Sky chart.** Per-target sky-brightness curves in mag/arcsec² on a reversed Y axis (brighter sky reads higher). Same time axis as Day. Y range is 16–22 mag/arcsec². See [Sky brightness](#sky-brightness) below for the K-S model details.

**Year chart.** Per-night session-floor altitude across 12 months, one curve per target. X axis runs from the 1st of the current month to the 1st of the same month next year, with month-boundary tick labels. Hover any point: tooltip shows `{Target}\n{date}\nFloor: {alt}°` (or `(no fit)` for nights where no D-hour window meets the active Horizon / Duration / Moon filter).

**Sessions chart.** Three per-target curves describe how well a Duration-long imaging window fits inside each night's visibility arc, given your Horizon floor:
- **Ceiling** — peak altitude reached inside any qualifying window.
- **Floor** — floor altitude of the best D-hour placement (transit-centred when it fits, wall-pushed otherwise).
- **Symmetric** — floor altitude of a strictly transit-centred placement; renders as `—` if a D-hour window can't fit symmetrically around transit.

Hover any of the three curves to see all three values for that night.

## Domain terms

- **HD Overlay** — the Day-chart's best-window step function. Bounded by **H**orizon (the Y floor) and **D**uration (the minimum window length). Click a target's Day curve to overlay; click again to restore.
- **D-hour window** — a contiguous span of length ≥ Duration that stays above Horizon. The "best D-hour window" is the highest-quality placement of such a window inside tonight's visibility arc.
- **Ceiling / Floor / Symmetric** — the three Sessions-chart curves (above).

## Chart interactions

- **Left-click a legend item** — toggle that target's curves on/off.
- **Left-click a Day curve** — overlay the HD step function (target's best window for tonight); a second click on the same curve restores it.
- **Right-click anywhere on the chart** — restore every replaced curve at once.
- **Hover any data point** — per-point tooltip with target, time/date, and value(s).

## Targets

Two selection modes:

- **Single mode** — combo + RA/Dec inputs drive one target at a time. Default: M31.
- **Multi mode** — checkbox listbox drives a set of targets. NINA ingestion auto-loads every `DeepSkyObjectContainer` `.json` under your NINA Targets root (folders `Calibration` and `Mosaics` skipped). Checkboxes default to **none-checked** so you opt targets in rather than out.

*Visible Tonight* / *Select All* / *Clear All* filter the listbox; sort by name, RA, declination, or transit time. Click **Graph** to (re)render the chart — the chart panel is blank at launch until Graph is clicked.

## Locations

A combo of named locations drives lat / lon / elevation and the per-location sky-brightness inputs. Built-in presets:

- **Penns Park** (40.28°N, 74.99°W, 80.67 m).
- **Hillsborough** (40.459456°N, 74.612921°W, 28.16 m).

A **Custom** slot holds in-progress edits when you scrub the lat / lon / elevation / Bortle / extinction controls without saving.

Per-location fields:
- **Latitude / Longitude / Elevation** — the observer position.
- **Bortle class (1–9)** — drives the moonless dark-sky baseline V₀ used by the Sky-brightness overlay. Picking a class also pre-fills a typical extinction value.
- **Extinction *k*** at 500 nm (mag/airmass) — drives airmass attenuation in the Sky-brightness overlay.

**Penns Park always boots.** The app launches there regardless of which location you used last, so the chart loads in a known state. Pick Hillsborough or any saved location after start-up freely.

## Filters & moon avoidance

Filters serve two purposes: they pin the active wavelength for the Sky-brightness overlay (via centre-nm) and they carry the per-filter Lorentzian moon-avoidance parameters used by the Sessions-chart curves and Day-chart HD overlay.

A master **Enable** checkbox in the Moon Avoidance group gates moon-avoidance globally. When off, all curves render moon-blind.

The filter library ships with:
- **H / O / S** — narrowband, 60° / 7-day Lorentzian, line-centre wavelengths.
- **L / R / G / B** — broadband, 120° / 14-day Lorentzian, Bessell-ish centres.

Two parallel filter-selection surfaces stay in sync: a **Filters** dropdown in the menubar, and a radio strip beside the Lorentzian controls.

- **Right-click any filter** (menu item or radio) — opens the Edit Filters dialog pre-positioned on that filter. Add / Remove / per-row Defaults restore.
- A **Defaults** button beside the filter strip restores the active filter to factory defaults.
- A trailing `*` on a filter's name indicates it differs from the shipped factory defaults; the top-level **Filters** menu shows `*` if any filter is modified.

Lorentzian-control edits on the main form auto-save the active filter on a 500 ms debounce; the Edit Filters dialog uses a transactional Save against a shadow copy.

## Sky brightness

Click the **Sky** radio (peer of Day / Year / Sessions) to switch to the per-target sky-brightness chart in mag/arcsec². The Y axis range is 16–22 mag/arcsec² with brighter sky reading higher.

Sky brightness composes three contributions in linear (nanolambert) space:
- **Dark-sky baseline** V₀ — driven by the location's Bortle class, scaled by target airmass and extinction.
- **Solar twilight** — quadratic empirical ramp from 0 (at astronomical dusk/dawn, sun ≤ −18°) up to ~10 mag of brightening at sunset (sun = 0°). Zero through the dark middle of the night.
- **Moon contribution** — Krisciunas–Schaefer 1991 closed-form. Zero when moon is below horizon; spikes brightness around moonrise/moonset and stays bright through moon-up nights.

Hover any point on a Sky curve: tooltip shows `{time}\n{sky:0.0} mag/arcsec²`. The active filter's centre wavelength scales atmospheric extinction via Rayleigh λ⁻⁴ — switching from B (445 nm) to Hα (656 nm) shifts curves toward darker sky during moon-up periods.

## Time controls

Date and Time pickers hold the observation moment and drive every chart and label. **Now** snaps the pickers to the current instant and refreshes the red now-line on the Day chart. There's no auto-update timer — the moment moves only when you move it.

## Install

Download `TargetPlanner-win-Setup.exe` from the [latest release](https://github.com/Apoplectic1/TargetPlanner/releases/latest). The installer drops the app into `%LocalAppData%\TargetPlanner` with Start Menu / Desktop shortcuts and an Apps & Features entry.

The installer is unsigned, so Windows SmartScreen will warn on first launch. Click *More info → Run anyway*.

## Updates

The app checks for updates on startup and prompts before downloading. You can also trigger a check manually via *Help → Check for Updates...*. Updates are delivered as Velopack delta packages from GitHub Releases.

## Build from source

Requires Visual Studio 2022+ (or the .NET 10 SDK + MSBuild) plus the **Astronomy.Core** library at `..\..\Library\Astronomy.Core\` relative to this repo, referenced via `ProjectReference`. The Library is its own git repo; clone it next to this one or the build fails.

The TP project targets `net10.0-windows10.0.19041` (the Win10 2004 contract version is required for SkiaSharp.Views.WindowsForms 3.119.0 — the bare `net10.0-windows` would fall back to a `net462` lib that doesn't load on .NET 10). See [`CLAUDE.md`](CLAUDE.md) for architecture and coding-agent guidance.

```powershell
dotnet build TargetPlanner.sln -c Debug
```

Or open `TargetPlanner.sln` in Visual Studio and F5.

## Defaults

This is a personal tool — the defaults reflect the author's setup:

- Default target: **M31**.
- Default location: **Penns Park** (boots here regardless of last-selected location).
- Default selection in Multi mode: **none checked** after a NINA load.
- NINA targets root: `E:\Photography\Astro Photography\Captures\Nina\Targets` (constant at `MainForm.NinaTargetsRootPath`).

## More documentation

- [`CLAUDE.md`](CLAUDE.md) — architecture, conventions, and coding-agent guidance.
- [`RELEASING.md`](RELEASING.md) — how to cut a new release.
- [`ROADMAP.md`](ROADMAP.md) — planned work.
- [`SCHEDULER_DESIGN.md`](SCHEDULER_DESIGN.md) — design notes for the upcoming interval scheduler.
