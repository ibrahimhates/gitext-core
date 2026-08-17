#!/usr/bin/env bash
#
# Version derivation — the shared source for ALL packaging scripts (P10-T01, ADR-0006).
#
# Usage:
#   source build/version.sh
#   VERSION="$(gitext_version)"
#
# or directly:
#   build/version.sh          → prints the version
#   build/version.sh --check  → verifies the version is releasable
#
# ─────────────────────────────────────────────────────────────────────────────
# WHY THIS FILE EXISTS
#
# The version is derived from the git tag via MinVer. If scripts computed it ON
# THEIR OWN (git describe, reading from a file…) that creates a second source, and
# two sources eventually diverge: the package says 1.0.0, the binary inside says
# 0.9.1. So the version is asked from MSBuild — the very value embedded in the binary.
#
# ⚠️ MEASURED (P10-T00) — MinVer OVERRIDES the `-p:Version=` parameter. Even if
# `-p:Version=7.7.7` is passed, the output is still the version derived from the tag,
# with no warning. The old package.sh used exactly this; the moment MinVer was
# added, that parameter silently became a no-op. MinVerVersionOverride is the ONLY
# valid way to impose a version from outside.

set -euo pipefail

_gitext_root() {
    cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd
}

# Unreleasable version: the default MinVer produces when there is no tag.
# Seeing this in a package's name means the tag never arrived.
GITEXT_UNRELEASABLE_PREFIX="0.0.0-alpha.0"

# What a version is allowed to look like — SemVer 2.0, which is what MinVer accepts.
GITEXT_VERSION_PATTERN='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'

# ─────────────────────────────────────────────────────────────────────────────
# The override's `v` prefix is stripped HERE, where the value enters.
#
# 🔴 MEASURED — what a person types is the TAG NAME. ADR-0006 defines tags with a `v`
# prefix, the release workflow's input asks for a "version", so `v0.1.0` is the natural
# thing to write. MinVer does not accept it:
#
#   MinVer : error MINVER1005: Invalid version override 'v0.1.0'
#
# and the packaging job dies at `dotnet publish` — after the version job went green, after
# the tests passed, on all three platforms at once. The value is normalised at this single
# point instead of at each of the four packaging scripts.
#
# ⚠️ It is EXPORTED, not just assigned. The packaging scripts source this file and then
# start `dotnet`; a plain assignment would stay in the shell and the child process would
# still see the original value. And it is done at load time, not inside a function: the
# functions are called as `$(…)`, i.e. in a SUBSHELL, from which an export never reaches
# the caller.
if [ -n "${MINVER_VERSION_OVERRIDE:-}" ]; then
    MINVER_VERSION_OVERRIDE="${MINVER_VERSION_OVERRIDE#v}"
    export MINVER_VERSION_OVERRIDE
fi

gitext_version() {
    local root
    root="$(_gitext_root)"

    # If MinVerVersionOverride is given, obey it — this is the only way to produce
    # a tagless trial version in CI via workflow_dispatch. The `v` prefix has already
    # been stripped as the file was loaded; what is left has to be checked, because
    # MinVer's own complaint arrives minutes later, from inside MSBuild's output.
    if [ -n "${MINVER_VERSION_OVERRIDE:-}" ]; then
        if ! [[ "$MINVER_VERSION_OVERRIDE" =~ $GITEXT_VERSION_PATTERN ]]; then
            cat >&2 <<EOF
ERROR: '$MINVER_VERSION_OVERRIDE' is not a valid version.

The value has to be SemVer: MAJOR.MINOR.PATCH, optionally followed by a pre-release
identifier — 0.1.0, 1.2.3-rc.1. A leading 'v' is accepted (it is the tag's form and is
stripped); anything else is refused here, because MinVer would only refuse it later,
in the middle of the packaging step.
EOF
            return 1
        fi

        printf '%s\n' "$MINVER_VERSION_OVERRIDE"
        return 0
    fi

    # Ask MSBuild: the same value that will be embedded in the binary.
    # `-t:MinVer` is required — the version is computed inside a target, not
    # evaluated as a property; if the target doesn't run, it returns empty/default.
    #
    # MSBuild returns plain text when a single property is requested (JSON if
    # multiple are requested). Measured: the output was exactly "0.0.0-alpha.0.49\n".
    #
    # ⚠️ MEASURED — the MinVer TARGET comes from the NuGet package, so it only exists
    # after a restore. On a fresh checkout without one, MSBuild fails with
    # "MSB4057: The target 'MinVer' does not exist in the project" — which is exactly
    # how the release workflow broke: its version job never restored.
    local version stderr_file
    stderr_file="$(mktemp)"

    version="$(dotnet msbuild "$root/src/GitExt.Desktop/GitExt.Desktop.csproj" \
        -t:MinVer -getProperty:MinVerVersion -nologo 2>"$stderr_file" | tr -d '\r\n')" || {
        # ⚠️ MSBuild's own message is PASSED THROUGH. It used to be discarded to
        # /dev/null and all that reached the log was "could not read version" —
        # which says nothing about what to do next.
        echo "ERROR: could not read version from MSBuild:" >&2
        cat "$stderr_file" >&2
        echo "Hint: has 'dotnet restore' been run? The MinVer target comes from the package." >&2
        rm -f "$stderr_file"
        return 1
    }

    rm -f "$stderr_file"

    if [ -z "$version" ]; then
        echo "ERROR: MinVerVersion returned empty. The MinVer package may be missing." >&2
        return 1
    fi

    printf '%s\n' "$version"
}

# Verifies the version is actually releasable.
#
# ⚠️ MEASURED (P10-T00) — actions/checkout defaults to `fetch-depth: 1`; in a shallow
# clone tags NEVER arrive and MinVer silently produces 0.0.0-alpha.0. Without this
# guard, pushing the `v1.0.0` tag would publish a version named "0.0.0-alpha.0" and
# no step would turn red. A silent wrong version is far more expensive than a broken build.
gitext_require_releasable_version() {
    local version="${1:-}"

    # ⚠️ MEASURED — written as `[ -n "$version" ] || version="$(gitext_version)"`, the `||`
    # SWALLOWS the failure: set -e does not apply on the right-hand side of `||`, the
    # assignment takes the exit code of the assignment itself, and the function carried on
    # with an EMPTY version and returned 0. The caller then printed "OK: " and went green
    # on a version that could not be read. The failure has to be propagated explicitly.
    if [ -z "$version" ]; then
        version="$(gitext_version)" || return 1
    fi

    if [ -z "$version" ]; then
        echo "ERROR: version came back empty." >&2
        return 1
    fi

    case "$version" in
        "$GITEXT_UNRELEASABLE_PREFIX"*)
            cat >&2 <<EOF
ERROR: version '$version' — this is NOT a releasable version.

MinVer could not find a valid version tag and fell back to the default. Possible causes:

  1. Shallow clone (most common): tags were not fetched in CI.
     → Add 'fetch-depth: 0' to the actions/checkout step.
  2. There is no 'v*' tag in the repository at all.
     → git tag v1.0.0 && git push --tags
  3. The tag was pushed without the 'v' prefix (ADR-0006 defines the 'v' prefix).

If you want to produce a tagless trial version, state it explicitly:
  MINVER_VERSION_OVERRIDE=1.0.0-test build/linux/package.sh
EOF
            return 1
            ;;
    esac

    printf '%s\n' "$version"
}

# If run directly (not sourced)
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
    case "${1:-}" in
        --check)
            # No `local` (outside a function), and the assignment and command are on
            # separate lines: the `v="$(...)"` form SWALLOWS the subshell's exit code
            # and set -e does not kick in.
            v="$(gitext_require_releasable_version)" || exit 1
            echo "OK: $v"
            ;;
        *) gitext_version ;;
    esac
fi
