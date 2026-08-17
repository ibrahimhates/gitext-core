#!/usr/bin/env bash
#
# Linux packaging (P06-T17, P10-T06/T08/T09): portable tarball + AppImage.
#
# Usage:
#   build/linux/package.sh
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/linux/package.sh   # tagless trial
#
# The version is derived from the git tag (P10-T01) — build/version.sh. The version
# is NOT given by hand.
#
# ⚠️ MEASURED — appimagetool is NOT installed on this machine and may not be
# elsewhere either. The script tries to download it; if it can't, the tarball is
# still produced and the AppImage step is skipped with the SKIP ANNOUNCED. Skipping
# silently would hide that the release script ran only halfway.
#
# ⚠️ MEASURED — appimagetool wants FUSE. Without FUSE it's run with
# `--appimage-extract-and-run` (the only way in containers).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Version from a SINGLE source: git tag → MinVer → the value embedded in the binary
# (P10-T01). This used to read VersionPrefix from Directory.Build.props; that field
# no longer exists.
# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

if [ $# -gt 0 ]; then
    echo "!! Version is no longer given as an argument — it's derived from the git tag (ADR-0006)." >&2
    echo "   For a tagless trial: MINVER_VERSION_OVERRIDE=$1 $0" >&2
    exit 2
fi

VERSION="$(gitext_require_releasable_version)"

RID="${RID:-linux-x64}"
OUT="$ROOT/dist"
STAGE="$OUT/$RID/gitext-core"

APP_ID="io.github.ibrahimhates.GitExtCore"

echo "== gitext-core $VERSION ($RID)"

rm -rf "$OUT/$RID"
mkdir -p "$STAGE"

echo "== publish (self-contained, single file)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$STAGE"

# ⚠️ MEASURED (P10-T00) — `-p:Version=` used to be here. After MinVer was added,
# that parameter became SILENTLY ineffective: MinVer computes the version itself and
# overwrites it. MinVerVersionOverride is the only valid way to impose a version
# from outside.

# The version in the package's name and the version inside the binary MUST be THE
# SAME. If they diverge, the user ends up with "I installed 1.0.0 but it says
# 0.9.1" and which one is correct can't be told from a bug report. Verified once here.
EMBEDDED="$("$STAGE/gitext-core" --version | head -1 | awk '{print $2}')"

if [ "$EMBEDDED" != "$VERSION" ]; then
    echo "!! VERSION MISMATCH: package '$VERSION', binary '$EMBEDDED'." >&2
    exit 1
fi

echo "   version verified: $EMBEDDED"

# Debug symbols in the publish folder bloat the tarball for no reason.
rm -f "$STAGE"/*.pdb

# ---------------------------------------------------------------- desktop assets

echo "== icons and desktop entry"
build/icons/generate.sh "$OUT/icons" >/dev/null

# Metadata files go into the tarball: install.sh copies them into system directories.
mkdir -p "$STAGE/share/applications" "$STAGE/share/metainfo" "$STAGE/share/icons"

cp "build/linux/$APP_ID.desktop" "$STAGE/share/applications/"
cp -r "$OUT/icons/hicolor" "$STAGE/share/icons/"

if [ -f "build/linux/$APP_ID.metainfo.xml" ]; then
    cp "build/linux/$APP_ID.metainfo.xml" "$STAGE/share/metainfo/"
fi

cp build/linux/install.sh "$STAGE/"
chmod +x "$STAGE/install.sh"

cp LICENSE "$STAGE/"
cp README.md "$STAGE/"

echo "== tarball"
TARBALL="$OUT/gitext-core-$VERSION-$RID.tar.gz"
tar -czf "$TARBALL" -C "$OUT/$RID" gitext-core
echo "   $TARBALL ($(du -h "$TARBALL" | cut -f1))"

# ---------------------------------------------------------------- AppImage

APPDIR="$OUT/$RID/AppDir"
rm -rf "$APPDIR"
# `usr/share/icons` MUST be created FIRST: otherwise `cp -r hicolor icons/` copies the
# source under the destination's own name and the hicolor layer is lost (measured —
# the icons ended up under usr/share/icons/256x256/... and no desktop environment
# could find them).
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/metainfo" "$APPDIR/usr/share/icons"

# The name of the published executable is `gitext-core`, matching `AssemblyName`.
cp "$STAGE/gitext-core" "$APPDIR/usr/bin/gitext-core"
chmod +x "$APPDIR/usr/bin/gitext-core"

cp "build/linux/$APP_ID.desktop" "$APPDIR/usr/share/applications/"
cp -r "$OUT/icons/hicolor" "$APPDIR/usr/share/icons/"

if [ -f "build/linux/$APP_ID.metainfo.xml" ]; then
    cp "build/linux/$APP_ID.metainfo.xml" "$APPDIR/usr/share/metainfo/"

    # ⚠️ MEASURED — appimagetool does NOT recognize the metainfo.xml name and emits
    # an "AppStream upstream metadata is missing" warning; it still looks for the old
    # `.appdata.xml` name. Both names are placed: the new name is the standard, the
    # old name silences appimagetool.
    cp "build/linux/$APP_ID.metainfo.xml" "$APPDIR/usr/share/metainfo/$APP_ID.appdata.xml"

    # What SHIPS is validated, and without the network — see the --no-appstream note
    # at appimagetool below. `--no-net` keeps every offline rule (schema, required
    # fields, licence identifiers); only the "is this link alive" question is dropped.
    if command -v appstreamcli >/dev/null 2>&1; then
        appstreamcli validate --no-net "$APPDIR/usr/share/metainfo/$APP_ID.metainfo.xml"
    else
        echo "   appstreamcli missing — metadata validation SKIPPED."
    fi
fi

# appimagetool WANTS both a .desktop file and an icon at the root, plus an AppRun
# (measured: if either is missing it says "Desktop file not found" / "icon not found").
cp "build/linux/$APP_ID.desktop" "$APPDIR/"
cp "$OUT/icons/hicolor/256x256/apps/$APP_ID.png" "$APPDIR/"
# A root-level .svg is also expected (the scalable icon is preferred).
cp "$OUT/icons/hicolor/scalable/apps/$APP_ID.svg" "$APPDIR/" 2>/dev/null || true

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
# AppImage entry point. `$APPDIR` is provided by the AppImage runtime at run time.
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/gitext-core" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

TOOL="$OUT/appimagetool"

if [ ! -x "$TOOL" ]; then
    echo "== downloading appimagetool"
    URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"

    if ! curl -fsSL -o "$TOOL" "$URL"; then
        echo "!! could not download appimagetool — AppImage step SKIPPED (tarball is ready)."
        exit 0
    fi

    chmod +x "$TOOL"
fi

echo "== AppImage"
APPIMAGE="$OUT/gitext-core-$VERSION-x86_64.AppImage"

# Without FUSE, appimagetool itself won't run either; it's told to extract itself and run.
if [ -e /dev/fuse ] && [ -r /dev/fuse ]; then
    RUN=("$TOOL")
else
    RUN=("$TOOL" --appimage-extract-and-run)
fi

# 🔴 MEASURED — `--no-appstream` is not a way of dodging validation; the metadata is
# validated a few lines above, offline. What is dropped here is appimagetool's own
# appstreamcli run, which is ONLINE: it fetches every <url> in the file and treats an
# unreachable one as a warning, and appstreamcli exits 3 on warnings — so the AppImage
# fails to build.
#
# The link it could not reach was our own bug tracker, and the URL is fine: at the same
# moment `github.com/git/git/issues` was answering 404 as well, and the repository's API
# says the issue tracker is open. GitHub throttles anonymous page requests, and a CI
# runner asks from a shared address. So this check makes the release depend on a coin
# flip with a third party, not on anything in the repository.
if ARCH=x86_64 "${RUN[@]}" --no-appstream "$APPDIR" "$APPIMAGE"; then
    echo "   $APPIMAGE ($(du -h "$APPIMAGE" | cut -f1))"
else
    echo "!! could not produce AppImage — tarball is still ready."
    exit 0
fi
