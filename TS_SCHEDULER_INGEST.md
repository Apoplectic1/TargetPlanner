# TS_SCHEDULER_INGEST.md

How TargetPlanner (TP) will read targets from the NINA Target Scheduler plugin's (TSP) SQLite database (`schedulerdb.sqlite`) instead of (or in addition to) the existing NINA `.json` sequence-file ingest.

This is **discovery + design**, not yet implementation. The broader Library / ISP / IS architectural dossier lives in [`SCHEDULER_DESIGN.md`](SCHEDULER_DESIGN.md); this file is scoped to the TP-side read feature. Source-of-truth for the schema is the EF entity classes under `..\TargetScheduler_Clone\nina.plugin.targetscheduler\NINA.Plugin.TargetScheduler\Database\Schema\`. The findings below were validated against a real snapshot at `TS DataBase Example\schedulerdb.sqlite` (10 projects, 102 targets, schema `PRAGMA user_version = 24`).

---

## 1. Why read TSP's database?

The user maintains their imaging-target inventory in TSP (projects, targets, RA/Dec, framing, exposure plans). TP today re-discovers a subset of that data by scanning NINA `.json` sequence files under `MainForm.NinaTargetsRootPath` — but those files only capture targets that have been built into a sequence. Reading `schedulerdb.sqlite` directly lets TP plot **every** target the user has authored in TSP, with no duplicate entry, and pick up new targets the moment TSP saves them.

TP's read is **read-only**. Writes belong to TSP (or to the user's future IS / ISP / XisfManager apps that already own the schema — see `MEMORY.md → project_intervalscheduler`). XisfManager already reads this same DB (`XisfFileManager/TargetScheduler/SqlLiteManager.cs`); TP's reader will be a smaller, simpler sibling focused on just `target` + `project`.

---

## 2. Source of truth and file location

| Concern | Value |
|---|---|
| Live path (single-machine dev) | `%LocalAppData%\NINA\SchedulerPlugin\schedulerdb.sqlite` |
| Live path (imaging-PC, canonical) | `\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite` — see [`reference_birdwatcher_imaging_pc`](file:///C:/Users/djsta/.claude/projects/E--Projects-VisualStudio-Astronomy-TargetPlanner/memory/reference_birdwatcher_imaging_pc.md) |
| Reference snapshot | `TS DataBase Example\schedulerdb.sqlite` (gitignored) |
| Backups (same dir) | `schedulerdb-YYYY-MM-DD-HH-mm-ss-backup.sqlite`, last 3 retained, written on plugin load |
| Schema owner | TSP (Tom Palmer) — `NINA.Plugin.TargetScheduler.Database.SchedulerDatabaseContext` |
| Schema version pragma | `PRAGMA user_version` — current snapshot reads `24`. EF6 migration scripts live under TSP's `Database\Initial\` and `Database\Migrate\` |
| Journal mode | `delete` (rollback journal, **not WAL**) — see §3 for locking implications |
| SQLite provider TSP uses | `System.Data.SQLite` via Entity Framework 6 |

TP does not need EF6 — it can use `Microsoft.Data.Sqlite` (modern, async-friendly, lightweight, single NuGet) or `System.Data.SQLite` (matches TSP exactly). Both interop fine over the same file as long as TP opens read-only.

---

## 3. How TP should open the database

- **Read-only.** Connection string: `Data Source=<path>;Mode=ReadOnly;Cache=Shared;` (Microsoft.Data.Sqlite) or `Data Source=<path>;Read Only=True;` (System.Data.SQLite). TP never writes — eliminates any risk of corrupting the user's live scheduler data.
- **Tolerate `SQLITE_BUSY`.** TSP's rollback-journal mode means a TSP writer briefly blocks readers. Use a short busy-timeout (~1–2 seconds) and accept the occasional miss; TP's UI flow can poll-retry.
- **Snapshot pattern.** Open → read once into a `List<Target>` (TP's existing in-memory shape) → close. Don't hold a connection across UI events. A typical scan of `target` + `project` is sub-millisecond at 100s of rows; reopen on demand.
- **Don't trust the file path is present.** A user without TSP installed (or who has never run it) has no `schedulerdb.sqlite`. TP must `File.Exists` first and gracefully fall back to NINA-`.json` ingest.
- **UNC paths work, but watch latency.** `\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite` is the canonical live source for users in this workflow. SMB latency is real (~5–20 ms per file open on a LAN); the snapshot pattern above mitigates it.

---

## 4. Schema overview (TP-relevant subset)

Eleven tables total. TP cares primarily about **`target`** and **`project`**; everything else is metadata about acquisition / grading / scheduling that doesn't affect "where is this target on the sky tonight?".

```
profilepreference (per NINA profile)
        │
        │  profileId (NINA profile guid, text)
        ▼
     project ──────────────► ruleweight (scoring config)
        │  projectid              │
        │                         ▼
        ▼                       (n/a for TP)
     target ──────────────► exposureplan ──► exposuretemplate (filter/gain/etc.)
        │  targetid               │  exposureTemplateId
        │                         ▼
        │                      (n/a for TP)
        ├──► filtercadenceitem       (per-target cadence, n/a for TP)
        ├──► overrideexposureorderitem (per-target order overrides, n/a for TP)
        └──► (linked from) acquiredimage ──► imagedata (image history, n/a for TP)
                                     │
                              flathistory  (flats taken, n/a for TP)
```

TP-essential tables: **`target`**, **`project`**. Optional context (project priority/state, filter list per target) comes from joining `project`, `exposureplan`, and `exposuretemplate`.

Row counts in the reference snapshot:

| Table | Rows |
|---|---:|
| target | 102 |
| project | 10 |
| exposureplan | 662 |
| exposuretemplate | 20 |
| acquiredimage | 1,178 |
| imagedata | 3,330 |
| filtercadenceitem | 177 |
| overrideexposureorderitem | 22 |
| ruleweight | 80 |
| profilepreference | 2 |
| flathistory | 0 |

---

## 5. Tables TP reads

### 5.1 `target` — the primary table

Schema (live SQL):

```sql
CREATE TABLE `target` (
    `Id`         INTEGER NOT NULL PRIMARY KEY,
    `name`       TEXT NOT NULL,
    `active`     INTEGER NOT NULL,         -- 0/1 bool
    `ra`         REAL,                     -- decimal HOURS [0, 24)
    `dec`        REAL,                     -- SIGNED decimal degrees [-90, +90]
    `epochcode`  INTEGER NOT NULL,         -- Epoch enum; J2000 = 2
    `rotation`   REAL,                     -- framing rotation (deg)
    `roi`        REAL,                     -- framing ROI percent (e.g. 100.0)
    `projectid`  INTEGER,                  -- FK -> project.Id
    `unusedOEO`  TEXT,                     -- legacy, ignore
    `guid`       TEXT,                     -- stable identifier
    `priority`   INTEGER DEFAULT -1,       -- -1 = "use project priority"
    FOREIGN KEY(`projectId`) REFERENCES `project`(`Id`)
)
```

Confirmed unit conventions from `Database\Schema\Target.cs:90-103` (`Coordinates = new Coordinates(Angle.ByHours(ra), Angle.ByDegree(dec), Epoch)`):

| Column | Unit | TP interpretation |
|---|---|---|
| `ra` | decimal hours | Same as TP's `Target.RightAscension`. **Direct copy.** |
| `dec` | signed decimal degrees | TP stores magnitude + `North`. **Pass through TP's constructor**, which normalizes (negative → flipped `North`). |
| `epochcode` | enum int (J2000 = 2) | All 102 sample rows are 2. **Treat anything else as a warning** and skip / flag. |
| `active` | 0/1 | Maps to TP's `Target.Enabled`. |
| `rotation`, `roi` | framing metadata | Not used by TP charts. Ignore (or carry through if TP ever surfaces framing). |
| `priority` | sentinel `-1` = inherit | Not relevant to TP plotting. |

Sample row (from snapshot):

```
Id=1  name='Jellyfish'  active=0  ra=6.297386  dec=22.543652  epochcode=2
      rotation=345.43   roi=100   projectid=1  priority=-1
      guid='d0a036b7-...'
```

Validation across the snapshot: RA range `[0.07, 23.78]` hours (full sky), Dec range `[-26.4°, +80.9°]` (mostly N hemisphere, some S). All 102 rows are `epochcode=2` (J2000). 42 inactive, 60 active. All `priority=-1` (i.e. user always inherits project-level priority — TP doesn't need to expose per-target priority).

### 5.2 `project` — context, optional filtering

Schema columns relevant to TP:

| Column | Type / meaning |
|---|---|
| `Id` | PK |
| `profileId` | NINA profile guid (text). **Same `schedulerdb.sqlite` can contain projects from multiple NINA profiles** — TP must scope. |
| `name` | Project name (e.g. `"Nebulae - Above 45"`) |
| `state` | `ProjectState` enum: `Draft=0`, `Active=1`, `Inactive=2`, `Closed=3` (column renamed via EF; raw SQL column is `state`) |
| `priority` | `ProjectPriority` enum: `Low=0`, `Normal=1`, `High=2` (raw column `priority`) |
| `minimumaltitude` | Degrees, signed. TSP's per-project altitude floor — informational for TP. |
| `maximumAltitude` | Degrees. `0` = unset. |
| `usecustomhorizon` | 0/1 — does this project use NINA's profile horizon? |
| `horizonoffset` | Degrees added on top of horizon. |
| `meridianwindow` | Minutes (`480` = ±4h around meridian, the most common value in the sample). |
| `createdate` / `activedate` / `inactivedate` | UNIX seconds (`long`). Nullable on the last two. |
| `isMosaic` | 0/1 |
| `guid` | Stable identifier |

Snapshot distribution: `state` ∈ {Draft: 1, Active: 5, Inactive: 4}; `priority` ∈ {Low: 1, Normal: 6, High: 3}. All 10 projects belong to one NINA `profileId`.

The other project columns (`filterswitchfrequency`, `ditherevery`, `enablegrader`, `flatsHandling`, `smartexposureorder`) are scheduling/acquisition concerns and irrelevant to plotting.

### 5.3 `exposureplan` — per-target filter inventory (optional)

If TP later wants to enrich the chart UI with "this target images in H/O/S filters" (e.g. to pick a filter-aware `FilterCenterNm` automatically per target), it's a join through `exposureplan.targetid → target.Id` and `exposureplan.exposureTemplateId → exposuretemplate.Id`, then `exposuretemplate.filtername`.

Sentinel: `exposureplan.exposure = -1.0` means "use `exposuretemplate.defaultexposure`" (`ExposurePlan.cs:115`). TP doesn't care about exposure values, only filter identity.

### 5.4 Tables TP can ignore

`acquiredimage`, `imagedata`, `flathistory`, `filtercadenceitem`, `overrideexposureorderitem`, `ruleweight`, `profilepreference` — all acquisition history, grading state, scheduling internals, or per-profile TSP UI prefs.

---

## 6. Mapping to TP's `Astronomy.Core.Targets.Target`

TP's existing `Target` type (`..\Library\Astronomy.Core\Targets\Target.cs`):

```csharp
public sealed class Target {
    public string  Name;             // human-readable
    public double  RightAscension;   // decimal hours [0, 24)
    public double  Declination;      // magnitude, non-negative degrees
    public bool    North;            // hemisphere flag
    public string  Directory;        // filesystem origin ("" if not from disk)
    public bool    Enabled;          // for multi-target ops
}
```

Direct mapping:

```csharp
var tpTarget = new Target(
    name:           ts.name,
    rightAscension: ts.ra,            // already in hours; no conversion
    declination:    ts.dec,           // signed degrees -> constructor normalizes
    north:          ts.dec >= 0,      // hint; constructor flips if needed
    directory:      "",               // or a synthetic "schedulerdb:<guid>" tag (see §7)
    enabled:        ts.active != 0
);
```

Notes:
- The `Target` ctor normalizes a negative `declination` by storing `|declination|` and flipping `north`, so the signed value can be passed straight in. The `north: ts.dec >= 0` is a redundant hint that matches what the ctor will compute anyway — kept for readability.
- `Directory` was designed for the NINA-`.json` origin path. For TSP-origin targets, leave empty or use a synthetic marker (see §7).

---

## 7. Loading strategy — design questions

These are decisions TP needs to make. None are blockers for prototyping; all should be answered before shipping.

### 7.1 Which NINA profile to scope to?

`schedulerdb.sqlite` can contain projects from multiple `profileId`s. Options:

1. **Read the active NINA profile from disk** (NINA stores it under `%LocalAppData%\NINA\Profiles\`). Pros: zero user friction. Cons: TP becomes coupled to NINA's profile-storage layout.
2. **Show a profile picker** in TP's UI. Pros: explicit, no NINA-internal scraping. Cons: extra click.
3. **Show targets from all profiles** with a profile column. Pros: maximum visibility. Cons: clutter for users with multiple test profiles.

**Recommendation:** (2) profile picker, populated from `SELECT DISTINCT profileId FROM project`, with the most-recently-modified profile pre-selected. Falls back to "<all profiles>" if there's only one.

### 7.2 Which project states to include?

`state ∈ {Draft, Active, Inactive, Closed}`. The sample has 1 Draft, 5 Active, 4 Inactive, 0 Closed.

**Recommendation:** include `Active` and `Inactive` by default; offer a checkbox to include `Draft` and `Closed`. TP is a planning tool — users plotting "what could I image tonight" want active targets but may want to evaluate drafts too.

### 7.3 Which targets within a project?

`target.active ∈ {0, 1}`. Sample: 42 inactive, 60 active.

**Recommendation:** mirror the per-target `active` flag onto TP's `Enabled`. Show inactive targets but unchecked by default — same UX as the existing checked-listbox.

### 7.4 Origin tagging for `Target.Directory`

TP's existing code uses `Directory` to distinguish NINA-file targets from locally-typed ones (the latter persist to `local-targets.json`). For TSP-origin targets, three options:

1. **Empty string** — treat them as ephemeral, recompute from DB every load. Locally-added targets (TP's sidecar) stay separate.
2. **Synthetic marker** like `"schedulerdb:<guid>"` — survives session state, lets TP detect duplicates if a target exists both in `.json` and TSP.
3. **A new origin enum** on `Target` — formal but Library-shaping.

**Recommendation:** (2) — synthetic marker. Zero Library change, lets TP's existing dedupe logic key on `Directory`, lets the Remove button skip the local-sidecar persistence path for DB-origin targets.

### 7.5 Refresh semantics

TSP writes to the DB as the user edits targets in NINA (and during nightly imaging). TP options:

1. **On-demand only** — reload when user clicks "Reload" or switches the source. Simplest.
2. **File watcher** (`FileSystemWatcher` on `schedulerdb.sqlite`) — auto-pick-up. Risk: events fire mid-write; TP must debounce + handle `SQLITE_BUSY`. SMB-mounted UNC paths fire watchers less reliably (Windows quirk).
3. **Poll on focus** — re-read when MainForm regains focus. Reasonable middle ground.

**Recommendation:** (1) for v1. (3) is the natural upgrade.

### 7.6 Coexistence with NINA-`.json` ingest

TP currently loads from `MainForm.NinaTargetsRootPath`. Three coexistence shapes:

1. **Replace** — TSP DB becomes the only source. Loses targets that exist only as `.json` sequence files.
2. **Source toggle** — radio button: "NINA targets folder" vs. "TSP database". One at a time.
3. **Merge** — load both, dedupe by name (or by guid where available). Same target editable in both ends up as one row in TP.

**Recommendation:** (2) for v1 with the source persisted in `SettingsStore`. (3) is appealing but the dedupe semantics get fiddly (`.json` files don't carry TSP guids).

---

## 8. Gotchas and footguns

- **Signed dec, not magnitude+flag.** TS stores `dec` as signed decimal degrees; TP stores magnitude + `North`. The TP constructor normalizes correctly — but if any TP code path ever reads `ts.dec` and treats it as a magnitude, sign loss silently mis-locates the target. The mapper should always go through the `Target(...)` constructor, never assign fields by hand.
- **`epochcode` is an enum, not a year.** The value `2` = J2000. The Library implicitly assumes J2000 throughout. Any TSP target with `epochcode != 2` (rare; would be `Epoch.JNOW = 1` or `B1950 = 0`) should be logged and either skipped or pre-processed for precession.
- **No `__MigrationHistory` table.** TSP's EF6 setup uses raw script migrations indexed by `PRAGMA user_version`. The current snapshot is at version 24. TP should record the version it was tested against and emit a single info-level log if it encounters a higher version (don't refuse to read — the relevant columns rarely move — but flag).
- **Rollback-journal, not WAL.** A TSP writer briefly blocks TP readers. Always open read-only with a busy-timeout; never assume reads are instant.
- **`profileId` is a NINA guid, not a name.** If TP shows a profile picker, it needs to resolve the guid to a human name. NINA stores profiles under `%LocalAppData%\NINA\Profiles\*.profile` — each file is XML with a `<Name>` element. TP can read those names without taking a dependency on NINA assemblies.
- **No `UNIQUE(name)` constraint.** Two targets can share a name (TSP's "GetPasteCopy" suffixes with " (Copy)", but the user can rename freely). The `guid` is the only stable identifier — TP's dedupe must key on guid where possible, name as a fallback only.
- **No user-defined indexes.** The schema has only primary-key indexes. At 100s-of-targets scale this is fine; if the DB ever grows to 10k+ targets (multi-decade archives), TP's filter-by-profileId-and-state query would benefit from an index — but that's TSP's call, not TP's.
- **Schema may drift.** TSP is actively developed (memory `reference_nina_local_sources` notes the local clone is ~one PR behind upstream). Validate column existence at startup, not at every read.
- **The snapshot in `TS DataBase Example/` is point-in-time.** Schema-relevant decisions should be re-confirmed against a freshly-copied live file when implementation starts; the snapshot date is `2026-05-16`.
- **XFM is already a reader.** `E:\Projects\VisualStudio\Astronomy\XisfFileManager\XisfFileManager\TargetScheduler\` has shipped logic for reading all 8 TSP tables via `Microsoft.Data.Sqlite`. The mapper pattern (`Data\ITableMapper.cs` + `Data\TableMappers.cs`, one concrete mapper per table, schema-as-comment at the top of each `Tables\*.cs` POCO) is clean, ORM-free, matches the stack IS will use, and is worth borrowing wholesale for TP's reader. Three caveats from reading XFM's code:
  - **No `Mode=ReadOnly`** in the connection string (`SqlLiteReader.cs:60` is `Data Source={file};`). TP must specify read-only — see §3.
  - **No busy-timeout.** TP must set one.
  - **Eager `SELECT *` over every table** including `imagedata` (3,330 rows + BLOBs in the snapshot). TP should query only `target` and `project`, with explicit column lists so additive TSP migrations don't break the read.
  - **Schema-drift evidence.** XFM's `Tables\Target.cs` schema comment shows `overrideExposureOrder TEXT`; the live snapshot has `unusedOEO TEXT`. TSP renamed the column at some point; XFM's POCO is out of date (the mapper doesn't read that column, so no runtime bug — but the comment lies). Reinforces "validate column existence at startup, not at every read" above.

---

## 9. Suggested implementation slice (not prescriptive)

If/when the user greenlights this:

1. **Library — none.** `Astronomy.Core.Targets.Target` already fits. No Library change needed.
2. **TP `Nina/`** — add a sibling to `TargetLoader.cs` (the `.json` ingest), e.g. `Scheduler/SchedulerDbLoader.cs`, that returns `IReadOnlyList<Target>` given a `schedulerdb.sqlite` path and an optional profile filter. Pure read, no UI. Lift the `ITableMapper<T>` interface + per-table mapper pattern from `XisfFileManager\Data\` (one file, ~175 lines for all 8 tables — TP needs only 2). Fix the connection string to include `Mode=ReadOnly;Cache=Shared;` and set a busy-timeout.
3. **TP `Settings/SettingsStore.cs`** — add a `TargetSource` enum (`NinaFolder`, `SchedulerDb`) plus the chosen `profileId` for the DB case.
4. **TP `Forms/MainForm.cs`** — add the source toggle, route both paths into `mSelection.SetKnownTargets(...)`. Existing chart pipeline unchanged (it's source-agnostic).
5. **Personal defaults** — extend `PersonalDefaults.cs` with `SchedulerDbPath` (default = `%LocalAppData%\NINA\SchedulerPlugin\schedulerdb.sqlite`) so per-developer overrides work the same way as the existing NINA root path. The author's machine would override to `\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite`.

Estimated touch: ~1 new file (~150 lines), small additions to MainForm and SettingsStore, no Library change.

---

## 10. What not to repeat (schema critique)

TP is *consuming* this schema, so the critique below is not actionable here — it documents the friction TP is wrapping defensively, and serves as the negative-space brief for the IS schema design (`docs/design/is-scheduler-db-schema.md`). Each item is a guardrail for that future design.

1. **Migration debris in the DDL.** Every `CREATE TABLE` carries its original columns inside the parens, then years of `ALTER TABLE ADD COLUMN` accreted on a single trailing line outside. `exposuretemplate` has 8 columns jammed onto one line after `maximumhumidity REAL,`. The schema text is the audit log.
2. **Dead columns left in place.** `target.unusedOEO TEXT` is literally in the schema. Same energy: `acquiredimage.exposureId INTEGER DEFAULT 0` with no FK declared.
3. **Sentinels instead of NULL or flags.** `exposure = -1` → "use template default"; `target.priority = -1` → "inherit"; `project.maximumAltitude = 0` → "unset"; `readoutmode = -1` → "default". Each consumer learns each sentinel separately. Zero documented in DDL.
4. **Dual identity on every entity.** `Id INTEGER PK` + `guid TEXT`. EF needs the int; sync / JSON uses the guid. Joins can use either. Pick one.
5. **`profileId` denormalized as 36-char TEXT across 6 tables** (`project`, `exposureplan`, `exposuretemplate`, `profilepreference`, `acquiredimage`, `flathistory`). No `profile` table. Scoping queries are scattered string compares.
6. **No indexes on FKs.** `target.projectid`, `exposureplan.targetid`, `acquiredimage.targetId`, `imagedata.acquiredimageid` — none indexed. Free at 100 targets; painful once `acquiredimage` is multi-year.
7. **Casing / quoting drift inside the same table.** `acquiredimage` alone mixes backticks and double-quotes plus three naming conventions: `Id`, `projectId`, `targetId`, `acquireddate`, `filtername`, `gradingStatus`, `rejectreason`.
8. **Rollback-journal, not WAL.** Actively hostile to the multi-reader use case (TP, XFM, ISP). One-line setting; never flipped.
9. **Magic-int enums with no CHECK, no enum table, no comment.** `epochcode = 2` means J2000 — readable only via NINA source. Same for `state` (0..3) and `priority` (0..2) in `project`.
10. **No `__MigrationHistory`.** Tracked via `PRAGMA user_version = 24` + raw SQL scripts. Consumers cannot introspect what migrations were applied — you externally know the version you tested against.

Synthesis: each decision is locally defensible (EF6 conventions, fear of dropping columns, "we'll index it when it hurts"), but the aggregate is what TP wraps defensively. The textbook outcome of single-author, Code-First, never-rewrite-the-schema development.
