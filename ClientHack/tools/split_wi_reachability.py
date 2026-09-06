"""Split `.-wI` into API-wrapper code and mutated-CRT code by reachability.

The two live interleaved -- the CRT continuation sits 1,390 bytes past a named
wrapper entry and the .eI4 interpreter read point 835 bytes past another -- so
no address range separates them. Reachability does: recursive descent from the
1,654 named wrapper entries marks wrapper code, recursive descent from the CRT
continuation marks CRT code, and what neither reaches is dormant.

Descent rules are deliberately conservative for mutated code:
  * follow direct call/jmp and both sides of a conditional;
  * a `call` is also treated as a jump, because the mutation layer routinely
    uses `call X` where X pops the return address with `lea esp,[esp+4]`;
  * stop at indirect transfers, `ret`, and outside executable memory.
"""
import argparse
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32, CS_OP_IMM

BASE = 0x400000
STOP = {"ret", "retf", "iretd", "int3", "hlt", "ud2"}


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
        ch = struct.unpack_from("<I", img, p + 36)[0]
        out.append((name, va, max(vs, rs), ch))
    return out


def descend(img, seeds, xok, covered, cap):
    """Mark every byte reachable from seeds. `covered` is a bytearray over the
    image, so overlapping runs from different seeds are counted once."""
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    seen = set()
    work = list(seeds)
    ins_count = 0
    while work and ins_count < cap:
        va = work.pop()
        while ins_count < cap:
            rva = va - BASE
            if va in seen or not xok(rva) or rva + 16 > len(img):
                break
            seen.add(va)
            ins = next(md.disasm(img[rva:rva + 16], va, 1), None)
            if ins is None:
                break
            ins_count += 1
            for k in range(ins.size):
                covered[rva + k] = 1
            m = ins.mnemonic
            if m in STOP:
                break
            ops = ins.operands
            direct = ops and ops[0].type == CS_OP_IMM
            if m == "jmp":
                if direct:
                    va = ops[0].imm
                    continue
                break
            if m == "call":
                if direct:
                    # also a jump: the mutation layer discards the pushed
                    # return address with `lea esp,[esp+4]` in the callee
                    work.append(ins.address + ins.size)
                    va = ops[0].imm
                    continue
                break
            if m.startswith("j"):
                if direct:
                    work.append(ops[0].imm)
                va += ins.size
                continue
            va += ins.size
    return ins_count, len(seen)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--wrappers", required=True)
    ap.add_argument("--crt-seed", default="0x05D06329")
    ap.add_argument("--cap", type=int, default=4000000)
    a = ap.parse_args()

    img = bytearray(open(a.dump, "rb").read())
    secs = sections(img)
    xr = [(va, va + sz) for _, va, sz, ch in secs if ch & 0x20000000]
    xok = lambda rva: any(lo <= rva < hi for lo, hi in xr)
    wi = next((va, sz) for name, va, sz, _ in secs if name == ".-wI")
    lo, hi = wi[0], wi[0] + wi[1]

    import csv
    entries = sorted({int(r["entry_va"], 16) for r in
                      csv.DictReader(open(a.wrappers, encoding="utf-8-sig"))})

    def share(cov):
        return sum(cov[lo:hi])

    cw = bytearray(len(img))
    n, s = descend(img, entries, xok, cw, a.cap)
    print("wrapper descent : %d instructions from %d entries, %d sites"
          % (n, len(entries), s))
    print("   .-wI bytes reached: %d / %d  (%.1f%%)"
          % (share(cw), hi - lo, 100.0 * share(cw) / (hi - lo)))

    cc = bytearray(len(img))
    n2, s2 = descend(img, [int(a.crt_seed, 16)], xok, cc, a.cap)
    print("CRT descent     : %d instructions from 1 seed, %d sites" % (n2, s2))
    print("   .-wI bytes reached: %d / %d  (%.1f%%)"
          % (share(cc), hi - lo, 100.0 * share(cc) / (hi - lo)))

    both = sum(1 for i in range(lo, hi) if cw[i] and cc[i])
    only_w = sum(1 for i in range(lo, hi) if cw[i] and not cc[i])
    only_c = sum(1 for i in range(lo, hi) if cc[i] and not cw[i])
    neither = (hi - lo) - both - only_w - only_c
    print("\n.-wI byte attribution:")
    print("   wrappers only : %9d  (%.1f%%)" % (only_w, 100.0 * only_w / (hi - lo)))
    print("   CRT only      : %9d  (%.1f%%)" % (only_c, 100.0 * only_c / (hi - lo)))
    print("   both          : %9d  (%.1f%%)" % (both, 100.0 * both / (hi - lo)))
    print("   neither       : %9d  (%.1f%%)" % (neither, 100.0 * neither / (hi - lo)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
