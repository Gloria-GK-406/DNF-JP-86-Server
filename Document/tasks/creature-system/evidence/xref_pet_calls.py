"""Read-only xrefs of direct x86 CALL rel32 in an A21 RVA memory image."""

import argparse
import struct
from pathlib import Path

BASE = 0x400000

parser = argparse.ArgumentParser()
parser.add_argument("image", type=Path)
parser.add_argument("targets", nargs="+", type=lambda value: int(value, 0))
args = parser.parse_args()
data = args.image.read_bytes()
targets = set(args.targets)
offset = 0
while (offset := data.find(b"\xE8", offset)) >= 0:
    if offset + 5 > len(data):
        break
    target = BASE + offset + 5 + struct.unpack_from("<i", data, offset + 1)[0]
    if target in targets:
        print(f"CALL VA {BASE + offset:08X} -> {target:08X}")
    offset += 1
