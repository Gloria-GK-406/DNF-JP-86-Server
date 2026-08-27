"""Line up our entry batch against the reference session's, frame by frame.

The older comparator windowed the reference by LINE index, so eleven C->S lines fell inside
its window and it showed 55 reference frames against our 66. This one windows by S->C
position: the reference batch is the 66 SEND frames starting at the SELECT_CHARACTER answer.

usage: cmpentry2.py <ours.txt> [out.md]
"""
import re
import sys

REF = r"D:\project\DNF\ServerS4A21\Server\DfoServer\bin\Debug\packet_log.reference-20260823.txt"
ENUMS = r"D:\project\DNF\ServerS4A21\Server\DfoServer\Network\Core\PacketTypesA21.cs"

names = {"CMD": {}, "NOTI": {}}
fam = None
er = re.compile(r"^\s*([A-Za-z_][A-Za-z_0-9]*)\s*=\s*(0[xX][0-9A-Fa-f]+|\d+)\s*,")
for line in open(ENUMS, encoding="utf-8-sig", errors="replace"):
    if "enum CmdPacketType" in line:
        fam = "CMD"
        continue
    if "enum NotiPacketType" in line:
        fam = "NOTI"
        continue
    if fam is None:
        continue
    m = er.match(line)
    if m:
        names[fam].setdefault(int(m.group(2), 0), m.group(1))

h = re.compile(
    r"^(SEND|RECV) cmd=0x([0-9A-Fa-f]+) type=0x([0-9A-Fa-f]+) len=(\d+) body=\((\d+)B\)(.*)")
sent = []
for line in open(REF, encoding="utf-8", errors="replace"):
    m = h.match(line)
    if not m or m.group(1) != "SEND":
        continue
    rest = m.group(6).strip()
    sent.append((
        "CMD" if int(m.group(2), 16) == 1 else "NOTI",
        int(m.group(3), 16),
        int(m.group(5)),
        rest[1:-1].replace(" ", "").lower() if rest.startswith("[") else ""))

# The batch begins at the answer to SELECT_CHARACTER and runs 66 frames.
start = next(i for i, f in enumerate(sent) if f[0] == "CMD" and f[1] == 0x0004)
ref = sent[start:start + 66]

ours = []
seen_select = False
oh = re.compile(r"^#(\d+)\s+(\S+)\s+(\S+)\s+0x([0-9A-Fa-f]{4})\s+\((\d+)B\)\s*(.*)$")
for line in open(sys.argv[1], encoding="utf-8"):
    m = oh.match(line)
    if not m:
        continue
    d, f, t = m.group(2), m.group(3), int(m.group(4), 16)
    n, b = int(m.group(5)), m.group(6).replace(" ", "")
    if d == "C->S" and t == 0x0004:
        seen_select = True
        continue
    if seen_select and d == "S->C":
        ours.append((f, t, n, b))

out = open(sys.argv[2], "w", encoding="utf-8") if len(sys.argv) > 2 else sys.stdout
w = out.write
w("reference: %d frames    ours: %d frames\n\n" % (len(ref), len(ours)))

same_shape = identical = 0
rows = []
for k in range(max(len(ref), len(ours))):
    r = ref[k] if k < len(ref) else None
    o = ours[k] if k < len(ours) else None
    mark = "!"
    if r and o:
        if (r[0], r[1]) != (o[0], o[1]):
            mark = "!"
        elif r[2] != o[2]:
            mark = "~"
            same_shape += 1
        elif r[3] and o[3] and r[3] != o[3]:
            mark = "b"
            same_shape += 1
        else:
            mark = " "
            same_shape += 1
            identical += 1
    rows.append((k, mark, r, o))


def fmt(x):
    if not x:
        return ""
    return "%-4s 0x%04X %-5d %s" % (x[0], x[1], x[2], names.get(x[0], {}).get(x[1], "")[:16])


w("%-4s %-30s %-30s\n" % ("#", "reference", "ours"))
w("-" * 70 + "\n")
for k, mark, r, o in rows:
    w("%-4d%s %-30s %-30s\n" % (k, mark, fmt(r), fmt(o)))
w("\n")
w("same protocol and order : %d of %d\n" % (same_shape, len(ref)))
w("byte for byte identical : %d\n" % identical)
w("differing               : %s\n"
  % ", ".join("#%d %s 0x%04X (%d vs %d)" % (k, r[0], r[1], r[2], o[2])
              for k, mark, r, o in rows if mark in "~b"))
if out is not sys.stdout:
    out.close()
    print("written")
