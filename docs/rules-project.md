# Project rules: XIV ConfigSync

Rules that only add to or tighten the global rules.

## Added prefixes

- `X`: product safety, specific to handling game and plugin files.

## Rules

- **X-01** Nothing writes to the game's or a plugin's configuration while the game is running.
- **X-02** No real user file is changed without a verified backup or snapshot taken first.
- **X-03** Binary game files are handled as whole files. No field inside them is read or changed
  on the basis of an assumed layout.
- **X-04** The product never reads game memory, never injects into the game process, never talks
  to game servers, and never automates anything in the game.
- **X-05** Analysis tools are read-only towards the folders they examine and refuse an output path
  inside them.
- **X-06** Character content IDs do not appear in reports, logs, issues or commits; a stable
  pseudonym is used instead.
- **X-07** Inventories, backups and any copy of real game or plugin data stay local and are never
  committed.
- **X-08** Features that run on their own (autostart, background sync, update checks) ship off or
  ask once.
- **X-09** Neither the product nor this repository contains material from the game: no logos,
  icons, artwork, screenshots, music or game text. "FINAL FANTASY" is not part of the product's
  name.
