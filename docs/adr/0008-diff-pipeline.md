# ADR-0008 — Diff Pipeline

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-29 |
| **Decision** | One `git` invocation per diff; paths from the raw section only; word-level diff computed locally; output decoded losslessly |

---

## Context

Phase 04 added the diff engine and viewer. Four decisions in it are non-obvious enough that a
contributor would reasonably ask "why is it done this way?" — and in three of them the
straightforward approach was measured and found to be **silently wrong**, not merely slower.

All measurements below were taken against real repositories (mostly `git/git`), not synthetic
fixtures.

---

## Decision 1 — Paths come from `--raw`, never from the `diff --git` header

A diff is read with a **single** invocation that carries both machine-readable metadata and
the patch text:

```
git show --root --first-parent --format= --raw --numstat -z --patch <sha> --
```

The `--raw -z` section provides old/new mode, old/new blob, status letter and **unescaped**
paths. The patch section is scanned **only** for hunk content.

### Why not parse `diff --git a/… b/…`?

Because it cannot be parsed correctly in general:

```
diff --git a/some dir/b -> c.txt b/some dir/b -> c.txt
diff --git "a/t\303\274rk\303\247e.txt" "b/t\303\274rk\303\247e.txt"
```

- With spaces in a path there is **no safe way** to find the boundary between the two paths —
  the literal sequence ` b/` can occur inside a filename.
- Non-ASCII names are quoted with octal escapes.

`--format=` is mandatory: without it the output begins with the commit message and the parser
silently returns an empty list.

### Consequences

- Records are matched to patch blocks **by order**, verified across 700 real commits. When the
  counts disagree (`--ignore-blank-lines` produces this: the file stays in the raw section but
  emits no patch block) the parser falls back to matching by the blob ids on the `index` line,
  and raises `DiffParseException` if that also fails. Attaching a hunk to the wrong file would
  show the user another file's changes.
- `DiffHunk` lists may legitimately be **empty** (100% rename, mode-only change, empty new
  file, binary).
- `--numstat` rides along at no extra cost and gives line counts even for files whose content
  is skipped.

---

## Decision 2 — Word-level diff is computed locally, not by `git --word-diff`

The plan called for `git diff --word-diff=porcelain` as "free and correct". It is neither.

Measured: for an **added or removed blank line**, git emits a bare `~` and the output carries
**no indication of which side it belongs to**. Reconstructing lines from that output put
**5,701 lines on the wrong side** across 150 commits of `git/git`. A character-level regex
(`--word-diff-regex=.`) fixes a separate spurious-space problem but not this one. Word diff
also *replaces* the line-based output, so it costs an extra `git` run.

Instead `InlineDiff` computes segments locally from the exact line texts the parser already
produced. There is no fidelity risk: the input is already correct.

**Line pairing was adapted from GitExtensions** (`GitUI/Editor/Diff/LinesMatcher.cs`): anchor
on the pair sharing the most word length, then recurse before and after it. Our first
implementation paired lines in order — which is only GitExtensions' *fallback* path — and
compared the wrong lines whenever the two sides had different line counts.

**The same pairing drives the side-by-side layout** (`SideBySideDiff`). Writing a second
matcher would have allowed the highlighted pair and the side-by-side pair to disagree — two
contradictory answers on one screen.

---

## Decision 3 — Output is decoded losslessly; content is decoded separately

`git diff` output is **not in a single encoding**. Headers are ASCII, but line contents are
the **bytes of the file**; git does not transcode them the way it transcodes commit messages
(`i18n.logOutputEncoding`). Decoding the whole stream as UTF-8 corrupts Latin-5 (or any
non-UTF-8) content **silently**.

So the process output is read losslessly (Latin-1 byte↔char), and content is re-decoded with
`DiffOptions.ContentEncoding`. The approach was taken from GitExtensions' `PatchProcessor`.

Two related notes:

- `Encoding.GetEncoding("ISO-8859-9")` **throws** on .NET — legacy code pages are not
  registered by default. `TextEncodings` registers `CodePagesEncodingProvider` and returns
  `null` for unknown names instead of throwing. No NuGet package is needed; the provider ships
  with the framework.
- A trailing `\r` on CRLF files is **kept** in the model (Phase 05 must hand the patch back to
  `git apply` byte-for-byte) and trimmed only for display.

---

## Decision 4 — The size guard counts changed lines, not bytes

Measured: a 12.7 MB text file produces a 439-byte patch when one line changes, and a **23 MB**
patch when all of it changes. The danger is in the size of the *change*, not the size of the
*file* — so the limit is `MaximumChangedLines`, read from `--numstat` before any content is
materialised. Line counts therefore stay correct in the file list even when content is skipped.

The initial value (20,000) was chosen before a viewer existed. Once the viewer could be
measured it turned out to be too conservative: a real 43,671-line diff (`po/zh_CN.po` between
`v2.20.0` and `v2.45.0`) turns into rows in **202 ms**, retains 45 MB, and scrolls at
**0.7 ms/frame**. The limit is now 50,000; the 800,000-line case that motivated the guard is
still blocked.

`MaximumOutputBytes` remains as a last-resort valve: per-file limits do not bound the total of
thousands of medium files. When it trips the output is **not parsed at all** — parsing half of
a truncated stream would silently produce incomplete data.

---

## Rejected alternatives

| Alternative | Why rejected |
|---|---|
| LibGit2Sharp diff | ADR-0002: git is accessed only through the CLI |
| Parsing `diff --git` headers | Not parseable in general (spaces, octal-escaped names) |
| `git diff --word-diff` | Loses the side of blank lines; 5,701 wrong lines in 150 commits |
| `-m` for merge diffs | Emits one section per parent, breaking the single-list assumption; `<merge>^N` is used instead |
| Two-pass read (metadata, then content) | Twice the cost for the same result |
| `core.bigFileThreshold` | Makes "too large" and "actually binary" indistinguishable |
| `:(exclude)` for large files | Drops the file from the list entirely; the user should still see that it changed |
| `git diff --ignore-case` | Does not exist |

---

## Status of related work

- Syntax highlighting is **deferred** to Phase 08 (`P08-T24`). GitExtensions does not implement
  its own highlighter either — it vendors `ICSharpCode.TextEditor` and layers diff colouring on
  top via `IHighlightingStrategy`. Our viewer is a virtualised `ListBox` over `DiffSegment`
  runs, so adopting an editor component would mean rewriting the view; that is an
  architectural decision, not a colour change.
- `IDiffReader` has **no pathspec support** yet: a single file's diff cannot be requested. Phase
  07 (blame, file history) will need it.
