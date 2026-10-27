"""Prepare a new private research runtime and a fully copied client; no shared writes."""
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import sqlite3
import subprocess
import time

workspace = Path(__file__).resolve().parents[3]
root = workspace / "artifacts/circus-probe"
source = Path(r"E:\Project\DNF\DNFClient-A21-protocol3")
if root.exists():
    raise SystemExit("Private runtime already exists; inspect it rather than overwrite it")
root.mkdir(parents=True)
config = json.loads((workspace / "Server/src/Dfo.Server.Host/appsettings.json").read_text(encoding="utf-8-sig"))
config.update({
    "Channel": {"UseScriptCatalog": "false", "ScriptVersion": "66", "PublicServerIp": "127.0.0.1",
                "ScriptPath": str(root / "channel_info.etc"),
                "Channels": [{"Id": 11, "Name": "Circus probe", "Host": "127.0.0.1", "Port": 22011}]},
    "Networking": {"Enabled": True, "UdpDiscoveryEnabled": False, "ChannelPort": 22000, "GamePorts": [22001]},
    "Database": {"Path": str(root / "probe.db")},
    "Pvf": {"ArchivePath": str(root / "client/Script.pvf")},
    "LauncherAuth": {"Enabled": True, "ListenUrl": "http://127.0.0.1:22080",
                     "PublicChannel": {"Host": "127.0.0.1", "Port": 22000}},
    "GmWeb": {"Enabled": False},
    "Game": {"EndpointHost": "127.0.0.1", "DailyResetTime": "06:00:00"},
    "GameLogin": {"Environment": 1, "ConnectionPort": 22011, "EndpointPortA": 22011, "EndpointPortB": 22011},
})
(root / "probe.json").write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
shutil.copy2(workspace / "Server/src/Dfo.Server.Host/Resources/Channel/channel_info.etc", root / "channel_info.etc")
with sqlite3.connect(root / "probe.db") as connection:
    connection.execute("CREATE TABLE initialization_settings(kind TEXT PRIMARY KEY CHECK(kind IN ('account','character')),revision INTEGER NOT NULL,input TEXT NOT NULL,updated_at INTEGER NOT NULL,updated_by TEXT NOT NULL)")
    connection.execute("INSERT INTO initialization_settings VALUES(?,?,?,?,?)", ("character", 1,
        json.dumps({"Level": 60, "PersonalCargoLevel": 8, "Items": []}), int(time.time()*1000), "circus-private-fixture"))
(root / "account.json").write_text(json.dumps({"loginId": "circusprobe", "password": "Circus"+secrets.token_hex(12)}), encoding="utf-8")
client = root / "client"
client.mkdir()
for file in source.iterdir():
    if file.is_file() and (file.suffix.lower() == ".dll" or file.name in
        ("DNF.exe", "DNF.unpacked-b1-debug.exe", "audio.xml", "dstr.dat", "Script.pvf")):
        shutil.copy2(file, client / file.name)
for name in ("Fonts", "ImagePacks2", "Music", "Shader", "SoundPacks", "Video"):
    print(f"Copy private resource directory: {name}", flush=True)
    shutil.copytree(source / name, client / name)
(client / "plugins").mkdir()
for file in (source / "plugins").glob("*.dll"):
    if file.name not in ("0000_MemoryDump.dll", "0050_A21RuntimeFix.dll", "0100_NetworkHook.dll"):
        shutil.copy2(file, client / "plugins" / file.name)
shutil.copy2(workspace / "artifacts/circus-client-trace/build/Release/0050_A21RuntimeFix.dll", client / "plugins/0050_A21RuntimeFix.dll")
(client / "ClientHack.ini").write_text("[ServerRedirect]\nEnabled=0\n[MultiInstance]\nEnabled=1\n[A21CircusTrace]\nEnabled=1\n[A21PickupTrace]\nEnabled=0\n", encoding="utf-8")
def digest(file):
    with file.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()
manifest = {"purpose": "isolated differential research; not gameplay acceptance", "client_source": str(source),
    "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=workspace, text=True).strip(),
    "source_dirty": True, "database": str(root / "probe.db"), "shared_assets": False,
    "client_verified": False, "ports": {"channel": 22000, "backend": 22001, "proxy": 22011, "http": 22080, "control": 22082},
    "hashes": {name: digest(client / name) for name in ("DNF.exe", "DNF.unpacked-b1-debug.exe", "Script.pvf", "plugins/0050_A21RuntimeFix.dll")}}
(root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"prepared": str(root), "client_verified": False}), flush=True)
