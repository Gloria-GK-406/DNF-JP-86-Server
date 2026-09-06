# vmp1-4 — why 1,499 walks into `.vmp1` "hit the 800-instruction budget"

Image: A21 memory dump `dnf-dump-20260822-232754-263-26888\DNF-memory-rva.bin`, base
`0x00400000`. `.vmp1` = `0x051BF000 .. 0x0598B000` (`0x7CC000` = 8,175,616 bytes,
characteristics `0xE0000020`, executable).

Walker: `ClientHack\tools\symeval.py`, `run(img, va, budget=800, follow=True, xok=<executable
section fence>)`. Entries: the 2,508 rows of `targets-text-to-vmp1-call.csv`.
This is the exact configuration `vmp1_census.py` used.

---

## Answer

**A loop — 1,493 of 1,499 (99.6%). And almost always the *same* loop: a 6-instruction
stack-probe at the head of every VMProtect VM entry, 1,484 of 1,499 (99.0%).**

Not a long straight chain: **zero** of the 1,499 walks executed 800 instructions. The
longest of the 1,499 is 85 instructions; the longest of all 2,508 is 133. Raising the
budget tenfold changes nothing.

Off the rails into non-code: **6 of 1,499 (0.4%)** — four walks whose branch target left
the image, two that hit undecodable bytes. Zero walks violated the executable fence.

The `"budget"` label is the reason this looked like a budget problem. `symeval.run`
reaches `return mc, trace, ("budget", None)` (symeval.py:644) two ways: by exhausting
the budget, and by falling out of the `while` through one of three `break`s — a revisited
address (line 393), an address outside the image (line 393), or an undecodable byte
(line 402). Every one of the 1,499 came through a `break`. **None came through the budget.**

---

## MEASURED

### 1. Reproduction, verbatim tool

`symeval.py` unmodified, `budget=800`:

```
{'budget': 1499, 'ret': 382, 'indirect call': 366, 'indirect jmp': 260,
 'cannot model int3': 1}
longest trace of any of the 2508 walks: 133
'budget' walks: n=1499  min=5  med=20  max=85  reaching 800 instructions: 0
```

Same run at `budget=8000` — byte-identical output, including `max=85`. The budget is
never the binding constraint; the `seen` set (symeval.py:389, 400) makes every walk a
simple path, so a trace can never be longer than its distinct-address count.

### 2. Splitting the `break`s

`scratchpad\symeval_i.py` (left by an earlier run) is `symeval.py` with those three
`break`s replaced by distinct returns; I ran it verbatim on the same 2,508 entries:

```
{'revisit': 1493, 'ret': 382, 'indirect call': 366, 'indirect jmp': 260,
 'out of image': 4, 'decode fail': 2, 'cannot model int3': 1}
```

1493 + 4 + 2 = **1499**. The bucket is 99.6% back edges.
`'left executable memory'` count: **0**.

### 3. The back edge is a real loop, and there are only three of them

Loop body = instructions from the back-edge target to the back edge:

| body | walks | back-edge target | what it is |
|---|---|---|---|
| 6 | **1,484** | 32 distinct addresses | VMProtect VM-entry stack probe |
| 8 | 8 | `0x02C72450` (`.text`) | MSVC `strlen` inner loop |
| 5 | 1 | `0x050BE18C` (`.tls`) | `__chkstk` / `_alloca_probe` |

### 4. The dominant loop, disassembled

Entry `0x054D59BF`, trace indices 0–26 (`symeval.py`, `follow=True`):

```
  0 054D59BF  push  0x6d23e559          ; VMProtect entry: push encrypted bytecode ptr
  1 054D59C4  push  ecx
  2 054D59C5  push  edx
  3 054D59C6  jmp   0x546eafe
  4 0546EAFE  push  ebx
  5 0546EAFF  push  eax
  6 0546EB00  jmp   0x5449528
  7 05449528  push  esi
  8 05449529  pushfd                    ; 8 dwords of VM context now on the stack
  9 0544952A  call  0x554ce97
 10 0554CE97  lea   esp, [esp + 4]
 11 0554CE9B  push  ebp
 12 0554CE9C  push  edi
 13 0554CE9D  jmp   0x542f873
 14 0542F873  call  0x542f878            ; get-eip idiom
 15 0542F878  pop   eax
 16 0542F879  sub   eax, 0x542f878       ; eax = image-base delta
 17 0542F87E  mov   edi, dword ptr fs:[0x18]   ; <-- TEB. NOT IN THE DUMP.
 18 0542F885  mov   edi, dword ptr [edi + 8]   ; NT_TIB.StackLimit
 19 0542F888  mov   ecx, esp
 20 0542F88A  sub   ecx, edi                   ; ecx = committed stack headroom
 21 0542F88C  cmp   ecx, 0x4000                ; <== back-edge target
 22 0542F892  jg    0x542f8a4                  ; <== the undecidable exit test
 23 0542F894  sub   edi, 0x1000
 24 0542F89A  test  dword ptr [edi], esi       ; touch the guard page -> commit it
 25 0542F89C  add   ecx, 0x1000
 26 0542F8A2  jmp   0x542f88c                  ; <== back edge; walk stops here
```

The mechanism, instruction by instruction: `addr_of` (symeval.py:361) ignores the segment
prefix, so `fs:[0x18]` is computed as the literal address `0x18`. `0x18 < BASE`, so
`Mach.load` (symeval.py:320) cannot read it out of the image and returns `sym("mem")`
(line 336). `edi` is symbolic → `sub ecx, edi` is symbolic → `setfl` sets `mc.fl = None`
(symeval.py:375) → `cond_true` returns `None` for the `jg` → symeval's policy is
**fall through and count it** (`mc.unsure += 1`) → the fall-through *is* the loop body →
`jmp` back to `0x0542F88C` → `rva in seen` → break → `"budget"`.

Corroboration that this single predicate is the whole story: of the 1,484 probe walks,
**1,482 recorded exactly one undecided predicate** for the entire walk (`unsure` histogram
over the 1,493 revisits: `{0:1, 1:1482, 2:2, 4:7, 5:1}`).

### 5. The 1,484 walks hit 32 copies of one byte-identical loop

Back-edge addresses among the 1,484: **32 distinct**, 26 in `.vmp1` and 6 in `.text`
(no copy is reached by only one entry; top four are `0x0542F88C` ×137, `0x054FEC3D` ×126,
`0x055073ED` ×125, `0x0549CAAE` ×118).

The 22 bytes at each of the 32 back-edge addresses are **identical**:

```
81 F9 00 40 00 00   cmp  ecx, 0x4000
7F 10               jg   +0x10
81 EF 00 10 00 00   sub  edi, 0x1000
85 37               test dword ptr [edi], esi
81 C1 00 10 00 00   add  ecx, 0x1000
```

Image-wide byte scan for that exact 22-byte string: **53 copies** —
`.vmp1` 41, `.text` 6, `.tls` 6. Byte scan for `64 8B 3D 18 00 00 00`
(`mov edi, fs:[0x18]`): **53 copies**, in the same 41/6/6 split. One-to-one. The 2,508
`.text→.vmp1` call targets reach 32 of those 53.

Base-rate note: 6 of the 32 copies live in `.text`, and 221 of the 1,484 walks take their
back edge in `.text` (e.g. `0x01C5689E`, 49 walks). Section membership is not the
discriminator here — the byte pattern is.

### 6. Modelling `fs:` alone clears 1,484 of the 1,493

Using `scratchpad\_c6_se_teb.py` (an earlier run's variant that adds a synthetic TEB:
`fs` base `0x7EFDD000`, `NT_TIB.StackLimit = ESP0 - 0x100000`), same 2,508 entries,
same `budget=800`:

```
{'indirect jmp': 1744, 'ret': 382, 'indirect call': 366, 'revisit': 9,
 'out of image': 4, 'decode fail': 2, 'cannot model int3': 1}
```

The 1,484 probe walks all become `indirect jmp`. Median trace 20 → 29, max 42 (still
nowhere near 800). Continuing entry `0x054D59BF` past the probe:

```
 23 0542F8A4  mov  dword ptr [edi], eax        ; stash the delta at StackLimit
 24 0542F8A6  mov  dword ptr [edi + 4], eax
 25 0542F8A9  mov  esi, dword ptr [esp + 0x20] ; read back the pushed 0x6d23e559
 26 0542F8AD  rol  esi, 9
 27 0542F8B0  add  esi, 0xbd81ace6
 28 0542F8B6  add  esi, eax                    ; decrypted VM bytecode pointer
 29 0542F8B8  xor  ebp, ebp
 30 0542F8BA  xor  ebx, ebx
 31 0542F8BC  mov  ebx, dword ptr [esi]
 32 0542F8BE  add  esi, 4
 33 0542F8C1  add  ebx, 0x4cc42998
 34 0542F8C7  add  ebx, dword ptr [edi]
 35 0542F8C9  jmp  ebx                         ; -> 0x0554A21E, resolved concretely
```

All 1,484 end at one of 32 `jmp reg` sites (`back_edge + 0x3D`). 257 of the 1,484 resolve
the register to a concrete address (32 distinct targets, 29 in `.vmp1`, 3 in `.text`);
the other 1,227 leave it symbolic because the `[esp+0x20]` slot was lost to aliasing.

### 7. The 9 residual back edges are two other real loops

**8 walks** — `0x05429B5F`, `0x054605DD`, `0x0541D057`, `0x0548C3BB`, `0x054F0A3E`,
`0x054C4C60`, `0x054B5814`, `0x055B82BA`. Each starts `lea esp,[esp+4]; call <.text>`
and runs straight into ordinary C++ (`0x01BF01E0`, in-degree 2033 → `0x018DFAE0` →
`0x02C4D3E1` → `0x02C616CD` → `0x02C6166F` → `0x02C72420`). `0x02C72420` is a real
function entry in `functions.csv` (span 139, in-degree 38) and its loop at `0x02C72450`
is MSVC `strlen`:

```
02C72450  mov  eax, dword ptr [ecx]
02C72452  mov  edx, 0x7efefeff
02C72457  add  edx, eax
02C72459  xor  eax, 0xffffffff
02C7245C  xor  eax, edx
02C7245E  add  ecx, 4
02C72461  test eax, 0x81010100
02C72466  je   0x2c72450
```

It loops because the string bytes at `[ecx]` are runtime data absent from the model.
These 8 "vmp1 entries" are trampolines into `.text`, not virtualized code.

**1 walk** — `0x0579B07A` → `0x050BE180` in `.tls` (executable, `0xE00000E0`):

```
050BE180  push ecx
050BE181  cmp  eax, 0x1000
050BE186  lea  ecx, [esp + 8]
050BE18A  jb   0x50be1a0
050BE18C  sub  ecx, 0x1000      ; <== back-edge target
050BE192  sub  eax, 0x1000
050BE197  test dword ptr [ecx], eax
050BE199  cmp  eax, 0x1000
050BE19E  jae  0x50be18c
```

`__chkstk`. This is the one revisit walk with `unsure == 0` — every predicate was decided
(`eax` folds to `0x2188` from `mov eax,0x2404926b; lea eax,[eax-0x240470e3]`), so the loop
is fully concrete and would terminate in two more iterations. symeval stopped anyway,
because the `seen` set forbids re-visiting an address at all. **symeval has no loop
support; a decidable loop and an undecidable one both die at the first back edge.**

### 8. The 6 that did go off the rails

| entry | ending | trace | wild target |
|---|---|---|---|
| `0x0531EAF3` | out of image | 35 | `call 0xCB325850` |
| `0x0587E919` | out of image | 6 | `call 0xF71C636F` |
| `0x0551AE67` | out of image | 28 | `call 0x8B521BAF` |
| `0x05790FE9` | out of image | 11 | `jmp 0xE4830E6B` |
| `0x0571968A` | undecodable | 12 | stops at `0x057196B5` |
| `0x0583EA69` | undecodable | 5 | stops at `0x0583EA71` |

Discriminator, with its base rate. Counting traces containing at least one opcode a
compiler and VMProtect never emit (`les/lds/salc/aas/das/arpl/bound/enter/outsb/pop es/
cmpsb/stosb/hlt`):

```
ret                  7 of  382  (1.8%)
revisit              2 of 1493  (0.1%)
indirect jmp         7 of  260  (2.7%)
indirect call       18 of  366  (4.9%)
out of image         3 of    4  (75.0%)
undecodable          2 of    2  (100.0%)
```

Five of the six. Example, `0x0551AE67`: `sbb al,0xe1 / cwde / movsb / enter 0xb77,-0x1f /
arpl / loope / outsb / das / pop es`. The 32 bytes there are
`1a 1d 8e 26 9d 7a ab 1c e1 98 45 a4 3b a6 a1 c9 …` — high entropy, no instruction
structure.

`in_degree` does **not** discriminate: five of the six have `in_degree == 1`, but so do
2,327 of the 2,508 entries (93%).

---

## INFERRED

* **The 22-byte loop is VMProtect's VM-entry stack-growth probe.** Inferred from the
  shape — `push imm32` + full register/flags spill, get-eip delta, `NT_TIB.StackLimit`,
  0x4000 headroom target, guard-page touch, then a decrypt-and-`jmp reg` handoff — not
  from a packer signature database. The measured facts (32 byte-identical copies, each
  preceded 1:1 by `mov edi, fs:[0x18]`, each terminating in `jmp reg`) are consistent
  with it and I know of no other construct that produces them.
* **The six off-rails entries are false-positive entry addresses, not real call targets.**
  `targets-text-to-vmp1-call.csv` was built by a linear byte scan for `0xE8`/`0xE9`
  (`vmp.py`), which mints a phantom target whenever an `E8`/`E9` byte is an operand byte
  inside a longer instruction. High-entropy bytes at the "entry" plus branch targets far
  outside a 122 MB image fit that. I did **not** verify the originating `.text` call
  sites decode as real instructions, so this remains inference.
* **UNKNOWN:** `0x05790FE9` is the odd one out. Its 11 instructions are a coherent
  VMProtect stub (`push ebp / mov ebp,[esp] / … / push edx / push ecx / pushfd`), it has
  `in_degree == 10`, and only its final `jmp rel32` (`E9 68 FE 09 DF` at `0x05790FFE`)
  carries a wild displacement. Whether that displacement is patched at runtime, whether
  the stub is still encrypted in this dump, or whether `0x05790FFE` is simply not an
  instruction boundary — I cannot tell from the dump alone.

---

## Correction to the earlier draft of this document

Its headline — "a loop, one loop, the same 22-byte loop" — **holds**, and its
identification of the stack probe is right. Three of its specifics do not survive
re-measurement with its own tool (`symeval_i.py`, run verbatim, same entries, same
budget):

| earlier claim | measured |
|---|---|
| "0 decode failures" | **2** (`0x0571968A`, `0x0583EA69`) |
| "0 fence violations" | 0 — correct. But **4** walks left the image (`out of image`), which it did not report |
| "exactly one entry of 2,508 is misaligned" | **6** walks show mid-instruction decoding; 5 of 6 carry non-compiler opcodes |
| "the same 22-byte loop in every single case" | 1,484 of 1,493. **9** hit two other loops: `strlen` at `0x02C72450` (8 walks) and `__chkstk` at `0x050BE18C` (1) |
| "15–33 distinct addresses per trace" | 15–**85** for the revisit bucket, 5–85 for the full 1,499 bucket |

Its "bounded 4-iteration" reading of the probe is also not something the dump settles —
the iteration count depends on `esp - NT_TIB.StackLimit` at entry, which is runtime state.

---

## Artifacts

Scripts and pickles under
`C:\Users\10239\AppData\Local\Temp\claude\D--project-DNF\1c50f207-441e-45a6-8f17-0f881c4703e0\scratchpad`:

* `_v4_symeval.py` — `symeval.py` with the three `break`s split into distinct returns
* `_v4_walk.py` / `_v4_res.pkl` — per-entry ending, trace, loop-body length, `unsure`
* `_v4_teb.py` / `_v4_teb_800.pkl` — the same with `fs:` modelled
* `_v4_tgt.py` / `_v4_handlers.pkl` — resolved VM-handoff targets for the 257
* `_v4_orig.py` — reproduction with unmodified `symeval.py` at budget 800 and 8000
* `_v4_trace.py` / `_v4_trace2.py` — single-entry traces (baseline / TEB)
