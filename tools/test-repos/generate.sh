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

    # Initial commit — every branch forks from HERE.
    echo "root" > root.txt
    git add . && git commit -q -m "initial"

    local root
    root="$(git rev-parse HEAD)"

    # 🔴 Each branch must start from the root, not from the previous branch's tip.
    # `git switch -c` without a start point branches off the CURRENT commit, so an
    # unqualified loop produces one long chain wearing 200 branch labels: 200 refs,
    # zero forks, maximum lane width of 1. That repo cannot stress what it exists to
    # stress. The explicit start point is the whole point of this repo.
    local b BRANCH_COUNT=200
    for ((b = 1; b <= BRANCH_COUNT; b++)); do
        git switch -c "branch/$b" "$root" >/dev/null 2>&1

        # Two commits per branch (init + update).
        echo "content $b" > "branch_$b.txt"
        git add . && git commit -q -m "init branch/$b"

        printf 'update %d\n' "$b" >> "branch_$b.txt"
        git add . && git commit -q -m "update branch/$b"
    done

    git switch -q main 2>/dev/null || git switch -q master 2>/dev/null || true

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
    local total="${GEN_LARGE_COUNT:-250000}"

    mkdir -p "$dir" && pushd "$dir" >/dev/null
    echo "[large-deep] → $dir ($total commits) ..."

    git init -q
    git config user.name "Test Author"
    git config user.email "test@test.com"

    # 🔴 Built with `fast-import`, not a commit loop.
    #
    # Two reasons, both found by measuring:
    #   1. The previous loop committed only every 1000th iteration, so "250000" produced
    #      250 commits — a repo two orders of magnitude smaller than its own name, used
    #      as the scale test. The count was never checked against the label.
    #   2. Even done correctly, 250k `git commit` invocations means 250k processes. At
    #      the ~5 ms per call measured in P09-T02 that is over 20 minutes of pure process
    #      overhead; fast-import does it in one process, in seconds.
    #
    # A synthetic scale repo is worth having precisely because the alternative — cloning
    # something like the kernel — costs gigabytes and cannot be reproduced offline.
    awk -v total="$total" '
        BEGIN {
            print "reset refs/heads/main";
            for (i = 1; i <= total; i++) {
                print "commit refs/heads/main";
                print "mark :" i;
                print "author Test Author <test@test.com> " (1600000000 + i) " +0000";
                print "committer Test Author <test@test.com> " (1600000000 + i) " +0000";
                msg = "commit #" i;
                print "data " length(msg);
                print msg;
                if (i > 1) { print "from :" (i - 1); }
                content = "line " i;
                print "M 644 inline data.txt";
                print "data " length(content);
                print content;
            }
            print "done";
        }
    ' | git fast-import --quiet --done

    git reset -q --hard refs/heads/main

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
