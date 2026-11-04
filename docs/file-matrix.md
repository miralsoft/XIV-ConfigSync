# File matrix: XIV ConfigSync

Status: **draft**. This is the working hypothesis of the feasibility analysis, not a design. Every
row carries how well it is known. "Confirmed" means seen in real files; "unconfirmed" means read
in a source or inferred, not yet tested; "unknown" means nobody knows yet. The experiments V00 to
V03 turn rows into confirmed ones or move them. Release 0.1 syncs only rows confirmed as shared.

## Classes

| Class | Meaning for the sync |
|---|---|
| Shared | Synced between devices |
| Device | Belongs to one device; never sent, never overwritten |
| Mixed | Holds both shared and device settings in one file; see below |
| Cache | Rebuilt by its owner; never synced |
| Sensitive | Holds credentials or account data; never synced by default |
| Ignore | Logs, dumps, backups the game or a tool keeps for itself |

## Game configuration folder

`Documents\My Games\FINAL FANTASY XIV - A Realm Reborn\`

| File or pattern | Class | Known | Note |
|---|---|---|---|
| `FFXIV.cfg` | Mixed | confirmed | Text, 213 keys in 15 sections. See the next section |
| `FFXIV_BOOT.cfg` | Device | confirmed (content), unconfirmed (class) | Launcher state: licence agreement flags, browser, region, data centre |
| `FFXIV_CHARA_nn.dat` | unknown | confirmed (existence) | 212 bytes each, one per character slot; likely the character creation presets |
| `cfgcopy\FFXIV_CFGCPY<id>.dat` | Device | confirmed (existence), unknown (purpose) | Device until its purpose is known |
| `FFXIV_CHR<id>\*.DAT` | Shared | unconfirmed | Per character: HUD, hotbars, macros, gear sets, key bindings, item order. V02 confirms file by file |
| `*.old` next to a file | Ignore | confirmed | The game's own previous generation; left in place on apply |
| `<name>-<COMPUTERNAME>[-n].<ext>` | Ignore | confirmed | Conflict copies a cloud client left behind; reported, never synced |
| `screenshots\`, `log\`, `downloads\` | Ignore | confirmed | Not configuration |

### Inside `FFXIV.cfg`

The file mixes settings that depend on the device with settings a player usually wants everywhere.
Conflict copies from an earlier whole-folder sync differ from today's file in exactly the expected
places: the graphics adapter name, window position and size, full-screen resolution, `UiHighScale`,
two dozen graphics quality keys, the screenshot folder and the game pad GUIDs.

| Section | Proposed class | Known |
|---|---|---|
| `Version` | Device | unconfirmed |
| `Network Settings` (includes the last login and a session ID) | Sensitive | confirmed (keys) |
| `Display Settings` (adapter, resolution, window, UI scale, gamma, frame rate) | Device | confirmed (differs between devices) |
| `Graphics Settings`, `Graphics Settings DX11` quality keys | Device | confirmed (differs between devices) |
| `Graphics Settings DX11` accessibility, touch pad and AFK keys | Shared | unconfirmed |
| `GamePad Settings` GUIDs and `ScreenShotDir` | Device | confirmed (differs between devices) |
| `GamePad Settings` other keys, `GamePad Button Settings` | Shared | unconfirmed |
| `Sound Settings`, `SoundPlay Settings` | Shared | unconfirmed; a player may want quieter sound on a laptop |
| `Mouse Settings`, `UI Settings`, `Move Operation`, `Cutscene Settings` | Shared | unconfirmed; `CameraZoom` and `DynamicRezoType` may be device |

Whether 0.1 treats `FFXIV.cfg` as Device as a whole, or syncs an allow-list of keys, is open
(see `open-points.md`). It is a text file of key and tab and value, so an allow-list does not
mean editing a binary file by field position.

## Launcher and plugin folder

`%APPDATA%\XIVLauncher\`

| File or pattern | Class | Known | Note |
|---|---|---|---|
| `pluginConfigs\<Plugin>.json` | Shared, opt-in per plugin | unconfirmed | One file per plugin. Some hold device paths or window positions |
| `pluginConfigs\<Plugin>\` | per plugin | confirmed (existence) | Holds caches, data exports and logs as often as settings |
| Navigation meshes (`*.navmesh`) | Cache | confirmed | About 430 MB on one device |
| Plugin logs (`*.ahlog`), lock files (`*.lock`), `*.bak`, `*_backup.json` | Ignore | confirmed (existence) | |
| Plugin data exports (`*.csv`) | unknown | confirmed (existence) | Inventory and market data; possibly per character, not per device |
| A plugin configuration holding an API key | Sensitive | confirmed | Found on device A. A synced copy puts the key into the cloud folder |
| `dalamudConfig.json` | Mixed | confirmed | Plugin repositories and profiles next to device paths, UI scale and fonts |
| `dalamudUI.ini` | Device | unconfirmed (launcher FAQ) | Window layout; valid only at the same resolution |
| `dalamudVfs.db` | unknown | unconfirmed (launcher FAQ says copyable) | About 12 MB |
| `launcherConfigV3.json` | Device | confirmed (keys) | Game path, patch path, window placement |
| `accountsList.json`, `uidCache.json` | Sensitive | confirmed (existence) | Account names; passwords are in the Windows credential store |
| `installedPlugins\` | Ignore | unconfirmed (launcher FAQ) | Must not be copied; the launcher installs plugins itself |
| `*.log`, `*.dmp`, `*.tspack` | Ignore | confirmed | |

## Consequences already visible

- A plugin is synced only when the user turns it on (opt-in), and each plugin needs its own rule for
  which of its files are settings. A size warning is needed before anything large is sent.
- The sync must scan what it is about to send for credentials and warn, because plugins store keys
  in plain text.
- Leftover conflict copies are detected and reported; removing them waits for a later release and
  always needs the user's confirmation.
