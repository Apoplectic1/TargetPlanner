# verify-ui.ps1 — helper functions for the verify-ui skill.
#
# Dot-source this file before using the helpers:
#   . "$PSScriptRoot\verify-ui.ps1"   # from a sibling script
#   . ".\.claude\skills\verify-ui\verify-ui.ps1"   # from repo root
#
# What's here:
#   Start-TPApp                  Launch the built TP exe; return the Process.
#   Stop-TPApp                   Close TP's main window cleanly (with kill fallback).
#   Get-TPMainWindow             AutomationElement for TP's MainForm.
#   Find-TPControl               AutomationElement for a named control inside TP.
#   Invoke-TPControl             Invoke (= click) a named control.
#   Set-TPComboIndex             Set ComboBox SelectedIndex by AutomationId.
#   Get-TPListboxItems           Read CheckedListBox items in display order.
#   Send-TPSnapshot              Drive Ctrl+N + fill notes + click OK.
#   Get-TPObservations           Parse tp.log; return USER_OBS_END entries.
#   Read-TPScreenshot            Path of a USER_OBS_END entry's PNG.
#
# Conventions:
#   - "Named control" = a WinForms control whose .Name in the Designer was set.
#     WinForms exposes Control.Name as the UIA AutomationId, so we find by that.
#   - All UIA waits are bounded; if a control doesn't appear in time the
#     helper throws (callers Try/Catch if a missing control is acceptable).
#   - tp.log lives at $env:APPDATA\TargetPlanner\Logs\tp.log (per CLAUDE.md).

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Win32 helpers for foreground activation + raw keystroke injection. SendKeys
# was unreliable for Ctrl+ chords against TP -- SendInput posts to the system
# input queue directly, bypassing message-routing quirks.
if (-not ('VerifyUiNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public class VerifyUiNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public long _pad1;
        [FieldOffset(8)] public long _pad2;
        [FieldOffset(16)] public long _pad3;
    }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public int type; public INPUTUNION U; }
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_CONTROL = 0x11;

    public static void SendCtrlChord(ushort vk) {
        INPUT[] inputs = new INPUT[4];
        for (int i = 0; i < 4; i++) inputs[i].type = INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = VK_CONTROL;
        inputs[1].U.ki.wVk = vk;
        inputs[2].U.ki.wVk = vk; inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
        inputs[3].U.ki.wVk = VK_CONTROL; inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@
}

$script:TPExePath = Join-Path $PSScriptRoot `
    '..\..\..\TargetPlanner\bin\x64\Debug\net10.0-windows10.0.19041\TargetPlanner.exe'
$script:TPExePath = [System.IO.Path]::GetFullPath($script:TPExePath)
$script:TPLogPath = Join-Path $env:APPDATA 'TargetPlanner\Logs\tp.log'

# Launch TP. Returns the System.Diagnostics.Process. Throws if the exe is
# missing or fails to produce a MainWindowHandle within $TimeoutSec.
function Start-TPApp {
    param([int]$TimeoutSec = 15)
    if (-not (Test-Path $script:TPExePath)) {
        throw "TP exe not found at $script:TPExePath. Build first."
    }
    $p = Start-Process -FilePath $script:TPExePath -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $p.Refresh()
        if ($p.HasExited) { throw "TP exited before showing a window (code=$($p.ExitCode))." }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and $p.MainWindowTitle) { return $p }
        Start-Sleep -Milliseconds 200
    }
    throw "TP did not present a MainWindowHandle within ${TimeoutSec}s."
}

# Close TP's main window cleanly. If it doesn't go down in $TimeoutSec, kill.
function Stop-TPApp {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutSec = 5)
    if ($null -eq $Process -or $Process.HasExited) { return }
    [void]$Process.CloseMainWindow()
    if (-not $Process.WaitForExit($TimeoutSec * 1000)) {
        $Process.Kill()
        $Process.WaitForExit(2000)
    }
}

# AutomationElement for TP's MainForm (matched by process id, not title).
function Get-TPMainWindow {
    param([System.Diagnostics.Process]$Process)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $Process.Id)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -eq $el) { throw "Could not find TP main window for pid=$($Process.Id)." }
    return $el
}

# AutomationElement for a control named $Name anywhere in TP's tree.
# Scope = Descendants so we don't care about container depth.
function Find-TPControl {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$TimeoutMs = 3000)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $Name)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $el = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($null -ne $el) { return $el }
        Start-Sleep -Milliseconds 100
    }
    throw "Control '$Name' not found within ${TimeoutMs}ms."
}

# Invoke (click) a button or other invokable control by AutomationId/Name.
function Invoke-TPControl {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $el = Find-TPControl -Root $Root -Name $Name
    $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pat.Invoke()
}

# Set a ComboBox to the given index (0-based). WinForms ComboBox exposes the
# SelectionItem pattern on its child ListItem elements.
function Set-TPComboIndex {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$Index)
    $combo = Find-TPControl -Root $Root -Name $Name
    $items = $combo.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    if ($Index -lt 0 -or $Index -ge $items.Count) {
        throw "Index $Index out of range; combo '$Name' has $($items.Count) items."
    }
    $item = $items[$Index]
    $pat = $item.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pat.Select()
}

# Read a CheckedListBox's items in display order. Returns string[] of item
# Names (which for TargetRow rows is the target's .Name via ToString()).
function Get-TPListboxItems {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $lb = Find-TPControl -Root $Root -Name $Name
    $items = $lb.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)))
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($it in $items) { [void]$out.Add($it.Current.Name) }
    return ,$out.ToArray()
}

# Find any UIA element of the given ControlType + Name (text) under $Root.
# Polls because newly-expanded popup menus take a few ms to appear in UIA's
# tree. Used for menu navigation where AutomationId is usually empty.
function Find-TPElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [System.Windows.Automation.ControlType]$ControlType,
        [string]$Name,
        [int]$TimeoutMs = 3000)
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)))
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $el = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($null -ne $el) { return $el }
        Start-Sleep -Milliseconds 100
    }
    throw "$ControlType '$Name' not found within ${TimeoutMs}ms."
}

# Walk a menu path (e.g. "Help","Feedback","Capture Observation Snapshot"):
# expand each non-leaf via ExpandCollapsePattern, invoke the leaf. Drives
# WinForms ToolStripMenuItems reliably (no keystroke games, no foreground
# focus dependency). WinForms exposes ToolStripMenuItem.Text as the UIA
# Name property; the path entries here are those Text strings (without
# their ampersand accelerator prefix).
function Invoke-TPMenuItem {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string[]]$Path)
    if ($null -eq $Path -or $Path.Count -lt 1) { throw "Path must have at least 1 entry." }

    $menuItemType = [System.Windows.Automation.ControlType]::MenuItem
    for ($i = 0; $i -lt $Path.Count - 1; $i++) {
        $el = Find-TPElementByName -Root $Root -ControlType $menuItemType -Name $Path[$i]
        $exp = $el.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $exp.Expand()
        Start-Sleep -Milliseconds 250    # popup population
    }
    $leaf = Find-TPElementByName -Root $Root -ControlType $menuItemType -Name $Path[-1]
    $invoke = $leaf.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
}

# Drive Help > Feedback > Capture Observation Snapshot → fill notes → click OK
# on UserObservationDialog. Writes one USER_OBS_START + one USER_OBS_END pair
# to tp.log; returns the 4-hex id.
#
# Originally tried Ctrl+N via SendInput. Two reliability problems killed it:
# (a) keystroke injection from a non-foreground PowerShell context is governed
# by Windows' foreground-lock rules and lands on whichever window happens to
# hold focus -- non-deterministic;  (b) even when the keystroke reaches TP,
# the menu-invocation path takes a deterministic UIA path through the
# AutomationElement tree, with no scheduling-vs-message-pump races.
function Send-TPSnapshot {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Notes = '')

    $mainWindow = Get-TPMainWindow -Process $Process
    Invoke-TPMenuItem -Root $mainWindow -Path 'Help', 'Feedback', 'Capture Observation Snapshot'

    # Poll for the dialog under TP's pid. Critically: TreeScope::Descendants,
    # NOT ::Children. UserObservationDialog uses Show(owner=MainForm), which
    # makes it a top-level owned window -- UIA represents that as a child of
    # MainForm in the tree, not a direct child of the desktop. Filtering by
    # Children excludes the dialog even though it's visible on screen.
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $Process.Id)
    $dlgNameCond = New-Object System.Windows.Automation.AndCondition(
        $cond,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)))
    $dlg = $null
    $deadline = (Get-Date).AddMilliseconds(5000)
    while ((Get-Date) -lt $deadline -and $null -eq $dlg) {
        Start-Sleep -Milliseconds 150
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dlgNameCond)
        foreach ($w in $windows) {
            if ($w.Current.Name -like 'Observation (id=*') { $dlg = $w; break }
        }
    }
    if ($null -eq $dlg) { throw "UserObservationDialog did not appear after menu invoke." }

    $title = $dlg.Current.Name
    $id = if ($title -match 'id=([0-9a-fA-F]{4})') { $matches[1] } else { '????' }

    # Notes textbox: only Edit-type control on the dialog.
    if ($Notes) {
        $editCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit)
        $edit = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
        $valuePat = $edit.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
        $valuePat.SetValue($Notes)
    }

    # Click OK. Located by control Name "OK" (the button's Text).
    $okCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'OK')))
    $ok = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $okCond)
    $okPat = $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $okPat.Invoke()

    # Wait for the dialog to close + the screenshot to flush.
    Start-Sleep -Milliseconds 600
    return $id
}

# Parse tp.log; return USER_OBS_END entries as objects { Id, Ctx, Notes, Screenshot }.
# Only entries from the CURRENT session (since the most recent
# "=== Session start" marker) are returned, so previous-run noise is filtered.
function Get-TPObservations {
    if (-not (Test-Path $script:TPLogPath)) { return @() }
    $all = Get-Content $script:TPLogPath -Encoding UTF8
    $startIdx = -1
    for ($i = $all.Length - 1; $i -ge 0; $i--) {
        if ($all[$i] -match 'Session start' -or $all[$i] -match 'StartNewSession') {
            $startIdx = $i; break
        }
    }
    if ($startIdx -lt 0) { $startIdx = 0 }
    $session = $all[$startIdx..($all.Length - 1)]

    $out = New-Object System.Collections.Generic.List[object]
    foreach ($line in $session) {
        if ($line -match 'USER_OBS_END\s+id=(?<id>[0-9a-fA-F]+)\s+ctx=(?<ctx>.*?)\s+screenshot=(?<shot>\S*)\s+notes="(?<notes>.*?)"\s*$') {
            $out.Add([pscustomobject]@{
                Id         = $matches['id']
                Ctx        = $matches['ctx']
                Notes      = $matches['notes']
                Screenshot = $matches['shot']
            })
        }
    }
    return ,$out.ToArray()
}

# Convenience: return the screenshot path for an observation id, or $null.
function Read-TPScreenshot {
    param([string]$Id)
    $obs = Get-TPObservations | Where-Object { $_.Id -eq $Id }
    if ($null -eq $obs) { return $null }
    return $obs.Screenshot
}
