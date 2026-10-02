"""Reproduce bounded, read-only warehouse evidence from the identified A21 dump and 70 ELF."""
import argparse
import hashlib
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from elftools.elf.elffile import ELFFile

BASE = 0x400000
DUMP_HASH = "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"
TW70_HASH = "cf45f4e8514c3bf560dd322c146f8441249f6c28246ec8566dcf9ebc4e1b2eb7"
REGIONS = [
    ("NOTI 000D list parameters", 0x117C670, 0x117C920),
    ("Main capacity 32 + stage * 8", 0x15B4640, 0x15B4687),
    ("Main UI rows and stage ceiling", 0x15B47C0, 0x15B4890),
    ("Main stage getter", 0x15B9090, 0x15B9097),
    ("Main event 10", 0x15BB196, 0x15BB242),
    ("Main item-slot request caller and stage setter", 0x15BB43E, 0x15BB4DB),
    ("Main upgrade request", 0x15B93E0, 0x15B9419),
    ("Avatar capacity and stage getter", 0x15A3470, 0x15A34D0),
    ("Avatar extra-slot setter", 0x15A36C0, 0x15A3790),
    ("Avatar setter bridge", 0x15B9300, 0x15B9320),
    ("Avatar stage ceiling 15", 0x15B9420, 0x15B944F),
    ("Personal cargo request locates item slot", 0x1A7A2F0, 0x1A7A380),
    ("Find item index", 0x16EF580, 0x16EF680),
    ("Personal cargo response", 0x111CCF0, 0x111CD90),
    ("Account create response", 0x1128480, 0x1128570),
    ("Account upgrade response", 0x1128570, 0x1128660),
    ("Account deposit response", 0x111C440, 0x111C530),
    ("Account withdraw response", 0x111C530, 0x111C630),
]
TW70 = [0x822A0A4, 0x822A4BC, 0x850C656, 0x850C698, 0x88C7E80,
        0x88C8056, 0x88C80BA, 0x88C8124, 0x828A4CA]

def disasm(blob, address, symbols=None):
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    result = []
    for ins in decoder.disasm(blob, address):
        reference = ""
        if symbols and ins.mnemonic == "call" and ins.op_str.startswith("0x"):
            reference = symbols.get(int(ins.op_str, 16), "")
        rva = f" RVA {ins.address-BASE:08X}" if symbols is None else ""
        result.append(f"VA {ins.address:08X}{rva} {ins.bytes.hex(' '):<24} {ins.mnemonic:<7} {ins.op_str} {reference}".rstrip())
    return result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dump", type=Path, required=True)
    parser.add_argument("--tw70", type=Path)
    args = parser.parse_args()
    out = Path(__file__).resolve().parent
    data = args.dump.read_bytes()
    assert hashlib.sha256(data).hexdigest() == DUMP_HASH, "Unexpected A21 dump"
    lines = ["ClientStatic; bounded instruction ranges, not complete function or dynamic execution claims.",
             "A21 image base 00400000; dump SHA256 " + DUMP_HASH]
    for title, start, end in REGIONS:
        lines += ["", f"## {title}; VA {start:08X}..{end:08X}"]
        lines += disasm(data[start-BASE:end-BASE], start)
    (out / "a21-capacity-and-upgrade.asm.txt").write_bytes(("\n".join(lines)+"\n").encode("utf-8"))
    if args.tw70:
        assert hashlib.sha256(args.tw70.read_bytes()).hexdigest() == TW70_HASH, "Unexpected 70 ELF"
        with args.tw70.open("rb") as handle:
            elf = ELFFile(handle)
            text = elf.get_section_by_name(".text")
            raw = text.data(); base = text["sh_addr"]
            symbols = list(elf.get_section_by_name(".symtab").iter_symbols())
            names = {s["st_value"]: s.name for s in symbols if s["st_value"]}
            lines = ["70Reference only; layouts and limits differ from A21.", "ELF SHA256 " + TW70_HASH]
            for address in TW70:
                symbol = next(s for s in symbols if s["st_value"] == address and s["st_size"])
                lines += ["", f"## {symbol.name}; VA {address:08X}, size {symbol['st_size']}"]
                lines += disasm(raw[address-base:address-base+symbol["st_size"]], address, names)
            (out / "tw70-capacity-and-upgrade.asm.txt").write_bytes(("\n".join(lines)+"\n").encode("utf-8"))

if __name__ == "__main__":
    main()
