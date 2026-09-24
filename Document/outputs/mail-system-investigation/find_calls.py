#!/usr/bin/env python3
"""Find direct E8 references to selected image VAs in the A21 RVA-indexed dump."""
import argparse
from pathlib import Path
import struct

BASE = 0x400000
p = argparse.ArgumentParser()
p.add_argument('dump', type=Path)
p.add_argument('targets', nargs='+')
a = p.parse_args()
b = a.dump.read_bytes()
targets = {int(s, 0) for s in a.targets}
for i in range(0, min(len(b) - 5, 0x3600000)):
    if b[i] != 0xE8:
        continue
    target = BASE + i + 5 + struct.unpack_from('<i', b, i + 1)[0]
    if target in targets:
        print(f'VA {BASE+i:08X} -> {target:08X}')
