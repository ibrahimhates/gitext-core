# ADR-0006 — Versioning and Dependency Management

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-28 |
| **Decision** | SemVer + Central Package Management + committed lock files |

---

## Versioning

**Semantic Versioning 2.0.0** — `MAJOR.MINOR.PATCH`.

What that means for a desktop application specifically:

| Component | Increments when |
|---|---|
| **MAJOR** | A change breaks user data: the settings file format changes incompatibly, the keyboard scheme is reorganised, or a feature is removed |
| **MINOR** | A new feature, backward compatible |
| **PATCH** | A bug fix or performance improvement with no user-visible behaviour change |

**Before `v1.0.0`:** no compatibility is promised. A `0.MINOR` bump may contain breaking changes.
This is stated plainly in the README.

**Tags** are `v`-prefixed: `v0.1.0`, `v1.2.3`. The Git tag is the single source of truth.

**Pre-releases:** `v0.2.0-beta.1`, `v1.0.0-rc.1`.

### How the version reaches the binary

The version is **never written by hand into any file**. `Directory.Build.props` holds a base
version; the real version is derived from the Git tag and passed to the build with `-p:Version=`.
This makes the "we tagged a release but the csproj still says the old version" class of mistake
impossible.

---

## Dependency management

### 1. Central Package Management

All package versions live in a single `Directory.Packages.props` at the repository root.
`.csproj` files declare `<PackageReference Include="..." />` with **no version**.

**Why:** carrying different versions of the same package across seven projects is a classic source
of hard-to-diagnose runtime failures. One file means one source of truth, and an upgrade is a
one-line change.

### 2. Lock files are committed

`RestorePackagesWithLockFile=true`. The `packages.lock.json` files are part of the repository.

**Why:** it guarantees that the same commit builds with the same dependency graph on any machine
and in CI. A transitive dependency silently updating and breaking something is the most
frustrating class of bug in a long-lived project. CI restores with `--locked-mode`; if the lock
file is stale, **the build fails**.

> **Implementation note:** NuGet updates an existing `packages.lock.json` whenever one is present,
> regardless of `RestorePackagesWithLockFile`. A RID-specific restore (`publish -r linux-x64`)
> therefore writes RID-specific entries and trimming's `ILLink.Tasks` into the lock file, which
> then breaks the next RID-less `--locked-mode` restore with NU1004. To prevent this,
> `NuGetLockFilePath` redirects RID-specific restores into `obj/`, leaving the committed lock files
> untouched.

### 3. Versions are pinned exactly

No floating versions (`12.*`) and no ranges. Upgrading is a deliberate act, never an accident.

### 4. Policy for adding a dependency

Adding a NuGet package requires a decision, not a reflex:

1. Could we write this ourselves with reasonable effort? (Under ~200 lines, we probably should.)
2. Is the package actively maintained — any commits in the last 12 months?
3. Is the license compatible? (See ADR-0005: no proprietary packages.)
4. How many transitive dependencies does it drag in?
5. How much does it add to the self-contained publish size?

**Pre-approved baseline** (no discussion needed): Avalonia, CommunityToolkit.Mvvm,
`Microsoft.Extensions.*`, and the xUnit test stack.

### 5. Upgrade cadence

- **Security updates:** immediately.
- **Patch versions:** monthly, in a batch.
- **Minor versions:** at phase boundaries, in a commit of their own, followed by a smoke test.
- **Major versions:** deserve their own ADR (as an Avalonia 13 would).

Dependabot is enabled, with **auto-merge disabled** — particularly for Avalonia, because tests do
not catch UI regressions.

---

## Target framework

`net10.0` for all projects. A single target; no multi-targeting.

Common settings enforced in `Directory.Build.props`:

| Setting | Value | Why |
|---|---|---|
| `Nullable` | `enable` | Catch null-reference errors at compile time |
| `TreatWarningsAsErrors` | `true` | Warnings never accumulate. Enforced from day one, because turning it on later never actually happens |
| `ImplicitUsings` | `enable` | Less noise |
| `LangVersion` | `latest` | |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` is applied by the compiler |
| `GenerateDocumentationFile` | `true` | Required for IDE0005 (unnecessary `using`) to run during build |
| `InvariantGlobalization` | `true` | Smaller publish output, no ICU dependency |

> `GenerateDocumentationFile=true` would normally demand XML comments on every public member
> (CS1591). That warning is suppressed: the documentation file exists only to enable IDE0005.

---

## Consequences

- Adding a package touches **two** files: `Directory.Packages.props` (the version) and the relevant
  `.csproj` (the reference).
- If a restore changes `packages.lock.json`, the change **must be committed**, or CI fails.
- The version number is never edited by hand in any source file.
