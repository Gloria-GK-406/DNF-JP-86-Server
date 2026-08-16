#!/usr/bin/env python3
"""Prove the positive controls can actually fail.

A control suite that always exits 0 is indistinguishable from one that checks nothing,
and this one was in that state: only two entries could raise the failure count, so
rewriting a layout wholesale still passed. That was found by mutating the extractor's
output and observing the suite stay green, so the fix ships with the same test.

Each mutation below corrupts a layout in a way a real regression could produce. Every one
must make the suite exit non-zero; the unmutated baseline must exit zero. A mutation that
passes means the control covering it is decorative.

Nothing is written to the repository -- mutated copies go to a temporary directory.

usage: check-controls-mutation.py <layouts.json> [tmpdir]
"""
import json
import os
import subprocess
import sys
import tempfile

LAYOUTS = sys.argv[1]
TMP = sys.argv[2] if len(sys.argv) > 2 else tempfile.mkdtemp(prefix="ctrl-mut-")
HERE = os.path.dirname(os.path.abspath(__file__))
base = json.load(open(LAYOUTS, encoding="utf-8"))
env = dict(os.environ, PYTHONIOENCODING="utf-8")


def field(width):
    return [{"enc": "u8", "width": width, "len": width, "src": "", "at": 0}]


def rewrite(entry, width):
    return {**entry, "state": "closed", "reason": "", "fields": field(width),
            "groups": [], "rule": str(width), "total": width}


MUTATIONS = [
    ("0x00C2 rewritten as a 1-byte closed body",
     lambda L: L.__setitem__("CMD:0x00C2", rewrite(L["CMD:0x00C2"], 1))),
    ("0x008F + 0x0008 + 0x00FA all rewritten to 28 bytes",
     lambda L: [L.__setitem__(k, rewrite(L[k], 28))
                for k in ("CMD:0x008F", "CMD:0x0008", "CMD:0x00FA")]),
    ("0x0117 state flipped to closed (inverted control)",
     lambda L: L.__setitem__("CMD:0x0117", {**L["CMD:0x0117"], "state": "closed"})),
    ("0x0286 empty body inflated to 1 byte",
     lambda L: L.__setitem__("CMD:0x0286", rewrite(L["CMD:0x0286"], 1))),
    ("0x02B5 dstring replaced by u32 (primitive identity)",
     lambda L: L.__setitem__("CMD:0x02B5",
                             {**L["CMD:0x02B5"],
                              "fields": [{"enc": "u32", "width": 4, "len": 4,
                                          "src": "", "at": 0}], "groups": []})),
    # The five above all inflate or falsely close. Regressions also run the other way --
    # an over-eager unfollowability flag downgrades a correct layout, and a gate that only
    # watches for inflation would not see it. These cover the downgrade direction, plus
    # the trivial-closure pathology a one-byte repeat group creates.
    ("0x0286 closed empty body downgraded to structure-unknown",
     lambda L: L.__setitem__("CMD:0x0286",
                             {**L["CMD:0x0286"], "state": "structure-unknown",
                              "reason": "no-field-access-found", "fields": [],
                              "groups": [], "rule": None, "total": None})),
    ("0x008F downgraded to partial with its fields intact",
     lambda L: L.__setitem__("CMD:0x008F",
                             {**L["CMD:0x008F"], "state": "partial",
                              "reason": "conditional-fields"})),
    ("0x00FA fields replaced by a 1-byte repeat group (trivially closes at any length)",
     lambda L: L.__setitem__("CMD:0x00FA",
                             {**L["CMD:0x00FA"], "state": "closed-parametric",
                              "fields": [],
                              "groups": [{"from": 0, "to": 0,
                                          "fields": [{"enc": "u8", "width": 1, "len": 1,
                                                      "src": "", "at": 0}]}],
                              "rule": "0 + n×1", "total": None})),
    ("0x0008 encodings scrambled u16,u8 -> u8,u8,u8 (total still 3)",
     lambda L: L.__setitem__("CMD:0x0008",
                             {**L["CMD:0x0008"],
                              "fields": [{"enc": "u8", "width": 1, "len": 1,
                                          "src": "", "at": i} for i in range(3)],
                              "groups": []})),
]


def run(mutate, label, expect_fail):
    layouts = json.loads(json.dumps(base))
    mutate(layouts)
    path = os.path.join(TMP, "mutated.json")
    json.dump(layouts, open(path, "w"))
    r = subprocess.run([sys.executable, os.path.join(HERE, "check-controls.py"), path],
                       capture_output=True, text=True, encoding="utf-8",
                       errors="replace", env=env)
    tail = [l for l in (r.stdout or "").splitlines() if "controls failed" in l]
    detected = r.returncode != 0
    ok = detected == expect_fail
    print("  %-52s exit=%d  %-22s %s"
          % (label, r.returncode, tail[0] if tail else "", "ok" if ok else "MISSED"))
    return ok


os.makedirs(TMP, exist_ok=True)
print("=== control-suite mutation test ===")
results = [run(m, label, True) for label, m in MUTATIONS]
results.append(run(lambda L: None, "unmutated baseline (must pass)", False))
missed = results.count(False)
print("mutations undetected: %d / %d" % (missed, len(MUTATIONS)))
sys.exit(1 if missed else 0)
