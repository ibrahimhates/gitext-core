#!/usr/bin/env bash
#
# Sürüm notu üretimi (P10-T03).
#
# Kullanım:
#   build/release-notes.sh v1.0.0            # v0.9.0..v1.0.0 arası
#   build/release-notes.sh v1.0.0 v0.9.0     # aralığı açıkça ver
#
# ─────────────────────────────────────────────────────────────────────────────
# NEDEN GITHUB'IN OTOMATİK NOTLARI YETMİYOR
#
# ⚠️ ÖLÇÜLDÜ (P10-T03) — bu depoda 53 commit'in 50'si DOĞRUDAN main'e atılmış;
# yalnızca 3 PR var, üçü de dependabot. GitHub'ın `generate_release_notes` özelliği
# notları PR başlıklarından derliyor. Tek geliştiricili, PR'sız bir akışta neredeyse
# boş bir sürüm notu üretir — "* Bump actions/checkout from 4 to 7" ve gerisi yok.
#
# Bu betik notları COMMIT'lerden üretiyor. Conventional Commits kullanılıyor
# (ölçüldü: 53 commit'in 44'ü uyumlu; uyumsuz 6'nın 3'ü dependabot, 2'si projenin
# ilk günkü commit'leri), yani commit'lerin kendisi zaten kategorize edilebilir durumda.
#
# Uyumsuz commit'ler ATLANMIYOR: "Diğer" başlığı altında listeleniyor. Sessizce
# düşürmek, sürüm notunun eksik olduğunu gizlerdi — bir kullanıcının aradığı değişiklik
# tam da biçime uymayan commit'te olabilir.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TAG="${1:-}"
PREVIOUS="${2:-}"

if [ -z "$TAG" ]; then
    echo "Kullanım: $0 <tag> [önceki-tag]" >&2
    exit 2
fi

REPO_URL="https://github.com/ibrahimhates/gitext-core"

# Önceki tag verilmediyse: bu tag'den önceki en yakın sürüm tag'i.
if [ -z "$PREVIOUS" ]; then
    PREVIOUS="$(git describe --tags --abbrev=0 "${TAG}^" 2>/dev/null || true)"
fi

if [ -n "$PREVIOUS" ]; then
    RANGE="${PREVIOUS}..${TAG}"
else
    # İlk sürüm: geçmişin tamamı.
    RANGE="$TAG"
fi

# Commit'i biçimlendirir: "- konu ([sha](url))"
emit() {
    local pattern="$1" heading="$2" body found=0

    while IFS='|' read -r sha subject; do
        [ -n "$sha" ] || continue

        # "feat(ui): şunu yap" → "şunu yap" (tür ve kapsam başlıkta zaten var)
        local text="${subject#*: }"

        if [ "$found" -eq 0 ]; then
            printf '\n### %s\n\n' "$heading"
            found=1
        fi

        printf -- '- %s ([%s](%s/commit/%s))\n' "$text" "${sha:0:7}" "$REPO_URL" "$sha"
    done < <(git log --no-merges --pretty='%H|%s' "$RANGE" | grep -E "\|${pattern}" || true)

    return 0
}

printf '## %s\n' "$TAG"

if [ -n "$PREVIOUS" ]; then
    printf '\n[%s ile karşılaştır](%s/compare/%s...%s)\n' \
        "$PREVIOUS" "$REPO_URL" "$PREVIOUS" "$TAG"
fi

# Kırıcı değişiklikler EN ÜSTTE: kullanıcının görmesi gereken ilk şey.
# `feat!:` veya `feat(ui)!:` biçimi (Conventional Commits § kırıcı değişiklik).
emit '(feat|fix|perf|refactor|build)(\([a-z0-9-]+\))?!: ' '⚠️ Kırıcı değişiklikler'

# Geri almalar üstlerde: bir şeyin geri alındığını duymak, yeni bir özelliği
# duymaktan daha aciltir — kullanıcı önceki sürümde ona güvenmiş olabilir.
emit 'revert(\([a-z0-9-]+\))?: '   'Geri alınanlar'

emit 'feat(\([a-z0-9-]+\))?: '     'Yeni özellikler'
emit 'fix(\([a-z0-9-]+\))?: '      'Düzeltmeler'
emit 'perf(\([a-z0-9-]+\))?: '     'Performans'
emit 'refactor(\([a-z0-9-]+\))?: ' 'İç düzenleme'
emit 'docs(\([a-z0-9-]+\))?: '     'Belgeler'
emit '(build|ci)(\([a-z0-9-]+\))?: ' 'Derleme ve CI'
emit 'test(\([a-z0-9-]+\))?: '     'Testler'

# `chore:` ve `style:` BİLİNÇLİ olarak listelenmiyor: tanımları gereği kullanıcıya
# görünen bir değişiklik içermezler (bağımlılık yükseltmesi, biçimlendirme). Sürüm
# notunu bunlarla doldurmak, okunmasını gereken satırları gömerdi.
# Diğer HER commit görünür — biçime uymayanlar dahil (aşağıda).

# Biçime uymayanlar — atılmıyor, görünür kılınıyor.
{
    others="$(git log --no-merges --pretty='%H|%s' "$RANGE" \
        | grep -vE '\|(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9-]+\))?!?: ' || true)"

    if [ -n "$others" ]; then
        printf '\n### Diğer\n\n'
        while IFS='|' read -r sha subject; do
            [ -n "$sha" ] || continue
            printf -- '- %s ([%s](%s/commit/%s))\n' "$subject" "${sha:0:7}" "$REPO_URL" "$sha"
        done <<< "$others"
    fi
}

printf '\n---\n\n'
printf 'Kurulum yönergeleri: [README](%s#installation)\n' "$REPO_URL"
printf '\nBu sürümdeki çıktıların bütünlüğü `SHA256SUMS` ile doğrulanabilir.\n'
