# Architecture: XIV ConfigSync

Status: implemented for release 0.1 (gates 3 and 4, 2026-11-04); the test on two real devices is
still open. What the product does is in `specification.md`; the formats are in `data-model.md`.

## Overview

```
 Game and plugin folders           Sync folder (OneDrive), chosen once
 on this device                    <name>.xivsync
 +-------------------+  back up    +-------------------------------+
 | FFXIV config      | ----------> | devices\<this device>\        |  written only by this device
 | pluginConfigs     |             |   objects, revisions, HEAD    |
 +-------------------+             |   received\<other device>     |
        ^                          | devices\<other device>\       |  read only here
        |  apply by rule           |   objects, revisions, HEAD    |
        +--------------------------+-------------------------------+
        |
 Local data (%LOCALAPPDATA%\XIVConfigSync): settings, rules, snapshots, journal, log
```

## Principles

- **One writer per folder.** A device writes only into its own folder in the shared folder and
  reads the others. No file in the shared folder is ever written by two devices, so the sync
  client never has to merge, and no conflict copies arise. Basis from gate 1: the conflict copies
  found on device A came from two devices that synced one game folder directly.
- **Immutable, content-addressed objects.** A file's content is stored once under its SHA-256 and
  never changed. A revision is a manifest that lists paths and hashes. A revision becomes visible
  only when the `HEAD` pointer is replaced last; a revision without it is incomplete and ignored.
- **Whole files only** (X-03). The product never changes a field inside a game file. The one
  exception planned is the advanced mode for `FFXIV.cfg` (0.2), a plain text file in a known
  `key<TAB>value` form.
- **The game is never touched while it runs** (X-01), and never waits for the product.
- **Every apply can be undone** (X-02): a verified local snapshot comes first, a journal makes an
  interrupted apply recoverable.

## Components

| Project | Target | Holds |
|---|---|---|
| `XivConfigSync.Core` | `net10.0` | Domain model, file matrix and rules, change detection, apply planning, conflict detection, manifest reading and writing, path safety, view models. No Windows and no WPF types |
| `XivConfigSync.Platform` | `net10.0-windows` | Thin adapters behind interfaces from Core: file system with atomic replace, process watch, OneDrive state (Windows property system), known folders, autostart entry |
| `XivConfigSync.App` | `net10.0-windows`, WPF | Views, tray, start-up, the three exception handlers, language choice |
| `XivConfigSync.Core.Tests` | `net10.0` | xUnit v3 tests of Core, including integration tests on generated fixtures |

The split follows the C# and WPF profiles: logic and view models in a project the compiler keeps
free of WPF, everything Windows-specific in pass-through adapters. CommunityToolkit.Mvvm is the
only planned runtime dependency.

Core is organised by the modules of the project package: Discovery (folders, characters, game
version; where the game version is read from is unconfirmed and part of the gate 3 prototype),
Inventory (scan and hash), Store (objects, revisions, `HEAD`, received markers),
Rules (matrix, user rules), Planner (apply plan and conflicts), Applier (snapshot, journal,
replace, undo), State (upload and receipt status), Settings.

## Flows

### Setting up a device

The user chooses one folder, the sync folder, and nothing else. Everything inside it is managed
by the application; the user never picks a device folder.

1. **First device:** the user chooses or creates a folder, for example `OneDrive\FFXIV Sync`. The
   application writes the sync file `<folder name>.xivsync` there, which marks the folder as a sync folder of this
   application, then creates its own device folder and makes the first backup.
2. **Every further device:** the user chooses the same folder. The application finds
   the `.xivsync` file, lists the devices it finds below `devices\` with their display names and last
   backups, and asks one question: is this a new device, or one of the listed devices set up
   again (for example after Windows was reinstalled)?
   - New device: the application creates its device folder, backs up this device first, then
     shows the preview of what the other devices offer (FR-045).
   - Set up again: the application takes over that device's identity and folder, backs up the
     current state as a new revision of it, and offers to restore an older one.
3. A folder that holds files but no `.xivsync` file, or more than one, is refused, so the application never writes into
   an unrelated folder.

No file in the sync folder lists the devices. The device list is the set of `device.json` files,
each written by its own device. A single list that every device updates would be written by two
devices at the same time sooner or later, and OneDrive would answer with a conflict copy; this is
the problem the layout exists to avoid. The `.xivsync` file is written once, by the first device, and
never changed.

### Opening the sync file

The extension `.xivsync` is registered for the current user (`HKCU\Software\Classes`, no
administrator rights). A double-click on the sync file opens the application on that sync folder:
on a device that is already set up there, it shows that folder's state and revisions; on a device
that is not, it starts the set-up with the folder already chosen. The portable executable
registers the extension only after asking once (X-08) and removes it when the user turns it off;
the installer (0.3) registers it at install and removes it at uninstall. The file carries an icon
of the application's own (X-09).
### Back up

1. Trigger: the game process ends, the user presses "back up now", or the application starts.
   Not while the game runs.
2. Take the device lock (one writer per device: tray, window and a later headless run share it).
3. Scan the configured folders: path, size, SHA-256. Exclusion rules drop caches, logs and lock
   files; files above the size limit are reported.
4. Compare with the last revision of this device. If nothing changed, stop.
5. Copy each new content into `objects\` through a temporary name, verify its hash, rename.
6. Write the revision manifest, then replace `HEAD` with the new revision ID (temporary file,
   then rename).
7. Report: "handed to OneDrive". The upload state follows from the OneDrive client (see below).

A file that changes while it is read (hash differs between two reads) is read again; after three
attempts the backup stops without publishing anything.

### Apply

1. The user picks a source: the latest revision of another device (the normal case) or an older
   revision of any device.
2. Planner: for every file in the source and on this device, take its rule and compare three
   hashes: the source's, the local file's, and the base (the hash both had when this device last
   applied or backed up that file).

   | Source vs local | What both last shared | Rule "always" | Rule "on request" |
   |---|---|---|---|
   | equal | any | nothing to do | nothing to do |
   | different | the source's state (only the local copy changed) | offer | offer |
   | different | the local state (only the source changed) | apply | offer |
   | different | neither, or unknown (first transfer) | conflict | conflict |
   | missing locally | any | apply | offer |

   "What both last shared" comes from the base table and, where it has no entry, from the history
   of both devices' revisions: a local file equal to an older state of the source means only the
   source changed; a source file equal to an older local state means only the local copy changed.
   "Never" files are not offered from another device. A conflict keeps both versions: the local
   file stays, the source version stays in the store, and the user chooses. Only "apply" is ever
   done without the user ticking it. No last-writer-wins.
3. Checks that refuse the whole apply: the game runs (the apply waits instead), the game version
   differs, a source object fails its hash, the source revision is incomplete.
4. Preview: every file with its action and reason. The preview writes nothing.
5. Snapshot: copy every file that will be replaced into a new local snapshot, verify the hashes.
6. Journal: record the plan, then replace file by file through a temporary file in the same
   folder and an atomic rename; mark each file done in the journal.
7. Verify the applied hashes, close the journal, back up the new local state.

At start, an open journal means an interrupted apply: the application finishes it or rolls it
back from the snapshot, after asking. "Undo last apply" restores the snapshot the same way.

### Upload and receipt state

- **Handed to OneDrive:** the revision is written locally.
- **Uploaded:** every file of the revision reports the in-sync state through the shell property
  `System.StorageProviderState` (measured in gate 1, see `status.md`: 2 in sync, 4 new and
  pending, 10 changed and pending). Read without opening the files. Reading it from C# is
  unconfirmed and part of the gate 3 prototype.
- **Received by device X:** device X has written `received\<this device>` with this revision's ID
  into its own folder after it read and verified the revision.
- **Unknown:** the folder is not a OneDrive folder, or the property cannot be read.

### Game state

The application watches for the game process (`ffxiv_dx11.exe`) with a process start and stop
notification, not by polling the file system. A running game defers every write to the game's and
the plugins' folders.

## Decisions taken in this architecture

| Topic | Choice | Rejected and why |
|---|---|---|
| Framework | C# on .NET 10, WPF, CommunityToolkit.Mvvm | WinUI 3 and Avalonia: no foundation profile (M-13), no gain for this application |
| Metadata | JSON files in the shared folder and in the local data folder | SQLite: a database file in a OneDrive folder is a known source of conflict copies and cannot be written by two devices; JSON is readable by hand when something goes wrong |
| Shared layout | One folder per device, content-addressed objects, `HEAD` written last | One shared folder all devices write: needs merging by OneDrive, which produced the conflict copies of 2024 |
| Change detection | SHA-256 of the content | Write times: the game rewrites unchanged files on every login (V00) |
| Conflicts | Three-way comparison against the last common hash per file | Last writer wins: forbidden by the project package |
| Deletions | Never propagated automatically | Tombstones that delete on the other device: a deleted character folder is rare and a wrong delete is not undoable from the other side |
| Objects | Stored uncompressed, one file per content | Compressed or packed: a plain file can be checked and copied by hand, and the game folder of device A holds only 157 files (without screenshots and logs) |
| Packaging | Portable single executable (WPF profile settings) in 0.1; per-user installer in 0.3 | MSIX: needs a signing certificate |
| Elevation | `asInvoker`; nothing needs administrator rights | Not applicable |

## Deploy and update mechanism

0.1 ships as one self-contained executable for `win-x64`, compressed, published as a GitHub
release. No automatic update; the application may show that a newer release exists only if the
user turns that on (X-08), and that is not part of 0.1.
