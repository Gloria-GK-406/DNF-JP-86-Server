from __future__ import annotations

import hashlib
import re
import struct
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

DUMP = Path(r"E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin")
IMAGE_BASE = 0x00400000
OUT = Path(__file__).with_name("raw-disassembly.txt")

# Repository addresses are RVAs. Sizes deliberately include surrounding control flow;
# conclusions in the report cite tighter ranges from this excerpt.
RANGES = {
    "C2S 03F0 builder function A": (0x0088AA80, 0x3C0),
    "C2S 03F0 builder function B": (0x0088AF10, 0x120),
    "C2S 03F0 builder function C": (0x0088ECF0, 0x220),
    "C2S 03F1 builder cluster A": (0x00889C50, 0x900),
    "C2S 03F7 builder": (0x008955D0, 0x180),
    "CMD 03F0 response handler": (0x00894CD0, 0x500),
    "CMD 03F1 response handler": (0x00891FD0, 0x500),
    "CMD 03F7 response handler": (0x008924B0, 0x500),
    "NOTI 03CD WEDDING_REQUEST": (0x00891140, 0x800),
    "NOTI 03CE WEDDING_RESPONSE": (0x00891960, 0x700),
    "NOTI 03CF WEDDING_AVATAR": (0x00898220, 0x380),
    "NOTI 03D0 WEDDING_INFO": (0x008959B0, 0x900),
    "NOTI 03D4 WEDDING_CEREMONY": (0x00895B10, 0x1700),
    "NOTI 03D7 WEDDING_CEREMONY_BEGIN": (0x00897B00, 0x750),
    "NOTI 03D8 COUPLE_ROOM": (0x008820F0, 0x550),
    "COUPLE_ROOM repeated-entry consumer": (0x00881510, 0x2E0),
    "COUPLE_ROOM room-state calculator": (0x00881DF0, 0x300),
    "stream helper near wedding readers": (0x023BAE40, 0x180),
}


def c_string(data: bytes, va: int) -> str | None:
    rva = va - IMAGE_BASE
    if not (0 <= rva < len(data)):
        return None
    raw = data[rva : rva + 160]
    end = raw.find(b"\0")
    if 4 <= end <= 120:
        chunk = raw[:end]
        if all(0x20 <= b < 0x7F or b in (9, 10, 13) for b in chunk):
            return chunk.decode("ascii", "replace")
    # Also report plausible UTF-16LE strings.
    chars = []
    for i in range(0, min(len(raw) - 1, 120), 2):
        lo, hi = raw[i], raw[i + 1]
        if lo == 0 and hi == 0:
            break
        if hi != 0 or lo < 0x20 or lo >= 0x7F:
            chars = []
            break
        chars.append(chr(lo))
    if len(chars) >= 4:
        return "".join(chars)
    return None


def annotations(data: bytes, op_str: str) -> str:
    notes = []
    for token in re.findall(r"0x[0-9a-fA-F]+", op_str):
        value = int(token, 16)
        s = c_string(data, value)
        if s:
            notes.append(f"str@{value:08X}={s!r}")
    return " ; " + ", ".join(notes) if notes else ""


def main() -> None:
    data = DUMP.read_bytes()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    rows = [
        f"dump={DUMP}",
        f"dump_sha256={hashlib.sha256(data).hexdigest().upper()}",
        f"image_base={IMAGE_BASE:08X}",
        "addresses: RVA then VA; instruction operands retain native VA",
        "",
    ]
    for name, (rva, size) in RANGES.items():
        rows.append(f"===== {name} RVA {rva:08X} VA {rva + IMAGE_BASE:08X} size {size:X} =====")
        for ins in md.disasm(data[rva : rva + size], rva + IMAGE_BASE):
            irva = ins.address - IMAGE_BASE
            raw = ins.bytes.hex(" ").upper()
            rows.append(
                f"RVA {irva:08X} VA {ins.address:08X}  {raw:<24} {ins.mnemonic:<8} {ins.op_str}"
                + annotations(data, ins.op_str)
            )
        rows.append("")
    OUT.write_text("\n".join(rows), encoding="utf-8", newline="\n")
    print(OUT)


if __name__ == "__main__":
    main()
