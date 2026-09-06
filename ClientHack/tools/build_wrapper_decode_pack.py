"""Phase 0: derive a decode pack for the wrapper-eligibility census.

Purely derived from the bytes already present in each work-list row -- no dump
reads, no external info.  Every decoded instruction carries the *verbatim*
space-separated lowercase hex substring as it appears in the input row, so a
fleet agent can quote evidence by copy, never from memory.
"""
import argparse, csv, json, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

md = Cs(CS_ARCH_X86, CS_MODE_32)

# mnemonics that essentially never appear in real 32-bit MSVC-compiled game code;
# their presence is strong evidence a decode alignment is wrong.
JUNK = {
    'lcall', 'ljmp', 'enter', 'into', 'bound', 'arpl', 'aaa', 'aas', 'aad',
    'aam', 'daa', 'das', 'in', 'out', 'insb', 'insd', 'insw', 'outsb', 'outsd',
    'outsw', 'hlt', 'iretd', 'salc', 'les', 'lds', 'lss', 'lfs', 'lgs',
    'xlatb', 'loop', 'loopne', 'loope', 'int1', 'int', 'ud2', 'cli', 'sti',
    'clts', 'lock', 'wait', 'fwait', 'sysenter', 'sysexit', 'cmc', 'clc',
    'stc', 'cld', 'std', 'xchg', 'sahf', 'lahf', 'popfd', 'pushfd', 'cpuid',
    'lar', 'lsl', 'verr', 'verw', 'str', 'sldt', 'smsw', 'lmsw', 'invd',
}
SEGOPS = {'push ds', 'push es', 'push ss', 'push cs', 'pop ds', 'pop es',
          'pop ss', 'pop fs', 'pop gs'}
PUSHY = {'push'}


def tohex(bs):
    return " ".join("%02x" % b for b in bs)


def parse(hexstr):
    return bytes(int(x, 16) for x in hexstr.split())


def decode(buf, start, limit=10):
    """Decode up to `limit` instructions from buf[start:]."""
    out = []
    for ins in md.disasm(buf[start:], start):
        out.append({
            "off": ins.address,
            "size": ins.size,
            "text": ("%s %s" % (ins.mnemonic, ins.op_str)).strip(),
            "mn": ins.mnemonic,
            "hex": tohex(buf[ins.address:ins.address + ins.size]),
        })
        if len(out) >= limit:
            break
    return out


def alignment_score(buf, start, ins):
    """How much does this alignment look like real compiled code?"""
    if not ins:
        return -99.0
    consumed = ins[-1]["off"] + ins[-1]["size"] - start
    avail = len(buf) - start
    junk = sum(1 for i in ins if i["mn"] in JUNK or i["text"] in SEGOPS)
    # a decode that dies before the window ends is a strong negative
    undecoded = avail - consumed if len(ins) < 10 else 0
    # very long immediates straddling the window edge are normal; short
    # instruction density is the useful signal
    return float(consumed) - 6.0 * junk - 4.0 * undecoded


def back_align(before):
    """Try every start offset; keep alignments whose stream ends exactly at the
    end of the window (flush against the call).

    Majority vote alone is not safe here: a junk FPU/segment instruction can
    swallow the real trailing pushes and still win the vote.  So we rank the
    distinct readings, junk-free first, and hand the agent the alternatives.
    """
    cands = []
    n = len(before)
    for s in range(n):
        ins = decode(before, s, limit=16)
        if not ins:
            continue
        if ins[-1]["off"] + ins[-1]["size"] != n:
            continue
        tail = ins[-4:]
        run = []
        for i in reversed(tail):
            if i["mn"] in PUSHY:
                run.insert(0, i)
            else:
                break
        junk = any(i["mn"] in JUNK or i["text"] in SEGOPS for i in tail)
        cands.append({"start": s, "tail": tail, "run": run, "junk": junk})

    # distinct readings keyed by the trailing push run + last instruction
    seen = {}
    for c in cands:
        key = (" + ".join(i["text"] for i in c["run"]),
               c["tail"][-1]["text"] if c["tail"] else "")
        e = seen.setdefault(key, {"count": 0, "junk": c["junk"],
                                  "last": key[1], "run": c["run"]})
        e["count"] += 1
        e["junk"] = e["junk"] and c["junk"]
    ranked = sorted(seen.values(),
                    key=lambda e: (e["junk"], -len(e["run"]), -e["count"]))

    best = ranked[0] if ranked else {"run": [], "last": "", "count": 0,
                                     "junk": True}
    return cands, best["last"], best["count"], best["run"], ranked[:3]


def push_run_after(ins):
    """Leading run of pushes in a resumed instruction stream."""
    run = []
    for i in ins:
        if i["mn"] in PUSHY:
            run.append(i)
        else:
            break
    return run


REGS = ("eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi")


def call_reg(ins):
    """Indirect `call <reg>` -- the register-loader stub's tell."""
    return [i for i in ins
            if i["mn"] == "call" and i["text"].split()[-1] in REGS]


def convergence(d5, d6):
    """Where the +5 and +6 decode streams re-synchronise.  Past that offset the
    two alignments are byte-identical, so the whole decision lives in the
    diverging prefix."""
    b5 = {i["off"] for i in d5}
    b6 = {i["off"] for i in d6}
    common = sorted(b5 & b6)
    if not common:
        return None
    conv = common[0]
    pre5 = [i for i in d5 if i["off"] < conv]
    pre6 = [i for i in d6 if i["off"] < conv]
    return {"at": conv, "prefix5": pre5, "prefix6": pre6}


def markers(ins):
    """Structural tells that an alignment is real compiled code."""
    m = []
    txt = [i["text"] for i in ins]
    mns = [i["mn"] for i in ins]
    if any(t.startswith("ret") for t in txt):
        m.append("ret")
    if mns.count("int3") >= 3:
        m.append("int3-pad")
    if "push ebp" in txt and "mov ebp, esp" in txt:
        m.append("prologue")
    if any(t.startswith("call ") for t in txt):
        m.append("call")
    if sum(1 for x in mns if x == "push") >= 3:
        m.append("push-chain")
    if any(x in JUNK for x in mns):
        m.append("junk:" + ",".join(sorted({x for x in mns if x in JUNK})[:3]))
    return m


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--worklist", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    rows = list(csv.DictReader(open(a.worklist, encoding="utf-8-sig")))
    with open(a.out, "w", encoding="utf-8") as fh:
        for r in rows:
            before = parse(r["caller_before_hex"])
            after = parse(r["caller_after_hex"])
            head = parse(r["wrapper_head_hex"])
            readable = r["source_section"] in (".text", ".tls")

            d5 = decode(after, 5)
            d6 = decode(after, 6)
            s5 = alignment_score(after, 5, d5)
            s6 = alignment_score(after, 6, d6)
            delta = s5 - s6
            hint = "resume@+5" if delta >= 4 else (
                   "resume@+6" if delta <= -4 else "AMBIGUOUS")

            cands, consensus, agree, prerun, ranked = back_align(before)
            cr5, cr6 = call_reg(d5), call_reg(d6)

            rec = {
                "entry_va": r["entry_va"],
                "call_site_va": r["call_site_va"],
                "section": r["source_section"],
                "caller_readable": readable,
                "module": r["module"],
                "function": r["function"],
                "convention": r["convention"],
                "group": r["group"] or "",
                "safe_len": r["safe_len"],
                "raw": {
                    "caller_before_hex": r["caller_before_hex"],
                    "caller_after_hex": r["caller_after_hex"],
                    "wrapper_head_hex": r["wrapper_head_hex"],
                },
                "call_insn_hex": tohex(after[:5]),
                "decoy_byte": "%02x" % after[5],
                "resume5": {"score": round(s5, 1), "insns": d5},
                "resume6": {"score": round(s6, 1), "insns": d6},
                "resume_hint": hint,
                "convergence": convergence(d5, d6),
                "markers5": markers(d5),
                "markers6": markers(d6),
                "resume_delta": round(delta, 1),
                "push_run_at5": push_run_after(d5),
                "push_run_at6": push_run_after(d6),
                "call_reg_at5": cr5,
                "call_reg_at6": cr6,
                "back_align": {
                    "n_alignments": len(cands),
                    "consensus_last": consensus,
                    "agreeing": agree,
                    "push_run_before": prerun,
                    "alternatives": [
                        {"last": e["last"], "count": e["count"],
                         "junk": e["junk"],
                         "run": " + ".join("%s [%s]" % (i["text"], i["hex"])
                                           for i in e["run"]) or "(none)"}
                        for e in ranked],
                },
                "head_insns": decode(head, 0),
            }
            fh.write(json.dumps(rec, ensure_ascii=False) + "\n")
    print("wrote %s (%d units)" % (a.out, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
