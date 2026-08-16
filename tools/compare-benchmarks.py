#!/usr/bin/env python3
"""P09-T16 — compares benchmark results against a baseline.

Usage:
    python3 tools/compare-benchmarks.py <baseline.json> <current.json> [--threshold 25]

Exit code is ALWAYS 0 — this tool warns, it doesn't fail CI.

    From the plan: "Not to turn it red, but to warn — CI machine noise produces
    false alarms."

The rationale is known from measurement: in P09-T02's `--fast` run (N=3), a
difference of up to 10% was seen between two runs of the same benchmark. Shared CI
machines share CPU with neighboring jobs; the fluctuation there is even larger.
Turning a threshold red would bury real regressions in a noise stream that gets ignored.
"""

import argparse
import json
import sys

# A threshold that stays above CI noise but doesn't miss a real slowdown.
# 25%: more than double the run-to-run fluctuation (~10%) observed in P09-T02.
DEFAULT_THRESHOLD = 25.0


def load(path):
    """Name → mean (ns) from BenchmarkDotNet's `-report-brief.json` file."""
    with open(path, encoding="utf-8") as handle:
        document = json.load(handle)

    results = {}

    for benchmark in document.get("Benchmarks", []):
        # The full name is used: two classes can have a method with the same name,
        # and matching by the short name would compare the wrong pair.
        name = benchmark.get("FullName") or benchmark.get("Method")
        statistics = benchmark.get("Statistics") or {}
        mean = statistics.get("Mean")

        if name and mean:
            results[name] = mean

    return results


def format_duration(nanoseconds):
    if nanoseconds >= 1_000_000:
        return f"{nanoseconds / 1_000_000:.2f} ms"
    if nanoseconds >= 1_000:
        return f"{nanoseconds / 1_000:.2f} µs"
    return f"{nanoseconds:.0f} ns"


def main():
    parser = argparse.ArgumentParser(description="Benchmark regression comparison")
    parser.add_argument("baseline", help="The baseline JSON file")
    parser.add_argument("current", help="The new run's JSON file")
    parser.add_argument(
        "--threshold",
        type=float,
        default=DEFAULT_THRESHOLD,
        help=f"Warning threshold, per cent (default {DEFAULT_THRESHOLD})",
    )
    arguments = parser.parse_args()

    try:
        baseline = load(arguments.baseline)
        current = load(arguments.current)
    except (OSError, json.JSONDecodeError) as error:
        # We do not break on a missing or corrupt file either: having no baseline to compare
        # against is not a regression.
        print(f"::warning::Could not compare: {error}")
        return 0

    shared = sorted(set(baseline) & set(current))

    if not shared:
        print("::warning::No benchmark in common with the baseline — comparison skipped.")
        return 0

    regressions = []
    improvements = []

    for name in shared:
        before = baseline[name]
        after = current[name]

        if before <= 0:
            continue

        change = (after - before) / before * 100.0

        if change >= arguments.threshold:
            regressions.append((name, before, after, change))
        elif change <= -arguments.threshold:
            improvements.append((name, before, after, change))

    print(f"Benchmarks compared: {len(shared)}  ·  threshold: {arguments.threshold:.0f}%")

    # The ones present only in the baseline or only in the new run are not skipped silently:
    # a missing benchmark may have been deleted or renamed, and that needs to be seen.
    for name in sorted(set(baseline) - set(current)):
        print(f"::warning::In the baseline, MISSING from the new run: {name}")

    for name in sorted(set(current) - set(baseline)):
        print(f"  new benchmark (not in the baseline): {name}")

    for name, before, after, change in improvements:
        print(f"  ⚡ {name}: {format_duration(before)} → {format_duration(after)}  ({change:+.1f}%)")

    for name, before, after, change in regressions:
        print(
            f"::warning::Performance regression: {name} "
            f"{format_duration(before)} → {format_duration(after)} ({change:+.1f}%)"
        )

    if regressions:
        print(f"\n{len(regressions)} benchmark(s) slowed down past the threshold.")
    else:
        print("\nNo regression past the threshold.")

    # Deliberate: we do not break CI.
    return 0


if __name__ == "__main__":
    sys.exit(main())
