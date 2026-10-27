"""Bounded rel32 xrefs inside A21 .text only. Raw candidates require instruction verification."""
from pathlib import Path
import mmap, struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
p = Path(r'E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin')
targets = {int(x, 16) for x in sys.argv[1:]}
md = Cs(CS_ARCH_X86, CS_MODE_32)
with p.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as b:
    start = 0x1000
    while True:
        pos = b.find(b'\xE8', start, 0x2829000)
        if pos < 0: break
        start = pos + 1
        target = pos + 0x400005 + struct.unpack_from('<i', b, pos + 1)[0]
        if target not in targets: continue
        print(f'\nCALL {pos + 0x400000:08X} -> {target:08X}')
        for ins in md.disasm(b[pos:pos+40], pos+0x400000):
            print(f'{ins.address:08X} {ins.mnemonic:8} {ins.op_str}')
