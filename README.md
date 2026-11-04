# XIV ConfigSync

Keep your FINAL FANTASY XIV client settings and Dalamud plugin configuration in step across your
own PCs, while each PC keeps the settings that belong to its screen.

> **Status: 0.1 in development, not released yet.** The application works end to end in tests;
> the test on two real PCs is still to come. There is no download yet.

## The problem

Play on two PCs and copy your settings folder from one to the other, and you also copy the
resolution and UI scale of the first PC to the second. Sync the whole folder with a cloud drive
and both PCs overwrite each other, leaving conflict copies behind. XIV ConfigSync syncs what
should be shared (hotbars, macros, key bindings, plugin settings) and leaves alone what belongs to
one device.

## How it works

- **One sync folder.** Choose one folder in your OneDrive (work or personal) on every PC, or
  double-click the `.xivsync` file in it. The application organises everything inside; each PC
  writes only its own part of the folder, so OneDrive never has to merge anything.
- **Back up everything.** After you close the game (or when you press "Back up now"), the
  application records all configuration files of the game and, if you want, of your plugins.
  Only files whose content changed are stored again. Caches, logs and lock files are left out.
- **Apply by rule.** What is taken from another PC follows a rule per file, which you can change:
  - *Always:* taken without asking, when only the other PC changed it (hotbars, key bindings,
    shared macros, mouse and keyboard settings).
  - *On request:* shown, taken only when you tick it (HUD layout, plugin settings, files the game
    changes by itself).
  - *Never:* belongs to one PC (`FFXIV.cfg` with graphics, resolution and UI scale; launcher
    files).
- **Conflicts keep both versions.** If a file changed on both PCs, nothing is overwritten; you
  decide on the Transfer page.
- **Every transfer can be undone.** Before a file is replaced, the current one is saved and
  checked; "Undo last transfer" puts it back. An interrupted transfer (for example a power cut)
  is finished or rolled back at the next start.
- **Honest upload state.** "Handed to OneDrive", "uploaded" (as OneDrive reports it) and
  "received by PC X" (confirmed by that PC) are shown separately.

## What it will not do

- It does not write anything into the game's or the plugins' folders while the game is running.
- It does not read game memory, inject into the game, talk to game servers, or automate anything
  in the game.
- It does not install, update or remove plugins.
- It does not collect data or send anything anywhere. There is no server, no account and no
  telemetry; syncing goes through a folder you already sync yourself.
- It does not delete files on your other PCs.
- It does not edit the game's settings files field by field. Files are handled whole.

## Requirements

Windows 10 or 11 (64-bit). The portable executable needs no installation and no administrator
rights. The game in its standard edition; the Steam edition uses the same folder and is
supported as experimental. Dalamud is optional.

## Building

Needs the .NET SDK pinned in `global.json`.

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
dotnet publish src\XivConfigSync.App\XivConfigSync.App.csproj -c Release -p:PublishSingleFile=true -o artifacts\publish
```

`build.ps1` runs every check CI runs: formatting of C# and XAML, the build with warnings as
errors, the tests, the dependency audit and the PowerShell checks for the analysis scripts in
`tools/`.

## Documentation

`docs/specification.md` (what it does), `docs/architecture.md` (how), `docs/data-model.md` (what
it stores), `docs/test-strategy.md`, `docs/file-matrix.md` (what each game file is) and
`docs/user-guide.md`.

## Legal

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd. FINAL FANTASY XIV
© SQUARE ENIX. This project is not affiliated with or endorsed by Square Enix. It contains no
material from the game. Dalamud and XIVLauncher are third-party tools, used at the player's own
choice and risk.

Licensed under the GNU General Public License v3.0 or later; see `LICENSE`.
