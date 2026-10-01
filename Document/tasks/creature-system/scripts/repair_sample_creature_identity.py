"""Repair only the three proven sample-import mistakes, with exact preconditions and backup.
Stop the server first. Dry-run is default; --apply performs the bounded transaction.
"""
import argparse
import datetime
import pathlib
import sqlite3
import struct

parser = argparse.ArgumentParser()
parser.add_argument("database", type=pathlib.Path)
parser.add_argument("--apply", action="store_true")
args = parser.parse_args()
path = args.database.resolve(strict=True)
expected = {948: (63000, 938), 949: (63003, 939), 950: (63010, 940)}
connection = sqlite3.connect(str(path))
updates = []
for uid, (template, old_identity) in expected.items():
    rows = connection.execute("SELECT i.item_core,s.owner_scope,s.owner_id FROM items i JOIN item_slots s ON s.core_uid=i.item_uid WHERE i.item_uid=?", (uid,)).fetchall()
    if len(rows) != 1:
        raise RuntimeError(f"UID {uid}: expected one held item, got {len(rows)}")
    core, scope, owner = rows[0]
    if len(core) != 82 or core[0] != 5 or struct.unpack_from("<i", core, 1)[0] != template or scope != "character" or owner != 2:
        raise RuntimeError(f"UID {uid}: ownership or template precondition failed")
    current = struct.unpack_from("<i", core, 5)[0]
    if current == uid:
        continue
    if current != old_identity:
        raise RuntimeError(f"UID {uid}: unexpected core identity {current}")
    fixed = bytearray(core)
    struct.pack_into("<i", fixed, 5, uid)
    updates.append((bytes(fixed), uid, core))
print(f"Validated {len(updates)} bounded sample corrections")
if args.apply and updates:
    backup = path.with_name(path.name + ".pet-identity-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S") + ".bak")
    with sqlite3.connect(str(backup)) as target:
        connection.backup(target)
    with connection:
        for fixed, uid, original in updates:
            cursor = connection.execute("UPDATE items SET item_core=? WHERE item_uid=? AND item_core=?", (fixed, uid, original))
            if cursor.rowcount != 1:
                raise RuntimeError(f"UID {uid}: changed during repair")
    print(f"Applied; backup: {backup}")
connection.close()
