#!/usr/bin/env python3
"""Authoritative Game protocol identity census, both directions and both families.

This supersedes the two earlier single-purpose censuses by resolving the one thing
they each left open: which of the client's two registries a handler belongs to, and
the 439 registrations that happen through runtime forwarders rather than the startup
batches. Identity here means the full quadruple endpoint + direction + family + type,
because the client really does keep two unrelated hash tables and a bare type number
does not select a protocol.

Three anchors, all direct `call` sites, all recovered with the same forward-alignment
voting used by the earlier censuses (x86 cannot be decoded backwards unambiguously, so
every alignment in a window before the call is swept forwards and those landing exactly
on the call vote on the operands):

  MakeHeader  0x01C90460   push <type>                      -> C->S, always CMD
  Register    0x00D89FC0   push <ctx>/<handler>/<type>       -> S->C, family by batch
  forwarders  0x00D855C0   push <ctx>/<handler>/<type>       -> S->C table A (CMD)
              0x00D85680   ... then a 0-arg thunk            -> S->C table B (NOTI)

Family attribution differs per path and is the reason this script exists:

  * Direct `Register` sites carry the registry in `ecx`, not as an immediate, so the
    site itself cannot say which table it wrote. The six startup batch functions each
    belong to exactly one orchestrator, and their address ranges separate cleanly at
    0x00900000, so the batch containing the site decides the family.
  * Forwarder sites need no inference: each forwarder fetches its own registry
    internally, so the forwarder identity *is* the family.

Both call shapes interpose a call between the pushes and the anchor, which the naive
voting rule treats as a block boundary and discards. GetWriter (builder path) and the
0-arg thunk 0x008BC420 (forwarder path) are therefore declared transparent; without
that the forwarder path resolves zero of its 439 sites.

RVA/VA: the dump is indexed by RVA and pushed handler addresses are absolute VAs.
RVA = VA - 0x00400000. Reading a pushed handler as an RVA lands mid-instruction and
makes every handler look like garbage.

usage: census-identity.py [out.tsv]
"""
import sys
import struct
import collections
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

# ---------------------------------------------------------------------------
# ANCHORS -- the whole client-specific surface of this script.
#
# Currently A21. To point it at another client, change this block and nothing
# else. How the A21 values were recovered, and what each one is pinned on, is
# in probes/a21-census-anchors.md; the A12 values are kept beside each so a
# swap back is a copy, not another hunt.
# ---------------------------------------------------------------------------
CLIENT = "A21"          # DNF.unpacked-b1-debug.exe, SHA-256 1F8244D3...
DUMP = r"D:\project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260822-232754-263-26888\DNF-memory-rva.bin"
IMAGE_BASE = 0x00400000

GET_WRITER = 0x0234CA50         # A12: 0x01C8F5B0
MAKE_HEADER = 0x0234D1E0        # A12: 0x01C90460
REGISTER = 0x0123C4A0           # A12: 0x00D89FC0
THUNK_0ARG = 0x00D02C30         # A12: 0x008BC420
FORWARDER = {0x01237660: "CMD", 0x01237720: "NOTI"}     # A12: 0x00D855C0 / 0x00D85680

# The six startup batch functions. Sites are attributed to a family by which batch
# function's body contains them.
#
# On A12 the two families' address ranges separated at 0x00900000 and that split could
# stand in as a fallback. On A21 they do not separate -- all six sit in 0x00D0..0x00D9 --
# so the mapping below is the only rule, and BATCH_SPLIT is None to keep the fallback
# from silently guessing. The mapping itself comes from startup at 0x0123ABD8, which
# hands one registry to one orchestrator and the other to the other.
BATCHES = {
    "CMD":  [0x00D0C2C0, 0x00D18BC0, 0x00D44AF0],       # A12: 0x008C5920, 0x008D04C0, 0x008F3C70
    "NOTI": [0x00D5F540, 0x00D885F0, 0x00D9A780],       # A12: 0x0090BD20, 0x009301C0, 0x0093EAD0
}
BATCH_SPLIT = None           # A12: 0x00900000. See above -- A21 has no such split.
TEXT_HI = 0x02C29000         # end of .text. A12: 0x01D10000

# The two `call Register` sites inside the forwarder bodies are not registrations: the
# forwarder passes on whatever its own caller pushed, so there is nothing to recover.
FORWARDER_INTERNAL = {0x01237676, 0x01237736}           # A12: 0x00D855D6, 0x00D85696

# A 64-byte window drops one real forwarder site (0x00C90723) whose first push sits 67
# bytes back behind a compiler-interleaved store run. Widening is safe because desynced
# alignments must agree with the true one to affect the majority.
WINDOW = 128

data = open(DUMP, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)


def rel32_target(at):
    if data[at] != 0xE8:
        return None
    return at + 5 + struct.unpack("<i", data[at + 1:at + 5])[0]


def call_sites(target):
    out, i = [], 0
    while True:
        i = data.find(b"\xe8", i, len(data) - 5)
        if i < 0:
            return out
        if rel32_target(i) == target:
            out.append(i)
        i += 1


def pushes_reaching(start, stop, transparent):
    """Forward-decode from `start`; return pushes reaching `stop`, or None when the
    instruction stream does not land exactly on `stop` (i.e. wrong alignment)."""
    off, seq = start, []
    while off < stop:
        ins = list(md.disasm(data[off:off + 16], off, count=1))
        if not ins or off + ins[0].size > stop:
            return None
        m, op = ins[0].mnemonic, ins[0].op_str
        if m == "push":
            seq.append(op)
        elif m == "call" and op.startswith("0x") and int(op, 16) in transparent:
            pass                 # consumes its own argument; earlier pushes still ours
        elif m in ("call", "jmp", "ret") or m.startswith("j"):
            seq = []             # other control flow: earlier pushes belong elsewhere
        off += ins[0].size
    return seq if off == stop else None


def vote(site, want, transparent, window):
    votes = collections.Counter()
    for start in range(site - window, site):
        seq = pushes_reaching(start, site, transparent)
        if seq is not None and len(seq) >= want:
            votes[tuple(seq[-want:])] += 1
    return votes.most_common(1)[0][0] if votes else None


def as_imm(op):
    try:
        return int(op, 16) if op.startswith("0x") else int(op)
    except ValueError:
        return None


def function_extent(entry, cap=4000):
    """Linear decode from a batch-function entry until int3 padding, returning the
    address just past its last decoded instruction. Batch functions are long straight
    runs of register calls; an early `ret` is not their end, so only padding stops it."""
    off, n = entry, 0
    while n < cap:
        ins = list(md.disasm(data[off:off + 16], off, count=1))
        if not ins or ins[0].mnemonic == "int3":
            break
        off += ins[0].size
        n += 1
    return off


# ---------------------------------------------------------------- C->S builders
c2s = collections.defaultdict(list)
c2s_unresolved = collections.defaultdict(list)
for s in call_sites(MAKE_HEADER):
    seq = vote(s, 1, {GET_WRITER}, 64)
    v = as_imm(seq[0]) if seq else None
    if v is None:
        kind = "no-push-in-block" if not seq else (
            "register" if len(seq[0]) == 3 and seq[0][0] in "re" else "indirect")
        c2s_unresolved[kind].append(s)
    else:
        c2s[v].append(s)

# ---------------------------------------------------------------- S->C registrations
batch_ranges = []
for fam, entries in BATCHES.items():
    for e in entries:
        batch_ranges.append((e, function_extent(e), fam))

def family_of_batch_site(site):
    for lo, hi, fam in batch_ranges:
        if lo <= site < hi:
            return fam, "batch"
    if BATCH_SPLIT is None:
        # No address rule exists for this client. Reporting the site as unattributed
        # costs one row; guessing costs a wrong family on a protocol nobody rechecks.
        return None, "unattributed"
    return ("CMD" if site < BATCH_SPLIT else "NOTI"), "range-fallback"

s2c = collections.defaultdict(list)          # (family, type) -> [(handler_rva, ctx, site, path)]
s2c_unresolved = []

for s in call_sites(REGISTER):
    if s in FORWARDER_INTERNAL:
        continue                 # inside a forwarder body: it passes on its caller's args
    seq = vote(s, 3, set(), WINDOW)
    if seq is None or len(seq) != 3:
        s2c_unresolved.append((s, "no-3-pushes", "direct"))
        continue
    ctx, fn, ty = seq
    ty_v, fn_v = as_imm(ty), as_imm(fn)
    if ty_v is None or fn_v is None:
        s2c_unresolved.append((s, "non-immediate", "direct"))
        continue
    if s in FORWARDER_INTERNAL:
        continue                 # the forwarders' own call to Register, not a registration
    fam, how = family_of_batch_site(s)
    if fam is None:
        s2c_unresolved.append((s, how, "direct"))
        continue
    s2c[(fam, ty_v)].append((fn_v - IMAGE_BASE, ctx, s, "batch:" + how))

for fwd, fam in FORWARDER.items():
    for s in call_sites(fwd):
        seq = vote(s, 3, {THUNK_0ARG}, WINDOW)
        if seq is None or len(seq) != 3:
            s2c_unresolved.append((s, "no-3-pushes", "forwarder"))
            continue
        ctx, fn, ty = seq
        ty_v, fn_v = as_imm(ty), as_imm(fn)
        if ty_v is None or fn_v is None:
            s2c_unresolved.append((s, "non-immediate", "forwarder"))
            continue
        s2c[(fam, ty_v)].append((fn_v - IMAGE_BASE, ctx, s, "forwarder"))

# ---------------------------------------------------------------- emit
rows = ["direction\tfamily\ttype\tsites\tprimary\tcontext\tregion\tall_sites\tpaths"]

for t in sorted(c2s):
    addrs = sorted(c2s[t])
    region = ("obfuscated" if all(a >= TEXT_HI for a in addrs)
              else "mixed" if any(a >= TEXT_HI for a in addrs) else "text")
    rows.append("C2S\tCMD\t0x%04X\t%d\t0x%08X\t\t%s\t%s\t"
                % (t, len(addrs), addrs[0], region,
                   ",".join("0x%08X" % a for a in addrs)))

for (fam, t) in sorted(s2c):
    recs = sorted(s2c[(fam, t)])
    handlers = sorted({r[0] for r in recs})
    ctxs = sorted({r[1] for r in recs})
    paths = sorted({r[3] for r in recs})
    region = ("obfuscated" if all(h >= TEXT_HI for h in handlers)
              else "mixed" if any(h >= TEXT_HI for h in handlers) else "text")
    # Register overwrites, so the live handler is the last one registered. Static and
    # runtime-forwarder type sets are disjoint, so in practice each type has exactly one.
    rows.append("S2C\t%s\t0x%04X\t%d\t0x%08X\t%s\t%s\t%s\t%s"
                % (fam, t, len(recs), handlers[-1], "|".join(ctxs), region,
                   ",".join("0x%08X" % r[2] for r in recs), "|".join(paths)))

# Unresolved sites keep their addresses in the TSV, not just a count on stdout. A count
# tells a later reader that 17 builders were missed; only the addresses let them go look
# at one. `region` carries the reason so the rows stay in the same six-field shape.
for kind in sorted(c2s_unresolved):
    addrs = sorted(c2s_unresolved[kind])
    rows.append("C2S\tCMD\t?\t%d\t\t\tunresolved:%s\t%s\t"
                % (len(addrs), kind, ",".join("0x%08X" % a for a in addrs)))
for site, why, path in sorted(s2c_unresolved):
    rows.append("S2C\t?\t?\t1\t\t\tunresolved:%s\t0x%08X\t%s" % (why, site, path))

out = sys.argv[1] if len(sys.argv) > 1 else "protocol-identity.tsv"
open(out, "w").write("\n".join(rows) + "\n")

print("C->S  MakeHeader sites : %d" % len(call_sites(MAKE_HEADER)))
print("      types resolved   : %d sites / %d types  (range 0x%04X..0x%04X)"
      % (sum(len(v) for v in c2s.values()), len(c2s), min(c2s), max(c2s)))
for k, v in sorted(c2s_unresolved.items()):
    print("      %-16s : %d" % (k, len(v)))
print("S->C  Register sites   : %d" % len(call_sites(REGISTER)))
for fwd, fam in FORWARDER.items():
    print("      forwarder %s -> %-4s : %d sites" % (hex(fwd), fam, len(call_sites(fwd))))
for fam in ("CMD", "NOTI"):
    ts = [t for (f, t) in s2c if f == fam]
    print("      table %-4s        : %d types  (max 0x%04X)" % (fam, len(ts), max(ts)))
print("      unresolved       : %d" % len(s2c_unresolved))
for s, why, path in s2c_unresolved[:8]:
    print("          0x%08X  %s  (%s)" % (s, why, path))
both = {t for (f, t) in s2c if f == "CMD"} & {t for (f, t) in s2c if f == "NOTI"}
print("      in both tables   : %d" % len(both))
print("wrote %s" % out)
