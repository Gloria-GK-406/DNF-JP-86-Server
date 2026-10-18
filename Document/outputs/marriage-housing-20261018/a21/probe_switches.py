"""解析 handler 中的 switch 跳表：case 值 -> 目标 -> 目标块内首个 dstr ID / 调用（只读）。

用法：python -I probe_switches.py <out> RVA[:title] ...
case 偏置从跳表前 8 条指令内的 `lea r,[r+K]` / `add r,K` / `sub r,K` / `dec r` 推回。
dstr ID 判据：push imm 落在 0x4000..0x20000 并紧随 call 024C14C0（取本地化字符串）。
"""
from __future__ import annotations
import struct, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG

GETSTR = 0x024C14C0

def block_summary(rva: int, n: int = 14) -> str:
    out = []
    ins = L.window(rva, rva + 0x60)[:n]
    last_imm = None
    for i in ins:
        if i.mnemonic == "push" and i.operands and i.operands[0].type == X86_OP_IMM:
            last_imm = i.operands[0].imm & 0xFFFFFFFF
        if i.mnemonic in ("call", "jmp") and i.operands and i.operands[0].type == X86_OP_IMM:
            t = (i.operands[0].imm & 0xFFFFFFFF) - L.BASE
            if t == GETSTR and last_imm is not None:
                out.append(f"dstr 0x{last_imm:05X}")
            elif i.mnemonic == "jmp" and last_imm is not None and 0x4000 <= last_imm < 0x20000:
                out.append(f"push 0x{last_imm:05X} -> jmp {t:08X} (likely dstr)")
            else:
                out.append(f"{i.mnemonic} {L.PRIMS.get(t, f'{t:08X}')}")
            if i.mnemonic == "jmp" or len(out) >= 4:
                break
        if i.mnemonic in ("ret",):
            out.append("ret"); break
    return "; ".join(out)

def main() -> None:
    name = sys.argv[1]
    lines = L.header_lines()
    data = L.image()
    for arg in sys.argv[2:]:
        rva_s, _, title = arg.partition(":")
        entry = int(rva_s, 16)
        fn = L.function(entry)
        lines.append(f"===== {title} entry RVA {entry:08X} VA {entry + L.BASE:08X} =====")
        for idx, ins in enumerate(fn):
            if ins.mnemonic != "jmp" or not ins.operands or ins.operands[0].type != X86_OP_MEM:
                continue
            op = ins.operands[0]
            if op.mem.base != 0 or op.mem.scale != 4:
                continue
            tab = (op.mem.disp & 0xFFFFFFFF) - L.BASE
            bias = 0; bytetab = None; limit = None
            reg = op.mem.index
            for prev in reversed(fn[max(0, idx - 8):idx]):
                m, o = prev.mnemonic, prev.operands
                if m == "movzx" and o[0].type == X86_OP_REG and o[0].reg == reg and o[1].type == X86_OP_MEM and o[1].mem.base != 0 and o[1].mem.index == 0:
                    bytetab = (o[1].mem.disp & 0xFFFFFFFF) - L.BASE
                    reg = o[1].mem.base
                elif m == "cmp" and o[0].type == X86_OP_REG and o[0].reg == reg and o[1].type == X86_OP_IMM and limit is None:
                    limit = o[1].imm
                elif m == "lea" and o[0].reg == reg and o[1].type == X86_OP_MEM and o[1].mem.index == 0:
                    bias = o[1].mem.disp; break
                elif m == "add" and o[0].type == X86_OP_REG and o[0].reg == reg and o[1].type == X86_OP_IMM:
                    bias = o[1].imm; break
                elif m == "sub" and o[0].type == X86_OP_REG and o[0].reg == reg and o[1].type == X86_OP_IMM:
                    bias = -o[1].imm; break
                elif m == "dec" and o[0].type == X86_OP_REG and o[0].reg == reg:
                    bias = -1; break
            lines.append(f"-- switch at RVA {ins.address - L.BASE:08X}: table {tab:08X} bytetable {bytetab and f'{bytetab:08X}'} bias {bias:+d} limit {limit}")
            if limit is None:
                continue
            for k in range(limit + 1):
                slot = data[bytetab + k] if bytetab is not None else k
                tgt = struct.unpack_from("<I", data, tab + 4 * slot)[0] - L.BASE
                lines.append(f"   case {k - bias:+d}: -> RVA {tgt:08X}  {block_summary(tgt)}")
        lines.append("")
    Path(__file__).with_name(name).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("ok")

if __name__ == "__main__":
    main()
