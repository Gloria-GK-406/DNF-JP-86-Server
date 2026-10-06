from pathlib import Path
import hashlib
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

dump = Path(r"E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin")
data = dump.read_bytes()
decoder = Cs(CS_ARCH_X86, CS_MODE_32)
print("dump:", dump)
print("sha256:", hashlib.sha256(data).hexdigest().upper())
print("image base: 00400000; addresses below are RVA")
ranges = [(0xD2690, 0x200), (0xD2150, 0x60), (0xD24A0, 0x140),
          (0xFCBD0, 0xEB0), (0xFA1B0, 0x1650)]
if len(sys.argv) > 1:
    ranges = [(int(a, 16), int(n, 16)) for a, n in (arg.split(":") for arg in sys.argv[1:])]
for start, size in ranges:
    print(f"\nRVA {start:08X}..{start+size:08X}")
    for ins in decoder.disasm(data[start:start+size], start + 0x400000):
        print(f"{ins.address-0x400000:08X} {ins.bytes.hex(' '):<24} {ins.mnemonic} {ins.op_str}")
