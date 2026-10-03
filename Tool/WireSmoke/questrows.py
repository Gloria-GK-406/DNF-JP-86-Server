"""Print every quest row a character owns, in an order that does not depend on the database.

usage: questrows.py <path to dfo-server.db>
"""
import sqlite3, sys
from pathlib import Path

db = sys.argv[1]
c = sqlite3.connect(Path(db).resolve().as_uri() + "?mode=ro", uri=True)
for table, order in (
    ("character_active_quests", "character_id, slot"),
    ("character_quest_notify_selections", "character_id, slot_index"),
):
    rows = c.execute("SELECT * FROM %s ORDER BY %s" % (table, order)).fetchall()
    cols = [d[0] for d in c.execute("SELECT * FROM %s LIMIT 0" % table).description]
    print("%s (%s): %d row(s)" % (table, ", ".join(cols), len(rows)))
    for r in rows:
        print("   %s" % (r,))
c.close()
