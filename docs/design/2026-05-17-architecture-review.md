# TargetPlanner — Architecture Review (2026-05-17)

Re-review of the codebase following the pipeline-collapse refactor and Local Horizon polyline work. Conducted with the same scope as prior rounds: Separation of Concerns, thread safety, maintainability, and roadmap readiness.

## Headline

The 2026-05-17 work is structurally a bigger deal than any prior round. Three changes stand out:

1. **`ChartCacheStore.EnsureAsync(ctx, dayKey) → ChartEvaluation`** is now the single pre-render seam. The coordinator's per-area diff table and three-way `Render` / `RefreshVisibility` / `ShowOnly` dispatch (~300 lines) collapsed into the cache.
2. **`TargetFitEntry.Tonight` + `MoonAltitudeEntry`** consolidate the two parallel fit paths and the two parallel moon-altitude paths flagged in the prior round. Day and Sky now read the same `NightFit.Tonight` and the same singleton `MoonAltitudeEntry` that the cache builds once.
3. **Local Horizon polyline (`HrzFileLoader` + `mLocalHorizon` + FileSystemWatcher hot-reload)** lands the PR-5 work end-to-end. `HdmKey.LocalHorizon` is reference-keyed; scalar profiles stay `null` to avoid cache thrash.

Most of last round's outstanding items closed. The new shape has one real concurrency edge and a handful of doc/code drift to clean up. The architecture is in better shape than I expected — the pipeline-collapse refactor moved staleness reasoning into the cache where it always belonged, and the `ChartEvaluation` type makes future short-circuit logic a record-field addition.

## What landed cleanly

**Single seam.** `ChartCacheStore.EnsureAsync` (`Caches/ChartCacheStore.cs:330`) is now the only place staleness lives. The coordinator pipeline (`State/ChartCoordinator.cs:151-215`) is `Interlocked.Increment` → compute `dayKey` from `NightCalculator.ComputeNight(ctx.Location)` → `await mCache.EnsureAsync(ctx, dayKey)` → generation guard → `mRenderActiveArea(ctx, eval)` → `mPostApplyHook(ctx)`. No conditional dispatch, no per-area stamping, no `mShowOnlyActiveArea` callback. This is the shape we'd hoped for; the doc comment in `ChartCoordinator.cs:22-28` ("Do not add side-paths…the straight-line shape is the SoC win") is the right ground rule.

**`dayKey` derived in the coordinator, not the cache.** The comment at `ChartCoordinator.cs:166-174` calls out exactly the right reason: reading `mCache.LocationNightCache` here would pick up the *previous* location's night on a date scrub (because `SetLocationAsync` inside `EnsureAsync` hasn't run yet), and the resulting stale dayKey would mismatch the sub-chart's post-SetLocation new dayKey — every `GetDayOrNull`/`GetMoonOrNull` would return null and targets would disappear. `NightCalculator.ComputeNight` is sub-ms; recomputing per pipeline is correct insurance. Good defensive engineering.

**The `Tonight` consolidation closes the prior "two parallel fit paths" finding.** `BuildFitEntryAsync` computes `(Nights[], Tonight)` in one `Task.Run` (`ChartCacheStore.cs:605-608`). Day's HD overlay reads `cache.GetFitOrNull(target, ctx.Hdm)?.Tonight`; Sky's hide-on-no-fit reads the same. Year and Sessions read `Nights[i]`. **One** library probe per (target, HdmKey), not three. The CLAUDE.md guarantee "Zero UI-thread `BestSession.For` / `AltAz.At` / `AstroUtil.GetMoonAltitude` calls in any cache-warm Render path" is accurate; the only remaining inline-Meeus paths are the defensive moon-cache-miss fallbacks at `AltitudeSubChart_Day.cs:835` and `AltitudeSubChart_Sky.cs:582`, both WARN-logged.

**`MoonAltitudeEntry`** as a singleton per `DayWindowKey` (`ChartCacheStore.cs:82-85`) is the moon analog of the Tonight consolidation — one minute-spaced moon altitude curve, shared by Day's underlay and Sky's overlay. `PrepareMoonAsync` fires unconditionally at `EnsureAsync:372-375` whenever `dayKey.Count > 0`, including when `ctx.Targets` is empty — so Day's startup render (no targets, before NINA load) hits a warm cache instead of triggering the WARN fallback. Nice touch.

**`HdmKey.LocalHorizon` reference-keyed**, scalar profiles stay `null` (`HdmKey.cs:46-53`, `ChartContext.cs:72`). The scalar-vs-polyline branch is at the snapshot edge: a `ScalarHorizonProfile` skips the key field entirely so every snapshot's fresh `WithScalarHorizon` factory doesn't thrash the cache, while a loaded `PolylineHorizonProfile` is referenced once per active site (cached in `MainForm.mLocalHorizon`). Cache thrash bug avoided cleanly.

**`Log.Diag` infrastructure** (`Support/Log.cs:95-106`) gives semi-permanent instrumentation that compiles to a single `IsDiagEnabled` check in Release. The `TP_DIAG="Coord,Cache,Day"` filter idiom is grep-friendly, and the `IsDiagEnabled` gate around expensive interpolations (`ChartCoordinator.cs:182`, `ChartCacheStore.cs:351`) prevents the string-build allocation in the disabled path. This is the right shape for "I might need to debug this six months from now."

**Form-CTS warmup is exactly the narrow form-only shape discussed in the prior follow-up.** Token captured at `MainForm.cs:1858`, `Task.WhenAny(warmup, cancelled)` race, `OperationCanceledException` swallowed. Cache surface remained CT-free. The comment at `MainForm.cs:152-156` is explicit about the c74224f cancellation-removal stance.

**`Location.Horizon` and `Location.Duration` are now `[Obsolete]`** — the `#pragma warning disable CS0618` at `MainForm.cs:1411-1426` localizes the transitional read into one place. `SortPresenter.cs:217` previously poked `mLocation.Horizon` directly; it now routes through `SnapshotCurrent().Policy.TargetFloorDeg`. Prior round's recommendation closed.

## Concurrency & SoC findings (new)

### `EnsureAsync` has a stamp race between concurrent pipelines

This is the one real concurrency edge in the new shape.

Trace: `OnDebounceTick` is async-void. It fires, calls `await RunPipelineAsync`, yields at the first `await`. The user keeps editing, the debounce timer restarts. The timer fires *again* while the first `RunPipelineAsync` is still suspended in `await mCache.EnsureAsync(...)`. The handler enters again, calls `Stop()`, reads pending, calls `await RunPipelineAsync` with the new context. **Two concurrent `RunPipelineAsync` calls now exist** — generations N and N+1.

Both call `EnsureAsync`. Both snapshot `prev = mLastEnsureCtx` under the lock at `ChartCacheStore.cs:338`. Both compute eval flags relative to the same prev. Both run their Prepare paths (idempotent per-key). Both reach the stamp at `ChartCacheStore.cs:400` — order of arrival is non-deterministic.

If N+1 finishes its awaits first (fast path: no location change, warm cache) and stamps `mLastEnsureCtx = ctxN+1`, then N finishes and stamps `mLastEnsureCtx = ctxN`. The cache now thinks ctxN is the latest. **The next** `EnsureAsync(ctxC)` diffs against the stale-stamp ctxN, computes the wrong eval flags, and stamps relative to it.

Today this is **benign**: sub-charts ignore the eval flags (Phase 7's short-circuit was shipped and reverted), and the actual cache *contents* are correct because every Prepare is idempotent per-key. The render is correctly serialized by `mGeneration` — only ctxN+1 renders. But the stamp drift is a latent trap for Phase 7's re-attempt or anyone who later wires consumer logic against `eval` flags.

**Fix shape (low cost):** the stamp at `:400` should be conditional. Under the same lock:

```csharp
lock (mGate)
{
    if (ReferenceEquals(mLastEnsureCtx, prev))
        mLastEnsureCtx = ctx;
    // else: a concurrent EnsureAsync stamped a newer ctx while we were
    // awaiting; their stamp wins and our diff was relative to a now-stale
    // prev. Don't clobber.
}
```

CAS-style — only stamp if the world hasn't moved. Same lock, one extra reference compare, no other changes required. Pair it with a comment naming the race so the next person doesn't strip the guard as "unnecessary."

`mLastEnsureCtx` is set inside `EnsureAsync` after the awaits but the prev is read at the top under a separate lock. That's the gap the race lives in. The proposed CAS closes it without serializing the awaits.

### `IAltitudeSubChart.RefreshVisibility` is dead surface but still on the interface — and one caller remains

`IAltitudeSubChart.cs:73` declares it, four sub-chart files implement it (Day at `:614`, Sky at `:441`, Year at `:345`, Sessions at `:380`), and three of those implementations have a comment saying "RefreshVisibility itself is going away in Phase 6." The lone caller is `MainForm.FilterMenuPresenter.cs:337-344` — post-EditFiltersDialog-Save, it loops `mSubCharts.Values` calling `sc.RefreshVisibility(refreshCtx, mCache)` instead of going through the coordinator. **This bypasses the supersession + cache-Ensure pipeline entirely** — the dialog-save path is the one place in the codebase that mutates chart state outside the single seam the new architecture is built around.

The right fix is one line: replace lines 337-344 with `mCoordinator?.Apply(SnapshotCurrent());`. Then delete `RefreshVisibility` from the interface and the four implementations. The dialog-save path becomes a normal coordinator-routed apply, picks up generation supersession, eval flags, etc.

### Doc-vs-code drift in CLAUDE.md

Line 64 says: "Form-lifecycle cancellation is the one CTS (`mFormClosingCts` on MainForm, **observed by** `PrepareManyAsync` / `PrepareFitsAsync` / `PrepareDayAsync` for clean shutdown only)." But the cache surface is CT-free — `MainForm.cs:152-156` is explicit ("The cache itself doesn't observe this token") and the actual `IChartCacheStore` signatures take no `CancellationToken` parameter. The CT is observed at the *outer* `Task.WhenAny` boundary in `MainForm.cs:1869`. Doc reads as if the cache observes the token; it doesn't. One-line fix.

### `ChartEvaluation` is documented as a "typed enforcement mechanism" but every flag is currently unread

`ChartEvaluation.cs:13-18` and `ChartCoordinator.cs:200-203` both reference Phase 7's planned short-circuit; the comments in `AltitudeSubChart_Day.cs:366-373` document the revert (LC2 paint instability across hidden→visible transitions). This is fine as transitional shape, but the gap between the doc ("typed enforcement") and the reality ("currently aspirational") matters for the next person reading. Either:

- Add a one-line "**Status:** populated by the cache, unread by render paths as of 2026-05-17. Phase 7 wiring deferred." to the `ChartEvaluation` doc comment, or
- Wire one obvious short-circuit (e.g. Sky's K-S walk on `BrightnessInputsChanged == false`) so the contract is exercised by at least one consumer.

The second option is better long-term — exercised contracts don't bit-rot — but the first is honest about today.

### Dead `TargetReady` / `LocationChanged` events still un-subscribed

Both `IChartCacheStore.cs:160` and `:167` declare them; `ChartCacheStore.cs:763` (FireTargetReady) and `:772` (FireLocationChanged) still allocate `EventArgs` and call `mUiContext.Post`. Handler-null check at `:766` keeps this cheap, but the public surface continues to advertise a contract nobody listens to. Same recommendation as last round: delete them. The cache invariant ("an awaited `*OrBuildAsync` returns a published entry") is the only signal callers need.

## Maintainability findings (new and persistent)

### `TryPublish` factoring still not done; the duplication count grew to five

The "lock + `ReferenceEquals(mLocation, location)` check + publish-or-discard + on-fault dict cleanup" pattern is now in:

- `BuildEntryAsync` (`:558-565` success, `:576-580` fault)
- `BuildFitEntryAsync` (`:620-627` success, `:632-636` fault)
- `BuildDayEntryAsync` (`:657-664` success, `:669-673` fault)
- `BuildMoonEntryAsync` (`:707-714` success, `:719-723` fault)
- `EnsureNightCacheAsync.ContinueWith` (`:751-755`, success-only)

Five copies of "lock-check-publish-discard"; five copies of "lock-check-remove-on-fault." Same shape; subtly different per-case (one updates `mEntries`, another `mFits`, etc.). The next axis (SessionSolvers per CLAUDE.md's open roadmap?) will be a sixth. Static helpers:

```csharp
private bool TryPublish<TKey, TVal>(Dictionary<TKey, TVal> store,
    Dictionary<TKey, Task<TVal>> inFlight, TKey key, TVal value, Location buildLocation)
{
    lock (mGate)
    {
        if (!ReferenceEquals(mLocation, buildLocation)) return false;
        store[key] = value;
        inFlight.Remove(key);
        return true;
    }
}

private void DropOnFault<TKey, TVal>(Dictionary<TKey, Task<TVal>> inFlight,
    TKey key, Location buildLocation)
{
    lock (mGate)
    {
        if (ReferenceEquals(mLocation, buildLocation)) inFlight.Remove(key);
    }
}
```

Each build method shrinks to its async body + two call sites. The next person who copy-pastes the pattern for SessionSolvers won't subtly invert the order.

### `MainForm_FormClosing` doesn't dispose two timers

`mFilterAutoSaveDebounce` (`MainForm.cs:194`) and `mCheckedToggleDebounce` (`:344`) are both `Stop()`'d at various sites but never `Dispose()`'d. Native HWND-backed `System.Windows.Forms.Timer` leaks the handle until process exit; not load-bearing (the process exits seconds later anyway), but inconsistent with the other timer disposes at `:509, 513`. Add two lines to `FormClosing`.

### `Log.Append` synchronously file-writes under `sGate`

`Log.cs:158-174`. Under `TP_DIAG=*` the threadpool cache builds and the UI-thread coordinator both call `Append` per-message; the lock serializes them, the `File.AppendAllText` is a sync disk write per call. On a fast SSD this is invisible; on slow storage or under heavy diag load it could throttle the cache builds (since `BuildFitEntryAsync.Log.Diag` runs on a threadpool thread). Not a problem at observed volumes. If diag instrumentation grows much, swap to a bounded `BlockingCollection<string>` + background drain thread. Defer until perf complaints surface — current shape is correct, just synchronous.

### Sub-chart `mLastTargets` fields are scaffolding for a short-circuit that was reverted

CLAUDE.md line 29 mentions "`mLastTargets` fields on sub-charts are scaffolding for a future smarter short-circuit; the flags are populated by the cache and currently unread by render paths." Either delete the fields (and the assignment sites) until they're needed, or wire one consumer so the scaffolding is exercised. Same logic as the `ChartEvaluation` recommendation above.

### `SetLocationAsync` awaits 5 stale-task collections sequentially

`ChartCacheStore.cs:508-534`. Each is a `foreach` with `try/await/catch Log.Warn`. The pattern is correct but increasingly verbose. Could be `await Task.WhenAll(allStaleTasks.Select(t => SilentAwait(t)))` with a single helper — cosmetic, not load-bearing.

## Summary

The pipeline-collapse refactor moved the architecture in a meaningfully better direction. The cache now owns staleness reasoning, the coordinator is a thin funnel, sub-charts are render-only and read from cache entries that are byte-identical across the four chart areas. The Local Horizon polyline path landed end-to-end, with hot-reload via FileSystemWatcher. `Location.Horizon` / `.Duration` are formally deprecated.

The two items worth landing in a small follow-up:

1. **Fix the `EnsureAsync` stamp race** with a CAS-style guard on the stamp (one lock, one reference compare). Today benign; trap for any future flag-consuming consumer.
2. **Route `FilterMenuPresenter.OpenEditFiltersDialog`'s post-save refresh through the coordinator** and delete `RefreshVisibility` from the interface + four sub-chart implementations. Closes the single bypass of the single-seam pipeline.

Both small enough to land together. After those, the `ChartEvaluation` flags can be wired to one obvious consumer (Sky's K-S brightness re-walk on `BrightnessInputsChanged`) to keep the typed-staleness contract exercised by at least one consumer — that's the cheapest way to prevent the type from rotting between now and Phase 7.

Everything else (dead events, `TryPublish` factoring, the two timer disposes, the doc drift) is cleanup, not architecture.
