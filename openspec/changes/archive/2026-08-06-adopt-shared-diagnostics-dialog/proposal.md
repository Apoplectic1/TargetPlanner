# Proposal: adopt-shared-diagnostics-dialog

## Why

TP's `Forms\DiagnosticsDialog` graduated verbatim into AL's new `Astronomy.Diagnostics.WinForms`
satellite (2026-08-06, `diagnostics-winforms-satellite`) so both WinForms consumers (TP, XFM) share
one implementation. TP's local copy is now the duplicate; retire it.

## What Changes

- Delete `TargetPlanner\Forms\DiagnosticsDialog.cs`; add `ProjectReference` +
  `TargetPlanner.sln` entry for `Astronomy.Diagnostics.WinForms`; retarget the two call sites
  (Ctrl+N `ProcessCmdKey`, App-menu item). Zero behavior change (the shared copy differs only in
  construction order and namespace).

## Capabilities

### New Capabilities

_None — pure dependency swap; `skip_specs: true`._

### Modified Capabilities

_None._

## Impact

Code: one file deleted, two call sites + csproj + sln. Payload gains `Astronomy.Diagnostics.WinForms.dll`.
