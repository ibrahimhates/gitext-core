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

The version is **never written by hand into any file**. It is derived from the Git tag by
[MinVer](https://github.com/adamralph/minver), which runs as a build-time-only package
(`PrivateAssets="all"`) referenced once from `Directory.Build.props` for every project.
This makes the "we tagged a release but the csproj still says the old version" class of mistake
impossible.

> **Superseded (P10-T01):** an earlier revision of this ADR said the version was passed with
> `-p:Version=`. That no longer works and is now actively wrong — see the measurement below.

**Why MinVer and not Nerdbank.GitVersioning** (measured, P10-T00): Nerdbank survives a shallow
clone and builds ~32 ms faster, but it requires `version.json` to carry MAJOR.MINOR **written by
hand** — a direct violation of the rule above. The build-time difference is within measurement
noise. The shallow-clone weakness is closed by `fetch-depth: 0` plus an explicit pre-publish check.

**Three measured traps**, each of which fails *silently* — green build, green tests, wrong version:

| Trap | What happens | Guard |
|---|---|---|
| `v` prefix not recognised | MinVer logs *"Ignoring non-version tag"* for `v1.0.0` and falls back to `0.0.0-alpha.0` | `MinVerTagPrefix=v` in `Directory.Build.props` |
| Shallow clone | `actions/checkout` defaults to `fetch-depth: 1`; no tags arrive, version becomes `0.0.0-alpha.0` | `fetch-depth: 0` **and** `build/version.sh --check` before packaging |
| `-p:Version=` is overridden | MinVer computes the version in a target and overwrites the property; `-p:Version=7.7.7` produced `1.0.1-alpha.0.1` | Use `MinVerVersionOverride` — the only override MinVer honours |

**The single source of truth is `build/version.sh`.** Packaging scripts ask MSBuild for the value
that gets embedded in the binary rather than computing their own. Two sources — a script deriving
the version and the compiler embedding another — drift eventually, and the result is a package
named `1.0.0` containing a binary that reports `0.9.1`. `package.sh` verifies the two match by
running `gitext-core --version` on the freshly published output and comparing.

**Sabotage verification:** disabling MinVer entirely (`MinVerSkip=true`) initially left every test
green — the SDK's fallback version `1.0.0` is valid semver, consistent across assemblies, and even
carries the commit sha. Versioning could have silently switched off and the application would have
reported `gitext-core 1.0.0` with nothing objecting. `Directory.Build.props` therefore embeds
MinVer's own `MinVerVersion` as assembly metadata, and a test asserts it is present and matches.

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
