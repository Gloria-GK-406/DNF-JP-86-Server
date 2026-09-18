#!/usr/bin/env python3
"""Join the static identity census with the upstream server's protocol name enums.

The client's own type-indexed diagnostic tables live in the runtime data region, which
is all zeroes in a code dump, so the binary cannot tell us what a protocol is called.
The upstream server tree carries two enums that can: `CmdPacketType` and
`NotiPacketType`. The A21 source contains 1272 CMD members and 1219 NOTI members; the
initial connection frame carries their respective END values 1271 and 1218. Spot checks
against protocols this repository has verified end to end -- including numbers that mean
different things in each family, such as 0x01BA MERCENARY_INFO vs GROWTH_WEAPON_EVENT_INFO
-- agree with no off-by-one. So the enum value is the wire type directly. Member count,
END value and actually registered identity count remain separate metrics.

Names are evidence of what a protocol is called, not of what it does. Upstream comments
and handler behaviour are a separate, weaker class of evidence, deliberately not merged
here.

Document assignment follows protocol identity rather than which client artifact was
found. A CMD's request and its response share one type and one exchange, so a single
C->S document owns both; the S->C CMD rows contribute the response-side handler to that
same document. NOTI is server-push only and owns the S->C documents. That is why the
C->S document set is the union of the two CMD directions and not just the builder census.

File naming follows from that. C->S is entirely CMD -- the client's one outbound header
builder writes command 0x01 as an immediate -- so a bare type number is unambiguous there
and the existing hand-written documents keep their paths. S->C carries both families over
one shared number space, so its names must state the family or NOTI 0x0001 and the CMD
0x0001 response would claim the same file.

External input: set ``DNF_A21_ENUMS_PATH`` when the historical upstream checkout path
below is not available.

usage: build-inventory.py [identity.tsv] [out.tsv]
"""
import os
import sys
import re
import collections

IDENTITY = sys.argv[1] if len(sys.argv) > 1 else "protocol-identity.tsv"
OUT = sys.argv[2] if len(sys.argv) > 2 else "protocol-inventory.tsv"
# Names come from the client's own enum table, so this has to follow whichever client
# the census was run against. A21 currently. The A12 table is
#   D:\project\86JP\Server\DfoServer\Network\Core\PacketTypes.cs
# and the two disagree on 375 NOTI and 361 CMD names -- pointing this at the wrong one
# mislabels a third of the tree and does not fail while doing it.
ENUMS = os.environ.get(
    "DNF_A21_ENUMS_PATH",
    r"D:\project\DNF\ServerS4A21\Server\DfoServer\Network\Core\PacketTypesA21.cs",
)

if not os.path.isfile(ENUMS):
    sys.exit(
        "A21 enum source not found: %s\nSet DNF_A21_ENUMS_PATH to "
        "ServerS4A21/Server/DfoServer/Network/Core/PacketTypesA21.cs." % ENUMS
    )

# ------------------------------------------------------------------ upstream names
names = {"CMD": {}, "NOTI": {}}
family = None
enum_re = re.compile(r"^\s*([A-Za-z_][A-Za-z_0-9]*)\s*=\s*(0[xX][0-9A-Fa-f]+|\d+)\s*,")
for line in open(ENUMS, encoding="utf-8-sig", errors="replace"):
    if "enum CmdPacketType" in line:
        family = "CMD"
        continue
    if "enum NotiPacketType" in line:
        family = "NOTI"
        continue
    if family is None:
        continue
    m = enum_re.match(line)
    if m:
        value = int(m.group(2), 0)
        names[family].setdefault(value, m.group(1))

# ------------------------------------------------------------------ identity census
rows = []
with open(IDENTITY) as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        parts = line.rstrip("\n").split("\t")
        row = dict(zip(header, parts))
        if row.get("type") == "?" or row.get("family") == "?":
            continue          # unresolved-site bookkeeping rows, not protocol identities
        rows.append(row)

c2s_build = {}     # type -> row (client builder sites)
s2c_cmd = {}       # type -> row (client response handler)
s2c_noti = {}      # type -> row (client notification handler)
for r in rows:
    t = int(r["type"], 16)
    if r["direction"] == "C2S":
        c2s_build[t] = r
    elif r["family"] == "CMD":
        s2c_cmd[t] = r
    else:
        s2c_noti[t] = r

# ------------------------------------------------------------------ document set
out = ["doc\tdirection\tfamily\ttype\tname\tname_source\tbuilder_sites\tbuilder_region"
       "\thandler_rva\thandler_region\thandler_path\tidentity_note"]

cmd_types = sorted(set(c2s_build) | set(s2c_cmd))
for t in cmd_types:
    b, h = c2s_build.get(t), s2c_cmd.get(t)
    name = names["CMD"].get(t, "")
    if b and h:
        note = "request+response"
    elif b:
        note = "request-only (no client response handler)"
    else:
        note = "response-handler-only (builder not found in .text; likely obfuscated region)"
    out.append("\t".join([
        "game/client-to-server/0x%04X.md" % t, "C2S", "CMD", "0x%04X" % t,
        name, "upstream CmdPacketType" if name else "none",
        b["all_sites"] if b else "", b["region"] if b else "",
        h["primary"] if h else "", h["region"] if h else "",
        h.get("paths", "") if h else "",
        note,
    ]))

for t in sorted(s2c_noti):
    h = s2c_noti[t]
    name = names["NOTI"].get(t, "")
    out.append("\t".join([
        "game/server-to-client/NOTI-0x%04X.md" % t, "S2C", "NOTI", "0x%04X" % t,
        name, "upstream NotiPacketType" if name else "none",
        "", "", h["primary"], h["region"],
        h.get("paths", ""),
        "notification (server push only)",
    ]))

with open(OUT, "w", encoding="utf-8", newline="\n") as stream:
    stream.write("\n".join(out) + "\n")

named = sum(1 for line in out[1:] if line.split("\t")[4])
print("upstream enums        : CMD %d entries, NOTI %d entries"
      % (len(names["CMD"]), len(names["NOTI"])))
print("C->S documents        : %d  (builder %d + response-handler-only %d)"
      % (len(cmd_types), len(c2s_build), len(cmd_types) - len(c2s_build)))
print("   both directions    : %d" % len(set(c2s_build) & set(s2c_cmd)))
print("S->C documents (NOTI) : %d" % len(s2c_noti))
print("total documents       : %d" % (len(out) - 1))
print("named                 : %d / %d  (%.1f%%)"
      % (named, len(out) - 1, 100.0 * named / (len(out) - 1)))
unnamed = [line.split("\t")[3] for line in out[1:] if not line.split("\t")[4]]
if unnamed:
    print("unnamed types         : %s%s"
          % (", ".join(unnamed[:12]), " ..." if len(unnamed) > 12 else ""))
print("wrote %s" % OUT)
