# Astrometry Library & Interval-Scheduling Plugin — Design Dossier

> This document is a working design record, not an implementation ticket. The intent is that it can be picked up cold months from now — or moved to a future project — and still make sense. Caveats and trade-offs are preserved, not filed off. Findings from reconnaissance into the NINA codebase and the existing Target Scheduler plugin are included verbatim (no details elided) so downstream decisions have the same context the author did.

---

## Context

Two things drive this plan:

1. **The roadmap calls for a shared astrometry library.** Ephemerides and XisfManager both currently duplicate or hand-roll astronomical math. Extracting a clean library lets both consume the same primitives, and opens the door to using them in other contexts (scripting, NINA plugins, future tools).

2. **Planned NINA Target Scheduler plugin.** An alternative to Tommy Oldham's Target Scheduler for NINA, aimed at a family of scheduling priorities:
   - **Meridian-chase**: optimize a series of targets crossing the meridian in succession.
   - **Narrow-window capture**: catch a target that's only briefly available from the user's location (low-culminating target with a short above-horizon arc, or one clipped by local obstructions).
   - **Keep-camera-busy**: maximize integrated exposure across a night given a pool of candidate targets.

All three are interval-scheduling problems with different objective weights. None of them are greedy-at-decision-time score-based scheduling (the pattern used by most amateur tools), which is explicitly being avoided.

Local horizon will eventually be a 360° azimuth→altitude profile (user-entered obstruction list), not a scalar. That single change ripples through most of the horizon-related primitives.

---

## Local reference sources

Three reference tree are available locally on this machine. They are not modified by work in this repo — they're reference material only.

- **NINA codebase**: `E:\Projects\VisualStudio\Astronomy\NINA`
  - Tracked branches: `master` (release line, currently NINA 3.2.0.9001) and `develop` (bleeding edge).
  - Reconnaissance notes below reflect two states: an initial pass against a late-2023 source snapshot (whose claims are mostly still correct for the 3.2.x tree), and a **delta pass against `develop` at SHA `0bc2986df`** capturing what's moved since.
  - Notable subprojects: `NINA.Astrometry` (existing astrometry code — check before reinventing), `NINA.Sequencer` (sequence composition, built-in items, triggers, conditions), `NINA.Plugin` (plugin base class and loader), `NINA.Profile` (profile / settings), `NINA.Core` (shared value types), `NINA.Equipment` (mediators for camera/telescope/etc).
  - Bundled native dependencies: **NOVAS31** (NIST C library) and **SOFA** (IAU C library, 2023-10-11 revision), consumed through managed P/Invoke wrappers in `NINA.Astrometry`. Both are reference-grade astronomical libraries; together they underpin NINA's coordinate transforms, Julian dates, sidereal time, refraction, and planet positions.
  - Ephemeris file bundled: `External/JPLEPH` (JPL DE series), valid for JD 2305424.5 – 2525008.5.

- **Target Scheduler plugin clone**: `E:\Projects\VisualStudio\Astronomy\TargetScheduler_Clone\nina.plugin.targetscheduler`
  - Currently at upstream tip `2ec0c4d` (v5.9.0.0 release tag, committed 2026-02-28). Fork `origin` and `upstream` are in sync.
  - Four projects: `NINA.Plugin.TargetScheduler` (~10 k LOC main plugin), `.Shared` (logging + constants), `.SyncService` (gRPC multi-instance sync), `.Test` (NUnit + Moq + FluentAssertions).
  - Has its own `CLAUDE.md` — prior Claude-assisted work captured conventions there; read before editing anything in that tree.

- **This repo**: `E:\Projects\VisualStudio\Astronomy\SGP Ephemerides`
  - Current home of the analytic Year/Optimal/OptimalFloor chart machinery — effectively a prototype of the session-analysis primitives we're going to formalize.

---

## Philosophy: Planning is a pure function of astrometry

The key insight driving the architecture: **weather, mount behavior, autofocus overruns, plate-solve retries, meridian flips — none of those move the sky**. They interrupt and delay execution, but the theoretically-optimal plan for a given night is a deterministic function of location, targets, and time. Consequences worth internalizing:

- **The scheduler plans; the executor reacts.** The scheduler's job is "what is the theoretically best use of these 8 hours given these N targets and my horizon profile?" The executor's job is "how far through that plan can I actually get tonight?"
- **Replanning is cheap.** If clouds kill hours 2–3 of the night, the scheduler regenerates the plan for hours 4–8 from scratch using the same primitives with an updated "already-integrated-on-each-target" state. No "partial plan salvage" logic.
- **Plans are deterministic and testable.** Given identical inputs (location, targets, horizon, night window), plans are identical.
- **"What-if" analysis is free.** Users can ask "what does tomorrow night look like if I add target X?" without the scheduler having to run the night.

The trade-off is clarity: this framing only works if the library really is pure and the scheduler really is stateless w.r.t. execution artifacts. Muddying either side — say, letting "last successful autofocus" leak into a scoring weight — breaks the determinism and rots the architecture fast.

---

## How existing schedulers behave (landscape)

Two broad philosophies are in use today:

### Score-at-decision-time (amateur tools)

**Examples:** NINA's Target Scheduler (Tommy Oldham), KStars/Ekos scheduler, Voyager RoboTarget, ACP (semi-pro).

At each decision point (target finishes, previous target fails, weather clears, etc.) the scheduler computes a score per candidate target and picks the highest. Scores are typically weighted sums over features:

- Current altitude (often linear; sometimes airmass-weighted).
- Distance from meridian (closer = better, to avoid pier flips in mid-session).
- Moon separation (further = better; critical for broadband, tolerable for narrowband).
- Moon illumination tolerance (per-target).
- Completion percentage vs. user's exposure goal.
- Priority tier / "must image first" flags.

Characteristics:
- **No long-horizon plan exists.** The scheduler reacts to the current moment.
- **Designed for fragility.** Amateur equipment fails unpredictably; committing to a 6-hour plan that dies on hour 1 is wasteful.
- **Poor at global optimization.** Two scientifically-equivalent targets that peak sequentially will both get imaged during their respective peaks only by luck; the scheduler has no model of future moments.
- **Simple to reason about and debug.** When a user asks "why did you pick X?" the scheduler can show the score breakdown.

### Interval-scheduling / optimization (research)

**Examples:** LSST Feature-Based Scheduler, LCO's Gurobi MILP-based scheduler, ALMA ranking-based, Hubble long-range planning.

These solve for a plan over a horizon (a night, a week, a semester) treating target visibility windows as intervals and maximizing a global utility function. LSST uses simulated annealing / greedy optimization over short horizons; LCO runs a mixed-integer linear program. They all share the property of producing a *plan* — a sequence of assignments — rather than a next-target decision.

Characteristics:
- **Globally optimal** (for the chosen objective), within discretization.
- **Re-planning on failure is expensive** but tractable (warm-start from the previous plan).
- **Requires a precise objective function.** "Maximize SNR-weighted integration time" vs. "maximize number of targets imaged" are different problems with different optimal plans.
- **Not common in amateur tools** — most amateurs aren't willing to specify objectives that precisely and are uncomfortable with "computer says target B is better than target A."

### Where this proposal sits

Structurally, this proposal is in the **interval-scheduling camp**. It's unusual for amateur tooling. This is a deliberate choice: accept the complexity of specifying an objective function (to be "integrated quality over a session") in exchange for globally-optimal plans that are deterministic, replay-safe, and introspectable.

The three modes (meridian-chase, narrow-window, keep-busy) are not three different schedulers — they're **the same underlying solver with different objective weights**. That convergence is a strong design signal.

---

## What NINA.Astrometry already provides

Reconnaissance pass, structured by concern:

### Coordinate primitives (keep, consume directly)

- `NINA.Astrometry.Coordinates` — RA/Dec equatorial coordinates tagged with `Epoch` (J2000 or JNOW). Supports epoch transform via SOFA; precession handled. Carries `ICustomDateTime` for apparent-epoch calculations.
- `NINA.Astrometry.TopocentricCoordinates` — alt/az at a given observer location, supports refraction-corrected transforms via `Transform(epoch, pressure, temp, humidity, wavelength)`.
- `NINA.Astrometry.Angle` — angular value type; immutable after construction.
- `NINA.Astrometry.Separation` — stores RA delta, Dec delta, angular distance, bearing between two coordinates. Created via `Coordinates - Coordinates` operator.
- `NINA.Astrometry.RectangularCoordinates` / `RectangularPV` — XYZ + velocity vectors for solar-system body calculations.

### Observer / site

- `NINA.Astrometry.ObserverInfo` — location (lat, lon, elevation) plus atmospheric parameters.
- `NINA.Astrometry.Location` — lower-level position holder.

### Time handling

- No custom Time class. Public API takes `System.DateTime`, assumed UTC with explicit `.ToUniversalTime()` calls.
- Internal: Julian Date via `AstroUtil.GetJulianDate(DateTime)` (split JD pairs handled by SOFA/NOVAS).
- ΔT (UT1–UTC) retrieved from `DatabaseInteraction.GetUT1_UTC(utcDate)` — **database-backed IERS EOP data**, cached per day with 3-day rolling window. `AstroUtil.DeltaT(DateTime)` returns ΔT via `TAI-UTC` + `UT1-UTC`.
- TT / TAI / UTC conversions go through `SOFA.UtcTai()` + `SOFA.TaiTt()`. Leap seconds embedded in the native library's table.
- **No `DateTimeOffset` used in public API.** If we adopt NINA.Astrometry we inherit the UTC-DateTime convention.

### Sidereal time, hour angle, altitude

- `AstroUtil.GetLocalSiderealTime(date, longitude) → hours` — LST.
- `AstroUtil.GetHourAngle(siderealTime, ra) → Angle`.
- `AstroUtil.GetAltitude(hourAngle, latitude, declination) → Angle` — exactly equivalent to the `AltAtHa` helper we just added to the Ephemerides codebase. We'll discard our local version once the plugin consumes NINA.Astrometry.

### Rise/set/transit framework

- Abstract `RiseAndSetEvent` base class + subclasses: `AstronomicalTwilightRiseAndSet`, `NauticalTwilightRiseAndSet`, `SunRiseAndSet`, `MoonRiseAndSet`, `CustomRiseAndSet`. Each exposes `Rise` and `Set` (nullable DateTime).
- Algorithm: quadratic fit on 2-hour altitude samples, solves discriminant to find zero-crossings. Code comment: "does not consider more than one rise and one set event" — **polar regions not handled correctly**.
- **Twilight threshold is hardcoded per subclass.** `AstronomicalTwilightRiseAndSet` uses −18°, `NauticalTwilightRiseAndSet` uses −12°. Not parameterized — to use a different sun altitude threshold we'd subclass or reimplement.
- **Refraction is NOT applied in rise/set** — raw geometric altitude is used. `NOVAS.Refract()` exists but isn't wired into the rise/set path.
- **No explicit transit-time solver.** Max altitude can be found by sampling `IDeepSkyObject.Altitudes`, but that's an O(N) scan over a precomputed curve. We'll add an analytic `TransitUtc(target, date, loc)` that inverts `LST = RA`.

### Horizon profiles

- `CustomHorizon` (in `NINA.Core.Model`) — polyline horizon, azimuth→altitude with 1-D spline interpolation. **Already exists.**
- **Not wired into `RiseAndSetEvent`.** The base class's virtual `AdjustAltitude(body)` returns a scalar, and subclasses follow suit. To honor a polyline horizon we'd need a new subclass that takes a `CustomHorizon` and interpolates at the body's current azimuth — essentially transcendental, needs bracketed solve.
- Exposed on `IDeepSkyObject.SetCustomHorizon(customHorizon)`; the object's precomputed `Altitudes` / `Horizon` curves honor it.

### Moon / Sun ephemerides

- `AstroUtil.GetMoonPosition(date, jd, observer) → NOVAS.SkyPosition` (RA, Dec, distance).
- `AstroUtil.GetMoonRiseAndSet(date, lat, lon) → RiseAndSetEvent`.
- `AstroUtil.GetMoonPhase(date) → MoonPhase` enum (NewMoon through WaningCrescent).
- `AstroUtil.GetMoonIllumination(date) → double` (0–1).
- `AstroUtil.GetMoonAltitude(date, observerInfo) → double` (non-obsolete; older `(date, lat, lon)` overload is `[Obsolete]`).
- `SOFA.Seps(ra1, dec1, ra2, dec2) → double` (radians) — direct angular separation between two points. This is what we'd use to compute moon separation at a specific instant.

### Airmass

- `AstroUtil.Airmass(altitude) → double` — plain `sec(z)` formula, returns NaN outside [0°, 90°]. Good enough for most imaging planning; if we ever want Kasten-Young refraction-corrected airmass we'll wrap.

### Planets / solar system bodies

- `NOVAS.Body` enum (Mercury through Pluto, Sun, Moon).
- `NOVAS.PlanetApparentCoordinates(jd_tt, body) → Coordinates`.
- `NOVAS.BodyPositionAndVelocity(jdtt, body, origin) → RectangularPV`.
- `BasicBody`, `Moon`, `Sun`, `Earth` classes in `NINA.Astrometry.Body`. Rise/set for non-stellar bodies works via these.
- **Comets and asteroids**: NOVAS / SOFA support orbital elements but no C# wrapper in NINA. Would need direct P/Invoke. Not a priority for this work.

### NOVAS / SOFA interop mechanics

- Both consumed through static classes `NINA.Astrometry.NOVAS` and `NINA.Astrometry.SOFA`.
- P/Invoke via `[DllImport]` against `NOVAS31lib.dll` / `SOFAlib.dll`.
- **NOVAS is not thread-safe** — `NINA.Astrometry.NOVAS.Place()` is guarded by `lock (lockObj)`. Implications: heavy scheduler workloads that want to parallelize per-target computations should use SOFA paths where possible, or be aware of serialization through this lock.
- **SOFA is stateless** — no locking needed.
- Ephemeris file (`External/JPLEPH`) is loaded at static initialization. Deployment concern for the plugin: bundling vs. relying on NINA's install.

### What's missing (our library's contribution)

Concrete primitives NINA.Astrometry does NOT provide:

1. **Transit-time solver.** Analytic `TransitUtc(target, date, loc) → DateTime`. Inverts `LST = RA`.
2. **Visibility-windows intersected with night.** `VisibilityWindows(target, night, horizonProfile) → [(start, end)]`. 0–2 segments per night. Builds on NINA's rise/set via the closed-form HA arithmetic we already have in the chart code.
3. **Horizon-profile-aware rise/set.** Extension of `RiseAndSetEvent` that takes `IHorizonProfile` and does bracketed-solve (coarse sample + Newton refine).
4. **Session-quality integral.** `IntegratedQuality(target, loc, start, duration, qualityFunc) → double`. Closed form for `q = sin(alt)`; 20-point numerical integration for anything else.
5. **Best-session-placement.** `BestSessionFor(target, night, horizonProfile, minDur, maxDur, qualityFunc) → (start, end, quality)?`. Already prototyped in `BuildOptimalSeries` — extract and generalize.
6. **Quality samples over a night.** `QualitySamples(target, loc, night, slotSize, qualityFunc)` → array for the scheduler's interval solver to consume.
7. **Horizon profile abstraction.** `IHorizonProfile` interface with scalar, polyline, and table implementations. NINA has `CustomHorizon` (polyline) but no interface over it.
8. **Moon separation at time / over window.** `MoonSeparation(target, loc, utc) → degrees`. Compose `AstroUtil.GetMoonPosition` + `SOFA.Seps`.
9. **Configurable twilight.** NINA's `AstronomicalTwilightRiseAndSet` is hardcoded at −18°; we want `Twilight(loc, date, sunAltBelowDeg)` with any threshold.
10. **Interval scheduling solver** (scheduler's job, not library's). Greedy / weighted-interval DP consumer of the primitives above.

### Code quality and stability assessment

- Most files last modified October/November 2023. Stable, not churning.
- Immutable value types (`Angle`, `Coordinates` after construction, `Separation`). Some mutability remains (`TopocentricCoordinates`, `Coordinates` setters).
- Thread safety: NOVAS locked, SOFA unlocked, `ConcurrentDictionary`-based caches in `AstroUtil.DeltaUT()`, explicit `lock` in `NighttimeCalculator`.
- One `[Obsolete]` method (`GetMoonAltitude(date, lat, lon)`). No other deprecations.
- Rise/set has the single-rise/set limitation noted in code comments — this is worth fixing for polar-edge cases but isn't blocking.
- Async usage is mostly cosmetic (`.Calculate().Result` in places) — not a problem but means we can't treat `NINA.Astrometry` async boundaries as real back-pressure signals.

### Delta pass against NINA 3.2.x `develop` (SHA `0bc2986df`)

Re-verification of the claims above against the current develop tip. Nothing in the preceding sections was invalidated — all coordinate primitives, `AstroUtil` methods, `SOFA`/`NOVAS` surface, `RiseAndSetEvent` base + subclasses, the NOVAS `lock`, and the `CustomHorizon` polyline location are as described. **Changes worth folding into library planning:**

- **New `CivilTwilightRiseAndSet`** subclass (−6° threshold) at `NINA.Astrometry/RiseAndSet/CivilTwilightRiseAndSet.cs`. Marked `internal`, which means our adapter layer will need its own parameterized-sun-altitude variant rather than reaching in through NINA's type (still supports our "configurable twilight threshold" primitive cleanly).
- **New `AstroUtil.DeltaUT(DateTime, db) → double`** method exposing UT1-UTC. Previously only `DeltaT` was surfaced. Useful if we ever want to compute sub-second precise transit times.
- **New `NighttimeCalculator` / `TwilightCalculator`** higher-level aggregators that bundle civil / nautical / astronomical / sun / moon rise-set into a single query. Worth wrapping in our adapter layer as a faster path to "what's the night window for this location on this date." Matches the flavor of `ITwilightCalculator` which is now injected into the plugin mediator set.
- **Still missing (confirmed):** horizon-profile-aware rise/set, standalone transit-time primitive. These remain contributions of the new library.

---

## How NINA plugins actually work

Reconnaissance into `NINA.Plugin`, `NINA.Sequencer`, and the dependency injection layer.

### Plugin lifecycle

- Base class: `NINA.Plugin.PluginBase` (implements `IPluginManifest`). Plugins derive from it.
- Lifecycle hooks: `Initialize()` (Task), `Teardown()` (Task). Both optional, default to completed.
- Plugin metadata comes from assembly attributes: `[Guid]`, `[AssemblyFileVersion]`, `[AssemblyTitle]`, `[AssemblyCompany]`, `[AssemblyDescription]`, plus `[AssemblyMetadata("Key","Value")]` for License, Homepage, MinimumApplicationVersion, and several optional fields.
- Plugin loader: `NINA.Plugin.PluginLoader` — uses MEF to scan plugin assemblies for exports of the sequencer interfaces (`ISequenceItem`, `ISequenceContainer`, `ISequenceCondition`, `ISequenceTrigger`), dockable VMs (`IDockableVM`), pluggable behaviors (`IPluggableBehavior`), and equipment providers (`IEquipmentProvider`).
- Composition uses **Ninject** under the hood (see `CompositionRoot.cs`) with MEF feeding into it. Plugins get constructor-injected with every relevant mediator — Camera, Telescope, Focuser, FilterWheel, Guider, Rotator, FlatDevice, WeatherData, Imaging, ApplicationStatus, SafetyMonitor, Switch, Dome, plus planetarium, image history, DSO search, framing assistant, solver factory, window service, etc.

### Sequence model

- Root abstraction: `ISequenceEntity` (Name, Description, Icon, Category, Status, init/teardown hooks).
- Three main specializations:
  - `ISequenceItem` — leaf instruction: `Run(progress, token)`, `ResetProgress`, `Skip`, `GetEstimatedDuration`, `ErrorBehavior`, `Attempts`.
  - `ISequenceContainer` — composite: holds `IList<ISequenceItem>`, has `Iterations`, `ExecutionStrategy` (Sequential or Parallel), Add/Remove/MoveUp/MoveDown, `Interrupt` handler.
  - `ISequenceTrigger` — runs before/after items: `ShouldTrigger(prev, next)`, `ShouldTriggerAfter(prev, next)`, `Run(context, progress, token)`, `AllowMultiplePerSet` flag.
- `ISequenceCondition` — monitors an invariant during execution: altitude, time, weather, safety. Examples in-repo: `AltitudeCondition`, `AboveHorizonCondition`, `SunAltitudeCondition`, `MoonAltitudeCondition`, `MoonIlluminationCondition`, `TimeCondition`, `TimeSpanCondition`, `LoopCondition`, `SafetyMonitorCondition`.
- Execution strategies: `SequentialStrategy`, `ParallelStrategy` (in `NINA.Sequencer/Container/ExecutionStrategy/`).
- Tree-structured composition. Root container + children, executed via `NINA.Sequencer.Sequencer.Start(progress, token)`. Pre-execution `Validate(MainContainer)` collects `Issues` from all items.

### Built-in sequence items (a non-exhaustive list to orient the reader)

- Camera: `CoolCamera`, `WarmCamera`, `SetReadoutMode`, `SetUSBLimit`, `DewHeater`.
- Imaging: `TakeExposure`, `TakeManyExposures`, `SmartExposure`, `TakeSubframeExposure`.
- Telescope: `SlewScopeToRaDec`, `SlewScopeToAltAz`, `ParkScope`, `UnparkScope`, `SetTracking`, `FindHome`.
- Guider: `StartGuiding`, `StopGuiding`, `Dither`.
- Other: Dome, Rotator, FilterWheel, Focuser, FlatDevice, Platesolving, Autofocus, Switch, SafetyMonitor.
- Utility: `Annotation`, `ExternalScript`, `MessageBox`, `WaitForAltitude`, `WaitForTime`, `WaitForTimeSpan`, `WaitUntilAboveHorizon`, `WaitForMoonAltitude`, `WaitForSunAltitude`.

A scheduler plugin can compose these programmatically — construct an `ISequenceRootContainer`, populate children, hand to `ISequencer.Start()`.

### Target / DSO model

- `IDeepSkyObject` (`NINA.Astrometry.Interfaces.IDeepSkyObject`) — canonical NINA target abstraction. Properties: `Id` (database key, often Cartes du Ciel/Stellarium format), `Name`, `Coordinates` (RA/Dec), `DSOType`, `Constellation`, `Magnitude`, `PositionAngle`, `SizeMin`, `Size`, `SurfaceBrightness`, `Altitudes` (`List<DataPoint>`), `Horizon`, `MaxAltitude`, `DoesTransitSouth`.
- `CoordinatesAt(DateTime)` applies precession to the reference epoch.
- Our scheduler should consume `IDeepSkyObject` directly for target inputs rather than defining a separate abstraction.

### Dispatch: the key architectural question

Two patterns exist:

1. **Passive UI items.** Plugin exports `ISequenceItem` / `ISequenceContainer` via MEF. User drags them into their sequence via the Advanced Sequencer UI. At runtime, when the user clicks Run, NINA executes the root container and our items run in turn. **This is how Target Scheduler works.**
2. **Programmatic sequence construction.** Plugin gets `ISequenceMediator` injected; calls `SetAdvancedSequence(ISequenceRootContainer)` or `AddAdvancedTarget(IDeepSkyObjectContainer)` to populate the sequencer. User still has to click "Start" on the sequencer itself — there is no headless mode exposed.

**Implication:** The plugin cannot fully automate a night without user interaction (clicking Start on the sequencer). It can fully compose the night's sequence programmatically, though, which is enough for the interval-scheduling goal.

Target Scheduler's approach: a container (`TargetSchedulerContainer`, extends `SequentialContainer`) that at runtime calls its `Planner.GetPlan()` and builds its child instruction tree dynamically. User drags one instance of this container into their sequence; everything inside is generated by the plugin. We'd do something similar.

### Events and callbacks

- Mediators expose property changes via `BaseINPC` pattern — subscribe to `PropertyChanged`.
- `IImagingMediator` exposes `event EventHandler<ImagePreparedEventArgs> ImagePrepared`.
- `ISequenceEntity.Status` is the lifecycle signal (`SequenceEntityStatus` enum: Running, Completed, Failed, Skipped, etc.).
- **No global event bus for "target finished" etc.** Subscribe to individual mediators or watch container status.

### Profile / settings

- `IProfile.PluginSettings` (type `IPluginSettings`) — namespaced by plugin GUID. API: `TryGetValue<T>`, `SetValue<T>`, `TryGetTypeOfField`. DataContract-serialized to NINA's profile XML.
- **No SQLite or database support from NINA itself** — plugins that need heavy state (target database, image history) bring their own. Target Scheduler uses EF6 + SQLite at `%APPDATA%/NINA/Plugins/Target Scheduler/schedulerdb.sqlite`.
- Our plugin likely needs similar: small settings via `PluginSettings`, target/project database via SQLite (probably EF Core 6 to match .NET 8 target, unless we follow TS's EF6 choice).

### UI contribution

- `IDockableVM` exports — plugin WPF user control panels that appear in NINA's main window docked layout.
- Sequence items are just MEF-exported ISequenceItem classes with `[ExportMetadata]` attributes (Name, Description, Icon SVG resource name, Category localization key).
- No explicit plugin theming API; inherits NINA's WPF theme (`ColorSchemaSettings` on profile).

### Testing

- `NINATest` uses **NUnit + Moq**. Mocks constructed per `new Mock<ISequenceItem>().Setup(...).Object`.
- Plugin tests would follow the same pattern — mock the mediators, construct plugin classes in isolation.
- No dedicated "NINA plugin test harness" exists; plugin authors write unit tests against their own classes with mocked mediators.

### Gotchas worth flagging up front

- Startup order is MEF → Ninject → PluginLoader. Circular dependency between two plugins fails at init. Keep our plugin self-contained. *(Ninject replaced with Microsoft.Extensions.DependencyInjection on NINA 3.2.x develop — see delta block below.)*
- All `Run()` methods are async `Task`. Blocking will freeze the UI because NINA awaits plugin items.
- Background-thread work must use `Dispatcher.Invoke` before touching WPF UI.
- Equipment mediators are singletons; `GetInfo()` returns null when the equipment is disconnected — plugins must null-check, not blindly call methods.
- Pre-start validation: items must populate their `Issues` collection; if any issues exist, the sequencer aborts before running.
- All items are `ICloneable` and `[JsonObject(OptIn)]`. Custom `Clone()` implementations needed; serialization is deliberate opt-in on fields with `[JsonProperty]`.
- MEF only finds types with `[Export]`. Plain class implementations are invisible.
- Conditions run continuously during item execution; long-running item bodies block condition checks.

### Delta pass against NINA 3.2.x `develop` (SHA `0bc2986df`)

Plugin-API verification against the current develop tip. `PluginBase` / `IPluginManifest` lifecycle, `PluginLoader` MEF discovery mechanics, `IPluginSettings` API, `IDockableVM` UI contribution, and sequence-item `[ExportMetadata]` keys are all **unchanged**. The following have shifted and matter to scheduler-plugin design:

- **IoC container: Ninject → Microsoft.Extensions.DependencyInjection.** `IoCBindings.cs` now returns an `IServiceProvider`. Plugins that relied on Ninject's `IKernel` directly would have broken. The MEF boundary is unaffected — plugins still `[Export]` and receive `[ImportingConstructor]` injection — so our plugin stays MS.DI-agnostic as long as we don't reach into the container ourselves.
- **New MEF contract: `ISequenceEntityUpgrader`** (`PluginLoader.cs` PartsImport line 759). Plugins can export upgraders via `[Export(typeof(ISequenceEntityUpgrader))]` to migrate their serialized sequence items across versions (stages: BeforeCreate, Create, AfterCreate, AfterPopulate). Our scheduler container's JSON state needs to be upgrade-safe across plugin versions — implementing `ISequenceEntityUpgrader` from v2 onwards is the sanctioned path.
- **New injected mediators** (not an exhaustive list of additions — flagging what's relevant):
  - `ITwilightCalculator` — parameterizable twilight computation; probably the right place to route our configurable-sun-altitude needs rather than duplicating the logic.
  - `IImageSaveMediator` — hooks into image-save pipeline; scheduler can update completion state (`TargetProgress`) from here without polling.
  - `IMessageBroker` — async pub/sub across plugins. `Task Publish(IMessage)`, `Subscribe(topic, subscriber)`. Opens the door to publishing scheduler events (e.g., "target X completed," "replanned because of cloud alert") that other plugins can consume.
  - `ISymbolBroker` — assigned to every sequence item; new symbol-based coordination surface.
  - `ISequenceMediator`, `IOptionsVM`, `IExposureDataFactory`, `IDomeSynchronization` — also new injections.
  - Total mediator count roughly doubled (~12 → ~25) since the original snapshot. Plan on constructor signatures getting long.
- **New plugin metadata key: `AltScreenshotURL`** (assembly attribute `[AssemblyMetadata("AltScreenshotURL", "...")]`). Cosmetic; supports alternate screenshot for light/dark theme.
- **`ISequenceMediator` expanded significantly.** New methods that matter to a scheduler:
  - `AddSimpleTarget(IDeepSkyObject)` — path into the Simple sequencer, which we'd probably ignore in favor of the advanced path.
  - `SaveContainer(ISequenceContainer, filePath, token)` — persist a plan to disk. Useful for "save tonight's generated plan" UX.
  - `StartAdvancedSequence(skipValidation)` — **the programmatic dispatch entry point**. `skipValidation=true` is the right call for scheduler-generated plans that pass our internal validation but might not satisfy NINA's per-item `Issues` collection.
  - `CancelAdvancedSequence()`, `IsAdvancedSequenceRunning()`, `GetAdvancedSequencerCurrentRunningItems()` — runtime control and introspection. The scheduler's replanning loop subscribes to these to detect interruption.
  - Events: `SequenceStarting`, `SequenceFinished` — lifecycle hooks for "night started"/"night ended" that the scheduler uses to kick off its planner and finalize `AcquiredImage` records.
- **`ISequenceRootContainer` gained `GetCurrentRunningItems()` and `FailureEvent`** — the real-time introspection surface that makes runtime replanning cheap. Subscribe to `FailureEvent` to trigger plan regeneration when an item fails; use `GetCurrentRunningItems()` to know where in the plan we are.
- **New condition `LoopWhile`** — expression-based loop guard (`NINA.Sequencer/Conditions/`). Not directly relevant to scheduler logic but worth knowing it exists for anyone composing schedule-adjacent user instructions.
- **`IDeepSkyObject` additions:** `ShiftTrackingRate`, `ShiftTrackingRateAt()`, `AlsoKnownAs`, `RotationPositionAngle`, `SetDateAndPosition()`, `Image` (BitmapSource). Old `Rotation` is deprecated in favor of `RotationPositionAngle`. If our plugin persists target records we should store `RotationPositionAngle`, not `Rotation`.
- **Expression-system sequence items** (`Constant`, `Variable`, `GlobalConstant`, `GlobalVariable`, `ResetVariable`, `ResetVariableToDate`) and full flat-device / dome / connect-equipment suites are new built-in items. Not directly scheduler concerns but part of the landscape a user's sequence might contain around our container.

**Takeaway for plugin design:** the programmatic-dispatch story got materially better since the original recon. `StartAdvancedSequence(skipValidation)` + `SequenceStarting`/`SequenceFinished` events + `FailureEvent` + `GetCurrentRunningItems()` is enough to build a genuine replanning loop without user clicks beyond the initial Start. Plus `IMessageBroker` gives us a clean way to emit scheduler events to other plugins if we ever want that. The two architectural strings attached: our plugin must implement `ISequenceEntityUpgrader` to survive plugin-version upgrades gracefully, and we should route twilight through `ITwilightCalculator` rather than `AstronomicalTwilightRiseAndSet` directly.

---

## Target Scheduler as a reference: patterns to adopt, gaps to address

Findings from the Target Scheduler plugin source (~13 k LOC, read in full via the reconnaissance pass).

### Conventions worth mirroring

- **MEF + `IPluginManifest`** entry point via `TargetScheduler.cs`. Assembly attributes carry all metadata. Standard NINA pattern.
- **Four-project split.** Main plugin + shared utility + optional sync service + test. We may skip the sync service initially but the rest is a sensible organization.
- **Post-build auto-copy to NINA plugins folder.** Saves the dev-loop friction. Copy their MSBuild targets.
- **NUnit 4 + FluentAssertions + Moq** in the test project. Custom `Assertions.AssertTime()` tolerance helper for DateTime comparisons. Test folder mirrors main project folder. Adopt.
- **`TSLogger` for logging** — TS's shared-logger pattern. We'll want something equivalent rather than plain `Console.WriteLine`.
- **EF6 + SQLite for durable state.** Schema in `/Database/Schema/*.cs`, numbered SQL migrations in `/Database/Migrate/{N}.sql`. Context is `SchedulerDatabaseContext`. Factory is `SchedulerDatabaseInteraction`. We'll follow similar structure, possibly upgrading to EF Core if NINA tolerates mixed EF versions.

### TS architecture (for comparison)

- **Planner.cs** orchestrates scheduling. Cascading filter pipeline: incomplete → visible → moon-avoidance → twilight → humidity. Then score among ready targets via `ScoringEngine`.
- **Nine scoring rules** (reflectively registered), all per-target:
  - `PercentCompleteRule`, `ProjectPriorityRule`, `SettingSoonestRule`,
  - `MeridianWindowPriorityRule`, `MeridianFlipPenaltyRule`,
  - `TargetSwitchPenaltyRule`, `SmartExposureOrderRule`, `MosaicCompletionRule`.
- **Weights are per-project**, persisted in `ProfilePreference.RuleWeight` EF entity. User-configurable in UI.
- **Data model**: Projects contain Targets contain ExposurePlans. ExposurePlans reference reusable ExposureTemplates. FilterCadence tracks per-target filter ordering and dither state. AcquiredImages table records completion.
- **Dispatch**: `TargetSchedulerContainer` is the user-draggable container. Internally builds `PlanContainer` → `InstructionContainer` → `SchedulerTakeExposure` subtree per planning cycle. `TargetSchedulerCondition` is the step that invokes `Planner.GetPlan()`.
- **Astrometry**: TS explicitly does **not** roll its own math. Consumes `NINA.Astrometry` throughout — `Coordinates`, `ObserverInfo`, `Epoch`, altitude/azimuth helpers. This validates the same choice for us.
- **UI**: `DatabaseManagerVM` (project/target CRUD), `PlanPreviewerViewVM` (nightly schedule preview), `ProfilePreferencesViewVM` (rule weights + thresholds), `SchedulerProgressVM` (runtime monitoring), `ReportingVM` (acquisition analytics and quality grades).
- **Quality grading**: `ImageGrader` assesses HFR / stars / RMS on acquired images. Doesn't feed back into scheduling decisions (see gaps below).
- **Sync**: `SyncService` is a gRPC service over named pipes for coordinating multiple NINA instances running against the same database. Elegant but ambitious; we'll skip initially.

### Where TS is underserved (these are specifically the gaps this plugin will address)

Verbatim from the reconnaissance:

1. **No interval-based scheduling.** Pure score-at-decision-time. Can't say "do target X every 2 hours" or "rotate through 3 targets every night." Each cycle is independent.
2. **Limited time-bounded constraints.** Meridian window is binary (inside/outside). No soft time windows like "prefer before 2 am" or "must start by midnight."
3. **No multi-target optimization.** Nine scoring rules are rich but all per-target. Can't express "image A, then B, then A again in same session" or "stagger targets to minimize filter switching cost across all." `SmartExposureOrderRule` exists but only reorders within a target's exposures.
4. **No weather forecasting.** Humidity-based rejection is real-time only. Can't plan around forecast rain or rising humidity trends.
5. **Image quality feedback loop is weak.** `ImageGrader` grades completed images but scoring ignores quality history. Can't say "skip this target if last 3 images had HFR > 3" or "prioritize high-confidence targets."
6. **No failure recovery.** If a target acquisition fails (slew timeout, guide lock lost), replanning is dumb — same score → likely same target selected again. No exponential backoff or "temporarily reject after N failures."
7. **Filter cadence is opaque.** Modeled as a state machine (`FilterCadence`, `OverrideExposureOrderItem`), not as a composable schedule. User manually specifies per-target order; hard to express "2 nights L, 1 night RGB" patterns declaratively.
8. **No fairness/load-balancing.** If 5 targets are equally ready, score decides. No built-in "round-robin if tied" or "prefer least-recently-imaged."

Our interval-scheduling approach naturally addresses (1), (2), (3), (8), gets a partial answer on (6) by re-planning on failure with updated state, and sidesteps (4)/(5)/(7) by being agnostic to those concerns (they're features a more sophisticated policy layer can add).

---

## The library vs. the scheduler: separation of concerns

The single most important structural decision in the design. Spelled out:

### What the astrometry library does

**Pure functions of (location, target, time, horizon).** No state, no scheduling policy, no opinions about what's "best" — just the math of where objects are, when they rise/set/transit, and how much quality-integrated-time is achievable for a given session.

Concretely, the library answers questions like:

- "Where is target X at UTC t, seen from location L?"
- "When does target X transit on the night of d from location L?"
- "What are target X's above-horizon windows during the night of d, given horizon profile H?"
- "What's the quality integral ∫q(alt(t))dt over the interval [t₁, t₂] for target X at location L?"
- "What's the best D-hour session for target X on night n? Returns (startUTC, endUTC, qualityScore)."

The library knows nothing about:
- Which targets matter more than others.
- User preferences about moon avoidance, meridian flips, filter choices.
- Equipment state, weather, or forecast.
- What a "good" plan looks like — it only answers "what's achievable?"

### What the scheduler does

**Combinatorial optimization over astrometry outputs, parameterized by user policy.** The scheduler consumes the library and produces plans (ordered, non-overlapping target assignments) that maximize a chosen objective subject to user-defined constraints.

Concretely, the scheduler:

- Accepts a list of targets with per-target policy (priority, required integration time, filter, moon-separation tolerance, etc.).
- Queries the library for each target's visibility windows and quality profiles.
- Solves an interval-scheduling problem to produce a plan.
- Re-plans when execution state changes (target completed, cloud interruption, etc.).

The scheduler knows nothing about:
- How to compute altitude or airmass.
- How twilight is defined.
- How the horizon profile is represented.

### Why this matters

- **The library is unit-testable with astronomy-textbook ground truth.** M31 transits at 89° from Penns Park on Oct 15 — known answer.
- **The scheduler is unit-testable with mocked library outputs.** Give it a fake `VisibilityWindows` for three targets with specified shapes; verify it produces the expected assignment.
- **Either side can be rewritten.** Swap the solver; library doesn't care. Swap the astrometry backend; scheduler doesn't care.
- **The library is reusable** for the UI chart, for XisfManager, for scripting, for the NINA plugin — anything that wants astrometry without the scheduler's opinions.

### Adapter layer for NINA integration

Because NINA.Astrometry already supplies coordinate primitives, ObserverInfo, altitude calculations, etc., the new library is probably structured in two layers:

1. **Core analytics layer.** Knows nothing about NINA. Takes primitive inputs (lat, lon, dec, RA, times, horizon function). Computes transit times, visibility windows, integrated quality, best-session-placement. Pure C# with no external dependencies. Unit-testable with no NINA references.
2. **NINA adapter.** Thin layer that accepts NINA types (`Coordinates`, `ObserverInfo`, `NighttimeData`, `CustomHorizon`) and calls into the core analytics layer, returning NINA types (or types defined by the plugin).

This lets the core analytics layer be consumed by the Ephemerides tool (currently not on NINA) without dragging NINA.Astrometry in, while letting the plugin benefit from NINA's coordinate transforms and ObserverInfo without duplicating.

The trade-off: some duplication at the boundary between the two layers. Accept it — clarity beats DRY at architectural scale.

---

## Objective function: what "best elevation × time" means

Stated objective: "maximize integration time of an object at best elevation." Making that computable requires a quality function `q(alt)` mapping altitude in degrees to a weight per unit time.

### Quality function options

| Model | Formula | Rationale |
|-------|---------|-----------|
| Time-only | `q(alt) = 1` if alt ≥ gate, else 0 | Just count minutes above threshold |
| Reciprocal airmass | `q(alt) = sin(alt)` | Linear in integration efficiency for plane-parallel atmosphere |
| SNR proxy | `q(alt) = √sin(alt)` | Background-limited SNR for sky-dominated noise |
| Extinction | `q(alt) = 10^(−0.2·k·(X−1))`, `X = 1/sin(alt)` | Magnitude loss through atmosphere; k = extinction coefficient (~0.15 at good sites) |

All are monotonic in altitude. For the simplest amateur framings, `sin(alt)` is probably the right default: physically motivated, computationally clean, integrates in closed form.

**Design decision:** the quality function should be a plug-in parameter (`Func<double, double>`), not hard-coded. Default to `sin(alt)` but let power users substitute.

### The key integral

For a stellar target with declination δ seen from latitude φ, altitude as a function of hour angle HA is:

```
sin(alt(HA)) = sin(φ)·sin(δ) + cos(φ)·cos(δ)·cos(HA)
```

So for quality function `q(alt) = sin(alt)`, the integrated quality over a session [HA₁, HA₂] has a **closed form**:

```
∫ sin(alt(HA)) dHA  =  sin(φ)·sin(δ)·(HA₂ − HA₁)
                     + cos(φ)·cos(δ)·(12/π)·[sin(HA₂·π/12) − sin(HA₁·π/12)]
```

Where HA is in sidereal hours. No numerical integration required. The library can compute session quality for any (target, location, start, duration) in a handful of trig ops.

For other quality functions (airmass reciprocal, extinction) the integral either has a closed form or degrades to cheap numerical integration (Simpson's rule over 10–20 points per session is microseconds). Either way, no performance issue.

### Consequence for scheduling

The scheduler can evaluate `integratedQuality(target, start, duration)` thousands of times during optimization without performance concern. This is the primitive that makes interval-scheduling tractable at interactive latency.

---

## Proposed astrometry library surface (revised)

Dividing by "consume from NINA.Astrometry" vs. "add in the new library." Everything in the "add" group is the new library's contribution; everything in "consume" means the new library delegates or re-exports.

### Consume from `NINA.Astrometry` (do not reinvent)

- `Coordinates`, `TopocentricCoordinates`, `Angle`, `Separation`, `ObserverInfo`, `Location` — coordinate primitives.
- `AstroUtil.GetJulianDate(DateTime) → double`.
- `AstroUtil.GetLocalSiderealTime(date, longitude) → hours`.
- `AstroUtil.GetHourAngle(siderealTime, ra) → Angle`.
- `AstroUtil.GetAltitude(hourAngle, latitude, declination) → Angle` — replaces our ad-hoc `AltAtHa`.
- `AstroUtil.Airmass(altitude) → double` — use for default `sin(alt)` quality computation and diagnostic displays.
- `AstroUtil.GetMoonPosition(date, jd, observer)`, `GetMoonIllumination(date)`, `GetMoonAltitude(date, observerInfo)`, `GetMoonPhase(date)` — moon data.
- `SOFA.Seps(ra1, dec1, ra2, dec2) → radians` — for moon-separation composition.
- `RiseAndSetEvent` hierarchy (base + twilight + sun + moon subclasses) — rise/set for stars/bodies via `CustomRiseAndSet`.
- `CustomHorizon` (in `NINA.Core.Model`) — polyline horizon representation. Adapter converts to our `IHorizonProfile`.
- `IDeepSkyObject` — target abstraction. Our scheduler's target type either is `IDeepSkyObject` or wraps it.

### Add in the new library

- `interface IHorizonProfile` — `double AltitudeAt(double azimuthDeg)`. Implementations: `ScalarHorizon`, `PolylineHorizon` (wraps `CustomHorizon`), `ObstructionTableHorizon` (360° user-entered).
- `TransitUtc(target, date, loc) → DateTime` — analytic inverse of `LST = RA` on the given date's night.
- `LowerCulminationAltitude(latDeg, decDeg) → degrees` — min altitude, mirror of `MeridianAltitude` (which NINA.Astrometry doesn't expose directly; we can compute from `AstroUtil.GetAltitude` with HA=0).
- `RiseSetUtc(target, night, loc, horizonProfile) → (DateTime?, DateTime?)` — horizon-profile-aware rise/set. Internally uses the scalar fast-path as a seed, refines against the profile at the candidate rise/set azimuth via Newton iteration.
- `VisibilityWindows(target, night, horizonProfile) → IReadOnlyList<(DateTime start, DateTime end)>` — above-horizon intervals intersected with the night window. Typically 0–2 segments per night.
- `IntegratedQuality(target, loc, startUtc, duration, qualityFunc) → double` — closed-form for `q = sin(alt)`; Simpson over 20 points for arbitrary `q`.
- `BestSessionFor(target, night, horizonProfile, minDur, maxDur, qualityFunc) → (DateTime start, DateTime end, double quality)?` — transit-centered when the visibility window allows; wall-pushed when it doesn't.
- `QualitySamples(target, loc, night, slotSize, qualityFunc) → IReadOnlyList<(DateTime start, DateTime end, double qualityPerHour)>` — sampled grid the scheduler consumes.
- `MoonSeparation(target, loc, utc) → degrees` — composition of `GetMoonPosition` + `SOFA.Seps`.
- `MoonSeparationIntervals(target, night, minSepDeg) → IReadOnlyList<(start, end)>` — intervals during the night where separation ≥ threshold.
- Configurable twilight: `ComputeNight(loc, date, sunAltBelowDeg = −18.0) → NightWindow`. Parameterized version of the hardcoded NINA twilight classes.

### Scheduler-side constructs (in the plugin, not the library)

- `interface IObservabilityConstraint` — composable observability predicates. Implementations: `AltitudeConstraint`, `AirmassConstraint`, `MoonSeparationConstraint`, `AtNightConstraint`, `AboveHorizonProfileConstraint`. Mirrors astroplan's pattern.
- `Plan` — ordered, non-overlapping (target, start, end, quality) assignments.
- `TargetProgress` — what has been imaged so far per target (completed TimeSpan, goal TimeSpan). Feeds back into replanning.
- `ISchedulingPolicy` — user policy (meridian-chase, narrow-window, keep-busy) expressed as objective weights and constraint composition.
- `GreedyIntervalScheduler` (initial implementation) — the solver.

### Thread safety and time representation

- **New library should be pure / instance-based / no static state.** Current `Astrometry.Location(...)` in the Ephemerides codebase mutates static fields for UI consumption; don't carry that forward. Mark it deprecated in Ephemerides.
- **Inherit NINA's `DateTime` UTC convention** since NINA.Astrometry uses it. Enforce UTC at library boundaries via method contracts; internal calculations use Julian Date.
- **Be aware of the NOVAS lock.** If the scheduler ever wants to parallelize per-target quality sampling across cores, route through SOFA where possible, or accept the serialization cost.

---

## Scheduler modes mapped to primitives

All three modes are **weighted interval scheduling over visibility windows with per-target quality functions**. They differ only in how weights and constraints are set.

### Meridian-chase

**Intent:** Maximize number of targets imaged near transit, accepting shorter sessions per target.

**Scheduler behavior:**
- Use transit-centered session preference: `BestSessionFor` with a "centered only" constraint that returns `null` if a transit-centered session doesn't fit.
- Sort candidate sessions by transit time.
- Greedy assignment in transit order; break ties by session quality.

**Primitives used:** `TransitUtc`, `VisibilityWindows`, `BestSessionFor(centered=true)`.

### Narrow-window capture

**Intent:** Catch targets that have short above-horizon windows; prioritize them because everything else has slack.

**Scheduler behavior:**
- Compute `VisibilityWindows` for all targets.
- Sort targets by max window length ascending (shortest visible = most constrained).
- Schedule shortest-window targets first; they're the hard constraints.
- Fit remaining targets into leftover slots.

**Primitives used:** `VisibilityWindows`, `BestSessionFor`, `IntegratedQuality` for tie-breaking.

### Keep-camera-busy

**Intent:** Maximize total quality-integrated time across the night.

**Scheduler behavior:**
- For each target, compute `QualitySamples` over the night at a chosen slot size (e.g., 10 minutes).
- Formulate as weighted interval scheduling: each target has a continuum of possible sessions parameterized by start time; pick non-overlapping sessions to maximize Σ quality.
- **Solver options**:
  - **Greedy with rollback** — sort targets by peak-quality-attainable descending; assign each its best still-available window; allow one-pass swap refinement. O(n² log n) worst case. Usually near-optimal for amateur scale (n < 30).
  - **Weighted interval scheduling DP** — classic O(n log n) if session durations are fixed per target.
  - **MILP** — full optimality; overkill for amateur scale; use Microsoft.SolverFoundation or similar. Useful if you want guaranteed-optimal plans; excessive otherwise.

**Primitives used:** `QualitySamples`, `BestSessionFor`, `VisibilityWindows`.

### Why this convergence is good

The three modes sharing primitives means: **one well-tested scheduler engine, three objective-weight configurations on top**. User choosing "meridian chase" vs "keep busy" via a dropdown really is just swapping objective weights. No mode-specific code path to maintain.

---

## Plugin integration architecture (NINA-specific)

### Dispatch model

Follow Target Scheduler's pattern: the plugin exports **one `ISequenceContainer` that the user drags into their sequence once**. At runtime the container calls into the scheduler's planner to produce a plan and then dynamically populates its child item list with the planned instructions.

The user's interaction model:

1. Install plugin.
2. Open the plugin's dockable UI panel (`IDockableVM`). Configure targets, projects, exposure plans, policy preferences.
3. Build a NINA sequence that includes the plugin's container at the top level (plus pre-/post-flight boilerplate: cool camera, set tracking, unpark, autofocus, safety triggers).
4. Click Start. The container queries the planner, constructs the night's plan, executes it, and re-plans on interruption or completion.

This is strictly conformant to NINA's passive-UI-item model; we don't need headless-mode hacks.

### Durable state

Same as TS: SQLite at `%APPDATA%/NINA/Plugins/<PluginName>/db.sqlite`. We use either EF6 (to match TS) or EF Core 6+ (if the ecosystem is friendly to it). Schema is ours to design — recommend:

- `Project` — name, priority, active/inactive, per-project policy overrides.
- `Target` — RA/Dec (J2000), name, project link, per-target policy overrides, custom horizon (optional override).
- `ExposurePlan` — target, filter, required time, completed time.
- `AcquiredImage` — timestamp, target, filter, quality metrics (HFR, stars, RMS) for quality-feedback-loop future enhancement.
- `HorizonProfile` — user's 360° obstruction table if not using NINA's `CustomHorizon`.

Migrations versioned with numbered SQL files à la TS.

### Plugin-wide settings

Small settings (slot size, default policy, UI preferences) via `IPluginSettings` keyed by plugin GUID. Large state (target database, acquired images) via SQLite as above.

### Dependency injection

MEF `[Export]` on plugin entry class; `[Export(typeof(ISequenceContainer))]` on our scheduler container; `[Export(typeof(IDockableVM))]` on our UI panels. Mediators (`ITelescopeMediator`, `ICameraMediator`, `IProfileService`, `INighttimeCalculator`, etc.) injected via `[ImportingConstructor]`.

### Testing

NUnit + Moq + FluentAssertions (match TS). Folder structure mirrors the main plugin. Custom tolerance helpers for DateTime/Angle comparisons. Mock NINA mediators; feed synthetic data to the planner; assert plan shapes.

### Deployment

Post-build step copies plugin DLLs into `%LOCALAPPDATA%/NINA/Plugins/<PluginName>/`. Debug launches NINA with our plugin loaded. Match TS's MSBuild targets.

---

## Gaps / things to not forget

Features not yet explicitly asked for but that every practical scheduler eventually needs. Flagged now so they don't become retrofits.

1. **Moon separation constraint.** Already in the library plan; must also be in the scheduler's default constraint stack (broadband imaging minimum ~40°; narrowband can tolerate 20°+).
2. **Meridian flip handling (GEM mounts).** Library primitive: `MeridianFlipTime(target, session, loc) → DateTime?`. Scheduler options: finish session entirely east, entirely west, or flip mid-session with configurable pause duration.
3. **Configurable twilight.** Parameterize sun-altitude threshold — broadband wants −18° (astronomical), some narrowband tolerates −12° (nautical).
4. **Airmass convention.** Use `AstroUtil.Airmass` for plain-refraction case; consider Kasten-Young if users start imaging down to 20° or below.
5. **Extended-target extent.** Current model is point-like (RA/Dec). M31 is 3° wide; its edge touches the horizon before its center does. Most schedulers ignore this; defer but know it's a choice.
6. **Time-of-night quality weighting.** Integrated-quality framing handles this automatically, but name the scoring value clearly (`IntegratedQuality`, not `Duration`).
7. **Priority / completion state.** Scheduler inputs (not library). `BestSessionFor` accepts `minDuration` / `maxDuration` so the scheduler can request partial sessions.
8. **Pier side persistence.** Scheduler concern: if a target has both a west and east session available in the same night, prefer imaging both on the same pier side to avoid the flip cost.
9. **Slew/settle time between targets.** Real-world cost. Scheduler budgets fixed cost per target transition. Astrometry library doesn't know about this.
10. **Filter changes.** Filter wheels have non-zero switch time; schedulers often prefer contiguous filter runs. Policy layer.
11. **Refraction in rise/set.** NINA's rise/set doesn't apply refraction; we may want to in the horizon-profile-aware rise/set — typically adds ~35' to the rise time and symmetrically to set.
12. **Polar edge cases.** NINA's rise/set algorithm explicitly doesn't handle polar regions. If we plan for users in Norway/Alaska/Antarctica we need to detect "target above horizon all night" / "target below all night" and handle gracefully.
13. **ΔT / UT1-UTC database.** NINA.Astrometry relies on an EOP database. Our adapter must make sure this is properly initialized; verify the data path works in a plugin context.
14. **NOVAS serialization.** Heavy parallel scheduler workloads should not hot-loop `NOVAS.Place()`. Use SOFA paths or accept the lock.

---

## Trade-offs and caveats

### Plan stability

Tight optimization over small changes in constraints can flip between wildly different plans. If plans are shown to users, add a **penalty for plan change** term relative to the previous plan to keep visualizations stable when knobs are nudged. Not an astrometry concern; a UX one.

### Combinatorial scaling

- Greedy: O(n² log n) in number of targets, trivial for amateur scale.
- DP with fixed durations: O(n log n).
- DP with continuous session starts discretized: O(n · T/Δt · n). At Δt=10 min and T=10 h, that's 60 × n² — tens of thousands for n=20. Still fast.
- MILP: exponential worst case; pragmatic solvers handle amateur-scale instances in seconds.

Start greedy; upgrade only if greedy fails in observed edge cases.

### Asymmetric costs

Imaging equipment performs setup at session start: autofocus, meridian flip checks, calibration images, dither initialization. A 6-hour single-target session has lower overhead ratio than two 3-hour sessions. Real schedulers add per-session-switch cost: `Q_T = ∫quality dt − switchingCost`. Parameterize in the scheduler (not library), default to zero for pure astronomical analysis.

### Integrated quality vs. instantaneous quality

The integrated-quality framing penalizes off-transit placement continuously. The `OptimalFloor` chart in Ephemerides uses a hard-cutoff floor altitude — a cruder summary. UI and scheduler must agree on what "better" means; showing both a Floor chart and a scheduled plan that disagrees confuses users.

Spot-check the planner on known-good scenarios early: "should this pick transit-centered M31 over an off-transit NGC 7000?" If your intuition disagrees with the solver's plan, the quality function probably needs a nonlinear bump (`sin²(alt)` penalizes low altitudes more aggressively).

### Re-planning on execution state change

Scheduler must accept a "what's already been imaged" state and produce a plan that respects it. Interface sketch:

```csharp
struct TargetProgress { Target target; TimeSpan completed; TimeSpan goal; double completionBonus; }

Plan Schedule(
    IReadOnlyList<Target> targets,
    Location location,
    NightWindow night,
    IHorizonProfile horizon,
    IReadOnlyList<TargetProgress> progress,    // <-- the state
    ISchedulingPolicy policy
);
```

Every material execution state change (target finished, 20 minutes lost to clouds, fresh meridian flip) regenerates the plan. Cheap for amateur scale (sub-second). Scheduler stays stateless w.r.t. execution artifacts.

### The "what a user thinks is best" problem

Users often disagree with mathematically-optimal plans. "Why did it pick X when Y is brighter?" — because Y is lower in the sky and the quality integral says X has higher SNR efficiency. Mitigations:
- **Show the per-target scoring.** For each assigned session, display its integrated quality number.
- **Allow "must image" overrides.** Let users pin targets to preferred slots. Scheduler optimizes around pinned choices.

### NINA version drift

NINA is a moving target. Plugin API stability is not guaranteed across major versions. MinimumApplicationVersion in the plugin manifest is a contract; regression-test our plugin against each new NINA release.

### EF6 vs. EF Core

TS uses EF6. If we adopt EF Core, the plugin will run in a separate EF context from any other plugin that uses EF6 against the same SQLite file. Unlikely to matter (each plugin has its own DB), but worth flagging if plugins ever share storage.

### Planet / comet targets

Stellar primitives assume fixed RA/Dec. Moon/planets/comets have non-fixed coordinates. NINA can compute moon positions; planet positions come from NOVAS; comet/asteroid positions need orbital-element interop we don't have today. For v1 we restrict to stellar targets. Moon imaging and planetary imaging users are currently underserved by this plugin design; a later expansion can add the variants.

### Thread safety of the new library

Core analytics layer should be pure functions — no shared mutable state, no caches that aren't thread-local or immutable-after-build. Test with parallel invocations. NINA.Astrometry's `ConcurrentDictionary` caches and the NOVAS `lock` are already thread-safe at that level.

---

## References worth studying

- **astroplan** (Python, astropy). Open-source reference for this exact problem shape. Study the `Observer` / `FixedTarget` / `Constraint` / `Scheduler` class decomposition. The constraint composition pattern (`constraints = [c1, c2, c3]`; scheduler evaluates the conjunction) is worth mirroring in C# via `IObservabilityConstraint`.
- **NINA's Target Scheduler plugin** — full source at `E:\Projects\VisualStudio\Astronomy\TargetScheduler_Clone\nina.plugin.targetscheduler`. Same platform, same user population, different (score-based) architecture. Study to understand what users currently expect and where it falls short.
- **LSST Feature-Based Scheduler** (Python, open source). Larger scale; same underlying "objective function over visibility windows" philosophy. Architectural inspiration, not direct code reuse.
- **LCO MILP scheduler** (papers only; internal code). MILP formulation well-documented in the literature. Useful if you ever go down the formal-optimization path.
- **NINA.Astrometry source** at `E:\Projects\VisualStudio\Astronomy\NINA\NINA.Astrometry`. Primary reference for coordinate / time / rise-set / moon / sun primitives. Treat as the canonical implementation of these concepts.
- **NOVAS31** (NIST C library) and **SOFA** (IAU C library) reference docs. The native DLLs bundled with NINA; their public APIs are what the managed wrappers expose.
- **CoordinateSharp** (currently in the Ephemerides repo's `packages.config`). Redundant once we adopt NINA.Astrometry; can be dropped from the plugin but retained in Ephemerides until that tool is migrated.

### Caveats on the landscape description

- **NINA TS internals** were read in detail during reconnaissance; findings reflect current source (one PR behind upstream release).
- **Voyager RoboTarget specifics** not studied directly; flip-timing specifics unclear.
- **ACP scheduler.** Known to exist, pro/semi-pro oriented; not studied.
- **Failure modes of amateur MILP attempts.** No awareness of any amateur-targeted MILP scheduler. One may exist that wasn't encountered.

---

## Suggested next steps

In rough priority order:

1. **Pin the objective function default.** Start with `q(alt) = sin(alt)`. Verify it produces plans that match intuition on a handful of test scenarios (M31 / M42 / NGC 7000 mix over a winter night). If not, try `√sin(alt)` or extinction-based.

2. **Prototype `BestSessionFor` standalone.** Pull the transit-centered-vs-wall-pushed logic from the current `BuildOptimalSeries` into a standalone function that returns `(start, end, quality)`. Wire it against the existing `NightCacheEntry` and validate against the charts.

3. **Design the `IHorizonProfile` interface.** Build `ScalarHorizon`, `PolylineHorizon` (wrapping NINA's `CustomHorizon`), `ObstructionTableHorizon` implementations. Convert all horizon-related primitives to take `IHorizonProfile`. Verify the existing Ephemerides chart still works with a `ScalarHorizon` wrapper.

4. **Write a greedy interval scheduler prototype.** Standalone C# library, no UI. Consumes mocked `QualitySamples`. Produces a `Plan` output. Test it on three scenarios (meridian chase, narrow windows, keep busy) with hand-constructed target inputs.

5. **Extract the core analytics layer.** Pure-C# library project with no dependencies. Primitives: transit solver, visibility windows, integrated-quality, best-session. Unit tests against textbook ground truth. Neither NINA nor CoordinateSharp needed.

6. **Build the NINA adapter layer.** Wraps NINA.Astrometry inputs and delegates to the core analytics layer. Depends on NINA assemblies.

7. **Build the NINA plugin scaffolding.** Empty plugin that loads, shows a dockable panel, exports an empty container. Tests the deployment + load cycle.

8. **Wire up the plugin's scheduler container.** The `TargetSchedulerContainer`-equivalent that calls into the scheduler's planner and constructs the instruction subtree dynamically.

9. **SQLite schema + EF context.** Projects, Targets, ExposurePlans, AcquiredImages. Migrations versioned with numbered SQL files.

10. **Moon separation primitives and constraint composition.** First enhancement beyond the minimum viable scheduler.

11. **Meridian flip handling** for GEM users.

12. **UI polish.** Plan-preview timeline (TS's `PlanPreviewerViewVM` as visual reference), target database management, policy configuration, runtime monitoring.

Each step is independently valuable. Even if only steps 1–3 land, the existing Ephemerides tool gets meaningfully cleaner. The plugin is a later deliverable that compounds on top.

---

## One-sentence summary

**A deterministic, replay-safe interval scheduler whose objective is `Σ ∫quality(altitude(t)) dt` over non-overlapping assigned sessions, implemented as a pure-C# core analytics library plus a thin NINA.Astrometry-backed adapter, packaged as a NINA plugin whose user-facing form is one sequence container, and whose conceptual contribution over Tommy Oldham's Target Scheduler is the move from score-at-decision-time to interval scheduling.** Architecturally cleaner than every amateur tool the author is aware of; scoped-down version of what LSST does.
