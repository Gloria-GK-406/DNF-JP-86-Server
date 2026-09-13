#!/usr/bin/env python3
"""Read-only external observer for the A21 ID 0x11 party-directory vector."""

from __future__ import annotations

import argparse
import contextlib
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import struct
import time
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from typing import Callable, Iterable, Iterator


PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
READ_ONLY_PROCESS_ACCESS = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ

TH32CS_SNAPPROCESS = 0x00000002
TH32CS_SNAPMODULE = 0x00000008
TH32CS_SNAPMODULE32 = 0x00000010
MEM_COMMIT = 0x00001000
PAGE_GUARD = 0x00000100
PAGE_NOACCESS = 0x00000001
WRITABLE_BASE_PROTECTIONS = {0x04, 0x08, 0x40, 0x80}
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

FINAL_VTABLE_RVA = 0x02DFFFD4
VECTOR_BEGIN_OFFSET = 0x18C
CNUSER_VTABLE_RVA = 0x02EF6CBC
PARTY_VTABLE_RVA = 0x02EF7380
CNUSER_ID_OFFSET = 0x08
CNUSER_PARTY_OFFSET = 0x444
PARTY_ID_OFFSET = 0x04
PARTY_TITLE_INDEX_OFFSET = 0x08
PARTY_ROSTER_OFFSET = 0x2C
PARTY_LEADER_SLOT_OFFSET = 0x4C
PARTY_SLOT_COUNT = 8
MAX_PARTY_IDS = 256
MAX_PARTY_CAPACITY = 4096
MAX_X86_ADDRESS = 0x1_0000_0000
SCAN_CHUNK_SIZE = 1024 * 1024


class VectorValidationError(ValueError):
    pass


@dataclass(frozen=True)
class DirectoryVectorState:
    object_address: int
    begin: int
    end: int
    capacity_end: int
    party_ids: list[int]

    @property
    def count(self) -> int:
        return len(self.party_ids)

    @property
    def capacity(self) -> int:
        if self.begin == 0:
            return 0
        return (self.capacity_end - self.begin) // 4

    def to_json(self) -> dict[str, object]:
        return {
            "object_address": f"0x{self.object_address:08X}",
            "begin": f"0x{self.begin:08X}",
            "end": f"0x{self.end:08X}",
            "capacity_end": f"0x{self.capacity_end:08X}",
            "count": self.count,
            "capacity": self.capacity,
            "party_ids": self.party_ids,
        }


@dataclass(frozen=True)
class CNUserState:
    object_address: int
    user_id: int
    party_address: int

    def to_json(self) -> dict[str, object]:
        return {
            "object_address": f"0x{self.object_address:08X}",
            "user_id": self.user_id,
            "party_address": f"0x{self.party_address:08X}",
        }


@dataclass(frozen=True)
class PartyObjectState:
    object_address: int
    party_id: int
    title_index: int
    member_user_ids: list[int]
    leader_slot: int

    @property
    def leader_user_id(self) -> int:
        return self.member_user_ids_by_slot[self.leader_slot]

    @property
    def member_user_ids_by_slot(self) -> list[int]:
        values = [0] * PARTY_SLOT_COUNT
        for slot, user_id in self.occupied_slots:
            values[slot] = user_id
        return values

    @property
    def occupied_slots(self) -> list[tuple[int, int]]:
        return list(self._occupied_slots)

    _occupied_slots: tuple[tuple[int, int], ...]

    def to_json(self) -> dict[str, object]:
        return {
            "object_address": f"0x{self.object_address:08X}",
            "party_id": self.party_id,
            "title_index": self.title_index,
            "member_user_ids": self.member_user_ids,
            "leader_slot": self.leader_slot,
            "leader_user_id": self.leader_user_id,
        }


def _read_exact(
    reader: Callable[[int, int], bytes], address: int, size: int
) -> bytes:
    try:
        data = reader(address, size)
    except (OSError, ValueError) as error:
        raise VectorValidationError("required memory range is unreadable") from error
    if len(data) != size:
        raise VectorValidationError(
            f"partial memory read: expected {size}, received {len(data)}"
        )
    return data


def inspect_directory_object(
    object_address: int, reader: Callable[[int, int], bytes]
) -> DirectoryVectorState:
    raw = _read_exact(reader, object_address + VECTOR_BEGIN_OFFSET, 12)
    begin, end, capacity_end = struct.unpack("<III", raw)

    if begin == end == capacity_end == 0:
        return DirectoryVectorState(object_address, begin, end, capacity_end, [])
    if begin == 0 or end == 0 or capacity_end == 0:
        raise VectorValidationError("partially null vector pointers")
    if any(pointer % 4 for pointer in (begin, end, capacity_end)):
        raise VectorValidationError("unaligned vector pointer")
    if not begin <= end <= capacity_end:
        raise VectorValidationError("vector pointers are out of order")
    if capacity_end >= MAX_X86_ADDRESS:
        raise VectorValidationError("vector capacity exceeds x86 address space")

    count_bytes = end - begin
    capacity_bytes = capacity_end - begin
    if count_bytes % 4 or capacity_bytes % 4:
        raise VectorValidationError("vector byte span is not four-byte aligned")
    if count_bytes // 4 > MAX_PARTY_IDS:
        raise VectorValidationError("party vector count exceeds safety bound")
    if capacity_bytes // 4 > MAX_PARTY_CAPACITY:
        raise VectorValidationError("party vector capacity exceeds safety bound")

    values = _read_exact(reader, begin, count_bytes) if count_bytes else b""
    party_ids = list(struct.unpack(f"<{count_bytes // 4}I", values)) if values else []
    return DirectoryVectorState(
        object_address, begin, end, capacity_end, party_ids
    )


def inspect_cnuser_object(
    object_address: int,
    expected_vtable: int,
    reader: Callable[[int, int], bytes],
) -> CNUserState:
    raw = _read_exact(reader, object_address, CNUSER_PARTY_OFFSET + 4)
    vtable = struct.unpack_from("<I", raw, 0)[0]
    user_id = struct.unpack_from("<I", raw, CNUSER_ID_OFFSET)[0]
    party_address = struct.unpack_from("<I", raw, CNUSER_PARTY_OFFSET)[0]
    if vtable != expected_vtable:
        raise VectorValidationError("CNUser vtable does not match")
    if not 0 < user_id <= 0xFFFF:
        raise VectorValidationError("CNUser id is outside the wire-id range")
    if party_address and party_address % 4:
        raise VectorValidationError("CNUser party pointer is unaligned")
    return CNUserState(object_address, user_id, party_address)


def inspect_party_object(
    object_address: int,
    expected_vtable: int,
    reader: Callable[[int, int], bytes],
) -> PartyObjectState:
    raw = _read_exact(reader, object_address, 0x70)
    vtable = struct.unpack_from("<I", raw, 0)[0]
    party_id = struct.unpack_from("<I", raw, PARTY_ID_OFFSET)[0]
    title_index = struct.unpack_from("<I", raw, PARTY_TITLE_INDEX_OFFSET)[0]
    roster = list(
        struct.unpack_from(f"<{PARTY_SLOT_COUNT}I", raw, PARTY_ROSTER_OFFSET)
    )
    leader_slot = struct.unpack_from("<I", raw, PARTY_LEADER_SLOT_OFFSET)[0]
    if vtable != expected_vtable:
        raise VectorValidationError("CParty vtable does not match")
    if not 0 < party_id <= 0xFFFF:
        raise VectorValidationError("CParty id is outside the wire-id range")
    if title_index > 0xFF:
        raise VectorValidationError("CParty title index is outside the wire range")
    if leader_slot >= PARTY_SLOT_COUNT:
        raise VectorValidationError("CParty leader slot is outside the roster")
    if any(user_id > 0xFFFF for user_id in roster):
        raise VectorValidationError("CParty roster id is outside the wire-id range")
    empty_user_ids = {0, 0xFFFF}
    occupied_slots = tuple(
        (slot, user_id)
        for slot, user_id in enumerate(roster)
        if user_id not in empty_user_ids
    )
    if roster[leader_slot] in empty_user_ids:
        raise VectorValidationError("CParty leader slot is empty")
    return PartyObjectState(
        object_address=object_address,
        party_id=party_id,
        title_index=title_index,
        member_user_ids=[
            user_id for user_id in roster if user_id not in empty_user_ids
        ],
        leader_slot=leader_slot,
        _occupied_slots=occupied_slots,
    )


def describe_directory_candidate(
    object_address: int, reader: Callable[[int, int], bytes]
) -> dict[str, object]:
    observation: dict[str, object] = {
        "object_address": f"0x{object_address:08X}",
    }
    try:
        raw = _read_exact(reader, object_address + VECTOR_BEGIN_OFFSET, 12)
        begin, end, capacity_end = struct.unpack("<III", raw)
        observation.update(
            begin=f"0x{begin:08X}",
            end=f"0x{end:08X}",
            capacity_end=f"0x{capacity_end:08X}",
        )
        state = inspect_directory_object(object_address, reader)
    except VectorValidationError as error:
        observation.update(
            status="rejected",
            reason=type(error).__name__,
            detail=str(error),
        )
        return observation

    observation.update(
        status="valid",
        count=state.count,
        capacity=state.capacity,
        party_ids=state.party_ids,
    )
    return observation


def iter_value_offsets(chunks: Iterable[bytes], needle: bytes) -> Iterator[int]:
    if not needle:
        raise ValueError("needle must not be empty")
    carry = b""
    consumed = 0
    for chunk in chunks:
        combined = carry + chunk
        combined_start = consumed - len(carry)
        search_from = 0
        while True:
            index = combined.find(needle, search_from)
            if index < 0:
                break
            yield combined_start + index
            search_from = index + 1
        carry = combined[-(len(needle) - 1) :] if len(needle) > 1 else b""
        consumed += len(chunk)


if os.name == "nt":
    ULONG_PTR = ctypes.c_size_t

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ULONG_PTR),
            ("th32ModuleID", wintypes.DWORD),
            ("cntThreads", wintypes.DWORD),
            ("th32ParentProcessID", wintypes.DWORD),
            ("pcPriClassBase", wintypes.LONG),
            ("dwFlags", wintypes.DWORD),
            ("szExeFile", wintypes.WCHAR * 260),
        ]

    class MODULEENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("th32ModuleID", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("GlblcntUsage", wintypes.DWORD),
            ("ProccntUsage", wintypes.DWORD),
            ("modBaseAddr", ctypes.c_void_p),
            ("modBaseSize", wintypes.DWORD),
            ("hModule", wintypes.HMODULE),
            ("szModule", wintypes.WCHAR * 256),
            ("szExePath", wintypes.WCHAR * 260),
        ]

    class MEMORY_BASIC_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("BaseAddress", ctypes.c_void_p),
            ("AllocationBase", ctypes.c_void_p),
            ("AllocationProtect", wintypes.DWORD),
            ("RegionSize", ctypes.c_size_t),
            ("State", wintypes.DWORD),
            ("Protect", wintypes.DWORD),
            ("Type", wintypes.DWORD),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Process32FirstW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(PROCESSENTRY32W),
    ]
    kernel32.Process32FirstW.restype = wintypes.BOOL
    kernel32.Process32NextW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(PROCESSENTRY32W),
    ]
    kernel32.Process32NextW.restype = wintypes.BOOL
    kernel32.Module32FirstW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(MODULEENTRY32W),
    ]
    kernel32.Module32FirstW.restype = wintypes.BOOL
    kernel32.Module32NextW.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(MODULEENTRY32W),
    ]
    kernel32.Module32NextW.restype = wintypes.BOOL
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.QueryFullProcessImageNameW.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        wintypes.LPWSTR,
        ctypes.POINTER(wintypes.DWORD),
    ]
    kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
    kernel32.ReadProcessMemory.argtypes = [
        wintypes.HANDLE,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_size_t,
        ctypes.POINTER(ctypes.c_size_t),
    ]
    kernel32.ReadProcessMemory.restype = wintypes.BOOL
    kernel32.VirtualQueryEx.argtypes = [
        wintypes.HANDLE,
        ctypes.c_void_p,
        ctypes.POINTER(MEMORY_BASIC_INFORMATION),
        ctypes.c_size_t,
    ]
    kernel32.VirtualQueryEx.restype = ctypes.c_size_t
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL


def _last_error(operation: str) -> OSError:
    code = ctypes.get_last_error()
    return OSError(code, f"{operation} failed with Win32 error {code}")


def _close_handle(handle: int | None) -> None:
    if handle and os.name == "nt":
        kernel32.CloseHandle(handle)


def _normal_path(path: str | Path) -> str:
    return os.path.normcase(os.path.abspath(os.fspath(path)))


def _query_process_path(handle: int) -> str:
    buffer = ctypes.create_unicode_buffer(32768)
    size = wintypes.DWORD(len(buffer))
    if not kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
        raise _last_error("QueryFullProcessImageNameW")
    return buffer.value


def find_exact_process(executable: Path) -> int | None:
    if os.name != "nt":
        raise RuntimeError("the directory probe requires Windows")
    expected = _normal_path(executable)
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == INVALID_HANDLE_VALUE:
        raise _last_error("CreateToolhelp32Snapshot(processes)")
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        more = kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while more:
            if entry.szExeFile.casefold() == executable.name.casefold():
                handle = kernel32.OpenProcess(
                    READ_ONLY_PROCESS_ACCESS, False, entry.th32ProcessID
                )
                if handle:
                    try:
                        with contextlib.suppress(OSError):
                            if _normal_path(_query_process_path(handle)) == expected:
                                return int(entry.th32ProcessID)
                    finally:
                        _close_handle(handle)
            more = kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        _close_handle(snapshot)
    return None


def open_process_readonly(process_id: int) -> int:
    if os.name != "nt":
        raise RuntimeError("the directory probe requires Windows")
    handle = kernel32.OpenProcess(READ_ONLY_PROCESS_ACCESS, False, process_id)
    if not handle:
        raise _last_error("OpenProcess(read-only)")
    return int(handle)


def find_module_base(process_id: int, executable: Path) -> int:
    expected = _normal_path(executable)
    snapshot = kernel32.CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, process_id
    )
    if snapshot == INVALID_HANDLE_VALUE:
        raise _last_error("CreateToolhelp32Snapshot(modules)")
    try:
        entry = MODULEENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        more = kernel32.Module32FirstW(snapshot, ctypes.byref(entry))
        while more:
            if _normal_path(entry.szExePath) == expected:
                if entry.modBaseAddr is None:
                    raise RuntimeError("matching module has no base address")
                return int(entry.modBaseAddr)
            more = kernel32.Module32NextW(snapshot, ctypes.byref(entry))
    finally:
        _close_handle(snapshot)
    raise RuntimeError("exact DNF executable module is not loaded yet")


def read_process_memory(handle: int, address: int, size: int) -> bytes:
    if size == 0:
        return b""
    buffer = (ctypes.c_ubyte * size)()
    received = ctypes.c_size_t()
    succeeded = kernel32.ReadProcessMemory(
        handle,
        ctypes.c_void_p(address),
        buffer,
        size,
        ctypes.byref(received),
    )
    if not succeeded or received.value != size:
        raise _last_error("ReadProcessMemory")
    return bytes(buffer)


def iter_writable_regions(handle: int) -> Iterator[tuple[int, int]]:
    address = 0
    while address < MAX_X86_ADDRESS:
        information = MEMORY_BASIC_INFORMATION()
        result = kernel32.VirtualQueryEx(
            handle,
            ctypes.c_void_p(address),
            ctypes.byref(information),
            ctypes.sizeof(information),
        )
        if result == 0:
            break
        base = int(information.BaseAddress or 0)
        size = int(information.RegionSize)
        protection = int(information.Protect)
        if (
            information.State == MEM_COMMIT
            and not protection & (PAGE_GUARD | PAGE_NOACCESS)
            and (protection & 0xFF) in WRITABLE_BASE_PROTECTIONS
            and size > 0
        ):
            yield base, min(size, MAX_X86_ADDRESS - base)
        next_address = base + max(size, 0x1000)
        if next_address <= address:
            break
        address = next_address


def _scan_region(handle: int, base: int, size: int, needle: bytes) -> Iterator[int]:
    carry = b""
    offset = 0
    while offset < size:
        request = min(SCAN_CHUNK_SIZE, size - offset)
        try:
            chunk = read_process_memory(handle, base + offset, request)
        except OSError:
            carry = b""
            offset += request
            continue
        combined = carry + chunk
        combined_base = base + offset - len(carry)
        search_from = 0
        while True:
            index = combined.find(needle, search_from)
            if index < 0:
                break
            yield combined_base + index
            search_from = index + 1
        carry = combined[-(len(needle) - 1) :]
        offset += request


def scan_directory_objects(
    handle: int, expected_vtable: int
) -> list[DirectoryVectorState]:
    needle = struct.pack("<I", expected_vtable)
    reader = lambda address, size: read_process_memory(handle, address, size)
    results: list[DirectoryVectorState] = []
    seen: set[int] = set()
    for base, size in iter_writable_regions(handle):
        for object_address in _scan_region(handle, base, size, needle):
            if object_address in seen:
                continue
            seen.add(object_address)
            try:
                results.append(inspect_directory_object(object_address, reader))
            except VectorValidationError:
                continue
            if len(results) >= 64:
                return sorted(results, key=lambda value: value.object_address)
    return sorted(results, key=lambda value: value.object_address)


def scan_directory_candidates(
    handle: int, expected_vtable: int
) -> list[dict[str, object]]:
    needle = struct.pack("<I", expected_vtable)
    reader = lambda address, size: read_process_memory(handle, address, size)
    results: list[dict[str, object]] = []
    seen: set[int] = set()
    for base, size in iter_writable_regions(handle):
        for object_address in _scan_region(handle, base, size, needle):
            if object_address in seen:
                continue
            seen.add(object_address)
            results.append(describe_directory_candidate(object_address, reader))
            if len(results) >= 128:
                return sorted(results, key=lambda value: str(value["object_address"]))
    return sorted(results, key=lambda value: str(value["object_address"]))


def scan_cnuser_objects(handle: int, expected_vtable: int) -> list[CNUserState]:
    needle = struct.pack("<I", expected_vtable)
    reader = lambda address, size: read_process_memory(handle, address, size)
    results: list[CNUserState] = []
    seen: set[int] = set()
    for base, size in iter_writable_regions(handle):
        for object_address in _scan_region(handle, base, size, needle):
            if object_address in seen:
                continue
            seen.add(object_address)
            try:
                results.append(
                    inspect_cnuser_object(object_address, expected_vtable, reader)
                )
            except VectorValidationError:
                continue
    return sorted(results, key=lambda value: value.user_id)


def scan_party_objects(handle: int, expected_vtable: int) -> list[PartyObjectState]:
    needle = struct.pack("<I", expected_vtable)
    reader = lambda address, size: read_process_memory(handle, address, size)
    results: list[PartyObjectState] = []
    seen: set[int] = set()
    for base, size in iter_writable_regions(handle):
        for object_address in _scan_region(handle, base, size, needle):
            if object_address in seen:
                continue
            seen.add(object_address)
            try:
                results.append(
                    inspect_party_object(object_address, expected_vtable, reader)
                )
            except VectorValidationError:
                continue
    return sorted(results, key=lambda value: value.party_id)


class JsonlRecorder:
    def __init__(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self._stream = path.open("a", encoding="utf-8", newline="\n")
        self._sequence = 0

    def write(self, event: str, **values: object) -> None:
        self._sequence += 1
        payload = {
            "timestamp_utc": datetime.now(timezone.utc).isoformat(
                timespec="milliseconds"
            ),
            "event_sequence": self._sequence,
            "event": event,
            **values,
        }
        self._stream.write(json.dumps(payload, ensure_ascii=True) + "\n")
        self._stream.flush()

    def close(self) -> None:
        self._stream.close()


def _write_ready(path: Path, executable: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "pid": os.getpid(),
        "watch_executable": str(executable),
        "process_access_mask": f"0x{READ_ONLY_PROCESS_ACCESS:04X}",
        "write_or_inject_access_requested": False,
        "final_vtable_rva": f"0x{FINAL_VTABLE_RVA:08X}",
        "vector_begin_offset": f"0x{VECTOR_BEGIN_OFFSET:03X}",
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def _candidate_signature(candidates: list[dict[str, object]]) -> str:
    return json.dumps(candidates, ensure_ascii=True, sort_keys=True)


def monitor(executable: Path, log_path: Path, ready_path: Path, interval: float) -> None:
    executable = executable.resolve(strict=True)
    recorder = JsonlRecorder(log_path)
    handle: int | None = None
    attached_process_id: int | None = None
    last_signature: str | None = None
    _write_ready(ready_path, executable)
    recorder.write(
        "watcher_ready",
        probe_pid=os.getpid(),
        executable=str(executable),
        process_access_mask=f"0x{READ_ONLY_PROCESS_ACCESS:04X}",
        write_or_inject_access_requested=False,
    )
    try:
        while True:
            process_id = find_exact_process(executable)
            if process_id is None:
                if attached_process_id is not None:
                    recorder.write("process_detached", process_id=attached_process_id)
                    _close_handle(handle)
                    handle = None
                    attached_process_id = None
                    last_signature = None
                time.sleep(interval)
                continue

            if process_id != attached_process_id:
                _close_handle(handle)
                handle = None
                try:
                    candidate_handle = open_process_readonly(process_id)
                    module_base = find_module_base(process_id, executable)
                except (OSError, RuntimeError) as error:
                    with contextlib.suppress(UnboundLocalError):
                        _close_handle(candidate_handle)
                    recorder.write(
                        "attach_pending",
                        process_id=process_id,
                        error_type=type(error).__name__,
                    )
                    time.sleep(interval)
                    continue
                handle = candidate_handle
                attached_process_id = process_id
                last_signature = None
                recorder.write(
                    "process_attached",
                    process_id=process_id,
                    module_base=f"0x{module_base:08X}",
                    expected_vtable=f"0x{module_base + FINAL_VTABLE_RVA:08X}",
                )

            assert handle is not None
            try:
                module_base = find_module_base(process_id, executable)
                candidates = scan_directory_candidates(
                    handle, module_base + FINAL_VTABLE_RVA
                )
            except (OSError, RuntimeError) as error:
                recorder.write(
                    "scan_error",
                    process_id=process_id,
                    error_type=type(error).__name__,
                )
                time.sleep(interval)
                continue

            signature = _candidate_signature(candidates)
            if signature != last_signature:
                recorder.write(
                    "directory_state",
                    process_id=process_id,
                    module_base=f"0x{module_base:08X}",
                    expected_vtable=f"0x{module_base + FINAL_VTABLE_RVA:08X}",
                    candidates=candidates,
                    instances=[
                        candidate
                        for candidate in candidates
                        if candidate.get("status") == "valid"
                    ],
                )
                last_signature = signature
            time.sleep(interval)
    finally:
        _close_handle(handle)
        with contextlib.suppress(FileNotFoundError):
            ready_path.unlink()
        recorder.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--log", type=Path, required=True)
    parser.add_argument("--ready", type=Path, required=True)
    parser.add_argument("--interval", type=float, default=2.0)
    return parser.parse_args()


def main() -> int:
    arguments = parse_args()
    if arguments.interval < 0.25:
        raise SystemExit("--interval must be at least 0.25 seconds")
    try:
        monitor(arguments.exe, arguments.log, arguments.ready, arguments.interval)
    except KeyboardInterrupt:
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
