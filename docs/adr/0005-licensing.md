# ADR-0005 — Licensing

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 (opened) · 2026-07-29 (decided) |
| **Decision** | **GPL-3.0-or-later** |

---

## Context

Licensing is the one decision in this project that is **practically irreversible**. From the moment
the first outside contribution arrives, changing the license requires the individual consent of
every contributor.

A repository with no `LICENSE` file grants **no rights at all** — legally it is "all rights
reserved". Nobody may use it, fork it, or contribute to it.

Relevant facts:

- gitext-core takes **no code** from GitExtensions. It is a clean-room reimplementation; only the
  ideas and the user experience are an inspiration. **GitExtensions' license therefore does not
  bind us** — the choice was entirely free.
- Our dependencies are permissive: Avalonia (MIT), CommunityToolkit (MIT), .NET (MIT). None imposes
  copyleft.
- The `git` binary is GPL-2.0, but we do not link against it — we execute it as a separate program
  (ADR-0002). That is not a derivative work and places no constraint on our license.

---

## Alternatives considered

### A) GPL-3.0 — **CHOSEN**

**For**
- Closest to the spirit and heritage of GitExtensions; that community knows this license.
- Nobody can take the code and build a closed-source commercial product from it. Derivatives must
  stay open.
- Most consistent with the project's free-software stance.

**Against**
- Companies are more reluctant to contribute to a GPL codebase.
- Prevents parts of the code being reused in permissively licensed projects.

### B) MIT — rejected

**For:** lowest possible contribution barrier, widest adoption, same license as the rest of our
ecosystem, and parts of the code could be reused elsewhere.

**Against:** someone can take the code, build a closed-source commercial product, and give nothing
back. Git GUIs are precisely a category where that happens — commercial Git clients are a
profitable market.

### C) Apache-2.0 — rejected

**For:** permissive like MIT, plus an explicit patent grant, which matters for corporate adoption.
Legally the clearest permissive option.

**Against:** no copyleft protection — same exposure as MIT.

### D) MPL-2.0 — rejected

**For:** file-level copyleft, a middle ground between GPL and MIT. Modified *files* must stay open,
but the project as a whole can be embedded in a closed product.

**Against:** less common, and most contributors do not know exactly what it entails. Not a
customary choice in the .NET desktop ecosystem.

---

## Rationale

This project is an **end-user application**, not a library. MIT's main advantage — "let my code be
reused elsewhere" — is largely void here; nobody is going to embed a Git GUI's view models in their
own project. GPL's protection, on the other hand, has real value: it prevents the project itself
from being turned into a closed-source derivative, which is a concrete risk in this category.

It is also consistent with the project's stated free-software position and with the GitExtensions
heritage.

**The counter-argument, recorded honestly:** attracting contributors will be this project's biggest
challenge, and GPL makes that somewhat harder. If the priority were purely "get as many developers
contributing as possible", Apache-2.0 would have been the better choice.

### Why `-or-later`?

`GPL-3.0-or-later` is the FSF's recommended form. If a GPLv4 is ever published, the project can
move to it. Choosing `GPL-3.0-only` would close that door for no benefit.

---

## Consequences

- `LICENSE` at the repository root contains the official GPL-3.0 text.
- `Directory.Build.props` declares `<PackageLicenseExpression>GPL-3.0-or-later</PackageLicenseExpression>`.
- Package metadata (deb `copyright`, rpm `License:`, AUR `license=()`, Flatpak AppStream
  `<project_license>`) must carry the license when those files are created.
- **Moving to a non-copyleft license is now effectively impossible.**
- **Dependency licenses must be checked before adding a package.** Permissive packages
  (MIT/Apache-2.0/BSD) can be added to a GPL-3.0 project without issue. Adding a GPL library is
  fine too. No proprietary or commercially licensed package may be added.
- Apache-2.0 code is compatible with GPL-3.0 **in one direction only**: Apache-2.0 code may be
  incorporated into a GPLv3 project, not the reverse.
- **No CLA.** A Contributor License Agreement would raise the contribution barrier for a project of
  this size without a corresponding benefit. A DCO (`Signed-off-by`) is sufficient if it ever
  becomes necessary.
