#!/usr/bin/env python3
"""Small, read-only A21 dump disassembler for GH #101 evidence.

The memory dump is indexed by RVA. Printed addresses include both RVA and the
preferred image VA (0x00400000 + RVA) so they can be matched against the
repository's generated protocol pages and the client source/literal censuses.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs


IMAGE_BASE = 0x00400000


def parse_int(value: str) -> int:
    return int(value, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("dump", type=Path)
    parser.add_argument("start", type=parse_int, help="start RVA")
    parser.add_argument("end", type=parse_int, help="exclusive end RVA")
    args = parser.parse_args()

    data = args.dump.read_bytes()
    if not 0 <= args.start < args.end <= len(data):
        raise SystemExit("range is outside dump")

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    blob = data[args.start : args.end]
    for insn in md.disasm(blob, args.start):
        raw = " ".join(f"{b:02X}" for b in insn.bytes)
        print(
            f"RVA {insn.address:08X} VA {insn.address + IMAGE_BASE:08X}  "
            f"{raw:<26} {insn.mnemonic:<8} {insn.op_str}"
        )


if __name__ == "__main__":
    main()
