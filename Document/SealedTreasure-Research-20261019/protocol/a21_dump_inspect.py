"""Read-only helper for the A21 RVA memory dump used by the sealed-treasure study."""

from __future__ import annotations

import argparse
import hashlib
import re
import struct
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs


DUMP = Path(
    r"E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin"
)
EXPECTED_SHA256 = "6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507"
IMAGE_BASE = 0x00400000


def decode_obfuscated_utf16(data: bytes, offset: int) -> str:
    """Decode the client string form used by prior A21 evidence scripts."""
    if offset + 4 > len(data):
        return ""
    seed = data[offset + 1] & 0xFE
    byte_count = (struct.unpack_from("<H", data, offset + 2)[0] ^ ((seed << 7) | seed)) * 2
    if byte_count > 16384 or offset + 4 + byte_count > len(data):
        return ""
    key = seed | 0x9A714CA0
    out = bytearray()
    full_words = byte_count // 4 * 4
    for index in range(0, full_words, 4):
        value = struct.unpack_from("<I", data, offset + 4 + index)[0] ^ key
        out += struct.pack("<I", value)
        key = (key * 0x1003F + value) & 0xFFFFFFFF
    for index in range(full_words, byte_count):
        value = data[offset + 4 + index] ^ (key & 0xFF)
        out.append(value)
        key = (key * 0x107 + value) & 0xFFFFFFFF
    return out.decode("utf-16le", errors="replace").rstrip("\0")


def disassemble(data: bytes, start_rva: int, end_rva: int) -> None:
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.skipdata = True
    print(f"RVA {start_rva:08X}:{end_rva:08X} VA {start_rva + IMAGE_BASE:08X}")
    for insn in md.disasm(data[start_rva:end_rva], start_rva + IMAGE_BASE):
        print(
            f"RVA {insn.address - IMAGE_BASE:08X} VA {insn.address:08X} "
            f"{insn.bytes.hex():<20} {insn.mnemonic:<8} {insn.op_str}"
        )


def string_matches(data: bytes, pattern: re.Pattern[str]) -> list[tuple[int, str, str]]:
    found: list[tuple[int, str, str]] = []
    seen: set[tuple[int, str]] = set()
    for match in re.finditer(rb"[ -~]{5,}", data):
        text = match.group().decode(errors="replace")
        if pattern.search(text):
            seen.add((match.start(), text))
            found.append((match.start(), "ascii", text))
    # All observed encrypted descriptors in this image begin with 00 54.  This is
    # the same bounded candidate walk as the established abyss evidence helper.
    for match in re.finditer(b"\x00\x54", data):
        text = decode_obfuscated_utf16(data, match.start())
        if text and len(text) < 2000 and pattern.search(text):
            key = (match.start(), text)
            if key not in seen:
                seen.add(key)
                found.append((match.start(), "obfuscated-utf16", text))
    return sorted(found)


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("identity")
    strings = sub.add_parser("strings")
    strings.add_argument("pattern")
    decode = sub.add_parser("decode")
    decode.add_argument("offsets", nargs="+")
    disasm = sub.add_parser("disasm")
    disasm.add_argument("start")
    disasm.add_argument("end")
    xrefs = sub.add_parser("xrefs")
    xrefs.add_argument("addresses", nargs="+")
    immediates = sub.add_parser("immediates")
    immediates.add_argument("values", nargs="+")
    args = parser.parse_args()

    data = DUMP.read_bytes()
    digest = hashlib.sha256(data).hexdigest().upper()
    if digest != EXPECTED_SHA256:
        raise SystemExit(f"unexpected dump SHA-256: {digest}")

    if args.command == "identity":
        print(DUMP)
        print("SHA256", digest)
        print("ImageBase", f"0x{IMAGE_BASE:08X}", "Size", len(data))
    elif args.command == "strings":
        pattern = re.compile(args.pattern, re.IGNORECASE)
        for offset, encoding, text in string_matches(data, pattern):
            print(f"RVA {offset:08X} VA {offset + IMAGE_BASE:08X} {encoding} {text!r}")
    elif args.command == "decode":
        for value in args.offsets:
            offset = int(value, 0)
            print(f"RVA {offset:08X} VA {offset + IMAGE_BASE:08X} {decode_obfuscated_utf16(data, offset)!r}")
    elif args.command == "disasm":
        disassemble(data, int(args.start, 0), int(args.end, 0))
    elif args.command == "xrefs":
        for value in args.addresses:
            address = int(value, 0)
            if address < IMAGE_BASE:
                address += IMAGE_BASE
            needle = struct.pack("<I", address)
            position = 0
            print(f"TARGET VA {address:08X} RVA {address - IMAGE_BASE:08X}")
            while True:
                position = data.find(needle, position)
                if position < 0:
                    break
                print(f"absolute xref RVA {position:08X} VA {position + IMAGE_BASE:08X}")
                position += 1
    elif args.command == "immediates":
        for value in args.values:
            number = int(value, 0)
            needle = struct.pack("<I", number)
            position = 0
            print(f"IMMEDIATE {number:#x}")
            while True:
                position = data.find(needle, position)
                if position < 0:
                    break
                print(f"raw match RVA {position:08X} VA {position + IMAGE_BASE:08X}")
                position += 1


if __name__ == "__main__":
    main()
