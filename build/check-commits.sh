#!/usr/bin/env bash
#
# Commit message format check (P10-T03) — Conventional Commits.
#
# Usage:
#   build/check-commits.sh                 # origin/main..HEAD
#   build/check-commits.sh v0.9.0..HEAD    # give the range explicitly
#
# ─────────────────────────────────────────────────────────────────────────────
# Release notes are generated from commits (build/release-notes.sh), because this
# repo has no PR flow: measured, 50 of 53 commits were pushed straight to main. So
# the commit message format is not a style preference, it's the INPUT to the release
# notes. A badly formatted commit falls into the "Other" heading, where no one reads it.
#
# NOT APPLIED RETROACTIVELY: the rule applies from today onward. The 6 non-compliant
# commits in the past (3 dependabot, 2 the project's first commits, 1 "init") are
# left as-is — rewriting history would break far more than it fixes.

set -euo pipefail

RANGE="${1:-}"

if [ -z "$RANGE" ]; then
    if git rev-parse --verify --quiet origin/main >/dev/null; then
        RANGE="origin/main..HEAD"
    else
        RANGE="HEAD~1..HEAD"
    fi
fi

# Conventional Commits types. `!` marks a breaking change.
PATTERN='^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9.-]+\))?!?: .+'

failed=0
checked=0

while IFS='|' read -r sha subject; do
    [ -n "$sha" ] || continue
    checked=$((checked + 1))

    # Dependabot uses its own message format and we can't change that.
    case "$subject" in
        "Bump "*) continue ;;
    esac

    if ! printf '%s' "$subject" | grep -qE "$PATTERN"; then
        if [ "$failed" -eq 0 ]; then
            echo "Commits that don't match the Conventional Commits format:" >&2
            echo >&2
        fi
        printf '  %s  %s\n' "${sha:0:7}" "$subject" >&2
        failed=$((failed + 1))
    fi
done < <(git log --no-merges --pretty='%H|%s' "$RANGE" 2>/dev/null)

if [ "$failed" -gt 0 ]; then
    cat >&2 <<'EOF'

Expected format:  <type>[(scope)][!]: <summary>

  feat(ui): add lane colors to commit graph
  fix: wrong branch name shown in detached HEAD
  perf(core): string pool for commit reading
  feat(settings)!: settings file format changed     ← breaking change

Types: feat fix docs style refactor perf test build ci chore revert

This format is the input to the release notes (build/release-notes.sh) — commits
that don't match fall into the "Other" heading and go unread.
EOF
    exit 1
fi

echo "OK: $checked commits checked, all compliant."
