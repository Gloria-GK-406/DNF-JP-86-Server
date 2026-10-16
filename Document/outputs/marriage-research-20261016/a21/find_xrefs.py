from __future__ import annotations

import hashlib
import struct
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32


DUMP = Path(r"E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin")
IMAGE_BASE = 0x00400000
OUT = Path(__file__).with_name("direct-xrefs.txt")

TARGETS = {
    "03F0 builder A": 0x0088AA80,
    "03F0 builder B": 0x0088AF10,
    "03F0 builder C": 0x0088ECF0,
    "03F1 UI dispatcher": 0x0088A200,
    "03F7 builder": 0x008955D0,
}


def main() -> None:
    data = DUMP.read_bytes()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    rows = [
        f"dump={DUMP}",
        f"dump_sha256={hashlib.sha256(data).hexdigest().upper()}",
        f"image_base={IMAGE_BASE:08X}",
        "Only direct near CALL (E8 rel32) references are listed; indirect calls are outside this scan.",
        "",
    ]
    for name, target_rva in TARGETS.items():
        hits: list[int] = []
        target_va = IMAGE_BASE + target_rva
        for i in range(len(data) - 5):
            if data[i] != 0xE8:
                continue
            rel = struct.unpack_from("<i", data, i + 1)[0]
            if IMAGE_BASE + i + 5 + rel == target_va:
                hits.append(i)
        rows.append(f"===== {name} target RVA {target_rva:08X} VA {target_va:08X}; direct_calls={len(hits)} =====")
        for hit in hits:
            start = max(0, hit - 0x50)
            end = min(len(data), hit + 0x90)
            rows.append(f"--- callsite RVA {hit:08X} VA {IMAGE_BASE + hit:08X} ---")
            for ins in md.disasm(data[start:end], IMAGE_BASE + start):
                irva = ins.address - IMAGE_BASE
                marker = "  <CALL>" if irva == hit else ""
                rows.append(f"RVA {irva:08X} VA {ins.address:08X}  {ins.mnemonic:<8} {ins.op_str}{marker}")
        rows.append("")
    OUT.write_text("\n".join(rows), encoding="utf-8", newline="\n")
    print(OUT)


if __name__ == "__main__":
    main()
