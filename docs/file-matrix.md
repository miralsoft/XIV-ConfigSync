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
| `FFXIV_CHARA_nn.dat` | Shared, not in 0.1 | unconfirmed (third-party source code) | Character creation appearance presets, 212 bytes each |
| `cfgcopy\FFXIV_CFGCPY<id>.dat` | Device, Sensitive | confirmed (content), unknown (purpose) | Section format masked with 0x73, like `MACRO.DAT`: a character's name, world and content ID, plus two values that change on every write. Rewritten together with `FFXIV.cfg` and `UISAVE.DAT`; V00: one value per character changed on a login with no change |
| `MACROSYS.dat` | Shared | confirmed (transfer) | The macros shared by all characters ("all characters" tab), in the top folder, not in a character folder. Masked like `MACRO.DAT` |
| `FFXIV_CHR<id>\*.DAT` | Shared | unconfirmed | Per character: HUD, hotbars, macros, gear sets, key bindings, item order. The rows below are confirmed file by file |
| `FFXIV_CHR<id>\HOTBAR.DAT` | Shared | confirmed (transfer) | Hotbars and cross hotbars |
| `FFXIV_CHR<id>\KEYBIND.DAT` | Shared | confirmed (transfer) | Key bindings; one changed binding is three bytes |
| `FFXIV_CHR<id>\CONTROL0.DAT` | Shared | confirmed (transfer) | Plain text: settings for mouse and keyboard mode, including which HUD info is shown (for example the server info) |
| `FFXIV_CHR<id>\COMMON.DAT`, `CONTROL1.DAT` | Shared | confirmed (format), unconfirmed (class) | Plain text like `FFXIV.cfg`: character settings and game pad mode, no window positions |
| `FFXIV_CHR<id>\ADDON.DAT` | Shared | confirmed (transfer, one direction) | HUD layout. Own format, not masked. A layout set up on a 16:9 device, with one element moved, was applied to a 16:10 device and showed correctly there. After play the game rewrote 380 of its bytes; V00: a login and logout with no change rewrote 1,307 bytes in about 450 places. The file changes without the user, so a changed hash alone does not mean a changed layout |
| `FFXIV_CHR<id>\UISAVE.DAT` | unknown | confirmed (changes by itself, V00) | Three 8-byte values change on every login with no change by the user; likely time stamps or counters. Not in 0.1 until its content is known |
| `FFXIV_CHR<id>\ITEMODR.DAT` | unknown | confirmed (changes by itself, V00) | Item order. V00: grew by 240 bytes at the end on a login with no change, the existing bytes unchanged. Probably follows the server's inventory, so likely not worth syncing |
| `*.old` next to a file | Ignore | confirmed | The game's own previous generation, rotated by the game when it writes; left in place on apply |
| `<name>-<COMPUTERNAME>[-n].<ext>` | Ignore | confirmed | Conflict copies a cloud client left behind; reported, never synced |
| `screenshots\`, `log\`, `downloads\` | Ignore | confirmed | Not configuration |

Experiment V00 (2026-11-04, device A): a login and logout with no change touched only the folder of
the character that logged in; the other character folders and the files in the top folder kept
their write times, except `FFXIV.cfg`. The game wrote `FFXIV.cfg` and these character files back
with identical content: `ACQ`, `COMMON`, `CONTROL0`, `CONTROL1`, `GEARSET`, `GS`, `HOTBAR`,
`MACRO`. A write time therefore says nothing; only the content does. `ADDON`, `UISAVE`, `ITEMODR`
and the `cfgcopy` file changed in content without any change by the user.

"Confirmed (transfer)" means: changed in the game on one device, copied as a whole file to the
other device while the game was closed, and seen in the game there (2026-11-04, from the 16:9
device to the 16:10 device; the other direction is not tested).

### Binary character files

A 17-byte header, the content, a 15-byte footer; most files mask the content with a single XOR
byte (confirmed on real files, layout as published by the libxivdat project). The upper half of the
header's type field changes with game patches, so it acts as a format version. The sync never
applies a file whose format version differs from the one on the target device; reading the field
is enough, nothing in the file is changed.

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

The game does not correct foreign device values: on a second device with another graphics adapter
and a 16:9 screen, a `FFXIV.cfg` taken over from a 16:10 device kept the first device's adapter
name, resolution and graphics quality after play (confirmed). The file must therefore never be
sent as a whole; the game will not repair it.

`UiHighScale` is confirmed device-specific: with the 16:10 device's value every HUD element was far too
large on the 16:9 device, so the owner lowered it by hand. The game stays usable either way.

The file is Device as a whole. An advanced mode, off by default, may later show the key-by-key
differences to the other device and take over single keys the user picks (see `decisions.md`). It
is a text file of key and tab and value, so this does not mean editing a binary file by field
position.

## Launcher and plugin folder

`%APPDATA%\XIVLauncher\`

| File or pattern | Class | Known | Note |
|---|---|---|---|
| `pluginConfigs\<Plugin>.json` | Shared, opt-in per plugin | unconfirmed | One file per plugin. Some hold device paths or window positions. V00: one login with no change rewrote 35 files below `pluginConfigs`, 9 of them with different content (settings and caches alike) |
| `pluginConfigs\<Plugin>\` | per plugin | confirmed (existence) | Holds caches, data exports and logs as often as settings |
| Navigation meshes (`*.navmesh`) | Cache | confirmed | About 430 MB on one device |
| Plugin logs (`*.ahlog`), lock files (`*.lock`), `*.bak`, `*_backup.json` | Ignore | confirmed (existence) | |
| Plugin data exports (`*.csv`) | unknown | confirmed (existence) | Inventory and market data; possibly per character, not per device |
| A plugin configuration holding an API key | Sensitive | confirmed | Found on device A. A synced copy puts the key into the cloud folder |
| `dalamudConfig.json` | Device in 0.1 (backed up, root `dalamud`, rule never) | confirmed (mixed), source code (profiles) | Plugin profiles refer to plugins by an ID made per installation, so copied profiles do not match the other device's plugins. Also holds device paths, UI scale and fonts. The custom repository list (`ThirdRepoList`) is merged between devices on request: missing entries are appended, nothing else changes (FR-072) |
| `dalamudUI.ini` | Device (backed up, rule never) | unconfirmed (launcher FAQ) | Window layout; valid only at the same resolution |
| `dalamudVfs.db` | Not backed up (about 12 MB, changed on every run) | source code | Shadow copy of the Dalamud and plugin settings. A missing or unreadable settings file is silently restored from it, so an applied file must always be complete and valid |
| `dalamudUserData\` | location | source code | Newer launch mode moves all of the above here; the sync has to detect which location is live |
| `launcherConfigV3.json` | Device (backed up, rule never) | confirmed (keys) | Game path, patch path, window placement |
| `accountsList.json`, `uidCache.json` | Sensitive (`accountsList.json` backed up, rule never; `uidCache.json` not backed up) | confirmed (existence) | Account names; passwords are in the Windows credential store |
| `installedPlugins\` | Ignore | unconfirmed (launcher FAQ) | Must not be copied; the launcher installs plugins itself |
| `*.log`, `*.dmp`, `*.tspack` | Ignore | confirmed | |

## Consequences already visible

- A plugin is synced only when the user turns it on (opt-in), and each plugin needs its own rule for
  which of its files are settings. A size warning is needed before anything large is sent.
- The sync must scan what it is about to send for credentials and warn, because plugins store keys
  in plain text.
- Leftover conflict copies are detected and reported; removing them waits for a later release and
  always needs the user's confirmation.
