# XIV ConfigSync

Keep your FINAL FANTASY XIV client settings and Dalamud plugin configuration in step across your
own PCs, while each PC keeps the settings that belong to its screen.

> **Status: not usable yet.** The project is in its feasibility analysis. This repository holds
> the plan and a few read-only analysis scripts, nothing you can install.

## The problem

Play on two PCs and copy your settings folder from one to the other, and you also copy the
resolution and UI scale of the first PC to the second. Sync the whole folder with a cloud drive
and both PCs overwrite each other, leaving conflict copies behind. XIV ConfigSync is meant to sync
what should be shared (hotbars, macros, gear sets, key bindings, plugin settings) and leave alone
what belongs to one device.

## What it will not do

- It does not read or change anything while the game is running.
- It does not read game memory, inject into the game, talk to game servers, or automate anything
  in the game.
- It does not install, update or remove plugins.
- It does not collect data or send anything anywhere. Syncing goes through a folder you already
  sync yourself, such as OneDrive; there is no server and no account.
- It does not copy launcher passwords or account files.
- It does not edit the game's binary settings files field by field. Files are handled whole.

## Analysis scripts

`tools/` holds read-only PowerShell scripts used during the analysis. They list files with size,
hash and time stamp, compare two such lists, and report where the configuration folders are. They
never write into the folders they examine. Run `build.ps1` to lint and test them.

## Legal

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd. FINAL FANTASY XIV
© SQUARE ENIX. This project is not affiliated with or endorsed by Square Enix. Dalamud and
XIVLauncher are third-party tools, used at the player's own choice and risk.

Licensed under the GNU General Public License v3.0 or later; see `LICENSE`.
