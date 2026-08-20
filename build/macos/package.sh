#!/usr/bin/env bash
#
# macOS packaging (P10-T20, P10-T21): .app bundle + distribution archive.
#
# Usage:
#   build/macos/package.sh                              # osx-arm64 (Apple Silicon)
#   RID=osx-x64 build/macos/package.sh                  # Intel
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/macos/package.sh
#
# Cross-compiled from Linux. The resulting .app CANNOT BE RUN on this machine —
# the README should say so honestly (P10-T25).
#
# ⚠️ MEASURED — `iconutil` (macOS-specific), `hdiutil` (macOS-specific), `png2icns`
# and `icnsutil` are NOT on this machine. So:
#   - the .icns is produced with our own script (build/macos/make-icns.py)
#   - the .dmg is NOT PRODUCED; a tar.gz carrying the .app is produced instead. A
#     DMG can only be produced on macOS (or a macOS runner); giving a working
#     archive is more honest than producing a fake DMG.

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

# 🔴 IncludeNativeLibrariesForSelfExtract is REQUIRED alongside PublishSingleFile. MEASURED on
# the v0.1.0 release: without it .NET leaves the native libraries (libSkiaSharp, libHarfBuzzSharp)
# NEXT TO the binary instead of embedding them, so "single file" was not single at all. install.sh
# copies only the binary, and the installed application died at startup with
#   DllNotFoundException: Unable to load shared library 'libSkiaSharp'
# Verified after the fix: the binary is alone in its directory and runs from an isolated directory
# with no libraries beside it.
echo "== publish (self-contained, single file)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$APP/Contents/MacOS"

rm -f "$APP/Contents/MacOS"/*.pdb

# Version verification: the binary can't be run here (Mach-O), so the embedded
# version string is searched for instead. This catches the package name and the
# version inside it diverging.
if ! grep -aq "$VERSION" "$APP/Contents/MacOS/gitext-core"; then
    echo "!! VERSION MISMATCH: '$VERSION' not found inside the binary." >&2
    exit 1
fi

echo "   version found in binary: $VERSION"

# ---------------------------------------------------------------- icon

echo "== icon (.icns)"
build/icons/generate.sh "$OUT/icons" >/dev/null

ICON_ARGS=()
for size in 16 32 64 128 256 512; do
    ICON_ARGS+=("$size:$OUT/icons/hicolor/${size}x${size}/apps/$APP_ID.png")
done

build/macos/make-icns.py "$APP/Contents/Resources/gitext-core.icns" "${ICON_ARGS[@]}"

# ---------------------------------------------------------------- Info.plist

# CFBundleShortVersionString is the version shown to the user and doesn't accept
# the SemVer pre-release suffix ("-rc.1") — Apple only expects a dotted number. The
# pre-release suffix is stripped; the full version stays in CFBundleVersion.
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

    <!-- True resolution instead of a scaled bitmap on Retina displays. -->
    <key>NSHighResolutionCapable</key>
    <true/>

    <!-- A regular app with a menu bar (not a background agent). -->
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>

    <!-- Open a repository via folder drag-and-drop. -->
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

# The bundle has no second place carrying version info; Info.plist is the single source.
python3 -c "
import plistlib, sys
with open('$APP/Contents/Info.plist', 'rb') as handle:
    plistlib.load(handle)
print('   Info.plist is valid')
"

cp LICENSE "$APP/Contents/Resources/"

# ---------------------------------------------------------------- archive

# The DMG will be produced on macOS (CI's macos runner). Here, a tar.gz preserving
# the .app: zip doesn't reliably carry the executable bit and symlinks.
echo "== archive"
ARCHIVE="$OUT/gitext-core-$VERSION-$RID.tar.gz"
tar -czf "$ARCHIVE" -C "$OUT/$RID" gitext-core.app
echo "   $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"

cat <<EOF

-- .dmg SKIPPED: hdiutil only exists on macOS.
   It's produced on CI's macOS runner with (P10-T21):
     hdiutil create -volname gitext-core -srcfolder "$APP" -ov -format UDZO \\
       "$OUT/gitext-core-$VERSION-$RID.dmg"
EOF
