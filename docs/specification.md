# Specification: XIV ConfigSync

Status: approved in gate 2 (2026-11-04); release 0.1 is implemented (gates 3 and 4). Revises the
preliminary specification of the project package in the light of the feasibility analysis (gate 1).

Related pages: `architecture.md` (how it is built), `data-model.md` (what it stores),
`test-strategy.md` (how it is proven), `file-matrix.md` (what each game file is),
`decisions.md` (why).

## 1. Purpose

A standalone Windows 11 application, in German and English, that backs up the client settings of
the game and the configuration of Dalamud plugins on each of the user's PCs into a folder the user
already syncs (OneDrive, work or personal), and applies them on another PC or on the same PC later.
What may be applied follows a rule per file that the user can change. Settings that belong to one
device, such as resolution and the in-game UI scale, are never applied from another device without
the user asking.

Not a goal: a general backup tool, backing up the game installation, changing game or Dalamud
binaries, running a cloud service, reading game memory or talking to game servers (rule X-04).

## 2. Findings this specification rests on

All confirmed in gate 1 unless marked otherwise; details in `file-matrix.md`.

- Shared and device settings separate at file level. `FFXIV.cfg` mixes both and holds the
  device's graphics adapter, resolution and UI scale; the game does not correct foreign values.
- Copying a whole character file from one device to the other while the game is closed carries
  the setting over (shared macros, hotbars, key bindings, HUD layout, an info display setting).
- A HUD layout made on a 16:9 screen shows correctly on a 16:10 screen.
- The game rewrites most files of the character that logs in on every login, mostly with the same
  content. The HUD layout, `UISAVE.DAT`, the item order and the `cfgcopy` file change by
  themselves. A write time says nothing; only the content does.
- The OneDrive client reports per file, without signing in, whether an upload is pending or done
  (measured on OneDrive for Business only).
- Dalamud keeps a shadow copy of plugin settings in `dalamudVfs.db` and may restore an older state
  from it when a file is missing or invalid (source code, not tested). Plugin profiles in
  `dalamudConfig.json` refer to plugins by an ID made per installation.

## 3. Core concept: back up everything, apply by rule

- **One sync folder.** The user chooses a single folder for all devices. On a further device the
  user chooses the same folder or double-clicks the sync file in it (`.xivsync`); the application recognises it, lists the devices already in it and
  creates this device's part by itself (`architecture.md`, "Setting up a device"). The user never
  chooses a folder per device.
- **Back up.** Each device records the whole state of its configuration folders, device settings
  included, as a revision in its own part of the sync folder. Configuration means everything
  except caches, logs and lock files, which are left out by default and can be included. Only the files whose content changed are uploaded
  again. Every file is stored whole; nothing inside a file is changed or picked apart (rule X-03).
- **Apply.** A revision, from another device or an older one of the same device, is applied file
  by file according to the file's rule:

  | Rule | Meaning | Default for |
  |---|---|---|
  | Always | Applied without asking when the source changed it and the local copy did not | Shared macros (`MACROSYS.dat`), hotbars, key bindings, mouse and keyboard settings; further character files once confirmed |
  | On request | Offered, applied only when the user picks it | HUD layout, `UISAVE.DAT`, item order, plugin settings, files with unknown class |
  | Never | Not offered for apply between devices; can still be restored on the device that made it | `FFXIV.cfg` as a whole, `FFXIV_BOOT.cfg`, `cfgcopy`, `dalamudConfig.json`, launcher files |

- The user can change any rule, per file pattern, per character and per plugin. Changing a
  "never" rule shows a warning that names what the file holds.
- A file the matrix does not know is backed up and gets "on request".
- Because everything is backed up, a finer choice can come later without a new backup format: for
  the plain text files (`FFXIV.cfg`, `COMMON.DAT`, `CONTROL0.DAT`, `CONTROL1.DAT`) single settings
  can be picked, as planned for `FFXIV.cfg` in 0.2. Picking single entries inside a binary file
  (one hotbar, one macro) needs a verified description of that file's format first (X-03) and is
  not planned before one exists.

## 4. Requirements

The numbers follow the project package (FR-001 to FR-070). Each line states what this
specification makes of it and in which release. "0.1" is the first public release.

### 4.1 Interface

| ID | Requirement | Release |
|---|---|---|
| FR-001 | Main window with the status of the game (running or not), the shared folder, the last backup, the last apply, warnings and conflicts. Compact view first; an extended view later | 0.1 |
| FR-002 | Run now, pause and history; in 0.1 for the one sync job, later per job | 0.1 / 0.2 |
| FR-003 | First-start wizard: choose the one sync folder (new or existing), name the device or recognise it as a device set up again, confirm the folders found, run the first backup. Job templates for local and USB targets | 0.1 / 0.2 |
| (new) | Own file extension `.xivsync` for the sync file, registered per user after asking once; a double-click opens the application on that sync folder or starts the set-up there | 0.1 |
| FR-004 | Tray icon; closing the window minimises or quits, asked once and remembered | 0.1 |
| FR-005 | Start with Windows (off by default, asked once, rule X-08); a headless command line mode | 0.1 (autostart), 0.3 (Task Scheduler, headless) |
| FR-006 | Notifications for errors, conflicts and a missing shared folder; success optional | 0.1 |
| FR-007 | Starting the game from the application | P2 |
| FR-008 | Diagnostics page, log page, backup browser, device list, settings | 0.1 (diagnostics, log, devices, settings), 0.2 (backup browser) |
| FR-009 | Usable without special knowledge; expert view for rules and exclusions | 0.1 |

### 4.2 Inventory and analysis

| ID | Requirement | Release |
|---|---|---|
| FR-010 | Find the game's configuration folder (standard edition under Documents; Steam uses the same folder and is experimental with a warning); override by hand. Several installations later | 0.1 / P2 |
| FR-011 | Find all character folders; the rules can differ per character. Character content IDs are never shown or logged (X-06); the user names the folders | 0.1 |
| FR-012 | Classify each file from the built-in matrix with its evidence level; unknown files are "on request" | 0.1 |
| FR-013, FR-017 | Device settings stay on the device by the "never" rule for `FFXIV.cfg`; no field inside a binary file is changed | 0.1 |
| FR-014 | The revision records the device's display name, the game version and the screen size and scaling, nothing more | 0.1 |
| FR-015, FR-016 | Before an apply, compare source and target device and show what each file will do, with reasons; only "always" files are pre-selected | 0.1 |
| FR-018 | Apply is refused when the game version of source and target differ | 0.1 |
| (new) | Advanced mode for `FFXIV.cfg`: compare key by key, apply picked keys (decisions 2026-11-04) | 0.2 |

### 4.3 Dalamud and plugins

| ID | Requirement | Release |
|---|---|---|
| FR-019 | Dalamud is optional; without it everything else works | 0.1 |
| FR-020, FR-024 | Back up plugin configuration files; apply per plugin on request | 0.1 (backup), 0.1 or 0.2 (apply, see 6.2) |
| FR-021 | Rules per plugin; built-in exclusions for caches, logs, lock files and large data | 0.1 |
| FR-022, FR-023 | Compare installed plugins between devices and list the missing ones; no installation, no copying of plugin binaries or repositories | 0.2 (list) |
| FR-025 | Detect credentials and absolute paths in plugin files; mark the file and warn before it is applied | 0.1 |

### 4.4 Backup

| ID | Requirement | Release |
|---|---|---|
| FR-026, FR-027 | Several independent jobs with their own source, target, triggers, exclusions and retention | 0.2 (0.1 has one sync job) |
| FR-028 | Targets: the shared folder (0.1); local folder, USB, UNC (0.2) | 0.1 / 0.2 |
| FR-029 | Export of a revision as a ZIP archive and import of such an archive | 0.2 |
| FR-030 | Manifest per revision: ID, time, device, versions, inventory with SHA-256, completion marker | 0.1 |
| FR-031 | Triggers: manual, after the game exits, at application start; schedule later | 0.1 / 0.3 |
| FR-032 | Retention by count and age; protected revisions are never removed | 0.1 (fixed default), 0.2 (settings) |
| FR-033 | USB target found by volume ID; never write to another medium | 0.2 |
| FR-034 | A missing target is reported and retried; it never blocks the game or another job | 0.1 |
| FR-035 | One local scan serves every job | 0.2 |
| FR-036 | Hashes, atomic publication, no partial revision is ever shown as complete; the backup completes locally even when the cloud is not reachable | 0.1 |
| FR-037 | Encryption | P2 |

### 4.5 Sync

| ID | Requirement | Release |
|---|---|---|
| FR-038 | Any number of devices with a stable ID and a display name | 0.1 |
| FR-039 | Each device writes only its own folder; a shared state is the set of all devices' latest revisions | 0.1 |
| FR-040 | One shared folder; further mirrors later | 0.1 / P2 |
| FR-041 | Only changed files are uploaded; change means changed content | 0.1 |
| FR-042, FR-043 | Conflict when both the source and the local copy changed since the last common state; both versions are kept, the user decides, nothing blocks the game | 0.1 |
| FR-044 | A file missing on the source is not deleted on the target; it is shown and can be removed by hand | 0.1 |
| FR-045 | Onboarding of a device: backup first, then a preview of what the other device offers | 0.1 |
| FR-046 | Applying an older own revision asks whether it should become the current state | 0.1 |
| FR-047, FR-058 | Nothing is written while the game runs (X-01); an apply waits until the game exits and is never forced | 0.1 |
| FR-048 | After the game exits: back up, then offer or run the apply | 0.1 |
| FR-049 | Upload state shown honestly: "handed to OneDrive", "uploaded" (from the client), "received by device X" (from that device's confirmation), "unknown" | 0.1 |
| FR-050 | Playing offline is always possible; backups queue locally | 0.1 |
| FR-051 | No junctions or symbolic links | 0.1 |

### 4.6 Restore and comparison

| ID | Requirement | Release |
|---|---|---|
| FR-052 | Restore of a whole revision, per character, per plugin, per file | 0.1 |
| FR-053 | A verified local snapshot of every file an apply will change is taken first (X-02); an apply can be undone in one step | 0.1 |
| FR-054 | Preview of every apply; the preview writes nothing | 0.1 |
| FR-055 | Backup browser with protect and delete | 0.2 |
| FR-056 | Compare two revisions at file level; text files key by key | 0.2 |
| FR-057 | A damaged revision is reported; files that fail their hash are never applied | 0.1 |

### 4.7 Application configuration

| ID | Requirement | Release |
|---|---|---|
| FR-059 | Export and import of settings and rules | 0.2 |
| FR-060 | Rules are separate from device paths, device ID and window settings | 0.1 |
| FR-061 | The application keeps no credentials of its own | 0.1 |
| FR-062 | Versioned settings with migration (D-10, D-11) | 0.1 |

### 4.8 Logging, robustness, security

| ID | Requirement | Release |
|---|---|---|
| FR-063 | Log with levels and rotation, one correlation ID per run; no content IDs, credentials or file contents in the log | 0.1 |
| FR-064 | Understandable messages for a missing folder, conflicts, incomplete uploads | 0.1 |
| FR-065 | The game never depends on the application | 0.1 |
| FR-066 | One writer at a time per device (a lock); a journal makes an interrupted apply recoverable; every step can be repeated | 0.1 |
| FR-067 | Hash checks, manifest validation, safe relative paths only (no `..`, no absolute paths, no links) | 0.1 |
| FR-068 | No telemetry; no sign-in | 0.1 |
| FR-069 | README, user guide, troubleshooting, changelog, third-party and trademark notes | 0.1 |
| FR-070 | Low background load: watching for the game process, no periodic full scans; targets measured in gate 3 | 0.1 |
| FR-071 | A clean-up page lists the conflict copies in the game folder with their original, date and size. Nothing is ticked by the application; the files the user ticks go to the Windows Recycle Bin after a confirmation, never deleted for good. A copy whose original is missing cannot be ticked. Each file is checked again before it goes (still a conflict copy, unchanged, original present), and nothing goes while the game runs. Added at the owner's request, 2026-11-05 | 0.1 |
| FR-072 | Dalamud's own folder (the parent of `pluginConfigs`) is backed up as a third area, top level only, without logs, crash dumps, caches and the shadow database; its files are never applied from another device. On request, the custom plugin repositories another device has and this one lacks are appended to this device's `dalamudConfig.json` after the user confirms the list; nothing else in the file changes, and it can be undone like an apply. Added at the owner's request, 2026-11-05 | 0.1 |
| FR-073 | The transfer list shows the files only this device has (proposal "only here", never selectable) when the user asks for files with nothing to do; the note says that nothing needs to be done. Owner's request, 2026-11-05 | 0.1 |
| FR-074 | The setting is XIVLauncher's folder, not the plugin settings folder; the plugin settings and Dalamud's files are found from it, also in the sandbox mode (`dalamudUserData`). Older settings are moved over; a plugin folder chosen by hand elsewhere stays in use. Owner's request, 2026-11-05 | 0.1 |
| FR-075 | Character names and, where given as text, worlds are suggested from plugin data (AutoRetainer, Lifestream, ICE, Allagan Tools, Allagan Market), matched by the content ID in the folder name, read-only; they replace only empty or numbered names, never one the user chose. Owner's request, 2026-11-05 | 0.1 |
| FR-076 | Rules per plugin on the rules page: its own apply rule (or the one for all plugin files) and "not backed up", stored as overrides and exclusions on `<Name>.*` and `<Name>/**`. Owner's request, 2026-11-05 | 0.1 |
| FR-077 | What is taken from other devices is chosen by area, not by file: hotbars and macros, key bindings and controls, gear sets, character settings, chat, HUD layout, inventory order, plugin settings, plugin sources. The application decides which files belong to which area; the user switches each area on or off per device. All are on by default. Files that belong to one device (`FFXIV.cfg`, `FFXIV_BOOT.cfg`, `cfgcopy`, Dalamud's own files) are in no area and never taken. The file and plugin rules stay as an advanced view, and a rule on a file wins over its area. Owner's request, 2026-11-06 | 0.1 |
| FR-078 | The character settings file `COMMON.DAT` belongs to the areas character settings and chat. When only one of them is on, the file is compared and taken without the settings of the other, which keep this device's values; the settings are told apart by name only (`Log*`, `IsLog*`, `ChatType` are chat), never by position. A file of another shape is not split and only offered whole. Owner's request, 2026-11-06 | 0.1 |
| FR-079 | For the areas the game rewrites by itself (HUD layout, inventory order), a file both devices changed since a shared state is decided by which state was backed up later, without a question. Without a shared state this never applies. Owner's request, 2026-11-06 | 0.1 |
| FR-080 | The first transfer with a device, when files differ and no shared state is known, asks once for all of them: take the other device's settings, keep this device's, or decide later on the transfer page. Owner's request, 2026-11-06 | 0.1 |
| FR-081 | With automatic transfer on, the areas switched on and the missing plugin sources are taken without asking, with a snapshot first; the notification names the areas, and the transfer page can undo it. The transfer list shows each file's area. Owner's request, 2026-11-06 | 0.1 |

## 5. Releases and priorities

| Release | Content |
|---|---|
| 0.1 "Sync" | One shared folder, any number of devices, back up everything (with cache exclusions), apply by rule with preview, snapshot and undo, conflicts kept, honest upload state, tray, autostart asked once, DE/EN, portable single executable |
| 0.2 "Backup" | ZIP export and import, local, USB and UNC targets, several jobs, backup browser, retention settings, comparison, advanced mode for `FFXIV.cfg`, list of missing plugins, settings export |
| 0.3 "Comfort" | Task Scheduler, headless mode, installer (per-user, no administrator), schedules |
| P2 | Starting the game, encryption, mirrors, several installations, plugin installation, automatic updates |

Priority inside a release follows the project package: safety (X-01 to X-03, conflicts, never
blocking the game) before function, function before comfort.

## 6. Decisions this specification asks for

### 6.1 Credentials in backed-up files

A plugin on device A stores an API key in plain text, and `FFXIV.cfg` holds a session ID.
Proposal: such files are backed up like any other, since the shared folder is the user's own
storage; they are marked in the revision, shown with a warning, never written to the log, and the
user can exclude them with one click. Alternative: exclude them by default.

### 6.2 Applying plugin settings in 0.1

Whether Dalamud prefers a valid settings file written from outside over its shadow copy is not
tested. Proposal: test it in the gate 3 prototype; apply of plugin settings ships in 0.1 only if
that test passes, otherwise in 0.2. Backup of plugin settings ships in 0.1 either way.

### 6.3 Character files not yet confirmed

`MACRO.DAT`, `GEARSET.DAT`, `COMMON.DAT` and `CONTROL1.DAT` stay "on request" until one transfer
each has been seen in the game; that check is part of gate 4 (two real devices).

Changed on 2026-11-06 by the owner's request for areas (FR-077): these files, the plugin settings
(6.2) and the HUD layout are taken automatically with their area. Every transfer keeps a verified
snapshot and can be undone. The checks in the game stay part of gate 4; a file that fails one is
taken out of its area before release.
