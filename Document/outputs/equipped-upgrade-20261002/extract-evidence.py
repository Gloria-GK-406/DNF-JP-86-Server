"""Bounded ClientStatic evidence for A21 CMD 0050 target-container coordinates."""
import hashlib
import struct
import sys
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

BASE = 0x400000
SHA256 = "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"
REGIONS = [
    ("Request target lookup and list selection", 0x016FD060, 0x016FD150),
    ("Request writes method then target list as separate U8", 0x016FD5E9, 0x016FD632),
    ("Alternate request writes list 38", 0x016FD947, 0x016FD99E),
    ("WriteU8 primitive", 0x0274D2E0, 0x0274D310),
    ("ACK read order after common success envelope", 0x0113DA15, 0x0113DAC0),
    ("ACK resolves target by list and slot", 0x0113DDEF, 0x0113DE22),
    ("Target resolver code, ending before jump-table data", 0x016F0000, 0x016F0059),
]

data = Path(sys.argv[1]).read_bytes()
assert hashlib.sha256(data).hexdigest() == SHA256, "Unexpected A21 dump"
decoder = Cs(CS_ARCH_X86, CS_MODE_32)
lines = ["ClientStatic only; bounded ranges, not a runtime capture.",
         "Image base 00400000; dump SHA256 " + SHA256]
for label, start, end in REGIONS:
    lines.extend(["", label])
    for ins in decoder.disasm(data[start - BASE:end - BASE], start):
        lines.append(f"VA {ins.address:08X} RVA {ins.address-BASE:08X} "
                     f"{ins.bytes.hex(' '):<24} {ins.mnemonic:<7} {ins.op_str}".rstrip())
lines.append("\nResolver jump table (data, not instructions):")
for list_type in [0, 3, 38]:
    branch = data[0x016F006C - BASE + list_type]
    target = struct.unpack_from("<I", data, 0x016F005C - BASE + branch * 4)[0]
    lines.append(f"list={list_type}, branch={branch}, target VA={target:08X}")
Path(__file__).with_name("a21-target-container.asm.txt").write_bytes(
    ("\n".join(lines) + "\n").encode("utf-8"))
