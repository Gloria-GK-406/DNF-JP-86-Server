# -*- coding: utf-8 -*-
"""DISJOINT_ITEM (CMD 0x001A) contextValue static investigation workbench (read-only).

Target: E:\\Project\\DNF\\DNFClient-A21-debug\\DNF.unpacked-b1-debug.exe
        (SHA-256 DC322D8F..., image base 0x00400000)
Same image + assets as the pickup investigation (Sep 2026).

Assets:
  func-source-file-a21.csv / literals-a21.csv  (client-debug-lab tools)
"""
import csv
import os
import struct
from pathlib import Path

import capstone
import pefile

REPOSITORY = Path(__file__).resolve().parents[3]
WORKSPACE = REPOSITORY.parent
EXE = os.environ.get(
    "A21_DEBUG_EXE",
    str(WORKSPACE / "DNFClient-A21-debug" / "DNF.unpacked-b1-debug.exe"))
TOOLS = os.environ.get(
    "A21_CLIENTHACK_TOOLS",
    str(REPOSITORY / "ClientHack" / "tools"))
IMAGEBASE = 0x00400000

_md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
_md.detail = True


class Image:
    def __init__(self, path=EXE):
        self.raw = open(path, "rb").read()
        self.pe = pefile.PE(data=self.raw, fast_load=True)
        self.sections = []
        for s in self.pe.sections:
            name = s.Name.rstrip(b"\x00").decode("ascii", "replace")
            self.sections.append(
                dict(name=name, va=s.VirtualAddress, vsz=s.Misc_VirtualSize,
                     raw=s.PointerToRawData, rsz=s.SizeOfRawData,
                     char=s.Characteristics))
        self.exec_ranges = []
        for s in self.sections:
            if s["vsz"] and (s["char"] & 0x20000000):
                self.exec_ranges.append((IMAGEBASE + s["va"],
                                         IMAGEBASE + s["va"] + max(s["vsz"], s["rsz"]), s))

    def va2off(self, va):
        rva = va - IMAGEBASE
        for s in self.sections:
            if s["va"] <= rva < s["va"] + max(s["vsz"], s["rsz"]):
                if rva - s["va"] < s["rsz"]:
                    return s["raw"] + (rva - s["va"])
                return None
        return None

    def read(self, va, n):
        off = self.va2off(va)
        if off is None:
            return None
        return self.raw[off:off + n]

    def u32(self, va):
        b = self.read(va, 4)
        return struct.unpack("<I", b)[0] if b and len(b) == 4 else None

    def u16(self, va):
        b = self.read(va, 2)
        return struct.unpack("<H", b)[0] if b and len(b) == 2 else None

    def u8(self, va):
        b = self.read(va, 1)
        return b[0] if b else None

    def section_of(self, va):
        rva = va - IMAGEBASE
        for s in self.sections:
            if s["va"] <= rva < s["va"] + max(s["vsz"], s["rsz"]):
                return s["name"]
        return None


IMG = Image()


def _load_csv(name):
    path = os.path.join(TOOLS, name)
    if not os.path.exists(path):
        return []
    with open(path, "r", encoding="utf-8", errors="replace", newline="") as f:
        return list(csv.DictReader(f))


SRCFILE = {}
for row in _load_csv("func-source-file-a21.csv"):
    SRCFILE.setdefault(int(row["func_va"], 16), set()).add(row["source_file"])

LITS = {}
for row in _load_csv("literals-a21.csv"):
    LITS.setdefault(int(row["func_va"], 16), []).append(
        (int(row["literal_va"], 16), row["text"]))

_sorted_starts = sorted(SRCFILE)


def src_of(va):
    """source file of the function containing va"""
    import bisect
    i = bisect.bisect_right(_sorted_starts, va) - 1
    if i < 0:
        return None
    best = _sorted_starts[i]
    if va - best > 0x30000:
        return None
    return "/".join(sorted(SRCFILE[best]))


def func_start_of(va):
    import bisect
    i = bisect.bisect_right(_sorted_starts, va) - 1
    if i < 0:
        return None
    best = _sorted_starts[i]
    if va - best > 0x30000:
        return None
    return best


def lits_in_func(fva):
    return LITS.get(fva, [])


def search_lits(sub, limit=200):
    out = []
    sub_l = sub.lower()
    for fva, items in LITS.items():
        for lva, text in items:
            if sub_l in text.lower():
                out.append((fva, lva, text))
                if len(out) >= limit:
                    return out
    return out


# ---------- disassembly ----------
def disasm(va, count=60, maxbytes=None):
    code = IMG.read(va, maxbytes or count * 16)
    if code is None:
        return []
    out = []
    for insn in _md.disasm(code, va):
        out.append(insn)
        if len(out) >= count:
            break
    return out


def show(va, count=60, maxbytes=None):
    for insn in disasm(va, count, maxbytes):
        print(f"{insn.address:08X}  {insn.mnemonic:<8} {insn.op_str}")


def show_until(va, end_va):
    code = IMG.read(va, end_va - va)
    if code is None:
        print(f"unreadable {va:08X}")
        return
    for insn in _md.disasm(code, va):
        print(f"{insn.address:08X}  {insn.mnemonic:<8} {insn.op_str}")


# ---------- xrefs ----------
_callers_cache = {}


def scan_callers():
    if _callers_cache:
        return _callers_cache
    tbl = {}
    for lo, hi, s in IMG.exec_ranges:
        if s["name"] not in (".text", ".code"):
            continue
        off = s["raw"]
        data = IMG.raw[off:off + s["rsz"]]
        base = IMAGEBASE + s["va"]
        pos = 0
        while True:
            idx = data.find(b"\xe8", pos)
            if idx < 0:
                idx = data.find(b"\xe9", pos)
                if idx < 0:
                    break
            va = base + idx
            rel = struct.unpack("<i", data[idx + 1:idx + 5])[0]
            tgt = va + 5 + rel
            if 0x00400000 <= tgt < 0x08000000:
                tbl.setdefault(tgt, []).append(va)
            pos = idx + 1
    _callers_cache.update(tbl)
    return _callers_cache


def callers_of(va):
    return sorted(scan_callers().get(va, []))


def calls_in_range(lo, hi):
    out = []
    code = IMG.read(lo, hi - lo)
    if code is None:
        return out
    for insn in _md.disasm(code, lo):
        if insn.mnemonic in ("call", "jmp") and insn.op_str.startswith("0x"):
            tgt = int(insn.op_str, 16)
            out.append((insn.address, insn.mnemonic, tgt))
    return out


def imms_in_range(lo, hi, want):
    out = []
    code = IMG.read(lo, hi - lo)
    if code is None:
        return out
    want = set(want) if not isinstance(want, set) else want
    for insn in _md.disasm(code, lo):
        for tok in insn.op_str.replace("[", " ").replace("]", " ").split(","):
            tok = tok.strip()
            if tok.startswith("0x"):
                try:
                    v = int(tok, 16)
                except ValueError:
                    continue
                if v in want:
                    out.append((insn.address, insn.mnemonic, insn.op_str))
    return out


# ---------- literal decrypt (live, for literals not in CSV) ----------
def _crypt(src, key):
    out = bytearray()
    k = key
    ln = len(src)
    nd = ln // 4
    for i in range(nd):
        d = struct.unpack_from("<I", src, i * 4)[0]
        v = (d ^ k) & 0xFFFFFFFF
        out += struct.pack("<I", v)
        k = (k * 0x1003F + v) & 0xFFFFFFFF
    for i in range(nd * 4, ln):
        c = src[i] ^ (k & 0xFF)
        out.append(c)
        k = (k * 0x107 + c) & 0xFFFFFFFF
    return bytes(out)


def dec_literal(va):
    b = IMG.read(va, 4)
    if not b or len(b) < 4:
        return None
    s = b[1] & 0xFE
    w = struct.unpack("<H", b[2:4])[0]
    n = (((s << 7) | s) ^ w) & 0xFFFFFFFF
    best = None
    for mode in (2, 1):
        ln = n * mode
        if ln == 0 or ln > 0x2000:
            continue
        data = IMG.read(va + 4, ln)
        if data is None or len(data) < ln:
            continue
        d = _crypt(data, (s | 0x9A714CA0) & 0xFFFFFFFF)
        if mode == 2:
            if len(d) % 2:
                continue
            try:
                t = d.decode("utf-16le")
            except Exception:
                continue
        else:
            try:
                t = d.decode("cp949")
            except Exception:
                try:
                    t = d.decode("latin1")
                except Exception:
                    continue
        t = t.rstrip("\x00")
        if not t:
            continue
        bad = sum(1 for c in t if ord(c) < 0x20 and c not in "\r\n\t")
        if bad * 4 > len(t):
            continue
        score = sum(1 for c in t if 0x20 <= ord(c) < 0x7F) / max(1, len(t))
        if best is None or score > best[0]:
            best = (score, t)
    if best and best[0] > 0.55:
        return best[1]
    return None


def find_pattern(pattern, lo=0x00401000, hi=0x03C00000):
    pat = pattern.split()
    res = []
    for s in IMG.sections:
        if not (s["char"] & 0x20000000):
            continue
        base = IMAGEBASE + s["va"]
        if base + s["rsz"] < lo or base > hi:
            continue
        data = IMG.raw[s["raw"]:s["raw"] + s["rsz"]]
        start = max(0, lo - base)
        end = min(len(data), hi - base)
        pos = start
        while pos < end:
            ok = True
            for i, p in enumerate(pat):
                if p == "??":
                    continue
                if data[pos + i] != int(p, 16):
                    ok = False
                    break
            if ok:
                res.append(base + pos)
                pos += 1
            else:
                pos += 1
    return res


if __name__ == "__main__":
    print("image ok; sections:",
          [(s["name"], hex(IMAGEBASE + s["va"]), hex(s["rsz"])) for s in IMG.sections])


# ---------- recursive disassembler ----------
def rdisasm(entry, limit=0x4000):
    """recursive descent from entry; returns {addr: insn}, set of call targets."""
    seen = {}
    work = [entry]
    calls = set()
    while work:
        va = work.pop()
        while True:
            if va in seen or va is None:
                break
            code = IMG.read(va, 16)
            if code is None:
                break
            insn = next(_md.disasm(bytes(code), va), None)
            if insn is None:
                break
            seen[va] = insn
            m = insn.mnemonic
            if m in ("jmp",):
                tgt = int(insn.op_str, 16) if insn.op_str.startswith("0x") else None
                if tgt and tgt not in seen:
                    work.append(tgt)
                break
            if m.startswith("j") or m in ("loop", "loopd"):
                tgt = int(insn.op_str, 16) if insn.op_str.startswith("0x") else None
                if tgt and tgt not in seen:
                    work.append(tgt)
                va = insn.address + insn.size
                continue
            if m == "call":
                tgt = int(insn.op_str, 16) if insn.op_str.startswith("0x") else None
                if tgt:
                    calls.add(tgt)
                va = insn.address + insn.size
                continue
            if m in ("ret", "retf", "iret", "int3"):
                break
            va = insn.address + insn.size
    return seen, calls


def show_r(entry):
    seen, calls = rdisasm(entry)
    for va in sorted(seen):
        insn = seen[va]
        print(f"{insn.address:08X}  {insn.mnemonic:<8} {insn.op_str}")
    return seen, calls
