"""Phase-2 construction plan for the .-wI wrapper-entry rewrite.

Turns the eligibility census into an actionable plan, applying the two
independent geometric constraints the census result CSV does not encode:

  * prefix landing points -- `safe_len` from the safety census: the earliest
    offset inside the wrapper body that still-running code jumps to. A patch
    may occupy [0, safe_len).
  * neighbour gap -- the distance to the NEXT named wrapper entry. A patch
    must not run into it. `check_eligibility_results.py` never checked this,
    which let 29 entries through whose neighbour sits inside the 9-10 byte
    inline template.

Patch mode: trampoline everywhere.

A 5-byte `E9 rel32` at the entry jumps to an allocated stub carrying the
per-convention template. Semantically identical to writing that template at the
entry -- it is position-independent (`inc [esp]` / `pop eax; mov [esp],eax`,
then `push imm32; ret`) -- but it needs 5 bytes instead of 9-10.

Inline was tried and withdrawn (2026-08-31). Live batch over user32:

  inline      59 entries rewritten, client died entering the channel with an
              illegal instruction at 0x0591AB75
  trampoline  84 entries rewritten, full session (dungeon clear, town, exit),
              no fault attributable to the rewrite

The cause is a blind spot in `next_entry_gap`: it is computed over NAMED
wrapper entries only, and `.-wI` also holds a large population of unnamed
wrappers that `resolve_wrappers.py` could not name -- the fault address sits in
exactly that region, 500 KB below the first named entry. Those neighbours are
invisible to the gap check, so a 9-10 byte patch can silently overwrite one.
Five bytes shrinks that blast radius and is all the design needs, so the inline
mode is not worth its risk for the ~4 extra bytes it saves.

Usage:
  python build_rewrite_plan.py --results wrapper-eligibility-results.csv \
      --worklist wrapper-eligibility-worklist.csv \
      --wrappers wrapper-apis-a21.csv --out wrapper-rewrite-plan.csv
"""
import argparse
import csv
import sys
from collections import Counter

TEMPLATE = {"A": 10, "B": 9, "C": 9, "D": 9}
TRAMPOLINE = 5

# Groups whose convention survived the census AND live validation. C is a
# register-loader group (live-falsified 2026-08-31); D was condemned by the
# census 99/102 and its exclusion was upheld live -- rewriting census-flagged
# D entries crashed a real session.
REWRITABLE_GROUPS = ("A", "B")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", required=True)
    ap.add_argument("--worklist", required=True)
    ap.add_argument("--wrappers", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    wl = {r["entry_va"].lower(): r for r in
          csv.DictReader(open(a.worklist, encoding="utf-8-sig"))}
    res = {r["entry_va"].lower(): r for r in
           csv.DictReader(open(a.results, encoding="utf-8-sig"))}

    # neighbour grid over every named wrapper entry, not just the work list
    entries = sorted({int(r["entry_va"], 16) for r in
                      csv.DictReader(open(a.wrappers, encoding="utf-8-sig"))})
    gap = {}
    for i, v in enumerate(entries):
        gap[v] = (entries[i + 1] - v) if i + 1 < len(entries) else 0x1000

    rows, stats = [], Counter()
    for key, unit in wl.items():
        r = res.get(key)
        verdict = (r["verdict"] if r else "MISSING").strip().upper()
        group = unit.get("group") or "odd"
        va = int(unit["entry_va"], 16)
        safe = int(unit["safe_len"])
        g = gap.get(va, 0)
        budget = min(safe, g)
        need = TEMPLATE.get(group, 99)

        if verdict != "VERIFIED-TRANSFER":
            mode, ok, why = "-", "no", "verdict %s" % verdict
        elif group not in REWRITABLE_GROUPS:
            mode, ok, why = "-", "no", "group %s excluded (live-falsified)" % group
        elif budget >= TRAMPOLINE:
            mode, ok, why = "trampoline", "YES", (
                "budget %d >= 5-byte E9%s" % (
                    budget,
                    "" if budget < need else " (inline would also fit; "
                                             "withdrawn, see module docstring)"))
        else:
            mode, ok, why = "-", "no", "budget %d < 5 (safe_len=%d gap=%d)" % (budget, safe, g)

        stats[(ok, mode)] += 1
        rows.append({
            "entry_va": unit["entry_va"], "group": group,
            "convention": unit["convention"], "module": unit["module"],
            "function": unit["function"], "verdict": verdict,
            "safe_len": safe, "next_entry_gap": g, "patch_budget": budget,
            "patch_mode": mode, "eligible": ok, "reason": why,
        })

    rows.sort(key=lambda x: x["entry_va"].lower())
    with open(a.out, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)

    total = sum(1 for r in rows if r["eligible"] == "YES")
    print("plan rows        : %d" % len(rows))
    print("eligible         : %d" % total)
    for (ok, mode), n in sorted(stats.items()):
        print("   %-3s %-11s %d" % (ok, mode, n))
    by = Counter((r["group"], r["patch_mode"]) for r in rows if r["eligible"] == "YES")
    print("eligible by group/mode:")
    for k, n in sorted(by.items()):
        print("   %-3s %-11s %d" % (k[0], k[1], n))
    # what the old checker would have shipped but must not
    bad = [r for r in rows if r["verdict"] == "VERIFIED-TRANSFER"
           and r["group"] in REWRITABLE_GROUPS
           and r["next_entry_gap"] < TEMPLATE.get(r["group"], 99)
           and int(r["safe_len"]) >= TEMPLATE.get(r["group"], 99)]
    print("\nentries the safe_len-only rule would have patched INTO their "
          "neighbour: %d (now trampoline or excluded)" % len(bad))
    print("wrote %s" % a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
