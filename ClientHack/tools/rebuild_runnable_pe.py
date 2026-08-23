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
import csv
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


# Runtime landmarks, per client (handoff docs).
#
# Every one of these is a property of ONE packed DNF.exe. Selecting the wrong
# profile produces a PE that jumps into unrelated code, so --client is required
# to match the binary being rebuilt; the identity is checked against the PE's
# own SizeOfImage/entry point before anything is written.
#
# How each is obtained:
#   cookie_rva    __security_init_cookie -- the point at which the protector has
#                 finished unpacking. Same five-byte prologue on both clients.
#   aep_va        the packed PE's own entry point + IMAGE_BASE. Confirmed at
#                 runtime: edi holds exactly this value when the startup gate
#                 intercepts the cookie call.
#   crt_cont_va   where the mutated CRT resumes after the cookie call. NOT
#                 reverse-engineered: it is the return address on the client
#                 stack at the gate intercept, reported as "client_return" by
#                 the host's startup trace (CLIENTHACK_STARTUP_TRACE=1).
#   tls_dir_rva   IMAGE_TLS_DIRECTORY, read from the PE's TLS data directory.
#                 On both clients it sits inside the dropped container section,
#                 so the descriptor is copied into .idat2 (callbacks empty).
TLS_DIR_SIZE = 0x18

CLIENTS = {
    "a12": {
        "size_of_image": 0x06693000,
        "cookie_rva": 0x0213BBBF,
        "aep_va": 0x06576731,
        "crt_cont_va": 0x04ED6E75,
        "tls_dir_rva": 0x06230018,
        "reloc_csv": "crossmodule-relocs.csv",
        # msvcp60's run is a data pointer, not name-resolvable as a function;
        # the stub resolves it through ORPHAN_SLOTS instead.
        "iat_drop_modules": {"msvcp60.dll"},
    },
    "a21": {
        "size_of_image": 0x0746E000,
        "cookie_rva": 0x028865DE,
        "aep_va": 0x07613FBC,
        "crt_cont_va": 0x05D06329,
        "tls_dir_rva": 0x06A673D8,
        "reloc_csv": "crossmodule-relocs-a21.csv",
        # This client's IAT is kernel32/msvcr71/ws2_32 only -- no data-pointer
        # run to carve out.
        "iat_drop_modules": set(),
    },
}

# Filled by select_client(); the a12 values are the defaults so that importing
# this module without choosing keeps the historical behaviour.
COOKIE_RVA = CLIENTS["a12"]["cookie_rva"]
AEP_VA = CLIENTS["a12"]["aep_va"]
CRT_CONTINUATION_VA = CLIENTS["a12"]["crt_cont_va"]
TLS_DIR_RVA = CLIENTS["a12"]["tls_dir_rva"]
IAT_DROP_MODULES = CLIENTS["a12"]["iat_drop_modules"]
DEFAULT_RELOC_CSV = CLIENTS["a12"]["reloc_csv"]
CLIENT_NAME = "a12"

# Cross-module fixup slots: pointers baked into the dump that the loader never
# re-resolves, because they are NOT declared imports of the rebuilt PE.
#
# Measured by differential analysis over three dumps -- see
# Docs/differential-crossmodule-fixups.md. The full verified surface is 472
# sites; 449 of them are covered by the declared import runs, leaving these 24.
# Every one resolves to a NAMED export, so this table is portable across
# Windows/DLL versions (no module_base + offset guesswork).
#
#   0x027D3xxx  (.code)  9 isolated API slots embedded in protection data
#   0x047122xx  (.tls)   msvcp60 C++ vftable / static-data exports used by the
#                        C++ static constructors _initterm runs -- these were the
#                        PRIMARY startup fault the B1 guard only saw second-hand
#   0x047124xx  (.tls)   2 further isolated user32 slots
#   0x04EEE12x  (.,_W)   3-entry TLS helper array the mutated CRT itself uses
#
# S4A21 has no counterpart list, and that is a decision rather than an omission.
# Orphan slots exist to cover sites the relocation table missed, and the S4A12
# table was built from a SAME-BOOT dump pair that could not see KnownDLL
# pointers at all. The S4A21 table is built from a cross-reboot pair with
# same-boot refinement, so every site that moves is already in it; adding a
# stub-resolved slot on top would apply a base delta to an already-correct
# value, which is precisely the double-application fault that made an early
# S4A12 table crash. If a site ever turns out to need name resolution here,
# it must simultaneously be excluded from the relocation table.
ORPHAN_SLOTS_A12 = [
    (0x027D3000, "advapi32.dll", "ControlService"),
    (0x027D3430, "shlwapi.dll", "StrStrIW"),
    (0x027D3474, "user32.dll", "SetWindowLongW"),
    (0x027D3490, "user32.dll", "GetWindow"),
    (0x027D34CC, "user32.dll", "GetKeyState"),
    (0x027D351C, "user32.dll", "DispatchMessageW"),
    (0x027D3560, "user32.dll", "SendMessageW"),
    (0x027D356C, "user32.dll", "wsprintfW"),
    (0x027D35F4, "winmm.dll", "timeGetTime"),
    (0x04712280, "msvcp60.dll", "??_8?$basic_ostringstream@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@7B@"),
    (0x04712288, "msvcp60.dll", "??_7?$basic_ios@DU?$char_traits@D@std@@@std@@6B@"),
    (0x04712294, "msvcp60.dll", "??_7?$basic_ostringstream@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@6B@"),
    (0x047122A4, "msvcp60.dll", "??_7?$basic_stringbuf@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@6B@"),
    (0x047122AC, "msvcp60.dll", "??_7?$basic_streambuf@DU?$char_traits@D@std@@@std@@6B@"),
    (0x047122B4, "msvcp60.dll", "??_7?$basic_ostream@DU?$char_traits@D@std@@@std@@6B@"),
    (0x047122FC, "msvcp60.dll", "?npos@?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@2IB"),
    (0x04712318, "msvcp60.dll", "?_C@?1??_Nullstr@?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@CAPBDXZ@4DB"),
    (0x0471231C, "msvcp60.dll", "?npos@?$basic_string@GU?$char_traits@G@std@@V?$allocator@G@2@@std@@2IB"),
    (0x04712484, "user32.dll", "GetParent"),
    (0x047124BC, "user32.dll", "IsWindowVisible"),
    (0x04EEE124, "kernel32.dll", "TlsGetValue"),
    (0x04EEE128, "kernel32.dll", "TlsSetValue"),
    (0x04EEE12C, "kernel32.dll", "TlsFree"),
    (0x04712340, "msvcp60.dll", "?_C@?1??_Nullstr@?$basic_string@GU?$char_traits@G@std@@V?$allocator@G@2@@std@@CAPBGXZ@4GB"),
]

ORPHAN_SLOTS_BY_CLIENT = {"a12": ORPHAN_SLOTS_A12, "a21": []}
ORPHAN_SLOTS = ORPHAN_SLOTS_A12


def select_client(name):
    """Point every client-specific landmark at one client's profile."""
    global COOKIE_RVA, AEP_VA, CRT_CONTINUATION_VA, TLS_DIR_RVA
    global ORPHAN_SLOTS, IAT_DROP_MODULES, DEFAULT_RELOC_CSV, CLIENT_NAME
    if name not in CLIENTS:
        raise ValueError("unknown client %r (have: %s)"
                         % (name, ", ".join(sorted(CLIENTS))))
    p = CLIENTS[name]
    COOKIE_RVA = p["cookie_rva"]
    AEP_VA = p["aep_va"]
    CRT_CONTINUATION_VA = p["crt_cont_va"]
    TLS_DIR_RVA = p["tls_dir_rva"]
    IAT_DROP_MODULES = p["iat_drop_modules"]
    DEFAULT_RELOC_CSV = p["reloc_csv"]
    ORPHAN_SLOTS = ORPHAN_SLOTS_BY_CLIENT[name]
    CLIENT_NAME = name


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


def _sub_eax_imm32(v):
    return bytes((0x2D,)) + struct.pack("<I", v & 0xFFFFFFFF)


def _mov_ecx_moffs(abs_va):             # mov ecx, dword ptr [imm32]
    return bytes((0x8B, 0x0D)) + struct.pack("<I", abs_va & 0xFFFFFFFF)


def _add_ecx_eax():
    return bytes((0x01, 0xC1))


def _mov_moffs_ecx(abs_va):             # mov dword ptr [imm32], ecx
    return bytes((0x89, 0x0D)) + struct.pack("<I", abs_va & 0xFFFFFFFF)


def _jz_rel32(off):                     # jz rel32 (near)
    return bytes((0x0F, 0x84)) + struct.pack("<i", off)


def load_reloc_table(csv_path):
    """Cross-module relocation sites measured by differential analysis.

    Themida's API wrappers do not store a plain pointer: they store
    ``api_address + K`` for a per-site constant K and undo K at call time.  That
    is invisible to a "does this value point into a module" test, but it is
    perfectly visible to a differential: adding a constant is transparent to
    subtraction, so the value still moves by exactly the module's base delta
    between two dumps.

    The consequence is that K never has to be recovered.  Re-basing the stored
    value by ``current_base - dump_base`` repairs plain and transformed forms
    alike -- this is a relocation table, not an import table.
    """
    rows = []
    with open(csv_path, newline="", encoding="utf-8-sig") as handle:
        for row in csv.DictReader(handle):
            rows.append((int(row["site_rva"], 16), row["module"].strip().lower()))
    return rows


def load_module_bases(csv_path):
    """Module base addresses recorded by 0000_MemoryDump at capture time."""
    bases = {}
    with open(csv_path, newline="", encoding="utf-8-sig") as handle:
        for row in csv.DictReader(handle):
            name = row["name"].strip().strip('"').lower()
            if name == "dnf.exe":
                continue
            bases.setdefault(name, int(row["base_va"], 16))
    return bases


def _emit_crossmodule_relocs(loadlib_va, str_va, relocs, bases):
    """Re-base every recorded cross-module site by its module's load delta.

    A module that fails to load leaves all of its sites untouched: the dumped
    value is what the exe has today, so keeping it cannot be worse.
    """
    by_module = {}
    for site_rva, module in relocs:
        by_module.setdefault(module, []).append(site_rva)
    code = bytearray()
    skipped = []
    for module in sorted(by_module):
        dump_base = bases.get(module)
        if dump_base is None:
            skipped.append(module)
            continue
        body = bytearray()
        body += _sub_eax_imm32(dump_base)          # eax = current_base - dump_base
        for site_rva in sorted(by_module[module]):
            site_va = IMAGE_BASE + site_rva
            body += _mov_ecx_moffs(site_va)
            body += _add_ecx_eax()
            body += _mov_moffs_ecx(site_va)
        code += _push_imm32(str_va(module))
        code += _call_abs(loadlib_va)              # eax = HMODULE or NULL
        code += _test_eax_eax()
        code += _jz_rel32(len(body))               # module missing -> keep sites
        code += body
    return bytes(code), skipped


def load_iat_runs(csv_path):
    """Contiguous same-module runs of the client IAT slots.

    Runs belonging to IAT_DROP_MODULES are left out: their entries are data
    exports, which cannot be declared as imported functions. On S4A12 that is
    the msvcp60 run, which the stub resolves by name instead."""
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
    runs = [r for r in runs if r["mod"].lower() not in IAT_DROP_MODULES]
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
    """Resolve every cross-module fixup slot by name.

    str_va(name) -> VA of a C string for that dll/func name.

    A failed lookup leaves the slot at its dumped value instead of writing NULL:
    a stale pointer is what the exe has today, a NULL one faults immediately.
    """
    code = bytearray()
    for slot_va, dll, func in ORPHAN_SLOTS:
        tail = (_push_imm32(0) + _push_eax() + _call_abs(getproc_va) +
                _test_eax_eax() + _jz_rel8(0) + _mov_moffs_eax(0))
        code += _push_imm32(str_va(dll))
        code += _call_abs(loadlib_va)         # eax = HMODULE or NULL
        code += _test_eax_eax()
        code += _jz_rel8(len(tail))           # module missing -> keep slot
        code += _push_imm32(str_va(func))
        code += _push_eax()
        code += _call_abs(getproc_va)         # eax = export address or NULL
        code += _test_eax_eax()
        code += _jz_rel8(len(_mov_moffs_eax(0)))   # export missing -> keep slot
        code += _mov_moffs_eax(slot_va)       # slot = resolved address
    return bytes(code)


def build_stub_b05(stub_va, ft, str_va, reloc_code=b"", exit_code=0x0000B005):
    """Resolve orphan slots + msvcp60, run cookie, ExitProcess(distinctive)."""
    code = bytearray()
    code += _emit_orphan_resolution(ft[(0, "LoadLibraryA")],
                                    ft[(0, "GetProcAddress")], str_va)
    code += reloc_code
    # call __security_init_cookie (relative)
    call_off = len(code)
    rel = (COOKIE_RVA + IMAGE_BASE) - (stub_va + call_off + 5)
    code += b"\xe8" + struct.pack("<i", rel)
    code += _push_imm32(exit_code)
    code += _call_abs(ft[(0, "ExitProcess")])
    code += b"\xcc"
    return bytes(code)


def build_stub_b1(stub_va, ft, str_va, reloc_code=b""):
    """Strategy C: resolve imports, set edi=AEP, run cookie, jmp CRT continuation."""
    code = bytearray()
    code += _emit_orphan_resolution(ft[(0, "LoadLibraryA")],
                                    ft[(0, "GetProcAddress")], str_va)
    code += reloc_code
    code += _mov_edi_imm32(AEP_VA)            # edi = original AEP (context ptr)
    code += _xor_esi_esi()                    # esi = 0 (matches capture)
    call_off = len(code)
    rel = (COOKIE_RVA + IMAGE_BASE) - (stub_va + call_off + 5)
    code += b"\xe8" + struct.pack("<i", rel)  # call __security_init_cookie
    jmp_off = len(code)
    rel2 = CRT_CONTINUATION_VA - (stub_va + jmp_off + 5)
    code += b"\xe9" + struct.pack("<i", rel2)  # jmp mutated CRT continuation
    return bytes(code)


def rebuild(original_path, memory_path, output_path, stage="b0", iat_csv=None,
            reloc_csv=None, modules_csv=None, drop_sections=None):
    original = Path(original_path).read_bytes()
    memory = Path(memory_path).read_bytes()
    # Cross-module relocation table (differential measurement) plus the module
    # bases recorded in the same capture the image comes from.  Both default to
    # files that sit next to the tool / next to the dump.
    relocs = []
    module_bases = {}
    reloc_skipped = []
    if stage in ("b0.5", "b1"):
        if reloc_csv is None:
            reloc_csv = Path(__file__).with_name(DEFAULT_RELOC_CSV)
        if modules_csv is None:
            modules_csv = Path(memory_path).with_name("DNF-modules.csv")
        if Path(reloc_csv).exists() and Path(modules_csv).exists():
            relocs = load_reloc_table(reloc_csv)
            module_bases = load_module_bases(modules_csv)
        elif Path(reloc_csv).exists():
            raise ValueError(
                "cross-module relocations need the capture's DNF-modules.csv; "
                "re-dump with a 0000_MemoryDump that records the module list")
    pe = _parse_pe32(original)
    # Refuse a profile/binary mismatch before writing anything. Every landmark
    # below is an absolute address in ONE client; used against the other they
    # produce a PE that jumps into unrelated code and faults with no clue as to
    # why. SizeOfImage and the entry point together identify the binary.
    expect = CLIENTS[CLIENT_NAME]
    if pe["size_of_image"] != expect["size_of_image"] or \
            pe["entrypoint_rva"] + IMAGE_BASE != expect["aep_va"]:
        raise ValueError(
            "--client %s does not match this DNF.exe: profile expects "
            "SizeOfImage %#x / AEP %#x, binary has %#x / %#x"
            % (CLIENT_NAME, expect["size_of_image"], expect["aep_va"],
               pe["size_of_image"], pe["entrypoint_rva"] + IMAGE_BASE))
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

    # Materialize original sections.  Anything named in --drop-sections keeps
    # only a placeholder header: VirtualSize as before, but zero raw data and no
    # MEM_READ/WRITE/EXECUTE, so the loader maps the window PAGE_NOACCESS.  The
    # bytes leave the file entirely, and a surviving reference faults at a
    # precise address instead of silently reading zeros.
    #
    # The window cannot simply be omitted: the image loader requires the section
    # RVAs to be contiguous, and a hole makes the whole PE "not a valid
    # application for this OS platform" before any code runs.  No other section
    # moves, so every surviving RVA is unchanged.
    drop = set(drop_sections or ())
    kept = []
    dropped = []
    # The entry stub writes resolved addresses into ORPHAN_SLOTS and into the
    # cross-module relocation sites. Some of those live in sections the original
    # header marks read-execute (the mutated-code section .,_W holds a 3-entry
    # TLS helper array), where the packer used VirtualProtect to write them at
    # runtime. A stub store into such a page faults before the stub can even
    # reach the cookie call, so grant WRITE to any section that owns a site.
    writes = [slot - IMAGE_BASE for slot, _dll, _fn in ORPHAN_SLOTS]
    writes += [site for site, _m in relocs]
    for index in range(pe["section_count"]):
        h = pe["section_offset"] + index * 40
        name = original[h:h + 8]
        vsize = _u32(original, h + 8)
        vaddr = _u32(original, h + 12)
        orig_raw = _u32(original, h + 16)
        chars = _u32(original, h + 36)
        materialized = max(vsize, orig_raw)
        label = name.rstrip(b"\0").decode("latin1", "replace")
        if index in drop or label in drop:
            dropped.append({"index": index, "name": label, "vaddr": vaddr,
                            "vsize": materialized})
            kept.append({"name": ("$gap%d" % index).encode(), "vsize": materialized,
                         "vaddr": vaddr, "raw_size": 0, "raw_ptr": 0,
                         "characteristics": 0x00000080})   # UNINIT_DATA, no access
            continue
        if materialized == 0:
            kept.append({"name": name, "vsize": vsize, "vaddr": vaddr,
                         "raw_size": 0, "raw_ptr": 0, "characteristics": chars})
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
        cursor = raw_ptr + raw_size
        if stage in ("b0.5", "b1") and any(
                vaddr <= w < vaddr + materialized for w in writes):
            chars |= 0x80000000
        kept.append({"name": name, "vsize": vsize, "vaddr": vaddr,
                     "raw_size": raw_size, "raw_ptr": raw_ptr,
                     "characteristics": chars})
    for spec in drop:
        if not any(d["index"] == spec or d["name"] == spec for d in dropped):
            raise ValueError(f"--drop-sections: no section matches {spec!r}")

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
    tls_rva = TLS_DIR_RVA
    if stage in ("b0.5", "b1"):
        names = set()
        for _slot, dll, func in ORPHAN_SLOTS:
            names.add(dll)
            names.add(func)
        for _site, module in relocs:
            names.add(module)
        for s in sorted(names):
            str_rva[s] = idata_va + len(idata_blob)
            idata_blob += _c_string(s)
        # Relocate the 24-byte IMAGE_TLS_DIRECTORY out of the protector section
        # that holds it, so that section can be dropped.  Only the descriptor
        # moves; its template (.lA0) and index slot (.data) stay put, and the
        # callback list is empty (verified against the dump).
        while len(idata_blob) % 4:
            idata_blob += b"\0"
        tls_rva = idata_va + len(idata_blob)
        idata_blob += memory[TLS_DIR_RVA:TLS_DIR_RVA + TLS_DIR_SIZE]
    idata_blob = bytes(idata_blob)

    def str_va(name):
        return IMAGE_BASE + str_rva[name]

    stub_rva = _align_up(idata_va + len(idata_blob), sect_align)
    stub_va = stub_rva  # keep RVA name below; VA passed to relative-code builders
    if stage == "b0":
        stub = build_stub_b0(ft_slots[(0, "ExitProcess")])
    elif stage == "b0.5":
        reloc_code, reloc_skipped = _emit_crossmodule_relocs(
            ft_slots[(0, "LoadLibraryA")], str_va, relocs, module_bases)
        stub = build_stub_b05(IMAGE_BASE + stub_rva, ft_slots, str_va,
                              reloc_code)
    else:  # b1
        reloc_code, reloc_skipped = _emit_crossmodule_relocs(
            ft_slots[(0, "LoadLibraryA")], str_va, relocs, module_bases)
        stub = build_stub_b1(IMAGE_BASE + stub_rva, ft_slots, str_va,
                             reloc_code)

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

    # Rewrite the whole section header table: surviving originals in their
    # original order, then the two new sections.  Wipe first so a dropped entry
    # leaves no stale header behind.
    table = kept + [sec_idata, sec_stub]
    new_section_count = len(table)
    hdr_base = pe["section_offset"]
    wipe_end = min(hdr_base + (pe["section_count"] + 2) * 40, new_size_of_headers)
    rebuilt[hdr_base:wipe_end] = b"\0" * (wipe_end - hdr_base)
    for slot, sec in enumerate(table):
        h = hdr_base + slot * 40
        name = sec["name"][:8].ljust(8, b"\0")
        struct.pack_into("<8sIIIIIIHHI", rebuilt, h,
                         name, sec["vsize"], sec["vaddr"],
                         sec["raw_size"], sec["raw_ptr"],
                         0, 0, 0, 0, sec["characteristics"])

    # File header: NumberOfSections.
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
        set_dir(DIR_TLS, tls_rva, TLS_DIR_SIZE)
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
        "client": CLIENT_NAME,
        "output_size": len(rebuilt),
        "image_base": IMAGE_BASE,
        "old_size_of_image": size_of_image,
        "new_size_of_image": new_size_of_image,
        "entrypoint_rva": stub_va,
        "idata_rva": idata_va,
        "stub_rva": stub_va,
        "section_count": new_section_count,
        "tls_dir_rva": tls_rva,
        "dropped_sections": dropped,
        "dropped_bytes": sum(d["vsize"] for d in dropped),
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--original", required=True, type=Path)
    ap.add_argument("--memory", required=True, type=Path)
    ap.add_argument("--output", required=True, type=Path)
    ap.add_argument("--stage", default="b0", choices=["b0", "b0.5", "b1"])
    ap.add_argument("--client", default="a12", choices=sorted(CLIENTS),
                    help="which packed client this DNF.exe is; selects the "
                         "cookie/AEP/CRT landmarks and the relocation table. "
                         "Checked against the binary before anything is written")
    ap.add_argument("--iat-csv", type=Path,
                    help="DNF-iat.csv (required for b0.5 and b1)")
    ap.add_argument("--reloc-csv", type=Path,
                    help="crossmodule-relocs.csv (defaults to the tool's own directory)")
    ap.add_argument("--modules-csv", type=Path,
                    help="DNF-modules.csv from the same capture (defaults next to --memory)")
    ap.add_argument("--drop-sections", default="",
                    help="comma-separated section indices and/or names to omit "
                         "entirely, e.g. 13")
    args = ap.parse_args()
    select_client(args.client)
    drop = set()
    for tok in args.drop_sections.split(","):
        tok = tok.strip()
        if not tok:
            continue
        drop.add(int(tok) if tok.isdigit() else tok)
    print(json.dumps(rebuild(args.original, args.memory, args.output,
                             args.stage, args.iat_csv,
                             args.reloc_csv, args.modules_csv,
                             drop), indent=2))


if __name__ == "__main__":
    main()
