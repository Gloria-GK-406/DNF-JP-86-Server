#!/usr/bin/env python3
"""Positive controls for the layout extractor.

A negative result from a static walk is only usable if the walk is known to work, so the
extractor is run against every protocol this repository has already reversed by hand and
confirmed against real captures. Expected lengths below are transcribed from those
documents and their stored samples, not from the extractor.

Closure is checked against the length *rule*, not a single number. A counted list makes
body length `fixed + n×group`, so the test is whether some non-negative integer n
reproduces the observed body length. 0x00AB's rule is 3 + n×9 and n=4 gives its 39 bytes,
which is exactly the structure its hand-written document records.

0x0117 is an inverted control: its documented straight-line width is 2 bytes while the
real body is 12, because the payload is written through a vtable. The extractor is
expected to *not* report a closed 2-byte layout. An extractor that confidently closes
0x0117 is wrong in precisely the way that matters most.

usage: check-controls.py <layouts.json>
"""
import sys
import json

# Ends an expected prefix that deliberately stops short: the fields before it are asserted,
# whatever follows is not. Used where the head has evidence and the tail does not.
OPEN_TAIL = "..."

# key, observed body length, expected non-repeating prefix, expected repeat group, note
#
# PROVENANCE. Every expectation below is transcribed from a hand-written document or a
# stored capture that predates this extractor. This matters more than it looks: a control
# whose expected value was copied out of the extractor's own output proves nothing, so the
# note names where each number came from and can be checked against that source.
CONTROLS = [
    ("CMD:0x00AB", 39, ["u8", "u8", "u8"], ["u8", "u32", "u32"],
     "0x00AB.md: `u8 触发标志 + u8 reported + u8 count(=4) + 4×(u8 tag, u32 v1, u32 v2)`"),
    ("CMD:0x01DD", 16, ["u8", "u8", "u32", "u16", "u16", "u16", "u16", "u8", "u8"], [],
     "0x01DD.md field table: 厂商桶/核心数/MHz/内存/GPU vendor/GPU device/显存/OS/位数"),
    ("CMD:0x00C2", 361, None, None, "0x00C2.md and catalog: exit-flush frame, 361 B"),
    ("CMD:0x00FA", 50, None, None, "catalog exit-flush sequence: 0x00FA(50B)"),
    ("CMD:0x008F", 6, ["u8", "u32", "u8"], [],
     "A21 captures: a21-live-entry-20260825 `#110 C->S CMD 0x008F (6B)`, "
     "a21-reference-entry-bytes-20260825 `#137 (6B)`, a21-dungeon-wire-20260826 "
     "`#94 ... 00 1e 00 00 00 01` = u8 mode + u32 flagIndex + u8 rewardFlag. "
     "The A12 page still records 5 B (u32 + u8); this control follows the A21 bytes, "
     "which is also what this server decodes."),
    ("CMD:0x0117", 12, None, None,
     "INVERTED. 0x0117.md: straight-line width 2 B but real body 12 B, payload written "
     "through a vtable. A closed 2-byte verdict here would be the dangerous failure."),
    ("CMD:0x0008", 3, ["u16", "u8"], [],
     "0x0008.md field table: `u16 查询目标 id`@0 + `u8 模式判别`@2; sample is 3 bytes"),
    ("CMD:0x0003", 1, ["u8"], [], "catalog: EXIT request body `00`, 1 B"),
    ("CMD:0x02A8", 2, ["u16"], [], "0x02A8.md: `body 固定 2 字节: struct { u16 value; }`"),
    ("CMD:0x0286", 0, [], [], "0x0286.md: `请求 body 长度 0`; capture seq 4 body 0"),
    ("CMD:0x01BA", 0, [], [], "catalog: capture seq 6 = 0x01BA, body 0"),
    # The three below carry a length-prefixed name, so total length is not a constant;
    # they are checked on shape. Each document's `4 + nameLength` must appear as the
    # single `dstring` primitive rather than as two separate fields -- which is exactly
    # what pins that primitive's identity.
    ("CMD:0x0005", None, ["u8", "dstring", OPEN_TAIL], [],
     "0x0005.md: 职业 u8 / i32 名字长度 + 名字字节, then a tail of single bytes. The head "
     "is what this control is for. The tail is left unasserted: the A12 page records four "
     "bytes, A21's two builder sites write five and six, and no A21 capture of this "
     "protocol exists to settle it -- the client refuses a bad name before sending, so the "
     "frame has never been seen on the wire here."),
    ("CMD:0x0006", None, ["u16", "dstring"], [],
     "0x0006.md: 角色列表下标 u16 / i32 名字长度 + 名字字节 (= 2 + 4+nameLength)"),
    ("CMD:0x02B5", None, ["dstring"], [],
     "0x02B5.md: i32 名字字节数 + 名字字节 (= 4 + nameLength)"),
]


# Controls whose protocol has a fixed body AND a builder the extractor can reach, so a
# non-closed verdict is a regression. Deliberately excludes 0x00C2 / 0x00FA (counted
# repeats), the three length-prefixed ones, 0x0117 (polymorphic) and 0x01BA (builder in
# the obfuscated region -- its 0-byte body is known only from a capture).
#
# 0x0008 was in this set while the analysed binary was the A12 client, whose builders for
# it agree. A21's do not: of eleven sites, seven write `u16, u8` and the rest write longer
# shapes behind branches, so the extractor reports `partial/builder-sites-disagree`. That
# verdict is true, and requiring `closed` here would be requiring the extractor to overstate
# what the client does. The layout is still gated -- the closure test below asserts it
# reproduces the captured three bytes -- so a rewritten layout is still caught.
MUST_CLOSE = {
    "CMD:0x00AB": True, "CMD:0x01DD": True, "CMD:0x008F": True,
    "CMD:0x02A8": True, "CMD:0x0286": True, "CMD:0x0003": True,
}


def closes_at(entry, observed):
    """Return the repeat counts reproducing `observed`, or None when none exist.

    With several repeat groups the length rule is fixed + Σ nᵢ×gᵢ, so closure is a
    bounded coin-problem search rather than one division. Returning the counts rather
    than a yes/no matters: they are the concrete claim a capture can refute."""
    fixed = sum(f["len"] if f["width"] is None else f["width"]
                for f in entry["fields"]) if entry["fields"] else 0
    sizes = []
    for g in entry.get("groups") or []:
        w = sum(f["len"] if f["width"] is None else f["width"] for f in g["fields"])
        if w > 0:
            sizes.append(w)
    rest = observed - fixed
    if rest < 0:
        return None
    if not sizes:
        return [] if rest == 0 else None

    solution = []

    def search(i, remaining, acc):
        if i == len(sizes):
            if remaining == 0:
                solution.extend(acc)
                return True
            return False
        for n in range(remaining // sizes[i] + 1):
            if search(i + 1, remaining - n * sizes[i], acc + [n]):
                return True
        return False

    return solution if search(0, rest, []) else None


layouts = json.load(open(sys.argv[1]))
print("%-12s %-18s %-14s %-6s %s" % ("key", "state", "rule", "body", "verdict"))
print("-" * 84)
fail = 0
for key, observed, want_prefix, want_group, note in CONTROLS:
    e = layouts.get(key)
    if e is None:
        print("%-12s %-18s %-14s %-6s MISSING" % (key, "-", "-", observed))
        fail += 1
        continue
    prefix = [f["enc"] for f in e["fields"]]
    group = [f["enc"] for g in (e.get("groups") or []) for f in g["fields"]]
    rule = e.get("rule") or "-"
    verdict = []

    if key == "CMD:0x0117":
        # The documented straight-line width is 2 while the real body is 12, so any
        # closed verdict at all is wrong here. Testing "closed AND does not close at 12"
        # was unreachable: its rule is `1 + n×1`, and a one-byte repeat group reproduces
        # every length, so the closure test could never fail. Assert on the state itself.
        if e["state"].startswith("closed"):
            verdict.append("FAIL falsely closed (%s, rule %s)" % (e["state"], rule))
            fail += 1
        else:
            verdict.append("ok not falsely closed (%s/%s)" % (e["state"], e["reason"]))
    else:
        if want_prefix is not None:
            # A control ending in OPEN_TAIL asserts its leading fields and says nothing
            # about what follows. It is for a protocol whose head is evidenced and whose
            # tail is not -- asserting the tail anyway would be transcribing a guess, and
            # asserting the extractor's own answer would make the control self-certifying.
            if want_prefix and want_prefix[-1] == OPEN_TAIL:
                head = want_prefix[:-1]
                ok = prefix[:len(head)] == head
                verdict.append("prefix MATCH (head of %d; tail not asserted)" % len(head)
                               if ok else "prefix DIFF %s" % (prefix,))
            else:
                ok = prefix == want_prefix
                verdict.append("prefix MATCH" if ok else "prefix DIFF %s" % (prefix,))
            if not ok:
                fail += 1
        if want_group is not None:
            ok = group == want_group
            verdict.append("group MATCH" if ok else "group DIFF %s" % (group,))
            if not ok:
                fail += 1
        # Gate the state class where the extractor is expected to derive the layout, so a
        # downgrade regression is caught as well as an inflation one. Declared per control
        # rather than inferred: 0x01BA's fixed length comes from a capture while its
        # builder sits in the obfuscated region, so the extractor *should* refuse to
        # derive it -- a blanket rule would fail the very honesty this control protects.
        if MUST_CLOSE.get(key):
            if not e["state"].startswith("closed"):
                verdict.append("FAIL state %s but this layout is derivable and fixed"
                               % e["state"])
                fail += 1
        # A repeat group one byte wide reproduces every length, so closure stops
        # discriminating. Flag it wherever a control claims a fixed body.
        gw = [sum(f["len"] if f["width"] is None else f["width"] for f in g["fields"])
              for g in (e.get("groups") or [])]
        if observed is not None and not e["fields"] and gw and min(gw) <= 1:
            verdict.append("FAIL closure is vacuous (no fixed fields, %d-byte group)"
                           % min(gw))
            fail += 1
        if observed is None:
            verdict.append("shape-only (length varies with a string field)")
        else:
            # A control that only prints is not a control. Every entry with a known body
            # length gates on closure, whether or not a field sequence was transcribed --
            # otherwise a regression that rewrites a layout wholesale still exits 0.
            n = closes_at(e, observed)
            if n is None:
                verdict.append("FAIL does not close at %d" % observed)
                fail += 1
            else:
                verdict.append("closes at n=%s" % (",".join(map(str, n)) if n else "0"))
            if not e["state"].startswith("closed"):
                verdict.append("note: state is %s" % e["state"])

    print("%-12s %-18s %-14s %-6s %s" % (key, e["state"], rule, observed,
                                         "; ".join(verdict)))
    print("%-12s %s" % ("", note))
    if e.get("site_agreement", "n/a") != "n/a":
        print("%-12s %d builder sites, agreement %s"
              % ("", len(e.get("sites", [])), e["site_agreement"]))
print("-" * 84)
print("controls failed: %d / %d" % (fail, len(CONTROLS)))
sys.exit(1 if fail else 0)
