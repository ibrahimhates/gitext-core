<div align="center">

# gitext-core

**English** · [Türkçe](./README.tr.md)

**A fast, native, cross-platform Git GUI — the GitExtensions experience, freed from Windows.**

[![Build](https://github.com/ibrahimhates/gitext-core/actions/workflows/ci.yml/badge.svg)](https://github.com/ibrahimhates/gitext-core/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ibrahimhates/gitext-core)](https://github.com/ibrahimhates/gitext-core/releases)
[![License](https://img.shields.io/badge/license-GPL--3.0--or--later-blue)](./LICENSE)

> ⚠️ **Status: beta — feature-complete for daily Git work, packaged for all three platforms.**
> Browsing history, diffing, staging by line, committing, branching, fetch/pull/push, rebase
> (including interactive), cherry-pick, revert, stash and conflict resolution are implemented
> and tested. Linux is exercised daily and every Linux package is verified in a clean container.
> Windows runs (verified under Wine and on CI) but has not been used as a daily driver;
> **macOS has not been run on real hardware** — treat it as community-supported.
>
> The interface is currently **Turkish only**; English localisation is the next piece of work.

</div>

![gitext-core — commit graph, dark theme](./docs/assets/screenshot-main-dark.png)

<details>
<summary>Light theme</summary>

![gitext-core — commit graph, light theme](./docs/assets/screenshot-main-light.png)

</details>

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

## Features

Everything below is **implemented and tested** unless marked otherwise.

- **Visual commit graph** — branches, merges, tags and refs rendered as a colour-coded DAG, virtualized for very large histories.
- **Diff & file inspection** — side-by-side and unified diffs with word-level highlighting and whitespace controls.
- **Staging & committing** — stage/unstage by file, by hunk, and by individual line; amend; sign-off; commit message templates.
- **Branch & remote operations** — checkout, create, rename, delete, fetch, pull, push, track/untrack, prune.
- **Advanced operations** — interactive rebase, cherry-pick, revert, stash management, reset, tag management.
- **Merge conflict resolution** — in-app three-way view plus integration with your configured `merge.tool`.
- **Repository browsing** — file tree at any revision, blame, file history, and history-following across renames.
- **Reflog browser** — find lost commits and undo your last action.
- **Search** — across commit messages, diff contents and file contents.
- **Submodules and worktrees** — recognised and navigable.
- **Git LFS** — works because we drive the real `git` (LFS is a clean/smudge filter, so it
  engages on its own). There is **no LFS-specific UI** yet: no pointer/actual-content
  indicator, no explicit fetch of LFS objects.

---

## Platform support

| Platform | Status |
|---|---|
| Linux — X11 | **Used daily.** Every package format verified in a clean container. |
| Linux — Wayland | Builds and runs (opt-in backend, see below) |
| Windows 10/11 | **Runs** — verified under Wine and on CI; not yet used as a daily driver |
| macOS (Apple Silicon + Intel) | Builds and launches on CI; **not run on real hardware** |

Linux is the first-class target and where development happens. Windows and macOS builds come from
the same codebase and the same release pipeline.

**Minimum versions**, measured rather than assumed:

| Platform | Floor | How it was established |
|---|---|---|
| Linux | **glibc 2.27** | `objdump -T` on the shipped binary; the highest symbol version required comes from `libSkiaSharp.so`. Verified running on Debian 11 (glibc 2.31) through Arch (2.44). |
| Windows | **Windows 10** | Declared in the application manifest. |
| macOS | **12.0 (Monterey)** | `LSMinimumSystemVersion` in the bundle. Not verified on hardware. |

> The native libraries come pre-built from NuGet, so the glibc floor is a property of those
> packages — not of the machine that built the release. Building on an older distribution
> would not lower it.

---

## Measured performance

Numbers from a real run on Linux x64 (.NET 10, ReadyToRun, self-contained), against
synthetic repositories built by `tools/test-repos/generate.sh`. Every figure below was
measured, not estimated — see [`benchmarks/baseline/`](./benchmarks/baseline/) for the raw
data and how to reproduce it.

| | Measured | Budget |
|---|---:|---:|
| Cold start to first frame | **~370 ms** | < 1.5 s |
| Open a 10k-commit repository | **99 ms** | < 1 s |
| Open a 500k-commit repository | **3.4 s** (first screen 1.3 s) | < 5 s |
| Diff a 1 000-line file | **3 ms** | < 200 ms |
| Refresh status, 10 000 files | **47 ms** | < 300 ms |
| Memory, 500k commits loaded | **368 MB** | < 600 MB |
| Idle memory (PSS) | **76 MB** | < 200 MB |
| Self-contained binary (trimmed) | **59 MB** | — |

With a `commit-graph` file present — which gitext-core detects and offers to create, but
never writes without asking — the first row of a 500k-commit graph appears in **7.8 ms**
instead of 1.3 s.

> **Caveats, stated plainly.** These come from one machine, and the two largest repositories
> are synthetic: 500k linear commits, not 500k commits of tangled real history. Scroll frame
> rate is not in the table because it needs interaction and has not been measured end to end
> yet; the built-in diagnostics panel (`Ctrl+Shift+F12`) reports frame times and dropped
> frames for when it is.

---

## Installation

> **Every command in this section has been run.** Each package below was installed and
> launched in a clean container or VM of its own distribution — not on the developer's
> machine, where everything is already present. Where something is *not* verified, it says so.

Distribution happens through **GitHub Releases**, plus community package repositories.
Replace `<version>` with the release you are installing.

### Requirements

- **Git ≥ 2.30** must be installed and on your `PATH`.
  gitext-core drives the real `git` binary rather than reimplementing it, so your hooks, credential
  helpers, `.gitconfig`, LFS setup and aliases all keep working exactly as they do in a terminal.
- No .NET runtime required — official builds are self-contained.

### Verifying your download

Every release ships a `SHA256SUMS` file:

```bash
sha256sum -c SHA256SUMS --ignore-missing
```

This catches a truncated download or a broken mirror. It is **not** a security guarantee: an
attacker who can replace the package can replace the checksum file too. If a `SHA256SUMS.asc`
is present, that signature is the stronger check.

### Linux

#### AppImage *(the universal option)*

```bash
curl -LO https://github.com/ibrahimhates/gitext-core/releases/download/v<version>/gitext-core-<version>-x86_64.AppImage
chmod +x gitext-core-<version>-x86_64.AppImage
./gitext-core-<version>-x86_64.AppImage
```

Self-contained: no .NET runtime, no Avalonia packages, nothing to install. It still needs
`git` on your `PATH`.

Verified on **Debian 11, Ubuntu 22.04, Debian 12, Fedora 41 and Arch** — that is glibc 2.31
through 2.44. The oldest of those is the floor: the binary requires `GLIBC_2.27` or newer.

Optional desktop integration (menu entry, icon) via
[Gear Lever](https://github.com/mijorus/gearlever) or `appimaged`.

#### Debian / Ubuntu / Linux Mint

```bash
sudo apt install ./gitext-core_<version>_amd64.deb
```

`apt` pulls in `git` automatically. Verified in clean **Debian 12** and **Ubuntu 24.04**
containers, including removal (`apt remove gitext-core` leaves nothing behind).

There is **no apt repository**, and there will not be one for now — see
[the decision](#packaging-decisions) below.

#### Fedora / RHEL

```bash
sudo dnf install ./gitext-core-<version>-1.fc41.x86_64.rpm
```

Verified in a clean **Fedora 41** container, including removal.

#### Arch Linux / Manjaro *(AUR)*

```bash
yay -S gitext-core-bin      # prebuilt binary (recommended)
yay -S gitext-core          # build from source
```

The package definitions live in [`build/arch/`](./build/arch/). The built package was
verified with `pacman -U` in a clean **Arch** container.

#### Portable tarball

```bash
curl -LO https://github.com/ibrahimhates/gitext-core/releases/download/v<version>/gitext-core-<version>-linux-x64.tar.gz
tar -xzf gitext-core-<version>-linux-x64.tar.gz
cd gitext-core

./install.sh              # into ~/.local — no root needed
sudo ./install.sh --system  # or into /usr/local, for all users
./install.sh --uninstall  # finds wherever it was installed
```

`install.sh` places the binary, the `.desktop` entry, the icon set and the AppStream
metadata, then refreshes the desktop caches. It warns if `git` is missing and if the target
`bin` directory is not on your `PATH`. Running it is optional — the extracted `gitext-core`
binary works on its own.

Verified in a clean **Debian 11** container: installed, ran against a real repository,
uninstalled with zero files left behind.

#### Flatpak

```bash
flatpak install flathub io.github.ibrahimhates.GitExtCore
```

> ⚠️ **The Flatpak build is not meaningfully sandboxed, and this is deliberate.**
> It holds `--filesystem=host` (a Git GUI must reach repositories anywhere on disk) and
> `--talk-name=org.freedesktop.Flatpak`, which lets it run *your* `git` on the host.
>
> The alternative — bundling `git` inside the sandbox — was measured and rejected: with a
> Python `pre-commit` hook and no interpreter in the runtime, `git commit` **failed while
> returning exit code 0**. The commit silently did not happen. The reasoning is written up in
> [ADR-0009](./docs/adr/0009-flatpak-and-git-access.md).
>
> If you want confinement, do not install this application. No packaging trick makes a Git GUI
> safe to sandbox away from the repositories it exists to edit. The other Linux channels above
> give you the same program without the pretence.

### Windows

```powershell
winget install io.github.ibrahimhates.GitExtCore
```

Or download `gitext-core-<version>-setup.exe` (installer) or
`gitext-core-<version>-win-x64.zip` (portable) from Releases.

The installer adds a Start Menu shortcut, offers an optional desktop shortcut and optional
`PATH` entry, supports clean uninstallation, and does **not** require administrator rights.
It warns before installing if `git` is not found.

> ⚠️ **The Windows builds are not code-signed.** SmartScreen will show
> *"Windows protected your PC"* on first run; you reach the application through
> **More info → Run anyway**.
>
> This is a cost decision, not an oversight: a code-signing certificate is a recurring annual
> expense that is out of proportion for a single-developer project, and the EV variant's
> hardware token would break automated releases. Verify your download against `SHA256SUMS`
> instead. If the project grows, this decision gets revisited.

`git.exe` is found via `PATH`, the Git for Windows install locations, and the Scoop and
Chocolatey paths. All of these were verified against a real Git for Windows installation.

### macOS

```bash
brew tap ibrahimhates/tap
brew install --cask gitext-core
```

Or download `gitext-core-<version>-osx-arm64.dmg` (Apple Silicon) or
`gitext-core-<version>-osx-x64.dmg` (Intel) from Releases.

> ⚠️ **The macOS build is not notarized.** Gatekeeper will refuse to open it and will claim
> the app *"is damaged"*. It is not damaged — it is unsigned, and that is the message macOS
> shows. Notarization requires a paid Apple Developer account, which this project does not have.
>
> The Homebrew cask clears the quarantine attribute for you during installation. If you
> install the `.dmg` by hand, do it yourself:
>
> ```bash
> xattr -dr com.apple.quarantine /Applications/gitext-core.app
> ```
>
> Do not run that command on software you did not verify. Check `SHA256SUMS` first.

> **Not yet run on real hardware.** The macOS bundle cross-compiles from Linux and is launched
> by CI on a macOS runner, but no one has used it as a daily driver. Treat macOS as
> community-supported: it should work, and reports are welcome.

### Packaging decisions

A few things are deliberately absent:

| Not provided | Why |
|---|---|
| apt repository / PPA | It has to stay alive forever — a dead repository permanently pollutes `apt update` for everyone who added it. Out of proportion at this scale. Flatpak and the AUR already give automatic updates. |
| Code signing (Windows) | Recurring annual cost; EV certificates additionally break unattended CI signing. |
| Notarization (macOS) | Requires a paid Apple Developer account. |
| In-app update check | The project promises no telemetry. A version check is not telemetry, but it would have to be off by default, and "was this installed by a package manager?" has no reliable answer — guessing wrong means telling an `apt`-managed install to download a tarball. Package managers already solve this. |

Every one of these is reversible, and none of them is hidden from you.

### Building the packages yourself

```bash
build/linux/package.sh            # tarball + AppImage
build/linux/package-deb-rpm.sh    # .deb + .rpm (uses containers if the tools are missing)
build/windows/package.sh          # portable ZIP + Inno Setup script
build/macos/package.sh            # .app bundle
build/checksums.sh                # SHA256SUMS (+ GPG if GPG_KEY_ID is set)
```

All artifacts land in `dist/`. **The version comes from the Git tag** — never from a file and
never from an argument (see [ADR-0006](./docs/adr/0006-versioning-and-dependencies.md)). To
build without tagging:

```bash
MINVER_VERSION_OVERRIDE=1.0.0-test build/linux/package.sh
```

Each script verifies that the version inside the binary matches the version in the package
name, and refuses to produce an untagged "release".

---

## Usage

> **The application is functional but this guide is not written yet.** Browsing history,
> diffing, staging by line, committing, branching, fetch/pull/push, rebase, cherry-pick,
> revert, stash and conflict resolution all work — see the screenshots above. What is missing
> here is the prose that walks you through them.
>
> In the meantime: `F1` opens the keyboard shortcut reference and `Ctrl+Shift+P` opens the
> command palette, which is the fastest way to find out what the application can do.

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

On Linux you also need the libraries Avalonia renders against. On any normal desktop install
they are already present — this list matters for containers and minimal systems.

Determined by inspecting the shipped binaries (`objdump -p`, plus the names Avalonia loads at
runtime), not by guessing:

| Needed | Why |
|---|---|
| `libfontconfig1` | Linked by `libSkiaSharp.so`. **The only hard link-time dependency.** |
| `libX11`, `libICE`, `libGL` | Loaded at runtime by the X11 backend (the default). |
| `libwayland-client`, `libwayland-egl`, `libxkbcommon` | Loaded only when `GITEXT_BACKEND=wayland`. |

```bash
# Debian / Ubuntu
sudo apt install libfontconfig1 libx11-6 libice6 libgl1 libxkbcommon0

# Fedora
sudo dnf install fontconfig libX11 libICE mesa-libGL libxkbcommon

# Arch
sudo pacman -S fontconfig libx11 libice libglvnd libxkbcommon
```

> Everything else — the .NET runtime, Skia, HarfBuzz — is inside the binary. A self-contained
> build in a bare `mcr.microsoft.com/dotnet/sdk` container runs `gitext-core --version` with
> no extra packages at all; the list above is what the *window* needs.

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
The Linux output is used daily; the Windows output has been run under Wine and on CI; the
macOS output has only been run on CI.

<details>
<summary><b>Why not NativeAOT?</b> — it wins on every metric, and is still not used</summary>

Measured during the performance phase:

| | Trimmed (shipped) | NativeAOT |
|---|---:|---:|
| Cold start to first frame | 370 ms | **186 ms** |
| Idle memory (PSS) | 76 MB | **54 MB** |
| Binary size | 59 MB | **28 MB** |

It also passed smoke tests. It is still not what ships, for one reason: **the whole
application has not been exercised under AOT.** Avalonia resolves parts of XAML through
reflection, and when that breaks under AOT it breaks *at runtime* — on a user's machine,
in a dialog nobody opened during testing, not during publish. A crash there costs more
than 184 ms of startup buys.

This is a decision to revisit once there is coverage that actually drives every window.

</details>

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

See **[CONTRIBUTING.md](./CONTRIBUTING.md)** for setup, the commit message convention (enforced
by CI), and the one rule that matters most here: *measure before you code*.

Participation is covered by the [Code of Conduct](./CODE_OF_CONDUCT.md).

The most useful contributions right now are **not** large features: bug reports from real
repositories with awkward history, and experience reports from GitExtensions users about what
actually matters day to day. Before starting anything large, open an issue first.

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
