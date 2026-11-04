# Data model: XIV ConfigSync

Status: gate 2 draft, 2026-11-04. Every stored format carries a `schema` number from its first
version, so a later release can read and migrate it (D-10, D-11). All JSON is UTF-8 without a
byte-order mark. Times are UTC in ISO 8601. Hashes are lower-case hexadecimal SHA-256.

## 1. Shared folder

```
<chosen folder>\XIV ConfigSync\
  store.json                          created once; schema of the layout
  devices\
    <deviceId>\
      device.json                     display name, created, last seen
      objects\<h0h1>\<hash>           file contents, named by hash, never changed
      revisions\<revisionId>.json     one manifest per revision, never changed
      HEAD                            the ID of the latest complete revision, replaced last
      received\<otherDeviceId>.json   which revision of that device this device has verified
```

Only the device named in the path writes below `devices\<deviceId>\`. `<h0h1>` is the first two
hex digits of the hash, to keep folders small.

### store.json

```json
{ "schema": 1, "created": "2026-11-04T20:00:00Z" }
```

### device.json

```json
{ "schema": 1, "deviceId": "3f2a...", "displayName": "Laptop", "created": "...", "lastSeen": "..." }
```

`deviceId` is a random 128-bit value made at first start and kept in the local settings. It says
nothing about the hardware.

### Revision manifest (`revisions\<revisionId>.json`)

```json
{
  "schema": 1,
  "revisionId": "20261104T201500Z-3f2a",
  "deviceId": "3f2a...",
  "created": "2026-11-04T20:15:00Z",
  "parent": "20261104T180300Z-3f2a",
  "origin": "game-exit",
  "appVersion": "0.1.0",
  "gameVersion": "...",
  "display": { "width": 2560, "height": 1600, "scalePercent": 150 },
  "roots": {
    "game": { "files": [ { "path": "FFXIV_CHR.../HOTBAR.DAT", "hash": "...", "size": 204832, "flags": [] } ] },
    "plugins": { "files": [ { "path": "SomePlugin.json", "hash": "...", "size": 1063, "flags": ["credential"] } ] }
  },
  "excluded": [ { "root": "plugins", "path": "SomePlugin/cache", "reason": "cache rule" } ]
}
```

- `origin`: `game-exit`, `manual`, `app-start`, `before-apply` (the backup taken right after an
  apply).
- `path` is relative to its root, with `/` as separator, validated on read: no empty segment, no
  `.` or `..`, no drive or root, no characters Windows forbids. A manifest with one invalid path
  is rejected as a whole.
- `flags`: `credential` (a credential pattern was found), `absolute-path` (the file names a path
  on this device), `large` (above the warning size).
- Character folder names contain the character's content ID. They stay in manifests because the
  files must be restored to the same folder; they are never shown or logged (X-06). The interface
  shows a name the user gives each character folder.

### HEAD

A text file with one revision ID. Written to a temporary file and renamed over the old one. A
reader that finds a manifest newer than `HEAD` ignores it: that revision was not completed.

### Received marker (`received\<otherDeviceId>.json`)

```json
{ "schema": 1, "revisionId": "20261104T201500Z-3f2a", "verified": "2026-11-04T20:20:11Z" }
```

Written by the receiving device after every object of that revision passed its hash check.

## 2. Local data (`%LOCALAPPDATA%\XIVConfigSync\`)

```
settings.json        application settings and this device's identity
rules.json           the user's rule overrides
bases\<sourceDeviceId>.json   per file: the hash both sides shared at the last apply or backup
snapshots\<snapshotId>\       files saved before an apply, with snapshot.json
journal.json         present only while an apply runs
logs\                rotated log files
```

### settings.json

```json
{
  "schema": 1,
  "deviceId": "3f2a...",
  "displayName": "Laptop",
  "sharedFolder": "C:\\Users\\...\\OneDrive\\Games",
  "gameConfigFolder": null,
  "pluginConfigFolder": null,
  "characterNames": { "FFXIV_CHR...": "Main" },
  "language": null,
  "closeAction": null,
  "autostart": false,
  "backupOnGameExit": null
}
```

`null` means "not chosen yet": the default applies, and for `closeAction` and
`backupOnGameExit` the user is asked once (X-08). Folders are `null` while discovery finds them.

### rules.json

```json
{
  "schema": 1,
  "overrides": [
    { "root": "game", "pattern": "FFXIV_CHR*/ADDON.DAT", "rule": "always" },
    { "root": "plugins", "pattern": "SomePlugin.json", "rule": "never" }
  ],
  "exclusions": [ { "root": "plugins", "pattern": "SomePlugin/cache/**" } ]
}
```

The built-in matrix ships with the application and is not stored here; an override wins over it,
and the most specific pattern wins among overrides.

### Base table (`bases\<sourceDeviceId>.json`)

```json
{ "schema": 1, "files": { "game/FFXIV_CHR.../HOTBAR.DAT": "...hash..." } }
```

Updated per file when an apply from that device succeeds and when a backup shows both sides equal.
It is what makes a three-way comparison possible without trusting write times.

### Snapshot (`snapshots\<snapshotId>\snapshot.json`)

```json
{
  "schema": 1,
  "snapshotId": "20261104T210000Z",
  "reason": "apply",
  "files": [ { "root": "game", "path": "...", "hash": "...", "existed": true } ],
  "complete": true
}
```

The copied files lie next to it under their relative paths. `existed: false` records a file the
apply will create, so an undo removes it. Snapshots are kept for the last 20 applies and at least
30 days.

### Journal (`journal.json`)

```json
{
  "schema": 1,
  "applyId": "...",
  "snapshotId": "20261104T210000Z",
  "source": { "deviceId": "...", "revisionId": "..." },
  "steps": [ { "root": "game", "path": "...", "hash": "...", "done": false } ]
}
```

Written before the first file is replaced, updated after each, deleted after the final check.

## 3. In memory (Core)

| Type | Holds |
|---|---|
| `Device` | ID, display name, last revision, received markers |
| `Revision` | the manifest above |
| `ConfigFile` | root, relative path, hash, size, flags, matrix entry |
| `MatrixEntry` | pattern, class (shared, device, mixed, cache, sensitive, ignore), default rule, evidence level, note |
| `Rule` | `Always`, `OnRequest`, `Never` |
| `PlanItem` | file, source hash, local hash, base hash, action (`Apply`, `Offer`, `Conflict`, `Skip`), reason |
| `ApplyPlan` | source revision, items, refusal reasons |
| `UploadState` | `HandedOver`, `Uploaded`, `ReceivedBy(device)`, `Unknown` |

## 4. Retention

A device keeps its last 50 revisions and every revision another device has marked as received
last. Objects no kept revision refers to are deleted by the owning device only. Protected
revisions (0.2) are never removed.
