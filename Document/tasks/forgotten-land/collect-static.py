"""Read-only bounded probes; no full-image disassembly or retained instruction graph."""
import hashlib
import json
import mmap
import re
import struct
from pathlib import Path

OUT = Path(__file__).parent / "evidence"
OUT.mkdir(exist_ok=True)
PATTERN = re.compile(r"lost.?land|forgotten.?land|lostland|chasing.?monster", re.I)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def strings(path, name):
    hits = []
    with path.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
        for match in re.finditer(rb"[\x20-\x7e]{6,}", data):
            value = match.group().decode("ascii")
            if PATTERN.search(value):
                hits.append({"offset": hex(match.start()), "text": value[:1000]})
                if len(hits) >= 250:
                    raise RuntimeError("Hit budget exceeded; narrow the probe")
    result = {"source": str(path), "sha256": sha(path), "pattern": PATTERN.pattern, "matches": hits}
    (OUT / name).write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(name, "matches", len(hits))


client = Path("E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin")
native = Path("E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r")
strings(client, "client-strings.json")
strings(native, "70-strings.json")
with native.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
    shoff = struct.unpack_from("<I", data, 32)[0]
    stride, count, names_index = struct.unpack_from("<HHH", data, 46)
    sections = [struct.unpack_from("<IIIIIIIIII", data, shoff + i * stride) for i in range(count)]
    names_section = sections[names_index]
    names = data[names_section[4]:names_section[4] + names_section[5]]
    def z(buffer, pos):
        return buffer[pos:buffer.find(b"\0", pos)].decode("utf-8", "replace")
    symbols = next(s for s in sections if z(names, s[0]) == ".symtab")
    strtab = sections[symbols[6]]
    text = data[strtab[4]:strtab[4] + strtab[5]]
    hits, functions = [], set()
    for pos in range(symbols[4], symbols[4] + symbols[5], symbols[9]):
        no, va, size, info, other, sec = struct.unpack_from("<IIIBBH", data, pos)
        if info & 15 != 2:
            continue
        functions.add(va)
        name = z(text, no)
        if PATTERN.search(name):
            hits.append({"va": hex(va), "size": size, "name": name})
    (OUT / "70-symbols.json").write_text(json.dumps({"source": str(native), "sha256": sha(native),
        "pattern": PATTERN.pattern, "distinct_function_addresses": len(functions), "matches": hits}, indent=2), encoding="utf-8")
    print("70 distinct function addresses", len(functions), "matches", len(hits))
