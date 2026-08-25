# What `.vmp1` actually is

DNF S4A21 client, unpacked-from-memory image, base `0x00400000`.
`.vmp1` = **VA 0x051BF000 - 0x0598B000, 7.80 MB, executable.**

## Answer in one line

**None of the three.** `.vmp1` is a *relocated, mutation-obfuscated copy of a large
slice of the program's own native x86 code* — real compiled C++ functions, moved out
of `.text`, split into basic blocks, scattered, and re-joined by computed jumps.
There *is* a shared dispatcher, and it *is* highly converged; the census missed it
because (a) the dispatcher is **replicated and polymorphic** (41 byte-identical VM
entries + 407 dispatch epilogues in 75 byte-variants), and (b) the dominant control
transfer in `.vmp1` is an **indirect** `jmp reg`, which a direct-branch in-degree
census cannot see at all.

---

## 1. The measurement that settles it: `.vmp1` calls the game's own functions

Raw scan of every `E8`/`E9` inside `.vmp1` whose displacement lands in `.text`:

| measured | value |
|---|---|
| `.vmp1` -> `.text` direct branches | **22,768** |
| ... landing *exactly* on a `functions.csv` entry point | **13,179 (58%)** |
| distinct `.text` functions so called | **746** |
| random base rate (67,074 entries / 44.16 MB) | **0.145%** |

58% vs a 0.145% base rate is a **400x** enrichment. Bytecode does not do that.
20 of the 746 are `func-source-file.csv` ground-truth functions, e.g.

```
0x0274D1E0  DNFClient\RDAR\CNGameSocket.cpp
0x022E6990  DNFClient\RDAR\CNRDUdpModule.cpp
0x0199B9F0  DNFClient\RDAR\CNRDStage.cpp
0x019D72A0  DNFClient\RDAR\CNRDSkillManager.cpp
0x027C42E0  Library\Include\Nengine\CNMemoryPool.cpp
```

`.vmp1` participates in the program's own call graph as a *caller*. It is code.

## 2. `.vmp1` contains plainly unobfuscated compiled functions

`0x0526AB41` — the single most-called `.vmp1` address (**1128 direct `call`s from
`.text`**, 14 from `.vmp1`, 1 `jmp`). Read end to end, it is four instructions:

```
0526AB41  mov  eax, dword ptr [ecx + 0xC4]
0526AB47  sub  eax, dword ptr [ecx + 0xC0]
0526AB4D  sar  eax, 2
0526AB50  ret
```

That is `std::vector<T*>::size()`: `(end - begin) >> 2`. Its 16-byte body
(`8B81C40000002B81C0000000C1F802C3`) occurs **exactly once in the whole image** —
it is not a copy of something in `.text`, it is a unique function body that lives
only in `.vmp1`.

`0x05796EBE` (78 `call`s from `.text`) is a textbook MSVC member function:

```
05796EBE  push ebp ; mov ebp,esp ; push ecx ; push esi
05796EC3  mov  esi, ecx            ; this
05796EC5  lea  ecx, [esi + 0x34]
05796ECA  and  eax, 0xF            ; 16-byte alignment test
05796ED0  cmp  eax, 0xC
05796ED3  ja   0x0579174C          ; slow path -> calls 0x027BC390 in .text
```

This is the *most-converged-address* result explained: `0x0526AB41` is reached by
1128 of the entries not because it is a dispatcher but because it is a hot inline
accessor the linker/protector left as an out-of-line helper.

## 3. The obfuscation is VMProtect-style **mutation**, and it is measurable

Byte-pattern density per KB (raw counts; `.text` is the base rate for
ordinary MSVC output):

| idiom | `.vmp1` (7.80 MB) | `.text` (44.16 MB) | `.tls` (2.26 MB) |
|---|---|---|---|
| `E8 01 00 00 00` `call $+6` (jump over a junk byte) | **28,462 (3.6/KB)** | 2,323 (0.05/KB) | 5,838 (2.5/KB) |
| `81 04 24 ..` `add dword ptr [esp], imm32` (patch return addr) | **11,551 (1.4/KB)** | 773 (0.02/KB) | 2,505 (1.1/KB) |
| `8D 64 24 04` `lea esp,[esp+4]` (obfuscated pop) | **25,668 (3.2/KB)** | 2,700 | 2,469 |
| `lea esp,[esp-4]` (obfuscated push) | **12,203 (1.5/KB)** | 187 | 3,652 |
| `9C 89 ..` `pushfd; mov [esp+n],reg` (obfuscated reg move) | **8,803 (1.1/KB)** | 1,350 | 2,861 |
| `FF E0/E1/E2/E3` `jmp eax/ecx/edx/ebx` | **18,461 (2.3/KB)** | 2,424 (0.05/KB) | 2,344 |
| `FF E6/E7` `jmp esi/edi` | **28** | 238 | 13 |
| `CC CC CC CC` int3 alignment padding | **0** | 264,631 (5.9/KB) | 1,302 |

Three things fall out of that table:

* **Zero int3 padding in 7.8 MB.** `.vmp1` is not compiler output laid down
  function-by-function; blocks are packed with no alignment.
* The mutation engine picks its scratch register uniformly from
  `eax/ecx/edx/ebx` (4693 / 4518 / 4644 / 4606) and **never** `esi`/`edi` — those
  two are reserved (see §4: `esi` is the VIP, `edi` the relocation base).
* **`.tls` (VA 0x04F79000-0x051BB000, 2.26 MB) has the same idiom densities.**
  It is the same kind of region and has also never been analysed.

Sampling 800 `jmp reg` sites in `.vmp1` and back-aligning the decode:

```
 50.3%   call $+5 ; pop R ; lea R,[R +/- K] ; jmp R     <- position-independent computed jump
 16.5%   (could not back-align in 56 bytes)
  ~3%    mov R,[esi] ... jmp R                          <- the dispatcher, see below
```

So **half of all control transfer inside `.vmp1` is a get-EIP-relative computed
jump.** That single fact explains "98% of internal call/jmp targets have in-degree 1":
the real edges are not direct branches, so the direct-branch census never saw them.

### A block read end to end

Entry `0x052F1302`, followed through the `call/add[esp]/ret` idiom
(script `_v1rd2.py` in SCRATCH implements it). One C++ member function, four blocks,
spread over 448 KB of `.vmp1`:

```
052F1302  pushfd ; mov [esp],ebp        == push ebp
052F1306  mov ebp, esp
052F1308  lea esp,[esp-4] ... popfd     == push ecx, push esi   (obfuscated)
052F1323  call 052F1329 / add [esp],0xFFFF1D6C / ret            -> jmp 0x052E3094

052E3094  popfd ; push ebp ; mov ebp,ecx ; mov esi,0x38A90777
052E309E  lea esi,[esi-0x1BDD7CB7] ; mov esi,esi ; mov esi,ebp  == esi = ecx (dead constant math)
052E30A9  call 0x01102C30                       ; <- real .text call
052E30AE  push esi ; mov esi, eax
052E30C0  call/add[esp]/ret                     -> jmp 0x05284056

05284056  popfd ; mov ecx,esi ; pop esi
0528405A  call 0x0163E260                       ; <- real .text call
0528405F  test al,al ; jne 0x052F9271 ; jmp 0x0527A673

0527A673  ... push ebp ; push edi ; jmp 0x019900B5   (tail back into .text)
052F9271  ... function epilogue, then jmp 0x0528D018 (VM entry, §4)
```

Ordinary C++: `this` in `ecx`, two helper calls, a boolean test, two exits.
Constants are split (`mov r,K1; lea r,[r+K2]`), register moves go through the stack,
and every edge is an obfuscated jump.

## 4. There **is** a shared dispatcher — it is replicated 41x and polymorphic

Scanning `.vmp1` for `mov edi, fs:[0x18]` returns **41 hits, and all 41 are preceded
at -11 by `E8 00 00 00 00` (get-EIP)**. Decoding one:

```
051FD538  call 0x051FD53D
051FD53D  pop  eax
051FD53E  sub  eax, 0x051FD53D          ; eax = image load delta
051FD543  mov  edi, fs:[0x18]           ; TEB
051FD54A  mov  edi, [edi + 8]           ; NT_TIB.StackLimit
051FD54D  mov  ecx, esp
051FD54F  sub  ecx, edi
051FD551  cmp  ecx, 0x4000              ; commit >=16 KB of stack
051FD557  jg   0x051FD569
051FD559    sub  edi, 0x1000
051FD55F    test [edi], esi             ; touch the guard page
051FD561    add  ecx, 0x1000
051FD567    jmp  0x051FD551
051FD569  mov  [edi],     eax           ; stash the delta at the stack limit
051FD56B  mov  [edi + 4], eax           ;   -> a per-thread relocation base
051FD56E  mov  esi, [esp + 0x20]        ; the encrypted handle pushed by the caller
051FD572  rol  esi, 9
051FD575  add  esi, 0xBD81ACE6
051FD57B  add  esi, eax                 ; esi = VIP (decrypted + relocated)
051FD57D  xor  ebp, ebp
051FD57F  xor  ebx, ebx
051FD581  mov  ebx, [esi]               ; fetch successor
051FD583  add  esi, 4                   ; advance VIP
051FD586  add  ebx, 0x4CC42998          ; decrypt
051FD58C  add  ebx, [edi]               ; relocate
051FD58E  jmp  ebx
```

All 41 are **byte-identical modulo their own self-relative address** — same
`rol 9`, same `0xBD81ACE6`, same `0x4CC42998`. Direct `E8`/`E9` branches landing on
those 41 addresses, counted across the whole image:

```
total 12,253   (41 targets; chance of a random 32-bit displacement hitting
                one of 41 exact addresses over 2.3M anchors is ~0.00002)
  0x054FEC24  985   (.text 169, .vmp1 816)
  0x0549CA95  942   (.text 181, .vmp1 761)
  0x055073D4  941   ...
  0x0542F873  909
  0x0538660E  609
  ... 36 more, tail down to x1
```

**That is the convergence the census was looking for.** It is invisible to a
per-entry symbolic walk with a small budget, and it is split 41 ways, so no single
address tops the "reached by N entries" list.

### The dispatcher's continuation exists too — in 75 byte-variants

Regex over `.vmp1` for `mov R,[esi] ... add R,[edi] ... jmp R` finds **407 sites**,
which collapse to **75 distinct byte sequences** and **39 distinct additive keys**:

```
x41  mov ebx,[esi] | add esi,4          | add ebx,0x4CC42998 | add ebx,[edi] | jmp ebx   (= the 41 entries)
x36  mov ecx,[esi] | add ecx,[edi]      | add ecx,0x2437F6A4 | add esi,8     | jmp ecx
x32  mov edx,[esi] | add edx,0x40BEAAB0 | add edx,[edi]      | add esi,5     | jmp edx
x28  mov edx,[esi] | lea esi,[esi+4]    | add edx,[edi]      | add edx,0x27247132 | jmp edx
x28  mov edx,[esi] | add edx,0x561F4210 | add edx,[edi]      | add esi,5     | jmp edx
x26  mov edx,[esi] | add esi,5          | add edx,0x7356DA88 | add edx,[edi] | jmp edx
x23  mov ebx,[esi] | add ebx,0x3E897F40 | add ebx,[edi]      | add esi,4     | jmp ebx
x21  mov ebx,[esi] | add ebx,0x31D7789A | add ebx,[edi]      | lea esi,[esi+5] | jmp ebx
...
```
(e.g. `0x051BFDBD`, `0x051D3CE5`, `0x051DBCAC`, `0x051DE11D`, `0x051E3A62`, `0x052F92F3`)

Every clone family differs in **register** (`eax/ebx/ecx/edx`), **instruction order**,
**additive key**, and **VIP stride** (`4` = pointer only, `5` = pointer + 1 operand
byte, `8` = pointer + dword operand), and `add esi,N` vs `lea esi,[esi+N]`. That is a
polymorphic replicated dispatcher, which by construction can never converge under a
byte-identity or a direct-branch test. In `.text` the same regex yields 43 hits over
44 MB; in `.vmp1`, 407 over 7.8 MB — a 48x enrichment.

Register roles are consistent across all of it: **`esi` = virtual instruction
pointer, `edi` = pointer to the per-thread relocation slot at the stack limit.** That
is exactly why the mutation engine never picks `esi`/`edi` for its scratch jumps (§3).

## 5. `.text` is not clean either

The `.text` side of these edges is the matching half of the same machine.
`0x0236A067`, sitting in the inter-function padding just before a normal MSVC
function at `0x0236A080`:

```
0236A067  push eax ; push esi ; call 0x0572F0D5      ; -> .vmp1
0236A073  push esi ; pushfd ; push ebp ; jmp 0x0576C234  ; -> .vmp1
0236A07D  int3 int3 int3
0236A080  push ebp ; mov ebp,esp ; sub esp,0x18 ...  ; ordinary function
```

and `0x0124E346` (a real `.text` function body) uses the identical mutation idioms
before entering the dispatcher:

```
0124E348  pushfd
0124E349  call 0124E34F / add [esp],0x03FB7F18 / ret   -> jmp 0x05206266
0124E358  lea esp,[esp+4] ; push ebp ; push edi
0124E35E  call 0x05218191
05218191  lea esp,[esp+4]      ; discard the return address
05218195  jmp 0x05226A37       ; -> VM entry trampoline
```

The context save the trampoline needs at `[esp+0x20]` is **not pushed at one site** —
it is spread across several mutated blocks that run before the trampoline is reached.
(Measured: 300 sampled `.text` -> trampoline sites, longest contiguous push-run is
7 dwords; none reaches the 9 dwords the trampoline indexes.)

## 6. Classification of the 2,508 entries

Walking all 2,508 `.text` -> `.vmp1` call targets with a mutation-aware walker
(handles `call/add[esp]/ret`; 250-instruction budget; `_v1all.py`):

| feature reached | entries | % |
|---|---|---|
| one of the 41 VM-entry trampolines | 1,351 | 53.9% |
| a direct `call` into `.text` | 807 | 32.2% |
| a tail `jmp` into `.text` | 824 | 32.9% |
| a `ret` | 646 | 25.8% |
| an indirect `jmp reg` (walk stops) | 801 | 31.9% |

Shape of the first 40 instructions of each entry:

```
1,073  no ret/jmp inside 40 instructions (mutated body, walker gives up)
  639  reaches a VM-entry trampoline (most within 2-7 instructions)
  392  mutated (pushfd/mov/popfd or call$+5+1 idioms present)
  327  tail-jmp straight back out to .text
   76  plain, unobfuscated native code that returns in <=8 instructions
```

## 7. What I could not determine

* **Where the pointer stream `esi` walks lives.** For the first fetch the key is
  fixed (`0x4CC42998`), so a stored dword `D` must satisfy `D + 0x4CC42998` in
  `.vmp1`. Scanning all 30.5M dwords of the image (aligned and unaligned) gives
  33,864 candidates, **0.111% — below the uniform-random expectation** — and no
  cluster: densest 4 KB page is 261/1024 in `.text`, indistinguishable from code
  density. Same null result for keys `0x40BEAAB0` and `0x2437F6A4`. So the stream is
  either built at runtime outside the module image, or held encrypted (`.vmp0`,
  16 KB, and `.eI4`, 1.05 MB, are both still high-entropy in this dump). **I cannot
  say how long a dispatch chain is** — whether the trampoline is a one-shot encrypted
  indirect branch or the head of a long threaded chain.
* Consequently I cannot say whether the "handler" bodies between two dispatch
  epilogues implement a *virtual ISA* or are simply the program's own basic blocks.
  What I *did* read in those bodies (§3, §4 samples at `0x051DBC61`, `0x051DE0D2`,
  `0x051D3CA0`) is program-specific native code — real `.text` calls, real function
  epilogues, `call eax` — not generic stack-machine opcodes. That argues for "own
  basic blocks", but it is an inference from ~6 samples, not a census.
* The 12,253 branch count into the trampolines comes from a raw byte scan; a small
  number of hits may be at non-instruction offsets. The false-positive rate is
  negligible arithmetically but I did not verify each site.

---

## Measured vs inferred

**Measured** (reproducible from the numbers above):
* `.vmp1` is 7.80 MB of executable, unpadded, mutation-obfuscated x86.
* 22,768 direct branches from `.vmp1` into `.text`; 13,179 land exactly on known
  function entries (400x base rate); 746 distinct functions, 20 of them
  `__FILE__`-anchored.
* At least two `.vmp1` functions are plain unobfuscated compiled code with unique
  bodies (`0x0526AB41` x1143 callers, `0x05796EBE` x81).
* 41 byte-identical VM-entry trampolines; 12,253 direct branches into them.
* 407 dispatch epilogues in 75 byte-variants / 39 keys; `esi` = VIP, `edi` = per-thread
  relocation slot; strides 4/5/8.
* ~50% of the 18,506 `jmp reg` sites are get-EIP computed jumps.
* Idiom densities 30-70x `.text`; zero int3 padding; `.tls` matches `.vmp1`.

**Inferred**:
* The census found no convergence because the dispatcher is replicated and
  polymorphic and because dispatch is indirect — not because there is no dispatcher.
* `.vmp1` is a *relocated copy of program code*, not a bytecode arena: it calls the
  game's own functions, contains unique unobfuscated function bodies, and is entered
  by ordinary `call` from `.text` at 2,508+ points.
* This is VMProtect-family **Mutation** (the `.vmp0`/`.vmp1` section pair, the
  `fs:[0x18]` stack-commit entry, the constant-splitting and stack-based register
  moves are all its signature) with the *virtualization* layer used only for a subset
  of edges — not whole-program virtualization.

**What would falsify this**: locating the `esi` pointer stream and finding that its
records index a small fixed set of generic handlers (a real opcode table). Then the
`.vmp1` bodies would be VM handlers after all and §1's `.text` calls would have to be
re-read as handler-side helper calls. The dump does not contain that stream, so the
question stays open.

---

### Scripts written for this (all in SCRATCH)

`_v1dis.py` linear disassembly at any VA; `_v1rd2.py` mutation-aware recursive
walker; `_v1pat.py` / `_v1tr2.py` trampoline census and clustering; `_v1in.py`
branch-into-trampoline census (writes `_v1tr.pkl`); `_v1cls.py` entry shape
classification; `_v1all.py` walk-all-2508 (writes `_v1all.pkl`); `_v1jr.py`
`jmp reg` producer classification; `_v1h.py` dispatch-epilogue reader.
