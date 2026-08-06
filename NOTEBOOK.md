# Notebook

Running lab notebook — chronological empirical findings made while doing the work: measurements, one-off observations, small gotchas that don't yet merit a reference-doc home. Newest entries first; date every entry. Substantial standalone records (designs, reviews, decisions) go to `docs/YYYY-MM-DD-<slug>.md` instead; findings that harden into standing truth graduate to ARCHITECTURE.md / README.md / CLAUDE.md and get pruned here.

## 2026-08-06 — NINA nightly moves into adjacent territory: offline framing sky map does single-target altitude planning

NINA develop 3.3.0.1051–1053 (upstream #333) overhauled the Framing Assistant's offline sky map into
a tested pipeline (`NINA.WPF.Base/SkySurvey/SkyMapSceneBuilder` et al.) and grew features that overlap
TP's mission at the single-target grain: a switchable **Alt/Az projection**, an optional **local/custom
horizon that clips annotations and imagery below it** (including wide-field), and offline **date/time
steppers** where the selected date **drives the altitude chart**. Persisted via new
`FramingAssistantSettings` fields + `SkyMapProjectionMode`. Not a threat to TP's multi-target
"plan tonight's session" story, but NINA's built-in "will *this* target clear *my* horizon on *that*
date" answer is now materially better — worth remembering when revisiting TP scope or pitching what TP
adds over stock NINA.



Settled the code-vs-docs warmup-timing disagreement (comments said ~1–2 s, docs said ~2–4 s) with a fresh Release-build measurement. Added a `Stopwatch` around `WarmupAsync` (`MainForm.TargetLoadingPresenter.cs`) logging `Warmup complete targets=… yearDaysMs=… fitsMs=… totalMs=…` under the `Cache` DIAG channel. Two Release runs over the startup image-library auto-load (`E:\…\Processing`, **77 targets** resolved):

| run | yearDaysMs | fitsMs | totalMs |
|---|---|---|---|
| 1 (cold) | 1877 | 24 | 1901 |
| 2 (warm) | 1956 | 25 | 1981 |

Findings: total warmup **~2 s for ~77 targets**, entirely **yearDays-dominated**; per-`HdmKey` fits are **near-free at boot** (~25 ms) because the boot Hdm carries no active filter (`F=(none)`), so the "plus a few seconds for fits" the docs claimed was stale. The "1–2 s" code comment was closer to right. Reconciled CLAUDE.md / VERIFICATION.md / `chart-fits-cache.md` + the two code comments to "~2 s for a ~77-target library". Distinct metrics left untouched (Sessions bg fit ~10 s, Day tonight fit ~10 ms, dual-series Render ~1 s — each a separate path, not re-measured). Historical ROADMAP shipped entries kept as period-accurate snapshots.

---

*Empirical findings before 2026-07-07 live in commit messages, ROADMAP.md §Recently shipped, and the dated `docs/` notes.*

---

*Empirical findings before 2026-07-07 live in commit messages, ROADMAP.md §Recently shipped, and the dated `docs/` notes.*
