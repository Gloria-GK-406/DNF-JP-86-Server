"""Enumerate the .vmp1 handler set by closure over dispatch biases.

The VM is direct-threaded and each dispatch site carries its OWN additive bias
(the manual analysis noted "per-handler bias, NOT 0x4CC42998" and it meant it
literally). Scanning the bytecode with a single bias therefore finds only the
handlers reachable through that one dispatch site -- the first pass found 158
with 0x5B7DF9F4 and a second bias immediately yielded 167 more.

So enumerate by fixed point instead:

  1. trace the handlers known so far, symbolically;
  2. harvest the additive constants their dispatch expressions carry;
  3. a real dispatch bias decodes the bytecode into a SHARP target distribution
     -- a few hundred addresses covering 90% of hits -- while an ordinary
     arithmetic constant decodes it into thousands. Keep the sharp ones;
  4. scan with each new bias, add the targets it concentrates on, repeat.
"""
import argparse, collections, json, re, struct, sys
import numpy as np
sys.path.insert(0, "e:/Project/DNF/DNF-JP-86-Server/.worktrees/client-debug-lab/ClientHack/tools")
import symeval as S

BASE = 0x400000
LO, HI = 0x051BF000, 0x051BF000 + 0x007CC000
SHARP_MAX = 400          # 90% of hits inside this many targets == a dispatch bias
MIN_HITS = 5000


def xranges(img):
    e = struct.unpack_from("<I", img, 0x3C)[0]; fh = e + 4
    n = struct.unpack_from("<H", img, fh + 2)[0]
    o = struct.unpack_from("<H", img, fh + 16)[0]
    st = fh + 20 + o
    out = []
    for i in range(n):
        p = st + i * 40
        vs, va, rs, ro = struct.unpack_from("<IIII", img, p + 8)
        if struct.unpack_from("<I", img, p + 36)[0] & 0x20000000:
            out.append((va, va + max(vs, rs)))
    return out


def dwords(blob):
    """Every unaligned dword position in .vmp1, as one uint32 array.

    Byte-granular because the bytecode stream is not dword-aligned: the layout
    is [next-handler ptr : 4][operand : 0..n], so a 1-byte operand shifts every
    following pointer by one."""
    b = np.frombuffer(blob, dtype=np.uint8)
    n = len(b) - 3
    return (b[0:n].astype(np.uint32)
            | (b[1:n + 1].astype(np.uint32) << 8)
            | (b[2:n + 2].astype(np.uint32) << 16)
            | (b[3:n + 3].astype(np.uint32) << 24))


def scan(dw, bias):
    t = dw + np.uint32(bias)            # wraps mod 2**32 like the CPU does
    sel = t[(t >= np.uint32(LO)) & (t < np.uint32(HI))]
    if sel.size == 0:
        return collections.Counter()
    vals, cnts = np.unique(sel, return_counts=True)
    return collections.Counter(dict(zip(vals.tolist(), cnts.tolist())))


def sharpness(hits):
    tot = sum(hits.values())
    if tot < MIN_HITS:
        return None, tot
    cum = k = 0
    for _, c in hits.most_common():
        cum += c; k += 1
        if cum >= 0.90 * tot:
            break
    return k, tot


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--seed", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--rounds", type=int, default=6)
    a = ap.parse_args()

    img = open(a.dump, "rb").read()
    xr = xranges(img)
    xok = lambda rva: any(lo <= rva < hi for lo, hi in xr)
    dw = dwords(img[LO - BASE:HI - BASE])

    handlers = {int(x, 16) for x in json.load(open(a.seed))}
    tried, good = set(), {}
    for rnd in range(1, a.rounds + 1):
        # 1-2. trace and harvest additive constants from dispatch expressions
        biases = collections.Counter()
        for va in sorted(handlers):
            mc, trace, (why, tgt) = S.run(img, va, budget=400, follow=True, xok=xok)
            txt = S.show(tgt) if tgt is not None else ""
            for m in re.finditer(r"0x([0-9A-Fa-f]{7,8})", txt):
                biases[int(m.group(1), 16)] += 1
            for ins in trace:
                m = re.match(r"^(?:add|lea) e\w\w?,? ?(?:\[e\w\w \+ )?(0x[0-9a-f]{7,8})",
                             ("%s %s" % (ins.mnemonic, ins.op_str)))
                if m:
                    biases[int(m.group(1), 16)] += 1
        # 3-4. keep the sharp ones, add what they concentrate on
        new = 0
        for b, _ in biases.most_common():
            if b in tried:
                continue
            tried.add(b)
            hits = scan(dw, b)
            k, tot = sharpness(hits)
            if k is None or k > SHARP_MAX:
                continue
            good[b] = (k, tot)
            for addr, _c in hits.most_common(k):
                if addr not in handlers:
                    handlers.add(addr); new += 1
        print("round %d: %d biases probed, %d sharp so far, handler set = %d (+%d)"
              % (rnd, len(tried), len(good), len(handlers), new))
        if new == 0:
            break

    print("\ndispatch biases (90%% of hits inside k targets):")
    for b, (k, tot) in sorted(good.items(), key=lambda kv: -kv[1][1]):
        print("   0x%08X  k=%-5d hits=%d" % (b, k, tot))
    json.dump(sorted("0x%08X" % v for v in handlers), open(a.out, "w"), indent=0)
    print("\nwrote %s: %d handlers" % (a.out, len(handlers)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
