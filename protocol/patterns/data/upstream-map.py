#!/usr/bin/env python3
"""Map protocol types to the upstream server code that handles or builds them.

Field meaning cannot come from the client binary: the instruction stream gives widths and
order, never what a number represents. The upstream server is the one place where these
protocols are read and written by code with names attached, so this locates, per type,
which upstream files to read.

Three call shapes carry a type in that tree:

    d[0x0005] = handler                      inbound dispatch registration
    CmdPacketType.CREATE_CHARACTER           symbolic reference in any file
    Build(0x00, 0x00AD, body)                outbound envelope construction

The result is a work list, not evidence. A file appearing here means it mentions the
type, which is where to look; whether it actually parses a given offset is for the reader
to establish from the code.

usage: upstream-map.py <inventory.tsv> <out.tsv>
"""
import sys
import os
import re
import collections

INVENTORY, OUT = sys.argv[1], sys.argv[2]
UPSTREAM = r"D:\project\86JP\Server\DfoServer"
ENUM_FILE = "PacketTypes.cs"

names = {}          # (family, value) -> NAME   and NAME -> (family, value)
by_name = {}
family = None
enum_re = re.compile(r"^\s*([A-Za-z_][A-Za-z_0-9]*)\s*=\s*(0[xX][0-9A-Fa-f]+|\d+)\s*,")
for line in open(os.path.join(UPSTREAM, "Network", "Core", ENUM_FILE),
                 encoding="utf-8-sig", errors="replace"):
    if "enum CmdPacketType" in line:
        family = "CMD"
    elif "enum NotiPacketType" in line:
        family = "NOTI"
    elif family:
        m = enum_re.match(line)
        if m:
            names[(family, int(m.group(2), 0))] = m.group(1)
            by_name.setdefault(m.group(1), []).append((family, int(m.group(2), 0)))

dispatch_re = re.compile(r"\bd\[\s*(0[xX][0-9A-Fa-f]{1,4})\s*\]\s*=")
symbol_re = re.compile(r"\b(Cmd|Noti)PacketType\.([A-Za-z_][A-Za-z_0-9]*)")
build_re = re.compile(r"Build\w*\(\s*(0[xX]0[01])\s*,\s*(0[xX][0-9A-Fa-f]{1,4})\s*,")

hits = collections.defaultdict(lambda: collections.defaultdict(set))   # key -> file -> kinds

for root, _dirs, files in os.walk(UPSTREAM):
    for fn in files:
        if not fn.endswith(".cs") or fn == ENUM_FILE:
            continue
        path = os.path.join(root, fn)
        rel = os.path.relpath(path, UPSTREAM).replace("\\", "/")
        try:
            text = open(path, encoding="utf-8-sig", errors="replace").read()
        except OSError:
            continue
        for m in dispatch_re.finditer(text):
            hits[("CMD", int(m.group(1), 16))][rel].add("dispatch")
        for m in symbol_re.finditer(text):
            fam = "CMD" if m.group(1) == "Cmd" else "NOTI"
            for f, v in by_name.get(m.group(2), []):
                if f == fam:
                    hits[(fam, v)][rel].add("symbol")
        for m in build_re.finditer(text):
            fam = "CMD" if int(m.group(1), 16) == 1 else "NOTI"
            hits[(fam, int(m.group(2), 16))][rel].add("build")

rows = ["family\ttype\tname\tkinds\tfiles"]
inventory_keys = set()
with open(INVENTORY, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        r = dict(zip(header, line.rstrip("\n").split("\t")))
        inventory_keys.add((r["family"], int(r["type"], 16)))

covered = 0
for key in sorted(hits):
    if key not in inventory_keys:
        continue
    fam, v = key
    files = hits[key]
    kinds = sorted({k for ks in files.values() for k in ks})
    rows.append("%s\t0x%04X\t%s\t%s\t%s"
                % (fam, v, names.get(key, ""), ",".join(kinds),
                   ",".join(sorted(files)[:6])))
    covered += 1

open(OUT, "w", encoding="utf-8").write("\n".join(rows) + "\n")
print("types referenced by upstream code : %d" % covered)
per_family = collections.Counter(k[0] for k in hits if k in inventory_keys)
for fam, n in sorted(per_family.items()):
    print("   %-5s %d" % (fam, n))
print("wrote %s" % OUT)
