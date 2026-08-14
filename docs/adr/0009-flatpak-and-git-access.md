# ADR-0009 — Flatpak and Access to the User's Git

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-08-14 |
| **Decision** | Ship Flatpak using `flatpak-spawn --host git`, and say plainly that it is not a meaningfully sandboxed application |

---

## Context

ADR-0002 chose to drive the user's real `git` binary as a subprocess. Every argument for that
decision — hooks run, credential helpers work, LFS works, `.gitconfig` applies verbatim — depends
on reaching **the user's** `git`, with **the user's** environment around it.

Flatpak runs applications in a sandbox. That is the entire point of Flatpak, and it is directly at
odds with the above. This ADR exists because getting it wrong would make the choice in ADR-0002
meaningless for every Flatpak user, while still appearing to work.

Two options, both real:

- **(a) `flatpak-spawn --host git`** — run the host's `git` outside the sandbox.
- **(b) Bundle `git` in the runtime** — the sandbox stays intact, `git` comes from us.

---

## Measurement

Both options were tested in clean containers rather than argued about.

### Bundling git: what actually breaks

A repository with an ordinary `pre-commit` hook — a Python script, which is what most teams'
hooks are — was committed to in an environment with `git` but **without** `python3`, simulating
a Flatpak runtime that bundles git but not a language interpreter:

```
$ git commit -m "feat: test"
/usr/bin/env: 'python3': No such file or directory
exit code: 0
$ git log --oneline
fatal: your current branch 'master' does not have any commits yet
```

Read that carefully. **The commit did not happen. The exit code was 0.** The only sign of failure
was one line on stderr from `env`, not from `git`. A GUI that surfaces "committed successfully"
here is lying to the user, and the user finds out later — after a push that pushes nothing, or a
branch that is missing work they believed was saved.

This is not a corner case. It is the *ordinary* case for any team whose hooks call `python`,
`node`, `ruby`, a linter, or a formatter. Bundling `git` means bundling every interpreter and tool
any user's hooks might invoke, which is impossible.

The same class of breakage applies to credential helpers (`git-credential-libsecret`, `gh auth`),
`diff.external`, `merge.tool`, and LFS — each one is a host binary that the user configured and
that would simply not exist inside the sandbox.

### `flatpak-spawn --host`: what it actually costs

`flatpak-spawn --host` executes a command **outside** the sandbox. It requires
`--talk-name=org.freedesktop.Flatpak`, and Flatpak's own documentation is explicit that this
permission is equivalent to giving up the sandbox: a process that can spawn arbitrary host
commands can do anything the user can do.

Reaching user repositories additionally requires `--filesystem=host`, since repositories live
anywhere the user keeps them.

---

## Decision

**Ship Flatpak with `--filesystem=host` and `--talk-name=org.freedesktop.Flatpak`, invoking the
host's `git` through `flatpak-spawn --host`. Document plainly that the Flatpak build is not
meaningfully sandboxed.**

The reasoning is not that sandboxing does not matter. It is that for *this* application the
sandbox was never achievable in an honest form:

- A Git GUI must read and write repositories anywhere on disk. `--filesystem=host` is unavoidable,
  and it alone already removes most of the sandbox's value.
- Given that, the remaining choice is between a working application and a **broken** one that
  merely looks sandboxed. Option (b) does not buy real security once `--filesystem=host` is
  granted; it only buys the appearance of it, at the cost of silently corrupting the user's
  workflow.

**What we will not do is pretend.** The Flatpak listing, the README, and the AppStream metadata all
state that this build runs the host's `git` and is not confined. A user who wants confinement
should not install this application at all — no packaging trick makes a Git GUI safe to sandbox
from the repositories it exists to edit.

---

## Consequences

- Flathub reviewers will see broad permissions and ask why. The answer is this document.
- `git` must exist **on the host**, not in the runtime — the same requirement as every other
  packaging format we ship (ADR-0002).
- If `flatpak-spawn` is unavailable or the portal denies the call, the application must fail
  **loudly** with an explanation, never fall back to a bundled `git`. A silent fallback would
  reintroduce exactly the failure measured above.
- The Flatpak build is therefore the **lowest-priority** distribution channel. The tarball,
  `.deb`, `.rpm`, AUR packages and AppImage all give the user the real thing without this
  compromise; Flatpak exists for users whose distribution offers nothing else.

---

## Revisit if

Flatpak gains a portal that can execute a *specific* host binary with the user's environment
without granting general host-spawn rights. That would make a genuinely confined build possible,
and this decision should be reopened immediately.
