"""List or merge split piles left behind by #314.

A pile is "split" when one list of one owner holds several template slots whose
item_core is identical except for the 4-byte count (bytes 5..8). The pile count
lives twice: item_slots.count and the count field of items.item_core; both are
updated together.

Usage:
  python -I repair_split_piles.py DB                     # list every split group (read-only)
  python -I repair_split_piles.py DB --apply --scope character --owner 9 --list 2
      # merge the groups of that one list into each group's lowest slot

Run --apply only with the server stopped (or the owner offline) and after a
backup. Stack limits are not known here: the listing shows each merged total so
it can be checked against the PVF before applying.
"""
import argparse
import sqlite3
import struct
import sys
from collections import defaultdict

COUNT = slice(5, 9)


def groups(db, scope=None, owner=None, list_type=None):
    sql = """SELECT s.owner_scope, s.owner_id, s.list_type, s.slot_index, s.count, s.core_uid, i.item_core
             FROM item_slots s JOIN items i ON i.item_uid = s.core_uid
             WHERE s.kind = 'template' AND i.is_mirror = 0"""
    args = []
    if scope is not None:
        sql += " AND s.owner_scope = ? AND s.owner_id = ? AND s.list_type = ?"
        args = [scope, owner, list_type]
    found = defaultdict(list)
    for row in db.execute(sql, args):
        sc, ow, lt, slot, count, uid, core = row
        core = bytes(core)
        if struct.unpack_from("<i", core, 5)[0] != count:
            raise SystemExit(f"count mismatch at {sc}:{ow}:{lt}:{slot}")
        key = (sc, ow, lt, core[:5] + core[9:])
        found[key].append((slot, count, uid, core))
    return {k: sorted(v) for k, v in found.items() if len(v) > 1}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("db")
    p.add_argument("--apply", action="store_true")
    p.add_argument("--scope", choices=["character", "account"])
    p.add_argument("--owner", type=int)
    p.add_argument("--list", type=int, dest="list_type")
    a = p.parse_args()
    if a.apply and (a.scope is None or a.owner is None or a.list_type is None):
        p.error("--apply needs --scope, --owner and --list")

    uri = f"file:{a.db}?mode={'rw' if a.apply else 'ro'}"
    db = sqlite3.connect(uri, uri=True)
    found = groups(db, a.scope, a.owner, a.list_type) if a.scope else groups(db)
    for (sc, ow, lt, _), piles in sorted(found.items(), key=lambda kv: kv[0][:3]):
        item = struct.unpack_from("<i", piles[0][3], 1)[0]
        detail = ", ".join(f"slot {s}={c}" for s, c, _, _ in piles)
        print(f"{sc} {ow} list {lt} item {item}: {detail} -> total {sum(c for _, c, _, _ in piles)}")
    print(f"{len(found)} split group(s)")
    if not a.apply or not found:
        return

    with db:
        for piles in found.values():
            slot, _, keep_uid, core = piles[0]
            total = sum(c for _, c, _, _ in piles)
            if total > 0x7FFFFFFF:
                raise SystemExit(f"total overflows at slot {slot}")
            merged = bytearray(core)
            struct.pack_into("<i", merged, 5, total)
            db.execute("UPDATE items SET item_core = ?, updated_at = CURRENT_TIMESTAMP WHERE item_uid = ?",
                       (bytes(merged), keep_uid))
            db.execute("UPDATE item_slots SET count = ? WHERE core_uid = ?", (total, keep_uid))
            for _, _, uid, _ in piles[1:]:
                db.execute("DELETE FROM item_slots WHERE core_uid = ?", (uid,))
                if db.execute("SELECT COUNT(*) FROM item_slots WHERE core_uid = ?", (uid,)).fetchone()[0] == 0:
                    db.execute("DELETE FROM items WHERE item_uid = ?", (uid,))
    print("merged", file=sys.stderr)


if __name__ == "__main__":
    main()
