#!/usr/bin/env bash
#
# Icon set generation (P10-T06).
#
# Usage:
#   build/icons/generate.sh [output-dir]
#
# Default output: build/icons/out/
#
# ─────────────────────────────────────────────────────────────────────────────
# Produces the Freedesktop icon theme structure:
#   hicolor/<size>x<size>/apps/io.github.ibrahimhates.GitExtCore.png
#   hicolor/scalable/apps/io.github.ibrahimhates.GitExtCore.svg
#
# ⚠️ MEASURED — a SEPARATE drawing is used for 16 and 22 px (gitext-core-small.svg).
# The full icon turns blurry at that size: three nodes, two lanes and a merge edge
# don't fit into 16 pixels. The small variant conveys the same idea with two nodes
# and a single edge. This is standard in Freedesktop icon themes — a 16 px icon is
# not a shrunk version of the 256 px one.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$HERE/out}"

APP_ID="io.github.ibrahimhates.GitExtCore"

# Threshold below which the simplified drawing is used.
SMALL_MAX=22

command -v rsvg-convert >/dev/null || {
    echo "ERROR: rsvg-convert not found (package: librsvg)." >&2
    exit 1
}

rm -rf "$OUT"

# The sizes Freedesktop expects. 512 is for Flathub and software centers.
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

# Scalable version: for HiDPI and arbitrary sizes.
mkdir -p "$OUT/hicolor/scalable/apps"
cp "$HERE/gitext-core.svg" "$OUT/hicolor/scalable/apps/$APP_ID.svg"

# Windows .ico — a single multi-size file. The simplified variant is used for small sizes.
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
    echo "-- .ico SKIPPED (ImageMagick not found)."
fi

count=$(find "$OUT" -type f | wc -l)
echo "== icon set ready: $OUT ($count files)"
find "$OUT" -type f -printf '   %P\n' | sort
