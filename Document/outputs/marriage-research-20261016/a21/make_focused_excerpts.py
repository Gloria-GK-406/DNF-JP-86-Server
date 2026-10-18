from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "raw-disassembly.txt"
OUTPUT = ROOT / "focused-excerpts.txt"

# These ranges are deliberately narrow. raw-disassembly.txt remains the exhaustive
# evidence; this file keeps the reads/writes used by a21.md easy to review.
RANGES = [
    ("03F0 request action=1 build", 0x0088ADB2, 0x0088ADF4),
    ("03F0 request actions=5/6/7 build", 0x0088AF4E, 0x0088B000),
    ("03F0 request action=2 build", 0x0088EE58, 0x0088EEB8),
    ("03F1 request action=1 outcome=0", 0x00889D90, 0x00889DDA),
    ("03F1 request action=2 outcome=0", 0x00889DF0, 0x00889E3A),
    ("03F1 request action=7 outcome=0", 0x00889E50, 0x00889E9A),
    ("03F1 UI dispatcher outcomes 1/2", 0x0088A200, 0x0088A410),
    ("03F7 ceremony-enter builder", 0x008956BD, 0x00895708),
    ("CMD 03F0 response fixed 11-byte read", 0x00894CD0, 0x00894D17),
    ("CMD 03F1 response fixed 12-byte read", 0x00891FD0, 0x00892054),
    ("CMD 03F7 response u32 read", 0x008924B0, 0x008924E7),
    ("NOTI 03CD fixed 11-byte read", 0x00891140, 0x008911D0),
    ("NOTI 03CE fixed 12-byte read", 0x00891960, 0x00891A24),
    ("NOTI 03D0 fixed 15-byte read", 0x008959B0, 0x00895A3C),
    ("NOTI 03D4 fixed 79-byte read", 0x00895B3D, 0x00895BE5),
    ("NOTI 03D7 fixed 68-byte read", 0x00897B00, 0x00897BAC),
    ("NOTI 03D8 count, 6-byte loop, 13-byte tail", 0x008820F0, 0x00882231),
    ("03D8 repeated-entry consumer u16 narrowing", 0x00881510, 0x008815A7),
]

ADDRESS = re.compile(r"^RVA ([0-9A-F]{8}) ")
lines = SOURCE.read_text(encoding="utf-8").splitlines()

header = [
    "A21 marriage focused disassembly excerpts",
    "source=raw-disassembly.txt",
    "dump_sha256=6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507",
    "image_base=00400000",
    "client_exe_sha256=1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD",
    "",
]

out = header
for name, start, end in RANGES:
    out.append(f"===== {name} RVA {start:08X}-{end:08X} =====")
    matched = 0
    for line in lines:
        match = ADDRESS.match(line)
        if match is None:
            continue
        address = int(match.group(1), 16)
        if start <= address <= end:
            out.append(line)
            matched += 1
    if matched == 0:
        raise RuntimeError(f"No instructions matched {name}")
    out.append("")

OUTPUT.write_text("\n".join(out) + "\n", encoding="utf-8")
