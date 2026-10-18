"""Bounded ASCII/symbol scan of the designated level-70 df_game_r binary."""

from __future__ import annotations

import hashlib
import re
import struct
from pathlib import Path


BINARY = Path(r"E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r")
PATTERN = re.compile(
    r"marri|wedding|spouse|couple|proposal|propose|engage|divorc|ceremon|(?:^|_)lover(?:$|_)|love.?point|magadha",
    re.IGNORECASE,
)


def c_string(data: bytes, offset: int) -> str:
    end = data.find(b"\0", offset)
    if end < 0:
        end = len(data)
    return data[offset:end].decode("utf-8", "replace")


def main() -> None:
    data = BINARY.read_bytes()
    section_offset = struct.unpack_from("<I", data, 32)[0]
    section_entry_size, section_count, section_name_index = struct.unpack_from("<HHH", data, 46)
    sections = [
        struct.unpack_from("<IIIIIIIIII", data, section_offset + index * section_entry_size)
        for index in range(section_count)
    ]
    section_name_table = sections[section_name_index]
    section_names = data[
        section_name_table[4] : section_name_table[4] + section_name_table[5]
    ]
    symbol_table = next(
        section for section in sections if c_string(section_names, section[0]) == ".symtab"
    )
    string_table = sections[symbol_table[6]]
    symbol_names = data[string_table[4] : string_table[4] + string_table[5]]

    functions: list[tuple[int, str, int]] = []
    for offset in range(
        symbol_table[4], symbol_table[4] + symbol_table[5], symbol_table[9]
    ):
        name_offset, value, size, info, _other, _section = struct.unpack_from(
            "<IIIBBH", data, offset
        )
        if info & 15 == 2:
            functions.append((value, c_string(symbol_names, name_offset), size))

    symbol_matches = [entry for entry in functions if PATTERN.search(entry[1])]
    ascii_matches = []
    for hit in re.finditer(rb"[\x20-\x7e]{6,}", data):
        value = hit.group().decode("ascii")
        if PATTERN.search(value):
            ascii_matches.append((hit.start(), value))

    print(f"sha256={hashlib.sha256(data).hexdigest().upper()}")
    print(f"function_symbol_entries={len(functions)}")
    print(f"unique_function_addresses={len({entry[0] for entry in functions})}")
    print(f"pattern={PATTERN.pattern}")
    print(f"symbol_matches={len(symbol_matches)}")
    for address, name, size in symbol_matches:
        print(f"symbol 0x{address:08X} size={size} {name}")
    print(f"ascii_matches={len(ascii_matches)}")
    for offset, value in ascii_matches:
        print(f"ascii 0x{offset:08X} {value}")


if __name__ == "__main__":
    main()
