#!/usr/bin/env python3
"""Acceptance checks over the generated protocol scaffold.

These read the *documents*, not the intermediate data, because the documents are the
deliverable. A check that only inspected the generator's own JSON would pass happily
while every document on disk was empty.

Checks, in the order the specification lists them:

  A-1  every censused identity has exactly one document, and every document an identity
  A-2  identity block complete; names agree with the upstream enums
  A-3  every body section is in exactly one state, never a silent third
  A-4  every field row carries offset, size, encoding and a meaning-or-unknown
  A-5  locating information present in every document
  A-6  index covers the document set exactly, both directions
  A-7  hand-written documents still carry their evidence, samples and work logs

usage: check-acceptance.py <inventory.tsv> <layouts.json> <protocol-root>
"""
import sys
import os
import re
import json
import collections

if len(sys.argv) < 4:
    sys.exit("usage: check-acceptance.py <inventory.tsv> <layouts.json> <protocol-root>\n"
             "  paths are resolved against the current directory; from patterns/data/ the\n"
             "  protocol root is ..\\.. -- passing .. silently checks the wrong tree")
INVENTORY, LAYOUTS, ROOT = sys.argv[1], sys.argv[2], os.path.abspath(sys.argv[3])
if not os.path.isdir(os.path.join(ROOT, "game")):
    sys.exit("not a protocol root (no game/ inside): %s" % ROOT)
BEGIN, END = "<!-- rks:machine:begin -->", "<!-- rks:machine:end -->"
STATES = ["布局已闭合（含计数重复组）", "布局已闭合", "布局部分可得", "结构未知"]

# Documents that exist on purpose without a censused identity behind them.
#
#   CMD-0x0001  A CMD's response shares its request's type and belongs in the C->S
#               document. This one was written up separately before that rule existed.
#               Moving hand-verified content to satisfy a counting rule would trade real
#               evidence for tidiness, so it is declared here and cross-linked from the
#               request document instead.
#   NOTI-0x0381 The A21 client registers no receive handler on this type, so the census --
#               which is a census of handlers -- cannot produce it. The A21 reference
#               server sends it in the entry batch regardless, and so does this server, so
#               there is something to document. A page the client will discard is still a
#               page about what goes on the wire.
DECLARED_EXTRA_DOCS = {
    "game/server-to-client/CMD-0x0001.md",
    "game/server-to-client/NOTI-0x0381.md",
}

rows = []
with open(INVENTORY, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        rows.append(dict(zip(header, line.rstrip("\n").split("\t"))))
layouts = json.load(open(LAYOUTS, encoding="utf-8"))

failures, notes = [], []


def fail(check, msg):
    failures.append("%-5s %s" % (check, msg))


# ---------------------------------------------------------------- A-1 coverage
expected = {r["doc"] for r in rows}
found = set()
for direction in ("client-to-server", "server-to-client"):
    d = os.path.join(ROOT, "game", direction)
    for fn in os.listdir(d):
        if fn.endswith(".md"):
            found.add("game/%s/%s" % (direction, fn))
missing = expected - found
extra = found - expected - DECLARED_EXTRA_DOCS
if missing:
    fail("A-1", "%d censused identities have no document: %s"
         % (len(missing), ", ".join(sorted(missing)[:5])))
if extra:
    fail("A-1", "%d documents have no censused identity: %s"
         % (len(extra), ", ".join(sorted(extra)[:5])))
for d in sorted(DECLARED_EXTRA_DOCS):
    if not os.path.exists(os.path.join(ROOT, d.replace("/", os.sep))):
        fail("A-1", "declared exception %s does not exist" % d)
notes.append("A-1  documents %d, identities %d, missing %d, undeclared extra %d, "
             "declared exceptions %d"
             % (len(found), len(expected), len(missing), len(extra),
                len(DECLARED_EXTRA_DOCS)))

# ---------------------------------------------------------------- per-document
handwritten, generated = [], []
state_counts = collections.Counter()
field_rows = bad_field_rows = 0
unknown_meaning = known_meaning = 0

for r in rows:
    path = os.path.join(ROOT, r["doc"].replace("/", os.sep))
    if not os.path.exists(path):
        continue
    text = open(path, encoding="utf-8").read()
    ident = "%s %s" % (r["family"], r["type"])

    if BEGIN not in text:
        handwritten.append(r["doc"])
        # A-7: hand-written documents keep their evidence sections
        if not re.search(r"##\s*(Identity and behavior|身份)", text):
            fail("A-7", "%s lost its identity section" % r["doc"])
        continue
    generated.append(r["doc"])
    machine = text.split(BEGIN, 1)[1].split(END, 1)[0]

    # A-2 identity block
    for field in ("Endpoint", "Direction", "Command 族", "Type", "名称"):
        if ("| %s |" % field) not in machine:
            fail("A-2", "%s: identity row '%s' missing" % (ident, field))
    if r["name"] and ("`%s`" % r["name"]) not in machine:
        fail("A-2", "%s: name %s not present in document" % (ident, r["name"]))

    # A-3 exactly one body state
    hit = [s for s in STATES if ("**状态：%s**" % s) in machine]
    if len(hit) != 1:
        fail("A-3", "%s: matched %d body states, expected exactly 1" % (ident, len(hit)))
    else:
        state_counts[hit[0]] += 1
        if hit[0] == "结构未知":
            if "原因：" not in machine:
                fail("A-3", "%s: structure-unknown without a stated cause" % ident)
        elif "| Offset | Size | Enc |" not in machine:
            fail("A-3", "%s: has a layout state but no field table" % ident)

    # A-4 field-row completeness
    for line in machine.splitlines():
        if not line.startswith("| ") or "|---" in line or "Offset" in line:
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 6 or line.startswith("| **"):
            continue
        off, size, enc, meaning, conf, src = cells
        field_rows += 1
        # Offset and size were previously unchecked, so a blank position column would
        # have passed. Accepted forms: an exact offset, a nominal one (`42\*`), a
        # group-scoped one, or `?` where a runtime count makes it unknowable.
        off_ok = bool(off) and (off.replace("\\*", "").replace("&nbsp;", "")
                                .replace("↳", "").strip().lstrip("?").strip("*") == ""
                                or any(c.isdigit() for c in off) or off.strip() == "?")
        size_ok = bool(size) and (size.strip() == "?" or size.strip().isdigit())
        if not enc.startswith("`") or not meaning or not src or not off_ok or not size_ok:
            bad_field_rows += 1
            if bad_field_rows <= 3:
                fail("A-4", "%s: incomplete field row: %s" % (ident, line[:80]))
        if meaning == "unknown":
            unknown_meaning += 1
        else:
            known_meaning += 1
            # A-8: a semantic claim needs a source and a grade. The default evidence text
            # ("构包指令" / "接收指令") only says the field exists, which is not evidence
            # of what it means -- that pairing is exactly how a guess gets laundered
            # into a fact, so it is rejected outright.
            if src.startswith(("构包指令", "接收指令")):
                fail("A-8", "%s offset %s: claims a meaning but cites only the default "
                            "field-existence evidence" % (ident, off))
            if conf not in ("Confirmed", "High", "Hypothesis"):
                fail("A-8", "%s offset %s: meaning without a confidence grade (%r)"
                     % (ident, off, conf))

    # A-5 locating information
    if r["direction"] == "C2S":
        if "| 构包点 |" not in machine:
            fail("A-5", "%s: no builder-site row" % ident)
    elif "| handler 入口 |" not in machine:
        fail("A-5", "%s: no handler-entry row" % ident)

# ---------------------------------------------------------------- A-6 index
index_path = os.path.join(ROOT, "game", "index.md")
if not os.path.exists(index_path):
    fail("A-6", "index.md missing")
else:
    idx = open(index_path, encoding="utf-8").read()
    linked = set(re.findall(r"\]\((client-to-server/[^)]+\.md|server-to-client/[^)]+\.md)\)",
                            idx))
    linked = {"game/" + x for x in linked}
    want = expected | DECLARED_EXTRA_DOCS
    if linked != want:
        fail("A-6", "index links %d, expected %d (missing %s, extra %s)"
             % (len(linked), len(want),
                ", ".join(sorted(want - linked)[:4]) or 0,
                ", ".join(sorted(linked - want)[:4]) or 0))
    notes.append("A-6  index entries %d (documents %d + declared exceptions %d)"
                 % (len(linked), len(expected), len(DECLARED_EXTRA_DOCS)))

# ---------------------------------------------------------------- report
notes.append("A-2  generated %d, hand-written %d" % (len(generated), len(handwritten)))
notes.append("A-3  body states: " + ", ".join("%s=%d" % (s, n)
                                              for s, n in state_counts.most_common()))
total_meaning = unknown_meaning + known_meaning
notes.append("A-4  field rows %d (bad %d); meanings known %d (%.1f%%), unknown %d"
             % (field_rows, bad_field_rows, known_meaning,
                100.0 * known_meaning / total_meaning if total_meaning else 0.0,
                unknown_meaning))

print("=== acceptance ===")
for n in notes:
    print("  " + n)
print("failures: %d" % len(failures))
for f in failures[:30]:
    print("  " + f)
if len(failures) > 30:
    print("  ... +%d more" % (len(failures) - 30))
sys.exit(1 if failures else 0)
