"""只读提取固定 A21 客户端的婚房/情侣树/戒指协议指令窗口。"""
from __future__ import annotations

import hashlib
import re
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM


EXE = Path(r"E:\Project\DNF\DNFClient-A21-protocol\DNF.unpacked-b1-debug.exe")
EXPECTED_SHA256 = "CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14"
IMAGE_BASE = 0x00400000

# 均为 RVA。构包点/handler 入口来自现有协议页，窗口刻意留出调用者和分支上下文。
WINDOWS = [
    ("C2S 03F4 ring upgrade builder", 0x00891580, 0x00891920),
    ("CMD 03F4 response consumer", 0x00892FF0, 0x00893380),
    ("NOTI 03D1 ring update", 0x00892B50, 0x00892FF0),
    ("NOTI 03D3 love point update", 0x00893550, 0x00893A80),
    ("C2S 03FD inventory open close builder A", 0x0087E620, 0x0087E890),
    ("C2S 03FD inventory open close builder B", 0x008806F0, 0x008808A0),
    ("CMD 03FD response consumer", 0x00880B60, 0x00880F20),
    ("C2S 03FE room move builder UI", 0x0087C5F0, 0x0087C830),
    ("C2S 03FE room move builder inventory", 0x0087E5E0, 0x0087E790),
    ("C2S 03FE room move builder third", 0x00880E20, 0x00881050),
    ("CMD 03FE response consumer", 0x0087E3C0, 0x0087E5A0),
    ("C2S 03FF inventory delete builder", 0x0087BF40, 0x0087C150),
    ("CMD 03FF response consumer", 0x0087E580, 0x0087E700),
    ("NOTI 03D8 room snapshot", 0x008820F0, 0x008824B0),
    ("NOTI 03D9 inventory snapshot", 0x00882240, 0x00882670),
    ("NOTI 03DA room update", 0x008824B0, 0x00882670),
    ("NOTI 03DB room refresh", 0x00882670, 0x00882920),
    ("C2S 0400 tree plant builder", 0x010483F0, 0x01048640),
    ("CMD 0400 response consumer", 0x0087E8E0, 0x0087E950),
    ("C2S 0401 tree water builder", 0x01049D40, 0x01049FA0),
    ("CMD 0401 response consumer", 0x0087E950, 0x0087EA10),
    ("C2S 0402 tree harvest builder", 0x010486D0, 0x01048870),
    ("CMD 0402 response consumer", 0x0087EA10, 0x0087EAA0),
    ("C2S 0403 tree remove builder", 0x010479D0, 0x01047C00),
    ("CMD 0403 response consumer", 0x0087EAA0, 0x0087EB80),
    ("NOTI 03DC tree snapshot", 0x0087E840, 0x0087E8E0),
    ("NOTI 03DD couple simple info", 0x00893190, 0x00893550),
    ("NOTI 03DE harvest reward", 0x0087EFF0, 0x0087F300),
]


def read_c_string(pe: pefile.PE, va: int) -> str | None:
    rva = va - IMAGE_BASE
    if rva < 0:
        return None
    try:
        data = pe.get_data(rva, 256)
    except Exception:
        return None
    if not data:
        return None
    raw = data.split(b"\0", 1)[0]
    if 4 <= len(raw) <= 200 and all(b in b"\t\r\n" or 0x20 <= b < 0x7F for b in raw):
        return raw.decode("ascii", errors="replace")
    # UTF-16LE 本地化/资源标识；要求至少 4 个字符且有终止符。
    end = data.find(b"\0\0")
    if end >= 7:
        end += end % 2
        try:
            text = data[:end].decode("utf-16le")
        except UnicodeDecodeError:
            return None
        if len(text) >= 4 and all(ch.isprintable() or ch in "\r\n\t" for ch in text):
            return text
    return None


def main() -> None:
    blob = EXE.read_bytes()
    sha = hashlib.sha256(blob).hexdigest().upper()
    assert sha == EXPECTED_SHA256, sha
    pe = pefile.PE(data=blob, fast_load=True)
    assert pe.OPTIONAL_HEADER.ImageBase == IMAGE_BASE
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True

    lines = [
        f"executable={EXE}",
        f"sha256={sha}",
        f"image_base={IMAGE_BASE:08X}",
        "Evidence=ClientStatic only; read-only PE decoding; RVA and VA are both printed.",
        "",
    ]
    for title, start, end in WINDOWS:
        lines.append(f"===== {title}; RVA {start:08X}..{end:08X}; VA {start + IMAGE_BASE:08X}..{end + IMAGE_BASE:08X} =====")
        data = pe.get_data(start, end - start)
        for ins in md.disasm(data, start + IMAGE_BASE):
            extra = []
            for op in ins.operands:
                value = None
                if op.type == X86_OP_IMM:
                    value = op.imm & 0xFFFFFFFF
                elif op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                    value = op.mem.disp & 0xFFFFFFFF
                if value is not None:
                    string = read_c_string(pe, value)
                    if string:
                        extra.append(f"str@{value:08X}={string!r}")
            suffix = " ; " + " | ".join(dict.fromkeys(extra)) if extra else ""
            lines.append(
                (f"RVA {ins.address - IMAGE_BASE:08X} VA {ins.address:08X} "
                 f"{ins.bytes.hex():<24} {ins.mnemonic:<8} {ins.op_str}{suffix}").rstrip()
            )
        lines.append("")

    out = Path(__file__).with_name("housing-a21-disassembly.txt")
    out.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8", newline="\n")
    print(out)


if __name__ == "__main__":
    main()
