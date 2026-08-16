#!/usr/bin/env python3
"""Validate field-meaning shards against the extracted layouts.

Enrichment is the one stage where a human or an agent can assert something the binary
does not say, so it is the one stage that needs a gate. Every check here rejects a claim
that cannot be placed: an offset that no extracted field starts at is not a field, and a
protocol with no field table cannot have field meanings at all.

What this cannot check is whether a meaning is *true*. It only proves a claim is
well-formed, addressable, graded, and sourced -- the reviewer judges the rest.

usage: check-meanings.py <layouts.json> <meanings-dir>
"""
import sys
import os
import json
import glob
import collections

LAYOUTS, MDIR = sys.argv[1], sys.argv[2]
layouts = json.load(open(LAYOUTS, encoding="utf-8"))
LEVELS = {"Confirmed", "High", "Hypothesis"}


def canonical_type(raw):
    """Normalise a hand-written type to the `0xABCD` form the layout keys use.

    Naive uppercasing turns `0x02bc` into `0X02BC`, which matches nothing and silently
    rejects every row -- the shards are hand-written, so the case of both the prefix and
    the digits varies and neither may decide whether a claim is accepted."""
    t = raw.strip()
    try:
        return "0x%04X" % int(t, 16)
    except ValueError:
        return None


def valid_offsets(entry):
    """Field-start offsets per scope: `""` for top level, `g1`, `g2`… inside repeat groups.

    Group offsets restart at zero, so they must be addressed separately or one meaning
    would be accepted for two unrelated fields that happen to share a number."""
    out = {"": set()}
    off = 0
    for f in entry.get("fields", []):
        w = f["len"] if f["width"] is None else f["width"]
        out[""].add(off)
        if w is None:
            out[""] = None
            break
        off += w
    for i, g in enumerate(entry.get("groups", [])):
        scope, goff, offs = "g%d" % (i + 1), 0, set()
        for f in g["fields"]:
            w = f["len"] if f["width"] is None else f["width"]
            offs.add(goff)
            if w is None:
                offs = None
                break
            goff += w
        out[scope] = offs
    return out


errors, rows, per_shard = [], 0, collections.Counter()
for path in sorted(glob.glob(os.path.join(MDIR, "*.tsv"))):
    shard = os.path.basename(path)
    with open(path, encoding="utf-8") as f:
        head = f.readline().rstrip("\n").split("\t")
        if head[:6] != ["family", "type", "offset", "meaning", "confidence", "source"]:
            errors.append("%s: bad header %s" % (shard, head))
            continue
        for lineno, line in enumerate(f, start=2):
            if not line.strip():
                continue
            p = line.rstrip("\n").split("\t")
            where = "%s:%d" % (shard, lineno)
            if len(p) < 6:
                errors.append("%s: needs 6 columns, has %d" % (where, len(p)))
                continue
            fam, ty, off, meaning, conf, src = (x.strip() for x in p[:6])
            if fam not in ("CMD", "NOTI"):
                errors.append("%s: family '%s' is not CMD/NOTI" % (where, fam))
                continue
            canon = canonical_type(ty)
            if canon is None:
                errors.append("%s: type '%s' is not hexadecimal" % (where, ty))
                continue
            key = "%s:%s" % (fam, canon)
            entry = layouts.get(key)
            if entry is None:
                errors.append("%s: %s is not a known protocol identity" % (where, key))
                continue
            if entry["state"] == "structure-unknown":
                errors.append("%s: %s has no field table (structure-unknown); "
                              "a field meaning cannot be placed" % (where, key))
                continue
            scope, _, raw_off = off.rpartition(":")
            try:
                off_i = int(raw_off)
            except ValueError:
                errors.append("%s: offset '%s' is not an integer or `gN:offset`"
                              % (where, off))
                continue
            scopes = valid_offsets(entry)
            if scope not in scopes:
                errors.append("%s: %s has no scope '%s' (available: %s)"
                              % (where, key, scope or "top-level",
                                 ", ".join(s or "top-level" for s in sorted(scopes))))
                continue
            ok = scopes[scope]
            if ok is not None and off_i not in ok:
                errors.append("%s: %s scope '%s' has no field starting at offset %d "
                              "(starts: %s)"
                              % (where, key, scope or "top-level", off_i,
                                 ",".join(map(str, sorted(ok)[:12]))))
                continue
            if conf not in LEVELS:
                errors.append("%s: confidence '%s' not in %s"
                              % (where, conf, "/".join(sorted(LEVELS))))
                continue
            if not meaning:
                errors.append("%s: empty meaning; omit the row instead" % where)
                continue
            if not src:
                errors.append("%s: meaning without a source" % where)
                continue
            rows += 1
            per_shard[shard] += 1

print("shards          : %d" % len(glob.glob(os.path.join(MDIR, "*.tsv"))))
print("valid rows      : %d" % rows)
for shard, n in sorted(per_shard.items()):
    print("   %-46s %4d" % (shard, n))
print("errors          : %d" % len(errors))
for e in errors[:40]:
    print("   %s" % e)
if len(errors) > 40:
    print("   ... +%d more" % (len(errors) - 40))
sys.exit(1 if errors else 0)
