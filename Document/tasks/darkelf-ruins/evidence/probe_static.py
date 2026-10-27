"""Bounded read-only ELF symbols and A21 literal/reference inventory. Addresses are VA."""
from pathlib import Path
import hashlib
import mmap
import re
import struct
from elftools.elf.elffile import ELFFile

OUT = Path(__file__).parent
ELF = Path(r'E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r')
DUMP = Path(r'E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin')
with ELF.open('rb') as stream, (OUT / '70-symbols.txt').open('w', encoding='utf8') as out:
    elf = ELFFile(stream)
    out.write(f'{ELF}\nSHA256 {hashlib.file_digest(stream, "sha256").hexdigest()}\n')
    count = 0
    for symbol in elf.get_section_by_name('.symtab').iter_symbols():
        if re.search(r'dark.?elf|historicsite|temple|license.?dungeon', symbol.name, re.I):
            out.write(f'{symbol["st_value"]:08X} {symbol["st_size"]} {symbol.name}\n')
            count += 1
    out.write(f'matches={count}\n')
with DUMP.open('rb') as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data, (OUT / 'client-literals.txt').open('w', encoding='utf8') as out:
    out.write(f'{DUMP}\nSHA256 {hashlib.file_digest(stream, "sha256").hexdigest()}\n')
    for match in re.finditer(rb'[\x20-\x7e]{5,}', data):
        if not re.search(rb'dark.?elf|historicsite', match[0], re.I):
            continue
        address = match.start() + 0x400000
        out.write(f'\n{address:08X} {match[0].decode()}\n')
        needle = struct.pack('<I', address)
        start = 0
        references = []
        while len(references) < 30:
            start = data.find(needle, start, min(len(data), 0x3000000))
            if start < 0: break
            references.append(f'{start + 0x400000:08X}')
            start += 4
        out.write('refs: ' + ' '.join(references) + '\n')
