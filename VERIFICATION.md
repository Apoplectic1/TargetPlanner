# Verification

How to verify a TargetPlanner change before claiming it done. The standing distinction: build + tests = **code-correct**; UI/visual changes additionally need **feature-correct** verification (visual appearance, hover, click handling) — state explicitly which level was reached.

## Build

```bash
dotnet build "TargetPlanner.sln" -c Debug
```

Pure-managed graph, so `dotnet build` is fine (auto-restores). `msbuild "TargetPlanner.sln" -restore -p:Configuration=Debug` is the fallback. Requires the sibling `..\Library\` repo cloned next to this one.

**Trap — new AL `ProjectReference` without a sln entry fails silently.** The sln lists the cross-repo
AL projects *explicitly*; MSBuild follows a new csproj `ProjectReference` regardless, so the build
stays green while Solution Explorer silently omits the project (bit 2026-08-10 when the `.WinForms`
shell gained its `Astronomy.Diagnostics.Windows` dep). When adding an AL project reference, add the
matching sln `Project` entry + config rows (reuse the Library sln's project GUID) in the same commit.

## Tests

```bash
# TP-side (State/, Caches/, Targets/, Filters/, Settings/, loaders) — project-scoped, x64
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64

# Library-side — run when the change touched AL (Astronomy.Core / .NINA / .XISF)
dotnet test ..\Library\Astronomy.sln
```

Test-strategy detail + phase roll-out: [`docs/design/test-project-plan.md`](docs/design/test-project-plan.md).

## UI / feature-correct

- **`/verify-ui` skill** — drives TP's UI handlers programmatically (launch, simulate clicks/keys, capture before/after via the Ctrl+N diagnostics infrastructure, read screenshots + ctx + DIAG lines). Use when a change touches a click/scrub/menu handler not covered by unit tests (`Forms/Presenters/*`, `Forms/MainForm.cs` event handlers).
- **Manual smoke convention:** boot (lands on last-selected site, seed default Penns Park) → check a handful of targets → render; watch `%APPDATA%\TargetPlanner\Logs\tp.log`. Diag categories via the `TP_DIAG` env var (`Coord`, `Cache`, `Day`, `UI`, `Overlay`; `*` = all — Debug builds default all-on).
- LC2 paint quirks make some regressions invisible to tests entirely (e.g. moon-overlay first paint) — for chart-visual changes, human eyes on the running app are the final gate; say so rather than claiming done.

## Perf guardrail

Cache pre-population budget: **~2 s for a ~77-target library** (yearDays-dominated; boot fits near-free — the boot Hdm has no active filter; measured 2026-07-07, Release). Any cache-path change should be sanity-checked against this ceiling (boot with the image library is the natural probe — a `Warmup complete … totalMs=` line lands in `tp.log` under the `Cache` DIAG channel).
