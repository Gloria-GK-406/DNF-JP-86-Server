"""Bounded A21 result-list inspection; no client attachment or player database access."""
import hashlib
import struct
import re
from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = Path("E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin")
OUT = Path(__file__).parent / "evidence/client-result-paths.txt"
EXPECTED = "6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507"
BASE = 0x400000
TARGETS = {
    0xC4DB10: 0x9D, 0xC4D670: 0x411, 0xCACC10: 0x10,
    0xCADD90: 0xC0, 0xC610E0: 0x10D, 0xC61260: 0x90,
    0xC62070: 0x2EE, 0xC616B0: 0x111,
    0x1155010: 0x6C, 0xEE2120: 0xC5,
    0x14348C0: 0xBE, 0x2647210: 0xC5,
}
with IMAGE.open("rb") as source:
    digest = hashlib.file_digest(source, "sha256").hexdigest().upper()
    if digest != EXPECTED:
        raise ValueError("Unexpected image identity")
    dis = Cs(CS_ARCH_X86, CS_MODE_32)
    with OUT.open("w", encoding="utf-8") as output:
        output.write(f"ClientStatic only; image SHA256={digest}; base={BASE:08X}\n")
        output.write("Collector block 01155010 is an identified basic-block boundary. Other targets are function entries.\n")
        for va, size in TARGETS.items():
            output.write(f"\n=== VA {va:08X}; bytes <= {size}; instructions <= 800 ===\n")
            source.seek(va - BASE)
            for count, (address, _, mnemonic, operands) in enumerate(dis.disasm_lite(source.read(size), va)):
                if count >= 800 or mnemonic == "int3":
                    break
                output.write(f"{address:08X} {mnemonic:8} {operands}\n")
        output.write("\n=== Candidate direct CALL references; validate instructions and receiver separately ===\n")
        targets = {0xC62070, 0xC610E0, 0xCADD90, 0xC4DB10}
        for chunk in range(0x1000, 0x2300000, 0x40000):
            source.seek(chunk)
            blob = source.read(0x40004)
            for match in re.finditer(b"\xe8", blob):
                at = match.start()
                if at >= 0x40000 or at + 5 > len(blob):
                    continue
                va = BASE + chunk + at
                target = va + 5 + struct.unpack_from("<i", blob, at + 1)[0]
                if target in targets:
                    output.write(f"{va:08X} -> {target:08X}\n")
print(OUT)
