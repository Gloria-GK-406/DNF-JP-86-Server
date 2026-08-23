"""Name the Themida API wrappers that `.text` calls, by evaluating them.

`name_api_sites.py` names the cross-module pointers whose per-site constant K
is zero. The other 452 stay anonymous there, and six attempts to recover K from
the stored data failed -- shared table, K=f(site), shared helper, 64K alignment,
a table in the image, symbolic evaluation of the code after the datum. Every one
of them started from the data and looked for the code that reads it, and nothing
in the image points at those addresses, so there was no thread to pull.

The anchor was wrong. `.text` issues direct calls into `.-wI`, and those targets
are real entry points by construction, because compiled game code calls them.
Evaluating a wrapper forward with the image's own bytes produces the API address
as a constant, and K never has to be named at all.

Validation is structural and strict: a computed target counts only when it lands
inside a module that was actually loaded, at an address that module really
exports. Run against several dumps and the answers must agree even though the
module bases differ -- that is the check worth trusting, because it is not
"lands on some export" but "lands on the same function under a different
layout".

Also emits, per wrapper, which relocation-table row it consumes and which of
four calling conventions it implements. Both matter for replacing the wrapper
layer later: the row tells you what becomes dead, the convention tells you what
an equivalent stub has to do.
"""
import argparse
import collections
import csv
import struct
import sys
from pathlib import Path

import symeval
from name_api_sites import export_map, load_modules

BASE = 0x400000
RET_MARK = 0x11110000
ARG_MARK = [0xAAAA0001, 0xAAAA0002, 0xAAAA0003, 0xAAAA0004, 0xAAAA0005]


def sections(orig):
    d = Path(orig).read_bytes()
    e = struct.unpack_from("<I", d, 0x3C)[0]
    fh = e + 4
    n = struct.unpack_from("<H", d, fh + 2)[0]
    t = fh + 20 + struct.unpack_from("<H", d, fh + 16)[0]
    out = []
    for i in range(n):
        h = t + i * 40
        name = d[h:h + 8].rstrip(b"\0").decode("latin1")
        vs, va, raw, _ = struct.unpack_from("<IIII", d, h + 8)
        ch = struct.unpack_from("<I", d, h + 36)[0]
        out.append((name, va, va + max(vs, raw), bool(ch & 0x20000000)))
    return out


def call_sites(img, text, target_span):
    """`call rel32` in .text whose target lands in the wrapper section.

    A byte scan is enough here and a full linear sweep is not worth its cost:
    a false positive cannot invent a name, because the target still has to
    evaluate to a real export of the module the loader actually mapped there.
    """
    lo, hi = text
    tlo, thi = target_span
    out = {}
    blob = img[lo:hi]
    start = 0
    while True:
        p = blob.find(b"\xE8", start)
        if p < 0 or p + 5 > len(blob):
            break
        start = p + 1
        rel = struct.unpack_from("<i", blob, p + 1)[0]
        tv = BASE + lo + p + 5 + rel
        if tlo <= tv - BASE < thi:
            out.setdefault(tv, BASE + lo + p)
    return out


class Resolver:
    def __init__(self, dump, orig, search):
        self.img = (Path(dump) / "DNF-memory-rva.bin").read_bytes()
        self.mods = load_modules(Path(dump) / "DNF-modules.csv")
        self.lo, self.hi = self.mods["dnf.exe"]
        self.ranges = sorted((a, b, m) for m, (a, b) in self.mods.items())
        self.secs = sections(orig)
        spans = sorted((a, b) for _n, a, b, x in self.secs if x)
        self.xok = lambda rva: any(a <= rva < b for a, b in spans)
        self.search = [Path(p) for p in search]
        self._exp = {}

    def exports(self, m):
        if m not in self._exp:
            p = next((d / m for d in self.search if (d / m).exists()), None)
            self._exp[m] = export_map(p) if p else {}
        return self._exp[m]

    def owner(self, v):
        for a, b, m in self.ranges:
            if a <= v < b:
                return m, a
        return None, None

    def resolve(self, entry, retaddr=None, markers=False):
        """Follow the wrapper to the API, carrying state across trampolines."""
        mc = symeval.Mach(self.img, retaddr)
        if markers:
            for i, v in enumerate(ARG_MARK):
                mc.mem[symeval.ESP0 + 4 + 4 * i] = symeval.C(v)
        cur = entry
        hit = None
        for _hop in range(120):
            mc, _tr, (_why, tgt) = symeval.run(
                self.img, cur, budget=4000, follow=True, mc=mc, xok=self.xok)
            if tgt is None or tgt[0] != "const":
                break
            c = tgt[1] & 0xFFFFFFFF
            m, base = self.owner(c)
            if m is not None and m != "dnf.exe":
                fn = self.exports(m).get(c - base)
                if fn:
                    hit = (m, fn)
                break
            if not (self.lo <= c < self.hi):
                break
            cur = c
        return hit, mc

    def convention(self, entry):
        """What the API sees at entry, versus what a plain `call` would give."""
        _hit, mc = self.resolve(entry, retaddr=RET_MARK, markers=True)
        esp = mc.r["esp"]
        if not symeval.is_c(esp):
            return "esp-symbolic"
        delta = esp[1] - symeval.ESP0

        def tag(v):
            if not symeval.is_c(v):
                return "?"
            x = v[1]
            if x == RET_MARK:
                return "R"
            if x == RET_MARK + 1:
                return "R+1"
            return "a%d" % (ARG_MARK.index(x) + 1) if x in ARG_MARK else "."
        return "esp%+d/%s" % (delta, tag(mc.load(symeval.C(esp[1]), 4)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("dump", help="dump directory (DNF-memory-rva.bin + modules)")
    ap.add_argument("orig", help="original packed DNF.exe, for section headers")
    ap.add_argument("--out", default="wrapper-apis-a21.csv")
    ap.add_argument("--relocs", help="crossmodule-relocs csv, to cross-check")
    ap.add_argument("--section", default=".-wI", help="wrapper section name")
    ap.add_argument("--search", nargs="*", default=[
        r"C:\Windows\SysWOW64", r"D:\project\DNF\DNFClient-A21-debug"])
    a = ap.parse_args()

    R = Resolver(a.dump, a.orig, a.search)
    text = next((lo, hi) for n, lo, hi, _x in R.secs if n == ".text")
    wi = next((lo, hi) for n, lo, hi, _x in R.secs if n == a.section)
    sites = call_sites(R.img, text, wi)
    print("candidate call sites in .text targeting %s: %d "
          "(byte scan -- includes false positives, which cannot be told apart "
          "from genuinely hard wrappers by counting alone)"
          % (a.section, len(sites)))

    b2s = {}
    rel = {}
    if a.relocs:
        for row in csv.DictReader(open(a.relocs, encoding="utf-8-sig")):
            s = int(row["site_rva"], 16)
            rel[s] = (row["module"].strip().lower(), row["resolution"].strip())
            for k in range(4):
                b2s[s + k] = s

    rows = []
    conv = collections.Counter()
    witness = collections.defaultdict(collections.Counter)
    for entry in sorted(sites):
        cs = sites[entry]
        hit, mc = R.resolve(entry, retaddr=cs + 5)
        if not hit:
            continue
        used = sorted({b2s[o] for o in mc.imgreads if o in b2s})
        for s in used:
            witness[s][hit[0]] += 1
        rows.append((entry, cs, hit[0], hit[1], R.convention(entry),
                     "0x%08X" % used[0] if len(used) == 1 else ""))
        conv[rows[-1][4]] += 1

    with open(a.out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["entry_va", "call_site_va", "module", "function",
                    "convention", "reloc_site"])
        for e, cs, m, fn, c, s in rows:
            w.writerow(["0x%08X" % e, "0x%08X" % cs, m, fn, c, s])
    print("named %d / %d   distinct APIs %d   -> %s"
          % (len(rows), len(sites), len({(r[2], r[3]) for r in rows}), a.out))

    print("\ncalling conventions (a plain `call` would be esp+0/R):")
    for k, v in conv.most_common():
        print("   %-16s %d" % (k, v))

    if rel:
        print("\nrelocation rows witnessed: %d / %d" % (len(witness), len(rel)))
        bad = [s for s, w in witness.items() if len(w) > 1]
        print("   rows two wrappers disagree about (must be 0): %d" % len(bad))
        firm = ("unique-delta", "refined-pair1")
        contra = [s for s, w in witness.items()
                  if len(w) == 1 and rel[s][1] in firm
                  and next(iter(w)) != rel[s][0]]
        print("   contradicting a confidently-resolved row (must be 0): %d"
              % len(contra))
        soft = [s for s in witness if rel[s][1] not in firm]
        print("   rows the differential could not pin down, now witnessed: %d"
              % len(soft))
        for s in sorted(soft):
            w = next(iter(witness[s]))
            print("      0x%08X  table=%-18s -> %-16s %s"
                  % (s, rel[s][0], w, "CORRECTED" if w != rel[s][0] else "ok"))


if __name__ == "__main__":
    sys.exit(main())
