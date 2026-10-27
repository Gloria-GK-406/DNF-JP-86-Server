"""After authorized main stop, take a final SQLite backup and use its private clone."""
import json
from pathlib import Path
import shutil
import sqlite3

root = Path(__file__).resolve().parents[3] / "artifacts/circus-probe"
snapshot = Path((root / "main-snapshot-path.txt").read_text().strip())
original = json.loads((snapshot / "restore.json").read_text())
source = Path(original["database"]).resolve(strict=True)
final = snapshot / "main-after-stop.db"
target = root / "default-probe.db"
if final.exists() or target.exists():
    raise SystemExit("Final snapshot/private database already exists; inspect rather than overwrite")
with sqlite3.connect(source.as_uri()+"?mode=ro", uri=True) as live, sqlite3.connect(final) as backup:
    live.backup(backup)
    if backup.execute("PRAGMA quick_check").fetchone()[0] != "ok":
        raise ValueError("Final snapshot failed quick_check")
shutil.copy2(final, target)
config = json.loads((root / "probe.json").read_text())
config["Research"] = {"UseDefaultPorts": True}
config["Networking"]["ChannelPort"] = 7001
config["Channel"]["Channels"][0]["Name"] = "#ch.11"
config["Channel"]["Channels"][0]["Port"] = 10011
config["LauncherAuth"]["ListenUrl"] = "http://127.0.0.1:18080"
config["LauncherAuth"]["PublicChannel"]["Port"] = 7001
config["GameLogin"] = {"Environment": 1, "ConnectionPort": 10011, "EndpointPortA": 10011, "EndpointPortB": 10011}
config["Database"]["Path"] = str(target)
(root / "default-probe.json").write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"private_database": str(target), "final_snapshot": str(final), "quick_check": "ok"}))
