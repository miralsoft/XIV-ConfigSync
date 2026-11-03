# Project: XIV ConfigSync

The identity card of the project.

## Identity

- **Name:** XIV ConfigSync
- **Slug:** xiv-configsync
- **Owner:** Sanaka
- **Summary:** A standalone Windows desktop application that keeps the local client settings of
  FINAL FANTASY XIV and the configuration of Dalamud plugins consistent between two or more of
  the user's own PCs, through a folder the user already syncs (OneDrive). Settings that belong to
  one device, such as resolution and UI scale, stay on that device.
- **Kind:** external. The documentation lives in this repository under `docs/`.
- **Committer identity:** `Sanaka <20637644+miralsoft@users.noreply.github.com>`, matching
  `.miralsoft-enforcement`.
- **Licence:** GPL-3.0-or-later (`LICENSE`).

## Declared languages, frameworks, and active profiles

- PowerShell 5.1: `rules/languages/powershell.md`. Used for the build script and the read-only
  analysis tools under `tools/`.
- Markdown for documentation.

The product's own language and UI framework are not declared yet. They are decided at the end of
the feasibility analysis; the recommendation is C# on .NET 10 with WPF.

## Targeted foundation version

5.2.0

## Code repositories

- `miralsoft/XIV-ConfigSync` (this repository): everything.

## Hosting and deploy summary

No server. Releases are Windows executables published on GitHub. Not released yet.

## High-level architecture summary

See `architecture.md`. In short: every device writes only into its own folder inside the shared
OneDrive folder, as immutable, content-addressed revisions. No file in the shared folder is ever
written by two devices, so the sync client has nothing to merge and no conflict copies arise.
