# Notebook

Running lab notebook — chronological empirical findings made while doing the work: measurements, one-off observations, small gotchas that don't yet merit a reference-doc home. Newest entries first; date every entry. Substantial standalone records (designs, reviews, decisions) go to `docs/YYYY-MM-DD-<slug>.md` instead; findings that harden into standing truth graduate to ARCHITECTURE.md / README.md / CLAUDE.md and get pruned here.

## 2026-07-07 — warmup perf figure disagrees between code comments and docs

Docs-architecture audit surfaced an unreconciled cache-warmup timing: the in-code comments say **~1–2 sec for 44 targets** (`Caches/ChartCacheStore.cs:34`, `Caches/IChartCacheStore.cs:36`) while every doc says **~2–4 sec** (CLAUDE.md threading section, ROADMAP.md, VERIFICATION.md, `docs/design/chart-fits-cache.md`). Left both as-is pending a fresh timing run — needs a Release-build stopwatch over the Penns Park 44-target NINA load to decide which figure is current, then align code comments + docs to the measured number.

---

*Empirical findings before 2026-07-07 live in commit messages, ROADMAP.md §Recently shipped, and the dated `docs/` notes.*
