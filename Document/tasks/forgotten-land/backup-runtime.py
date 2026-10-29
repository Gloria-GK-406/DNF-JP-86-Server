"""SQLite backup API snapshot into this task's ignored directory; never edit the source database."""
import hashlib
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("Pass the verified absolute live database path")
source = Path(sys.argv[1]).resolve(strict=True)
private = Path(__file__).resolve().parent / "private"
private.mkdir(exist_ok=True)
target = private / ("player-backup-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ") + ".db")
tables = ("accounts", "characters", "character_items", "items", "item_slots", "character_stats", "character_fatigue", "character_daily_counters")
with sqlite3.connect(source.as_uri() + "?mode=ro", uri=True) as live, sqlite3.connect(target) as snapshot:
    live.execute("BEGIN")
    present = {row[0] for row in live.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    expected = {name: live.execute('SELECT COUNT(*) FROM "' + name + '"').fetchone()[0]
        for name in tables if name in present}
    live.backup(snapshot)
    actual = {name: snapshot.execute('SELECT COUNT(*) FROM "' + name + '"').fetchone()[0] for name in expected}
    integrity = snapshot.execute("PRAGMA quick_check").fetchone()[0]
    live.rollback()
    if actual != expected or integrity != "ok":
        raise RuntimeError("Backup verification failed")
with target.open("rb") as stream:
    digest = hashlib.file_digest(stream, "sha256").hexdigest().upper()
report = {"source": str(source), "backup": str(target), "sha256": digest,
    "verified_counts": actual, "quick_check": integrity, "method": "SQLite backup API, consistent read transaction"}
(private / "backup-manifest.json").write_bytes(json.dumps(report, indent=2).encode("utf-8"))
print(json.dumps(report, indent=2))
