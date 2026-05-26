# Code Review — UI-Driven Async (TargetPlanner)

**Reviewer focus:** UI responsiveness, thread safety, async correctness, SoC.
**Scope:** TargetPlanner WinForms project; library-side `Astronomy.*` projects in scope only where TP consumes them.
**Date:** 2026-05-26
**Severity scale:** 🔴 high (correctness / deadlock / crash) · 🟡 medium (perf / latent footgun) · 🟢 low (style / future-proofing)

---

## Executive summary

The project is in unusually good shape for an async-heavy WinForms app. The orchestration architecture (`ChartCoordinator` → `IChartCacheStore.EnsureAsync` → `IAltitudeSubChart.Render`) is well-thought-out, with explicit decisions around supersedence (monotonic `mGeneration`), per-axis in-flight dedupe (`CacheAxis<TKey,TVal>`), and stale-build discard (`ReferenceEquals(mLocation, buildLocation)`). There are no `.Wait()` / `.GetAwaiter().GetResult()` deadlock vectors on async work, `Progress<T>` is used correctly for marshalling, and a `CancellationTokenSource` exists at the form-lifecycle boundary.

The findings below are mostly about *consistency* — the same defensive patterns aren't applied uniformly across every entry point — plus a handful of micro-issues (sync file I/O on the UI thread, missing `ConfigureAwait(false)` in cache internals, an unwatched `Task.Delay().ContinueWith()` continuation). There are no urgent correctness defects in the async surface.

The single most leveraged change is **standardising the `async void` event-handler contract** (§2.A). It removes a real crash class with a one-line per-handler edit.

---

## 1. UI Responsiveness & Deadlocks

### 1.A 🟡 Cache internals don't use `ConfigureAwait(false)`

**Locations:**
- `TargetPlanner/Caches/ChartCacheStore.cs:234` (`await SetLocationAsync(...)`)
- `TargetPlanner/Caches/ChartCacheStore.cs:244` (`await PrepareMoonAsync(...)`)
- `TargetPlanner/Caches/ChartCacheStore.cs:258–266` (the three `PrepareXxxAsync` awaits)
- `TargetPlanner/Caches/ChartCacheStore.cs:462` (`await Task.WhenAll(staleAwaits)`)
- `TargetPlanner/Caches/ChartCacheStore.cs:483, 513` (`await EnsureNightCacheAsync(...)`)
- `TargetPlanner/Caches/CacheAxis.cs:69` (`await mBuild(...)`)
- `TargetPlanner/Caches/CacheAxis.cs:129` (`await Task.WhenAll(tasks)` in `PrepareAsync`)
- `TargetPlanner/Caches/ChartCacheStore.cs:440–443` (the nested `SafeAwait` helper)

**Risk:** Every cache await currently resumes on the captured `SynchronizationContext` — i.e. the UI thread. For a fully cache-warm `EnsureAsync` that's six SyncContext round-trips of essentially no-op continuations queued on the UI message pump. Cumulatively this nudges scrub debounce latency upward and adds head-of-line blocking pressure on the UI pump precisely when the user is scrubbing.

There's no current *deadlock* vector — nothing in this code path calls `.Wait()` / `.Result` on these tasks — but the discipline is load-bearing for future maintenance: the first time a caller blocks (e.g. an exit-time `Cache.FlushAsync().Wait()` for hygiene), the lack of `ConfigureAwait(false)` becomes a classic single-threaded-context deadlock.

**Counter-argument:** the cache's *consumers* (Render callbacks, `PushSkyKSInputs`, label updates) genuinely need to land on the UI thread. That's fine — they're hooked from `RunPipelineAsync` *after* `EnsureAsync` returns. `Progress<T>` (`MainForm.cs:1434`) likewise captures `SynchronizationContext.Current` at construction time, so adding `ConfigureAwait(false)` to cache awaits doesn't affect Progress marshalling.

**Recommendation:** Add `.ConfigureAwait(false)` to every internal await in `ChartCacheStore.cs` and `CacheAxis.cs`. The pattern is already correctly applied in `TargetScanner.cs:82,91,101,122,125,149,172,174` and `ImageLibrary/ImageLibraryLoader.cs:42` — extend the same convention to the cache. Library-layer code should never marshal back to the UI unless the caller explicitly resumes there.

### 1.B 🟢 `.Result` access inside `ContinueWith`

**Location:** `TargetPlanner/Caches/ChartCacheStore.cs:601`

```csharp
mNightCacheTask = task.ContinueWith(t =>
{
    if (t.IsFaulted || t.IsCanceled) return null;
    NightCache nc = t.Result;        // ← .Result use
    ...
}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
```

**Risk:** Minimal. The `IsFaulted/IsCanceled` guard before `.Result` ensures it won't throw, and `ExecuteSynchronously` on `TaskScheduler.Default` means we're on the threadpool, not the UI sync context. So no deadlock surface.

But `.Result` is a known code-smell that future readers will flinch at. Prefer the explicit shape:

```csharp
NightCache nc = t.GetAwaiter().GetResult();
```

or simpler, rewrite the continuation as an async local function:

```csharp
mNightCacheTask = AwaitAndPublishAsync(task, location);

async Task<NightCache> AwaitAndPublishAsync(Task<NightCache> t, Location loc)
{
    NightCache nc;
    try { nc = await t.ConfigureAwait(false); }
    catch { return null; }
    lock (mGate)
    {
        if (ReferenceEquals(mLocation, loc)) mNightCache = nc;
    }
    return nc;
}
```

That removes the `ContinueWith` ceremony entirely and folds the IsFaulted handling into the natural `try/catch`.

### 1.C 🟡 Synchronous file I/O on the UI thread

**Locations:**
- `Settings/SettingsStore.cs:42` (`File.ReadAllText` from `MainForm` boot path)
- `Settings/SettingsStore.cs:Save` is called from many UI-thread callsites: `SessionsRebuildDebounce_Tick`, `ComboBox_Location_SelectionIndexChanged:344`, `Button_BrowseHorizon_Click:596`, `HandleEditDefaultsClick:964`, `FormClosing:546`.
- `Horizons/HrzFileLoader.Load` is called from UI handlers: `LocationPresenter.cs:406,543,598`, plus indirectly via `LoadLocalHorizonForCurrentLocation()` at boot.
- `Settings/LocalTargetStore.Load/Save` from constructor + add/remove handlers.

**Risk:** Today these files are small (kilobytes), so the blocking is sub-frame. But settings.json + filters.json fan out per Bortle change, per location pick, per filter scrub auto-save. If the user has a slow disk (HDD, network share, AV scanning) the cumulative blocking is felt as UI hitches during scrubs.

**Recommendation:** Not urgent. When you next touch the settings store, consider async variants (`File.WriteAllTextAsync`, `JsonSerializer.SerializeAsync(stream)`) and a small debounce around `Save()` so the high-frequency callers (Bortle, Extinction, Filter scrubs) coalesce. The Bortle/Extinction path already settles via `SessionsRebuildDebounce_Tick` so the natural fix is to move `SettingsStore.Save` calls into the trailing-edge tick rather than into every individual edit handler.

---

## 2. Async Patterns & State Management

### 2.A 🔴 Inconsistent try/catch wrapping on `async void` handlers

This is the most actionable finding in the review.

The author has correctly recognised that `async void` exceptions escape into the WinForms unhandled-exception filter and crash the process, and has wrapped most handlers defensively:

| Handler | File:Line | Wrapped? |
|---|---|---|
| `Button_Graph_Click` | `ChartBuildPresenter.cs:66` | ✅ |
| `CheckedToggleDebounce_Tick` | `ChartBuildPresenter.cs:158` | ✅ |
| `SessionsRebuildDebounce_Tick` | `LocationPresenter.cs:213` | ✅ |
| `OnDebounceTick` | `ChartCoordinator.cs:136` | ✅ |
| `HorizonReloadDebounce_Tick` | `LocationPresenter.cs:536` | ✅ (synchronous body, but try/catch present) |
| **`Button_CheckedTargets_Click`** | `ChartBuildPresenter.cs:178` | ❌ |
| **`OnCheckUpdatesClick`** | `MainForm.cs:891` | ❌ |
| **`ComboBox_Location_SelectionIndexChanged`** | `LocationPresenter.cs:304` | ❌ |
| **`OnTargetListDragDrop`** | `TargetLoadingPresenter.cs:139` | ❌ |
| **`Shown` lambda (UpdateService check)** | `MainForm.cs:490` | ❌ (relies on callee's catch-all) |

**Risk:** Each unwrapped handler is a process-crash vector. A `NullReferenceException` mid-await in any of those propagates as an `AggregateException` posted back to the UI sync context, bypasses the WinForms `Application.ThreadException` path on modern .NET in many configurations, and reaches `AppDomain.UnhandledException` — typical outcome on .NET 10 is process termination.

**Why this matters more than usual:** the author's pattern in `Button_Graph_Click` proves they understand the contract. The inconsistency is the most common shape of "this almost-shipped fine, except for one Tuesday."

**Recommendation:** standardise. Two options:

**Option 1 — local pattern, replicate everywhere:**

```csharp
private async void Button_CheckedTargets_Click(object sender, EventArgs e)
{
    try
    {
        Log.Diag("UI", $"Button_CheckedTargets.Click checkedCount={CheckedListBox_SelectedTargets.CheckedItems.Count}");
        mCheckedToggleDebounce?.Stop();
        await RunGraphBuildAsync(HarvestCheckedTargets());
    }
    catch (Exception ex)
    {
        Log.Error("Button_CheckedTargets_Click threw", ex);
    }
}
```

**Option 2 — extract a `SafeAsync` helper** (one place, reuse everywhere):

```csharp
// In Support/AsyncHandler.cs
internal static class AsyncHandler
{
    public static async void Fire(Func<Task> work, string contextForLog)
    {
        try { await work().ConfigureAwait(true); }
        catch (Exception ex) { Log.Error($"{contextForLog} threw", ex); }
    }
}

// Caller:
private void Button_CheckedTargets_Click(object sender, EventArgs e)
    => AsyncHandler.Fire(async () => {
        mCheckedToggleDebounce?.Stop();
        await RunGraphBuildAsync(HarvestCheckedTargets());
    }, nameof(Button_CheckedTargets_Click));
```

The helper-extraction approach pays back faster as more handlers are added; it also surfaces the pattern as a discoverable convention in `Support/`.

Either way, audit the five flagged handlers above and bring them in line.

### 2.B 🟡 `Shown` event lambda swallows update-check exceptions silently

**Location:** `MainForm.cs:490`

```csharp
Shown += async (s, e) => await UpdateService.CheckOnStartupAsync(this);
```

`UpdateService.CheckOnStartupAsync` (`Updates/UpdateService.cs:37–42`) has an intentional catch-all that swallows every exception with no log line:

```csharp
catch (Exception)
{
    // Startup path: silent on any failure...
}
```

**Risk:** Velopack version drift, GitHub token issues, or a transient bug in `Manager.IsInstalled` would silently disable update checking for that user forever. There's no diagnostic trail.

**Recommendation:** add a single `Log.Warn(...)` inside the catch with the exception so debugging "why doesn't the user see update prompts?" is one `tp.log` grep away. The user-facing silence requirement is preserved; only the diagnostic surface improves.

```csharp
catch (Exception ex)
{
    Log.Warn("UpdateService.CheckOnStartupAsync swallowed exception (silent by design)", ex);
}
```

### 2.C 🟡 Re-entrancy: not every async button handler guards against double-click

**Locations:**
- `Button_Graph_Click` (`ChartBuildPresenter.cs:66`) — ✅ correctly disables `Button_GraphTarget` for the duration of `RunGraphBuildAsync`.
- **`Button_CheckedTargets_Click`** (`ChartBuildPresenter.cs:178`) — ❌ no disable. A second click during the await queues a second `RunGraphBuildAsync`.
- **`OnTargetListDragDrop`** (`TargetLoadingPresenter.cs:139`) — ❌ the listbox isn't disabled during the scan; a second drop spawns a concurrent `LoadFromPathsAsync`.
- **`ComboBox_Location_SelectionIndexChanged`** (`LocationPresenter.cs:304`) — ❌ the user could pick a third location while the second's `ResetForLocationChange` is still awaiting.

**Risk for `Button_CheckedTargets_Click`:** the coordinator's monotonic `mGeneration` does correctly supersede; only one pipeline's render writes wins. So no chart corruption. But two concurrent pipelines mean two concurrent `EnsureAsync` calls, two concurrent `CreateChartProgress` sinks competing for the bar (the older bumps `mChartBuildGeneration`, the newer takes over), and double the cache-prep work for the same targets (deduped per-key, but the dedupe lock churns). Mild perf issue, not a correctness one.

**Risk for `OnTargetListDragDrop`:** more serious. Two concurrent scans against `mFormClosingCts.Token` both call `AddScannedTargets` which calls `mSelection.AddKnownTargets`. The second call would see the first's adds in `KnownTargets` and dedupe them correctly — but they also both kick `StartCacheWarmup` independently, so two warmup chains race. Again the cache deduplicates per-key so eventual state is correct, but it's wasted CPU and the progress bar fights with itself.

**Risk for `ComboBox_Location_SelectionIndexChanged`:** the location-pick path calls `ResetForLocationChange` which calls `mCoordinator.Cancel()` followed by `await mCoordinator.ApplyImmediateAsync(...)`. A second pick before the first awaits returns: the second Cancel + ApplyImmediateAsync supersedes via generation as designed. Likely fine — but the `SettingsStore.Save(mAppSettings)` on line 344 of the *first* call could race the second's. Settings.json's last-writer-wins under JSON serialisation is benign.

**Recommendation:** standardise on the `Button_GraphTarget.Enabled = false` pattern for any async button handler that initiates a cache-warming or pipeline-running gesture. For drag-drop, gate the second drop with a `bool mScanning` flag (cheaper than disabling the listbox, which would block the user from re-arranging while drag-loading).

### 2.D 🟢 `Progress<T>` usage is exemplary

Specifically calling this out as a positive: `MainForm.CreateChartProgress` (`MainForm.cs:1429`) correctly captures `TaskScheduler.FromCurrentSynchronizationContext()` at the UI thread, hands a fresh sink per Apply, uses closure-captured generation stamping (`gen != mChartBuildGeneration` check inside the Report callback) for supersedence, and gates the deferred-hide on `mBarOwnerGen` so a follow-on pipeline can't be clobbered. `ChartCoordinator.OffsetProgress` (`ChartCoordinator.cs:243`) cleanly composes Render's local ticks into the outer sink's coordinate space.

This is the right shape — keep it. No `IProgress<T>` → `Dispatcher.Invoke` antipattern anywhere in the codebase.

### 2.E 🟡 Echo-guard flags are scattered and not exception-safe

`MainForm` carries five separate "I'm pushing into the UI programmatically, don't echo back" guard fields:

- `mUpdatingUiFromVm` (`MainForm.cs:67`)
- `mSyncingLocationUI` (`MainForm.cs:323`)
- `mSuppressFilterEvents` (`MainForm.cs:198`)
- `mSuppressFormClosingSave` (`MainForm.cs:172`)
- `mEditFiltersDialogOpen` (`MainForm.cs:203`)

The flip pattern is consistently:

```csharp
bool wasUpdating = mUpdatingUiFromVm;
mUpdatingUiFromVm = true;
try { /* UI writes */ }
finally { mUpdatingUiFromVm = wasUpdating; }
```

That's correct (the `finally` makes it exception-safe), but only some callsites use the `try/finally`. `Button_AddTarget_Click` (line 1316–1319) uses the pattern; `OnVmCheckedSetChanged` (line 162–177) uses the pattern; but several Designer-wired handlers in `LocationPresenter.cs` (e.g. `ComboBox_TimeZone_SelectedIndexChanged:109`) inspect `mSyncingLocationUI` defensively without ever flipping it themselves.

**Risk:** not a current bug — each guard's lifecycle is owned by one site at a time. But the cross-cutting state is exactly the shape that breeds future bugs: a new handler added in 12 months that forgets the `try/finally` and throws between `flag = true` and `flag = false` leaves the form in a permanently-suppressed state until restart.

**Recommendation (future-tense, not blocking):** consolidate into a single `using` scope helper:

```csharp
// Support/EchoGuard.cs
internal sealed class EchoGuard : IDisposable
{
    private readonly Action<bool> mSet;
    private readonly bool mPrev;
    public EchoGuard(Func<bool> get, Action<bool> set) { mPrev = get(); mSet = set; mSet(true); }
    public void Dispose() => mSet(mPrev);
}

// Caller:
using (new EchoGuard(() => mUpdatingUiFromVm, v => mUpdatingUiFromVm = v))
{
    ComboBox_SelectTarget.Text = t.Name;
}
```

Pays back the first time a handler needs to enter two guards in nested scopes. Acceptable to defer; flagging now because the field set has reached five.

### 2.F 🟢 `async void` on timer Tick is acceptable

`OnDebounceTick` (`ChartCoordinator.cs:136`), `CheckedToggleDebounce_Tick`, `SessionsRebuildDebounce_Tick` are all `async void` `Tick` handlers with try/catch wraps. This is correct WinForms convention — `System.Windows.Forms.Timer.Tick` has no awaitable signature. No change needed.

---

## 3. Cancellation & Exception Handling

### 3.A 🟡 `CancellationToken` does not thread through the coordinator or cache

**Locations:**
- `IChartCacheStore.EnsureAsync` (`Caches/IChartCacheStore.cs`) — no CT parameter.
- `ChartCoordinator.Apply` / `ApplyImmediateAsync` — no CT parameter.
- `CacheAxis.GetOrBuildAsync` / `PrepareAsync` — no CT parameter.

The author has explicitly documented this as an intentional design choice (`ChartCacheStore.cs:32–37` remarks):

> The cache itself does not cancel in-flight builds on a Location swap; `SetLocationAsync` just drops the cache dicts under the lock and starts fresh. Builds that were running against the old location keep going on the threadpool and discard themselves at publish time via the `ReferenceEquals(mLocation, location)` check inside `BuildEntryAsync`.

The trade-off: **lock-free at the cost of wasted CPU during a location swap.** With ~1–2 sec per full-44-target rebuild on the post-CS-removal Meeus path, the wasted CPU is bounded and the simplification is real (no per-await CT plumbing, no `OperationCanceledException` to triage).

**This is a defensible architecture decision.** The risk it carries is asymmetric: it's only correct as long as the per-rebuild compute stays bounded. Two scenarios where it would bite:

1. A future cache axis with longer compute (per-target XISF preview generation, per-night brightness simulation) — wasted CPU scales linearly with how much can be in flight at once.
2. A `Dispose()` race — if `ChartCacheStore.Dispose()` ever grew real cleanup (rather than the current no-op at line 465), an orphaned in-flight build could still be writing into a "disposed" store.

**Recommendation:** keep the no-CT design today. Document the boundary explicitly: in `IChartCacheStore.EnsureAsync`'s XML doc, state the contract — "no CT plumbing; supersedence is via the `ReferenceEquals(mLocation, buildLocation)` discard, callers may not cancel in-flight work." Future implementors of the interface need that constraint visible at the API boundary, not buried in a remarks paragraph on the concrete type. When the perf-budget paragraph in `CLAUDE.md` is approached or breached, this is the first decision to revisit.

### 3.B 🟡 Unobserved exceptions on `Task.Delay(...).ContinueWith(...)` continuations

**Locations:**
- `MainForm.cs:1462–1473` (chart-progress deferred hide)
- `MainForm.cs:1513–1521` (scan-progress deferred hide)

```csharp
Task.Delay(ProgressBarHoldMs).ContinueWith(_ =>
{
    if (mBarOwnerGen != gen) return;
    mBarOwnerGen = 0;
    ProgressBar_MultiTargetProcessing.Value   = 0;       // touching control
    ProgressBar_MultiTargetProcessing.Visible = false;   // touching control
}, uiSched);
```

**Risk:** if the form is closing in the 200 ms hold window and the control is disposed, `Value = 0` throws `ObjectDisposedException` in the continuation. With `ContinueWith` (no `OnlyOnRanToCompletion`, no try/catch), the exception is captured into the returned task — which nobody is observing. It will eventually surface via `TaskScheduler.UnobservedTaskException` at GC time (often well after the user has restarted the app and forgotten the issue), unless the app's `ThrowUnobservedTaskExceptions` legacy switch is on (in which case it terminates the process at GC time).

**Recommendation:** two clean fixes.

**Fix 1 — guard the continuation:**

```csharp
Task.Delay(ProgressBarHoldMs).ContinueWith(_ =>
{
    try
    {
        if (mBarOwnerGen != gen) return;
        if (IsDisposed || !IsHandleCreated) return;
        mBarOwnerGen = 0;
        ProgressBar_MultiTargetProcessing.Value   = 0;
        ProgressBar_MultiTargetProcessing.Visible = false;
    }
    catch (ObjectDisposedException) { /* form closed */ }
}, uiSched);
```

**Fix 2 — rewrite as an async helper (cleaner):**

```csharp
async void HideAfterHold(int gen)
{
    try
    {
        await Task.Delay(ProgressBarHoldMs).ConfigureAwait(true);
        if (mBarOwnerGen != gen || IsDisposed) return;
        mBarOwnerGen = 0;
        ProgressBar_MultiTargetProcessing.Value   = 0;
        ProgressBar_MultiTargetProcessing.Visible = false;
    }
    catch (Exception ex) { Log.Warn("Progress-bar hide-after-hold threw", ex); }
}
```

Fix 2 is cleaner and consistent with the rest of the codebase's async-void+try/catch convention.

### 3.C 🟢 `Form_FormClosing` ordering: CTS cancel → Dispose

**Location:** `MainForm.cs:562–568`

```csharp
try { mFormClosingCts.Cancel(); } catch (ObjectDisposedException) { }
mFormClosingCts.Dispose();

mCoordinator?.Dispose();
mCache?.Dispose();
```

**Observation:** correct. Cancel before Dispose, which lets the `StartCacheWarmup`'s `Task.WhenAny(warmup, cancelled)` race wake up the awaiter before the cache reference is destroyed (currently `Dispose` is a no-op so it doesn't strictly matter today). Worth keeping as the cache evolves.

### 3.D 🟢 `OperationCanceledException` is correctly the one un-swallowed exception

`TargetLoadingPresenter.LoadFromPathsAsync:110`, `GetImageLibraryTargets:175`, `GetJsonTargets:213` correctly distinguish OCE (expected, silently consumed) from other exceptions (logged via `Log.Error`). Good.

`ImageLibraryLoader.ParseFileAsync:71` uses the `catch (Exception ex) when (ex is not OperationCanceledException)` filter pattern — also correct, and meaningfully better than `catch (OperationCanceledException) { throw; } catch (Exception ex) { ... }` because it preserves the original stack.

### 3.E 🟢 Exception strategy in `CacheAxis.RunBuildAsync` is sound

`CacheAxis.cs:65–78`: on fault, the broken task is removed from `mInFlight` (via `DropOnFault`) so the next `GetOrBuildAsync` retries fresh rather than re-awaiting a permanently-faulted task. The rethrow propagates the fault to `PrepareAsync`'s `Task.WhenAll`, which surfaces it to the caller. Tight pattern.

---

## 4. Structure & Maintainability

### 4.A 🟡 `RenderArea` dispatch via string-keyed dictionary

**Location:** `ChartBuildPresenter.cs:204–211, 224–247`

```csharp
private string SelectedArea()
{
    if (RadioButton_Sessions.Checked) return "Sessions";
    if (RadioButton_Year.Checked)     return "Year";
    if (CheckBox_Sky != null && CheckBox_Sky.Checked) return "Sky";
    return "Day";
}

private void RenderArea(ChartContext ctx, IProgress<(int, int)> progress = null)
{
    if (!mSubCharts.TryGetValue(ctx.ActiveArea, out var sc)) return;
    sc.Render(ctx, mCache, progress);
    ...
}
```

**Why this works:** the dictionary is built once in `InitializeDynamicControls` (line 702) with `StringComparer.Ordinal`, and `SelectedArea()` returns one of four string literals. Mismatches between `SelectedArea()` and the dictionary key set would silently no-op.

**Why this is a smell:** the four string literals are duplicated between `SelectedArea()`, the dictionary initializer, and any future caller that wants to check "am I in Sky mode?" The compiler can't catch a typo (`"Year"` vs `"year"`). The `ChartContext.ActiveArea` field is also typed as `string` (verified by reading the snapshot path in `MainForm.cs:1149`).

**Recommendation:** introduce an enum, threaded through `ChartContext.ActiveArea`:

```csharp
public enum ChartArea { Day, Sky, Year, Sessions }

// SnapshotCurrent:
ActiveArea: SelectedArea(),

// SelectedArea:
private ChartArea SelectedArea()
{
    if (RadioButton_Sessions.Checked) return ChartArea.Sessions;
    if (RadioButton_Year.Checked)     return ChartArea.Year;
    if (CheckBox_Sky?.Checked == true) return ChartArea.Sky;
    return ChartArea.Day;
}

// Dictionary keyed on enum:
private Dictionary<ChartArea, IAltitudeSubChart> mSubCharts;
```

Net effect: typo-proof, refactor-safe, and the diag logs `Log.Diag("Coord", $"... activeArea={ctx.ActiveArea} ...")` still work because enum `ToString()` is fine. Pure ergonomic win; low risk; defer until you're touching `ChartContext` for another reason.

### 4.B 🟢 Pipeline collapse is well done

The 2026-05-17 pipeline-collapse refactor (cache `EnsureAsync` as single staleness gate) is exactly the right shape. `ChartCoordinator.RunPipelineAsync` is 60 lines, straight-line, with one decision (`gen != mGeneration` guard). The previous shape (per-area diff table + 3-way `Render/RefreshVisibility/ShowOnly` dispatch, per `CLAUDE.md` notes) would have been much harder to reason about.

The author's stance — codified in the class-level `<remarks>` — that new staleness signals belong as new fields on `ChartEvaluation` rather than as new side-paths is the right architectural defense. Worth preserving.

### 4.C 🟢 Method reuse: `RunGraphBuildAsync` already factored well

`Button_Graph_Click` and `CheckedToggleDebounce_Tick` and `Button_CheckedTargets_Click` all converge into `RunGraphBuildAsync(IReadOnlyList<Target>)`. The shared funnel is the right level of abstraction — single-vs-multi divergence is one parameter, and the coordinator handles supersedence. Don't merge further.

### 4.D 🟡 Special-case branching: `GetModeWindow` switch is on the edge

**Location:** `AltitudeSubChart_Day.cs:286–305`

```csharp
private static (DateTime, DateTime, double)? GetModeWindow(NightFit tonight, DayChartMode mode)
{
    switch (mode)
    {
        case DayChartMode.Transit:
            return tonight.CenteredStartUtc is { } cs
                && tonight.CenteredEndUtc is { } ce
                && tonight.CenteredFloor is { } cf
                ? (cs, ce, cf)
                : ((DateTime, DateTime, double)?)null;
        case DayChartMode.Floor:
        default:
            return tonight.StartUtc is { } s
                && tonight.EndUtc is { } e
                && tonight.Floor is { } f
                ? (s, e, f)
                : ((DateTime, DateTime, double)?)null;
    }
}
```

Today's two modes (`Floor`, `Transit`) read different field-triples from the same `NightFit` record. With two modes, the switch is acceptable. The pattern that creaks if a third mode arrives is the `default → Floor` fall-through (a new enum value silently dispatches to Floor) and the duplicated `is { } x` boilerplate.

**Recommendation:** if a third mode is on the roadmap, refactor to a strategy lookup on the enum:

```csharp
private static readonly Dictionary<DayChartMode, Func<NightFit, (DateTime, DateTime, double)?>> WindowSelectors
    = new()
    {
        [DayChartMode.Floor]   = f => f.StartUtc is { } s && f.EndUtc is { } e && f.Floor is { } fl
                                    ? (s, e, fl) : null,
        [DayChartMode.Transit] = f => f.CenteredStartUtc is { } cs && f.CenteredEndUtc is { } ce
                                    && f.CenteredFloor is { } cf ? (cs, ce, cf) : null,
    };
```

Or better, add the projection to `NightFit` itself as a method:

```csharp
// NightFit:
public (DateTime Start, DateTime End, double Floor)? WindowFor(DayChartMode mode) { ... }
```

Either removes the silent-fallthrough risk. Defer until mode-count grows.

### 4.E 🟢 SoC observation: MainForm field set has grown past partial-class manageable

MainForm declares ~35 private fields across `MainForm.cs` and the seven `MainForm.*Presenter.cs` partial files. The author has acknowledged this as a deliberate trade-off ("constructor-injecting all of that is more ceremony than the move is worth"). For a single-developer WinForms app this is correct; for a multi-developer codebase the calculus would flip toward Presenter objects with constructor injection so each concern's state is locally encapsulated.

No action recommended today. Re-evaluate when adding a second contributor or when the eighth partial file is contemplated.

### 4.F 🟢 SoC observation: cache axes generalized neatly

`CacheAxis<TKey, TVal>` (`Caches/CacheAxis.cs`) is a clean extraction of the four byte-identical axes that previously lived inline in `ChartCacheStore`. The injected build delegate keeps the genuinely-distinct compute in the store. Good pattern. The `mGate` is shared with the store so all four axes + the night cache + the `mLocation` swap reset atomically under one lock (`SetLocationAsync:406–430`). Correct.

---

## 5. Threading-model summary (positive findings worth preserving)

These aren't issues; they're architectural decisions that the review specifically confirms are sound and worth defending against future erosion.

1. **Single-writer cache + ReferenceEquals stale-discard.** Lock-free post-CS-removal because Meeus math is pure. The `ReferenceEquals(mLocation, buildLocation)` publish-time check is the canonical replacement for cancellation, and it works because (a) compute is bounded, (b) per-key dedupe means wasted CPU is bounded, (c) location swaps are infrequent + user-initiated.

2. **Monotonic generation supersedence in ChartCoordinator.** `Interlocked.Increment` + `Volatile.Read` guard at the side-effect boundary is the right shape for a UI-thread coordinator with overlapping async pipelines.

3. **`Progress<T>` captures SyncContext at construction.** Used consistently in `CreateChartProgress` and `BeginScanProgress`. No `Control.Invoke` / `BeginInvoke` boilerplate anywhere except the FileSystemWatcher callback (which is correct — `FileSystemWatcher.Changed` fires off-thread and `BeginInvoke` is the right marshalling primitive there).

4. **`mFormClosingCts` cancellation boundary.** Narrow and explicit: observed only at `TargetScanner.ScanAsync` and `StartCacheWarmup`'s `Task.WhenAny` race. The cache awaits don't observe it, which is consistent with the no-CT cache contract.

5. **`OperationCanceledException` triage.** Cleanly separated from other faults at every catch site. No silent swallows of cancellation that should have surfaced.

---

## Priority ladder

If picking up findings one at a time:

| Order | Finding | Effort | Risk reduction |
|---|---|---|---|
| 1 | §2.A — wrap the five unwrapped `async void` handlers | 30 min | crash class |
| 2 | §3.B — guard the `Task.Delay().ContinueWith()` continuations | 15 min | unobserved-exception class |
| 3 | §1.A — add `ConfigureAwait(false)` to cache internals | 1 hr | perf + future deadlock-proofing |
| 4 | §2.B — add `Log.Warn` in `UpdateService` catch-all | 5 min | diagnosability |
| 5 | §2.C — disable `Button_CheckedTargets` during await; gate drag-drop with `mScanning` flag | 30 min | re-entrancy / wasted CPU |
| 6 | §1.B — rewrite `EnsureNightCacheAsync` `ContinueWith` as async local function | 30 min | code-smell removal |
| 7 | §3.A — document the no-CT cache contract in the interface XML | 15 min | future-implementor safety |
| 8 | §4.A — `ChartArea` enum typing | 45 min | type safety |
| 9+ | §1.C, §2.E, §4.D — defer until adjacent changes bring them to hand | — | — |

Items 1–4 are the ones I'd bundle into a single "async hardening" PR. Everything below is fair game for incremental cleanup on whatever PR happens to touch the area.

---

## Closing observations

Two things that are easy to miss in a code review of this size, that I want to call out explicitly:

**The author has internalised the right async patterns.** The `async void` try/catch convention, the `Progress<T>` SyncContext capture, the supersedence-via-generation pattern, the `ReferenceEquals` stale-discard — none of these are easy to get right on the first try, and they're all consistently applied across most of the codebase. The findings above are about *covering the last 10%* of the surface, not about restructuring.

**The cache architecture is unusually principled for a WinForms project.** `IChartCacheStore.EnsureAsync` as a single staleness gate, `ChartEvaluation` as the diff-flag carrier between cache and coordinator, `CacheAxis<TKey,TVal>` as the dedupe primitive — this is the kind of design that survives the next two years of feature additions without erosion, *provided* the "no side-paths" rule documented in `ChartCoordinator.cs:21–28` is respected. Worth re-reading that remarks paragraph at every PR that touches the pipeline.
