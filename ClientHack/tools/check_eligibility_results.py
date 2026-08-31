"""Machine-check fleet results for the wrapper eligibility census.

Every fleet verdict is only as good as its evidence: this tool re-verifies
each result row against the work list it claims to describe.

  * verdict must be one of the four legal values;
  * evidence_hex must be a byte-exact substring (space-separated hex, same
    formatting as the work list) of that unit's caller_before_hex,
    caller_after_hex, or wrapper_head_hex -- anything else means the agent
    wrote bytes from memory, and the row is discarded;
  * coverage: which work-list units have an accepted row;
  * --calibration: score the known-truth batch (16 live-verified transfer
    samples, 4 live-falsified C samples, and blind rows are reported
    separately) and print a per-group confusion matrix. Fleet is cleared for
    the full run at >= 90% on the labeled set with all groups covered.

Usage:
  python check_eligibility_results.py --worklist <wl.csv> --results <r.csv>
      [--calibration] [--truth calibration-truth.csv]
"""
import argparse
import csv
import sys

LEGAL = {"VERIFIED-TRANSFER", "REGISTER-LOADER", "MISMATCH", "ODD"}


def load_truth(path):
    return {r["entry_va"].lower(): r["truth"]
            for r in csv.DictReader(open(path, encoding="utf-8-sig"))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--worklist", required=True)
    ap.add_argument("--results", required=True)
    ap.add_argument("--calibration", action="store_true")
    ap.add_argument("--truth", default="calibration-truth.csv")
    ap.add_argument("--out", default="wrapper-rewrite-eligibility.csv")
    a = ap.parse_args()

    wl = {r["entry_va"].lower(): r for r in
          csv.DictReader(open(a.worklist, encoding="utf-8-sig"))}
    truth = load_truth(a.truth) if a.calibration else {}

    accepted, bad_evidence, bad_verdict, unknown = [], [], [], []
    for r in csv.DictReader(open(a.results, encoding="utf-8-sig")):
        key = r.get("entry_va", "").lower()
        unit = wl.get(key)
        if unit is None:
            unknown.append(r)
            continue
        verdict = (r.get("verdict") or "").strip().upper()
        ev = (r.get("evidence_hex") or "").strip().lower()
        if verdict not in LEGAL:
            bad_verdict.append((key, verdict))
            continue
        windows = " ".join([
            unit["caller_before_hex"].lower(),
            unit["caller_after_hex"].lower(),
            unit["wrapper_head_hex"].lower(),
        ])
        if ev and ev in windows:
            r["_accepted"] = True
            accepted.append(r)
        else:
            bad_evidence.append((key, ev[:40]))

    print("results rows      : %d" % (len(accepted) + len(bad_evidence) +
                                      len(bad_verdict) + len(unknown)))
    print("accepted (evidence verified): %d" % len(accepted))
    print("rejected: bad evidence      : %d" % len(bad_evidence))
    print("rejected: bad verdict       : %d" % len(bad_verdict))
    print("rejected: unknown entry     : %d" % len(unknown))
    covered = len({r["entry_va"].lower() for r in accepted})
    print("work-list coverage: %d / %d (%.1f%%)"
          % (covered, len(wl), 100.0 * covered / max(1, len(wl))))

    if truth:
        labeled = [(r, truth[r["entry_va"].lower()])
                   for r in accepted if r["entry_va"].lower() in truth]
        blind = [r for r in accepted if r["entry_va"].lower() not in truth]
        conf = {}
        correct = 0
        for r, want in labeled:
            got = r["verdict"].strip().upper()
            conf.setdefault(want, {}).setdefault(got, 0)
            conf[want][got] += 1
            if got == want:
                correct += 1
        print("\ncalibration: %d/%d correct (%.0f%%)  [blind rows ignored: %d]"
              % (correct, len(labeled),
                 100.0 * correct / max(1, len(labeled)), len(blind)))
        print("confusion (truth -> got):")
        for want, got in sorted(conf.items()):
            print("  %-18s -> %s" % (want, got))

    by_group = {}
    for r in accepted:
        unit = wl[r["entry_va"].lower()]
        by_group.setdefault(unit.get("group") or "odd", {}).setdefault(
            r["verdict"].strip().upper(), 0)
        by_group[unit.get("group") or "odd"][r["verdict"].strip().upper()] += 1
    print("\nverdicts by group:")
    for g, d in sorted(by_group.items()):
        print("  %-4s %s" % (g, d))

    if not a.calibration:
        n = 0
        with open(a.out, "w", newline="", encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(["entry_va", "group", "convention", "module",
                        "function", "verdict", "safe_len", "eligible"])
            for r in accepted:
                unit = wl[r["entry_va"].lower()]
                verdict = r["verdict"].strip().upper()
                eligible = (verdict == "VERIFIED-TRANSFER"
                            and unit.get("group") in ("A", "B", "D")
                            and int(unit["safe_len"]) >= (
                                10 if unit["group"] == "A" else 9))
                w.writerow([unit["entry_va"], unit["group"],
                            unit["convention"], unit["module"],
                            unit["function"], verdict, unit["safe_len"],
                            "YES" if eligible else "no"])
                n += 1
        print("\nwrote %s (%d rows)" % (a.out, n))
    return 0


if __name__ == "__main__":
    sys.exit(main())
