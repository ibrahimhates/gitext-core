#!/usr/bin/env python3
"""XAML'deki kullanıcı metinlerini çeviri anahtarlarına taşır (P11-T04).

İKİ AŞAMALI, çünkü anahtarlar İngilizce olmalı ama kaynak metinler Türkçe:

    1) tools/i18n/extract-xaml.py --collect
       Bütün literal metinleri toplar ve tools/i18n/catalog.json üretir.
       Her girdinin "en" alanı BOŞ gelir; İngilizce çeviriler oraya yazılır.

    2) tools/i18n/extract-xaml.py --apply
       Anahtarı İNGİLİZCE çeviriden türetir, XAML'leri {loc:Translate ...} ile
       değiştirir, en.json ve tr.json'u yazar.

⚠️ Anahtar neden İngilizceden türetiliyor: kaynak dil İngilizce (ADR yok ama kural bu).
Türkçeden türetilseydi anahtarlar 'depo_ac' gibi olurdu ve yarın fr.json yazan biri
anlamadığı bir dilde anahtarlarla uğraşırdı.

⚠️ Neden betik: 37 dosyada 430 öznitelik var. Elle taşımak sessiz hata üretir — atlanan
bir Text= sonsuza kadar Türkçe kalır ve kimse fark etmez. Betik hiçbirini atlamıyor.
Tekrar çalıştırılabilir: zaten {loc:Translate ...} olanları görmezden geliyor.
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

# Kullanıcıya metin gösteren öznitelikler.
ATTRIBUTES = ["Text", "Header", "Content", "Title", "Watermark", "ToolTip.Tip"]

# Değeri bunlarla BAŞLIYORSA dokunma: bağlama, kaynak veya zaten çevrilmiş.
SKIP_PREFIXES = ("{", "$")


def slugify(text: str) -> str:
    """İngilizce metinden anahtar parçası: 'Open repository' -> 'open_repository'."""
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()
    text = re.sub(r"[^a-zA-Z0-9]+", "_", text).strip("_").lower()
    return re.sub(r"_+", "_", text)[:44] or "text"


def view_prefix(path: pathlib.Path) -> str:
    """Dosya adından anahtar öneki: MainWindow.axaml -> main."""
    name = re.sub(r"(?<!^)(?=[A-Z])", "_", path.stem).lower()
    for suffix in ("_view", "_window", "_dialog", "_button"):
        name = name.removesuffix(suffix)
    return name or "app"


def decode_entities(value: str) -> str:
    """XAML kaçışlarını gerçek karaktere çevirir; JSON'a düz metin yazılıyor."""
    return (value
            .replace("&quot;", '"')
            .replace("&#10;", "\n")
            .replace("&#13;", "")
            .replace("&amp;", "&")
            .replace("&lt;", "<")
            .replace("&gt;", ">"))


def each_literal(source: str):
    """Dosyadaki çevrilebilir öznitelikleri (attribute, ham değer) olarak verir."""
    for attribute in ATTRIBUTES:
        for match in re.finditer(rf'(\b{re.escape(attribute)}=")([^"]*)(")', source):
            value = match.group(2)

            if not value.strip() or value.startswith(SKIP_PREFIXES):
                continue

            # Yalnızca noktalama/simge olan değerler (ikonlar, ayraçlar) çevrilmez.
            if not re.search(r"[a-zA-ZğüşıöçĞÜŞİÖÇ]", value):
                continue

            yield attribute, value


def collect() -> int:
    """Metinleri toplar; mevcut çeviriler korunur."""
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
    print(f"katalog: {len(catalog)} girdi ({added} yeni), {pending} çeviri bekliyor")
    print(f"  {CATALOG.relative_to(ROOT)}")
    return 0


def apply() -> int:
    """Anahtarları İngilizce çeviriden türetip XAML'leri ve dil dosyalarını yazar."""
    if not CATALOG.exists():
        print("HATA: önce --collect çalıştırın.", file=sys.stderr)
        return 1

    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    missing = [k for k, e in catalog.items() if not e["en"]]

    if missing:
        print(f"HATA: {len(missing)} girdinin İngilizcesi yok. İlk beşi:", file=sys.stderr)
        for key in missing[:5]:
            print(f"  {key}", file=sys.stderr)
        return 1

    # identifier -> anahtar. Aynı İngilizce metin aynı anahtarı paylaşıyor.
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

    print(f"\n{total} metin taşındı, {len(english)} anahtar")
    return 0


def write_locale(filename: str, code: str, name: str, entries: dict[str, str]) -> None:
    """Dil dosyasını yazar; XAML dışından gelen mevcut anahtarlar korunuyor."""
    path = LOCALES / filename
    existing: dict[str, str] = {}

    if path.exists():
        loaded = json.loads(path.read_text(encoding="utf-8"))
        loaded.pop("_meta", None)
        # test.* anahtarları testlerin kendi sabitleri; korunuyorlar.
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
