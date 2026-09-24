#!/usr/bin/env python3
"""Read-only disassembly of chosen A21 memory-image VA ranges."""

import argparse
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

BASE = 0x400000


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("dump", type=Path)
    parser.add_argument("ranges", nargs="+", help="VA_START:VA_END")
    args = parser.parse_args()
    data = args.dump.read_bytes()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    for item in args.ranges:
        start, end = (int(part, 0) for part in item.split(":"))
        assert BASE <= start < end <= BASE + len(data)
        print(f"\n### VA {start:08X}..{end:08X}")
        for ins in md.disasm(data[start-BASE:end-BASE], start):
            print(f"{ins.address:08X} {ins.mnemonic:<8} {ins.op_str}")


if __name__ == "__main__":
    main()
