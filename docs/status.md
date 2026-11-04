# Status: XIV ConfigSync

## Overall

- Gate 1 (feasibility): done 2026-11-04, feasible.
- Gate 2 (specification, architecture, data model, test strategy): approved 2026-11-04.
- Gates 3 and 4 (prototype of the critical paths, core functions): approved 2026-11-04 and worked
  on in one run that night. Release 0.1 is implemented and tested on one machine with generated
  data and with copies of real data. **Open:** the test on two real devices, the Dalamud test
  with a running game, the owner's review, and the first CI run of the .NET workflow (needs a
  push). Not released.

## Product (`src/`, `tests/`)

- **Core** (`XivConfigSync.Core`, `net10.0`): sync folder with `.xivsync` file, per-device store
  with content-addressed objects, revisions and `HEAD` written last; scanner with exclusions and
  credential flags; rules from the file matrix with user overrides; apply planner (three-way, with
  history); applier with verified snapshot, journal, recovery and undo; engine with a device
  lock; view models; texts in English and German.
- **Platform** (`XivConfigSync.Platform`): game process watch, OneDrive upload state, game
  installation and version, autostart and file association for the current user.
- **App** (`XivConfigSync.App`, WPF): set-up wizard, overview, transfer, rules, settings, log,
  about; tray icon; one instance per user with hand-over of `.xivsync` files; portable single
  executable of 67 MB that starts in about 1.5 s.
- **Tests:** 77 xUnit tests (unit, two- and three-device flows, a simulated crash at every step
  of an apply, damaged and incomplete revisions, localisation completeness), 40 Pester tests for
  the analysis tools. Two deliberately planted defects were caught by the tests.
- **Manual test on device A (2026-11-04, on copies of the real game and plugin folders):** set-up
  of a new sync folder; first backup of 160 files in 1.5 s, with 345 navigation cache files of a
  plugin and 45 old conflict copies left out; second device joined by opening the `.xivsync`
  file; transfer of two conflicting files through the interface, checked by hash, `FFXIV.cfg`
  untouched; undo; game start and exit simulated with a stand-in process (writes refused while
  running, backup 15 s after exit); second instance handing over to the first. No real game or
  plugin file was written, and no registry entry was left behind.

## Analysis tools (`tools/`)

Read-only inventory, comparison, environment report, cloud-state recorder and key-by-key
comparison of text settings, with Pester tests; PSScriptAnalyzer runs through
`Invoke-ProjectAnalyzer.ps1`.

## Findings from the feasibility analysis

- Transfer test (2026-11-04): whole files carry settings between devices; a HUD from 16:9 works
  on 16:10. Shared macros live in `MACROSYS.dat` in the top folder.
- Experiment V00: the game rewrites the logged-in character's files on every login; the HUD
  layout, `UISAVE.DAT`, the item order and the `cfgcopy` file change by themselves.
- OneDrive (MA-09): the upload state is readable per file without signing in (4 pending new,
  10 pending changed, 2 in sync), measured on OneDrive for Business, from PowerShell and from C#.
  A personal OneDrive is not measured yet.
- `FFXIV.cfg` and `dalamudConfig.json` are mixed files; one plugin configuration holds an API key
  in plain text.
