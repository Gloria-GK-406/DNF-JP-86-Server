"""解析 NPC 菜单 dispatch（RVA 021AB244 起的 switch，menu id 0x3ED..0x41C）的各 case 目标（只读）。
大司祭/婚房相关 NPC 菜单项由 PVF 给出 menu id；这里只给客户端侧 id -> 处理块。"""
from __future__ import annotations
import struct, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L
from capstone.x86 import X86_OP_IMM

TABLE_VA, BIAS, LIMIT = 0x025AC338, 0x3ED, 0x2F

def summarize(rva: int) -> str:
    out = []; last = None
    for i in L.window(rva, rva + 0x80)[:24]:
        if i.mnemonic == "push" and i.operands[0].type == X86_OP_IMM:
            last = i.operands[0].imm & 0xFFFFFFFF
        if i.mnemonic in ("call", "jmp") and i.operands[0].type == X86_OP_IMM:
            t = (i.operands[0].imm & 0xFFFFFFFF) - L.BASE
            name = L.PRIMS.get(t, f"{t:08X}")
            out.append(f"{i.mnemonic} {name}" + (f"(push 0x{last:X})" if last is not None and i.mnemonic == "call" else ""))
            if i.mnemonic == "jmp":
                break
        elif i.mnemonic == "mov" and "0x39adf" in i.op_str:
            out.append(i.op_str)
    return "; ".join(out[:6])

def main() -> None:
    d = L.image()
    lines = L.header_lines() + [f"NPC menu switch at RVA 021AB244: case = edi, table VA {TABLE_VA:08X}, bias 0x{BIAS:X}"]
    for k in range(LIMIT + 1):
        tgt = struct.unpack_from("<I", d, TABLE_VA - L.BASE + 4 * k)[0] - L.BASE
        lines.append(f"menu {BIAS + k:4d} (0x{BIAS + k:03X}) -> RVA {tgt:08X}: {summarize(tgt)}")
    Path(__file__).with_name("npc-menu-dispatch.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[7:]))

if __name__ == "__main__":
    main()
