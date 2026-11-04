# Open points: XIV ConfigSync

## Game files

- Where the UI scale lives. `FFXIV.cfg` holds `UiBaseScale` and `UiHighScale`; whether that is the
  whole story is what experiment V01 answers.
- The HUD layout (`ADDON.DAT`) carries over from a 16:9 device to a 16:10 device (transfer test,
  2026-11-04). Open: the other direction, and why the game rewrote a few hundred bytes of the
  applied file after play.
- What the `cfgcopy` folder (`FFXIV_CFGCPY<id>.dat`) is for. No source describes it.
- Which files the game rewrites on every start even when nothing changed (V00).
- When the game writes: at logout, at exit, or continuously.
- The game keeps a `.old` copy next to several files. Whether a sync must carry it or leave it.
- In which release the advanced mode for `FFXIV.cfg` comes (compare key by key, show the
  differences, let the user pick keys to take over); see `decisions.md`. Until then the file is
  device-specific as a whole.

## Plugins

- How the sync detects plain-text credentials and warns before such a file leaves the device (an
  API key was found in one plugin configuration).
- Whether Dalamud keeps a valid settings file written from outside, or prefers the copy in its
  shadow database `dalamudVfs.db`. Part of experiment V02f.
- Which plugin files are settings and which are caches, data exports or logs, plugin by plugin.
- Which plugin settings depend on the screen, such as window positions.
- Whether plugin data exports (`*.csv`) belong to a character, which would make them shareable.

## OneDrive

- What the sync state shows while the device is offline (expected: stays at "upload pending").
- How OneDrive behaves when two devices write the same file; needs device B.
- Whether a personal OneDrive reports the same states as OneDrive for Business, where it was
  measured.
- Whether the owner wants to sync through the work OneDrive at all. Tests run in its `Games`
  folder with the owner's consent.

## Tooling

- The extended warm-up (see `decisions.md`, 2026-11-04) did not end the intermittent analyzer
  failures in CI: two of the next four CI attempts failed with `RULE_ERROR` "Get-Command is not
  recognized", ten times each, always in `tests/XivInventory.Tests.ps1`; a rerun passed both
  times. It has not been reproduced locally (every local build passed).
- A changed release does not end it either (measured 2026-11-04, eight sequential CI runs each, on
  a test branch): PSScriptAnalyzer 1.24.0 failed 3 of 8 runs, all with the same "Get-Command is not
  recognized" error; 1.25.0 failed 6 of 8, five with that error and one where the analyzer step
  ended with exit code 5 and no output. The error therefore also exists in 1.24.0, and staying on
  the pinned 1.25.0 costs nothing. A one-time retry for exactly this error is now in place (see
  `decisions.md`). It does not cover the run that ended with exit code 5, whose cause is unknown.
- The retry does not help (measured 2026-11-04, eight sequential CI runs on `main`): 3 of 8 failed.
  In each failed run the error, once it appeared, came back on the second analysis and in every
  file analysed after it. The fault is a state of the PowerShell process, not of one analysis, so
  analysing again in the same process cannot clear it. Answered by running the analysis in a
  separate process that is replaced on this fault (see `decisions.md`); whether that ends the
  failures in CI is seen in the next runs. A process that ends before reporting every file, as in
  the run with exit code 5, is now replaced the same way.
