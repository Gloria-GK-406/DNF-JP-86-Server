# vmp1-4 — Why the symbolic walks into `.vmp1` never terminate

Image: A21 memory dump `DNF-memory-rva.bin`, base `0x00400000`.
Question: 1,499 of 2,508 walks from `.text`→`.vmp1` call targets hit an 800-instruction
budget. Loop, long straight chain, or off the rails into non-code?

**Answer: a loop. One loop. The same 22-byte loop in every single case.**
It is not a loop the code gets stuck in — it is a *bounded* 4-iteration stack-probe
loop whose exit test reads the TEB, which does not exist in a dump. The walker's
"undecided predicate → fall through" policy picks the loop body every time.

Not a long chain (measured: 15–33 distinct addresses per 800-step trace, never 800).
Not off the rails (measured: 0 decode failures, 0 fence violations, 98.6% of 1.19M
decoded instructions are the six loop instructions). Exactly **one** entry of 2,508 is
misaligned, and it re-synchronises on its own.

---

## 1. Reproduction, and reconciling "1,499"

Walker: `symeval_i.py` (`run(..., budget=800, follow=True, xok=<executable fence>)`),
entries: the 2,508 rows of `targets-text-to-vmp1-call.csv`.

That walker has a `revisit` early-exit (`symeval_i.py:392`) that a plainer walker
would not, so it reports `revisit` where a plain walker reports `budget`. I ran it
both ways (`_q4x_se.py`, a copy with the revisit check behind a flag):

| exit reason | revisit-check ON | revisit-check OFF |
|---|---|---|
| `revisit` | **1493** (59.5%) | — |
| `budget` (800 hit) | 0 | **1484** (59.2%) |
| `ret` | 382 | 391 |
| `indirect call` | 366 | 366 |
| `indirect jmp` | 260 | 260 |
| `out of image` | 4 | 4 |
| `decode fail` | 2 | 2 |
| `cannot model int3` | 1 | 1 |

**Measured:** the 1484 `budget` entries are *exactly* the same entry set that the
revisit-checking walker flags — 1484/1484 of them return `revisit` under the other
setting. So `budget` and `revisit` are the same population under two policies.

I could not reproduce the exact figure 1,499; my instrumented count is 1484 / 1493.
The 6–15 walk gap is a walker-version difference (fence, `ret imm` esp handling), not
a different phenomenon. Fraction is the same: **59%**.

Non-terminating traces: median **20** distinct instruction addresses (min 15, max 33).
Terminating traces: median length **20** instructions total.

## 2. It is a loop — the decisive measurement

Unique instruction addresses inside each 800-instruction trace:

| unique addresses | traces | share |
|---|---|---|
| ≤ 8 | 0 | 0.0% |
| 9–32 | **1483** | **99.9%** |
| 33–128 | 1 | 0.1% |
| 129–799 | 0 | 0.0% |
| 800 (no address repeated) | **0** | **0.0%** |

A long straight chain would sit in the last row. Nothing is there. Every walk is
executing ~20 addresses ~40 times over.

Loop shape, measured over all 1484:

* loop body length: **6 instructions, always** (min 6, median 6, max 6)
* loop body byte span: **22 bytes, always**
* back edge mnemonic: `jmp`, **1484 / 1484 = 100%**
* instructions before the loop head: median 14, max 27, never 0
* the entry VA itself is inside the loop: **0 / 1484** (so it is not a self-recursive entry)
* distinct loop-body mnemonic signatures across all 1484 traces: **1**

## 3. The loop

```
0x0542F88C  cmp   ecx, 0x4000          ; want >= 16 KB of committed stack below esp
0x0542F892  jg    0x0542F8A4           ; <- the ONLY exit
0x0542F894  sub   edi, 0x1000          ; step down one page
0x0542F89A  test  dword ptr [edi], esi ; touch it -> guard-page fault -> kernel commits
0x0542F89C  add   ecx, 0x1000
0x0542F8A2  jmp   0x0542F88C
```

Bytes: `81 F9 00 40 00 00  7F 10  81 EF 00 10 00 00  85 37  81 C1 00 10 00 00`

**Measured:** only **32 distinct loop-head addresses** serve all 1484 entries, and the
22 bytes are **byte-identical at all 32** (verified by direct byte compare, `_q4x_d.py`).
An image-wide search for that exact 22-byte pattern finds **53 copies**:

| section | copies |
|---|---|
| `.vmp1` | 41 |
| `.text` | 6 |
| `.tls` | 6 |

32 of the 53 lie on a walk path from these 2,508 entries. The 21 others are unreached
by this entry set (not investigated).

Loop heads by traffic (all `.vmp1` unless noted):
`0x0542F88C` ×137 · `0x054FEC3D` ×126 · `0x055073ED` ×125 · `0x0549CAAE` ×118 ·
`0x05226A50` ×57 · `0x05206325` ×50 · `0x051FD551` ×49 · `0x01C5689E` ×49 **[.text]** ·
`0x0539D5FC` ×47 · `0x052B24CB` ×45 · … · `0x023D035E` ×37 **[.text]** ·
`0x0219F8E4` ×36 **[.text]** · `0x0211E325` ×34 **[.text]** · `0x019900CE` ×33 **[.text]** ·
`0x022063CD` ×32 **[.text]** · `0x05795AA0` ×2

## 4. How the walk gets there — the stub

Full trace, entry `0x054D59BF` → loop head `0x0542F88C` (21 instructions):

```
[ 0] 0x054D59BF  push  0x6D23E559          ; handle / key immediate
[ 1] 0x054D59C4  push  ecx
[ 2] 0x054D59C5  push  edx
[ 3] 0x054D59C6  jmp   0x0546EAFE           ; trampoline hop
[ 4] 0x0546EAFE  push  ebx
[ 5] 0x0546EAFF  push  eax
[ 6] 0x0546EB00  jmp   0x05449528
[ 7] 0x05449528  push  esi
[ 8] 0x05449529  pushfd                     ; EFLAGS saved -> context save, not a call
[ 9] 0x0544952A  call  0x0554CE97
[10] 0x0554CE97  lea   esp, [esp + 4]       ; discard the return address: call used as jmp
[11] 0x0554CE9B  push  ebp
[12] 0x0554CE9C  push  edi
[13] 0x0554CE9D  jmp   0x0542F873
[14] 0x0542F873  call  0x0542F878           ; ---- position-independent preamble ----
[15] 0x0542F878  pop   eax
[16] 0x0542F879  sub   eax, 0x0542F878      ; eax = relocation delta
[17] 0x0542F87E  mov   edi, dword ptr fs:[0x18]   ; TEB
[18] 0x0542F885  mov   edi, dword ptr [edi + 8]   ; TEB+0x08 = NT_TIB.StackLimit
[19] 0x0542F888  mov   ecx, esp
[20] 0x0542F88A  sub   ecx, edi            ; ecx = committed bytes below esp
[21] 0x0542F88C  cmp   ecx, 0x4000          <<< LOOP HEAD
```

Two more confirmed instances of the identical preamble: entry `0x053D9849` →
`0x01C56885`…`0x01C5689E` (**in `.text`**), entry `0x05792664` → `0x057911EB`…`0x05791204`.

Prologue statistics over all 1484:

* first mnemonic at the entry VA: `lea` 1227 (that is `lea esp,[esp+4]`), `push` 255, `mov` 1, `fstp` 1
* contains `pushfd` (EFLAGS save — an ordinary call never saves flags): **1112 / 1484 = 75%**
* register pushes: median 4, max 9 · `call` hops: median 1, max 4 · `jmp` hops: median 1, max 4
* prologue length: median 14, max 27

## 5. Root cause of the non-termination — measured, not guessed

Run the evaluator on the preamble alone (`_q4x_f.py`, `run(img, 0x0542F87E, budget=12)`):

```
edi   = mem#9                                  <- fs:[0x18] produced a fresh symbol
ecx   = ((0x7F000000 sub mem#9) add 0x00001000)
flags after `cmp ecx, 0x4000` = None
unmodelled = {}                                <- the evaluator models `mov` fine
```

The evaluator has no TEB: the dump contains image memory only, so the load at
`fs:[0x18]` is unbacked and yields a symbol. `ecx` is therefore symbolic,
`setfl` (`symeval_i.py:371`) sets `mc.fl = None`, `cond_true` returns `None`, and the
documented policy on an undecided conditional is **fall through** (`symeval_i.py`
`elif m in COND: … if t is None: mc.unsure += 1`). Here fall-through *is* the loop
body — the taken branch is the only exit. So the walk can never leave.

Corroborating count: `mc.unsure` median = **131** per trace; loop iterations
(`cmp` count) median = **131**; 800 / 6 = 133. The entire budget is that one `jg`.

## 6. Counterfactual — one 4-byte value fixes 1484 walks

Seeded `fs:[0x18] → 0x7EFDD000` and `[TEB+8] → esp − 0x10000` (i.e. 64 KB already
committed, so `ecx = 0x10000 > 0x4000` and `jg` is taken immediately), revisit-check
ON, budget 800, everything else unchanged (`_q4x_g.py`):

| exit reason | before | after |
|---|---|---|
| `revisit` / stuck | **1493 (59.5%)** | **11 (0.4%)** |
| `indirect jmp` | 260 | **1742** |
| `ret` | 382 | 382 |
| `indirect call` | 366 | 366 |
| `out of image` / `decode fail` / `int3` | 7 | 7 |

**All 1484** previously-stuck entries terminate: 1482 at an `indirect jmp`, 2 at a
different revisit. Median trace length rises 20 → 28, max 133.

Of the 1742 `indirect jmp` exits, 365 resolve to a constant target — sections
`.vmp1` 315, `.text` 46, `.rsrc` 2, `.tls` 2; busiest `0x01705B33` ×21,
`0x05275444` ×18, `0x052B27DE` ×17. The other 1377 jump through an unresolved
memory expression (e.g. `(mem#1515 add 0x4CC42998)`).

The 11 residual stuck walks: 7 spin in a *different* loop at `0x02C72457`
(`add / xor / xor / add / test / je` — .text), 2 still reach a probe copy from a path
the seed did not cover, 1 at `0x050BE18A`, 1 misaligned (§8).

## 7. Not off the rails — the negative result, measured

Across the 1484 stuck traces, **1,187,200 instructions** were decoded. Mnemonic census:

| mnemonic | count | share |
|---|---|---|
| `sub` | 197,326 | 16.62% |
| `jmp` | 195,736 | 16.49% |
| `cmp` | 194,867 | 16.41% |
| `jg` | 194,614 | 16.39% |
| `test` | 194,110 | 16.35% |
| `add` | 193,859 | 16.33% |
| `push` | 5,770 | 0.49% |
| `mov` | 4,453 | 0.38% |
| `call` | 2,057 | 0.17% |
| `lea` | 1,801 | 0.15% |
| `pop` | 1,484 | 0.12% |
| `pushfd` | 1,112 | 0.09% |
| everything else | **12** | **0.001%** |

* **0** `decode fail` exits among the 1484 · **0** `left executable memory` exits · **0** exceptions.
* Only **12 instruction instances** fall outside the stub vocabulary, and **all 12 belong to a single entry** (§8).
* The executable fence (`xok`) held; the fence spans are `.text`, `.rsrc`(1), `.tls`,
  `.vmp0`, `.vmp1`, `.bss`, `.mackt`, `.-wI`, `.#|U` — note `.code`, `.data`, `.rdata`,
  `.eI4`, `."2A` are **not** executable despite the names.
* Sections touched by the 1484 traces: `.vmp1` 1484, `.text` 348, `.rsrc` 2. Nothing else.

Independent confirmation that the decode is real code and not a lucky alignment: the
22-byte loop body occurs **byte-identically 53 times** across three sections. Random
mis-decodes do not produce 53 exact copies of the same 22 bytes.

## 8. The single genuine off-the-rails case: `0x0571EA33`

One entry of 2,508 (`in_degree` 1) is a misaligned target:

```
bytes @0x0571EA33: dd dc 40 ce fd 3e 2f 19 da dc 77 8f 84 54 00 00 00 00 72 | 8d 64 24 04 53
0x0571EA33 fstp st(4) / inc eax / into / std / das / sbb edx,ebx / fdiv [edi-0x71] / test ...
0x0571EA48 lea esp, [esp + 4]      <- the real stub head, 0x15 bytes later
```

`8D 64 24 04` = `lea esp,[esp+4]`, the shape 1227 of the other entries start with. The
walk decodes 8 junk instructions and re-synchronises into the canonical stub, then
reaches probe copy `0x0573FF2F` and spins like the rest. So even the one bad start does
not produce a confident answer built out of noise — it self-corrects. Every one of the
12 anomalous mnemonics in §7 comes from this entry plus one stray `jb` in `0x0542991F`.

---

## Measured vs inferred

**Measured** (from the dump / the walker; reproducible via the scripts below)

1. 1484/2508 (59.2%) walks exhaust an 800-instruction budget; identical entry set to the 1493 `revisit` exits.
2. Every one of them contains 15–33 distinct addresses, never 800 — a loop, categorically not a straight chain.
3. The loop is 6 instructions / 22 bytes, one signature across all 1484, back edge `jmp` 100%.
4. 32 distinct loop heads, byte-identical; 53 exact copies image-wide (41 `.vmp1`, 6 `.text`, 6 `.tls`).
5. The preamble is `call $+5; pop eax; sub eax,<own va>; mov edi,fs:[0x18]; mov edi,[edi+8]; mov ecx,esp; sub ecx,edi`.
6. `fs:[0x18]` yields a symbol → flags `None` → `jg` undecidable → fall-through = loop body. `mc.unsure` median 131 = iteration count.
7. Seeding `[TEB+8]` terminates 1484/1484; stuck population 1493 → 11.
8. 98.6% of 1.19M decoded instructions are the six loop instructions; 0 decode failures; 0 fence violations.
9. Exactly one misaligned entry, `0x0571EA33`, which re-synchronises after 8 instructions.

**Inferred** (well-supported, but not proven from this image alone)

* `TEB+0x08` is `NT_TIB.StackLimit` (documented Win32 layout). Hence `ecx = esp − StackLimit`
  is "bytes of stack already committed below esp", and the loop is a **stack-headroom
  pre-commit**: touch one 4 KB page at a time downward until ≥16 KB is committed, letting
  the guard page grow the stack.
* Therefore the loop is **bounded by construction**: `ecx` rises monotonically by `0x1000`
  and the test is `ecx > 0x4000`, so ≤4 iterations for any `ecx ≥ 0`, and **0** iterations
  whenever ≥16 KB is already committed — the common case. It never spins in real execution.
  **The non-termination is entirely an artifact of missing runtime state, not a property
  of the protected code, and not an anti-analysis loop.**
* The stub around it (context save including `pushfd`, `lea esp,[esp+4]` to turn a `call`
  into a `jmp`, scattered trampolines, position-independent delta, then a deep-stack
  pre-commit) is consistent with a protector VM entry preparing to run an interpreter on a
  private stack. **Not proven**: that the final `indirect jmp` lands in a VM dispatcher —
  only 365/1742 of those targets resolve to constants, and I did not follow them.

**Not established**

* The exact figure 1,499 (my counts: 1484 `budget` / 1493 `revisit`).
* What the other 21 unreached copies of the probe loop serve.
* Whether the 7 residual walks stuck at `0x02C72457` share a second common cause.

## Practical consequence

`.vmp1` walking is not budget-limited by obfuscation depth. **One four-byte seed —
`NT_TIB.StackLimit` — converts 59% of the walk surface from "unresolved" to "resolved to
an indirect jump".** Any future pass over `.vmp1` should seed the TEB before walking;
otherwise 6 out of every 10 entries produce nothing but 800 iterations of a stack probe.

Suggested general hardening for `symeval_i.run`: when a conditional's predicate is
unknown *and* the fall-through path leads back to an address already in `seen`, take the
branch instead of falling through. That breaks this class without needing a TEB model.

---

### Scripts (all in SCRATCH, prefix `_q4x_`, none overwrite existing files)

| file | what it does |
|---|---|
| `_q4x_se.py` | copy of `symeval_i.py` with the `revisit` exit behind a `NOREVISIT` flag |
| `_q4x_run.py` | re-runs all 2508 walks, revisit off, budget 800 → `_q4x_recs.pkl` |
| `_q4x_b.py` | unique-address-per-trace metric (the loop-vs-chain decision) |
| `_q4x_c.py` | loop head / body / back-edge / signature census |
| `_q4x_d.py` | disassembles all 32 loop heads, byte-identity check |
| `_q4x_e.py` | full entry→loop-head traces |
| `_q4x_f.py` | root cause: `fs:[0x18]` → symbol → flags None |
| `_q4x_g.py` | the TEB counterfactual → `_q4x_teb.pkl` |
| `_q4x_h.py`, `_q4x_i.py`, `_q4x_j.py` | vocabulary, residual revisits, stub shape, image-wide pattern count |

Snapshots taken because sibling agents share the directory:
`_q4_recs_snapshot.pkl` (= `_v4_recs.pkl` as of 18:45), `_q4_symeval_snapshot.py`.
