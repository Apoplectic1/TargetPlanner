#requires -version 5.1
# Build, pack, and (optionally) upload a TargetPlanner release to GitHub.
#
# Prerequisites (one-time per machine):
#   dotnet tool install -g vpk
#   $env:GITHUB_TOKEN = "<personal-access-token-with-public_repo-scope>"
#
# Per-release flow (see RELEASING.md):
#   1. git tag vX.Y.Z on main (local only -- do NOT push yet)
#   2. .\scripts\release.ps1   # build -> gate -> pack -> push main -> upload -> push tag
#
# The script owns the pushes so the invariant "a tag on origin always has an installable
# GitHub Release" holds by construction (the v1.1.0-v1.2.0 tags-without-Releases wrinkle):
#   - `main` pushes BEFORE upload: vpk's createRelease names a tag that doesn't exist on
#     origin yet, so GitHub materialises the tag ref at the default branch HEAD -- which
#     must already be the release commit.
#   - The local tag pushes AFTER a successful upload. GitHub's tag from the upload is
#     LIGHTWEIGHT, ours is annotated (same commit, different object), so the push step
#     verifies the remote commit and force-replaces the tag. Upload fails -> no tag on origin.
#
# The script reads the latest reachable tag via `git describe --tags --abbrev=0` and uses
# that as the release version. MinVer (in TargetPlanner.csproj) reads the same tag at build
# time so the assembly version matches.

[CmdletBinding()]
param(
    # Skip the GitHub upload step (useful for local dry-runs of vpk pack).
    [switch] $NoUpload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $tag = git describe --tags --abbrev=0 2>$null
    if (-not $tag) {
        throw "No git tag reachable from HEAD. Tag a release first (e.g. 'git tag v1.0.0')."
    }
    $version = $tag.TrimStart('v')
    Write-Host "Releasing TargetPlanner $version (tag $tag)" -ForegroundColor Cyan

    if (-not $NoUpload) {
        # Publish gates: the script is about to push main + tag on the user's behalf, so the
        # local state must be exactly the documented publish state (RELEASING.md branch policy).
        $branch = git rev-parse --abbrev-ref HEAD
        if ($branch -ne 'main') { throw "Publishing runs from main (currently on '$branch'). Dry-run anywhere with -NoUpload." }
        if (git status --porcelain) { throw "Working tree is dirty - publish requires a clean tree at the tagged commit." }
        $tagCommit = git rev-parse "$tag^{commit}"
        $headCommit = git rev-parse HEAD
        if ($tagCommit -ne $headCommit) { throw "Tag $tag points at $($tagCommit.Substring(0,8)) but HEAD is $($headCommit.Substring(0,8)) - tag the release commit first." }
    }
    elseif ((git rev-parse "$tag^{commit}") -ne (git rev-parse HEAD)) {
        Write-Host "note: HEAD is past tag $tag - dry-run still packs $version." -ForegroundColor Yellow
    }

    Write-Host "`n--> dotnet build (Release|x64)" -ForegroundColor Cyan
    dotnet build TargetPlanner.sln -c Release -p:Platform=x64 -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    # The output path derives from the csproj TFM — hardcoding it shipped stale payloads when
    # the 2026-08-10 TFM raise left the old output dir on disk (v1.3.4/v1.3.5 packed v1.3.3
    # bits; the path check passed against the leftover directory).
    $tfm = [regex]::Match((Get-Content (Join-Path $repoRoot 'TargetPlanner\TargetPlanner.csproj') -Raw),
        '<TargetFramework>([^<]+)</TargetFramework>').Groups[1].Value
    if (-not $tfm) { throw "Could not read <TargetFramework> from TargetPlanner.csproj" }
    $bin = Join-Path $repoRoot "TargetPlanner\bin\x64\Release\$tfm"
    if (-not (Test-Path $bin)) { throw "Build output not found at $bin" }

    # Stamp gate (XFM model): the packed exe must stamp the tag's version — catches stale
    # output dirs and MinVer cache leaks alike, making this class of failure unshippable.
    $exeVer = (Get-Item (Join-Path $bin 'TargetPlanner.exe')).VersionInfo.ProductVersion
    if (($exeVer -split '\+')[0] -ne $version) {
        throw "Packed TargetPlanner.exe stamps '$exeVer' - expected '$version' from tag $tag (stale output dir or MinVer mismatch)."
    }

    # AL coordination gate (see RELEASING.md): the payload embeds the sibling Library working
    # tree at pack time, unpinned - it must be a published (tagged, clean) AL state.
    $alDirty = git -C (Join-Path $repoRoot '..\Library') status --porcelain
    if ($alDirty) { throw "..\Library working tree is dirty - commit and release AL first (Library\RELEASING.md)." }
    $alVer = (Get-Item (Join-Path $bin 'Astronomy.Core.dll')).VersionInfo.ProductVersion
    if ($alVer -match '-alpha') { throw "Embedded Astronomy.Core.dll stamps '$alVer' (untagged AL state) - release AL first (Library\RELEASING.md)." }

    Write-Host "`n--> vpk pack" -ForegroundColor Cyan
    vpk pack `
        -u TargetPlanner `
        -v $version `
        -p $bin `
        -e TargetPlanner.exe `
        --packTitle 'TargetPlanner' `
        --icon (Join-Path $repoRoot 'TargetPlanner\Manager.ico')
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

    if ($NoUpload) {
        Write-Host "`nDone. Skipping upload (-NoUpload). Output is in .\Releases\" -ForegroundColor Yellow
        return
    }

    if (-not $env:GITHUB_TOKEN) {
        throw "GITHUB_TOKEN env var is not set. Either set it or re-run with -NoUpload."
    }

    # main goes up BEFORE the upload: GitHub materialises the (not-yet-pushed) tag at the
    # default branch HEAD when vpk creates the Release, so that HEAD must be this commit.
    Write-Host "`n--> git push origin main" -ForegroundColor Cyan
    git push origin main
    if ($LASTEXITCODE -ne 0) { throw "git push origin main failed" }

    Write-Host "`n--> vpk upload github (publish)" -ForegroundColor Cyan
    # --tag aligns the GitHub release tag with the git tag (vpk's default would
    # be the bare version "1.0.0", but our git/MinVer convention is "v1.0.0").
    vpk upload github `
        --repoUrl 'https://github.com/Apoplectic1/TargetPlanner' `
        --token $env:GITHUB_TOKEN `
        --tag $tag `
        --publish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

    # Tag pushes only after a successful upload -- a tag on origin implies an installable
    # Release. GitHub's createRelease minted a LIGHTWEIGHT tag at this commit during the
    # upload, so the plain push of our ANNOTATED tag is normally rejected as already-exists
    # (same commit, different tag object -- bit v1.3.4 and v1.3.5, 2026-08-10). Handle it:
    # verify the remote tag sits on the release commit, then force-replace it with the
    # annotated tag. A force onto any OTHER commit is refused -- that is a real conflict.
    Write-Host "`n--> git push origin $tag" -ForegroundColor Cyan
    git push origin $tag
    if ($LASTEXITCODE -ne 0) {
        $remoteLines = @(git ls-remote origin "refs/tags/$tag*")
        $peeled = $remoteLines | Where-Object { $_ -match '\^\{\}$' } | Select-Object -First 1
        $plain  = $remoteLines | Where-Object { $_ -notmatch '\^\{\}$' } | Select-Object -First 1
        $remoteRef = if ($peeled) { $peeled } else { $plain }   # peeled line = annotated; absent for lightweight
        $remoteCommit = ($remoteRef -split '\s+')[0]
        if ($remoteCommit -ne $headCommit) {
            throw "Remote tag $tag points at '$remoteCommit', not the release commit $($headCommit.Substring(0,8)) - resolve manually (Release is live)."
        }
        Write-Host "    remote $tag is GitHub's lightweight tag at the release commit - replacing with the annotated tag" -ForegroundColor Yellow
        git push --force origin $tag
        if ($LASTEXITCODE -ne 0) { throw "git push --force origin $tag failed (Release is live; push the tag manually)" }
    }

    Write-Host "`nReleased TargetPlanner $version to GitHub." -ForegroundColor Green
}
finally {
    Pop-Location
}
