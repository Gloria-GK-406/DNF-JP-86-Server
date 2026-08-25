# .vmp1 — which entries deserve the CThread::Create / ISocket::Write treatment

DNF S4A21, image base 0x00400000, dump `dnf-dump-20260822-232754-263-26888`.
Section table read from the on-disk PE (`D:\project\DNF\DNFClient-A21-debug\DNF.exe`):

    .vmp1   0x051BF000 - 0x0598B000   8,175,616 bytes

Everything below marked **[M]** is measured from the image or from the CSVs.
Everything marked **[I]** is inference on top of that. Where I could not pin
something down I say unknown.

---

## 0. First, a correction to the premise

**[M]** Neither of the two functions upstream reimplemented is itself a .vmp1 entry.

* `ISocket::Write` is at **0x02870190**, in `.text`, 160 bytes, plain readable code.
  It calls `ws2_32!send` directly through IAT slot `0x03029688` at **0x028701A4**,
  then `WSAGetLastError` at 0x028701AF. Nothing about it is obfuscated.
* The only reference to the `kernel32!CreateThread` IAT slot `0x03029294` anywhere in
  the image is at **0x02C65744**, inside the CRT `_beginthreadex` at **0x02C656CD**,
  also plain `.text`. (Scan: 1 hit, section `.text`.)

So the thing that made those two worth rewriting was not that they were virtualized.
**[I]** It was that a clean reimplementation was cheaper than owning the client's
version. That criterion transfers; "is it VM'd" is a separate axis. Below I rank on
both: *how opaque it is* and *how much of the tree stops if you can't have it*.

---

## 1. Why .vmp1 has never been analysed

**[M]** `callgraph.csv` contains **zero** edges whose callee lands in .vmp1
(0 of 526,525). `functions.csv` has **zero** entries in .vmp1. The whole section is
invisible to the existing tooling because no .vmp1 address was ever seeded as a
function start.

**[M]** Scanning the body of every function in `functions.csv` with capstone and
collecting direct `call`/`jmp` immediates that land in .vmp1:

| quantity | count |
|---|---|
| control-flow edges from .text into .vmp1 | **7,972** |
| distinct .vmp1 target addresses | **4,268** |
| targets reached by `call` | 1,673 |
| targets reached by `jmp` | 2,867 |
| targets reached by both | 272 |

The `jmp`-only targets are **not** shared functions and must not be counted as such.
Example: 0x055073D4 has 106 distinct "callers" and 0x0549CA95 has 105 — but every one
of those edges is a `push ebp; push edi; jmp <target>` tail sitting inside mutated
.text bodies clustered at 0x01D0xxxx–0x01D5xxxx. That is a VM *entry*, one per
protected block, not a call to a utility. **The real call surface is the 1,673.**

---

## 2. What kind of thing lives there

**[M]** Three distinct populations, identified from first bytes and by following the
fallthrough/unconditional-jump path:

**(a) VMProtect VM entry, "push key" form** — 267 entries whose first instruction is
`push imm32` followed within 8 instructions by a `jmp`. Example 0x057131CC:

```
057131CC push 0xfea3fb4a ; push ecx ; push edx ; push ebx ; jmp 0x0236A067
```

There are **262 distinct dispatch targets for those 267 stubs** — this is not one
shared interpreter you can reverse once, each protected block gets its own entry.

**(b) VMProtect VM entry, "grow the stack first" form** — the dominant one.
Example 0x0549CA95:

```
0549CA95 call $+5 ; pop eax ; sub eax, 0x549ca9a          ; position-independent
0549CAA0 mov edi, fs:[0x18] ; mov edi, [edi+8]            ; TEB -> StackLimit
0549CAAE cmp ecx, 0x4000 ; sub edi,0x1000 ; test [edi],esi ; guard-page touch loop
0549CACB mov esi,[esp+0x20] ; rol esi,9 ; add esi,0xbd81ace6 ; bytecode pointer
0549CADE mov ebx,[esi] ; add esi,4 ; add ebx,0x4cc42998 ; add ebx,[edi] ; jmp ebx
```

**[M] Important:** this same prologue also exists at .text addresses —
0x019900B5, 0x023D0345, 0x01C56885 are byte-identical in shape. A .vmp1 stub that
jumps *out* into .text is therefore still virtualized. My first pass got this wrong
and the corrected classifier follows across the section boundary.

**(c) Genuinely relocated native code.** Mutated (junk `jcc`, opaque constants,
dead branches) but it does reach a `ret` and can be read with effort.

**[M]** Verdicts for the 1,673 call-reached entries, following control flow across
sections until `ret` / `jmp reg` / decode failure:

| verdict | count | meaning |
|---|---|---|
| vm_stack | 920 | reaches the `fs:[0x18]` VM prologue — virtualized |
| vm_dispatch | 294 | reaches an indirect `jmp reg` — virtualized |
| bad / oob / loop | 258 | linear decode desyncs on mutation — not readable as-is |
| **opaque subtotal** | **1,472** | |
| ret | 201 | native, mutated, readable |

**[M]** .vmp1 also hosts the protector's own code: `wrapper-apis-a21.csv` puts **215
API call sites inside .vmp1**, and their API mix is registry / window enumeration /
event log / msvcp60 string helpers — `RegCloseKey`×28, `basic_string::_Tidy`×25,
`RegOpenKeyExA`×18, `RegQueryValueExA`×14, `EnumWindows`×3, `GetWindowThreadProcessId`,
`OpenEventLogW`, `InternetGetConnectedState`. That is anti-cheat environment probing.
It is a **different** population from the game functions .text calls, and should not
be confused with them.

---

## 3. The shape of the ranking problem

**[M]** Transitive reach is useless here. I computed, for each entry, the number of
.text functions that can reach it through `callgraph.csv`: it saturates at
**~39,780 of 67,074 for essentially every entry** (0x0526AB41 → 39,862;
0x0520FE26 → 39,794; a 1-caller entry → 39,784). The callgraph is one giant blob.
Base rate kills the metric. Ranking must use **direct .text callers** and
**distinct caller source files**.

**[M]** Distribution of the 1,472 opaque call-reached entries by distinct .text callers:

| callers | entries |
|---|---|
| 1 | **1,345** |
| 2–4 | 80 |
| 5–9 | 24 |
| 10–24 | 15 |
| 25+ | **8** |

**[M]** Only **333 of the 1,472** have any caller with a `__FILE__` literal, even
after propagating two hops up the callgraph. Naming coverage is genuinely thin.

**[I] The headline:** the "reimplement it" list is short. 91% of the opaque entries
are one-off protected functions that block exactly one caller — you fix those by
fixing that caller, not by writing a clean reference. The list worth the
CThread::Create treatment is **47 entries** (≥5 callers), and realistically the
**top ~15**.

---

## 4. Ranked short list

### Tier A — the TCP/UDP path. Small caller counts, but they gate the reason the project exists.

**A1. 0x0577254B** — verdict `bad` (mutated, `mov eax,0x7146d049; call 0x571427b`,
then `jmp 0x5749b25` — linear decode dies immediately). 5 call sites, 3 callers.
Signature **[M]**: `thiscall(void* buf, int len) -> int`, return sign-tested.

Callers **[M]**, both named by `func-source-file.csv`:
* **0x028704A0 = `nengine::ISocket::Recv`** (`Library\Include\Nengine\ISocket.cpp`;
  literals `'RECV'`, `'nengine::ISocket::Recv'`, `'ERROR : recv : Not Connected'`).
  Call site 0x028705A2, inside the recv loop:
  ```
  02870570 mov ecx,[esi+0x1c4] ; push 0 ; push 0x80 ; lea eax,[ebp-0x84] ; push eax ; push ecx
  02870585 call edi                    ; ws2_32!recv  (slot 0x0302961C ref'd at 0x02870560)
  02870592 push eax                    ; nbytes
  02870593 lea edx,[ebp-0x84] ; push edx ; lea ecx,[esi+0x1d8]
  028705A2 call 0x0577254B             ; <-- .vmp1
  028705A7 test eax,eax ; jns 0x02870570   ; negative == stop
  ```
* **0x0274E6D0 = `CNGameSocket` send path** (`DNFClient\RDAR\CNGameSocket.cpp`,
  in_degree **959**; literals `'TCP_SEND'`, `' [2-> COMMAND PACKET] Packet : [%d], Seq : %d, S…'`,
  `'error : ENUM_NEOPLE_FAULT_SEND_LOCK'`). Calls it 4× — 0x0274E855, 0x0274E9A5,
  0x0274E9C4, 0x0274EC96 — with `this` = game-core singleton `[0x03B7CEE4] + 0x15E218`
  and `+ 0xAF1F8`, args `(buf, len)`; one site passes `push 0xE` (14 = header size).

**[I]** Same primitive on both the recv-side reassembly buffer (`ISocket+0x1D8`) and the
send-side assembly buffers: a byte-stream append / drain (`Push(data,len)`), possibly
with the transform applied. **Rank #1: it sits on both directions of the TCP stream and
its two callers own 959+ call sites of packet traffic between them.**

**A2. 0x05790FE9** — verdict `oob` (opaque `jmp` into unmapped space after a
push/pop shuffle — mutation, decode desyncs at 0x05790FFE). 8 callers, 10 sites.
Signature **[M]**: `thiscall(void* data, int len)`.
* **0x0274D1E0 = `CNGameSocket::MakePacket`** (literal `'CNGameSocket::MakePacket'`,
  in_degree **1062**). Call site 0x0274D2AF:
  ```
  0274D289 call [0x3029160]      ; kernel32!EnterCriticalSection
  0274D29B call [0x3029164]      ; kernel32!LeaveCriticalSection
  0274D2A1 push 0xE              ; 14-byte header
  0274D2A3 lea eax,[ebp-0x14] ; push eax
  0274D2A7 mov ecx,esi           ; this = CNGameSocket
  0274D2AF call 0x05790FE9       ; <-- .vmp1
  ```
* Also the **tail-jmp target of `ISocket::Recv`** at 0x02870600.

**[I]** Writes the 14-byte C→S packet header into the outgoing buffer under the
send lock. **Rank #2: every outbound packet passes through it.**

**A3–A6. The UDP / P2P subsystem.** These have few callers each but between them they
cover the entire `UdpClientSystem` surface, and each one is used by several functions
in the same file (so "callers" understates them):

| entry | verdict | sites | who calls it **[M]** |
|---|---|---|---|
| **0x056ACE5A** | vm_stack | 10, all from `CNRDUdpModule.cpp` | 0x022E7B40 / 0x022E8E10 / 0x022EA720 (`'!UDP SEND CHECK PACKET TO(USE_RELAY)…'`), plus `GameCoreDebug.cpp` 0x01630A00 ×8 |
| **0x056C9E34** | vm_stack | 3 in `CNRDUdpModule.cpp` + 4 in `GameCoreDebug.cpp` | same cluster |
| **0x057018B9** | bad | 6, **all** in `CNRDUdpIdentify.cpp` (0x022E3D30, 0x022E4080) | NAT/STUN discovery — literals `' STUN Server로 부터 응답 없음'`, `'STEP_IP_DEPENDENT_SYMMETRIC_CHECK IP :'` |
| **0x052CF55A** | vm_stack | 9 | `CNRDUdpModule.cpp` ×2, plus `CNRoom.cpp`, `CNWarRoom.cpp`, `CNAssaultRoom.cpp`, `CNRDDualRaidPacketManager.cpp` — the room→UDP bridge |

**A7. 0x05238824** — vm_stack, 15 callers / 16 sites, `thiscall(1 arg)`.
**[M]** Two sites in **`CNSelectChannelSocket.cpp` 0x010BF840**
(`'CNSelectChannelSocket::retryConnectFailprevChannel'`,
`'EXIT>> shutdown dnf.exe because it cannot connect…'`), plus
`CNRDMainHudMenuManager.cpp` ×3, `CNRDInterfaceManager.cpp` ×2, `GameCoreSub.cpp`.
**[I]** Channel-select / login-socket retry path — blocks the very first thing a
private server has to get through.

**A8. 0x05778441** — vm_dispatch, 3 sites all in **`GameCoreNetwork.cpp` 0x0163BBD0**
(which is on the parse surface, `parsers.csv`). Small but sits at the network dispatch root.

> **[M] For contrast, do not spend time on these:** 0x051DADEC (called 3× by
> `CNGameSocket.cpp` 0x0274DDD0/0x0274E6D0 and by `GameCoreSub.cpp`), 0x05792154,
> 0x0578C174, 0x05797C9B (all `UdpManager.cpp`/`UdpCore.cpp`), 0x0573C0AA, 0x052E5931,
> 0x057FAE8D, 0x0579F66F — all verdict `ret`, i.e. native and readable. Read them.

### Tier B — the eight high-fan-in primitives (25+ callers). Biggest raw blast radius.

Signatures below are **[M]** — measured over every call site by counting stack pushes
between the `ecx` load and the `call`, and reading the instruction after the `call`.
The *names* are **[I]** and deliberately vague; I could not pin exact identities.

| # | entry | verdict | callers / sites | measured signature | caller source files **[M]** | what it is **[I]** |
|---|---|---|---|---|---|---|
| B1 | **0x057131CC** | vm_stack | **104 / 135** | `thiscall(int)`, 134/135 sites push exactly 1 arg; `this` = `[esi+0x38]` (71×) or `[esi]` (13×); next insn reloads the same `this` 44× → called back-to-back | `CNRDInterfaceManager.cpp`, `AdvanceAltarShopConfirmWindow.cpp`, `AtSwordmanLevelupSupportPopupWindow.cpp`, `ResponsePeerWindow.cpp`, `CNSelectCharacterModule.cpp`, +7 | A **text/font draw-state setter**. At 0x014A155E it is the third of a chain: `push 0xFF00FF00; call 0x027ED8B0` (an ARGB colour), `push 3; call 0x027ED890`, `push 2; call 0x057131CC`. Cheap to reimplement from the chain's shape. |
| B2 | **0x052F1302** | vm_stack | **64 / 95** | `thiscall()`, 84/91 sites push 0 args; returns a **pointer** (74× `test eax,eax`, 8× `mov ecx,eax` to chain); `this` is usually the result of a vtable call at `[vtbl+0x10C]` | `CNPriest.cpp`, `CNMage.cpp`, `CNATSwordman.cpp`, `IRDCharacter.cpp`, `AppendageFactory.cpp`, `CNRDUdpModule.cpp`, +8 | Character/appendage accessor returning a sub-object. Exact identity **unknown**. |
| B3 | **0x05796EBE** | vm_dispatch | **58 / 66** | `thiscall()`, **66/66** sites push 0 args; returns **bool in al** (62× `test al,al`, 3× `cmp al,1`); `this` = result of vtable slot `[vtbl+0x300]` (45×) or `lea ecx,[esi+0x46c]` | `CNSniper.cpp`, `CNThrowWeapon.cpp`, `CNRDItemManager.cpp`, `CNRDObjectParticleCreater.cpp`, `GameCoreNetwork_NOTI_Etc.cpp`, +9 | A const predicate on the object returned by vtable+0x300. Identity **unknown**; the 0-arg/bool contract makes a clean stub trivially safe to try. |
| B4 | **0x052C51AD** | bad | **53 / 68** | `thiscall` with a stack arg (`push 1`/`push 0` seen directly at 0x00E0F173, 0x00E128F5, 0x00E12B87); return **unused** | `CNRDUdpModule.cpp`(5), `GameCoreNetwork_NOTI_Town.cpp`(8 sites in 0x01191D80), `GameCoreInit.cpp`, `MovieDirector.cpp`, `CNBossTowerModule.cpp`, `CNAdvanceAltarModule.cpp`, +11 — **5 of its callers are on the inbound parse surface** (`parsers.csv`: 0x011040A0, 0x01146000, 0x01171F50, 0x01191D80, 0x01278210) | A module-level bool setter. Highest parse-surface exposure of any entry in the list. |
| B5 | **0x0571DFC0** | vm_stack | **42 / 58** | `thiscall()`, **58/58** push 0 args, returns **bool**; `this` = `lea ecx,[esi+0x1130]`, `[esi+0x1158]`, `[esi+0x1134]`, `[esi+0xa8]`, `[esi+0x68c8]` — an **embedded sub-object at many different offsets of many different owners** | `GameCoreNetwork_NOTI_Etc.cpp`, `CNRDUdpModule.cpp`, `CNRDPassiveObject.cpp`, `CNPriest.cpp`, `CNTownModule.cpp`, +7 | **The best clean-reference candidate in Tier B.** The by-value embedding pattern says this is a small reusable library type (timer / flag-set / counter), not game logic — exactly the CThread::Create situation. |
| B6 | **0x0520FE26** | vm_stack | **29 / 42** | `thiscall()`, 33/42 push 0 args; returns an **int that the caller immediately multiplies** (`imul eax,edi` ×7, `imul eax,[ebp+0xc]` ×5, `imul eax,[ebp-0x80]` ×3) | `GameCoreNetwork_NOTI_Dungeon.cpp`(8), `CNRDStage.cpp`(6), `CNRDDungeon.cpp`, `CNEntryDungeonPartyManager.cpp`, `CNRDUdpModule.cpp`, +5 | A stage/dungeon dimension or count used as a scale factor. |
| B7 | **0x0574E0A2** | vm_stack | **27 / 30** | `thiscall(1 arg)` at **30/30** sites; `this` = `[esi+0x38]` (16×) / `[esi]` (8×); next insn reloads `[esi+0x38]` 14× | only `GameCoreNetwork_CMD_Town.cpp` names a caller; caller literals are UI (`'Interface/WindowCommon.img'`, `'UI/Quest/Mission/…'`) | **Same class as B1** (`this` from `+0x38`, same call chain shape). Do B1 and B7 together. |
| B8 | **0x051FE32D** | vm_stack | **25 / 26** | `thiscall()`, 20/26 push 0 args; `this` = `lea ecx,[ebx+0x1ac]`, `[esi+0x1cc]`, `[edi+0x1b4]` — embedded sub-object again | `GameCoreNetwork_CMD_Town.cpp`(**7**), `GameCoreNetwork_NOTI_Dungeon.cpp`(3), `CNSelectDungeonModule.cpp`, `CNMainGameModule.cpp`, `CNPVPModule.cpp`, +7 | Like B5, a small embedded type. Heavy on the town CMD builder. |

### Tier C — 10–24 callers, worth doing after Tier A/B

**[M]** entry / verdict / callers / top caller files:

* **0x052FA543** vm_stack, 21, `thiscall() -> bool` (23/27 `test al,al`) — `CNRDRandomOptionManager.cpp`, `TitleBook.cpp`, `AppendageFactory.cpp`
* **0x05743E63** bad, 17 — **no source-file attribution at all**, unknown
* **0x052D68D0** bad, 16 callers / **57 sites** — `CNRDNpc.cpp`(5), `GameCoreNetwork_NOTI_Dungeon.cpp`(4), `CNRDPopupMenu.cpp`(2); 1 parser caller
* **0x05744276** vm_stack, 16, `thiscall()` with `this` = `mov ecx,eax` 16/17 — `GameCoreNetwork_NOTI_Dungeon.cpp`(3), `CNBreakingObjectModule.cpp`(2)
* **0x0528F709** bad, 14 — `IRDCharacter.cpp`, `CNRDTagSystem.cpp`
* **0x05229BE0** vm_dispatch, 14, **2 parser callers** (0x006CC420, 0x01119280 `GameCoreNetwork_CMD_Town.cpp`)
* **0x0537816D** vm_stack, 14 — **`thiscall` called from constructors**: at 0x01BFFE08 the caller has just written `[esi]=0x0334AABC`, `[esi+8]=0x0334AA6C` (vftables) under SEH; other sites write `[esi]=0x0334B16C/0x0334B81C/0x0334BECC` right after the call. **[I]** a base-class ctor/init — a clean reference is realistic here.
* **0x057112B3** vm_dispatch, 13 — `GameCoreNetwork_CMD_Town.cpp`(5), `StorageWindow.cpp`, `ItemSellConfirmWindow.cpp`, `CNRDExpertJobShop.cpp` — the shop/storage commit path
* **0x052734C1** / **0x05220639** vm_stack, 13 each — both called once from each of `CNMainGameModule`, `CNTownModule`, `CNPVPModule`, `CNBossTowerModule`, `CNAdvanceAltarModule`, `CNWarRoomModule` **[I]** a virtual on the module base class
* **0x05224B29** vm_stack, 12 — `GameCoreNetwork_NOTI_Dungeon.cpp`(6)
* **0x051C6D67** vm_stack, 12 — `CNRDInterfaceManager.cpp`(5), `CNSelectChannelSocket.cpp`, `MatchChannelMoveManager.cpp`
* **0x0570F46E** bad, 12 — broad UI (`JewelryBattleWindow`, `CNTownModule`, `AppendageFactory`)
* **0x051FCC55** vm_stack, 11 — `MovieDirector.cpp`, `EquipmentFactory.cpp`

Full 47-entry list (≥5 callers) is reproducible with the scripts in §6.

---

## 5. Explicitly NOT worth the treatment

**[M]** The single largest fan-in in all of .vmp1 is **0x0526AB41 with 477 distinct
.text callers** — and it is four instructions of plain code:

```
0526AB41  mov eax,[ecx+0xC4] ; sub eax,[ecx+0xC0] ; sar eax,2 ; ret
```

That is `(end - begin) / 4` — the element count of a 4-byte-element vector stored at
`this+0xC0`/`+0xC4`. Nothing to reimplement; just read it. Same for
**0x052B6B0D** (56 callers, `ret`), **0x052F7C50** (16), **0x052D8D44** (7),
**0x0571799B** (7), **0x051F957A** (6) and the other 195 `ret`-verdict entries.

**[M]** Ignore the `jmp`-only targets entirely — 0x055073D4 (106), 0x0549CA95 (105),
0x054FEC24 (98), 0x0542F873 (96), 0x0539D5E3 (32), 0x053F41EE (31), 0x055A8E75 (27),
0x0538660E (26), 0x055D3942 (26), 0x056DC56A (23), 0x056301C3 (22), 0x0520630C (21)
and 14 more. These look like the biggest blockers in a naive count and are not
functions at all: each is one VMProtect VM entry for one mutated .text block. Their
"caller" counts are entry-site counts.

**[M]** Ignore the protector's own .vmp1 code (the 215 registry/EnumWindows/event-log
API sites). It is not called from the game tree.

---

## 6. Method, so this is reproducible

Scripts written to `…/scratchpad/`:

* `_v1scan.py` — capstone sweep of every `functions.csv` body, collects direct
  `call`/`jmp` immediates landing in 0x051BF000–0x0598B000 → `_v1e.pkl`
* `_v1cls3.py` — control-flow follow across section boundaries, verdicts
  ret / vm_stack / vm_dispatch / bad / oob → `_v1follow3.pkl`
* `_v1attr.py` — caller → source file, propagating up to 2 hops through
  `callgraph.csv` when the direct caller has no `__FILE__` → `_v1attr.pkl`
* `_v1call.py` — the ranked table (call-reached only)
* `_v1sig.py` — per-target calling convention: stack args between the `ecx` load and
  the `call`, whether the caller fixes `esp`, what the next instruction does with the
  return value
* `_v1site.py`, `_v1sib.py`, `_v1byfile.py`, `_who.py`, `_iatref.py`, `_v1reach.py`

---

## 7. Measured vs inferred, summary

**Measured.** Section bounds. 7,972 edges / 4,268 targets / 1,673 call-reached /
1,472 opaque. The 1/2-4/5-9/10-24/25+ caller distribution. The two VM-entry byte
patterns and the fact that the stack-grow prologue also lives in .text. 262 distinct
dispatchers for 267 push-key stubs. 215 protector API sites in .vmp1 and their API mix.
Zero .vmp1 edges in `callgraph.csv`. Transitive reach saturating at ~39,780.
Every calling convention, arity and return-type claim in the tables. Every source-file
attribution (from `func-source-file.csv`, direct or 2-hop). Every quoted literal and
every quoted disassembly. `ISocket::Write` at 0x02870190 being plain .text.
The single `CreateThread` reference at 0x02C65744.

**Inferred.** What the functions *are*. A1 being a stream-buffer append: inferred from
being called with `(buf, len)` from both `ISocket::Recv`'s recv loop and
`CNGameSocket`'s send path. A2 writing the packet header: inferred from `push 0xE`
plus a 14-byte local plus the surrounding critical section. B1/B7 being font/draw state:
inferred from the `0xFF00FF00` argument in the adjacent call chain. B5/B8 being small
reusable library types: inferred from being embedded by value at many different offsets
in many different owner classes. 0x0537816D being a base ctor: inferred from vftable
stores bracketing the call. The claim that the short list is ~15 entries: inferred from
the caller distribution.

**Unknown.** The exact identity (class + method name) of every Tier B and Tier C entry.
0x05743E63 (17 callers), 0x052A3A37, 0x0576A33A, 0x052B68E6 have no source-file
attribution at all and I have nothing to say about them beyond their signatures.
Whether A1 also applies the packet transform, or only buffers. Whether any .vmp1 entry
holds the packet cipher — I did not find one, and I did not prove there isn't one.
