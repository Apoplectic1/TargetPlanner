# Releasing TargetPlanner

Distribution: [GitHub Releases on Apoplectic1/TargetPlanner](https://github.com/Apoplectic1/TargetPlanner/releases) via [Velopack](https://velopack.io). Setup.exe installs to `%LocalAppData%\TargetPlanner` with Start Menu / Desktop shortcuts and an Apps & Features entry. Updates are checked at app startup (prompted) and via `Help → Check for Updates...`.

## One-time setup

1. Install the Velopack CLI (.NET tool, global install):
   ```powershell
   dotnet tool install -g vpk
   ```

2. Generate a GitHub Personal Access Token with the `public_repo` scope at https://github.com/settings/tokens, and set it as an environment variable:
   ```powershell
   $env:GITHUB_TOKEN = "ghp_..."
   ```
   For permanent install: `setx GITHUB_TOKEN ghp_...` (re-open the shell to pick it up).

## Per-release flow

1. Make sure the working tree is clean and `dev`/`main` reflect the commits you want to ship.
2. Tag the release commit with `vX.Y.Z` (semver, `v`-prefixed):
   ```powershell
   git tag v1.0.1
   git push origin v1.0.1
   ```
3. Run the release script from the repo root:
   ```powershell
   .\scripts\release.ps1
   ```

   What it does:
   - Reads the version from `git describe --tags --abbrev=0` (drops the `v`).
   - Builds `Release|x64` (which MinVer stamps with the same version into the assembly).
   - `vpk pack` produces `Releases/Setup.exe` plus a delta package and `RELEASES` manifest.
   - `vpk upload github --publish` uploads everything to a new GitHub release.

4. Verify on https://github.com/Apoplectic1/TargetPlanner/releases that the release shows up with `Setup.exe`, `TargetPlanner-X.Y.Z-full.nupkg`, and `RELEASES`.

5. Existing installs will detect the release on next launch (or via `Help → Check for Updates...`).

## Local dry-run (no upload)

```powershell
.\scripts\release.ps1 -NoUpload
```

Output lands in `.\Releases\`. Run `.\Releases\Setup.exe` to install locally and confirm the install / shortcuts / Apps & Features entry. Bump the tag, rebuild, and re-launch the previously-installed app to verify the in-app update prompt.

## Versioning notes

- Versions are derived from git tags via the [MinVer](https://github.com/adamralph/minver) NuGet package (see `<MinVerTagPrefix>v</MinVerTagPrefix>` in `TargetPlanner/TargetPlanner.csproj`). No version files to edit.
- Untagged commits past a tag get a prerelease shape like `1.0.1-alpha.0.5+sha`. Velopack `--prerelease=false` (the default in `UpdateService`) ignores these, so dev builds don't accidentally roll out to installed users.
- The `Releases/` folder is in `.gitignore` — the artifacts live on GitHub, not in the repo.

## What is NOT in scope

- **Code signing.** Without a code signing certificate, Windows SmartScreen will warn on first install for new users. Acceptable for personal / lab use; revisit if distributing more broadly.
- **Self-hosted update server.** Distribution is GitHub Releases. To switch later, change the `GithubSource` in `TargetPlanner/Updates/UpdateService.cs` and the `vpk upload` target in `scripts/release.ps1`.
