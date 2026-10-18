"""从给定函数沿直接调用 BFS，列出可达的 MakeHeader 构包点及其 type（只读，静态下界）。

用法：python -I probe_reach.py <depth> RVA[:title] ...  输出追加到 reach-housing.txt
间接调用（虚表/函数指针）不跟随，所以结果只是“直接可达”的下界。
"""
from __future__ import annotations
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L
from capstone.x86 import X86_OP_IMM

MAKEHEADER = 0x0234D1E0
STOP = set(L.PRIMS)

def calls_of(entry):
    fn = L.function(entry, limit=4000)
    out = []
    for n, i in enumerate(fn):
        if i.mnemonic == "call" and i.operands and i.operands[0].type == X86_OP_IMM:
            t = (i.operands[0].imm & 0xFFFFFFFF) - L.BASE
            if t == MAKEHEADER:
                typ = None
                for p in reversed(fn[max(0, n - 6):n]):
                    if p.mnemonic == "push" and p.operands[0].type == X86_OP_IMM and (p.operands[0].imm & 0xFFFF) and 0 < p.operands[0].imm < 0x500:
                        typ = p.operands[0].imm; break
                out.append(("MH", i.address - L.BASE, typ))
            elif t not in STOP and L.TEXT_START <= t < L.TEXT_END:
                out.append(("CALL", i.address - L.BASE, t))
    return out

def main() -> None:
    depth = int(sys.argv[1])
    lines = []
    for arg in sys.argv[2:]:
        rva_s, _, title = arg.partition(":")
        root = int(rva_s, 16)
        seen = {root: 0}; frontier = [(root, [root])]; hits = []
        while frontier:
            fn, path = frontier.pop(0)
            if len(path) > depth + 1:
                continue
            for kind, site, x in calls_of(fn):
                if kind == "MH":
                    hits.append((x, site, path))
                elif x not in seen and len(seen) < 3000:
                    seen[x] = len(path); frontier.append((x, path + [x]))
        lines.append(f"===== reach from {title} RVA {root:08X} depth {depth}; functions visited {len(seen)} =====")
        for typ, site, path in sorted(hits, key=lambda h: (h[0] or 0, h[1])):
            lines.append(f"type {('0x%04X' % typ) if typ is not None else '?':>6} site {site:08X} path " + " > ".join(f"{p:08X}" for p in path))
        lines.append("")
    out = Path(__file__).with_name("reach-housing.txt")
    prev = out.read_text(encoding="utf-8") if out.exists() else "\n".join(L.header_lines()) + "\n"
    out.write_text(prev + "\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))

if __name__ == "__main__":
    main()
