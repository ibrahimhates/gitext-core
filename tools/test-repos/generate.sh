#!/usr/bin/env bash
# ── Test Repository Generator for P09-T01 ────────────────────────────────
# Generates reproducible Git repos of varying sizes/complexities to drive
# performance benchmarking (Faz 09). All output goes under a single directory.
#
# Usage:
#   bash generate.sh ./test-repos              # small + medium + wide + many-files
#   GEN_LARGE=1    bash generate.sh ./test-repos  # adds large-deep (~250k commits)
#
# Repos created:
#   small      — ~500 linear commits           (quick smoke test)
#   medium     — ~3.2k commits + 10 merges     (graph layout baseline)
#   wide       — ~400 commits + 200 parallel   (branch panel stress)
#   many-files — 1 k files in one commit       (working-tree scan)
#   large-deep — ~250k linear commits           (regression guard, optional)
# ───────────────────────────────────────────────────────────────────────────

set -euo pipefail

OUTPUT_DIR="${1:?Usage: bash $0 <output-dir>}"

echo "git version $(git --version)"
echo "=== Test Repo Generator (P09-T01) ==="
echo "Output: $OUTPUT_DIR"

# ── helpers ────────────────────────────────────────────────────────────────
ensure_git() {
    if ! command -v git >/dev/null 2>&1; then
        echo "FATAL: git not found in PATH" >&2
        exit 1
    fi
}

init_repo() {
    local dir="$1"
    mkdir -p "$dir/src"
    pushd "$dir" >/dev/null
    git init -q
    git config user.name "Test Author"
    git config user.email "test@test.com"
}

# Create a real merge commit using plumbing commands (avoids git 2.55 merge bug).
# Usage: create_merge <branch_name> <message>
create_merge() {
    local branch_sha="$1"
    local msg="$2"
    # Tree of current HEAD, parents are HEAD and the feature branch tip.
    local merge_sha
    merge_sha=$(git commit-tree "$(git rev-parse HEAD^{tree})" \
        -p HEAD -p "$branch_sha" -m "$msg")
    git update-ref refs/heads/main "$merge_sha"
}

# ── small: ~500 linear commits ─────────────────────────────────────────────
gen_small() {
    local dir="$OUTPUT_DIR/small"
    echo "[small] → $dir ..."

    init_repo "$dir"

    # Initial commit so we have a branch to work on.
    echo "fn main() {}" > src/file.cs
    git add . && git commit -q -m "initial"

    local i
    for ((i = 1; i <= 500; i++)); do
        printf '// small #%d\n' "$i" >> src/file.cs
        git add . && git commit -q -m "small commit #$i"
    done

    popd >/dev/null
}

# ── medium: ~2k linear + 10 feature branches (20 commits each) with merges
gen_medium() {
    local dir="$OUTPUT_DIR/medium"
    echo "[medium] → $dir ..."

    init_repo "$dir"

    # Initial commit so we have a branch to work on.
    echo "fn main() {}" > src/file.cs
    git add . && git commit -q -m "initial"

    local BRANCH_NAME="main"

    # Create 10 feature branches, each with ~20 commits, merged back.
    local f i MERGE_COUNT=10
    for ((f = 1; f <= MERGE_COUNT; f++)); do
        git switch -c "feature/$f" >/dev/null 2>&1

        for ((i = 1; i <= 20; i++)); do
            printf '// feature %d #%d\n' "$f" "$i" >> "src/feature_$f.cs"
            git add . && git commit -q -m "feature/$f #$i"
        done

        # Create a diverge commit on main so merge is NOT fast-forward.
        git checkout "$BRANCH_NAME" >/dev/null 2>&1 || true
        printf '// main diverge #%d\n' "$f" >> src/main.cs
        git add . && git commit -q -m "main diverge #$f"

        # Create a real merge commit (plumbing, avoids git 2.55 bug).
        create_merge "feature/$f" "Merge feature/$f into main"
    done

    # 2000 linear commits on the default branch.
    for ((i = 1; i <= 2000; i++)); do
        printf '// main #%d\n' "$i" >> src/main.cs
        git add . && git commit -q -m "main update #$i"
    done

    popd >/dev/null
}

# ── wide: ~400 commits + 200 parallel branches (no merges) ────────────────
gen_wide() {
    local dir="$OUTPUT_DIR/wide"
    echo "[wide] → $dir ..."

    init_repo "$dir"

    # Initial commit.
    echo "root" > root.txt
    git add . && git commit -q -m "initial"

    local BRANCH_NAME="main"
    local b i BRANCH_COUNT=200
    for ((b = 1; b <= BRANCH_COUNT; b++)); do
        git switch -c "branch/$b" >/dev/null 2>&1

        # Two commits per branch (init + update).
        echo "content $b" > "branch_$b.txt"
        git add . && git commit -q -m "init branch/$b"

        printf 'update %d\n' "$b" >> "branch_$b.txt"
        git add . && git commit -q -m "update branch/$b"
    done

    popd >/dev/null
}

# ── many-files: 1 k files in a single commit ───────────────────────────────
gen_many_files() {
    local dir="$OUTPUT_DIR/many-files"
    echo "[many-files] → $dir ..."

    init_repo "$dir"

    # This repo is purely about file count — one massive initial commit.
    local f
    for ((f = 1; f <= 1000; f++)); do
        echo "file #$f content" > "file_$f.txt"
    done

    git add . && git commit -q -m "Initial commit with 1000 files"

    popd >/dev/null
}

# ── large-deep: ~250k linear commits (optional, takes time) ────────────────
gen_large_deep() {
    local dir="$OUTPUT_DIR/large-deep"
    mkdir -p "$dir" && pushd "$dir" >/dev/null
    echo "[large-deep] → $dir ..."

    git init -q
    git config user.name "Test Author"
    git config user.email "test@test.com"

    local total=250000
    local i
    for ((i = 1; i <= total; i++)); do
        printf '%d\n' "$i" >> data.txt
        if (( i % 1000 == 0 )); then
            git add . && git commit -q -m "batch #$(( i / 1000 ))"
            echo "  ... $i / $total ..." >&2
        fi
    done

    popd >/dev/null
}

# ── main ───────────────────────────────────────────────────────────────────
main() {
    ensure_git

    # Clean up any previous run.
    rm -rf "$OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"

    gen_small
    gen_medium
    gen_wide
    gen_many_files

    if [[ "${GEN_LARGE:-0}" == "1" ]]; then
        gen_large_deep
    else
        echo "Skipping large-deep. Set GEN_LARGE=1."
    fi

    echo ""
    echo "=== Done ==="
    local name d size commits
    for d in "$OUTPUT_DIR"/*/; do
        [[ -d "$d" ]] || continue
        name=$(basename "$d")
        size=$(du -sh -- "$d" 2>/dev/null | cut -f1)
        commits=$(git -C "$d" rev-list --all --count 2>/dev/null || echo "?")
        printf "  %-15s %6s commits, %5s disk\n" "$name" "$commits" "$size"
    done | sort
}

main
