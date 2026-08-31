"""Safety census for the .-wI wrapper-entry rewrite plan (phase 0).

Two questions, both prerequisites the wrapper-recovery doc left open:

1. The shared components -- .-wI addresses referenced >=100 times. The doc
   lists three examples and says they were never analysed. If the rewritten
   entries stop executing their original bodies, does anything that still runs
   (mutated CRT, .mackt stubs, .tls, unnamed wrappers, game .text) depend on
   paths through them?

2. Entry-rewrite collateral. The plan overwrites the first ~10 bytes of each
   named wrapper entry. Does any control transfer land INSIDE a named wrapper
   body rather than at its entry -- in particular inside the overwritten
   prefix? A mid-body landing point past the prefix keeps working (those bytes
   stay untouched); one inside the prefix breaks.

Method: capstone linear sweep with byte-restart over the native executable
sections (.text, .rsrc@04F69000, .tls, .vmp0, .bss, .mackt, .-wI); E8/E9
byte-scan over .vmp1 and .#|U, where linear decode is bytecode-shaped noise.
Byte-scan hits are flagged as such (they can only overcount, never undercount).

Usage:
  python wrapper_rewrite_safety.py --dump <dumpdir> --wrappers wrapper-apis-a21.csv
"""
import argparse
import csv
import struct
from collections import defaultdict

import capstone

IMAGE_BASE = 0x00400000
PREFIX_MAX = 16          # longest entry rewrite is ~10 bytes; sweep 16 to be safe
BODY_MAX = 0x1000        # body extent cap between consecutive named entries
SHARED_REFS = 100        # the doc's shared-component threshold


def parse_sections(img):
    """Section table from the dumped image header (RVA-mapped, header at 0)."""
    e = struct.unpack_from("<I", img, 0x3C)[0]
    assert img[e:e + 4] == b"PE\0\0", "not a PE"
    fh = e + 4
    nsec = struct.unpack_from("<H", img, fh + 2)[0]
    osz = struct.unpack_from("<H", img, fh + 16)[0]
    st = fh + 20 + osz
    secs = []
    for i in range(nsec):
        h = st + i * 40
        name = img[h:h + 8].rstrip(b"\0").decode("latin1")
        vs, va, raw = struct.unpack_from("<III", img, h + 8)
        chars = struct.unpack_from("<I", img, h + 36)[0]
        secs.append({"name": name, "rva": va, "vsize": vs, "raw": raw,
                     "exec": bool(chars & 0x20000000)})
    return secs


def section_of(secs, va):
    rva = va - IMAGE_BASE
    for s in secs:
        if s["rva"] <= rva < s["rva"] + s["vsize"]:
            return s["name"]
    return None


def make_md():
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    md.detail = True
    return md


def sweep_native(img, md, secs, sec, wi):
    """Linear decode with byte-restart; yield (src_va, mnemonic, target).

    Disassembly addresses are VA-based so capstone-computed branch targets
    compare directly against the VA range of .-wI.
    """
    start, size = sec["rva"], sec["vsize"]
    va0 = IMAGE_BASE + start
    off = 0
    while off < size:
        window = img[start + off:start + off + 0x4000]
        broke = True
        for insn in md.disasm(window, va0 + off):
            broke = False
            if insn.mnemonic in ("call", "jmp") and len(insn.operands) == 1 \
                    and insn.operands[0].type == capstone.x86.X86_OP_IMM:
                t = insn.operands[0].imm
                if wi[0] <= t < wi[1]:
                    yield (insn.address, insn.mnemonic, t)
            off = insn.address - va0 + insn.size
        if broke:
            off += 1


def sweep_bytescan(img, secs, sec, wi):
    """E8/E9 rel32 byte scan for bytecode-shaped sections; overcounts."""
    start, size = sec["rva"], sec["vsize"]
    code = img[start:start + size]
    for i in range(size - 5):
        op = code[i]
        if op in (0xE8, 0xE9):
            rel = struct.unpack_from("<i", code, i + 1)[0]
            t = IMAGE_BASE + start + i + 5 + rel
            if wi[0] <= t < wi[1]:
                yield (IMAGE_BASE + start + i,
                       "call?" if op == 0xE8 else "jmp?", t)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True, help="dump directory")
    ap.add_argument("--wrappers", required=True, help="wrapper-apis-a21.csv")
    ap.add_argument("--out", default="wrapper-shared-components.csv")
    a = ap.parse_args()

    from pathlib import Path
    d = Path(a.dump)
    img = (d / "DNF-memory-rva.bin").read_bytes()
    secs = parse_sections(img)
    sec_by_name = {s["name"]: s for s in secs}
    print("sections:")
    for s in secs:
        print("  %-8s %08X-%08X exec=%s" % (s["name"], IMAGE_BASE + s["rva"],
                                            IMAGE_BASE + s["rva"] + s["vsize"],
                                            s["exec"]))

    wi_sec = sec_by_name[".-wI"]
    wi = (IMAGE_BASE + wi_sec["rva"],
          IMAGE_BASE + wi_sec["rva"] + wi_sec["vsize"])

    entries = []          # (entry_va, convention, module, function)
    for row in csv.DictReader(open(a.wrappers, encoding="utf-8-sig")):
        entries.append((int(row["entry_va"], 16), row["convention"],
                        row["module"], row["function"]))
    entries.sort()
    ent_set = {e[0] for e in entries}
    print("named wrapper entries: %d" % len(ent_set))

    # body extents: entry -> end (next entry, capped); None where the gap
    # exceeds BODY_MAX (the space in between is then not wrapper body)
    body_end = {}
    for i, (va, *_rest) in enumerate(entries):
        nxt = entries[i + 1][0] if i + 1 < len(entries) else wi[1]
        body_end[va] = nxt if nxt - va <= BODY_MAX else va + BODY_MAX

    def locate(t):
        """Classify a .-wI target: entry / prefix-hit / mid-body / other."""
        if t in ent_set:
            return "entry", t
        import bisect
        idx = bisect.bisect_right([e[0] for e in entries], t) - 1
        if idx >= 0:
            base = entries[idx][0]
            if base <= t < base + PREFIX_MAX:
                return "prefix-hit", base
            if base <= t < body_end[base]:
                return "mid-body", base
        return "other", None

    # ---- census ------------------------------------------------------------
    refs = defaultdict(list)              # target -> [(src_va, src, mnem)]
    for sec in secs:
        if not sec["exec"]:
            continue
        if sec["name"] in (".vmp1", ".#|U"):
            gen = sweep_bytescan(img, secs, sec, wi)
        elif sec["name"] == ".-wI" or sec["name"] in (
                ".text", ".tls", ".mackt", ".vmp0", ".bss", ".rsrc"):
            gen = sweep_native(img, make_md(), secs, sec, wi)
        else:
            continue
        n = 0
        for src_va, mnem, t in gen:
            refs[t].append((src_va, sec["name"], mnem))
            n += 1
        print("  swept %-8s -> %6d transfers into .-wI" % (sec["name"], n))

    # ---- question 1: shared components --------------------------------------
    md = make_md()
    shared = sorted(((t, v) for t, v in refs.items() if len(v) >= SHARED_REFS),
                    key=lambda kv: -len(kv[1]))
    print("\n=== shared components (refs >= %d): %d ===" % (SHARED_REFS,
                                                            len(shared)))
    rows = []
    for t, v in shared:
        kind, base = locate(t)
        by_src = defaultdict(int)
        by_mnem = defaultdict(int)
        in_body = 0
        for src_va, src, mnem in v:
            by_src[src] += 1
            by_mnem[mnem] += 1
            k, b = locate(src_va) if wi[0] <= src_va < wi[1] else ("outside", None)
            if k in ("entry", "prefix-hit", "mid-body"):
                in_body += 1
        print("\n0x%08X  refs=%d  kind=%s" % (t, len(v), kind))
        for src, c in sorted(by_src.items(), key=lambda kv: -kv[1]):
            print("    from %-8s %5d   (%s)" % (src, c,
                  ", ".join("%s:%d" % kv for kv in sorted(by_mnem.items()))))
        print("    refs originating inside named wrapper bodies: %d/%d"
              % (in_body, len(v)))
        ins = list(md.disasm(img[t - IMAGE_BASE:t - IMAGE_BASE + 40], t))[:6]
        print("    " + " | ".join("%s %s" % (i.mnemonic, i.op_str) for i in ins))
        rows.append({"target": "0x%08X" % t, "refs": len(v), "kind": kind,
                     "by_section": ";".join("%s:%d" % kv for kv in by_src.items()),
                     "from_named_bodies": in_body})
    if rows:
        with open(a.out, "w", newline="", encoding="utf-8") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print("wrote %s" % a.out)
    else:
        print("no shared components found; %s not written" % a.out)

    # ---- question 2: entry-rewrite collateral --------------------------------
    print("\n=== collateral check over %d distinct .-wI targets ===" % len(refs))
    hits = defaultdict(list)
    for t, v in refs.items():
        kind, base = locate(t)
        if kind in ("prefix-hit", "mid-body"):
            hits[kind].append((t, base, v))

    # The decisive metric: a landing point inside a rewritten prefix is only a
    # problem if the code that jumps there SURVIVES the rewrite. Sources inside
    # a named wrapper body die with their host; everything else (mutated CRT,
    # unnamed .-wI code, .mackt, .tls, .text, VM bytecode) keeps running.
    def source_class(src_va, src_sec):
        if wi[0] <= src_va < wi[1]:
            k, _b = locate(src_va)
            return "dies-with-host" if k != "other" else "survives (.-wI unnamed/CRT)"
        return "survives (%s)" % src_sec

    survivors = []
    all_refs = []
    for kind in ("prefix-hit",):
        for t, base, v in hits[kind]:
            for src_va, src, mnem in v:
                cls = source_class(src_va, src)
                # self = source inside the very wrapper whose prefix is hit
                k, _b = locate(src_va) if wi[0] <= src_va < wi[1] else ("outside", None)
                self_ref = (k in ("entry", "prefix-hit", "mid-body")
                            and _b == base)
                if cls.startswith("survives"):
                    idx = [e[0] for e in entries].index(base)
                    _va, conv, mod, func = entries[idx]
                    survivors.append((t, base, conv, mod, func,
                                      src_va, cls, mnem))
                all_refs.append((t, base, t - base, src_va, src, mnem,
                                 cls, "self" if self_ref else "external"))
    with open("wrapper-prefix-refs-all.csv", "w", newline="",
              encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["target", "host_entry", "offset", "source_va",
                    "source_section", "mnemonic", "source_class", "scope"])
        for row in all_refs:
            w.writerow(["0x%08X" % row[0], "0x%08X" % row[1], row[2],
                        "0x%08X" % row[3], row[4], row[5], row[6], row[7]])
    print("wrote wrapper-prefix-refs-all.csv (%d refs)" % len(all_refs))
    print("prefix-hits referenced from SURVIVING code: %d" % len(survivors))
    for t, base, conv, mod, func, src_va, cls, mnem in sorted(survivors)[:40]:
        print("  target 0x%08X (+%d in 0x%08X %s!%s conv=%s) <- %s 0x%08X %s"
              % (t, t - base, base, mod, func, conv,
                 cls.split(" (")[0], src_va, mnem))
    with open("wrapper-prefix-survivors.csv", "w", newline="",
              encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["target", "host_entry", "offset", "convention", "module",
                    "function", "source_va", "source_class", "mnemonic"])
        for t, base, conv, mod, func, src_va, cls, mnem in sorted(survivors):
            w.writerow(["0x%08X" % t, "0x%08X" % base, t - base, conv, mod,
                        func, "0x%08X" % src_va, cls, mnem])
    print("wrote wrapper-prefix-survivors.csv")

    for kind in ("prefix-hit", "mid-body"):
        n_refs = sum(len(v) for _t, _b, v in hits[kind])
        print("%s: %d targets / %d refs" % (kind, len(hits[kind]), n_refs))
    not_entry = sum(1 for t in refs if locate(t)[0] == "other")
    named_targets = sum(1 for t in refs if locate(t)[0] == "entry")
    print("targets that are named entries: %d; unaffiliated .-wI code: %d"
          % (named_targets, not_entry))


if __name__ == "__main__":
    main()
