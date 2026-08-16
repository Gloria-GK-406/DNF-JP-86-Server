#!/usr/bin/env python3
"""Prove that regeneration cannot destroy hand-written content.

These documents are drafts meant to be refined as the game is walked forward, so the
whole approach collapses if a rebuild silently overwrites what a person added. That is a
property worth demonstrating rather than asserting.

The test plants three distinguishable kinds of content and rebuilds:

  1. text appended to a generated document's human zone
  2. an edit to a fully hand-written document, which has no machine zone at all
  3. a field meaning added through the shard mechanism -- the supported way to change the
     machine zone, which must survive precisely because it is an input, not an edit

After the rebuild, 1 and 2 must be byte-identical and 3 must appear in the regenerated
machine zone. Everything is restored afterwards, so the tree is left as it was found.

usage: check-regeneration-safety.py <data-dir> <protocol-root>
"""
import sys
import os
import subprocess
import json

DATA, ROOT = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
BEGIN, END = "<!-- rks:machine:begin -->", "<!-- rks:machine:end -->"

MARK_GENERATED = "\n## 实机验证记录（重新生成安全性测试植入）\n\n哨兵内容 SENTINEL-HUMAN-ZONE-9f3a.\n"
MARK_HANDWRITTEN = "\n<!-- SENTINEL-HANDWRITTEN-5c1b -->\n"
SENTINEL_MEANING = "哨兵含义 SENTINEL-MEANING-7d2e"

layouts = json.load(open(os.path.join(DATA, "protocol-layouts.json"), encoding="utf-8"))

# Pick a generated document that has a usable field table, so the meaning has somewhere
# to land, and is not one of the hand-written ones.
target_key = target_doc = None
for key, e in sorted(layouts.items()):
    if e["direction"] == "C2S" and e["state"] == "closed" and e["fields"]:
        path = os.path.join(ROOT, e["doc"].replace("/", os.sep))
        if os.path.exists(path) and BEGIN in open(path, encoding="utf-8").read():
            target_key, target_doc = key, e["doc"]
            break
if target_key is None:
    print("no suitable generated document found")
    sys.exit(1)

hand_doc = "game/client-to-server/0x00AB.md"
gen_path = os.path.join(ROOT, target_doc.replace("/", os.sep))
hand_path = os.path.join(ROOT, hand_doc.replace("/", os.sep))
shard = os.path.join(DATA, "meanings", "zz-regeneration-safety-probe.tsv")

def read_bytes(p):
    with open(p, "rb") as f:
        return f.read()


def write_bytes(p, data):
    with open(p, "wb") as f:
        f.write(data)


# Bytes, not text. Rewriting a probe file through text I/O normalises its line endings,
# which leaves a hand-written document showing as modified with an empty diff -- a
# side effect the test exists to rule out, not to introduce.
gen_before_b = read_bytes(gen_path)
hand_before_b = read_bytes(hand_path)
gen_before = gen_before_b.decode("utf-8")
hand_before = hand_before_b.decode("utf-8")
fam, ty = target_key.split(":")

failures = []
restored = False
try:
    write_bytes(gen_path, gen_before_b + MARK_GENERATED.encode("utf-8"))
    write_bytes(hand_path, hand_before_b + MARK_HANDWRITTEN.encode("utf-8"))
    open(shard, "w", encoding="utf-8", newline="\n").write(
        "family\ttype\toffset\tmeaning\tconfidence\tsource\n"
        "%s\t%s\t0\t%s\tHypothesis\t重新生成安全性测试\n" % (fam, ty, SENTINEL_MEANING))

    # Without an explicit encoding the local codepage cannot decode UTF-8 output and
    # `stdout` comes back None, so the failure path below would crash on a None slice --
    # the one path that only runs when something has already gone wrong.
    r = subprocess.run([sys.executable, os.path.join(DATA, "rebuild.py")],
                       cwd=DATA, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if r.returncode != 0:
        failures.append("rebuild failed: %s" % ((r.stdout or r.stderr or "")[-400:]))

    gen_after = open(gen_path, encoding="utf-8").read()
    hand_after = open(hand_path, encoding="utf-8").read()

    if not gen_after.endswith(MARK_GENERATED):
        failures.append("human zone of %s was not preserved" % target_doc)
    if gen_after.split(END, 1)[-1] != gen_before.split(END, 1)[-1] + MARK_GENERATED:
        failures.append("content after the machine zone of %s changed" % target_doc)
    if read_bytes(hand_path) != hand_before_b + MARK_HANDWRITTEN.encode("utf-8"):
        failures.append("hand-written %s was modified" % hand_doc)
    machine = gen_after.split(BEGIN, 1)[1].split(END, 1)[0]
    if SENTINEL_MEANING not in machine:
        failures.append("shard meaning did not reach the machine zone of %s" % target_doc)
    if gen_after.split(BEGIN, 1)[0] != gen_before.split(BEGIN, 1)[0]:
        failures.append("content before the machine zone of %s changed" % target_doc)
finally:
    write_bytes(gen_path, gen_before_b)
    write_bytes(hand_path, hand_before_b)
    if os.path.exists(shard):
        os.remove(shard)
    subprocess.run([sys.executable, os.path.join(DATA, "rebuild.py")],
                   cwd=DATA, capture_output=True, text=True,
                   encoding="utf-8", errors="replace")
    # Verify the restore rather than announcing it. A probe that leaves a modified
    # hand-written document behind while printing "restored" is worse than no probe.
    restored = (read_bytes(hand_path) == hand_before_b
                and not os.path.exists(shard)
                and read_bytes(gen_path).split(END.encode())[-1]
                == gen_before_b.split(END.encode())[-1])
    if not restored:
        failures.append("probe files were not restored to their original bytes")

print("=== regeneration safety ===")
print("  probe document      : %s (%s)" % (target_doc, target_key))
print("  hand-written probe  : %s" % hand_doc)
print("  human zone preserved: %s" % ("no" if any("human zone" in f or "after the machine"
                                                  in f for f in failures) else "yes"))
print("  hand-written intact : %s" % ("no" if any("hand-written" in f for f in failures)
                                      else "yes"))
print("  shard meaning merged: %s" % ("no" if any("shard meaning" in f for f in failures)
                                      else "yes"))
print("  tree restored       : %s (verified)" % ("yes" if restored else "NO"))
print("failures: %d" % len(failures))
for f in failures:
    print("  " + f)
sys.exit(1 if failures else 0)
