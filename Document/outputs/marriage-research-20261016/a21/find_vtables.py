from __future__ import annotations

import hashlib
import struct
from pathlib import Path


DUMP = Path(r"E:\Project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260906-224825-165-31384\DNF-memory-rva.bin")
IMAGE_BASE = 0x00400000
OUT = Path(__file__).with_name("vtable-xrefs.txt")

TARGETS = {
    "03F0-A direct caller/UI callback": 0x0088B040,
    "03F0-B direct caller/UI callback": 0x0088B3F0,
    "03F0-C direct caller/UI callback": 0x0088EF30,
    "03F1 UI dispatcher": 0x0088A200,
    "03F7 builder": 0x008955D0,
}


def u32(data: bytes, rva: int) -> int | None:
    if not (0 <= rva <= len(data) - 4):
        return None
    return struct.unpack_from("<I", data, rva)[0]


def va_to_rva(va: int) -> int:
    return va - IMAGE_BASE


def type_name_from_vtable(data: bytes, vtable_rva: int) -> str | None:
    col_va = u32(data, vtable_rva - 4)
    if col_va is None:
        return None
    col_rva = va_to_rva(col_va)
    if not (0 <= col_rva <= len(data) - 20):
        return None
    signature, _offset, _cd_offset, type_desc_va, _hier_va = struct.unpack_from("<IIIII", data, col_rva)
    if signature not in (0, 1):
        return None
    type_desc_rva = va_to_rva(type_desc_va)
    if not (0 <= type_desc_rva + 8 < len(data)):
        return None
    raw = data[type_desc_rva + 8 : type_desc_rva + 8 + 160]
    name = raw.split(b"\0", 1)[0]
    if not name.startswith(b".?"):
        return None
    return name.decode("ascii", "replace")


def main() -> None:
    data = DUMP.read_bytes()
    rows = [
        f"dump={DUMP}",
        f"dump_sha256={hashlib.sha256(data).hexdigest().upper()}",
        f"image_base={IMAGE_BASE:08X}",
        "Pointer hits are searched as little-endian VAs. RTTI names are reported only when a valid MSVC complete-object-locator precedes a candidate vftable.",
        "",
    ]
    for label, target_rva in TARGETS.items():
        target_va = IMAGE_BASE + target_rva
        needle = struct.pack("<I", target_va)
        hits: list[int] = []
        start = 0
        while True:
            hit = data.find(needle, start)
            if hit < 0:
                break
            hits.append(hit)
            start = hit + 1
        rows.append(f"===== {label}: target RVA {target_rva:08X} VA {target_va:08X}; pointer_hits={len(hits)} =====")
        for hit in hits:
            found = []
            # The hit may be any slot, so try nearby 4-byte aligned addresses as candidate starts.
            for candidate in range(max(4, hit - 0x100), hit + 1, 4):
                name = type_name_from_vtable(data, candidate)
                if name:
                    found.append((candidate, name, (hit - candidate) // 4))
            if found:
                for table_rva, name, slot in found:
                    rows.append(
                        f"pointer RVA {hit:08X} VA {IMAGE_BASE + hit:08X}; "
                        f"vftable RVA {table_rva:08X} VA {IMAGE_BASE + table_rva:08X}; slot={slot}; RTTI={name}"
                    )
            else:
                rows.append(f"pointer RVA {hit:08X} VA {IMAGE_BASE + hit:08X}; no nearby validated RTTI vftable")
        rows.append("")
    OUT.write_text("\n".join(rows), encoding="utf-8", newline="\n")
    print(OUT)


if __name__ == "__main__":
    main()
