# Decisions: XIV ConfigSync

Append-only. Never edit or delete past entries; a later entry may supersede an earlier one.

## Log

- (2026-11-03) Licence GPL-3.0-or-later. Rationale: a modified copy has to stay open, so nobody can
  rename the tool and sell it closed. AGPL was rejected because there is no network service for its
  extra clause to cover. MIT was rejected because it allows exactly that closed repackaging.
- (2026-11-03) The agent entrypoint file is kept out of this repository and exists only locally.
  Rationale: it points to rules in a private repository that outside contributors cannot read, so
  in a public repository it would send them to a dead end. This departs from the default layout,
  which places that file at the root.
- (2026-11-03) The enforcement configuration and the CI workflows are committed. Rationale: CI needs
  them, they contain no internal text, and CI checks only the owner's own commits, so outside
  contributors are not bound by the owner's commit rules.
- (2026-11-03) Commits are made as Sanaka; the initial commit is rewritten to that identity before
  further work is pushed. Rationale: private project.
- (2026-11-03) Sync comes before backup. Release 0.1 is the sync with a snapshot before every apply;
  a separate backup export follows in 0.2. Rationale: sync is the owner's main need, and immutable
  revisions plus the pre-apply snapshot already cover rollback.
- (2026-11-03) The in-game server backup of settings is not an alternative. Rationale: it is a
  one-time transfer for a change of PC, does not support two PCs in parallel and does not cover
  plugins.
- (2026-11-03) Standalone Windows application, not a Dalamud plugin. Dalamud matters only as the
  owner of plugin configuration files that are synced.
- (2026-11-03) The standard (non-Steam) game edition is the first target; Steam follows later.
- (2026-11-03) Development tools Pester 6.2.0 and PSScriptAnalyzer 1.25.0, pinned with SHA-512 and
  restored into the ignored `.modules` folder. Approved by the owner.
- (2026-11-03) PSScriptAnalyzer runs a warm-up pass whose results are discarded, in `build.ps1` and
  in the copied CI workflow. Rationale: on the first analyses in a fresh process the rule
  UseCorrectCasing in 1.25.0 intermittently throws a NullReferenceException from
  `CommandInfo.Parameters` (in 6 of 9 fresh processes, measured). After one pass it did not recur
  in 162 analyses over 6 processes. The strict pass that follows still fails on any analyzer error.
  The CI workflow therefore differs from its template in this one step.
- (2026-11-04) `Export-Inventory.ps1` also copies the inventoried folder's top-level `*.cfg` files
  into a folder next to the inventory, unless `-NoConfigSnapshot` is given. Rationale: an
  inventory holds hashes only, so it shows that `FFXIV.cfg` changed but not which setting. The copy
  holds a last-login value and a session ID, so like the inventories it is written only below the
  ignored `out/` folder and never committed. Rejected: storing the values inside the inventory
  JSON, which would have changed its schema for one file; asking the owner to copy the file by
  hand at every step, which is easy to forget mid-experiment.
- (2026-11-04) The OneDrive experiment runs in a folder of its own inside the owner's OneDrive for
  Business, in its `Games` folder, as the owner offered. No game data goes there. Rejected: waiting
  for a personal OneDrive, which device A does not have configured.
- (2026-11-04) `dalamudConfig.json` is device-specific in release 0.1. Rationale: Dalamud's source
  code refers to plugins in profiles by an ID made per installation, so a copied profile does not
  match the other device's plugins, and a plugin in no profile is added to the default one.
- (2026-11-04) The Steam edition is supported only as experimental, with a warning shown to the
  user. Rationale: the owner cannot test it. The configuration folder is the same as for the
  standard edition (launcher source code), so support costs little, but it is unverified.
  This narrows the earlier entry that Steam "follows later".
- (2026-11-04) `FFXIV.cfg` is never synced as a whole. An advanced mode, off by default, may compare
  it key by key with the other device's copy, show the differences, and let the user pick single
  keys to take over. Rationale: the file mixes device settings with settings a player wants
  everywhere, and the owner had to set `UiHighScale` by hand on the second device to make the game
  playable, so a silent transfer of any display key would break it. The release is decided in
  gate 2. A picked key is written into a text file the game owns, so the pre-apply snapshot covers
  it like any other apply. Rejected: syncing a fixed allow-list without asking, because which keys a
  player wants shared differs from player to player.
- (2026-11-04) The file matrix is kept as a public draft in `docs/file-matrix.md`, with how well
  each row is known. Device names, character IDs and values from real files stay out of it.
- (2026-11-04) Correction to the entry on the advanced mode for `FFXIV.cfg`: with the first device's
  `UiHighScale` the game stayed usable on the second device, but every HUD element was shown far too
  large; the owner lowered the value by hand to get the right size. The decision stands: the value
  is device-specific, and a silent transfer would spoil the HUD on the other device, not break the
  game.
- (2026-11-04) The PSScriptAnalyzer warm-up in `build.ps1` and in the CI workflow also runs the
  `-SuppressedOnly` analysis, and analyzer errors raised by that pass are now collected and reported
  like those of the main pass instead of aborting the step. Rationale: that pass failed once in CI
  with a `RULE_ERROR` after the plain warm-up. Chosen by the owner. Rejected: retrying a file once
  and failing only if the error persists, which would soften the rule that any analyzer error
  fails; moving to a newer release, which needs a new pinned dependency.
- (2026-11-04) PSScriptAnalyzer stays at the pinned 1.25.0. In `build.ps1` and in the CI workflow, a
  file whose only analyzer errors are "The term 'Get-Command' is not recognized" is analysed once
  more, and the second result counts; any other analyzer error, or the same one again, still fails
  the gate. Rationale: in eight sequential CI runs each, 1.24.0 failed 3 and 1.25.0 failed 6, nearly
  all with this error, so the error is not specific to one release and a version change does not
  end it; it has never appeared locally. Chosen by the owner. This narrows the earlier rejection of
  a retry: it covers one known error text only. Rejected: pinning 1.24.0, which departs from the
  language profile without ending the failures; retrying on any analyzer error, which would hide a
  crashed rule.
- (2026-11-04) Supersedes the entry on the one-time retry in the same process, which did not help:
  in CI the "Get-Command is not recognized" fault, once it appears, spoils every later analysis in
  the same process. PSScriptAnalyzer now runs through `Invoke-ProjectAnalyzer.ps1`, used by both
  `build.ps1` and the CI workflow. It analyses in a separate Windows PowerShell process that
  reports one result per file as it goes. On that one fault, the affected file and every file
  after it are analysed again in a fresh process, while the results before it are kept. A process
  that crashes or exceeds its time limit is handled the same way. Any other analyzer error, an
  analyzer that cannot be loaded, or a file that needs more than three processes fails the gate at
  once. Tests play each of these cases with a fake worker and run the real analyzer twice. The CI
  job and the analyzer step also carry a time limit. Chosen by the owner, who asked for reliable
  runs that catch every failure clearly. Rejected: a process per file from the start, which would
  load the analyzer eleven times on every run; letting the analyzer step only warn in CI, which
  would give up the check there.
- (2026-11-04) Rule X-09: no material from the game in the product or the repository, and no
  "FINAL FANTASY" in the product name. Rationale: Square Enix's materials usage policy for the game
  covers the non-commercial use of its texts, images, video and music and does not mention
  software, so a tool that uses none of them stays outside it; the disclaimer in the README stays.
  The name "XIV ConfigSync" is kept for now; none of the terms read forbids it, and whether the
  owner wants a neutral name before the first release is decided in gate 2.
- (2026-11-04) Every analyzer worker gets a module analysis cache of its own
  (`PSModuleAnalysisCachePath`, honoured by Windows PowerShell 5.1, measured). Rationale: in the
  first CI run that met the "Get-Command is not recognized" fault after the change to separate
  workers, the fault came back in all four fresh workers of that job, always on the same file. So
  it lives in state the processes of one job share, not in one process, and the module analysis
  cache under `%LOCALAPPDATA%` is the shared state that command discovery reads. Whether it is the
  cause is not proven; the next runs show it. The same run showed that the attempts were counted
  from the first worker, so a file could get one worker more than the limit; they are now counted
  per file.
- (2026-11-04) Narrows the entry before: only the workers after a failure get a module analysis
  cache of their own; the first keeps the one the machine shares. Rationale: the next CI run
  showed that the runner image itself points `PSModuleAnalysisCachePath` at one pre-built cache
  for the whole machine, which fits the shared state the fault lives in, and that an empty cache
  costs about 20 s per worker start there, which made the analyzer step 64 s instead of 17 s and
  let the hang test run out of time. The worker now also loads the two base modules of Windows
  PowerShell by path before anything else, so its own commands need no command discovery; measured
  locally against 300 large modules with an empty cache, a worker started in 0.3 s instead of
  1.2 s. The hang test allows 10 s instead of 3 s.
- (2026-11-04) The owner drops two feasibility tests: the transfer in the other direction (A to B)
  and the OneDrive tests offline and with two writing devices. Rationale, the owner's: the transfer
  copies whole files, so a direction that works works the other way too; and offline behaviour and
  concurrent writes are OneDrive's job, a cloud made for this, not the program's. Conflicts between
  two devices are handled in gate 2 by design (each device writes only its own folder), not by test.
- (2026-11-04) The owner drops the environment report and baseline inventory of device B.
  Rationale: the game files of device B are at hand from two uploads, and its screen size is
  known; nothing in the feasibility question depends on more.
- (2026-11-04) Gate 1 is closed: feasible. Proposed scope for 0.1, for the owner's decision: whole
  files only; files the game does not change by itself (shared macros, hotbars, key bindings,
  mouse and keyboard settings) are synced when their content changes; the HUD layout, which the
  game changes on every login, is sent only on the user's request; `UISAVE.DAT`, the item order
  and plugin settings stay out of 0.1. Rejected: syncing on a changed write time, since the game
  rewrites unchanged files on every login (experiment V00); syncing the HUD automatically, since a
  change the game made by itself would overwrite a deliberate change from the other device.
- (2026-11-04) Replaces the proposed scope for 0.1 in the entry before. Backing up and applying
  are separate. Every device backs up all of its configuration files, game and plugins, as whole
  files, into its own folder in the cloud. Applying a backup, on the same device or from the other
  one, follows a rule per file that the user can change: "always" (the shared files confirmed by
  the transfer test; further character files once they are confirmed), "on request" (HUD layout,
  `UISAVE.DAT`, item order, plugin settings) and "never" (device files such as `FFXIV.cfg` as a
  whole, `FFXIV_BOOT.cfg`, `cfgcopy`, `dalamudConfig.json`, launcher files). Rationale, the
  owner's: a backup of everything costs nothing and keeps every option; what is safe to carry
  across devices is a question for applying, and the user should be able to define it. Rejected:
  leaving files out of the backup because they may not be applied. Open for gate 2: how the
  backup handles credentials found in files, and which plugin caches are excluded by size.
- (2026-11-04) Both a work and a personal OneDrive are supported; the sync only needs a folder.
  The upload state was measured only on OneDrive for Business and is checked on a personal one in
  gate 2.
- (2026-11-04) The owner accepts the framework recommendation (C# on .NET 10 with WPF) and keeps
  the name "XIV ConfigSync"; this settles the name question left for gate 2 above.
- (2026-11-04) The owner approves gate 2: the revised specification, architecture, data model,
  test strategy and priorities. Gate 2 produces documents only; product code starts with the
  gate 3 prototype after a further approval.
- (2026-11-04) Gate 2 draft: `specification.md`, `architecture.md`, `data-model.md` and
  `test-strategy.md`. The decisions in them, each with what was rejected: metadata as JSON files,
  not SQLite (a database file in a synced folder breaks when two devices touch it); objects stored
  uncompressed and named by their hash; a three-way comparison against the last common hash per
  file decides between apply, offer and conflict; deletions are never carried to another device
  automatically; the game version must match for an apply; a portable executable in 0.1 and a
  per-user installer in 0.3. Open for the owner: credentials in backed-up files
  (`specification.md` 6.1) and whether applying plugin settings ships in 0.1 (6.2).
- (2026-11-04) One sync folder for all devices, as the owner asked: the user chooses a single
  folder on every device, and the application recognises it by a marker file (`store.json`),
  lists the devices already in it and creates the part for a new device by itself, or takes over
  an existing device's part when a device is set up again. The user never chooses a folder per
  device. The per-device parts inside it stay, because they keep every file written by one device
  only. Rejected: a configuration file in the sync folder that lists the devices and is updated by
  each of them, which OneDrive would turn into conflict copies when two devices write it; the list
  is read from each device's own `device.json` instead. Also confirmed by the owner: everything is
  backed up, device settings included, so that a finer choice of what to apply can come later
  without a new backup.
- (2026-11-04) The marker file of the sync folder gets its own extension, `.xivsync`, named after
  the folder (`FFXIV Sync.xivsync`), and replaces the `store.json` of the entry before. The owner's
  idea: like a project file of a backup program, a double-click opens the application on that
  sync folder, and on a device not yet set up it starts the set-up with the folder chosen. The
  extension is registered per user without administrator rights, only after asking once in the
  portable executable (X-08), by the installer from 0.3. Rejected: a fixed name such as
  `store.json`, which Windows opens in an editor. Before release the extension is checked against
  known uses so that it does not take over another program's files.
- (2026-11-04) The owner accepts the two proposals left open in `specification.md`. Credentials:
  files holding them are backed up like any other, marked in the revision, shown with a warning,
  never written to the log, and can be excluded with one click; rejected: excluding them by
  default, since the sync folder is the owner's own storage. Plugin settings: backed up in 0.1;
  applying them ships in 0.1 only if the gate 3 test shows that Dalamud keeps a valid file written
  from outside, otherwise in 0.2.
- (2026-11-04) The owner approves gate 3 (prototype of the critical paths) and gate 4 (core
  functions, as far as they can be built without the owner) to be worked on in one run, locally,
  with nothing pushed until the owner has reviewed the gate reports. The real two-device test of
  gate 4, the Dalamud test with a running game, and gate 5 stay with the owner. No real game or
  plugin folder is written during this run; only generated fixtures and copies are used.
- (2026-11-04) Approved dependencies (R-15), all named by the WPF and C# profiles:
  CommunityToolkit.Mvvm (MVVM base, MIT), xunit.v3 (tests, Apache-2.0) and XamlStyler.Console
  (XAML format check, development only, MIT). Any further dependency needs a new approval.
- (2026-11-04) The SDK is pinned at 10.0.403, the version the WPF profile was verified with; the
  owner installs it. Rejected: pinning the installed 10.0.303, which would depart from the profile.
- (2026-11-04) The owner allows a test folder `Games\ConfigSync-Dev-Test` in the OneDrive used for
  the earlier measurement, for test files only, to verify reading the upload state from C#.
- (2026-11-04) Gate 3 measurements, on device A: the game version is read from
  `game\ffxivgame.ver` in the installation folder (found through the uninstall entry, XIVLauncher's
  `GamePath` or the default folder), read-only; the OneDrive upload state can be read from C#
  through the shell's automation object, on any thread, in 11 to 24 ms per file, with the same
  values as in gate 1 (4 pending, 2 in sync). Because of that cost, only the files a backup added
  are checked. The game process is found by name every 5 s while it is not running, and waited for
  while it runs.
- (2026-11-04) Gate 3 and 4 implementation choices. CommunityToolkit.Mvvm as the MVVM base (WPF
  profile). The tray icon uses Windows Forms' `NotifyIcon`, part of the Windows Desktop runtime, so
  no package; the Windows Forms analyzer advice WFO0003 on DPI is suppressed in the application
  project because the WPF profile requires the DPI awareness in `app.manifest`. One instance per
  user (a named mutex); a second start hands its `.xivsync` argument over through a named pipe
  restricted to the current user. The execution level is `asInvoker`; nothing needs
  administrator rights, so the extraction finding of the WPF profile does not apply. Symbols are
  embedded, so the published file has nothing beside it.
- (2026-11-04) The planner offers every differing file that is not "never", also when the local
  copy is the newer one. It applies on its own only what is newer on the source and unchanged here
  ("always" files). Rejected: skipping files whose local copy is newer, which left no way to take
  the other device's version by hand, for example after undoing a transfer. An undone transfer
  forgets the shared state of its files and is removed from the list of undoable transfers.
- (2026-11-04) Notifications after a backup name only conflicts in "always" files; offers are not
  announced, because the game changes some of them on every login (V00).
- (2026-11-04) The resource files carry no `reader` and `writer` headers. Rationale: the shared
  pre-commit hook reads the public key token in those headers as an API key and blocks the commit;
  the build does not need them. Reported to the owner as a false positive of the hook.
- (2026-11-04) The foundation has no CI template for C#; `.github/workflows/dotnet-checks.yml`
  follows the PowerShell template's shape and runs what the C# and WPF profiles name: format,
  build with warnings as errors, tests, XAML format and the dependency audit. `actions/setup-dotnet`
  is pinned by commit to v6.0.0, the newest release older than three weeks. `build.ps1` runs the
  same checks locally.
- (2026-11-05) An independent review of the gate 3 and 4 code found 16 defects; all are fixed,
  each with a test that fails without its fix (`tests/.../ReviewTests.cs`). The decisions behind
  the fixes:
  - A known common state (the base table) decides alone; the revision histories only fill in
    where it is unknown. Rejected: using both together, which let an old source state that equals
    a new local one look like "only the source changed" and overwrite the local change.
  - A file of the source that this device's scan did not cover (left out by a rule, too large, a
    link) is hashed where it is and only offered, never applied on its own.
  - Every set-up gives the device a new epoch, also when it is set up again under its old
    identity; other devices ignore their common state and the history of an earlier epoch, so a
    fresh installation's default files are a conflict, not an update. Rejected: no automatic
    backup after setting up again, which would still let the first manual backup through.
  - An apply checks, right before it writes, that every target still has the hash the plan saw,
    and the game state before every single write; a failure in between rolls back at once.
  - Undo, finishing and rolling back an interrupted apply save the current state first ("safety"
    snapshot). Finishing skips steps already done and leaves files that changed since the
    interruption alone.
  - No backup while an interrupted apply is open; the recovery question comes back at the next
    operation.
  - A revision ID is never earlier than the device's last one, instead of refusing backups when
    the clock went back.
  - Objects are deleted by retention only when older than two days, so an object whose manifest
    has not arrived (or a second PC wrongly using the same identity) is not taken away. Setting a
    PC up again as a device that was active within a day asks first.
  - Symbolic links and junctions in the configuration folders are not followed; other reparse
    points, such as OneDrive placeholders, are read as files.
- (2026-11-05) A second independent review, of the fixes, found 12 more points; all are fixed and
  tested. The decisions:
  - When the game is started during an apply or an undo, nothing more is written, not even a
    rollback; the journal stays and the user finishes or rolls back once the game is closed.
    Rejected: rolling back at once, which wrote game files while the game was starting.
  - An undo is journalled like an apply (journal kind `restore`), so a power cut during an undo
    is recovered the same way. No undo and no change of the folders while a journal is open.
  - Journals and snapshots record the absolute folder of each root; recovery and undo refuse
    another folder.
  - An apply that failed or was rolled back is removed from the list of undoable transfers; a
    finished one records what it wrote, and a file whose written state is unknown counts as
    changed in the undo warning.
  - A configured game folder that does not exist refuses planning; nothing is ever written into
    a folder that is not there. A plugin folder that does not exist is treated as not backed up.
  - A file that exists but cannot be read is only offered; links and unreadable folders are listed
    as left out of the backup instead of disappearing silently.
  - Applying an older revision from an earlier epoch of the source does not reset the common
    state with that device.
- (2026-11-05) A third independent review, of the application in daily use, found 12 points; all
  are fixed. The decisions:
  - "Handled" is kept apart from "received" (`handled.json`): a revision of another device counts
    as handled only when it was applied as far as the rules allow, or announced when automatic
    apply is off. A refused automatic apply (another game version, the folder away) is tried again
    and announced once per revision and reason. Rejected: marking it handled with the receipt,
    which lost the update for good on a patch day.
  - A device that is set up always starts normally, also when its sync folder is not reachable;
    the overview says so and the application tries again. The set-up never runs on its own after
    the first one. "Change sync folder" keeps the current folder until the new set-up is complete,
    and cancelling returns to it; it is refused while an interrupted write is open.
  - The set-up can be repeated after a partial failure without adding the PC twice; a failed first
    backup does not fail the set-up. Closing the window while it writes is not possible.
  - The question whether closing the window keeps the application in the notification area is
    asked at the first close, not preset; the first hide shows a short hint.
  - Rule changes go through the same operation queue as backups and transfers, and are saved
    before they are used. Settings: the registry first, and the settings are put back when saving
    fails.
  - The game's own launcher and configuration tool count as "the game runs", since they edit
    `FFXIV.cfg` and `FFXIV_BOOT.cfg`.
  - The transfer page keeps the comparison and the ticks across refreshes; the status line is
    announced to screen readers.
  - Known limitation for the release check (gate 5): the colours are fixed and do not follow the
    Windows high-contrast themes.
- (2026-11-05) The analyzer worker searches only the modules of Windows PowerShell itself and the
  pinned modules restored for this repository (`PSModulePath` set in the worker before the
  analyzer loads). Rationale: after the push of the gate 3 and 4 work, the "Get-Command is not
  recognized" fault failed the PowerShell job twice in a row, in all three workers each time, on
  the same runner image as the last green run. In every failed CI run so far it hit the same file,
  `tests/XivInventory.Tests.ps1`, the file that calls the most commands the analyzer cannot
  resolve (functions of the project's own module); each such name makes it search every module on
  the module path, and the runner image carries hundreds that a developer machine does not. So the
  fault is taken to come from that search, not from chance; this is the best-supported reading,
  not a proven cause, and the next CI runs show it. A fresh worker and an empty module analysis
  cache stay as they are. The restriction also makes the analysis the same on every machine; it
  found nothing new locally. Rejected: excluding the file from the analysis or allowing the fault
  in it, which gives up the check (R-23); a further series of CI reruns to measure, which the
  owner does not want.
- (2026-11-05) Supersedes the entry before: the analyzer worker searches the full module path
  again. Measured locally the same day, 15 fresh processes each: with the restricted path the
  "Get-Command is not recognized" fault came in 5, with the full path in none. The green CI run
  after the change was chance. What was found instead:
  - The fault comes from the analyzer itself. Its rules run in parallel and look commands up
    through one runspace pool; now and then a lookup runs in a runspace that has no `Get-Command`.
    The command cache keeps each lookup as a `Lazy` value, which keeps a thrown exception too, so
    every later lookup of that name in the process fails the same way. That is why the fault
    spoils the rest of a process and a fresh process is the only cure.
  - Analysing the file once more in the same process, the form foundation 5.2.1 uses, cleared the
    other form (`NullReferenceException`) every time and the `Get-Command` form in 1 of 39 cases
    measured. Reported to the owner for the foundation.
  - The worker now recognises the fault by its exception (`CommandNotFoundException` for
    `Get-Command`), not by the English text: on a German Windows the message did not match and the
    fault failed the gate at once instead of starting a fresh worker.
  Rejected: limiting the analyzer's runspace pool to one runspace by reflection, which hung the
  analysis with a busy processor (stopped after hours); running the worker on one processor core,
  1 fault in 15 runs against 3 in 10 at more than twice the time.
- (2026-11-05) The set-up wizard reports completion once, after it stopped working. Found in the
  owner's first real set-up: the window refuses to close while the set-up works (review 3), and
  Completed was raised before the working flag was cleared, so the window stayed open while the
  overview and the tray icon opened behind it; a second click on Finish opened both a second time.
  Now the flag is cleared first, Finish does nothing once the set-up is complete or while it works,
  and the application opens at most one overview and one tray icon per process. A test covers it;
  it fails when either half of the fix is taken out.
- (2026-11-05) A clean-up page for the conflict copies in the game folder (FR-071), at the owner's
  request. Only the files the user ticks, only after a confirmation, and only to the Windows
  Recycle Bin, through the shell with its warning before a file would be deleted for good; a file
  still there afterwards counts as not moved. Each file is looked up again first and left alone
  unless it is still a conflict copy of the game folder, unchanged since it was listed, with its
  original present. Refused while the game runs (X-02), under the device lock. Rationale: the
  owner's 45 old copies from the whole-folder sync of 2024 clutter the folder, and the only safe
  way to let a user remove files from the game folder is one that can be undone and that cannot
  reach a file the user did not choose. Rejected: deleting for good, which cannot be undone;
  ticking all copies by default, which the owner ruled out; letting a copy without its original
  be removed, since it may be the only version. Tested with a fake Recycle Bin, with each
  safeguard taken out once to see a test fail, in the interface on a copy of the game folder
  (refused there because the game was running), and with the real Recycle Bin on a scratch file.
- (2026-11-05) Dalamud's own folder becomes a third backup area, `dalamud`: the files directly in
  the parent of `pluginConfigs` (`dalamudConfig.json`, `dalamudUI.ini`, `launcherConfigV3.json`,
  `accountsList.json` and any other file there), with the rule "never" for every file (FR-072).
  Rationale: the owner noticed that the folder holds more than plugin settings, and the gate 2
  decision to back up everything, with these files restorable only on their own device, had been
  implemented for `pluginConfigs` alone, which was a gap in the implementation, not a decision.
  The folder is derived from the plugin folder only when that is named `pluginConfigs`, so a
  folder chosen by hand never makes its parent a backup root. Left out: subfolders (programs,
  downloads, plugins, which Dalamud installs itself, and `pluginConfigs`, already its own area),
  logs, crash dumps of about 165 MB each, `uidCache.json`, and `dalamudVfs.db`: Dalamud's shadow
  copy of the settings, about 12 MB and changed on every run, so 50 kept revisions could hold
  600 MB of it, while the settings it mirrors are backed up themselves. A device with an older
  version rejects revisions with the new area, so all devices need this version.
- (2026-11-05) The custom plugin repositories (`ThirdRepoList` in `dalamudConfig.json`) are merged
  between devices on request, at the owner's request: the repositories the chosen device has and
  this one lacks are appended, copied entry by entry from its latest backup; existing entries and
  every other setting stay as they are, and nothing is removed. The user sees the list and
  confirms. The merged file is stored as an object in this device's part and written through the
  applier, so it has a verified snapshot, a journal and an undo, and is refused while the game
  runs. The structure was read from the owner's file (an object with `$type` and `$values`, each
  entry with `$type`, `Url` and `IsEnabled`); a file of any other shape is refused and left
  untouched. Checked on a copy of the owner's real file: the merged file differs only by the five
  lines of the new entry; this check found that .NET 10 already writes CRLF on Windows, which a
  conversion on top doubled, fixed and covered by a test. Whether Dalamud keeps a settings file
  written from outside is the open Dalamud test; until then the merge is unverified in the game.
  Rejected: applying `dalamudConfig.json` as a whole, which would carry profiles with
  installation-specific plugin IDs and device paths; merging in both directions or removing
  repositories, which the owner did not ask for and which could drop a repository on purpose;
  merging automatically, which would add code sources to Dalamud without the user seeing them.
- (2026-11-05) Items of the combo boxes (devices, backups, rules, languages, devices in the set-up)
  give their name as their text form, which a screen reader announces. Found in the interface test
  of the repository merge: the items were records and announced their full form, device IDs
  included. A test covers every such record.
- (2026-11-06) Four requests of the owner from 2026-11-05, built after the owner decided that
  development stays on device A and device B is used for tests only:
  - The transfer list shows the files only this device has, with the proposal "only here", when
    the user asks for files with nothing to do; the note says that nothing needs to be done
    (FR-073). Rationale: a count alone did not say which files or whether anything was needed.
    The rows are for display only and can never be selected.
  - The setting is XIVLauncher's folder (FR-074). The plugin settings and Dalamud's files are
    found from it, also in the sandbox mode, where dalamudUserData wins when it holds plugin
    settings. Settings that knew only the plugin folder get the launcher folder when the plugin
    folder is Dalamud's pluginConfigs; a plugin folder chosen by hand elsewhere stays in use.
    Rejected: keeping the plugin folder as the setting and deriving its parent, which the owner
    found the wrong way round.
  - Character names and worlds are suggested from plugin data, read-only (FR-075). Measured on
    device A: the game's character files hold no name as text; AutoRetainer, Lifestream, ICE,
    Allagan Tools and Allagan Market store the name by content ID, which is the hexadecimal number
    in the folder name FFXIV_CHR<ID>. Only worlds stored as text are used (AutoRetainer,
    Lifestream): Allagan's world numbers would need a table of world names, which is material
    from the game (X-09). A suggestion replaces only an empty or numbered name, never one the user
    chose; it is taken at the set-up and at each start, and the settings offer to take all
    suggestions. On device A the plugins know 2 of 5 characters. Rejected: reading names from the
    game's binary files, whose layout is not known (rule against invented field positions).
  - Rules per plugin (FR-076): a plugin is <Name>.* and <Name>/** in the plugin folder; its
    rule is an override on both patterns, and "not backed up" a user exclusion on both, so the
    existing rule set carries it and the exclusion list shows it. A plugin's rule wins over the
    rule for all plugin files by the patterns' specificity. Backing up everything stays the
    default.
- (2026-11-06) Areas instead of files (FR-077 to FR-081). The owner found setting up the transfer
  too much work: "als Spieler hast du keine Ahnung davon, es sollte immer um die Funktionen gehen",
  and the program has to decide which files belong to what. Decided:
  - Nine areas a player recognises, each switched on or off per device and stored in `rules.json`
    (`areas`); all on by default. A switched-on area is the rule "always" for its files, a
    switched-off one "never". A rule on a single file or plugin still wins, so the file and plugin
    views stay as an advanced view; switching an area removes a rule on exactly its own pattern.
  - Files that belong to one device stay in no area and are never taken. `UISAVE.DAT` stays
    outside every area until its content is known.
  - `COMMON.DAT` holds both character settings and the chat (font size, tabs, filters). With one of
    the two areas off, the file is merged by setting name: the source's file with this device's
    values for the kept settings. It is compared without those settings, and the shared state is
    recorded as the hash of the compared part, marked `cmp:`. Measured on device A: the file is
    `Key<TAB>Value` lines, section headers and one NUL; the chat settings are the 70 or so keys
    beginning with `Log` or `IsLog`, and `ChatType`. Rejected: splitting `ADDON.DAT` to take the
    chat window's size out of the HUD layout, since its format is not known. The size and position
    of the chat window go with the HUD layout.
  - HUD layout and inventory order: the game rewrites them on every login (V00), so a conflict
    would come after nearly every session. Both changed since a shared state: the state backed up
    later wins, the other is offered. Without a shared state this never applies: found by review
    finding 3's test, where a device set up again would otherwise have pushed its fresh HUD layout
    to the others.
  - The first transfer with a device asks once (take theirs, keep mine, later) instead of showing a
    conflict per file. "Keep mine" records the source's files as seen, so they are not asked about
    again and the other device takes them from here.
  - A device now also records the files it finds equal when nothing is applied. Before, the shared
    state was only recorded on an apply, so the device that had given everything knew no shared
    state and would have asked about the first transfer again.
  - Automatic transfer also adds the missing plugin sources, when that area is on.
  - Consequence for 6.2 and 6.3 of the specification: the unconfirmed character files, the plugin
    settings and the HUD layout are taken automatically. Accepted because every transfer keeps a
    verified snapshot and can be undone; the checks in the game stay part of gate 4, and a file
    that fails one leaves its area before release.
  - Rejected: a choice "never take this file" on each row of the transfer page. It keeps the user
    thinking in files, which is what the owner wanted to leave behind.
- (2026-11-06) Single settings within an area (FR-082). The owner asked whether single settings
  could be chosen, "nur Schriftgröße aber nicht die Positionen", and for plugins too if it works.
  Decided for the game first:
  - Measured on device A: `COMMON.DAT`, `CONTROL0.DAT` and `CONTROL1.DAT` are named
    `Key<TAB>Value` lines. They are split into groups by the start of the setting's name; the
    first group that matches takes a setting, and the last group of each file takes the rest, so a
    setting a later game version adds is never lost. The split of 2026-11-06 (chat or not) became
    one case of this. Two corrections came out of the inventory: the chat colours (`Color...`)
    belong to the chat, and the hotbar display settings in `COMMON.DAT` (`Hotbar...`) to the area
    hotbars, which therefore also covers `COMMON.DAT`.
  - Checked against copies of all 15 real files of device A, in every pairing and for every group:
    the merged file has the source's lines and order, and only the switched-off group's values.
    The check found that `COMMON.DAT` holds `LockonDefaultZoom` twice with different values, so a
    repeated setting is matched by its occurrence, not only by its name.
  - Not possible: single window positions (`ADDON.DAT`), single hotbars, macros, key bindings or
    gear sets. Their formats are binary and not known, and changing them by guessed positions is
    ruled out.
  - Plugins next, after the Dalamud test: most keep JSON, so their fields could be chosen
    generically, with fields that look like positions, sizes or paths suggested to stay. Deferred
    because the field names are the developers' own and because it is not yet known whether
    Dalamud keeps a settings file written from outside.
- (2026-11-06) Showing which settings differ (FR-083). The owner wanted to see what differs, not
  only that a file is taken, for plugins too, readable and with the details folded away. Decided:
  - Selecting a row on the transfer page shows the differing settings below the list: the value
    here, the value on the other PC, and what happens when the file is taken. Read-only.
  - The game's text files (`COMMON.DAT`, `CONTROL0.DAT`, `CONTROL1.DAT`, `FFXIV.cfg`) are compared
    by setting name and occurrence; JSON files of plugins and Dalamud by field path. Groups: the
    parts of FR-082, else the file's section or the top-level field.
  - Readability: about 30 settings get a readable name (chat font size and transparency, window
    size, volumes, sizes of map and tooltips and similar), derived from the setting's own name;
    the internal name stays visible under it. These come first and their groups are open; the
    rest is folded. Rejected: a readable name for each of the about 600 settings of the game, and
    any names for plugin fields, which only each plugin defines.
  - Values whose name suggests a credential or a session (password, token, key, session, last
    login and similar) are shown as dots; long values are shortened; at most 400 differences are
    listed.
  - Binary files (hotbars, macros, HUD layout, key bindings) only say that they differ.
- (2026-11-06) Choosing single settings (FR-084). The owner asked to take only certain values of a
  file, for example only one setting of a group. Decided:
  - Only for the game's split text files (`COMMON.DAT`, `CONTROL0.DAT`, `CONTROL1.DAT`), whose
    merge already works by setting name (FR-078, FR-082). Each setting can follow its group, be
    always taken, or always keep this device's value. The choice wins over the group switch, a
    rule on the file wins over the choice.
  - The choice is made where the difference is seen, in the differences of the transfer page, and
    stored per device in `rules.json` under the file name and setting, so it holds for every
    character. It is permanent, not for one transfer, because the owner wants the transfer to run
    by itself; the automatic transfer uses it. The areas page counts the choices and can reset them.
  - An area that is on with all its groups switched off now keeps the whole file here; before, the
    file was then taken whole.
  - Rejected: a choice for one transfer only (the automatic transfer would undo it next time);
    `FFXIV.cfg` (belongs to one device and is never taken); plugin JSON fields (after the Dalamud
    test, as agreed); binary files (no field may be changed by a guessed position).
- (2026-11-06) ACT in the backup (FR-085 to FR-087). The owner noted that ACT was missing: only its
  settings matter, never its logs, and the backup should follow which plugins are installed
  instead of needing code per plugin, with a proposal per plugin that can be changed. Decided:
  - A new root `act` is the roaming folder `%APPDATA%`, never walked as a whole: only the paths
    found for ACT and its plugins are read. Paths are relative to it, so they fit a device with
    another Windows user name. Rejected: one root per folder (ACT's folder, `anoyetta`, plugin
    folders), which would need roots that change with the plugins.
  - The plugins come from ACT's own list in its settings file. Their settings are found in ACT's
    settings folder (ACT's convention for plugins), in a plugin's own folder (settings files at
    its top level only), through paths written in those settings (this finds cactbot's user
    folder in OverlayPlugin's settings), and in a short table for places no path points to
    (Hojoring's `anoyetta\ACT`). The table is data, so a new case is one entry.
  - Paths that look like logs, caches, temporary files or backups are never followed, judged only
    below the roaming folder so a user name never decides. Exclusions in the file matrix keep
    logs, caches, backups and program files out even when a user's path names them. A named
    folder above 20 MB is shown, not proposed.
  - ACT's own settings file holds this device's plugin paths, windows and log folder: backed up,
    never taken. The plugins' settings follow the area; since ACT rewrites them on every exit,
    a file both changed goes to the state backed up later.
  - Program files are not backed up (about 330 MB on the owner's device, and code should not be
    copied between devices); the transfer page names the plugins the other device has and this
    one lacks instead.
  - While ACT runs, its files are not written; the automatic transfer leaves the revision
    unhandled so a later try takes them. ACT is recognised by its process name.
  - Older versions of the application reject revisions with the root `act`, as with `dalamud`:
    all devices need this version.
  - Tests never read the real roaming folder: each simulated device has its own. An earlier test
    run before this guard read the owner's ACT folder; it only read, and the files' times show no
    change.
