# Contributing to gitext-core

Thanks for considering it. This document says what the project expects, so that a
contribution does not get stuck in review over something that could have been stated up front.

---

## What is most useful right now

The project is written primarily for its author's own daily use, and it is early. The most
valuable contributions today are **not** large features:

1. **Bug reports from real repositories.** Especially ones with unusual history — submodules,
   worktrees, orphan branches, huge files, non-UTF-8 filenames, ancient `git` versions.
2. **Experience reports from GitExtensions users** about what actually matters day to day.
   This project measures itself against GitExtensions; hearing which parts of that workflow
   are missing is worth more than a patch.
3. **Small, focused fixes** with a test that fails before and passes after.

Before starting anything large, **open an issue first.** A big pull request that does not fit
the architecture is painful for everyone — most of all for you.

---

## Before you write code

Read the relevant [ADR](./docs/adr/). Several decisions are enforced by the build rather than
by review, and a change that contradicts one will fail before a human sees it:

| ADR | What it constrains |
|---|---|
| [0002](./docs/adr/0002-git-backend.md) | We run the real `git` CLI. Do not add LibGit2Sharp or reimplement Git behaviour. |
| [0003](./docs/adr/0003-solution-structure.md) | `GitExt.Core` and `GitExt.Graph` must not reference any UI package. Violations fail the build with `GITEXT001`. |
| [0004](./docs/adr/0004-mvvm-and-di.md) | One composition root. No service locator. |
| [0006](./docs/adr/0006-versioning-and-dependencies.md) | Central package versions, committed lock files, no hand-written version numbers. |
| [0009](./docs/adr/0009-flatpak-and-git-access.md) | Under Flatpak we run the host's `git`, and we say so plainly. |

If you believe an ADR is wrong, that is a legitimate contribution — open an issue arguing it.
Changing the decision and changing the code are two separate steps.

---

## The rule that matters most: measure before you code

This project has a habit that is not optional:

> **Do not optimise, and do not work around a behaviour, until you have measured it.**

Concretely — if you are about to write code because "git does X" or "Avalonia does Y", prove
it first with a real repository or a real window, and put the measurement in the commit
message or a comment. Several of the bugs found in this codebase were the opposite of what
everyone assumed, including:

- `MenuItem.InputGesture` does **not** execute commands. It is display-only.
- MinVer silently ignores `v`-prefixed tags unless told about the prefix, falling back to
  `0.0.0-alpha.0` with a green build.
- Bundling `git` in a Flatpak makes `git commit` **fail silently with exit code 0** when a
  hook needs an interpreter that is not in the sandbox.

None of these would have been found by reasoning about the documentation.

The same applies to fixes: **verify the fix by breaking it.** If you cannot make your new test
fail by reverting your change, the test is not testing anything.

---

## Development setup

Requires the **.NET 10 SDK** and **git 2.30+**.

```bash
git clone https://github.com/ibrahimhates/gitext-core.git
cd gitext-core

dotnet restore --locked-mode   # --locked-mode is required (ADR-0006)
dotnet build -c Release        # must be zero warnings — they are errors here
dotnet run --project src/GitExt.Desktop
```

### Tests

```bash
dotnet test -c Release

# a single test
dotnet test -c Release --filter "FullyQualifiedName~GitExt.Core.Tests.PartialStagingTests"
```

UI tests use Avalonia's headless backend and run without a desktop session.

> **Tests that parse `git` output must run against a fixture repository created with real
> `git`** — not a hand-written string. Git's output format varies by version, and a
> hand-written fixture encodes the assumption we are trying to test.

### Linux window backend

Defaults to X11 (which works on Wayland through XWayland). To force one:

```bash
GITEXT_BACKEND=wayland gitext-core
GITEXT_BACKEND=x11     gitext-core
```

---

## Commit messages

**[Conventional Commits](https://www.conventionalcommits.org/).** This is checked by CI on
pull requests.

```
<type>[(scope)][!]: <summary>

feat(ui): add lane colours to the commit graph
fix: wrong branch name shown on detached HEAD
perf(core): intern commit text
feat(settings)!: settings file format changed     ← breaking change
```

Types: `feat` `fix` `docs` `style` `refactor` `perf` `test` `build` `ci` `chore` `revert`.

This is not a style preference — **release notes are generated from commits**
(`build/release-notes.sh`), because this repository does not have a PR-based history for
GitHub's own generator to read. A commit that does not follow the format lands under "Other"
where nobody reads it.

Write the *why* in the body. "What" is visible in the diff; "why" is not, and it is what the
next person needs.

---

## Pull requests

- Branch from `main`. Name it after what it does: `fix/detached-head-branch-name`.
- Keep it focused. Two unrelated changes are two pull requests.
- The checklist in the PR template is real — CI enforces most of it.
- **Every destructive operation needs a documented way to undo it.** This is a project rule,
  not a nicety: a Git GUI that loses work is worse than no Git GUI.

CI runs on Linux, Windows and macOS, plus a job against the oldest supported `git` (2.30.2),
a publish smoke test for all four target platforms, and a performance regression check.

---

## Code style

`.editorconfig` is applied by the compiler (`EnforceCodeStyleInBuild`), so style problems are
build errors, not review comments. `dotnet format` fixes most of them.

Two conventions the tooling cannot enforce:

- **Comments explain why, not what.** A comment restating the code is noise. A comment
  recording a measurement, a rejected alternative, or a trap someone already fell into is the
  most valuable thing in the file.
- **Match the surrounding code.** Naming, comment density, and idiom should look like the file
  you are editing, not like your personal preference.

---

## Reporting a bug

Include:

1. What you did, what you expected, what happened.
2. Output of `gitext-core --version`.
3. Output of `git --version` and your platform.
4. **`gitext-core --headless` run in the affected repository** — it prints the git executable
   in use, repository layout, refs, status and every git command that ran. It is usually
   enough to diagnose the problem without a back-and-forth.

If the repository is private, the shape of the history usually matters more than its contents:
number of commits, branches, whether there are submodules, worktrees, or unusual refs.

---

## Localisation

The UI is translated from JSON files in `src/GitExt.UI/Locales/`. **English is the source
language**; Turkish ships alongside it. Users switch at runtime from *Settings → Appearance →
Language*.

### Adding a UI string

Never hard-code user-visible text. In XAML use the markup extension, in code use `Loc`:

```xml
<TextBlock Text="{loc:Translate settings.theme}" />
```

```csharp
Notice = Loc.T("merge.already_up_to_date");
Warning = Loc.F("merge.commits_will_be_merged", count);   // with placeholders
```

Then add the key to **both** `en.json` and `tr.json`, and regenerate the built-in fallback:

```bash
tools/i18n/generate-fallback.py    # rewrites Localization/BuiltInEnglish.cs
```

Forgetting any of these is not possible to miss. `LocaleCompletenessTests` fails when the key
sets differ, when a value is empty, or when the `{0}` placeholders do not match between
languages; CI fails when `BuiltInEnglish.cs` has drifted from `en.json`.

Key format is `area.purpose`, lower-case, derived from the **English** text — never from a
translation.

> **Why English is compiled in as well:** the translator falls back to English for any key a
> translation is missing — but that English used to come from `en.json` too. Delete or corrupt
> that file and the fallback became an empty dictionary, so the UI filled with raw key names
> (`settings.language`). This was measured. `BuiltInEnglish.cs` is generated *from* `en.json`
> so the two cannot disagree, and it costs about 32 KB.

### Adding a language

Drop a file into `src/GitExt.UI/Locales/`. That is the whole procedure — no code change, no
`.csproj` change:

```json
{
  "_meta": { "code": "fr", "name": "Français" },
  "settings.theme": "Thème"
}
```

The file name is the language code; `_meta.name` is what appears in the language dropdown, and
it should be written **in that language** ("Français", not "French") — the person looking for
it is the person who reads it. Keys missing from a translation fall back to English, so a
partial translation is usable from the first commit.

### What is *not* translated

- **Code comments.** They stay in Turkish.
- **Git terminology and product names** — `SHA`, `URL`, `Push`, `Git`, `gitext-core` — which
  read the same in both languages.
- **`GitExt.Core` messages.** The core layer cannot depend on the UI (ADR-0003), so its
  exception text stays English and diagnostic. What the user sees is chosen in the UI from
  `GitException.Kind`; see `Loc.GitError`. An unclassified failure (`GitFailureKind.Unknown`)
  falls through to git's own message on purpose — hiding an unknown git error behind invented
  text would make it undiagnosable.

---

## Releasing

Maintainers only — see **[docs/RELEASING.md](./docs/RELEASING.md)**. The short version: add a
`<release>` entry to the AppStream metadata, push a `v`-prefixed tag, review the draft release
the workflow opens.

---

## License

By contributing you agree that your contribution is licensed under
[GPL-3.0-or-later](./LICENSE), the same as the project.
