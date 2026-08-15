# Releasing

The release pipeline runs on a tag push. Everything below the tag step is automated; this
document is the short list of what a human does.

> **Versions come from Git tags** ([ADR-0006](./adr/0006-versioning-and-dependencies.md)).
> No file contains a hand-written version number, and no build script accepts one as an
> argument. The tag is the only source.

---

## Before tagging

1. **Add a `<release>` entry** to `build/linux/io.github.ibrahimhates.GitExtCore.metainfo.xml`
   — newest first, and keep `type="development"` while the version is `0.x`.

   Software centres show this list as "what changed"; an outdated one means users see no
   version history at all. Validate it:

   ```bash
   appstreamcli validate build/linux/io.github.ibrahimhates.GitExtCore.metainfo.xml
   ```

2. **Confirm the tree is green.**

   ```bash
   dotnet build -c Release          # zero warnings — they are errors here
   AVALONIA_HEADLESS=1 dotnet test -c Release
   tools/i18n/generate-fallback.py --check
   ```

3. *(Optional)* **Rehearse locally.** The whole pipeline runs without CI — this is how every
   release so far was verified:

   ```bash
   git tag v0.1.0-dryrun
   build/linux/package.sh            # tarball + AppImage
   build/linux/package-deb-rpm.sh    # .deb + .rpm
   build/windows/package.sh          # ZIP + Inno Setup script
   build/macos/package.sh            # .app bundle
   build/checksums.sh                # SHA256SUMS
   build/release-notes.sh v0.1.0-dryrun
   git tag -d v0.1.0-dryrun
   ```

---

## Tagging

```bash
git tag v0.1.0
git push --tags
```

The tag **must** carry the `v` prefix — MinVer ignores tags without it and silently falls back
to `0.0.0-alpha.0`. That is measured, not theoretical; `build/version.sh --check` refuses to
package such a version, and the release workflow runs that check first.

---

## What the workflow does

`.github/workflows/release.yml`, six jobs:

| Job | What it does |
|---|---|
| `version` | Derives the version once and hands it to every other job. Refuses to continue on an unreleasable version. |
| `test` | Full suite. Pulling a published package back is far more expensive than not shipping it. |
| `linux` | tarball, AppImage, `.deb`, `.rpm` — then **installs the `.deb` and `.rpm` in clean Debian and Fedora containers** and runs them. |
| `windows` | ZIP + installer, **and runs the `.exe`** (only possible on that runner). |
| `macos` | `.app` for both architectures, `.dmg` via `hdiutil`, **and launches the binary**. |
| `release` | Collects everything, writes `SHA256SUMS` (signs it if `GPG_KEY_ID` is set), generates notes from commits, opens a **draft** release. |

The release is a **draft** — nothing is public until you review the notes and press publish.

---

## After publishing

These live outside the repository and are done by hand:

- **AUR** — push `build/arch/gitext-core-bin/PKGBUILD` and `build/arch/gitext-core/PKGBUILD`
  with the new `pkgver` and `sha256sums`.
- **winget** — submit `build/windows/manifests/` to
  [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs).
- **Homebrew** — update `build/macos/gitext-core.rb` in the tap with the new version and
  checksums.
- **Flathub** — `build/flatpak/io.github.ibrahimhates.GitExtCore.yml`, new URL and SHA256.
  Read [ADR-0009](./adr/0009-flatpak-and-git-access.md) first: this build is deliberately not
  sandboxed and reviewers will ask why.

---

## Deliberately absent

Each of these is a decision with a reason, not an oversight — and each is reversible.

| Not provided | Why |
|---|---|
| apt repository / PPA | It has to stay alive forever; a dead repository permanently pollutes `apt update` for everyone who added it. |
| Windows code signing | Recurring annual cost; EV certificates additionally break unattended CI signing. Users see a SmartScreen warning, and the README says so. |
| macOS notarization | Requires a paid Apple Developer account. Gatekeeper calls the app "damaged" — it is not, it is unsigned, and the README says that too. |
| GPG signing | Infrastructure is written and tested; it activates as soon as `GPG_KEY_ID` and `GPG_PRIVATE_KEY` exist as repository secrets. |
