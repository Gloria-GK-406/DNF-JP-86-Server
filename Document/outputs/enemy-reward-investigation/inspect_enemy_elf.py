"""Read-only, symbol-bound ELF helper for the bundled 70-level df_game_r."""
import bisect
import sys

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from elftools.elf.elffile import ELFFile

ELF = r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r"

with open(ELF, "rb") as stream:
    elf = ELFFile(stream)
    section = elf.get_section_by_name(".text")
    code, base = section.data(), section["sh_addr"]
    syms = sorted((s["st_value"], s["st_size"], s.name)
                  for s in elf.get_section_by_name(".symtab").iter_symbols()
                  if s["st_info"]["type"] == "STT_FUNC" and s["st_size"] and s["st_value"])
    starts = [s[0] for s in syms]
    md = Cs(CS_ARCH_X86, CS_MODE_32)

    def owner(addr):
        index = bisect.bisect_right(starts, addr) - 1
        return syms[index] if index >= 0 and addr < syms[index][0] + syms[index][1] else None

    if sys.argv[1] == "calls":
        targets = {int(value, 0) for value in sys.argv[2:]}
        for address, size, name in syms:
            if not (base <= address and address + size <= base + len(code)):
                continue
            for ins in md.disasm(code[address-base:address-base+size], address):
                if ins.mnemonic == "call" and ins.op_str.startswith("0x") and int(ins.op_str, 16) in targets:
                    print(f"{ins.address:08x} {name} -> {ins.op_str}")
    elif sys.argv[1] == "dwarf-type":
        names = set(sys.argv[2:])
        dwarf = elf.get_dwarf_info()
        for cu in dwarf.iter_CUs():
            for die in cu.iter_DIEs():
                if die.tag not in ("DW_TAG_structure_type", "DW_TAG_class_type"):
                    continue
                attr = die.attributes.get("DW_AT_name")
                if not attr or attr.value.decode(errors="replace") not in names:
                    continue
                print(f"TYPE {attr.value.decode()} DIE {die.offset:x} size={die.attributes.get('DW_AT_byte_size')}")
                for child in die.iter_children():
                    if child.tag != "DW_TAG_member":
                        continue
                    child_name = child.attributes.get("DW_AT_name")
                    child_loc = child.attributes.get("DW_AT_data_member_location")
                    print(" ", child_name.value.decode(errors="replace") if child_name else "?", child_loc.value if child_loc else "?")
                break
