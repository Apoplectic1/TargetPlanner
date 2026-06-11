---
name: verify-ui
description: Drive TP's UI handlers programmatically — launch the app, simulate clicks/keystrokes, capture before/after snapshots via Ctrl+N's existing observation-dialog infrastructure, read the resulting screenshots + ctx + DIAG log lines. Use when a refactor or new feature touches a click/scrub/menu handler and the change isn't covered by unit tests (anything in `Forms/Presenters/*` or `Forms/MainForm.cs` event handlers). Skip for pure-data refactors, pure-cache changes, or non-UI library work — `dotnet test` already covers those.
---

# verify-ui — one-command UI handler verification for TargetPlanner

## When to invoke

- A change touches a `Button_*_Click`, `*_ValueChanged`, `*_CheckedChanged`,
  `*_KeyDown`, or menu handler in `Forms/` or `Forms/Presenters/*`.
- A presenter extraction / partial-class move (verifies the wiring still works).
- A `ChartCoordinator` or `mSelection` pathway change where you want to
  confirm UI handlers still funnel through correctly.

Skip when the change is:
- Purely in `State/`, `Caches/`, `Targets/`, `Filters/`, or a Library project —
  unit tests cover those.
- Behaviour-equivalent comment / rename work.

## The leverage point: TP's diagnostics dialog (menu-driven)

TP ships a screenshot+context-capture primitive via the
`DiagnosticsDialog`. Users invoke it via Ctrl+N (wired through
`MainForm.ProcessCmdKey`) **or** Help → Feedback → Capture Diagnostics
Snapshot. This skill drives the menu path; the keystroke path was tried
first and abandoned (foreground-lock rules + scheduling races made
`SendInput Ctrl+N` non-deterministic from non-foreground PowerShell —
sometimes the dialog opened seconds late, sometimes not at all). The
menu path takes a deterministic walk through the UIA tree with no
race conditions.

The dialog uses `Graphics.CopyFromScreen` — the only way to capture
LC2's SKControl chart pixels (`Control.DrawToBitmap` returns blank for
Skia surfaces). One snapshot writes one `USER_OBS_END id=<4-hex>` line
to tp.log with notes + `GetObservationContext` snapshot + PNG path.

Verification recipe: snapshot baseline → perform action → snapshot
result → close TP → read log + PNGs → compare.

## Recipe

1. **Make sure TP is built**:
   ```bash
   dotnet build "TargetPlanner.sln" -c Debug -v:m | tail -5
   ```
   Helper expects `TargetPlanner/bin/x64/Debug/net10.0-windows10.0.19041/TargetPlanner.exe`.

2. **Drive the verification** via a PowerShell block that dot-sources the
   helper and uses its functions:
   ```powershell
   . ".\.claude\skills\verify-ui\verify-ui.ps1"

   $p = Start-TPApp
   try {
       Start-Sleep -Seconds 6                              # let image library load
       $root = Get-TPMainWindow -Process $p

       $idBefore = Send-TPSnapshot -Process $p -Notes "baseline"

       # ... perform the action under test using the helpers below ...
       Invoke-TPControl -Root $root -Name "Button_Now"
       Start-Sleep -Milliseconds 800

       $idAfter = Send-TPSnapshot -Process $p -Notes "after Button_Now"
   } finally {
       Stop-TPApp -Process $p
   }

   Get-TPObservations | ConvertTo-Json -Depth 3
   ```

3. **Read the screenshot PNGs** for each observation. The
   `Get-TPObservations` output has `Screenshot` paths; use the `Read`
   tool on each PNG to view what was captured. The same PNGs survive one
   rotation cycle (post-launch they're in `%APPDATA%\TargetPlanner\screenshots\`;
   after the next TP launch they move to `screenshots.prev\`).

4. **Read tp.log** at `%APPDATA%\TargetPlanner\Logs\tp.log` for any DIAG
   lines between `USER_OBS_START id=<x>` and `USER_OBS_END id=<x>` — they
   show which code paths fired between the two snapshots.

5. **Report**: cite the observation ids, what the ctx snapshots say,
   what the screenshots show, and what the DIAG lines confirm fired.

## Helper API (verify-ui.ps1)

| Function | Purpose |
|---|---|
| `Start-TPApp [-TimeoutSec 15]` | Launch the built TP exe; return Process. Throws if exe missing or no MainWindowHandle within timeout. |
| `Stop-TPApp -Process $p [-TimeoutSec 5]` | CloseMainWindow + kill fallback. Always call in a `finally`. |
| `Get-TPMainWindow -Process $p` | AutomationElement for TP's MainForm (matched by pid). |
| `Find-TPControl -Root $r -Name <name> [-TimeoutMs 3000]` | UIA element by AutomationId (= WinForms Control.Name). |
| `Invoke-TPControl -Root $r -Name <name>` | Click a button or invokable control. |
| `Set-TPComboIndex -Root $r -Name <name> -Index <n>` | Pick a ComboBox item by 0-based index. |
| `Set-TPCheckboxState -Root $r -Name <name> -Checked $true/$false` | Toggle a CheckBox via TogglePattern (no-op if already in target state). |
| `Get-TPCheckboxState -Root $r -Name <name>` | Read CheckBox state ($true/$false). |
| `Set-TPRadioButton -Root $r -Name <name>` | Select a RadioButton via SelectionItemPattern (sibling radios auto-deselect). |
| `Set-TPSpinnerValue -Root $r -Name <name> -Value <double>` | Set a NumericUpDown via RangeValuePattern (clamps to Min/Max). |
| `Get-TPSpinnerValue -Root $r -Name <name>` | Read a NumericUpDown's current value. |
| `Set-TPTextValue -Root $r -Name <name> -Value <string>` | Set a TextBox via ValuePattern. |
| `Get-TPText -Root $r -Name <name>` | Read TextBox text (ValuePattern) or Label text (NameProperty fallback). |
| `Set-TPDatePicker -Root $r -Name <name> -Date <DateTime>` | Set a DateTimePicker via Win32 DTM_SETSYSTEMTIME (fires ValueChanged). |
| `Get-TPListboxItems -Root $r -Name <name>` | Read (Checked)ListBox items in display order via Win32 LB_GETTEXT. |
| `Find-TPElementByName -Root $r -ControlType <t> -Name <name>` | Find any UIA element by ControlType + Name (used for menu items, dialog OK button). |
| `Invoke-TPMenuItem -Root $r -Path "Help","Feedback","..."` | Walk a menu path: expand each non-leaf, invoke the leaf. |
| `Send-TPSnapshot -Process $p [-Notes <s>]` | Help → Feedback → Capture Diagnostics Snapshot → fill notes → click OK. Returns the 4-hex id. |
| `Get-TPObservations` | Parse current-session `USER_OBS_END` lines from tp.log. |
| `Read-TPScreenshot -Id <hex>` | Screenshot path for a given observation id. |

## Worked example: verify Button_Now re-ranks a Transit-sorted listbox

This is the fix from commit `c1b888c` (latent-bug surfaced during the
MainForm decomposition session). The picker handlers always re-sorted
when a time-dependent sort was active; `Button_Now_Click` didn't, leaving
the listbox stale on snap-to-now.

```powershell
. ".\.claude\skills\verify-ui\verify-ui.ps1"

$p = Start-TPApp
try {
    Start-Sleep -Seconds 6
    $root = Get-TPMainWindow -Process $p

    # Switch to Transit sort (combo index 1; 0 is Name).
    Set-TPComboIndex -Root $root -Name "ComboBox_SortTargets" -Index 1
    Start-Sleep -Milliseconds 400

    # Tag a baseline snapshot + capture listbox order.
    $idBefore = Send-TPSnapshot -Process $p -Notes "Transit sort, before Button_Now"
    $before   = Get-TPListboxItems -Root $root -Name "CheckedListBox_SelectedTargets"

    # Snap to now.
    Invoke-TPControl -Root $root -Name "Button_Now"
    Start-Sleep -Milliseconds 800

    # Post-click snapshot + listbox order.
    $idAfter = Send-TPSnapshot -Process $p -Notes "Transit sort, after Button_Now"
    $after   = Get-TPListboxItems -Root $root -Name "CheckedListBox_SelectedTargets"
} finally {
    Stop-TPApp -Process $p
}

# Verification: order must have changed (Button_Now ran ResortSelectedTargets
# against the new "now"). If they're identical, the latent bug regressed.
$changed = ($null -ne (Compare-Object $before $after -SyncWindow 0))
[pscustomobject]@{
    OrderChanged = $changed
    IdBefore     = $idBefore
    IdAfter      = $idAfter
    FirstBefore  = $before[0..2]
    FirstAfter   = $after[0..2]
} | ConvertTo-Json
```

Then `Read` the two screenshots (`Read-TPScreenshot -Id $idBefore` and
`...-Id $idAfter`) to verify the listbox display *visually* matches the
order changes.

## Caveats

- **Control.Name must be set.** WinForms exposes `Control.Name` as the
  UIA `AutomationId`. Most TP controls have a Designer-set Name
  (`Button_Now`, `ComboBox_SortTargets`, `CheckedListBox_SelectedTargets`),
  but custom-added controls may not — check `MainForm.Designer.cs` if
  a `Find-TPControl` lookup fails. Menu items don't have AutomationId;
  use `Invoke-TPMenuItem` / `Find-TPElementByName` (which match on
  Text/Name property) instead.
- **Modeless dialogs appear at Descendants scope, not Children.**
  `DiagnosticsDialog` (and any other `Show(owner=mainForm)` dialog)
  is a top-level owned window — UIA tree-parent is MainForm, NOT the
  desktop. Polling with `TreeScope::Children` misses it; use
  `TreeScope::Descendants`. `Send-TPSnapshot` already handles this.
- **No keystroke injection.** All UI driving goes through UIA patterns
  (Invoke / Expand / Value / etc.). The keystroke path (SendInput,
  SendKeys) was tried for Ctrl+N and abandoned -- foreground-lock rules
  make non-deterministic from scripted contexts. Menu-driven invocation
  is reliable; prefer adding a menu item over a shortcut when a new
  test-only entry point is needed.
- **Chart-pixel verification is via screenshot only.** UIA sees the LC2
  SKControl as one opaque element — listbox / button / label state is
  structured + assertable, but "does the chart curve look right" needs
  human eyes on the PNG.
- **Screenshot rotation.** `Log.StartNewSession()` moves `screenshots/`
  to `screenshots.prev/` on next launch. Read PNGs immediately after
  `Stop-TPApp` and before any subsequent `Start-TPApp`.
- **One snapshot ≠ thorough verification.** Use the recipe to drive the
  specific UI handler path under test, not as a generic smoke test. For
  generic "did it launch" use `/run` instead.
