#!/usr/bin/env python3
"""Materialize a fixed-base PE32 memory image into an analysis-only PE file."""

import argparse
import json
import struct
from pathlib import Path


def _u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def _u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def _align_up(value, alignment):
    if alignment == 0 or alignment & (alignment - 1):
        raise ValueError("PE alignment must be a non-zero power of two")
    return (value + alignment - 1) & ~(alignment - 1)


def _parse_pe32(data):
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("input does not contain an MZ header")
    pe_offset = _u32(data, 0x3C)
    if pe_offset + 24 > len(data) or data[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ValueError("input does not contain a valid PE signature")
    file_header = pe_offset + 4
    machine = _u16(data, file_header)
    section_count = _u16(data, file_header + 2)
    optional_size = _u16(data, file_header + 16)
    optional_offset = file_header + 20
    if machine != 0x14C:
        raise ValueError("only x86 PE files are supported")
    if optional_size < 96 or optional_offset + optional_size > len(data):
        raise ValueError("optional header is truncated")
    if _u16(data, optional_offset) != 0x10B:
        raise ValueError("only PE32 optional headers are supported")
    section_offset = optional_offset + optional_size
    if section_offset + section_count * 40 > len(data):
        raise ValueError("section table is truncated")
    return {
        "pe_offset": pe_offset,
        "section_count": section_count,
        "optional_offset": optional_offset,
        "optional_size": optional_size,
        "section_offset": section_offset,
        "entrypoint_rva": _u32(data, optional_offset + 16),
        "image_base": _u32(data, optional_offset + 28),
        "section_alignment": _u32(data, optional_offset + 32),
        "file_alignment": _u32(data, optional_offset + 36),
        "size_of_image": _u32(data, optional_offset + 56),
        "size_of_headers": _u32(data, optional_offset + 60),
        "directory_count": _u32(data, optional_offset + 92),
    }


def rebuild_analysis_pe(original_path, memory_path, output_path,
                        anchor_rva=None, anchor_bytes=None):
    original_path = Path(original_path)
    memory_path = Path(memory_path)
    output_path = Path(output_path)
    original = original_path.read_bytes()
    memory = memory_path.read_bytes()
    pe = _parse_pe32(original)
    size_of_image = pe["size_of_image"]
    size_of_headers = pe["size_of_headers"]
    file_alignment = pe["file_alignment"]
    if len(memory) != size_of_image:
        raise ValueError(
            f"memory image length {len(memory):#x} does not match "
            f"SizeOfImage {size_of_image:#x}"
        )
    section_table_end = pe["section_offset"] + pe["section_count"] * 40
    if size_of_headers < section_table_end or size_of_headers > len(original):
        raise ValueError("SizeOfHeaders does not contain the complete section table")

    rebuilt = bytearray(_align_up(size_of_headers, file_alignment))
    rebuilt[:size_of_headers] = original[:size_of_headers]
    cursor = len(rebuilt)
    sections = []
    for index in range(pe["section_count"]):
        header_offset = pe["section_offset"] + index * 40
        name_bytes = original[header_offset:header_offset + 8]
        name = name_bytes.split(b"\0", 1)[0].decode("ascii", "replace")
        virtual_size = _u32(original, header_offset + 8)
        virtual_address = _u32(original, header_offset + 12)
        original_raw_size = _u32(original, header_offset + 16)
        materialized_size = max(virtual_size, original_raw_size)
        if materialized_size == 0:
            raw_size = 0
            raw_pointer = 0
            copy_size = 0
        else:
            if virtual_address >= size_of_image:
                raise ValueError(f"section {name!r} starts outside SizeOfImage")
            copy_size = min(materialized_size,
                            size_of_image - virtual_address)
            raw_size = _align_up(copy_size, file_alignment)
            raw_pointer = _align_up(cursor, file_alignment)
            if raw_pointer > len(rebuilt):
                rebuilt.extend(b"\0" * (raw_pointer - len(rebuilt)))
            rebuilt.extend(b"\0" * raw_size)
            rebuilt[raw_pointer:raw_pointer + copy_size] = (
                memory[virtual_address:virtual_address + copy_size]
            )
            cursor = raw_pointer + raw_size
        struct.pack_into("<II", rebuilt, header_offset + 16,
                         raw_size, raw_pointer)
        sections.append({
            "index": index,
            "name": name,
            "virtual_address": virtual_address,
            "virtual_size": virtual_size,
            "raw_pointer": raw_pointer,
            "raw_size": raw_size,
            "copied_size": copy_size,
        })

    optional_offset = pe["optional_offset"]
    struct.pack_into("<I", rebuilt, optional_offset + 64, 0)
    if pe["directory_count"] > 4 and pe["optional_size"] >= 96 + 5 * 8:
        rebuilt[optional_offset + 96 + 4 * 8:
                optional_offset + 96 + 5 * 8] = b"\0" * 8
    if anchor_rva is not None:
        if anchor_rva < 0 or anchor_rva >= size_of_image:
            raise ValueError("analysis anchor is outside SizeOfImage")
        if anchor_bytes is not None:
            observed = memory[anchor_rva:anchor_rva + len(anchor_bytes)]
            if observed != anchor_bytes:
                raise ValueError(
                    f"analysis anchor mismatch: expected {anchor_bytes.hex()}, "
                    f"observed {observed.hex()}"
                )
        struct.pack_into("<I", rebuilt, optional_offset + 16, anchor_rva)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(rebuilt)
    return {
        "output": str(output_path),
        "output_size": len(rebuilt),
        "section_count": pe["section_count"],
        "image_base": pe["image_base"],
        "size_of_image": size_of_image,
        "entrypoint_rva": (
            anchor_rva if anchor_rva is not None else pe["entrypoint_rva"]
        ),
        "sections": sections,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", required=True, type=Path)
    parser.add_argument("--memory", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--anchor-rva", type=lambda value: int(value, 0))
    parser.add_argument("--anchor-bytes",
                        help="hex bytes required at --anchor-rva")
    arguments = parser.parse_args()
    anchor_bytes = (
        bytes.fromhex(arguments.anchor_bytes)
        if arguments.anchor_bytes is not None else None
    )
    if anchor_bytes is not None and arguments.anchor_rva is None:
        parser.error("--anchor-bytes requires --anchor-rva")
    summary = rebuild_analysis_pe(
        arguments.original,
        arguments.memory,
        arguments.output,
        arguments.anchor_rva,
        anchor_bytes,
    )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
