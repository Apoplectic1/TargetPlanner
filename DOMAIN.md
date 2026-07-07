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
- **TP's role** is planning, not capture: **TPP** = today's single-filter planning (current TP); **TPS** = a planned multi-filter scheduling mode. TP reads the same NINA `.json` sequence files and the `.xisf` image library that the capture pipeline produces.
- Post-night, **XFM** (XisfFileManager) grades frames and writes graded counts back to the TS `scheduler.db`.
- Full cross-repo data-flow (scheduler.db, Catalog.db, the IS/ISP scheduler family) lives in the parent [`..\CLAUDE.md`](../CLAUDE.md).

## Planning strategy

- **Target floor / min-duration** are the two per-site planning scalars (`PlanningPreferences`). Home sites use a **45° floor** (avoid the murky low-altitude suburban sky + local obstructions); the dark-sky site drops to **30°**. The **240-minute (4 h) minimum duration** is the floor for what counts as a usable "best session" — the D-hour window (`BestSession.For`) must clear both the horizon/floor and the moon-clear gate for at least that long.
- **Moon avoidance** — two independent mechanisms: (1) a per-filter **Lorentzian avoidance gate** (ACP-style separation-vs-illumination curve, matches TS's formula to 1e-12) that carves moon-bright intervals out of the visibility window; (2) **K-S sky brightness** (Krisciunas–Schaefer) prediction on the Sky chart, modelling how much the moon brightens the sky at the target given filter bandwidth + Bortle + extinction. Narrowband filters tolerate a closer/brighter moon than broadband.
- **Filter choice vs the moon** is the core nightly decision TP informs: which filter's D-hour window survives tonight's moon, and for how long. This is what the Sessions / Year charts + the K-S Sky curve exist to answer.

## Rig

*Not yet captured here — imaging rig specifics (camera / sensor + pixel scale, OTA, mount, filter wheel) aren't recorded in the repo. Fill in when a domain decision depends on them (e.g. the K-S bandwidth defaults or a pixel-scale-dependent FWHM gate). Until then, the filter set `H / O / S / L / R / G / B` (`Filters/FilterLibrary.cs`) is the known rig-facing surface.*
