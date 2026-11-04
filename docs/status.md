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

- **Done (2026-11-04):** `Watch-CloudState.ps1` records the cloud sync state of a folder's files
  without opening them.

## OneDrive (MA-09)

- **Measured on device A (2026-11-04):** the OneDrive client reports, readable without signing
  in, whether a file is still waiting for upload or in sync. New file waiting: shell property
  `System.StorageProviderState` 4, no reparse point yet. Changed file waiting: 10. In sync: 2,
  with reparse point. 30 MB were up after about eight seconds.
- **Open:** offline behaviour (needs the owner to disconnect), two devices (needs device B).

## File matrix

- **Draft (2026-11-04):** `docs/file-matrix.md`, from conflict copies of an earlier whole-folder
  sync and a read-only look at the plugin folder. `FFXIV.cfg` and `dalamudConfig.json` turned out
  to be mixed files, and one plugin configuration holds an API key in plain text.
