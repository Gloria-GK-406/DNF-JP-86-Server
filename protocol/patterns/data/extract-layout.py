#!/usr/bin/env python3
"""Recover body field sequences from the client's own serialisation code, both directions.

The client never memcpys a struct onto the wire. Outbound builders call a small set of
shared write primitives once per field, and inbound handlers pull fields off a global
stream through the mirror-image read primitives. Field order and width are therefore
readable straight out of the instruction stream, which is what this does.

  C->S  anchor = `call MakeHeader`, terminator = `call Finalize`
  S->C  anchor = handler entry,     terminator = `ret` or padding

Width comes from which primitive was called, not from guesswork:

  WriteU8/16/32 -> 1/2/4      ReadU8/16/32 -> 1/2/4
  WriteBytes    -> the pushed length, so no inference is needed
  ReadDString   -> variable, length lives in the stream

Two things make the output honest rather than merely plausible.

First, closure. Field widths must sum to the observed body length wherever a real
capture exists. Where they do not, the walk missed a branch or loop -- or the payload
is not static at all. `patterns/game-frame.md` records the discriminator: an indirect
call through a vtable means the payload is written polymorphically and no static layout
exists. This script reports `has_indirect` rather than emitting a field list it cannot
justify, because a confidently wrong layout is worse than an admitted gap.

Second, every builder site. A type usually has several builders, and a constant pushed
on one code path is not a constant field -- 0x0008 looks perfectly closed at the captured
site while three other sites write runtime values there. All sites are extracted and
compared; disagreement is reported, not averaged away.

For S->C the handler does not receive the frame buffer, so reads happen at whatever depth
the handler delegates to. Calls are followed to a bounded depth and the reads are inlined
in call order. Depth and branch presence are reported with each result so a reader can
tell a straight-line certainty from a reconstructed guess.

usage: extract-layout.py <inventory.tsv> <out.json>
"""
import sys
import json
import struct
import collections
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

# ---------------------------------------------------------------------------
# ANCHORS -- the whole client-specific surface of this script.
#
# Currently A21. To point it at another client, change this block and nothing
# else. Every value below is read off the instruction stream, not matched by
# position; probes/a21-census-anchors.md records what each one is pinned on.
# The A12 values are kept beside each so a swap back is a copy.
# ---------------------------------------------------------------------------
CLIENT = "A21"          # DNF.unpacked-b1-debug.exe, SHA-256 1F8244D3...
DUMP = r"D:\project\DNF\DNFClient-A21-protocol\dumps\dnf-dump-20260822-232754-263-26888\DNF-memory-rva.bin"
IMAGE_BASE = 0x00400000
TEXT_HI = 0x02C29000            # end of .text.  A12: 0x01D10000
OPAQUE_LO = 0x0355E000          # first non-code address; calls beyond are unfollowable.
                                # A12: 0x02300000

GET_WRITER = 0x0234CA50         # A12: 0x01C8F5B0
MAKE_HEADER = 0x0234D1E0        # A12: 0x01C90460
FINALIZE = 0x0234E6D0           # A12: 0x01C8FDD0

# Width is whichever primitive the builder called. The three fixed-width writers each
# advance the writer's own byte counter at [ecx+0x2BCC2C] by exactly their width, which
# is where these widths come from -- not from the ordering of their addresses.
WRITES = {
    0x0234D2E0: ("u8", 1),          # inc [counter],    push 1.   A12: 0x01C90550
    0x0234D310: ("u16", 2),         # add [counter], 2, push 2.   A12: 0x01C90580
    0x0234D340: ("u32", 4),         # add [counter], 4, push 4.   A12: 0x01C905B0
    0x0234CED0: ("bytes", None),    # ret 8: pointer and length.  A12: 0x01C8FAC0
    0x0234D4A0: ("dstring", None),  # measures, then walks.       A12: 0x01C90710
}

# The read side runs off two globals: the stream pointer [0x03BAAE38] and the remaining
# byte count [0x03BAAE3C]. Each primitive advances one and decrements the other by its
# own width.
#
# A21 has no dstring reader. Handlers inline "read a length, then read that many bytes"
# -- 58 such pairs scattered across handlers, gathered into no helper -- so a string
# reports here as u32 + bytes, which is what the instructions actually do.
READS = {
    0x023C5230: ("u8", 1),          # mov cl, byte [eax];  inc eax.    A12: 0x01D00420
    0x023C52A0: ("u16", 2),         # mov cx, word [eax];  add eax, 2. A12: 0x01D00490
    0x023C5310: ("u32", 4),         # mov ecx, dword[eax]; add eax, 4. A12: 0x01D00500
    0x023C5380: ("bytes", None),    # memcpy of [ebp+0xc].             A12: 0x01D00570
}

MAX_INSN = 600
MAX_DEPTH = 4
BUILDER_CALL_DEPTH = 3


def detect_repeat_groups(insns, fields, start):
    """Find repeated field groups by locating backward branches in the walked region.

    A linear walk sees a loop body exactly once, so a counted list looks like a single
    entry. 0x00AB is the reference case: the walk recovers `u8,u8,u8` then one
    `u8,u32,u32`, which is the true structure, but the real body carries that trailing
    group four times. Without this pass the extractor reports 12 bytes for a 39-byte
    body -- right shape, wrong length, and no hint that a count governs it.

    A backward jump whose target lies inside the walked region delimits a loop body;
    fields inside that span repeat. Overlapping spans are merged and only the outermost
    is kept, so nested loops report as one group rather than a spurious pair."""
    spans = []
    for ins in insns:
        if ins.mnemonic.startswith("j") and ins.op_str.startswith("0x"):
            try:
                target = int(ins.op_str, 16)
            except ValueError:
                continue
            if start <= target < ins.address:
                spans.append((target, ins.address))
    if not spans:
        return fields, []
    spans.sort()
    merged = [list(spans[0])]
    for lo, hi in spans[1:]:
        if lo <= merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], hi)
        else:
            merged.append([lo, hi])

    groups, flat = [], []
    for lo, hi in merged:
        inside = [f for f in fields if lo <= f["at"] <= hi]
        if inside:
            groups.append({"from": lo, "to": hi, "fields": inside})
    grouped_ids = {id(f) for g in groups for f in g["fields"]}
    flat = [f for f in fields if id(f) not in grouped_ids]
    return flat, groups

data = open(DUMP, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)


def disasm_at(off):
    ins = list(md.disasm(data[off:off + 16], off, count=1))
    return ins[0] if ins else None


def restamp(fields, call_site, callee):
    """Re-address fields recovered from a delegate to the call site that invoked it.

    Wire order follows invocation order, not where the callee happens to live. A helper
    at a lower address would otherwise sort ahead of everything its caller wrote first,
    and every offset after it would be reported as uncertain when it is exact."""
    out = []
    for f in fields:
        g = dict(f)
        g["at"] = call_site
        g["via"] = "0x%08X" % callee
        out.append(g)
    return out


def source_hint(trail):
    """Last memory operand seen before a field was pushed.

    The builder loads the value from the game object immediately before pushing it, so
    `[esi+0x40]` identifies which object field became this wire field. It does not say
    what the field means, but it groups fields that come from the same structure and
    gives a concrete thing to look at in a debugger."""
    for ins in reversed(trail[-6:]):
        if ins.mnemonic in ("mov", "movzx", "movsx", "lea", "fld", "push") and "[" in ins.op_str:
            frag = ins.op_str[ins.op_str.find("["):]
            return frag.split("]")[0] + "]"
    return ""


def walk_builder(site, depth=0, seen=None):
    """Collect the write sequence between one `call MakeHeader` and its `Finalize`.

    Calls that are not a known primitive are followed rather than skipped. Skipping them
    was a real defect: a builder that delegates a length-prefixed string to a helper would
    have its fields silently omitted while the result still claimed to be closed --
    asserting "these are all the fields" on the strength of not having looked. Anything
    that cannot be followed (packed region, depth limit, indirect target) sets a flag that
    forbids the closed verdict."""
    if seen is None:
        seen = set()
    fields, trail, sub_groups = [], [], []
    off = site + 5
    has_indirect = has_branch = has_unfollowable = False
    terminated = False
    n = 0
    pending_len = None
    while n < MAX_INSN:
        ins = disasm_at(off)
        if ins is None or ins.mnemonic == "int3":
            break
        m, op = ins.mnemonic, ins.op_str
        if m == "call":
            if op.startswith("0x"):
                t = int(op, 16)
                if t == FINALIZE:
                    terminated = True
                    break
                if t in WRITES:
                    kind, width = WRITES[t]
                    fields.append({"enc": kind, "width": width,
                                   "len": pending_len if width is None else width,
                                   "src": source_hint(trail), "at": ins.address})
                    pending_len = None
                elif t == GET_WRITER:
                    pass
                elif t >= OPAQUE_LO or t >= len(data):
                    has_unfollowable = True
                elif depth >= BUILDER_CALL_DEPTH or t in seen:
                    has_unfollowable = True
                else:
                    seen.add(t)
                    sub = walk_builder(t - 5, depth + 1, seen)
                    for f in restamp(sub["fields"], ins.address, t):
                        fields.append(f)
                    # A delegate's repeat group must come along too, and with the caller's
                    # address. Dropping it lost fields outright; keeping the callee's
                    # address sorted the group ahead of everything the caller wrote first,
                    # which then marked every later offset as merely nominal.
                    for g in sub.get("groups", []):
                        sub_groups.append({**g,
                                           "fields": restamp(g["fields"], ins.address, t)})
                    has_indirect = has_indirect or sub["has_indirect"]
                    has_branch = has_branch or sub["has_branch"]
                    has_unfollowable = has_unfollowable or sub["has_unfollowable"]
            else:
                has_indirect = True
        elif m == "push":
            v = None
            if op.startswith("0x"):
                try:
                    v = int(op, 16)
                except ValueError:
                    v = None
            elif op.isdigit():
                v = int(op)
            if v is not None and 0 < v <= 0x10000:
                pending_len = v
        elif m == "ret":
            break
        elif m.startswith("j") and m != "jmp":
            has_branch = True
        trail.append(ins)
        off += ins.size
        n += 1
    flat, groups = detect_repeat_groups(trail, fields, site)
    return {"fields": flat, "groups": groups + sub_groups, "all_fields": fields,
            "terminated": terminated, "has_unfollowable": has_unfollowable,
            "has_indirect": has_indirect, "has_branch": has_branch}


def walk_handler(entry, depth=0, seen=None):
    """Collect the read sequence from a handler entry, following calls in order."""
    if seen is None:
        seen = set()
    if entry in seen or depth > MAX_DEPTH or entry >= len(data):
        # Returning empty *without a flag* would let the caller conclude "no fields here"
        # from having stopped looking -- the same defect already fixed on the builder
        # side. A depth limit or a revisit is a gap in the walk, not a fact about the
        # protocol, so it must forbid a closed verdict.
        return {"fields": [], "groups": [], "has_indirect": False,
                "has_branch": False, "has_unfollowable": True, "opaque": []}
    seen.add(entry)
    fields, trail, opaque, sub_groups = [], [], [], []
    has_indirect = has_branch = has_unfollowable = False
    off, n = entry, 0
    pending_len = None
    while n < MAX_INSN:
        ins = disasm_at(off)
        if ins is None or ins.mnemonic == "int3":
            break
        m, op = ins.mnemonic, ins.op_str
        if m in ("call", "jmp") and op.startswith("0x"):
            t = int(op, 16)
            if t in READS:
                kind, width = READS[t]
                fields.append({"enc": kind, "width": width,
                               "len": pending_len if width is None else width,
                               "src": source_hint(trail), "at": ins.address,
                               "depth": depth})
                pending_len = None
            elif t >= OPAQUE_LO or t >= len(data):
                opaque.append(t)
                has_unfollowable = True
            elif t not in (GET_WRITER,):
                sub = walk_handler(t, depth + 1, seen)
                fields.extend(restamp(sub["fields"], ins.address, t))
                for sg in sub.get("groups", []):
                    sub_groups.append({**sg,
                                       "fields": restamp(sg["fields"], ins.address, t)})
                opaque.extend(sub["opaque"])
                has_indirect = has_indirect or sub["has_indirect"]
                has_branch = has_branch or sub["has_branch"]
                has_unfollowable = has_unfollowable or sub.get("has_unfollowable", False)
            if m == "jmp":
                break
        elif m in ("call", "jmp"):
            has_indirect = True
            if m == "jmp":
                break
        elif m == "push":
            v = None
            if op.startswith("0x"):
                try:
                    v = int(op, 16)
                except ValueError:
                    v = None
            elif op.isdigit():
                v = int(op)
            if v is not None and 0 < v <= 0x10000:
                pending_len = v
        elif m == "ret":
            break
        elif m.startswith("j") and m != "jmp":
            has_branch = True
        trail.append(ins)
        off += ins.size
        n += 1
    flat, groups = detect_repeat_groups(trail, fields, entry)
    return {"fields": flat, "groups": groups + sub_groups, "all_fields": fields,
            "has_indirect": has_indirect, "has_unfollowable": has_unfollowable,
            "has_branch": has_branch, "opaque": sorted(set(opaque))}


def sum_width(fields):
    total = 0
    for f in fields:
        w = f["len"] if f["width"] is None else f["width"]
        if w is None:
            return None
        total += w
    return total


def length_rule(result):
    """Body length as `fixed` when static, or `fixed + n×group` when a counted list is
    present. Reporting the arithmetic rather than one number is what lets a reader check
    closure against a real capture: 0x00AB's rule is 3 + n×9, and n=4 gives its 39."""
    fixed = sum_width(result.get("fields", []))
    groups = [sum_width(g["fields"]) for g in result.get("groups", [])]
    if fixed is None or any(g is None for g in groups):
        return None, None
    if not groups:
        return fixed, str(fixed)
    parts = [str(fixed)] + ["n%d×%d" % (i + 1, g) if len(groups) > 1 else "n×%d" % g
                            for i, g in enumerate(groups)]
    return None, " + ".join(parts)


def classify(result, direction="C2S"):
    """Two states only: a usable field list, or an explained gap. Never a silent third.

    The cause is direction-specific, because the same instruction means different things
    on each side. An indirect call inside a *builder* is evidence the payload really is
    written polymorphically -- that is the documented discriminator for a builder whose
    widths do not close. An indirect call inside a *handler* is only evidence that the
    walk stopped; it says nothing about the payload. Reporting the C->S conclusion on an
    S->C protocol would assert something stronger than the instruction supports, and for
    NOTI 0x00AD it would contradict a measured result: a reachability scan with positive
    controls established that its handler reads no fields at all."""
    fields = result.get("fields", [])
    groups = result.get("groups", [])
    inbound = direction == "S2C"
    unfollowable = result.get("has_unfollowable")
    cut = "unfollowable-call-in-handler" if inbound else "unfollowable-call-in-builder"
    if not fields and not groups:
        # A builder that reached Finalize having written nothing describes an empty body,
        # which is a known layout of zero bytes -- not an unknown one. 0x0286 and 0x01BA
        # are both confirmed empty by capture. Outbound only: an inbound handler that
        # reads nothing is indistinguishable from one whose reads could not be followed.
        if (not inbound and result.get("terminated")
                and not result.get("has_indirect") and not unfollowable):
            return "closed", "empty-body"
        if result.get("has_indirect") and not inbound:
            return "structure-unknown", "polymorphic-payload"
        if result.get("opaque"):
            return "structure-unknown", "reads-inside-packed-region"
        # A truncated walk must never be described as having gone everywhere. Saying
        # "walked the whole reachable call graph and found no field access" when a depth
        # limit or a revisit cut the walk short is a claim about code never looked at.
        if result.get("has_indirect") or unfollowable:
            return "structure-unknown", cut
        return "structure-unknown", "no-field-access-found"
    if result.get("has_indirect"):
        return "partial", cut if inbound else "polymorphic-tail"
    if unfollowable:
        return "partial", cut
    allf = fields + [f for g in groups for f in g["fields"]]
    if any(f["len"] is None for f in allf):
        return "partial", "variable-width-primitive"
    if groups:
        return "closed-parametric", "counted-repeat-group"
    if result.get("has_branch"):
        return "partial", "conditional-fields"
    return "closed", ""


# ------------------------------------------------------------------------- run
inventory = []
with open(sys.argv[1], encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        inventory.append(dict(zip(header, line.rstrip("\n").split("\t"))))

out = {}
stats = collections.Counter()

for row in inventory:
    key = "%s:%s" % (row["family"], row["type"])
    entry = {"doc": row["doc"], "direction": row["direction"],
             "family": row["family"], "type": row["type"]}

    if row["direction"] == "C2S":
        variants, sites = [], []
        if row["builder_sites"]:
            sites = [int(a, 16) for a in row["builder_sites"].split(",") if a]
        for s in sites:
            if s >= len(data):
                continue
            r = walk_builder(s)
            r["site"] = s
            r["obfuscated"] = s >= TEXT_HI
            r["total"], r["rule"] = length_rule(r)
            r["state"], r["reason"] = classify(r, "C2S")
            variants.append(r)
        entry["sites"] = [{k: v for k, v in s.items() if k != "all_fields"}
                          for s in variants]
        shapes = {(tuple((f["enc"], f["len"]) for f in v["fields"]),
                   tuple(tuple((f["enc"], f["len"]) for f in g["fields"])
                         for g in v["groups"])) for v in variants}
        entry["site_agreement"] = ("n/a" if len(variants) < 2 else
                                   "consistent" if len(shapes) == 1 else "divergent")
        rank = {"closed": 0, "closed-parametric": 1, "partial": 2, "structure-unknown": 3}
        # When call sites disagree, the shape the most of them write is the one to report.
        # The tie-break used to be "whichever site wrote the most fields", and that is not a
        # measure of evidence -- it is a measure of how long one function happened to be.
        # `0x0008` is where it showed: seven sites write `u16, u8` and the capture is three
        # bytes, while two sites write `u16, u8, u32`; the longer shape won and the extractor
        # reported a seven-byte layout the wire refutes. Counting sites gets that right, and
        # a genuine tie still falls back to the longer shape rather than picking arbitrarily.
        support = collections.Counter(
            (tuple((f["enc"], f["len"]) for f in v["fields"]),
             tuple(tuple((f["enc"], f["len"]) for f in g["fields"]) for g in v["groups"]))
            for v in variants)

        def shape_of(v):
            return (tuple((f["enc"], f["len"]) for f in v["fields"]),
                    tuple(tuple((f["enc"], f["len"]) for f in g["fields"])
                          for g in v["groups"]))

        def better(v, than):
            if than is None:
                return True
            if rank[v["state"]] != rank[than["state"]]:
                return rank[v["state"]] < rank[than["state"]]
            if support[shape_of(v)] != support[shape_of(than)]:
                return support[shape_of(v)] > support[shape_of(than)]
            return (len(v["fields"]) + len(v["groups"])
                    > len(than["fields"]) + len(than["groups"]))

        best = None
        for v in variants:
            if better(v, best):
                best = v
        if best is None:
            entry["state"], entry["reason"] = "structure-unknown", "no-builder-site-found"
            entry["fields"], entry["groups"], entry["total"], entry["rule"] = [], [], None, None
        else:
            entry["fields"] = best["fields"]
            entry["groups"] = best["groups"]
            entry["total"], entry["rule"] = best["total"], best["rule"]
            entry["state"], entry["reason"] = best["state"], best["reason"]
            if entry["state"].startswith("closed") and entry["site_agreement"] == "divergent":
                entry["state"], entry["reason"] = "partial", "builder-sites-disagree"
    else:
        h = int(row["handler_rva"], 16) if row["handler_rva"] else None
        if h is None:
            entry["state"], entry["reason"] = "structure-unknown", "no-handler-found"
            entry["fields"], entry["groups"], entry["total"], entry["rule"] = [], [], None, None
        else:
            r = walk_handler(h)
            entry["fields"] = r["fields"]
            entry["groups"] = r["groups"]
            entry["total"], entry["rule"] = length_rule(r)
            entry["opaque"] = ["0x%08X" % t for t in r["opaque"][:8]]
            entry["has_indirect"] = r["has_indirect"]
            entry["has_branch"] = r["has_branch"]
            entry["state"], entry["reason"] = classify(r, "S2C")
            entry["handler"] = h

    stats[(row["direction"], entry["state"])] += 1
    out[key] = entry

json.dump(out, open(sys.argv[2], "w"), indent=1)

print("extracted %d protocols" % len(out))
for direction in ("C2S", "S2C"):
    tot = sum(v for (d, _), v in stats.items() if d == direction)
    print("  %s  (%d)" % (direction, tot))
    for (d, st), v in sorted(stats.items()):
        if d == direction:
            print("      %-20s %5d   %5.1f%%" % (st, v, 100.0 * v / tot))
print("wrote %s" % sys.argv[2])
