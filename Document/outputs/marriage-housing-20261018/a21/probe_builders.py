"""枚举婚房/树/胶囊/婚姻相关 C2S type 的全部 MakeHeader 构包点并输出所在函数（只读）。"""
from __future__ import annotations
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L

TYPES = list(range(0x03F2, 0x0407))
MAKEHEADER = 0x0234D1E0

def main() -> None:
    sites = L.callers(MAKEHEADER)
    found: dict[int, list[int]] = {}
    for s in sites:
        # 向前 0x30 字节内对齐前向反汇编，找最后一个 push imm 落入 TYPES
        for back in range(0x30, 0, -1):
            ins_list = L.window(s - back, s + 5)
            if not ins_list or ins_list[-1].address - L.BASE != s:
                continue
            t = None
            for i in ins_list:
                if i.mnemonic == "push" and i.operands and i.operands[0].type == 2:
                    v = i.operands[0].imm & 0xFFFFFFFF
                    if v in TYPES:
                        t = v
            if t is not None:
                found.setdefault(t, []).append(s)
            break
    out = L.header_lines()
    out.append(f"MakeHeader RVA {MAKEHEADER:08X} direct callers={len(sites)}")
    for t in sorted(found):
        out.append(f"type 0x{t:04X}: sites " + ", ".join(f"{s:08X}" for s in found[t]))
    out.append("")
    done = set()
    for t in sorted(found):
        for s in found[t]:
            e = L.find_entry(s)
            if e in done:
                out.append(f"(type 0x{t:04X} site {s:08X} in already dumped function {e:08X})")
                continue
            done.add(e)
            out += L.dump_function(e, f"C2S 0x{t:04X} builder (site RVA {s:08X})")
            cs = L.callers(e)
            out.append("direct callers of entry: " + (", ".join(f"{c:08X}(fn {L.find_entry(c):08X})" for c in cs) or "none (vtable/indirect?)"))
            out.append("imm refs of entry VA: " + ", ".join(f"{r:08X}" for r in L.imm_refs(e + L.BASE, 0x1000, len(L.image()))[:20]))
            out.append("")
    Path(__file__).with_name("builders-disassembly.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    print("ok", {hex(k): len(v) for k, v in found.items()})

if __name__ == "__main__":
    main()
