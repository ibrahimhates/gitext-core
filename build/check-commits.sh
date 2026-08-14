#!/usr/bin/env bash
#
# Commit mesajı biçim denetimi (P10-T03) — Conventional Commits.
#
# Kullanım:
#   build/check-commits.sh                 # origin/main..HEAD
#   build/check-commits.sh v0.9.0..HEAD    # aralığı açıkça ver
#
# ─────────────────────────────────────────────────────────────────────────────
# Sürüm notları commit'lerden üretiliyor (build/release-notes.sh), çünkü bu depoda
# PR akışı yok: ölçüldü, 53 commit'in 50'si doğrudan main'e atılmış. Yani commit
# mesajının biçimi bir üslup tercihi değil, sürüm notunun GİRDİSİ. Biçimi bozuk bir
# commit "Diğer" başlığına düşer ve orada kimse okumaz.
#
# GEÇMİŞE DÖNÜK UYGULANMIYOR: kural bugünden itibaren geçerli. Geçmişteki 6 uyumsuz
# commit (3 dependabot, 2 projenin ilk commit'leri, 1 "init") olduğu gibi bırakılıyor —
# geçmişi yeniden yazmak, düzelttiğinden çok daha fazlasını kırar.

set -euo pipefail

RANGE="${1:-}"

if [ -z "$RANGE" ]; then
    if git rev-parse --verify --quiet origin/main >/dev/null; then
        RANGE="origin/main..HEAD"
    else
        RANGE="HEAD~1..HEAD"
    fi
fi

# Conventional Commits türleri. `!` kırıcı değişikliği işaretler.
PATTERN='^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9.-]+\))?!?: .+'

failed=0
checked=0

while IFS='|' read -r sha subject; do
    [ -n "$sha" ] || continue
    checked=$((checked + 1))

    # Dependabot kendi mesaj biçimini kullanıyor ve onu değiştiremeyiz.
    case "$subject" in
        "Bump "*) continue ;;
    esac

    if ! printf '%s' "$subject" | grep -qE "$PATTERN"; then
        if [ "$failed" -eq 0 ]; then
            echo "Conventional Commits biçimine uymayan commit'ler:" >&2
            echo >&2
        fi
        printf '  %s  %s\n' "${sha:0:7}" "$subject" >&2
        failed=$((failed + 1))
    fi
done < <(git log --no-merges --pretty='%H|%s' "$RANGE" 2>/dev/null)

if [ "$failed" -gt 0 ]; then
    cat >&2 <<'EOF'

Beklenen biçim:  <tür>[(kapsam)][!]: <özet>

  feat(ui): commit grafiğine şerit renkleri eklendi
  fix: detached HEAD'de yanlış dal adı gösteriliyordu
  perf(core): commit okumada metin havuzu
  feat(settings)!: ayar dosyası biçimi değişti     ← kırıcı değişiklik

Türler: feat fix docs style refactor perf test build ci chore revert

Bu biçim sürüm notlarının girdisi (build/release-notes.sh) — uymayan commit'ler
"Diğer" başlığına düşer ve kullanıcı tarafından okunmaz.
EOF
    exit 1
fi

echo "OK: $checked commit denetlendi, hepsi uyumlu."
