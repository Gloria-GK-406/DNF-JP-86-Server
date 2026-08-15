#!/usr/bin/env python3
"""Census of server->client receive handlers by protocol type.

The client dispatches inbound frames through a runtime hash map, so the map
itself cannot be read from a code-only dump. The *registrations* are static
though: three batch functions call

    push <context>
    push <handler fn>
    push <type>
    mov  ecx, <registry>
    call Register            (0x00D89FC0)

once per protocol. Censusing that call recovers the whole type -> handler table
statically.

The pushed handler is an absolute VA, while the dump is indexed by RVA, so it is
reported both ways: RVA = VA - 0x00400000. Reading the immediate as an RVA lands
mid-instruction and makes every handler look like garbage.

Push recovery uses the same forward-alignment voting as the builder census: every
alignment in a window before the call is swept forwards, alignments landing
exactly on the call vote on the last three pushes, and the majority wins.

usage: handlers.py [out.tsv]
"""
import sys
import struct
import collections
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

DUMP = r"D:\project\DNF\DNFClient-debug\dumps\dnf-dump-20260814-184532-552-3888\DNF-memory-rva.bin"
REGISTER = 0x00D89FC0
IMAGE_BASE = 0x00400000    # manifest.json: the dump is indexed by RVA = VA - image_base

data = open(DUMP, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)


def rel32_target(at):
    if data[at] != 0xE8:
        return None
    return at + 5 + struct.unpack("<i", data[at + 1:at + 5])[0]


def pushes_from(start, stop):
    """Forward-decode from `start`; return the pushes reaching `stop`, or None if
    the stream does not land exactly on `stop`."""
    off, seq = start, []
    while off < stop:
        ins = list(md.disasm(data[off:off + 16], off, count=1))
        if not ins or off + ins[0].size > stop:
            return None
        m, op = ins[0].mnemonic, ins[0].op_str
        if m == "push":
            seq.append(op)
        elif m in ("call", "jmp", "ret") or m.startswith("j"):
            seq = []
        off += ins[0].size
    return tuple(seq[-3:]) if off == stop else None


def args_for(call_site, window=48):
    votes = collections.Counter()
    for start in range(call_site - window, call_site):
        seq = pushes_from(start, call_site)
        if seq is not None and len(seq) == 3:
            votes[seq] += 1
    return votes.most_common(1)[0][0] if votes else None


sites = []
i = 0
while True:
    i = data.find(b"\xe8", i, len(data) - 5)
    if i < 0:
        break
    if rel32_target(i) == REGISTER:
        sites.append(i)
    i += 1

rows, unresolved = [], []
for s in sites:
    args = args_for(s)
    if args is None:
        unresolved.append((s, "no-3-pushes"))
        continue
    ctx, fn, ftype = args                       # pushed in reverse argument order
    try:
        ftype_v = int(ftype, 16) if ftype.startswith("0x") else int(ftype)
        fn_v = int(fn, 16) if fn.startswith("0x") else int(fn)
    except ValueError:
        unresolved.append((s, "non-immediate: type=%s fn=%s" % (ftype, fn)))
        continue
    rows.append((ftype_v, fn_v, ctx, s))

rows.sort()
out = sys.argv[1] if len(sys.argv) > 1 else "receive-handlers.tsv"
with open(out, "w") as f:
    f.write("type\thandler_rva\thandler_va\tcontext\tregistration_site_rva\n")
    for ftype, fn, ctx, s in rows:
        f.write("0x%04X\t0x%08X\t0x%08X\t%s\t0x%08X\n"
                % (ftype, fn - IMAGE_BASE, fn, ctx, s))
    for s, why in unresolved:
        f.write("?\t?\t?\t%s\t0x%08X\n" % (why, s))

print("Register call sites : %d" % len(sites))
print("  resolved          : %d" % len(rows))
print("  unresolved        : %d" % len(unresolved))
for s, why in unresolved[:10]:
    print("      0x%08X  %s" % (s, why))
types = [r[0] for r in rows]
dup = [t for t, c in collections.Counter(types).items() if c > 1]
print("distinct types      : %d   range 0x%04X..0x%04X" % (len(set(types)), min(types), max(types)))
print("types registered >1x: %s" % ["0x%04X" % t for t in sorted(dup)])
print("wrote %s" % out)
