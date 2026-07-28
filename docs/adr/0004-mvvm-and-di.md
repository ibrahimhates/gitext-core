# ADR-0004 — MVVM and Dependency Injection

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 |
| **Decision** | CommunityToolkit.Mvvm + Microsoft.Extensions.DependencyInjection |

---

## Context

Avalonia works through XAML data binding and expects an MVVM foundation. We also need a way to get
`GitExt.Core` services into view models.

Most of this application's UI state will be **large, frequently changing lists** — commit rows,
file lists, diff lines. The cost of property-change notification matters.

---

## Decision

### MVVM: `CommunityToolkit.Mvvm`

A library that generates code at **compile time** from `[ObservableProperty]` and `[RelayCommand]`
attributes, with essentially zero runtime cost.

**Why:**
- Source generators — no reflection, no `INotifyPropertyChanged` boilerplate, no extra allocation
  at runtime.
- Small and focused. It is a helper, not a framework; it does not take over the application's
  architecture.
- Maintained by Microsoft, mature, widely used.
- Shallow learning curve — a new contributor picks it up in an hour.

**Rejected:**

| Alternative | Why rejected |
|---|---|
| **ReactiveUI** | Powerful but heavy. Steep Rx learning curve, unreadable stack traces, difficult debugging. A Git client does not need that level of reactive machinery, and it raises the contributor barrier for little return. |
| **Prism / MvvmCross** | Overkill at this scale. We do not need a module system or a navigation framework. |
| **Hand-written `INotifyPropertyChanged`** | Hundreds of lines of repetition and inevitable mistakes. Source generators already solve this. |
| **No MVVM (code-behind)** | Untestable. |

### DI: `Microsoft.Extensions.DependencyInjection`

Standard, lightweight, familiar to everyone in the .NET ecosystem, and integrates naturally with
`Microsoft.Extensions.Logging` and `Microsoft.Extensions.Configuration`.

**Composition root:** `GitExt.Desktop/Program.cs` only. Service registration happens nowhere else.

**Service Locator is banned.** No class takes `IServiceProvider` and resolves services from it.
Dependencies arrive through the constructor. The only exception is view-model factories, which sit
behind typed factory interfaces or `Func<T>`.

---

## Lifetimes

| Scope | Example | Lifetime |
|---|---|---|
| Application-wide | process runner, settings, logging | Singleton |
| Per repository | repository context, log reader, status reader | Scoped — created when a repository opens, disposed when it closes |
| Transient | dialog view models | Transient |

**The per-repository scope matters.** The application will eventually have multiple repositories
open at once. Every repository-bound service lives in that repository's scope and is cleaned up —
along with its caches and running tasks — when the repository closes. If this is not established
from the beginning, multi-repository support cannot be added later without a rewrite.

---

## Threading rules

This is where an MVVM design produces the most bugs, so the rules are explicit:

1. **No Git process is ever run on the UI thread.** No exceptions.
2. `GitExt.Core` is **entirely thread-agnostic** — it knows nothing about `Dispatcher` or
   `SynchronizationContext`. All of its async methods use `ConfigureAwait(false)`.
3. Marshalling back to the UI happens in `GitExt.UI`, explicitly, via `Dispatcher.UIThread`.
4. Long-running work must be cancellable; each repository scope cancels its own
   `CancellationTokenSource` on close.
5. Large lists are pushed to the UI in **batches**. Raising one `CollectionChanged` event per
   commit would kill the application at 100k commits.

---

## Consequences

- View models derive from `ObservableObject` and are `partial` (required by the source generator).
- `GitExt.UI.Tests` exercises view models against fake `GitExt.Core` services, without starting a
  real Git process.
- View ↔ view model mapping is established by convention (`XxxView` ↔ `XxxViewModel`) or explicit
  `DataTemplate`s.
