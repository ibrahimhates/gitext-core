#!/usr/bin/env python3
"""C# tarafındaki kullanıcı metinlerini toplar (P11-T05).

    tools/i18n/extract-csharp.py --collect    # catalog-cs.json üretir/günceller
    tools/i18n/extract-csharp.py --report     # taşınmamış dizeleri listeler

⚠️ Bu betik XAML'deki gibi OTOMATİK DEĞİŞTİRME YAPMIYOR. Sebebi ölçülebilir bir fark:
XAML'de metin bir özniteliğin tamamı, bağlamı belli. C#'ta ise aynı dize
`Error = "..."` da olabilir, `$"... {x} ..."` içinde de, `throw new(...)` içinde de —
her biri farklı bir dönüşüm gerektiriyor (indeksleyici mi, Format mı, hiç dokunma mı).
Kör bir yeniden yazma sessizce yanlış kod üretirdi.

Betiğin işi: hiçbir dizenin ATLANMAMASINI garanti etmek. Değiştirme elle yapılıyor,
sonra --report ile kalan sıfırlanana kadar kontrol ediliyor.
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

# Bu dosyalardaki dizeler kullanıcıya gitmiyor.
SKIP_FILES = {"Translator.cs", "LanguageInfo.cs", "LocaleFile.cs", "TranslateExtension.cs"}


def is_comment(line: str) -> bool:
    return line.lstrip().startswith(("//", "///", "*", "/*"))


def each_string(path: pathlib.Path):
    """Türkçe karakter içeren dize sabitlerini (satır no, dize) olarak verir."""
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
    print(f"katalog: {len(catalog)} dize ({added} yeni), {pending} çeviri bekliyor")
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

    print(f"\ntoplam {total} taşınmamış dize")
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
