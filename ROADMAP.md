# TargetPlanner — Roadmap

Last updated 2026-06-11. Shipped history lives in [CHANGELOG.md](CHANGELOG.md) (append-only, dated); git holds commit-level detail.

## Currently open (priority order)

Migrated from CLAUDE.md so the agent-facing reference stays lean. Order is rough recommendation, not a commitment.

1. **Velopack auto-update smoke-test** — `0.0.1589-ga2c5a97` shipped 2026-05-22 (commit `a1db619`); TP's Velopack API surface (`VelopackApp.Build().Run()`, `UpdateManager`, `GithubSource`, `IsInstalled`, `CheckForUpdatesAsync`, `DownloadUpdatesAsync`, `ApplyUpdatesAndRestart`, `UpdateInfo.TargetFullRelease.Version`) compiles clean on the new version. The dry-run release-cycle smoke test (`vpk pack` + `vpk release` + install + self-update on .NET 10) is still pending — auto-update flow hasn't been verified against a Velopack-hosted release feed at this version. (2026-08-02: bumped to `1.2.0`, matching the vpk CLI — same API surface, still compiles clean; XFM verified the identical stack end-to-end the same day, so the remaining risk here is TP-specific wiring, not Velopack itself. The next real TP release exercises it.)

2. **XISF pixel-data read support in TP via `Astronomy.PCL`** — distinct from the header-only `Astronomy.XISF` (which shipped 2026-05-18 and TP consumes transitively via `Astronomy.NINA`). This item is the *pixel-data* path through the native AVX2 PCL wrapper, for downstream image-processing needs. The native AVX2 / `/fp:fast` / pinned-toolset work in `Astronomy.PCL.Native.dll` is already in place (Library repo, commits `e7ae75c` / `6072f2f` / `b13266f`); TP just needs to consume it. AnyCPU was dropped from TP in commit `31527a7` ahead of this work, since `Astronomy.PCL`'s `<Platforms>x64</Platforms>` would force the issue when the reference goes in. Prep checklist for the moment pixel-data XISF integration starts — do as a dedicated commit immediately before the first feature commit, not bundled:

   1. **Build the native DLL first** via `msbuild` on `Library\Astronomy.sln` (Debug *and* Release if you want both TP configurations to consume it). The `dotnet build` trap in CLAUDE.md applies — managed-only builds silently skip the C++ vcxproj.
   2. **Add `<ProjectReference Include="..\..\Library\Astronomy.PCL\Astronomy.PCL.csproj" />`** to `TargetPlanner.csproj`. `Astronomy.PCL.csproj` already wires the native vcxproj with the correct `ReferenceOutputAssembly=false` / `SkipGetTargetFrameworkProperties=true` / `UndefineProperties=TargetFramework` idiom plus a `<Content Pack="true">` for `Astronomy.PCL.Native.dll`, so MSBuild copies the native DLL into TP's output automatically.
   3. **Optionally add `Astronomy.PCL` + `Astronomy.PCL.Native`** to `TargetPlanner.sln` as view-only entries (`ActiveCfg` set, `Build.0` omitted) — same convention as Library's `Vendored PCL` solution folder. Solution Explorer visibility + F12 navigation; csproj reference is the source of truth.
   4. **Verify** `TargetPlanner\bin\x64\<Configuration>\net10.0-windows10.0.19041\Astronomy.PCL.Native.dll` shows up in TP's output after build.

   Sequencing gotcha: TP's transitive `<Content>` references `bin\x64\$(Configuration)\Astronomy.PCL.Native.dll`, so TP Debug requires Library Debug native on disk, TP Release requires Library Release. Easiest rule: build `Library\Astronomy.sln` in both Debug and Release once via `msbuild`, then switch TP configurations freely.

3. **Lower-priority perf chasing** if anyone wants to push further — `GetSunAltitude` / `GetMoonAltitude` per-call allocations (144 B / 56 B; root cause not obvious without an allocation profiler), and `Math.FusedMultiplyAdd` in the `MoonPosition` periodic-term loops (~1-5%, hardware-FMA-dependent). The big wins from the 2026-05-04 session (`MoonSeparation.ObserveAt` -54%, `BestSession_For_Narrowband` -53%) already exhausted the easy lifts; remaining items are diminishing returns.

4. ~~**Code-quality audit residuals**~~ — closed 2026-05-24. The three small residuals from the 2026-05-19 six-tier audit shipped: filter-from-builtin dedup (CenterNm drift fix in `EditFiltersForm.OnGridCellClick` + `OnFilterDefaultsClick` simplified to record `with` expression), Year/Sessions tooltip-lookup pattern extracted to `Charts/FitTooltipResolver.cs`, view-radio handler boilerplate folded into `OnViewRadioCheckedChanged(RadioButton)` helper in `MainForm.ChartBuildPresenter.cs`. See [`docs/2026-05-19-code-quality-audit.md`](docs/2026-05-19-code-quality-audit.md) for per-item details.

5. **Rich-Target migration onto `Astronomy.NINA.Target` (scheduler-era, deferred)** — Phase C re-scoped 2026-05-21: the image library shipped as a minimal *bare-target* source via `ImageLibrary/ImageLibraryLoader.cs`. The remaining originally-Phase-C work — swapping every `Astronomy.Core.Targets.Target` callsite onto the rich `Astronomy.NINA.Target` (wraps the old type via `.Geometry`) and surfacing per-target Filter on the Sky chart (tint / legend badge / per-target K-S bandwidth) — is now scheduler-era. Do it when the scheduler mode actually needs the richness and the young `Astronomy.NINA.Target` shape has settled; migrating now would be churn against a moving type for zero current benefit. **Phase D** (`InputTargetAdapter` bidirectional to NINA's `InputTarget`) introduces the `NINA.Plugin` NuGet dep and unblocks future NINA sequence-JSON export; queued behind the migration.

6. ~~**Auto-paint chart on startup**~~ — closed 2026-05-26 (commit `4f709a8`). MainForm's constructor fires one `Apply(SnapshotCurrent(Array.Empty<Target>()))` right after coordinator construction, so the chart paints its baseline (axes, dusk/dawn gradient, moon overlay on Day) instead of staying blank-gray. Target curves only appear when the user explicitly checks a target or clicks `Button_Graph` — `LastAppliedTargets` stays empty until the user acts. Earlier iteration (commit `c055986`, same-day) auto-painted with the just-seeded `SelectedSingle` but introduced a special case where the combo target was always implicitly plotted regardless of checked state; the empty-targets approach honours "render reflects user intent" across every code path. See 2026-05-26 entry in §Recently shipped for details.

7. ~~**`ProgressBar_Processing` during scrubs**~~ — closed 2026-05-26 (commits `9ff3573` initial + `eabff00` Aero-animation-lag fix). `ChartCoordinator` owns the progress lifecycle via a `defaultProgressFactory` wired at construction; every Apply path — H/M/D scrubs, filter, moon, date/time/Now, location edits, `ResetForLocationChange`, graph-build — drives the bar through one funnel without per-callsite wrapping. The bar reliably reaches 100 % on cold pipelines via a 1 s hold-then-reset that masks Aero's ~500 ms internal animation lag (chart pipeline ticks `Value` 0→max in tens of ms; under default visual styles the visible bar lags behind the setter). Three progress shapes collapsed to two: chart pipeline (coordinator-owned `Progress<T>` closure) + load paths (`BeginScanProgress` / new `FinishScanProgress`). See the 2026-05-26 entry in §Recently shipped for details.

## Future-flagged TP-side work

These are real TP work items, deferred until a trigger condition lands. They aren't speculative — the shape is known; only timing is open.

### HorizonProfileSection overlay on Day chart

Optional polyline horizon overlay painting the per-site `.hrz` profile as a filled area (matching NINA's CustomHorizon visual). Pick up when the visualization is wanted. Note: the **planning** composition (target qualifies only when it clears `MaxOfHorizonProfile(polyline, ScalarHorizonProfile(floor))`) already ships — this is purely visual.

### Interim TP fix: Sky chart low-altitude K-S gate

`AltitudeSubChart_Sky.BuildOrUpdateTargetSeries` gates the per-minute K-S compute on target altitude — when `t.Altitude < KsLowAltitudeGateDeg` (currently 10°), the per-minute `ObservablePoint.Y` is set to `null` (line break) and the tooltip reads "(low altitude — K-S unreliable)". The chart's X-axis still spans the full night; curves visibly terminate at the gate boundary as the target rises through 10° at dusk or descends through 10° at dawn.

**Why the gate exists.** K-S 1991's dark-sky baseline `vDark = v0 − 2.5·log₁₀(X) + k·(X−1)` has an extinction term `k·(X−1)` that dominates at high airmass with high k. For Bortle 8–9 sites (k₅₀₀ ≥ 0.4), this predicts a sky darker than zenith from extinction alone below ~10° altitude — physically wrong for urban regimes where off-axis light pollution actually brightens the horizon via in-scattering (a Garstang/Falchi regime K-S doesn't model). Concrete test case (Markarian's Chain in Denver B9 on 2026-05-24): K-S predicted mag 21.5 (H filter) / 31.4 (O filter) at target alt 0.79° — far below the chart's `[16, 26]` axis floor.

**Related axis widening (paired with the gate).** The Sky chart's static Y axis was widened from `[16, 22]` to `[16, 26]` so bandwidth-aware K-S predictions for narrowband filters at darker sites land inside the chart. Without the widening, narrowband H at Bortle 5 (V₀≈20.5) predicts mag ~24 mid-night — invisibly clipped below the legacy axis floor. Tradeoff: 10-mag span vs legacy 6-mag squishes per-mag pixel height by ~40%. Future upgrade path (deferred): per-render dynamic bounds computed from v0 + bandwidth (~50 LOC, threading live bounds through the Labeler / inversion math / moon overlay scaling). Static `[16, 26]` is acceptable in the meantime; covers every realistic site+filter combo encountered.

**Remove this gate when the Library adopts a horizon-aware sky-brightness model** — see `..\Library\ROADMAP.md` §Open: K-S unphysical extinction-overdrive at low altitudes. Likely landing condition is Garstang 1986 / Falchi 2016 framework adoption (research-tool scope; not on near horizon). At that point K-S is reliable through the urban-horizon regime and the gate constant + branch can be deleted.

### Day + Sky chart merge

Day and Sky are conceptually two views of the same single-night data: same X axis, same fit decision (`BestSession.For` with identical args), same target colors, same dusk/dawn / now-line sections, same fit-tonight legend filter. They differ only in Y axis (altitude `[0, 90]` vs K-S `[16, 26]` with inverted Labeler), per-target compute (`AltitudeCurve.Sample` vs per-minute K-S walk), and a few mode-specific features (Day's HD overlay + horizon line + moon underlay; Sky's `RefreshSkyBrightness` cheap-scrub path). Merge into a single `AltitudeSubChart_DaySky` with both series sets present and a "Sky" `CheckBox` in the chart's top-left toggling visibility — cleaner mental model, instant toggle, ~100-200 LOC saved.

Deferred because the simpler ChartCoordinator skip-Render-on-redundant-area-change optimization (2026-05-17) achieves the same instant-toggle UX at far lower complexity, and the merge introduces real risks the user shouldn't take on without a second concrete use case. **Trigger to revisit:** when AL scheduler-result charts produce a second "two views of the same data" instance, the merged-chart pattern becomes reusable infrastructure rather than a one-off.

Design notes preserved for the future implementation:

- Single `AltitudeSubChart_DaySky` class using LC2 multi-Y-axis support: `CartesianChart.YAxes = new Axis[] { mYAxisAltitude, mYAxisSky }`. Each `LineSeries` has `ScalesYAt = 0` (altitude) or `1` (K-S, inverted Labeler).
- Two parallel series dicts populated per Render: `mAltitudeSeriesByTarget`, `mSkySeriesByTarget`. Always-compute-both — ~1 sec extra Render time for 44 targets, comparable to the cache-prep budget; instant mode toggle.
- Mode toggle via WinForms `CheckBox` overlaid through `mChart.Controls.Add(checkBox)` at chart top-left with margin. `CheckedChanged` reassigns `mChart.Series`, toggles `mYAxis*.IsVisible`, toggles `mHorizonLine.IsVisible` (altitude-mode only), swaps tooltip controller (Day's smooth-curve interpolated 300 ms vs Sky's per-DataPoint snap 30 ms), gates HD overlay click handler (no-op in Sky mode).
- Remove `RadioButton_Sky` from `MainForm.Designer.cs`; `RadioButton_Day` is the sole radio for the merged chart, Sky checkbox inside is the mode toggle.
- LC2 multi-axis stability risk (GitHub #470 / #1883) — Phase 0 prototype recommended (~2-3 hours) before committing: verify dual-axis rendering, axis-visibility toggle, section rendering across both axes, and `ScalePixelsToData` semantics with a hidden axis. Falls back cleanly to single-axis-swap design (Option B, less elegant but safer) if multi-axis is flaky.
- Estimated total: ~2-2.5 days when picked up.

## Historical / archive

The original 4-step sequencing plan (correctness audit → extract Astronomy.Core → in-place cleanup → NINA plugin) is complete or relocated. Step 4 (NINA plugin) moved to `..\IntervalScheduler\ROADMAP.md` on 2026-05-23 — it was IS work tracked here while there was no IS repo to receive it. The 2026-04-21 whole-repo CODE_REVIEW.md audit is closed (archived at `docs/archive/CODE_REVIEW-2026-04-21.md`); its final residual was Velopack which shipped 2026-05-22 and is now item 1 above. See the per-date entries in [CHANGELOG.md](CHANGELOG.md) for the substantive history.

## Recently shipped (digest)

Full dated history: [CHANGELOG.md](CHANGELOG.md) — the append-only shipped-history journal (entries relocated there 2026-07-12; git holds commit-level detail).

- 2026-08-11 — Ctrl+N wiring hoisted to AL `DiagnosticsHotkey.Register` (AL `f7d1423`): TP's local filter deleted; TP + XFM wire identically — WinForms routing uniform by construction.
- 2026-08-11 — Ctrl+N invoke-capture shipped and reverted same day (AL `371c204` → `06500c4`, user decision): the uniform TSM/TP/XFM contract is capture at OK time only; open-menu shots stay on Capture-in-5s.
- 2026-08-11 — Ctrl+N routed via app-level `IMessageFilter` (`Support/DiagnosticsKeyFilter.cs`): now fires in MenuStrip menu mode + modal WinForms dialogs (TSM parity; obs f231).
- 2026-08-06 — MinVer cross-repo cache workaround (`<MinVerVerbosity>` key-split in csproj): AL ProjectReferences no longer leak the Library's version onto the exe (bug shipped in XFM v2.1.0–v2.2.0; latent here).
- 2026-06-11 — Ctrl+N "Observation" dialog renamed "Diagnostics"; helper label dropped.
- 2026-05-28 — MoonEphemeris + AltitudeCurve reshape; TP cache axes rekeyed to `NightDate`.
- 2026-05-27 — verify-ui skill + Capture snapshot; MainForm partial-class decomposition (−42%); TP-side test project Phases 1–4 (187 tests).
- 2026-05-26 — Chart baseline paint at boot; scrub-path progress bar; misc polish.
- 2026-05-23 — `SessionSolvers` UI surfacing (Longest / Highest sort modes).
