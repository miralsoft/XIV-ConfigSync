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
