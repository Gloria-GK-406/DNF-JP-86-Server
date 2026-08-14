#!/usr/bin/env python3
"""Rebuild a runnable, fixed-base PE32 from the unpacked DNF memory image.

Unlike rebuild_analysis_pe.py (analysis-only), this tool produces a PE the
Windows loader will map at ImageBase and execute:

  * all original sections are materialized from the RVA memory image (post-unpack
    state), so the mutated CRT / client code is present without running the
    Themida AEP;
  * a fresh ".idat2" section carries an Import Directory the loader can resolve
    in a clean process (the dump's baked-in IAT addresses are stale);
  * a fresh ".chstub" section carries a small entry stub;
  * the entry point is redirected to that stub and directories that reference the
    protection section are neutralized.

Staged candidates (handoff section 10):
  b0    map + resolve one kernel32 import + run stub + ExitProcess(distinctive)
  b0.5  (todo) full 406-import rebuild + orphan slots + TLS + cookie, then exit
  b1    (todo) Strategy C stub: edi=AEP -> call cookie -> jmp 0x04ED6E75
"""

import argparse
import json
import struct
from pathlib import Path

IMAGE_BASE = 0x00400000

# --- optional header field offsets (from optional_offset) ---
OPT_ENTRY = 16
OPT_IMAGE_BASE = 28
OPT_SECTION_ALIGN = 32
OPT_FILE_ALIGN = 36
OPT_SIZE_OF_IMAGE = 56
OPT_SIZE_OF_HEADERS = 60
OPT_CHECKSUM = 64
OPT_NUM_DIRS = 92
OPT_DIR0 = 96  # DataDirectory[0]

DIR_EXPORT = 0
DIR_IMPORT = 1
DIR_RESOURCE = 2
DIR_EXCEPTION = 3
DIR_SECURITY = 4
DIR_BASERELOC = 5
DIR_DEBUG = 6
DIR_TLS = 9
DIR_LOADCFG = 10
DIR_BOUND = 11
DIR_IAT = 12
DIR_DELAY = 13


def _u16(data, off):
    return struct.unpack_from("<H", data, off)[0]


def _u32(data, off):
    return struct.unpack_from("<I", data, off)[0]


def _align_up(value, alignment):
    if alignment == 0 or alignment & (alignment - 1):
        raise ValueError("alignment must be a non-zero power of two")
    return (value + alignment - 1) & ~(alignment - 1)


def _parse_pe32(data):
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("input does not contain an MZ header")
    pe_offset = _u32(data, 0x3C)
    if pe_offset + 24 > len(data) or data[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ValueError("input does not contain a valid PE signature")
    file_header = pe_offset + 4
    if _u16(data, file_header) != 0x14C:
        raise ValueError("only x86 PE files are supported")
    section_count = _u16(data, file_header + 2)
    optional_size = _u16(data, file_header + 16)
    optional_offset = file_header + 20
    if _u16(data, optional_offset) != 0x10B:
        raise ValueError("only PE32 optional headers are supported")
    section_offset = optional_offset + optional_size
    return {
        "pe_offset": pe_offset,
        "file_header": file_header,
        "section_count": section_count,
        "optional_offset": optional_offset,
        "optional_size": optional_size,
        "section_offset": section_offset,
        "entrypoint_rva": _u32(data, optional_offset + OPT_ENTRY),
        "image_base": _u32(data, optional_offset + OPT_IMAGE_BASE),
        "section_alignment": _u32(data, optional_offset + OPT_SECTION_ALIGN),
        "file_alignment": _u32(data, optional_offset + OPT_FILE_ALIGN),
        "size_of_image": _u32(data, optional_offset + OPT_SIZE_OF_IMAGE),
        "size_of_headers": _u32(data, optional_offset + OPT_SIZE_OF_HEADERS),
        "directory_count": _u32(data, optional_offset + OPT_NUM_DIRS),
    }


def _c_string(text):
    return text.encode("ascii") + b"\0"


# Runtime landmarks (handoff docs).
COOKIE_RVA = 0x0213BBBF                 # __security_init_cookie
AEP_VA = 0x06576731                     # original Themida AEP (edi at cookie)
CRT_CONTINUATION_VA = 0x04ED6E75        # mutated CRT resume point (client_return)
TLS_DIR_RVA = 0x06230018                # IMAGE_TLS_DIRECTORY (callbacks empty)
TLS_DIR_SIZE = 0x18

# 9 orphan API slots embedded in protection data (handoff 8.1): (slot_va, dll, func)
ORPHAN_SLOTS = [
    (0x027D3000, "advapi32.dll", "ControlService"),
    (0x027D3430, "shlwapi.dll", "StrStrIW"),
    (0x027D3474, "user32.dll", "SetWindowLongW"),
    (0x027D3490, "user32.dll", "GetWindow"),
    (0x027D34CC, "user32.dll", "GetKeyState"),
    (0x027D351C, "user32.dll", "DispatchMessageW"),
    (0x027D3560, "user32.dll", "SendMessageW"),
    (0x027D356C, "user32.dll", "wsprintfW"),
    (0x027D35F4, "winmm.dll", "timeGetTime"),
]
# msvcp60 data export (basic_string<wchar> vftable): resolve module_base + offset.
MSVCP60_SLOT_VA = 0x04712340
MSVCP60_MODULE = "msvcp60.dll"
MSVCP60_OFFSET = 0x60F0


# --- tiny x86 assembler (fixed-base, absolute addressing) ---
def _push_imm32(v):
    return b"\x68" + struct.pack("<I", v & 0xFFFFFFFF)


def _push_eax():
    return b"\x50"


def _call_abs(slot_va):                 # call dword ptr [imm32]
    return b"\xff\x15" + struct.pack("<I", slot_va & 0xFFFFFFFF)


def _mov_moffs_eax(abs_va):             # mov [imm32], eax
    return b"\xa3" + struct.pack("<I", abs_va & 0xFFFFFFFF)


def _add_eax_imm32(v):
    return b"\x05" + struct.pack("<I", v & 0xFFFFFFFF)


def _mov_edi_imm32(v):
    return b"\xbf" + struct.pack("<I", v & 0xFFFFFFFF)


def _xor_esi_esi():
    return b"\x31\xf6"


def _test_eax_eax():
    return b"\x85\xc0"


def _jz_rel8(off):                      # jz rel8 (short)
    return b"\x74" + struct.pack("<b", off)


def load_iat_runs(csv_path):
    """Return contiguous same-module runs of the 406 IAT slots, excluding the
    single msvcp60 data pointer (handled by the stub)."""
    import csv as _csv
    rows = []
    with open(csv_path, newline="") as f:
        for r in _csv.DictReader(f):
            rows.append((int(r["slot_rva"], 16), r["module"], r["function"]))
    rows.sort()
    runs = []
    cur = None
    for rva, mod, fn in rows:
        if cur and rva == cur["end"] + 4 and mod.lower() == cur["mod"].lower():
            cur["funcs"].append(fn)
            cur["end"] = rva
        else:
            if cur:
                runs.append(cur)
            cur = {"start": rva, "end": rva, "mod": mod, "funcs": [fn]}
    if cur:
        runs.append(cur)
    # drop the msvcp60 data-pointer run (not name-resolvable)
    runs = [r for r in runs if r["mod"].lower() != "msvcp60.dll"]
    return runs


# DLLs excluded from the merged standard imports for B1. Empty by default: the
# client statically imports az.dll!PutRSInfo, and loading ClientHack's Az.dll is
# exactly how B1 gets the server redirect + client-check bypasses (it runs with
# CLIENTHACK_B1_MODE=1 so the host skips the now-pointless anti-debug/IAT repair).
# ijl15.dll is a harmless compatibility stub. Set this to exclude them for a
# ClientHack-free bring-up run.
B1_EXCLUDED_IMPORT_DLLS = set()


def parse_standard_imports(memory, imp_rva, exclude=None):
    """Parse the original standard Import Directory out of the memory image.

    Returns a list of {"dll", "funcs" (names or "#ord"), "firstthunk_rva"} in
    original order, skipping DLLs in `exclude`.
    """
    exclude = {d.lower() for d in (exclude or set())}
    out = []
    idx = 0
    while True:
        oft, tds, fc, nameptr, ft = struct.unpack_from("<IIIII", memory,
                                                        imp_rva + idx * 20)
        if oft == 0 and nameptr == 0 and ft == 0:
            break
        end = memory.index(b"\0", nameptr)
        dll = memory[nameptr:end].decode("ascii", "replace")
        thunk_rva = oft if oft else ft
        funcs = []
        k = 0
        while True:
            t = struct.unpack_from("<I", memory, thunk_rva + k * 4)[0]
            if t == 0:
                break
            if t & 0x80000000:
                funcs.append(f"#{t & 0xFFFF}")
            else:
                ne = memory.index(b"\0", t + 2)
                funcs.append(memory[t + 2:ne].decode("ascii", "replace"))
            k += 1
        if dll.lower() not in exclude:
            out.append({"dll": dll, "funcs": funcs, "firstthunk_rva": ft})
        idx += 1
    return out


def build_import_blob(base_rva, imports):
    """Build an import directory blob to be placed at RVA base_rva.

    imports: list of dicts:
        { "dll": "kernel32.dll",
          "funcs": ["ExitProcess", ...],
          "firstthunk_rva": int or None }
      firstthunk_rva None  -> FirstThunk array is allocated inside this blob
                              (used for the stub's own imports).
      firstthunk_rva given -> FirstThunk points to an existing materialized
                              array (used for the 406 client slots).

    Returns (blob_bytes, ft_slots) where ft_slots[(dll, func)] = absolute VA of
    that function's resolved IAT slot (for the stub to call).
    """
    # Layout: [descriptors][per-dll OFT arrays][per-dll internal FT arrays]
    #         [IMAGE_IMPORT_BY_NAME entries][dll name strings]
    n = len(imports)
    desc_size = (n + 1) * 20
    blob = bytearray()

    # First, reserve descriptor area; fill later.
    blob += b"\0" * desc_size

    # Emit OFT arrays, internal FT arrays, IBN entries, names; track offsets.
    oft_off = {}
    ft_off = {}          # only for internal FT
    ibn_off = {}         # (dll_index, func) -> offset
    name_off = {}

    # IBN + names pool first so OFT/FT can reference them.
    # We'll place OFT/FT arrays, then IBNs, then names. Compute in passes.
    # Pass A: reserve OFT and (internal) FT arrays.
    for i, imp in enumerate(imports):
        cnt = len(imp["funcs"])
        oft_off[i] = len(blob)
        blob += b"\0" * ((cnt + 1) * 4)
        if imp.get("firstthunk_rva") is None:
            ft_off[i] = len(blob)
            blob += b"\0" * ((cnt + 1) * 4)

    # Pass B: emit IBN entries (skip ordinal imports "#N").
    for i, imp in enumerate(imports):
        for fn in imp["funcs"]:
            if fn.startswith("#"):
                continue
            if len(blob) & 1:
                blob += b"\0"
            ibn_off[(i, fn)] = len(blob)
            blob += struct.pack("<H", 0)          # hint
            blob += _c_string(fn)

    # Pass C: emit dll name strings.
    for i, imp in enumerate(imports):
        name_off[i] = len(blob)
        blob += _c_string(imp["dll"])

    # Pass D: fill OFT and internal FT arrays with thunk values.
    def thunk(i, fn):
        if fn.startswith("#"):
            return 0x80000000 | (int(fn[1:]) & 0xFFFF)   # import by ordinal
        return base_rva + ibn_off[(i, fn)]
    for i, imp in enumerate(imports):
        entries = [thunk(i, fn) for fn in imp["funcs"]] + [0]
        struct.pack_into("<%dI" % len(entries), blob, oft_off[i], *entries)
        if i in ft_off:
            struct.pack_into("<%dI" % len(entries), blob, ft_off[i], *entries)

    # Pass E: fill descriptors.
    ft_slots = {}                         # keyed by (descriptor_index, func)
    for i, imp in enumerate(imports):
        if imp.get("firstthunk_rva") is None:
            ft_rva = base_rva + ft_off[i]
        else:
            ft_rva = imp["firstthunk_rva"]
        struct.pack_into("<IIIII", blob, i * 20,
                         base_rva + oft_off[i],   # OriginalFirstThunk
                         0,                        # TimeDateStamp
                         0,                        # ForwarderChain
                         base_rva + name_off[i],   # Name
                         ft_rva)                   # FirstThunk
        for j, fn in enumerate(imp["funcs"]):
            ft_slots[(i, fn)] = IMAGE_BASE + ft_rva + j * 4
    # terminator descriptor already zero.
    return bytes(blob), ft_slots, desc_size


def build_stub_b0(exitprocess_slot_va, exit_code=0x0000B000):
    """push exit_code; call [ExitProcess]; int3."""
    code = bytearray()
    code += _push_imm32(exit_code)
    code += _call_abs(exitprocess_slot_va)
    code += b"\xcc"                                          # int3 (unreached)
    return bytes(code)


def _emit_orphan_resolution(loadlib_va, getproc_va, str_va):
    """Resolve the 9 orphan slots + the msvcp60 data pointer.

    str_va(name) -> VA of a C string for that dll/func name.
    """
    code = bytearray()
    for slot_va, dll, func in ORPHAN_SLOTS:
        code += _push_imm32(str_va(dll))
        code += _call_abs(loadlib_va)         # eax = HMODULE
        code += _push_imm32(str_va(func))
        code += _push_eax()
        code += _call_abs(getproc_va)         # eax = proc
        code += _mov_moffs_eax(slot_va)       # slot = proc
    # msvcp60 data pointer: base + offset (skip write if not loaded)
    code += _push_imm32(str_va(MSVCP60_MODULE))
    code += _call_abs(loadlib_va)             # eax = msvcp60 base or 0
    code += _test_eax_eax()
    code += _jz_rel8(len(_add_eax_imm32(0)) + len(_mov_moffs_eax(0)))
    code += _add_eax_imm32(MSVCP60_OFFSET)
    code += _mov_moffs_eax(MSVCP60_SLOT_VA)
    return bytes(code)


def build_stub_b05(stub_va, ft, str_va, exit_code=0x0000B005):
    """Resolve orphan slots + msvcp60, run cookie, ExitProcess(distinctive)."""
    code = bytearray()
    code += _emit_orphan_resolution(ft[(0, "LoadLibraryA")],
                                    ft[(0, "GetProcAddress")], str_va)
    # call __security_init_cookie (relative)
    call_off = len(code)
    rel = (COOKIE_RVA + IMAGE_BASE) - (stub_va + call_off + 5)
    code += b"\xe8" + struct.pack("<i", rel)
    code += _push_imm32(exit_code)
    code += _call_abs(ft[(0, "ExitProcess")])
    code += b"\xcc"
    return bytes(code)


def build_stub_b1(stub_va, ft, str_va):
    """Strategy C: resolve imports, set edi=AEP, run cookie, jmp CRT continuation."""
    code = bytearray()
    code += _emit_orphan_resolution(ft[(0, "LoadLibraryA")],
                                    ft[(0, "GetProcAddress")], str_va)
    code += _mov_edi_imm32(AEP_VA)            # edi = original AEP (context ptr)
    code += _xor_esi_esi()                    # esi = 0 (matches capture)
    call_off = len(code)
    rel = (COOKIE_RVA + IMAGE_BASE) - (stub_va + call_off + 5)
    code += b"\xe8" + struct.pack("<i", rel)  # call __security_init_cookie
    jmp_off = len(code)
    rel2 = CRT_CONTINUATION_VA - (stub_va + jmp_off + 5)
    code += b"\xe9" + struct.pack("<i", rel2)  # jmp mutated CRT continuation
    return bytes(code)


def rebuild(original_path, memory_path, output_path, stage="b0", iat_csv=None):
    original = Path(original_path).read_bytes()
    memory = Path(memory_path).read_bytes()
    pe = _parse_pe32(original)
    size_of_image = pe["size_of_image"]
    size_of_headers = pe["size_of_headers"]
    file_align = pe["file_alignment"]
    sect_align = pe["section_alignment"]
    opt = pe["optional_offset"]
    if len(memory) != size_of_image:
        raise ValueError(f"memory length {len(memory):#x} != SizeOfImage "
                         f"{size_of_image:#x}")

    # Room for two extra section headers? Expand SizeOfHeaders if needed.
    new_section_count = pe["section_count"] + 2
    hdr_end = pe["section_offset"] + new_section_count * 40
    first_section_vaddr = _u32(original, pe["section_offset"] + 12)
    new_size_of_headers = size_of_headers
    if hdr_end > new_size_of_headers:
        new_size_of_headers = _align_up(hdr_end, file_align)
    if new_size_of_headers > first_section_vaddr:
        raise ValueError(f"expanded headers {new_size_of_headers:#x} overrun the "
                         f"first section VA {first_section_vaddr:#x}")

    rebuilt = bytearray(_align_up(new_size_of_headers, file_align))
    rebuilt[:size_of_headers] = original[:size_of_headers]
    struct.pack_into("<I", rebuilt, opt + OPT_SIZE_OF_HEADERS, new_size_of_headers)
    cursor = len(rebuilt)

    # Materialize original sections.
    for index in range(pe["section_count"]):
        h = pe["section_offset"] + index * 40
        vsize = _u32(original, h + 8)
        vaddr = _u32(original, h + 12)
        orig_raw = _u32(original, h + 16)
        materialized = max(vsize, orig_raw)
        if materialized == 0:
            struct.pack_into("<II", rebuilt, h + 16, 0, 0)
            continue
        if vaddr >= size_of_image:
            raise ValueError("section starts outside SizeOfImage")
        copy_size = min(materialized, size_of_image - vaddr)
        raw_size = _align_up(copy_size, file_align)
        raw_ptr = _align_up(cursor, file_align)
        if raw_ptr > len(rebuilt):
            rebuilt.extend(b"\0" * (raw_ptr - len(rebuilt)))
        rebuilt.extend(b"\0" * raw_size)
        rebuilt[raw_ptr:raw_ptr + copy_size] = memory[vaddr:vaddr + copy_size]
        struct.pack_into("<II", rebuilt, h + 16, raw_size, raw_ptr)
        cursor = raw_ptr + raw_size

    # --- Build new sections (import blob + stub) ---
    idata_va = _align_up(size_of_image, sect_align)
    enable_tls = stage in ("b0.5", "b1")

    if stage == "b0":
        # Descriptor 0: stub's own kernel32 import (internal FT).
        imports = [{"dll": "kernel32.dll", "funcs": ["ExitProcess"],
                    "firstthunk_rva": None}]
    else:
        if iat_csv is None:
            raise ValueError(f"stage {stage} requires --iat-csv")
        runs = load_iat_runs(iat_csv)
        # Descriptor 0: stub imports (internal FT). Then one descriptor per run
        # (external FT = the existing materialized slot arrays).
        imports = [{"dll": "kernel32.dll",
                    "funcs": ["LoadLibraryA", "GetProcAddress", "ExitProcess"],
                    "firstthunk_rva": None}]
        for r in runs:
            imports.append({"dll": r["mod"], "funcs": r["funcs"],
                            "firstthunk_rva": r["start"]})
        if stage == "b1":
            # Merge the original standard imports so the loader loads every DLL
            # the client startup needs (d3d9/fmodex/gdi32/...). Exclude
            # az.dll/ijl15.dll to avoid injecting ClientHack.
            orig_imp_rva = _u32(original, opt + OPT_DIR0 + DIR_IMPORT * 8)
            std = parse_standard_imports(memory, orig_imp_rva,
                                         exclude=B1_EXCLUDED_IMPORT_DLLS)
            imports.extend(std)

    idata_blob, ft_slots, _ = build_import_blob(idata_va, imports)
    import_dir_size = (len(imports) + 1) * 20

    # Append a string pool (dll/func names the stub resolves at runtime).
    idata_blob = bytearray(idata_blob)
    str_rva = {}
    if stage in ("b0.5", "b1"):
        names = set()
        for _slot, dll, func in ORPHAN_SLOTS:
            names.add(dll)
            names.add(func)
        names.add(MSVCP60_MODULE)
        for s in sorted(names):
            str_rva[s] = idata_va + len(idata_blob)
            idata_blob += _c_string(s)
    idata_blob = bytes(idata_blob)

    def str_va(name):
        return IMAGE_BASE + str_rva[name]

    stub_rva = _align_up(idata_va + len(idata_blob), sect_align)
    stub_va = stub_rva  # keep RVA name below; VA passed to relative-code builders
    if stage == "b0":
        stub = build_stub_b0(ft_slots[(0, "ExitProcess")])
    elif stage == "b0.5":
        stub = build_stub_b05(IMAGE_BASE + stub_rva, ft_slots, str_va)
    else:  # b1
        stub = build_stub_b1(IMAGE_BASE + stub_rva, ft_slots, str_va)

    def add_section(name, vaddr, vsize, characteristics, payload):
        nonlocal cursor
        raw_size = _align_up(len(payload), file_align)
        raw_ptr = _align_up(len(rebuilt), file_align)
        if raw_ptr > len(rebuilt):
            rebuilt.extend(b"\0" * (raw_ptr - len(rebuilt)))
        rebuilt.extend(b"\0" * raw_size)
        rebuilt[raw_ptr:raw_ptr + len(payload)] = payload
        cursor = raw_ptr + raw_size
        return {"name": name, "vaddr": vaddr, "vsize": vsize,
                "raw_size": raw_size, "raw_ptr": raw_ptr,
                "characteristics": characteristics}

    sec_idata = add_section(b".idat2", idata_va, len(idata_blob),
                            0xC0000040, idata_blob)   # INIT_DATA|READ|WRITE
    sec_stub = add_section(b".chstub", stub_va, len(stub),
                           0x60000020, stub)          # CODE|EXECUTE|READ

    # Write the two new section headers.
    for slot, sec in enumerate((sec_idata, sec_stub)):
        h = pe["section_offset"] + (pe["section_count"] + slot) * 40
        name = sec["name"][:8].ljust(8, b"\0")
        struct.pack_into("<8sIIIIIIHHI", rebuilt, h,
                         name, sec["vsize"], sec["vaddr"],
                         sec["raw_size"], sec["raw_ptr"],
                         0, 0, 0, 0, sec["characteristics"])

    # File header: bump NumberOfSections.
    struct.pack_into("<H", rebuilt, pe["file_header"] + 2, new_section_count)

    # Optional header updates.
    new_size_of_image = _align_up(stub_va + len(stub), sect_align)
    struct.pack_into("<I", rebuilt, opt + OPT_SIZE_OF_IMAGE, new_size_of_image)
    struct.pack_into("<I", rebuilt, opt + OPT_ENTRY, stub_va)
    struct.pack_into("<I", rebuilt, opt + OPT_CHECKSUM, 0)

    # Data directories: import -> ours; neutralize protection-referencing dirs;
    # keep Resource.
    def set_dir(index, va, size):
        struct.pack_into("<II", rebuilt, opt + OPT_DIR0 + index * 8, va, size)

    set_dir(DIR_IMPORT, idata_va, import_dir_size)
    zero_dirs = [DIR_EXPORT, DIR_EXCEPTION, DIR_SECURITY, DIR_BASERELOC,
                 DIR_DEBUG, DIR_LOADCFG, DIR_BOUND, DIR_IAT, DIR_DELAY]
    if enable_tls:
        # TLS callbacks are empty (verified); the loader only allocates the slot
        # and copies the zero template -- no protection code runs.
        set_dir(DIR_TLS, TLS_DIR_RVA, TLS_DIR_SIZE)
    else:
        zero_dirs.append(DIR_TLS)
    for d in zero_dirs:
        if d < pe["directory_count"]:
            set_dir(d, 0, 0)

    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    Path(output_path).write_bytes(rebuilt)
    return {
        "output": str(output_path),
        "stage": stage,
        "output_size": len(rebuilt),
        "image_base": IMAGE_BASE,
        "old_size_of_image": size_of_image,
        "new_size_of_image": new_size_of_image,
        "entrypoint_rva": stub_va,
        "idata_rva": idata_va,
        "stub_rva": stub_va,
        "section_count": new_section_count,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--original", required=True, type=Path)
    ap.add_argument("--memory", required=True, type=Path)
    ap.add_argument("--output", required=True, type=Path)
    ap.add_argument("--stage", default="b0", choices=["b0", "b0.5", "b1"])
    ap.add_argument("--iat-csv", type=Path,
                    help="DNF-iat.csv (required for b0.5 and b1)")
    args = ap.parse_args()
    print(json.dumps(rebuild(args.original, args.memory, args.output,
                             args.stage, args.iat_csv), indent=2))


if __name__ == "__main__":
    main()
