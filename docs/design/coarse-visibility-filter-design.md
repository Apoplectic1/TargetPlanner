# Coarse Visibility Filter — Design Conversation

This document captures the literal Q&A flow that shaped the design of
`Astronomy.Core/Session/CoarseVisibility.cs` and the broader
`IntervalScheduler` architecture that sits behind it.

User messages are quoted verbatim (typos preserved per the user's "literal"
ask). Claude responses preserve substance and most wording, with light
compression of filler text.

The implementation that came out of this discussion is the single file
`Astronomy.Core/Session/CoarseVisibility.cs` — one public method
`bool IsEverAboveHorizon(target, location, night, horizon)` wrapping
`VisibilityWindows.For(...)`.

The architectural decisions surfaced here (four-phase pipeline, four-
component product architecture, SQLite + binary cache hybrid, naming
standards for TSP / ISP / IS / ISS, etc.) are scoped to FUTURE work
beyond this PR. They're recorded here so the reasoning is preserved for
later planning passes (Plan #2: lockdown decisions; Plan #3: simulator +
IS/ISP architecture).

---

### Turn 1 — User

> Astronomy Library: Let's plan a function that given a night, location and
> target list, it returns a list of targets that have any visibility that
> night meaning the target altitude becomes > 0. This first function should
> be fast or course; it's intention to pick from a large list. Once this
> function returns it's list, other exixting or yet to be written,
> Astronomony functions will do detailed visicility calculations by time
> windows. Use of target time caches may be useful. Ask questions.

### Turn 1 — Claude (exploration + clarifying questions)

Launched parallel Explore agents to map (a) Core visibility primitives and
(b) existing batch/cache patterns. Key findings:

- `TargetGeometry.HourAngleAtAltitude(lat, dec, 0)` is the O(1) primitive:
  returns NaN = never rises, +∞ = circumpolar, else HA in hours.
- `VisibilityWindows.For(target, location, night, horizon)` already does
  above-horizon ∩ night in O(1) per target — but allocates a window list
  and takes an `IHorizonProfile`.
- Core has zero batch APIs; everything is single-target. No prior
  coarse-filter attempt anywhere in the codebase.
- Natural per-night shared cache is `LST(dusk)` / `LST(dawn)` — same for
  every target, computed once amortizes across 100+ target checks.

Asked three clarifying questions:

**Q1: What horizon should "visibility" mean?**
- (a) Geometric 0° (literal — most permissive)
- (b) Location.Horizon scalar
- (c) Caller-supplied IHorizonProfile

**Q2: How should the new function relate to VisibilityWindows.For?**
- (a) New batch entry in Session/ (recommended)
- (b) Add bool method to VisibilityWindows
- (c) Thin wrapper, no new math

**Q3: How ambitious should caching be in this first iteration?**
- (a) Single-call internal cache (recommended)
- (b) Reusable NightContext object
- (c) No caching

### Turn 2 — User

Answered:
- **Q1**: Geometric 0°.
- **Q2**: New batch entry in Session/.
- **Q3** (replaced with broader investigation request):

> After a series of one or more elimination filters, intention is to
> provide what will be the NINA plugin (and the TargetPlanner app) with a
> minute accurate visible target list. There are multiple condidtions
> which will influnce selection which i have not considered yet - this is
> where I need your help for investigation. What do other schedulers do?
> LSST, Tom Palmer's TS whose disk location you have access to. etc.

Followed immediately by clarification:

> by altitude > 0 i mean above the local horizon.

### Turn 2 — Claude (acknowledge horizon + launch research)

Acknowledged that the threshold isn't strictly geometric 0° but the
**configured local horizon**. Decided to use `IHorizonProfile.MinAltitude`
as the conservative coarse threshold (most permissive — never wrongly
rejects). Q1 answer was reinterpreted accordingly.

Launched three parallel investigations:
1. Tom Palmer's NINA Target Scheduler at
   `E:\Projects\VisualStudio\Astronomy\TargetScheduler_Clone\nina.plugin.targetscheduler`
2. NINA.Astrometry at `E:\Projects\VisualStudio\Astronomy\NINA\NINA.Astrometry`
3. LSST/Rubin FBS + astroplan (web research)

### Turn 3 — Claude (research synthesis)

**Tom Palmer's TS — 5-stage filter pipeline:**
Incomplete → Visibility → MoonAvoidance → Twilight → Humidity. The
Visibility stage is itself multi-stage: closed-form `RisesAtLocation()`
cheap reject, then a 10-second altitude-sample grid cached via MemoryCache
(12h TTL, key = `{targetId, date, lat, lon, RA, Dec, interval}`), then
meridian-window clip, meridian-flip-pause clip, max-altitude check.

**Rubin FBS — two-tier cache:**
`Almanac` of survey-lifetime sun/moon/planet positions (scipy interpolators
for O(1) MJD lookup) + per-decision-step `Conditions` snapshot holding
HEALpix maps for altitude/azimuth/HA/airmass/sky-brightness/etc. Basis
functions never recompute — they read from `Conditions`. Pipeline runs
scalar feasibility gates (cheapest), then mask basis functions (per-pixel
NaN), then reward functions.

**astroplan — closest direct match:**
Exposes `is_observable(constraints, observer, targets, time_range) → bool[]`
and `observability_table(...)`. Constraints are a polymorphic class
hierarchy: AltitudeConstraint, AirmassConstraint, AtNightConstraint,
MoonSeparationConstraint, MoonIlluminationConstraint, SunSeparationConstraint,
LocalTimeConstraint, etc. Each returns a `(targets × times)` boolean matrix.

**Consolidated condition checklist** (rough cheapest → costliest):
1. Above horizon at any time during night (this filter)
2. Min duration above horizon
3. Twilight level (astronomical / nautical / civil)
4. Max altitude (zenith avoidance)
5. Hour angle / azimuth limits
6. Local horizon profile
7. Meridian window (avoid flip during exposure)
8. Sun separation
9. Moon separation weighted by lunar phase + altitude
10. Airmass
11. Sky brightness
12. Cadence / time-since-last-visit
13. Live weather
14. Project / completion state
15. Filter / band loaded

**Three patterns shared across all three references:**
- Chain of independent filters, each with single responsibility.
- Cheap-first ordering — closed-form rejects before any sampling.
- Per-night cache shared across stages — at minimum sun/moon/sidereal; in
  TS's case the 10-second altitude grid feeds every subsequent stage.

Then asked three follow-up questions about horizon (re-confirm),
filter-chain architecture (static methods vs. polymorphic base vs.
monolithic IsObservable), and PR scope (just coarse filter vs. + cache
scaffold vs. + filter skeletons).

### Turn 3 — User (interrupt to clarify)

> the elimination series of filters will eventally include things like moon
> brightness and seperatation, camera filters to be used, and time
> available for imaging to list a few - there will be other filters I have
> not thought of yet. Speed is the goal of this first target list filter
> pass to reduce a list of 100+ targets down to what is potentially
> oportuniscticly possible for tonight.

### Turn 4 — User (architectural framing)

> again we are at the course planning stage; I'm trying to investigate now
> in order to eliminate large program architectural changes that might be
> needed later at plugin development time. I am investigating what other
> systems do; this will inform what I want to do. I want to remain as
> consistent with the interval scheduler idea as possible - what an actual
> telescope does: time per image, meridian flips, weather changes, other
> unanticipated problems should not influence an "intervals of targets"
> sequenced together. If changes to what will be plugin ui input values
> change or the unanticplated happens, I want (what will be) the plugin to
> have a "replan" button to reconstruct a new interval set starting "now".
> The 15 item list above is a good start but I would like your verbose
> pro-and con take on this. Let's continue discussing please.

### Turn 4 — Claude (interval-scheduler framing + 15-item analysis)

**The model split.** The interval-scheduler-plus-replan model partitions
conditions cleanly:

- **Build-time inputs**: knowable before the night, shape the interval
  set. Geometric reality (rises, sets, transit, flip timing), slowly-
  varying ephemeris (moon position and phase tonight), and user
  configuration (filters loaded, projects active, targets completed).
- **Execution-time observables**: evolve unpredictably during the night.
  Weather, equipment health, seeing, mount stalls. These do **not** shape
  the interval set; they trigger a replan when divergence exceeds threshold.

Cleaner than TS's per-exposure humidity gating (which can fragment plans
into inconsistent stop-start chunks). The Replan-only model says "the
plan is the plan until invalidated, then it's a new plan, fully
recomputed from now."

**The 15 items, grouped:**

*Cluster 1: Pre-night geometric invariants — elimination filters or
interval-fit constraints.*

1. **Above horizon any time during night** — cheapest reject; foundation;
   closed-form `MeridianAltitude > horizon`. **This filter.**
2. **Min duration above horizon** — drops hopeless-but-rises cases.
   Concept already exists (`Location.Duration`). Per-target override is
   the realistic future need. **Second filter, or fold into first.**
4. **Max altitude** — GEM no-go zones, roof lines. Most amateurs don't
   hit it; when they do, usually a meridian-flip issue. **Optional,
   opt-in filter.**
5. **HA / Az limits** — mostly subsumed by #6 if horizon polyline
   captures obstructions. **Redundant with #6.**
6. **Local horizon profile** — critical for backyard observers. Coarse
   filter uses `MinAltitude` (most permissive); azimuth-aware refinement
   happens in interval scheduler. **Yes, MinAltitude here.**
7. **Meridian window** — mandatory for GEM mounts but it's an
   interval-fitting constraint, not a candidate filter. A target with no
   clean post-flip window is still valid for pre-flip imaging.
   **Interval scheduler, not chain.**

*Cluster 2: Predictable nightly conditions.*

3. **Twilight level** — narrowband tolerates nautical; broadband demands
   astronomical dark. Defines window endpoints, not yes/no per target.
   **Interval fitting, not chain.**
8. **Sun separation** — irrelevant for nighttime DSO. **Skip.**
9. **Moon separation weighted by phase + lunar altitude** — biggest
   non-geometric constraint. Hard floor as elimination filter; finer
   weighting as interval scoring. **Future filter, not this PR.**

*Cluster 3: User configuration — pre-filter, change triggers replan.*

14. **Project / completion state** — requires data model that doesn't
    exist yet (plugin territory). **Future plugin filter.**
15. **Camera filters / band loaded** — requires equipment model. User
    confirmed this matters. **Future plugin filter.**

*Cluster 4: Real-time observables — NOT in interval scheduler.*

13. **Live weather** — execution-time only, replan trigger.
    Predicting weather inside the planner is a category error.
    **Skip.**

*Cluster 5: Skip for amateur use.*

10. **Airmass** — monotonic transformation of altitude. Pick one;
    altitude is more intuitive. **Redundant.**
11. **Sky brightness modeling** — overkill; subsumed by moon-sep +
    altitude + twilight. Rubin needs it for survey-grade calibration;
    amateur DSO imaging rarely benefits. **Skip.**
12. **Cadence / time-since-last-visit** — survey concept. **Skip.**

**Things missing from the 15-item list** (interval scheduler inputs):
- Slew time between targets
- Per-target exposure budget
- Auto-focus / dither / plate-solve / settle overhead
- Replan trigger semantics (user-pressed only vs. auto-detected)
- Imaging session boundary (replan from now-forward only, or also re-pick
  the *current* target?)

**Implications for THIS PR:**
- Elimination chain is build-time only; no live-condition filters.
- Filters are independent and stateless; order matters only for performance.
- Interval scheduler is the only thing that knows about time windows;
  filters answer "is this target a candidate?" yes/no.

So the coarse filter is the simplest possible thing: geometric
`MeridianAltitude > horizon.MinAltitude` AND above-horizon arc intersects
night window. Everything else moves to its own filter or to the interval
scheduler.

Discussion prompts: meridian-flip as scheduler-internal; twilight as
clipping not eliminating; auto-replan trigger semantics; per-target
duration override.

### Turn 5 — User

> The idea of running a before imaging precomputation phase is intreguing.

### Turn 5 — Claude (four-phase model)

Reframed the architecture as four phases:

```
Phase A — Coarse elimination (closed-form, O(1) per target, no allocation)
    "Does this target ever go above local horizon during night?"  ← THIS PR
Phase B — Precompute cache (per surviving target × per N-second sample)
    altitude[t], azimuth[t], moonSeparation[t], localHorizonAlt(az[t])
Phase C — Sample-based elimination (query cache; no recomputation)
    Min N minutes above horizon, moon hard-floor, mount limits
Phase D — Interval scheduler (query cache; build the plan)
```

**Pros of four-phase model:**
1. Single source of truth — Phases C and D read same grid; no drift
   between stages computing "alt at time T" differently.
2. Cost control — pay sampling once per replan; downstream is array
   lookups, not recomputation.
3. "Minute accurate" becomes a single parameter (sample interval).
4. Replan semantics fall out for free.
5. Matches the validated TS pattern.
6. Phase A's design becomes obvious — stateless closed-form predicate.

**Cons:**
1. Resolution is sticky — sub-minute boundaries snap to nearest sample;
   need closed-form refinement on top for things like exact transit time.
2. Existing Core helpers become dual-purpose (closed-form for grid build
   + one-shot questions; sampled for canonical filter/scheduler answers).
3. Cache invalidation is complete, not incremental; need discipline about
   what goes IN cache vs. as a knob.
4. Sample-grid algorithms are a different paradigm from closed-form.
5. Precompute is overkill for very small lists (premature optimization to
   branch on this).
6. Multi-night planning is per-night by construction.

**What this means for THIS PR:**
- Probably **no need for a `FilterVisible(IEnumerable<Target>)` batch
  entry in Core**. If Phase B is the natural caller, it loops itself.
  Core API can be just `bool IsEverAboveHorizon(target, location, night, horizon)`.
- **No need for per-batch LST cache in Core.** Saving microseconds is
  invisible next to Phase B's precompute cost.

Both simplifications shrink the PR and leave zero baggage for Phases B/C/D.

**Open questions:** sample resolution (60 s vs. 10 s vs. 1 s), what goes
IN the per-target cache, where Phase B lives (`Astronomy.Core/Planning/`
vs. a new `Astronomy.Planner` project).

### Turn 6 — User

> let's continue to discuss. just to make clear, doing a 5 minute
> computaion before the telecope is even turned on to allow as much "table
> look up" as possible is acceptable and maybe best. I don't think having
> this precomputed 5 minute table (a saved json file?) interferes with a
> "replan" button or with target filter evaluations that would need to be
> run during imaging at regular intervals. Only target additions or
> deletions might require a new 5 minute evaluation. I'd like to impose a
> minimum of compuational load on the PC nina is running on as possible. A
> new though: I will need some form of simulator to run evaluations that
> include and expose commands and data to and from NINA - more intersting
> work to do! Can we save this converstion as it evaloves (meaning the
> literal Q&A flow) in a text file?

### Turn 6 — Claude (5-min budget, JSON, runtime, simulator, save options)

**5-minute precompute budget — what fits:**

| Resolution | Samples (100 targets × 12 h) | Compute (~15 trig ops/sample) |
|---|---|---|
| 60 s | 72 K | ~1 M ops, ~10 ms |
| 10 s | 432 K | ~6 M ops, ~60 ms |
| 1 s | 4.32 M | ~65 M ops, ~600 ms |

Even 1-second sampling for 100 targets fits well under a second. 5 minutes
lets you go to 1000 targets at 1 s, or 100 targets at 1 s across 30 nights,
or fold in extra columns (parallactic angle, airmass, refraction-corrected
altitude, sky brightness model). Budget isn't constraining — question
becomes "what's worth caching."

**JSON persistence — size matters at scale:**

- **Per-target summary** (~50 numbers/target × 1000 targets) ≈ 1.5 MB JSON.
  *Trivial; persist as JSON.*
- **Per-minute grid** (100 × 720 × 5) ≈ 6 MB JSON. *Fine.*
- **Per-10-second grid** (100 × 4320 × 5) ≈ 35 MB JSON, ~5 MB gzipped.
  *Borderline.*
- **Per-second grid** (100 × 43200 × 5) ≈ 430 MB JSON. *Don't — use binary.*

*Pros of JSON:* inspectable; schema-flexible; Newtonsoft.Json already a
dependency; version-controllable for tests.

*Cons of JSON:* verbose at scale; floating-point round-trips; schema drift
without explicit version field.

**Recommendation:** persist a JSON summary table (cheap, inspectable,
useful for fast UI). Persist the full grid only if survival across plugin
restarts matters; if so, use binary (MessagePack-CSharp or `BinaryWriter`).

A clean two-file pattern:
```
{cache-root}/{location-hash}/{date}/
    summary.json     ← small, JSON, per-target rollups, human-readable
    grid.bin         ← large, binary, per-target × per-time samples
    manifest.json    ← location, dates, target IDs, schema version, mtime
```

Manifest lets you decide on plugin start whether the cache is still valid;
mismatch → rebuild affected pieces.

**Runtime filter evaluations under interval model — they're light:**
- Pre-interval sanity checks (target above horizon at actual current time).
- Borderline-condition recheck (moon separation if within ~5° of threshold).
- Weather/safety polling — handled by NINA's safety condition; planner's
  role is purely "be ready to replan when triggered."

None touch the per-target grid; all are spot queries against current time.

**Simulator (future scope):**
- Mock NINA mediator surface (`ITelescopeMediator`, `ICameraMediator`, etc.)
- Time acceleration (10× or jump-to-next-event)
- Injectable failures (cloud, plate-solve, mount stall, focuser drift)
- Replay (capture real session events, replay through plugin under test)
- NINA's own simulator devices get partway; full plugin testing wants
  in-process mock-mediator harness.

Worth a separate planning pass when ready. Out of scope for the coarse
filter PR.

**On saving the conversation:**
Plan-mode restriction means I can only write to the plan file. Two
options offered:
1. Append to plan file as "Design discussion log" appendix; move to
   standalone file post-exit-plan-mode.
2. Wait until plan-mode exit, then write standalone file as first action.

Recommended option 1 — capture incrementally so we don't lose it if
session is interrupted; migrate to permanent home post-exit.

Sub-questions:
- Format: Markdown with `### User` / `### Claude` headers, or terser?
- Final location: in-repo (`docs/design/`) or out-of-repo (`notes/`)?

### Turn 7 — User

> 1. Append the conversation to the plan file as a "Design discussion log"
> appendix, capturing the literal Q&A as we go. --- lets capture as much
> of what we've been discussing already as possible. Let me know how that
> went.

### Turn 7 — Claude

Wrote the plan file with the discussion log captured.

### Turn 8 — User

> Let's evalaute json vs sqlite as plamer does as a single source of
> targets, how to image the targets, status and th5 5 minute compute
> result home.

### Turn 8 — Claude (JSON vs SQLite, per-domain analysis + hybrid recommendation)

**Four data domains, very different shapes:**

| Domain | Size | Access | Lifetime | Schema |
|---|---|---|---|---|
| **Targets** | 100–1000 records, ~20 fields. <1 MB. | Read-mostly. Startup load; user-edit writes. | Years. | Stable. |
| **Exposure plans** | 5–20 per target → ~10 K records. Hundreds of KB. | Read at planning; small writes (count-completed) per session. | Years. | Stable. |
| **Status / history** | Append-only acquired-image records. 100s/night → 10 K+/year. Hundreds of MB over years. | Append per image; cross-date queries. | Forever / retention. | Stable but evolving (new quality metrics). |
| **5-min precompute grid** | Per-night per-target × per-time samples. 1–100 MB. | Batch write at precompute; dense indexed reads at planning. | Single night, regenerable. | Array of doubles per metric per sample. |

**JSON, per domain:**
- *Targets:* natural fit. Read whole file, write on edit. Inspectable, version-controllable. **Wins.**
- *Exposure plans:* foreign-key semantics; whole-file rewrite on count-update wastes IO. **Acceptable for read, awkward for write.**
- *Status / history:* append-only, query-driven, grows forever. Whole-file load to filter is slow; corrupt-on-crash risk. **Loses.**
- *Precompute grid:* fine at 60 s (~6 MB), breaks at 1 s (~430 MB); FP round-trip cost. **Loses at scale.**

**SQLite, per domain:**
- *Targets:* overkill but not painful. Buys real spatial queries. Tool needed for inspection. **Either-or.**
- *Exposure plans:* shines. Foreign keys, partial transactional updates, cross-target queries trivial. **Wins.**
- *Status / history:* what SQLite was made for. Append-INSERT, indexed queries, gigabyte-scale. **Wins decisively.**
- *Precompute grid:* awkward. BLOB defeats random access; one-row-per-sample inflates with row-header overhead and is slower than array indexing. **Wrong primary store.**

**What TS does:** SQLite for projects, targets, exposure plans, acquired-image history, profile preferences. SyncService implies SQLite-as-source-of-record for multi-PC sync. Per-target altitude grid is in-memory `MemoryCache` (12h TTL), NOT persisted in SQLite — even TS treats per-night precompute as ephemeral.

**Recommendation: hybrid, same shape as TS plus an explicit grid file**

```
{user-data-root}/TargetPlanner/
    scheduler.db                ← SQLite: targets, exposure_plans,
                                    acquired_images history, projects,
                                    cache_runs (pointers to grid files)
    cache/{location-hash}/{date}/
        manifest.json           ← small, JSON, what this cache holds,
                                    schema version, mtime, target hash
        summary.json            ← small, JSON, per-target rollups for UI
        grid.bin                ← large, binary, per-target × per-time
```

Concrete benefits:
- Schema migrations affect only `scheduler.db`; cache files can be blown away.
- 5-min precompute is recoverable; corrupt → delete and rebuild.
- Replan optionally regenerates precompute; never touches SQLite.
- Multi-PC future addressed by syncing `scheduler.db`; each PC re-precomputes its own grid.
- "Things I value forever" vs. "things I can throw away" is physically separated.

### Turn 9 — User

Resolutions:
- Astronomy.Planner (separate project). Confirmed.
- SQLite for relational, binary side-file for grid, JSON for manifest/summary. Confirmed.
- Predicate-only Phase A scope. Confirmed.
- TS schema compatibility — "import and export is a requirement. I do not like the way TS organizes it's sqlite tables. I think this could be improved upon — investigation is needed."
- SQLite library choice (Microsoft vs. System) — "needs to be discussed."
- User-data-root — `%LOCALAPPDATA%\TargetPlanner\` confirmed.
- Schema versioning — "Use a schema_version row from day one."

### Turn 9 — Claude (Microsoft vs. System SQLite; queues TS schema Explore)

**Microsoft.Data.Sqlite (recommended for new code):** released 2016
alongside .NET Core; maintained by Microsoft (EF Core team); thin ADO.NET
provider over SQLitePCLRaw + native SQLite; MIT-licensed; native binaries
~1 MB; targets .NET Standard 2.0+ and .NET Framework 4.6.1+; async-native;
no EF Core dependency baked in; active development.

**System.Data.SQLite (legacy but stable):** mid-2000s; maintained by the
SQLite team; ADO.NET provider with built-in EF6 / LINQ-to-SQL support;
public-domain; larger native binaries (~3–5 MB); less async-native; more
features out of the box; slower-moving.

**Compatibility note:** SQLite the format is the .db file on disk.
Microsoft and System are just .NET bindings — both read/write the same
files identically. So even if TS uses System and we use Microsoft,
sharing `.db` files for import/export is unaffected.

**Recommendation: Microsoft.Data.Sqlite + Dapper.** Modern, smaller,
MIT-licensed, recommended by Microsoft. Dapper (~100 KB NuGet, no native
deps) layers POCO mapping over ADO.NET. EF Core is overkill for this
project's complexity.

Launched a TS-schema Explore in background.

### Turn 10 — User

> There are features TS implemnts that I don't want or need - like dual
> telecope corrdination and I don't like TS's weighting scheme. FYI.

### Turn 10 — Claude

Narrowed TS-interop scope:
- **Out of scope:** TS `SyncService` (multi-telescope coordination) and TS scoring engine / 9-rule weighting system.
- **In scope for import/export:** projects, targets, exposure plans, acquired-image history.

### Turn 11 — User (via AskUserQuestion answer)

> Microsoft.Data.Sqlite + Dapper (Recommended).

### Turn 11 — Claude (TS schema dump landed; library locked; quirks catalogued)

**SQLite library locked:** `Microsoft.Data.Sqlite` + Dapper.

**TS uses EF6 + `System.Data.SQLite`.** File-format compatibility holds.

**TS DB location:** `%LOCALAPPDATA%\NINA\SchedulerPlugin\schedulerdb.sqlite`. Auto-backup keeps 3 most recent timestamped copies.

**TS tables IN our import/export scope:**
- `project`, `target`, `exposureplan`, `exposuretemplate`, `acquiredimage`,
  `imagedata`, `flathistory`, `overrideexposureorderitem`, `filtercadenceitem`.

**TS tables/fields OUT of scope:**
- `ruleweight`, sync fields in `profilepreference`, `SyncService` gRPC layer.

**TS structural quirks to NOT inherit (concrete improvement targets):**

1. **ImageMetadata as JSON blob** — quality metrics buried in TEXT; no SQL queries on metrics. *Fix:* explicit typed columns.
2. **No image file path column.** *Fix:* dedicated `file_path TEXT`.
3. **Missing FK constraints** on several tables. *Fix:* declare FKs with `ON DELETE CASCADE`.
4. **Booleans as INTEGER without CHECK constraints.** *Fix:* `CHECK (col IN (0,1))`.
5. **Enums as INTEGER without lookup tables.** *Fix:* lookup tables or TEXT names.
6. **Denormalized `profileId`** across 6 tables. *Fix:* single profile table.
7. **`profilepreference` is a multi-purpose grab-bag.** *Fix:* split.
8. **No grading audit trail.** *Fix:* `grading_history` table (later moved entirely out of scope per Turn 12).
9. **`action` field semantic conflation.** *Fix:* separate enum types per table.
10. **Legacy `target.unusedOEO` column** — TS4→5 migration cruft. *Fix:* don't carry over.
11. **No explicit indexes.** *Fix:* explicit indexes for hot queries.

**TS patterns worth mirroring:**
- GUIDs for stable cross-profile refs.
- Versioned migrations + PRAGMA user_version.
- JSON for genuinely freeform data (NOT for queryable metrics).
- Auto-backup on schema migration (3 timestamped copies).

### Turn 12 — User (via AskUserQuestion answer)

> I also want to avoid TS's image grading entirely. I do that later
> afterwards independently. TS file names: since no image grading and no
> telescope sync, and I move my images after each night is through, I
> think this is not needed. "image grading" and aquired totals hook back
> into XisfManager which will eventally need access to the
> Astronomy.Planner.

### Turn 12 — Claude (further narrowing + v1 schema sketch)

**Further narrowing:**
- No image grading in our schema (XisfManager domain).
- No quality metrics (FWHM, HFR, etc.) — XisfManager domain.
- No file paths or image BLOBs — user moves files post-night.
- XisfManager ↔ Planner integration: shared `scheduler.db`; XisfManager updates `exposure_plan.accepted_count` via UPDATE.

**Schema sketch v1** (7 tables, ~75 lines):

```sql
CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE profile (
    id                    INTEGER PRIMARY KEY,
    nina_profile_id       TEXT NOT NULL UNIQUE,
    name                  TEXT NOT NULL,
    latitude_deg          REAL NOT NULL,
    longitude_deg         REAL NOT NULL,
    elevation_m           REAL NOT NULL DEFAULT 0,
    horizon_profile_path  TEXT,
    horizon_min_alt_deg   REAL NOT NULL DEFAULT 0,
    location_hash         TEXT NOT NULL,
    created_at            INTEGER NOT NULL
);

CREATE TABLE project (
    id, guid, profile_id, name, description, state, priority,
    created_at, active_at, inactive_at, minimum_time_minutes,
    minimum_altitude_deg, maximum_altitude_deg, use_custom_horizon,
    horizon_offset_deg, meridian_window_minutes, dither_every, is_mosaic
);

CREATE TABLE target (
    id, guid, project_id, name, active, ra_hours, dec_deg, epoch,
    rotation_deg, roi
);

CREATE TABLE exposure_template (
    id, guid, profile_id, name, filter_name, default_exposure_sec,
    gain, offset, bin, readout_mode, twilight_level, minutes_offset,
    moon_avoidance_*, dither_every, maximum_humidity
);

CREATE TABLE exposure_plan (
    id, guid, target_id, exposure_template_id,
    desired_count, accepted_count, enabled
);

CREATE TABLE cache_run (
    id, profile_id, night_date, location_hash, target_list_hash,
    grid_path, summary_path, manifest_path, sample_resolution_sec,
    target_count, created_at, schema_version,
    UNIQUE (profile_id, night_date)
);
```

**Key deviations from TS:**
- All booleans have `CHECK (col IN (0,1))`.
- All enums stored as TEXT with `CHECK (col IN (...))` — no integer enum brittleness.
- `profileId` denormalization eliminated.
- All FKs declared explicitly.
- Explicit indexes on hot query paths.
- No `acquiredimage` table; integration counts live as `exposure_plan.accepted_count`.

### Turn 13 — User

> I'm thinking XisfManager would become the "front-end" to the interval
> scheduler. I intensely dislike doing edits and changes on NINA's plugin
> page and want a more flexable and NINA independent way of working with
> this new interval schedular.

### Turn 13 — Claude (architectural pivot — three-component model)

**Three-component model** (later superseded by Turn 15's four-component):
- **XisfManager** = front-end. Edits, precompute, plan review, Replan.
- **Astronomy.Planner** = shared library.
- **NINA plugin** = thin executor. Reads plan, runs intervals, writes execution status. No editing.

**SQLite cross-machine note:** if XisfManager runs on a desktop and the
plugin runs on the imaging PC (e.g., `\\BIRDWATCHER\…`), the WAL mode
chosen for single-machine concurrency is unsafe over network shares.

### Turn 14 — User (via AskUserQuestion answers + follow-up)

- **TargetPlanner future:** "TargetPlanner may move into XisfManager - I'm not sure but this is likely. XisfManager is .net 10 so charts need to be recoded. TargetPlanner might evolve into IntervalSchduler's (the new plugin name) simulator."
- **Plugin UI scope:** Moderate runtime UI.
- **DB access model:** "Deploy and sync but XisfManager and interact with BIRDWATCHER at any time."

> xisfManager has access to my imaging directory structure and routines i
> use to manage both newly aquired and permanently stored images.

### Turn 14 — Claude (decisions captured; library targeting + concurrency notes)

- Plugin = `IntervalScheduler`. Moderate runtime UI.
- DB = deploy-and-sync, with XisfManager live UNC read access during night.
- TargetPlanner likely merges into XisfManager; may become the simulator.
- All `Astronomy.*` libraries target `netstandard2.0`. Microsoft.Data.Sqlite + Dapper support it.
- XisfManager owns all image-directory routines.

### Turn 15 — User (architecture pivot + naming standardization)

> Sorry another pivot: the IntervalSchedular Plugin (ISP) should have
> editing capability and a new .net 10 C# application called
> IntervalSchedular (IS) will be used to do the heavy lifting. Lets
> standardize on and use these names: Existing Tom Palmer Nina plugin:
> TargetSchedular (TSP). New Nina plugin I'm developing is called
> IntervalScheduler Plugin (ISP). New C# .net 10 application to interface
> with ISP is called IntervalScheduler (IS). The new C# .net 10 ISP
> simulator is called ISSimulator (ISS). let's use these acronyms: ISP,
> IS, ISS and TSP or the names outright. Ask your questions again please..

### Turn 15 — Claude

**Naming standards:** TSP, ISP, IS, ISS.

**Four-component architecture (supersedes Turn 13's three-component):**
- **IS** (new .NET 10 desktop) — heavy lifting: precompute, scheduler, editing UI.
- **ISP** gains editing capability — reverses Turn 13's "thin executor" framing.
- **XisfManager** — separate, image management and grading.
- **ISS** — simulator (TargetPlanner may evolve into it).

### Turn 16 — User (via AskUserQuestion answers)

**Q1 (IS/ISP editing split):** IS-heavy / ISP-light. With added detail:

> your list of ISP editing allows would also include a by camera filter
> exposure plan for each interval target with on-the-fly editing like
> per filter: enable/disable (skip), filter type, exposure time, gain,
> and offset. A custom "filter" is used to indicate when and how much
> dither is called for. we'll work out those details later..

**Q2 (Next focus):**

> We need to plan each of 1, 2, and 3 so don't forget these. Lets plan
> Phase a as you've written it.

### Turn 16 — Claude (Phase A Final Plan written; Plans #2 + #3 queued)

Final Plan section populated — single new file
`Astronomy.Core/Session/CoarseVisibility.cs`, single public method
`bool IsEverAboveHorizon(...)`, ~30 lines total. Wraps
`VisibilityWindows.For(...).Count > 0` after a `night.IsValid` guard and
null-arg checks. No existing files modified. Verification by `dotnet
build` + manual smoke check (no test framework per CLAUDE.md).

**Plans #2 and #3 queued (won't forget):**
- Plan #2 — lock down sample resolution, single vs. multi-location,
  per-image audit (Open #2, #4, #5).
- Plan #3 — ISS simulator + IS/ISP architecture coordination (Open #7,
  #8, #9, #10).

---

## Outcome

The implementation: `Astronomy.Core/Session/CoarseVisibility.cs`, one
method, ~30 lines.

```csharp
public static bool IsEverAboveHorizon(
    Target target, Location location, NightWindow night, IHorizonProfile horizon)
{
    if (target == null) throw new ArgumentNullException(nameof(target));
    if (location == null) throw new ArgumentNullException(nameof(location));
    if (horizon == null) throw new ArgumentNullException(nameof(horizon));

    if (!night.IsValid) return false;

    return VisibilityWindows.For(target, location, night, horizon).Count > 0;
}
```

The architectural decisions surfaced during this discussion (four-phase
pipeline, four-component product architecture, SQLite + binary cache
hybrid, naming standards) are captured here for future planning passes.
