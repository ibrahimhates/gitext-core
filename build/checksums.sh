#!/usr/bin/env bash
#
# SHA256SUMS üretimi ve doğrulaması (P10-T04).
#
# Kullanım:
#   build/checksums.sh              # dist/ içindeki paketler için SHA256SUMS üret
#   build/checksums.sh --verify     # üretilmiş dosyayı doğrula
#
# ─────────────────────────────────────────────────────────────────────────────
# Checksum bir GÜVENLİK önlemi değil, BÜTÜNLÜK önlemidir. Saldırgan dosyayı
# değiştirebiliyorsa SHA256SUMS'ı da değiştirebilir. Yakaladığı şey: yarım inen
# dosya, bozuk ayna, kesilmiş aktarım. GPG imzası olmadan bundan fazlasını
# vaat etmek yanıltıcı olur — README bunu böyle söylemeli.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="${DIST:-$ROOT/dist}"
SUMS="$DIST/SHA256SUMS"

cd "$DIST"

# Yalnızca dağıtılan çıktılar. Ara ürünler (appimagetool, açılmış klasörler)
# checksum listesine girmemeli — kullanıcı onları indirmiyor.
mapfile -t ARTIFACTS < <(
    find . -maxdepth 1 -type f \
        \( -name 'gitext-core-*.tar.gz' \
        -o -name 'gitext-core-*.AppImage' \
        -o -name 'gitext-core-*.zip' \
        -o -name 'gitext-core-*.deb' \
        -o -name 'gitext-core-*.rpm' \
        -o -name 'gitext-core-*.dmg' \
        -o -name 'gitext-core-*.pkg.tar.zst' \) \
        -printf '%P\n' | sort
)

if [ "${1:-}" = "--verify" ]; then
    [ -f "$SUMS" ] || { echo "HATA: $SUMS yok." >&2; exit 1; }
    sha256sum -c "$SUMS"
    exit $?
fi

if [ "${#ARTIFACTS[@]}" -eq 0 ]; then
    # Sessizce boş bir SHA256SUMS üretmek, paketlemenin başarısız olduğunu gizlerdi.
    echo "HATA: $DIST içinde dağıtılabilir çıktı yok. Paketleme çalıştı mı?" >&2
    exit 1
fi

sha256sum "${ARTIFACTS[@]}" > "$SUMS"

echo "== SHA256SUMS (${#ARTIFACTS[@]} dosya)"
cat "$SUMS"

# Ürettiğimiz dosyanın kendisinin doğrulandığını görmeden bitirmiyoruz:
# yanlış dizinden üretilmiş bir liste de "başarılı" görünür.
echo
echo "== doğrulama"
sha256sum -c "$SUMS"

# ---------------------------------------------------------------- GPG (opsiyonel)
#
# İmzalama yalnızca bir anahtar AÇIKÇA verilmişse yapılıyor. Anahtar yoksa adım
# ATLANDIĞI SÖYLENEREK geçiliyor — sessizce geçmek, imzalı sanılan imzasız bir
# sürüm üretirdi.
if [ -n "${GPG_KEY_ID:-}" ]; then
    echo
    echo "== GPG imzası ($GPG_KEY_ID)"
    gpg --batch --yes --local-user "$GPG_KEY_ID" --armor --detach-sign "$SUMS"
    echo "   $SUMS.asc"
else
    echo
    echo "-- GPG imzalama ATLANDI (GPG_KEY_ID verilmedi)."
fi
