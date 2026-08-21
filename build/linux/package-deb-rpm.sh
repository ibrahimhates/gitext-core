#!/usr/bin/env bash
#
# Debian (.deb) and Fedora (.rpm) packages (P10-T11, P10-T12).
#
# Usage:
#   build/linux/package-deb-rpm.sh          # build both
#   build/linux/package-deb-rpm.sh deb      # .deb only
#   build/linux/package-deb-rpm.sh rpm      # .rpm only
#
# Prerequisite: build/linux/package.sh must have run (dist/linux-x64/gitext-core ready).
#
# ─────────────────────────────────────────────────────────────────────────────
# ⚠️ MEASURED — dpkg-deb and rpmbuild are NOT installed on this machine (Arch). If
# the tool is missing, the package is built in a container instead. Requiring the
# tools to be installed on the host would tie the ability to release to the dev
# machine's distro.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

VERSION="$(gitext_require_releasable_version)"
STAGE="$ROOT/dist/linux-x64/gitext-core"
OUT="$ROOT/dist"

APP_ID="io.github.ibrahimhates.GitExtCore"

[ -x "$STAGE/gitext-core" ] || {
    echo "ERROR: $STAGE/gitext-core does not exist. Run build/linux/package.sh first." >&2
    exit 1
}

WHAT="${1:-both}"

# The semver pre-release suffix ("-rc.1") is INVALID in package versions:
#   deb — '-' is the revision separator; '~' instead sorts BEFORE 1.0.0 in version
#         ordering, i.e. 1.0.0~rc.1 < 1.0.0. That is exactly the correct behavior.
#   rpm — '-' can't be used at all; '~' is used for the same reason.
# Only the FIRST '-' is converted: "1.0.1-alpha.0.3" → "1.0.1~alpha.0.3".
PKG_VERSION="${VERSION/-/~}"

# Prepare Docker if needed. The user's global docker configuration is NOT TOUCHED:
# a broken credsStore/context left over from Docker Desktop is common (measured in P10-T00).
setup_docker() {
    DOCKER_CFG="$(mktemp -d)"
    printf '{"auths":{}}' > "$DOCKER_CFG/config.json"
    export DOCKER_CONFIG="$DOCKER_CFG"
    export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"
}

# ---------------------------------------------------------------- shared tree

# Both packages install the same file layout; it's prepared in one place.
build_tree() {
    local root="$1"

    install -Dm755 "$STAGE/gitext-core" "$root/usr/bin/gitext-core"
    install -Dm644 "build/linux/$APP_ID.desktop" "$root/usr/share/applications/$APP_ID.desktop"
    install -Dm644 "build/linux/$APP_ID.metainfo.xml" "$root/usr/share/metainfo/$APP_ID.metainfo.xml"
    install -Dm644 LICENSE "$root/usr/share/doc/gitext-core/copyright"

    (cd "$STAGE/share/icons" && find hicolor -type f) | while read -r rel; do
        install -Dm644 "$STAGE/share/icons/$rel" "$root/usr/share/icons/$rel"
    done
}

# ---------------------------------------------------------------- .deb

make_deb() {
    echo "== .deb ($PKG_VERSION)"

    local work="$OUT/deb"
    rm -rf "$work"
    mkdir -p "$work/DEBIAN"

    build_tree "$work"

    # Installed-Size is in kilobytes; dpkg-deb doesn't compute it, we provide it.
    local size
    size=$(du -sk "$work/usr" | cut -f1)

    cat > "$work/DEBIAN/control" <<EOF
Package: gitext-core
Version: $PKG_VERSION
Section: vcs
Priority: optional
Architecture: amd64
Maintainer: gitext-core contributors <https://github.com/ibrahimhates/gitext-core>
Installed-Size: $size
Depends: git (>= 1:2.30)
Homepage: https://github.com/ibrahimhates/gitext-core
Triggers: icon-theme, desktop-file
Description: Fast native Git GUI
 gitext-core is a fast, native Git GUI. It rebuilds the GitExtensions experience
 on modern .NET and Avalonia: a commit graph that makes tangled histories readable,
 a UI that maps onto how Git actually works, and speed that stays out of your way.
 .
 It drives the real git command line rather than reimplementing it, so hooks,
 aliases, credential helpers and configuration behave exactly as in a terminal.
 .
 gitext-core collects no telemetry of any kind.
EOF

    local deb="$OUT/gitext-core_${PKG_VERSION}_amd64.deb"

    if command -v dpkg-deb >/dev/null; then
        dpkg-deb --build --root-owner-group "$work" "$deb" >/dev/null
    else
        echo "   dpkg-deb missing — building in a container"
        setup_docker
        docker run --rm -v "$OUT:/out" debian:12 \
            dpkg-deb --build --root-owner-group /out/deb "/out/$(basename "$deb")" >/dev/null
    fi

    echo "   $deb ($(du -h "$deb" | cut -f1))"
}

# ---------------------------------------------------------------- .rpm

make_rpm() {
    echo "== .rpm ($PKG_VERSION)"

    local work="$OUT/rpm"
    rm -rf "$work"
    mkdir -p "$work/BUILDROOT/usr" "$work/SPECS"

    build_tree "$work/BUILDROOT"

    cat > "$work/SPECS/gitext-core.spec" <<EOF
Name:           gitext-core
Version:        $PKG_VERSION
Release:        1.fc41
Summary:        Fast native Git GUI

License:        GPL-3.0-or-later
URL:            https://github.com/ibrahimhates/gitext-core

Requires:       git >= 2.30

# The binary is self-contained and trimmed; rpmbuild's automatic post-processing
# would damage it.
%global __os_install_post %{nil}
%global debug_package %{nil}
AutoReqProv:    no

%description
gitext-core is a fast, native Git GUI. It rebuilds the GitExtensions experience on
modern .NET and Avalonia: a commit graph that makes tangled histories readable, a UI
that maps onto how Git actually works, and speed that stays out of your way.

It drives the real git command line rather than reimplementing it, so hooks, aliases,
credential helpers and configuration behave exactly as in a terminal.

gitext-core collects no telemetry of any kind.

%files
/usr/bin/gitext-core
/usr/share/applications/$APP_ID.desktop
/usr/share/metainfo/$APP_ID.metainfo.xml
/usr/share/icons/hicolor
%license /usr/share/doc/gitext-core/copyright

# Rebuild caches so icons and menu entries appear after install/upgrade.
# %posttrans runs AFTER all packages in a transaction are installed — this
# prevents a partially-upgraded system from showing stale caches.
%posttrans
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database /usr/share/applications 2>/dev/null || true
fi

%changelog
* $(LC_ALL=C date '+%a %b %d %Y') gitext-core contributors - $PKG_VERSION-1
- See https://github.com/ibrahimhates/gitext-core/releases
EOF

    if command -v rpmbuild >/dev/null; then
        rpmbuild --define "_topdir $work" --define "_rpmdir $OUT" \
            --buildroot "$work/BUILDROOT" -bb "$work/SPECS/gitext-core.spec" >/dev/null
    else
        echo "   rpmbuild missing — building in a container"
        setup_docker
        docker run --rm -v "$OUT:/out" fedora:41 sh -c '
            dnf install -y -q rpm-build >/dev/null 2>&1
            rpmbuild --define "_topdir /out/rpm" --define "_rpmdir /out" \
                --buildroot /out/rpm/BUILDROOT -bb /out/rpm/SPECS/gitext-core.spec' >/dev/null
    fi

    # rpmbuild puts the output in an arch-named subfolder; it's moved up to the parent dir.
    #
    # ⚠️ Files produced in the container belong to ROOT: `mv` used to fail with a
    # permission error that got swallowed by `|| true`, leaving the rpm stuck under
    # dist/x86_64/. It's moved without suppressing the error, and if the directory
    # itself can't be removed, it's removed inside the container.
    local produced
    produced=$(find "$OUT/x86_64" -name '*.rpm' 2>/dev/null | head -1)

    if [ -n "$produced" ]; then
        if ! mv "$produced" "$OUT/" 2>/dev/null; then
            setup_docker
            docker run --rm -v "$OUT:/out" fedora:41 \
                sh -c 'mv /out/x86_64/*.rpm /out/ && rmdir /out/x86_64'
        else
            rmdir "$OUT/x86_64" 2>/dev/null || true
        fi
    fi

    # ⚠️ The search is pinned to THIS version. Written as `gitext-core-*.rpm` it matched
    # ANY .rpm lying in dist/, so a leftover from an earlier run was reported as this run's
    # output — and the guard below would have accepted it even if rpmbuild had produced
    # nothing at all. Measured: with a `0.1.0~dryrun` file left over from a previous trial,
    # the freshly built `0.1.0` package was built correctly and the OLD name was printed.
    local rpm
    rpm=$(find "$OUT" -maxdepth 1 -name "gitext-core-$PKG_VERSION-*.rpm" | head -1)

    [ -n "$rpm" ] || { echo "ERROR: could not produce .rpm ($PKG_VERSION)." >&2; exit 1; }

    echo "   $rpm ($(du -h "$rpm" | cut -f1))"
}

case "$WHAT" in
    deb)  make_deb ;;
    rpm)  make_rpm ;;
    both) make_deb; make_rpm ;;
    *)    echo "Usage: $0 [deb|rpm]" >&2; exit 2 ;;
esac
