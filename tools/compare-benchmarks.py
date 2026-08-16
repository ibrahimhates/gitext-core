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
    parser = argparse.ArgumentParser(description="Benchmark regresyon karşılaştırması")
    parser.add_argument("baseline", help="Temel çizgi JSON dosyası")
    parser.add_argument("current", help="Yeni koşunun JSON dosyası")
    parser.add_argument(
        "--threshold",
        type=float,
        default=DEFAULT_THRESHOLD,
        help=f"Uyarı eşiği, yüzde (varsayılan {DEFAULT_THRESHOLD})",
    )
    arguments = parser.parse_args()

    try:
        baseline = load(arguments.baseline)
        current = load(arguments.current)
    except (OSError, json.JSONDecodeError) as error:
        # Dosya yoksa veya bozuksa da kırmıyoruz: karşılaştırılacak bir temel çizgi
        # olmaması bir regresyon değil.
        print(f"::warning::Karşılaştırma yapılamadı: {error}")
        return 0

    shared = sorted(set(baseline) & set(current))

    if not shared:
        print("::warning::Temel çizgiyle ortak benchmark yok — karşılaştırma atlandı.")
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

    print(f"Karşılaştırılan benchmark: {len(shared)}  ·  eşik: %{arguments.threshold:.0f}")

    # Yalnızca temel çizgide veya yalnızca yeni koşuda olanlar sessizce atlanmıyor:
    # eksik bir benchmark, silinmiş ya da adı değişmiş olabilir ve bunu görmek gerekir.
    for name in sorted(set(baseline) - set(current)):
        print(f"::warning::Temel çizgide var, yeni koşuda YOK: {name}")

    for name in sorted(set(current) - set(baseline)):
        print(f"  yeni benchmark (temel çizgide yok): {name}")

    for name, before, after, change in improvements:
        print(f"  ⚡ {name}: {format_duration(before)} → {format_duration(after)}  ({change:+.1f}%)")

    for name, before, after, change in regressions:
        print(
            f"::warning::Performans gerilemesi: {name} "
            f"{format_duration(before)} → {format_duration(after)} ({change:+.1f}%)"
        )

    if regressions:
        print(f"\n{len(regressions)} benchmark eşiğin üstünde yavaşladı.")
    else:
        print("\nEşiği aşan gerileme yok.")

    # Kasıtlı: CI'yı kırmıyoruz.
    return 0


if __name__ == "__main__":
    sys.exit(main())
