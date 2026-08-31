"""Lift a walked .vmp1 function from VM bytecode to readable pseudo-code.

Inputs are all measured, not assumed:
  * vm-walk.jsonl      -- the dispatch sequence of each function, from walking
                          real live-captured keys
  * vmp1-opcodes-a21.csv -- what each handler does, from the handler census
  * the dump           -- for the inline operands

Immediates are not stored in the clear. Each VM_PUSH_IMM handler folds the
inline operand into a key derived from the VIP and then runs a fixed bijective
chain, e.g.

    eax = VIP - [CTX+4] + operand + K ; rol 7 ; dec ; not ; ror 1 ;
    xor 0x31DB5127 ; ror 7 ; not

with K per handler. `[CTX+4]` holds the image delta, which vm_entry sets from
its own self-relocation probe and which is 0 for a fixed-base dump. So the
chain is computable statically -- but only if it is read out of each handler's
own trace rather than assumed, which is what extract_imm_chain does.

The stack folder then turns the stack machine back into expressions: pushes
become values, an operation pops its arguments and pushes a term. What comes
out is not the original source -- register allocation and instruction selection
are gone forever -- but it is the same computation.
"""
import argparse
import collections
import csv
import json
import re
import struct
import sys

BASE = 0x400000
M32 = 0xFFFFFFFF


def rol(v, n):
    n &= 31
    return ((v << n) | (v >> (32 - n))) & M32


def ror(v, n):
    n &= 31
    return ((v >> n) | (v << (32 - n))) & M32


CHAIN_OPS = {
    "add": lambda v, k: (v + k) & M32,
    "sub": lambda v, k: (v - k) & M32,
    "xor": lambda v, k: v ^ k,
    "or": lambda v, k: v | k,
    "and": lambda v, k: v & k,
    "rol": lambda v, k: rol(v, k),
    "ror": lambda v, k: ror(v, k),
    "shl": lambda v, k: (v << k) & M32,
    "not": lambda v, _: (~v) & M32,
    "neg": lambda v, _: (-v) & M32,
    "dec": lambda v, _: (v - 1) & M32,
    "inc": lambda v, _: (v + 1) & M32,
}


def rolw(v, n, w):
    n %= w
    m = (1 << w) - 1
    return ((v << n) | (v >> (w - n))) & m


def extract_imm_chain(insns):
    """The arithmetic a VM_PUSH_IMM handler applies, read from its own trace.

    Starts at the instruction that folds in the inline operand
    (`add eax, [esi+N]`) and runs until the value is stored or pushed."""
    chain = []
    started = False
    width = 32
    for ins in insns:
        t = ins["text"]
        if not started:
            # 32-bit variants fold the operand with `add eax, [esi+N]`;
            # 1-byte variants use `add al, byte ptr [esi+N]` and then run the
            # whole chain at byte width, which changes what rol/ror mean.
            m32 = re.match(r"^add eax, dword ptr \[esi \+ \d+\]$", t)
            m8 = re.match(r"^add al, byte ptr \[esi \+ \d+\]$", t)
            if m32 or m8:
                started = True
                width = 32 if m32 else 8
            continue
        reg = "eax" if width == 32 else "al"
        if t.startswith("mov dword ptr [esp], eax") or t in ("push eax", "push ax"):
            break
        if t == "xor ah, ah":          # zero-extend before the word push
            continue
        m = re.match(r"^(\w+) %s(?:, (.+))?$" % reg, t)
        if not m:
            if t.split()[0] in ("mov", "push", "pushfd", "lodsd", "call", "lea",
                                "jmp", "nop"):
                continue
            break
        op, arg = m.group(1), m.group(2)
        if op not in CHAIN_OPS:
            break
        if arg is None or op in ("not", "neg", "dec", "inc"):
            chain.append((op, 0))
        elif arg.startswith("0x") or arg.isdigit():
            chain.append((op, int(arg, 0)))
        else:
            break
    if not started:
        return None
    return {"width": width, "ops": chain}




class Lifter:
    def __init__(self, img, opcodes, pack):
        self.img = img
        self.ops = opcodes
        self.pack = pack
        self.chains = {}
        for h, u in pack.items():
            if opcodes.get(h, {}).get("op") == "VM_PUSH_IMM":
                c = extract_imm_chain(u["insns"])
                if c:
                    self.chains[h] = c

    def raw(self, va, n):
        o = va - BASE
        if o < 0 or o + n > len(self.img):
            return None
        return int.from_bytes(self.img[o:o + n], "little")

    def operand(self, vip, advance):
        n = advance - 4
        if n <= 0:
            return None, 0
        return self.raw(vip + 4, n), n

    def immediate(self, handler, vip, operand):
        """Replay the handler's own decode chain. delta ([CTX+4]) is 0 here."""
        rec = self.chains.get(handler)
        if rec is None or operand is None:
            return None
        w = rec["width"]
        mask = (1 << w) - 1
        v = (vip + operand) & mask
        for op, k in rec["ops"]:
            if op == "rol":
                v = rolw(v, k, w)
            elif op == "ror":
                v = rolw(v, w - (k % w), w)
            else:
                v = CHAIN_OPS[op](v, k) & mask
        return v

    def lift(self, fn):
        out = []
        for s in fn["trace"]:
            h = s["handler"]
            rec = self.ops.get(h)
            op = rec["op"] if rec else "VM_UNKNOWN"
            vip = int(s["vip"], 16)
            adv = s["advance"] or 0
            val, n = self.operand(vip, adv)
            item = {"vip": s["vip"], "handler": h, "op": op,
                    "operand_raw": val, "operand_bytes": n}
            if op == "VM_PUSH_IMM":
                item["imm"] = self.immediate(h, vip, val)
            elif op in ("VM_PUSH_REG", "VM_POP_REG") and val is not None:
                item["reg"] = val & 0xFF
            out.append(item)
        return out


# vm_entry's caller pushes the key, then ecx, edx, ebx, eax, esi, EFLAGS, ebp,
# edi -- the save order the earlier analysis measured and found invariant across
# all 53 entry clones. So the VM does not start on an empty stack: its first
# pops are those incoming values, and a folder that starts empty reports them as
# underflow.
ENTRY_STACK = ["key", "in_ecx", "in_edx", "in_ebx", "in_eax", "in_esi",
               "in_EFLAGS", "in_ebp", "in_edi"]


def negated(x):
    """If x is syntactically a negation, return what it negates."""
    if x.startswith("~(") and x.endswith(")"):
        return x[2:-1]
    if x.startswith("~"):
        return x[1:]
    return None


def nor(a, b):
    """`~(a | b)`, reduced. VMProtect emits every boolean through NOR because
    NOR is functionally complete, so the readable operator has to be recovered:
    `~(a|a)` is `~a`, `~(~a | ~b)` is `a & b`, and `~(~a | b)` is `a & ~b`."""
    if a == b:
        n = negated(a)
        return n if n is not None else "~%s" % a
    na, nb = negated(a), negated(b)
    if na is not None and nb is not None:      # ~(~x | ~y)  ==  x & y
        return "(%s & %s)" % (na, nb)
    if na is not None:                         # ~(~x | y)   ==  x & ~y
        return "(%s & ~%s)" % (na, b)
    if nb is not None:
        return "(%s & ~%s)" % (nb, a)
    return "~(%s | %s)" % (a, b)


def fold(listing, annotate=None):
    """Turn the stack machine back into expressions."""
    st, lines = list(ENTRY_STACK), []
    def pop():
        return st.pop() if st else "<underflow>"
    for i in listing:
        op = i["op"]
        if op == "VM_PUSH_REG":
            st.append("r%d" % i.get("reg", -1))
        elif op == "VM_PUSH_IMM":
            v = i.get("imm")
            if v is None:
                st.append("imm?")
            else:
                tag = annotate(v) if annotate else None
                st.append("0x%08X%s" % (v, " <%s>" % tag if tag else ""))
        elif op == "VM_PUSH_VSP":
            st.append("VSP")
        elif op == "VM_POP_REG":
            lines.append("r%d = %s" % (i.get("reg", -1), pop()))
        elif op == "VM_POP_DISCARD":
            pop()
        elif op == "VM_NOR":
            b, a = pop(), pop()
            st.append(nor(a, b))
        elif op == "VM_ADD":
            b, a = pop(), pop()
            st.append("(%s + %s)" % (a, b))
        elif op in ("VM_SHL", "VM_SHR"):
            b, a = pop(), pop()
            st.append("(%s %s %s)" % (a, "<<" if op == "VM_SHL" else ">>", b))
        elif op == "VM_LOAD":
            st.append("[%s]" % pop())
        elif op == "VM_STORE":
            v, a = pop(), pop()
            lines.append("[%s] = %s" % (a, v))
        elif op == "VM_ADD_VSP":
            lines.append("VSP += 0x%X" % (i.get("operand_raw") or 0))
        elif op == "VM_JMP":
            lines.append("goto %s" % pop())
        elif op == "VM_EXIT":
            lines.append("return")
        else:
            lines.append("%s%s" % (op, "" if i["operand_raw"] is None
                                   else " 0x%X" % i["operand_raw"]))
    if st:
        lines.append("; stack left: %s" % ", ".join(st[-6:]))
    return lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--walk", required=True)
    ap.add_argument("--opcodes", required=True)
    ap.add_argument("--pack", required=True)
    ap.add_argument("--index", type=int, default=0)
    ap.add_argument("--count", type=int, default=1)
    ap.add_argument("--raw", action="store_true", help="show the VM listing too")
    a = ap.parse_args()

    img = open(a.dump, "rb").read()
    opcodes = {r["handler"]: r for r in
               csv.DictReader(open(a.opcodes, encoding="utf-8-sig"))}
    pack = {u["handler"]: u for u in
            (json.loads(l) for l in open(a.pack, encoding="utf-8"))}
    fns = [json.loads(l) for l in open(a.walk, encoding="utf-8")]
    # name any immediate that lands inside a section, so a decoded constant can
    # be told apart from an address at a glance
    e = struct.unpack_from("<I", img, 0x3C)[0]
    fh = e + 4
    nsec = struct.unpack_from("<H", img, fh + 2)[0]
    osz = struct.unpack_from("<H", img, fh + 16)[0]
    stab = fh + 20 + osz
    SECS = []
    for i in range(nsec):
        q = stab + i * 40
        nm = img[q:q + 8].rstrip(bytes(1)).decode("latin1")
        vs, va, rs, ro = struct.unpack_from("<IIII", img, q + 8)
        SECS.append((nm, va + BASE, va + BASE + max(vs, rs)))

    def ann(v):
        for nm, lo, hi in SECS:
            if lo <= v < hi:
                return nm
        return None

    lf = Lifter(img, opcodes, pack)
    print("immediate decode chains recovered for %d handlers" % len(lf.chains))

    cov = collections.Counter()
    for fn in fns[a.index:a.index + a.count]:
        listing = lf.lift(fn)
        for i in listing:
            cov[i["op"]] += 1
        print("\n" + "=" * 72)
        print("VM function  key=%s  vip=%s  %d steps  stop=%s"
              % (fn["key"], fn["vip"], fn["steps"], fn["stop"]))
        print("=" * 72)
        if a.raw:
            for i in listing:
                extra = ""
                if "reg" in i:
                    extra = " r%d" % i["reg"]
                elif i.get("imm") is not None:
                    extra = " = 0x%08X" % i["imm"]
                elif i["operand_raw"] is not None:
                    extra = " raw=0x%X" % i["operand_raw"]
                print("   %s  %-16s%s" % (i["vip"], i["op"], extra))
            print("   ---- folded ----")
        for line in fold(listing, annotate=ann):
            print("   " + line)
    print("\nopcode coverage over the printed functions:", dict(cov.most_common()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
