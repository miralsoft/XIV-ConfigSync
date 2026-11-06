# Status: XIV ConfigSync

## Overall

- Gate 1 (feasibility): done 2026-11-04, feasible.
- Gate 2 (specification, architecture, data model, test strategy): approved 2026-11-04.
- Gates 3 and 4 (prototype of the critical paths, core functions): approved 2026-11-04 and worked
  on in one run that night. Release 0.1 is implemented and tested on one machine with generated
  data and with copies of real data. **Open:** the test on two real devices, the Dalamud test
  with a running game, and the owner's review. Not released.
- Owner's requests of 2026-11-05 (FR-073 to FR-076, built 2026-11-06): files only on this device in
  the transfer list, XIVLauncher folder as the setting, character names from plugin data, rules
  per plugin; tested with generated data and in the interface on scratch copies. Development stays
  on device A; device B is for tests.
- Areas instead of files (FR-077 to FR-081, 2026-11-06, owner's request): what is taken from other devices is switched per area (hotbars, chat, HUD layout and so on); `COMMON.DAT` is split by setting name between character settings and chat; HUD layout and inventory order take the state backed up last; the first transfer with a device asks once. Tested with generated data and in the interface on scratch copies; not yet tried in the game. Within an area, groups of settings in the game's text files can be switched on their own (FR-082), checked against copies of the real files. The transfer page shows which settings differ inside a selected file, for the game's text files and plugin JSON (FR-083). Single settings of the character and control settings can be set to always be taken or always stay here (FR-084), tested with generated data.
- Dalamud backup area and plugin repository merge (FR-072, 2026-11-05): tested with generated
  settings, on a copy of the owner's real file and in the interface on scratch copies (merge and undo);
  not yet tried in the game.
- Clean-up page for old conflict copies (FR-071, 2026-11-05): ticked files to the Recycle Bin after
  a confirmation; not yet tried on the real game folder, since the game ran during the test.
- First real set-up by the owner (2026-11-05): a second click on Finish opened a second overview
  and tray icon; fixed with a test (`decisions.md`).
- CI (2026-11-05): the .NET workflow passed its first run. The analyzer fault "Get-Command is not
  recognized" is now understood (a cached failed lookup inside PSScriptAnalyzer spoils its
  process) and reproduced locally; a fresh worker stays the cure, and the fault is now recognised
  in any language. It still fails the PowerShell job when one file needs more than three workers,
  as twice on 2026-11-05. Foundation 5.2.1's same-process retry does not clear this form
  (measured); 5.2.2's fix for `PublicKeyToken` in `.resx` applies here. The project still
  declares foundation 5.2.0.

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
- **Tests:** 218 xUnit tests (unit, two- and three-device flows, a simulated crash at every step
  of an apply, damaged and incomplete revisions, localisation completeness, one test per finding
  of the independent review), 41 Pester tests for the analysis tools and the analyzer wrapper. Deliberately planted
  defects were caught by the tests, twice before and three times after the review fixes.
- **Independent reviews (2026-11-05):** three rounds (code, the fixes, daily use and the interface)
  with 16, 12 and 12 findings; all fixed, the logic ones with tests; see `decisions.md`.
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
