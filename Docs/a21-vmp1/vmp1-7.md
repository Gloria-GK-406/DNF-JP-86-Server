# `.vmp1` vs. the VMProtect 2.x/3.x layout playbook

**Target.** DNF S4A21 client, memory dump `DNF-memory-rva.bin` (122,085,376 B),
image base `0x00400000`; on-disk PE `DNF.exe` (28,398,592 B).
`.vmp1` = VA `0x051BF000`-`0x0598B000` (8,175,616 B). `.vmp0` = `0x051BB000`-`0x051BF000` (16,384 B).

**Method.** Everything marked *measured* came out of scripts in
`...\scratchpad\v7\` run against the dump this session (PE parse, byte-pattern
census, capstone tracing, and **Unicorn emulation of 282 real VM entries**,
1.45 M instructions). *Inferred* is my reading on top of the numbers.

**Headline.** There is a genuine VMProtect-family VM here - I recovered its
entry, its key schedule, its dispatcher and 774 handler entry points. But of
19 textbook layout predictions, **7 hold, 3 hold partially, 8 fail, 1 unknown**.
The failures cluster on one axis: *nothing about this VM is confined to the
`.vmp` sections, and nothing about it is polymorphic.*

---

## Scoreboard

| # | Standard VMProtect 2.x/3.x prediction | Verdict |
|---|---|---|
| P1 | Protector emits `.vmp0` + `.vmp1`, RWX | **HOLDS** |
| P2 | PE entry point redirected into a `.vmp` section | **FAILS** |
| P3 | Base relocations removed / `.reloc` folded in | **HOLDS** |
| P4 | Protector installs a TLS callback for early anti-debug | **FAILS** |
| P5 | Virtualized function body replaced in place in `.text` by `push key; call vm_entry` | **FAILS** |
| P6 | VM entry = `push imm32` + individually-pushed GPRs + `pushfd`, then call | **HOLDS (exactly)** |
| P7 | Position independence via `call $+5; pop r; sub r, self_va` | **HOLDS** |
| P8 | VIP derived from the pushed key by a rotate/add chain | **HOLDS** |
| P9 | Dispatch = decrypt opcode **byte**, index a **handler table** | **FAILS** |
| P10 | Per-protected-function polymorphism (own keys, own handler permutation) | **FAILS** |
| P11 | Handler bodies live inside the `.vmp` section | **FAILS** |
| P12 | Bytecode lives inside the `.vmp` section | **PARTIAL** |
| P13 | VIP walks in one fixed direction | **HOLDS (forward)** |
| P14 | VM register file is a stack frame addressed off `ebp`/`esp` | **PARTIAL** |
| P15 | Imports destroyed; API calls routed through protector thunks | **FAILS** |
| P16 | `.vmp1` is uniformly high-entropy encrypted bytecode | **FAILS** |
| P17 | Anti-debug / anti-tamper primitives concentrated in the `.vmp` sections | **FAILS** |
| P18 | No `VMProtect` marker strings survive | **HOLDS (uninformative)** |
| P19 | `.vmp0` holds packed originals / import descriptors | **UNKNOWN** |

---

## What I recovered (the VM itself)

### Canonical VM entry - `0x051FD53D`, 83 bytes

*Measured*, byte-for-byte:

```
051FD53D  58                 pop  eax
051FD53E  2D 3D D5 1F 05     sub  eax, 051FD53D      ; eax = image delta
051FD543  64 8B 3D 18000000  mov  edi, fs:[0x18]     ; TEB
051FD54A  8B 7F 08           mov  edi, [edi+8]       ; NtTib.StackLimit
051FD54D  8B CC / 2B CF      mov  ecx,esp / sub ecx,edi
051FD54F..051FD567           guard-page probe loop, 0x1000 at a time,
                             until esp-edi > 0x4000
051FD569  89 07 / 89 47 04   mov [edi],eax / mov [edi+4],eax   ; stash delta
051FD56E  8B 74 24 20        mov  esi, [esp+0x20]    ; the pushed key
051FD572  C1 C6 09           rol  esi, 9
051FD575  81 C6 E6AC81BD     add  esi, 0xBD81ACE6
051FD57B  03 F0              add  esi, eax           ; esi = VIP
051FD57D  33 ED / 33 DB      xor  ebp,ebp / xor ebx,ebx
051FD581  8B 1E              mov  ebx, [esi]         ; <-- DISPATCH
051FD583  83 C6 04           add  esi, 4
051FD586  81 C3 9829C44C     add  ebx, 0x4CC42998
051FD58C  03 1F              add  ebx, [edi]
051FD58E  FF E3              jmp  ebx
```

Reached from `.text` like this (*measured*, stub `0x051BF7D9`, called from
`0x01A85371` and `0x01A859CC`):

```
051BF7D9  push 0xD123CF7B
051BF7DE  push ecx / push edx / push ebx / push eax / push esi
051BF7E3  call 05207472        -> lea esp,[esp+4]  (discard ret addr)
05207476  pushfd / push ebp / push edi
05207479  jmp 051E84A0 -> jmp 051FD538 -> call 051FD53D
```

The save order is invariant across all 282 entry stubs:
`key, ecx, edx, ebx, eax, esi, EFLAGS, ebp, edi` - 9 dwords, `esp` excluded,
key at `[esp+0x20]`. **P6 and P7 hold exactly as written in the playbook.**

### The key schedule verifies arithmetically

*Measured.* Taking `delta = 0` (no reloc directory, so fixed base) and computing
`VIP = rol(key,9) + 0xBD81ACE6`, `handler0 = [VIP] + 0x4CC42998` for the 282
`push imm32` stubs: **235/258 VIPs land inside `.vmp1`**, and the 258 decoded
first-handlers collapse onto only **32 distinct addresses**
(`0x01705B33` x22, `0x05275444` x18, `0x052B27DE` x17, `0x05731CF2` x16, ...).
Random arithmetic does not converge like that. The model is correct.

### A decoded handler, and the shape of a bytecode instruction

*Measured*, handler `0x05275444` traced under emulation:

```
05275444  mov bl,bl                    ; junk
05275446  xor eax,eax
05275448  mov al, byte [esi+4]         ; 1-byte operand = VM register index
0527544E  call 01A935EA  --> .text     ; lea esp,[esp+4]; shl eax,2
0526BB2A  add eax, edi                 ; edi = VM context
0526BB2C  pop dword [eax]              ; VM_POP_REG
0526BB2E  lodsd                        ; next handler ptr, esi += 4
0526BB2F  add esi, 1                   ; step past the operand -> +5 total
0526BB35  add eax, [edi]               ; + image delta
0526BB37  call 0199B79D  --> .text
0199B7A1  add eax, 0x5B7DF9F4          ; per-handler bias, NOT 0x4CC42998
0199B7A7  jmp eax                      ; next handler
```

Stream layout is `[next-handler ptr : 4][operand : 0..n]`, i.e. the pointer for
step *k+1* sits ahead of step *k*'s operand.

*Measured* VIP deltas over 60 emulated VM functions (3,417 real steps):
`+5` x1889, `+4` x549, `+8` x324, `+12` x223, `+9` x151, `+6` x41, `+10` x37, ...
3,372 forward vs 45 backward (the 45 are VM branches, magnitudes 1e4-1e7).
**P13 holds, direction = forward.** Also *measured*: across those runs there
were **0** `jmp reg` sites where `esi` had not advanced - i.e. every indirect
jump in the VM is a real dispatch, none is intra-handler obfuscation.

### P14 - the register file is not an `ebp` frame  **PARTIAL**

*Measured.* `edi` is the VM context pointer and it is derived from
`fs:[0x18] -> [edi+8]` (TEB `NtTib.StackLimit`), then walked down by the guard-page
probe until `esp - edi > 0x4000`. `[edi]` holds the image delta; the register
file is `[edi + idx*4]` (`shl eax,2; add eax,edi; pop dword [eax]`).
So the VM context does live on the thread stack, but it is anchored to
StackLimit via the TEB, not to `ebp` or a protector-allocated block.

---

## The eight failures, with the numbers

### P9 - there is no handler table, and no opcode byte  **FAILS**

*Measured.* Dispatch reads a **32-bit** value and jumps to it after adding a
bias and the image delta (`0x051FD581`). Nothing is indexed.

Confirmation: I collected the 774 handler entry addresses observed under
emulation and searched the entire 122 MB image for each as a little-endian
dword. **758 of 774 never appear anywhere.** The 16 that do appear are
scattered singletons across `.text` / `.#|U` / `.code`, and there are **zero**
clusters of >=6 handler dwords within 0x40-byte spacing. There is no
`VmHandlersTable` in this image.

*Inferred:* this is direct threading. The bytecode *is* a chain of biased
handler addresses. Every classic VMProtect unpacking technique that starts by
locating the handler table is inapplicable here.

### P10 - the VM is not polymorphic  **FAILS**

*Measured.* Four independent signatures of the entry
(`64 8B 3D 18000000 8B 7F 08`; `C1 C6 imm8 81 C6 imm32`;
`8B 1E 83 C6 04 81 C3 imm32 03 1F FF E3`; the raw dwords `BD81ACE6` and
`4CC42998`) each return **exactly 53 hits**, at the same 53 addresses.
Normalising only the self-referential `sub eax, imm32`, the 83-byte bodies hash
to **one** value across all 53. `rol` amount = 9 in all 53. `K1` = `0xBD81ACE6`
in all 53. `K2` = `0x4CC42998` in all 53. Key slot = `[esp+0x20]` in all 53.

One VM design, one key set, 53 clones. Key diversity exists only in the
`push imm32` (282 distinct keys) and in the per-handler tail bias
(e.g. `0x5B7DF9F4` at `0x0199B7A1`).

Handler bodies *are* duplicated-and-mutated rather than shared: the 774 handler
entries have 196 distinct 8-byte prefixes, 562 distinct 16-byte prefixes,
743 distinct 32-byte prefixes (largest 8-byte cluster: 144 members).

### P11 - handlers are not confined to `.vmp1`  **FAILS**

*Measured*, from 262 emulated VM invocations:

| | `.vmp1` | `.text` | `.rsrc` (`0x04F69000`) | other |
|---|---|---|---|---|
| handler entry points (774) | 679 | 85 | 9 | 1 |
| dispatch tails (566) | 477 | 84 | 5 | - |

Hot examples outside `.vmp1`: handler `0x0236649F` (`.text`) executed 445x,
`0x022FF6B7` (`.text`) 128x, `0x04F69924` (`.rsrc`) 50x, `0x04F69585`
(`.rsrc`) 35x. Handler tails in `.text` too - `0x0199B7A1 add eax,0x5B7DF9F4;
jmp eax` is a dispatch tail sitting in `.text`.

Note the `.rsrc` at `0x04F69000` has characteristics `E00000E0` - RWX. Section
names are camouflage, and VM handler code executes out of a "resource" section.

The 53 entry clones themselves: **6 in `.text`, 6 in `.tls`, 41 in `.vmp1`**,
and 33 of them are used by stubs called from `.text`
(26 `.vmp1`, 6 `.text`, 1 `.tls`).

> Correction to the earlier `vmp1-7.md` in this directory, which said the 53
> clones sit in `.text/.tls/.rsrc/.vmp1`: **none of the 53 entry clones is in
> `.rsrc`.** `.rsrc` holds 9 *handler bodies*, not entries.

### P12 - bytecode is mostly but not only in `.vmp1`  **PARTIAL**

*Measured.* Of 258 decodable VIPs: 235 `.vmp1`, 22 `.text`, 1 `.rsrc`.
18,190 VIP observations under emulation span `0x051BF5CE`-`0x057984F4`,
touching 462 distinct 4 KB pages - **410 of which are also executed as code**.
Bytecode and handler code are interleaved inside the same pages.

### P5 - the `.text` bodies were never replaced in place  **FAILS**

This is the biggest structural deviation and it changes how you attack the file.

*Measured.* `.text` contains 6,304 `call rel32` sites reaching 3,599 distinct
`.vmp1` addresses, plus 6,846 `jmp rel32` sites reaching 5,059 more.
Base-rate control (same 8,175,616-byte window size, same scan):

| window | E8 sites | distinct targets | sites/target |
|---|---|---|---|
| `.vmp1` `051BF000` | **6304** | **3599** | **1.75** |
| `.data` `0395E000` | 1142 | 1142 | 1.00 |
| `.code` `03029000` | 1726 | 1726 | 1.00 |
| `.#\|U` `05D8C000` | 830 | 829 | 1.00 |
| unmapped `08000000` | 276 | 276 | 1.00 |

Noise never repeats a target; `.vmp1` targets do. Independently, 2,255 of the
3,599 targets begin with `8D 64 24 04` (`lea esp,[esp+4]`) and 261 begin with
`68` (`push imm32`) - 70 % carry a protector-specific prologue where chance
would give ~14 per byte value. Under linear disassembly of the owning function,
62.8 % of a 400-function sample of sites land on a real instruction boundary
(the rest are linear-disassembly derailments at jump tables, not evidence
against the sites).

Now the shape of the call site (*measured*, `0x00E12700`, a function whose
`__FILE__` is `DNFClient\RDAR\CNBossTowerModule.cpp`):

```
00E12B3E  mov  esi, eax
00E12B40  mov  ecx, esi
00E12B42  call 052734C1        <<< ordinary __thiscall into .vmp1
00E12B47  call dword [3029608]
...
00E12B8B  call 052C51AD        <<< again
00E12BE8  ret  4
```

The caller is untouched MSVC. The **callee's original `.text` address is gone**
and every call site was rewritten to point straight into `.vmp1`. VMProtect
does the opposite: it keeps the function at its original RVA and overwrites the
body with a stub. Consequences:

- 4,648 of 67,074 `.text` functions (6.93 %, 26.5 % of `.text` bytes) contain at
  least one transfer into `.vmp1`; median span 500 B vs 224 B overall.
- 130 of the 759 `__FILE__`-bearing ground-truth functions (17.1 %),
  1,110 of 8,798 literal-bearing functions (12.6 %), and **38 of the 459
  parsers** call into `.vmp1`.
- Of the 3,599 `.text`-called `.vmp1` targets, **1,783 funnel into one of 33
  `vm_entry` copies** by static jump-following (an undercount - the chain
  crosses opaque conditionals my follower stops at); the remaining ~1,816 are
  mutation-only native entry points.

*Inferred:* the protector had full relocation/symbol knowledge and did
call-site rewriting, which a drop-in PE protector cannot do. Either the
protection ran inside the build, or a later stage rewrote the call graph.

### P15 - imports are not hidden  **FAILS**

*Measured.* 1,125 `call/jmp dword ptr [imm32]` sites inside `.vmp1` resolve to
named IAT slots (420 slots total, living in `.code` and `.tls`), covering
**122 distinct APIs**: `GetProcAddress` x195, `GetModuleHandleA` x66,
`GetTickCount` x66, `LeaveCriticalSection` x60, `LoadLibraryA` x43,
`msvcr71!malloc`/`free` x34 each, `OpenProcess` x7, `OpenThread` x7,
`VirtualProtect` x6, `GetThreadContext` x2, ...

Concrete: `0x058808D4  call dword [0513E1BC]  ; kernel32!GetModuleHandleA`.
No import obfuscation whatsoever inside the "protected" section.

### P16 - `.vmp1` is bimodal, not uniform  **FAILS**

*Measured*, 64 KB entropy windows plus three independent locators:

| | `0x051BF000`-`~0x057C0000` (6.0 MB) | `0x057C0000`-`0x0598B000` (1.8 MB) |
|---|---|---|
| entropy | 7.20 - 7.48 | 6.18 - 7.02 |
| dominant bytes | `D3`, `A9`, `00` | `00`, `FF`, `24` |
| VM handler entries | 679 | 0 |
| bytecode addresses observed | 18,190 (max `0x057984F4`) | 0 |
| IAT call sites | 3 | **1,084** |
| pages executed under emulation | 1,154 / 1,537 (75 %) | 1 / 459 |
| `lea esp,[esp+4]` per KB | 3.21 | 3.24 |

The high region is *mutated native x86*, same mutation engine (identical
`lea esp` density), calling Win32 through a plain IAT - e.g. `0x05880874`
onward: split constants (`mov eax,0x349B702C; lea eax,[eax-0x698BAA24]`),
`lea esp,[esp-6]` frame games, opaque jumps, then a clean
`call [kernel32!GetModuleHandleA]` at `0x058808D4`. Only 23 ASCII runs of >=8
chars exist in the whole 1.8 MB - its literals are encrypted like the rest of
the client's.

13 of the 53 `vm_entry` clones do sit above `0x057C0000`, but **none of the 282
`.text`-reachable entry stubs does**, so the high region's VM usage is internal
and I did not exercise it.

### P2, P4, P17 - the protector-shell predictions all fail

*Measured* from the PE header:

- `AddressOfEntryPoint` RVA `0x07213FBC` -> VA `0x07613FBC`, inside `.#|U`
  (the outer packer), **not** in `.vmp0`/`.vmp1`. **P2 fails.**
- TLS directory at VA `0x06E673D8`: `AddressOfCallBacks = 0`. There is no TLS
  callback at all. **P4 fails.**
- Anti-debug census by section - `rdtsc`: `.#|U` 563, `.-wI` 205, `.text` 56,
  `.vmp1` **36**. `int 2D`: `.#|U` 406, `.text` 55, `.vmp1` **14**.
  `cpuid`: `.#|U` 532, `.vmp1` **14**. `IsDebuggerPresent` referenced only from
  `.text` (x2). The whole `CreateToolhelp32Snapshot` / `Process32*` /
  `Module32*` / `Thread32*` enumeration cluster is in **`.tls`**, one site each.
  **P17 fails** - the tamper checks live in the outer packer and in `.tls`.

### The predictions that do hold

- **P1.** Sections are named `.vmp0` (`0x051BB000`, 16 KB, chars `E0000060` =
  CODE|INIT_DATA|RWX) and `.vmp1` (`0x051BF000`, chars `E0000020` = CODE|RWX).
  Standard naming, standard characteristics.
- **P3.** DataDirectory[5] (basereloc) is `0/0`. No relocations. The VM computes
  a delta anyway and adds it at every dispatch - the textbook belt-and-braces.
- **P18.** Zero occurrences of `VMProtect` / `vmprotect` in 122 MB. (The only
  `vmp0`/`vmp1` byte hits are the section-header strings at RVA < 0x1000.)
  Expected, therefore uninformative.

### P19 - `.vmp0` is unexplained  **UNKNOWN**

*Measured.* 16,384 B, 8,316 non-zero, all of them in the first `0x2181` bytes,
byte histogram flat (top non-zero values `0x80` x126, `0x40` x114, `0xC0` x111 -
a 32-value-spaced comb). Entropy 4.93 counting the zero tail. Zero `vm_entry`
signatures. Two `E8` sites from `.text` reach it (both at noise level). One IAT
reference. **Not** a handler table (no clustered handler dwords), and not code I
could reach. I have no evidence for what it is; say unknown.

---

## Two facts that reframe everything above

**A. The `.vmp` sections do not exist in the shipped file.** *Measured*: of 15
sections, only three carry raw file bytes - `."2A` (`rawptr 0x400`, 0x33800 B),
`.#|U` (`rawptr 0x33C00`, 27.9 MB, entropy 7.998) and the second `.rsrc`
(`rawptr 0x1ACF400`). `.text`, `.code`, `.data`, `.rdata`, `.eI4`, the first
`.rsrc`, `.tls`, `.vmp0`, `.vmp1`, `.bss`, `.mackt`, `.-wI` all have
`PointerToRawData = SizeOfRawData = 0`. The entry point is inside `.#|U`.

*Inferred:* the VMProtect-shaped output was itself re-packed. `.vmp1` is
materialised in memory at load time by the outer packer. Every observation in
this note is only obtainable from the dump - a static tool on `DNF.exe` sees an
8 MB hole where `.vmp1` should be.

**B. The existing analysis corpus has zero `.vmp1` coverage.** *Measured*: all
67,074 rows of `functions.csv` are in `.text`; `callgraph.csv`'s 526,525 edges
contain **0** edges whose callee is in `.vmp1`. So `parsers.csv` - advertised as
the complete parse surface - is complete only with respect to `.text`, and 38 of
its 459 parsers demonstrably call into `.vmp1`.

*Inferred:* "complete parse surface" is a claim about `.text`, not about the
client. Whether any read primitive is reachable from inside the VM is not
something I measured, and it should not be assumed either way.

---

## Reproduce

Scripts in `C:\Users\10239\AppData\Local\Temp\claude\D--project-DNF\1c50f207-441e-45a6-8f17-0f881c4703e0\scratchpad\v7\`:
`a.py` PE header - `b.py`/`c.py` entropy - `e.py` `.text`->`.vmp1` rel32 scan -
`g.py` stub walker - `j.py`/`n.py` signature census - `l.py` 53-clone identity -
`m.py` back-edge test - `o.py`/`p.py` key schedule - `q.py` handler-table test -
`r.py`/`s.py`/`z.py` IAT - `v.py`/`w.py`/`x.py` `.text` coverage - `y.py`
base-rate control - `ae.py` entry funnelling - `emu3.py`/`ab.py`/`ac.py`/`af.py`
Unicorn emulation.
Needs `unicorn` (pip-installed this session) and `capstone`. Note: a stale
`C:\Users\10239\AppData\Local\Temp\glob.py` shadows the stdlib - run scripts
from the scratchpad, never with cwd on `/tmp`.

## What would falsify this

- **P9 (no handler table):** find a contiguous array of >=16 dwords that, after
  one constant bias, all land on executable addresses reached by `jmp reg`.
  I searched only for the un-biased form; a table biased by an unknown constant
  would have escaped me.
- **P5 (call-site rewriting):** show that some `.vmp1` call target is also
  present, byte-identical, at a `.text` address that other code still calls -
  that would make it relocation, not replacement.
- **P10 (not polymorphic):** find a 54th VM entry using a different `rol`
  amount or different `K1`/`K2`. All four of my signatures key on the same
  83-byte body; a genuinely re-generated entry would match none of them.
- **P16 boundary at `0x057C0000`:** emulate entries reached only from inside the
  high region (13 clones sit there) and see whether bytecode appears above it.
- **P12/P11 counts:** they come from 262 emulated invocations that stopped on
  the first unmapped access (214 of 282 runs). A fuller harness would raise the
  handler count and could move the `.text`/`.vmp1` split.
