# RELEASING.md — publishing TP to GitHub

> **Charter:** the rules for pushes to the public GitHub mirror. **The local repo is ground
> truth; GitHub is the public face** — a distribution channel, never the canonical location.
> Nothing here changes how development works; it only governs what the public sees and when.

## The mirror

`origin` = https://github.com/Apoplectic1/TargetPlanner (public). No other remotes. `main` is
the only branch on origin.

## Branch policy

- **`dev` = working branch.** All work lands here. **`dev` never pushes.**
- **`main` = distribution-ready ref, and every push of `main` carries a tag** — `vX.Y.Z`
  (semver, `v`-prefixed; the portfolio convention). Publish = fast-forward `main` to the
  chosen `dev` commit, tag it, push both:
  ```bash
  git checkout main && git merge --ff-only dev
  git tag vX.Y.Z
  git push origin main vX.Y.Z
  git checkout dev
  ```
- Publish at natural completion points (a shipped unit of work, docs riding the same commit) —
  not on a schedule, and never mid-change. The working tree must be clean and the build/tests
  green at the published commit (see `VERIFICATION.md`).
- **AL coordination (pre-flight):** the installer embeds the sibling `..\Library` working tree
  at pack time, unpinned. If AL is dirty or has moved past its last published tag, **publish AL
  first** (see Library `RELEASING.md`) so the payload's `Astronomy.*` DLLs stamp a clean
  `X.Y.Z` that exists on AL's public mirror. `release.ps1` enforces this — it aborts on a
  dirty Library tree or an `-alpha` MinVer stamp in the payload. No tag → no push: the tag is what
  makes a `main` state a published state.
- **Docs-only exception (2026-08-02):** a `main` push may omit the tag when the delta contains
  only documentation/images — nothing that changes the built app — so the GitHub storefront
  (README, screenshots) can update without minting a release. Any change to code or build
  inputs keeps the full no-tag-no-push rule.

## Distribution: Velopack installers, built locally

Installers ship as GitHub Releases **packed and uploaded from this machine** via
`scripts\release.ps1` — the portfolio's one release mechanism (same model as TSM/XFM; the
sibling Library repo stays unpublished, so only local builds resolve TP's
`ProjectReference`s). Setup.exe installs to `%LocalAppData%\TargetPlanner` with Start Menu /
Desktop shortcuts and an Apps & Features entry.

One-time setup: `dotnet tool install -g vpk`, and `$env:GITHUB_TOKEN` = a PAT with
`public_repo` scope (only needed for upload; `-NoUpload` dry-runs without it). For a
permanent install: `setx GITHUB_TOKEN ghp_...` (re-open the shell to pick it up).

Per-release flow:
```powershell
# on main, at the published commit (see Branch policy)
git tag vX.Y.Z
git push origin main vX.Y.Z
.\scripts\release.ps1          # build Release|x64 → vpk pack → upload to GitHub Releases
```
- **Versions come from the tag** via MinVer (`<MinVerTagPrefix>v</MinVerTagPrefix>`, same as
  TSM/XFM) — the same tag gates the `main` push, names the GitHub Release, stamps the
  assembly, and shows in the window title (`TargetPlanner X.Y.Z`). No version files; untagged
  commits shape as `-alpha` prereleases, which the updater's prerelease filter never offers
  to installed copies.
- **The installed app self-updates**: startup check of this repo's Releases via Velopack
  (`Updates/UpdateService.cs`, prompted), plus a manual surface at
  `Help → Check for Updates...`.
- **Dry-run:** `.\scripts\release.ps1 -NoUpload` → artifacts in `Releases\` (gitignored); run
  the Setup.exe there to test an install locally. vpk refuses to re-pack a version already
  present in `Releases\` — delete that folder before repeating a dry-run at the same tag.
- The app's `Velopack` NuGet package and the `vpk` CLI should stay on matching versions
  (both 1.2.0 as of 2026-08-02) — `vpk pack` warns on skew.

Latest released tag: **`v1.3.2`** (Ctrl+N dialog consumed from AL's `Astronomy.Diagnostics.WinForms`
— payload gains that DLL, stamped `1.5.0`; MIT license; AL coordination gate staged). Prior:
`v1.3.1`. (Historical wrinkle: `v1.1.0`–`v1.2.0` were tagged but
never published as GitHub Releases — `v1.0.0` was the only installable release before
`v1.3.0`.)

## Content rules (what is deliberately public)

- **`README.md` is the storefront** — user-facing description (behaviour, defaults, chart
  UX). Development/testing minutiae stay out.
- **Site coordinates + personal presets ship** — a deliberate solo-consumer trade-off (see
  `DOMAIN.md` → personal presets). If TP ever ships to others, split-to-gitignored is the
  fix — not scrubbing history.
- **Never in the repo, so never published:** tokens/credentials (none exist).
- History publishes whole. Anything that must not be public must never be committed — there
  is no post-hoc scrub step.
- **No code signing.** Windows SmartScreen will warn on first install for new users —
  acceptable for personal/lab use; revisit if distributing more broadly.
