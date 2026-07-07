# Test project — phased rollout plan

Phase 1 shipped 2026-05-27 (this commit). Phases 2–4 are not yet implemented but their shape is fixed; this doc is the authoritative pointer for picking them up.

## Why this exists

TargetPlanner had zero TP-side tests through 2026-05-26. CLAUDE.md actively forbade them ("Tests live in the Library repo, not here."). The just-shipped [`cache-contract.md`](cache-contract.md) made the gap concrete — 10 lifecycle/threading invariants documented, none enforced by an automated test.

`TargetPlanner.Tests/` now exists as a sibling project to TargetPlanner under the same solution, mirroring the Library's house style (xUnit, `OutputType=Exe`, raw `Assert.*`, no FluentAssertions/Moq).

## csproj + solution shape (Phase 1, shipped)

`TargetPlanner.Tests\TargetPlanner.Tests.csproj`:

```xml
<TargetFramework>net10.0-windows10.0.19041</TargetFramework>
<OutputType>Exe</OutputType>
<Nullable>disable</Nullable>
<Platforms>x64</Platforms>
```

TFM matches TargetPlanner's exactly (`net10.0-windows10.0.19041`, pinned by SkiaSharp via LiveCharts2). A consumer on bare `net10.0-windows` (defaults to `7.0`) would fail TFM compatibility against the ProjectReference to TargetPlanner.csproj.

Solution: `TargetPlanner.Tests` registered in `TargetPlanner.sln` with Debug|x64 / Release|x64 config rows. Run tests **project-scoped** (not solution-scoped) so the future `Astronomy.PCL.Native` vcxproj reference doesn't break the test build:

```
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64
```

## Phase 1 — Tier A pure-logic coverage (shipped 2026-05-27)

Layout: `TargetPlanner.Tests\Tests\` for `[Fact]` classes. Ten classes, 89 tests total (Theory inline-data counts).

| File | Targets | Tests |
|------|---------|-------|
| `HdmKeyTests.cs` | per-field equality matrix; `ReferenceEquals(LocalHorizon)`; GetHashCode stability | 12 |
| `DayWindowKeyTests.cs` | tick+count equality; `ChartStartUtc` preserves Utc Kind; default-struct = explicit zeros | 6 |
| `ChartContextTests.cs` | `Hdm` derived (ScalarHorizonProfile nulled, polyline passed through, all 5 fields source from Policy); record `with`; structural equality | 8 |
| `PlanningPolicyTests.cs` | `WithScalarHorizon`; `MoonProfile == null` when toggle off / filter null; happy-path projects to `ToProfile()` | 6 |
| `PlanningPreferencesTests.cs` | `Default`; FromDto null → Default; ToDto/FromDto round-trip; `with` | 5 |
| `ChartEvaluationTests.cs` | required `BrightnessInputsChanged`; record equality; `with` | 3 |
| `TargetIdentityTests.cs` | `NormalizeName` (trim + " Stars" strip + case-insens, theory); `AreSameTarget` (~1 arcmin tol, 0h/24h seam, cos(dec) pole convergence, opposite hemispheres); `SelectNewTargets` dedup + bucket-by-name + existing-set screen + null skip + order preservation | 19 |
| `SkyCentroidTests.cs` | empty/null → ArgumentException; single-point identity; 0h/24h seam (23.9h+0.1h → ~0h, not ~12h); pole; equator; antipodal-at-pole; RA wrap to [0,24); symmetric-meridian | 8 |
| `FilterTests.cs` | `ToProfile` Lorentzian projection (drops Name/CenterNm/BandwidthNm); record `with`; field-by-field equality (Name, BandwidthNm) | 6 |
| `FilterLibraryTests.cs` (in-memory only) | Find / Add / RemoveAt / Replace / ReplaceAll / ReplaceAll(null) → clear; `BuiltinDefaults` H/O/S/L/R/G/B; `FindBuiltinDefault` case-insens; `DiffersFromBuiltinDefault` field-by-field; `DefaultLibrary` ≡ BuiltinDefaults | 16 |

No test helpers needed in Phase 1 — Tier-A surface is TP-only types with no location fixtures.

## Phase 2 — persistence + 3 small refactors (shipped 2026-05-27)

Production-code refactors needed before the persistence tests can land (additive overloads; no behavior change for existing callers):

| Class | Refactor |
|-------|----------|
| `Settings\SettingsStore.cs` | Add `Load(string path)` + `Save(string path, AppSettings)`. Existing `Load()` / `Save(s)` delegate to overloads passing `FilePath`. |
| `Settings\LocalTargetStore.cs` | Add `Load(string path)` + `Save(string path, IEnumerable<Target>)`. Same delegation pattern. |
| `Filters\FilterLibrary.cs` | Add `LoadOrDefault(string path)` overload. `Save(string path)` already exists. |

Test classes:

| File | Coverage |
|------|----------|
| `SettingsStoreTests.cs` | missing-file → seed + save; Pattern C fill (null Roots, empty NamedLocations); "Custom" site strip; corrupt JSON → fallback seed; version mismatch → fallback |
| `LocalTargetStorePersistenceTests.cs` | round-trip 0/1/N targets; null DTO skipped; whitespace name skipped; corrupt JSON → empty list; missing file → empty list; signed-hemisphere preservation |
| `FilterLibraryPersistenceTests.cs` | Save→Load round-trip; missing-file → DefaultLibrary; corrupt-JSON → defaults + log; `MigrateLegacyFields` 0-CenterNm → builtin fill; user-renamed → 0 stays |

New helper: `Tests\Support\TempDirectory.cs` — `IDisposable` wrapping `Path.GetTempPath() + Guid` with recursive Dispose. Used by all three persistence test classes.

Estimated: ~26 tests + 3 refactors + 1 helper. One PR.

## Phase 3 — cache contract enforcement (shipped 2026-05-27)

Direct map from [`cache-contract.md`](cache-contract.md) invariants → test names. The cache-contract doc IS the test list.

`CacheAxisTests.cs` (synthetic key+value types, e.g. `string` → `string`, with a `Func` build delegate):
- Per-key dedupe: concurrent `GetOrBuildAsync(k)` → one build invocation
- Stale-publish discard: build started against `loc1`, `DrainAndReset` swaps to `loc2`, old build's publish → no entry
- `DrainAndReset` returns in-flight task list for caller drain
- Faulted build dropped from in-flight; next call starts fresh
- `PrepareAsync` ticks `IProgress<int>` per completion; surfaces faults via `WhenAll`
- Empty/null `keys` → no-op

`ChartCacheStoreTests.cs` (real Astronomy.Core types; `TestLocations` helper lands here):
- Lifecycle invariants (5): single-location, monotonic-growth, first-build-idempotence, stale-build-silent-discard, EnsureAsync-idempotent
- EnsureAsync diff matrix (5 rows): location → all axes drop; HdmKey → fits only; DayWindowKey → day+moon only; brightness → flag only; targets ref → render ticks
- Pessimistic work sizing: `EnsureWork == 0` on warm scrub; non-zero on stale
- `mLastEnsureCtx` CAS-style stamp: overlapping `EnsureAsync` doesn't flip flags in wrong order
- `SetLocationAsync` drains every axis (Count=0 after; old-location entries never surface)

New helper: `Tests\Support\TestLocations.cs` — duplicated from `Library\Astronomy.Core.Tests\Tests\TestLocations.cs` (4 static `Location` factories: PennsPark, Sydney, Equator, Reykjavik). Header comment notes the duplication. Duplication beats cross-repo coupling — the file is ~30 lines of immutable POCO factories.

`ChartCoordinator` is **out of Phase 3** — depends on `System.Windows.Forms.Timer` (requires message pump). If ever tested, needs an `ITimer` abstraction + DI seam. Deferred indefinitely.

Estimated: ~30 tests across 2 classes + 1 helper. One PR.

## Phase 4 — scanner / loader fixture-driven tests (shipped 2026-05-27)

| File | Coverage |
|------|----------|
| `TargetLoaderTests.cs` | 3–4 hand-curated `.json` fixtures: positive Dec, negative Dec (NegativeDec flag), sexagesimal RA assembly, missing fields, malformed JSON |
| `ImageLibraryLoaderTests.cs` | synthetic `.xisf` header fixtures generated per-test; IMAGETYP=Light gate; non-Light file skipped |
| `TargetScannerTests.cs` | recursive walk; mosaic Panel grouping; comet folder exclusion; centroid aggregation across 2 frames; unreadable-dir tolerance |

Fixture generation: `Tests\Support\SyntheticXisf.cs` — writes a minimal valid XISF (8-byte ASCII signature + 4-byte LE XML length + 4-byte reserved + UTF-8 XML payload, header-only, no image attachment block) with caller-supplied FITS keywords, adapted from `Library\Astronomy.XISF.Tests\XisfHeaderReaderTests.cs`'s `WriteSyntheticXisf` helper (cross-repo duplication, sync if either drifts). No `TestData\` folder and no `<Link>` to Library's `test.xisf` — each test builds its own fixture file on demand via `SyntheticXisf.Write` + `SyntheticXisf.LightFrameKeywords`.

Estimated: ~22 tests across 3 classes + 1 shared synthetic-fixture helper. One PR.

## What does NOT go in

- **`ChartCoordinator`** — `System.Windows.Forms.Timer` dependency. Deferred indefinitely.
- **MainForm + presenter partials, `Charts\AltitudeSubChart_*`, `OverlayController`, `EditFiltersForm`, `DiagnosticsDialog`** — WinForms message pump / LC2 SKControl paint required.
- **`Designer.cs`, Velopack, `Log.*`, `UpdateService`** — generated / external lifecycle / infrastructure.
- **Benchmarks** — defer until a TP-side hot path needs profiling (Library benchmarks cover the math hot paths).

## Build invocation

Canonical:

```
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64
```

Filter to a class (useful during phase development):

```
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~HdmKeyTests"
```

From Visual Studio: Test Explorer auto-discovers via `xunit.runner.visualstudio`.

Post-PCL.Native fallback (when global `dotnet build TargetPlanner.sln` breaks on the project graph):

```
msbuild "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -restore -p:Configuration=Debug -p:Platform=x64
dotnet test "TargetPlanner.Tests\TargetPlanner.Tests.csproj" -c Debug -p:Platform=x64 --no-build
```
