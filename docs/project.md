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

- C# on .NET 10: `rules/languages/csharp.md`. The product (`src/`, `tests/`).
- WPF: `rules/frameworks/wpf.md`. The desktop application (`src/XivConfigSync.App`).
- PowerShell 5.1: `rules/languages/powershell.md`. Used for the build script and the read-only
  analysis tools under `tools/`.
- XAML and `.resx` resources, checked by XamlStyler and the build.
- Markdown for documentation.

The SDK is pinned in `global.json` (10.0.403) and packages are locked (`packages.lock.json`).

## Targeted foundation version

5.2.0

## Code repositories

- `miralsoft/XIV-ConfigSync` (this repository): everything.

## Hosting and deploy summary

No server. Releases are Windows executables published on GitHub. Not released yet.

## High-level architecture summary

See `architecture.md`. In short: every device backs up all of its configuration files into its own
folder inside the shared OneDrive folder, as immutable, content-addressed revisions, and applies
another device's files by a rule per file. No file in the shared folder is ever written by two
devices, so the sync client has nothing to merge and no conflict copies arise.
