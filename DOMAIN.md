# Domain — observing context

The human/strategy home: the observing world TargetPlanner serves, distinct from [README.md](README.md) (app behaviour/UX) and [ARCHITECTURE.md](ARCHITECTURE.md) (code mechanics). Read when a change's "is this right?" depends on how the user actually images, not on the code. The cross-repo portfolio vocabulary (TP / NINA / TS / XFM / …) is defined in the parent [`..\CLAUDE.md`](../CLAUDE.md).

## Sites

The author's four personal presets are checked into `Settings/PersonalDefaults.cs` (`BuildSeedSettings()`) and ship in the public binary — a deliberate solo-consumer trade-off (site coordinates + floor values are public; the code comment notes the split-to-gitignored-partial path if TP ever ships to others). Boot lands on the last-selected site; on first run that's the seed's `LastSelectedLocationName` = **Penns Park**.

| Site | Lat / Lon | Elev | Bortle | Floor | Min-dur | Role |
|---|---|---|---|---|---|---|
| **Penns Park** | 40.2828°N, 74.9974°W | 81 m | 5 | 45° | 240 min | Home (PA suburban); seed boot default |
| **Hillsborough** | 40.4595°N, 74.6129°W | 28 m | 5 | 45° | 240 min | Home (NJ suburban) |
| **Cherry Springs** | 41.66°N, 77.82°W | 690 m | 1 | 30° | 240 min | Dark-sky destination (PA) — lower floor, darker skies |
| **Denver** | 39.7405°N, 105.0252°W | 1609 m | 9 | — | — | Travel site (mile-high, urban-bright) |

- **Horizon profiles** — per-site `.hrz` polyline horizon files are produced by the external `HRZ Generator` tool (not under Claude) and consumed by TP as `LocalHorizon`. When both a `.hrz` polyline and a non-zero scalar floor exist, TP composes them via `MaxOfHorizonProfile` (the higher of the two wins at each azimuth).
- **Elevation** is honored for rise/set (refracted horizon dip via `MeeusUtility.HorizonDipDeg`) and moon parallax, but NOT for the −6/−12/−18° twilight thresholds (those reference the celestial horizontal plane by convention).

## Capture workflow

- Imaging runs on the **BIRDWATCHER** PC (`\\BIRDWATCHER\…`) via **NINA** + the **Target Scheduler (TS)** plugin. The user moved off Sequence Generator Pro (SGP) to this NINA + TS workflow.
- **TP's role** is planning, not capture — single-filter planning of tonight's targets; multi-filter scheduling belongs to the scheduler side of the portfolio (TS/TSM today, IS/ISM planned). TP reads the same NINA `.json` sequence files and the `.xisf` image library that the capture pipeline produces.
- Post-night, **XFM** (XisfFileManager) grades frames and writes graded counts back to the TS `scheduler.db`.
- Full cross-repo data-flow (scheduler.db, Catalog.db, the IS/ISM scheduler pair) lives in the parent [`..\CLAUDE.md`](../CLAUDE.md).

## Planning strategy

- **Target floor / min-duration** are the two per-site planning scalars (`PlanningPreferences`). Home sites use a **45° floor** (avoid the murky low-altitude suburban sky + local obstructions); the dark-sky site drops to **30°**. The **240-minute (4 h) minimum duration** is the floor for what counts as a usable "best session" — the D-hour window (`BestSession.For`) must clear both the horizon/floor and the moon-clear gate for at least that long.
- **Moon limit** — one physical model, two surfaces: **K-S sky brightness** (Krisciunas–Schaefer) predicts how much the moon brightens the sky at the target given Bortle + extinction + band center. The Sky chart plots the absolute prediction; the placement gate accepts a minute iff the moon-driven brightening (Δmag over the moonless baseline) is within the active filter's **ToleranceMag**. Narrowband filters tolerate more brightening than broadband (emission-line signal doesn't scale with sky continuum) — NB 1.0 mag vs BB 0.30 by default. (The former ACP/TS Lorentzian gate was replaced 2026-07-24; its implied tolerance wobbled ~10–30× across the lunar cycle.)
- **Filter choice vs the moon** is the core nightly decision TP informs: which filter's D-hour window survives tonight's moon, and for how long. This is what the Sessions / Year charts + the K-S Sky curve exist to answer.

## Rig

Source of truth is **XFM's typed equipment models** (`XisfFileManager/Models/Telescopes/*.cs`, `Models/Cameras/*.cs` — XFM stamps these into XISF `TELESCOP`/`FOCALLEN`/`APTDIA` keywords); this section is the planning-facing summary. **Default combination: APM107R + Z183** (the "R" suffix = Riccardi 0.75× reducer, per `TelescopeConfiguration.GetTelescopeName`).

**OTAs** — each usable native or with the shared Riccardi 0.75× reducer:

| Name | What | Aperture | Native FL (f/) | Reduced FL (f/) |
|---|---|---|---|---|
| **APM107** | APM 107 mm Super ED refractor | 107 mm | 700 mm (f/6.5) | **531 mm (f/5.0)** ← default as APM107R |
| **EVO150** | Sky-Watcher EvoStar 150 refractor | 150 mm | 1000 mm (f/6.7) | 750 mm (f/5.0) |
| **NWT254** | 10″ Newtonian | 254 mm | 1100 mm (f/4.3) | 825 mm (f/3.2) |

**Cameras** (pixel scale on the default 531 mm):

| Name | What | Pixels | Scale @531 mm | Gain/Offset presets |
|---|---|---|---|---|
| **Z183** | ZWO ASI183MM Pro (2019, mono, QE 84%) | 2.4 µm (4.8 bin2) | **0.93″/px** (1.86 bin2) ← default | NB (111, 10) / BB (53, 10) |
| **Z533** | ZWO ASI533MC Pro (2021, OSC RGGB, QE 91%) | 3.76 µm | 1.46″/px | (100, 50) |
| **Q178** | QHY5III178M (2018, mono, QE 81%) | 2.4 µm | 0.93″/px | (40, 15) |
| **A144** | Atik Infinity (2018, colour RGGB, QE 70%) | 6.45 µm | 2.51″/px | — |

Default combo (Z183 @531 mm): ~0.93″/px, FOV ≈ **1.4° × 1.0°** (5496×3672 on the 13.2×8.8 mm sensor); native 700 mm tightens to 0.71″/px. The Z183 NB-vs-BB gain split (111 vs 53) is the capture-side echo of the narrowband-vs-broadband moon strategy above.

**Filters + wheel** — Starlight Xpress USB 7-position, 1.25″ (XFM `Keyword/KeywordList.cs`): Astrodon 3 nm Hα + [O III], Chroma 3 nm SII, Astrodon E-Series LRGB. TP's `Filters/FilterLibrary.cs` builtin `CenterNm`/`BandwidthNm` values (H 656.3/3, O 500.7/3, S 672.4/3, L 550/300, R 650/60, G 525/65, B 450/100) describe exactly this physical set — they're the K-S bandwidth inputs, so a filter swap on the wheel should be mirrored there.

**Mount** — not recorded in any repo (NINA on BIRDWATCHER owns pointing/guiding; TP plans purely from sky geometry). Add here only if a planning decision comes to depend on it (e.g. meridian-flip windows).
