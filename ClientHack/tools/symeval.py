"""A partial evaluator for x86-32, used to read through Themida's mutation.

This is deliberately NOT a deobfuscator. It knows nothing about Themida, its
templates, or how it chooses constants -- it implements instruction semantics,
one mnemonic at a time, and lets the obfuscation collapse as a side effect.
That distinction is the whole reason it works: an obfuscator's rule set is
unbounded and adversary-controlled, while the x86 instruction set is closed, so
the list of things left to implement can actually be finished.

Three mechanisms do the work, and only the second involves "equivalence":

  * concrete evaluation -- the generator bakes its constants into the code, so
    most of the junk is just a long arithmetic problem;
  * expression simplification -- involutions cancel, `(x op k1) op k2` folds;
    this is algebra, not pattern-matching against the obfuscator;
  * dead code -- never removed, because it never has to be. A value nothing
    reads simply does not appear in the answer.

What it cannot see through is anything genuinely dependent on runtime state
that is absent from the dump, and virtualization -- evaluating a VM interpreter
faithfully yields the interpreter, not the function it interprets.

Accuracy notes, each of which cost a large fraction of the results until fixed:

  * memory is byte-addressed. The mutator writes one stack slot at byte, word
    and dword widths; a dword-only model loses the slot on the first narrow
    write, and every later read of it misses.
  * conditional branches are decided, not skipped. Opaque predicates are only
    useful to the obfuscator if they are decidable at generation time, so their
    inputs are concrete and the flags model resolves them.
  * `ret` and `ret imm` adjust esp. Chains hop through `push X; ret`
    trampolines, and an unadjusted esp is only visible once a chain has more
    than one hop -- it hides behind a median hop count of 1.
  * an instruction that is not modelled clears the flags rather than leaving
    stale ones for a later jcc to be decided from.
  * the walk is fenced to executable sections. Section names in this image are
    camouflage; a mispredicted branch otherwise decodes .data as instructions
    and reports a confident answer built out of noise.

Entry point: run(img, start_va, retaddr=None, budget=, follow=, mc=, xok=).
Pass `mc` back in to continue a chain across a trampoline with state intact.
"""
import sys
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_REG, X86_OP_MEM

BASE = 0x400000
M32 = 0xFFFFFFFF
ESP0 = 0x7F000000
MAX_NODES = 40

SUB = {
    "eax": ("eax", 0, 32), "ax": ("eax", 0, 16), "al": ("eax", 0, 8), "ah": ("eax", 8, 8),
    "ebx": ("ebx", 0, 32), "bx": ("ebx", 0, 16), "bl": ("ebx", 0, 8), "bh": ("ebx", 8, 8),
    "ecx": ("ecx", 0, 32), "cx": ("ecx", 0, 16), "cl": ("ecx", 0, 8), "ch": ("ecx", 8, 8),
    "edx": ("edx", 0, 32), "dx": ("edx", 0, 16), "dl": ("edx", 0, 8), "dh": ("edx", 8, 8),
    "esi": ("esi", 0, 32), "si": ("esi", 0, 16),
    "edi": ("edi", 0, 32), "di": ("edi", 0, 16),
    "ebp": ("ebp", 0, 32), "bp": ("ebp", 0, 16),
    "esp": ("esp", 0, 32), "sp": ("esp", 0, 16),
}
COND = {"je", "jne", "jz", "jnz", "ja", "jae", "jb", "jbe", "jg", "jge", "jl",
        "jle", "js", "jns", "jo", "jno", "jp", "jnp", "jcxz", "jecxz",
        "loop", "loope", "loopne"}

_uid = [0]


def sym(tag):
    _uid[0] += 1
    return ("sym", "%s#%d" % (tag, _uid[0]))


def C(n):
    return ("const", n & M32)


def is_c(e):
    return e[0] == "const"


def nodes(e):
    if e[0] in ("const", "sym"):
        return 1
    return 1 + sum(nodes(x) for x in e[2:])


def cap(e):
    return sym("big") if nodes(e) > MAX_NODES else e


def rot(v, n, w=32):
    n %= w
    v &= (1 << w) - 1
    return ((v << n) | (v >> (w - n))) & ((1 << w) - 1)


BIN = {
    "add": lambda a, b: (a + b) & M32,
    "sub": lambda a, b: (a - b) & M32,
    "xor": lambda a, b: a ^ b,
    "and": lambda a, b: a & b,
    "or": lambda a, b: a | b,
    "mul": lambda a, b: (a * b) & M32,
    "shl": lambda a, b: (a << (b & 31)) & M32,
    "shr": lambda a, b: (a & M32) >> (b & 31),
    "rol": lambda a, b: rot(a, b),
    "ror": lambda a, b: rot(a, 32 - (b % 32)),
    "sar": lambda a, b: (((a - (1 << 32)) if a & 0x80000000 else a)
                         >> (b & 31)) & M32,
}
INVERSE = {"add": "sub", "sub": "add", "xor": "xor", "rol": "ror", "ror": "rol"}


def binop(op, a, b):
    if is_c(a) and is_c(b) and op in BIN:
        return C(BIN[op](a[1], b[1]))
    # x op 0 / x op x identities
    if is_c(b):
        if b[1] == 0 and op in ("add", "sub", "xor", "or", "shl", "shr",
                                "rol", "ror"):
            return a
        if op == "and" and b[1] == M32:
            return a
        if op == "mul" and b[1] == 1:
            return a
    if a == b:
        if op == "xor" or op == "sub":
            return C(0)
        if op in ("and", "or"):
            return a
    # (x op k1) op k2  ->  x op (k1 op k2)   for the associative-with-constant ops
    if is_c(b) and a[0] == "op" and is_c(a[3]) and op in INVERSE:
        inner = a[2]
        iop = a[1]
        if {op, iop} <= {"add", "sub"}:
            k = (a[3][1] if iop == "add" else -a[3][1])
            k += (b[1] if op == "add" else -b[1])
            k &= M32
            return binop("add", inner, C(k)) if k else inner
        if op == iop == "xor":
            return binop("xor", inner, C(a[3][1] ^ b[1]))
        if op == iop in ("rol", "ror"):
            return binop(op, inner, C((a[3][1] + b[1]) % 32))
        if {op, iop} == {"rol", "ror"}:
            d = (a[3][1] - b[1]) if iop == "rol" else (b[1] - a[3][1])
            d %= 32
            return inner if d == 0 else binop("rol", inner, C(d))
    return cap(("op", op, a, b))


def unop(op, a):
    if is_c(a):
        v = a[1]
        return C({"not": ~v, "neg": -v,
                  "bswap": int.from_bytes(v.to_bytes(4, "little"), "big")}[op])
    if a[0] == "un" and a[1] == op and op in ("not", "neg", "bswap"):
        return a[2]                       # involutions cancel
    return cap(("un", op, a))


def show(e):
    if e[0] == "const":
        return "0x%08X" % e[1]
    if e[0] == "sym":
        return e[1]
    if e[0] == "un":
        return "%s(%s)" % (e[1], show(e[2]))
    return "(%s %s %s)" % (show(e[2]), e[1], show(e[3]))


def _z(f): return f["ZF"]
def _s(f): return f["SF"]
def _c(f): return f["CF"]
def _o(f): return f["OF"]
def _p(f): return f["PF"]
def _be(f): return f["CF"] or f["ZF"]
def _l(f): return f["SF"] != f["OF"]
def _le(f): return f["ZF"] or f["SF"] != f["OF"]


def _n(g):
    return lambda f: not g(f)


CC = {"e": _z, "z": _z, "ne": _n(_z), "nz": _n(_z),
      "s": _s, "ns": _n(_s), "o": _o, "no": _n(_o),
      "b": _c, "c": _c, "nae": _c, "ae": _n(_c), "nb": _n(_c), "nc": _n(_c),
      "be": _be, "na": _be, "a": _n(_be), "nbe": _n(_be),
      "l": _l, "nge": _l, "ge": _n(_l), "nl": _n(_l),
      "le": _le, "ng": _le, "g": _n(_le), "nle": _n(_le),
      "p": _p, "pe": _p, "np": _n(_p), "po": _n(_p)}


def flags_of(op, a, b, res, w):
    """The four flags a concrete ALU result implies.

    Only the concrete case is modelled: an opaque predicate is only useful to
    the obfuscator if it is decidable at generation time, so the branches that
    matter here are exactly the ones whose inputs fold to constants.
    """
    m = (1 << w) - 1
    sign = 1 << (w - 1)
    res &= m
    zf = res == 0
    sf = bool(res & sign)
    pf = bin(res & 0xFF).count("1") % 2 == 0
    if op in ("sub", "cmp", "dec"):
        cf = (a & m) < (b & m)
        of = bool(((a ^ b) & (a ^ res)) & sign)
    elif op in ("add", "inc"):
        cf = (a & m) + (b & m) > m
        of = bool((~(a ^ b) & (a ^ res)) & sign)
    elif op == "neg":
        cf = (b & m) != 0
        of = (b & m) == sign
    else:                                   # and/or/xor/test/shifts
        cf = of = False
    return {"ZF": zf, "SF": sf, "CF": cf, "OF": of, "PF": pf}


def cond_true(fl, suffix):
    if fl is None or suffix not in CC:
        return None
    return bool(CC[suffix](fl))


def stack(a):
    """Is this address in the wrapper's own synthetic stack?

    A store through an address we could not fold has to invalidate memory --
    it might have written anywhere.  But "anywhere" cannot include this stack:
    ESP0 is a synthetic base that no value in the image points at, and the
    addresses that poison the model are wild pointers built from the caller's
    registers.  Keeping the private frame trusted is what lets a chain survive
    a decoy store, and a wrong relaxation cannot invent a name -- the answer
    still has to land on a real export of the predicted module.
    """
    return abs(a - ESP0) < 0x10000


class Mach:
    def __init__(self, img, retaddr=None):
        self.img = img
        self.r = {k: sym(k + "0") for k in ("eax", "ebx", "ecx", "edx",
                                            "esi", "edi", "ebp")}
        self.r["esp"] = C(ESP0)
        self.mem = {}
        self.bmem = {}                      # byte-addressed, written by movsb
        self.other = {}
        if retaddr is not None:
            self.mem[ESP0] = C(retaddr)     # what `call` pushed
        self.aliased = False
        self.df = 0                         # direction flag, set by std/cld
        self.fl = None                      # last concrete ALU result's flags
        self.flmap = {}                     # pushfd symbol tag -> flag snapshot
        self.unsure = 0                     # branches taken on unknown flags
        self.unmodelled = {}                # mnemonic -> times it produced an opaque value
        self.unk = {}                       # one symbol per unknown address
        self.imgreads = []                  # RVAs read straight out of the image
        self.misses = []                    # address expressions we could not fold
        self.alias_src = []                 # stores that poisoned the stack model

    def read_reg(self, name):
        if name not in SUB:
            # segment and FPU registers show up in these chains; model them as
            # opaque rather than crashing -- 16% of chains were lost to this.
            self.other.setdefault(name, sym(name))
            return self.other[name]
        full, off, width = SUB[name]
        e = self.r[full]
        if width == 32:
            return e
        e = binop("shr", e, C(off)) if off else e
        return binop("and", e, C((1 << width) - 1))

    def write_reg(self, name, val):
        if name not in SUB:
            self.other[name] = val
            return
        full, off, width = SUB[name]
        if width == 32:
            self.r[full] = val
            return
        keep = binop("and", self.r[full], C(M32 ^ (((1 << width) - 1) << off)))
        ins = binop("shl", binop("and", val, C((1 << width) - 1)), C(off)) \
            if off else binop("and", val, C((1 << width) - 1))
        self.r[full] = binop("or", keep, ins)

    def _byte(self, e, i):
        """Byte i of an expression; folds to a constant when e is one."""
        return binop("and", binop("shr", e, C(8 * i)), C(0xFF))

    def _read(self, a, size):
        """Known value at a concrete address, at byte granularity.

        The obfuscator writes the same stack slot at byte, word and dword
        widths, so a dword-only model loses the slot on the first `mov word
        ptr [esp+N]` and every later read of it misses.
        """
        if self.aliased and not stack(a):
            return None
        if all((a + i) in self.bmem for i in range(size)):
            e = C(0)
            for i in range(size):
                e = binop("or", e, binop("shl", self.bmem[a + i], C(8 * i)))
            return e
        base = a & ~3
        if base in self.mem and a + size <= base + 4:
            e = self.mem[base]
            if a != base:
                e = binop("shr", e, C(8 * (a - base)))
            if size < 4:
                e = binop("and", e, C((1 << (8 * size)) - 1))
            return e
        return None

    def load(self, addr_e, size):
        if is_c(addr_e):
            a = addr_e[1]
            known = self._read(a, size)
            if known is not None:
                return known
            # a literal address inside the dumped image reads a known value
            if BASE <= a < BASE + len(self.img) and size in (1, 2, 4):
                off = a - BASE
                self.imgreads.append(off)
                return C(int.from_bytes(self.img[off:off + size], "little"))
        # One unknown address is one unknown value, not a fresh unknown per
        # read: giving repeated loads the same symbol lets them cancel.
        self.misses.append((addr_e, size))
        key = (show(addr_e), size)
        if key not in self.unk:
            self.unk[key] = sym("mem")
        return self.unk[key]

    def store(self, addr_e, size, val):
        if not is_c(addr_e):
            self.aliased = True
            self.alias_src.append(addr_e)
            return
        a = addr_e[1]
        if size == 4 and a % 4 == 0:
            self.mem[a] = val
            for i in range(4):
                self.bmem.pop(a + i, None)
            return
        # Narrower or misaligned: spill whatever dword it partially overwrites
        # down to bytes first, so the untouched bytes of that slot survive.
        for base in {(a + i) & ~3 for i in range(size)}:
            if base in self.mem:
                dw = self.mem.pop(base)
                for j in range(4):
                    self.bmem.setdefault(base + j, self._byte(dw, j))
        for i in range(size):
            self.bmem[a + i] = self._byte(val, i)


def addr_of(mc, ins, op):
    e = C(op.mem.disp & M32)
    if op.mem.base:
        e = binop("add", e, mc.read_reg(ins.reg_name(op.mem.base)))
    if op.mem.index:
        e = binop("add", e, binop("mul", mc.read_reg(ins.reg_name(op.mem.index)),
                                  C(op.mem.scale)))
    return e


def setfl(mc, op, a, b, r, w):
    if is_c(a) and is_c(b) and is_c(r):
        mc.fl = flags_of(op, a[1], b[1], r[1], w)
    else:
        mc.fl = None


def run(img, start_va, retaddr=None, budget=120, follow=False, mc=None,
        xok=None):
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    # A `push target; ret` trampoline is a jump, not a call: resuming it on a
    # fresh machine loses the registers and stack the next hop reads from, and
    # the chain dies at the first `lea esp,[esp+N]; ret`.  Accepting a machine
    # lets the caller carry that state across hops.
    if mc is None:
        mc = Mach(img, retaddr)
    rva = start_va - BASE
    seen = set()
    trace = []
    while len(trace) < budget:
        if rva in seen or rva < 0 or rva + 16 > len(img):
            break
        # Section names in this image are camouflage; the characteristics are
        # the truth.  Without this fence a mispredicted branch walks into .data
        # and decodes bytes that were never instructions, and the walk reports
        # a confident answer built entirely out of noise.
        if xok is not None and not xok(rva):
            return mc, trace, ("left executable memory", None)
        ins = next(md.disasm(img[rva:rva + 16], BASE + rva, 1), None)
        if ins is None:
            break
        seen.add(rva)
        trace.append(ins)
        m, ops = ins.mnemonic, ins.operands

        def val(op):
            if op.type == X86_OP_IMM:
                return C(op.imm)
            if op.type == X86_OP_REG:
                return mc.read_reg(ins.reg_name(op.reg))
            return mc.load(addr_of(mc, ins, op), op.size or 4)

        def put(op, e):
            if op.type == X86_OP_REG:
                mc.write_reg(ins.reg_name(op.reg), e)
            else:
                mc.store(addr_of(mc, ins, op), op.size or 4, e)

        if m == "mov":
            put(ops[0], val(ops[1]))
        elif m == "lea":
            put(ops[0], addr_of(mc, ins, ops[1]))
        elif m in ("add", "sub", "xor", "and", "or"):
            a, b = val(ops[0]), val(ops[1])
            r = binop(m, a, b)
            setfl(mc, m, a, b, r, (ops[0].size or 4) * 8)
            put(ops[0], r)
        elif m in ("shl", "shr", "rol", "ror", "sal", "sar"):
            r = binop("shl" if m == "sal" else m, val(ops[0]), val(ops[1]))
            mc.fl = None                    # shift flags are not modelled
            put(ops[0], r)
        elif m in ("bt", "bts", "btr", "btc"):
            # Only CF is architecturally defined here, so only CF is touched;
            # the rest keep their previous values rather than being invented.
            base, off = val(ops[0]), val(ops[1])
            w = (ops[0].size or 4) * 8
            if is_c(base) and is_c(off):
                bit = (base[1] >> (off[1] % w)) & 1
                if mc.fl is not None:
                    mc.fl = dict(mc.fl, CF=bool(bit))
                if m != "bt":
                    mask = 1 << (off[1] % w)
                    nv = {"bts": base[1] | mask,
                          "btr": base[1] & ~mask,
                          "btc": base[1] ^ mask}[m]
                    put(ops[0], C(nv))
            else:
                mc.fl = None
                if m != "bt":
                    put(ops[0], sym(m))
        elif m in ("adc", "sbb"):
            a, b = val(ops[0]), val(ops[1])
            cf = mc.fl["CF"] if mc.fl is not None else None
            if cf is None:
                mc.fl = None
                put(ops[0], sym(m))
            else:
                r = binop("add" if m == "adc" else "sub",
                          a, binop("add", b, C(1 if cf else 0)))
                setfl(mc, "add" if m == "adc" else "sub", a, b, r,
                      (ops[0].size or 4) * 8)
                put(ops[0], r)
        elif m in ("rcl", "rcr"):
            # rotate *through* carry: 33 bits, so it needs CF as an input.
            a, b = val(ops[0]), val(ops[1])
            cf = mc.fl["CF"] if mc.fl is not None else None
            w = (ops[0].size or 4) * 8
            if cf is None or not (is_c(a) and is_c(b)):
                mc.fl = None
                put(ops[0], sym(m))
            else:
                v = (a[1] & ((1 << w) - 1)) | (int(cf) << w)
                n = b[1] % (w + 1)
                v = ((v << n) | (v >> (w + 1 - n))) & ((1 << (w + 1)) - 1)                     if m == "rcl" else                     ((v >> n) | (v << (w + 1 - n))) & ((1 << (w + 1)) - 1)
                mc.fl = dict(mc.fl, CF=bool(v >> w))
                put(ops[0], C(v & ((1 << w) - 1)))
        elif m == "xadd":
            a, b = val(ops[0]), val(ops[1])
            r = binop("add", a, b)
            setfl(mc, "add", a, b, r, (ops[0].size or 4) * 8)
            put(ops[1], a)
            put(ops[0], r)
        elif m.startswith("cmov") and m[4:] in CC:
            t = cond_true(mc.fl, m[4:])
            if t is None:
                put(ops[0], sym(m))
            elif t:
                put(ops[0], val(ops[1]))
        elif m == "lahf":
            if mc.fl is None:
                mc.write_reg("ah", sym("lahf"))
            else:
                f = mc.fl
                mc.write_reg("ah", C((f["SF"] << 7) | (f["ZF"] << 6) |
                                     (f["PF"] << 2) | 2 | f["CF"]))
        elif m == "sahf":
            v = mc.read_reg("ah")
            if is_c(v):
                mc.fl = {"SF": bool(v[1] & 0x80), "ZF": bool(v[1] & 0x40),
                         "PF": bool(v[1] & 0x04), "CF": bool(v[1] & 0x01),
                         "OF": mc.fl["OF"] if mc.fl else False}
            else:
                mc.fl = None
        elif m in ("cld", "std"):
            mc.df = 0 if m == "cld" else 1
        elif m in ("rep movsb", "movsb", "rep movsd", "movsd"):
            wide = m.endswith("d")
            cnt = mc.r["ecx"] if m.startswith("rep") else C(1)
            si, di = mc.r["esi"], mc.r["edi"]
            step = 4 if wide else 1
            if is_c(cnt) and is_c(si) and is_c(di) and not mc.df                     and 0 < cnt[1] * step <= 0x2000:
                n = cnt[1] * step
                for i in range(n):
                    b = mc.load(C((si[1] + i) & M32), 1)
                    if not is_c(b):
                        mc.aliased = True
                        break
                    mc.bmem[(di[1] + i) & M32] = b[1] & 0xFF
                else:
                    mc.r["esi"] = C(si[1] + n)
                    mc.r["edi"] = C(di[1] + n)
                    if m.startswith("rep"):
                        mc.r["ecx"] = C(0)
            else:
                mc.aliased = True           # copied who knows where
                for reg in ("esi", "edi"):
                    mc.r[reg] = sym(reg)
                if m.startswith("rep"):
                    mc.r["ecx"] = C(0)
        elif m in ("iretd", "iret", "sysenter", "int", "int3", "ud2"):
            return mc, trace, ("cannot model %s" % m, None)
        elif m in ("not", "bswap"):
            put(ops[0], unop(m, val(ops[0])))   # neither touches flags
        elif m == "neg":
            b = val(ops[0])
            r = unop("neg", b)
            setfl(mc, "neg", C(0), b, r, (ops[0].size or 4) * 8)
            put(ops[0], r)
        elif m == "inc":
            a = val(ops[0])
            r = binop("add", a, C(1))
            setfl(mc, "inc", a, C(1), r, (ops[0].size or 4) * 8)
            put(ops[0], r)
        elif m == "dec":
            a = val(ops[0])
            r = binop("sub", a, C(1))
            setfl(mc, "dec", a, C(1), r, (ops[0].size or 4) * 8)
            put(ops[0], r)
        elif m in ("movzx", "movsx"):
            put(ops[0], val(ops[1]))
        elif m == "xchg":
            a, b = val(ops[0]), val(ops[1])
            put(ops[0], b)
            put(ops[1], a)
        elif m == "push":
            mc.r["esp"] = binop("sub", mc.r["esp"], C(4))
            mc.store(mc.r["esp"], 4, val(ops[0]))
        elif m == "pushfd":
            # When the flags are known this is ordinary data: pushing the real
            # EFLAGS lets `pushfd; pop eax; and eax,K` fold like any other
            # arithmetic instead of poisoning the chain with an opaque value.
            if mc.fl is not None:
                f = mc.fl
                e = C(0x202 | f["CF"] | (f["PF"] << 2) | (f["ZF"] << 6) |
                      (f["SF"] << 7) | (mc.df << 10) | (f["OF"] << 11))
            else:
                e = sym("flags")
            mc.r["esp"] = binop("sub", mc.r["esp"], C(4))
            mc.store(mc.r["esp"], 4, e)
        elif m == "pop":
            put(ops[0], mc.load(mc.r["esp"], 4))
            mc.r["esp"] = binop("add", mc.r["esp"], C(4))
        elif m == "popfd":
            e = mc.load(mc.r["esp"], 4)
            if is_c(e):
                v = e[1]
                mc.fl = {"CF": bool(v & 1), "PF": bool(v & 4),
                         "ZF": bool(v & 0x40), "SF": bool(v & 0x80),
                         "OF": bool(v & 0x800)}
                mc.df = 1 if v & 0x400 else 0
            else:
                mc.fl = mc.flmap.get(e[1]) if e[0] == "sym" else None
            mc.r["esp"] = binop("add", mc.r["esp"], C(4))
        elif m in ("cmp", "test"):
            a, b = val(ops[0]), val(ops[1])
            r = binop("sub" if m == "cmp" else "and", a, b)
            setfl(mc, "sub" if m == "cmp" else "and", a, b, r,
                  (ops[0].size or 4) * 8)
        elif m in ("clc", "stc", "cmc"):
            if mc.fl is not None:
                mc.fl = dict(mc.fl)
                mc.fl["CF"] = (m == "stc") if m != "cmc" else not mc.fl["CF"]
        elif m == "nop":
            pass
        elif m == "jmp":
            if ops and ops[0].type == X86_OP_IMM:
                rva = ops[0].imm - BASE
                continue
            return mc, trace, ("indirect jmp", mc.read_reg(
                ins.reg_name(ops[0].reg)) if ops[0].type == X86_OP_REG
                else sym("target"))
        elif m in COND:
            t = cond_true(mc.fl, m[1:]) if m.startswith("j") else None
            if t is None:
                mc.unsure += 1              # unknown predicate: fall through
            elif t and ops and ops[0].type == X86_OP_IMM:
                rva = ops[0].imm - BASE
                continue
        elif m in ("ret", "retf"):
            # `ret` pops, and `ret imm` pops further.  Returning without
            # applying that leaves esp low by 4 (+imm) for the next hop, which
            # only shows up once a chain has more than one hop -- so it hid
            # behind a median hop count of 1.
            tgt = mc.load(mc.r["esp"], 4)
            pop = 8 if m == "retf" else 4
            if ops and ops[0].type == X86_OP_IMM:
                pop += ops[0].imm
            mc.r["esp"] = binop("add", mc.r["esp"], C(pop))
            return mc, trace, ("ret", tgt)
        elif m == "call":
            if ops and ops[0].type == X86_OP_IMM:
                if follow:
                    mc.r["esp"] = binop("sub", mc.r["esp"], C(4))
                    mc.store(mc.r["esp"], 4, C(ins.address + ins.size))
                    rva = ops[0].imm - BASE
                else:
                    rva += ins.size
                continue
            return mc, trace, ("indirect call", val(ops[0]))
        elif m.startswith("set") and m[3:] in CC:
            t = cond_true(mc.fl, m[3:])
            put(ops[0], C(1 if t else 0) if t is not None else sym(m))
        else:
            # An instruction we do not model may well have written flags, so
            # keeping the previous ones would let a later jcc be decided from
            # a value that never existed.
            mc.fl = None
            mc.unmodelled[m] = mc.unmodelled.get(m, 0) + 1
            for op in ops[:1]:
                if op.type == X86_OP_REG:
                    mc.write_reg(ins.reg_name(op.reg), sym(m))
        rva += ins.size
    return mc, trace, ("budget", None)


def main():
    img = (Path(sys.argv[1]) / "DNF-memory-rva.bin").read_bytes()
    start = int(sys.argv[2], 16)
    ret = int(sys.argv[3], 16) if len(sys.argv) > 3 else None
    mc, trace, (why, tgt) = run(img, start, ret)
    print("chain 0x%08X  (%d instructions)  exit: %s" % (start, len(trace), why))
    if tgt is not None:
        print("   target/return = %s" % show(tgt))
    print()
    for k in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp"):
        e = mc.r[k]
        if e[0] == "sym" and e[1].startswith(k + "0"):
            continue
        print("   %-4s = %s" % (k, show(e)))


if __name__ == "__main__":
    main()
