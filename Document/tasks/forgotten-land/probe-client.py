"""Bounded memory-mapped A21 inspection. Budget: 384 MiB RSS; 800 instructions per target."""
import csv
import mmap
import os
import struct
import re
import json
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
import ctypes
from ctypes import wintypes

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).parent / "evidence"
IMAGE = Path("E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin")
BASE = 0x400000
DIS = Cs(CS_ARCH_X86, CS_MODE_32)


def check_budget():
    class Counters(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD), ("faults", wintypes.DWORD)] + [
            (name, ctypes.c_size_t) for name in ("peak_working", "working", "peak_paged", "paged",
                "peak_nonpaged", "nonpaged", "pagefile", "peak_pagefile")]
    counters = Counters()
    counters.cb = ctypes.sizeof(counters)
    get_memory = ctypes.windll.psapi.GetProcessMemoryInfo
    get_memory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD]
    get_memory.restype = wintypes.BOOL
    if not get_memory(wintypes.HANDLE(-1), ctypes.byref(counters), counters.cb):
        raise ctypes.WinError()
    if counters.working > 384 * 1024 * 1024:
        raise RuntimeError("RSS budget exceeded")


def interesting(va):
    return 0xC48000 <= va < 0xC4F500 or 0xCA9000 <= va < 0xCB9000


with IMAGE.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
    # The existing literal index is a locator. Re-decode the cited literals from this exact image.
    def literal(va):
        offset = va - BASE
        first = data[offset:offset + 4]
        seed = first[1] & 0xFE
        count = ((seed << 7) | seed) ^ struct.unpack_from("<H", first, 2)[0]
        if not 0 < count < 1000:
            raise ValueError("Literal size is outside the narrow probe budget")
        key = seed | 0x9A714CA0
        raw = data[offset + 4:offset + 4 + count * 2]
        result = bytearray()
        words = len(raw) // 4
        for i in range(words):
            value = struct.unpack_from("<I", raw, i * 4)[0] ^ key
            result.extend(struct.pack("<I", value))
            key = (key * 0x1003F + value) & 0xFFFFFFFF
        for i in range(words * 4, len(raw)):
            value = raw[i] ^ (key & 0xFF)
            result.append(value)
            key = (key * 0x107 + value) & 0xFFFFFFFF
        return result.decode("utf-16le").rstrip("\0")
    literal_addresses = [0x319DFA0, 0x319DFC4, 0x319E060, 0x319E148, 0x31A72F0]
    (OUT / "client-decoded-literals.json").write_text(json.dumps({hex(va): literal(va)
        for va in literal_addresses}, indent=2, ensure_ascii=False), encoding="utf-8")
    with (OUT / "client-npc-role-xrefs.txt").open("w", encoding="utf-8") as output:
        needle = b"\x68" + struct.pack("<I", 0x36568E4)
        start, end = 0x2AAB190 - BASE, 0x2ACB190 - BASE
        while True:
            pos = data.find(needle, start, end)
            if pos < 0:
                break
            start = pos + len(needle)
            output.write(f"\n=== golden commerce literal reference VA {pos+BASE:08X} ===\n")
            for ins in DIS.disasm(data[pos:pos+220], pos+BASE):
                if ins.mnemonic == "int3":
                    break
                output.write(f"{ins.address:08X} {ins.mnemonic:8} {ins.op_str}\n")
    # Inspect the registered endpoints only; retain matching rows, never decoded instruction objects.
    inventory = ROOT / "protocol/patterns/data/protocol-inventory.tsv"
    with inventory.open(encoding="utf-8-sig") as source, (OUT / "client-protocol-links.txt").open("w", encoding="utf-8") as output:
        for row in csv.DictReader(source, delimiter="\t"):
            fields = list(row.values())
            addresses = [int(v, 16) for v in fields if v and v.startswith("0x") and len(v) == 10]
            for rva in set(addresses):
                if rva >= len(data):
                    continue
                va = rva + BASE
                matches = []
                for index, ins in enumerate(DIS.disasm(data[rva:rva + 2000], va)):
                    if index >= 160 or ins.mnemonic == "int3":
                        break
                    if ins.mnemonic in ("call", "jmp") and ins.op_str.startswith("0x"):
                        target = int(ins.op_str, 16)
                        if interesting(target):
                            matches.append(f"{ins.address:08X}: {ins.mnemonic} {ins.op_str}")
                if interesting(va) or matches:
                    output.write("\t".join(str(v) for v in fields) + "\n" + "\n".join(matches) + "\n")
                check_budget()
    # Known entrypoints only; do not start decoding in the middle of an instruction.
    targets = [0xC5E4B0, 0xC5E5D0, 0xC5E3A0, 0x1835F90, 0xC4EF10, 0xC4EF80, 0xC4EFE0, 0xCB2230, 0xC4B320, 0xC4B330, 0xC4B450, 0x171F170, 0xC4DB10, 0xC4D140, 0x11551C9, 0x1154840, 0x171C8B0, 0xC4F030, 0xC4EF20, 0xC4E6C0, 0xC4B870, 0xCB1AA0, 0xC4B300, 0xC4B3C0, 0x171C750, 0x171C760, 0xC4F1C0, 0xCB19C0, 0x1148240, 0x1149950, 0xC4D060, 0xC4D4C0, 0xC4A680, 0xCB6910, 0xC4D640, 0xC4D670,
        0xC4E850, 0xC4EB00, 0xC4DDB0, 0x175C950, 0xC227E0, 0xC22880]
    builder_site = 0xC22E8A - BASE
    symbol_builder_start = data.rfind(b"\x55\x8b\xec", builder_site - 0x400, builder_site)
    if symbol_builder_start >= 0:
        targets.append(symbol_builder_start + BASE)
    symbol_caller_site = 0x267C1FC - BASE
    symbol_caller_start = data.rfind(b"\x55\x8b\xec", symbol_caller_site - 0x800, symbol_caller_site)
    if symbol_caller_start >= 0:
        targets.append(symbol_caller_start + BASE)
    targets = sorted({va for va in targets if BASE < va < BASE + len(data)})
    with (OUT / "client-disassembly.txt").open("w", encoding="utf-8") as output:
        for va in targets:
            output.write(f"\n=== VA {va:08X}; RVA {va-BASE:08X} ===\n")
            cap = 4000 if va == 0x175C950 else 2600 if va == 0x1154840 else 800
            for index, ins in enumerate(DIS.disasm(data[va-BASE:va-BASE+cap*6], va)):
                if index >= cap or ins.mnemonic == "int3":
                    break
                output.write(f"{ins.address:08X} {ins.mnemonic:8} {ins.op_str}\n")
            check_budget()
    with (OUT / "client-module-callers.txt").open("w", encoding="utf-8") as output:
        for chunk in range(0x1000, 0x2300000, 0x40000):
            blob = data[chunk:min(chunk + 0x40000 + 4, len(data))]
            for match in re.finditer(b"\xe8", blob):
                p = match.start()
                if p + 5 > len(blob) or p >= 0x40000:
                    continue
                va = BASE + chunk + p
                target = va + 5 + struct.unpack_from("<i", blob, p + 1)[0]
                if 0xC4B2D0 <= target < 0xC4EF20 or 0xCA9000 <= target < 0xCB6900 or target in (0xC22E70, 0xC4EF70, 0xC4EF80, 0xC4EFD0):
                    output.write(f"{va:08X} -> {target:08X}\n")
            check_budget()
    with (OUT / "client-dungeon4000-sites.txt").open("w", encoding="utf-8") as output:
        hits = 0
        for chunk in range(0x1000, 0x2300000, 0x40000):
            blob = data[chunk:min(chunk + 0x40000 + 4, len(data))]
            for match in re.finditer(b"\x68\xa0\x0f\x00\x00", blob):
                if match.start() >= 0x40000:
                    continue
                rva = chunk + match.start()
                hits += 1
                if hits > 100:
                    raise RuntimeError("Too many push4000 sites")
                output.write(f"\n=== push 4000 VA {rva+BASE:08X} ===\n")
                for ins in DIS.disasm(data[rva:rva+150], rva+BASE):
                    if ins.mnemonic == "int3":
                        break
                    output.write(f"{ins.address:08X} {ins.mnemonic:8} {ins.op_str}\n")
            check_budget()
print("Bounded A21 probe complete")
