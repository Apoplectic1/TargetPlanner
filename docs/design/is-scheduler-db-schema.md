# IS `scheduler.db` — schema design brief

**Status:** Sketched 2026-05-16, not yet implemented. Authoritative schema is **owned by IS** (the .NET 10 desktop app per `MEMORY.md → project_intervalscheduler`); ISP / ISS / XisfManager / TP read it, never write through it as a side channel. (XisfManager's image-grading write of `exposure_plan.accepted_count` is the one consumer-write carve-out and is treated as part of IS's contract.)

This brief is the positive inversion of the antipatterns documented in [`../../TS DataBase Example/TS_SCHEDULER_INGEST.md`](../../TS%20DataBase%20Example/TS_SCHEDULER_INGEST.md) §10 ("What not to repeat"). Each section below names the TS antipattern it is replacing and the rule IS adopts instead. The aim is not a green-field clean-room rebuild of TS — it's a deliberate, narrow set of design rules that future migration scripts and future consumers can rely on without having to learn historical magic.

---

## 1. Scope of this brief

In scope:
- **Rules** the schema will obey (naming, identity, FKs, enums, NULLs, journaling, indexes, migration tracking).
- **Sketch of tables** IS needs to own — names, relationships, and a few non-obvious columns. Not the full DDL.
- **Open decisions** that have to be made before writing migration 0001 (mostly around coexistence with TS).

Out of scope:
- Full DDL — written once the rules are accepted and the open decisions resolved.
- ORM choice (EF Core vs. Dapper vs. raw `Microsoft.Data.Sqlite`). Schema rules are ORM-agnostic.
- Plan / scoring / runtime-execution tables (ISP-owned, not IS-owned — separate brief).

---

## 2. Rules (the inversions)

Each rule cites the TS antipattern in `TS DataBase Example/TS_SCHEDULER_INGEST.md §10` it replaces.

### R1 — One identity per row (replaces TS §4)

Every entity has a single primary key. **GUID as PK**, stored as `BLOB(16)` (not `TEXT(36)`) for both storage and comparison cost. Apps round-trip entities over JSON anyway; the GUID is the only stable identifier the network sees, so it is also the only key the database needs. No surrogate `Id INTEGER AUTOINCREMENT` shadow column.

Trade-off: 16-byte PKs are slightly larger than 4-byte int PKs (matters at billions of rows; here it does not). Removes a whole class of "which key did this consumer use?" bugs.

### R2 — Normalize `profile` (replaces TS §5)

A `profile` table with the GUID PK and a human-readable `name` column. Every consuming table holds `profile_id BLOB(16)` as an FK — not a 36-char string scattered across six tables. Scoping queries become an indexed integer-comparison join, not a full-table string scan.

### R3 — NULL means unset; sentinels are forbidden (replaces TS §3)

If a value is optional or inherited, the column is `NULL`. No `-1` for "use default", no `0` for "unset". Inheritance is explicit:

- `target.priority` is `NULL` when the target inherits `project.priority`. Resolution is `COALESCE(target.priority, project.priority)`, computed in the consumer or a `VIEW`, not stored.
- `exposure_plan.exposure_seconds` is `NULL` when the plan inherits `exposure_template.default_exposure_seconds`. Same pattern.
- "Unset" maxima (e.g. `project.maximum_altitude_deg`) are `NULL`, not `0`.

### R4 — Real enums via lookup tables + CHECK (replaces TS §9)

Every magic int gets two things: a `CHECK (col IN (0, 1, 2, ...))` on the column and a lookup table whose PK is the enum value and whose `name` column documents it. E.g.:

```sql
CREATE TABLE project_state (
    id    INTEGER PRIMARY KEY,
    name  TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO project_state (id, name) VALUES (0, 'Draft'), (1, 'Active'), (2, 'Inactive'), (3, 'Closed');

CREATE TABLE project (
    ...
    state_id INTEGER NOT NULL REFERENCES project_state(id),
    CHECK (state_id IN (0, 1, 2, 3))
);
```

A SQL consumer can `JOIN project_state` to get human names without consulting source code. Same treatment for `priority`, `epoch_code`, `flats_handling`, `twilight_level`. Migration scripts add new enum values via `INSERT INTO project_state` + relaxed `CHECK`, never via in-place re-numbering.

### R5 — Index every FK (replaces TS §6)

Every `REFERENCES` column gets an explicit `CREATE INDEX`. Default name is `ix_<table>_<col>`. This is mandatory in the migration scripts, not an afterthought. Composite indexes for hot join paths (`(profile_id, state_id)` on `project`, `(target_id, exposure_template_id)` on `exposure_plan`).

### R6 — WAL is the default journal mode (replaces TS §8)

`PRAGMA journal_mode = WAL` is run by the migration runner on first open. Readers do not block writers and vice versa. This is the only mode that makes the multi-consumer use case (IS edits, ISP reads + writes grading state, XisfManager writes `accepted_count`, TP reads targets) actually work concurrently. `synchronous = NORMAL` is the WAL companion default.

### R7 — `snake_case` everywhere, no quoting required (replaces TS §7)

Every identifier is `snake_case`. Tables singular, columns lowercase. No backticks, no double-quotes anywhere in the DDL. A reader can grep the schema and the application code with the same query.

### R8 — Schema migrations tracked, idempotent, and introspectable (replaces TS §10)

A `schema_migration` table holds one row per applied migration:

```sql
CREATE TABLE schema_migration (
    version     INTEGER PRIMARY KEY,
    name        TEXT NOT NULL,
    applied_at  INTEGER NOT NULL  -- UNIX seconds
) WITHOUT ROWID;
```

`PRAGMA user_version` stays in sync as a fast version check, but the table is the authoritative log. Each migration script is `NNNN_short_name.sql` under `migrations/`, runs in a transaction, and self-inserts its row. Any consumer can `SELECT version, name FROM schema_migration ORDER BY version` to know exactly what shape it is reading.

### R9 — Dropped columns are dropped (replaces TS §2)

When a column becomes unused, the migration that retires it drops the column. SQLite 3.35+ (March 2021) supports `ALTER TABLE ... DROP COLUMN` directly; older targets do the table-rebuild dance. There is never a `unused_xxx` column in the live schema; the column either exists and is used or it does not exist. Migration `NNNN_drop_<col>.sql` is a normal migration script, not a special case.

### R10 — DDL is rewritten clean on every major migration, not appended (replaces TS §1)

When `schema_migration.version` jumps a major number (every ~10 minor migrations), the migration script rewrites the affected `CREATE TABLE` cleanly via the SQLite table-rebuild idiom (`CREATE new`, `INSERT INTO new SELECT FROM old`, `DROP old`, `ALTER RENAME new TO old`). The DDL stored in `sqlite_master` does not accumulate `ALTER TABLE` trailers. Reading `sqlite_master` gives you the canonical current schema, not its evolution history — the migration history lives in `schema_migration` and in the `migrations/` directory.

### R11 — Boolean columns are `INTEGER` with `CHECK (col IN (0, 1))`

Minor. Same on-disk shape as TS but with the constraint, so a consumer cannot accidentally insert `2`.

### R12 — Timestamps are UNIX seconds (`INTEGER NOT NULL`)

UTC, no zone column. Already what TS does and it works. Documented once here so consumers do not invent their own conventions.

---

## 3. Tables (sketch)

Names below use the IS conventions. Where a TS table maps directly, the TS name is noted in parens — the IS schema is *informed by* TS's data model but does not preserve TS's names.

### Core hierarchy

- `profile` (replaces the scattered `profileId` TEXT — see R2) — `id BLOB(16) PK`, `name TEXT NOT NULL`, `created_at INTEGER NOT NULL`. Probably also `nina_profile_guid TEXT NULL` so IS can correlate its profile records back to NINA's actual profile files if it ever wants to.
- `project` (TS `project`) — `id`, `profile_id` FK, `name`, `description`, `state_id` FK → `project_state`, `priority_id` FK → `project_priority`, `created_at`, `active_at` (NULL = never activated), `inactive_at` (NULL = still active), `minimum_time_minutes`, `minimum_altitude_deg` (NULL = no floor), `maximum_altitude_deg` (NULL = no ceiling), `use_custom_horizon INTEGER CHECK (0,1)`, `horizon_offset_deg`, `meridian_window_minutes`, `is_mosaic INTEGER CHECK (0,1)`, `enable_grader INTEGER CHECK (0,1)`. CHECK constraints, no sentinels.
- `target` (TS `target`) — `id`, `project_id` FK, `name`, `enabled INTEGER CHECK (0,1) NOT NULL`, `ra_hours` (decimal hours `[0, 24)`, CHECK), `dec_degrees_signed` (decimal degrees `[-90, +90]`, CHECK), `epoch_id` FK → `epoch`, `rotation_deg`, `roi_percent`, `priority_id` FK → `project_priority` NULL (NULL = inherit from project), `created_at`.

Decision worth flagging: TS stores `dec` as **signed degrees**, TP stores it as **magnitude + `North` bool**. The IS schema sticks with signed degrees (one column, CHECK-able, joinable). Consumers that want magnitude+flag (the TP convention) convert at read.

### Acquisition / scheduling

- `exposure_template` (TS `exposuretemplate`) — gain, offset, bin, readout mode, filter name, twilight level, moon avoidance config, etc. NULL means "not set" wherever TS uses `-1` or `0`.
- `exposure_plan` (TS `exposureplan`) — `id`, `target_id` FK, `exposure_template_id` FK, `exposure_seconds` (NULL = inherit template default), `desired_count`, `acquired_count`, `accepted_count`, `enabled INTEGER CHECK (0,1)`.
- `enum: project_state`, `project_priority`, `epoch`, `flats_handling`, `twilight_level` — lookup tables per R4.

### History / image data (consumer-writable for XisfManager)

- `acquired_image` (TS `acquiredimage`) — `id`, `project_id` FK, `target_id` FK, `exposure_plan_id` FK (real FK, not the TS "INTEGER DEFAULT 0" non-FK), `acquired_at`, `filter_name`, `grading_status_id` FK → `grading_status`, `reject_reason_id` FK NULL → `reject_reason`, `metadata_json TEXT`, `image_path TEXT` if XisfManager tracks file location.
- `flat_history` (TS `flathistory`) — same data, normalized FKs.

### Plan / runtime state — out of scope

IS may compute `plan` rows (5-minute precompute per IS architecture) and write them for ISP to read. That is a separate brief (`docs/design/is-plan-schema.md` once it exists) because the plan shape is determined by the IS scheduler's output, which has not been written yet.

---

## 4. Coexistence with TS — the migration question

This is the biggest unresolved decision and has to be answered before any DDL is written.

**Option A — fresh start.** IS ships with an empty `scheduler.db` at first launch. User re-enters projects/targets/templates in IS's UI. TS's `schedulerdb.sqlite` is left alone (it continues to drive any TSP-using NINA workflows the user keeps).

- Pros: clean schema from row 1; no import-mapping bugs; no synchronization story.
- Cons: user re-enters 10 projects / 102 targets / 20 templates. Tedious but one-time.

**Option B — one-time import.** IS migration `0001_seed.sql` (or a Code-First importer) reads from a user-pointed `schedulerdb.sqlite` and translates rows into IS's schema, fixing sentinels → NULLs, magic ints → enum FKs, GUIDs → BLOB(16), `profileId` strings → `profile.id`. After import, IS owns the data; the source `schedulerdb.sqlite` is no longer touched.

- Pros: zero re-entry; user gets to keep their target inventory.
- Cons: importer is a one-shot piece of code that has to be maintained until everyone is migrated; mapping errors are silent data corruption; need an "imported_from" provenance column on every row.

**Option C — dual-read.** IS schema is fresh, but IS exposes a read-only view that joins/projects TS's `schedulerdb.sqlite` *into* IS's schema shape at query time. New work flows into IS-native tables; legacy projects stay in TS until the user explicitly migrates them.

- Pros: smoothest UX; no lossy import.
- Cons: every reader (ISP, XisfManager, TP) has to know about both sources; cross-source joins are a footgun; "which DB owns this target?" becomes a routine question.

**Recommendation (to discuss):** **B with provenance**. One importer, written carefully and tested against the snapshot in `TS DataBase Example/`. Each imported row carries `imported_from_ts_guid TEXT NULL` so the lineage is preserved. The importer is deletable once the user is done with the transition. Avoids C's permanent dual-source complexity and avoids A's re-entry tax.

---

## 5. Open decisions (beyond the migration question)

1. **ORM.** EF Core 9, Dapper, or hand-rolled `Microsoft.Data.Sqlite`? EF Core gives migrations + LINQ at the cost of (a) the EF6-style "schema is whatever the entities say it is" temptation that produced TS's mess, and (b) a startup-time cost. Dapper + hand-written migrations gives full control at the cost of more code. **Lean: Dapper + hand-written migrations.** The schema is the source of truth; entity classes are dumb POCOs.
2. **GUID storage.** `BLOB(16)` is the right answer for size and compare-speed, but every consumer SDK has to agree on byte-order (Microsoft .NET's `Guid.ToByteArray()` returns mixed-endian). A `helpers.sql` documenting the chosen byte-order convention, plus a `Guid.ToBlob()` / `Guid.FromBlob()` helper in a shared `IS.Schema` library, prevents the cross-consumer footgun.
3. **`epoch_code` semantics.** TS stores `J2000 = 2`. IS will almost certainly also be J2000-only in practice. Worth still modeling the enum (`J2000`, `JNOW`, `B1950`) for forward-compat or simplify to a single `NOT NULL DEFAULT 2 CHECK (epoch_id = 2)` until a non-J2000 use case appears?
4. **`acquired_image` ownership of grading writes.** XisfManager's grading flow writes `accepted_count`. Does it write directly to `exposure_plan.accepted_count` (TS's pattern) or to a new `image_grade` row that a trigger rolls up into `accepted_count`? Triggers add complexity but make "where did this number come from?" answerable.
5. **Per-target priority.** Snapshot shows all 102 targets have `priority = -1` (always inherit). Worth keeping the column at all? Lean: keep it as `NULL = inherit`, since the data model wants it even if the user has not used it.
6. **Mosaic relationship.** TS's `isMosaic` is a project-level bool but mosaic panels are siblings under one project. Is there value in modeling a `mosaic` table that groups its `target` rows, or is the flat project→targets relationship sufficient?

---

## 6. References

- [`../../TS DataBase Example/TS_SCHEDULER_INGEST.md`](../../TS%20DataBase%20Example/TS_SCHEDULER_INGEST.md) — TS schema discovery + the §10 critique this brief inverts.
- `MEMORY.md → project_intervalscheduler` — the IS architecture context that puts IS in charge of the schema.
- `MEMORY.md → reference_birdwatcher_imaging_pc` — where the canonical `scheduler.db` lives once IS is deployed.
- `..\..\..\TargetScheduler_Clone\nina.plugin.targetscheduler\NINA.Plugin.TargetScheduler\Database\Schema\*.cs` — TS entity classes (semantic source of column meanings).
- TSP migration scripts under `..\..\..\TargetScheduler_Clone\nina.plugin.targetscheduler\NINA.Plugin.TargetScheduler\Database\Initial\` and `Database\Migrate\` — the historical migration trail.
- `E:\Projects\VisualStudio\Astronomy\XisfFileManager\XisfFileManager\TargetScheduler\` and `XisfFileManager\Data\TableMappers.cs` — XFM's shipped TS reader. The `ITableMapper<T>` interface + per-table mapper pattern over `Microsoft.Data.Sqlite` is the reference shape for IS consumer-side mappers (replace TS-schema-aware POCOs with IS-schema-aware ones; keep the interface). XFM's footguns documented inline in [`../../TS DataBase Example/TS_SCHEDULER_INGEST.md`](../../TS%20DataBase%20Example/TS_SCHEDULER_INGEST.md) §8 — fix them in IS readers from day one.
