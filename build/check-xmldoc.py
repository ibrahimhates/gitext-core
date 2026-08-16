#!/usr/bin/env python3
"""Finds unbalanced tags in XML doc comments.

Usage:
    build/check-xmldoc.py

The compiler catches this as CS1570, but only for the file it is compiling — a broken
`<para>` in one project hides every other error behind it. This runs over the whole tree
in a second and points straight at the line.

It earned its place while translating comments in bulk: a closing `</para>` was dropped
and the build failed with a message that named the end of the block, not the missing tag.
"""

import pathlib
import re
import sys

# Tags that must be balanced. Self-closing forms (<see .../>), and tags carrying
# attributes (<list type="bullet">), are handled.
TAGS = (
    "para", "remarks", "summary", "list", "item", "b", "c", "i",
    "description", "term", "example", "code", "returns", "value",
)

ROOTS = ("src", "tests", "benchmarks")


def unbalanced(xml: str) -> list[tuple[str, int, int]]:
    problems = []

    for tag in TAGS:
        opened = len(re.findall(rf"<{tag}(?:\s[^>]*)?>", xml))
        closed = len(re.findall(rf"</{tag}>", xml))
        selfclosed = len(re.findall(rf"<{tag}(?:\s[^>]*)?/>", xml))

        if opened - selfclosed != closed:
            problems.append((tag, opened - selfclosed, closed))

    return problems


def main() -> int:
    findings = []

    for root in ROOTS:
        directory = pathlib.Path(root)

        if not directory.is_dir():
            continue

        for path in sorted(directory.rglob("*.cs")):
            if "obj/" in str(path) or "bin/" in str(path):
                continue

            block: list[str] = []
            start: int | None = None

            def flush(block: list[str], start: int | None) -> None:
                if not block or start is None:
                    return

                for tag, opened, closed in unbalanced("\n".join(block)):
                    findings.append((path, start, tag, opened, closed))

            for number, line in enumerate(
                    path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
                stripped = line.lstrip()

                if stripped.startswith("///"):
                    if start is None:
                        start = number

                    block.append(stripped[3:])
                else:
                    flush(block, start)
                    block, start = [], None

            flush(block, start)

    if findings:
        print(f"Unbalanced XML doc tags: {len(findings)}\n", file=sys.stderr)

        for path, line, tag, opened, closed in findings[:20]:
            print(f"  {path}:{line}  <{tag}> opened={opened} closed={closed}", file=sys.stderr)

        return 1

    print("OK: XML doc tags are balanced.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
