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
#
# ─────────────────────────────────────────────────────────────────────────────
# CI AND BUILD FILES ARE CHECKED MORE STRICTLY
#
# In workflow and build files the exemption above does not apply: there is no fixture
# data and no translation work in them, so a quoted string is not a citation, it is a
# message someone will read in a CI log. The release workflow used to print
# `::error::Sürüm boş` and version.sh `HATA: sürüm okunamadı`; both were translated by
# hand, and nothing stopped them coming back.
#
# The two files that are ABOUT Turkish are exempt by path: this script carries the
# alphabet as its own pattern, and the i18n tooling produces the Turkish locale.

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

# CI and build files: the WHOLE line is checked, strings included. What is written there
# is not fixture data, it is what a person reads in a CI log.
CI_GLOBS = (
    (".github/workflows", "*.yml"),
    (".github/workflows", "*.yaml"),
    ("build", "*.sh"),
    ("build", "*.py"),
    ("tools", "*.py"),
)

# Exempt by path: these two carry the Turkish alphabet as data, not as prose.
CI_EXEMPT = ("build/check-comments.sh", "tools/i18n/")

ci_findings = []

for root, pattern in CI_GLOBS:
    directory = pathlib.Path(root)

    if not directory.is_dir():
        continue

    for path in sorted(directory.rglob(pattern)):
        text = str(path).replace("\\", "/")

        if not path.is_file() or any(exempt in text for exempt in CI_EXEMPT):
            continue

        for number, line in enumerate(
                path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            if any(character in line for character in TURKISH):
                ci_findings.append((path, number, line.strip()[:88]))

if ci_findings:
    print(f"Turkish characters in CI/build files: {len(ci_findings)}\n", file=sys.stderr)

    for path, number, text in ci_findings[:20]:
        print(f"  {path}:{number}  {text}", file=sys.stderr)

    if len(ci_findings) > 20:
        print(f"  … and {len(ci_findings) - 20} more", file=sys.stderr)

    print(
        "\nWorkflow and build files are English throughout — comments AND the messages\n"
        "they print. A CI log is read by whoever is on call, not only by the author.",
        file=sys.stderr)
    sys.exit(1)

print("OK: no Turkish found in comments.")
print("OK: no Turkish found in CI/build files.")
PY
