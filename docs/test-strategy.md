# Test strategy: XIV ConfigSync

Status: gate 2 draft, 2026-11-04.

## 1. Levels

| Level | What | Where it runs |
|---|---|---|
| Unit | Core logic: rules and matrix lookup, path validation, manifest reading and writing, change detection, the apply planner's three-way table, conflict detection, retention, settings migration, credential patterns | `dotnet test`, local and CI |
| Integration | Whole flows on generated folders: a fake game folder, a fake plugin folder and a fake shared folder for two or three simulated devices, all under a temporary directory | `dotnet test`, local and CI |
| Fault injection | An apply interrupted after each step (through a test seam in the applier), a hash mismatch, a truncated object, a missing `HEAD`, an invalid path in a manifest, a locked file, a full disk simulated by the file system adapter | `dotnet test` |
| Platform | The adapters against real Windows: atomic replace, process watch, OneDrive state | local only, by hand with a checklist; a few automated where they need no OneDrive |
| End to end | Two real devices (A 16:10, B 16:9), real game, real OneDrive | by the owner before each release, with a written checklist |
| Interface | Keyboard only, Narrator, 150 and 200 percent, both languages (WPF profile) | by hand before each release |

Integration tests never touch a real game or plugin folder, and the test fixtures contain no real
game data (X-07, X-09). Binary fixtures are random bytes with the right file names: the product
handles files as opaque blobs, so their content does not matter to it.

## 2. Gate 3 prototype: what it must prove

The project package names four critical paths for gate 3. With the findings of gate 1 they are:

1. **Store and revision publication:** objects, manifest, `HEAD` written last; a reader never sees
   a half-written revision. Includes a crash after every step.
2. **Apply with snapshot, journal and undo:** every interruption point recovers to either the old
   or the new state, never a mix; undo restores the hashes of the snapshot exactly.
3. **Conflict model:** the planner's table in `architecture.md` for all combinations, including
   the cases the game creates by itself (V00).
4. **OneDrive state from C#:** reading `System.StorageProviderState` for a file without opening
   it, on OneDrive for Business and, if available, a personal OneDrive.

And three facts gate 1 left open, each answered by a small measurement:

5. Where the game version can be read without touching the game (a file in the installation).
6. Whether Dalamud keeps a valid plugin settings file written from outside, or restores its shadow
   copy (decides `specification.md` 6.2). Run on a copy of a single plugin's settings, with a
   backup first and the owner's consent.
7. Detecting the game process start and stop without polling, and its cost in the background.

## 3. Test cases from the project package

| ID | Case | Covered by | Release |
|---|---|---|---|
| T01 | Fresh profile without the game: diagnostics explain, no crash | integration | 0.1 |
| T02 | Game without Dalamud | integration, end to end | 0.1 |
| T03 | Launcher detection and starting the game | dropped with FR-007 | P2 |
| T04 | All or selected characters | integration | 0.1 |
| T05 | 16:10 to 16:9 with different UI scale: the target's scale stays | integration (rule "never" for `FFXIV.cfg`), end to end | 0.1 |
| T06 | HUD with mixed data: no partial change, warning | integration (rule "on request"), end to end | 0.1 |
| T07 | ZIP backup with manifest and hashes | integration | 0.2 |
| T08 | Damaged archive or object: no silent partial restore | fault injection | 0.1 (objects), 0.2 (ZIP) |
| T09 | Apply while the game runs: nothing written, the game not disturbed | integration with a fake process watch, end to end | 0.1 |
| T10 | OneDrive offline: the job waits, the game starts | integration (state "handed over"); the real offline case is not tested, owner's decision of 2026-11-04 | 0.1 |
| T11 | Written locally, cloud state unknown: no claim of a finished upload | integration with a fake state adapter, platform | 0.1 |
| T12 | Conflict on two devices: both versions kept, resolved in the interface | integration, end to end | 0.1 |
| T13 | External drive missing | integration | 0.2 |
| T14 | Changed USB drive letter | platform | 0.2 |
| T15 | Two jobs at the same time: one scan, no competing writes | integration | 0.2 |
| T16 | Task Scheduler and tray at once: no double writer | integration (device lock) | 0.1 (lock), 0.3 (scheduler) |
| T17 | Restoring an old backup asks before it becomes current | integration | 0.1 |
| T18 | Unknown plugin: backed up, applied only on request | integration | 0.1 |
| T19 | Settings export and import do not overwrite device paths | integration | 0.2 |
| T20 | Portable and installer behave alike, both languages, clean uninstall | by hand | 0.1 (portable), 0.3 (installer) |
| T21 | Foundation compliance and public repository audit | CI checks, by hand before release | every release |

## 4. Acceptance from the project package

| ID | Criterion | Proven by |
|---|---|---|
| A01 | Full backup and selective restore with rollback on real test copies | gate 4, end to end |
| A02 | Two devices 16:10 and 16:9: UI scale and HUD not overwritten by accident | gate 4, end to end; the HUD case is "on request" by design |
| A03 | Macros, gear sets, plugin settings transferred only where safe, and correctly | gate 4, one transfer per file kind seen in the game |
| A04 | Conflict with changes on both devices: both versions kept | gate 4 |
| A05 | Offline or OneDrive error: playing possible, state visible, retry later | gate 4, as far as it can be produced without disconnecting the network |
| A06 | Several jobs, media, ZIP, retention without wrong deletions | 0.2 |
| A07 | Installer, portable, tray, scheduled runs, DE/EN | 0.1 (portable, tray, DE/EN), 0.3 (rest) |
| A08 | Tests, documentation, code and foundation compliance checked, nothing confidential published | gate 5 |

## 5. Gates in the build

From gate 3 on, `build.ps1` and CI run, and every one must pass: `dotnet format whitespace
--verify-no-changes`, the build with warnings as errors at `latest-recommended`, `dotnet test`
(no tests found counts as a failure), XamlStyler in passive mode, `dotnet list package
--vulnerable --include-transitive`, the existing PowerShell checks, and the hooks. A test that is
not run in CI (platform, end to end, interface) is listed in the release checklist with its result,
so nothing looks automated that is not.
