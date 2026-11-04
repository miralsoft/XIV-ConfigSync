# Open points: XIV ConfigSync

## Game files

- Where the UI scale lives. `FFXIV.cfg` holds `UiBaseScale` and `UiHighScale`; whether that is the
  whole story is what experiment V01 answers.
- What the `cfgcopy` folder (`FFXIV_CFGCPY<id>.dat`) is for.
- Which files the game rewrites on every start even when nothing changed (V00).
- When the game writes: at logout, at exit, or continuously.
- The game keeps a `.old` copy next to several files. Whether a sync must carry it or leave it.

- `FFXIV.cfg` mixes device settings (display, graphics, adapter, game pad GUIDs, screenshot
  folder) with settings a player may want everywhere (sound, mouse, camera, cut scenes, game pad
  buttons, accessibility). Open for gate 2: treat the whole file as device-specific in 0.1, or sync
  an allow-list of keys. The file is text, so an allow-list is not field editing of a binary file,
  but it does mean writing into a file the game owns.
- What `FFXIV_CHARA_nn.dat` holds; likely character creation presets.

## Plugins

- Plugins keep credentials in plain text in their configuration (an API key was found in one).
  How the sync detects and warns before such a file leaves the device.
- `dalamudConfig.json` mixes plugin repositories and profiles with device paths, fonts and UI
  scale. Whether any of it is synced, and whether syncing plugin profiles can make the launcher
  install or enable plugins on the other device.
- Whether plugin data exports (`*.csv`) belong to a character, which would make them shareable.

- Which plugin files are caches that must never be synced. One plugin alone keeps about 430 MB of
  navigation meshes in its configuration folder on device A.
- Which plugin settings depend on the screen, such as window positions.

## OneDrive

- Whether a local app can tell an uploaded file from one still waiting, without signing in to a
  Microsoft account.
- Which OneDrive the owner wants to use; device A has only a work account configured.
