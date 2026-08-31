"""Walk .vmp1 bytecode from real, live-captured VM entry keys.

Static enumeration of this VM does not work -- the control flow into vm_entry is
itself mutated, so no stub shape matches (15 candidates out of 14,760 clone
references, one of which produced a valid VIP). The keys therefore come from
live capture: 0160_VmEntryTrace traps byte 0 of all 53 vm_entry clones, reads
the key the caller left at [esp+0x20], and emulates the `call $+5` it displaced
so the trap stays armed for every later entry.

With a real key the rest is static:

    VIP  = rol(key, 9) + 0xBD81ACE6
    h    = [VIP] + 0x4CC42998          ; vm_entry's own bias, then esi += 4
    loop:  trace h  ->  operand width and h's OWN dispatch bias
           next = [esi] + bias ; esi += 4 + operand_width

The per-handler bias is the part that defeats scanning: each dispatch site adds
its own constant, so decoding the stream with one bias only follows the
handlers that one site reaches. Taking it from the handler's own trace is what
makes the walk exact.

Stream layout, measured: [next-handler ptr : 4][current operand : 0..n], i.e.
the pointer for step k+1 sits ahead of step k's operand.
"""
import argparse
import collections
import json
import re
import struct
import sys

sys.path.insert(0, "e:/Project/DNF/DNF-JP-86-Server/.worktrees/client-debug-lab/ClientHack/tools")
import symeval as S

BASE = 0x400000
LO, HI = 0x051BF000, 0x051BF000 + 0x007CC000
ENTRY_BIAS = 0x4CC42998
HEXC = re.compile(r"0x([0-9A-Fa-f]{7,8})")


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


def vip_advance(trace):
    """4 for the next-handler pointer plus the operand width consumed."""
    d = 0
    for ins in trace:
        m, ops = ins.mnemonic, ins.op_str
        if m == "lodsd":
            d += 4
        elif m == "lea" and ops.startswith("esi, [esi"):
            mt = re.search(r"\[esi ([+-]) (0x[0-9a-f]+|\d+)\]", ops)
            if mt:
                d += int(mt.group(2), 0) * (1 if mt.group(1) == "+" else -1)
        elif m in ("add", "sub") and ops.startswith("esi,"):
            try:
                v = int(ops.split(",")[1].strip(), 0)
            except ValueError:
                continue
            d += v if m == "add" else -v
    return d


def dispatch_bias(tgt_expr, trace):
    """The additive constant the handler folds into the next-handler pointer.

    Prefer the exit expression -- it is the value actually jumped to -- and fall
    back to the last `add reg, imm32` in the body for the ret-dispatch style,
    where the target is staged on the stack before leaving."""
    if tgt_expr:
        c = HEXC.findall(tgt_expr)
        if c:
            return int(c[-1], 16)
    for ins in reversed(trace):
        if ins.mnemonic == "add":
            c = HEXC.findall(ins.op_str)
            if c:
                return int(c[-1], 16)
    return None


class Walker:
    def __init__(self, img, xok, budget):
        self.img = img
        self.xok = xok
        self.budget = budget
        self.cache = {}

    def dw(self, va):
        o = va - BASE
        if o < 0 or o + 4 > len(self.img):
            return None
        return int.from_bytes(self.img[o:o + 4], "little")

    def handler(self, va):
        """(advance, bias, n_insns, exit) for a handler, traced once."""
        if va in self.cache:
            return self.cache[va]
        if not self.xok(va - BASE):
            r = (None, None, 0, "not executable")
        else:
            mc, trace, (why, tgt) = S.run(self.img, va, budget=self.budget,
                                          follow=True, xok=self.xok)
            expr = S.show(tgt) if tgt is not None else ""
            r = (vip_advance(trace), dispatch_bias(expr, trace), len(trace), why)
        self.cache[va] = r
        return r

    def walk(self, vip0, max_steps):
        ptr = self.dw(vip0)
        if ptr is None:
            return [], "bad VIP"
        h = (ptr + ENTRY_BIAS) & 0xFFFFFFFF
        esi = vip0 + 4
        steps = []
        while len(steps) < max_steps:
            adv, bias, n, why = self.handler(h)
            steps.append({"handler": "0x%08X" % h, "vip": "0x%08X" % esi,
                          "advance": adv, "bias": None if bias is None else "0x%08X" % bias,
                          "insns": n})
            if adv is None or bias is None or adv <= 0:
                return steps, "handler did not dispatch (%s)" % why
            ptr = self.dw(esi)
            if ptr is None:
                return steps, "VIP out of image"
            h = (ptr + bias) & 0xFFFFFFFF
            esi += adv
            if not (LO <= esi < HI):
                return steps, "VIP left .vmp1"
            if not self.xok(h - BASE):
                return steps, "next handler not executable"
        return steps, "step cap"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--functions", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-steps", type=int, default=2000)
    ap.add_argument("--budget", type=int, default=300)
    a = ap.parse_args()

    img = open(a.dump, "rb").read()
    xr = xranges(img)
    xok = lambda rva: any(lo <= rva < hi for lo, hi in xr)
    w = Walker(img, xok, a.budget)

    fns = json.load(open(a.functions))
    if a.limit:
        fns = fns[:a.limit]

    why_counts = collections.Counter()
    lengths = []
    handlers = collections.Counter()
    with open(a.out, "w", encoding="utf-8") as fh:
        for entry, key, vip, h0 in fns:
            steps, why = w.walk(int(vip, 16), a.max_steps)
            why_counts[why] += 1
            lengths.append(len(steps))
            for s in steps:
                handlers[s["handler"]] += 1
            fh.write(json.dumps({"entry": entry, "key": key, "vip": vip,
                                 "steps": len(steps), "stop": why,
                                 "trace": steps}, ensure_ascii=False) + "\n")

    lengths.sort()
    print("VM functions walked : %d" % len(fns))
    print("steps  min/median/max: %d / %d / %d"
          % (lengths[0], lengths[len(lengths) // 2], lengths[-1]))
    print("distinct handlers   : %d" % len(handlers))
    print("stop reasons        :")
    for k, v in why_counts.most_common():
        print("   %-40s %d" % (k, v))
    print("top handlers        : %s"
          % [(h, c) for h, c in handlers.most_common(8)])
    json.dump([[h, c] for h, c in handlers.most_common()],
              open("handlers-from-walk.json", "w"), indent=0)
    print("wrote %s and handlers-from-walk.json" % a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
