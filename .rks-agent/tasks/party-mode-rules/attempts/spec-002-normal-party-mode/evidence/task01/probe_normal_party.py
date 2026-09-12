#!/usr/bin/env python3
"""Locate Normal-party labels and immediate references in the fixed A21 client."""

from __future__ import annotations

import argparse
import hashlib
import struct
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
import pefile


EXPECTED_EXE_SHA256 = "CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14"
EXPECTED_DSTR_SHA256 = "60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10"
RESOURCE_TERMS = ("队伍中只剩下", "解散队伍", "退出队伍", "普通地下城")
RESOURCE_IDS = (1126, 1129, 1130, 21254, 21255, 48602)
BYTE_ANCHORS = {
    # Same class: in-party check, exact-one-member branch, then two stored choices.
    0x023F0278: "E8733B5FFF84C00F84A8000000",
    0x023F0285: "8B0D90C9A5036A00E8EE705FFF83F801",
    # Only the second choice sends the empty-body LEAVE_PARTY (0x000D).
    0x023F02C2: "6A006A0DE885C735008BC8E80ECF3500E8F9E33500",
    # The adjacent prompt formats dstr 1126 with the remaining character name.
    0x023F0391: "6866040000E825114D00",
    # Automatic settings builder falls back to category Normal (2), target 0.
    0x0240C043: "6A02E8060A34008BC8E88F1234006A00",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def get_file_resource(pe: pefile.PE, resource_id: int) -> bytes:
    for type_entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if str(type_entry.name or "") != "FILE":
            continue
        for id_entry in type_entry.directory.entries:
            if id_entry.struct.Id != resource_id:
                continue
            language = id_entry.directory.entries[0]
            data = language.data.struct
            return pe.get_data(data.OffsetToData, data.Size)
    raise KeyError(f"FILE/{resource_id} not found")


def decode_sc01(raw: bytes) -> str:
    if raw[:4] != b"SC01":
        raise ValueError(f"unexpected dstr resource signature: {raw[:4]!r}")
    plain_size = struct.unpack_from("<I", raw, 4)[0]
    encrypted = raw[8:]
    key = 0x5819AF17
    plain = bytearray()
    for offset in range(0, len(encrypted) // 4 * 4, 4):
        cipher_word = struct.unpack_from("<I", encrypted, offset)[0]
        plain.extend(struct.pack("<I", cipher_word ^ key))
        key = cipher_word
    return bytes(plain[:plain_size]).decode("utf-8", errors="strict")


def find_push_refs(pe: pefile.PE, value: int) -> list[int]:
    pattern = b"\x68" + struct.pack("<I", value)
    refs: list[int] = []
    for section in pe.sections:
        if not section.IMAGE_SCN_MEM_EXECUTE:
            continue
        blob = section.get_data()
        offset = 0
        while True:
            hit = blob.find(pattern, offset)
            if hit < 0:
                break
            refs.append(
                pe.OPTIONAL_HEADER.ImageBase
                + section.VirtualAddress
                + hit
            )
            offset = hit + 1
    return refs


def emit_reference_window(pe: pefile.PE, reference_va: int) -> None:
    image_base = pe.OPTIONAL_HEADER.ImageBase
    rva = reference_va - image_base
    blob = pe.get_data(rva, 160)
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    print(f"\n## reference window from 0x{reference_va:08X}")
    for instruction in decoder.disasm(blob, reference_va):
        print(
            f"{instruction.address:08X}  "
            f"{instruction.bytes.hex().upper():<24} "
            f"{instruction.mnemonic:<8} {instruction.op_str}"
        )


def verify_byte_anchors(pe: pefile.PE) -> None:
    image_base = pe.OPTIONAL_HEADER.ImageBase
    passed = 0
    for va, expected_hex in BYTE_ANCHORS.items():
        expected = bytes.fromhex(expected_hex)
        actual = pe.get_data(va - image_base, len(expected))
        if actual != expected:
            raise ValueError(
                f"anchor 0x{va:08X} mismatch: "
                f"expected={expected.hex().upper()} actual={actual.hex().upper()}"
            )
        print(f"anchor=0x{va:08X}|status=PASS|bytes={expected.hex().upper()}")
        passed += 1
    print(f"byte_anchors={passed}/{len(BYTE_ANCHORS)} PASS")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", type=Path)
    parser.add_argument("dstr", type=Path)
    parser.add_argument("--disassemble", action="store_true")
    args = parser.parse_args()

    exe_hash = sha256(args.exe)
    dstr_hash = sha256(args.dstr)
    if exe_hash != EXPECTED_EXE_SHA256:
        parser.error(f"unexpected EXE SHA-256: {exe_hash}")
    if dstr_hash != EXPECTED_DSTR_SHA256:
        parser.error(f"unexpected dstr SHA-256: {dstr_hash}")

    print(f"exe_sha256={exe_hash}")
    print(f"dstr_sha256={dstr_hash}")
    dstr_pe = pefile.PE(str(args.dstr), fast_load=False)
    lines = decode_sc01(get_file_resource(dstr_pe, 102)).splitlines()
    for term in RESOURCE_TERMS:
        matches = [line for line in lines if term in line]
        print(f"term={term}|count={len(matches)}")
        for line in matches:
            print(line)

    exe_pe = pefile.PE(str(args.exe), fast_load=False)
    verify_byte_anchors(exe_pe)
    all_refs: list[int] = []
    for resource_id in RESOURCE_IDS:
        refs = find_push_refs(exe_pe, resource_id)
        all_refs.extend(refs)
        rendered = ",".join(f"0x{value:08X}" for value in refs) or "none"
        print(f"push_resource_id={resource_id}|refs={rendered}")
    if args.disassemble:
        for reference_va in sorted(set(all_refs)):
            emit_reference_window(exe_pe, reference_va)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
