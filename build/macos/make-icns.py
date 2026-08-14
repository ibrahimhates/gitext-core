#!/usr/bin/env python3
"""macOS .icns ikonu üretir (P10-T20).

Kullanım:
    build/macos/make-icns.py <çıktı.icns> <boyut>:<png> [<boyut>:<png> ...]

⚠️ ÖLÇÜLDÜ — `iconutil` (macOS'a özgü), `png2icns` ve `icnsutil` bu makinede yok ve
Linux'ta hiçbiri standart değil. ICNS biçimi yeterince basit olduğu için burada
doğrudan yazılıyor: yayın hattını kurulu olmayabilecek bir araca bağlamak, macOS
paketini üretilemez kılardı.

BİÇİM (Apple Icon Image):
    Başlık:  'icns' (4 bayt) + toplam dosya uzunluğu (4 bayt, big-endian)
    Öğeler:  tür (4 bayt) + öğe uzunluğu (4 bayt, big-endian) + veri
             Öğe uzunluğu BAŞLIĞI DA İÇERİYOR (yani veri + 8).

Modern türler PNG verisini olduğu gibi taşıyor — dönüştürme gerekmiyor.
"""

import struct
import sys

# Boyut → ICNS tür kodu. 'ic' ile başlayanlar PNG kabul ediyor.
#
# Retina (@2x) türleri AYRI kodlar: 'ic12' 32 px veriyi 16 px'lik yuvanın 2x'i olarak
# gösteriyor. İkisini de koymak, Finder'ın HiDPI ekranda bulanık bir ikon çizmesini
# engelliyor.
TYPES = {
    16: b"icp4",
    32: b"icp5",
    64: b"icp6",
    128: b"ic07",
    256: b"ic08",
    512: b"ic09",
    1024: b"ic10",   # 512@2x
}

RETINA = {
    32: b"ic11",     # 16@2x
    64: b"ic12",     # 32@2x
    256: b"ic13",    # 128@2x
    512: b"ic14",    # 256@2x
}


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2

    output = sys.argv[1]
    entries: list[tuple[bytes, bytes]] = []

    for spec in sys.argv[2:]:
        size_text, _, path = spec.partition(":")

        if not path:
            print(f"HATA: '{spec}' <boyut>:<png> biçiminde değil.", file=sys.stderr)
            return 2

        size = int(size_text)

        with open(path, "rb") as handle:
            data = handle.read()

        if not data.startswith(b"\x89PNG"):
            print(f"HATA: {path} bir PNG değil.", file=sys.stderr)
            return 1

        if size in TYPES:
            entries.append((TYPES[size], data))

        # Aynı PNG hem normal hem retina yuvasına giriyor: 32 px görüntü hem 32 px
        # ikon hem de 16 px ikonun 2x'i olarak geçerli.
        if size in RETINA:
            entries.append((RETINA[size], data))

    if not entries:
        print("HATA: hiçbir geçerli boyut verilmedi.", file=sys.stderr)
        return 1

    body = b"".join(
        kind + struct.pack(">I", len(data) + 8) + data
        for kind, data in entries
    )

    with open(output, "wb") as handle:
        handle.write(b"icns" + struct.pack(">I", len(body) + 8) + body)

    print(f"   {output} ({len(body) + 8} bayt, {len(entries)} öğe)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
