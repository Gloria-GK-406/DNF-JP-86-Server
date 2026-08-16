#!/usr/bin/env python3
"""Does an S->C handler ever read the frame body?

Handlers do not receive the frame buffer as an argument; they pull fields from
a global inbound stream through a small set of read primitives (see
patterns/game-receive.md). So "does this handler parse a body" reduces to "can
this handler reach a read primitive". This walks the static call graph from each
entry to a bounded depth, decoding every function linearly until int3 padding.

Two things make the *negative* answer usable rather than merely suggestive:

  * Positive controls. Pass a handler already known to parse a body in the same
    invocation. If a known reader comes back clean, the walk is broken, not the
    handler. Known-good controls: 0x008C7E20 (CMD 0x0001 LOGIN, ReadU8) and
    0x00936740 (NOTI 0x00C5, ReadDString).

  * Declared blind spots. Parts of the image are packed; calls landing at or
    above OPAQUE_LO cannot be followed. Those targets are counted and printed
    with the negative result, so the reader can judge how much the walk missed
    instead of taking "no reads" at face value.

Used to establish that NOTI 0x00AD discards its body while NOTI 0x01C7 and
0x0187 parse theirs -- see patterns/game-settings.md.

RVA/VA convention as everywhere in this repository: the dump is indexed by RVA,
RVA = VA - 0x00400000.

Usage:  python readreach.py <max_depth> <entry_rva> [<entry_rva> ...]
"""
import sys, struct, collections
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

DUMP = r"D:\project\DNF\DNFClient-debug\dumps\dnf-dump-20260814-184532-552-3888\DNF-memory-rva.bin"
data = open(DUMP, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)

READS = {0x01D00420: "ReadU8", 0x01D00490: "ReadU16", 0x01D00500: "ReadU32",
         0x01D00570: "ReadBytes", 0x01C92000: "ReadDString", 0x01C920A0: "ReadDStringW"}
OPAQUE_LO = 0x02300000          # beyond here the image is packed; calls are unfollowable
MAXDEPTH = int(sys.argv[1])


def calls_from(fn, cap=400):
    """Linear decode from fn until int3 padding or ret; collect direct call/jmp targets.

    Tail jumps count as calls: several handlers end in `jmp <sender>` and the
    body-reading question does not care about the distinction.
    """
    out, off, n = [], fn, 0
    while n < cap:
        ins = list(md.disasm(data[off:off + 16], off, count=1))
        if not ins:
            break
        i = ins[0]
        if i.mnemonic == "int3":
            break
        if i.mnemonic in ("call", "jmp") and i.op_str.startswith("0x"):
            out.append(int(i.op_str, 16))
        if i.mnemonic == "ret":
            break
        off += i.size
        n += 1
    return out


for spec in sys.argv[2:]:
    entry = int(spec, 16)
    seen, opaque, found = set(), set(), None
    q = collections.deque([(entry, [])])
    while q and not found:
        fn, path = q.popleft()
        if fn in seen or len(path) > MAXDEPTH:
            continue
        seen.add(fn)
        for t in calls_from(fn):
            if t in READS:
                found = path + ["%s(0x%08X)" % (READS[t], t)]
                break
            if t >= OPAQUE_LO or t >= len(data):
                opaque.add(t)
                continue
            q.append((t, path + ["0x%08X" % t]))
    print("=== handler 0x%08X (depth<=%d, %d functions walked) ===" % (entry, MAXDEPTH, len(seen)))
    if found:
        print("   READS via: " + " -> ".join(found))
    else:
        print("   no read primitive reachable")
        if opaque:
            print("   blind spots (calls into packed region): " +
                  ", ".join("0x%08X" % t for t in sorted(opaque)[:8]) +
                  (" ... +%d" % (len(opaque) - 8) if len(opaque) > 8 else ""))
