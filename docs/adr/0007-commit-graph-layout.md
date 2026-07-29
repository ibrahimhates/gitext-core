# ADR-0007 — Commit Graph Layout

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-29 |
| **Decision** | Straight-branch lane assignment, fed by `git log --topo-order` |

---

## Context

The commit graph is this project's centrepiece — the thing GitExtensions users value most. Two
questions had to be settled before writing any code:

1. **Which layout style?** How are commits assigned to vertical lanes, and what happens to a
   branch's lane as history unfolds?
2. **In what order do commits arrive?** Lane assignment is a forward pass, so the input order
   determines whether the result is even correct.

Both were investigated against real repositories before implementation.

---

## Part 1 — Layout style

### Alternatives considered

**Curved branches** — maintain a list of active lanes; when a branch merges, later branches
shift left to fill the gap. Used by GitExtensions and SmartGit.

- ✅ Compact: fewer lanes for the same history.
- ❌ A branch wanders horizontally as unrelated branches merge, so the eye cannot follow it.

**Straight branches** — every commit of a branch stays in the same column. A lane is *blocked*
rather than reused when reusing it would make an edge overlap an unrelated commit. Used by
GitKraken; described in detail by [pvigier](https://pvigier.github.io/2019/05/06/commit-graph-drawing-algorithms.html).

- ✅ A branch is a straight vertical line, trivially followable.
- ✅ Lane positions do not move as the graph is scrolled.
- ❌ Wider graphs — more lanes are occupied at once.

### Decision: straight branches

The project's stated purpose is a graph that "makes tangled histories genuinely readable". A
branch that stays in one column serves that directly; compactness does not. Width is
manageable — and a lane cap can be introduced if real repositories prove otherwise.

The algorithm keeps a set of *forbidden* lanes per commit: lanes that, if used, would route an
edge through an unrelated commit. Selection prefers the lane of an existing child, falling back
to the leftmost non-forbidden free lane.

---

## Part 2 — Commit ordering

### The problem

Lane assignment is a single forward pass: when a commit is processed, the lanes reserved by its
already-placed children determine where it goes. **This requires every child to be processed
before its parents.** Otherwise a parent lands above its child and the edge between them points
upward — visibly broken.

### Measurement

`git log`'s default order is reverse chronological. It is tempting to assume that a parent, being
older, always comes after its child. **This is false**, and it was verified rather than assumed:

A repository was built with a branch whose commit dates are older than its merge base
(easily produced by rebases, imports, or clock skew). In default order:

```
64b8f98 2020-01-01 base            ← parent, line 2
7ad1722 2010-01-01 yan-COK-ESKI    ← its child, line 3
```

The parent is emitted **before** its child. Git's walker pops the newest pending commit by date,
and a skewed date puts the parent first.

Both `--date-order` and `--topo-order` eliminate the violation. `--topo-order` is preferred: it
additionally keeps a branch's commits contiguous instead of interleaving unrelated branches by
date, which is what makes the graph readable.

### The cost, and how it disappears

Correct ordering requires git to walk the whole graph before emitting anything, so the first
commit is no longer instant:

| Repository | Default order | `--topo-order` |
|---|---|---|
| 50 000 commits | 1 ms | 161 ms |
| 200 000 commits | 1 ms | 602 ms |

Extrapolating, 500 000 commits would cost roughly 1.5 s before the first row could be drawn —
tolerable but a large share of the performance budget.

**A `commit-graph` file removes the cost entirely:**

| 200 000 commits | First output |
|---|---|
| `--topo-order`, no commit-graph | ~600 ms |
| `--topo-order`, with commit-graph | **1 ms** |

Writing the file took 558 ms once and produced 12 MB. The generation numbers it stores let git
answer topological questions without walking history.

---

## Decision

**Feed the layout from `git log --topo-order`, and rely on the `commit-graph` file to keep it fast.**

Correctness is not negotiable: a graph that draws upward-pointing edges on repositories with
skewed dates is broken, and such repositories are common.

Regarding the `commit-graph` file:

- **Use it when present.** Git writes it automatically during `gc` on modern versions, so many
  repositories already have one.
- **When absent, offer to create it** — do not create it silently. Writing files into the user's
  repository without asking violates the project's principle of never surprising the user
  (ADR-0002 keeps us on the user's own `git`; the same respect applies to their repository state).
- Surface the difference honestly: a large repository without a commit-graph opens noticeably
  slower, and the user should know why and what fixes it.

---

## Consequences

- `GitExt.Graph` receives commits in topological order and may rely on **children always
  preceding parents**. This invariant must be asserted in tests, not assumed.
- The layout is computed incrementally in a forward pass; rows already emitted are never
  recomputed, so lanes stay stable while scrolling.
- Repositories without a `commit-graph` file have a slower first paint. This is a user-visible
  behaviour difference and belongs in the diagnostics output.
- Lane count is unbounded by default. If real repositories produce unusable widths, a cap plus
  an overflow indicator can be added — deferred until measurement justifies it.

---

## References

- [Commit Graph Drawing Algorithms — pvigier](https://pvigier.github.io/2019/05/06/commit-graph-drawing-algorithms.html)
- `git help log` — `--topo-order`, `--date-order`
- `git help commit-graph`
