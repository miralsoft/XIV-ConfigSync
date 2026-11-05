# User guide: XIV ConfigSync

For release 0.1. The application is in German and English; it follows the language of Windows
unless you choose one in the settings.

## 1. First PC

1. Start `XIVConfigSync.exe`. Nothing needs to be installed.
2. **Sync folder:** choose a folder inside your OneDrive, for example `OneDrive\FFXIV Sync`. It
   must be empty or not exist yet. The application creates it and puts a file
   `FFXIV Sync.xivsync` into it.
3. **This device:** give the PC a name you will recognise on the other PCs.
4. **Folders:** the game's settings folder, the plugin settings folder and the installation
   folder are found by themselves. Change them only if the game or Dalamud is installed elsewhere.
5. **Automatic actions:** choose what may happen without asking: a backup after playing, taking
   "always" files from other PCs, opening `.xivsync` files with the application, starting with
   Windows. Everything can be changed later.
6. **Finish:** the first backup is made.

## 2. Every further PC

Wait until OneDrive has synced the sync folder to this PC, then either double-click the
`.xivsync` file in it or start the application and choose the same folder. The application
recognises the folder and shows the PCs already in it.

- Choose **Add this PC as a new device** for a PC that was never set up.
- Choose **This PC is already in the sync folder** after reinstalling Windows on a PC that was
  set up before; it takes over its old part of the sync folder and its backups.

After the set-up, open **Transfer**. On the first transfer every file that differs between the
PCs is shown as a conflict, because the application does not yet know which version is the right
one. Tick what you want from the other PC, or use **Take all conflicts from the source**, then
**Take selected**. From then on, changes flow by themselves according to the rules.

## 3. Day to day

- Play as usual. When you close the game, the application waits a few seconds for the game to
  finish writing, then backs up, then takes from the other PCs what the rules allow.
- If a file was changed on both PCs, you get a notification; decide on the **Transfer** page.
  Files are compared as a whole: two different hotbar slots changed on two PCs are still a
  conflict, because both live in one file. Then take the version you want and redo the other
  change by hand.
- To avoid conflicts, let the application run in the notification area (or start with Windows):
  it then takes the other PC's changes before you start the game.
- The application can run in the notification area. Closing the window asks once whether it
  should keep running there.

## 4. The Transfer page

- **Device** and **Backup:** where to take settings from. "Latest backup" is the normal case.
  Older backups of any PC, including this one, can be restored the same way.
- **Compare** shows every file that differs, with a proposal:
  - *Apply* (ticked): only the other PC changed it, and its rule is "always".
  - *Offered* (not ticked): it differs, and you decide.
  - *Conflict* (not ticked): both PCs changed it. Both versions are kept until you decide.
- Files with the rule "never" are not offered from another PC.
- **Take selected** saves the current files first, then replaces them. **Undo last transfer**
  puts the saved files back.
- **Merge plugin repositories:** adds the custom plugin repositories of the chosen PC that this
  PC lacks to Dalamud's settings here. You see the list and confirm it first. Repositories
  already there and every other Dalamud setting stay as they are; nothing is removed. After
  **Compare**, a note says how many are missing. **Undo last transfer** takes it back. Dalamud
  uses the new repositories from its next start.
- Nothing is written while the game runs.

## 5. Rules

The **Rules** page lists every known file with its default rule and how well it is known. Change
a rule with the list in its row. **Leave out of the backup** takes files out of the backup by a
pattern, for example `SomePlugin/*.json`.

Why some defaults are "on request": the game changes the HUD layout (`ADDON.DAT`), `UISAVE.DAT`
and the item order on every login by itself. Taking them automatically would overwrite a real
change on the other PC with a change the game made on its own.

## 6. What the upload state means

| Shown | Meaning |
|---|---|
| Handed to the sync folder; cloud state unknown | Written into the sync folder. The folder is not a OneDrive folder, or OneDrive gives no state. |
| Handed to OneDrive, upload pending | OneDrive has not uploaded everything yet. |
| Uploaded (OneDrive reports it in sync) | OneDrive reports every new file of the backup as in sync. |
| received by PC X | PC X has read and checked this backup. |

## 7. Troubleshooting

- **"Some files of this backup are missing or damaged":** OneDrive has not finished downloading
  the other PC's backup. Wait, then compare again.
- **"An interrupted transfer":** a transfer or an undo was stopped, by a power cut or because the
  game was started. Close the game; the application asks whether to finish it or go back to the
  state before. Until then it makes no backup and no transfer.
- **"The game settings folder cannot be found":** the folder in the settings no longer exists.
  Nothing is transferred into a missing folder; choose the right one in the settings.
- **"The game version differs":** update the game on both PCs first; files are only exchanged
  between the same game version.
- **"The sync folder cannot be found":** the drive or OneDrive is not available. Connect it and
  start again, or choose the sync folder again.
- **Old conflict copies:** if the game folder holds files such as `HOTBAR-PCNAME.DAT` from an
  earlier whole-folder sync, the overview counts them; they are not backed up and not
  transferred. **Show and clean up** (or the **Clean up** page) lists them with the file each is a
  copy of. Tick the ones you no longer need and choose **Move ticked files to the Recycle Bin**;
  after you confirm, exactly those go to the Windows Recycle Bin, from where you can restore them.
  Nothing is ticked for you. A copy whose original is missing cannot be ticked, since it may be
  the only version left. While the game runs, nothing is moved.
- **Log:** the **Log** page and the log folder (`%LOCALAPPDATA%\XIVConfigSync\logs`) show what
  happened. Character IDs and your user name are not written into the log.

## 8. Where things are

| What | Where |
|---|---|
| Settings, rules, snapshots before a transfer, log | `%LOCALAPPDATA%\XIVConfigSync` |
| Files saved before an undo or a recovery ("safety" snapshots) | `%LOCALAPPDATA%\XIVConfigSync\snapshots`, each in a folder with a `snapshot.json` and the files under `files\` |
| Backups of all PCs | your sync folder |

To remove the application: turn off "Start with Windows" and "Open .xivsync files" in the
settings, quit it, and delete the executable and `%LOCALAPPDATA%\XIVConfigSync`. Your sync folder
stays until you delete it.
