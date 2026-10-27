"""Read bounded VA ranges from the recorded A21 dump; never retain instruction objects."""
import mmap
from pathlib import Path
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

DUMP = Path(r'E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin')
md = Cs(CS_ARCH_X86, CS_MODE_32)
with DUMP.open('rb') as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
    for request in sys.argv[1:]:
        start, length = (int(x, 16) for x in request.split(':'))
        if not 0 < length <= 0x10000: raise ValueError('Maximum window is 64 KiB')
        print(f'\nVA {start:08X}, length {length:X}')
        for ins in md.disasm(data[start - 0x400000:start - 0x400000 + length], start):
            print(f'{ins.address:08X} {ins.mnemonic:8} {ins.op_str}')
