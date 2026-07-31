# Architecture Decision Records

These documents record the significant technical decisions behind gitext-core, together with the
alternatives that were considered and rejected.

**Read the relevant record before proposing a change to one of these areas.** Most "why is it
done this way?" questions are answered here, and several of these decisions are enforced by the
build rather than by code review.

| ADR | Subject | Decision | Status |
|---|---|---|---|
| [0001](./0001-ui-framework.md) | UI framework | Avalonia 12.1 | Accepted |
| [0002](./0002-git-backend.md) | How we talk to Git | Drive the `git` CLI as a subprocess | Accepted |
| [0003](./0003-solution-structure.md) | Solution and project layout | 4 production + 3 test projects, one-way dependencies | Accepted |
| [0004](./0004-mvvm-and-di.md) | MVVM and dependency injection | CommunityToolkit.Mvvm + Microsoft.Extensions.DI | Accepted |
| [0005](./0005-licensing.md) | Licensing | GPL-3.0-or-later | Accepted |
| [0006](./0006-versioning-and-dependencies.md) | Versioning and dependencies | SemVer + Central Package Management + committed lock files | Accepted |
| [0007](./0007-commit-graph-layout.md) | Commit graph layout | Straight-branch lanes, fed by `--topo-order` | Accepted |
| [0008](./0008-diff-pipeline.md) | Diff pipeline | One `git` call, paths from `--raw`, local word diff, lossless decoding | Accepted |

## Decisions enforced by the build

Two of these are not merely conventions — breaking them fails the build:

- **ADR-0003:** `GitExt.Core` and `GitExt.Graph` may not reference any UI package.
  A violation produces error **`GITEXT001`**.
- **ADR-0006:** CI restores with `--locked-mode`. A stale `packages.lock.json` fails the build.

## Adding a new record

Significant technical choices get their own record: copy the structure of an existing one, take
the next number, and state the rejected alternatives honestly — the rejected options are the most
useful part of these documents.

Records are immutable once accepted. If a decision is reversed, write a new record that supersedes
the old one and update the old one's status; do not rewrite history.
