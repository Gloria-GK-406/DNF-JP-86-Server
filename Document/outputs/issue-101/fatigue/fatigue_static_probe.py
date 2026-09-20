#!/usr/bin/env python3
"""Small bounded disassembler for GH #101 A21 fatigue evidence.

The input is the existing RVA-indexed memory dump.  Printed addresses are RVA;
absolute operands keep the image-base-adjusted VA used by the client.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs


DEFAULT_DUMP = Path(
    r"E:\Project\DNF\DNFClient-A21-protocol\dumps"
    r"\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin"
)


def parse_int(value: str) -> int:
    return int(value, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("start", type=parse_int)
    parser.add_argument("end", type=parse_int)
    parser.add_argument("--dump", type=Path, default=DEFAULT_DUMP)
    parser.add_argument("--hash", action="store_true")
    args = parser.parse_args()

    blob = args.dump.read_bytes()
    if args.hash:
        print(f"dump_sha256={hashlib.sha256(blob).hexdigest().upper()}")
    print(f"dump={args.dump}")
    print(f"range=RVA 0x{args.start:08X}..0x{args.end:08X}")

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    for insn in md.disasm(blob[args.start : args.end], args.start):
        raw = insn.bytes.hex(" ").upper()
        print(f"{insn.address:08X}  {raw:<32}  {insn.mnemonic:<8} {insn.op_str}")


if __name__ == "__main__":
    main()
