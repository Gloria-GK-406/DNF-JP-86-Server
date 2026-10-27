"""Bounded mapped-image disassembly and direct-call inventory (RVA arguments)."""
import argparse
import hashlib
import mmap
import struct
from pathlib import Path
import capstone

parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=['window', 'calls', 'pointer'])
parser.add_argument('addresses', nargs='+', type=lambda x: int(x, 16))
parser.add_argument('--length', type=lambda x: int(x, 16), default=0x400)
parser.add_argument('--scan-start', type=lambda x: int(x, 16), default=0x1000)
parser.add_argument('--scan-end', type=lambda x: int(x, 16), default=0x2C29000)
args = parser.parse_args()
if not 0 < args.length <= 0x4000:
    parser.error('window must be at most 16 KiB')
path = Path(r'E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260922-194214-883-23144\DNF-memory-rva.bin')
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
with path.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as data:
    expected = 'aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d'
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError('Mapped client identity changed; re-establish address evidence first')
    print(f'Client mapped SHA-256 {expected}; base VA 00400000')
    for target in args.addresses:
        print(f'\nTARGET RVA {target:08X}')
        if args.mode == 'window':
            for instruction in decoder.disasm(data[target:target + args.length], target + 0x400000):
                if instruction.mnemonic != 'int3':
                    print(f'{instruction.address - 0x400000:08X} {instruction.mnemonic} {instruction.op_str}')
        elif args.mode == 'calls':
            # Only candidate E8/E9 instructions in the bounded executable section.
            for opcode in (b'\xe8', b'\xe9'):
                start = args.scan_start
                while (start := data.find(opcode, start, args.scan_end)) >= 0:
                    displacement = struct.unpack_from('<i', data, start + 1)[0]
                    if start + 5 + displacement == target:
                        mnemonic = 'call' if opcode[0] == 0xE8 else 'jmp'
                        print(f'{start:08X} {mnemonic}')
                    start += 1
        else:
            start = args.scan_start
            needle = struct.pack('<I', target + 0x400000)
            while (start := data.find(needle, start, min(len(data), args.scan_end))) >= 0:
                print(f'{start:08X} pointer candidate')
                start += 4
