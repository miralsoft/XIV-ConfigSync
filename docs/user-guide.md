# User guide: XIV ConfigSync

For release 0.1. The application is in German and English; it follows the language of Windows
unless you choose one in the settings.

## 1. First PC

1. Start `XIVConfigSync.exe`. Nothing needs to be installed.
2. **Sync folder:** choose a folder inside your OneDrive, for example `OneDrive\FFXIV Sync`. It
   must be empty or not exist yet. The application creates it and puts a file
   `FFXIV Sync.xivsync` into it.
3. **This device:** give the PC a name you will recognise on the other PCs.
4. **Folders:** the game's settings folder, the XIVLauncher folder (usually
   `%APPDATA%XIVLauncher`) and the installation folder are found by themselves. From the
   XIVLauncher folder the application takes the plugin settings and Dalamud's own settings, also
   in Dalamud's sandbox mode; the plugins themselves are installed by Dalamud. Change the folders
   only if the game or XIVLauncher is installed elsewhere.
5. **Characters:** where a plugin already knows a character (AutoRetainer, Lifestream, ICE,
   Allagan Tools, Allagan Market), its name and, where given as text, its world are used as the
   character's name. You can rename every character in the settings.
6. **Automatic actions:** choose what may happen without asking: a backup after playing, taking
   the areas switched on from other PCs, opening `.xivsync` files with the application, starting with
   Windows. Everything can be changed later.
7. **Finish:** the first backup is made.

## 2. Every further PC

Wait until OneDrive has synced the sync folder to this PC, then either double-click the
`.xivsync` file in it or start the application and choose the same folder. The application
recognises the folder and shows the PCs already in it.

- Choose **Add this PC as a new device** for a PC that was never set up.
- Choose **This PC is already in the sync folder** after reinstalling Windows on a PC that was
  set up before; it takes over its old part of the sync folder and its backups.

On the first transfer, the application does not yet know which PC has the right settings. It asks
once: **Yes** takes the settings of the other PC (the files here are saved first), **No** keeps the
settings of this PC, and the other PC takes them from here, **Cancel** leaves the decision for the
**Transfer** page. From then on, changes flow by themselves in the areas that are switched on.

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
  - *Apply* (ticked): only the other PC changed it, and its area is switched on.
  - *Offered* (not ticked): it differs, and you decide.
  - *Conflict* (not ticked): both PCs changed it. Both versions are kept until you decide.
- Files of an area switched off, and settings of one PC, are not offered from another PC.
- Files that exist only on this PC, not in the chosen backup, are never deleted. A note says how
  many there are; **Also show files with nothing to do** lists them with the proposal *Only here*.
- **Take selected** saves the current files first, then replaces them. **Undo last transfer**
  puts the saved files back.
- **Merge plugin repositories:** adds the custom plugin repositories of the chosen PC that this
  PC lacks to Dalamud's settings here. You see the list and confirm it first. Repositories
  already there and every other Dalamud setting stay as they are; nothing is removed. After
  **Compare**, a note says how many are missing. **Undo last transfer** takes it back. Dalamud
  uses the new repositories from its next start.
- Nothing is written while the game runs.

## 5. Areas

The **Areas** page says what this PC takes from your other PCs. You do not need to know any file
names: switch an area on or off, and the application knows which files belong to it.

| Area | What it holds |
|---|---|
| Hotbars and macros | Hotbars, cross hotbars, character macros and shared macros |
| Key bindings and controls | Key bindings, mouse and keyboard, game pad |
| Gear sets | Your saved gear sets |
| Character settings | Name plates, camera, battle effects and the other character settings, without the chat |
| Chat | Font size and transparency, time stamps, chat bubbles, colours, tabs, filters and channels of the chat window |
| HUD layout | Size and position of every window on screen, the chat window included |
| Inventory order | The order of items in inventory, saddlebag and retainers |
| Plugin settings | The settings of your Dalamud plugins |
| Plugin sources | Custom plugin repositories another PC has and this one lacks |

Some areas can be opened further: under **Chat**, **Character settings**, **Hotbars and macros** and **Key bindings and controls**, single groups of settings can be switched, for example only the chat's font size and transparency. This works where the game keeps its settings as named text. Window positions, the contents of hotbars, macros, key bindings and gear sets can only be taken whole.

All areas are on at first. The switches apply to this PC only: if the chat is too large on your
laptop, switch **Chat** off there, and the laptop keeps its own chat settings while everything else
still comes over. The size and position of the chat window belong to the HUD layout.

Settings of one PC (resolution, graphics, sound, the launcher) are in no area and never taken.

For the HUD layout and the inventory order, the game rewrites the files on every login. When both
PCs changed them, the state backed up last wins instead of a question.

**Plugins (advanced)** gives a single plugin its own rule: **As the area** follows the area
"Plugin settings", or choose always, on request or never for this plugin alone. **Not backed up**
leaves a plugin out of the backup entirely; it then also shows under the exclusions.
**Files (advanced)** lists every known file with its rule; a rule chosen there wins over the area.
**Leave out of the backup** takes files out of the backup by a pattern, for example
`SomePlugin/*.json`.

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
