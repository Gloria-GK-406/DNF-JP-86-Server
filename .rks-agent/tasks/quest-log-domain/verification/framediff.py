"""Compare two entry-batch logs frame by frame and say exactly where they differ.

Two runs of the same build never produce identical bytes: the server stamps the time and
mints a fresh instance id for every item it creates. So the question a diff has to answer is
not "are they equal" but "do they differ anywhere the run itself does not explain".

usage: framediff.py a.txt b.txt
"""
import re
import sys

HEAD = re.compile(r"^#(\d+)\s+(\S+)\s+(\S+)\s+0x([0-9A-Fa-f]{4})\s+\((\d+)B\)\s*(.*)$")


def read(path):
    frames = []
    for line in open(path, encoding="utf-8"):
        m = HEAD.match(line)
        if not m:
            continue
        frames.append((m.group(2), m.group(3), int(m.group(4), 16), int(m.group(5)),
                       bytes.fromhex(m.group(6).replace(" ", ""))))
    return frames


a, b = read(sys.argv[1]), read(sys.argv[2])
print("%s: %d frames    %s: %d frames" % (sys.argv[1], len(a), sys.argv[2], len(b)))
if len(a) != len(b):
    print("*** different frame counts")
    sys.exit(1)

shape = [i for i in range(len(a)) if a[i][:4] != b[i][:4]]
if shape:
    print("*** frames whose direction, family, type or length differ: %s" % shape)
    sys.exit(1)
print("every frame has the same direction, family, type and length")

runs = []
for i, (fa, fb) in enumerate(zip(a, b)):
    if fa[4] == fb[4]:
        continue
    offsets = [k for k in range(len(fa[4])) if fa[4][k] != fb[4][k]]
    # Collapse consecutive offsets into runs; a changed field is a run, not n bytes.
    grouped, start = [], offsets[0]
    for prev, cur in zip(offsets, offsets[1:] + [None]):
        if cur != prev + 1:
            grouped.append((start, prev - start + 1))
            start = cur
    runs.append((i, fa[1], fa[2], grouped))

if not runs:
    print("and every frame is byte for byte the same")
    sys.exit(0)

print()
print("%-5s %-5s %-8s %s" % ("#", "fam", "type", "differing runs (offset+length)"))
print("-" * 70)
for i, fam, typ, grouped in runs:
    print("%-5d %-5s 0x%04X   %s"
          % (i, fam, typ, ", ".join("%d+%d" % g for g in grouped)))
print()
print("%d of %d frames differ" % (len(runs), len(a)))
