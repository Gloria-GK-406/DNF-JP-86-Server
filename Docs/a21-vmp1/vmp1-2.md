# .vmp1 and the two .data pages 0x0399C000 / 0x03B89000

**Bottom line: the premise is wrong, and the correction is the useful part.**

Neither page is bytecode and neither is a key table.

* `0x0399C000` is the **MSVC CRT / Dinkumware STL static-data block** of the original
  binary. Its one hot cell, `0x0399CEE0`, is **`__security_cookie`** (with
  `__security_cookie_complement` at `0x0399CEE4`). That single global accounts for
  **39,120 of the 39,368** references the page receives image-wide.
* `0x03B89000` is **zero-fill BSS** — 4096/4096 zero bytes, on a page that the live
  process had **never written** (`PAGE_WRITECOPY`, still unbroken COW at dump time).

And `.vmp1` does **not** read either page heavily. Measured, from a flow-following
disassembly seeded at every `.text -> .vmp1` entry: **5** operands touch page A (all
five are the security-cookie prologue) and **0** touch page B.

The pages `.vmp1` actually reads hard are elsewhere, and one of them *does* change how
`.vmp1` should be attacked — see §5.

---

## 1. What is at 0x0399C000 (MEASURED)

Page content: 2,567 of 4096 bytes non-zero. `re.finditer(rb'[ -~]{5,}')` over the page
returns 43 strings, every one of them an MSVC RTTI type descriptor or a CRT artifact:

```
0399C3F4  '.?AVinvalid_argument@std@@'
0399C418  '.?AVerror_category@std@@'
0399C43C  '.?AV_Generic_error_category@std@@'
0399C4DC  '.?AVbad_function_call@tr1@std@@'
0399C508  '.?AVregex_error@tr1@std@@'
0399C530  'Copyright (c) 1992-2004 by P.J. Plauger, licensed by Dinkumware, Ltd. ALL RIGHTS RESERVED.'
0399C630  '.?AV?$time_put@_WV?$ostreambuf_iterator@_WU?$char_traits@_W@std@@@std@@@std@@'
0399CE80  '.?AVbad_typeid@std@@'
0399CEA0  '.?AV__non_rtti_object@std@@'
0399CEC4  '.?AVtype_info@@'
```

Each descriptor is preceded by the dword `0x03688AA4` — the shared `type_info` vftable
pointer, i.e. these are real `_TypeDescriptor` records, not loose strings.

The hot cell:

```
0399CEE0  4E E6 40 BB   B1 19 BF 44   ...
          ^ 0xBB40E64E  ^ 0x44BF19B1
          0xBB40E64E XOR 0x44BF19B1 == 0xFFFFFFFF   (exact ones-complement pair)
```

Reference distribution inside the page (image-wide dword scan, all 122,085,376 bytes):

| target | refs |
|---|---|
| `0x0399CEE0` | **39,120** |
| `0x0399CD34` | 12 |
| everything else (142 addrs) | 236 |
| **page total** | 39,368 |

Six randomly sampled `.text` reference sites (seed 1 of the 39,120):

```
00BF8AA3 : push edi | mov eax, dword ptr [0x399cee0] | xor eax, ebp
02C40146 : push edi | mov eax, dword ptr [0x399cee0] | xor eax, ebp
01302166 : push edi | mov eax, dword ptr [0x399cee0] | xor eax, ebp
00ADD3A3 : push edi | mov eax, dword ptr [0x399cee0] | xor eax, ebp
0271B796 : push edi | mov eax, dword ptr [0x399cee0] | xor eax, ebp
007A3583 : (mid-instruction, false positive)
```

**INFERRED:** `0x0399CEE0` = `__security_cookie`, `0x0399CEE4` =
`__security_cookie_complement`. `0xBB40E64E` is MSVC's compile-time default cookie for
x86 and `0x44BF19B1` its default complement; the `mov eax,[cookie]; xor eax,ebp`
prologue is the canonical `/GS` frame-guard sequence. Confidence: high.

**Base rate — this is the point.** 29,303 of the 67,074 catalogued `.text` functions
(**43.7%**) reference this page. It is the single most-referenced global in the image.
"Function X touches 0x0399C000" carries essentially zero information; it means the
function was compiled with `/GS`, which nearly half of them were.

**INFERRED (secondary):** the cookie still holds its *compile-time default* at dump
time, so `__security_init_cookie` had not randomized it yet. Combined with §2 this
places the dump very early — the VMProtect unpacker had restored `.data` from its
compressed copy, but CRT/global construction had barely started. Confidence: medium.
(Alternative not excluded: the packer pins it.)

## 2. What is at 0x03B89000 (MEASURED)

The page is **entirely zero** — 0/4096 non-zero bytes. It sits inside one contiguous
zero run:

```
zero run: 0399FFA4 .. 04E5D000   (21,745,756 bytes)
```

That is not a dump hole. `manifest.json` reports `hole_bytes: 0`,
`failed_blocks: 0`, `skipped_regions: 0`, `bytes_written: 122085376`. The zero run's
start aligns to a protection boundary in `DNF-pages.csv`:

```
0x0395E000,0x039A0000, ... ,protect=0x00000004, ok    <- PAGE_READWRITE  (COW broken = written)
0x039A0000,0x04E27000, ... ,protect=0x00000008, ok    <- PAGE_WRITECOPY  (never written)
0x04E27000,0x04E28000, ... ,protect=0x00000004, ok
0x04E28000,0x04E5D000, ... ,protect=0x00000008, ok
```

And `.data` has **no file backing at all** — from the PE section table,
`.data va=0355E000 vs=014CC000 raw=00000000 praw=00000000`. Raw size zero. Every
non-zero byte in `.data` was written at runtime.

Chain: `.data` loads zero-filled -> the region containing `0x03B89000` is still
`PAGE_WRITECOPY` -> nothing ever wrote it -> it is zero. Three independent measurements
(byte content, protection flag, section raw size) agree.

Its heaviest cells and how `.text` uses them:

```
03B8967C  x349     03B89680  x344     -- adjacent dwords, always loaded as a pair
03B89618  x28                         -- singleton object pointer
03B89BB8  x42 (+BBC/BC0/BC4/...)      -- a global C++ object instance
```

```
004EDD58  mov ecx, dword ptr [0x3b8967c]
004EDD64  sub esp, 8
004EDD67  mov eax, esp
004EDD6C  mov dword ptr [eax], ecx
004EDD6E  mov edx, dword ptr [0x3b89680]
004EDD74  push 1
004EDD76  push 0xea9b
004EDD7B  mov dword ptr [eax + 4], edx
004EDD7E  call 0x28c14c0              ; literals: "getStr(%d) == none", "Fatal Error"
```

```
027BB97D  mov esi, dword ptr [0x3b89618]      ; NULL at dump time
027BB98D  mov ecx, dword ptr [esi + ecx*4 + 0x24]
027BB997  mov dword ptr [ecx + eax*4 + 0x2114], edx

027C2DAC  mov ecx, 0x3b89bb8                  ; `this` for a global object
027C2DB1  call 0x27bbbe0
```

**INFERRED:** `0x03B89000` is a page of ordinary zero-initialised C++ globals —
an 8-byte struct passed by value into the localized-string getter at `0x028C14C0`, a
manager singleton pointer, a global object instance. Confidence: high for "zero-init
globals", medium for the individual semantic labels.

Base rate check: 294 distinct `.text` functions reference the page, and the five that
carry a `__FILE__` literal name five *unrelated* source files —
`PopupWindow\ResponsePeerWindow.cpp`, `PassiveObject\CNBoxDestroyManager.cpp`,
`PopupWindow\RandomBoxSystem\RandomBoxItemModePT.cpp`, `CNRDBaseInterface.cpp`,
`PopupWindow\China\EventNoticeWindowEx.cpp`. No single owning module. It is a linker
bucket, not a subsystem.

## 3. Does .vmp1 actually read these pages? No. (MEASURED)

**Method A — exhaustive byte scan.** Every one of the 8,175,616 `.vmp1` bytes, read as
an unaligned little-endian dword (a superset of *any* absolute-address encoding):

| | page A `0399C000` | page B `03B89000` |
|---|---|---|
| `.text` | 39,206 | 1,136 |
| `.code` | 108 | 1 |
| **`.vmp1`** | **10** | **1** |
| other sections | 44 | 30 |

**Method B — flow-following disassembly, the reliable one.** Seeded at every direct
`.text -> .vmp1` target (3,599 distinct `E8` + 5,059 distinct `E9` = 8,288 seeds; note
the brief's figure was 2,508, a different dedup), following intra-`.vmp1` `call`/`jcc`
and chaining through `jmp`. 560,508 instructions decoded. Counting only genuine
mod/rm `disp32` operands with no base and no index register:

| section | absolute-mem operands |
|---|---|
| `.tls` | **361** |
| `.code` | 178 |
| `.text` | 117 |
| `.data` | 94 |
| `.vmp1` | 26 |

Of the 94 `.data` operands, the complete page-A set is five instructions, and page B is
empty:

```
051F9591  mov eax, dword ptr [0x399cee0]     ; -> xor eax, ebp
0570D2E2  mov eax, dword ptr [0x399cee0]     ; -> xor eax, ebp
057087F1  mov eax, dword ptr [0x399cee0]     ; -> xor eax, ebp
0579C1CA  push dword ptr [0x399cee0]         ; -> pop eax; ... xor eax, ebp
0579F561  push dword ptr [0x399cee0]         ; -> pop eax; xor eax, ebp

page B 03B89000 : 0
```

The single page-B "hit" from Method A, at `0x05424003`, is not an operand at all —
it is the `E8` opcode byte of `call 0x545F8A3`.

**Why Method A over-reports and Method B does not.** The `.vmp1` raw scan appears to
find 186,240 `.data`-range dwords, but they are instruction bytes, not addresses. The
top "target" is `0x0424648D` with 25,668 hits — those bytes are `8D 64 24 04`, i.e.
`lea esp, [esp+4]`, VMProtect's stack-adjust filler. Same for `0x0424A48D`
(`8D A4 24 04`, 9,015 hits) and `0x03C0339C` (`9C 33 C0 03`, `pushfd; xor eax,eax`,
1,857 hits). Likewise the twenty apparent "RVA-form" references to page A are junk
immediates inside mutated instructions — `push 0x1f037899`,
`lea ecx, [ecx - 0x555bbd6c]`.

**Any claim that `.vmp1` reads these two pages heavily is an artifact of a raw dword
scan over mutated code.** That is almost certainly where the premise came from.

## 4. Limits — what this does NOT prove

* Method B visited 567,532 of 8,175,616 `.vmp1` bytes (~7%). Obfuscated indirect jumps
  terminate the walk. It covers entry prologues and straight-line stretches, not the
  whole section. Method A does cover 100% of the bytes, which is why both are reported.
* **Neither method can see a runtime-computed address.** If `.vmp1` contains virtualized
  handlers whose operands come from an encrypted bytecode stream, a data address can be
  reached without its bytes ever appearing in `.vmp1`. I did not decrypt any VM
  bytecode. "No evidence of heavy reads" is not "provably zero reads."
* `.vmp1` entropy is flat at ~7.35–7.48 across all 125 64KB blocks. No high-entropy
  bytecode blob is separable from mutated code by entropy alone. I did **not** locate a
  VM bytecode stream or a handler dispatch table; that remains **unknown**.
* Entry `0x057131CC` opens `push 0xfea3fb4a; push ecx; push edx; push ebx; jmp <far>`,
  which is the classic VMProtect VM-entry shape, so virtualized handlers probably exist
  somewhere. Not confirmed, not located.

## 5. What .vmp1 does read — and this one is actionable

The 361 `.tls` operands are not scattered. They cluster in one block at `0x0513E000`,
and every top entry is an **indirect call**:

```
0513E220  x29   call dword ptr [0x513e220]
0513E230  x24   call dword ptr [0x513e230]
0513E228  x16   call dword ptr [0x513e228]
0513E1BC  x16   call dword ptr [0x513e1bc]
```

Resolved against the dump's `DNF-iat.csv` (420 slots, `iat_matches: 420`), that block
is the **rebuilt import table**:

```
0513E1B4 -> 75A0E770  kernel32.dll!CloseHandle
0513E1BC -> 75A0D5F0  kernel32.dll!GetModuleHandleA
0513E220 -> 75A06780  kernel32.dll!GetProcAddress
0513E224 -> 771042D0  kernel32.dll!EnterCriticalSection
0513E228 -> 77103350  kernel32.dll!LeaveCriticalSection
0513E230 -> 75A0DBE0  kernel32.dll!GetTickCount
0513E41C -> 7C3426C3  msvcr71.dll!strncpy
0513E424 -> 7C342151  msvcr71.dll!free
0513E428 -> 7C3416E9  msvcr71.dll!malloc
0513E09C -> 75A21CB0  kernel32.dll!LoadLibraryA
```

Reachable `.vmp1` code hits **58 distinct named APIs across 229 operand sites**:

```
GetProcAddress x29   GetTickCount x25   LeaveCriticalSection x17   GetModuleHandleA x16
EnterCriticalSection x13   malloc x9   CloseHandle x9   strncpy x7   LoadLibraryA x7
free x6   wcslen x6   memmove x5   strncat x4   FreeLibrary x4   GetFileSize x3
ReleaseMutex x3   SetFilePointer x3   GetCurrentProcessId x3   WaitForSingleObject x3
IsBadReadPtr x3   _snprintf x3   GetCurrentProcess x3   GetLastError x3   SetLastError x3
OpenProcess x2   CreateFileA x2   FindFirstFileA x2   MapViewOfFile x1   Sleep x1   ...
```

The remaining bulk is a **read-only float/SSE constant pool** in `.code` at
`0x03043000`–`0x0304B000`, touched only by FP loads and never written:

```
0538EB70  fadd  dword ptr [0x304396c]        053C15BC  movss  xmm0, dword ptr [0x304396c]
053A9AA2  subss xmm0, dword ptr [0x30440e8]  0534E790  addss  xmm2, dword ptr [0x30440e8]
052C7E1D  andpd xmm3, xmmword ptr [0x3045ad0]
```

(`andpd` against a 16-byte constant is the standard `fabs`/sign-mask idiom.)

**So the correct answer to "does it change how .vmp1 should be attacked":** yes, but the
lever is `0x0513E000` in `.tls`, not either `.data` page. Import slots are named, so
every `call dword ptr [0x513Exxx]` inside `.vmp1` is a free semantic anchor in code that
has none — 229 of them. Cross-referencing which `.vmp1` entry reaches which API gives a
first partition of the 8.2MB by function (file I/O, mutex/anti-tamper, dynamic API
resolution, timing) without decoding a single obfuscated instruction. `GetTickCount x25`
and `IsBadReadPtr x3` in particular are timing/anti-debug tells.

## 6. What would falsify this

Set a hardware read watchpoint on `0x0399CEE0` and on `0x03B89000` in the live client
and record EIP at each hit. If a material fraction of EIPs land in
`0x051BF000`–`0x0598B000`, my conclusion is wrong and the reads are being issued from
runtime-computed addresses that static analysis cannot see (§4). For page B specifically,
also re-dump a client that has reached the town/in-game stage: if `0x03B89000` is
non-zero there, its "empty" state is a property of *this* early dump, not of the page.

---

### Provenance

* Image: `DNF-memory-rva.bin`, pid 26888, base `0x00400000`, 122,085,376 bytes,
  `holes=0 failed_blocks=0 skipped_regions=0`, `analysis_only: true`,
  `pre_feature_plugins: true`, `early_antidebug_active: true`.
* Section bounds from the on-disk PE header of `DNF.exe`; page protections from
  `DNF-pages.csv`; import resolution from `DNF-iat.csv` (420/420 matched).
* Scripts written this run: `_vw.py` (flow-following operand census, output `_vw.pkl`),
  `_w32.npy` (unaligned dword view of the whole image), `_vmp1_e8.pkl` (entry seeds),
  `_pg_A.pkl` / `_pg_B.pkl` (per-page reference indices). All in SCRATCH.
