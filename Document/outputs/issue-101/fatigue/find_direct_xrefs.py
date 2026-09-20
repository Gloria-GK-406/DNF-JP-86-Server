#!/usr/bin/env python3
"""Find bounded direct-call and absolute-address xrefs in the A21 RVA dump."""

from __future__ import annotations

import argparse
import struct
from pathlib import Path


DEFAULT_DUMP = Path(
    r"E:\Project\DNF\DNFClient-A21-protocol\dumps"
    r"\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin"
)
IMAGE_BASE = 0x00400000
TEXT_END = 0x02C29000


def parse_int(value: str) -> int:
    return int(value, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("targets", nargs="+", type=parse_int)
    parser.add_argument("--dump", type=Path, default=DEFAULT_DUMP)
    parser.add_argument("--start", type=parse_int, default=0)
    parser.add_argument("--end", type=parse_int, default=TEXT_END)
    args = parser.parse_args()

    blob = args.dump.read_bytes()
    lo, hi = args.start, min(args.end, len(blob))
    for target in args.targets:
        print(f"target=0x{target:08X}")
        calls = []
        for site in range(lo, hi - 4):
            if blob[site] != 0xE8:
                continue
            displacement = struct.unpack_from("<i", blob, site + 1)[0]
            if site + 5 + displacement == target:
                calls.append(site)
        print("direct_calls=" + ",".join(f"0x{x:08X}" for x in calls))

        # Client instructions carry image-base-adjusted VAs in absolute operands.
        needle = struct.pack("<I", target + IMAGE_BASE)
        refs = []
        cursor = lo
        while True:
            site = blob.find(needle, cursor, hi)
            if site < 0:
                break
            refs.append(site)
            cursor = site + 1
        print("absolute_va_bytes=" + ",".join(f"0x{x:08X}" for x in refs))


if __name__ == "__main__":
    main()
