"""Read only the verified A21 dungeon timer fields, without attaching a debugger."""
import argparse
import ctypes
import datetime
import hashlib
import json
import struct
from ctypes import wintypes
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('pid', type=int)
parser.add_argument('--output', type=Path)
args = parser.parse_args()
kernel = ctypes.WinDLL('kernel32', use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
    ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
kernel.ReadProcessMemory.restype = wintypes.BOOL
kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
    wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel.QueryFullProcessImageNameW.restype = wintypes.BOOL
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
handle = kernel.OpenProcess(0x1010, False, args.pid)  # QUERY_LIMITED_INFORMATION | VM_READ
if not handle:
    raise ctypes.WinError(ctypes.get_last_error())
used = 0

def read(address, length):
    global used
    if not (0x10000 <= address < 0x80000000 and 0 < length <= 256):
        raise ValueError('Field outside bounded 32-bit client read')
    used += length
    if used > 4096:
        raise ValueError('4 KiB read budget exhausted')
    buffer = ctypes.create_string_buffer(length)
    count = ctypes.c_size_t()
    if not kernel.ReadProcessMemory(handle, address, buffer, length, ctypes.byref(count)) or count.value != length:
        raise OSError(f'Cannot read verified field at {address:08X}')
    return buffer.raw

def uint(address):
    return struct.unpack('<I', read(address, 4))[0]

try:
    executable_buffer = ctypes.create_unicode_buffer(1024)
    characters = wintypes.DWORD(len(executable_buffer))
    if not kernel.QueryFullProcessImageNameW(handle, 0, executable_buffer, ctypes.byref(characters)):
        raise ctypes.WinError(ctypes.get_last_error())
    executable = Path(executable_buffer.value)
    if executable.name.lower() != 'dnf.exe':
        raise ValueError('Expected DNF.exe')
    with executable.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    if digest != '1f8244d3f7309bbe2fde4299e1863a894f6b28d5707bf01732980585b20b00dd':
        raise ValueError('Client identity changed')
    core = uint(0x3A5B348)
    module = uint(core + 0xAC + 2 * 4)
    stage_getter = uint(uint(module) + 0x60)
    if read(stage_getter, 7) != bytes.fromhex('8B8198020000C3'):
        raise ValueError('Dungeon module stage getter changed')
    stage = uint(module + 0x298)
    dungeon = uint(stage + 0xCC)
    if not dungeon:
        raise ValueError('No dungeon object: enter ordinary 3200 and remain in its first room')
    dungeon_id = uint(dungeon + 0x174)
    if dungeon_id not in (3200, 3201, 3202):
        raise ValueError(f'Expected ordinary giant dragon, found {dungeon_id}')
    parameters = struct.unpack('<3i', read(dungeon + 0xBE8, 12))
    manager = uint(dungeon + 0xBF4)
    if not manager:
        raise ValueError('Native timer manager is not initialized')
    manager_parameters = struct.unpack('<3i', read(manager + 0x384, 12))
    inverse = read(0x3601BC0, 256)

    def decode_word(encoded):
        return int.from_bytes(bytes((inverse[value] - index) & 255
            for index, value in enumerate(encoded)), 'little')

    timer_bytes = read(manager + 0x368, 28)
    timer = [decode_word(timer_bytes[index:index + 4]) for index in range(0, 28, 4)]
    flags = timer[3]
    elapsed = 0
    if flags & 3 == 3:
        if flags & 4:
            elapsed = timer[5]
        else:
            if not 0 <= timer[2] < 16:
                raise ValueError('Timer bank outside verified 16 banks')
            clock = decode_word(read(0x4C6D210 + timer[2] * 4, 4))
            elapsed = (clock + timer[4]) & 0xFFFFFFFF
    result = dict(at=datetime.datetime.now().astimezone().isoformat(), pid=args.pid,
        executable=str(executable), clientExeSha256=digest, dungeonId=dungeon_id,
        parameters=parameters, managerParameters=manager_parameters,
        timerDecoded=timer, nativeElapsed=elapsed, bytesRead=used,
        evidence='Read-only bounded runtime fields; timer units and material curve require separate proof')
    text = json.dumps(result, indent=2)
    if args.output:
        if args.output.exists():
            raise ValueError('Refuse overwriting runtime evidence')
        args.output.write_text(text, encoding='utf8')
    print(text)
finally:
    kernel.CloseHandle(handle)
