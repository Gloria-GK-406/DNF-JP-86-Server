"""Print every quest row a character owns, in an order that does not depend on the database.

usage: questrows.py <path to dfo-server.db>
"""
import sqlite3, sys

db = sys.argv[1]
c = sqlite3.connect("file:%s?mode=ro" % db.replace("\\", "/"), uri=True)
for table, order in (
    ("character_active_quests", "character_id, slot"),
    ("character_quest_notify_selections", "character_id, slot_index"),
):
    try:
        rows = c.execute("SELECT * FROM %s ORDER BY %s" % (table, order)).fetchall()
        cols = [d[0] for d in c.execute("SELECT * FROM %s LIMIT 0" % table).description]
    except sqlite3.Error as e:
        print("%s: %s" % (table, e))
        continue
    print("%s (%s): %d row(s)" % (table, ", ".join(cols), len(rows)))
    for r in rows:
        print("   %s" % (r,))
c.close()
