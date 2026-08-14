#!/usr/bin/env bash
#
# Sürüm türetme — TÜM paketleme betiklerinin ortak kaynağı (P10-T01, ADR-0006).
#
# Kullanım:
#   source build/version.sh
#   VERSION="$(gitext_version)"
#
# veya doğrudan:
#   build/version.sh          → sürümü yazdırır
#   build/version.sh --check  → sürümün yayınlanabilir olduğunu doğrular
#
# ─────────────────────────────────────────────────────────────────────────────
# NEDEN BU DOSYA VAR
#
# Sürüm git tag'inden MinVer ile türetiliyor. Betiklerin bunu KENDİ başına
# hesaplaması (git describe, dosyadan okuma…) ikinci bir kaynak yaratır ve iki
# kaynak er ya da geç ayrışır: paketin adı 1.0.0, içindeki ikili 0.9.1 der.
# Bu yüzden sürüm MSBuild'e sorulur — ikiliye gömülen değerin ta kendisine.
#
# ⚠️ ÖLÇÜLDÜ (P10-T00) — MinVer `-p:Version=` parametresini EZİYOR. `-p:Version=7.7.7`
# verilse bile çıktı tag'den türetilen sürüm oluyor, uyarı yok. Eski package.sh tam da
# bunu kullanıyordu; MinVer eklendiği an o parametre sessizce etkisiz kaldı.
# Sürümü dışarıdan dayatmanın TEK geçerli yolu MinVerVersionOverride.

set -euo pipefail

_gitext_root() {
    cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd
}

# Yayınlanamaz sürüm: tag yokken MinVer'in ürettiği varsayılan.
# Bir paketin adında bunu görmek, tag'in gelmediği anlamına gelir.
GITEXT_UNRELEASABLE_PREFIX="0.0.0-alpha.0"

gitext_version() {
    local root
    root="$(_gitext_root)"

    # MinVerVersionOverride verilmişse ona uy — CI'da workflow_dispatch ile etiketsiz
    # deneme sürümü üretmenin tek yolu bu.
    if [ -n "${MINVER_VERSION_OVERRIDE:-}" ]; then
        printf '%s\n' "$MINVER_VERSION_OVERRIDE"
        return 0
    fi

    # MSBuild'e sor: ikiliye gömülecek değerin aynısı.
    # `-t:MinVer` şart — sürüm bir target içinde hesaplanıyor, property olarak
    # değerlendirilmiyor; target çalıştırılmazsa boş/varsayılan dönüyor.
    #
    # Tek property istendiğinde MSBuild düz metin döndürüyor (birden çok istenirse JSON).
    # Ölçüldü: çıktı tam olarak "0.0.0-alpha.0.49\n".
    local version
    version="$(dotnet msbuild "$root/src/GitExt.Desktop/GitExt.Desktop.csproj" \
        -t:MinVer -getProperty:MinVerVersion -nologo 2>/dev/null | tr -d '\r\n')" || {
        echo "HATA: sürüm MSBuild'den okunamadı." >&2
        return 1
    }

    if [ -z "$version" ]; then
        echo "HATA: MinVerVersion boş döndü. MinVer paketi eksik olabilir." >&2
        return 1
    fi

    printf '%s\n' "$version"
}

# Sürümün gerçekten yayınlanabilir olduğunu doğrular.
#
# ⚠️ ÖLÇÜLDÜ (P10-T00) — actions/checkout varsayılanı `fetch-depth: 1`; sığ klonda
# tag'ler HİÇ gelmiyor ve MinVer sessizce 0.0.0-alpha.0 üretiyor. Bu koruma olmadan
# `v1.0.0` tag'ine basmak "0.0.0-alpha.0" adlı bir sürüm yayınlar ve hiçbir adım
# kırmızıya dönmez. Sessiz yanlış sürüm, kırık build'den çok daha pahalı.
gitext_require_releasable_version() {
    local version="${1:-}"
    [ -n "$version" ] || version="$(gitext_version)"

    case "$version" in
        "$GITEXT_UNRELEASABLE_PREFIX"*)
            cat >&2 <<EOF
HATA: sürüm '$version' — bu yayınlanabilir bir sürüm DEĞİL.

MinVer geçerli bir sürüm tag'i bulamadı ve varsayılana düştü. Olası nedenler:

  1. Sığ klon (en yaygın): CI'da tag'ler getirilmemiş.
     → actions/checkout adımına 'fetch-depth: 0' ekleyin.
  2. Depoda hiç 'v*' tag'i yok.
     → git tag v1.0.0 && git push --tags
  3. Tag 'v' öneksiz atılmış (ADR-0006 'v' önekli tanımlıyor).

Etiketsiz deneme sürümü üretmek istiyorsanız açıkça belirtin:
  MINVER_VERSION_OVERRIDE=1.0.0-test build/linux/package.sh
EOF
            return 1
            ;;
    esac

    printf '%s\n' "$version"
}

# Doğrudan çalıştırıldıysa (source edilmediyse)
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
    case "${1:-}" in
        --check)
            # `local` yok (fonksiyon dışı) ve atama ile komut ayrı satırda:
            # `v="$(...)"` biçimi alt kabuğun çıkış kodunu YUTAR ve set -e devreye girmez.
            v="$(gitext_require_releasable_version)" || exit 1
            echo "OK: $v"
            ;;
        *) gitext_version ;;
    esac
fi
