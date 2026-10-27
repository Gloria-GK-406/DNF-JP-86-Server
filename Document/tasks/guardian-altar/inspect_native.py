"""Bounded static evidence reader: mmap inputs, decode selected functions only."""
import argparse
import ctypes
import hashlib
import mmap
import struct
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32


def budget():
    class Counters(ctypes.Structure):
        _fields_ = [("cb", ctypes.c_ulong), ("faults", ctypes.c_ulong)] + [
            (name, ctypes.c_size_t) for name in (
                "peak_ws", "ws", "peak_paged", "paged", "peak_nonpaged", "nonpaged", "pagefile", "peak_pagefile", "private")]
    counters = Counters()
    counters.cb = ctypes.sizeof(counters)
    if not ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.c_void_p(-1), ctypes.byref(counters), counters.cb):
        raise OSError("Cannot check process memory budget")
    if counters.private > 512 * 1024 * 1024:
        raise MemoryError("Abort: reader private memory exceeds 512 MiB")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("path", type=Path)
    parser.add_argument("--symbols", default="AdvanceAltar")
    parser.add_argument("--function", action="append", default=[])
    parser.add_argument("--rva", action="append", default=[])
    parser.add_argument("--xrefs", action="append", default=[])
    parser.add_argument("--length", type=int, default=400)
    args = parser.parse_args()
    if not 0 < args.length <= 16384:
        parser.error("length must be 1..16384")
    print("Input:", args.path)
    with args.path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
        print("SHA256:", digest)
        with mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
            md = Cs(CS_ARCH_X86, CS_MODE_32)
            if args.xrefs:
                targets = {int(value, 0) for value in args.xrefs}
                offset = 0x1000
                end = min(len(data) - 5, 0x2300000)
                while offset < end:
                    offset = data.find(b"\xe8", offset, end)
                    if offset < 0:
                        break
                    target = offset + 0x400000 + 5 + struct.unpack_from("<i", data, offset + 1)[0]
                    if target in targets:
                        start = data.rfind(b"\x55\x8b\xec", max(0x1000, offset - 0x1000), offset)
                        print(f"candidate call {offset + 0x400000:#x} -> {target:#x}, nearby prologue {start + 0x400000:#x}")
                    offset += 1
                return
            if args.rva:
                for value in args.rva:
                    address = int(value, 0)
                    print(f"\n## RVA {address:#x}; VA base 0x400000")
                    for ins in md.disasm(data[address:address + args.length], address + 0x400000):
                        print(f"{ins.address:#010x}: {ins.mnemonic:8} {ins.op_str}")
                    budget()
                return
            shoff = struct.unpack_from("<I", data, 32)[0]
            entsize, count, _ = struct.unpack_from("<HHH", data, 46)
            sections = [struct.unpack_from("<IIIIIIIIII", data, shoff + i * entsize) for i in range(count)]
            table = next(s for s in sections if s[1] == 2)
            strings = sections[table[6]]
            symbols = {}
            selected = []
            for offset in range(table[4], table[4] + table[5], table[9]):
                no, address, size, info, _, _ = struct.unpack_from("<IIIBBH", data, offset)
                if info & 15 != 2:
                    continue
                start = strings[4] + no
                end = data.find(b"\0", start, strings[4] + strings[5])
                name = data[start:end].decode("utf8", "replace")
                symbols[address] = name
                if args.symbols.lower() in name.lower():
                    selected.append((address, size, name))
            for address, size, name in sorted(selected):
                if not args.function:
                    print(f"{address:#010x}\t{size}\t{name}")
                    continue
                if not any(value in name for value in args.function):
                    continue
                print(f"\n## {address:#x} {name} ({size} bytes)")
                section = next(s for s in sections if s[3] <= address < s[3] + s[5])
                offset = section[4] + address - section[3]
                for ins in md.disasm(data[offset:offset + min(size, 16384)], address):
                    suffix = ""
                    if ins.mnemonic == "call" and ins.op_str.startswith("0x"):
                        suffix = " ; " + symbols.get(int(ins.op_str, 16), "")
                    print(f"{ins.address:#010x}: {ins.mnemonic:8} {ins.op_str}{suffix}")
                budget()


if __name__ == "__main__":
    main()
