#!/usr/bin/env bash
#
# İkon seti üretimi (P10-T06).
#
# Kullanım:
#   build/icons/generate.sh [çıktı-dizini]
#
# Varsayılan çıktı: build/icons/out/
#
# ─────────────────────────────────────────────────────────────────────────────
# Freedesktop ikon teması yapısı üretiliyor:
#   hicolor/<boyut>x<boyut>/apps/io.github.ibrahimhates.GitExtCore.png
#   hicolor/scalable/apps/io.github.ibrahimhates.GitExtCore.svg
#
# ⚠️ ÖLÇÜLDÜ — 16 ve 22 px için AYRI bir çizim kullanılıyor (gitext-core-small.svg).
# Tam ikon o boyutta bulanıklaşıyor: üç düğüm, iki şerit ve merge bağı 16 piksele
# sığmıyor. Küçük varyant aynı fikri iki düğüm ve tek bağla anlatıyor. Bu Freedesktop
# ikon temalarında olağan — 16 px'lik ikon 256 px'liğin küçültülmüşü değildir.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$HERE/out}"

APP_ID="io.github.ibrahimhates.GitExtCore"

# Küçük boyutlarda sadeleştirilmiş çizim kullanılan eşik.
SMALL_MAX=22

command -v rsvg-convert >/dev/null || {
    echo "HATA: rsvg-convert bulunamadı (paket: librsvg)." >&2
    exit 1
}

rm -rf "$OUT"

# Freedesktop'un beklediği boyutlar. 512 Flathub ve yazılım merkezleri için.
for size in 16 22 24 32 48 64 128 256 512; do
    dir="$OUT/hicolor/${size}x${size}/apps"
    mkdir -p "$dir"

    if [ "$size" -le "$SMALL_MAX" ]; then
        source_svg="$HERE/gitext-core-small.svg"
    else
        source_svg="$HERE/gitext-core.svg"
    fi

    rsvg-convert -w "$size" -h "$size" "$source_svg" -o "$dir/$APP_ID.png"
done

# Ölçeklenebilir (scalable) sürüm: HiDPI ve rastgele boyutlar için.
mkdir -p "$OUT/hicolor/scalable/apps"
cp "$HERE/gitext-core.svg" "$OUT/hicolor/scalable/apps/$APP_ID.svg"

# Windows .ico — çok boyutlu tek dosya. Küçük boyutlar için sade varyant kullanılıyor.
if command -v magick >/dev/null; then
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT

    for size in 16 24 32 48 64 128 256; do
        if [ "$size" -le "$SMALL_MAX" ]; then
            src="$HERE/gitext-core-small.svg"
        else
            src="$HERE/gitext-core.svg"
        fi
        rsvg-convert -w "$size" -h "$size" "$src" -o "$tmp/$size.png"
    done

    magick "$tmp"/16.png "$tmp"/24.png "$tmp"/32.png "$tmp"/48.png \
           "$tmp"/64.png "$tmp"/128.png "$tmp"/256.png "$OUT/gitext-core.ico"
else
    echo "-- .ico ATLANDI (ImageMagick yok)."
fi

count=$(find "$OUT" -type f | wc -l)
echo "== ikon seti hazır: $OUT ($count dosya)"
find "$OUT" -type f -printf '   %P\n' | sort
