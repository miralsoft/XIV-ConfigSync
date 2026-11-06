# To-dos: XIV ConfigSync

## Feasibility analysis

- [x] Repository skeleton, licence, hooks, CI workflows
- [x] Read-only inventory and comparison tool with tests
- [x] Baseline inventory and backup on device A
- [x] Experiment V00 (login and logout with no change) on device A (2026-11-04); V01 and V02 are covered by the transfer test and the key-by-key comparison
- [x] Transfer test from device B to device A: shared macros, hotbar, key binding, HUD layout and
  a HUD info setting, each changed on B, copied as whole files and seen in the game on A; then
  rolled back from a verified backup (2026-11-04)
- [x] ~~Environment report and baseline inventory on device B~~ dropped (owner's decision, see `decisions.md`)
- [x] Key-by-key comparison and settings snapshot for text configuration files
- [x] Draft file matrix from existing files (`docs/file-matrix.md`)
- [x] File matrix confirmed by experiments for the files release 0.1 needs; the remaining rows are checked in gate 2
- [x] Cross test V03: done by the transfer test; the other direction is not tested (owner's decision, see `decisions.md`)
- [x] Cloud-state recorder and OneDrive single-device test in a separate folder
- [x] ~~OneDrive offline test and two-device conflict test~~ dropped (owner's decision, see `decisions.md`)
- [x] Feasibility report and framework recommendation (2026-11-04); waiting for the owner's decision on gate 2

## Gate 2: specification and architecture

- [x] Revised specification with releases and priorities (`specification.md`)
- [x] Architecture with the decisions taken (`architecture.md`)
- [x] Data model of the shared folder and the local data (`data-model.md`)
- [x] Test strategy, gate 3 prototype scope, mapping of T01 to T21 and A01 to A08 (`test-strategy.md`)
- [x] Owner's review and decisions on credentials (6.1) and plugin apply in 0.1 (6.2)
- [x] Gate 3 and 4 approval (2026-11-04)

## Gates 3 and 4: prototype and core functions

- [x] Solution per the C# and WPF profiles: pinned SDK, locked packages, strict build, format
  checks, tests, CI workflow
- [x] Core: store, scanner, rules, planner, applier with snapshot, journal and undo, engine
- [x] Platform: game process, OneDrive state, game version, autostart, file association
- [x] Application: set-up, overview, transfer, rules, settings, log, about, tray, one instance
- [x] Texts in English and German with a completeness test
- [x] Portable single executable
- [x] Manual test on one machine with copies of real data
- [x] User guide, README
- [x] First CI run of the .NET workflow (2026-11-05, passed)
- [x] Clean-up page for old conflict copies (FR-071, 2026-11-05); still to try on the real game folder
- [x] Owner's requests of 2026-11-05 (FR-073 to FR-076): files only here, launcher folder, character names, rules per plugin
- [x] Areas instead of files (FR-077 to FR-081, 2026-11-06): switches per area, chat split from the character settings, newest state wins for HUD and inventory, one question at the first transfer
- [x] Single groups of settings within an area (FR-082, 2026-11-06): chat, character settings, hotbar display, controls
- [ ] Choosing single fields of plugin settings (after the Dalamud test)
- [ ] Test on two real devices (gate 4 acceptance A01 to A05)
- [ ] Dalamud test: does Dalamud keep a plugin settings file written from outside (decides
  whether applying plugin settings stays in 0.1)
- [ ] Transfer check of MACRO, GEARSET, COMMON, CONTROL1 in the game (now taken automatically with their area; a failing file leaves its area), and of COMMON with the chat area off
- [ ] Upload state on a personal OneDrive
- [ ] Manual accessibility check before release (keyboard only, Narrator, 150 and 200 percent,
  high contrast: the colours are fixed today)
- [ ] Gate 5: release candidate, changelog, release notes, release procedure
