#requires -version 5.1
# Build, pack, and (optionally) upload a TargetPlanner release to GitHub.
#
# Prerequisites (one-time per machine):
#   dotnet tool install -g vpk
#   $env:GITHUB_TOKEN = "<personal-access-token-with-public_repo-scope>"
#
# Per-release flow (see RELEASING.md):
#   1. git tag vX.Y.Z on main, push main + tag
#   2. .\scripts\release.ps1
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

    Write-Host "`n--> dotnet build (Release|x64)" -ForegroundColor Cyan
    dotnet build TargetPlanner.sln -c Release -p:Platform=x64 -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    $bin = Join-Path $repoRoot 'TargetPlanner\bin\x64\Release\net10.0-windows10.0.19041'
    if (-not (Test-Path $bin)) { throw "Build output not found at $bin" }

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

    Write-Host "`n--> vpk upload github (publish)" -ForegroundColor Cyan
    # --tag aligns the GitHub release tag with the git tag (vpk's default would
    # be the bare version "1.0.0", but our git/MinVer convention is "v1.0.0").
    vpk upload github `
        --repoUrl 'https://github.com/Apoplectic1/TargetPlanner' `
        --token $env:GITHUB_TOKEN `
        --tag $tag `
        --publish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

    Write-Host "`nReleased TargetPlanner $version to GitHub." -ForegroundColor Green
}
finally {
    Pop-Location
}
