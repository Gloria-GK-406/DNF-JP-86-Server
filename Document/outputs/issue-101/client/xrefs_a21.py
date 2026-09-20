#!/usr/bin/env python3
"""List direct x86 rel32 call/jump references in an A21 RVA-indexed dump."""

from __future__ import annotations

import argparse
import struct
from pathlib import Path


IMAGE_BASE = 0x00400000


def number(value: str) -> int:
    return int(value, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("dump", type=Path)
    parser.add_argument("targets", nargs="+", type=number, help="target RVAs")
    args = parser.parse_args()
    data = args.dump.read_bytes()

    for target in args.targets:
        print(f"target RVA {target:08X} VA {target + IMAGE_BASE:08X}")
        for opcode, label in ((0xE8, "call"), (0xE9, "jmp")):
            pos = 0
            while True:
                pos = data.find(bytes((opcode,)), pos, len(data) - 4)
                if pos < 0:
                    break
                displacement = struct.unpack_from("<i", data, pos + 1)[0]
                if pos + 5 + displacement == target:
                    print(
                        f"  {label:<4} from RVA {pos:08X} "
                        f"VA {pos + IMAGE_BASE:08X}"
                    )
                pos += 1


if __name__ == "__main__":
    main()
