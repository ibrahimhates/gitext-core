#!/usr/bin/env sh
#
# gitext-core kurulum betiği (P10-T08).
#
# Kullanım:
#   ./install.sh                 # ~/.local altına kur (root gerekmez)
#   sudo ./install.sh --system   # /usr/local altına kur (tüm kullanıcılar)
#   ./install.sh --uninstall     # kaldır
#
# POSIX sh — bash varsayılmıyor. Minimal konteynerlerde /bin/bash olmayabiliyor
# ve kurulum betiğinin çalışmaması, paketin hiç çalışmaması demek.

set -eu

APP_ID="io.github.ibrahimhates.GitExtCore"
BINARY="gitext-core"

SELF_DIR="$(cd "$(dirname "$0")" && pwd)"

MODE=""
ACTION="install"

for arg in "$@"; do
    case "$arg" in
        --system)    MODE="system" ;;
        --user)      MODE="user" ;;
        --uninstall) ACTION="uninstall" ;;
        -h|--help)
            sed -n '3,12p' "$0"
            exit 0
            ;;
        *)
            echo "Bilinmeyen seçenek: $arg" >&2
            exit 2
            ;;
    esac
done

user_prefix() {
    p="${XDG_DATA_HOME:-$HOME/.local}"
    # XDG_DATA_HOME ~/.local/share'i gösteriyorsa bir üste çık: ikili ~/.local/bin'e gider.
    case "$p" in
        */share) p="$(dirname "$p")" ;;
    esac
    printf '%s' "$p"
}

# 🔴 Kaldırmada mod BELİRTİLMEMİŞSE kurulumun nerede olduğu ARANIYOR.
# Ölçüldü: `install.sh --system` ile kurulup `install.sh --uninstall` çağrıldığında
# betik kullanıcı dizinine bakıyor, hiçbir şey bulamıyor, "kaldırıldı" diyor ve
# sistemde 14 dosya bırakıyordu — üstelik çıkış kodu 0. Kullanıcının kurulumu hangi
# modla yaptığını hatırlamasını beklemek, tam da bu sessiz başarısızlığı üretir.
if [ -z "$MODE" ] && [ "$ACTION" = "uninstall" ]; then
    if [ -f "/usr/local/bin/$BINARY" ]; then
        MODE="system"
    elif [ -f "$(user_prefix)/bin/$BINARY" ]; then
        MODE="user"
    else
        echo "gitext-core kurulu görünmüyor (/usr/local ve $(user_prefix) denendi)." >&2
        echo "Farklı bir konuma kurduysanız dosyaları elle silin." >&2
        exit 1
    fi
    echo "== kurulum bulundu: $MODE"
fi

[ -n "$MODE" ] || MODE="user"

if [ "$MODE" = "system" ]; then
    PREFIX="/usr/local"
    if [ "$(id -u)" -ne 0 ]; then
        echo "HATA: sistem geneli kurulum/kaldırma için root gerekiyor." >&2
        echo "      'sudo $0 $*' deneyin." >&2
        exit 1
    fi
else
    PREFIX="$(user_prefix)"
fi

BIN_DIR="$PREFIX/bin"
APP_DIR="$PREFIX/share/applications"
ICON_ROOT="$PREFIX/share/icons"
METAINFO_DIR="$PREFIX/share/metainfo"
LICENSE_DIR="$PREFIX/share/licenses/$BINARY"

# ---------------------------------------------------------------- kaldırma

if [ "$ACTION" = "uninstall" ]; then
    echo "== kaldırılıyor ($PREFIX)"
    rm -f "$BIN_DIR/$BINARY"
    rm -f "$APP_DIR/$APP_ID.desktop"
    rm -f "$METAINFO_DIR/$APP_ID.metainfo.xml"
    rm -rf "$LICENSE_DIR"
    # İkonlar boyut boyut dağıldığı için tek tek siliniyor.
    find "$ICON_ROOT/hicolor" -name "$APP_ID.png" -delete 2>/dev/null || true
    find "$ICON_ROOT/hicolor" -name "$APP_ID.svg" -delete 2>/dev/null || true

    command -v update-desktop-database >/dev/null 2>&1 && \
        update-desktop-database "$APP_DIR" 2>/dev/null || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && \
        gtk-update-icon-cache -f -t "$ICON_ROOT/hicolor" 2>/dev/null || true

    # "Kaldırıldı" demek yetmiyor; gerçekten kaldırıldığı kontrol ediliyor.
    # Yarım kalmış bir kaldırma, sonraki kurulumun eski dosyalarla karışmasına yol açar.
    leftover=$(find "$PREFIX" -name "$APP_ID*" -o -path "*/bin/$BINARY" 2>/dev/null | wc -l)

    if [ "$leftover" -ne 0 ]; then
        echo "!! $leftover dosya kaldırılamadı:" >&2
        find "$PREFIX" -name "$APP_ID*" -o -path "*/bin/$BINARY" 2>/dev/null >&2
        exit 1
    fi

    echo "   kaldırıldı."
    echo
    echo "Ayarlarınız SİLİNMEDİ. Onları da kaldırmak için:"
    echo "  rm -rf \"\${XDG_CONFIG_HOME:-\$HOME/.config}/gitext-core\""
    exit 0
fi

# ---------------------------------------------------------------- ön kontrol

# git bir ÇALIŞMA ZAMANI bağımlılığı (ADR-0002): uygulama git'i alt süreç olarak
# çalıştırıyor. Yoksa kurulum yine de tamamlanıyor ama kullanıcı bunu şimdi bilmeli,
# uygulamayı ilk açtığında değil.
if ! command -v git >/dev/null 2>&1; then
    echo "!! UYARI: 'git' PATH üzerinde bulunamadı."
    echo "   gitext-core git'i alt süreç olarak çalıştırıyor; git olmadan çalışmaz."
    echo "   Dağıtımınızın paket yöneticisiyle kurun (ör. 'sudo apt install git')."
    echo
fi

[ -f "$SELF_DIR/$BINARY" ] || {
    echo "HATA: '$SELF_DIR/$BINARY' yok. Arşiv eksik açılmış olabilir." >&2
    exit 1
}

# ---------------------------------------------------------------- kurulum

echo "== kuruluyor: $PREFIX"

mkdir -p "$BIN_DIR" "$APP_DIR" "$METAINFO_DIR" "$LICENSE_DIR"

install -m 755 "$SELF_DIR/$BINARY" "$BIN_DIR/$BINARY"
echo "   $BIN_DIR/$BINARY"

if [ -f "$SELF_DIR/share/applications/$APP_ID.desktop" ]; then
    install -m 644 "$SELF_DIR/share/applications/$APP_ID.desktop" "$APP_DIR/"
    echo "   $APP_DIR/$APP_ID.desktop"
fi

if [ -f "$SELF_DIR/share/metainfo/$APP_ID.metainfo.xml" ]; then
    install -m 644 "$SELF_DIR/share/metainfo/$APP_ID.metainfo.xml" "$METAINFO_DIR/"
fi

if [ -d "$SELF_DIR/share/icons/hicolor" ]; then
    # Dizin yapısını koruyarak kopyala: her boyut kendi klasörüne.
    (cd "$SELF_DIR/share/icons" && find hicolor -type f) | while read -r rel; do
        mkdir -p "$ICON_ROOT/$(dirname "$rel")"
        install -m 644 "$SELF_DIR/share/icons/$rel" "$ICON_ROOT/$rel"
    done
    echo "   $ICON_ROOT/hicolor/... (ikonlar)"
fi

[ -f "$SELF_DIR/LICENSE" ] && install -m 644 "$SELF_DIR/LICENSE" "$LICENSE_DIR/"

# Masaüstü ortamının yeni girdiyi görmesi için önbellekler tazeleniyor.
# Bu araçlar minimal sistemlerde olmayabilir — yokluğu hata değil.
command -v update-desktop-database >/dev/null 2>&1 && \
    update-desktop-database "$APP_DIR" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && \
    gtk-update-icon-cache -f -t "$ICON_ROOT/hicolor" 2>/dev/null || true

echo
echo "== kuruldu: $("$BIN_DIR/$BINARY" --version 2>/dev/null | head -1)"

# PATH uyarısı: ~/.local/bin bazı dağıtımlarda varsayılan PATH'te DEĞİL.
# Sessiz kalmak, "kurdum ama komut bulunamıyor" ile sonuçlanırdı.
case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *)
        echo
        echo "!! '$BIN_DIR' PATH üzerinde değil. Kabuk yapılandırmanıza ekleyin:"
        echo "     export PATH=\"$BIN_DIR:\$PATH\""
        ;;
esac
