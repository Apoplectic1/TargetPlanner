# TargetPlanner — Code Review (2026-04-21)

A whole-repo audit across `TargetPlanner/` (WinExe) and `Astronomy.Core/` (netstandard2.0 library). Goal: identify patterns worth cleaning up before further feature work, and sequence the cleanup so we address correctness first and polish last.

Audit was triggered by a cluster of runtime exceptions hit in a single repro session — `Collection was modified` in `ShowChartAreaSeries`, `Cross-thread operation not valid` from `Chart.Invalidate()`, `ArgumentNullException` in `SeriesFor(target)` — all symptoms of the same underlying learner-era patterns: shared mutable state, `async void` with `Task.Run` touching WinForms, null-prone setter side effects, static state in `Astrometry`, manual unsubscribe/resubscribe triple-binding, deprecated APIs, SGP-era leftovers.

## Severity legend

- **P1** — Active correctness / crash risk. Manifests or will manifest under plausible use. Fix first.
- **P2** — Design debt that will cause P1s if left alone or if requirements grow (scheduler, NINA plugin, XisfFileManager). Fix in the second wave.
- **P3** — Polish / readability. Fix opportunistically or batch as a cleanup commit.

## Counts at a glance

| Category                                | P1 | P2 | P3 |
| --------------------------------------- | -- | -- | -- |
| 1. Threading model                      |  5 |  5 |  1 |
| 2. State ownership & null safety        |  4 |  8 |  0 |
| 3. POCOs with side-effecting setters    |  1 |  4 |  1 |
| 4. UI binding patterns                  |  0 |  3 |  1 |
| 5. Error handling                       |  3 |  4 |  0 |
| 6. Dead code & naming                   |  0 |  1 |  9 |
| 7. Chart rebuild model                  |  0 |  4 |  1 |
| 8. Resource lifetime                    |  0 |  5 |  1 |
| 9. API surface & consumer contract (Core) |  0 |  5 |  2 |
| 10. Documentation & conventions (Core)  |  1 |  4 |  2 |
| **Total**                               | **14** | **43** | **18** |

---

## Category 1 — Threading model

### P1-1.1 `BuildSeriesList` is `async void` and swallows exceptions
`TargetPlanner/Charts/AltitudeSeries.cs:77` — `public async void BuildSeriesList()`. `Task.Run` failures silently vanish to `SynchronizationContext`; downstream code then iterates a null `mYearCache` and crashes with `NullReferenceException`. Change to `async Task`, let the caller (`AltitudeChart.BuildTargetSeriesList`) observe the Task.

### P1-1.2 `GetNinaTargets` is `async void` with a bare catch
`TargetPlanner/Forms/MainForm.cs:878` — same issue. Failures vanish; UI state (progress bar) doesn't reset. Convert to `async Task`, propagate exceptions, reset UI in `finally`.

### P1-1.3 `mTargetList` mutated concurrently during iteration
`TargetPlanner/Charts/AltitudeChart.cs:79, 187, 210, 226` iterate `mTargetList`; `AddToTargetList` / `ClearTargetList` mutate it. A timer tick or spinner event during `BuildSeriesList` can trigger concurrent modification. Source of the `Collection was modified` crash. Take a snapshot (`.ToList()`) before iterating *or* lock around mutations.

### P1-1.4 Chart indexer calls assume keys exist
`TargetPlanner/Charts/AltitudeChart.cs:124, 205` — `mChart.Series[legendItem.SeriesName]` and `mChart.ChartAreas[chartAreaName]` throw if the key is missing (legend click before series are fully rendered). Use `TryGetValue` / `ContainsKey` guards.

### P1-1.5 Timer has no `SynchronizingObject`
`TargetPlanner/Forms/MainForm.cs:62-65` — `System.Timers.Timer` fires on the thread pool. Every 5s it writes `mLocation.DateTime` / `mLocation.TimeZone` without synchronization while the UI thread reads and a `Task.Run` may clone. Set `mTimer.SynchronizingObject = this;` so the handler fires on the UI thread; remove the `Invoke` hack at line 661 afterward.

### P2-1.1 Shared mutable `mLocation` reference across threads
`TargetPlanner/Forms/MainForm.cs:20` + `TargetPlanner/Charts/AltitudeChart.cs:19` + `TargetPlanner/Charts/AltitudeSeries.cs:36`. One `Location` object is held by `MainForm`, passed to `AltitudeChart.Location`, then copied into every `AltitudeSeries.Location`. Mutated from UI thread (spinners, combobox), timer thread, and the `OnTimedEvent` path. `Clone(Location)` in `BuildYearSeries` can capture an inconsistent snapshot mid-mutation. Either serialize all mutations under a lock, or switch to "swap whole reference" semantics (immutable value-type update).

### P2-1.2 `AltitudeSeries.Location` / `Target` are public settable properties
`TargetPlanner/Charts/AltitudeSeries.cs:36-37`. No null checks, no invariant. `BuildSeriesList` reads them without validation. Make init-only (public get, `internal` or `private` set) and require callers to construct a new `AltitudeSeries`.

### P2-1.3 Stale `mEagerLoad` reuse across threads without documented guarantees
`Astronomy.Core/Night/NightCalculator.cs:16`, `Night/TwilightCalculator.cs:13`, `Moon/MoonSeparation.cs:12` — each module holds a `static readonly EagerLoad` and passes it to `CoordinateSharpGate.Calculate`. The lock serializes the call but doesn't certify that `EagerLoad` itself is safe to share. Document the assumption with a comment, or construct a fresh `EagerLoad` per call.

### P2-1.4 `Chart.Invalidate()` call sites assume UI thread with no guard
`TargetPlanner/Charts/AltitudeChart.cs:232, 256, 375`. Currently all callers are UI-thread events, but there's no assertion. Add `Debug.Assert(!mChart.InvokeRequired, ...)` to prevent regressions.

### P2-1.5 `BuildMoonSeries` / `BuildDaySeries` assume UI thread with no guard
`TargetPlanner/Charts/AltitudeSeries.cs:115, 154`. Same shape — called from `BuildSeriesList` pre-`await`, so on UI thread today. Add debug assertion.

### P3-1.1 Bare `Invoke(...)` in `OnTimedEvent` without `InvokeRequired` guard
`TargetPlanner/Forms/MainForm.cs:661`. Works because `Invoke` is safe from UI thread too, but asymmetric. Clean up once `SynchronizingObject` is set (P1-1.5).

---

## Category 2 — State ownership & null safety

### P1-2.1 Null-target crash chain
`TargetPlanner/Forms/MainForm.cs:999-1019` sets `mTarget = mTargetList.Find(...)` and bails with `return` when null — but the null assignment persists. Next `Button_GraphEphemeride_Click` passes `null` to `AltitudeChart.AddToTargetList`, which flows into `AltitudeChart.SeriesFor(target)` at `TargetPlanner/Charts/AltitudeChart.cs:58-65` — `Dictionary.TryGetValue(null)` throws `ArgumentNullException`. Source of the `key` ArgNull we hit today. Three fixes chainable:
 1. In the handler, use a temp local and only assign `mTarget` after the Find succeeds.
 2. In `SeriesFor`, guard `if (target == null) return /* null or sentinel */;`.
 3. In every iteration over `mTargetList`, skip nulls.

### P1-2.2 `mYearCache` null dereference in render methods
`TargetPlanner/Charts/AltitudeSeries.cs:243, 299` — `RenderYearSeries` and `RenderOptimalSeries` iterate `mYearCache` without null guards. `mYearCache` is null until the first `await Task.Run(() => ComputeYearCache())` completes. If the async path fails or is called out-of-order, both throw. Add `if (mYearCache == null) return;` at the top of both.

### P1-2.3 `Clone<T>` returns null silently on malformed input
`TargetPlanner/Charts/AltitudeSeries.cs:447-450` — `JsonConvert.DeserializeObject<T>` can return null; downstream `locationClone.DateTime = ...` then throws. Not likely under normal flow but a clear hole. Throw `InvalidOperationException` on null deserialize result.

### P1-2.4 Bare `mTarget` dereferences in combobox handler
`TargetPlanner/Forms/MainForm.cs:951-968` — `ShowCheckBoxObjectToolTip` finds a target by name and dereferences `found.Directory` without a null check. If the CheckedListBox is out of sync with `mTargetList`, crash on mouse-move.

### P2-2.1 Partial assignment then bail in `ComboBox_SelectTarget`
Already captured in P1-2.1 but worth separating: the anti-pattern is "assign to shared state, check, bail if invalid". Prefer "validate first, then assign".

### P2-2.2 `AltitudeChart.Location` public settable
`TargetPlanner/Charts/AltitudeChart.cs:19`. External callers can swap the reference mid-build. Make init-only.

### P2-2.3 `mSeriesByTarget` keyed by `Target` reference, not value
`TargetPlanner/Charts/AltitudeChart.cs:34`. Two `Target` objects with identical RA/Dec but different references are separate keys. Across target-list reloads, `mSeriesByTarget` accumulates stale entries. Either implement `Target.Equals`/`GetHashCode` (discouraged — Core has no identity concept) or key by `Target.Name`.

### P2-2.4 `mLocalDateTime` tuple mutated from timer thread
`TargetPlanner/Forms/MainForm.cs:54, 180, 591, 599, 657, 685`. Written from timer thread, read from UI thread. Not lock-protected. Also, `Tuple<DateTime, TimeZone>` is legacy — a C# 7 value tuple (`(DateTime value, TimeZoneInfo zone)`) is clearer.

### P2-2.5 `Location.DateTime` defaults to `DateTime.Now` in ctor
`Astronomy.Core/Locations/Location.cs:60-71`. Nondeterministic default breaks unit-testability for a library that's meant to be consumed by multiple apps. Leave `DateTime` unset (`default`) and require the caller to set it, or use `DateTime.MinValue` as a sentinel.

### P2-2.6 `Location.TimeZone` uses obsolete `System.TimeZone`
`Astronomy.Core/Locations/Location.cs:55, 70`. Compiler emits CS0618. `System.TimeZone` doesn't understand DST properly. Replace with `System.TimeZoneInfo`; update `NightCalculator.cs:22` and all call sites.

### P2-2.7 `NightWindow` sentinel contract is implicit
`Astronomy.Core/Night/NightWindow.cs:5-10`. `DateTime.MinValue` on `AstronomicalDawn` or `Dusk` signals "no night this day" but nothing enforces it. Every consumer must remember to check. Add a `bool IsValid` computed property (or switch to `(DateTime? Dawn, DateTime? Dusk)`).

### P2-2.8 Inconsistent null-check posture across Core public API
`Astronomy.Core/Session/BestSession.cs:28` and `Session/RiseSet.cs:51` validate arguments; `AltAz.At/Of`, `TargetGeometry.*`, `MoonSeparation.DegreesAt` do not. External consumers will hit `NullReferenceException` instead of `ArgumentNullException`. Standardize to `ArgumentNullException` at every public-method boundary.

---

## Category 3 — POCOs with side-effecting setters

### P1-3.1 `Target.Declination` setter forces `North` based on sign
`Astronomy.Core/Targets/Target.cs:28-48`. Assigning any non-negative value overwrites `North = true`, even if the caller just set `North = false`. JSON round-trips with Newtonsoft can flip hemisphere depending on property order. Split: store magnitude in `Declination`, keep `North` independent (remove the forced assignment in the else branch), or add an atomic `SetDeclination(double, bool north)` and deprecate the raw setter for external use.

### P2-3.1 `Target.RightAscension` setter derives `RaHours/Minutes/Seconds`
`Astronomy.Core/Targets/Target.cs:11-24`. Derived-fields-as-state pattern. Works, but any refactor (e.g., making `RaHours` a computed property) will quietly break serialization. Prefer `RaHours => Math.Floor(RightAscension);` etc.

### P2-3.2 `Location.Latitude` / `Longitude` setters flip hemisphere flags
`Astronomy.Core/Locations/Location.cs:9-49`. Same shape as Target.Declination but slightly less invasive (only negative values flip). Still fragile for JSON round-tripping. Consider an atomic `SetLatitude(double, bool north)` or leave the raw setter in place but document the coupling in XML comments plus CLAUDE.md.

### P2-3.3 `TextBox_Declination.Text` set triggers hemisphere side effect in handler
`TargetPlanner/Forms/MainForm.cs:1012-1013`. Setting the text box fires `TextChanged`, which writes `mTarget.Declination = ...`, which forces `North`. `CheckBox_TargetNorth` may now disagree with reality. Either refresh the checkbox after, or change the Declination setter (see P1-3.1).

### P2-3.4 Settings DTO round-trip has no schema migration
`TargetPlanner/Settings/SettingsStore.cs:24-43`. If fields are added or removed in a future `AppSettings`, there's no migration. Currently `Version = 1` is stored but never read. Read it on `Load`, compare, apply transforms or reset.

### P3-3.1 Derived D/M/S properties typed as `double`
`Astronomy.Core/Targets/Target.cs:22-24`, `Locations/Location.cs:24-26`. Callers often expect integers for degrees/minutes and a fractional for seconds. Document the convention (or just return `int`/`double` explicitly and accept the rounding cost).

---

## Category 4 — UI binding patterns

### P2-4.1 Triple-bound coordinate inputs with manual subscribe/unsubscribe
`TargetPlanner/Forms/MainForm.cs:198-211, 213-253, 258-312, 327-384`. Four near-identical blocks (lat, lon, RA, dec) each with six event subscriptions to pause/resume. An input added (a fifth control) requires touching every block. Extract a `CoordinateInput` helper that owns the triple and exposes one `Value` property.

### P2-4.2 `mSyncingLocationUI` guard exists only for location
`TargetPlanner/Forms/MainForm.cs:42` (added this session). A cleaner approach would apply the same guard pattern to RA/Dec/Target state so programmatic updates don't trigger user-input handlers. Promote to a general reentrancy guard attached to the `CoordinateInput` helper.

### P2-4.3 Regex validation pattern looks malformed
`TargetPlanner/Forms/MainForm.cs:219, 278, 341, 397` — `"  ^ [0-9]"` (two leading spaces, space around `^`, no end anchor). Duplicated verbatim four times. Pull to a named constant and verify the intent. Likely should be `@"^\d"` or similar.

### P3-4.1 Handlers set `.Text` to cascade updates
`TargetPlanner/Forms/MainForm.cs:209, 269, 330, 388` and others. Functional but implicit. A helper class with a clear "programmatic vs user" mode would make this obvious.

---

## Category 5 — Error handling

### P1-5.1 Bare `catch { return; }` in `ComboBox_SelectTarget`
`TargetPlanner/Forms/MainForm.cs:1015-1018`. Catches *everything* including `StackOverflowException`, `OutOfMemoryException`, bugs. Either remove or narrow to expected exception types.

### P1-5.2 Bare `catch (Exception ex)` in `GetNinaTargets`
`TargetPlanner/Forms/MainForm.cs:894-912`. Shows `ex.Message` in a MessageBox but swallows the stack trace. Log the full exception to `Debug.WriteLine` or a log file before the message.

### P1-5.3 `TargetLoader` silently skips malformed JSON
`TargetPlanner/Nina/TargetLoader.cs:43-45`. Bare `catch`. User can't tell how many files failed or why. Collect failures in a list, return/report after enumeration.

### P2-5.1 `SettingsStore.Load`/`Save` swallow all exceptions
`TargetPlanner/Settings/SettingsStore.cs:23-55`. `catch (Exception) { }` on both. Corrupt file silently resets to defaults; permission denied silently loses the user's settings. At minimum, `Debug.WriteLine(ex)`. Ideally a proper log file.

### P2-5.2 Empty guards mask missing invariants
`TargetPlanner/Forms/MainForm.cs:339, 379, 577, 584`. `if (mTarget == null) return;` / `if (mAltitudeChart == null) return;` papers over "why is this ever null?" Document invariants with `Debug.Assert` or restructure so the null state can't happen.

### P2-5.3 `IntegratedQuality.OverSession` has no bounds on quality function
`Astronomy.Core/Session/IntegratedQuality.cs:17-46`. If caller's `altitudeQuality` returns `NaN` or throws for an edge case altitude, the integral silently corrupts. Document the contract; consider a `Debug.Assert(!double.IsNaN(q) && !double.IsInfinity(q))`.

### P2-5.4 `BestSession.For` validates relationship but not absolute duration values
`Astronomy.Core/Session/BestSession.cs:29-30`. Checks `min > max` but not `min ≤ 0`. Negative or zero duration produces nonsense. Add `if (minDuration <= TimeSpan.Zero) throw new ArgumentException(...)`.

---

## Category 6 — Dead code & naming

### P2-6.1 Unused private fields
`TargetPlanner/Charts/AltitudeChart.cs:24-26` — `mTarget`, `mSeries`, `mSeriesList` initialized in ctor but never read. Delete.

### P3-6.1 Typos in user-facing chart titles
`TargetPlanner/Forms/MainForm.cs:635, 940` — "Poper Motion" (should be "Proper"). Also inconsistent: line 149 says "Altitude" while 635 says "Proper Motion". Pick one and apply everywhere.

### P3-6.2 Typos in comments / identifiers
Several: `// ****** Right Ascention ******` (`MainForm.cs:315`, should be Ascension); `Button_ClearEphemride` vs `Button_ClearEphemeride_Click` (`MainForm.Designer.cs` vs `.cs`); `mEgagerLoad` in `Support/Astrometry.cs:25` (should be `mEagerLoad`); "jLocation" stub referenced in commit `70bb331` — verify it's gone.

### P3-6.3 Banner comments 110+ asterisks wide
`TargetPlanner/Forms/MainForm.cs:164, 195-197, 255-257, 314-316, 372-374, 433-435, 571-573, 704-706, 845-847`, also ComboBox_Location sandwich comments at `659-684`. Replace with `#region ... #endregion` or delete.

### P3-6.4 Empty method `RemoveFromTargetList`
`TargetPlanner/Charts/AltitudeChart.cs:293-296`. Either implement or delete.

### P3-6.5 Stale "Phase N" references in Core comments
`Astronomy.Core/Session/BestSession.cs:21-22`, `Session/VisibilityWindows.cs:19-20`, `Night/NightCalculator.cs:13`, `Night/TwilightCalculator.cs:20`. Reference an internal refactor plan with no definition. Replace with concrete TODOs or delete.

### P3-6.6 `CoordinateSharpGate` comment references `TargetPlanner` code
`Astronomy.Core/CoordinateSharpGate.cs:10` — Core file naming specific TargetPlanner classes (`Astrometry.Location`, `BuildMoonSeries`, `AddDawnDuskGradient`). Core shouldn't reference consumers. Rewrite the comment to describe the CoordinateSharp contract abstractly.

### P3-6.7 SGP-era leftovers in the UI
`TargetPlanner/Forms/MainForm.Designer.cs` — a few controls still have `Sgp` in their name despite the recent rename; verify after the current working tree is clean.

### P3-6.8 Legacy `Tuple.Create` usage
`TargetPlanner/Forms/MainForm.cs:54, 180, 591, 599, 657, 685` — replace with value-tuple syntax `(DateTime.Now, ...)` for readability.

### P3-6.9 Dead `mComponents` in Designer
`TargetPlanner/Forms/MainForm.Designer.cs:13` — unused. Let the Designer regenerate.

---

## Category 7 — Chart rebuild model

### P2-7.1 Full teardown on every Graph click
`TargetPlanner/Forms/MainForm.cs:605-644`. Each click clears the panel, constructs a fresh `AltitudeChart`, re-runs `BuildTargetSeriesList` for the whole list. Discards `mSeriesByTarget`, `mNowLines`, legend toggle state. Slow for large lists, sheds per-user state. Prefer a "reload targets in place" path.

### P2-7.2 `mNowLines` references survive chart rebuild
`TargetPlanner/Charts/AltitudeChart.cs:36, 53, 99`. Dictionary isn't cleared when the outer chart is rebuilt (new `AltitudeChart` instance — but in the "keep the chart" fix above, this matters). Either clear in a reset method or bind lifetime explicitly.

### P2-7.3 `mChartAreaList` not cleared in `Button_GraphEphemeride_Click`
`TargetPlanner/Charts/AltitudeChart.cs:144-159`, called from `TargetPlanner/Forms/MainForm.cs:628-630`. `MainForm.InitializeDynamicControls` calls `ClearChartAreaList()` first (`:138`), but `Button_GraphEphemeride_Click` does not. On repeat clicks, chart areas accumulate.

### P2-7.4 `AddChartAreaToChart` clears all ChartAreas on every radio switch
`TargetPlanner/Charts/AltitudeChart.cs:162-176`. Radio button → `ShowChartAreaSeries` → `AddChartAreaToChart` clears `mChart.ChartAreas` and re-adds the one selected area. User zoom / legend state lost every switch. Prefer `Visible = true/false` on existing areas.

### P3-7.1 Chart title inconsistent between init and rebuild paths
`TargetPlanner/Forms/MainForm.cs:149` says "Altitude", `:635` says "Proper Motion" (with typo). Unify.

---

## Category 8 — Resource lifetime

### P2-8.1 `AltitudeChart` / wrapped `Chart` not disposed on rebuild
`TargetPlanner/Forms/MainForm.cs:619` constructs a new chart but never disposes the old one (`Panel.Controls.Clear()` removes but doesn't dispose). GDI handle leak across many clicks. Implement `IDisposable` on `AltitudeChart` (forwarding to `mChart.Dispose()`); call it before reassignment.

### P2-8.2 `System.Timers.Timer` never disposed
`TargetPlanner/Forms/MainForm.cs:62-65`. Keeps firing if the form is recreated. Stop + Dispose in `FormClosing`.

### P2-8.3 `ToolTip` created in Load, not disposed
`TargetPlanner/Forms/MainForm.cs:31-32, 76-79`. Same shape.

### P2-8.4 `AltitudeChartForm` instances accumulate
`TargetPlanner/Forms/MainForm.cs:938` — every `Button_GraphTargetList_Click` creates a new popup and leaks the old one. Either single-instance (cache and `Show()`/`BringToFront()`) or wire up a close handler that disposes.

### P2-8.5 `AltitudeChart` doesn't implement `IDisposable`
`TargetPlanner/Charts/AltitudeChart.cs:14`. Blocks clean handling of the above.

### P3-8.1 `Panel_AltitudeChart.Controls.Clear()` doesn't dispose child controls
`TargetPlanner/Forms/MainForm.cs:607`. Related to P2-8.1. Clear + Dispose loop.

---

## Category 9 — API surface & consumer contract (Core)

### P2-9.1 `AltAz.At/Of` returns `Tuple<double, double>`
`Astronomy.Core/AltAz.cs:13, 31`. Consumers unpack via `.Item1` (altitude), `.Item2` (azimuth). Zero type-level documentation. Replace with a named `readonly struct AltAz { public double Altitude; public double Azimuth; }` (or `AltAzResult` if `AltAz` stays as the static helper).

### P2-9.2 `DateTime.Kind` assumption unstated
`Astronomy.Core/AltAz.cs:33` calls `location.DateTime.ToUniversalTime()`. If the caller passes UTC with `Kind = Utc`, `ToUniversalTime` returns it unchanged; if `Unspecified`, behavior depends on system. Document expectations with an XML comment on every method that reads `Location.DateTime`.

### P2-9.3 `RiseSet.NextAtOrAfter` overloads with identical signature intent
`Astronomy.Core/Session/RiseSet.cs:17-40, 48-65` — scalar vs profile. Name-based differentiation would be clearer: `NextAtOrAfter_Scalar` and `NextAtOrAfter_Profile`, or a single overload taking `IHorizonProfile` (wrap scalar at the call site with `ScalarHorizonProfile`).

### P2-9.4 `RiseSet.NextAtOrAfter` returns `(null, null)` for two different conditions
`Astronomy.Core/Session/RiseSet.cs:24-25`. Consumer can't tell "circumpolar" from "never rises". Return a named state: `enum RiseSetState { Found, Circumpolar, NeverRises }` as an out-parameter or wrap the result.

### P2-9.5 BestSession window boundary inclusive/exclusive not documented
`Astronomy.Core/Session/BestSession.cs:48-58`. Transit-at-dusk-exactly is included, transit-at-dawn-exactly is excluded. Document.

### P3-9.1 Mixed `Tuple<T1,T2>` and `(Start, End)` named tuples
`Astronomy.Core/Session/VisibilityWindows.cs:25` uses `(DateTime Start, DateTime End)`. `AltAz` uses `Tuple<double,double>`. Pick one style (named tuples or structs for public API).

### P3-9.2 Inconsistent collection return types
All Core collection returns use `IReadOnlyList<T>` today, which is good. Document the convention in CLAUDE.md so it doesn't drift.

---

## Category 10 — Documentation & conventions (Core)

### P1-10.1 Obsolete `System.TimeZone` warning at compile time
Same issue as P2-2.6 but flagging from the "public API" angle: the warning appears in every consumer build (TargetPlanner, and will in XFM / plugin). Must be fixed before external consumers are asked to suppress it.

### P2-10.1 No XML documentation generated
`Astronomy.Core/Astronomy.Core.csproj:9` sets `<GenerateDocumentationFile>false</GenerateDocumentationFile>`. Consumers get no intellisense. Flip to `true` and add `///` comments to every public type/method — at minimum for DateTime.Kind rules, signed/unsigned conventions, null contracts, edge-case returns.

### P2-10.2 Signed-degree convention not stated at API surface
Documented in CLAUDE.md but absent from code. A NINA plugin author reading the `TargetGeometry` source cold wouldn't know the flag / magnitude split. Add a module-level comment block or a `/// <remarks>` to every public method that takes signed input.

### P2-10.3 `Target`/`Location` derived property coupling undocumented
Touched on in P2-3.*. Add `/// <remarks>Setting this property also updates X and may change hemisphere flag Y.</remarks>` so IntelliSense warns the caller.

### P2-10.4 CLAUDE.md doesn't flag the XisfFileManager-facing contract
Core is meant to be consumable by XFM and a future NINA plugin. Update CLAUDE.md to list the Core "consumer contract" explicitly (null rules, DateTime.Kind rules, thread-safety, serialization readiness).

### P3-10.1 Stale Phase-N comments (see P3-6.5)
Listed there; note here that they block writing proper XML docs.

### P3-10.2 Derived property naming in Core
`RaHours`, `DecDegrees`, `LatDegrees` — ambiguous between "whole hours/degrees" and "decimal hours/degrees". Rename to `RaHoursComponent` etc., or keep and document aggressively.

---

## Investigations that didn't pan out

### Optimal-chart positive spike — not a thread-safety artifact
Originally thought to be caused by cross-thread `Series.Points` mutation (W0 compute/render split) or CoordinateSharp concurrency (W0 `CoordinateSharpGate`). **Neither fix eliminated it.** User confirmed determinism across runs. Remaining theories:
 1. **Rendering artifact.** `-90.0` sentinel day adjacent to a real-altitude day: the chart's line segment runs from the off-chart sentinel (below Y-axis minimum of 10) up to the real value — visually an upward vertical line. Fix would be `DataPoint.IsEmpty = true` on sentinel points plus `Series.EmptyPointStyle.BorderWidth = 0` so the chart breaks the line instead of drawing through. Moved out of Wave 1 since it's a P2 polish, not correctness. Track as **P2-Ren.1** — render sentinel `-90` points as empty.
 2. **Math anomaly.** One-day isolated high value with legitimate low neighbors. Requires a step-through of `RenderOptimalSeries` on the specific spike date with logged `(entry.YearAlt, entry.TransitInNight, ahStart, ahEnd, s, e, windowMax, floorAlt, centeredAlt)` to tell. Not yet investigated.
User asked to defer; track here so we don't retrace these theories.

## Cross-cutting themes

Rather than category-by-category, here are the root issues that generate most of the findings. Fixing these collapses many P2s.

1. **`mLocation` is a shared mutable reference.** Most of Category 1 and half of Category 2 trace back here. Fix: either serialize mutations behind a lock, or adopt value semantics (`ImmutableLocation` + swap-the-reference).
2. **POCO setters with side effects on sibling properties.** Target.Declination, Location.Latitude, Location.Longitude all have this. JSON round-trips are where it bites hardest. Fix: atomic `Set*(value, flag)` methods; deprecate the raw setter for external callers; or split magnitude from hemisphere into independent properties.
3. **`async void` everywhere on the UI thread.** `BuildSeriesList`, `GetNinaTargets`. Fix: convert to `async Task`; let the outer handler (which is an event handler and thus can remain `async void`) `await` the Task and handle exceptions.
4. **No `IDisposable` discipline on long-lived objects.** Chart, Panel children, Timer, ToolTip, popup form. Fix: implement IDisposable on `AltitudeChart`; dispose everything in `MainForm_FormClosing`.
5. **Chart teardown-and-rebuild as the only update path.** Creates most of Category 7 and contributes to Category 8. Fix: `AltitudeChart.ReloadTargets(List<Target>)` method that keeps the chart and swaps series.
6. **Triple-bound coordinate inputs with hand-maintained unsubscribe.** Category 4 almost entirely. Fix: a `CoordinateInput` helper class that owns the spinners/textbox/checkbox triple.
7. **No XML docs on Core.** Most of Category 10. Fix: one pass through the public surface adding `///` comments.

---

## Suggested cleanup waves

Based on impact × risk. Each wave is a separate commit (or small PR-equivalent). Waves 1 and 2 are the load-bearing ones; 3 is polish.

### Wave 1 — Stop the bleeding (P1s)

Closes every active crash path the user has hit. Small diffs, localized changes, strong signal on the next repro run.

1. Null-target chain (P1-2.1): validate-then-assign in `ComboBox_SelectTarget_SelectedIndexChanged`; null guard in `AltitudeChart.SeriesFor`; skip-null in every `mTargetList` iteration.
2. `async void` → `async Task` for `BuildSeriesList` and `GetNinaTargets` (P1-1.1, P1-1.2), with try/catch wrappers at the event-handler boundary only.
3. Snapshot `mTargetList` before iteration in `AltitudeChart` (P1-1.3).
4. Null guards on `mYearCache` in the render methods (P1-2.2).
5. Narrow the bare catches in `ComboBox_SelectTarget` and `GetNinaTargets` (P1-5.1, P1-5.2).
6. Replace `System.TimeZone` with `System.TimeZoneInfo` in `Location` (P1-10.1 / P2-2.6).
7. Fix `Target.Declination` side effect (P1-3.1) — prefer splitting magnitude from hemisphere.

### Wave 2 — Structural cleanup (P2s)

Addresses the root causes that would otherwise re-spawn P1s.

1. `mLocation` ownership model: put it behind a single `LocationService` (or immutable + swap-reference) so mutations are controlled (P2-1.1, P2-2.4, P2-3.2).
2. `AltitudeChart` as `IDisposable`, disposing on rebuild; dispose Timer, ToolTip, and popup form in `FormClosing` (P2-8.1…5).
3. Introduce a `ReloadTargets` path on `AltitudeChart` that keeps the chart instance alive across Graph clicks (P2-7.*).
4. `CoordinateInput` helper encapsulating triple-bound D/M/S + textbox + checkbox (P2-4.1, P2-4.2).
5. `TimerSynchronizingObject = this` + remove `Invoke` hack (P1-1.5 already; resulting cleanup is P2).
6. Make `AltitudeSeries.Location` / `Target` init-only, and make `AltitudeChart.Location` init-only (P2-1.2, P2-2.2).
7. Null-argument validation at every public Core method boundary (P2-2.8).
8. `NightWindow.IsValid` or `(DateTime?, DateTime?)` for explicit contract (P2-2.7).
9. `AltAz` return type as a named struct (P2-9.1).
10. `RiseSet` return state enum (P2-9.4).
11. Chart indexers → `TryGetValue` (P1-1.4 already; polish in Wave 2).
12. Log in `SettingsStore.Save`/`Load` and `TargetLoader` instead of silent swallow (P2-5.1, P1-5.3).

### Wave 3 — Polish (P3s)

Batch into a single "cleanup" commit. Safe, low-risk, high-readability.

1. Typo sweep: "Poper Motion", "Right Ascention", `mEgagerLoad`, `Button_ClearEphemride`.
2. Banner comment replacement with `#region`s (or deletion).
3. Dead fields (`mTarget`, `mSeries`, `mSeriesList`) deletion.
4. Empty `RemoveFromTargetList` deletion.
5. Replace `Tuple.Create` calls with value-tuple syntax.
6. Delete stale Phase-N comments.
7. `CoordinateSharpGate` comment rewrite (Core-internal).
8. Flip on `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and add `///` comments (Wave 3 because non-breaking; could slide into Wave 2 if time allows).

---

## How to use this document

- Each finding has a stable ID (e.g. `P1-2.1`) so we can reference them in commits / PRs.
- Waves are independent — Wave 1 can land, be tested, and ship before Wave 2 starts.
- If a finding turns out to be wrong on closer inspection, strike it here with a note; don't just drop it silently.
- New findings can be appended (as `P2-7.5` etc.) as they surface.
