"""Read-only A21 memory-image disassembly at explicit VA boundaries."""
import argparse
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE_BASE = 0x00400000
DUMP = Path(r"E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("start", type=lambda x: int(x, 0))
    parser.add_argument("end", type=lambda x: int(x, 0))
    args = parser.parse_args()
    data = DUMP.read_bytes()
    assert IMAGE_BASE <= args.start < args.end <= IMAGE_BASE + len(data)
    blob = data[args.start - IMAGE_BASE:args.end - IMAGE_BASE]
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    decoder.skipdata = False
    position = args.start
    for instruction in decoder.disasm(blob, position):
        print(f"{instruction.address:08X}  {instruction.bytes.hex(' '):<25} {instruction.mnemonic:<7} {instruction.op_str}")
        position = instruction.address + instruction.size
    if position != args.end:
        print(f"STOP {position:08X}; requested end {args.end:08X}")


if __name__ == "__main__":
    main()
