"""Phase 0 for the .vmp1 handler-semantics census.

Each unit is one VM handler. The evidence is produced deterministically:

  * the collapsed instruction trace (symeval follows the `call`-used-as-`jmp`
    trampolines the mutation layer sprinkles through every handler, so what
    comes out is the handler's real instruction sequence);
  * every instruction with its verbatim bytes, so a reader quotes evidence by
    copying rather than from memory -- the discipline that kept the wrapper
    census at zero hallucinated citations across 1,654 rows;
  * the symbolic machine effect: which VM registers and stack slots the handler
    read and wrote, expressed over the VM context pointer edi and the virtual
    instruction pointer esi;
  * how often the walk over 961 real VM functions dispatched to it, and the
    operand width it consumes.

The VM context layout is known and measured: edi is the context pointer, [edi]
holds the image delta, and the virtual register file is [edi + idx*4]. esi is
the virtual instruction pointer; the stream is
[next-handler ptr : 4][operand : 0..n].
"""
import argparse
import csv
import json
import re
import struct
import sys

sys.path.insert(0, "e:/Project/DNF/DNF-JP-86-Server/.worktrees/client-debug-lab/ClientHack/tools")
import symeval as S

BASE = 0x400000


def xranges(img):
    e = struct.unpack_from("<I", img, 0x3C)[0]
    fh = e + 4
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


def sections(img):
    e = struct.unpack_from("<I", img, 0x3C)[0]
    fh = e + 4
    n = struct.unpack_from("<H", img, fh + 2)[0]
    o = struct.unpack_from("<H", img, fh + 16)[0]
    st = fh + 20 + o
    out = []
    for i in range(n):
        p = st + i * 40
        name = img[p:p + 8].rstrip(b"\0").decode("latin1")
        vs, va, rs, ro = struct.unpack_from("<IIII", img, p + 8)
        out.append((name, va, max(vs, rs)))
    return out


def where(secs, va):
    rva = va - BASE
    for name, sva, sz in secs:
        if sva <= rva < sva + sz:
            return name
    return "?"


ESI = re.compile(r"\besi0\b")
EDI = re.compile(r"\bedi0\b")


def readable(expr):
    """Rename the symbolic seeds to what they mean in this VM."""
    t = S.show(expr)
    t = ESI.sub("VIP", t)
    t = EDI.sub("CTX", t)
    return t


def effect(mc):
    """Registers the handler leaves changed, and the memory it touched."""
    regs = {}
    for k in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "esp"):
        e = mc.r.get(k)
        if e is None:
            continue
        t = readable(e)
        if t == k + "0":
            continue
        regs[k] = t
    writes = []
    for addr, val in list(mc.mem.items())[:24]:
        a = readable(addr) if not isinstance(addr, int) else "0x%X" % addr
        writes.append({"addr": a, "value": readable(val)})
    return regs, writes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--handlers", required=True, help="vmp1-handlers-a21.csv")
    ap.add_argument("--out", required=True)
    ap.add_argument("--top", type=int, default=276)
    ap.add_argument("--budget", type=int, default=300)
    a = ap.parse_args()

    img = open(a.dump, "rb").read()
    xr = xranges(img)
    xok = lambda rva: any(lo <= rva < hi for lo, hi in xr)
    secs = sections(img)

    rows = list(csv.DictReader(open(a.handlers, encoding="utf-8-sig")))[:a.top]
    n = 0
    with open(a.out, "w", encoding="utf-8") as fh:
        for r in rows:
            va = int(r["handler"], 16)
            mc, trace, (why, tgt) = S.run(img, va, budget=a.budget,
                                          follow=True, xok=xok)
            regs, writes = effect(mc)
            rec = {
                "handler": r["handler"],
                "section": where(secs, va),
                "dispatch_refs": int(r["dispatch_refs"]),
                "vip_advance": r["vip_advance"],
                "operand_bytes": (int(r["vip_advance"]) - 4
                                  if r["vip_advance"] not in ("", "None") else None),
                "dispatch_bias": r["dispatch_bias"],
                "n_insns": len(trace),
                "exit": why,
                "exit_expr": readable(tgt) if tgt is not None else None,
                "regs_changed": regs,
                "memory_writes": writes,
                "unmodelled": dict(mc.unmodelled),
                "insns": [{
                    "va": "0x%08X" % i.address,
                    "sec": where(secs, i.address),
                    "text": ("%s %s" % (i.mnemonic, i.op_str)).strip(),
                    "hex": " ".join("%02x" % b for b in i.bytes),
                } for i in trace],
            }
            fh.write(json.dumps(rec, ensure_ascii=False) + "\n")
            n += 1
    print("wrote %s: %d handlers" % (a.out, n))
    return 0


if __name__ == "__main__":
    sys.exit(main())
