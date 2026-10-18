"""找出与婚房 town 101(0x65) 相关的代码：调用 town-id getter 02019650 后比较 0x65 的函数，
以及其所在函数内（或直接被调用的函数内）的 MakeHeader 构包 type（只读）。"""
from __future__ import annotations
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L
from capstone.x86 import X86_OP_IMM

TOWN_GETTER = 0x02019650
MAKEHEADER = 0x0234D1E0

def main() -> None:
    out = L.header_lines()
    sites = L.callers(TOWN_GETTER)
    fns = {}
    for s in sites:
        ws = L.window(s, s + 16)
        if len(ws) >= 2 and ws[1].mnemonic == "cmp" and ws[1].operands[1].type == X86_OP_IMM and ws[1].operands[1].imm == 0x65:
            fns.setdefault(L.find_entry(s), []).append(s)
    out.append(f"town getter RVA {TOWN_GETTER:08X} callers={len(sites)}; followed by cmp eax,0x65 in {sum(map(len, fns.values()))} sites / {len(fns)} functions")
    for e, ss in sorted(fns.items()):
        types = []
        if e > 0:
            fn = L.function(e, limit=3000)
            for n, i in enumerate(fn):
                if i.mnemonic == "call" and i.operands[0].type == X86_OP_IMM and (i.operands[0].imm & 0xFFFFFFFF) - L.BASE == MAKEHEADER:
                    for p in reversed(fn[max(0, n - 6):n]):
                        if p.mnemonic == "push" and p.operands[0].type == X86_OP_IMM and 0 < p.operands[0].imm < 0x500:
                            types.append(f"0x{p.operands[0].imm:04X}"); break
        out.append(f"fn {e:08X} sites {','.join(f'{s:08X}' for s in ss)} callers {','.join(f'{c:08X}' for c in L.callers(e)[:5]) if e > 0 else '-'} makeheader {types}")
    Path(__file__).with_name("town101-xrefs.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    print("\n".join(out[6:]))

if __name__ == "__main__":
    main()
