"""Read-only little-endian displacement finder for the A21 RVA dump."""
from pathlib import Path

blob = Path(r"E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260912-222635-726-46724\DNF-memory-rva.bin").read_bytes()
for disp in (0x43C, 0x46C, 0x5E4, 0x634, 0x638, 0x63C, 0x640, 0x644):
    needle = disp.to_bytes(4, "little")
    positions = []
    at = 0
    while (at := blob.find(needle, at)) >= 0:
        if at < 0x3000000:
            positions.append(at)
        at += 1
    print(f"{disp:03X}: " + " ".join(f"{p:08X}" for p in positions[:250]))
