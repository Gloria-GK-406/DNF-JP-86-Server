"""Read-only bounded A21 string/xref and TW70 symbol evidence. No whole-image disassembly."""
import hashlib
import ctypes
from ctypes import wintypes
import json
import mmap
import re
import struct
import sys
from pathlib import Path

CLIENT = Path(r"E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260922-194214-883-23144\DNF-memory-rva.bin")
SERVER70 = Path(r"E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r")

class MemoryCounters(ctypes.Structure):
    _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD)] + [
        (name, ctypes.c_size_t) for name in ("PeakWorkingSetSize", "WorkingSetSize",
        "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
        "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage", "PrivateUsage")]

def memory_guard():
    counters = MemoryCounters()
    counters.cb = ctypes.sizeof(counters)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    api = ctypes.WinDLL("psapi", use_last_error=True).GetProcessMemoryInfo
    api.argtypes = [wintypes.HANDLE, ctypes.POINTER(MemoryCounters), wintypes.DWORD]
    if not api(kernel.GetCurrentProcess(), ctypes.byref(counters), counters.cb):
        raise ctypes.WinError(ctypes.get_last_error())
    if max(counters.PrivateUsage, counters.WorkingSetSize) > 512 * 1024 * 1024:
        raise MemoryError("512 MiB process private-bytes/working-set abort threshold exceeded")
    return {"private_bytes": counters.PrivateUsage, "working_set": counters.WorkingSetSize,
        "peak_working_set": counters.PeakWorkingSetSize}

def identity(path):
    with path.open("rb") as stream:
        return {"path": str(path), "size": path.stat().st_size, "sha256": hashlib.file_digest(stream, "sha256").hexdigest()}

def positions(data, needle, limit=40):
    found = 0
    chunk_size = 4*1024*1024
    pattern = re.compile(re.escape(needle))
    for start in range(0, len(data), chunk_size):
        chunk = data[start:min(len(data), start+chunk_size+len(needle)-1)]
        for match in pattern.finditer(chunk):
            if match.start() >= chunk_size:
                break
            yield start+match.start()
            found += 1
            if found >= limit:
                return

print(json.dumps({"mode": sys.argv[1], "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": memory_guard()}))
if sys.argv[1] == "client":
    print(json.dumps(identity(CLIENT)))
    with CLIENT.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
        for text in ("[linked dungeon]", "[on clear add passive object]", "[next]", "circus", "CIRCUS", "LINKED_DUNGEON", "LINK_DUNGEON", "RingRing"):
            memory_guard()
            for encoding in ("ascii", "utf-16le"):
                for offset in positions(data, text.encode(encoding)):
                    va = offset + 0x400000
                    refs = [hex(ref + 0x400000) for ref in positions(data, struct.pack("<I", va))]
                    print(json.dumps({"text": text, "encoding": encoding, "va": hex(va), "absolute_xrefs": refs}))
        if len(sys.argv) > 2:
            from capstone import Cs, CS_ARCH_X86, CS_MODE_32
            md = Cs(CS_ARCH_X86, CS_MODE_32)
            for value in sys.argv[2:]:
                memory_guard()
                if value.startswith("literal:"):
                    va = int(value.split(":")[1], 0)
                    offset = va-0x400000
                    header = data[offset:offset+4]
                    if struct.unpack_from("<H", header)[0] == 0x2CA1:
                        decoded = data[offset+4:offset+4+4096].decode("utf-16le").split("\0")[0]
                    else:
                        seed = header[1] & 0xFE
                        length = (((seed << 7) | seed) ^ struct.unpack_from("<H", header, 2)[0])*2
                        if not 0 < length <= 8192:
                            raise ValueError("Literal decode limited to 8192 bytes")
                        raw = data[offset+4:offset+4+length]
                        plain = bytearray()
                        key = seed | 0x9A714CA0
                        for index in range(length//4):
                            word = struct.unpack_from("<I", raw, index*4)[0] ^ key
                            plain.extend(struct.pack("<I", word))
                            key = (key*0x1003F+word) & 0xFFFFFFFF
                        for byte in raw[length//4*4:]:
                            char = byte ^ (key & 255)
                            plain.append(char)
                            key = (key*0x107+char) & 0xFFFFFFFF
                        decoded = plain.decode("utf-16le").rstrip("\0")
                    print(json.dumps({"literal_va": hex(va), "text": decoded}, ensure_ascii=False))
                    continue
                if value.startswith("callers:"):
                    target = int(value.split(":")[1], 0)
                    matches = []
                    for offset in positions(data, b"\xe8", 3000000):
                        if len(matches) >= 64 or offset+5 > len(data):
                            break
                        destination = (0x400000+offset+5+struct.unpack_from("<i", data, offset+1)[0]) & 0xFFFFFFFF
                        if destination == target:
                            matches.append(hex(0x400000+offset))
                    print(json.dumps({"target": hex(target), "raw_rel32_call_candidates": matches,
                        "limit": 64, "requires_instruction_validation": True}))
                    continue
                if value.startswith("memrefs:"):
                    displacement = int(value.split(":")[1], 0)
                    md.detail = True
                    matches = []
                    for offset in positions(data, struct.pack("<i", displacement), 128):
                        if offset < 0x1000 or offset > 0x02C00000:
                            continue
                        for prefix in (2, 3, 4):
                            start = offset-prefix
                            ins = next(md.disasm(data[start:start+15], start+0x400000), None)
                            if ins and any(op.type == 3 and op.mem.disp == displacement for op in ins.operands):
                                matches.append(f"{ins.address:08x}: {ins.mnemonic} {ins.op_str}")
                                break
                    md.detail = False
                    print(json.dumps({"displacement": hex(displacement), "raw_memory_operand_candidates": matches,
                        "limit": 128, "requires_instruction_validation": True}))
                    continue
                if value.startswith("words:") or value.startswith("rtti:"):
                    parts = value.split(":")
                    va = int(parts[1], 0)
                    count = int(parts[2], 0) if len(parts) > 2 else 8
                    if not (1 <= count <= 128):
                        raise ValueError("Word reads limited to 128 DWORDs")
                    def u32(address):
                        if not (0x400000 <= address <= len(data)+0x400000-4):
                            raise ValueError(f"Pointer outside mapped image: {address:#x}")
                        return struct.unpack_from("<I", data, address-0x400000)[0]
                    if value.startswith("rtti:"):
                        locator = u32(va-4)
                        descriptor = u32(locator+12)
                        start = descriptor+8-0x400000
                        print(json.dumps({"vtable": hex(va), "locator": hex(locator), "descriptor": hex(descriptor),
                            "name": data[start:start+256].split(b"\0")[0].decode("ascii", "replace")}))
                    else:
                        print(json.dumps({"va": hex(va), "words": [hex(u32(va+i*4)) for i in range(count)]}))
                    continue
                if value.startswith("xref:"):
                    address = int(value.split(":")[1], 0)
                    limit = int(value.split(":")[2], 0) if len(value.split(":")) > 2 else 40
                    if not 1 <= limit <= 256:
                        raise ValueError("Xref limit must remain within 1..256")
                    print(json.dumps({"address": hex(address), "absolute_refs": [hex(pos+0x400000)
                        for pos in positions(data, struct.pack("<I", address), limit)]}))
                    continue
                if value.startswith("field:"):
                    _, start, end, displacement = value.split(":")
                    start, end, displacement = int(start, 0), int(end, 0), int(displacement, 0)
                    if end-start > 65536:
                        raise ValueError("Field search limited to 64 KiB")
                    md.detail = True
                    for ins in md.disasm(data[start-0x400000:end-0x400000], start):
                        if any(operand.type == 3 and operand.mem.disp == displacement for operand in ins.operands):
                            print(f"{ins.address:08x}: {ins.mnemonic} {ins.op_str}")
                    md.detail = False
                    continue
                va, size = (int(part, 0) for part in value.split(":"))
                if size > 4096:
                    raise ValueError("Explicit disassembly is limited to 4096 bytes per range")
                print("\nRANGE", hex(va), size)
                for ins in md.disasm(data[va-0x400000:va-0x400000+size], va):
                    print(f"{ins.address:08x}: {ins.mnemonic} {ins.op_str}")
else:
    print(json.dumps(identity(SERVER70)))
    with SERVER70.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
        off = struct.unpack_from("<I", data, 32)[0]
        size, count = struct.unpack_from("<HH", data, 46)
        sections = [struct.unpack_from("<IIIIIIIIII", data, off+i*size) for i in range(count)]
        sym = next(section for section in sections if section[1] == 2)
        strings_section = sections[sym[6]]
        strings = data[strings_section[4]:strings_section[4]+strings_section[5]]
        patterns = ("circus", "pierrot", "linked", "linkdungeon")
        found = 0
        for pos in range(sym[4], sym[4]+sym[5], sym[9]):
            if (pos-sym[4]) % (sym[9]*16384) == 0:
                memory_guard()
            name, va, length, *_ = struct.unpack_from("<IIIBBH", data, pos)
            text = strings[name:strings.find(b"\0", name)].decode("utf8", "replace")
            if any(pattern in text.lower() for pattern in patterns):
                print(json.dumps({"symbol": text, "va": hex(va), "length": length}))
                found += 1
        print(json.dumps({"patterns": patterns, "matching_symbols": found, "limitation": "absence of named symbols is not proof of absence of equivalent behavior"}))
print(json.dumps({"memory_final": memory_guard()}))
