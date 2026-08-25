# `.vmp1` vs `.-wI` — structural comparison, and the symeval decision

DNF S4A21 client, unpacked-from-memory image, base `0x00400000`.
This file **supersedes an earlier `vmp1-6.md`**; a section at the end lists which
of that version's claims I confirm, refute, or could not reproduce.

---

## Answer in three lines

1. **Same protector, two different subsystems — not one generator at two
   settings, and not unrelated tools.** They share one obfuscation substrate at
   near-identical rates (stack/`pushfd`/`xchg` idiom: 47.6% of `.vmp1` entries,
   41.7% of `.-wI`, 0.7% of `.text`). `.-wI` additionally carries an arithmetic
   mutation layer that is not merely dialed down in `.vmp1` but **absent** — six
   operator families sit at literal zero across 22,150 executed `.vmp1`
   instructions. And their call topologies are categorically different, which is
   a structural choice rather than an intensity knob.
2. **They are not peers.** `.-wI` is the import-thunk arena (1,654 ground-truth
   API wrappers, one stub per call site). `.vmp1` is a code region that
   *consumes* it — 215 of those thunks are called from inside `.vmp1`.
3. **symeval can be pointed at `.vmp1` as-is — it needs no code change — but not
   for the job it does on `.-wI`.** The expensive part of symeval (byte-addressed
   stack memory, `pushfd`/`popfd`, `ret imm` esp bookkeeping) is exactly the
   machinery `.vmp1`'s idiom needs. The arithmetic-folding part mostly idles
   there. `.vmp1`'s blocking problem is that it has **no function inventory at
   all**, not that it is obfuscated.

---

## 0. What each section is (MEASURED — PE header of `DNF.exe`)

| | `.vmp1` | `.-wI` |
|---|---|---|
| VA range | `0x051BF000`–`0x0598B000` | `0x05998000`–`0x05D57D15` |
| size | 8,175,616 (7.80 MB) | 3,931,413 (3.75 MB) |
| characteristics | `E0000020` | `60000020` |
| decoded | **R W X**, CODE | **R X**, CODE (no write) |
| raw file backing | none (`rawptr=0`) | none (`rawptr=0`) |

`.vmp1` is **writable and `.-wI` is not**. Both are materialized at runtime by
the unpacker; only `."2A`, `.#|U` and the second `.rsrc` have file-backed bytes.

Per the standing warning, I took none of this from section names — the same
header shows `.code`/`.data` as `C0000040` (no execute) and `.tls`/`.bss`/`.rsrc`
as `E0000020`/`E00000E0` (execute), confirming the names are camouflage.

**Ground truth stops at `.text`.** `functions.csv` (67,074), `parsers.csv` (459),
`func-source-file.csv` (759) and `literals.csv` (45,688 rows) are **100% `.text`**
— zero rows in either section under study. `.vmp1` genuinely has never been
inventoried.

### Role, from `wrapper-apis-a21.csv` (1,654 rows, independent ground truth)

- All **1,654** wrapper `entry_va` are in `.-wI`. `.-wI` **is** the API-thunk arena.
- Their `call_site_va` distribution: `.text` 749, `.tls` 673, **`.vmp1` 215**,
  `.mackt` 16, `.#|U` 1.
- **1,641 of 1,654 `reloc_site` values point into `.vmp1`.**

So `.vmp1` is upstream of `.-wI`: it contains code that calls the thunks.

> I tested the obvious reading of `reloc_site` — "the dword there is the resolved
> API pointer" — and **refuted it**: only 11/1,642 dwords at or beside the site
> land in the module the row names. I did not determine what `reloc_site` means.
> **Unknown.**

---

## 1. Instruction mix (MEASURED)

**Method note that changes the answer.** Linear sweep of an obfuscated blob
mostly measures *byte statistics*, not the instruction stream, because it decodes
from wrong offsets. The mnemonics it makes `.vmp1` look rich in — `aas`,
`pushal`, `ljmp`, `loop`/`loope`/`loopne`, `jecxz`, `jp`/`jnp`, `cmpsb` — are all
**single-byte opcodes that appear when you decode data** (`0x3F`, `0x60`, `0xEA`,
`0xE0`–`0xE3`, `0x7A`/`0x7B`, `0xA6`). They are decode artifacts, not operators.
I therefore report two aligned methods and lean on the stricter one:

- **aligned walk** — recursive descent from real entry points, following
  fallthrough / direct `jcc` / direct `jmp` / direct `call`, fenced to the
  section. 414,711 instructions in `.vmp1`, 274,286 in `.-wI`.
- **executed traces** — mnemonics collected from `symeval_i.run()` traces, i.e.
  instructions the partial evaluator actually stepped. Guaranteed on a real
  boundary *and* on a reachable path. 22,150 (`.vmp1`), 50,401 (`.-wI`),
  31,609 (`.text` control).

### The `.-wI` operator alphabet, and its absence from `.vmp1`

Raw counts, both methods:

| op | walk `.vmp1` | walk `.-wI` | trace `.vmp1` | trace `.-wI` |
|---|---:|---:|---:|---:|
| `bt`   | **0** | 566 | **0** | 104 |
| `btr`  | **0** | 570 | **0** | 84 |
| `btc`  | **0** | 570 | **0** | 204 |
| `bts`  | 10 | 523 | **0** | 179 |
| `bsf`  | **0** | 259 | **0** | 28 |
| `bsr`  | **0** | 231 | **0** | 20 |
| `shld` | 5 | 302 | **0** | 37 |
| `shrd` | 2 | 287 | **0** | 38 |
| `xadd` | 7 | 285 | **0** | 201 |
| `bswap`| 1 | 4,033 | **0** | 749 |
| `neg`  | 84 | 5,549 | **0** | 686 |
| `not`  | 201 | 8,212 | 18 | 1,266 |
| `clc`  | 361 | 5,654 | 4 | 912 |
| `stc`  | 237 | 5,452 | 18 | 937 |
| `cmc`  | 455 | 5,553 | 7 | 1,021 |
| `setcc`| — | — | 2 | 347 |
| totals | 414,711 | 274,286 | 22,150 | 50,401 |

Six families — `bt`/`bts`/`btr`/`btc`, `bsf`/`bsr`, `shld`/`shrd`, `xadd`,
`bswap`, `neg` — are at **literally zero** in 22,150 executed `.vmp1`
instructions. `bt`-family alone runs at 113 per 10k in `.-wI`; at that rate the
expected count in a 22,150-instruction `.vmp1` sample is ~251. Observed: 0.

That is an **alphabet difference, not an intensity difference**. A "setting"
dials how often an operator fires; it does not remove it from the emitter.

### Where `.vmp1` sits relative to compiled code

Per 10k executed instructions:

| | `.vmp1` | `.-wI` | `.text` ctrl |
|---|---:|---:|---:|
| `push`/`pop` | 2,393.2 | 1,060.7 | 2,279.4 |
| `lea` | 438.8 | 370.8 | 431.8 |
| `call` | 695.7 | 495.0 | 646.0 |
| `ret` | 181.0 | 126.8 | 246.4 |
| `mov` | 1,625.3 | 1,268.6 | 3,572.4 |
| `cmp`/`test` | 534.1 | 780.9 | 690.0 |
| `movzx`/`movsx` | 8.1 | 236.5 | 103.8 |
| `inc`/`dec` | 70.4 | 547.2 | 40.5 |
| **`pushfd`/`popfd`** | **451.5** | 336.5 | 28.8 |
| **`xchg`** | 67.7 | 135.9 | 1.9 |
| `jmp` | 752.1 | 1,080.5 | 74.0 |

On `push`/`pop`, `lea`, `call`, `ret`, `inc`/`dec` **`.vmp1` tracks `.text`, not
`.-wI`**. Its two genuine obfuscation markers are `pushfd`/`popfd` (15.7× `.text`,
and *higher* than `.-wI`) and `xchg` (36× `.text`), plus 10× the `jmp` rate.

Vocabulary breadth: 95% of the stream needs 34 mnemonics in `.vmp1`, 41 in
`.-wI`, but only **18** in `.text`. Both obfuscated blobs are far broader than
compiled code; `.-wI` is the broadest.

> **Confound I did not fully eliminate:** `rcl`/`rcr` is the one family where the
> two methods disagree (walk says `.vmp1` higher at 57.7/10k vs 44.6; traces say
> `.-wI` higher at 249 vs 17). `rcl`/`rcr` are `D0`–`D3 /2 /3`, common in
> garbage decode. I do not rely on that family.

---

## 2. In-degree distribution (MEASURED)

Direct `call`+`jmp` with immediate targets, taken from **aligned `.text` function
bodies** (`functions.csv` spans), scored identically for every destination:

| destination | distinct targets | edges | max in-degree | in-degree == 1 |
|---|---:|---:|---:|---:|
| `.vmp1` | 4,272 | 8,003 | **1,064** | 90.1% |
| `.-wI` | 691 | **691** | **1** | **100.0%** |
| `.text` (control) | 183,832 | 1,540,332 | 62,591 | 59.5% |

**`.-wI` has exactly as many edges as targets, and its maximum in-degree is 1.**
No `.-wI` address is reached from two different `.text` sites. That is a perfect
one-stub-per-call-site mapping, independently corroborated by
`wrapper-apis-a21.csv`: 1,654 rows, 1,654 distinct `entry_va`, one per call site.

`.vmp1` has real sharing. Its top target `0x0526AB41` is reached **1,064** times
(1,128 by the call-only count in `targets-text-to-vmp1-call.csv`), and it
disassembles to four instructions of ordinary compiled code:

```
0526AB41  mov  eax, dword ptr [ecx + 0xC4]
0526AB47  sub  eax, dword ptr [ecx + 0xC0]
0526AB4D  sar  eax, 2
0526AB50  ret
```

That is a `(end - begin) / 4` container-size accessor, unobfuscated, shared by a
thousand call sites. **A per-call-site stub generator and a shared-routine region
are not two settings of one generator.**

---

## 3. Chain length (MEASURED)

Straight-line from each entry to its first terminator (following direct
intra-section `jmp`), cap 400:

| | p50 | p90 | p99 | hit cap |
|---|---:|---:|---:|---:|
| `.-wI` (713 entries) | 36 | 85 | 120 | 0.1% |
| `.vmp1` (2,508 entries) | 26 | 400 | 400 | **38.7%** |
| `.text` ctrl (2,500) | 37 | 92 | 400 | 2.0% |

`.-wI` is **tightly bounded** — p99 at 120 instructions — exactly what a template
expander with a size budget produces. `.vmp1` is **bimodal and unbounded**: 4
instructions for the accessor above, past 400 for 38.7% of entries. That is the
length profile of real code of varying size, not of generated stubs.

Terminators differ categorically:

| | `.vmp1` | `.-wI` |
|---|---|---|
| `ret` | 189 | **364** |
| `jmp` indirect | 289 | 262 |
| `call` indirect | **348** | 9 |
| `jmp` → `.text` | **416** | 0 |
| `jmp` → `.mackt` | 0 | 29 |
| undecodable | 159 | 25 |

`.vmp1` exits into `.text` 416 times and never into `.mackt`; `.-wI` exits into
`.mackt` and never into `.text`. Their escape hatches do not overlap.

Multi-hop chains under symeval (hops until a target stops folding): `.-wI`
p50=1, p90=1 — **810 of 1,654 end at a single `ret`**. `.vmp1` p50=1, p90=12.

---

## 4. Junk density (MEASURED)

### 4a. Two idioms, scored symmetrically

The earlier version of this file judged `.vmp1` only by `.-wI`'s signature, which
is the base-rate error. Scoring each section under **both** alphabets:

- **ARITH idiom** = `.-wI` signature (`not`/`neg`/`bswap`/`clc`/`stc`/`cmc`/`bt*`/`bsf`/`bsr`/`shld`/`shrd`/`xadd`/`setcc`)
- **STACK idiom** = `.vmp1` signature (`pushfd`/`popfd`/`xchg` + `lea esp,…` + `mov dword ptr [esp…],…`)

| entries | ARITH p50 | ARITH p90 | ARITH >6% | STACK p50 | STACK p90 | STACK >6% |
|---|---:|---:|---:|---:|---:|---:|
| `.vmp1` (2,508) | 0.000 | 0.000 | **0.9%** | 0.050 | 0.333 | **47.6%** |
| `.-wI` (713) | 0.118 | 0.227 | **76.6%** | 0.053 | 0.170 | **41.7%** |
| `.text` (2,500) | 0.000 | 0.000 | 0.6% | 0.000 | 0.000 | 0.7% |

**This is the most important table in the report.** It shows one shared layer and
one exclusive layer:

- The **STACK idiom is shared** — 47.6% vs 41.7%, and effectively absent from
  `.text` (0.7%). Same substrate, comparable intensity.
- The **ARITH idiom is exclusive to `.-wI`** — 76.6% vs 0.9%, and `.vmp1`'s 0.9%
  is statistically indistinguishable from `.text`'s 0.6% baseline.

### 4b. Aggregate junk share of executed instructions

Sum of all ARITH-family operators as a fraction of executed instructions:

| | share |
|---|---:|
| `.-wI` | **15.7%** |
| `.vmp1` | **0.49%** |
| `.text` ctrl | 0.21% |

`.vmp1` is **32× less arithmetic-junk-dense than `.-wI`**, and only 2.3× above
unobfuscated compiled code.

### 4c. Functional junk

A `.-wI` thunk spends **36–85 instructions** (p50–p90) to accomplish one API
call — a **40–80× expansion** over the single `call dword ptr [IAT]` it replaces.
`.vmp1`'s long entries are long because they are doing real work, not because
they are inflated.

### 4d. Entry classification (400-instruction aligned walk, `.-wI` alphabet)

| class | `.vmp1` | `.-wI` | `.text` ctrl |
|---|---:|---:|---:|
| clean compiled | **73.9%** | 12.1% | 95.0% |
| short trampoline | 19.2% | 3.5% | 1.2% |
| ARITH-mutated | **1.2%** | **75.3%** | 0.7% |
| garbage/data | 4.4% | 3.9% | 0.0% |

### 4e. Byte-level entropy (4 KB blocks, sampled 1-in-8)

| | high-entropy blocks (>7.5) | median entropy |
|---|---:|---:|
| `.text` | 0.0% | 6.32 |
| **`.vmp1`** | **0.8%** | 7.25 |
| **`.-wI`** | **75.0%** | 7.77 |
| `.#|U` (packed blob) | 99.8% | 7.95 |
| `.data` | 0.0% (98.6% all-zero) | 0.00 |

`.-wI` sits next to the packed payload `.#|U`. `.vmp1` sits between `.text` and
`.-wI`, far closer to `.text`.

---

## 5. Verdict — same generator, or different tools?

**Neither, precisely. Same protector product, two different subsystems running
different pass pipelines.**

Evidence for a **common origin**:
- The STACK obfuscation substrate is present in both at comparable rates
  (47.6% / 41.7%) and absent from `.text` (0.7%).
- Both are runtime-materialized with no file backing.
- They are functionally coupled: `.vmp1` code calls `.-wI` thunks (215 sites),
  and 1,641/1,654 thunk `reloc_site`s point into `.vmp1`.

Evidence **against "one generator, one knob"**:
- Six arithmetic operator families at **literal zero** in executed `.vmp1` — an
  alphabet difference, not an intensity difference.
- In-degree topology is categorical, not gradual: max in-degree **1** vs
  **1,064**; 691 edges over 691 targets vs 8,003 over 4,272.
- Page protection differs: `.vmp1` is **RWX**, `.-wI` is **RX**.
- Entropy regime differs by two orders of magnitude in high-entropy block share
  (0.8% vs 75.0%).
- Length profile differs in kind: bounded (p99=120) vs unbounded (38.7% past 400).
- Escape hatches are disjoint: `.vmp1`→`.text`, `.-wI`→`.mackt`.

**INFERRED** (structure is consistent with this; I did not prove it): `.-wI` is
the protector's import-protection module and `.vmp1` is its code-relocation
module, the latter holding application code moved out of `.text` and lightly
wrapped in the shared stack layer. Commercial protectors ship exactly this
split. I did not identify the vendor and make no claim about which product it is
— note that symeval's own docstring names Themida, and the literal names
`.vmp0`/`.vmp1` suggest VMProtect, but **section names in this image are
demonstrably camouflage** and I treat both as unevidenced.

---

## 6. Can `symeval.py` be pointed at `.vmp1` as-is?

### First, an honest limit on my own numbers

I could **not** reproduce the pipeline that produced `wrapper-apis-a21.csv`.
Driving `symeval_i.run()` directly from each thunk entry with the correct return
address resolved a concrete target for only **103/1,654 = 6.2%** of `.-wI`
stubs — against 1,654 that are known-resolved. The CSV's `convention` column
(`esp+4/R` ×709, `esp+0/R+1` ×689, `esp+4/a1` ×103, `esp+8/a2` ×102, …) shows the
real pipeline had a **per-stub extraction layer** on top of `run()` that I did not
reconstruct. I also tried reading the machine's stack slots and registers for any
value landing in a loaded module: 1.3% on `.-wI`, 0 matching the recorded module.

**Therefore I deliberately do not base the verdict on a measured
success-rate comparison.** Any such rate I could quote measures my driver, not
symeval. Quoting one would be a narrow judgment supporting a wide conclusion.

I also found and corrected a **bug in my own chase driver** that would have
inflated `.vmp1`: 435/900 chains were bucketed "hop-limit", but **431 of those 435
had resolved to a single concrete address on hop 1** and my loop then re-ran from
that address forever (100% of hop-limit chains repeat an address; 431 have
exactly one distinct hop target). Those were successes mislabelled as failures.

### What the structure does support

**Yes — symeval runs on `.vmp1` unmodified. No code change is required.**

- Nothing in `symeval_i.py` is `.-wI`-specific. It implements x86 semantics one
  mnemonic at a time; the docstring is explicit that this is the design.
- Its `xok` fence is built from section **characteristics**, not names, and
  `.vmp1` is `E0000020` (executable), so the fence already admits it. I verified
  this by running it there.
- The accuracy work its docstring says was expensive — byte-addressed memory,
  `pushfd`/`popfd` as ordinary data, `ret`/`ret imm` esp adjustment across
  trampoline hops — is **precisely the machinery `.vmp1`'s STACK idiom needs**.
  That idiom is 47.6% of `.vmp1` entries. This transfers cleanly.

**But it will not pay off the way it does on `.-wI`, for two reasons:**

1. **There is little left to fold.** symeval's arithmetic simplification —
   `binop`/`unop`, the `INVERSE` table, involution cancelling — exists to collapse
   the ARITH layer. That layer is 15.7% of `.-wI` and **0.49% of `.vmp1`**, which
   is 2.3× the `.text` baseline. The engine's main lever idles.
2. **`.vmp1` has no per-entry "answer" to extract.** On `.-wI` the question is
   well-posed and single-valued: *which API does this stub reach?* `.vmp1` is not
   a thunk arena — it is mostly ordinary relocated code with real fan-in
   (max 1,064), real control flow, and 38.7% of entries running past 400
   instructions. Pointing symeval there and expecting `.-wI`-style yields is a
   category error, independent of how well the engine works.

### What `.vmp1` actually needs first

A **function inventory**. It has none — `functions.csv` is 100% `.text`, so all
67,074 known functions, 459 parsers and 759 source-file attributions stop at the
`.text` boundary while 8.2 MB of executable, writable, callable code sits
un-enumerated. The seed set is already in hand: **4,272 distinct `.vmp1` addresses
reached by 8,003 direct edges from aligned `.text` bodies**, of which 73.9%
disassemble as clean compiled code. That is a boundary-detection and
function-carving job, not a deobfuscation job.

Ordering recommendation: carve functions in `.vmp1` first, then use symeval
selectively on the STACK-idiom entries and the 348 `call`-indirect / 289
`jmp`-indirect terminators where a target genuinely needs folding.

---

## 7. Reconciliation with the previous `vmp1-6.md`

| prior claim | status |
|---|---|
| "Eight obfuscation operators … occur exactly zero times in aligned `.vmp1`" | **CONFIRMED**, and strengthened — six families at zero in 22,150 *executed* instructions, plus `bswap`/`neg` |
| "Different emitters, almost certainly the same protector product" | **CONFIRMED in direction**, but its own evidence was one-sided; the shared STACK layer (47.6%/41.7%) is the missing half |
| "`.-wI` 94.8% of entries yield a concrete transfer target" | **COULD NOT REPRODUCE** — my driver gets 6.2% against 1,654 known-resolved; neither figure should be trusted without the `convention` extraction layer |
| "`.vmp1` 24.6%" | **COULD NOT REPRODUCE**; and my own first attempt at this number was inflated by a driver bug (§6) |
| in-degree from linear sweep (`.vmp1` 97.5% / `.-wI` 99.5% indeg-1) | **SUPERSEDED** — linear sweep of an obfuscated blob measures byte statistics; aligned `.text`-sourced figures are 90.1% / 100.0%, max 1,064 / max 1 |
| "bytes per instruction 2.99 vs 2.77" | **NOT RELIED ON** — derived from linear sweep |
| "a 6-line `fs:` segment fix takes `.vmp1` to 34.9%" | **UNTESTED by me** |

---

## What would falsify this

The load-bearing claim is that the ARITH alphabet is *absent* from `.vmp1` rather
than rare. It falls if a properly-aligned sample of `.vmp1` an order of magnitude
larger than mine (say 250k executed instructions, reached from seeds other than
`.text` direct calls — e.g. from the `.tls` and `.vmp1`-internal call sites I did
not walk) turns up `bt`/`bsf`/`shld`/`xadd`/`bswap` at anything approaching
`.-wI`'s 113/9.5/15/40/149 per 10k. My sample is 22,150 executed and 414,711
aligned-walk instructions, covering only **5.1%** of `.vmp1`'s bytes — so this is
a claim about the reachable frontier, not about all 8.2 MB.

The in-degree claim falls if `.-wI` targets reached from `.tls` (673 wrapper call
sites live there, which I did not scan for in-degree) turn out to share stubs
across call sites; I measured max in-degree 1 **from `.text` only**.

## Scripts

All in `SCRATCH`, runnable: `_z6_walk.py` (aligned recursive descent),
`_z6_ent2.py` (entry classification), `_z6_style.py` (both-idiom scoring),
`_z6_final.py` (trace mix + verified in-degree), `_z6_chase2.py` (symeval chase),
`_z6_api.py` / `_z6_ptr.py` (refuted reloc-site hypothesis), `_z6_comp.py`
(entropy), `_z6_cyc.py` (the driver-bug check).
