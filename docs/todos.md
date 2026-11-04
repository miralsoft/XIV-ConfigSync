# To-dos: XIV ConfigSync

## Feasibility analysis

- [x] Repository skeleton, licence, hooks, CI workflows
- [x] Read-only inventory and comparison tool with tests
- [x] Baseline inventory and backup on device A
- [ ] Experiments V00 (idle), V01 (UI scale), V02a to V02f (single settings) on device A
- [x] Transfer test from device B to device A: shared macros, hotbar, key binding, HUD layout and
  a HUD info setting, each changed on B, copied as whole files and seen in the game on A; then
  rolled back from a verified backup (2026-11-04)
- [ ] Environment report and baseline inventory on device B
- [x] Key-by-key comparison and settings snapshot for text configuration files
- [x] Draft file matrix from existing files (`docs/file-matrix.md`)
- [ ] File matrix confirmed by experiments: shared, device-specific, cache, sensitive
- [ ] Cross test V03: one direction done by the transfer test; the other direction is open
- [x] Cloud-state recorder and OneDrive single-device test in a separate folder
- [ ] OneDrive offline test and two-device conflict test
- [ ] Feasibility report and framework decision (draft written; waits for the experiments)
