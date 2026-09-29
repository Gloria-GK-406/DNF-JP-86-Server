"""Find ELF code references to a struct offset, with containing symbols."""
import argparse
from bisect import bisect_right
from pathlib import Path
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_MEM

ap = argparse.ArgumentParser()
ap.add_argument("offset", type=lambda x: int(x, 16))
args = ap.parse_args()
path = Path(r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r")
with path.open("rb") as stream:
    elf = ELFFile(stream)
    syms = elf.get_section_by_name(".symtab")
    funcs = sorted((s["st_value"], s["st_value"] + s["st_size"], s.name)
                   for s in syms.iter_symbols() if s["st_info"]["type"] == "STT_FUNC" and s["st_size"])
    starts = [a for a, _, _ in funcs]
    section = elf.get_section_by_name(".text")
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    for ins in md.disasm(section.data(), section["sh_addr"]):
        if any(op.type == X86_OP_MEM and op.mem.disp == args.offset for op in ins.operands):
            i = bisect_right(starts, ins.address) - 1
            caller = funcs[i][2] if i >= 0 and ins.address < funcs[i][1] else "<unknown>"
            print(f"{ins.address:08x}: {ins.mnemonic} {ins.op_str} ; {caller}")
