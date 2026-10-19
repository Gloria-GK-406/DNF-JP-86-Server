"""Read-only, bounded A21 collection-box evidence extraction.

Addresses in output are RVA; direct branch operands remain Capstone's VA.
This is linear disassembly, not a recovered control-flow graph. Embedded jump
tables must not be interpreted as instructions without following a live path.
"""

import argparse
import hashlib
import json
from pathlib import Path

import capstone


EXPECTED_DUMP_SHA256 = "52a9d002eb15ab6301069a954d84b77a122b9b6928f8886a1b0653054428ac72"
RANGES = {
    "select_request": (0x8990D0, 0x899127),
    "insert_request": (0x89E7D0, 0x89E904),
    "remove_request": (0x89E950, 0x89EA59),
    "extend_request": (0x898D90, 0x898DF0),
    "select_response": (0x8A6A30, 0x8A6ACE),
    "insert_response": (0x898FC0, 0x89902D),
    "remove_response": (0x899030, 0x8990C5),
    "extend_response": (0x89F070, 0x89F14C),
    "box_state": (0x8A7490, 0x8A7793),
    "box_state_apply": (0x8A73E0, 0x8A7482),
    "box_items_replace": (0x8A70A0, 0x8A70EC),
    "box_flag_test": (0x899290, 0x89931B),
    "box_inactive_test": (0x899340, 0x8993E4),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    data = args.dump.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != EXPECTED_DUMP_SHA256:
        parser.error("Dump identity differs; do not apply these RVA to another build.")
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    lines = ["A21 mapped-image evidence; RVA labels, VA branch operands."]
    for label, (start, end) in RANGES.items():
        lines.append(f"\n## {label}: RVA {start:08X}..{end:08X} (end exclusive)")
        for insn in decoder.disasm(data[start:end], 0x400000 + start):
            lines.append(f"{insn.address - 0x400000:08X} {insn.bytes.hex():24} {insn.mnemonic} {insn.op_str}".rstrip())
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "a21-bounded.asm.txt").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    (args.output / "inputs.json").write_text(json.dumps({
        "dump": str(args.dump.resolve()),
        "sha256": digest,
        "image_base": "0x00400000",
        "address_labels": "RVA",
        "branch_operands": "VA",
        "method": "bounded linear disassembly; no runtime capture",
        "ranges": {name: [hex(start), hex(end)] for name, (start, end) in RANGES.items()},
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
