# The 2,508 `.vmp1` entries called from `.text` — how many are live?

DNF S4A21 unpacked-from-memory image, base `0x00400000`.
`.vmp1` = `0x051BF000 – 0x0598B000`, 8.18 MB, characteristics `0xE0000020` (code, exec).

## Answer in one line

**All 2,508 are live — but only ~448 of them are *callees*.**
2,060 of the 2,508 (82.1%) begin by throwing the return address away, which means
the `call` that reaches them is an obfuscated **jump**, not a call: those entries are
relocated `.text` basic blocks, entered exactly once each. The remaining 448 are real
callable stubs, and the call traffic into them comes disproportionately from the
client's *most* alive code — named `.cpp` functions, string-carrying functions, and
above all the network layer.

I found **no measurable dead subset**. The largest bucket with no positive
caller-graph evidence is **50 entries (2.0%)**, and even those sit behind valid call
instructions in real functions.

---

## 0. What the 2,508 actually is (reproduced, not assumed)

`targets-text-to-vmp1-call.csv` = 2,508 rows, `sum(in_degree)` = 5,211.
I reconstructed the call sites independently and matched it:

| construction | distinct `.vmp1` targets |
|---|---|
| raw `E8` scan, site in `.text`, target in `.vmp1` | 3,599 |
| raw `E9` scan (tail-jumps) | 5,059 |
| **the canonical CSV** | **2,508** |
| capstone linear sweep of every `functions.csv` span, `call imm` only | 1,679 |

Every one of the 2,508 is present in the raw `E8` set, and 2,506 of 2,508 in-degrees
match the raw `E8` site count exactly (the two that differ by one:
`0x05796EBE` 77 vs 78, `0x052FA543` 67 vs 68). So the canonical set is
**"distinct `.text` `call rel32` targets in `.vmp1`, after a validity filter"**, and I
can rebuild its call sites: **5,213 sites**, all 5,213 inside a `functions.csv`
function span, **2,777 distinct `.text` caller functions** (4.1% of the 67,074).

Two things the 2,508 is *not*:

* **Not the API import surface.** All 1,654 `wrapper-apis-a21.csv` entries live in
  `.-wI` (`0x05998000–0x05D57D15`), none in `.vmp1`. Zero of the 2,508 is a wrapper.
  (Conversely 215 wrapper *call sites* are inside `.vmp1` — `.vmp1` calls Win32.)
* **Not the VM entry surface.** Zero of the 2,508 matches the 86-byte `vm_entry` stub
  catalogued in `vmp1-1.md` (41 copies). `.text` reaches the VM some other way.
  Separately: `.text` also has **489 `Jcc` branches into `.vmp1`, 461 distinct targets,
  with zero overlap** with the 2,508 — a third, independent entry surface.

## 1. MEASURED — the call sites are real instructions

Independent instruction-boundary test: for each of the 5,213 sites, start a linear
sweep at each of the 24 preceding bytes and count how many sweeps land exactly on the
site (x86 self-synchronises, so a true boundary attracts consensus).

```
consensus >= 12/24 : 4,839 / 5,213 sites   (2,344 of the 2,508 targets)
consensus <=  3/24 :    11 / 5,213 sites   (10 targets, all sites weak)
```

The 164 targets with no >=12 site are **not false positives** — they sit in `.text`
regions that VMProtect itself mutated, so the surrounding bytes disassemble as junk
while the call is plainly real:

```
0191BB98  cmp   edi, 0xc
0191BB9B  ja    0x529c1d8          <- .text Jcc into .vmp1
0191BBA1  call  0x52d3c4c          <- the "unconfirmed" site, consensus 10/24
```

## 2. MEASURED — the call sites are *more* central than average, not less

CFG walk from each caller's entry (follow `jcc` both ways, `jmp imm`, fallthrough;
stop at `ret`/`jmp reg`):

```
.vmp1 call sites CFG-reachable from their function entry : 1,809 / 5,213  (34.7%)
```

That looks damning until you run the matched control — **all** direct call sites in
the **same 2,777 functions**:

```
all direct call sites in those functions, CFG-reachable : 42,293 / 136,774 (30.9%)
    of which .vmp1-directed, CFG-reachable              :  1,812 /   4,153 (43.6%)
```

The walker loses ~2/3 of *every* function's call sites because it cannot follow
switch jump tables. Against that local base rate, `.vmp1` sites score **43.6% vs
30.9%** — they are more, not less, central in their own functions. The 34.7% figure
is a property of my walker, not of `.vmp1`.

## 3. MEASURED — caller profile vs. the 67,074-function base rate

Every axis is independent and every one is enriched.

| property of the caller | ALL .text (67,074) | .vmp1 callers (2,777) | ratio |
|---|---|---|---|
| in `func-source-file.csv` (ground truth) | 1.13% | **3.75%** | 3.3x |
| has own decrypted literals | 13.1% | **28.2%** | 2.2x |
| reaches a Win32 API within 4 calls | 17.5% | **27.6%** | 1.6x |
| on the parse surface (`parsers.csv`) | 0.68% | **1.19%** | 1.7x |
| can reach a GT anchor in the callgraph | 59.4% | **79.5%** | 1.34x |

Control: the complement (`ALL − callers`, n=64,297) sits at 1.02% / 12.5% / 17.1% /
0.66% / 58.5% — i.e. the whole enrichment is carried by the caller set.

**Do not** use `functions.csv:in_degree` for this — `callgraph.csv` contains self
edges (`0x00401000 -> 0x00401000`), so 0.0% of functions have in_degree 0. Excluding
self edges the real orphan rate is 3.7% image-wide.

## 4. MEASURED — source files (ground truth)

**104 of the 2,777 callers are in `func-source-file.csv` — 13.7% of all 759 GT
functions**, spread over 40 distinct `.cpp` files. Not a corner of the client:

```
7  GameCoreNetwork_NOTI_Etc.cpp      3  CNGameSocket.cpp            1  CNRDSkillManager.cpp
4  GameCoreNetwork_NOTI_Dungeon.cpp  3  PassiveObject\Cinematic\MovieDirector.cpp
4  CNRDUdpModule.cpp                 2  CNRDUdpIdentify.cpp         1  Character\CNPriest.cpp
3  GameCoreSub.cpp                   2  CNPVPModule.cpp             1  CNRDDungeon.cpp
3  GameCoreNetwork_CMD_Town.cpp      2  PassiveObject\CNTileGameManager.cpp
   ... + Localization\japan\HousingSystem\GrowingTreeSystem.cpp, ItemFactory.cpp,
       CNTownModule.cpp, IRDCharacter.cpp, Nengine\ISocket.cpp, CND3DBaseTexture.cpp, ...
```

**The strongest signal in this whole analysis** — GT functions split by subsystem:

```
GT network-layer funcs (GameCoreNetwork*|GameSocket|Udp*|ISocket|PacketHandler):
      35 / 108  call .vmp1   (32.4%)
GT everything else:
      69 / 651  call .vmp1   (10.6%)
Fisher exact, two-sided:  p = 3.4e-8
```

Base rate for comparison: 4.1% of all `.text` functions call `.vmp1`. The client's
socket/packet layer is 8x that rate. Worked examples, all VAs verifiable:

```
0x022EA720  CNRDUdpModule.cpp        -> 11 distinct .vmp1 entries
              literals: "battlemode", "D:\Work\1229\Source\DNFClient\RDAR\CNRDUdpModule.cpp"
0x01146000  GameCoreNetwork_NOTI_Dungeon.cpp -> 7 entries  (also a parsers.csv function,
              287 read-primitive calls)
0x01119280  GameCoreNetwork_CMD_Town.cpp     -> 6 entries
0x027090E0  CNTownModule.cpp                 -> 8 entries
0x01630A00  GameCoreDebug.cpp                -> 9 entries
0x051DADEC  <- 0x01644210 GameCoreSub.cpp, 0x0274DDD0 + 0x0274E6D0 CNGameSocket.cpp
0x056ACE5A  <- 0x022E7B40, 0x022E8E10, 0x022EA720 CNRDUdpModule.cpp
0x057018B9  <- 0x022E3D30, 0x022E4080 CNRDUdpIdentify.cpp
```

## 5. MEASURED — target-side sanity

Bounded flow walk (<=600 instructions) from each of the 2,508:

```
reaches a ret                              1,028
calls back into .text                      1,262
hits an indirect jmp (VM dispatch)         2,028
any of the above  ->  2,499 / 2,508
```

The 9 that show nothing are still code, just not function entries:
`0x0586F95C` = `mov eax,[ebp-0x7c]; mov ecx,[ebp-0x7c]; mov edx,[eax+8];
sub edx,[ecx+4]; jmp 0x0588AF34` — a container-size computation on an *already
established* `ebp` frame, i.e. the middle of a relocated function. The other 8 start
with `lea esp,[esp+4]` / `push ebp; mov ebp,[esp]` — VMProtect return-address idioms.

## 6. THE STRUCTURAL RESULT — 82% of the "calls" are not calls

First instruction at each of the 2,508 entries:

| entry shape | entries | call sites they carry |
|---|---|---|
| discards the return address immediately (`lea esp,[esp+4]` x1,997, `pop reg` x63) | **2,060** (82.1%) | **2,060** |
| `pushfd` / `pushal` / `push imm` — VM-context save | 427 (17.0%) | 2,002 |
| mid-body / other | 21 (0.8%) | 1,151 |
| | **2,508** | **5,213** |

The first row is **exactly 1:1** — 2,060 entries, 2,060 call sites, one each. A `call X`
followed at `X` by `lea esp,[esp+4]` pops the pushed return address and never comes
back: it is an unconditional **jump** wearing a call opcode, VMProtect's standard
linear-disassembly / call-ret-pairing breaker. These 2,060 are relocated `.text`
basic-block tails, and each `.text` site is the single edge into it.

The 427 VM-context entries are the real shared stubs (avg 4.7 sites each), and they
are where the ground truth concentrates: of the 104 GT-called targets, **96 are
VM-context shape**, 5 discard, 3 other.

The 21 "other" carry 1,151 sites, dominated by one address:

```
0526AB41  mov eax, [ecx+0xC4] ; sub eax, [ecx+0xC0] ; sar eax, 2 ; ret
```
`std::vector<T*>::size()` — 1,128 call sites, **498 distinct caller functions**, 23 of
them GT-named (`CNAuraIce.cpp`, `CNChangeTeam.cpp`, `CNRDUdpModule.cpp`,
`MovieDirector.cpp`, `IRDMonster.cpp`, `CNMage.cpp`, `CNSwamp.cpp`, ...).
Confirms `vmp1-0.md` §2 independently. Two more hot ones:

```
057131CC  push 0xFEA3FB4A ; push ecx ; push edx ; push ebx ; jmp 0x0236A067
          -- 136 sites, 105 callers; pushes a key and jumps BACK into .text
052F1302  pushfd ; mov [esp],ebp ; mov ebp,esp ; lea esp,[esp-4] ; pushfd ...
          -- 123 sites, 74 callers; VM context frame build
```

Concentration: top 1 target = 21.6% of all 5,213 sites, top 10 = 35.4%, top 100 =
51.5%. 2,327 targets have exactly one site; 2,354 have exactly one caller function.

## 7. The dead-candidate ceiling: 50 entries (2.0%)

Real in-degree, self edges excluded:

```
.text functions with no non-self caller  : 2,463 / 67,074 (3.7%)
.vmp1 callers with no non-self caller    :   213 /  2,777 (7.7%)   <- 2.1x
targets reached ONLY through such callers:    50 /  2,508 (2.0%)
```

That 2.1x is the only counter-signal I found, and it does not survive scrutiny.
`callgraph.csv` is a direct-call graph, so C++ virtual methods are orphans by
construction — and the orphan `.vmp1` callers are exactly that:

```
address appears as a dword inside .code (the real data section holding vftables):
  ALL .text functions                  1,636 / 67,074   2.4%
  .vmp1 callers                          127 /  2,777   4.6%
  orphan .vmp1 callers                    13 /    213   6.1%   <- 2.8x vs orphans
  ALL orphan .text functions              53 /  2,463   2.2%
```

Ruled out as the explanation: `.vmp1` branching back into them. `.vmp1` makes 22,768
direct `E8`/`E9` branches into `.text` hitting 746 function entries, but only **2 of
the 213** orphan callers are among them (0.9%) against a 1.1% image-wide base rate —
no enrichment, hypothesis dead.

The 50 orphan-only targets, for anyone who wants to chase them:
```
05218767 0521CEE9 05223330 0525E7A0 05297A8B 052E00C0 052E6505 0530E3F7 05349D8A
0534A897 053E59A4 053F0AC3 0540502D 0540DD39 054419EE 05463E86 054E3EBA 0551DA89
0554AD81 055537D0 0557771D 05579B0F 0559C3E0 055AD2ED 055B82BA 055E35BF 055EA1D6
055FB38D 056025CC 05621303 056436B8 0564F25A 0565C270 05660E84 05667452 05675965
05679675 0567D85A 0567FB63 056DAEC3 056EDD4C 056EF067 05706DB2 05709C05 0571BD82
05744261 0574ECC8 0578DAF8 0579766A 057981C0
```

## 8. Liveness roll-up

| criterion | targets passing |
|---|---|
| A — >=1 call site at consensus >=12/24 | 2,344 |
| B — >=1 caller that reaches a ground-truth anchor | 1,813 |
| C — target body behaves like executing code | 2,499 |
| D — >=1 caller with a real (non-self) `.text` caller | 2,458 |
| **A or B or C** | **2,508** |
| A and C | 2,336 |
| A and B and C | 1,691 |

B is the weak criterion and it is a *coverage* limit, not a death certificate: its
base rate over all 67,074 `.text` functions is 59.4%, so failing B mostly means the
anchor set (759 source-file functions + 459 parsers) does not reach that corner of the
program.

---

## Measured vs. inferred

**Measured** — everything in §0–§7 above: the 5,213 reconstructed call sites and their
2,777 owning functions; the 24-way boundary consensus; the CFG walk and its matched
control; all five base-rate comparisons; the 104 GT callers and their 40 files; the
Fisher test; the bounded target-side walk; the first-instruction census; the 1:1
site/entry identity for the discard bucket; the vftable and `.vmp1`-back-edge rates.

**Inferred** —
1. `lea esp,[esp+4]` at entry means the call is a jump (VMProtect idiom; the 1:1
   site-to-entry ratio is the corroboration). *Confidence: high.*
2. Therefore the 2,060 discard-shape entries are relocated `.text` code, not
   subroutines, and `.vmp1` is partly a **code-relocation** target, not only a VM.
   *Confidence: high* — consistent with `vmp1-0.md`.
3. All 2,508 execute. *Confidence: high for the ~2,458 with non-orphan callers;
   medium for the 50 orphan-only ones — they have valid call instructions in real
   functions, but nothing in a direct-call graph proves their callers run.*
4. The network layer is disproportionately protected by `.vmp1`. *Confidence: high*
   (p = 3.4e-8), **but** note this is measured over the 759 GT functions only, which
   are themselves a biased sample (functions that happen to carry a `__FILE__`
   literal — `printf`-style logging is denser in network code). The honest claim is
   "among functions that log their own source file, network ones call `.vmp1` 3x more
   often", not "the protector targeted networking".

**What would falsify this:** a runtime trace. If a breakpoint on a sample of the 2,060
discard-shape entries never fires during a normal login-to-dungeon session, my
"all live" claim collapses to "all structurally valid". Nothing in a static dump can
distinguish reachable-in-principle from actually-executed, and I did not run the
client. Second falsifier: if the 1,997 `lea esp,[esp+4]` entries turn out to be
reached mostly by paths other than their single `.text` call site, the 1:1 argument
that they are one-shot relocated blocks weakens.

**Scripts (all under SCRATCH):** `_v3_scan.py`, `_v3_callers.py`, `_v3_valid.py`,
`_v3_live.py`, `_v3_ctl.py`, `_v3_prof.py`, `_v3_final.py`, `_v3_tgt.py`,
`_v3_mod.py`, `_v3_net.py`, `_v3_weak.py`, `_v3_shape.py`, `_v3_orph.py`, `_v3_vt.py`.
Intermediates: `_v3_sitemap.pkl` (site→target→owner), `_v3_consensus.pkl`,
`_v3_reachsite.pkl`, `_v3_tgt.pkl`, `_v3_kind.pkl`.

**One correction for the fleet:** the root set I first tried — "function entry appears
as an aligned dword outside `.text`" — is **noise**. 1,361,491 such dwords point into
`.text`; at the 0.145% chance of a random `.text` address being a `functions.csv`
entry that predicts ~1,974 hits and the observed count is 1,824. Global reachability
built on it is meaningless. The real vftables live in **`.code`** (`0x03029000–
0x0395E000`), which despite its name is a *data* section (`chars=0xC0000040`).
