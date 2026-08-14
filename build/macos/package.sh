#!/usr/bin/env bash
#
# macOS paketleme (P10-T20, P10-T21): .app bundle + dağıtım arşivi.
#
# Kullanım:
#   build/macos/package.sh                              # osx-arm64 (Apple Silicon)
#   RID=osx-x64 build/macos/package.sh                  # Intel
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/macos/package.sh
#
# Linux'tan çapraz derleniyor. Üretilen .app bu makinede ÇALIŞTIRILAMAZ — README
# bunu dürüstçe söylemeli (P10-T25).
#
# ⚠️ ÖLÇÜLDÜ — `iconutil` (macOS'a özgü), `hdiutil` (macOS'a özgü), `png2icns` ve
# `icnsutil` bu makinede YOK. Bu yüzden:
#   - .icns kendi betiğimizle üretiliyor (build/macos/make-icns.py)
#   - .dmg ÜRETİLMİYOR; yerine .app'i taşıyan bir tar.gz üretiliyor. DMG yalnızca
#     macOS'ta (veya macOS runner'ında) üretilebilir; sahte bir DMG üretmektense
#     çalışan bir arşiv vermek dürüst olan.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

VERSION="$(gitext_require_releasable_version)"

RID="${RID:-osx-arm64}"
OUT="$ROOT/dist"
APP="$OUT/$RID/gitext-core.app"

APP_ID="io.github.ibrahimhates.GitExtCore"

echo "== gitext-core $VERSION ($RID)"

rm -rf "$OUT/$RID"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "== yayın (self-contained, tek dosya)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$APP/Contents/MacOS"

rm -f "$APP/Contents/MacOS"/*.pdb

# Sürüm doğrulaması: ikili burada çalıştırılamıyor (Mach-O), bu yüzden gömülü
# sürüm dizesi aranıyor. Paket adı ile içerideki sürümün ayrışmasını yakalar.
if ! grep -aq "$VERSION" "$APP/Contents/MacOS/gitext-core"; then
    echo "!! SÜRÜM UYUŞMAZLIĞI: '$VERSION' ikilinin içinde bulunamadı." >&2
    exit 1
fi

echo "   sürüm ikilide bulundu: $VERSION"

# ---------------------------------------------------------------- ikon

echo "== ikon (.icns)"
build/icons/generate.sh "$OUT/icons" >/dev/null

ICON_ARGS=()
for size in 16 32 64 128 256 512; do
    ICON_ARGS+=("$size:$OUT/icons/hicolor/${size}x${size}/apps/$APP_ID.png")
done

build/macos/make-icns.py "$APP/Contents/Resources/gitext-core.icns" "${ICON_ARGS[@]}"

# ---------------------------------------------------------------- Info.plist

# CFBundleShortVersionString kullanıcıya gösterilen sürüm ve SemVer ön sürüm ekini
# ("-rc.1") kabul etmiyor — Apple yalnızca noktalı sayı bekliyor. Ön sürüm eki
# kırpılıyor; tam sürüm CFBundleVersion'da duruyor.
SHORT_VERSION="${VERSION%%-*}"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key>
    <string>$APP_ID</string>
    <key>CFBundleName</key>
    <string>gitext-core</string>
    <key>CFBundleDisplayName</key>
    <string>gitext-core</string>
    <key>CFBundleExecutable</key>
    <string>gitext-core</string>
    <key>CFBundleIconFile</key>
    <string>gitext-core</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$SHORT_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright (C) 2026 gitext-core contributors. GPL-3.0-or-later.</string>

    <!-- Retina ekranlarda ölçeklenmiş bitmap yerine gerçek çözünürlük. -->
    <key>NSHighResolutionCapable</key>
    <true/>

    <!-- Menü çubuğu olan olağan bir uygulama (arka plan ajanı değil). -->
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>

    <!-- Klasör sürükle-bırak ile depo açma. -->
    <key>CFBundleDocumentTypes</key>
    <array>
        <dict>
            <key>CFBundleTypeName</key>
            <string>Folder</string>
            <key>CFBundleTypeRole</key>
            <string>Viewer</string>
            <key>LSItemContentTypes</key>
            <array>
                <string>public.folder</string>
            </array>
        </dict>
    </array>
</dict>
</plist>
EOF

# Bundle'ın sürüm bilgisi taşıyan ikinci bir yeri yok; Info.plist tek kaynak.
python3 -c "
import plistlib, sys
with open('$APP/Contents/Info.plist', 'rb') as handle:
    plistlib.load(handle)
print('   Info.plist geçerli')
"

cp LICENSE "$APP/Contents/Resources/"

# ---------------------------------------------------------------- arşiv

# DMG macOS'ta üretilecek (CI'ın macos runner'ı). Burada .app'i koruyan bir tar.gz:
# zip, çalıştırılabilir bitini ve sembolik bağları güvenilir taşımıyor.
echo "== arşiv"
ARCHIVE="$OUT/gitext-core-$VERSION-$RID.tar.gz"
tar -czf "$ARCHIVE" -C "$OUT/$RID" gitext-core.app
echo "   $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"

cat <<EOF

-- .dmg ATLANDI: hdiutil yalnızca macOS'ta var.
   CI'ın macOS runner'ında şu komutla üretiliyor (P10-T21):
     hdiutil create -volname gitext-core -srcfolder "$APP" -ov -format UDZO \\
       "$OUT/gitext-core-$VERSION-$RID.dmg"
EOF
