# Status: XIV ConfigSync

## Overall

Feasibility analysis (gate 1) done on 2026-11-04: feasible. The report is finished and waits for the owner's decision on gate 2. No product code.

## Analysis tools (`tools/`)

- **Done:** read-only inventory, comparison and environment report in PowerShell 5.1, with Pester
  tests and PSScriptAnalyzer; `build.ps1` runs all gates. Baseline inventory of device A taken; a
  verified backup of its game configuration folder exists locally.
- **Done (2026-11-04):** key-by-key comparison of the game's text settings files, and a snapshot
  of those files next to every inventory, so that a change can be traced to the setting behind it.
- **Done (2026-11-04):** experiment V00. The game rewrites the logged-in character's files on
  every login, most with identical content; HUD layout, a UI save file, the item order and the
  `cfgcopy` file change by themselves. See `file-matrix.md`.

- **Done (2026-11-04):** `Watch-CloudState.ps1` records the cloud sync state of a folder's files
  without opening them.
- **Done (2026-11-04):** the game publisher's terms on materials and names read (rule X-09), and a
  draft framework recommendation and scope for 0.1 written for the feasibility report.
- **Done (2026-11-04):** PSScriptAnalyzer runs through `Invoke-ProjectAnalyzer.ps1` in a separate
  process that is replaced when the analyzer breaks it; `build.ps1` and CI share the script.

## OneDrive (MA-09)

- **Measured on device A (2026-11-04):** the OneDrive client reports, readable without signing
  in, whether a file is still waiting for upload or in sync. New file waiting: shell property
  `System.StorageProviderState` 4, no reparse point yet. Changed file waiting: 10. In sync: 2,
  with reparse point. 30 MB were up after about eight seconds. The owner confirmed that the test
  files arrived on device B.
- **Open:** offline behaviour (needs the owner to disconnect), two devices writing the same file.

## File matrix

- **Transfer test (2026-11-04):** five settings changed on device B, copied to device A as whole
  files with the game closed, all five seen in the game on A, including a HUD layout from 16:9 on
  16:10. Rolled back afterwards from a hash-checked backup. Shared macros live in `MACROSYS.dat`
  in the top folder, not in the character folder.

- **Draft (2026-11-04):** `docs/file-matrix.md`, from conflict copies of an earlier whole-folder
  sync and a read-only look at the plugin folder. `FFXIV.cfg` and `dalamudConfig.json` turned out
  to be mixed files, and one plugin configuration holds an API key in plain text.
