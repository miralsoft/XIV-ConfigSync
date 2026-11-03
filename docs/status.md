# Status: XIV ConfigSync

## Overall

Feasibility analysis (gate 1) in progress since 2026-11-03. No product code.

## Analysis tools (`tools/`)

- **Done:** read-only inventory, comparison and environment report in PowerShell 5.1, with Pester
  tests and PSScriptAnalyzer; `build.ps1` runs all gates. Baseline inventory of device A taken; a
  verified backup of its game configuration folder exists locally.
- **Next:** the in-game experiments V00 to V02 on device A, the baseline of device B, then the file
  matrix.
