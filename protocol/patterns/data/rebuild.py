#!/usr/bin/env python3
"""Rebuild the whole protocol document scaffold from the client dump and upstream tree.

Pipeline order matters: identity feeds the inventory, the inventory feeds both layout
extraction and generation, and the controls gate the run. A failing control stops the
rebuild rather than letting a broken extractor write 1400 confident documents.

Regeneration is safe to run at any time. Documents written by hand are skipped, machine
zones are replaced in place, and field meanings come from `meanings/*.tsv` rather than
from the documents, so nothing a person wrote is at risk.

usage: rebuild.py [--skip-controls]
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROTOCOL_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

STEPS = [
    ("identity census", ["census-identity.py", "protocol-identity.tsv"]),
    ("inventory + names", ["build-inventory.py", "protocol-identity.tsv",
                           "protocol-inventory.tsv"]),
    ("layout extraction", ["extract-layout.py", "protocol-inventory.tsv",
                           "protocol-layouts.json"]),
    ("positive controls", ["check-controls.py", "protocol-layouts.json"]),
    ("document generation", ["generate-docs.py", "protocol-inventory.tsv",
                             "protocol-layouts.json", PROTOCOL_ROOT]),
]

skip = "--skip-controls" in sys.argv
for name, argv in STEPS:
    if skip and name == "positive controls":
        print("== %s: SKIPPED ==" % name)
        continue
    print("== %s ==" % name)
    r = subprocess.run([sys.executable] + argv, cwd=HERE)
    if r.returncode != 0:
        print("FAILED at '%s' (exit %d); scaffold not regenerated" % (name, r.returncode))
        sys.exit(r.returncode)
print("== rebuild complete ==")
