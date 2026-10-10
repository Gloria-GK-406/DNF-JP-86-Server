"""Read-only, reproducible A21 evidence; addresses printed as RVA."""
import hashlib
import sys
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

BASE = 0x400000
dump = Path(sys.argv[1])
data = dump.read_bytes()
print(f"dump={dump}\nsha256={hashlib.sha256(data).hexdigest()}")
decoder = Cs(CS_ARCH_X86, CS_MODE_32)
for title, start, end in [
    ("named predicate", 0x2B91EF0, 0x2B91F70),
    ("named virtual wrapper", 0x18C91A0, 0x18C91C0),
    ("monster code getter", 0x18A7F90, 0x18A7FA0),
    ("death sequence argument", 0x1155540, 0x1155560),
    ("death row sequence", 0x1946D30, 0x1946D48),
    ("experience dictionary", 0x1946A40, 0x1946AD0),
    ("panel row key", 0x25E92DF, 0x25E92F0),
    ("panel key lookup", 0x25E9595, 0x25E95B5),
]:
    print(f"\n{title}")
    for ins in decoder.disasm(data[start-BASE:end-BASE], start):
        print(f"RVA {ins.address-BASE:08X} {ins.bytes.hex(' '):<30} {ins.mnemonic} {ins.op_str}")
