"""Read only a named Host's cwd and non-secret configuration overrides; never dump its environment."""
import ctypes
import json
import struct
import sys
from ctypes import wintypes
from pathlib import Path

if len(sys.argv) not in (2, 4) or ctypes.sizeof(ctypes.c_void_p) != 8:
    raise SystemExit("Use 64-bit Python and pass the verified Host PID.")
pid = int(sys.argv[1])
kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
kernel.ReadProcessMemory.restype = wintypes.BOOL
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
query = ctypes.WinDLL("ntdll").NtQueryInformationProcess
query.argtypes = [wintypes.HANDLE, wintypes.ULONG, ctypes.c_void_p, wintypes.ULONG, ctypes.POINTER(wintypes.ULONG)]
query.restype = wintypes.LONG
handle = kernel.OpenProcess(0x410, False, pid)  # QUERY_INFORMATION | VM_READ; no write/terminate rights.
if not handle:
    raise ctypes.WinError(ctypes.get_last_error())
try:
    def read(address, size):
        if size > 65536:
            raise ValueError("Read budget exceeded")
        buffer = ctypes.create_string_buffer(size)
        count = ctypes.c_size_t()
        if not kernel.ReadProcessMemory(handle, address, buffer, size, ctypes.byref(count)) or count.value != size:
            raise ctypes.WinError(ctypes.get_last_error())
        return buffer.raw

    information = (ctypes.c_void_p * 6)()
    returned = wintypes.ULONG()
    status = query(handle, 0, information, ctypes.sizeof(information), ctypes.byref(returned))
    if status != 0:
        raise OSError(f"NtQueryInformationProcess: {status:#x}")
    parameters = struct.unpack("<Q", read(information[1] + 0x20, 8))[0]
    header = read(parameters, 0x88)
    length, capacity, cwd_address = struct.unpack_from("<HH4xQ", header, 0x38)
    if length > capacity or length > 32768:
        raise ValueError("Invalid cwd length")
    cwd = read(cwd_address, length).decode("utf-16le")
    address = struct.unpack_from("<Q", header, 0x80)[0]
    block = bytearray()
    end = None
    for offset in range(0, 65536, 512):
        block.extend(read(address + offset, 512))
        for index in range(max(0, len(block) - 516), len(block) - 3, 2):
            if block[index:index + 4] == b"\0\0\0\0":
                end = index
                break
        if end is not None:
            break
    if end is None:
        raise ValueError("Environment terminator exceeds 64 KiB budget")
    allowed = {"database__path", "pvf_archive_path", "pvf__archivepath", "networking__enabled",
        "networking__channelport", "networking__udpdiscoveryenabled", "aspnetcore_environment",
        "dotnet_environment", "networking__gameports__0", "networking__gameports__1"}
    selected = {}
    environment = {}
    for entry in block[:end].decode("utf-16le").split("\0"):
        name, separator, value = entry.partition("=")
        if separator and name and not name.startswith("="):
            environment[name] = value
        if separator and name.casefold() in allowed:
            selected[name] = value
    if len(sys.argv) == 4:
        destination = Path(sys.argv[3]).resolve()
        private = Path(__file__).resolve().parent / "private"
        if sys.argv[2] != "--save-private-env" or not destination.is_relative_to(private):
            raise ValueError("Environment snapshots must stay inside this task's ignored private directory")
        destination.parent.mkdir(exist_ok=True)
        destination.write_bytes(json.dumps(environment, ensure_ascii=False).encode("utf-8"))
    print(json.dumps({"pid": pid, "cwd": cwd, "overrides": selected}, indent=2, ensure_ascii=False))
finally:
    kernel.CloseHandle(handle)
