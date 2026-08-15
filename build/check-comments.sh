#!/usr/bin/env bash
#
# Comment language check.
#
# Usage:
#   build/check-comments.sh
#
# ─────────────────────────────────────────────────────────────────────────────
# The codebase is written in one language: English. Identifiers, comments and commit
# messages alike, so that reading it does not require a second language.
#
# Comments used to be Turkish and were translated in bulk. Without a check, new Turkish
# comments creep back in one commit at a time and nobody notices until the mixture is
# large again.
#
# Only COMMENTS are checked. Turkish inside string literals is legitimate and common:
#   - test fixture data that exercises UTF-8 (author names, file names, commit messages)
#   - the Turkish translations themselves, in Locales/tr.json
#   - suppression justifications
# Those are deliberately left alone.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

python3 - <<'PY'
import pathlib
import sys

TURKISH = "ğüşıöçĞÜŞİÖÇ"
ROOTS = ("src", "tests", "benchmarks")

findings = []

for root in ROOTS:
    directory = pathlib.Path(root)

    if not directory.is_dir():
        continue

    for path in sorted(directory.rglob("*.cs")):
        if "obj/" in str(path) or "bin/" in str(path):
            continue

        for number, line in enumerate(
                path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            stripped = line.lstrip()

            if not stripped.startswith(("//", "///")):
                continue

            if any(character in line for character in TURKISH):
                findings.append((path, number, stripped[:88]))

if findings:
    print(f"Comments containing Turkish characters: {len(findings)}\n", file=sys.stderr)

    for path, number, text in findings[:20]:
        print(f"  {path}:{number}  {text}", file=sys.stderr)

    if len(findings) > 20:
        print(f"  … and {len(findings) - 20} more", file=sys.stderr)

    print(
        "\nComments are written in English (see CONTRIBUTING.md). String literals are\n"
        "exempt — only comment lines are checked.",
        file=sys.stderr)
    sys.exit(1)

print("OK: no Turkish found in comments.")
PY
