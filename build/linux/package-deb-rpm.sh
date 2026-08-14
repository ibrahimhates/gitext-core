#!/usr/bin/env bash
#
# Debian (.deb) ve Fedora (.rpm) paketleri (P10-T11, P10-T12).
#
# Kullanım:
#   build/linux/package-deb-rpm.sh          # ikisini de üret
#   build/linux/package-deb-rpm.sh deb      # yalnızca .deb
#   build/linux/package-deb-rpm.sh rpm      # yalnızca .rpm
#
# Önkoşul: build/linux/package.sh çalışmış olmalı (dist/linux-x64/gitext-core hazır).
#
# ─────────────────────────────────────────────────────────────────────────────
# ⚠️ ÖLÇÜLDÜ — dpkg-deb ve rpmbuild bu makinede (Arch) KURULU DEĞİL. Araç yoksa
# paket bir konteynerde üretiliyor. Araçları host'a kurmayı zorunlu kılmak,
# yayın yapabilmeyi geliştirme makinesinin dağıtımına bağlardı.

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
    echo "HATA: $STAGE/gitext-core yok. Önce build/linux/package.sh çalıştırın." >&2
    exit 1
}

WHAT="${1:-both}"

# Semver ön sürümü ("-rc.1") paket sürümlerinde GEÇERSİZ:
#   deb — '-' revizyon ayırıcısı; '~' ise sürüm sıralamasında 1.0.0'dan ÖNCE gelir,
#         yani 1.0.0~rc.1 < 1.0.0. Doğru davranış tam olarak bu.
#   rpm — '-' hiç kullanılamaz; aynı nedenle '~' kullanılıyor.
# Yalnızca İLK '-' dönüştürülüyor: "1.0.1-alpha.0.3" → "1.0.1~alpha.0.3".
PKG_VERSION="${VERSION/-/\~}"

# Docker gerekiyorsa hazırla. Kullanıcının global docker yapılandırmasına DOKUNULMUYOR:
# Docker Desktop'tan kalma bozuk bir credsStore/context yaygın (P10-T00'de ölçüldü).
setup_docker() {
    DOCKER_CFG="$(mktemp -d)"
    printf '{"auths":{}}' > "$DOCKER_CFG/config.json"
    export DOCKER_CONFIG="$DOCKER_CFG"
    export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"
}

# ---------------------------------------------------------------- ortak ağaç

# Her iki paket de aynı dosya düzenini kuruyor; tek yerde hazırlanıyor.
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

    # Installed-Size kilobayt cinsinden; dpkg-deb hesaplamıyor, biz veriyoruz.
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
        echo "   dpkg-deb yok — konteynerde üretiliyor"
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
Release:        1%{?dist}
Summary:        Fast native Git GUI

License:        GPL-3.0-or-later
URL:            https://github.com/ibrahimhates/gitext-core

Requires:       git >= 2.30

# İkili self-contained ve trimmed; rpmbuild'in otomatik işlemleri ona zarar verir.
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

%changelog
* $(LC_ALL=C date '+%a %b %d %Y') gitext-core contributors - $PKG_VERSION-1
- See https://github.com/ibrahimhates/gitext-core/releases
EOF

    if command -v rpmbuild >/dev/null; then
        rpmbuild --define "_topdir $work" --define "_rpmdir $OUT" \
            --buildroot "$work/BUILDROOT" -bb "$work/SPECS/gitext-core.spec" >/dev/null
    else
        echo "   rpmbuild yok — konteynerde üretiliyor"
        setup_docker
        docker run --rm -v "$OUT:/out" fedora:41 sh -c '
            dnf install -y -q rpm-build >/dev/null 2>&1
            rpmbuild --define "_topdir /out/rpm" --define "_rpmdir /out" \
                --buildroot /out/rpm/BUILDROOT -bb /out/rpm/SPECS/gitext-core.spec' >/dev/null
    fi

    # rpmbuild çıktıyı mimariye göre alt klasöre koyuyor; üst dizine taşınıyor.
    #
    # ⚠️ Konteynerde üretilen dosyalar ROOT'a ait: `mv` izin hatası verip
    # `|| true` ile yutuluyordu ve rpm dist/x86_64/ altında kalıyordu. Hata
    # bastırılmadan taşınıyor, dizin de kendisi silinemezse konteynerde siliniyor.
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

    local rpm
    rpm=$(find "$OUT" -maxdepth 1 -name 'gitext-core-*.rpm' | head -1)

    [ -n "$rpm" ] || { echo "HATA: .rpm üretilemedi." >&2; exit 1; }

    echo "   $rpm ($(du -h "$rpm" | cut -f1))"
}

case "$WHAT" in
    deb)  make_deb ;;
    rpm)  make_rpm ;;
    both) make_deb; make_rpm ;;
    *)    echo "Kullanım: $0 [deb|rpm]" >&2; exit 2 ;;
esac
