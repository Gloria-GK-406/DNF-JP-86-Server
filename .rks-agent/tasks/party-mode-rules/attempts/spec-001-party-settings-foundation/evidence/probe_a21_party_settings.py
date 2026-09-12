"""Reproduce the read-only A21 party-settings static evidence windows.

The addresses below are VAs for the pinned 0x00400000 image.  The script emits
identity data, selected disassembly windows, and the exact dstr lines used by
the accompanying evidence record.  It never writes to the client inputs.
"""

from __future__ import annotations

import argparse
import hashlib
import struct
import sys
from pathlib import Path

import pefile
from capstone import CS_ARCH_X86, CS_MODE_32, Cs


IMAGE_BASE = 0x00400000
EXPECTED_EXE_SHA256 = "CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14"
EXPECTED_DSTR_SHA256 = "60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10"
EXPECTED_BYTES = {
    0x009C6469: "6A0C",          # SET_PARTY_INFO type
    0x009C649A: "3BF3",          # title-index zero gate
    0x00C2481D: "6A02",          # Normal category default
    0x019E6F74: "6A00",          # Story category
    0x0240C043: "6A02",          # invalid/missing category fallback
    0x0117210B: "84C074083C01",  # information block for type 0 or 1
    0x011722A2: "66897860",      # info[9] -> party object +0x60
    0x011722C7: "897864",        # info[10..11] -> party object +0x64
    0x02534ED7: "6683786000",    # category 0 label branch
    0x02534EF4: "6683786001",    # category 1 label branch
    0x0240E0E6: "6683786000",    # Story target display branch
    0x0240E210: "6683786001",    # Cycle target display branch
    0x02595B67: "6683786000",    # Story leader-transfer restriction
    0x02720E09: "6683786002",    # Normal-vs-mode-mismatch branch
}
WINDOWS = (
    ("set-party generic edit builder", 0x009C6373, 0x009C6520),
    ("set-party normal default builder", 0x00C24785, 0x00C24880),
    ("set-party story builder", 0x019E6EC0, 0x019E6FA0),
    ("set-party automatic/default builder", 0x0240BF33, 0x0240C070),
    ("set-party response handler", 0x01119990, 0x01119A10),
    ("party-info notification header and type branch", 0x01171FF8, 0x01172117),
    ("party-info information block", 0x01172117, 0x011722F2),
    ("party-info common extra-record trailer", 0x011725E4, 0x011726C0),
    ("party category label selection", 0x02534EC0, 0x02534F20),
    ("story target display branch", 0x0240E0E1, 0x0240E180),
    ("cycle target display branch", 0x0240E20B, 0x0240E2A0),
    ("story leader-transfer restriction", 0x02595B40, 0x02595BC0),
    ("party-mode dungeon-entry mismatch", 0x02720E04, 0x02720E90),
)
RESOURCE_TERMS = (
    "剧情地下城",
    "循环地下城",
    "没有循环地下城区域",
    "无法进入与现在组队模式不符的地下城",
    "在剧情地下城队伍中无法委任队长",
    "组队进入剧情地下城， 可以共享队长的任务经验值",
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def emit_disassembly(exe_path: Path) -> None:
    pe = pefile.PE(str(exe_path), fast_load=True)
    if pe.OPTIONAL_HEADER.ImageBase != IMAGE_BASE:
        raise ValueError(
            f"unexpected image base 0x{pe.OPTIONAL_HEADER.ImageBase:08X}"
        )
    for va, expected_hex in EXPECTED_BYTES.items():
        expected = bytes.fromhex(expected_hex)
        actual = pe.get_data(va - IMAGE_BASE, len(expected))
        if actual != expected:
            raise ValueError(
                f"byte anchor mismatch at VA 0x{va:08X}: "
                f"expected={expected.hex().upper()} actual={actual.hex().upper()}"
            )
    print(f"byte_anchors={len(EXPECTED_BYTES)}/{len(EXPECTED_BYTES)} PASS")
    decoder = Cs(CS_ARCH_X86, CS_MODE_32)
    for label, start_va, end_va in WINDOWS:
        rva = start_va - IMAGE_BASE
        blob = pe.get_data(rva, end_va - start_va)
        print(f"\n## {label}: VA 0x{start_va:08X}..0x{end_va:08X}")
        consumed = 0
        for instruction in decoder.disasm(blob, start_va):
            if instruction.address >= end_va:
                break
            raw = instruction.bytes.hex().upper()
            print(
                f"{instruction.address:08X}  {raw:<24} "
                f"{instruction.mnemonic:<8} {instruction.op_str}"
            )
            consumed = instruction.address + instruction.size - start_va
        print(f"# decoded_bytes={consumed}/{len(blob)}")


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


def emit_dstr(dstr_path: Path) -> None:
    pe = pefile.PE(str(dstr_path), fast_load=False)
    text = decode_sc01(get_file_resource(pe, 102))
    print("\n## dstr FILE/102 selected lines")
    lines = text.splitlines()
    for term in RESOURCE_TERMS:
        matches = [line for line in lines if term in line]
        if not matches:
            raise ValueError(f"required dstr term not found: {term}")
        print(f"term={term}|count={len(matches)}")
        for line in matches:
            print(line)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", type=Path)
    parser.add_argument("dstr", type=Path)
    args = parser.parse_args()
    for path in (args.exe, args.dstr):
        if not path.is_file():
            parser.error(f"input does not exist: {path}")
    exe_sha256 = sha256(args.exe)
    dstr_sha256 = sha256(args.dstr)
    if exe_sha256 != EXPECTED_EXE_SHA256:
        parser.error(f"unexpected exe SHA-256: {exe_sha256}")
    if dstr_sha256 != EXPECTED_DSTR_SHA256:
        parser.error(f"unexpected dstr SHA-256: {dstr_sha256}")
    print(f"python={sys.version.split()[0]}")
    print(f"exe={args.exe.resolve()}|size={args.exe.stat().st_size}|sha256={exe_sha256}")
    print(
        f"dstr={args.dstr.resolve()}|size={args.dstr.stat().st_size}|"
        f"sha256={dstr_sha256}"
    )
    emit_disassembly(args.exe)
    emit_dstr(args.dstr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
