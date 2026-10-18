"""只读扫描固定 A21 EXE 中婚房关键全局和构包函数的直接代码引用。"""
from __future__ import annotations

import hashlib
from collections import deque
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_INS_CALL, X86_OP_IMM, X86_OP_MEM


EXE = Path(r"E:\Project\DNF\DNFClient-A21-protocol\DNF.unpacked-b1-debug.exe")
EXPECTED = "CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14"
BASE = 0x00400000
GLOBALS = {
    0x039ADF68: "character/wedding manager pointer (03D1/03D3 consumer)",
    0x039ADF58: "couple-room/tree manager pointer",
}
CALL_TARGETS = {
    BASE + 0x008916F0: "03F4 ring-upgrade builder",
    BASE + 0x0087E79C: "03FD room inventory open-close builder A",
    BASE + 0x0088073D: "03FD room inventory open-close builder B",
    BASE + 0x010484F0: "0400 tree-plant builder candidate",
    BASE + 0x01049E60: "0401 tree-water builder candidate",
    BASE + 0x01048790: "0402 tree-harvest builder candidate",
    BASE + 0x01047AA0: "0403 tree-remove builder",
}


def fmt(ins) -> str:
    return f"RVA {ins.address-BASE:08X} VA {ins.address:08X} {ins.bytes.hex():<20} {ins.mnemonic:<8} {ins.op_str}"


def main() -> None:
    blob = EXE.read_bytes()
    sha = hashlib.sha256(blob).hexdigest().upper()
    assert sha == EXPECTED, sha
    pe = pefile.PE(data=blob, fast_load=True)
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    md.skipdata = True
    hits = []
    for section in pe.sections:
        name = section.Name.rstrip(b"\0").decode("ascii", "replace")
        if not (section.Characteristics & 0x20000000):
            continue
        start = BASE + section.VirtualAddress
        insns = list(md.disasm(section.get_data(), start))
        for index, ins in enumerate(insns):
            reasons = []
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                    absolute = op.mem.disp & 0xFFFFFFFF
                    if absolute in GLOBALS:
                        reasons.append(GLOBALS[absolute])
            if ins.id == X86_INS_CALL and ins.operands and ins.operands[0].type == X86_OP_IMM:
                target = ins.operands[0].imm & 0xFFFFFFFF
                if target in CALL_TARGETS:
                    reasons.append(f"direct call -> {CALL_TARGETS[target]}")
            if reasons:
                left = max(0, index - 8)
                right = min(len(insns), index + 18)
                hits.append((section.Name, reasons, insns[left:right]))

    lines = [
        f"executable={EXE}", f"sha256={sha}", f"image_base={BASE:08X}",
        "Evidence=ClientStatic only; direct instruction references only.", ""
    ]
    for number, (section, reasons, context) in enumerate(hits, 1):
        lines.append(f"===== hit {number}; section={section!r}; reason={' | '.join(reasons)} =====")
        lines.extend(fmt(ins) for ins in context)
        lines.append("")
    out = Path(__file__).with_name("housing-a21-xrefs.txt")
    out.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8", newline="\n")
    print(f"{out} hits={len(hits)}")


if __name__ == "__main__":
    main()
