# boundary-1: the non-`.text` executable sections of DNF S4A21

Image base `0x00400000`. Bytes from `DNF-memory-rva.bin`; headers from `DNF.exe`.
Rerun 2026-08-25. Supersedes the 2026-08-24 version of this file, which was right
about the headline and **wrong** about the msvcp60 premise (see "Premise" below).

---

## Headline

**No. `.tls` carries zero `__FILE__` literals, and zero literal-pool pointers of any
kind. Neither does `.bss`, `.rsrc`@04F69000, or `.vmp0`. `.tls` must NOT be named the
way `.text` is.**

`.tls` is a **statically linked third-party runtime**: SQLite 3 + Crypto++ + zlib 1.2.8
+ RSA `md5.c` + **Tencent TDR**, wrapped around a machine-fingerprint / environment-scan
API set, linked against **msvcp60** for `std::string`. Its strings are **plaintext**;
the game's are encrypted. It has its **own private RTTI block**. It is a different
codebase that happens to share an address space with the game.

---

## 1. The literal test

`decrypt_all_literals.py` only ever scanned `.text` (`blob = img[tlo:thi]`). I pointed
the same machinery at every executable section, two ways.

**Method A** — byte-for-byte the original: immediate operands
(`push imm32`, `mov r32,imm32`, `C7 05`, `C7 45`, `3D`, `81 3D`) whose value lands in the
non-executable literal pool `.code`+`.data`+`.rdata` = `0x03029000`–`0x04E5D000` (31.7 MB).

**Method B** — strictly stronger: **every dword at every alignment** landing in the pool.
This also catches pointers held in tables, vtables, and relocation-patched slots, which
Method A cannot see.

| section | VA | bytes | A: ptrs / decrypt | B: ptrs / decrypt | B rate | `__FILE__` |
|---|---|---:|---:|---:|---:|---:|
| `.text`  | 00401000 | 46,301,184 | 76,683 / 36,444 | 221,253 / 36,493 | **16.494 %** | **349** |
| `.rsrc`  | 04F69000 | 65,536 | 6 / **0** | 171 / **0** | 0 % | **0** |
| `.tls`   | 04F79000 | 2,367,488 | 200 / **1** | 13,231 / **15** | **0.113 %** | **0** |
| `.vmp0`  | 051BB000 | 16,384 | 5 / **0** | 62 / **0** | 0 % | **0** |
| `.bss`   | 0598B000 | 36,864 | 10 / **0** | 353 / **0** | 0 % | **0** |
| *(`.vmp1`)* | 051BF000 | 8,175,616 | 1,381 / 13 | 42,556 / 161 | 0.378 % | 3 |
| *(`.mackt`)* | 05994000 | 16,384 | 7 / 0 | 249 / 1 | 0.402 % | 0 |
| *(`.-wI`)* | 05998000 | 3,931,413 | 1,181 / 2 | 29,201 / 41 | 0.140 % | 0 |
| *(`.#\|U`)* | 05D8C000 | 27,899,904 | 8,330 / 6 | 210,607 / 293 | 0.139 % | 1 |

### The control that makes the zeros mean something

The pointer *count* is not the discriminator — the pool is 31.7 MB of a 4 GB space, so
0.737 % of random dwords land in it by chance, and every large section will show
thousands of "pool pointers" whatever it contains. The **decrypt rate** is the
discriminator, and it needs a floor.

I measured the floor directly: **20,000 uniformly random addresses inside the pool**,
fed to `dec()`:

```
  decrypt to >=3 chars:  29 / 20000  = 0.145 %   ... with a Source path: 0
  4-byte-aligned variant: 106 / 20000 = 0.530 %
```

- `.text` = **16.494 %** — 114x the floor. Real literal pointers.
- `.tls` = **0.113 %** — *at or below* the 0.145 % false-positive floor. Indistinguishable
  from feeding the decryptor random garbage. Its 15 "literals" (`BuffLoop1.ani`,
  `hardcore mode`, `Monster/Goblin/SkyCastle/BullsEye/pants.equ`, …) are the method's own
  noise, not references.
- `.rsrc`/`.vmp0`/`.bss` = **0 %** of 171 / 62 / 353 probes.
- Zero `Source\` paths in 40,000 random control probes, so the `__FILE__` counts above
  carry no false positives at all. `.text` 349, `.tls` **0**.

Independent corroboration from the disassembly (capstone linear sweep of `.tls`,
797,283 instructions, counting immediate + absolute-memory operands by target section):

```
.tls data refs:  .tls 79,758 | .vmp1 2,432 | .text 328 | .data 193 | .#|U 66 | .code 29
```

`.tls` addresses **its own bytes** 79,758 times and the game's encrypted literal blob
(`.code`) **29 times**. It does not use the game's data segment. Its strings are its own.

---

## 2. What `.tls` actually is — plaintext strings inside `.tls` itself

`strings.csv` never covered `.tls` (it only holds `.code`/`.eI4`/`.data`/`.rsrc`/`."2A`).
Extracting directly from `0x04F79000`–`0x051BB000`:

| VA | string |
|---|---|
| `0x051460C4` | `SQLite format 3` |
| `0x051466E8` | `CREATE TABLE sqlite_master(` |
| `0x05148880` | `REINDEXEDESCAPEACHECKEYBEFOREIGNOREGEXPLAINSTEAD…` (SQLite keyword blob) |
| `0x051A24A0` | `os_win.c:%d: (%d) %s(%s) - %s` |
| `0x051A3370` | `INSERT INTO vacuum_db.sqlite_master   SELECT type, name, tbl_name, rootpage, sql …` |
| `0x05174EE8` | `$Id: md5.c,v 1.1.1.1 2004/05/17 13:23:36 rcrittenden0569 Exp $` |
| `0x05174F28` | `$MD5Version: 1.0.0 November-19-1997 $` |
| `0x05145788` | `inflate 1.2.8 Copyright 1995-2013 Mark Adler` |
| `0x05175674` | `inet_ntoa failed when parse tdr_ip_t` |
| `0x0517569C` | `value variable of tdr_ip_t is invalid` |
| `0x051756C4`.. | `tdr_time_t` / `tdr_date_t` / `tdr_datetime_t is invalid` |
| `0x05174E14` | `\Tencent\DeskUpdate` |
| `0x05174E28` | `\Tencent\Desktop` |
| `0x05175E40` | `QTalk.exe` |
| `0x05174F8C`, `0x05175198`, `0x05175D04`… | `az.dll` (x5) |
| `0x05174D3C`… | `iphlpapi.dll`, `msvcr71.dll`, `gdi32.dll`, `user32.dll`, `ws2_32.dll` |

`tdr_ip_t` / `tdr_time_t` / `tdr_datetime_t` are **Tencent TDR** (Type Definition
Representation), Tencent's internal serialization library. Together with
`\Tencent\DeskUpdate`, `QTalk.exe` and `az.dll` this is a Tencent-operator runtime,
not a Neople game module.

**Note the string handling itself is a discriminator.** Every literal in `.text` is
encrypted (that is the entire reason `literal_cipher.py` exists). Every string in `.tls`
is plaintext. Two different build pipelines.

`.tls` also carries `os_win.c` — a source-file name — but it is SQLite's, in SQLite's
own `os_win.c:%d:` format, nothing like the `D:\Work\1229\Source\DNFClient\RDAR\…`
expansions that mark game code.

### Private RTTI block — `.tls` brings its own C++ type system

Scanning the whole image for `.?AV…@@` / `.?AU…@@` type-descriptor names:

```
.data 393   |   .tls 17   |   .-wI 6   |   everything else 0
```

The game's 393 all live in `.data`. `.tls`'s 17 live in a private block at
`0x0519FA70`–`0x051A03E8`, and **14 of 17 are Crypto++**:

```
0x0519FAB8  .?AVInteger@CryptoPP@@
0x0519FAD8  .?AVException@CryptoPP@@
0x0519FB00  .?AVInvalidArgument@CryptoPP@@
0x0519FB90  .?AVNotImplemented@CryptoPP@@
0x0519FC18  .?AVBERDecodeErr@CryptoPP@@
0x0519FC50  .?AVConstByteArrayParameter@CryptoPP@@
0x0519FC90  .?AVDivideByZero@Integer@CryptoPP@@
0x0519FD28  .?AUInputRejected@?$InputRejecting@VBufferedTransformation@CryptoPP@@@CryptoPP@@
0x0519FDC0  .?AVParameterNotUsed@AlgorithmParametersBase@CryptoPP@@
0x0519FE38  .?AVValueTypeMismatch@NameValuePairs@CryptoPP@@
0x0519FEC0  .?AVSelfTestFailure@CryptoPP@@
0x0519FF88  .?AUNoChannelSupport@BufferedTransformation@CryptoPP@@
0x051A00A8  .?AVByteArrayParameter@CryptoPP@@
0x051A0020  .?AV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@
0x0519FA70/A88/03E8  .?AVexception@@ / .?AVbad_alloc@std@@ / .?AVtype_info@@
```

13 of these descriptor addresses are referenced by dwords inside `.tls`. Zero game
classes appear. A relocated slice of game code would be throwing game exceptions and
constructing game objects; this throws `CryptoPP::BERDecodeErr`.

---

## 3. Premise check — msvcp60: **confirmed, exactly 596**

The 2026-08-24 version of this file claimed "msvcp60 is called from nowhere in this
image." That was wrong: it read `SCRATCH/wrapper-apis-a21.csv` (695 rows, a truncated
copy) instead of the real table at
`D:\project\DNF\DNF-JP-86-Server\.worktrees\client-unpack-lab\ClientHack\tools\wrapper-apis-a21.csv`
(1,654 rows). `msvcp60.dll` is loaded at `0x5BA40000` in `DNF-modules.csv`.

Wrapper call sites by originating section × module:

| source | total | breakdown |
|---|---:|---|
| `.text` | 749 | user32 366, gdi32 59, shell32 53, wininet 45, advapi32 32, fmodex 30, … **msvcp60 1** |
| `.tls` | 673 | **msvcp60 596**, advapi32 30, user32 19, shell32 6, shlwapi 6, psapi 3, iphlpapi 3, msvcr71 2, … |
| `.vmp1` | 215 | advapi32 86, **msvcp60 73**, user32 32, … |
| `.mackt` | 16 | msvcp60 8, user32 5, … |

**678 msvcp60 call sites in the whole image; 596 of them in `.tls`, 1 in `.text`.**
The premise's number is exact.

But the premise's *inference* — "msvcp60 means game C++ code" — inverts the evidence.
46 MB and 67,074 functions of game `.text` use msvcp60 **once**. The game does not use
MSVC6 STL. Only `.tls` (and its `.vmp1` half) does. msvcp60 is therefore the single
best marker that a byte range is **not** game code.

The 596 resolve to `std::basic_string<char>` internals (327), `std::_Lockit` (215),
`_Xran`/`_Xlen` (27), `ios_base` (7), and a handful of stream types — i.e. an ordinary
`std::string`-heavy C++ program.

### The non-CRT APIs `.tls` calls — a fingerprinting profile

Complete non-msvcp60 list from `.tls`'s 673 wrapper call sites (77 rows):

- **Network identity**: `GetAdaptersInfo`, `GetAdaptersAddresses`, `SendARP`, `Netbios`,
  `NetWkstaTransportEnum`, `NetApiBufferFree`
- **Registry sweep**: `RegOpenKeyExA` x11, `RegEnumKeyExA`, `RegEnumValueA` x2,
  `RegQueryInfoKeyA`, `RegQueryValueExA` x4, `RegCloseKey` x10
- **Process/module enumeration**: `EnumProcessModules`, `GetModuleFileNameExA`,
  `GetModuleInformation`
- **Window enumeration**: `EnumWindows` x3, `GetWindowTextA/W`, `GetClassNameA`,
  `GetClassLongA`, `GetWindowThreadProcessId` x3, `IsWindowVisible` x2, `GetWindow`,
  `GetParent`, `GetDesktopWindow`, `GetIconInfo`
- **Machine profile**: `EnumDisplayDevicesA`, `GetSystemMetrics`, `GetFileVersionInfoA`,
  `VerQueryValueA`, `CoCreateGuid`, `ReadEventLogW`
- **Paths**: `SHGetFolderPathA`, `SHGetSpecialFolderPathA` x4, `PathCombineA` x4,
  `PathFileExistsA` x2, `DoEnvironmentSubstA`

MAC address + ARP + NetBIOS + adapter list + registry + display device + running-window
titles + loaded-module list + event log, all marshalled through TDR and hashed with
md5/Crypto++ into a SQLite file. That is HWID collection and environment scanning. It is
not a game subsystem.

---

## 4. Coupling to `.text` is thin, and it is protector-shaped

Capstone sweep of all of `.text`, every direct `call`/`jmp` with an immediate target:

| edge | sites | distinct game functions |
|---|---:|---:|
| `.text` → `.tls` | 43 | 33 |
| `.text` → `.rsrc`@04F69000 | 50 | ~38 |
| `.text` → `.vmp0` | **0** | 0 |
| `.text` → `.bss` | **0** | 0 |

33 of 67,074 functions (0.05 %) touch `.tls` directly, scattered across unrelated
systems — `GameCoreNetwork_CMD_Etc.cpp`, `GameCoreNetwork_NOTI_Dungeon.cpp`,
`CNRDUserInfoPacketHandler.cpp`, `IRDCharacter.cpp`, `IRDMonster.cpp`,
`CNRDUdpModule.cpp`, `CNGameSocket.cpp`, `CNRDSecurityMessage.cpp`,
`AICharacterFactory.cpp`, `Main.cpp`. That is a check sprinkled over the codebase, not a
module boundary.

And the targets are not functions. `0x050006A2` (19 of the 43 call sites) is a textbook
**VMProtect VM entry**:

```
050006A2  pushfd
050006A3  pushal
050006A4  push  0x4278154a          ; encrypted VM handler key
050006A9  push  ecx
050006AA  lea   esp, [esp - 6]      ; junk-interleaved
...
050006C0  call  0x50068ce           ; -> .tls
```

`0x04F9CA99` (from `CNRDSecurityMessage.cpp`) is the same shape. So the `.text`→`.tls`
edges are entries into virtualized code whose body lives as `.vmp1` bytecode — the
protector's trampolines, not relocated game functions.

`.tls` itself is dominated by internal control flow: 19,210 internal calls, 9,526
internal jumps, 3,094 calls + 6,722 jumps into `.vmp1`, 668 calls into the `.-wI`
import-wrapper section (matching its 673 wrapper call sites), and only **33 calls +
37 jumps back into `.text`**.

---

## 5. Per-section verdicts

### `.rsrc` @ `0x04F69000` — 64 KB, chars `E00000E0` (RWX) — **splice/detour table**
Only **3 of 16 pages** hold anything (~8.4 KB); pages `0x04F6C000`+ are entirely zero.
87.2 % of the section is zero. Content is pure trampoline:

```
04F69122  push 0xa23fbf3 ; push ecx/edx/ebx ; jmp 0x5795b94        -> .vmp1
04F6B2C8  pushal ; pushfd ; call 0x4fae197 (-> .tls) ; popfd ; popal
04F6B2D5  mov eax,[0x399cee0] ; push 0x1644c9c ; ret               -> back into .text
```

That last one returns to `0x01644C9C`, one instruction past the `.text` call site
`0x01644C97` inside `GameCoreSub.cpp`. Edges: 73 calls / 175 jmps → `.vmp1`,
12 calls / 3 jmps → `.tls`, 9 calls / 39 jmps → `.text`. **Not game code, not even a
module — 8 KB of stubs splicing `.text` into `.tls`/`.vmp1`.**

### `.tls` — 2.37 MB, chars `E00000E0` (RWX) — **third-party protection runtime**
522 of 578 pages populated, entropy 5.964, 26.6 % zeros — dense native x86 code plus its
own read-write data. Identified above. **Not game code. Do not name it like `.text`.**

### `.vmp0` — 16 KB, chars `E0000060` (RWX) — **protector data, effectively not code**
Pages `0x051BB000`/`0x051BC000` are ~98 % nonzero at entropy **7.645 / 7.684** (encrypted
or compressed); `0x051BD000` holds 376 bytes; `0x051BE000` is zero. The linear sweep
finds 7,297 "instructions" but only **3 internal calls** and 6 internal jumps — no
coherent control flow, i.e. the decode is garbage-in. **0** `.text` edges point at it.
0 literals of 62 probes.

### `.bss` — 36 KB, chars `E0000020` (RWX) — **encrypted blob; the X bit is unused**
All 9 pages ~95 % nonzero at entropy **7.685–7.874** — flat-random. The sweep finds
**zero** internal calls and 58 calls to targets outside the image. **0** `.text` edges.
0 literals of 353 probes. This is a data blob that happens to sit in an executable
section — exactly the "section names are camouflage" trap, but in the other direction:
the X bit here is noise.

One real structure, at the very tail (`0x05993F40`, 192 bytes from the end, in an
otherwise zeroed run): an import-descriptor-shaped record —
`FFFFFFFF | 0x05593F40 | 0x05593F38 | … | 0x6F45E44E | "DNFBase.dll\0"` followed by
`FF 15 38 3F 99 05` = `call dword ptr [0x05993F38]`. A single dangling
**`DNFBase.dll`** import thunk. Medium confidence on interpretation; the bytes are exact.

---

## 6. Section table, for the record (from `DNF.exe`'s on-disk header)

`AddressOfEntryPoint` RVA `0x07213FBC` → VA `0x07613FBC`, inside `.#|U`.

| name | VA | VSize | end | chars | X? |
|---|---|---|---|---|---|
| `.text` | 00401000 | 02C28000 | 03029000 | E0000020 | **X** |
| `.code` | 03029000 | 00935000 | 0395E000 | C0000040 | – |
| `.data` | 0395E000 | 014CC000 | 04E2A000 | C0000040 | – |
| `.rdata` | 04E2A000 | 00033000 | 04E5D000 | C0000040 | – |
| `.eI4` | 04E5D000 | 0010C000 | 04F69000 | 40000040 | – |
| `.rsrc` | 04F69000 | 00010000 | 04F79000 | E00000E0 | **X** |
| `.tls` | 04F79000 | 00242000 | 051BB000 | E00000E0 | **X** |
| `.vmp0` | 051BB000 | 00004000 | 051BF000 | E0000060 | **X** |
| `.vmp1` | 051BF000 | 007CC000 | 0598B000 | E0000020 | **X** |
| `.bss` | 0598B000 | 00009000 | 05994000 | E0000020 | **X** |
| `.mackt` | 05994000 | 00004000 | 05998000 | E0000060 | **X** |
| `.-wI` | 05998000 | 003BFD15 | 05D57D15 | 60000020 | **X** |
| `."2A` | 05D58000 | 00033668 | 05D8B800 | C0000040 | – |
| `.#\|U` | 05D8C000 | 01A9B7E0 | 07827800 | 60000020 | **X** |
| `.rsrc` | 07828000 | 00045F28 | 0786E000 | 40000040 | – |

Two sections are named `.rsrc`. The 64 KB one at `0x04F69000` is the executable stub
table analysed here; the real resource directory is the second one at `0x07828000`.

---

## 7. Naming recommendation

| section | label | confidence |
|---|---|---|
| `.tls` | `PROTECT_RUNTIME` — Tencent-side protection/telemetry runtime: SQLite 3, Crypto++, zlib 1.2.8, RSA md5.c, Tencent TDR, msvcp60 `std::string`, HWID + environment scan. **Not game code.** | **high** |
| `.rsrc`@04F69000 | `PROTECT_STUBS` — ~8 KB of detour/VM-entry trampolines splicing `.text` into `.tls`/`.vmp1`; 13/16 pages empty | **high** |
| `.vmp0` | `VMP_DATA` — 16 KB, entropy 7.6+, no coherent control flow, no inbound `.text` edges | medium |
| `.bss` | `OPAQUE_BLOB` — 36 KB flat-random data; X bit unused; one dangling `DNFBase.dll` import thunk at the tail | medium |

---

## 8. Loose end worth a follow-up (not in scope)

`.vmp1` is the only non-`.text` section with plausibly-real `__FILE__` hits — 3 of them,
above the 0.145 % floor at 0.378 %:

```
0x031EBAE8  D:\Work\1229\Source\DNFClient\RDAR\CNRDItemShopWindowVer2.cpp
0x032820C8  D:\Work\1229\Source\DNFClient\RDAR\Stackable\CNCoinOfFortune.cpp
0x035E96E0  D:\Work\1229\Source\DNFClient\RDAR\Library\CNAntiBot.cpp
```

Item shop, coin-of-fortune, anti-bot — exactly the three you would virtualize. Those are
game `__FILE__` pointers surviving as VMProtect bytecode operands, i.e. `.vmp1` *does*
hold relocated game code. `.tls` does not. (`.#|U` similarly yields one:
`DNFShared\GameScript\RDARScriptItem.cpp` at `0x036329D0`.)

---

## What would falsify this

`.tls` code reaching the game's literal decryptor with a pool address it **computed at
runtime** (base + index) rather than stored as an immediate or a dword — neither method
can see computed addresses. Test: breakpoint the decryptor entry, filter return
addresses to `0x04F79000`–`0x051BB000`, and see whether any `__FILE__` blob is ever
decrypted on `.tls`'s behalf. A hit would weaken the `__FILE__` argument specifically —
but the msvcp60 asymmetry (596 vs 1), the plaintext-vs-encrypted string split, the
private Crypto++ RTTI block, and the SQLite/TDR/Tencent strings each independently
survive it.

Conversely, to falsify the *naming* recommendation you would need `.tls` bytes that
belong to a named game translation unit. 40,000 control probes returned zero false
`Source\` paths, so `func-source-file.csv` could be extended to `.tls` at no precision
cost the moment such a literal exists. None does.

## Reproduce

Scripts written this run, all in SCRATCH
`C:\Users\10239\AppData\Local\Temp\claude\D--project-DNF\1c50f207-441e-45a6-8f17-0f881c4703e0\scratchpad`:

- `_v1_secs.py` — section table + characteristics from `DNF.exe`
- `_v1_lit.py` — methods A and B over every executable section (the table in §1)
- `_v1_char.py` — entropy / zero-fraction / page occupancy / ASCII runs per section
- `_v1_sweep.py [sections…]` — capstone linear sweep, call/jmp/data-ref targets by section
- `_v1_callers.py` — `.text` functions with direct edges into `.tls`/`.rsrc`/`.vmp0`/`.bss`,
  annotated with their `__FILE__` and literals
- `_v1_rtti2.py` — RTTI type-descriptor scan of the whole image, by section
- `_v1_dis.py 0xVA [n]` — linear disassembly at any VA with API annotation
  (`peek.py` only knows `.text` function entries)

Run everything with `PYTHONIOENCODING=utf-8`; the console codepage is GBK and will throw
on the Korean literals otherwise.
