# CHANGELOG.md — shipped history

**Charter:** the shipped-history journal — append-only, dated entries, roughly newest first
(order preserved verbatim from the ROADMAP.md relocation of 2026-07-12, per the A2 revision).
Current plans and the short digest live in [ROADMAP.md](ROADMAP.md); git holds commit-level
detail. New entries are appended at the top as work ships. (Provenance: some early entries
were first archived out of CLAUDE.md's "Open follow-ups" / "What shipped" sections to keep
that file under the perf-warning threshold, then relocated here; commit hashes preserved
throughout for archaeology.)


### 2026-08-10 — v1.3.5: payload realigned to AL 1.7.1

No app changes; AL cut a docs-only v1.7.1 (CONSUMERS.md records XFM as third consumer) after TP
v1.3.4 packed against 1.7.0, and all app payloads realigned on 1.7.1 the same day so embedded
`Astronomy.*` stamps read identically across TP/TSM/XFM. Also carries the RELEASING.md note on the
GitHub-mints-lightweight-tag race in release.ps1's final tag push (bit v1.3.4).

### 2026-08-10 — app TFM raised 10.0.19041 → 10.0.26100.0 (portfolio unification)

Rode AL's `diagnostics-portable-core` consumer window: app + Tests TFM raised to
`net10.0-windows10.0.26100.0` so all three portfolio apps (TP/TSM/XFM) share one SDK surface. The
SkiaSharp asset-floor rationale for the old 19041 pin still holds at any ≥ 19041 (comment updated in
the csproj). AL's Diagnostics is now a layered stack (TFM-neutral core + `.Windows` capture +
`.WinForms` shell) — TP's surface is unchanged, `DiagnosticsDialog.ShowOrFocus` as before, rebuilt
against the new layering. 184/184 tests green, 0 warnings. Ctrl+N manual re-check pending.

### 2026-08-06 — release path hardened: tag on origin ⇒ installable Release

`scripts\release.ps1` now owns the pushes: publish gates up front (branch is `main`, clean
tree, tag points at `HEAD` — all skipped under `-NoUpload`), then `push origin main` →
`vpk upload` → `push origin <tag>` only after the upload succeeds. `main` must go up before
the upload because vpk's createRelease names a not-yet-pushed tag and GitHub materialises it
at the default-branch HEAD. Closes the tag-without-Release failure mode (the historical
`v1.1.0`–`v1.2.0` wrinkle) by construction; RELEASING.md flow updated (tag locally, script
pushes). Rode along: CLAUDE.md's stale `Velopack 0.0.1589` → `1.2.0`.

### 2026-08-06 — Ctrl+N dialog now consumed from AL (`adopt-shared-diagnostics-dialog`)

`Forms\DiagnosticsDialog.cs` deleted in favour of AL's new `Astronomy.Diagnostics.WinForms`
satellite (the dialog graduated there verbatim — TP was its origin). New `ProjectReference` + sln
entry; the Ctrl+N `ProcessCmdKey` and App-menu call sites retarget; zero behavior change. XFM
adopts the same shared dialog the same day. Tests 184.

### 2026-08-02 — MIT license adopted

`LICENSE` added (MIT, © 2020–2026 Dan Stark) and linked from the README — same-day portfolio move with AL (also MIT) and TSM (copyright holder aligned). Repo was previously unlicensed (all rights reserved by default).

### 2026-08-02 — released v1.3.1: all Library DLLs ship Release (sln-membership fix)

v1.3.0's payload carried **Debug** builds of `Astronomy.Catalog` and `Astronomy.Diagnostics`:
both were `ProjectReference`d but missing from `TargetPlanner.sln`, and MSBuild's sln
semantics *unset* Configuration/Platform for references outside the solution — so they built
with their own defaults (Debug|AnyCPU) in every sln-driven build since they were adopted.
Fixed by adding both to the sln with `Release|x64` mappings; all five `Astronomy.*` DLLs now
resolve from `bin\x64\Release`. (TSM is immune — its release publishes the *project*, so
Configuration flows to all references; XFM has no Library refs.) Lesson: when a new
`ProjectReference` crosses repo boundaries, add the project to the consuming sln too —
`dotnet build <sln>` silently Debug-builds outside members.

### 2026-08-02 — released v1.3.0 (first published release since v1.0.0)

First release under the aligned flow, and the first GitHub Release since April's `v1.0.0` —
`v1.1.0`/`v1.1.1`/`v1.2.0` had been tagged but never published, so installed copies (still
1.0.0) jump straight to 1.3.0 via the Velopack delta (baseline verified byte-identical to the
published 1.0.0 assets). Ships everything since: XISF cache work, zero-warning ratchet +
fixture fix, the convention alignment below, Velopack 1.2.0. This also closes the
release-cycle portion of ROADMAP item #1's Velopack smoke test; the in-app self-update prompt
on an installed 1.0.0 is the remaining field check.

### 2026-08-02 — release conventions aligned with TSM/XFM (title, script, RELEASING)

Portfolio-wide convention pass, applied the same day to XFM: window title is now app name +
version (`TargetPlanner 1.2.0`, the `v` prefix dropped — TSM pattern, now a portfolio-general
rule); RELEASING.md rewritten to the shared structure (charter, mirror, branch policy — `dev`
never pushes, every `main` push carries a tag, ff-only merges — local-Velopack distribution,
content rules); `release.ps1` header flow aligned (tag rides a `main` push, not a bare tag
push). Dependency bumps riding along: Velopack `0.0.1589-ga2c5a97` → `1.2.0` (matching the
vpk CLI; API surface unchanged) and Test SDK `18.6.0` → `18.8.1`.

### 2026-08-01 — zero-warning ratchet + the fixture drift it flushed out

Both projects now build with `<TreatWarningsAsErrors>` (portfolio-wide ratchet, same day as AL/TSM; both
were already warning-clean in Debug and Release, so it locks in the existing state — the next warning is a
build break, and AL's ratchet also arrives transitively through the `ProjectReference`s).

Running the suite for verification flushed out a **pre-existing** break: 6 XISF tests failing because
`SyntheticXisf` — a deliberate cross-repo copy of AL's fixture, whose own comment says "sync if either
drifts" — had drifted. AL made the `<Image>` `geometry` attribute mandatory on 2026-07-29 (mandatory per
the XISF spec; present on all 18,650 real frames) and updated its fixture; TP's copy lagged, so
`XisfHeaderReader` rejected every synthetic file and `ParseFileAsync` returned null. Fixed by writing
`geometry="5496:3672:1"` like AL's fixture; the drift incident is now recorded in the fixture's comment.
184/184 green. Lesson for the paired-commit convention: an AL contract change isn't fully landed until the
consumers' *test fixtures* are re-run, not just their builds.

### 2026-07-24 — DiagnosticsDialog thins to the WinForms shell over `ObservationSession` (Library `731a245`)

The Ctrl+N observation orchestration — id minting, USER_OBS START/CAP/END/CANCEL sequencing with
the single-terminator guarantee, capture counting, status wording, the hide → grab → reshow
choreography, and the guarded context-provider call — moved to
`Astronomy.Diagnostics.ObservationSession` (lifted from TSM's `DiagnosticsWindow`, the model; AL
contract assumption #25). The dialog keeps only framework glue: the `Form`/controls, the
`sCurrent` singleton, and three delegates (owner `Bounds` / `Hide()`+owner `Refresh()` /
`Show()`+focus-notes) with `settleDelayMs: 0` (WinForms hide is synchronous — no DWM settle
needed, unlike WinUI's 450 ms default). `mTerminationLogged` replaced by the idempotent
`mSession.Cancel()` in `FormClosing`. **Three deliberate UX changes, unifying to TSM's
conventions (decided 2026-07-24):** (1) Enter now types a newline and **Ctrl+Enter commits**
(was inverted — `AcceptButton` dropped, Esc still cancels); (2) new **"Capture in 5 s"** button
for shooting light-dismiss menus/dropdowns (dialog widened 420→480 px); (3) status text becomes
the shared `captured N · HH:mm:ss` / `capture failed — see tp.log` wording (was ASCII `-`).
Log-line grammar unchanged. Verified: sln builds, TargetPlanner.Tests 184/184; **visual pass
complete** (2026-07-24, session id=d162: instant capture + delayed capture + checkpoint END, one
terminator, build stamp = this commit). The pass caught one layout bug — the status text
overlapped OK at 480 px — fixed same day (client 480→600 + MinimumSize floor, `532d659`) and
re-verified.

### 2026-07-24 — K-S Δmag moon gate migration (Library `9e16469`)

The Library replaced the ACP/TS Lorentzian with the K-S Δmag gate; TP migrated per
`docs/2026-07-24-ks-dmag-migration.md`. `Filter` record shrank 9 → 4 fields (five
Lorentzian/Relax fields → one `ToleranceMag`; `CenterNm`/`BandwidthNm` kept for the Sky
chart); builtins now NB 1.0 mag (H/O/S) / BB 0.30 (L/R/G/B) — the per-filter distinctions
the old table hand-encoded (OIII stricter than Hα) now emerge from K-S wavelength physics.
The Moon Avoidance group box collapsed 11 controls → 4 ("Moon Limit (K-S Δmag)": Enable,
tolerance spinner, units label, filter strip — all Designer-declared, repositionable);
`EditFiltersForm` 9 → 4 columns; `MigrateLegacyFields` deleted (no back-compat). The dead
`MoonSweepSample`/`NightCacheEntry.MoonSamples`/`MoonAgeDays` year-sweep — written every
pass, read nowhere — was deleted rather than ported. `filters.json` re-seeded from the new
builtins. Expected chart shift (physics, not regression): stricter near full moon (strongly
for broadband), more permissive at half/crescent; moonset boundary ~2 min later, now
agreeing with the Sky chart. 184 tests pass.

### 2026-05-23 — `SessionSolvers` UI surfacing: Longest / Highest sort modes

TP's UI surface for the `SessionSolvers` Library API (flagged "needs UX design" in "Currently open") shipped as two new `ComboBox_SortTargets` modes: **Longest** (`SessionSolvers.LongestDuration`) and **Highest** (`SessionSolvers.LowestHorizon`), alongside the existing sort modes. `MaybeResortForSessionSolversInputChange()` wires live re-ranking on any input the solvers depend on — Floor / Duration / Location / Filter / Moon / Horizon edits all trigger a re-sort while one of the two modes is active. Commit `fec33c9`; `Forms/Presenters/MainForm.SortPresenter.cs:242-296`.

### 2026-06-11 — Rename Ctrl+N "Observation" dialog → "Diagnostics"; drop helper label

Mirrors the same-day TSM rename (TSM commit `ca97d89`) so both apps name the Ctrl+N
screenshot/notes feature "Diagnostics" — disambiguating it from TP's *domain* "observation"
(the `ObservationMoment` / observing-the-sky vocabulary, untouched). The shared
`Astronomy.Diagnostics` **log protocol keeps its `USER_OBS_*` marker names** (`UserObservationStart`/
`End`/`Cancel`/`Capture`), so existing `tp.log` parsing and `Get-TPObservations` are unaffected.

- `Forms/UserObservationDialog.cs` → `Forms/DiagnosticsDialog.cs` (class + file); window title
  `"Observation (id=…)"` → `"Diagnostics (id=…)"`; removed the helper-notes `Label` (notes box now
  fills the dialog).
- `MainForm`: `GetObservationContext` → `GetDiagnosticsContext`; `HandleCaptureObservationClick` →
  `HandleCaptureDiagnosticsClick`; menu item **"Capture Observation Snapshot"** → **"Capture
  Diagnostics Snapshot"**; Feedback tooltip reworded (also fixed stale "tick what you observed" — the
  checklist was dropped 2026-05-19).
- **verify-ui skill updated in lockstep** (`verify-ui.ps1` matched two now-changed literals: the menu
  path and the `'Observation (id=*'` title filter) so UI-automation keeps working; `SKILL.md` refs too.
- Current-state docs updated (`CLAUDE.md`, `ARCHITECTURE.md`, `test-project-plan.md`); dated history
  left intact. The 2026-05-27 "Capture Observation Snapshot menu item" entry below records the
  original name.

### 2026-05-28 — MoonEphemeris + AltitudeCurve reshape; TP cache axes rekey

Extracted the per-minute observational compute that TP rolled inline
into pure-function AL primitives, then rekeyed TP's two per-night cache
axes to consume them. One PR each side; AL shipped first
(`Library 4a0f309`), TP commits against the new surface (`TP c3ca26b`).
Aligns TP's cache shape with the planned IS (NINA scheduler plugin) cache so the
AL primitive surface is stable before IS design starts.

**AL primitives** (new + reshaped, pure functions; no AL-side caching
per the "no static mutable state in Core" contract):

- `Astronomy.Core.Moon.MoonEphemeris.Sample(location, startUtc, step,
  count)` returning `IReadOnlyList<MoonSample>`. Each `MoonSample`
  carries topocentric `AltDegGeometric` + `AltDegApparent` + `AzDeg`
  + `DistanceKm` + `AgeDays` + `PhaseAngleDeg` + `IlluminatedFrac` —
  all positions parallax-aware via `MoonPosition.Topocentric`;
  apparent altitude via `Refraction.SaemundssonDeg`. Reuses existing
  `MoonPosition`, `LunarAge`, `Saemundsson` primitives internally.
- Reshaped `Astronomy.Core.Session.AltitudeCurve.Sample` from
  `IReadOnlyList<double>` to `IReadOnlyList<AltAzSample>` (each sample
  carries `AltDegGeometric` + `AltDegApparent` + `AzDeg`). Internal
  linear LST advance optimization keeps the per-sample cost flat
  versus per-sample LST recompute.

**TP cache shape** (`Caches/`):

- New `NightDate` value type: `readonly record struct
  NightDate(DateOnly DuskDate)`. The local calendar date on which the
  night's astronomical dusk occurs, evaluated under the site's
  `TimeZoneInfo`. Static factory `NightDate.Of(NightWindow, TimeZoneInfo)`
  returns `default(NightDate)` for invalid polar-night windows;
  `EnsureAsync` treats `default` as the skip sentinel for trajectory +
  moon prep (year + fits still build).
- `mMoonAxis` rekeyed `DayWindowKey → NightDate`. Entry type renamed
  `MoonAltitudeEntry → MoonEphemerisEntry`; builder delegates to
  `MoonEphemeris.Sample`.
- `mDayAxis` rekeyed `(Target, DayWindowKey) → (Target, NightDate)`.
  Entry type renamed `TargetDayAltitudeEntry → TargetTrajectoryEntry`;
  builder delegates to reshaped `AltitudeCurve.Sample`.
- `BuildDayWindowForDate(Location, NightDate)` is the cache-internal
  helper that resolves the night's chart-window UTC bounds from the
  date (local-noon UTC anchor → `NightCalculator.ComputeNight` →
  `ChartLayout.BuildDayWindow`). The resulting `DayWindowKey` rides
  inside each entry's `Window` field, so consumers needing render-time
  X bounds still have them.
- `DayWindowKey` survives only as the render-time X-axis bounds carried
  inside the cache entries; `ChartLayout.BuildDayWindow` remains the
  single producer.
- TP-side `MoonSample` (the year-day moon-sweep sample carried by
  `NightCacheEntry`) renamed to `MoonSweepSample` to avoid collision
  with the new `Astronomy.Core.Moon.MoonSample`.

**TP consumers** (`Charts/`, `State/`):

- `ChartCoordinator.RunPipelineAsync` derives `nightDate` via
  `NightDate.Of(NightCalculator.ComputeNight(ctx.Location,
  ctx.Observation.Utc), ctx.Observation.Zone)` and passes it to
  `EnsureAsync(ctx, nightDate, progress)`.
- `AltitudeSubChart_Day` + `AltitudeSubChart_Sky` derive `nightDate`
  the same way and pull samples off `GetTrajectoryOrNull` /
  `GetMoonOrNull`. The Day chart reads `Samples[i].AltDegGeometric`;
  Sky's K-S walk gates on the same fit decision as before but the moon
  overlay now reads from the shared cache entry.
- `MoonOverlay.FetchOrCompute` consumes `MoonSample.AltDegGeometric`
  off the cached entry. The defensive inline fallback (cache-miss race)
  calls `MoonEphemeris.Sample` directly so the inline path is
  byte-identical to the cached one (no more parallel `AstroUtil.GetMoonAltitude`
  walk).

**Verification**:

- AL: 468 tests pass (`Astronomy.Core.Tests`), 26 XISF, 67 NINA.
- TP: 187 tests pass (`TargetPlanner.Tests`) including `ChartCacheStoreTests`
  rewritten against the new key + entry shapes.
- UI smoke: Penns Park boot loaded clean, `Button_VisibleTonight`
  rendered 5 targets, `nightDate=2026-05-28` logged through the Coord
  pipeline, all cache lookups hit (`dayEntryNull=0 fitEntryNull=0`),
  no `MoonOverlay.FetchOrCompute` cache-miss WARNs fired.

**Out-of-scope follow-ups** (unlocked, not landed):

- BLUE-check could now read from cached `AltAzSample` instead of
  recomputing — the new shape unblocks the optimization but doesn't
  force it.
- IS scaffolding can begin against the stable AL surface.
- Sky chart moon-toggle UX: K-S brightness curves don't change when
  the moon-avoidance toggle flips (correct physics — moon scattered
  light is always present; toggle gates fit decisions, not the
  brightness compute). A future design could split per-target curves
  into "moon-clear" vs "moon-affected" segments so the toggle has a
  visual effect.

### 2026-05-27 — verify-ui skill + Capture Observation Snapshot menu item

New project skill at `.claude/skills/verify-ui/` that drives TP's UI
handlers programmatically -- launches the app, dispatches clicks / menu
invocations / spinner sets / date picks via `System.Windows.Automation`
+ Win32 `SendMessage` + `PostMessage`, captures before/after snapshots
through TP's existing `UserObservationDialog` flow, parses tp.log for
`USER_OBS_END` entries with their ctx + screenshot paths. One-command
verification of any handler in `Forms/` or `Forms/Presenters/*`. Twelve
helper functions cover buttons, menus, combos, checkboxes, radios,
spinners, textboxes, labels, date pickers, listboxes, and the snapshot
dialog (`Start-TPApp`, `Send-TPSnapshot`, `Invoke-TPControl`,
`Invoke-TPMenuItem`, `Set-TPDatePicker`, `Set-TPCheckboxState`, ...).
Worked example in `SKILL.md` covers the Button_Now resort verification
(the latent bug fixed earlier today as part of the MainForm
decomposition).

Two TP-side changes paired with the skill:

- **Help > Feedback > Capture Observation Snapshot** menu item
  surfacing the existing `UserObservationDialog` discoverably (commit
  `5fe2342`). Same dialog Ctrl+N opens; the menu item exists for users
  without the shortcut and for reliable UIA invocation from the skill.
- `.gitignore` flipped from blanket `.claude/` to
  `.claude/settings.local.json` so project skills / agents / commands /
  settings ride along with the repo; only per-developer permission
  state stays local (commit `9e27f2e`).

Four UIA gotchas the skill discovered the hard way and now documents
inline + in `SKILL.md` caveats:

1. **Menu invocation > keystroke injection.** `SendInput`-style Ctrl+N
   from non-foreground PowerShell is governed by Windows'
   foreground-lock rules and lands on whichever window happens to hold
   focus -- non-deterministic. Driving menu items via UIA's
   `InvokePattern` takes a deterministic UIA-tree walk with no race
   conditions.
2. **PostMessage WM_KEYDOWN > Win32 DTM_SETSYSTEMTIME > UIA
   ValuePattern** for `DateTimePicker`. UIA `ValuePattern.SetValue`
   silently no-ops (long-standing WinForms accessibility-provider bug);
   `DTM_SETSYSTEMTIME` returned 0 against the picker's
   `NativeWindowHandle` in TP's WinForms version. `PostMessage`
   `WM_KEYDOWN VK_UP/VK_DOWN` looped per day-delta fires
   `DatePicker_KeyDown` reliably -- bypasses foreground locks.
3. **Win32 LB_GETTEXT > UIA ListItem enumeration** for
   `CheckedListBox`. The WinForms accessibility provider doesn't
   reliably surface `ListItem` children in the UIA tree (list-
   virtualization bug, worse with the `DupeAwareCheckedListBox`
   subclass). `SendMessage(LB_GETCOUNT/LB_GETTEXT)` against the
   listbox's HWND reads the item store directly.
4. **TreeScope::Descendants > TreeScope::Children** for owned dialogs.
   `UserObservationDialog.Show(owner=MainForm)` makes the dialog a
   top-level owned window -- UIA represents it as a child of MainForm
   in the tree, NOT a direct child of the desktop. Polling with
   `Children` scope misses it.

And one verification-design lesson, also caveat'd: **pick the right
oracle.** The Button_Now resort verification's first attempt asserted
on listbox-rank diff -- which gave a false "didn't change" signal even
though the handler chain ran end-to-end, because Transit sort is
RA-driven and date-invariant for small day-deltas. The right oracle
was the date-sensitive astrometry labels: Lunar Illumination shifted
65% -> 89% across a 3-day date jump, proving the full chain
(`DatePicker_KeyDown` -> `Value` setter -> `ValueChanged` ->
`OnObservationMomentChanged` -> `RefreshAstrometryLabels`) fired
exactly as designed.

Skill commits: `5fe2342` (TP menu), `9e27f2e` (gitignore + initial
skill), `1c448b3` (full API + working DatePicker).

### 2026-05-27 — MainForm.cs partial-class decomposition (-710 lines, -42%)

Eight-step pass on `Forms/MainForm.cs` taking it from 1669 to **959 lines**.
Seven new presenter partials in `Forms/Presenters/`, plus the existing
`TargetLoadingPresenter` absorbed Add/Remove. Behaviour is preserved exactly
except for one latent-bug fix called out below; tests stay at 187/187, build
clean throughout.

**New presenter partials (in extraction order):**

- `MainForm.ChartCoordinatorPresenter.cs` (102 lines, commit `dc0f348`) —
  coordinator construction + `DayChartModeChanged` wiring + baseline-paint
  kick. Constructor reduces to four sequenced calls; the post-apply hook
  (the load-bearing piece — central seam for Render-adjacent side effects)
  is now in an obvious file. Also dropped 3 unused usings from MainForm.cs
  that the LSP flagged after the edit.
- `MainForm.ObservationMomentPresenter.cs` (107 lines, commit `9f3bb17`) —
  `DatePicker_ValueChanged` + `TimePicker_ValueChanged` + `DatePicker_KeyDown`
  + `Button_Now_Click` + new `OnObservationMomentChanged(bool resortIfTimeKeyed)`
  helper folding the common tail (label refresh + now-line update +
  conditional resort + `mCoordinator?.Apply(SnapshotCurrent())`).
- `MainForm.AppMenuPresenter.cs` (192 lines, commit `eceaa0e`) — Help
  (Updates / About / Feedback) + File > Defaults (Edit / Clear) + the
  `TryDeleteFile` / `TryDeleteDirectory` static helpers used only by Clear.
  Self-contained subsystem; no chart state, no VM coupling.
- `MainForm.ProgressBarPresenter.cs` (161 lines, commit `34c03f0`) —
  `mChartBuildGeneration` / `mBarOwnerGen` fields + `ProgressBarHoldMs` const
  + `CreateChartProgress` / `BeginScanProgress` / `FinishScanProgress`. The
  shared generation counter that lets chart pipelines and scan paths
  mutually invalidate each other's stale callbacks is now visibly
  co-located with both producers.
- `MainForm.TransientNoticePresenter.cs` (79 lines, commit `8a49694`) —
  pooled transient-notice widget (`mTransientNotice` / `mTransientLabel` /
  `mTransientTimer` fields + `ShowTransientMessage`). Fully self-contained
  UI widget.
- `MainForm.AstrometryLabelsPresenter.cs` (71 lines, commit `88d7000`) —
  `RefreshAstrometryLabels` + `FormatZoned` formatter overloads. Pure
  render-to-label logic for the dawn/dusk/sun/moon side panel.
- `MainForm.SelectionCommandsPresenter.cs` (144 lines, commit `8cd0c54`) —
  `ComboBox_SelectTarget_SelectedIndexChanged` + `Button_UncheckAll` /
  `SelectAllTargets` / `ClearAllTargets` + `Button_VisibleTonight_Click`.
  **Evaluated against `SelectionVmPresenter` as a possible home** and
  declined: its header explicitly carves out "Other UI -> VM paths
  (Button_*Click handlers, ...) live with their respective concerns." A
  sibling presenter preserves that contract.

**Existing presenter extended:**

- `MainForm.TargetLoadingPresenter.cs` grew by 52 lines (commit `b16377b`)
  to absorb `Button_AddTarget_Click` + `Button_RemoveTarget_Click`. They
  fit the "every path that brings targets into the form" framing already
  on the file: Add literally brings a transient target in, Remove is its
  symmetric unwind, and both already share `LocalTargetStore.Save`
  plumbing with the file-scan paths.

**Latent-bug fix surfaced during the refactor** (commit `c1b888c`,
between extractions 2 and 3): `Button_Now_Click` was silently NOT
calling `ResortSelectedTargets` while `DatePicker_ValueChanged` /
`TimePicker_ValueChanged` did. Result: snap-to-now on a Transit /
Rise-sorted listbox left the display reflecting pre-snap ordering on a
meaningful time change. Caught by the
`OnObservationMomentChanged(bool resortIfTimeKeyed)` helper extraction
making the divergence visible. Fix: `Button_Now` now passes
`resortIfTimeKeyed: true` like the picker handlers. UI behaviour change
not covered by unit tests; user-verified in the app.

**Trailing cleanup** (commit `ff8fb9e`): dropped 5 unused usings from
MainForm.cs that the six extractions made dead
(`Astronomy.Core.Astrometry` / `.Night` / `.Sun`,
`TargetPlanner.Forms`, `System.Threading.Tasks`). LSP flagged
`TargetPlanner.Filters` as unused twice during the pass — both were
false alarms; `FilterLibrary mFilterLibrary` still consumes it at the
field declaration. The build is the source of truth; the LSP's stale
intermediate state can mislead. Always verify with `dotnet build`
before dropping a using the LSP flags.

**MainForm.cs partial graph after this session:** 15 partial files
(`MainForm.cs` itself plus 14 presenters under `Forms/Presenters/`).
Roughly grouped by concern:

| Presenter | Lines | Concern |
|---|---|---|
| `LocationPresenter` | 630 | Combo edit + settle debounce + equivalence + ResetForLocationChange |
| `FilterMenuPresenter` | 587 | Filter menu construction + persistence + Lorentzian/moon-avoidance |
| `TargetLoadingPresenter` | 416 | Load/Browse/drag-drop + Add/Remove + warmup |
| `SelectionVmPresenter` | 379 | Bidirectional VM <-> UI sync + listbox paint |
| `ChartBuildPresenter` | 362 | RunGraphBuildAsync + RenderArea + view-radio handlers |
| `SortPresenter` | 310 | Sort modes + Resort |
| `AppMenuPresenter` | 192 | Help / File menu handlers |
| `ProgressBarPresenter` | 161 | ProgressBar lifecycle |
| `SelectionCommandsPresenter` | 144 | Button commands that mutate selection |
| `CoordinatePresenter` | 140 | Lat/Lon/RA/Dec CoordinateInput callbacks |
| `ObservationMomentPresenter` | 107 | Date/Time/Now scrubs |
| `ChartCoordinatorPresenter` | 102 | Coordinator construction + boot wiring |
| `TransientNoticePresenter` | 79 | Auto-dismissing notice popup |
| `AstrometryLabelsPresenter` | 71 | Side-panel astrometry labels |
| **MainForm.cs** | **959** | Fields, constructor, dynamic-init, lifecycle, listbox helpers, SnapshotCurrent, GetObservationContext |

Constructor flow in MainForm.cs is now substantially more navigable;
the file's remaining content is genuinely form-shaped
(`InitializeDynamicControls`, `MainForm_Load`, `MainForm_FormClosing`,
listbox interaction helpers, the load-bearing `SnapshotCurrent`
overloads). Further slimming candidates are smaller and less cohesive
than the eight just shipped — natural stopping point.

### 2026-05-27 — TP-side test project (Phase 4 — scanner/loader fixtures)

Last unshipped phase of the 4-phase rollout. 35 new tests across 3
loader/scanner classes + 1 fixture helper. Total now 187 tests in ~16 sec.

**Synthetic fixtures**: all 4 phases now generate test fixtures per-test
via `TempDirectory` rather than committing binary blobs. JSON .NINA
target files are hand-built strings; XISF files come from a new
`Tests/Support/SyntheticXisf.cs` helper adapted from Library's
`XisfHeaderReaderTests.WriteSyntheticXisf` (minimal valid XISF: 8-byte
signature + 4-byte LE length-prefix + UTF-8 XML payload; no image
attachment block since header-only parsing). Cross-repo duplication;
sync if either drifts.

**`TargetLoaderTests`** (11) — NINA `.json` parser:
- Null/whitespace/missing path → null
- Positive Dec (M31 canonical): sexagesimal RA + Dec assembled
- `NegativeDec: true` flag flips magnitude to north=false via Target ctor
- Sexagesimal RA assembly (12h 30m 30s → 12.5083 hours)
- " Stars" suffix stripped via TargetIdentity.NormalizeName
- Missing `Target` node / `TargetName` / `InputCoordinates` → null
- Malformed JSON → null (no throw, log warn)
- `Directory` field carries source path

**`ImageLibraryLoaderTests`** (9) — `.xisf` light-frame parser via
Library's `XisfHeaderReader`:
- Null/whitespace/missing path → null
- Light frame (IMAGETYP=LIGHT + OBJECT + RA + DEC) → Target; RA in
  degrees converted to hours (M51 at 202.469625° → 13.498h)
- Non-light frame (IMAGETYP=DARK) → null
- Missing IMAGETYP → null (defensive: no-IMAGETYP files are outside
  the user's pipeline; safer to skip)
- Missing RA → null
- " Stars" suffix in OBJECT keyword stripped via NormalizeName
- Empty OBJECT falls back to filename stem
- RA wraparound at 360° → stays in [0, 24)

**`TargetScannerTests`** (14) — recursive walk + grouping:
- Null/whitespace/None-kind → empty
- Non-existent path / empty dir → empty (logs warn, no throw)
- Standalone `.json` → 1 target
- Mosaic Panel grouping: `Sh2-126 Panel 1.json` + `Panel 2.json` +
  `Panel 3.json` → 1 target at centroid, named after the folder
- Comet-prefixed `.json` excluded
- Recursive nested-dir walk finds deep `.json` files
- `.xisf` in `Captures/` collapses by target folder via spherical
  centroid (two frames at slightly-offset coords → one target between)
- `.xisf` outside `Captures/` (processing outputs, loose files) ignored
- `Comet ...` target folder excluded before headers are read
- Single-file scan (Browse picking one file) returns just that file
- Single-file `.xisf` scan works without `Captures/` ancestor
- `TargetFileKinds.Json` ignores `.xisf` siblings (and vice versa)
- Cancelled `CancellationToken` → throws (OCE or TCE; tests
  `Assert.ThrowsAnyAsync` to accept either)

Total Phase 4: 35 tests + `SyntheticXisf.cs` helper (40 LOC).

**Closes the 4-phase test-project rollout.** 187 tests total in
~16 sec. The original deficiency ("no automated test surface for TP")
is closed at the infrastructure layer (Phase 1), persistence layer
(Phase 2), cache contract (Phase 3), and load/scan layer (Phase 4).
ChartCoordinator + UI presenters + LC2 sub-charts stay deferred
indefinitely per the rollout plan (require WinForms message pump /
SKControl paint / Timer DI seam).

### 2026-05-27 — TP-side test project (Phase 3 — cache contract enforcement)

Phase 3 of the rollout: 31 new tests across 2 cache classes + 1 helper +
1 `InternalsVisibleTo` toggle. Total now 152 tests in ~17 sec (Phase 3 is
where the wall-clock starts to matter — each `ChartCacheStoreTests` run
exercises real Meeus + BestSession compute against M31 at PennsPark, so
~50–200 ms per test).

**The cache-contract.md doc IS the test list.** Every invariant in
"Lifecycle invariants" + "EnsureAsync semantics" is a [Fact] by the same
name on the contract side.

**`CacheAxisTests`** (12 tests, via `InternalsVisibleTo` since
`CacheAxis<TKey, TVal>` is internal):

- Per-key dedupe: concurrent `GetOrBuildAsync("k")` returns the same Task
  reference; build delegate runs exactly once.
- Fast-path after publish: three sequential reads → one build.
- Stale-publish discard: build started against `loc1`, swap to `loc2` via
  mutable Location accessor, complete the build → `GetOrNull` returns
  null (TryPublish's `ReferenceEquals(currentLocation, buildLocation)`
  check fails).
- Faulted-build cleanup: async lambda throws on call 1; second call
  starts a fresh build. Subtle: production build delegates are async, so
  the fault wraps into the returned Task — `DropOnFault` runs AFTER
  mInFlight is populated. A synchronous-throw lambda runs `RunBuildAsync`'s
  catch *before* mInFlight gets the key, leaving a stale faulted task;
  test had to use `async (k, l) => { … throw; }` to match production
  shape. Documented inline in the test.
- `DrainAndReset` clears mStore + mInFlight; returns in-flight task list
  for caller drain. **Pinned the location-swap requirement** explicitly:
  a same-location DrainAndReset still lets orphan publishes land in the
  new mStore dict; the stale-discard contract only holds when the
  owning store swaps mLocation FIRST under the same lock (as
  `ChartCacheStore.SetLocationAsync` does).
- `PrepareAsync` fans out + ticks `IProgress<int>` per completion;
  surfaces faults via `WhenAll`; null/empty key collections no-op.
- `GetOrNull` returns null pre-build.

**`ChartCacheStoreTests`** (19 tests):

- **Construction** (2) — null location throws; ctor stores location;
  `LocationNightCache` starts null (lazily built on first per-target
  prep).
- **Per-axis Prepare/Get round-trips** (5) — `PrepareManyAsync` publishes
  yearDays AND builds NightCache; `PrepareFitsAsync` publishes fits;
  `PrepareDayAsync` publishes 720-minute altitude arrays;
  `PrepareMoonAsync` publishes singleton per DayWindowKey; different
  DayWindowKeys yield independent moon entries.
- **Lifecycle invariants** (4) — `GetOrNull(null)` returns null;
  `SetLocationAsync(newLocation, …)` drops every axis atomically;
  `SetLocationAsync` ref-equal-and-same-utc is no-op (preserves cache);
  monotonic growth across multiple targets.
- **Idempotence** (1) — `PrepareManyAsync` called twice returns the same
  `TargetCacheEntry` instance (fast-path to published entry).
- **EnsureAsync diff matrix** (7) — null ctx throws; first cold call
  reports non-zero `EnsureWork`; warm call reports zero; location change
  drops all axes; HdmKey change preserves yearDays + day, rebuilds fits;
  DayWindowKey change preserves year + fits, rebuilds day + moon;
  brightness inputs change flips `BrightnessInputsChanged` flag without
  any axis flip (Bortle/ExtinctionK ride this path, not HdmKey);
  empty-targets ctx still preps moon (boot baseline); polar-day sentinel
  (`default(DayWindowKey).Count == 0`) skips Day + Moon prep but still
  builds yearDays + fits.

**Helper**:

- `Tests/Support/TestLocations.cs` — adapted from
  `Library/Astronomy.Core.Tests/Tests/TestLocations.cs`. Key change vs
  the Library version: **cached as `static readonly` fields, not
  expression-bodied properties**. The Library's `=> new Location(...)`
  pattern returns a fresh instance per access — which breaks the cache's
  internal `ReferenceEquals` checks (and downstream `Target.Default` /
  `Assert.Same` assertions across multiple invocations within one test).
  TP-side tests rely on reference identity for dict-key lookups + the
  stale-discard contract. Static fields evaluate once at class init.
  Same reasoning applied to `ChartCacheStoreTests`' shared `M31` static —
  documented inline.

**`TargetPlanner.csproj` change**:

- Added `<InternalsVisibleTo Include="TargetPlanner.Tests" />` so the
  generic `CacheAxis<TKey, TVal>` (internal sealed) is reachable from
  the test assembly without lifting it to the public surface.

`ChartCoordinator` is **deliberately not tested** in Phase 3 — its
`System.Windows.Forms.Timer` dependency requires a message-pump fixture
or an `ITimer` abstraction. Deferred per the rollout plan.

### 2026-05-27 — TP-side test project (Phase 2 — persistence)

Phase 2 of the test-project rollout: 32 new tests across 3 persistence
classes + 1 helper, plus 3 small additive refactors that make %APPDATA%
mocking trivial. Total now 121 tests in ~280 ms.

**Refactors** (additive `Load(string path)` / `Save(string path, ...)`
overloads; existing parameterless methods delegate via `FilePath` —
zero behaviour change for production callers):

- `Settings\SettingsStore.cs` — `Load(string path)` + `Save(string path, AppSettings)`
- `Settings\LocalTargetStore.cs` — `Load(string path)` + `Save(string path, IEnumerable<Target>)`
- `Filters\FilterLibrary.cs` — `LoadOrDefault(string path)` (`Save(string path)` already existed at line 153)

Each parameterless method becomes `=> Method(FilePath, …)` one-liner.
Both Save overloads create the parent directory if missing, so a fresh
`%APPDATA%\TargetPlanner` install bootstraps cleanly.

**New tests** (use the new `TempDirectory` helper for per-test
GUID-scoped temp dirs, recursive Dispose-cleanup):

- **SettingsStoreTests** (12) — missing-file → seed + save + file
  exists; present-and-current round-trips user state; Pattern C fill on
  null Roots; Pattern C fill on null OR empty NamedLocations; "Custom"
  site stripped (incl. case-insens); corrupt JSON → fallback seed +
  overwrite; version mismatch → fallback seed; null-path throws;
  Save creates nested parent dirs.
- **LocalTargetStorePersistenceTests** (11) — round-trip 0/1/N targets;
  signed-hemisphere preservation (Dec magnitude positive, North flag
  false for southern); null enumerable → empty JSON array; null target
  in list skipped; whitespace-name DTO skipped on Load; corrupt JSON →
  empty list; missing file → empty list (no file created); null path
  throws.
- **FilterLibraryPersistenceTests** (9) — Save→LoadOrDefault round-trip;
  missing file → DefaultLibrary (no file created); corrupt JSON →
  DefaultLibrary; empty array → DefaultLibrary; `MigrateLegacyFields`
  fills CenterNm=0 from builtin (legacy filters.json predating CenterNm);
  user-renamed filter keeps CenterNm=0; non-zero user value NOT
  overwritten by migration; null-path throws; Save creates nested dirs.

**Helper**:

- `Tests\Support\TempDirectory.cs` — `IDisposable` wrapping
  `Path.GetTempPath() + "TPTests_" + Guid.NewGuid()`. `using` block in
  every test; recursive Dispose-cleanup is best-effort (swallowed
  cleanup failures so antivirus / locked-handle issues during teardown
  don't fail the test).

### 2026-05-27 — TP-side test project (Phase 1)

Closes the "no automated test surface" deficiency at the infrastructure
layer. New `TargetPlanner.Tests/` xUnit project (matches Library house
style: `net10.0-windows10.0.19041` x64, `OutputType=Exe`,
`Nullable=disable`, raw `Assert.*`, no FluentAssertions/Moq) lands 89
tests across 10 Tier-A pure-logic classes:

- **HdmKeyTests** (12) — per-field equality matrix; `ReferenceEquals`
  semantics for `LocalHorizon` (the cache-thrash mitigation that nulls
  scalar profiles); GetHashCode stability.
- **DayWindowKeyTests** (6) — tick+count equality; `ChartStartUtc`
  preserves `DateTimeKind.Utc`; default-struct semantics matching
  explicit zeros.
- **ChartContextTests** (8) — `Hdm` derived property (ScalarHorizonProfile
  nulled, polyline passed through, all 5 fields source from Policy);
  record `with`; structural equality.
- **PlanningPolicyTests** (6) — `WithScalarHorizon`; `MoonProfile == null`
  when master toggle off or filter null; happy-path projects to
  `ActiveFilter.ToProfile()`.
- **PlanningPreferencesTests** (5) — `Default` (30°/240min); FromDto null
  → Default; ToDto/FromDto round-trip.
- **ChartEvaluationTests** (3) — required `BrightnessInputsChanged`;
  record equality; `with`.
- **TargetIdentityTests** (19) — `NormalizeName` (case-insens " Stars"
  strip); `AreSameTarget` ~1 arcmin tolerance including 0h/24h RA wrap
  and cos(dec) scaling at high latitudes; `SelectNewTargets` dedup +
  bucket-by-name + existing-set screen + null skip + input-order
  preservation.
- **SkyCentroidTests** (8) — empty/null → ArgumentException; single-point
  identity; 0h/24h seam (23.9h+0.1h → ~0h, not ~12h); RA wrap to [0,24);
  pole behavior; symmetric-meridian centroid lands ABOVE input Dec by
  ~0.21° (chord midpoint pulls toward the rotation axis when reprojected
  to unit length -- caught a wrong intuition during test authoring).
- **FilterTests** (6) — `ToProfile` drops Name/CenterNm/BandwidthNm;
  record `with`; field-by-field structural equality (incl. Name and
  BandwidthNm so HdmKey invalidation fires).
- **FilterLibraryTests** (16, in-memory only) — Find / Add / RemoveAt /
  Replace / ReplaceAll / ReplaceAll(null) → clear; `BuiltinDefaults`
  H/O/S/L/R/G/B pinned; `FindBuiltinDefault` case-insens;
  `DiffersFromBuiltinDefault` field-by-field across the 8-value
  footprint; `DefaultLibrary` ≡ BuiltinDefaults.

**TFM gotcha** caught during csproj authoring: the plan's bare
`net10.0-windows` (which sibling `Astronomy.XISF.Tests` uses via
`Directory.Build.props`) wouldn't work for TP because TP itself pins
`net10.0-windows10.0.19041` (SkiaSharp / LiveCharts2 floor). TFM
compatibility requires consumer ≥ producer on the platform-version axis,
so the test project matches TP exactly.

**Invocation** is project-scoped, not solution-scoped, so a future
`Astronomy.PCL.Native` vcxproj reference doesn't break the test build:

```
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64
```

Phase 1 also includes: `TargetPlanner.sln` registers the test project
(Debug|x64 + Release|x64 config rows); CLAUDE.md's "Tests live in the
Library repo, not here" line flipped to point at TP-side coverage;
4-phase rollout plan committed at `docs/design/test-project-plan.md`
(Phases 2-4 enumerate the remaining ~78 tests: persistence + cache
contract enforcement + scanner/loader fixtures).

### 2026-05-26 — Chart baseline paint at boot (empty-targets Apply)

ROADMAP item #7 closed (commit `4f709a8`). MainForm's constructor fires
one `mCoordinator.Apply(SnapshotCurrent(Array.Empty<Target>()))` right
after coordinator construction; the pipeline runs with no targets,
`EnsureAsync` only preps the moon altitudes, and the sub-charts paint
their non-target scaffolding (axes, dusk/dawn gradient, moon overlay on
Day) instead of staying blank-gray. Target curves stay absent until the
user explicitly checks a target or clicks `Button_Graph` -- the rule
"rendered targets == user intent" holds across every code path
(scrubs, radio toggles, location edits, etc., all read
`LastAppliedTargets` which stays empty until user action).

`SelectionVmPresenter.OnVmKnownTargetsChanged` still auto-seeds the
combo's default value via `SetSelectedSingle(firstSorted)` once the
image-library load completes -- that's separate from chart rendering
and was always the original responsibility of that handler.

Earlier same-day iteration (commit `c055986`) auto-painted via
`Apply(SnapshotCurrent(new[] { firstSorted }))` -- the just-seeded
combo target got implicitly plotted regardless of checked state. The
asymmetry between Day's "fit-tonight-only" filter (which hid the
target on Day) and Year/Sessions (which showed it across the year)
made the special case user-visible. Removing the auto-paint of the
combo target eliminates the bifurcation.

### 2026-05-26 — Misc polish (rename, async-void wraps, popup removal)

- **ProgressBar control renamed** `ProgressBar_MultiTargetProcessing`
  → `ProgressBar_Processing` across code + docs (commit `c2a04fd`). The
  shorter name better reflects what the control surfaces for: scan loads,
  chart-pipeline scrubs, graph builds, and location-reset rebuilds --
  not just "multi-target" work.

- **5 unwrapped `async void` handlers** gained `try/catch + Log.Error`
  wraps (commit `f2e6660`), bringing the codebase to 10/10 wrapped
  `async void` handlers. Source: §2.A from the 2026-05-26 `/ultrareview`
  multi-agent code review (`docs/design/2026-05-26-code-review-async-ui.md`).
  Wrapped: `Button_CheckedTargets_Click`,
  `ComboBox_Location_SelectionIndexChanged`, `OnTargetListDragDrop`,
  `OnCheckUpdatesClick`, and the `Shown += async` lambda. Of these, only
  `ComboBox_Location_SelectionIndexChanged` carries genuine risk
  (`SettingsStore.Save` in the synchronous prefix can throw `IOException` /
  `UnauthorizedAccessException`); the other four are defense-in-depth
  that earns its keep through consistency with the existing 5 wrapped
  `async void` handlers.

- **§2.B `Log.Warn` in `UpdateService.CheckOnStartupAsync`** silent catch
  (commit `f2e6660`). User-facing silence requirement preserved; diagnostic
  trail added so "the update prompt never appears" is one tp.log grep
  away from a root cause.

- **Filters Defaults transient popup removed** (commit `56c7749`). The
  Defaults button in `GroupBox_Moon_Filters` used to flash a 1-second
  "Filters reset to defaults" transient after the reset finished;
  removed as redundant noise -- the visible feedback the user actually
  needs is already there via `RefreshFilterMenuLabels()` (strips the
  `" *"` modified-markers as filters now equal their builtins) and
  `SetActiveFilter` (snaps the Lorentzian controls to the reset values).

The other two items from the same review (§3.B `Task.Delay` →
`async void` rewrite, §1.A `.ConfigureAwait(false)` sprinkle across cache
internals) were attempted then reverted -- the first introduced an
`await Task.Delay` SyncContext-capture bug that regressed the progress
bar; the second was pure speculative future-proofing with no current
benefit (the review doc itself classified it as "No runtime behaviour
change today"). The review doc survives at
`docs/design/2026-05-26-code-review-async-ui.md` as a historical
reference even though most of its bundled changes didn't ship.

### 2026-05-26 — Scrub-path progress bar: coordinator-owned lifecycle

ROADMAP item #8 closed (commits `9ff3573` + follow-up). `ProgressBar_Processing`
now surfaces during every chart-pipeline scrub — H/M/D, filter, moon-avoidance,
date/time/Now, location edits, and `ResetForLocationChange` — not just load
paths and graph-build. Warm-cache scrubs (no stale axes per the cache's diff)
keep the bar invisibly fast; cold scrubs show a per-target tick across cache
prep + sub-chart Render combined.

**Single architectural seam.** `ChartCoordinator` gained a
`defaultProgressFactory: Func<IProgress<(int Done, int Total)>>` parameter
wired at construction (`MainForm` passes `CreateChartProgress`). Every
`Apply(ctx)` / `ApplyImmediateAsync(ctx)` without an explicit progress
arg now builds a fresh `Progress<T>` with a closure that owns the bar
state and threads it through `mCache.EnsureAsync(ctx, dayKey, progress)`
→ wraps with `OffsetProgress` for `sc.Render(ctx, cache, progress)` so
per-target ticks across all four sub-charts continue Done smoothly
without a Maximum resize. Zero scrub-handler callsites were touched.

**Cache-side work estimation.**  `ChartCacheStore.EnsureAsync` computes
pessimistic `ensureWork = yearWork + fitWork + dayWork + moonWork` from
its existing diff flags (`yearWork` + `dayWork` + `moonWork` gated by
`locOrDate`; `fitWork` by `locOrDate || HdmChanged` since HdmKey is
independent of Location/Date); `renderWork = targets.Count` adds the
sub-chart pass. Total = ensureWork + renderWork sized once. If
`ensureWork == 0` the cache skips the initial Report entirely so the
sink stays hidden through the warm pipeline.

**Threading is `Progress<T>`-marshaled.** `CacheAxis.PrepareAsync` ticks
its per-target progress via `ContinueWith(TaskScheduler.Default)` —
ThreadPool. The factory's `new Progress<(int, int)>(...)` captures
`SynchronizationContext.Current` at construction (called from the
UI-thread coordinator), so Report callbacks marshal back to the UI
thread before touching the WinForms bar. Initial design used a custom
`IProgress<T>` class that mutated the bar directly from whatever thread
called Report — cross-thread accesses on cold scrubs got swallowed by
the coordinator's `OnDebounceTick` catch, leaving the bar stuck at its
first (UI-thread) tick.

**1 s hold-then-reset with ownership tracking; Aero animation lag.** On
`Done >= Total` the closure schedules a 1 s hold-then-reset via
`Task.Delay` on the UI `TaskScheduler`. Why 1 s rather than something
shorter: WinForms `ProgressBar` under default Aero visual styles uses
a smooth-fill animation that lags ~500 ms behind the `Value` setter
internally. The chart pipeline ticks `Value` from 0 to max in tens of
ms; each setter interrupts the previous animation. With a 200 ms hold
the reset fires while the visible bar is still animating up — the user
sees the bar peak at ~40 % then reverse as `Value=0` fires. With 1 s
the animation has ~500 ms to climb and the user gets another ~500 ms
of stable 100 % before the reset. An experimental
`SetWindowTheme(handle, " ", " ")` workaround that disabled Aero visual
styles entirely was tried and abandoned (commit `38c9973`) -- the
longer hold alone suffices without sacrificing the green gradient.

A new `mBarOwnerGen` field on `MainForm` tracks which pipeline
currently owns the bar's visible state (stamped on first claim); the
deferred reset bails if a newer pipeline has taken over, which kills
two visual quirks: a cold follow-on during the hold can reset the bar
to 0 % cleanly (its first Report re-stamps ownership), and a warm
follow-on still gets the reset (since the outgoing pipeline retained
ownership through to its delayed reset).

**Progress shapes collapsed 3 → 2.** Chart pipeline (coordinator-owned
`Progress<T>` closure) + load paths (`BeginScanProgress` + new
`FinishScanProgress`). The deleted `BeginChartBuildProgress` /
`FinishChartBuildProgress` helpers are gone; `RunGraphBuildAsync` no
longer has explicit progress orchestration — just
`Button_GraphTarget.Enabled` toggling around `ApplyImmediateAsync`.
`OffsetProgress` is private nested on `ChartCoordinator`;
`ActionProgress<T>` is private nested on `ChartCacheStore`. All single-
use, all SoC-aligned (the coordinator owns the funnel, the cache owns
work estimation, the closure in `CreateChartProgress` owns the bar).

Incidental fix: `ChartCoordinator.Apply(ctx, progress)`'s `progress`
parameter was previously captured in `RunPipelineAsync` but never
forwarded to `EnsureAsync` — the existing graph-build flow's claim of
per-target ticks driving the bar was not actually true at runtime. Now
it is.

Build-clean (0/0); the bar's UX behavior across rapid scrubs + warm /
cold / Sky-render edge cases was verified by the user before the commit
landed.

### 2026-05-22 — LocationPresenter extraction

**`MainForm.LocationPresenter` extracted** (commit `ecf12fd`) — seventh
partial-class file split (after Sort / Coordinate / FilterMenu /
TargetLoading / SelectionVm / ChartBuild). Lifts 25 methods covering the
location-state + per-site preferences + local-horizon polyline plumbing
out of `MainForm.cs`:

- **Site-characteristic spinners (7):** LocalElevation / Bortle /
  Extinction / TimeZone / TargetDuration / TargetFloor handlers +
  `PersistPlanningPreferencesToActiveSite` (mirror `mPlanningPreferences`
  back onto the active `NamedSite.Preferences` so close-time save persists).
- **Edit funnel + debounce (4):** `OnLocationEdited` (single attach point
  for lat/lon/elev/N/W/Bortle/Extinction edits),
  `RestartSessionsRebuildDebounce` / `SessionsRebuildDebounce_Tick`
  (250 ms scrub debounce that branches on `LocationsCacheEquivalent`),
  `ResetForLocationChange` (clear checked set / blank chart / drop +
  rebuild cache against the new location).
- **Location-keying helper:** `LocationsCacheEquivalent` (geometry-only
  diff used by the debounce tick).
- **Location combo (2):** `ComboBox_Location_SelectionIndexChanged` +
  `ComboBox_Location_DropDown` (re-fire trick for re-picking the same
  item after a Custom auto-switch).
- **Startup pickers (2):** `PickStartupLocation` + `PickStartupPreferences`
  resolve initial state from `mAppSettings.LastSelectedLocationName`.
- **Local-horizon polyline plumbing (9):** `ApplySiteHorizon`,
  `LoadLocalHorizonForCurrentLocation`, `GetCurrentHorizonPath`,
  `UpdateHorizonPathLabel`, `ConfigureHorizonWatcher` +
  `HorizonWatcher_FileChanged` + `HorizonReloadDebounce_Tick` (the
  `FileSystemWatcher` hot-reload pipeline), `Button_BrowseHorizon_Click`
  (file picker that persists the `.hrz` path onto `NamedSite`),
  `InitializeLocalHorizonControls` (wires the lot at form init).

`ClampToRange` stays in `MainForm.cs` (used by CoordinatePresenter +
FilterMenuPresenter + this presenter); fields stay per the partial-class
pattern; `SnapshotCurrent` + `RefreshAstrometryLabels` also stay. Pure
move; behaviour identical.

### 2026-05-22 — ChartBuildPresenter + Velopack bump

- **MainForm.ChartBuildPresenter extracted** (commit `e6b5ed6`) — sixth
  partial-class file split (after Sort / Coordinate / FilterMenu /
  TargetLoading / SelectionVm). Lifts the chart-rendering plumbing out of
  `MainForm.cs`: `Button_Graph_Click` / `Button_CheckedTargets_Click` +
  `RunGraphBuildAsync`, `CheckedToggleDebounce_Tick`,
  `HarvestCheckedTargets`, `SelectedArea` (Day / Sky / Year / Sessions
  resolver), `RenderArea` (coordinator's post-await render delegate),
  the chart-area UI (`ShowOnlyAltitudeChart` /
  `OnSubChartIdealHeightChanged` / `ResizeAltitudeChartArea`), the four
  radio `CheckedChanged` handlers, and `PushSkyKSInputs`.
  `BeginChartBuildProgress` / `FinishChartBuildProgress` /
  `BeginScanProgress` stay in `MainForm.cs` (shared with the load paths
  via `mChartBuildGeneration`); `SnapshotCurrent` also stays (used from
  many places). Pure move; behaviour identical.
- **Velopack 0.0.1298 → 0.0.1589-ga2c5a97** (commit `a1db619`) — bumped
  to the latest snapshot per ROADMAP item 5. Velopack is pre-1.0 so the
  `-g<sha>` suffix marks newer snapshots since the unsuffixed 1298. TP's
  Velopack API surface (`VelopackApp.Build().Run()`, `UpdateManager`,
  `GithubSource`, `IsInstalled`, `CheckForUpdatesAsync`,
  `DownloadUpdatesAsync`, `ApplyUpdatesAndRestart`,
  `UpdateInfo.TargetFullRelease.Version`) compiles clean on the new
  version. The dry-run release-cycle smoke test (`vpk pack` +
  `vpk release` + install + self-update on .NET 10) is the user-run
  half; item 2 ("Velopack auto-update smoke-test") stays open until that's verified.

### 2026-05-22 — Load-path polish: Browse multi-select, load progress, SelectionVmPresenter

Three follow-ons to the day's target-loading rework:

- **Browse multi-select** (commit `036e7c8`) — `Button_BrowseTargetList`'s
  dialog became a multi-select `OpenFileDialog` (`PromptForFilesOrFolder`).
  One OK returns either a multi-selection of `.json`/`.xisf` files XOR a
  single folder (navigate into it and click Open) — never both at once.
  Browse and drag-drop now share `LoadFromPathsAsync`, literally the same
  code path; `GetBrowsedTargets` / `GetDroppedTargets` are 5-line wrappers.
- **Per-file load progress** (commit `30cb059`) — wires
  `ProgressBar_Processing` to the three Load/Browse buttons +
  drag-drop. Sister helper `BeginScanProgress` takes
  `IProgress<(int Done, int Total)>` (Total unknown up front, so the first
  report after enumeration sizes Maximum); shares `mChartBuildGeneration`
  so a chart click mid-scan invalidates the scan's callbacks (and vice
  versa). `TargetScanner.ScanAsync` pre-filters work units (xisf pairing +
  Captures + comet drop, json by extension), then ticks
  `Interlocked.Increment` per file processed.
- **`MainForm.SelectionVmPresenter` extracted** (commit `2cfe2fe`) — ROADMAP
  item 8. The `TargetSelection` <-> UI-sync cluster lifted out of
  `MainForm.cs` into a new partial-class file: `WireSelectionVm`, the four
  `OnVm*` handlers, the two `OnCheckedListBox*` handlers, the per-target
  color rebuilders (`RebuildTargetColors` / `RecomputeDupeSetColors`), the
  listbox tint callbacks (`GetDupeRowBackground` /
  `GetCheckboxInteriorTint`), and `OnSelectedTargetsMouseDown`. Same
  partial-class-file-split pattern as SortPresenter / CoordinatePresenter /
  TargetLoadingPresenter — fields stay in `MainForm.cs`, methods relocate.
  Pure move; behaviour identical. Also drops an orphaned 5-line
  `IProgress<string>` comment at the old WireSelectionVm site.

### 2026-05-22 — Recursive target scanner: folder-grouped, centroid-located

The three `GroupBox_Target` buttons (Load Image Library, Load NINA Targets,
Browse) and target-list drag-drop converge on one recursive scanner
(`Targets/TargetScanner.cs`) that resolves **one `Target` per real sky
target**. The walk is depth-first and error-tolerant — an unreadable folder is
logged and skipped, never fatal (the stock `EnumerateFiles(AllDirectories)`
aborts on the first `UnauthorizedAccessException`); `.xisf` headers parse in
parallel off-thread; no directory is skipped by name.

**A target is a group of files, collapsed to one `Target` whose coordinate is
the spherical centroid of the group** (`Targets/SkyCentroid.cs` — RA/Dec →
unit vector → vector mean → back, the only seam-safe way to average sky
coordinates). `.xisf` groups by target folder (above `Captures/`), centroid
over every `IMAGETYP=Light` frame — a mosaic folder is one target across all
panels. A `.json` mosaic is a folder of `… Panel <n>` files, centroid over the
panels' planned coordinates; a standalone `.json` is one target unchanged.

**Why centroid, not a per-file coordinate test.** The first cut (`6c03c4d`)
keyed identity on stars-stripped name + RA/Dec within ~1′. That fails for
`.xisf`: the `RA`/`DEC` keyword is the *per-frame plate-solved centre*, which
dithers 12–14′ across a session — every frame resolved as a distinct target
(Abell 21: 184 frames → 184 rows). `978afb9` moves identity to folder grouping
+ centroid, so per-frame scatter cannot split a target. Verified on the real
library: Abell 21's 184 frames → one target at 07h29m13s +13°18′.

Other facts: comets excluded (every comet folder is `Comet …`, skipped before
its headers are read); loads **add** rather than replace (`AddKnownTargets`;
`Button_ClearAllTargets` is the lone empty path; `local-targets.json` seeds at
startup; `OnVmKnownTargetsChanged` uses `preserveSelection: true`);
`TargetIdentity` (name + ~1′) survives only for the already-loaded /
cross-source skip and the listbox duplicate tint, now over stable centroids.
Retires `ImageLibraryScanner` / `LoadBrowsedPathAsync` /
`LooksLikeImageLibraryRoot` from TP's load path. All TP-side — no `Astronomy.*`
changes. Build-clean (0/0); the in-app target list needs human spot-checking.

### 2026-05-21 — Drag-and-drop targets onto the list

`CheckedListBox_SelectedTargets` now accepts Explorer file-drops (commit
`97c513f`). Drop any mix of NINA `.json` / `.xisf` files and target folders —
`DataFormats.FileDrop` hands over the paths, each is classified + loaded via the
existing `LoadBrowsedPathAsync` (`.json` → NINA target, `.xisf` → image-library
target, folder → library scan or NINA walk), and the combined result
wholesale-replaces the known-target set. One-off, like Browse — no persist, no
sidecar append. Sidesteps the Windows file-dialog limitation (no dialog picks
files *and* folders, let alone a mix). New `GetDroppedTargets` +
`OnTargetListDragEnter` / `OnTargetListDragDrop` in
`MainForm.TargetLoadingPresenter.cs` (its first addition since the extraction);
`AllowDrop` + the two drag events wired in `InitializeDynamicControls`.
Build-clean 0/0; the Browse button stays for discoverability.

### 2026-05-21 — Extract MainForm.TargetLoadingPresenter partial

ROADMAP "Currently open" item 8. `MainForm.cs` had grown large; the
target-source-UX work (below) was built extraction-ready for exactly this.

Commit `1862770` lifts the target-loading cluster — the three Load/Browse button
handlers, the image-library / NINA-.json / type-detecting-browse orchestration,
the never-throw pure-loader wrappers (`LoadImageLibraryAsync` /
`LoadNinaTargetsAsync`), the fallback folder pickers, and `StartCacheWarmup`
(13 methods) — plus the `NinaTargetsRootPath` / `ImageLibraryRootPath` properties
out of `MainForm.cs` into a new `Forms/Presenters/MainForm.TargetLoadingPresenter.cs`,
matching the `SortPresenter` / `CoordinatePresenter` partial-class-file-split
pattern. A pure move within one `partial class MainForm` — behavior- and
compile-identical, every caller resolves transparently; `MainForm.cs` is ~300
lines lighter. Build-clean 0/0.

The `TargetSelection` VM↔UI-sync handlers were deliberately left out (a distinct
concern) — see Currently-open item 8 for the follow-on `SelectionVmPresenter`.

### 2026-05-21 — Target-source UX: startup swap, fallback browse, type-detecting Browse

Follow-on to the image-library source (same day). Four commits restructure how
targets enter TP.

**C1 (`229810b`) — Clear / Uncheck buttons.** The existing `Button_ClearAllTargets`
only *unchecked* all targets; renamed to `Button_UncheckAll` / "Uncheck All". A
new `Button_ClearAllTargets` / "Clear All Targets" empties the known-target list
outright (`SetKnownTargets(empty)`).

**C2 (`9b8c582`) — Startup swap + NINA-load button + fallback browse.** Startup
auto-loads the image library instead of NINA `.json` (`GetImageLibraryTargets`
with `offerFallbackBrowse: false` — a missing/empty/failed root logs and boots
empty, no dialog). New `Button_LoadJsonTargets` ("Load NINA Sequencer Targets")
loads `NinaTargetsRoot` directly. Both Load buttons fall back to a
`FolderBrowserDialog` when their configured root yields nothing, and a successful
browse persists the chosen path (`SettingsStore.Save`). New helpers: `GetJsonTargets`,
never-throw `LoadImageLibraryAsync` / `LoadNinaTargetsAsync`, shared `PromptForFolder`.

**C3 (`5db875e`) — Type-detecting Browse.** `Button_BrowseTargetList` is now a
one-off loader: a folder-capable `OpenFileDialog` whose result is classified — a
`.json` file → one NINA target, a `.xisf` file → one image-library target
(`XisfHeaderReader`), a `Captures/`-bearing folder → image-library scan, a
`.json` folder → NINA walk. New single-file loaders `TargetLoader.LoadFile` +
`ImageLibraryLoader.LoadFileAsync`. Browse is one-off — no persist, no sidecar
append. The old `GetNinaTargets` (last caller was Browse) and the dead
`mProcessObjectGeneration` field were removed; `ProgressBar_ProcessObject` is now
an idle Designer control.

The work was built extraction-ready (thin handlers, cohesive load methods) for a
future `MainForm.TargetLoadingPresenter` partial-class extraction — see Currently
open. Build-clean (0/0) per commit; new Designer buttons sit at placeholder
positions for VS2026-designer repositioning.

### 2026-05-21 — Phase C re-scoped: image library as a target source

Phase C was originally a three-part "C1-C3" (migrate TP onto the rich
`Astronomy.NINA.Target`, add the image library as a source, surface per-target
filters on the Sky chart). A measured smoke scan plus a design pass re-scoped it
down to one minimal feature.

**Measurement.** A full scan of the user's real image library — 14,015 `.xisf`
frames across 70 targets — takes **1.4 s**, 0 parse failures
(`Astronomy.NINA.Xisf.ImageLibraryScanner`, gated smoke test). On-demand
scanning is fast enough that no database / cache is needed: the `.xisf` files
are the source of truth, and the scan regenerates everything each run. A
persistent DB would only ever be a home for *goals* (authored intent, not on
disk) — scheduler-manager territory (today: ISM's authored-intent-store charter).

**Shipped (commit `fbfd3d9`).** A "Load Image Library" button gives TP a second
target source alongside NINA `.json`. `ImageLibrary/ImageLibraryLoader.cs` calls
`ImageLibraryScanner.ScanAsync` and down-converts each `TargetReport` to a bare
`Astronomy.Core.Targets.Target` (name + RA + Dec) — library targets are handled
identically to `.json` targets. `AppSettings.ImageLibraryRoot` (seeded in
`PersonalDefaults`, Pattern-C self-heal in `SettingsStore`) resolves via
`MainForm.ImageLibraryRootPath`; `GetImageLibraryTargets` wholesale-replaces the
known-target set with no sidecar append (an image-library load is a clean full
replace). `StartCacheWarmup` was extracted from `GetNinaTargets` and is now
shared by both load paths. The Designer button was hand-placed in
`GroupBox_Target` — final position is a VS-designer reposition.

**Data model.** The image library and NINA `.json` are two independent target
*lenses* (backward fact vs. forward intent); each load wholesale-replaces the
set — they are never merged. Plan-vs-actual reconciliation is
scheduler-manager territory (since shipped in TSM).

**Deferred to the scheduler era** (no longer Phase C): the rich-type migration onto
`Astronomy.NINA.Target` and Sky-chart filter surfacing. The Library foundation
(`Astronomy.NINA` rich types + scanner) shipped in Phases A/B, so deferring TP's
consumption of the richness costs nothing.

Build-clean (0/0); the new button, the load action, and the populated target
list need human visual verification.

### 2026-05-19 — Fix L: UTC-internal Day/Sky chart X-axis (DST-correct labels)

Follow-on to the named-TZ refactor. With DST-aware zones in place, the Day and
Sky single-night charts surfaced a latent axis bug (observation id=b4d1, Oct 31
2026 / PP): on a night crossing a DST transition the post-transition X-axis
labels read 1 h ahead of real local clock.

Root cause — two time frames mixed on the X-axis: per-minute data points
positioned in "chart-clock" (wall-clock arithmetic from a local `chartStart`),
while gradient sections / now-line / HD-overlay endpoints positioned in
"real-local-clock" (`ConvertTimeFromUtc`). On a non-DST night the frames
coincide so the bug was invisible. Separately, `ChartLayout.BuildDayWindow`
computed `count` (per-minute sample count) from the wall-clock span, but the
data is sampled at UTC instants — so a fall-back night was 60 samples short
(true UTC span is 1 h longer than the wall-clock face) and the dawn gradient
landed off-chart.

Fix (commit `b17a074`): the Day/Sky X-axis is now fully **UTC-internal** — every
plotted X is the OADate of a UTC instant; the axis Labeler (`AxisTimeLabel`) is
the single seam that converts to the site wall clock via
`TimeZoneInfo.ConvertTimeFromUtc`, DST rules evaluated per-instant. UTC is
monotonic so the altitude curve stays smooth (no doubled-back curve, no data
gap); a fall-back night naturally shows a duplicated "1:00 AM" tick and a
spring-forward night skips "2:00 AM" — the correct, intended signal of the
transition. `BuildDayWindow` gains an `EndUtc` tuple member and computes `count`
from the UTC span via the new `LocalChartHourToUtc` helper (non-DST nights:
identical `count` + `DayWindowKey`, no cache re-key). Day's HD-overlay window
tuple, Day's hover tooltip (custom `curveTooltipFormatter`), and Sky's
per-minute tooltip strings all moved into the UTC frame; Sky's K-S twilight gate
was already UTC-vs-UTC and untouched. Year / Sessions charts (day-grained X
axis) and the Library are untouched — the whole defect and fix live in TP
display code.

User-verified on fall-back / spring-forward / Mountain-zone nights.

### 2026-05-19 — Named-TZ refactor + settings.json collapse + persistence-fix sweep + UI logging detail

The named-TZ follow-up flagged on 2026-05-18 shipped, plus a settings-file collapse that closed a long-standing sync gap between `personal-defaults.json` and `settings.json`, plus a sweep of persistence bugs surfaced via the Ctrl+N feedback loop, plus a UI-logging detail expansion that captures every discrete user gesture for `USER_OBS` bracketing.

**Named-TZ refactor (Library `73e5777` + TP `70c563b`).** New cross-app DTO `Astronomy.NINA.Persistence.NamedSite` on the Library side -- flat POCO carrying `Name`, positive-magnitude Lat/Lon with paired N/W flags, Elevation, BortleClass, ExtinctionK, LocalHorizonPath, `TimeZoneId` (Windows TZ ID string, e.g. `"Eastern Standard Time"`), Preferences. Resolves to `Location.TimeZoneInfo` via `TimeZoneInfo.FindSystemTimeZoneById`, which returns a DST-aware `TimeZoneInfo` whose `ConvertTime*` methods evaluate adjustment rules per UTC instant. TP migrates off `NamedLocationSetting` (deleted) onto the shared shape via a new `Astronomy.NINA` ProjectReference; the `UtcOffsetHours` double is gone outright (no shim per the no-backward-compat stance — Newtonsoft silently drops the unknown field on old settings.json files). UI surface: `NumericUpDown_TimeZone` (-12..+12 spinner) replaced by `ComboBox_TimeZone` (DropDownList bound to `TimeZoneInfo.GetSystemTimeZones()`); Designer layout pass at MainForm.Designer `60cee9a` by the user. Bug class fixed: every chart-pipeline `ConvertTime*` call (post `0a6926f`-threaded `TimeZoneInfo`) was structurally incapable of DST because the zone was a `CreateCustomTimeZone(...)` result with no `AdjustmentRules`. Replacing the resolver flips them all DST-aware in one move — Year chart per-night labels now track ST↔DST transitions correctly; a NY-machine planner targeting a Denver site sees MDT wall-clock all season.

A short reversal cycle followed (Library `5b334c5` → `802f7cb`, TP `70be602` → `0ec08e4`) when a `NamedSite.Normalize()` load-time defensive normaliser was added then reverted — the user correctly pushed back that "fix the JSON" addresses the source while "normalise on load" only addresses the symptom. Both `personal-defaults.json` and `settings.json` were corrected directly for the Denver-longitude-sign bug instead (negative magnitude paired with `West: true` would have loaded as 105°E via Location's sign-normalisation; corrected to positive magnitude).

**Settings file collapse (TP `1c041c8`).** Dropped the gitignored `%LocalAppData%\TargetPlanner\personal-defaults.json` in favour of a single `%AppData%\TargetPlanner\settings.json`. The dual-file model caused sync confusion -- edits to personal-defaults.json didn't propagate to existing entries in settings.json under the old `MergeBuiltins` zero-fill rule. `PersonalDefaults.cs` becomes a single `BuildSeedSettings()` static factory; on first run (settings.json missing) the seed creates the file with 4 hardcoded sites (Penns Park, Hillsborough, Cherry Springs, Denver) + `NinaTargetsRoot` + `LastSelectedLocationName`. After first run settings.json is fully authoritative; `SettingsStore.Load` applies **Pattern C** (fill any null/empty top-level field from seed -- additive-schema migration self-heal) and strips any saved "Custom"-named site (sentinel-reservation for the `ComboBox_Location` free-edit row). `MergeBuiltins` / `BuildDefaultNamedLocations` / `Clone` all dropped. Dead `PersonalDefaults` static scalars (Latitude/Longitude/Elevation/BortleClass/ExtinctionK) removed in a follow-up cleanup (TP `25bd092`).

`File > Clear All Data...` menu replaced by `File > Defaults`:
- `Defaults > Edit settings.json` opens the file in the OS-default editor via `Process.Start` + `UseShellExecute=true`, sets a FormClosing-save suppression flag, and `Application.Exit()`s immediately so the user's hand-edits aren't clobbered by an exit-time save of in-memory state (TP `9a8ec20` after the lost-edits bug surfaced).
- `Defaults > Clear (factory reset)...` confirms via YesNo, wipes settings.json + filters.json + local-targets.json + Logs/ recursively, sets the suppress flag, and exits (TP `9f24e37` dropped the redundant follow-up dialog after the confirm).

**Boot-default behaviour change** (within TP `1c041c8`): TP previously always booted to `PersonalDefaults.LocationName` regardless of last in-session pick (per the deleted `project_boot_default_pennspark` memory). Now TP boots to `mAppSettings.LastSelectedLocationName` -- seeded to "Penns Park" on first run, then overwritten each combo pick. Behaviour-change-by-design — the dual-file source-of-truth that justified the override is gone. A `DefaultLocationName` field can restore the always-boot-home semantics later if wanted.

**Persistence-fix sweep.** Four discrete bug classes:
1. Saved sites named "Custom" (historical artefacts from earlier `Location.Default` fallback paths) stripped on load so `ComboBox_Location` doesn't show duplicate "Custom" entries alongside the sentinel (TP `03c3e8a`).
2. `ResetForLocationChange` clears `mVisibleTaggedTargets` so the previous site's Visible-Tonight checkbox-interior tints don't linger after a site swap -- the tag is per-site (computed against the active site's visibility window) so it goes stale on swap (TP `b36df9e`).
3. `NumericUpDown_TargetFloor_ValueChanged` / `NumericUpDown_TargetDuration_ValueChanged` now mirror the updated `mPlanningPreferences` back to the active `NamedSite.Preferences` via `PersistPlanningPreferencesToActiveSite`. Previously the spinner edited only the in-memory record which isn't part of mAppSettings; the close-time save persisted stale per-site Preferences. The 60° edit on Target Floor vanished on close (TP `d64b8d8`).
4. `MainForm_FormClosing` does `ActiveControl = null` before save so any NumericUpDown with typed-but-not-blurred text commits via `OnLeave` -- which is what actually converts Text → Value. `ValidateChildren()` only fires `Validating` events, which doesn't trigger the conversion (the `4e37886` attempt missed this distinction; superseded by `65cccb9`).

**Observation dialog simplification (within TP `1c041c8`).** Dropped the 12-item `CheckedListBox` from `UserObservationDialog` -- items weren't consumed programmatically anywhere; only the free-form notes carried any debug signal. Dialog shrinks to 420×220 (notes + OK + Cancel). `USER_OBS_END` line format drops `checked=[...]`; empty notes encode as `notes="(checkpoint)"` so `grep checkpoint tp.log` finds "all okay" moments cleanly.

**ChartLayout gradient tolerance (TP `7e9206e`).** Minimum dusk/dawn gradient width reduced from 30 min to 6 min and hoisted into a named `MinGradientMinutes` constant. Chart now sits closer to the actual night window; the extra-hour widening kicks in only on nights where dusk/dawn lands within 6 min of an integer hour.

**UI logging detail (TP `b3fcd54`).** Added `Log.Diag("UI", ...)` lines to ~25 discrete-event handlers that previously fired silently: ComboBox picks (Location / TimeZone / Bortle / SelectTarget / SortTargets), NumericUpDown ValueChanged (LocalElevation / Extinction / TargetFloor / TargetDuration), Lat/Lon CoordinateInput edits, target gestures (Add / Remove / Visible Tonight / Clear All / Select All / Browse / Checked-Targets / ListBox item check), menu picks (Defaults > Edit / Clear, Filters > each, Help > Check Updates / About / Open Notes Folder). The `USER_OBS_START`/`END` timestamp window now bookends a complete UI play-by-play. Always-on (no gate on dialog-open state) -- log-volume math showed the 100-300 lines/session footprint is trivial and pre-open history is genuinely useful for diagnosis. Release builds default to DIAG off (zero overhead). `TP_DIAG=UI` env var enables just this category if you want to filter chart-pipeline noise out.

Code-correct (TP builds clean post each commit; Library tests 67/67 green post the brief Normalize roundtrip). Visual verify in VS2026 still recommended for: TZ ComboBox layout fit in GroupBox_Location (Designer layout pass at `60cee9a` settles this), Year/Sessions chart axis labels across DST boundaries (Mar 14 / Nov 7 2027 with Denver as active site).

### 2026-05-18 — Observation-dialog catches (id=7abd): time-of-day correctness across the chart pipeline

First substantive use of the Ctrl+N observation dialog (shipped 2026-05-17) surfaced three time-of-day bugs that the dusk/dawn / moon-altitude paths had hidden. The dialog round-tripped a single user note — *"Time now is correct - red bar in graph time now is not"* — into the broken line in ~one diff. Fix sequence (commits `fb1cfa2`, Library `6431e43`, TP `f94d13e`, TP `0a6926f`):

1. **Now-line plotted UTC ticks on a local-time axis.** `UpdateNowLine` on all four sub-charts (Day / Sky / Sessions / Year) called `.ToOADate()` directly on `ctx.Observation.Utc`. `ToOADate` ignores `DateTime.Kind`, so the red bar landed `(local–UTC)` hours to the right of the actual now-moment (EDT observer at 7:36 pm saw the bar at 11:36 pm). Regression from Phase 2's `Location.DateTime → Observation.Utc` rename — caller Kind flipped, `UpdateNowLine` wasn't audited. Fixed by `.ToLocalTime()` conversion inside each impl + interface contract note.
2. **Moon Rise/Set labels showed prior-night events for non-UTC observers.** `AstroUtil.GetMoonRiseAndSet(utc, ...)` searches a single UTC calendar day. For an EDT observer that UTC day is `[yesterday-evening-local, today-evening-local]` — so the rise event found is today's morning rise (correct) but the set event is yesterday's evening set (off by ~63 min, the lunar daily regression). Tonight's actual moonset lives in *tomorrow's* UTC day. Fix is Library-side: new sibling `AstroUtil.GetMoonRiseAndSetForNight(duskUtc, dawnUtc, ...)` scans three UTC days and returns `(latest rise ≤ dawn, earliest set ≥ dusk)`, mirroring `NightCalculator.BracketingPair` for sun events. TP's `RefreshAstrometryLabels` switches to the new wrapper, passing `night.AstronomicalDusk` / `night.AstronomicalDawn`. Library tests cover the waxing-crescent case from the failure report, a direct A/B vs the legacy API proving the set is now ~1 day later, and `DateTime.MinValue` short-circuit (12 tests pass, +4 new).
3. **Chart axes + display labels honored machine zone instead of Location's UTC offset.** `ChartLayout.BuildDayWindow` did `night.AstronomicalDusk.ToLocalTime()` (machine zone) while the user's Location carried its own `TimeZoneInfo` (no-DST custom zone from `NumericUpDown_TimeZone`). User screenshot showed `UTC=-5` for Penns Park while machine was on EDT (UTC-4) — the spinner was decorative. Threaded `TimeZoneInfo` through `BuildDayWindow(NightWindow, TimeZoneInfo)`, `IAltitudeSubChart.UpdateNowLine(now, zone)`, the HD overlay window labels in `AltitudeSubChart_Day`, and the four `RefreshAstrometryLabels` time labels. Source on every callsite is `ctx.Observation.Zone` / `mObservation.Zone` — same `TimeZoneInfo` the spinner mutates, so a zone-spinner edit now rebases the chart's `DayWindowKey.ChartStartUtcTicks` and triggers cache invalidation.

Plus a small companion null-aware-formatting follow-up: `FormatZoned(DateTime?, TimeZoneInfo)` renders `--:--` instead of `12:00 AM` when the underlying UTC is `MinValue` or null — surfaces "no event" for polar-summer night windows or the new bracket API's circumpolar fallback.

Code-correct (`dotnet build` clean, 451 → 12-new Library tests green); visual verify still needed for: red-bar lands at the expected wall-clock instant after scrubbing time; chart axis labels read at Location zone (e.g. UTC=-5 picker time vs UTC=-4 machine clock should now diverge by 1 h on the X axis); moon labels match the chart's actual moon-set on edge cases (full moon up-all-night, near-new down-all-night).

### 2026-05-18 — Location refactor Phase 2: Library `Location` strip + TP-side decoupling

Two coordinated commits closing the Phase-1 transitional bridge: **Library `b3fc182`** strips `Horizon` / `Duration` / `DateTime` / `MinutesAboveHorizon` from `Astronomy.Core.Locations.Location` (removing the `[Obsolete]` markers and the fields underneath), adds `LocalHorizon: IHorizonProfile`, retains `TimeZoneInfo`. New types `Astronomy.Core.Time.ObservationMoment` (record struct `(Utc, Zone)`, factories `FromLocal` + `Now`) and `Astronomy.Core.Horizons.MaxOfHorizonProfile` (pointwise-max combinator). `NightCalculator.ComputeNight(Location, DateTime utc)` + companion `TwilightCalculator.ComputeNight` gain the explicit utc parameter. `AltAzCalculator.Of` deleted; `AltAzCalculator.At(target, location, utc)` is the sole entry. `NightCache` ctor takes startingUtc as a third positional. **TP `e794cfc`** introduces `TargetPlanner.State.PlanningPreferences` (sealed record `(TargetFloorDeg, MinDuration)`), replaces the `mLocalDateTime` tuple field with `mObservation: ObservationMoment`, threads `Observation` through `ChartContext`. `SnapshotCurrent` composes site polyline + user floor via `MaxOfHorizonProfile`. `ChartCacheStore.LocationCacheEquivalent` collapses to pure geometry; date axis lives on a new `mLastSetUtc` shadow detected via `ctx.Observation.Utc` diff. `SetLocationAsync` gains explicit utc; sub-charts (Day/Sky/Year/Sessions) read `ctx.Observation.Utc` instead of `ctx.Location.DateTime`. `NamedLocationSetting.FromState(Location, PlanningPreferences, localHorizonPath)` replaces the prior `FromLocation`; persistence schema nests `Preferences` under one JSON object (no flat-shape fallback — solo-consumer no-backwards-compat rule).

Old `settings.json` files with the flat `Horizon` / `DurationMinutes` JSON shape will fail to load cleanly under the new schema. By design — hand-edit or wipe `%APPDATA%\TargetPlanner\settings.json` once.

Locked decisions: combinator in Library (per consumer-agnostic rule); remove `Of` entirely (per review's "canonical idiom is `At`"); `ObservationMoment` record struct (matches AL convention over the legacy tuple); `PlanningPreferences` in TP (TP-specific shape; IS/ISM will have their own richer types when they materialize); nested JSON; coordinated same-day paired commit. Zero `#pragma warning disable CS0618` directives remain in either repo. 451 Library tests pass (+7 new for `MaxOfHorizonProfile` and `ObservationMoment`); TP builds clean. Manual UI smoke remains user-driven (boot, site switch, spinner edits, polyline horizon, date scrub, DST night, hot-reload, settings persistence, Ctrl+N dialog).

### 2026-05-17 — Post-collapse follow-up: stamp-race CAS + single-seam closure

Small follow-up driven by the architecture-review pass at [`docs/design/2026-05-17-architecture-review.md`](docs/design/2026-05-17-architecture-review.md). Two items:

- **`ChartCacheStore.EnsureAsync` CAS stamp guard.** Two concurrent pipelines (debounce tick firing while a prior `RunPipelineAsync` is still awaiting) could race at `mLastEnsureCtx = ctx`, leaving the older stamp in place. Today benign — sub-charts don't consume eval flags so the over-invalidating diff on the next call settles into idempotent per-key Prepare paths — but a latent trap for any future flag-consumer. The CAS (`if (ReferenceEquals(mLastEnsureCtx, prev)) mLastEnsureCtx = ctx;`) catches one direction; the residual case still over-invalidates (safe). Full-ordering would pipe coordinator `gen` into the cache and stamp by max-gen; deferred since current consumers don't need it.

- **`RefreshVisibility` removed from `IAltitudeSubChart` + four implementations.** `FilterMenuPresenter.OpenEditFiltersDialog`'s post-save loop was the last caller. `RefreshActiveFilterAfterDialogSave → SetActiveFilter` already routes through `mCoordinator.Apply(SnapshotCurrent())`; the explicit `foreach sc.RefreshVisibility(...)` was redundant on the common path and bypassed the single-seam pipeline on the empty-library edge case. Replaced with a single belt-and-suspenders `mCoordinator?.Apply(SnapshotCurrent())` (debounce collapses the double-Apply on the common path) and deleted the interface method + ~120 lines of implementation across Day/Sky/Year/Sessions. Render now owns the full hide-on-no-fit / fit-re-evaluation path that RefreshVisibility used to mirror on Day/Sky.

### 2026-05-17 — Chart-pipeline SoC pipeline collapse + observation dialog + Library Saemundsson

Multi-day push driven by the moon-missing-on-Sky→Day bug spiraling into a paradigm conversation: the coordinator's dispatch table had accreted special-case branches, every bug fix added another, and the user wanted a one-shot refactor that made the "do nothing" tests live in the data layer (cache) rather than the orchestration layer. In rough commit order:

- **Library `f444ef2` — Saemundsson 1986 refraction, drop Bennett.** Single canonical refraction formula taking geometric (true) altitude (the direction every Library caller actually starts from). `SunPosition.ApparentAltitudeAt` switches to Saemundsson; `SkyBrightness.KsAt` docstring clarifies `moonAltDeg` wants apparent altitude. 324/324 tests pass; new tests cover geometric-vs-apparent horizon behaviour.

- **TP `cafbf3c` — `MoonAltitudeEntry` + per-`DayWindowKey` moon slot.** New cache axis for moon altitudes (singleton, not target-keyed). Mirrors `TargetDayAltitudeEntry`'s shape. Cleared on `SetLocationAsync` alongside per-target dicts. Scaffolding for Phase 8; not yet consumed.

- **TP `6aeff48` — `ChartEvaluation` record + thread through `Render` signature.** Typed staleness record carrying per-axis change flags (`LocationChanged` / `TargetsChanged` / `HdmChanged` / `DayModeChanged` / `BrightnessInputsChanged`) plus the current `DayKey` / `HdmKey` / `DayMode`. `IAltitudeSubChart.Render` gains the eval param; sub-charts accept and initially ignore it. Coordinator constructs a `FullChange` placeholder for now.

- **TP `d4fe74c` — `EnsureAsync(ctx, dayKey) → ChartEvaluation` single entry.** Cache becomes the single source of truth for pre-render staleness. `EnsureAsync` diffs the supplied `ctx` against the last-applied `ctx` (per Location value-equivalence, Targets ref-list, Hdm key, DayMode, brightness inputs) and runs `SetLocationAsync` + `PrepareManyAsync` + `PrepareFitsAsync` + `PrepareDayAsync` + `PrepareMoonAsync` in sequence. Each downstream Prepare is internally idempotent so warm-ctx calls settle in the per-key fast paths.

- **TP `07f49f0` — Coordinator collapse (91 ins / 330 del).** Drops the per-area diff table (`locationKeyChanged` / `targetsChanged` / `hdmKeyChanged` / `dayModeChanged` / `activeNeedsFullRender` / `hdmOnlyChanged`), per-area stamps (`mLastAppliedByArea` / `mEverRendered`), and 3-way dispatch (`Render` / `RefreshVisibility` / `ShowOnly`). `RunPipelineAsync` is now: generation bump → compute `dayKey` from `ctx.Location` → `cache.EnsureAsync(ctx, dayKey)` → generation guard → render active area → post-apply hook. One path, same path every time.

- **TP `0c57ff3` — `Log.Diag(category, message)` semi-permanent diag channel.** Extends `%APPDATA%\TargetPlanner\Logs\tp.log` (later relocated under `Logs\`) with diag-level writes tagged `DIAG/<category>`. Categories filtered via the `TP_DIAG` environment variable: comma-separated list, `"*"` for all, empty/unset for none. Debug builds default to all-on; Release defaults to off (zero overhead). Coord / Cache / Day / Sky / UI call sites added so chart-pipeline investigations persist across runs without VS attached.

- **TP `2a0d36e` — Day moon paint Control.Refresh fix + Sky moon overlay + UI logging.** The fix to the bug that originally prompted the refactor: LC2's SKControl skips Fill-only `LineSeries` on the first paint after a hidden→visible cycle. Day's moon series (`Stroke=null`, `Fill` set) became invisible on first Sky→Day after Sky-active date scrubs; Sky's moon overlay rendered fine across the same sequence because Sky was visible during scrubs (LC2 cache stayed hot). Workaround at the dispatch layer (`MainForm.RenderArea`): render sub-chart, flip `ShowOnly` visibility, then `sc.Control.Refresh()` to force a synchronous repaint that picks up the new Series state. Sky moon overlay added as a feature (translucent grey fill mapped from altitude → magnitude range) which doubled as the diagnostic that pinpointed the bug. UI event logging via `Log.Diag("UI", ...)` for DatePicker / TimePicker / radios / CheckBox_Sky / Button_Graph / Button_Now so the click→pipeline→render trail is self-evident.

- **TP `ac1e40e` — `ChartCoordinator` computes dayKey from `ctx.Location`, not from cache.** Mid-flight regression: on date scrub the coordinator was reading `mCache.LocationNightCache?.Starting` for dayKey derivation, but `SetLocationAsync` inside `EnsureAsync` hadn't cleared+rebuilt NightCache yet — so the coordinator picked up the OLD night's dayKey, `PrepareDayAsync` / `PrepareMoonAsync` built under that OLD key, and the sub-chart Render then derived the NEW dayKey from the post-clear NightCache → 100% cache miss → all 38 targets vanished from Day. Fix: always `NightCalculator.ComputeNight(ctx.Location)` directly (sub-millisecond Meeus). Caught by `dayEntryNull=38` in the new Day target-filter diag line + the `WARN Day moon cache miss` from the Phase-8 defensive fallback.

- **TP `923a5b6` — Day + Sky read moon from cache (Phase 8).** Both sub-charts read per-minute moon altitudes from `cache.GetMoonOrNull(dayKey).AltitudesPerMinute` instead of `AstroUtil.GetMoonAltitude` inline. `BuildOrUpdateMoonSeries` signature changed: takes pre-computed `IReadOnlyList<double>` altitudes + chartStart + count + illumination, no longer takes Location. Inline `ComputeMoonAltitudesInline` kept in each sub-chart as a defensive fallback. Sky.Render refactored to use `ChartLayout.BuildDayWindow(night)` so the `DayWindowKey` it queries with matches the one EnsureAsync built under.

- **TP `796d92d` — Form labels off `AstrometryUi.For` onto cache (Phase 9; `AstrometryUi.cs` deleted).** Eliminates the parallel-compute path that did its own `NightCalculator.ComputeNight(location)` every form-spinner tick, structurally identical to `ChartCacheStore.LocationNightCache.Starting`. `RefreshAstrometryLabels` now reads `NightWindow` from cache (with `NightCalculator.ComputeNight` fallback for early-init before `mCache` exists), then computes the five non-cached values inline (sun altitude via `SunPosition.AltAzAt`, moon altitude / phase via `AstroUtil`, moon rise / set via `AstroUtil.GetMoonRiseAndSet`).

- **TP `97f5dc2` then `759f968` — Sub-chart `Render` short-circuit (Phase 7) shipped, then reverted.** Each sub-chart got a predicate to skip Render when no input it cared about changed since the last successful Render (Day cares about Location/Targets/Hdm/DayMode; Sky also cares about BrightnessInputs; Year/Sessions just Location/Targets/Hdm). Made `ChartEvaluation` flags load-bearing. **Reverted** when user reported "moon position and shape shifts on day → sky → day toggles": LC2's paint state across hidden→visible `Control` transitions isn't stable even with identical Series/Values data — short-circuit skipped the Series reassignment that LC2 apparently needs to reset its internal paint state. `mLastTargets` fields kept on each sub-chart as scaffolding for a smarter short-circuit that also accounts for visibility transitions; Phase 7 can be re-attempted later if/when LC2 paint stability is verified.

- **TP `aa3e082` — Hoist `PrepareMoonAsync` out of the targets-present branch.** Startup edge case: at first invoke `ctx.Targets.Count == 0`, so `EnsureAsync` (correctly) skipped per-target prep — but `PrepareMoonAsync` was nested under that branch, so the moon entry never got built, and Day's startup Render hit its defensive inline fallback with a WARN. Moon is target-independent (function of Location + night only); hoisted out of the targets-present branch so any valid dayKey triggers moon prep.

- **TP `21ee261` → `dbd644f` → `0995243` — Observation dialog evolution (Ctrl+N).** Right-click-title-bar invoked at first, modal at first, then rebound to Ctrl+N (Alt+M conflicted with Windows menu accelerator) and made modeless + TopMost so the user can interact with the main UI while the dialog stays open. Writes `USER_OBS_START id=<4-hex>` / `USER_OBS_END id=<4-hex> ctx=(area=...,date=...,...) screenshot=... checked=[...] notes="..."` / `USER_OBS_CANCEL id=<4-hex>` markers bracketing the user's investigation window. Screenshot capture via `Graphics.CopyFromScreen` on the MainForm bounds (LC2's SKControl paints via Skia and `Control.DrawToBitmap` returns blank for it); dialog hides itself before the capture so it doesn't appear in the saved PNG. Pre-seeded checklist editable via `personal-defaults.json`. Mark sub-feature (Ctrl+M, mid-session timestamp pin with inline label) shipped then removed pending revisit.

- **TP `21ee261` then `f83ade7` then `3b7aab9` — Logs folder consolidation + Help → Feedback menu.** `tp.log`, `tp.log.prev`, `screenshots/`, `screenshots.prev/` all relocated under `%APPDATA%\TargetPlanner\Logs\` (was scattered at the TargetPlanner/ root). Single delete clears every captured-observation artifact while leaving `settings.json` / `filters.json` / `local-targets.json` untouched. `Log.StartNewSession()` (called from `Program.Main` before MainForm boot) rotates both `tp.log` → `tp.log.prev` and `screenshots/` → `screenshots.prev/` so each run starts fresh; one session back preserved for postmortem. `Clear All Data` upgraded to delete the whole `Logs/` directory recursively. New Help → Feedback menu item (tooltip explains the Ctrl+N observation feature) with single child "Open Notes Folder" that launches Explorer on the Logs directory.

Worth knowing for future spelunkers:

- Phase 7's short-circuit was the chosen-and-shipped pattern for making `ChartEvaluation` flags "load-bearing" (the user-stated paradigm intent of the typed-record approach). The revert means the flags are populated by the cache but only the `DayKey` / `HdmKey` / `DayMode` fields are read (by sub-chart cache-lookup keys). The bool flags are scaffolding for a future smarter short-circuit. Don't be surprised that the typed paradigm has weaker enforcement than the plan documents promised.
- The `Control.Refresh()` workaround in `RenderArea` is load-bearing for every sub-chart's first paint after a hidden→visible cycle. Don't remove without verifying LC2 paint stability across `Visible` toggles independently.
- `AstrometryUi.cs` is gone. If `git log -- TargetPlanner/Support/AstrometryUi.cs` shows old activity, the file existed pre-`796d92d` and was the parallel-compute path that's now inlined into `RefreshAstrometryLabels`.
- The observation dialog (Ctrl+N) is solo-dev infrastructure, not user-facing functionality. The README doesn't mention it; the `Feedback` menu tooltip is the only in-app discovery. If TP becomes a multi-user tool, decide whether Ctrl+N + USER_OBS_* persistence is worth keeping in shipped builds (it gates on the dialog being modeless + persisted to disk).

### 2026-05-17 — HD overlay: per-target toggle inside global mode + sticky fast-path

Two follow-up commits to `091aa56` (sticky-across-H/D/M-scrubs + strict global mode). The strict guard turned out to be too restrictive — the user wanted to opt individual targets out of a global apply without losing the auto-extend behaviour across scrubs:

- **TP `b891bcd`** — per-target left-click re-enabled in HD-overlay global mode. New `OverlayController.mGlobalOptOuts: HashSet<LineSeries>` tracks per-target exceptions; `EnsureGlobalApplied` skips opt-outs so H/D/M scrubs don't re-overlay an opted-out target. Bidirectional (toggle-off adds to opt-outs, toggle-on removes). Status messages annotate with `(global -- excluded)` / `(global -- restored)` when the click happens in global mode so the user knows they're still in it. Drains cleanly: toggling off the last backup exits global mode and clears opt-outs; right-click apply-all clears opt-outs at start for a fresh global state. Considered deriving opt-outs from `(visible-fitting) \ mBackups` (no new field) but rejected — would conflate "user opted out" with "user just toggled off in per-target mode pre-global," defeating the auto-extend behaviour.
- **TP `f1cf369`** — sticky fast-path for rapid re-toggle without mouse movement. After a toggle the curve is replaced by the step shape so the cursor no longer sits on a hit-testable curve; a second click at the same pixel would miss or grab a neighbour. `OverlayController.TryToggleAt` now takes pixel coords; when the new click pixel matches the last successful toggle's pixel exactly, the sticky target is re-toggled without a hit-test. **Pixel-exact match (no tolerance)** — any non-zero tolerance would create a dead zone around the sticky target where adjacent or overlapping curves can't be selected. A 1-pixel mouse nudge falls back to the normal hit-test. Sticky state cleared on `ClearAll` / `RestoreAll` / `ToggleAll` / `PruneStaleBackups` when the target goes stale / drain-to-empty in `TryToggleAt`.

CLAUDE.md glossary entry for HD Overlay, ARCHITECTURE.md's day-chart click-semantics paragraph, and README.md's HD Overlay + Chart interactions sections updated in the same pass — they all referenced `mOverlay.RestoreAll()` for right-click, stale since `091aa56` swapped it to `ToggleAll`.

### 2026-05-13 — Per-site local horizon (.hrz) + UTC offset (Location refactor Phase 1)

Phase 1 of the Location refactor. **Phase 2 (Library `Location` strip) is queued** under "Currently open" above.

- **TP `6d17600` — `.hrz` polyline horizon + per-site UTC offset.** `NamedLocationSetting` gains `LocalHorizonPath` (NINA `.hrz` polyline reference) and `UtcOffsetHours` (`double?`). New `TargetPlanner/Horizons/HrzFileLoader.cs` parses NINA's two-column whitespace format; failures fall back to scalar `TargetFloorDeg`. `MainForm.mLocalHorizon: IHorizonProfile` field loaded at startup + site-pick + `FileSystemWatcher` change (500 ms debounce), threaded through `SnapshotCurrent` into `PlanningPolicy.LocalHorizon`. `HdmKey` gains an `IHorizonProfile LocalHorizon` field (reference-compared, null for `ScalarHorizonProfile`) so a polyline swap / hot-reload invalidates the per-(target, HdmKey) fits cache without thrashing on scalar edits. Designer-managed `Button_BrowseHorizon` + `Label_HorizonPath` live in `GroupBox_Location` (GroupBox grew 30 px; `GroupBox_LocalDateTime` shifted down). Per-site UTC offset via `NumericUpDown_TimeZone` spinner (-12..+12 whole hours) replaces the prior implicit `TimeZoneInfo.Local`; `NamedLocationSetting.TimeZoneFromUtcOffsetHours(hours)` builds a no-DST custom `TimeZoneInfo` so the offset round-trips through `Location.TimeZoneInfo.BaseUtcOffset.TotalHours`. DST simplification is intentional — "this site is at UTC-5" stays UTC-5 year-round.

### 2026-05-13 — Gemini code-review triage + ChartCoordinator pre-stamp fix

External (Gemini) architectural review surfaced 8 findings across concurrency, SoC, and perf themes in prep for the next two features (SessionSolvers UX, XISF integration). Most were already-roadmap'd, misreads of the code, or low-impact deferrals; one real race surfaced and landed:

- **TP `eeb13c7` — `ChartCoordinator` pre-stamps `mLastAppliedTargets` at pipeline entry.** Gemini's "snapshot drift" framing (#6/#7) pointed at a benign Location-edit-then-radio scenario — `mLocation` reference reads are atomic and the cache keys on `(Location, Target)`, so the snapshot is internally consistent. Tracing the call paths surfaced a different real race: the checkbox toggle path routes through a *separate* 250 ms `mCheckedToggleDebounce` → `RunGraphBuildAsync` → `ApplyImmediateAsync` (asymmetric with every other handler's coordinator-debounced `Apply`). A radio click during the ~2 sec cache-cold pipeline-await window calls the no-arg `SnapshotCurrent`, which reads `LastAppliedTargets` before the checkbox pipeline has stamped, capturing the pre-toggle target set. The radio's smaller-target pipeline completes first (cache de-duped via in-flight build sharing) and stamps the stale set as the SoT; bug persisted across subsequent radio / HMD / sort actions until user-driven recovery via `Button_CheckedTargets`. Fix moves the stamp from end-of-success to start-of-pipeline. `mLastAppliedTargets`'s semantic shifts from "last successfully rendered" to "last intended to render"; bail-safe per the commit message's analysis; consumer audit clean (`SnapshotCurrent` no-arg, `SortPresenter`, `LastAppliedFor`).

**Triage residuals — no new action:**

- **#1 `ObservablePoint` per-minute allocations** in `AltitudeSubChart_Day` (~1,440 × N visible targets per Day render via `data[i] = new ObservablePoint(...)` at lines 601, 638) — deferred. Gen-0, click-triggered, no UX symptom listed. Mutating `p.X` / `p.Y` in place is the plausible 1-2 LOC fix but depends on LC2 v2.1.0-dev-365 firing redraws on `INotifyPropertyChanged` — unverified. Profile first if SessionSolvers UX work surfaces GC stutter.
- **#4 `GetMoonAltitude` 56 B/call** — already item 5 above ("Lower-priority perf chasing"); Library-side fix. Gemini's "~80 KB/render × 44 targets" framing was off by a factor of 44 — the moon series is a single instance, not per-target.
- **#5 polyline-horizon persistence** — already PR-5 sketch above; `[Obsolete]` marks shipped in TP `972a757` / Library `301fa3f`. Gemini's "promote `PlanningPolicy` to a first-class persisted entity" overshoots — `PlanningPolicy` is a transient snapshot, the schema gap is on `NamedLocationSetting` (planned to gain `string? LocalHorizonPath`).
- **#8 native isolation for XISF** — already item 6 above; `AllowUnsafeBlocks=false` pre-wiring from `ac467d8` plus `Imaging/README.md` boundary declaration is sufficient. Designing `IImageMetadataProvider` preemptively violates the "don't design for hypothetical future requirements" rule.
- **#2 `ChartLayout.BuildDayWindow` "returns records"** — dismissed; returns a 5-element value tuple, already stack-allocated.
- **#3 DSU dicts in `RecomputeDupeSetColors`** — dismissed; called only on `KnownTargetsChanged` (NINA load + Add/Remove) with N≈44 — allocations measured in bytes, not KB.

**Net Gemini assessment.** Diagnostic skill > prescriptive skill. Useful for surfacing the polyline-horizon plan independently (positive signal about PR-5 direction) and the ObservablePoint pattern (worth a profiler pass eventually). Recommendations consistently overshot; lean on the diagnoses, not the fixes.

### 2026-05-13 — Architectural-review campaign: post-ship re-review follow-ups

A second-pass review of the just-shipped campaign (above) flagged three high-leverage refinements ahead of the next two features (SessionSolvers UX, Local Horizon polyline). All three landed across two TP commits + one Library commit:

- **Library `301fa3f` + TP `972a757` — `[Obsolete]` `Location.Horizon` / `.Duration` + UpdateHorizonLine semantics + SortPresenter routing.** `Location.Horizon` and `Location.Duration` are now `[Obsolete]` (warning, not error) — Library scheduling helpers MUST take horizon as an `IHorizonProfile` and duration as a `TimeSpan` explicitly, never read off a captured `Location` reference. The four transitional TP-side reads (`SnapshotCurrent`'s policy projection, `CoordinatePresenter`'s spinner sync, `NamedLocationSetting`'s `FromLocation` persistence, the chart-sub-area horizon-line reads) are pragma-suppressed with one-line rationales each. `SortPresenter`'s `TargetOrdering.ByRise(... mLocation.Horizon)` — the one TP-side read outside that transitional set — now routes through `SnapshotCurrent().Policy.TargetFloorDeg`. Companion: `UpdateHorizonLine` now receives `ctx.Policy.TargetFloorDeg` (the user's scalar spinner) rather than `LocalHorizon.MinAltitude`. Pre-emptive PR-5 correctness: with a polyline horizon, `LocalHorizon.MinAltitude` would be the polyline's lowest sample, and the green chart line would sit below the floor the user just set on the spinner. The polyline still drives per-azimuth fit decisions through the cache; the chart line is a UI affordance for the spinner knob, not the polyline.

- **TP `9111675` — Day/Sky's `BestSession.For` lifted into the cache.** `NightFit` (in `Caches/TargetFitEntry.cs`) now also carries `StartUtc` / `EndUtc` alongside Ceiling / Floor / CenteredFloor. `TargetFitEntry` now exposes a `Tonight: NightFit` slot in addition to the per-night `Nights[i]` year array — tonight's index in the year grid isn't 0 (the grid anchors at 1st-of-month, not today), so a dedicated slot is the right shape. `ChartCacheStore.BuildFitEntryAsync` computes both year array + Tonight in one `Task.Run` (`ComputeNightFits` + new `ComputeTonightFit` helper). `AltitudeSubChart_Day` reads `cache.GetFitOrNull(target, ctx.Hdm)?.Tonight` for its HD-overlay window box (StartUtc / EndUtc / Floor straight off the cached entry); `AltitudeSubChart_Sky` reads `cache.GetFitOrNull(target, ctx.Hdm)?.Tonight.Floor.HasValue` for hide-on-no-fit. Day's `ComputeBestDayWindow` static helper and Sky's `HasFit` static helper are deleted. Net effect: zero UI-thread `BestSession.For` calls anywhere in the chart render path, and Day/Sky/Year/Sessions all read fit decisions from the same cache — single source of truth ahead of SessionSolvers UI work.

Deferred from the same review (acknowledged tradeoffs, raise again when SessionSolvers adds a fourth axis): `TryPublish` factoring (the lock/identity/publish idiom is now in four places in `ChartCacheStore`), generic `CacheEntry<TKey, TValue>` consolidation, `mPendingContext` / `mPendingProgress` fences on `ChartCoordinator`, dead `TargetReady` / `LocationChanged` events on `IChartCacheStore` (zero subscribers; reintroduce when the IS plugin needs them).

### 2026-05-13 — Architectural-review campaign (9 commits)

Reviewer-driven multi-PR sweep through TP's chart pipeline, kicked off by a "Sessions tab no curves" bug repro that exposed deeper SoC drift between sub-charts and the cache. In commit order:

- **`c74224f` — Cache + sub-charts: lift fit compute into ChartCacheStore, drop CTS scaffolding.** Year / Sessions sub-charts stopped owning `Task.Run` + `CancellationTokenSource`; per-(target, HdmKey) fits moved into `ChartCacheStore.ComputeNightFits` behind `GetFitOrBuildAsync` / `PrepareFitsAsync`. Sub-charts became synchronous render-only painters. Closes the original Sessions-no-curves repro (the symptom was an in-flight fit compute losing its CTS-supersession race against a radio swap). Design rationale at [`docs/design/chart-fits-cache.md`](docs/design/chart-fits-cache.md).
- **`660f396` — PlanningPolicy on ChartContext + concurrency hardening (PR 1 + PR 2).** New `State/PlanningPolicy.cs` record aggregates `TargetFloorDeg` / `MinDuration` / `MoonProfile` / `FilterCenterNm` / `IHorizonProfile LocalHorizon`. `ChartContext` replaces the prior scattered fields with a single `Policy` and a derived `Hdm` property. `HdmKey` projects from `Policy` instead of reading scattered MainForm fields. The `IHorizonProfile` seam is pre-wired through `WithScalarHorizon(...)` so the future `.hrz` work (deferred per user request) is pure plumbing. Companion concurrency hardening: `Interlocked.Increment` / `Volatile.Read` fences on `ChartCoordinator.mGeneration`, `try/catch` on every `async void Tick`, `PrepareManyAsync` continuation fault propagation, `TryPublish<TKey, TVal>` factor in `ChartCacheStore`, form-lifecycle `mFormClosingCts` for warmup cancellation on close.
- **`caacff7` — Coordinator owns target SoT; drop mLastRenderedTargets + Reorder (PR 3).** `ChartCoordinator.LastAppliedTargets` is now the canonical source of "what was last rendered." MainForm's `mLastRenderedTargets` parallel store removed. `IAltitudeSubChart.Reorder` deleted — sort changes route through the coordinator (`Apply(SnapshotCurrent(sorted))`); the diff sees a `Targets` reference change and Renders from cache (still cheap because the set is unchanged). The no-arg `SnapshotCurrent()` reads `mCoordinator.LastAppliedTargets`.
- **`3e12470` — Day altitude curves into the cache; Day.Render becomes synchronous (PR 4).** New `Caches/TargetDayAltitudeEntry.cs` + `Caches/DayWindowKey.cs` introduce a third cache axis. `ChartCacheStore.ComputeDayAltitudes` lifts the per-minute `AltAz.At` sweep (44 targets × 1440 min ≈ 63k Meeus calls) off the UI thread. Coordinator's pipeline awaits `PrepareDayAsync(targets, dayKey)` for Day-area Renders. `AltitudeSubChart_Day.Render` becomes synchronous, matching Year / Sessions.
- **`ac467d8` — AstrometryUi → immutable record; PCL isolation boundary pre-set (PR 6).** Last static-mutable-state holdout in TP eliminated. `Support/AstrometryUi.cs` converted from static class to sealed record with `For(location)` factory and `Empty`; MainForm holds `mAstrometryUi` field, rebuilt on every `RefreshAstrometryLabels()`. Pre-shipped: new `TargetPlanner/Imaging/README.md` declaring the PCL native-marshalling boundary, plus `<AllowUnsafeBlocks>false</AllowUnsafeBlocks>` in `TargetPlanner.csproj` as a compile-time assertion before the XISF PR lands.
- **`4536de3` — Year + Sessions: O(1) series-to-target tooltip lookup (PR 7.1).** `mTargetBySeries` dict populated in Render, replaces the prior O(N) scan in tooltip formatters. Hot path on hover.
- **`c7da4b2` / `ed66492` / `17db0b0` — MainForm presenter file splits (PR 7.4a / 7.4b / 7.4c).** Three partial-class extractions into `Forms/Presenters/`: `MainForm.SortPresenter.cs` (sort + populate + listbox-row plumbing), `MainForm.CoordinatePresenter.cs` (the four `CoordinateInput` callbacks + model→UI sync methods), `MainForm.FilterMenuPresenter.cs` (~16-method filter + moon-avoidance cluster). MainForm.cs drops from 2689 → 2128 lines (−21%) without behaviour changes. One commit per presenter so any regression is easy to bisect. These are partial-class file splits rather than real Presenter objects because each cluster orchestrates 6+ form controls + the VM + the coordinator and constructor-injecting all of that would have been heavier ceremony than the relocation.

PR-5 (Local Horizon `.hrz` ingestion) was scoped in the plan but deferred per user request. The `IHorizonProfile` seam is pre-wired so the future loader + UI + persistence work is pure TP plumbing — see the "Future-flagged UX/Core split — Local Horizon vs Target Floor" section above for the remaining sketch.

### 2026-05-10 — Add/Remove target buttons + dupe-set visual flagging

User-driven target lifecycle: **Add** merges the combo's resolved `SelectedSingle` (NINA-known or transient-from-spinners) into `Checked`, persisting locally-typed targets to a sidecar JSON; **Remove** drops the target from `KnownTargets` entirely (NINA-loaded targets reappear on next browse, locally-added stay gone). Sidecar at `%AppData%\TargetPlanner\local-targets.json`, merged into `KnownTargets` after every NINA `Load(...)` so a re-browse doesn't wipe user additions. Spinner-edit handlers honor the combo's typed `Text` as the new target's `Name` so "type a fresh name + spinner-edit RA/Dec + Add" works end-to-end. Clear-All-Data dialog deletes the sidecar alongside the other persistent files.

`CheckedListBox_SelectedTargets` rows now visually flag duplicates: targets sharing `Name` OR `(round(RA, 6), round(Dec, 6), North)` form transitive groups (DSU/union-find), and each group with size > 1 gets a stable pastel background. Required two framework-level workarounds: `CheckedListBox` hard-codes `DrawMode = Normal` in its property setter (silent no-op) and `OnDrawItem` does its own checkbox+text paint without calling `base.OnDrawItem` (so the standard `DrawItem` event never fires). `Forms/DupeAwareCheckedListBox.cs` re-enables `OwnerDrawFixed` via `CreateParams` and owns the entire row paint, exposing a `Func<int, Color?> RowBackground` callback. The listbox items are now `TargetRow` wrappers (instead of bare name strings) so index-based lookups return the right `Target` instance even when two rows share a name — fixes the "highlight either row, see the same RA/Dec in spinners" symptom that the user spotted.

`TargetSelection` gained `AddKnownTarget` + `RemoveKnownTarget` mutators (incremental, fire `KnownTargetsChanged` + `CheckedSetChanged` + `SelectedSingleChanged` exactly as needed). `Button_CheckedTargets` (added earlier in this session, commit `a028f68`) was joined by `Button_AddTarget` and `Button_RemoveTarget` for the full lifecycle.

### 2026-05-04 — .NET 10 migration + Library perf wave

Long session covering, in commit order:

- **Astronomy.Core review** — closed all 5 small findings + 6 missing test files + profile-aware `VisibilityWindows.For` refinement + 4 ROADMAP residuals (Library `d38fed9` `629e37b` `d11a6dc` `319e4df`; TP `a98a45e`).
- **Portfolio framework bump to .NET 10** — TP `net481` → `net10.0-windows10.0.19041` (TP `85bc590`); Astronomy.Core → `net10.0` (Library `b834f52`); Astronomy.PCL → `net10.0` (Library `c7eeff9`); Astronomy.Core.Tests pinned `LangVersion latest` (Library `6d66881`); Astronomy.Core nullable + LangVersion latest (Library `2bd3c20`); LocalLib reference dropped (`OpenFolderDialog` → stock `FolderBrowserDialog`, single-select).
- **Library perf opts** — BDN baseline (Library `6d9f402`); `MoonSeparation.ObserveAt` single-pass alt+az dedup (Library `adfdd5f`, −49% time, −100% alloc); `ObserverInfo` class → readonly struct (Library `8ca5b37`); `MoonPosition` periodic tables `int[,]` → `int[]` flat (Library `383c38c`, −10% on `GetMoonAltitude`); `BestSession` + `SessionSolvers` accept null altitudeQuality → closed-form `SinAltitudeOverSession` fast path (Library `e83a110`); TP charts drop their `SinAltQuality` lambdas (TP `14a87ea`). Cumulative `BestSession_For_Narrowband` 177 µs → 83 µs (-53%, -60% alloc).
- **TP UX fixes** — progress-bar wired (TP `0cec432`); 8 px gap above `Panel_AltitudeChart` (TP `4fdd479`); `NightCache.ComputeYearStartDay` off-by-one fix (Library `0d4ef83`); Year + Sessions exact 1st-of-month CustomSeparators (TP `bcd148a`); `RightChromePx` 24 → 40 (TP `a5d6171`); MainForm Designer VS-regen cleanup (TP `7b81158`).
- **Memory + framework_stance memory** — rewritten 2026-05-04 to reflect uniformly-net10 portfolio (NINA migrated upstream too, verified at `E:\Projects\VisualStudio\Astronomy\NINA\NINA\NINA.csproj:462`).

- **TP migrated `net481` → `net10.0-windows10.0.19041`.** Single TP commit. csproj: `TargetFramework` bumped, `LangVersion` 10 → `latest`, `AutoGenerateBindingRedirects` removed (irrelevant on modern .NET), `<ServerGarbageCollection>` + `<ConcurrentGarbageCollection>` MSBuild properties added (replaced the deleted App.config `<runtime>` block). The `Win10 2004` Windows API contract version is needed because `SkiaSharp.Views.WindowsForms 3.119.0` (transitive via LiveCharts2) only ships modern-.NET assets at `net8.0-windows10.0.19041` — the default `net10.0-windows7.0` would fall all the way back to the package's `net462` lib, which doesn't load on .NET 10. LocalLib reference dropped: its reflection-based `OpenFolderDialog` multi-select hack relied on `System.Windows.Forms.FileDialogNative+IFileDialog` internals that don't survive into modern WinForms. `MainForm.Button_BrowseTargetList_Click` now uses stock `FolderBrowserDialog` (single-select; multi-select was a nice-to-have). `App.config` deleted (modern .NET ignores `<startup>` and `<runtime>` blocks). Astronomy.Core (`netstandard2.0`) and Astronomy.PCL (`net8.0`) sibling assemblies unchanged — both forward-compat with the new TP. **Velopack 0.0.1298** is forward-compat through netstandard2.0 fallback; bump to 0.0.1589+ shipped 2026-05-22 (see entry above).

- **Phase 4 — Chart migration to LiveCharts2 SHIPPED.** All four chart areas (Day / Sky / Year / Sessions) ported off `System.Windows.Forms.DataVisualization.Charting` to LC2 v2.1.0-dev-365. Each area is a sub-chart class implementing `Charts.IAltitudeSubChart` (common: Control, IdealHeight, IdealHeightChanged, UpdateNowLine, UpdateHorizonLine, Render, Reorder, RefreshVisibility, Dispose). MainForm holds `Dictionary<string, IAltitudeSubChart> mSubCharts` keyed by area; picker / spinner / debounce / Graph-click traffic dispatches via foreach + dict lookup. Sky keeps a typed `mLC2Sky` reference for `ActiveFilterCenterNm` + `RefreshSkyBrightness` (K-S quirks outside the interface). `BestSession.ResolveCandidates(...)` (Library) added to expose visibility ∩ moon-clear so Sessions's PlaceBest + PlaceCentered see identical inputs. Year switched from night-max `YearAlt` to session-floor altitude (more actionable planning metric). The legacy `AltitudeChart.cs` (~1400 lines) + `AltitudeSeries.cs` (~900 lines) + `LegendClickHandler.cs` + `LegendHitTester.cs` (dead after the custom-legend pivot) deleted; DataVisualization package reference dropped. Phase 4 commits: `bebf909` PR4a Day · `582b4fb` PR4a Day plot-area lock · `edc2c9b` PR4b prep (ChartLayout hoist) · `5763bbc` PR4b Sky · `7d5d3b2` PR4c Year + universal hide rule · `99f2fc3` Year → floor metric · `46dcd4f` PR4d Sessions · plus PR4e (this commit). Companion Library commits: `6fce6b0` (BestSession non-positive-duration → null) · `a251524` (BestSession.ResolveCandidates public).

- **Chart-package investigation — LiveCharts2 chosen** — comparison (OxyPlot / ScottPlot / LiveCharts2) against the four representative tests + the dual-target (`net481` today, `.NET 10` post-migration) requirement, plus the MS-Charts→LC2 migration findings, now live in the prototype's `CLAUDE.md` at `E:\Projects\VisualStudio\LiveCharts2Prototype\` (relocated out of TP's `Prototypes/` 2026-05-08 once the investigation finished — own git repo, no longer a TP submodule). The prototype was the pattern playbook for the Phase 4 migration above.
- **Core solvers for parameter exploration** — Library `c2c1f5e` + `9504461` add `Astronomy.Core.Session.SessionSolvers` with the full six-method surface: `LongestDuration` / `LongestDurationIn` / `LowestHorizon` (transit-centered-or-wall-pushed placement, parallel to `BestSession.PlaceBest`) and `LongestDurationCentered` / `LongestDurationCenteredIn` / `LowestHorizonCentered` (strict-centered placement, parallel to `BestSession.PlaceCentered` / Symmetric-curve UI semantics). Foundation for downstream "what's possible tonight?" UI surfaces in TP and plan-relaxation paths in IS / ISM. `MoonClearIntersect` promoted from `private` → `internal` to let `SessionSolvers` reuse the existing moon sweep. 23 new tests, 164/164 in suite. TP UI consumers are a separate follow-up.
- **Per-sub-interval moon-aware Optimal placement** — `RenderOptimalSeries` no longer runs placement against moon-blind visibility windows on partially-moon-impacted nights. Library `3737cfa` promoted `BestSession.PlaceBest` to public, added `BestSession.PlaceCentered`, and added the new `SessionAltitude` class with `Floor` / `Ceiling` evaluation helpers. TP follow-up commit migrates the Optimal-chart per-night loop to call `BestSession.PlaceBest` (Floor / Ceiling via `SessionAltitude`) and `BestSession.PlaceCentered` (Symmetric) over `(visibility ∩ moon-clear)` candidates derived chart-side from cached `MoonSamples`. `ComputeBestDayWindow` (Day overlay) also moved to `SessionAltitude.Floor` for SoC consolidation. Retires Step-3 cleanup item "chart-side `BuildOptimalSeries` math". `HasMoonClearViableWindow` short-circuit dropped (PlaceBest/PlaceCentered returning null is the new sentinel).
- **CoordinateSharp roll-your-own** — pure-C# Meeus replacement landed in `e602bdb` (Library) + `2249834` (TP). Cache pre-population dropped from ~17 min to 2-4 sec on 44 targets; Astronomy.Core is now lock-free and managed-only.
- **Moon-avoidance re-enable** — committed alongside the CS removal; bisection disables removed.
- **Cache invalidation on Location change** — `LocationsCacheEquivalent` gates `mCache.SetLocationAsync`; lat/lon edits ride the debounce, combo picks fire immediately (commit `56269db`).
- **Year-chart visibility** — `RebuildDayTooltip` no longer hides Year-curve series for targets with no D-hour fit tonight (Day-only filter). Commit `24e3213`.
- **Location.Elevation end-to-end** — `Location` POCO gains `Elevation` (Library `2df74c1`); spinner wired into `MainForm.Designer.cs` + `SyncLocationUIFromModel` + `OnLocationEdited` + `LocationsCacheEquivalent` (TP `8b2a6d7`). Hillsborough preset (40.459456°N, 74.612921°W, 28.16 m) added; `MergeBuiltins` auto-fills existing settings by name match.
- **Elevation-dip on rise/set** — `MeeusUtility.HorizonDipDeg(elevationM)` + elevation-aware thresholds in `SunEvents.RiseSetOn` / `AstroUtil.GetMoonRiseAndSet` (Library `65ca166`); TP `Astrometry.cs` passes `localLocation.Elevation` (TP `8484ed8`).
- **Refresh dependent labels on Location edits** — extracted `RefreshAstrometryLabels()` from `UpdateLocalDateTimeEvents`; called from `OnLocationEdited` and `ComboBox_Location_SelectionIndexChanged` so dusk/dawn/altitude/illumination/phase/moon-rise-set track lat/lon/elevation/combo edits in real time (TP `8484ed8`).
- **Penns-Park-on-boot default** — `PickStartupLocation` always prefers Penns Park when present (commit `43fc931`); `LastSelectedLocationName` still tracks user's combo pick for persistence but no longer drives start-up.
