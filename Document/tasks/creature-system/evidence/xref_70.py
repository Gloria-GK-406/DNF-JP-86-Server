"""List direct call sites to selected 70 ELF functions (read-only)."""
import argparse
from bisect import bisect_right
from pathlib import Path
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM

ELF = Path(r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r")

ap = argparse.ArgumentParser()
ap.add_argument("targets", nargs="+", type=lambda x: int(x, 16))
args = ap.parse_args()
with ELF.open("rb") as stream:
    elf = ELFFile(stream)
    syms = elf.get_section_by_name(".symtab")
    funcs = sorted((s["st_value"], s["st_value"] + s["st_size"], s.name)
                   for s in syms.iter_symbols() if s["st_info"]["type"] == "STT_FUNC" and s["st_size"])
    starts = [a for a, _, _ in funcs]
    text = elf.get_section_by_name(".text")
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    hits = []
    for ins in md.disasm(text.data(), text["sh_addr"]):
        if ins.mnemonic.startswith("call") and ins.operands and ins.operands[0].type == X86_OP_IMM:
            dest = ins.operands[0].imm & 0xffffffff
            if dest in args.targets:
                i = bisect_right(starts, ins.address) - 1
                caller = funcs[i][2] if i >= 0 and ins.address < funcs[i][1] else "<unknown>"
                hits.append((dest, ins.address, caller))
    for dest, site, caller in sorted(hits):
        print(f"{dest:08x} <- {site:08x} {caller}")
