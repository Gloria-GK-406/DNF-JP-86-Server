"""Read selected launch settings from the authorized Host and back up its live SQLite DB.

Only allowlisted service configuration is retained. Other process environment
values are discarded; no credentials or client command lines are exported.
"""
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
from pathlib import Path
import sqlite3
import struct
import sys

pid = int(sys.argv[1])
root = Path(__file__).resolve().parents[3] / "artifacts/circus-probe"
exe = Path(r"E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Dfo.Server.Host.exe")
kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
kernel.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
process = kernel.OpenProcess(0x410, False, pid)
if not process:
    raise ctypes.WinError(ctypes.get_last_error())
def read(address, count):
    buffer = ctypes.create_string_buffer(count)
    actual = ctypes.c_size_t()
    if not kernel.ReadProcessMemory(process, address, buffer, count, ctypes.byref(actual)):
        raise ctypes.WinError(ctypes.get_last_error())
    if actual.value != count:
        raise ValueError("Short process-memory read")
    return buffer.raw
try:
    image = ctypes.create_unicode_buffer(32768)
    image_size = wintypes.DWORD(len(image))
    if not kernel.QueryFullProcessImageNameW(process, 0, image, ctypes.byref(image_size)):
        raise ctypes.WinError(ctypes.get_last_error())
    if Path(image.value).resolve() != exe.resolve():
        raise ValueError("PID no longer belongs to the expected main Host")
    ntdll = ctypes.WinDLL("ntdll")
    query = ntdll.NtQueryInformationProcess
    query.argtypes = [wintypes.HANDLE, wintypes.ULONG, ctypes.c_void_p, wintypes.ULONG, ctypes.c_void_p]
    basic = ctypes.create_string_buffer(48)
    if query(process, 0, basic, 48, None) != 0:
        raise ValueError("Cannot inspect 64-bit Host PEB")
    peb = struct.unpack_from("<Q", basic.raw, 8)[0]
    parameters = struct.unpack("<Q", read(peb+0x20, 8))[0]
    environment = struct.unpack("<Q", read(parameters+0x80, 8))[0]
    raw = bytearray()
    for offset in range(0, 128*1024, 1024):
        raw.extend(read(environment+offset, 1024))
        decoded = raw.decode("utf-16le")
        if "\0\0" in decoded:
            decoded = decoded.split("\0\0", 1)[0]
            break
    else:
        raise ValueError("Environment exceeds the bounded 128 KiB read")
    allowed = ("DATABASE__", "PVF__", "NETWORKING__", "CHANNEL__", "GAME__", "GAMELOGIN__", "LAUNCHERAUTH__")
    exact = ("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "GMWEB__ENABLED", "GMWEB__LISTENURL", "GMWEB__ICONPACK")
    settings = {}
    for line in decoded.split("\0"):
        key, separator, value = line.partition("=")
        if separator and (key.upper().startswith(allowed) or key.upper() in exact):
            settings[key] = value
    cwd_descriptor = read(parameters+0x38, 16)
    cwd_length = struct.unpack_from("<H", cwd_descriptor)[0]
    cwd_pointer = struct.unpack_from("<Q", cwd_descriptor, 8)[0]
    cwd = read(cwd_pointer, cwd_length).decode("utf-16le")
finally:
    kernel.CloseHandle(process)

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
snapshot = root / ("main-before-switch-"+stamp)
snapshot.mkdir()
base_settings = json.loads((exe.parent / "appsettings.json").read_text(encoding="utf-8-sig"))
environment_name = settings.get("DOTNET_ENVIRONMENT", settings.get("ASPNETCORE_ENVIRONMENT", "Production"))
config_files = [exe.parent / "appsettings.json", exe.parent / f"appsettings.{environment_name}.json"]
for file in config_files:
    if file.exists():
        (snapshot / file.name).write_bytes(file.read_bytes())
        extra = json.loads(file.read_text(encoding="utf-8-sig"))
        if "Database" in extra:
            base_settings["Database"] = extra["Database"]
database_setting = next((v for k, v in settings.items() if k.upper() == "DATABASE__PATH"), base_settings["Database"]["Path"])
database = Path(database_setting)
if not database.is_absolute():
    database = exe.parent / database
database = database.resolve(strict=True)
backup = snapshot / "main-consistent.db"
with sqlite3.connect(database.as_uri()+"?mode=ro", uri=True) as source, sqlite3.connect(backup) as target:
    source.backup(target)
    integrity = target.execute("PRAGMA quick_check").fetchone()[0]
    tables = {name for (name,) in target.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    counts = {name: target.execute(f'SELECT COUNT(*) FROM "{name}"').fetchone()[0]
              for name in ("accounts", "characters", "items", "item_slots") if name in tables}
if integrity != "ok":
    raise ValueError("Consistent database backup failed quick_check")
manifest = {"pid": pid, "exe": str(exe), "exe_sha256": digest(exe), "working_directory": cwd,
            "configuration_environment": settings, "environment_name": environment_name,
            "database": str(database), "backup": str(backup), "backup_sha256": digest(backup),
            "backup_api": "sqlite3.Connection.backup", "quick_check": integrity, "counts": counts,
            "captured_at": datetime.datetime.now(datetime.timezone.utc).isoformat()}
(snapshot / "restore.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
(root / "main-snapshot-path.txt").write_text(str(snapshot), encoding="utf-8")
print(json.dumps({"snapshot": str(snapshot), "database": str(database), "settings": settings,
                  "quick_check": integrity, "counts": counts}, ensure_ascii=False))
