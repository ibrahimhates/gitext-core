#!/usr/bin/env python3
"""Collects the user-facing texts on the C# side (P11-T05).

    tools/i18n/extract-csharp.py --collect    # produces/updates catalog-cs.json
    tools/i18n/extract-csharp.py --report     # lists the strings not yet moved

⚠️ This script DOES NOT REWRITE AUTOMATICALLY the way the XAML one does. The reason is a
measurable difference: in XAML the text is the whole of an attribute and its context is
clear. In C# the same string can be `Error = "..."`, or inside `$"... {x} ..."`, or inside
`throw new(...)` — each needing a different transformation (the indexer? Format? leave it
alone?). A blind rewrite would silently produce wrong code.

The script's job is to guarantee that NO string is MISSED. The replacement is done by hand,
then checked with --report until nothing is left.
"""

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
UI = ROOT / "src" / "GitExt.UI"
CATALOG = pathlib.Path(__file__).resolve().parent / "catalog-cs.json"

TURKISH = "ğüşıöçĞÜŞİÖÇ"

# The strings in these files do not reach the user.
SKIP_FILES = {"Translator.cs", "LanguageInfo.cs", "LocaleFile.cs", "TranslateExtension.cs"}


def is_comment(line: str) -> bool:
    return line.lstrip().startswith(("//", "///", "*", "/*"))


def each_string(path: pathlib.Path):
    """Yields the string literals containing a Turkish character as (line number, string)."""
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if is_comment(line):
            continue

        for match in re.finditer(r'"((?:[^"\\]|\\.)*)"', line):
            value = match.group(1)

            if any(ch in value for ch in TURKISH):
                yield number, value


def sources():
    for directory in ("ViewModels", "Views", "Controls", "Commands", "Settings", "Themes", "Storage"):
        folder = UI / directory

        if folder.is_dir():
            for path in sorted(folder.rglob("*.cs")):
                if path.name not in SKIP_FILES and "obj/" not in str(path):
                    yield path


def collect() -> int:
    catalog: dict[str, dict[str, str]] = {}

    if CATALOG.exists():
        catalog = json.loads(CATALOG.read_text(encoding="utf-8"))

    added = 0

    for path in sources():
        for _, value in each_string(path):
            if value not in catalog:
                catalog[value] = {"en": ""}
                added += 1

    CATALOG.write_text(
        json.dumps(catalog, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")

    pending = sum(1 for e in catalog.values() if not e["en"])
    print(f"catalog: {len(catalog)} strings ({added} new), {pending} awaiting translation")
    return 0


def report() -> int:
    total = 0

    for path in sources():
        found = list(each_string(path))

        if not found:
            continue

        total += len(found)
        print(f"\n{path.relative_to(ROOT)} ({len(found)})")

        for number, value in found[:6]:
            print(f"  {number}: {value[:78]}")

        if len(found) > 6:
            print(f"  … {len(found) - 6} tane daha")

    print(f"\n{total} strings not yet moved in total")
    return 0 if total == 0 else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--collect", action="store_true")
    parser.add_argument("--report", action="store_true")
    args = parser.parse_args()

    if args.collect:
        return collect()

    if args.report:
        return report()

    parser.error("--collect veya --report verin")
    return 2


if __name__ == "__main__":
    sys.exit(main())
