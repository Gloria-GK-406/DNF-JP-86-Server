"""A21 婚房调查共用的只读反汇编工具（ClientStatic）。

- 只读取固定 SHA-256 的 A21 EXE 与内存镜像，不写任何客户端文件。
- 镜像按 RVA 平铺；VA = RVA + 0x00400000。
- 函数按递归下降（跟随条件/无条件跳转，遇 ret 停）收集，避免线性反汇编把
  ret 之后的数据当成代码。
"""
from __future__ import annotations

import hashlib
import re
import struct
from functools import lru_cache
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

ROOT = Path(r"E:\Project\DNF\DNFClient-A21-protocol")
EXE = ROOT / "DNF.unpacked-b1-debug.exe"
DUMP = ROOT / "dumps" / "dnf-dump-20260906-224825-165-31384" / "DNF-memory-rva.bin"
EXE_SHA256 = "CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14"
DUMP_SHA256 = "6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507"
BASE = 0x00400000
TEXT_START, TEXT_END = 0x1000, 0x1000 + 0x2C28000

# 已知原语（RVA），来源 protocol/patterns/data/probes/a21-census-anchors.md，本工具会再核对字节。
PRIMS = {
    0x0234CA50: "GetWriter",
    0x0234D1E0: "MakeHeader",
    0x0234D2E0: "WriteU8",
    0x0234D310: "WriteU16",
    0x0234D340: "WriteU32",
    0x0234CED0: "WriteBytes(ptr,len)",
    0x0234D4A0: "WriteDString",
    0x0234E6D0: "Finalize/Send",
    0x023C5230: "ReadU8",
    0x023C52A0: "ReadU16",
    0x023C5310: "ReadU32",
    0x023C5380: "ReadBytes(dst,len)",
    0x0123C4A0: "Register",
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


@lru_cache(maxsize=1)
def image() -> bytes:
    assert sha256(EXE) == EXE_SHA256, "A21 EXE SHA-256 mismatch"
    assert sha256(DUMP) == DUMP_SHA256, "A21 dump SHA-256 mismatch"
    data = DUMP.read_bytes()
    # 抽查 EXE 与镜像在 .text 中的字节一致（EXE 由该镜像重建）。
    import pefile

    pe = pefile.PE(str(EXE), fast_load=True)
    for rva in (0x008820F0, 0x0087E620, 0x010483F0, 0x00893190, 0x0234D1E0):
        assert pe.get_data(rva, 64) == data[rva : rva + 64], hex(rva)
    return data


def header_lines() -> list[str]:
    image()
    return [
        f"exe={EXE}",
        f"exe_sha256={EXE_SHA256}",
        f"dump={DUMP}",
        f"dump_sha256={DUMP_SHA256}",
        f"image_base={BASE:08X}  (VA = RVA + base)",
        "evidence=ClientStatic; read-only",
        "",
    ]


_md = Cs(CS_ARCH_X86, CS_MODE_32)
_md.detail = True


def insn_at(rva: int):
    data = image()
    for ins in _md.disasm(data[rva : rva + 16], rva + BASE, count=1):
        return ins
    return None


def ascii_or_utf16(va: int) -> str | None:
    rva = va - BASE
    data = image()
    if not (0 <= rva < len(data) - 4):
        return None
    chunk = data[rva : rva + 160]
    raw = chunk.split(b"\0", 1)[0]
    if 4 <= len(raw) <= 150 and all(0x20 <= b < 0x7F for b in raw):
        return raw.decode("ascii")
    try:
        end = 0
        while end + 1 < len(chunk) and (chunk[end] or chunk[end + 1]):
            end += 2
        if end >= 8:
            t = chunk[:end].decode("utf-16le")
            if all(c.isprintable() for c in t):
                return t
    except UnicodeDecodeError:
        pass
    return None


def annotate(ins) -> str:
    extra = []
    if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == X86_OP_IMM:
        t = (ins.operands[0].imm & 0xFFFFFFFF) - BASE
        if t in PRIMS:
            extra.append(PRIMS[t])
        else:
            extra.append(f"->RVA {t:08X}")
    for op in ins.operands:
        v = None
        if op.type == X86_OP_IMM:
            v = op.imm & 0xFFFFFFFF
        elif op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
            v = op.mem.disp & 0xFFFFFFFF
        if v is not None and v > BASE:
            s = ascii_or_utf16(v)
            if s:
                extra.append(f"str@{v:08X}={s!r}")
    return (" ; " + " | ".join(extra)) if extra else ""


def fmt(ins) -> str:
    return (
        f"RVA {ins.address - BASE:08X} VA {ins.address:08X} "
        f"{ins.bytes.hex():<22} {ins.mnemonic:<7} {ins.op_str}{annotate(ins)}"
    )


def function(rva: int, limit: int = 6000):
    """递归下降收集函数指令；返回按地址排序的指令列表。"""
    data = image()
    seen: dict[int, object] = {}
    work = [rva]
    while work and len(seen) < limit:
        cur = work.pop()
        while cur not in seen:
            ins = insn_at(cur)
            if ins is None:
                break
            seen[cur] = ins
            nxt = cur + ins.size
            m = ins.mnemonic
            if m.startswith("j") and ins.operands and ins.operands[0].type == X86_OP_IMM:
                tgt = (ins.operands[0].imm & 0xFFFFFFFF) - BASE
                if TEXT_START <= tgt < TEXT_END and (m != "jmp" or abs(tgt - rva) < 0x4000):
                    work.append(tgt)
                if m == "jmp":
                    break
            elif m == "jmp":
                # 跳表 `jmp [reg*4 + TABLE]`：读取表项，仅跟随离函数入口 0x4000 内的目标
                op = ins.operands[0] if ins.operands else None
                if op is not None and op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.scale == 4:
                    tab = (op.mem.disp & 0xFFFFFFFF) - BASE
                    for k in range(64):
                        if not (0 <= tab + 4 * k + 4 <= len(data)):
                            break
                        tgt = struct.unpack_from("<I", data, tab + 4 * k)[0] - BASE
                        if not (abs(tgt - rva) < 0x4000 and TEXT_START <= tgt < TEXT_END):
                            break
                        work.append(tgt)
                break
            elif m in ("ret", "retf", "int3"):
                break
            cur = nxt
    return [seen[k] for k in sorted(seen)]


def dump_function(rva: int, title: str) -> list[str]:
    lines = [f"===== {title}; entry RVA {rva:08X} VA {rva + BASE:08X} ====="]
    lines += [fmt(i) for i in function(rva)]
    lines.append("")
    return lines


def window(start: int, end: int) -> list:
    data = image()
    return list(_md.disasm(data[start:end], start + BASE))


def find_entry(rva: int, back: int = 0x3000) -> int:
    """从函数内部地址向前找最近的函数入口：CC 填充边界（前两字节为 CC），
    或前一字节为 CC/90/C3 的 `push ebp; mov ebp, esp`。找不到返回 -1。"""
    data = image()
    for p in range(rva, max(TEXT_START, rva - back), -1):
        if data[p] != 0xCC and data[p - 1] == 0xCC and data[p - 2] == 0xCC:
            return p
        if data[p : p + 3] == bytes((0x55, 0x8B, 0xEC)) and data[p - 1] in (0xCC, 0x90, 0xC3):
            return p
    return -1


@lru_cache(maxsize=1)
def call_index() -> dict[int, list[int]]:
    """全 .text 的 E8 rel32 直接调用索引：target RVA -> [call site RVA]。"""
    data = image()
    idx: dict[int, list[int]] = {}
    for m in re.finditer(b"\xe8", data[TEXT_START:TEXT_END], flags=re.DOTALL):
        site = TEXT_START + m.start()
        rel = struct.unpack_from("<i", data, site + 1)[0]
        tgt = site + 5 + rel
        if TEXT_START <= tgt < TEXT_END:
            idx.setdefault(tgt, []).append(site)
    return idx


def callers(target_rva: int) -> list[int]:
    return call_index().get(target_rva, [])


def imm_refs(value: int, start: int = TEXT_START, end: int = TEXT_END) -> list[int]:
    data = image()
    pat = struct.pack("<I", value)
    out = []
    p = data.find(pat, start, end)
    while p != -1:
        out.append(p)
        p = data.find(pat, p + 1, end)
    return out
