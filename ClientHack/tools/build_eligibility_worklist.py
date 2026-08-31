"""Build the per-wrapper work list for the fleet eligibility census.

Phase 2 of the wrapper rewrite needs, for every NAMED wrapper, an
independently verified verdict: is its measured convention real (a transfer
wrapper the entry rewrite covers) or a measurement artifact (a
register-loader stub that must never be rewritten -- the C-group lesson)?

A fleet of agents can produce those verdicts only if every unit is
self-contained and every conclusion is falsifiable. This generator emits one
CSV row per wrapper with all the context an agent needs WITHOUT touching the
dump (caller window bytes, wrapper head bytes, census safe-length), so the
unit task is pure reading and judgment, and a checker can later re-verify
every quoted byte against the dump.

Columns:
  entry_va, call_site_va, module, function, convention,
  caller_before (16 bytes ending at the call), caller_after (32 bytes from
  the call), wrapper_head (16 bytes at the entry), safe_len (max patch
  length from the prefix census; 16 = clean host), batch (dispatch hint)

Usage:
  python build_eligibility_worklist.py --dump <dumpdir> \
      --wrappers wrapper-apis-a21.csv \
      --prefix-refs wrapper-prefix-refs-all.csv \
      --out wrapper-eligibility-worklist.csv
"""
import argparse
import csv
from pathlib import Path

BASE = 0x400000
PREFIX_MAX = 16

# The four canonical conventions plus the artifact suspects. Everything else
# ("esp+0/?", "esp-832/." and friends) is non-canonical and lands in batch
# "odd" for a human, not the fleet.
CONVENTION_RANK = {
    "esp+0/R+1": "B", "esp+4/R": "A", "esp+4/a1": "C", "esp+8/a2": "D",
}


def min_external_offset(host, refs):
    """Smallest offset of an EXTERNAL landing inside the host's 16-byte
    prefix, or PREFIX_MAX when the host is clean."""
    best = PREFIX_MAX
    for r in refs.get(host, ()):
        if r["scope"] == "external":
            best = min(best, int(r["offset"]))
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--wrappers", default="wrapper-apis-a21.csv")
    ap.add_argument("--prefix-refs", default="wrapper-prefix-refs-all.csv")
    ap.add_argument("--out", default="wrapper-eligibility-worklist.csv")
    a = ap.parse_args()

    img = (Path(a.dump) / "DNF-memory-rva.bin").read_bytes()

    refs = {}
    for r in csv.DictReader(open(a.prefix_refs, encoding="utf-8")):
        refs.setdefault(r["host_entry"].lower(), []).append(r)

    rows = []
    for w in csv.DictReader(open(a.wrappers, encoding="utf-8-sig")):
        entry = int(w["entry_va"], 16)
        site = int(w["call_site_va"], 16)
        group = CONVENTION_RANK.get(w["convention"])
        safe = min_external_offset(w["entry_va"].lower(), refs)
        caller_before = img[site - 16 - BASE:site - BASE]
        caller_after = img[site - BASE:site - BASE + 32]
        head = img[entry - BASE:entry - BASE + 16]
        batch = (group or "odd") + (
            "-clean" if safe >= PREFIX_MAX else "-tight")
        rows.append({
            "entry_va": w["entry_va"], "call_site_va": w["call_site_va"],
            "source_section": w["source_section"],
            "module": w["module"], "function": w["function"],
            "convention": w["convention"], "group": group or "",
            "safe_len": safe, "batch": batch,
            "caller_before_hex": caller_before.hex(" "),
            "caller_after_hex": caller_after.hex(" "),
            "wrapper_head_hex": head.hex(" "),
        })

    with open(a.out, "w", newline="", encoding="utf-8") as fh:
        wtr = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        wtr.writeheader()
        wtr.writerows(rows)

    from collections import Counter
    print("units: %d" % len(rows))
    for k, v in Counter(r["batch"] for r in rows).most_common():
        print("  batch %-10s %5d" % (k, v))


if __name__ == "__main__":
    main()
