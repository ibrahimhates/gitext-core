# ADR-0003 — Solution and Project Structure

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 |
| **Decision** | 4 production + 3 test projects, with strictly one-way dependencies |

---

## Context

This is a long-lived project. If the structure is wrong at the start, business logic leaks into the
UI, tests become impractical, and the option of changing the UI framework (the fallback plan in
ADR-0001) is lost.

---

## Decision

```
gitext-core.slnx
│
├── src/
│   ├── GitExt.Core/        →  (no dependencies)
│   ├── GitExt.Graph/       →  GitExt.Core
│   ├── GitExt.UI/          →  GitExt.Core, GitExt.Graph, Avalonia
│   └── GitExt.Desktop/     →  GitExt.UI
│
└── tests/
    ├── GitExt.Core.Tests/  →  GitExt.Core
    ├── GitExt.Graph.Tests/ →  GitExt.Graph
    └── GitExt.UI.Tests/    →  GitExt.UI, Avalonia.Headless
```

The arrows point one way and are never reversed.

### Responsibilities

#### `GitExt.Core` — the layer that talks to Git

- Process execution (ADR-0002)
- Command wrappers (log, status, refs, diff, objects, …)
- Output parsers — the riskiest code in the project
- Domain models: `CommitInfo`, `GitRef`, `FileStatus`, `DiffHunk`, …
- Repository discovery and validation

**Hard rule:** no UI package references. Not `Avalonia`, not `ReactiveUI`, nothing. This project
must be usable from a console application. **Enforced by the build** — see below.

#### `GitExt.Graph` — commit DAG layout

- Lane assignment: which vertical lane each commit occupies
- Edge routing: the geometry of parent–child connections
- Colour assignment
- Incremental layout, for endless scrolling

**Its output is pure data:** `(row, lane, colour, edges)`. No pixels, no `DrawingContext`, no
Avalonia.

**Why a separate project?** This is the hardest algorithm in the application, and it must be
**testable without a UI**. We can feed it text-based DAG fixtures and assert on the expected
layout. Proving the algorithm correct before drawing anything removes most of the risk from that
work.

#### `GitExt.UI` — the Avalonia interface

Views (`.axaml`), view models, custom controls (including the commit graph renderer), themes,
styles, converters, and UI services (dialogs, notifications, shortcut routing).

The only library project that depends on Avalonia.

#### `GitExt.Desktop` — the entry point

`Program.Main`, `AppBuilder` configuration, the DI composition root, platform bootstrap,
single-instance handling, command-line arguments, application icons and publish profiles.

**Why separate from `GitExt.UI`?** Keeping the publish/packaging target apart from the UI library
lets `GitExt.UI` be tested headlessly, and makes it cheap to add a second shell later (a CLI
diagnostic mode, for instance).

---

## Enforcement

The layering rule is not a convention — it is a build failure.

`build/NoUiDependencies.props` is imported by `GitExt.Core` and `GitExt.Graph`. It inspects the
resolved reference closure and raises error **`GITEXT001`** if any Avalonia, SkiaSharp, HarfBuzz
or ReactiveUI assembly appears. Because it examines the resolved closure, **transitive** references
are caught too.

A second, independent check exists in the test suite (`LayeringTests` in both projects), which
inspects the compiled assembly's references at runtime. If someone removes or bypasses the MSBuild
target, the tests still catch it.

We do not rely on discipline. We rely on the compiler.

---

## Alternatives considered

### A single project — rejected
Fast to start, impossible six months later. The separation between UI and Git logic must be
enforced by the compiler, not by good intentions.

### More projects (Core.Abstractions, Core.Parsing, UI.Controls, …) — rejected
Premature subdivision at this size. Build time and navigation cost exceed the benefit. If a project
genuinely outgrows itself, it gets split *then*.

### Folding `GitExt.Graph` into `GitExt.Core` — rejected
For the testability reason above. Keeping it separate also stops the algorithm from being polluted
by UI concerns.

---

## Additional structural choices

- **Solution format:** `.slnx` (the newer XML-based format). Unlike the classic `.sln` it does not
  generate merge conflicts.
- **`Directory.Build.props`** at the root: `TargetFramework`, `Nullable=enable`,
  `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`, `LangVersion=latest` in one place.
- **`Directory.Packages.props`** at the root: central package version management (ADR-0006).
- **`.editorconfig`** at the root: code style is enforced by the compiler and is not a discussion
  topic.
- **Test framework:** xUnit v3 with Shouldly for assertions. FluentAssertions is deliberately not
  used, because of its v8 licensing change.

---

## Consequences

- Adding a Git feature always follows the same path: command + parser + test in `Core` → layout in
  `Graph` if needed → view model in `UI` → view.
- A UI framework change would be confined to `GitExt.UI` and `GitExt.Desktop`.
- Layering violations surface as build errors, not as review comments.
