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
using System.Text;
public class VerifyUiNative {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // SendMessage overloads -- two for ListBox queries (count + per-item text).
    // CharSet.Auto + Unicode build of WinForms gives us the W variant under the hood.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, StringBuilder lParam);

    // DateTimePicker DTM_SETSYSTEMTIME: writes a new value into the control,
    // which fires DTN_DATETIMECHANGE -> WinForms ValueChanged. That's what
    // makes Set-TPDatePicker functionally equivalent to a user spinning the
    // picker -- the form's handler chain (e.g. DatePicker_ValueChanged ->
    // OnObservationMomentChanged) runs as a side effect.
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME {
        public ushort wYear, wMonth, wDayOfWeek, wDay;
        public ushort wHour, wMinute, wSecond, wMilliseconds;
    }
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref SYSTEMTIME lParam);

    public const int LB_GETCOUNT       = 0x018B;
    public const int LB_GETTEXTLEN     = 0x018A;
    public const int LB_GETTEXT        = 0x0189;
    public const int DTM_SETSYSTEMTIME = 0x1002;
    public const int GDT_VALID         = 0;

    // PostMessage WM_KEYDOWN/WM_KEYUP for key injection targeted at a
    // specific hwnd. Bypasses foreground-lock rules (which break SendInput
    // from non-foreground PowerShell). Used by Set-TPDatePicker to walk
    // the picker via the existing DatePicker_KeyDown handler.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP   = 0x0101;
    public const ushort VK_UP   = 0x26;
    public const ushort VK_DOWN = 0x28;

    public static void PostKey(IntPtr hwnd, ushort vk) {
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
    }

    public static bool SetDateTimePicker(IntPtr hwnd, DateTime dt) {
        SYSTEMTIME st = new SYSTEMTIME {
            wYear        = (ushort)dt.Year,
            wMonth       = (ushort)dt.Month,
            wDayOfWeek   = (ushort)dt.DayOfWeek,
            wDay         = (ushort)dt.Day,
            wHour        = (ushort)dt.Hour,
            wMinute      = (ushort)dt.Minute,
            wSecond      = (ushort)dt.Second,
            wMilliseconds = (ushort)dt.Millisecond,
        };
        IntPtr r = SendMessage(hwnd, DTM_SETSYSTEMTIME, (IntPtr)GDT_VALID, ref st);
        return r.ToInt32() != 0;
    }

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

    public static string[] ReadListBoxItems(IntPtr hwnd) {
        int count = SendMessage(hwnd, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (count <= 0) return new string[0];
        string[] items = new string[count];
        for (int i = 0; i < count; i++) {
            int len = SendMessage(hwnd, LB_GETTEXTLEN, (IntPtr)i, IntPtr.Zero).ToInt32();
            if (len < 0) { items[i] = string.Empty; continue; }
            StringBuilder sb = new StringBuilder(len + 1);
            SendMessage(hwnd, LB_GETTEXT, (IntPtr)i, sb);
            items[i] = sb.ToString();
        }
        return items;
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

# Set a ComboBox to the given index (0-based). WinForms ComboBox only
# exposes its items in the UIA tree when the dropdown is open, so we
# Expand first, then enumerate at Descendants scope (the popup listbox
# sits a level or two below the combo), select the item via
# SelectionItemPattern (selection auto-collapses the dropdown in
# WinForms' default DropDownStyle).
function Set-TPComboIndex {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$Index)
    $combo = Find-TPControl -Root $Root -Name $Name
    $expand = $combo.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    try {
        # Wait for the dropdown's items to register with UIA.
        $listItemType = [System.Windows.Automation.ControlType]::ListItem
        $itemCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $listItemType)
        $items = $null
        $deadline = (Get-Date).AddMilliseconds(2000)
        while ((Get-Date) -lt $deadline) {
            $items = $combo.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                $itemCond)
            if ($items.Count -gt 0) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($Index -lt 0 -or $Index -ge $items.Count) {
            throw "Index $Index out of range; combo '$Name' has $($items.Count) items."
        }
        $selPat = $items[$Index].GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selPat.Select()
    } finally {
        # Defensive collapse in case the Select didn't auto-collapse.
        if ($expand.Current.ExpandCollapseState -eq
            [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            $expand.Collapse()
        }
    }
}

# Toggle a CheckBox to the desired Checked state via TogglePattern. No-op
# if it's already in that state (avoids spurious CheckedChanged events).
function Set-TPCheckboxState {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [bool]$Checked)
    $el = Find-TPControl -Root $Root -Name $Name
    $tog = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $current = $tog.Current.ToggleState -eq
        [System.Windows.Automation.ToggleState]::On
    if ($current -ne $Checked) { $tog.Toggle() }
}

# Read a CheckBox's checked state. Returns $true / $false.
function Get-TPCheckboxState {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $el = Find-TPControl -Root $Root -Name $Name
    $tog = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    return ($tog.Current.ToggleState -eq
        [System.Windows.Automation.ToggleState]::On)
}

# Select a RadioButton via SelectionItemPattern. Sibling radios are
# automatically deselected by the SelectionItemPattern.Select semantics
# (sibling radios share an implicit SelectionPattern container in WinForms).
function Set-TPRadioButton {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $el = Find-TPControl -Root $Root -Name $Name
    $sel = $el.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
}

# Set a NumericUpDown's value via RangeValuePattern. The WinForms
# AccessibilityObject for NumericUpDown clamps to Min/Max and fires
# ValueChanged on the underlying control as a side effect.
function Set-TPSpinnerValue {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [double]$Value)
    $el = Find-TPControl -Root $Root -Name $Name
    $rv = $el.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
    $rv.SetValue($Value)
}

# Read a NumericUpDown's current value as double.
function Get-TPSpinnerValue {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $el = Find-TPControl -Root $Root -Name $Name
    $rv = $el.GetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern)
    return [double]$rv.Current.Value
}

# Set a TextBox's text via ValuePattern. Fires TextChanged. WinForms
# multiline textboxes work too; values with newlines round-trip cleanly.
function Set-TPTextValue {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [string]$Value)
    $el = Find-TPControl -Root $Root -Name $Name
    $vp = $el.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $vp.SetValue($Value)
}

# Read text content from a TextBox or Label. ValuePattern (TextBox) is
# tried first; falls back to NameProperty (Label, immutable text labels
# don't expose ValuePattern). Returns the displayed text as a string.
function Get-TPText {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $el = Find-TPControl -Root $Root -Name $Name
    $vp = $null
    try {
        $vp = $el.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern)
    } catch { $vp = $null }
    if ($null -ne $vp) { return [string]$vp.Current.Value }
    return [string]$el.Current.Name
}

# Walk a DateTimePicker to a target date by PostMessage'ing WM_KEYDOWN
# VK_UP/VK_DOWN to its hwnd. Each key press fires the picker's KeyDown
# handler -- TP's DatePicker_KeyDown subtracts/adds 1 day per arrow
# press -- which runs the full handler chain (Value setter ->
# ValueChanged -> OnObservationMomentChanged) just like a user keyboard
# press would.
#
# Why not simpler approaches:
#   - ValuePattern.SetValue silently no-ops on WinForms DateTimePicker
#     (long-standing accessibility-provider bug; SetValue returns S_OK
#     but the control doesn't update).
#   - Win32 DTM_SETSYSTEMTIME returns 0 against the picker's hwnd in
#     this WinForms version (possibly a wrapped vs inner-hwnd issue).
#   - SendInput-style keyboard injection respects foreground-lock rules
#     and lands on whichever window has focus -- non-deterministic.
# PostMessage delivers directly to the picker's window queue regardless
# of foreground state, so the test isn't focus-dependent.
function Set-TPDatePicker {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [DateTime]$Date)
    $picker = Find-TPControl -Root $Root -Name $Name
    $hwnd = [IntPtr]$picker.Current.NativeWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) {
        throw "DateTimePicker '$Name' has no NativeWindowHandle."
    }
    # Parse current displayed value to compute day delta.
    $current = ($picker.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    $currentDt = [DateTime]::Parse($current)
    $delta = ($Date.Date - $currentDt.Date).Days
    if ($delta -eq 0) { return }
    $vk = if ($delta -lt 0) { [VerifyUiNative]::VK_DOWN } else { [VerifyUiNative]::VK_UP }
    $steps = [Math]::Abs($delta)
    for ($i = 0; $i -lt $steps; $i++) {
        [VerifyUiNative]::PostKey($hwnd, $vk)
        # Small inter-key gap so each press's handler (which calls
        # mCoordinator.Apply -> debounced render) doesn't stack up
        # ahead of the next press.
        Start-Sleep -Milliseconds 30
    }
}

# Read a (Checked)ListBox's items in display order. Returns string[] of
# item Names (each item's ToString() in WinForms -- TargetRow rows
# resolve to the Target's .Name).
#
# Uses raw Win32 SendMessage(LB_GETCOUNT) + LB_GETTEXT against the
# listbox's HWND, not UIA. WinForms ListBox / CheckedListBox / the
# DupeAwareCheckedListBox subclass don't reliably expose ListItem
# children in their UIA tree (the WinForms accessibility provider is
# bugged around list virtualization). Win32 messages target the control
# directly and return whatever the listbox's item-store contains --
# faithful to what's on screen.
function Get-TPListboxItems {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name)
    $lb = Find-TPControl -Root $Root -Name $Name
    $hwnd = [IntPtr]$lb.Current.NativeWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) {
        throw "ListBox '$Name' has no NativeWindowHandle; cannot read items."
    }
    return ,[VerifyUiNative]::ReadListBoxItems($hwnd)
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
