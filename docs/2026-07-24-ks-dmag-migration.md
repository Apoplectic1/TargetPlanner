# TP migration: K-S Δmag moon gate (Library `9e16469`, 2026-07-24)

**If you are reading this because TP won't build: that is expected.** The Library replaced the
Lorentzian moon-avoidance model with a K-S sky-brightness Δmag gate, breaking TP's compile until
this migration lands. Full rationale + calibration:
`Library/openspec/changes/ks-dmag-moon-gate/` (proposal / design / spec) and the Library
CHANGELOG entry *2026-07-24 — K-S Δmag moon gate*.

## What changed in the Library

- `MoonAvoidanceProfile` (SeparationDeg / WidthDays / RelaxEnabled / RelaxMinAltDeg /
  RelaxMaxAltDeg / RelaxScale) and the `MoonAvoidance` statics are **gone**.
- New `Astronomy.Core.Moon.MoonLimitProfile`: **`Enabled` / `ToleranceMag` / `CenterNm`** —
  accept a minute iff the K-S-predicted sky brightening from the moon is ≤ `ToleranceMag`
  (mag/arcsec² over the moonless baseline). Singletons `Disabled` / `Narrowband` (1.0, 656) /
  `Broadband` (0.30, 540), factory `Custom(toleranceMag, centerNm)`, `With(...)`.
- `BestSession.For` / `ResolveCandidates` and `SessionSolvers.{LongestDuration, LowestHorizon,
  LongestDurationCentered, LowestHorizonCentered}` now take `MoonLimitProfile?`. Null/Disabled
  semantics unchanged (moon-blind).
- The gate refraction-corrects moon altitude internally and reads site params (Bortle → v0,
  `ScaleK(ExtinctionK, CenterNm)`) from the `Location` — TP passes nothing new.
- `MoonAvoidance.DaysInLunarCycle` → `LunarAge.SynodicMonthDays` (same value).

## TP changes required

1. **`Filters/Filter.cs`** — record drops the five Lorentzian/Relax fields, gains
   `double ToleranceMag`; keeps `Name`, `CenterNm`, `BandwidthNm` (Sky chart still uses both
   band fields). Ctor arity 9 → 5. `ToProfile()` becomes
   `new MoonLimitProfile(enabled: true, ToleranceMag, CenterNm)`.
2. **`Filters/FilterLibrary.cs`** — rebuild the 7 builtin rows with the new shape.
   **Defaults: H/O/S → ToleranceMag 1.0 (narrowband); L/R/G/B → 0.30 (broadband).**
   `DiffersFromBuiltinDefault` compares the new field set. Drop `MigrateLegacyFields` if it
   references dead fields (no back-compat, per portfolio rule).
3. **`State/PlanningPolicy.cs`** — `MoonProfile` property type → `MoonLimitProfile`; update the
   Lorentzian-contract doc paragraphs (lines ~11-14, ~27-33, ~69-73).
4. **`Caches/ChartCacheStore.cs`** — profile re-derivation (`:514-516`) and the three compute
   helpers (`:729` / `:765` / `:779`) swap the type; `BestSession.ResolveCandidates` call
   unchanged in shape. **Also delete the dead moon sweep**: `ComputeYearDays`'s
   `MoonSamples` population (`:660`, `:683-694`) is written and read nowhere — delete it,
   `Caches/MoonSample.cs` (`MoonSweepSample`), and `NightCacheEntry.MoonSamples` (`:57`)
   rather than porting them.
5. **UI collapse** — `Forms/MainForm.Designer.cs`: of the 11 controls in
   `GroupBox_MoonAvoidance`, keep `CheckBox_Moon_AvoidanceEnable` + the `GroupBox_Moon_Filters`
   radio strip; **replace the Separation/Width/Relax controls (6 numerics + relax checkbox +
   labels) with one spinner**: "Moon impact tolerance (Δmag)", range 0.00–3.00, increment 0.05,
   2 decimals. `Forms/Presenters/MainForm.FilterMenuPresenter.cs`: `WriteProfileToControls`
   (`:549-574`), `OnLorentzianControlChanged` (`:458-475`), `OnRelaxEnabledChanged`
   (`:481-487`), `SetLorentzianControlsEnabled` (`:582-607`) collapse accordingly — the
   500 ms auto-save debounce and active-filter write-back pattern stay as-is.
6. **`Forms/EditFiltersForm.cs`** — 9 grid columns → 4 (`Name`, `Tolerance (Δmag)`,
   `Center (nm)`, `Bandwidth (nm)`); `FilterRow` shadow props / `From` / `NewDefault` /
   `ToFilter` follow; `RecomputeFormSize` auto-shrinks.
7. **`Forms/Presenters/MainForm.SortPresenter.cs`** — `:248` / `:269` pass
   `ctx.Policy.MoonProfile` to `SessionSolvers`; type-only change.
8. **`State/HdmKey.cs`** — **no design change needed**: it keys on `Filter` structural equality,
   so the new field set flows through automatically.
9. **Tests** — 7 files touch the old shape: `FilterTests`, `FilterLibraryTests`,
   `FilterLibraryPersistenceTests`, `PlanningPolicyTests`, `HdmKeyTests`, `ChartContextTests`,
   `ChartCacheStoreTests`. Rework onto the 5-field record + `MoonLimitProfile`.
10. **Docs** — TP ARCHITECTURE/README/CHANGELOG mention the Lorentzian (~24 hits across 8 files);
    update alongside.

## Operational step (one-time, no migration code)

Delete `%APPDATA%\TargetPlanner\filters.json`. The old shape partially binds (a missing
`ToleranceMag` lands at 0.0 = maximally strict); deleting re-seeds from the new builtins.

## Expected behavior shift (physics, not a regression)

Calibration showed the Lorentzian's implied sky-quality tolerance wobbled ~10–30× across the
lunar cycle. With the cycle-median defaults, **near full moon the gate is stricter than the old
rule for both families** (fewer fits on bright-moon nights — strongly so for broadband, whose old
full-moon boundary admitted ≈5–6× integration cost) and **more permissive at half/crescent**.
Raise a filter's tolerance toward ~1.6 to approximate the classic full-moon Hα behavior.
Also: the moonset boundary shifts ~2 min later (apparent altitude — now agrees with the Sky chart).

## Verification

`..\build-all.ps1` green end-to-end, TP unit tests pass, then visual verification: the collapsed
Moon Avoidance group box, per-filter tolerance editing in Edit Filters, and chart fit changes on
a bright-moon vs crescent night.
