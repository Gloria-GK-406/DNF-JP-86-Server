"""Bounded ELF symbol / string scan of df_game_r binaries for housing/couple/tree/capsule/ring features.

Usage: python -I scan_df_game_r.py <df_game_r> [<df_game_r> ...]
Prints sha256, matching function/object symbols (mangled), and matching printable strings (ASCII >= 6).
"""
import hashlib
import re
import sys

from elftools.elf.elffile import ELFFile
from elftools.elf.sections import SymbolTableSection

SYM_RE = re.compile(r"(?i)housing|couple|wedding|marri|spouse|lover|lovepoint|love_point|decoration|deco_?inven|"
                    r"tree|plant|water|harvest|capsule|growth|furniture|upgrade_?ring|weddingring|ring_up")
STR_RE = re.compile(rb"(?i)housing|couple|wedding|marri|spouse|love.?point|decoration|tree_|_tree|water|harvest|"
                    rb"capsule|furniture|upgrade.?ring")
NOISE = re.compile(r"(?i)rb_tree|_Rb_tree|tree_node|btree|tree_iterator|RbTree|Rb_|_M_|subtree|watermark")

for path in sys.argv[1:]:
    data = open(path, "rb").read()
    print("=" * 100)
    print("file", path)
    print("sha256", hashlib.sha256(data).hexdigest().upper(), "size", len(data))
    with open(path, "rb") as fh:
        elf = ELFFile(fh)
        seen = set()
        hits = []
        total = 0
        for sec in elf.iter_sections():
            if not isinstance(sec, SymbolTableSection):
                continue
            for sym in sec.iter_symbols():
                total += 1
                name = sym.name
                if not name or name in seen:
                    continue
                if SYM_RE.search(name) and not NOISE.search(name):
                    seen.add(name)
                    hits.append((sym["st_value"], sym["st_info"]["type"], sym["st_size"], name, sec.name))
        print("symbol entries", total, "matching unique names", len(hits))
        for v, t, s, n, sn in sorted(hits, key=lambda x: x[3]):
            print(f"SYM {v:08x} {t:10s} {s:6d} {sn:8s} {n}")
    strs = set()
    for m in re.finditer(rb"[\x20-\x7e]{6,}", data):
        s = m.group(0)
        if STR_RE.search(s):
            strs.add((m.start(), s.decode("ascii")))
    print("matching strings", len(strs))
    for off, s in sorted(strs):
        print(f"STR {off:08x} {s}")
