"""Read-only x86 ELF function extractor for the 70 reference binary."""
import argparse
from pathlib import Path
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM

ELF = Path(r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pattern", nargs="+", help="substring in mangled symbol name")
    ap.add_argument("--output", type=Path)
    args = ap.parse_args()
    with ELF.open("rb") as stream:
        elf = ELFFile(stream)
        syms = elf.get_section_by_name(".symtab")
        names = {s["st_value"]: s.name for s in syms.iter_symbols() if s["st_value"] and s.name}
        wanted = [s for s in syms.iter_symbols() if s["st_info"]["type"] == "STT_FUNC" and s["st_size"] and any(p in s.name for p in args.pattern)]
        wanted.sort(key=lambda s: s["st_value"])
        md = Cs(CS_ARCH_X86, CS_MODE_32)
        md.detail = True
        lines = []
        for sym in wanted:
            addr, size = sym["st_value"], sym["st_size"]
            section = elf.get_section(sym["st_shndx"])
            offset = section["sh_offset"] + addr - section["sh_addr"]
            stream.seek(offset)
            code = stream.read(size)
            lines.append(f"\n### {addr:08x} size={size} {sym.name}")
            for ins in md.disasm(code, addr):
                dest = ""
                if ins.mnemonic.startswith("call") and ins.operands and ins.operands[0].type == X86_OP_IMM:
                    target = ins.operands[0].imm & 0xffffffff
                    dest = f" ; {names.get(target, '')}"
                lines.append(f"{ins.address:08x}: {ins.mnemonic:8} {ins.op_str}{dest}")
        output = "\n".join(lines) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(output, encoding="utf-8")
        else:
            print(output)


if __name__ == "__main__":
    main()
