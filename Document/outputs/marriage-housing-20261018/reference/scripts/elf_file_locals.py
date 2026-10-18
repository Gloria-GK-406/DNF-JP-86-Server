"""List symtab entries that follow selected STT_FILE markers (local symbols of that translation unit),
and global symbols whose names match a regex. Usage: python -P elf_file_locals.py <elf> <file-regex> <name-regex>"""
import re, sys
from elftools.elf.elffile import ELFFile
from elftools.elf.sections import SymbolTableSection

path, file_re, name_re = sys.argv[1], re.compile(sys.argv[2]), re.compile(sys.argv[3])
with open(path, "rb") as fh:
    elf = ELFFile(fh)
    symtab = elf.get_section_by_name(".symtab")
    current = None
    for sym in symtab.iter_symbols():
        t = sym["st_info"]["type"]
        if t == "STT_FILE":
            current = sym.name if file_re.search(sym.name) else None
            if current:
                print(f"FILE {sym.name}")
            continue
        if current and sym["st_info"]["bind"] == "STB_LOCAL" and sym.name:
            print(f"  LOCAL {sym['st_value']:08x} {t:10s} {sym['st_size']:6d} {sym.name}")
    seen = set()
    print("GLOBAL name matches")
    for sym in symtab.iter_symbols():
        if sym["st_info"]["bind"] != "STB_LOCAL" and sym.name and name_re.search(sym.name) and sym.name not in seen:
            seen.add(sym.name)
            print(f"  G {sym['st_value']:08x} {sym['st_info']['type']:10s} {sym['st_size']:6d} {sym.name}")
