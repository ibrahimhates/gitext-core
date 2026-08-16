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
#
# QUOTED SPANS INSIDE A COMMENT are exempt for the same reason. An English sentence often
# has to cite the Turkish input a measurement used — `şğüıöç.txt` in the path-quoting
# finding, "Türkçe" in the encoding one, <c>Ölçüm</c> in the --author regex one. Naming the
# real input is what makes those notes verifiable; paraphrasing it would lose the finding.
# So `…`, "…" and <c>…</c> spans are stripped before the check, and only the prose around
# them has to be English.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

python3 - <<'PY'
import pathlib
import re
import sys

TURKISH = "ğüşıöçĞÜŞİÖÇ"
ROOTS = ("src", "tests", "benchmarks")

# Spans that quote something verbatim: an inline-code tag, a backtick span, a quoted
# string. What is inside them is the cited input, not prose.
QUOTED = re.compile(r"<c>.*?</c>|`[^`]*`|\"[^\"]*\"")

findings = []

for root in ROOTS:
    directory = pathlib.Path(root)

    if not directory.is_dir():
        continue

    for path in sorted(directory.rglob("*")):
        if path.suffix not in (".cs", ".axaml") or not path.is_file():
            continue

        if "obj/" in str(path) or "bin/" in str(path):
            continue

        text = path.read_text(encoding="utf-8", errors="replace")

        # In XAML, the comment is a <!-- --> block rather than a line prefix, so the lines
        # inside one are found first and then checked the same way.
        inside = set()

        if path.suffix == ".axaml":
            for match in re.finditer(r"<!--.*?-->", text, re.S):
                start = text.count("\n", 0, match.start()) + 1
                end = text.count("\n", 0, match.end()) + 1
                inside.update(range(start, end + 1))

        for number, line in enumerate(text.splitlines(), 1):
            stripped = line.lstrip()

            if not (stripped.startswith(("//", "///")) or number in inside):
                continue

            prose = QUOTED.sub("", line)

            if any(character in prose for character in TURKISH):
                findings.append((path, number, stripped[:88]))

if findings:
    print(f"Comments containing Turkish characters: {len(findings)}\n", file=sys.stderr)

    for path, number, text in findings[:20]:
        print(f"  {path}:{number}  {text}", file=sys.stderr)

    if len(findings) > 20:
        print(f"  … and {len(findings) - 20} more", file=sys.stderr)

    print(
        "\nComments are written in English (see CONTRIBUTING.md). String literals are\n"
        "exempt, and so are quoted spans inside a comment (`…`, \"…\", <c>…</c>) — a\n"
        "measurement note may cite the Turkish input it used.",
        file=sys.stderr)
    sys.exit(1)

print("OK: no Turkish found in comments.")
PY
