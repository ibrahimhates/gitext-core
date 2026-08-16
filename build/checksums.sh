#!/usr/bin/env bash
#
# SHA256SUMS generation and verification (P10-T04).
#
# Usage:
#   build/checksums.sh              # generate SHA256SUMS for packages in dist/
#   build/checksums.sh --verify     # verify the generated file
#
# ─────────────────────────────────────────────────────────────────────────────
# A checksum is not a SECURITY measure, it's an INTEGRITY measure. If an attacker
# can modify the file, they can modify SHA256SUMS too. What it catches: a
# half-downloaded file, a broken mirror, a truncated transfer. Promising more than
# that without a GPG signature would be misleading — the README should say so.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="${DIST:-$ROOT/dist}"
SUMS="$DIST/SHA256SUMS"

cd "$DIST"

# Distributed outputs only. Intermediate artifacts (appimagetool, extracted folders)
# must not enter the checksum list — the user doesn't download those.
# ⚠️ The pattern is `gitext-core[-_]*`, NOT `gitext-core-*`: Debian package names use
# an underscore (gitext-core_1.0.0_amd64.deb). When only the hyphen was matched, .deb
# silently fell outside the list — the checksum file was missing something but looked
# "successful."
mapfile -t ARTIFACTS < <(
    find . -maxdepth 1 -type f \
        \( -name 'gitext-core[-_]*.tar.gz' \
        -o -name 'gitext-core[-_]*.AppImage' \
        -o -name 'gitext-core[-_]*.zip' \
        -o -name 'gitext-core[-_]*.deb' \
        -o -name 'gitext-core[-_]*.rpm' \
        -o -name 'gitext-core[-_]*.dmg' \
        -o -name 'gitext-core[-_]*.pkg.tar.zst' \) \
        -printf '%P\n' | sort
)

if [ "${1:-}" = "--verify" ]; then
    [ -f "$SUMS" ] || { echo "ERROR: $SUMS does not exist." >&2; exit 1; }
    sha256sum -c "$SUMS"
    exit $?
fi

if [ "${#ARTIFACTS[@]}" -eq 0 ]; then
    # Silently producing an empty SHA256SUMS would hide that packaging failed.
    echo "ERROR: no distributable output in $DIST. Did packaging run?" >&2
    exit 1
fi

sha256sum "${ARTIFACTS[@]}" > "$SUMS"

echo "== SHA256SUMS (${#ARTIFACTS[@]} files)"
cat "$SUMS"

# We don't finish without seeing the file we produced verify itself: a list
# generated from the wrong directory would also look "successful."
echo
echo "== verification"
sha256sum -c "$SUMS"

# ---------------------------------------------------------------- GPG (optional)
#
# Signing only happens if a key is EXPLICITLY given. If there's no key, the step is
# passed with the SKIP announced — passing silently would produce a release that
# looks signed but isn't.
if [ -n "${GPG_KEY_ID:-}" ]; then
    echo
    echo "== GPG signature ($GPG_KEY_ID)"
    gpg --batch --yes --local-user "$GPG_KEY_ID" --armor --detach-sign "$SUMS"
    echo "   $SUMS.asc"
else
    echo
    echo "-- GPG signing SKIPPED (GPG_KEY_ID not given)."
fi
