"""Decrypt every obfuscated string literal in .text and attribute it to a function.

The image's string literals are encrypted, which is why a plain strings pass
over it returns 21,921 entries that are mostly noise -- the "noise" was
ciphertext. The cipher was recovered from the disassembly during the analysis
fleet run (see decstr.py): a 4-byte header carries the length and the key seed,
the body is a dword-chained XOR, and the tail bytes use a second multiplier.

It is self-validating. A wrong address produces either a failed decode or text
that fails the printability score, so scanning every immediate operand that
points into the data range costs nothing in precision -- what comes back is
either a real literal or nothing.

The payoff is not the strings themselves. It is that many of them are __FILE__
expansions carrying the client's own source paths, which gives exact module
attribution for the functions that reference them -- ground truth, where the
API-fingerprint approach could only guess.
"""
import bisect
import csv
import re
import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from decstr import BASE, dec, img                            # noqa: E402

SCRATCH = Path(__file__).resolve().parent
ORIG = Path(r"D:\project\DNF\DNFClient-A21-debug\DNF.exe")


def sections():
    d = ORIG.read_bytes()
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


def main():
    secs = sections()
    tlo, thi = next((a, b) for n, a, b, _x in secs if n == ".text")
    data = [(a, b) for n, a, b, x in secs if not x and n in
            (".code", ".data", ".rdata")]
    lo = min(a for a, _b in data) + BASE
    hi = max(b for _a, b in data) + BASE
    print("literal address window: 0x%08X - 0x%08X" % (lo, hi))

    entries = sorted(int(r["func_va"], 16) for r in
                     csv.DictReader((SCRATCH / "functions.csv")
                                    .open(encoding="utf-8")))

    # Immediate-carrying encodings that hand an address to code.
    OPS = [(b"\x68", 1)] + [(bytes((0xB8 + r,)), 1) for r in range(8)] + \
          [(b"\xC7\x05", 6), (b"\xC7\x45", 3), (b"\x3D", 1), (b"\x81\x3D", 6)]
    blob = img[tlo:thi]
    cand = Counter()
    for op, immoff in OPS:
        start = 0
        while True:
            p = blob.find(op, start)
            if p < 0 or p + immoff + 4 > len(blob):
                break
            start = p + 1
            v = struct.unpack_from("<I", blob, p + immoff)[0]
            if lo <= v < hi:
                cand[(BASE + tlo + p, v)] += 1
    print("immediate operands pointing into the data window: %d" % len(cand))

    cache = {}
    per_func = defaultdict(dict)
    kinds = Counter()
    for (site, v) in cand:
        if v not in cache:
            try:
                cache[v] = dec(v)
            except Exception:
                cache[v] = None
        r = cache[v]
        if not r or len(r[1]) < 3:
            continue
        kinds[r[0]] += 1
        i = bisect.bisect_right(entries, site) - 1
        if i >= 0:
            per_func[entries[i]][v] = r[1]

    total = sum(len(v) for v in per_func.values())
    print("literals decrypted: %d distinct addresses, %d (function, literal) pairs"
          % (len([k for k, v in cache.items() if v]), total))
    print("by encoding: %s" % dict(kinds))
    print("functions carrying at least one literal: %d" % len(per_func))

    with (SCRATCH / "literals.csv").open("w", newline="",
                                         encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["func_va", "literal_va", "text"])
        for fn in sorted(per_func):
            for va, t in sorted(per_func[fn].items()):
                w.writerow(["0x%08X" % fn, "0x%08X" % va,
                            t.replace("\r", " ").replace("\n", " ")[:400]])

    # __FILE__ expansions give exact module attribution.
    SRC = re.compile(r"[A-Za-z]:\\[^\"\r\n]*?\\Source\\([^\"\r\n]+?\.(?:cpp|h|c|hpp))",
                     re.I)
    ALT = re.compile(r"((?:DNFClient|DNFShared|nengine|RDCommon|RDAR)"
                     r"[\\/][^\"\r\n]+?\.(?:cpp|h|c|hpp))", re.I)
    fileof = {}
    for fn, lits in per_func.items():
        for va, t in lits.items():
            m = SRC.search(t) or ALT.search(t)
            if m:
                fileof.setdefault(fn, m.group(1).replace("/", "\\"))
    print()
    print("functions with a __FILE__ source path: %d" % len(fileof))
    mods = Counter()
    for p in fileof.values():
        parts = p.split("\\")
        mods["\\".join(parts[:2]) if len(parts) > 1 else parts[0]] += 1
    print("top source directories:")
    for m, c in mods.most_common(25):
        print("   %-52s %d" % (m, c))

    with (SCRATCH / "func-source-file.csv").open("w", newline="",
                                                 encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["func_va", "source_file"])
        for fn in sorted(fileof):
            w.writerow(["0x%08X" % fn, fileof[fn]])
    print()
    print("wrote literals.csv and func-source-file.csv")


if __name__ == "__main__":
    main()
