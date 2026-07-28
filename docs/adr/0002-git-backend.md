# ADR-0002 — How We Talk to Git

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 |
| **Decision** | Run the system's `git` executable as a subprocess |

---

## Context

The application has to talk to Git. There are two fundamental approaches:

1. **Library:** use a reimplementation of Git in-process, via `libgit2` (through LibGit2Sharp).
2. **CLI:** run the user's real `git` binary as a child process and parse its output.

This is the most consequential decision in the project. Getting it wrong means hitting a wall at
partial staging and again at rebase and conflict resolution, with a very expensive reversal.

---

## Alternatives considered

### A) The `git` CLI — **CHOSEN**

**For**

- **Hooks work.** `pre-commit`, `commit-msg`, `pre-push`, `prepare-commit-msg` — everything the
  user configured runs exactly as it does in a terminal. libgit2 does not run hooks. This alone is
  decisive: a GUI that silently skips hooks breaks team workflows and destroys user trust.
- **Credential helpers work.** `git-credential-libsecret`, `gh auth`, SSH agent, 2FA flows — all
  free. We do not have to build an authentication layer, which would be months of work in a
  security-sensitive area.
- **Git LFS works.** LFS is a smudge/clean filter; it does not engage without the `git` process.
- **The user's `.gitconfig` applies verbatim** — aliases, `merge.tool`, `diff.external`,
  `core.autocrlf`, `include.path`, conditional `includeIf` blocks, all of it.
- **As current as Git itself.** Sparse checkout, partial clone, `worktree`, `switch`/`restore`,
  reftable, the commit-graph file — features libgit2 gets years later, or never, are available on
  day zero.
- **There is no ambiguity about correctness.** When behaviour is disputed, "whatever `git` does is
  correct" is a usable answer. With libgit2 you get "git does X, libgit2 does Y".
- **"Show the command" comes for free.** The transparency we promise users is just displaying the
  command we already ran, rather than reconstructing a plausible one.
- No licensing complexity: executing `git` is not linking against it.

**Against**

- **`git` must be installed.** An extra requirement for users. → Acceptable: our audience already
  uses Git in a terminal. It is stated plainly in the README.
- **Process startup cost.** ~2–5 ms on Linux, noticeably worse on Windows (~20 ms+).
  → Mitigation: minimise the number of invocations, avoid N+1 command patterns, stream one large
  `git log` rather than issuing many small commands. A dedicated performance concern.
- **Parsing text is brittle.** Output formats can change between versions.
  → Mitigation: machine-readable formats only (`--porcelain=v2`, `-z`, `%x00`-delimited
  `--format`); never parse human-readable output; enforce a minimum Git version; run parser tests
  against more than one Git version in CI.
- Locale accidents. → Mitigation: `LC_ALL=C` on every invocation, plus `-c core.quotepath=false`.

### B) LibGit2Sharp — rejected

**For**
- No process startup cost; reading objects (especially walking history) is fast.
- Rich, type-safe .NET API.
- Does not require `git` to be installed.

**Why rejected**

The deciding factor is not speed but **coverage**:

- **It does not run hooks.** Disqualifying. A commit GUI that skips `pre-commit` is not acceptable.
- **No credential helper integration.** We would have to build authentication ourselves — SSH
  agent, keyring, OAuth, 2FA. Enormous, and risky to get wrong.
- **No LFS support.**
- **It trails Git.** libgit2 picks up modern Git features (partial clone, sparse index, reftable)
  late or not at all.
- **Rebase and merge behaviour is not identical to Git's.** All of the advanced-operations work
  would be at risk.
- Shipping a native library: getting the right `.so`/`.dylib`/`.dll` per RID and verifying it works
  inside AppImage and Flatpak is extra burden.

### C) Hybrid (libgit2 for reads, CLI for writes) — rejected for now

Superficially attractive: fast reads plus correct writes.

**Why rejected:**
- **Two models of Git, two sources of truth.** Inconsistencies — especially around index state and
  ref resolution — produce bugs that are extremely hard to diagnose.
- Maintaining both dependencies and learning both APIs.
- **Premature optimisation.** We have not yet measured the CLI falling short.

**The door is not fully closed:** if profiling later identifies a **specific and narrow** read path
where the CLI is genuinely inadequate (for example bulk commit-metadata scanning on a huge
repository), that path can be isolated behind an interface and accelerated. That will be justified
by measurement, not assumed up front.

---

## Decision

**The `git` CLI**, behind a single abstraction layer in `GitExt.Core`.

### Rules

1. **All Git invocations go through one door.** `Process.Start` is not called anywhere except the
   process-runner implementation.
2. **Never parse human-readable output.** Every command must have a machine-readable form; if it
   does not, we do not use that command.
   - Status: `git status --porcelain=v2 -z --branch`
   - Log: `git log --format=<custom, %x00-delimited> -z`
   - Refs: `git for-each-ref --format=…`
   - Diff: `git diff --numstat -z` plus a separate unified-diff parser
3. **The environment is made deterministic on every call:** `LC_ALL=C`, `GIT_TERMINAL_PROMPT=0`,
   `GIT_OPTIONAL_LOCKS=0` for read-only calls, `-c core.quotepath=false`, `GIT_PAGER=cat`,
   `GIT_EDITOR=false`.
   `GIT_TERMINAL_PROMPT=0` is critical: without it, a command that wants credentials hangs the UI
   indefinitely. `GIT_EDITOR=false` prevents commands that would open an editor from stalling.
4. **User data is never interpolated into a command line.** Commit messages, paths and ref names
   are passed as argument arrays or over stdin. No shell interpretation (`UseShellExecute = false`).
5. **Every call is cancellable and has a timeout.**
6. **Every call is logged** and shown to the user in a Git output panel.

### Minimum Git version

**2.30** (January 2021). `--porcelain=v2`, `for-each-ref` formatting and `switch`/`restore` are
safely available there, and every supported distribution ships at least that. The application
checks the version at startup and reports a clear error if it is too old.

---

## Consequences

- `git` is a **runtime dependency** and is declared as such in the README and in package metadata
  (deb `Depends:`, rpm `Requires:`, AUR `depends=()`).
- **The Flatpak sandbox needs special attention:** should the sandboxed app reach the host `git`
  via `flatpak-spawn --host`, or bundle its own? A bundled `git` can run the user's hooks but may
  not see their system configuration. This is the weakest point of this decision and must be
  resolved before publishing to Flathub.
- Windows needs `git.exe` discovery logic (PATH, `%ProgramFiles%\Git`, registry).
- The number of process invocations is tracked as a performance budget.
- Parsers are the riskiest code in the project. They are tested against fixture repositories
  created with real `git`, never against hand-written sample output.

---

## References

- [libgit2sharp](https://github.com/libgit2/libgit2sharp)
- GitExtensions itself also predominantly drives the `git` process — a mature reference that
  reached the same conclusion.
