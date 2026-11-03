# Open points: XIV ConfigSync

## Game files

- Where the UI scale lives. `FFXIV.cfg` holds `UiBaseScale` and `UiHighScale`; whether that is the
  whole story is what experiment V01 answers.
- What the `cfgcopy` folder (`FFXIV_CFGCPY<id>.dat`) is for.
- Which files the game rewrites on every start even when nothing changed (V00).
- When the game writes: at logout, at exit, or continuously.
- The game keeps a `.old` copy next to several files. Whether a sync must carry it or leave it.

## Plugins

- Which plugin files are caches that must never be synced. One plugin alone keeps about 430 MB of
  navigation meshes in its configuration folder on device A.
- Which plugin settings depend on the screen, such as window positions.

## OneDrive

- Whether a local app can tell an uploaded file from one still waiting, without signing in to a
  Microsoft account.
- Which OneDrive the owner wants to use; device A has only a work account configured.
