"""Overlay adversarial-verification verdicts onto the census result CSV.

A verifier's job was to attack the census verdict; where it overturned one, the
overturned verdict wins, because it was reached with the census's reasoning in
hand and had to argue against it.  Overturned rows are recorded so a human can
see exactly what changed and why.
"""
import argparse, csv, glob, json, os, sys

LEGAL = {"VERIFIED-TRANSFER", "REGISTER-LOADER", "MISMATCH", "ODD"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--census", required=True)
    ap.add_argument("--verify", nargs="+", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--changelog", required=True)
    a = ap.parse_args()

    census = {r["entry_va"].lower(): r for r in
              csv.DictReader(open(a.census, encoding="utf-8-sig"))}

    ver, bad = {}, []
    for pat in a.verify:
        for p in sorted(glob.glob(pat)):
            try:
                data = json.load(open(p, encoding="utf-8-sig"))
            except Exception as e:
                bad.append("%s unreadable: %s" % (os.path.basename(p), e))
                continue
            for r in data if isinstance(data, list) else []:
                k = (r.get("entry_va") or "").lower()
                if k in census:
                    ver[k] = r

    changed = []
    for k, v in ver.items():
        cur = census[k]
        new = (v.get("corrected_verdict") or "").strip().upper()
        if new not in LEGAL or new == cur["verdict"]:
            continue
        ev = (v.get("evidence_hex") or "").strip().lower()
        changed.append({
            "entry_va": cur["entry_va"],
            "census_verdict": cur["verdict"],
            "verified_verdict": new,
            "reason": (v.get("reason") or "").replace("\n", " "),
        })
        cur["verdict"] = new
        if ev:
            cur["evidence_hex"] = ev
        cur["evidence_note"] = ("[overturned by adversarial verification] "
                                + (v.get("reason") or "").replace("\n", " "))

    with open(a.out, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["entry_va", "verdict", "evidence_hex", "evidence_note"])
        for k in sorted(census):
            r = census[k]
            w.writerow([r["entry_va"], r["verdict"], r["evidence_hex"],
                        r["evidence_note"]])

    with open(a.changelog, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["entry_va", "census_verdict", "verified_verdict", "reason"])
        for c in sorted(changed, key=lambda x: x["entry_va"]):
            w.writerow([c["entry_va"], c["census_verdict"],
                        c["verified_verdict"], c["reason"]])

    print("census rows        : %d" % len(census))
    print("verified rows      : %d" % len(ver))
    print("overturned         : %d" % len(changed))
    for b in bad:
        print("  PROBLEM %s" % b)
    print("wrote %s and %s" % (a.out, a.changelog))
    return 0


if __name__ == "__main__":
    sys.exit(main())
