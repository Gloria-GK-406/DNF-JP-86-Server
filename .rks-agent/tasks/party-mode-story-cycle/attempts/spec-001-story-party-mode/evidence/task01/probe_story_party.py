#!/usr/bin/env python3
"""Verify fixed A21 Story-party code windows and resource strings."""

from __future__ import annotations

import argparse
import hashlib
import re
import struct
import subprocess
from pathlib import Path

import pefile


EXPECTED_EXE_SHA256 = "DC322D8FB2A8A33FCE31065CEEAC5801ED854F34409190FE257AACF2663306E9"
EXPECTED_DSTR_SHA256 = "60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10"
EXPECTED_IMAGE_BASE = 0x00400000
UPSTREAM_COMMIT = "c58cd2b479b0357cb6ca78887c9d638b080f4638"

UPSTREAM_FILES = {
    "Server/DfoServer/Network/Parsers/Party/SetPartyInfoRequest.cs":
        "1FE9E0FFCF429806E5FF9573ADE0677D573162331631805B010095FEE2E35605",
    "Server/DfoServer/Network/Handlers/PartyHandler.cs":
        "19D4938762568504724C509B783588884B5BFB3179FC58E81F26610F20F2DEC5",
    "Server/DfoServer/Game/Party/Party.cs":
        "5E7179FF7E0BF7D289DDFEDD71EFF49685F35CF1EBBD180BBA0E1839252209C4",
    "Server/DfoServer/Game/Party/PartyManager.cs":
        "F5EB4A596E5B147A5B45B946CCE535E4685D282202A729A2DCEF276803F97073",
    "Server/DfoServer/Network/Builders/Party/PartyInfoNotiBuilder.cs":
        "DE20E3A8AF412F21B8F88529B2A6C2FB903F61242A8126470609020842C85CD3",
}

RESOURCE_LINES = {
    48600: "48600>剧情地下城",
    48601: "48601>循环地下城",
    48605: "48605>没有循环地下城区域。",
    48612: "48612>无法进入与现在组队模式不符的地下城。",
    48613: "48613>在剧情地下城队伍中无法委任队长",
    49275: "49275>组队进入剧情地下城， 可以共享队长的任务经验值。",
}

# Every address is a VA in the fixed image.  The byte window is deliberately
# small enough to identify the branch/call sequence but large enough to reject
# a coincidental opcode match after a rebuild.
BYTE_ANCHORS = {
    "builder_target_zero_gate": (0x019E6E7A, "663973640F851C010000"),
    "builder_header_info0_title": (
        0x019E6EF8,
        "6A006A0CE84F5BD6008BC8E8D862D6006A01E8415BD6008BC8E8CA63D60056",
    ),
    "builder_capacity_and_opaque": (
        0x019E6F34,
        "0FB6535052E8125BD6008BC8E89B63D6008B435C50E8025BD6008BC8E8EB63D600"
        "0FB64B6051E8F15AD6008BC8E87A63D6006A00E8E35A",
    ),
    "builder_story_category_and_target": (
        0x019E6F66,
        "6A00E8E35AD6008BC8E86C63D6006A00E8D55AD6008BC8E85E63D600"
        "8B0DECC9A503E8533247FF50E8BD5AD6008BC8E87663D600",
    ),
    "submit_story_branch_target": (
        0x009C616B,
        "395F4475418B0DECC9A503E8654049008985C4F7FFFF3BC30F8EA3",
    ),
    "submit_positive_target_opaque_defaults": (
        0x009C6181,
        "3BC30F8EA3000000E822BACDFF8BC8E8BB0BE80189879C0000003BC37D06"
        "899F9C000000C787A000000001000000",
    ),
    "submit_negative_target_reject": (
        0x009C6407,
        "399F9C0000007D3353687E4A0000E8A6B0EF018B4FB083C404506809020000"
        "E845E893018D8DACF7FFFFC745FCFFFFFFFFE8035FB2FFE926020000",
    ),
    "submit_category_and_target_write": (
        0x009C64F6,
        "0FB64F4451E85065D8018BC8E8D96DD8018B95C4F7FFFF52E83D65D801"
        "8BC8E8F66DD801",
    ),
    "consumer_story_category": (0x0240E0E1, "E80A6F5FFF66837860000F85180100"),
    "consumer_target_lookup_compare": (
        0x0240E11A,
        "8B4064508D4DD051E8A93629FE83C4088BCEC645FC06E8BB6E5FFF8B5064"
        "8B0DECC9A50352E89CC0A4FE50E8362F29FE",
    ),
    "consumer_target_progress": (
        0x0240E16E,
        "E87D6E5FFF8B506452E8343A29FE8BC8E84D2B29FE",
    ),
    "progress_target_lookup": (
        0x006A0CF6,
        "8B450885C0790D8B0DECC9A503E8D8947B0085C00F8E96000000508D45E850"
        "E8762E9E008BC8",
    ),
    "target_compare_first_lookup": (
        0x006A10A8,
        "8B450833FF897DF03BC77E16508D45D050E8D22A9E008BC8",
    ),
    "target_compare_second_lookup": (
        0x006A1182,
        "8B450C3BC77E16508D45C850E8FD299E008BC8E836FB9D00",
    ),
    "story_leader_transfer_block": (
        0x02595B51,
        "8B0D80C9A503E8B4E144FF85C074338BC8E889F446FF668378600075256A00"
        "68E5BD0000E846B932008B0DB8C9A50383C404506809020000E8E2F0D6FFE9",
    ),
    "non_story_leader_transfer_send": (
        0x02595BBB,
        "6A006A79E88C6E1B008BC8E815761B0053E87F6E1B008BC8E808771B00",
    ),
    "quest_mutation_story_refresh": (
        0x00E76790,
        "8BCEE859E8B800668378600075078BCEE8CB06B700",
    ),
    "party_notification_refresh": (
        0x011724C7,
        "8BCEE8222B89000FB68DDBFEFFFF394848741080BDD7FEFFFF0074078BCEE886498700",
    ),
    "entry_non_normal_mismatch": (
        0x02720DF9,
        "E8A22F2CFF85C074278BC8E8E7412EFF6683786002741968E4BD0000E8A6061A00506800020000",
    ),
}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def sha256_file(path: Path) -> str:
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


def decode_dstr(raw: bytes) -> list[str]:
    if raw[:4] != b"SC01":
        raise ValueError(f"unexpected dstr signature: {raw[:4]!r}")
    plain_size = struct.unpack_from("<I", raw, 4)[0]
    encrypted = raw[8:]
    key = 0x5819AF17
    plain = bytearray()
    for offset in range(0, len(encrypted) // 4 * 4, 4):
        cipher_word = struct.unpack_from("<I", encrypted, offset)[0]
        plain.extend(struct.pack("<I", cipher_word ^ key))
        key = cipher_word
    return bytes(plain[:plain_size]).decode("utf-8", errors="strict").splitlines()


def verify_upstream(repository: Path) -> None:
    resolved = subprocess.check_output(
        ["git", "-C", str(repository), "rev-parse", UPSTREAM_COMMIT], text=True
    ).strip()
    if resolved != UPSTREAM_COMMIT:
        raise ValueError(f"unexpected upstream commit resolution: {resolved}")
    for path, expected in UPSTREAM_FILES.items():
        content = subprocess.check_output(
            ["git", "-C", str(repository), "show", f"{UPSTREAM_COMMIT}:{path}"]
        )
        actual = sha256_bytes(content)
        if actual != expected:
            raise ValueError(
                f"upstream mismatch {path}: expected={expected} actual={actual}"
            )
        print(
            f"upstream={path}|bytes={len(content)}|sha256={actual}|status=PASS"
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("dstr", type=Path)
    parser.add_argument("--upstream", type=Path, required=True)
    args = parser.parse_args()

    exe_hash = sha256_file(args.exe)
    dstr_hash = sha256_file(args.dstr)
    if exe_hash != EXPECTED_EXE_SHA256:
        parser.error(f"unexpected EXE SHA-256: {exe_hash}")
    if dstr_hash != EXPECTED_DSTR_SHA256:
        parser.error(f"unexpected dstr SHA-256: {dstr_hash}")

    exe = pefile.PE(str(args.exe), fast_load=False)
    if exe.OPTIONAL_HEADER.ImageBase != EXPECTED_IMAGE_BASE:
        parser.error(
            f"unexpected ImageBase: 0x{exe.OPTIONAL_HEADER.ImageBase:08X}"
        )

    print(f"exe_sha256={exe_hash}|image_base=0x{EXPECTED_IMAGE_BASE:08X}|status=PASS")
    print(f"dstr_sha256={dstr_hash}|status=PASS")

    lines = decode_dstr(get_file_resource(pefile.PE(str(args.dstr)), 102))
    line_by_id: dict[int, str] = {}
    for line in lines:
        match = re.match(r"^(\d+)>", line)
        if match is not None:
            line_by_id[int(match.group(1))] = line
    for resource_id, expected in RESOURCE_LINES.items():
        actual = line_by_id.get(resource_id)
        if actual != expected:
            raise ValueError(
                f"resource {resource_id} mismatch: expected={expected!r} actual={actual!r}"
            )
        print(f"resource={resource_id}|text={actual}|status=PASS")

    for label, (va, expected_hex) in BYTE_ANCHORS.items():
        expected = bytes.fromhex(expected_hex)
        actual = exe.get_data(va - EXPECTED_IMAGE_BASE, len(expected))
        if actual != expected:
            raise ValueError(
                f"anchor {label} at 0x{va:08X} mismatch: "
                f"expected={expected.hex().upper()} actual={actual.hex().upper()}"
            )
        print(
            f"anchor={label}|va=0x{va:08X}|bytes={len(expected)}|"
            f"sha256={sha256_bytes(actual)}|status=PASS"
        )

    verify_upstream(args.upstream)
    print(
        f"SUMMARY: anchors={len(BYTE_ANCHORS)}/{len(BYTE_ANCHORS)} "
        f"resources={len(RESOURCE_LINES)}/{len(RESOURCE_LINES)} "
        f"upstream={len(UPSTREAM_FILES)}/{len(UPSTREAM_FILES)} PASS"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
