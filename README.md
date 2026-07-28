<div align="center">

# gitext-core

**English** · [Türkçe](./README.tr.md)

**A fast, native, cross-platform Git GUI — the GitExtensions experience, freed from Windows.**

<!-- TODO(readme-01): Add badges once CI has run and a release exists.
     Planned: build status, latest release, downloads, license.
     [![Build](https://github.com/ibrahimhates/gitext-core/actions/workflows/ci.yml/badge.svg)](…)
     [![Release](https://img.shields.io/github/v/release/ibrahimhates/gitext-core)](…)
     [![License](https://img.shields.io/github/license/ibrahimhates/gitext-core)](./LICENSE)
-->

> ⚠️ **Status: pre-alpha — not usable yet.**
> The project builds and opens a window, but **it does not do anything with Git yet.**
> There is no release. Everything marked *(planned)* below describes the intended end state,
> not current behaviour.

</div>

<!-- TODO(readme-02): Insert the main screenshot here (commit graph, dark theme, real repo).
     Blocked on the commit graph being presentable.
     Target: docs/assets/screenshot-graph-dark.png @ 2x, plus a light variant.
     Also add a short animated GIF/WebM of staging + committing. -->

---

## Why does this exist?

On Windows, [GitExtensions](https://github.com/gitextensions/gitextensions) is loved for three
things: a commit graph that makes tangled histories genuinely readable, a UI that maps directly
onto how Git actually works, and speed that never gets in your way.

It cannot run natively on Linux — it is built on Windows Forms. The alternatives are largely
Electron-based, which means hundreds of megabytes of RAM for what is fundamentally a text and
graph renderer.

**gitext-core** rebuilds that experience on modern .NET and [Avalonia UI](https://avaloniaui.net/):
native rendering, native performance, one codebase for Linux, Windows and macOS.

This is a **clean-room reimplementation inspired by** GitExtensions, not a port and not affiliated
with the GitExtensions project.

### Design principles

| | |
|---|---|
| **Git, not a Git-shaped abstraction** | The UI mirrors Git's real model — refs, objects, the index, the worktree. Nothing is hidden "for your own good". |
| **Show the command** | Every operation surfaces the underlying `git` invocation. You should always be able to learn from the tool, and reproduce it in a terminal. |
| **Fast on huge repos** | Virtualized, incremental rendering. A 500k-commit history must scroll at display refresh rate. |
| **Keyboard first** | Every frequent action reachable without the mouse. |
| **Light on resources** | Native widgets, no browser engine. |
| **No telemetry** | Ever. |

---

## Features *(planned)*

- **Visual commit graph** — branches, merges, tags and refs rendered as a colour-coded DAG, virtualized for very large histories.
- **Diff & file inspection** — side-by-side and unified diffs with word-level highlighting and whitespace controls.
- **Staging & committing** — stage/unstage by file, by hunk, and by individual line; amend; sign-off; commit message templates.
- **Branch & remote operations** — checkout, create, rename, delete, fetch, pull, push, track/untrack, prune.
- **Advanced operations** — interactive rebase, cherry-pick, revert, stash management, reset, tag management.
- **Merge conflict resolution** — in-app three-way view plus integration with your configured `merge.tool`.
- **Repository browsing** — file tree at any revision, blame, file history, and history-following across renames.
- **Submodules, worktrees and Git LFS awareness.**
- **Reflog browser** — find lost commits and undo your last action.
- **Search** — across commit messages, diff contents and file contents.

---

## Platform support

| Platform | Status |
|---|---|
| Linux — X11 | Builds and runs |
| Linux — Wayland | Builds and runs (opt-in backend, see below) |
| Windows 10/11 | Cross-compiles; not yet run on target |
| macOS (Apple Silicon + Intel) | Cross-compiles; not yet run on target |

Linux is the first-class target and where development happens. Windows and macOS builds come from
the same codebase and the same release pipeline.

<!-- NOTE(readme-03): Minimum OS versions still unconfirmed. Avalonia 12 baseline is .NET 8+;
     the glibc floor for self-contained Linux builds needs pinning (likely Ubuntu 22.04 /
     glibc 2.35), as does the macOS floor (likely 12.0). Confirm when building the AppImage
     on an old-glibc container. -->

---

## Installation

> **None of these are available yet.** No release has been published. This section documents the
> intended distribution channels; every command will be verified before the first release ships.

Distribution happens primarily through **GitHub Releases**, plus community package repositories.

### Requirements

- **Git ≥ 2.30** must be installed and on your `PATH`.
  gitext-core drives the real `git` binary rather than reimplementing it, so your hooks, credential
  helpers, `.gitconfig`, LFS setup and aliases all keep working exactly as they do in a terminal.
- No .NET runtime required — official builds are self-contained.

### Linux

#### AppImage *(the universal option)*

<!-- TODO(readme-04): Replace <version> once the first release is tagged. -->

```bash
curl -LO https://github.com/ibrahimhates/gitext-core/releases/download/v<version>/gitext-core-<version>-x86_64.AppImage
chmod +x gitext-core-<version>-x86_64.AppImage
./gitext-core-<version>-x86_64.AppImage
```

Optional desktop integration (menu entry, icon) via [Gear Lever](https://github.com/mijorus/gearlever) or `appimaged`.

#### Flatpak *(planned — Flathub)*

```bash
flatpak install flathub io.github.ibrahimhates.GitExtCore
flatpak run io.github.ibrahimhates.GitExtCore
```

<!-- NOTE(readme-05): The Flatpak sandbox needs filesystem=host to reach user repositories, and
     access to the host `git` via flatpak-spawn or a bundled git. Decide before submitting to
     Flathub — this determines whether user hooks still work under Flatpak. -->

#### Arch Linux / Manjaro *(AUR)*

```bash
yay -S gitext-core-bin      # prebuilt binary (recommended)
yay -S gitext-core          # build from source
```

#### Fedora / RHEL / openSUSE *(RPM)*

```bash
sudo dnf install ./gitext-core-<version>-1.x86_64.rpm
```

<!-- TODO(readme-06): Evaluate publishing to Fedora COPR for `dnf copr enable` installs. -->

#### Debian / Ubuntu / Linux Mint *(DEB)*

```bash
sudo apt install ./gitext-core_<version>_amd64.deb
```

<!-- TODO(readme-07): Evaluate hosting an apt repository (or a PPA) so users get updates
     automatically rather than re-downloading a .deb each release. -->

#### Portable tarball

```bash
tar -xzf gitext-core-<version>-linux-x64.tar.gz
./gitext-core/gitext-core
```

### Windows

<!-- TODO(readme-08): Fill in once the Windows pipeline exists.
     Planned: portable .zip, an MSI or Inno Setup installer, and a winget manifest.
     Code signing is unresolved — an unsigned installer triggers SmartScreen. Decide whether to
     buy a certificate or ship portable-only at first. -->

```powershell
winget install gitext-core   # planned
```

Or download the portable `gitext-core-<version>-win-x64.zip` from Releases and run `gitext-core.exe`.

### macOS

<!-- TODO(readme-09): Fill in once the macOS pipeline exists.
     Planned: a .dmg with a signed + notarized .app bundle, and a Homebrew cask.
     Notarization requires a paid Apple Developer account — unresolved. Until then users need
     `xattr -dr com.apple.quarantine`, which must be documented honestly here. -->

```bash
brew install --cask gitext-core   # planned
```

---

## Usage

> **Not yet applicable.** The application currently opens an empty window. This section will
> document the real workflow as features land.

<!-- TODO(readme-15): Write the actual usage guide. It should cover, in this order:
     1. Opening a repository (folder picker, recent list, drag-and-drop)
     2. Reading the commit graph — what the lane colours and ref badges mean
     3. Inspecting a commit and its diff
     4. Staging by file / hunk / line, and committing
     5. Branching, fetching, pulling, pushing
     6. Rebase, cherry-pick, stash, and resolving conflicts
     7. The Git output panel — how to see the exact command that ran
     8. Keyboard shortcut reference
     Each section needs a screenshot. Blocked on the corresponding feature existing. -->

### Choosing a window backend (Linux)

gitext-core defaults to X11, which also works on Wayland sessions through XWayland. Avalonia's
native Wayland backend is opt-in and can be selected with an environment variable:

```bash
GITEXT_BACKEND=wayland gitext-core   # native Wayland
GITEXT_BACKEND=x11     gitext-core   # force X11
GITEXT_BACKEND=auto    gitext-core   # default
```

If the window fails to appear or renders incorrectly, switching backends is the first thing to try.

---

## Building from source

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** or later | [download](https://dotnet.microsoft.com/download) |
| Git | **2.30** or later | Also a runtime dependency |

On Linux you also need the desktop libraries Avalonia renders against. On a normal desktop
install these are already present.

<!-- NOTE(readme-10): The exact per-distro package names still need pinning by building in a
     clean container for Debian/Fedora/Arch. Verified only on an Arch-based system so far,
     where everything was already installed. -->

### Clone, build, run

```bash
git clone https://github.com/ibrahimhates/gitext-core.git
cd gitext-core

dotnet restore
dotnet build -c Release
dotnet run --project src/GitExt.Desktop
```

### Test

```bash
dotnet test
```

### Produce a self-contained binary

```bash
# Linux
dotnet publish src/GitExt.Desktop -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/linux-x64

# Windows
dotnet publish src/GitExt.Desktop -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/win-x64

# macOS (Apple Silicon)
dotnet publish src/GitExt.Desktop -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/osx-arm64
```

All four target RIDs (`linux-x64`, `win-x64`, `osx-arm64`, `osx-x64`) cross-compile from Linux.
Only the Linux output has been executed and verified so far.

<!-- NOTE(readme-11): NativeAOT has not been evaluated yet. Trimming currently produces zero IL
     warnings, but this must be re-verified as reflection-heavy features land. -->

### Project layout

```
src/
├── GitExt.Core/      # Git process layer, models, output parsers — no UI dependency
├── GitExt.Graph/     # Commit DAG lane-assignment algorithm — pure, heavily tested
├── GitExt.UI/        # Avalonia views, view models, controls, themes
└── GitExt.Desktop/   # Entry point, platform bootstrap, DI composition root
```

`GitExt.Core` and `GitExt.Graph` must not reference any UI package. This is enforced at build
time — a violation fails the build with error `GITEXT001` rather than waiting for code review.

### Technology

| Area | Choice |
|---|---|
| UI framework | Avalonia 12.1 |
| Git backend | The `git` CLI, driven as a subprocess |
| Runtime | .NET 10 |
| MVVM | CommunityToolkit.Mvvm |
| Tests | xUnit v3 + Shouldly |

Each of these choices — and the alternatives that were rejected — is recorded in
**[docs/adr/](./docs/adr/)**. Read the relevant record before proposing a change to one of these
areas; several of the decisions there are enforced by the build rather than by code review.

---

## Contributing

Contributions are welcome once the foundation is in place.

<!-- TODO(readme-13): Write CONTRIBUTING.md and CODE_OF_CONDUCT.md.
     Should cover: coding style (.editorconfig), commit message convention, branch naming,
     how to run tests, how to add a Git operation end to end, and PR review expectations. -->

Until then, the most useful contributions are issues: bug reports, missing scope, and experience
reports from GitExtensions users about what actually matters day to day.

---

## Acknowledgements

- [GitExtensions](https://github.com/gitextensions/gitextensions) — the standard this project measures itself against.
- [Avalonia UI](https://avaloniaui.net/) — the framework making native cross-platform .NET UI practical.
- [Git](https://git-scm.com/) — the thing itself.

---

## License

[GNU General Public License v3.0 or later](./LICENSE) — `GPL-3.0-or-later`

Copyright (C) 2026 gitext-core contributors.

gitext-core is free software: you may use, study, share and modify it. If you distribute a
modified version, it must also be free software under the same license.

This program is distributed in the hope that it will be useful, but **without any warranty**;
without even the implied warranty of merchantability or fitness for a particular purpose.
