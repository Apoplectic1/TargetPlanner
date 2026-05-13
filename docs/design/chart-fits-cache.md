# Chart fits cache — completion of the SoC refactor

## Context

The original SoC refactor (commits `0f6c81c` / `1e1986d` / `3425f8e`) decoupled chart construction from cache construction, decoupled cache state from selection state, and decoupled selection state from UI controls. But it left one concern misallocated: the per-(target, night) **fit decision** (`BestSession.ResolveCandidates` + `PlaceBest` + `PlaceCentered` + `SessionAltitude.{Floor, Ceiling}`) still lived in the Year and Sessions sub-charts, each running its own `Task.Run` with a `CancellationTokenSource` to handle scrubs and supersession.

That misallocation surfaced as a reproducible "Sessions tab shows no curves" bug:
1. `Button_SelectAllTargets` → Day shows curves (fast — Day samples altitudes directly).
2. `RadioButton_Sessions` → empty (Sessions kicks its bg fit task; ~10 sec wall time for 44 targets).
3. `RadioButton_Year` → curves (Year kicks its own bg task too, finishes faster).
4. `RadioButton_Sessions` → still empty (in the meantime, `ChartCoordinator.RunPipelineAsync`'s inactives `RefreshVisibility` loop had cancelled and restarted Sessions's bg task; user clicked back before it finished).

The user's stated architectural intent: *"All charts are constructed with previously constructed data. H/D/M scrubs are an exception. Charts are effectively render-only. Separation of concerns is the overall goal."*

This document is the design rationale for the cache-completion pass that lifts fit compute out of the sub-charts and into `ChartCacheStore`, alongside a cancellation-removal sweep.

## Strategy

1. **Lift fit compute into `ChartCacheStore`** alongside yearDays. Cache stores `mFits: Dictionary<(Target, HdmKey), TargetFitEntry>`. Sub-charts call `cache.GetFitOrNull(target, hdmKey)` and paint synchronously.
2. **Remove cancellation infrastructure**: post-CS-removal, compute is fast enough that the elaborate CTS plumbing is overkill. Generation-counter supersession on the coordinator side; publish-time stale check on the cache side.

In-flight builds whose inputs are superseded (e.g. a Location changed mid-yearDays-build) run to completion and either publish-and-get-discarded or land in a cache slot whose key is no longer the current one. CPU waste is small (~1-2 sec for 44 targets) and bounded because builds dedupe per-key. The simpler code path is worth the trade.

## Architecture

### Types

```csharp
// State/HdmKey.cs
public readonly struct HdmKey : IEquatable<HdmKey>
{
    public double HorizonDeg { get; init; }
    public long DurationTicks { get; init; }
    public MoonAvoidanceProfile Profile { get; init; }  // reference identity
    public double FilterCenterNm { get; init; }
}
```

`MoonAvoidanceProfile` is immutable; reference identity is the cheap-equality fast path. Bortle / ExtinctionK are intentionally excluded — they affect Sky's K-S brightness, not fit decisions.

```csharp
// Caches/TargetFitEntry.cs
public sealed class TargetFitEntry
{
    public Target Target { get; }
    public HdmKey Key { get; }
    public IReadOnlyList<NightFit> Nights { get; }  // index-aligned with YearDays
}

public readonly struct NightFit
{
    public double? Ceiling { get; init; }
    public double? Floor { get; init; }
    public double? CenteredFloor { get; init; }
}
```

One `NightFit` per (target, night) carries everything both Year and Sessions need. Year reads `Floor`; Sessions reads all three. They share the upstream `BestSession.ResolveCandidates` resolve, so a single pass computes all three fields per night with one resolve + two placements + three altitude calls. Net Meeus calls per night drop ~25% vs the previous separate-paths model.

### `ChartCacheStore` extension

```csharp
TargetFitEntry GetFitOrNull(Target t, HdmKey key);
Task<TargetFitEntry> GetFitOrBuildAsync(Target t, HdmKey key);
Task PrepareFitsAsync(IEnumerable<Target> targets, HdmKey key,
                      IProgress<int> progress = null);
```

Mirrors the existing yearDays surface:
- `mFits: Dictionary<(Target, HdmKey), TargetFitEntry>` and `mInFlightFits: Dictionary<(Target, HdmKey), Task<TargetFitEntry>>` under the existing `mGate`.
- `BuildFitEntryAsync(target, key, location)` reads `mEntries[target].YearDays` (already published — `PrepareFitsAsync` awaits `GetOrBuildAsync` for the yearDays first), then runs `ComputeNightFits` on the threadpool.
- `Task.Run` per (target, HdmKey). Library is lock-free; parallel-across-cores follows naturally.
- `SetLocationAsync` clears `mFits` + `mInFlightFits` alongside the yearDays state.
- No HDM-driven cancellation. In-flight fit builds for an old `HdmKey` run to completion and publish under that key (cached for free if the user scrubs back).

### `ChartContext.Hdm`

```csharp
public HdmKey Hdm => new HdmKey {
    HorizonDeg     = Location.Horizon,
    DurationTicks  = Location.Duration.Ticks,
    Profile        = MoonProfile,
    FilterCenterNm = ActiveFilterCenterNm,
};
```

Derived property; existing call sites unchanged.

### Coordinator pipeline

```csharp
private async Task RunPipelineAsync(ChartContext ctx, IProgress<int> progress)
{
    int gen = ++mGeneration;
    // ...diff: locationKeyChanged, targetsChanged, hdmKeyChanged...

    try
    {
        if (locationKeyChanged) await mCache.SetLocationAsync(ctx.Location);
        if (ctx.Targets is { Count: > 0 })
        {
            await mCache.PrepareManyAsync(ctx.Targets, progress);
            await mCache.PrepareFitsAsync(ctx.Targets, ctx.Hdm, progress);
        }
    }
    catch (Exception ex) { Log.Error(...); return; }

    if (gen != mGeneration) return;                       // superseded; bail

    bool activeNeedsFullRender = !activeEverRendered
        || locationKeyChanged || targetsChanged || hdmKeyChanged;
    if (activeNeedsFullRender) mRenderActiveArea(ctx);
    else mShowOnlyActiveArea(ctx);

    mPostApplyHook?.Invoke(ctx);
    // ...stamp mLastAppliedByArea...
}
```

Deleted from the pre-refactor coordinator: `mPipelineCts`, `SupersedeAndRunAsync`, `OperationCanceledException` catches, the foreach-inactives `RefreshVisibility` loop, the separate `hdmChanged` arm, and the `HdmChanged` helper. Multiple pipelines may overlap during rapid scrubs; the generation guard ensures only the latest writes Render state. The cache de-dupes the heavy compute under the hood.

### Sub-charts (Year and Sessions)

`Render(ctx, cache)` walks `ctx.Targets`, reads `cache.GetOrNull(target)` for yearDays and `cache.GetFitOrNull(target, ctx.Hdm)` for fits, and paints synchronously. No `Task.Run`, no `BeginInvoke`, no `CancellationTokenSource`. Tooltips format on hover from the cached `NightFit` plus the index-matched `yearDays[i].SentinelX` — no pre-formatted `~16k strings × 44 targets` upfront.

`RefreshVisibility(ctx, cache) => Render(ctx, cache);` keeps the contract shape uniform across sub-charts (Day and Sky have legitimately different RefreshVisibility bodies for fit-tonight filter and K-S walk respectively).

### Cancellation footprint

After the pass:

- **Removed**: `ChartCoordinator.mPipelineCts`, `ChartCacheStore.mLocationCts`, `ChartCacheStore.WithExternalCancel`, `AltitudeSubChart_{Year,Sessions}.mVisibilityCts`, `CancellationToken` parameters on `IChartCacheStore` / `IAltitudeSubChart.Render` / `MainForm.RenderArea`, every `ct.ThrowIfCancellationRequested()` call in the chart pipeline files, the `Button_Cancel` button plus its handler and `mGraphBuildInProgress` gate, the `mCachePrepCts` warmup token.
- **Kept**: `MainForm.mCheckedToggleDebounce` and `mSessionsRebuildDebounce` (UI debounces), `ChartCacheStore`'s publish-time `ReferenceEquals(mLocation, location)` stale check (no token needed, just a value compare under `mGate`).

### Eager fits pre-pop after NINA load

```csharp
HdmKey hdm = SnapshotCurrent(allLoaded).Hdm;
_ = Task.Run(async () =>
{
    await mCache.PrepareManyAsync(allLoaded);
    await mCache.PrepareFitsAsync(allLoaded, hdm);
});
```

`PrepareManyAsync` and `PrepareFitsAsync` share dedupe with the lazy path (the coordinator's pipeline awaits the same `mInFlight` / `mInFlightFits` tasks). If the user clicks Graph before the eager pass finishes, the lazy await transparently joins the in-flight task and surfaces a progress bar for whatever remains; if they wait, the first Sessions / Year click is instant.

### Eviction

None. `mFits` grows monotonically within a Location; cleared on `SetLocationAsync`. Per-entry size ~12 KB (4 nullable doubles × 365 nights), so 44 targets × 50 distinct `HdmKey` values worst case ≈ 26 MB — fine. The "scrub Horizon up, then back down" workflow becomes free once the second key is cached.

## Bug closure

Original repro `invoke → SelectAll → Sessions → Year → Sessions` after the refactor:

| Step | Cache state | What runs | User sees |
| --- | --- | --- | --- |
| NINA load completes | (in flight) | Task.Run → PrepareManyAsync (~1-2 s) → PrepareFitsAsync (~2-8 s, silent) | empty chart |
| SelectAll (eager done) | warm | both prepares no-op → Day.Render synchronous | Day curves immediately |
| SelectAll (eager mid-flight) | partial | both prepares await shared in-flight tasks → progress bar → Day.Render | progress → Day curves |
| Sessions | warm | gen guard passes → Sessions.Render synchronous from cache | instant curves ← bug fixed |
| Year | warm | same — instant | instant curves |
| Sessions (second click) | warm | path C (showOnly) — no diff change | instant |
| Horizon scrub | fits stale; yearDays unchanged | PrepareFitsAsync for new HdmKey (~2-8 s) → re-render | progress → new curves |
| Horizon scrub back | fits@H₀ still in cache | both prepares no-op → re-render | instant |

## Out of scope

- Day's per-target tonight `BestSession.For` filter call. ~10 ms total for 44 targets; not worth its own cache key.
- Sky's K-S brightness compute. Different inputs (Bortle / ExtinctionK / Filter, not H/D/M); separate refactor candidate.
- Library-side fit cache (`Astronomy.Core`). TP owns its UX caching per the consumer-agnostic Library stance.
- Disk persistence of fits across app restarts. Memory-only; rebuild on startup.
- Promotion of the cache pattern to a shared `Astronomy.Charts` library — see memory note `project_charts_library_future.md`.
