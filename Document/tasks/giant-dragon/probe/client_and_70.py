"""Read-only bounded evidence extraction; no whole-image disassembly retention.

Budget: mapped inputs, one 512-byte disassembly window; abort at 512 MiB RSS.
String/xref hits are leads, not proof of runtime reachability or a call path.
"""
import hashlib
import json
import mmap
from pathlib import Path
import re
import struct
import sys
import ctypes
import capstone
from elftools.elf.elffile import ELFFile

out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
client = Path(r'E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260922-194214-883-23144\DNF-memory-rva.bin')
native = Path(r'E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r')
identities = {}
def memory_guard():
    if sys.platform != 'win32':
        return
    class Counters(ctypes.Structure):
        _fields_ = [('cb', ctypes.c_ulong), ('faults', ctypes.c_ulong)] + [(name, ctypes.c_size_t) for name in
            ['peak_working', 'working', 'peak_paged', 'paged', 'peak_nonpaged', 'nonpaged', 'pagefile', 'peak_pagefile']]
    counters = Counters()
    counters.cb = ctypes.sizeof(counters)
    kernel = ctypes.windll.kernel32
    kernel.GetCurrentProcess.restype = ctypes.c_void_p
    api = ctypes.windll.psapi.GetProcessMemoryInfo
    api.argtypes = [ctypes.c_void_p, ctypes.POINTER(Counters), ctypes.c_ulong]
    if not api(kernel.GetCurrentProcess(), ctypes.byref(counters), counters.cb):
        raise OSError('Cannot verify disassembly memory budget')
    if counters.working > 512 * 1024 * 1024:
        raise MemoryError('512 MiB working-set abort threshold exceeded')

for label, path in [('client_mapped_image', client), ('server70', native), ('content', Path(r'E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf'))]:
    memory_guard()
    with path.open('rb') as f:
        identities[label] = dict(path=str(path), sha256=hashlib.file_digest(f, 'sha256').hexdigest())
(out / 'inputs.json').write_text(json.dumps(identities, indent=2), encoding='utf8')
with native.open('rb') as f:
    elf = ELFFile(f)
    rows = [f'{s.entry.st_value:08x} {s.name}' for s in elf.get_section_by_name('.symtab').iter_symbols()
            if re.search(r'nateram|kurun|dragon.*dungeon|use.?fatigue.?only', s.name, re.I)]
    (out / '70-dragon-symbols.txt').write_text('\n'.join(rows) or 'No matching symbols in this 70 binary. This does not prove an A21 feature absent.\n', encoding='utf8')
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
with client.open('rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as data, (out / 'a21-string-xrefs.asm.txt').open('w', encoding='utf8') as output:
    for term in ['[use fatigue only start dungeon]', '[nateram time attack info]', '[GET NATERAM CURRENT TIME]', 'Nateram', 'NATERAM', 'nateram', 'kor_shonan_holy', 'chn_shonan_holy']:
        memory_guard()
        for encoding in ['ascii', 'utf-16le']:
            needle = term.encode(encoding)
            start = 0
            for _ in range(20):
                hit = data.find(needle, start)
                if hit < 0: break
                start = hit + len(needle)
                output.write(f'\nSTRING {term} ({encoding}) RVA {hit:08X}\n')
                pointer = struct.pack('<I', hit + 0x400000)
                pos = 0
                for _ in range(20):
                    ref = data.find(pointer, pos, min(len(data), 0x2d00000))
                    if ref < 0: break
                    pos = ref + 4
                    output.write(f'XREF byte position RVA {ref:08X}; alignment not assumed\n')
                    for ins in decoder.disasm(data[max(0, ref-24):ref+100], 0x400000+max(0,ref-24)):
                        output.write(f'{ins.address-0x400000:08X} {ins.mnemonic} {ins.op_str}\n')
    for start, length in [(0xD3F320, 768), (0xB7A2F0, 128), (0x6D3EA0, 896), (0x6D4B90, 320), (0xD57B30, 272), (0x133B8A7, 80), (0x2276514, 112)]:
        memory_guard()
        output.write(f'\nBOUNDED code site RVA {start:08X}, {length} bytes\n')
        for ins in decoder.disasm(data[start:start+length], start+0x400000):
            output.write(f'{ins.address-0x400000:08X} {ins.mnemonic} {ins.op_str}\n')
