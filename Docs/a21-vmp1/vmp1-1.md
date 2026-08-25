# .vmp1 convergence clusters 0x0542F873 and 0x054FEC24

**Answer in one line:** neither is an interpreter core nor a compiler-emitted shared
prologue. Both are *copies of the same 86-byte VMProtect VM-entry stub* (`vm_entry`) —
the fixed preamble that commits stack, recovers the relocation delta, decrypts the
bytecode pointer, and jumps to the first bytecode handler. There are **41 byte-identical
copies of it inside .vmp1**. These two are simply the copies that happened to serve the
densest slice of .text. The convergence counts describe VMProtect's code emitter, not
anything about the client.

Also worth recording up front, because it changes how .vmp1 should be treated:
**.vmp1 is not uniformly VM material.** It contains ordinary compiled C++ as well —
see §5.

---

## 1. The two addresses hold literally the same bytes

```
0x0542F873  e800000000582d78f84205648b3d180000008b7f088bcc2bcf81f9004000007f10
            81ef00100000853781c100100000ebe889078947048b742420c1c60981c6e6ac81
            bd03f033ed33db8b1e83c60481c39829c44c031f
0x054FEC24  e800000000582d29ec4f05648b3d180000008b7f088bcc2bcf81f9004000007f10
            81ef00100000853781c100100000ebe889078947048b742420c1c60981c6e6ac81
            bd03f033ed33db8b1e83c60481c39829c44c031f
```

86 bytes each. Differing offsets: **+0x07, +0x08, +0x09 only** — the immediate of
`sub eax, <own address>`. Every VM key constant is identical in both.

Annotated (addresses from 0x0542F873):

```
0542F873  e8 00000000    call $+5
0542F878  58             pop  eax
0542F879  2d 78f84205    sub  eax, 0x542F878     ; eax = image relocation delta
0542F87E  64 8b3d 18...  mov  edi, fs:[0x18]     ; TEB
0542F885  8b 7f 08       mov  edi, [edi+8]       ; NT_TIB.StackLimit
0542F888  8b cc          mov  ecx, esp
0542F88A  2b cf          sub  ecx, edi
0542F88C  81 f9 00400000 cmp  ecx, 0x4000        ;  \
0542F892  7f 10          jg   done               ;   | commit at least 16 KB of
0542F894  81 ef 00100000 sub  edi, 0x1000        ;   | stack below esp by walking
0542F89A  85 37          test [edi], esi         ;   | the guard page downward
0542F89C  81 c1 00100000 add  ecx, 0x1000        ;   |
0542F8A2  eb e8          jmp  loop               ;  /
0542F8A4  89 07     done:mov  [edi], eax         ; VM ctx slot 0 = reloc delta
0542F8A6  89 47 04       mov  [edi+4], eax       ; VM ctx slot 1 = reloc delta
0542F8A9  8b 74 24 20    mov  esi, [esp+0x20]    ; per-function bytecode key
0542F8AD  c1 c6 09       rol  esi, 9             ;  \
0542F8B0  81 c6 e6ac81bd add  esi, 0xBD81ACE6    ;   | decrypt -> VIP
0542F8B6  03 f0          add  esi, eax           ;  /
0542F8B8  33 ed          xor  ebp, ebp
0542F8BA  33 db          xor  ebx, ebx
0542F8BC  8b 1e          mov  ebx, [esi]         ; fetch first handler slot
0542F8BE  83 c6 04       add  esi, 4             ; advance VIP
0542F8C1  81 c3 9829c44c add  ebx, 0x4CC42998    ;  | decrypt handler address
0542F8C7  03 1f          add  ebx, [edi]         ;  /  (+ reloc delta)
0542F8C9  ff e3          jmp  ebx                ; enter the VM
```

So at the moment of `jmp ebx`: **esi = virtual instruction pointer, edi = VM context
base** (bottom of the 16 KB stack window it just committed).

## 2. 41 identical copies exist

Scanning .vmp1 for `64 8B 3D 18 00 00 00 8B 7F 08 8B CC 2B CF 81 F9 00 40 00 00 7F 10`:

```
051FD538 0520630C 05226A37 0528D018 052B24B2 052B6829 0538660E 0539D5E3
053F41EE 0542F873 0549CA95 054FEC24 055073D4 055A8E75 055D3942 056301C3
056477D6 056BE24D 056DB54C 056DC56A 0570E9DC 0573FF16 0577D7CC 057911EB
057924FD 05795A87 057B448D 057BC480 057FFB43 0580E618 0581B70C 05843F40
0584E06A 058904CF 058A0156 058B10B3 058B3C0A 05987BD7 05987FE5 05988F4B
0598981E
```

All 41 differ from each other in **exactly the 3 self-address bytes** and nowhere else.
Same `rol 9`, same `0xBD81ACE6`, same `0x4CC42998` in all 41. Six more copies sit in
.text and six in .tls (53 image-wide).

## 3. The convergence counts reproduce — and the two named clusters have two unnamed twins

I re-derived the grouping independently of the earlier symeval census, by concretely
walking each of the 2508 .text→.vmp1 targets through VMProtect's push-shuffle chain
(model: `push`/`pushfd`/`pushal`/`pop`/`lea esp,[esp±k]`/`jmp imm`/`call imm`/`ret`)
until it lands on one of the 41 preambles. 1261 of 2508 resolved. Group sizes:

| preamble | entries | | preamble | entries |
|---|---|---|---|---|
| **0x0542F873** | **136** | | 0x052B6829 | 38 |
| **0x054FEC24** | **126** | | 0x056DC56A | 38 |
| 0x055073D4 | 125 | | 0x056301C3 | 37 |
| 0x0549CA95 | 118 | | 0x0577D7CC | 36 |
| 0x05226A37 | 57 | | 0x055D3942 | 35 |
| 0x0520630C | 50 | | 0x0538660E | 34 |
| 0x051FD538 | 49 | | 0x056BE24D | 32 |
| 0x0539D5E3 | 47 | | 0x0570E9DC | 30 |
| 0x052B24B2 | 45 | | 0x055A8E75 | 29 |
| 0x053F41EE | 42 | | 0x0528D018 | 27 |
| 0x0573FF16 | 41 | | 0x057911EB | 5 |
| 0x056477D6 | 40 | | 0x057924FD | 2 |
| 0x056DB54C | 40 | | 0x05795A87 | 2 |

136 and 126 match the census's 137 and 126 (one entry my walker stopped on early).
The important part is what the census's top-2 framing hid: **0x055073D4 (125) and
0x0549CA95 (118) are the same thing at the same scale.** It is a 26-way split whose
top four are co-equal, not two hubs.

## 4. Which copy a site uses is decided by .text address, i.e. by build order

Direct .text→preamble edges, with the .text span each preamble serves:

```
0x051FD538  n= 24   .text 0x010F83A4 - 0x0176EEB7
0x0520630C  n= 25   .text 0x00B1E541 - 0x017CE8E9
0x05226A37  n= 20   .text 0x00C486F1 - 0x01705C36
0x0528D018  n= 17   .text 0x018361DA - 0x01B46F0A
0x052B24B2  n= 17   .text 0x01892700 - 0x01BC4075
0x052B6829  n= 12   .text 0x01835155 - 0x01BC40AA
0x0538660E  n= 29   .text 0x01BD0DF2 - 0x01D054DB
0x0539D5E3  n= 35   .text 0x01BD67C0 - 0x01CFD240
0x053F41EE  n= 35   .text 0x01BD4FEA - 0x01CFD4ED
0x0542F873  n=103   .text 0x01D107C3 - 0x02057720   <-- asked about
0x0549CA95  n=114   .text 0x01D1075B - 0x0205765F
0x054FEC24  n=104   .text 0x01D15699 - 0x02057E0E   <-- asked about
0x055073D4  n=111   .text 0x01D1D564 - 0x02057D21
0x055A8E75  n= 29   .text 0x020733F9 - 0x0219A534
0x055D3942  n= 29   .text 0x020733F2 - 0x0219FD0D
0x056301C3  n= 26   .text 0x021AA0AD - 0x022185ED
0x056477D6  n= 17   .text 0x021AA158 - 0x02222457
0x056BE24D  n= 21   .text 0x02233322 - 0x022E85BA
0x056DB54C  n= 17   .text 0x0222A66C - 0x022E7AFF
0x056DC56A  n= 30   .text 0x02225D09 - 0x022E32F7
0x0570E9DC  n= 11   .text 0x023663D6 - 0x0273D11D
0x0573FF16  n= 10   .text 0x0236634A - 0x027C4349
0x0577D7CC  n= 11   .text 0x023680BC - 0x0274FC07
0x057911EB  n=  2   .text 0x028A03C9 - 0x02BDAB3A
0x057924FD  n=  2   .text 0x028A1425 - 0x02BDAB2A
0x05795A87  n=  4   .text 0x028A03DD - 0x02C62899
(15 further preambles have no direct .text edge)
```

Preamble VA order tracks .text VA order monotonically, in bands of 2–4 co-serving
copies. **Every one of these 855 edges is a `jmp`; not one is a `call`.**

0x0542F873 and 0x054FEC24 belong to the band serving .text
**0x01D1_0000 – 0x0205_8000** together with 0x0549CA95 and 0x055073D4 —
432 virtualization sites in total.

## 5. Base rate: .vmp1 also holds plain compiled code

The largest .text→.vmp1 target overall is **0x0526AB41 with 1064 edges, 1063 of them
`call`**:

```
0526AB41  mov eax, [ecx+0xC4]
0526AB47  sub eax, [ecx+0xC0]
0526AB4D  sar eax, 2
0526AB50  ret
```

That is `std::vector<T>::size()` for a 4-byte T — ordinary compiled C++ living inside
.vmp1, called from 1063 .text sites. Any future .vmp1 work must not assume the section
is uniformly VM.

Note also that "top .vmp1 targets" mixes categories. 0x057131CC (136 edges) is not a
preamble but a *shuffle fragment* — `push 0xFEA3FB4A; push ecx; push edx; push ebx;
jmp 0x236A067` — with the bytecode key visible in the `push imm32`. 0x052C51AD is the
same shape (`push 0x8D23D382; call 0x52DA40C`).

## 6. The decrypt arithmetic verifies, and the destinations are VM handlers

Concrete example. Entry 0x051BF7D9 (`push 0xD123CF7B; push ecx; push edx; push ebx;
push eax; push esi; call 0x05207472; …`) reaches preamble 0x051FD538 with the key at
`[esp+0x20]`:

```
rol(0xD123CF7B, 9) + 0xBD81ACE6 = 0x0520A488       -> .vmp1   (VIP)
[0x0520A488] = 0xB4AC319B ; + 0x4CC42998            -> first handler
```

Across the 200 entries where I recovered the key concretely:
**198/200 decrypted VIPs land in .vmp1**, and **174/200 first-handler addresses land
in .vmp1**, resolving to only **28 distinct first-handler addresses** — a small shared
handler set, exactly what a VM looks like.

Two of the most-used first handlers:

```
05731CF2  mov eax, [esi+4]        ; 05212 2F7  xor eax, eax
05731CF9  and eax, 0xFF           ;            mov al, [esi+4]
05731CFF  lea ebp, [ebp]          ;            shl eax, 2
05731D05  rol eax, 2              ;            jmp 0x5251198
05731D08  lea eax, [eax+edi]      ;
05731D0B  jmp 0x5756DE0           ;
```

`[esi+4]` is a one-byte operand read out of the bytecode stream; `×4` indexes the VM
register file based at `edi`. Each handler terminates in **its own `jmp` to the next
handler** — VMProtect threaded dispatch. That is why no single high-in-degree
"interpreter core" address exists to be found: there is no central loop to converge on.
The vm_entry preamble is the only shared point, and it is duplicated 41 ways precisely
so that it is not one either.

## 6b. Structural proof that no interpreter core exists

Full linear sweep of .vmp1 (0x051BF000–0x0598B000), counting every `call`/`jmp`
immediate whose target is also inside .vmp1: **2,733,599 instructions, 97,953 distinct
internal targets.**

```
top internal in-degree                 in-degree distribution
   0x054FEC24  740   <-- asked about      1      : 95486
   0x0549CA95  701                        2-9    :  2398
   0x055073D4  690                        10-99  :    46
   0x0542F873  686   <-- asked about      100+   :    23
   0x0539D5E3  515
   0x0538660E  507      All 20 of the top-20 are vm_entry preambles from §2.
   0x053F41EE  495      The "100+" class (23 addresses) is essentially exactly
   0x051FD538  472      the set of 26 preambles that .text reaches.
   0x0520630C  464
   0x05226A37  434
   0x052B24B2  357
   0x0528D018  344
   0x052B6829  343
   0x056BE24D  341
   0x056301C3  323
   0x056DC56A  323
   0x0573FF16  323
   0x056DB54C  321
   0x0577D7CC  320
   0x0570E9DC  300
```

Two things follow directly:

1. **The two addresses asked about are #4 and #1 on this list** — they really are the
   convergence points of .vmp1, and they are of a kind with 24 others.
2. **97.5% of all internal targets have in-degree exactly 1.** A VM with a central
   dispatch loop would put one address at the top with a count comparable to the total
   handler-execution count. Nothing like that exists. The interpreter is fully threaded:
   handler → handler, one-to-one, no hub. The vm_entry preambles are the *only*
   high-in-degree addresses in the whole 8.2 MB section, and they are duplicated 41 ways
   so that even the entry is not a single chokepoint.

The gap between 740 static edges and the 104 direct .text edges (§4) / 126 walked
entries (§3) into 0x054FEC24 is the shuffle chain: VMProtect splits the 9-push
sequence across ~600 further .vmp1 fragments, each ending in its own
`jmp <vm_entry>` (e.g. 0x051C2AE5 `push ebp; push edi; jmp 0x520630C`).

## 7. What the protected code is

Shape of the protected sites (band 0x01D1–0x0205):

- 103 sites in 96 distinct .text functions (0x0542F873); 104 in 98 (0x054FEC24) —
  about one virtualization entry per function.
- Owning "functions" have median span 152 / 156 bytes, with the `jmp` at median
  offset ~27. Short native stub, then everything else is in the VM.

Literal census of the band 0x01D10000–0x02058000 (448 functions, 3022 literals),
against the whole-image base rate (8798 functions, 45688 literals):

| token | band | image-wide |
|---|---|---|
| `ani` | 39.4% | 13.0% |
| `Anim` | 37.6% | 10.9% |
| `Effect` | 31.7% | 8.3% |
| `Monster` | 2.55% | 1.71% |
| `Attack` | 2.15% | 1.13% |
| `Dungeon` | 0.10% | 0.76% |
| `Packet` | 0.03% | 0.19% |

Every class-shaped literal in the band is a skill/effect passive object:
`CNWallOfIce::procAppend`, `CNApocalypse::procAppend`, `CNMegaDrill…`,
`CNBigBangPunch…`, `CNDarkSteel*::procAppend`, `CNOvergrowth*::procAppend`,
`CNFlashGlobe::procAppend`, `CNBlast::procAppend`, `CNGunner::procAppend`, …
Top `Effect/Animation/<dir>` prefixes in the band: SubWeapon (22), TransformFlapper (21),
MegaDrill (21), BigBangPunch (18), RisingUpper (14), SwingEx (13), Bellatrix (11),
SpearOfVictory (11), ScudGenocide (8).

Ground-truth `__FILE__` evidence for the band (only 9 functions in range carry one):
`CNThrowWeapon.cpp`, `CNStorageBird.cpp`, `CNRidableObject.cpp` (×4), `CNMage.cpp` (×2),
`CNPriest.cpp` — i.e. `DNFClient\RDAR\Character\`, `\Monster\`, `\PassiveObject\`.

Whole-image virtualization density (`jmp` edges .text→.vmp1, 3866 total) confirms the
targeting: .text 0x01C0_0000–0x0230_0000 carries **3030 of 3866 (78%)**, peaking at
0x0210_0000 (640) and 0x01D0_0000 (520). Outside that window density is negligible
(≤ 50/MB).

---

## Measured vs inferred

**Measured** (bytes, edges, counts — all reproducible from SCRATCH):
- §1 byte identity and the 3-byte diff; the annotated 86-byte body.
- §2 the 41 copies and their pairwise identity.
- §3 group sizes from an independent walk (136 / 126 / 125 / 118 / …).
- §4 the per-preamble .text bands; all 855 edges are `jmp`.
- §5 0x0526AB41 is plain `vector::size()` with 1063 `call` edges.
- §6 the decrypt arithmetic on a live key; 198/200 VIPs and 174/200 handlers in .vmp1;
  28 distinct first handlers; the two handler bodies.
- §6b the full-section in-degree sweep: 2,733,599 instructions, 97,953 targets,
  95,486 of them in-degree 1, top-20 all preambles.
- §7 site/function counts, span statistics, literal percentages, `__FILE__` names,
  density histogram.

**Inferred:**
- *Which preamble copy a site jumps to carries no semantic meaning.* Grounds: the copies
  are byte-identical with identical keys, and the assignment is monotone in .text
  address. So "137 entries converge on 0x0542F873" is a fact about VMProtect's emitter
  (it duplicates `vm_entry` periodically as it walks functions in address order), not
  about DNF. Confidence: high.
- *The band 0x01D1_0000–0x0205_8000 is skill/effect PassiveObject code
  (`CN*::procAppend`).* Grounds: 3–4× literal enrichment plus consistent `__FILE__` and
  class-name evidence. Confidence: medium — enrichment is strong but not exclusive, and
  per-function attribution is thin (of the 96/98 owning functions only 3 and 0 carry
  literals directly; the rest are literal-free stubs).
- *These are whole-function virtualizations rather than per-block mutations.* Grounds:
  ~1 site per owning function, short spans, `jmp`-only edges. Confidence: medium — the
  functions.csv boundaries inside this obfuscated band may themselves be synthetic.

**Unknown / not established:**
- Bytecode semantics past the first handler. No devirtualization was attempted, and
  nothing here supports a claim about what any individual virtualized function computes.
- Whether the 15 preambles with no direct .text edge are dead copies or reached only
  through longer chains.
- Why 26 of 200 first-handler decodes resolved into .text rather than .vmp1 — most
  likely my stack model picking the wrong `[esp+0x20]` slot on those chains, but not
  demonstrated.
- Whether VMProtect's choice of *which* functions to virtualize inside the band tracks
  anything the server can observe.

## Reproduction

Helpers written for this, in SCRATCH:
- `vpeek.py` — VA→offset disassembler that works outside functions.csv's range
  (functions.csv stops at 0x02DF3903, so `peek.py` silently mis-resolves every .vmp1
  address; it returned the same bogus function for both targets asked about).
- `vwalk.py` — concrete walker of the VMProtect push-shuffle chain to its preamble.
- `_grp.json` (preamble → entry list), `_rec.json` (entry → preamble, key, VIP, first
  handler).
