# Architecture: XIV ConfigSync

Status: proposal. Nothing of the product is implemented. This page records the intended shape so
the feasibility analysis can test it.

## Overview

- **Local side:** discovery of the game and plugin configuration folders, an inventory with
  SHA-256 per file, and a device profile that says which files belong to this device only.
- **Shared side:** a store inside a folder the user already syncs with OneDrive. Each device owns
  one subfolder and is the only writer of it. Content is stored by hash and never changed after it
  is written. A device publishes a new state by writing a small commit object last; a state
  without its commit object is incomplete and is ignored.
- **Applying:** only while the game is closed. Before anything is applied, the current local files
  are kept as a snapshot, so every apply can be rolled back.
- **Cloud status:** a device confirms in its own folder which state of the other device it has
  received. That confirmation is the honest answer to "has it arrived", because a successful local
  write into the OneDrive folder proves nothing about the upload.

## Components

Planned, not built: Core (store, manifest, classification), Platform (file system, process
detection, OneDrive folder), ViewModels, App (UI). The repository currently contains only the
read-only analysis tools in `tools/`.

## Contract between components

Not applicable yet.

## Deploy and update mechanism

Not decided. A portable executable first; an installer later.
