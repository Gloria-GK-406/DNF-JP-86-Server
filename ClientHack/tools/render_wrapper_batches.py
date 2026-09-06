"""Render decode-pack units into compact per-batch markdown for fleet agents."""
import argparse, json, os, sys


def ins_line(ins, limit=7):
    if not ins:
        return "(no valid decode)"
    parts = []
    for i in ins[:limit]:
        parts.append("%s  [%s]" % (i["text"], i["hex"]))
    if len(ins) > limit:
        parts.append("...")
    return " / ".join(parts)


def render(u, idx):
    L = []
    L.append("### U%03d  %s   %s!%s" % (idx, u["entry_va"], u["module"],
                                        u["function"]))
    L.append("group=%s  convention=%s  section=%s  safe_len=%s  call_site=%s"
             % (u["group"] or "(empty/odd)", u["convention"], u["section"],
                u["safe_len"], u["call_site_va"]))
    L.append("call insn: `%s`   byte@+5: `%s`   mechanical hint: **%s** (delta %s)"
             % (u["call_insn_hex"], u["decoy_byte"], u["resume_hint"],
                u["resume_delta"]))
    conv = u.get("convergence")
    if conv:
        L.append("streams converge at +%d  -> only the prefix below differs"
                 % conv["at"])
        L.append("  prefix if resume@+5: %s" % ins_line(conv["prefix5"], 4))
        L.append("  prefix if resume@+6: %s" % ins_line(conv["prefix6"], 4))
    else:
        L.append("streams never converge inside the 32-byte window")
    L.append("  A) resume@+5 (score %s, %s): %s"
             % (u["resume5"]["score"], ",".join(u["markers5"]) or "-",
                ins_line(u["resume5"]["insns"])))
    L.append("  B) resume@+6 (score %s, %s): %s"
             % (u["resume6"]["score"], ",".join(u["markers6"]) or "-",
                ins_line(u["resume6"]["insns"])))
    ba = u["back_align"]
    pr = " + ".join("%s [%s]" % (i["text"], i["hex"])
                    for i in ba["push_run_before"]) or "(none)"
    L.append("caller_before back-aligned: %d valid alignments; best reading ends `%s` (%d alignments)"
             % (ba["n_alignments"], ba["consensus_last"], ba["agreeing"]))
    L.append("  push run immediately before the call: %s" % pr)
    if len(ba.get("alternatives", [])) > 1:
        L.append("  competing readings of these same bytes (junk-free first):")
        for e in ba["alternatives"]:
            L.append("    - ends `%s` x%d%s -> push run %s"
                     % (e["last"], e["count"], "  [contains junk]" if e["junk"] else "",
                        e["run"]))
    cr = u["call_reg_at6"] or u["call_reg_at5"]
    if cr:
        L.append("  indirect `call <reg>` in window: %s"
                 % ", ".join("%s@+%d [%s]" % (i["text"], i["off"], i["hex"])
                             for i in cr))
    if u["push_run_at6"]:
        L.append("  push run at resume+6: %s"
                 % " + ".join("%s [%s]" % (i["text"], i["hex"])
                              for i in u["push_run_at6"]))
    L.append("wrapper head: %s" % ins_line(u["head_insns"], 6))
    L.append("RAW before: `%s`" % u["raw"]["caller_before_hex"])
    L.append("RAW after : `%s`" % u["raw"]["caller_after_hex"])
    L.append("RAW head  : `%s`" % u["raw"]["wrapper_head_hex"])
    return "\n".join(L)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack", required=True)
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--calibration", action="store_true")
    a = ap.parse_args()

    units = [json.loads(l) for l in open(a.pack, encoding="utf-8")]
    os.makedirs(a.outdir, exist_ok=True)
    batches = []

    if a.calibration:
        groups = [("CAL", units, 30)]
    else:
        readable = [u for u in units if u["caller_readable"]]
        decisive = [u for u in readable if u["resume_hint"] != "AMBIGUOUS"]
        ambiguous = [u for u in readable if u["resume_hint"] == "AMBIGUOUS"]
        # keep each batch homogeneous in group so one rule governs one agent
        groups = []
        for tag, pool, size in (("DEC", decisive, 30), ("AMB", ambiguous, 12)):
            bygroup = {}
            for u in pool:
                bygroup.setdefault(u["group"] or "odd", []).append(u)
            for g, us in sorted(bygroup.items()):
                # group D is the high-risk group: halve its batch size
                sz = max(6, size // 2) if g == "D" else size
                groups.append(("%s-%s" % (tag, g), us, sz))

    for tag, pool, size in groups:
        for n in range(0, len(pool), size):
            chunk = pool[n:n + size]
            name = "%s-%02d" % (tag, n // size + 1)
            body = "\n\n".join(render(u, i + 1) for i, u in enumerate(chunk))
            path = os.path.join(a.outdir, name + ".md")
            with open(path, "w", encoding="utf-8") as fh:
                fh.write("# batch %s  (%d units)\n\n%s\n" % (name, len(chunk), body))
            batches.append({"batch": name, "path": path, "n": len(chunk),
                            "group": chunk[0]["group"] or "odd",
                            "kind": tag.split("-")[0],
                            "entries": [u["entry_va"] for u in chunk]})
    with open(a.manifest, "w", encoding="utf-8") as fh:
        json.dump(batches, fh, ensure_ascii=False, indent=1)
    print("batches: %d   units: %d   avg chars/unit: %d"
          % (len(batches), sum(b["n"] for b in batches),
             sum(os.path.getsize(b["path"]) for b in batches) //
             max(1, sum(b["n"] for b in batches))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
