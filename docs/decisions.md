# Decisions: XIV ConfigSync

Append-only. Never edit or delete past entries; a later entry may supersede an earlier one.

## Log

- (2026-11-03) Licence GPL-3.0-or-later. Rationale: a modified copy has to stay open, so nobody can
  rename the tool and sell it closed. AGPL was rejected because there is no network service for its
  extra clause to cover. MIT was rejected because it allows exactly that closed repackaging.
- (2026-11-03) The agent entrypoint file is kept out of this repository and exists only locally.
  Rationale: it points to rules in a private repository that outside contributors cannot read, so
  in a public repository it would send them to a dead end. This departs from the default layout,
  which places that file at the root.
- (2026-11-03) The enforcement configuration and the CI workflows are committed. Rationale: CI needs
  them, they contain no internal text, and CI checks only the owner's own commits, so outside
  contributors are not bound by the owner's commit rules.
- (2026-11-03) Commits are made as Sanaka; the initial commit is rewritten to that identity before
  further work is pushed. Rationale: private project.
- (2026-11-03) Sync comes before backup. Release 0.1 is the sync with a snapshot before every apply;
  a separate backup export follows in 0.2. Rationale: sync is the owner's main need, and immutable
  revisions plus the pre-apply snapshot already cover rollback.
- (2026-11-03) The in-game server backup of settings is not an alternative. Rationale: it is a
  one-time transfer for a change of PC, does not support two PCs in parallel and does not cover
  plugins.
- (2026-11-03) Standalone Windows application, not a Dalamud plugin. Dalamud matters only as the
  owner of plugin configuration files that are synced.
- (2026-11-03) The standard (non-Steam) game edition is the first target; Steam follows later.
- (2026-11-03) Development tools Pester 6.2.0 and PSScriptAnalyzer 1.25.0, pinned with SHA-512 and
  restored into the ignored `.modules` folder. Approved by the owner.
- (2026-11-03) PSScriptAnalyzer runs a warm-up pass whose results are discarded, in `build.ps1` and
  in the copied CI workflow. Rationale: on the first analyses in a fresh process the rule
  UseCorrectCasing in 1.25.0 intermittently throws a NullReferenceException from
  `CommandInfo.Parameters` (in 6 of 9 fresh processes, measured). After one pass it did not recur
  in 162 analyses over 6 processes. The strict pass that follows still fails on any analyzer error.
  The CI workflow therefore differs from its template in this one step.
- (2026-11-04) `Export-Inventory.ps1` also copies the inventoried folder's top-level `*.cfg` files
  into a folder next to the inventory, unless `-NoConfigSnapshot` is given. Rationale: an
  inventory holds hashes only, so it shows that `FFXIV.cfg` changed but not which setting. The copy
  holds a last-login value and a session ID, so like the inventories it is written only below the
  ignored `out/` folder and never committed. Rejected: storing the values inside the inventory
  JSON, which would have changed its schema for one file; asking the owner to copy the file by
  hand at every step, which is easy to forget mid-experiment.
- (2026-11-04) The OneDrive experiment runs in a folder of its own inside the owner's OneDrive for
  Business, in its `Games` folder, as the owner offered. No game data goes there. Rejected: waiting
  for a personal OneDrive, which device A does not have configured.
- (2026-11-04) `dalamudConfig.json` is device-specific in release 0.1. Rationale: Dalamud's source
  code refers to plugins in profiles by an ID made per installation, so a copied profile does not
  match the other device's plugins, and a plugin in no profile is added to the default one.
- (2026-11-04) The Steam edition is supported only as experimental, with a warning shown to the
  user. Rationale: the owner cannot test it. The configuration folder is the same as for the
  standard edition (launcher source code), so support costs little, but it is unverified.
  This narrows the earlier entry that Steam "follows later".
- (2026-11-04) `FFXIV.cfg` is never synced as a whole. An advanced mode, off by default, may compare
  it key by key with the other device's copy, show the differences, and let the user pick single
  keys to take over. Rationale: the file mixes device settings with settings a player wants
  everywhere, and the owner had to set `UiHighScale` by hand on the second device to make the game
  playable, so a silent transfer of any display key would break it. The release is decided in
  gate 2. A picked key is written into a text file the game owns, so the pre-apply snapshot covers
  it like any other apply. Rejected: syncing a fixed allow-list without asking, because which keys a
  player wants shared differs from player to player.
- (2026-11-04) The file matrix is kept as a public draft in `docs/file-matrix.md`, with how well
  each row is known. Device names, character IDs and values from real files stay out of it.
- (2026-11-04) Correction to the entry on the advanced mode for `FFXIV.cfg`: with the first device's
  `UiHighScale` the game stayed usable on the second device, but every HUD element was shown far too
  large; the owner lowered the value by hand to get the right size. The decision stands: the value
  is device-specific, and a silent transfer would spoil the HUD on the other device, not break the
  game.
- (2026-11-04) The PSScriptAnalyzer warm-up in `build.ps1` and in the CI workflow also runs the
  `-SuppressedOnly` analysis, and analyzer errors raised by that pass are now collected and reported
  like those of the main pass instead of aborting the step. Rationale: that pass failed once in CI
  with a `RULE_ERROR` after the plain warm-up. Chosen by the owner. Rejected: retrying a file once
  and failing only if the error persists, which would soften the rule that any analyzer error
  fails; moving to a newer release, which needs a new pinned dependency.
