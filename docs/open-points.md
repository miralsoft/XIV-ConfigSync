# Open points: XIV ConfigSync

## Game files

- Where the UI scale lives. `FFXIV.cfg` holds `UiBaseScale` and `UiHighScale`; whether that is the
  whole story is what experiment V01 answers.
- Whether the HUD layout (`ADDON.DAT`) carries over to a device with another aspect ratio. A
  third-party source describes the positions as relative to the screen, with a question mark; a
  community tool keeps one copy per resolution anyway. Experiment V03 decides; the fallback is one
  copy per aspect ratio, always as a whole file.
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

- Whether the extended warm-up (see `decisions.md`, 2026-11-04) ends the intermittent analyzer
  failures in CI. If one recurs, the owner chooses between a retry and a newer PSScriptAnalyzer
  release.
