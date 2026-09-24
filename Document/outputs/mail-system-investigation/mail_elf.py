"""Read-only exploration of the bundled level-70 df_game_r ELF."""
import bisect
import re
import sys

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from elftools.elf.elffile import ELFFile

PATH = r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r"
with open(PATH, "rb") as stream:
    elf = ELFFile(stream)
    sec = elf.get_section_by_name(".text")
    code, base = sec.data(), sec["sh_addr"]
    syms = sorted((s["st_value"], s["st_size"], s.name)
                  for s in elf.get_section_by_name(".symtab").iter_symbols()
                  if s["st_info"]["type"] == "STT_FUNC" and s["st_size"] and s["st_value"])
    starts = [s[0] for s in syms]
    md = Cs(CS_ARCH_X86, CS_MODE_32)

    def owner(addr):
        i = bisect.bisect_right(starts, addr) - 1
        return syms[i] if i >= 0 and addr < syms[i][0] + syms[i][1] else None

    def instructions(symbol):
        a, z, _ = symbol
        return md.disasm(code[a-base:a-base+z], a)

    mode, *args = sys.argv[1:]
    if mode == "sym":
        pattern = re.compile(args[0], re.I)
        for a, z, n in syms:
            if pattern.search(n):
                print(f"{a:08x} {z:5} {n}")
    elif mode == "asm":
        for arg in args:
            addr = int(arg, 0)
            s = owner(addr)
            if not s:
                continue
            print(f"\n### {s[0]:08x} {s[2]}")
            for i in instructions(s):
                target = owner(int(i.op_str, 16)) if i.mnemonic == "call" and i.op_str.startswith("0x") else None
                print(f"{i.address:08x} {i.mnemonic:7} {i.op_str:44} {target[2] if target else ''}")
    elif mode == "calls":
        for arg in args:
            s = owner(int(arg, 0))
            if not s:
                continue
            print(f"\n### {s[0]:08x} {s[2]}")
            for i in instructions(s):
                if i.mnemonic != "call":
                    continue
                target = owner(int(i.op_str, 16)) if i.op_str.startswith("0x") else None
                print(f"{i.address:08x} -> {i.op_str:12} {target[2] if target else ''}")
    elif mode == "refs":
        targets = {int(x, 0) for x in args}
        for s in syms:
            a, z, n = s
            if not (base <= a and a + z <= base + len(code)):
                continue
            for i in instructions(s):
                if i.mnemonic == "call" and i.op_str.startswith("0x") and int(i.op_str, 16) in targets:
                    print(f"{i.address:08x} {n} -> {i.op_str}")
    elif mode == "str":
        for arg in args:
            addr = int(arg, 0)
            for section in elf.iter_sections():
                lo = section["sh_addr"]
                if lo <= addr < lo + section["sh_size"]:
                    data = section.data()[addr-lo:]
                    print(f"{addr:08x} {data.split(bytes([0]), 1)[0].decode(errors='replace')}")
                    break
