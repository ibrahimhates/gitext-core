#!/usr/bin/env bash
#
# Release notes generation (P10-T03).
#
# Usage:
#   build/release-notes.sh v1.0.0            # v0.9.0..v1.0.0 range
#   build/release-notes.sh v1.0.0 v0.9.0     # give the range explicitly
#
# ─────────────────────────────────────────────────────────────────────────────
# WHY GITHUB'S AUTOMATIC NOTES AREN'T ENOUGH
#
# ⚠️ MEASURED (P10-T03) — in this repo, 50 of 53 commits were pushed DIRECTLY to
# main; there are only 3 PRs, all three from dependabot. GitHub's
# `generate_release_notes` feature compiles notes from PR titles. In a single-developer,
# PR-less flow it produces an almost-empty release note — "* Bump actions/checkout
# from 4 to 7" and nothing else.
#
# This script generates notes from COMMITS instead. Conventional Commits is used
# (measured: 44 of 53 commits comply; of the 6 non-compliant ones, 3 are dependabot,
# 2 are the project's first-day commits), so the commits themselves are already
# in a categorizable shape.
#
# Non-compliant commits are NOT DROPPED: they are listed under the "Other" heading.
# Silently dropping them would hide that the release note is incomplete — the change
# a user is looking for could be exactly the commit that doesn't match the format.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TAG="${1:-}"
PREVIOUS="${2:-}"

if [ -z "$TAG" ]; then
    echo "Usage: $0 <tag> [previous-tag]" >&2
    exit 2
fi

REPO_URL="https://github.com/ibrahimhates/gitext-core"

# ⚠️ The tag is used as a git REVISION below. On a manual (workflow_dispatch) run it does not
# exist yet, and every `git log` here then fails with "unknown revision" — the notes come out
# empty while the log fills with fatal: lines. MEASURED with a made-up tag. Fall back to HEAD,
# which is exactly the commit such a run is building.
if git rev-parse --verify --quiet "${TAG}^{commit}" >/dev/null; then
    REVISION="$TAG"
else
    REVISION="HEAD"
    echo "note: tag '$TAG' does not exist yet; notes are generated from HEAD." >&2
fi

# If the previous tag isn't given: the nearest version tag before this one.
if [ -z "$PREVIOUS" ]; then
    PREVIOUS="$(git describe --tags --abbrev=0 "${REVISION}^" 2>/dev/null || true)"
fi

if [ -n "$PREVIOUS" ]; then
    RANGE="${PREVIOUS}..${REVISION}"
else
    # First release: the entire history.
    RANGE="$REVISION"
fi

# Formats a commit: "- subject ([sha](url))"
emit() {
    local pattern="$1" heading="$2" body found=0

    while IFS='|' read -r sha subject; do
        [ -n "$sha" ] || continue

        # "feat(ui): do this" → "do this" (type and scope are already in the heading)
        local text="${subject#*: }"

        if [ "$found" -eq 0 ]; then
            printf '\n### %s\n\n' "$heading"
            found=1
        fi

        printf -- '- %s ([%s](%s/commit/%s))\n' "$text" "${sha:0:7}" "$REPO_URL" "$sha"
    done < <(git log --no-merges --pretty='%H|%s' "$RANGE" | grep -E "\|${pattern}" || true)

    return 0
}

printf '## %s\n' "$TAG"

if [ -n "$PREVIOUS" ]; then
    printf '\n[Compare with %s](%s/compare/%s...%s)\n' \
        "$PREVIOUS" "$REPO_URL" "$PREVIOUS" "$TAG"
fi

# Breaking changes AT THE TOP: the first thing the user needs to see.
# The `feat!:` or `feat(ui)!:` form (Conventional Commits § breaking change).
emit '(feat|fix|perf|refactor|build)(\([a-z0-9-]+\))?!: ' '⚠️ Breaking changes'

# Reverts near the top: hearing that something was reverted is more urgent than
# hearing about a new feature — the user may have relied on it in the previous release.
emit 'revert(\([a-z0-9-]+\))?: '   'Reverted'

emit 'feat(\([a-z0-9-]+\))?: '     'New features'
emit 'fix(\([a-z0-9-]+\))?: '      'Fixes'
emit 'perf(\([a-z0-9-]+\))?: '     'Performance'
emit 'refactor(\([a-z0-9-]+\))?: ' 'Internal'
emit 'docs(\([a-z0-9-]+\))?: '     'Documentation'
emit '(build|ci)(\([a-z0-9-]+\))?: ' 'Build and CI'
emit 'test(\([a-z0-9-]+\))?: '     'Tests'

# `chore:` and `style:` are DELIBERATELY not listed: by definition they don't
# contain a user-visible change (dependency upgrade, formatting). Filling the
# release note with these would bury the lines that need to be read.
# EVERY other commit is shown — including ones that don't match the format (below).

# Non-compliant commits — not dropped, made visible.
{
    others="$(git log --no-merges --pretty='%H|%s' "$RANGE" \
        | grep -vE '\|(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9-]+\))?!?: ' || true)"

    if [ -n "$others" ]; then
        printf '\n### Other\n\n'
        while IFS='|' read -r sha subject; do
            [ -n "$sha" ] || continue
            printf -- '- %s ([%s](%s/commit/%s))\n' "$subject" "${sha:0:7}" "$REPO_URL" "$sha"
        done <<< "$others"
    fi
}

printf '\n---\n\n'
printf 'Installation instructions: [README](%s#installation)\n' "$REPO_URL"
printf "\nThe integrity of this release's outputs can be verified with \`SHA256SUMS\`.\n"
