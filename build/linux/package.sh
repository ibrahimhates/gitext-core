#!/usr/bin/env bash
#
# Linux paketleme (P06-T17): taşınabilir tarball + AppImage.
#
# Kullanım:
#   build/linux/package.sh
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/linux/package.sh   # etiketsiz deneme
#
# Sürüm git tag'inden türetiliyor (P10-T01) — build/version.sh. Elle sürüm VERİLMEZ.
#
# ⚠️ ÖLÇÜLDÜ — appimagetool bu makinede kurulu DEĞİL ve olmayabilir de. Betik onu
# indirmeye çalışır; indiremezse tarball yine üretilir ve AppImage adımı ATLANDIĞI
# SÖYLENEREK geçilir. Sessizce geçmek, yayın betiğinin yarım çalıştığını gizlerdi.
#
# ⚠️ ÖLÇÜLDÜ — appimagetool FUSE istiyor. FUSE yoksa `--appimage-extract-and-run`
# ile çalıştırılıyor (konteynerlerde tek yol).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Sürüm TEK kaynaktan: git tag → MinVer → ikiliye gömülen değer (P10-T01).
# Eskiden burada Directory.Build.props'tan VersionPrefix okunuyordu; o alan artık yok.
# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

if [ $# -gt 0 ]; then
    echo "!! Sürüm artık argümanla verilmiyor — git tag'inden türetiliyor (ADR-0006)." >&2
    echo "   Etiketsiz deneme için: MINVER_VERSION_OVERRIDE=$1 $0" >&2
    exit 2
fi

VERSION="$(gitext_require_releasable_version)"

RID="${RID:-linux-x64}"
OUT="$ROOT/dist"
STAGE="$OUT/$RID/gitext-core"

echo "== gitext-core $VERSION ($RID)"

rm -rf "$OUT/$RID"
mkdir -p "$STAGE"

echo "== yayın (self-contained, tek dosya)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$STAGE"

# ⚠️ ÖLÇÜLDÜ (P10-T00) — burada eskiden `-p:Version=` vardı. MinVer eklendikten sonra
# o parametre SESSİZCE etkisiz: MinVer sürümü kendi hesaplayıp üzerine yazıyor.
# MinVerVersionOverride, sürümü dışarıdan dayatmanın tek geçerli yolu.

# Paketin adındaki sürüm ile ikilinin içindeki sürüm AYNI olmalı. Ayrışırlarsa
# kullanıcı "1.0.0 kurdum ama 0.9.1 diyor" ile karşılaşır ve hangisinin doğru olduğu
# hata raporundan anlaşılmaz. Burada bir kez doğrulanıyor.
EMBEDDED="$("$STAGE/gitext-core" --version | head -1 | awk '{print $2}')"

if [ "$EMBEDDED" != "$VERSION" ]; then
    echo "!! SÜRÜM UYUŞMAZLIĞI: paket '$VERSION', ikili '$EMBEDDED'." >&2
    exit 1
fi

echo "   sürüm doğrulandı: $EMBEDDED"

# Yayın klasöründeki hata ayıklama sembolleri tarball'ı gereksiz şişiriyor.
rm -f "$STAGE"/*.pdb

cp LICENSE "$STAGE/"
cp README.md "$STAGE/"

echo "== tarball"
TARBALL="$OUT/gitext-core-$VERSION-$RID.tar.gz"
tar -czf "$TARBALL" -C "$OUT/$RID" gitext-core
echo "   $TARBALL ($(du -h "$TARBALL" | cut -f1))"

# ---------------------------------------------------------------- AppImage

APPDIR="$OUT/$RID/AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# Yayınlanan çalıştırılabilirin adı `AssemblyName` ile `gitext-core`.
cp "$STAGE/gitext-core" "$APPDIR/usr/bin/gitext-core"
chmod +x "$APPDIR/usr/bin/gitext-core"

cp build/linux/gitext-core.desktop "$APPDIR/usr/share/applications/"
cp build/linux/gitext-core.png "$APPDIR/usr/share/icons/hicolor/256x256/apps/"

# appimagetool kökte hem .desktop hem simge ve bir AppRun İSTİYOR (ölçüldü:
# ikisinden biri eksikse "Desktop file not found" / "icon not found" diyor).
cp build/linux/gitext-core.desktop "$APPDIR/"
cp build/linux/gitext-core.png "$APPDIR/"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
# AppImage giriş noktası. `$APPDIR` çalıştırma anında AppImage tarafından veriliyor.
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/gitext-core" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

TOOL="$OUT/appimagetool"

if [ ! -x "$TOOL" ]; then
    echo "== appimagetool indiriliyor"
    URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"

    if ! curl -fsSL -o "$TOOL" "$URL"; then
        echo "!! appimagetool indirilemedi — AppImage adımı ATLANDI (tarball hazır)."
        exit 0
    fi

    chmod +x "$TOOL"
fi

echo "== AppImage"
APPIMAGE="$OUT/gitext-core-$VERSION-x86_64.AppImage"

# FUSE yoksa appimagetool'un kendisi de çalışmaz; kendini açıp çalışması söyleniyor.
if [ -e /dev/fuse ] && [ -r /dev/fuse ]; then
    RUN=("$TOOL")
else
    RUN=("$TOOL" --appimage-extract-and-run)
fi

if ARCH=x86_64 "${RUN[@]}" "$APPDIR" "$APPIMAGE"; then
    echo "   $APPIMAGE ($(du -h "$APPIMAGE" | cut -f1))"
else
    echo "!! AppImage üretilemedi — tarball yine de hazır."
    exit 0
fi
