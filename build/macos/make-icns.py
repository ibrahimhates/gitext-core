#!/usr/bin/env python3
"""Produces a macOS .icns icon (P10-T20).

Usage:
    build/macos/make-icns.py <output.icns> <size>:<png> [<size>:<png> ...]

⚠️ MEASURED — `iconutil` (macOS-specific), `png2icns` and `icnsutil` are not on this
machine, and none of them are standard on Linux either. Since the ICNS format is
simple enough, it's written directly here: tying the release pipeline to a tool that
might not be installed would make the macOS package unbuildable.

FORMAT (Apple Icon Image):
    Header:  'icns' (4 bytes) + total file length (4 bytes, big-endian)
    Entries: type (4 bytes) + entry length (4 bytes, big-endian) + data
             Entry length INCLUDES THE HEADER TOO (i.e. data + 8).

Modern types carry the PNG data as-is — no conversion needed.
"""

import struct
import sys

# Size → ICNS type code. Types starting with 'ic' accept PNG.
#
# Retina (@2x) types are SEPARATE codes: 'ic12' presents the 32 px data as the 2x
# version of the 16 px slot. Providing both prevents Finder from drawing a blurry
# icon on HiDPI screens.
TYPES = {
    16: b"icp4",
    32: b"icp5",
    64: b"icp6",
    128: b"ic07",
    256: b"ic08",
    512: b"ic09",
    1024: b"ic10",   # 512@2x
}

RETINA = {
    32: b"ic11",     # 16@2x
    64: b"ic12",     # 32@2x
    256: b"ic13",    # 128@2x
    512: b"ic14",    # 256@2x
}


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2

    output = sys.argv[1]
    entries: list[tuple[bytes, bytes]] = []

    for spec in sys.argv[2:]:
        size_text, _, path = spec.partition(":")

        if not path:
            print(f"ERROR: '{spec}' is not in <size>:<png> format.", file=sys.stderr)
            return 2

        size = int(size_text)

        with open(path, "rb") as handle:
            data = handle.read()

        if not data.startswith(b"\x89PNG"):
            print(f"ERROR: {path} is not a PNG.", file=sys.stderr)
            return 1

        if size in TYPES:
            entries.append((TYPES[size], data))

        # The same PNG goes into both the normal and retina slots: a 32 px image is
        # valid both as the 32 px icon and as the 2x version of the 16 px icon.
        if size in RETINA:
            entries.append((RETINA[size], data))

    if not entries:
        print("ERROR: no valid size given.", file=sys.stderr)
        return 1

    body = b"".join(
        kind + struct.pack(">I", len(data) + 8) + data
        for kind, data in entries
    )

    with open(output, "wb") as handle:
        handle.write(b"icns" + struct.pack(">I", len(body) + 8) + body)

    print(f"   {output} ({len(body) + 8} bytes, {len(entries)} entries)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
