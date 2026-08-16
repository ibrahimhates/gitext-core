#!/usr/bin/env sh
#
# gitext-core install script (P10-T08).
#
# Usage:
#   ./install.sh                 # install under ~/.local (no root needed)
#   sudo ./install.sh --system   # install under /usr/local (all users)
#   ./install.sh --uninstall     # uninstall
#
# POSIX sh — bash is not assumed. Minimal containers may not have /bin/bash, and
# the install script not running means the package doesn't run at all.

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
            echo "Unknown option: $arg" >&2
            exit 2
            ;;
    esac
done

user_prefix() {
    p="${XDG_DATA_HOME:-$HOME/.local}"
    # If XDG_DATA_HOME points at ~/.local/share, go up one level: the binary goes to ~/.local/bin.
    case "$p" in
        */share) p="$(dirname "$p")" ;;
    esac
    printf '%s' "$p"
}

# 🔴 If the mode is NOT SPECIFIED on uninstall, the install location is SEARCHED FOR.
# Measured: after installing with `install.sh --system` and calling
# `install.sh --uninstall`, the script looked in the user directory, found nothing,
# said "uninstalled," and left 14 files on the system — with exit code 0.
# Expecting the user to remember which mode they installed with produces exactly
# this silent failure.
if [ -z "$MODE" ] && [ "$ACTION" = "uninstall" ]; then
    if [ -f "/usr/local/bin/$BINARY" ]; then
        MODE="system"
    elif [ -f "$(user_prefix)/bin/$BINARY" ]; then
        MODE="user"
    else
        echo "gitext-core does not appear to be installed (tried /usr/local and $(user_prefix))." >&2
        echo "If you installed to a different location, remove the files by hand." >&2
        exit 1
    fi
    echo "== installation found: $MODE"
fi

[ -n "$MODE" ] || MODE="user"

if [ "$MODE" = "system" ]; then
    PREFIX="/usr/local"
    if [ "$(id -u)" -ne 0 ]; then
        echo "ERROR: system-wide install/uninstall requires root." >&2
        echo "      try 'sudo $0 $*'." >&2
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

# ---------------------------------------------------------------- uninstall

if [ "$ACTION" = "uninstall" ]; then
    echo "== uninstalling ($PREFIX)"
    rm -f "$BIN_DIR/$BINARY"
    rm -f "$APP_DIR/$APP_ID.desktop"
    rm -f "$METAINFO_DIR/$APP_ID.metainfo.xml"
    rm -rf "$LICENSE_DIR"
    # Icons are spread across sizes, so they're removed one by one.
    find "$ICON_ROOT/hicolor" -name "$APP_ID.png" -delete 2>/dev/null || true
    find "$ICON_ROOT/hicolor" -name "$APP_ID.svg" -delete 2>/dev/null || true

    command -v update-desktop-database >/dev/null 2>&1 && \
        update-desktop-database "$APP_DIR" 2>/dev/null || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && \
        gtk-update-icon-cache -f -t "$ICON_ROOT/hicolor" 2>/dev/null || true

    # Saying "uninstalled" isn't enough; it's checked that things were actually removed.
    # A half-finished uninstall leads to the next install colliding with leftover files.
    leftover=$(find "$PREFIX" -name "$APP_ID*" -o -path "*/bin/$BINARY" 2>/dev/null | wc -l)

    if [ "$leftover" -ne 0 ]; then
        echo "!! $leftover file(s) could not be removed:" >&2
        find "$PREFIX" -name "$APP_ID*" -o -path "*/bin/$BINARY" 2>/dev/null >&2
        exit 1
    fi

    echo "   uninstalled."
    echo
    echo "Your settings were NOT deleted. To remove them too:"
    echo "  rm -rf \"\${XDG_CONFIG_HOME:-\$HOME/.config}/gitext-core\""
    exit 0
fi

# ---------------------------------------------------------------- preflight

# git is a RUNTIME dependency (ADR-0002): the app runs git as a subprocess. Without
# it, the install still completes, but the user should know that now, not when
# they first open the app.
if ! command -v git >/dev/null 2>&1; then
    echo "!! WARNING: 'git' was not found on PATH."
    echo "   gitext-core runs git as a subprocess; it will not work without git."
    echo "   Install it with your distro's package manager (e.g. 'sudo apt install git')."
    echo
fi

[ -f "$SELF_DIR/$BINARY" ] || {
    echo "ERROR: '$SELF_DIR/$BINARY' does not exist. The archive may be incompletely extracted." >&2
    exit 1
}

# ---------------------------------------------------------------- install

echo "== installing: $PREFIX"

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
    # Copy while preserving the directory structure: each size into its own folder.
    (cd "$SELF_DIR/share/icons" && find hicolor -type f) | while read -r rel; do
        mkdir -p "$ICON_ROOT/$(dirname "$rel")"
        install -m 644 "$SELF_DIR/share/icons/$rel" "$ICON_ROOT/$rel"
    done
    echo "   $ICON_ROOT/hicolor/... (icons)"
fi

[ -f "$SELF_DIR/LICENSE" ] && install -m 644 "$SELF_DIR/LICENSE" "$LICENSE_DIR/"

# Caches are refreshed so the desktop environment sees the new entry.
# These tools may not exist on minimal systems — their absence is not an error.
command -v update-desktop-database >/dev/null 2>&1 && \
    update-desktop-database "$APP_DIR" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && \
    gtk-update-icon-cache -f -t "$ICON_ROOT/hicolor" 2>/dev/null || true

echo
echo "== installed: $("$BIN_DIR/$BINARY" --version 2>/dev/null | head -1)"

# PATH warning: ~/.local/bin is NOT on the default PATH on some distros.
# Staying silent would result in "I installed it but the command isn't found."
case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *)
        echo
        echo "!! '$BIN_DIR' is not on PATH. Add it to your shell configuration:"
        echo "     export PATH=\"$BIN_DIR:\$PATH\""
        ;;
esac
