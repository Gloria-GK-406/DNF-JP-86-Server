"""Merge fleet JSON shards into the census result CSV.

Agents return JSON (not CSV) so that commas and semicolons inside evidence
notes can never corrupt a field.  This script is the only place CSV is written.
"""
import argparse, csv, glob, json, os, sys

LEGAL = {"VERIFIED-TRANSFER", "REGISTER-LOADER", "MISMATCH", "ODD"}


def load_shards(patterns):
    rows, problems = [], []
    for pat in patterns:
        for path in sorted(glob.glob(pat)):
            try:
                data = json.load(open(path, encoding="utf-8-sig"))
            except Exception as e:
                problems.append("%s: unreadable (%s)" % (os.path.basename(path), e))
                continue
            if isinstance(data, dict):
                data = data.get("results") or data.get("rows") or []
            if not isinstance(data, list):
                problems.append("%s: not a JSON array" % os.path.basename(path))
                continue
            for r in data:
                if not isinstance(r, dict):
                    continue
                r["_shard"] = os.path.basename(path)
                rows.append(r)
    return rows, problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--shards", nargs="+", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--odd-for", default="",
                    help="jsonl decode pack; every caller-unreadable unit in it "
                         "is emitted as ODD (per project decision)")
    a = ap.parse_args()

    rows, problems = load_shards(a.shards)
    seen, dupes = {}, []
    for r in rows:
        k = (r.get("entry_va") or "").lower()
        if k in seen:
            dupes.append(k)
            continue
        seen[k] = r

    added = 0
    if a.odd_for:
        for line in open(a.odd_for, encoding="utf-8"):
            u = json.loads(line)
            if u["caller_readable"]:
                continue
            k = u["entry_va"].lower()
            if k in seen:
                continue
            head = u["raw"]["wrapper_head_hex"].split()
            seen[k] = {
                "entry_va": u["entry_va"],
                "verdict": "ODD",
                # first four head bytes: always a literal substring of RAW head
                "evidence_hex": " ".join(head[:4]),
                "evidence_note": ("caller-unreadable (%s): call-site shape check "
                                  "does not apply and the 16-byte head shows no "
                                  "loader fingerprint; left for human review"
                                  % u["section"]),
                "_shard": "(mechanical ODD)",
            }
            added += 1

    bad = [r for r in seen.values()
           if (r.get("verdict") or "").strip().upper() not in LEGAL]

    with open(a.out, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["entry_va", "verdict", "evidence_hex", "evidence_note"])
        for r in sorted(seen.values(), key=lambda x: x["entry_va"].lower()):
            w.writerow([r.get("entry_va", ""),
                        (r.get("verdict") or "").strip().upper(),
                        (r.get("evidence_hex") or "").strip().lower(),
                        (r.get("evidence_note") or "").replace("\n", " ")])

    print("shards merged   : %d rows" % len(rows))
    print("mechanical ODD  : %d rows" % added)
    print("duplicates      : %d" % len(dupes))
    print("illegal verdict : %d" % len(bad))
    for p in problems:
        print("  PROBLEM %s" % p)
    print("wrote %s (%d rows)" % (a.out, len(seen)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
