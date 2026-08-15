#!/usr/bin/env python3
"""Census of client->server frame builders by protocol type.

Anchors on every direct `call MakeHeader` (0x1C90460) and recovers the frame type
from the last `push` that reaches it. Two builder shapes exist:

    push <type>                        push <type>
    call GetWriter   (0x1C8F5B0)       mov  ecx, <writer-param>
    mov  ecx, eax                      call MakeHeader
    call MakeHeader

The second shape receives the writer as a function parameter instead of fetching
it; 0x0117's builder at 0x01CD5300 is one. Anchoring on MakeHeader alone covers
both. The GetWriter call is treated as transparent when scanning for the push,
because it consumes that very push and returns the writer.

x86 cannot be decoded backwards unambiguously, so every alignment in a window
before the call is swept *forwards*; alignments whose stream lands exactly on the
call are "synced" and vote on which push is last. Majority wins -- a desynced
alignment must coincide with the real one to affect the outcome.

Three weaker rules were tried and rejected:
  * requiring the push to sit immediately before the call misses the 252 sites
    where the compiler interleaved an unrelated store;
  * "last push within 32 bytes, any alignment" mis-attributes a push from another
    code path -- it read type 0x2AFF at 0x00E40D75, where the type is in ebx;
  * a single backward walk desyncs and silently gives up (168 sites).

A push of a register means the type is computed at runtime; such sites are
reported as `register` rather than guessed at.

usage: census.py [out.tsv]
"""
import sys
import struct
import collections
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

DUMP = r"D:\project\DNF\DNFClient-debug\dumps\dnf-dump-20260814-184532-552-3888\DNF-memory-rva.bin"
GET_WRITER = 0x1C8F5B0
MAKE_HEADER = 0x1C90460
CODE_HI = 0x01D10000      # everything confirmed as .text lives below this

data = open(DUMP, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)


def rel32_target(at):
    if data[at] != 0xE8:
        return None
    return at + 5 + struct.unpack("<i", data[at + 1:at + 5])[0]


def classify_push(ins):
    op = ins.op_str
    if op.startswith("0x"):
        return ("imm", int(op, 16))
    if op.isdigit():
        return ("imm", int(op))
    if len(op) == 3 and op[0] in "re":
        return ("register", None)
    return ("indirect", None)


def last_push_from(start, stop):
    """Decode forward from `start`; return the last push before `stop`, or None if
    the instruction stream does not land exactly on `stop` (wrong alignment)."""
    off, last = start, None
    while off < stop:
        ins = list(md.disasm(data[off:off + 16], off, count=1))
        if not ins or off + ins[0].size > stop:
            return None
        m, op = ins[0].mnemonic, ins[0].op_str
        if m == "push":
            last = ins[0]
        elif m == "call" and op == "0x%x" % GET_WRITER:
            pass                 # GetWriter consumes the type push and returns the writer;
                                 # the push still belongs to this MakeHeader call
        elif m in ("call", "jmp", "ret") or m.startswith("j"):
            last = None          # other control flow: any earlier push belongs to another block
        off += ins[0].size
    return last if off == stop else None


def type_operand(makeheader_call, window=64):
    """Recover the type pushed for this GetWriter call.

    x86 cannot be decoded backwards unambiguously, so instead every alignment in
    a window before the call is swept forwards; alignments whose stream lands
    exactly on the call are 'synced'. Synced alignments vote on which push is the
    last one, and the majority wins -- a desynced alignment has to agree with the
    real one to affect the result, which is far stronger than a single backward walk.
    """
    votes = collections.Counter()
    for start in range(makeheader_call - window, makeheader_call):
        push = last_push_from(start, makeheader_call)
        if push is not None:
            votes[classify_push(push)] += 1
    if not votes:
        return ("no-push-in-block", None)
    return votes.most_common(1)[0][0]


sites = []
i = 0
while True:
    i = data.find(b"\xe8", i, len(data) - 5)
    if i < 0:
        break
    if rel32_target(i) == MAKE_HEADER:
        sites.append(i)
    i += 1

typed = {}
other = collections.defaultdict(list)
for s in sites:
    kind, value = type_operand(s)
    if kind == "imm":
        typed.setdefault(value, []).append(s)
    else:
        other[kind].append(s)

rows = ["type\tsites\tnote\taddresses"]
for t in sorted(typed):
    addrs = sorted(typed[t])
    if t > 0x0FFF:
        note = "suspect-type-out-of-range"
    elif all(a >= CODE_HI for a in addrs):
        note = "outside-text"
    elif any(a >= CODE_HI for a in addrs):
        note = "mixed-region"
    else:
        note = "ok"
    rows.append("0x%04X\t%d\t%s\t%s" % (t, len(addrs), note, ",".join("0x%08X" % a for a in addrs)))
for kind in sorted(other):
    addrs = sorted(other[kind])
    rows.append("?\t%d\t%s\t%s" % (len(addrs), kind, ",".join("0x%08X" % a for a in addrs)))

out = sys.argv[1] if len(sys.argv) > 1 else "builder-sites.tsv"
open(out, "w").write("\n".join(rows) + "\n")

resolved = sum(len(v) for v in typed.values())
print("MakeHeader call sites   : %d" % len(sites))
print("  type resolved         : %d sites / %d distinct types" % (resolved, len(typed)))
for kind in sorted(other):
    print("  %-21s : %d  %s" % (kind, len(other[kind]),
                                ",".join("0x%08X" % a for a in sorted(other[kind])[:8])))
inrange = [t for t in typed if t <= 0x0FFF]
print("types <= 0x0FFF         : %d   range 0x%04X..0x%04X" % (len(inrange), min(inrange), max(inrange)))
print("distinct types in .text : %d" % len({t for t, v in typed.items() if any(a < CODE_HI for a in v)}))
print("sites per type          : %s" % dict(sorted(collections.Counter(len(v) for v in typed.values()).items())))
print("wrote %s" % out)
