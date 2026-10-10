"""Read-only quota row audit; preserves the legacy database authority."""
import argparse
import json
import sqlite3
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("database", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
connection = sqlite3.connect(args.database.resolve().as_uri() + "?mode=ro", uri=True)
try:
    connection.execute("PRAGMA query_only=ON")
    result = {
        "database": str(args.database.resolve()),
        "mirror": connection.execute(
            "SELECT character_id,day,entries,clears,daily_reward_claimed "
            "FROM character_mirror_daily ORDER BY character_id,day").fetchall(),
        "counters": connection.execute(
            "SELECT character_id,counter_key,period,value,last_consumed_at "
            "FROM character_daily_counters ORDER BY character_id,counter_key").fetchall(),
    }
finally:
    connection.close()
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"mirrorRows": len(result["mirror"]), "counterRows": len(result["counters"])}))
