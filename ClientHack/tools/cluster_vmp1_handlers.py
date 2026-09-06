"""Collapse the handler address set into distinct implementations.

vm_entry ships as 41-53 byte-identical clones, so the mutation layer clearly
duplicates code; if the handlers are cloned the same way, an address set of
~1200 is not ~1200 behaviours. Normalise each trace to its mnemonic/operand
SHAPE -- addresses and the per-site additive constants dropped, register names
kept -- and count the classes.
"""
import argparse, collections, json, re, struct, sys
sys.path.insert(0, "e:/Project/DNF/DNF-JP-86-Server/.worktrees/client-debug-lab/ClientHack/tools")
import symeval as S
BASE = 0x400000

def xranges(img):
    e = struct.unpack_from("<I", img, 0x3C)[0]; fh = e + 4
    n = struct.unpack_from("<H", img, fh + 2)[0]
    o = struct.unpack_from("<H", img, fh + 16)[0]; st = fh + 20 + o
    out = []
    for i in range(n):
        p = st + i * 40
        vs, va, rs, ro = struct.unpack_from("<IIII", img, p + 8)
        if struct.unpack_from("<I", img, p + 36)[0] & 0x20000000:
            out.append((va, va + max(vs, rs)))
    return out

HEX = re.compile(r"0x[0-9a-f]+")

def shape(trace):
    """mnemonic + operand form, with every literal replaced by K."""
    return " | ".join(HEX.sub("K", "%s %s" % (i.mnemonic, i.op_str)).strip()
                      for i in trace)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--handlers", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    img = open(a.dump, "rb").read()
    xr = xranges(img); xok = lambda r: any(lo <= r < hi for lo, hi in xr)
    addrs = [int(x, 16) for x in json.load(open(a.handlers))]
    groups = collections.defaultdict(list)
    empty = 0
    for va in addrs:
        mc, trace, (why, tgt) = S.run(img, va, budget=400, follow=True, xok=xok)
        if not trace:
            empty += 1; continue
        groups[shape(trace)].append(va)
    print("addresses traced : %d  (%d produced no trace)" % (len(addrs), empty))
    print("distinct shapes  : %d" % len(groups))
    sizes = sorted((len(v) for v in groups.values()), reverse=True)
    print("clone factor     : max %d, median %d" % (sizes[0], sizes[len(sizes)//2]))
    cum = 0; k = 0
    for s in sizes:
        cum += s; k += 1
        if cum >= 0.90 * sum(sizes): break
    print("90%% of addresses covered by %d shapes" % k)
    out = [{"shape_id": i, "n_clones": len(v), "representative": "0x%08X" % min(v),
            "addresses": ["0x%08X" % x for x in sorted(v)], "shape": sh}
           for i, (sh, v) in enumerate(sorted(groups.items(), key=lambda kv: -len(kv[1])))]
    json.dump(out, open(a.out, "w"), indent=1)
    print("wrote %s" % a.out)
    return 0

if __name__ == "__main__":
    sys.exit(main())
