# Status: XIV ConfigSync

## Overall

Feasibility analysis (gate 1) in progress since 2026-11-03. No product code.

## Analysis tools (`tools/`)

- **Done:** read-only inventory, comparison and environment report in PowerShell 5.1, with Pester
  tests and PSScriptAnalyzer; `build.ps1` runs all gates. Baseline inventory of device A taken; a
  verified backup of its game configuration folder exists locally.
- **Done (2026-11-04):** key-by-key comparison of the game's text settings files, and a snapshot
  of those files next to every inventory, so that a change can be traced to the setting behind it.
- **Next:** the in-game experiments V00 to V02 on device A and the baseline of device B. They are
  waiting for the owner.

## File matrix

- **Draft (2026-11-04):** `docs/file-matrix.md`, from conflict copies of an earlier whole-folder
  sync and a read-only look at the plugin folder. `FFXIV.cfg` and `dalamudConfig.json` turned out
  to be mixed files, and one plugin configuration holds an API key in plain text.
