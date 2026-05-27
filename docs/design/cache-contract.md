# Cache contract

The stable invariants `IChartCacheStore` promises to callers — what future implementors and re-readers can rely on without re-reading the implementation. Companion to [chart-fits-cache.md](chart-fits-cache.md) (the design rationale that produced the fits axis) and ARCHITECTURE.md §Cache store (the narrative deep-dive); duplication is intentional where the contract needs to stand alone.

Closing finding §3.A of [`2026-05-26-code-review-async-ui.md`](2026-05-26-code-review-async-ui.md) — the no-CT design is documented at the API boundary rather than buried in remarks on the concrete type.

## Purpose

Per-Location, in-memory cache of every per-target precompute the four chart sub-areas (Day / Sky / Year / Sessions) read on Render. The cache hides three things from the sub-charts:

1. **What's needed to render right now.** Sub-charts call `Get*OrNull` and skip the target if it returns null — they never call `BestSession.For`, `AltAz.At`, or `AstroUtil.GetMoonAltitude` in any cache-warm path.
2. **What changed since last render.** `EnsureAsync` is the single staleness gate; it diffs the incoming `ChartContext` against the last-applied snapshot and rebuilds only the axes whose inputs moved.
3. **Concurrency.** Per-key in-flight dedupe, lock-protected read accessors, threadpool compute.

The cache is **not** a brightness model (Sky's K-S walk runs inline per filter / Bortle scrub — different cadence), **not** a persistence layer (memory-only, rebuilt on app restart), and **not** a Library facility (TP owns its UX caching; `Astronomy.Core` stays consumer-agnostic).

## Public surface

Four cache axes, each a `(key) → value` store:

| Axis     | Key                      | Value                                              | Invalidated on                           |
|----------+--------------------------+----------------------------------------------------+------------------------------------------|
| yearDays | `Target`                 | 365 × `NightCacheEntry`                            | `SetLocationAsync`                       |
| fits     | `(Target, HdmKey)`       | `TargetFitEntry` (year array + Tonight slot)       | `SetLocationAsync` or new `HdmKey`       |
| day      | `(Target, DayWindowKey)` | `TargetDayAltitudeEntry` (minute-spaced altitudes) | `SetLocationAsync` or new `DayWindowKey` |
| moon     | `DayWindowKey`           | `MoonAltitudeEntry` (singleton per night)          | `SetLocationAsync` or new `DayWindowKey` |

Per-axis methods:
- `GetXxxOrNull(key)` — sync read; lock-protected; `null` ⇒ "not yet built" (caller skips the target). Safe on the UI thread on every Render.
- `PrepareXxxAsync(keys, IProgress<int>?)` — fan out builds; await all completions; ticks progress per build-completed.

Pipeline + lifecycle methods:
- `EnsureAsync(ctx, dayKey, IProgress<(int,int)>?)` — single pre-render entry point; returns `ChartEvaluation`. See §EnsureAsync semantics.
- `SetLocationAsync(loc, startingUtc)` — drains every axis, swaps in the new location + UTC anchor, awaits in-flight stale tasks.

Observation properties:
- `CurrentLocation` — the location all entries are keyed against; lock-protected read.
- `LocationNightCache` — published per-location `NightCache`; `null` until the first build for the active location completes.

## Lifecycle invariants

1. **Single-location at a time.** Every published entry is keyed implicitly against `CurrentLocation`. A `SetLocationAsync` drops every entry across every axis atomically (one lock acquisition) before the new location is observable.
2. **Monotonic growth within a location.** Entries don't get evicted while the location is stable — H/D/M scrubs accumulate `HdmKey`s in the fits axis; date scrubs accumulate `DayWindowKey`s in day/moon. Scrub-up-then-scrub-back is free after the first crossing.
3. **First-build idempotence.** Within a location, `PrepareXxxAsync(...)` called twice with the same keys joins the existing in-flight task on the second call; it never double-computes.
4. **Stale-build silent discard.** A build started against an old location runs to completion on the threadpool, then drops its result at publish via `ReferenceEquals(currentLocation, buildLocation)`. Callers see nothing.
5. **EnsureAsync is idempotent.** `EnsureAsync(ctx, dayKey)` called twice with the same `(ctx, dayKey)` settles in every axis's per-key fast paths on the second call.

## Threading and cancellation

**Read accessors (`GetXxxOrNull`, `CurrentLocation`, `LocationNightCache`)** are synchronous, lock-protected, safe from any thread. The intended caller is the UI thread on every sub-chart `Render`.

**Prepare and Ensure methods** are internally `Task.Run`-backed; safe to call from the UI thread (returned Task is what the coordinator awaits). No external synchronization required: concurrent `EnsureAsync` invocations from overlapping coordinator pipelines are safe, because each per-axis Prepare path is itself idempotent + per-key de-duped, and the `mLastEnsureCtx` stamp is CAS-style (race-loser leaves the older stamp in place, next pipeline's eval flags are conservative — extra work, never wrong work).

**The cache does not accept `CancellationToken` parameters anywhere on its public surface.** This is intentional, not an oversight. Promises:

- **Callers cannot cancel in-flight work.** A `SetLocationAsync` that drops every cache entry does *not* interrupt the threadpool builds that are still running against the old location; they finish and discard their results at publish.
- **Form-lifecycle cancellation lives outside the cache.** `MainForm.mFormClosingCts` is observed only at the outer `Task.WhenAny(warmupTask, cancelledTask)` race in `StartCacheWarmup` — the cache awaits themselves never observe a token.
- **Supersession is via the location-identity check**, not via cancellation. The cache axis only publishes when `ReferenceEquals(currentLocation, buildLocation)` still holds.

Rationale (`ChartCacheStore.cs:29–37`): full-44-target rebuild is ~1–2 sec on the post-CS-removal Meeus path. Wasted CPU during a supersession is bounded and small; the simplification is real — no per-await CT plumbing, no `OperationCanceledException` triage, no token lifecycle management.

Constraints — when to revisit:

- **Per-rebuild compute must stay bounded.** The no-CT design degrades gracefully only as long as overlapping rebuilds can't pile up CPU faster than the user can scrub. CLAUDE.md's "2–4 sec / 44 targets" perf budget is the explicit ceiling; a future axis whose worst-case approaches it warrants re-examining the contract (per-target XISF preview generation and per-night brightness simulation are the realistic candidates).
- **`Dispose()` must stay benign.** Today a no-op. A future implementation that grew real cleanup (closing handles, flushing to disk) would race orphaned in-flight builds writing into a disposed store — and would need to add either a CT pathway or an `IsDisposed`-gated publish.

## EnsureAsync semantics

**Single pre-render entry point.** Diffs `ctx` against the last-applied snapshot under one lock, runs every axis whose inputs changed, returns a `ChartEvaluation` describing what downstream consumers should react to.

Inputs:
- `ctx: ChartContext` — full immutable snapshot of pipeline inputs (Location / Targets / Policy / Observation / ActiveArea / TargetColors / DayMode).
- `dayKey: DayWindowKey` — the Day chart's minute-spaced window for the active night. Pass `default(DayWindowKey)` (`Count == 0`) to skip Day/Moon prep on polar nights or empty-targets boots.
- `progress: IProgress<(int Done, int Total)>?` — optional ticks for combined prep + render work. The cache sizes `Total` from its staleness diff (pessimistic upper bound: `yearWork + fitWork + dayWork + moonWork + renderWork`) and ticks `Done` per axis-completion. **When ensure-work is zero, no Report fires** — a warm-cache scrub never surfaces the progress UI.

Output (`ChartEvaluation`):
- `BrightnessInputsChanged` — Bortle / ExtinctionK / `ActiveFilter` moved since last Apply. Coordinator's post-apply hook gates the Sky K-S re-walk on this.
- `EnsureWork` / `RenderWork` — pessimistic tick counts; coordinator uses `EnsureWork == 0` as the warm-cache gate.

Diff scope:

| Change in `ctx`                                                                                  | Cache effect                                                                          |
|--------------------------------------------------------------------------------------------------+---------------------------------------------------------------------------------------|
| Location geometry (lat / lon / N / W / elev)                                                     | `SetLocationAsync` — every axis drops                                                 |
| Date anchor (`Observation.Utc.Date` or year-start-day)                                           | `SetLocationAsync` — NightCache.Starting + YearStartDay both depend on the UTC anchor |
| `HdmKey` change (TargetFloor / MinDuration / ActiveFilter / MoonAvoidanceEnabled / LocalHorizon) | fits axis rebuild only; year + day preserved                                          |
| `DayWindowKey` change (date / dusk-dawn window)                                                  | day + moon axes rebuild; year + fits preserved                                        |
| Brightness inputs (BortleClass / ExtinctionK / ActiveFilter)                                     | `BrightnessInputsChanged = true`; no axis flips (Sky K-S walks inline)                |
| `Targets` reference change                                                                       | Render ticks; cache is keyed per-target so the set diff is implicit                   |

## Caller obligations

Must not:
- Mutate `ChartContext` after passing it to `EnsureAsync` — it's an immutable snapshot (the `record` shape enforces this).
- Treat `GetXxxOrNull → null` as a wait condition. Null means "skip this target this Render"; the next Render re-queries.
- Call cache methods from inside a build delegate (would re-enter `mGate`); the cache itself only does this from `EnsureAsync` via documented paths.

Free to:
- Call `Get*OrNull` from the UI thread on every Render. The lock is contended only by the cache's own publishes.
- Have overlapping `EnsureAsync` calls in flight (coordinator does this routinely during rapid scrubs).
- Construct fresh `Location` / `Target` / `HdmKey` instances per call as long as value-equivalence holds — `LocationCacheEquivalent` / `HdmKey.Equals` / `DayWindowKey.Equals` absorb reference churn.
- Pass `null` for any optional progress sink.

## What's intentionally NOT in the contract

- **The four-axis internal shape.** Future implementations are free to merge axes, add new axes (e.g. an XISF preview axis when that lands), or change build cadence — as long as the per-axis `Get*OrNull` / `Prepare*Async` surface stays.
- **Eviction policy.** Today: monotonic within a Location. A future implementation could add LRU eviction across HdmKeys without breaking callers.
- **Disk persistence.** Memory-only; rebuild on app restart. Persistence would be additive without breaking the contract.
- **In-flight build counts / parallelism.** Threadpool concurrency is the implementation's call; sub-charts must not assume a single in-flight build per axis.
- **`ChartEvaluation` field set.** Used today only for the post-apply hook's Sky-K-S gate plus progress sizing; if a future consumer wants to react to other diff axes, add fields to `ChartEvaluation` rather than peeking at internal cache flags.
