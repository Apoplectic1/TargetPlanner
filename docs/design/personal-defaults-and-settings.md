# Personal defaults + user settings architecture

**Status: SUPERSEDED 2026-05-19.** This document describes the three-layer
defaults model (hardcoded C# constants → gitignored `personal-defaults.json` →
`settings.json`) that shipped 2026-05-08 and was collapsed on 2026-05-19. The
`personal-defaults.json` layer was dropped: `settings.json` is now the single
user-state file, seeded on first run from the `PersonalDefaults.BuildSeedSettings()`
C# factory, with "Pattern C" fill for additive-schema migration. The collapse
was driven by sync confusion — edits to `personal-defaults.json` didn't
propagate to existing `settings.json` entries under the old `MergeBuiltins`
zero-fill rule. Current architecture: see the "Defaults resolve at runtime,
two layers deep" bullet in `CLAUDE.md`. This file is kept for the historical
design rationale only.

---

**Original status:** Designed 2026-04-26, not yet implemented.

## Problem

Three intertwined needs:

1. **Ship-safe defaults**: the public binary / public source must not contain author-specific values (location name, lat/long, personal disk paths). Anyone downloading the source or the installer gets neutral placeholders.
2. **Dev convenience**: on the author's machine, the app should still start with the author's preferred location, NINA targets root, etc., without manual setup every fresh install or version bump.
3. **Runtime user prefs**: things the user changes in-app (last selected target, last horizon/duration spinner values, sort order, window size, panel splits, etc.) need to persist across sessions.

Earlier candidate approaches that were rejected:

- **`#if PERSONAL_DEFAULTS` with build-config toggle** — works, but personal data still lives in committed source, just guarded. Doesn't satisfy goal #1.
- **Gitignored partial-class file with build-config `Compile Remove`** — scrubs source successfully but adds MSBuild fiddling and requires the release script to know which configuration excludes the personal file.
- **Properties\Settings.settings + auto-generated wrapper** — see footgun section below.

## Chosen architecture: three layers, runtime-resolved

```
SettingsStore (settings.json)         ← runtime user choices, persisted on change
        ↓ if absent / field missing
PersonalDefaults (personal-defaults.json)  ← dev-only override, gitignored
        ↓ if absent / field missing
hardcoded constants                    ← ship-safe public placeholders
```

Same compiled binary works for everyone. No build-time pivots, no `#if`, no MSBuild conditional includes. The pivot happens entirely at startup based on which files exist on disk.

### Files

| File | Purpose | Location | Lifecycle | In repo? |
|---|---|---|---|---|
| Hardcoded constants | Ship-safe fallbacks | `Settings/PersonalDefaults.cs` (committed) | Compile-time | Yes (public-safe values only) |
| `personal-defaults.json` | Dev override of ship defaults | `%LocalAppData%\TargetPlanner\personal-defaults.json` | Read once at static-ctor time | No (per-developer file) |
| `settings.json` | Runtime user choices | `%AppData%\TargetPlanner\settings.json` | Read at startup, written on change | No (per-user file) |

Why `%LocalAppData%` for personal-defaults vs `%AppData%` for settings: matches the existing `SettingsStore` convention for settings, and personal-defaults is *machine-local* dev data that shouldn't roam.

## Implementation plan

### 1. New file: `TargetPlanner/Settings/PersonalDefaults.cs`

```csharp
public static class PersonalDefaults
{
    public static string LocationName { get; private set; } = "Custom";
    public static double Latitude { get; private set; } = 40.0;
    public static double Longitude { get; private set; } = 75.0;        // West-positive
    public static string NinaTargetsRoot { get; private set; } =
        @"C:\Users\Public\Documents\NINA\Targets";

    static PersonalDefaults() { TryLoad(); }

    private static void TryLoad()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TargetPlanner", "personal-defaults.json");
        if (!File.Exists(path)) return;
        try
        {
            var d = JsonConvert.DeserializeObject<Dto>(File.ReadAllText(path));
            if (!string.IsNullOrWhiteSpace(d?.LocationName)) LocationName = d.LocationName;
            if (d?.Latitude.HasValue == true)               Latitude = d.Latitude.Value;
            if (d?.Longitude.HasValue == true)              Longitude = d.Longitude.Value;
            if (!string.IsNullOrWhiteSpace(d?.NinaTargetsRoot)) NinaTargetsRoot = d.NinaTargetsRoot;
        }
        catch { /* malformed: silently fall through to public defaults */ }
    }

    private class Dto
    {
        public string LocationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string NinaTargetsRoot { get; set; }
    }
}
```

### 2. Edits to existing files

- **`TargetPlanner/Forms/MainForm.cs`** — change `private const string NinaTargetsRootPath = @"E:\..."` to `private static string NinaTargetsRootPath => PersonalDefaults.NinaTargetsRoot;` (or just inline `PersonalDefaults.NinaTargetsRoot` at the two call sites).
- **`TargetPlanner/Forms/MainForm.Designer.cs`** — `ComboBox_Location.Text = "Penns Park"` → either drop (let runtime code set it) or change to `"Custom"`. The runtime location-load logic should set the text from the loaded settings anyway.
- **`TargetPlanner/Settings/SettingsStore.cs`** — line 49: `LastSelectedLocationName = "Penns Park"` → `LastSelectedLocationName = PersonalDefaults.LocationName`. This makes the layered fallback work.
- **`Astronomy.Core/Locations/Location.cs`** (sibling repo) — `Location.Default` constructor uses hardcoded lat/long. Two options:
  - Option A: Pass defaults in via `Location.Default(string name, double lat, double lon)` and call from TargetPlanner with `PersonalDefaults.*` values.
  - Option B: Change Astronomy.Core's hardcoded defaults to public-safe ones (the lib stays generic; consumers override). Probably the cleaner long-term answer since Core shouldn't know about TargetPlanner's personal-defaults pattern.

### 3. The author's personal `personal-defaults.json` (one-time, on the author's machine)

```json
{
  "LocationName": "<your location name>",
  "Latitude": 0.0,
  "Longitude": 0.0,
  "NinaTargetsRoot": "<your NINA targets root>"
}
```

Save to `%LocalAppData%\TargetPlanner\personal-defaults.json`. Never enters the repo.

### 4. Extending `SettingsStore` (the user-changeable persistence)

To remember any in-app choice the user makes, just add fields to `AppSettings` and call `SettingsStore.Save(settings)` when they change. Candidates worth persisting:

- Last horizon spinner value
- Last duration spinner value
- Last sort-by selection
- Last graphed target list (for restoring on launch)
- Window size / position
- Panel split positions
- Last view-area selection (Day / Year / Optimal)

The Load/Save infrastructure already handles missing files, malformed JSON, and graceful fallback.

## Footgun: do NOT use `Properties\Settings.settings`

VS's built-in Settings system (auto-generated `Settings.Designer.cs` from `.settings` files) writes to a path that includes the assembly version, e.g. `%LocalAppData%\TargetPlanner\TargetPlanner.exe_Url_<hash>\1.0.0.0\user.config`. **Every Velopack version bump wipes the user's saved settings.** The existing `SettingsStore` writes to a version-independent path (`%AppData%\TargetPlanner\settings.json`) and avoids this entirely. Stay on it.

## Sequence at startup

1. `Main()` runs, calls `VelopackApp.Build().Run()`, then constructs `MainForm`.
2. First touch of `PersonalDefaults` triggers its static constructor → reads `personal-defaults.json` if present.
3. `MainForm` ctor calls `SettingsStore.Load()` → reads `settings.json` if present; if any field is missing, the default it falls back to (in `Load`'s else branch) is sourced from `PersonalDefaults.*`.
4. App uses the resolved values throughout.
5. User changes a value → `SettingsStore.Save(settings)` writes the updated `settings.json`.

## What to do when picking this back up

1. Decide on the Astronomy.Core `Location.Default` change (option A or B above).
2. Implement `PersonalDefaults.cs` per the snippet above.
3. Update the three TargetPlanner call sites (MainForm, MainForm.Designer, SettingsStore).
4. Create your local `personal-defaults.json` so your dev experience preserves the personal location.
5. Add `personal-defaults.json` to `.gitignore` (defensive — the file lives in `%LocalAppData%`, not the repo, but in case someone ever drops a copy in the repo for testing).
6. Build, F5, verify the location loads from the personal file. Delete the file, verify the public defaults take over.
