#!/usr/bin/env python3
"""Re-key meaning rows whose offset addresses a repeat-group field.

The shard format originally had one offset column, which silently merged a top-level
offset with the identically numbered offset inside a repeat group. Group offsets are now
written `gN:offset`. This repairs rows authored before that: a row whose offset matches no
top-level field but matches exactly one group scope is rewritten to name that scope.

Ambiguity is never resolved by preference. A row matching several groups, or matching
none, is left untouched and reported -- guessing which field a meaning belongs to is the
exact failure the scope prefix exists to prevent.

usage: migrate-meaning-scopes.py <layouts.json> <meanings-dir> [--apply]
"""
import sys
import os
import json
import glob

LAYOUTS, MDIR = sys.argv[1], sys.argv[2]
APPLY = "--apply" in sys.argv
# Layout extraction can change under a shard -- a fixed extractor may split a field
# differently -- leaving a meaning addressing a byte that is no longer a field start.
# Such a row is not merely stale, it is unplaceable, and keeping it would attach a claim
# to whichever field happened to inherit the number. `--prune` removes those rows and
# records them verbatim so the reasoning behind them is not lost.
PRUNE = "--prune" in sys.argv
pruned = []
layouts = json.load(open(LAYOUTS, encoding="utf-8"))


def scope_offsets(entry):
    out, off = {"": set()}, 0
    for f in entry.get("fields", []):
        w = f["len"] if f["width"] is None else f["width"]
        out[""].add(off)
        if w is None:
            break
        off += w
    for i, g in enumerate(entry.get("groups", [])):
        goff, offs = 0, set()
        for f in g["fields"]:
            w = f["len"] if f["width"] is None else f["width"]
            offs.add(goff)
            if w is None:
                break
            goff += w
        out["g%d" % (i + 1)] = offs
    return out


changed = unresolved = 0
for path in sorted(glob.glob(os.path.join(MDIR, "*.tsv"))):
    lines = open(path, encoding="utf-8").read().split("\n")
    out, dirty = [], False
    for n, line in enumerate(lines, start=1):
        p = line.split("\t")
        if n == 1 or len(p) < 6 or not line.strip():
            out.append(line)
            continue
        fam, ty, off = p[0].strip(), p[1].strip(), p[2].strip()
        if ":" in off:
            out.append(line)
            continue
        try:
            key = "%s:0x%04X" % (fam, int(ty, 16))
            off_i = int(off)
        except ValueError:
            out.append(line)
            continue
        entry = layouts.get(key)
        if entry is None:
            out.append(line)
            continue
        scopes = scope_offsets(entry)
        if off_i in scopes.get("", set()):
            out.append(line)
            continue
        hits = [s for s, offs in scopes.items() if s and off_i in offs]
        if len(hits) == 1:
            p[2] = "%s:%d" % (hits[0], off_i)
            out.append("\t".join(p))
            dirty = True
            changed += 1
            print("  %s:%d  %s offset %d -> %s"
                  % (os.path.basename(path), n, key, off_i, p[2]))
        else:
            why = "matches %d groups" % len(hits) if hits else "matches no scope"
            unresolved += 1
            print("  %s:%d  %s offset %d UNRESOLVED (%s)%s"
                  % (os.path.basename(path), n, key, off_i, why,
                     " -> pruned" if PRUNE else ""))
            if PRUNE:
                pruned.append("%s\t%d\t%s\t%s\t%s"
                              % (os.path.basename(path), n, key, why, line))
                dirty = True
            else:
                out.append(line)
    if dirty and APPLY:
        open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out))

if PRUNE and pruned and APPLY:
    # Deliberately outside the shard directory: it is a record, not an input, and the
    # generator and validator both treat every .tsv under meanings/ as a live shard.
    rej = os.path.join(os.path.dirname(MDIR.rstrip(os.sep)), "pruned-meaning-rows.tsv")
    header = "shard\tline\tidentity\treason\toriginal_row\n"
    with open(rej, "a", encoding="utf-8", newline="\n") as f:
        if os.path.getsize(rej) == 0 if os.path.exists(rej) else True:
            f.write(header)
        f.write("\n".join(pruned) + "\n")
    print("pruned rows recorded in %s" % rej)

print("re-keyed   : %d%s" % (changed, "" if APPLY else "  (dry run; pass --apply)"))
print("unresolved : %d" % unresolved)
