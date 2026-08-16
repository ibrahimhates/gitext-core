#!/usr/bin/env python3
"""Moves the user-facing texts in XAML onto translation keys (P11-T04).

IN TWO STAGES, because the keys have to be English while the source texts are Turkish:

    1) tools/i18n/extract-xaml.py --collect
       Collects every literal text and produces tools/i18n/catalog.json.
       Each entry's "en" field arrives EMPTY; the English translations are written there.

    2) tools/i18n/extract-xaml.py --apply
       Derives the key from the ENGLISH translation, rewrites the XAML with
       {loc:Translate ...}, and writes en.json and tr.json.

⚠️ Why the key is derived from the English: the source language is English (there is no ADR
but that is the rule). Derived from Turkish, the keys would look like 'depo_ac', and someone
writing fr.json tomorrow would be wrestling with keys in a language they do not read.

⚠️ Why a script: there are 430 attributes across 37 files. Moving them by hand produces silent
mistakes — a missed Text= stays Turkish forever and nobody notices. The script misses none of
them. It can be re-run: it ignores the ones that are already {loc:Translate ...}.
"""

import argparse
import json
import pathlib
import re
import sys
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parents[2]
VIEWS = ROOT / "src" / "GitExt.UI" / "Views"
LOCALES = ROOT / "src" / "GitExt.UI" / "Locales"
CATALOG = pathlib.Path(__file__).resolve().parent / "catalog.json"

# The attributes that show text to the user.
ATTRIBUTES = ["Text", "Header", "Content", "Title", "Watermark", "ToolTip.Tip"]

# Leave it alone when the value STARTS with one of these: a binding, a resource, or already translated.
SKIP_PREFIXES = ("{", "$")


def slugify(text: str) -> str:
    """The key fragment from the English text: 'Open repository' -> 'open_repository'."""
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()
    text = re.sub(r"[^a-zA-Z0-9]+", "_", text).strip("_").lower()
    return re.sub(r"_+", "_", text)[:44] or "text"


def view_prefix(path: pathlib.Path) -> str:
    """The key prefix from the file name: MainWindow.axaml -> main."""
    name = re.sub(r"(?<!^)(?=[A-Z])", "_", path.stem).lower()
    for suffix in ("_view", "_window", "_dialog", "_button"):
        name = name.removesuffix(suffix)
    return name or "app"


def decode_entities(value: str) -> str:
    """Turns the XAML escapes into real characters; plain text is written to the JSON."""
    return (value
            .replace("&quot;", '"')
            .replace("&#10;", "\n")
            .replace("&#13;", "")
            .replace("&amp;", "&")
            .replace("&lt;", "<")
            .replace("&gt;", ">"))


def each_literal(source: str):
    """Yields the file's translatable attributes as (attribute, raw value)."""
    for attribute in ATTRIBUTES:
        for match in re.finditer(rf'(\b{re.escape(attribute)}=")([^"]*)(")', source):
            value = match.group(2)

            if not value.strip() or value.startswith(SKIP_PREFIXES):
                continue

            # Values that are only punctuation/symbols (icons, separators) are not translated.
            if not re.search(r"[a-zA-ZğüşıöçĞÜŞİÖÇ]", value):
                continue

            yield attribute, value


def collect() -> int:
    """Collects the texts; existing translations are preserved."""
    catalog: dict[str, dict[str, str]] = {}

    if CATALOG.exists():
        catalog = json.loads(CATALOG.read_text(encoding="utf-8"))

    added = 0

    for path in sorted(VIEWS.glob("*.axaml")):
        source = path.read_text(encoding="utf-8")
        prefix = view_prefix(path)

        for _, raw in each_literal(source):
            text = decode_entities(raw)
            identifier = f"{prefix}|{text}"

            if identifier not in catalog:
                catalog[identifier] = {"tr": text, "en": ""}
                added += 1

    CATALOG.write_text(
        json.dumps(catalog, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")

    pending = sum(1 for e in catalog.values() if not e["en"])
    print(f"catalog: {len(catalog)} entries ({added} new), {pending} awaiting translation")
    print(f"  {CATALOG.relative_to(ROOT)}")
    return 0


def apply() -> int:
    """Derives the keys from the English translations and writes the XAML and locale files."""
    if not CATALOG.exists():
        print("ERROR: run --collect first.", file=sys.stderr)
        return 1

    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    missing = [k for k, e in catalog.items() if not e["en"]]

    if missing:
        print(f"ERROR: {len(missing)} entries have no English. The first five:", file=sys.stderr)
        for key in missing[:5]:
            print(f"  {key}", file=sys.stderr)
        return 1

    # identifier -> key. The same English text shares the same key.
    keys: dict[str, str] = {}
    english: dict[str, str] = {}
    turkish: dict[str, str] = {}

    for identifier in sorted(catalog):
        entry = catalog[identifier]
        prefix = identifier.split("|", 1)[0]
        key = f"{prefix}.{slugify(entry['en'])}"

        if key in english and english[key] != entry["en"]:
            index = 2
            while f"{key}_{index}" in english and english[f"{key}_{index}"] != entry["en"]:
                index += 1
            key = f"{key}_{index}"

        keys[identifier] = key
        english[key] = entry["en"]
        turkish[key] = entry["tr"]

    total = 0

    for path in sorted(VIEWS.glob("*.axaml")):
        source = path.read_text(encoding="utf-8")
        original = source
        prefix = view_prefix(path)
        count = 0

        for attribute in ATTRIBUTES:
            def replace(match: re.Match[str]) -> str:
                nonlocal count
                raw = match.group(2)

                if not raw.strip() or raw.startswith(SKIP_PREFIXES):
                    return match.group(0)

                if not re.search(r"[a-zA-ZğüşıöçĞÜŞİÖÇ]", raw):
                    return match.group(0)

                key = keys.get(f"{prefix}|{decode_entities(raw)}")

                if key is None:
                    return match.group(0)

                count += 1
                return f'{match.group(1)}{{loc:Translate {key}}}{match.group(3)}'

            source = re.sub(rf'(\b{re.escape(attribute)}=")([^"]*)(")', replace, source)

        if source == original:
            continue

        if "xmlns:loc=" not in source:
            source = re.sub(
                r'(xmlns:x="http://schemas\.microsoft\.com/winfx/2006/xaml")',
                r'\1\n        xmlns:loc="using:GitExt.UI.Localization"',
                source,
                count=1)

        path.write_text(source, encoding="utf-8")
        total += count
        print(f"  {path.name}: {count}")

    write_locale("en.json", "en", "English", english)
    write_locale("tr.json", "tr", "Türkçe", turkish)

    print(f"\n{total} texts moved, {len(english)} keys")
    return 0


def write_locale(filename: str, code: str, name: str, entries: dict[str, str]) -> None:
    """Writes the locale file; existing keys that came from outside XAML are preserved."""
    path = LOCALES / filename
    existing: dict[str, str] = {}

    if path.exists():
        loaded = json.loads(path.read_text(encoding="utf-8"))
        loaded.pop("_meta", None)
        # The test.* keys are the tests' own constants; they are preserved.
        existing = loaded

    merged = {**existing, **entries}
    payload = {"_meta": {"code": code, "name": name}, **dict(sorted(merged.items()))}

    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--collect", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    if args.collect:
        return collect()

    if args.apply:
        return apply()

    parser.error("--collect veya --apply verin")
    return 2


if __name__ == "__main__":
    sys.exit(main())
