# TU 002 — `DNFClient\RDAR\Appendage\china\CNEventRhythmAction.cpp`

Briefed span `0x00CCF740`–`0x00CD2790`. **The span is too narrow at the high end** (see
"Span correction" below). Real module ≈ `0x00CCF740`–`0x00CD56B0`.

## What this unit implements

A **client-side appendage class (appendage class id 323, `sizeof == 0xA4C`)** that runs the
2011 China event-dungeon **rhythm mini-game**. A `CNConveyer` monster spawns "luggage"
objects that ride toward the player; the player presses in time; each luggage resolves to
**good / bad / miss**; the per-note score values accumulate into anti-cheat-guarded
counters; and when the round ends the client **sends its own tally to the server
(CMD 0x372) and the server replies with its authoritative tally (CMD 0x372)**, which the
client compares and logs.

Evidence anchors: `__FILE__` = `D:\Work\1229\Source\DNFClient\RDAR\Appendage\china\CNEventRhythmAction.cpp`
at `0x031B14F0`; method-name literals `CNEventRhythmAction::setUID` (`0x031B1590`),
`::setResult` (`0x031B16A8`), `::proc` (`0x031B1B10`); asset literals
`PassiveObject/ActionObject/C_Event/rhythm/animation/{good,bad,miss,startCount}.ani`,
`Map/Event/2011_Event_Dungeon/{zone,tutorialKey}.img`, `Monster/Conveyer/rhythm.img`.

---

## 1. Server-relevant: the wire protocol

This unit is the **only** producer of CMD `0x371` and CMD `0x372` in the image
(scan of `push imm32` sites for both ids: `0x371` → `0xCD25F0`, `0xCD3192`, `0xCD35D3`,
plus the registration site `0x1118EA9`; `0x372` → `0xCD01E7`, plus registration
`0x1118EBC`). No other translation unit sends them.

Socket helpers used (verified by disassembly, not by name guess):
`0x0274CA50` = `getGameSocket()` (returns global `[0x03B7CEE4]`),
`0x0274D1E0` = `CNGameSocket::MakePacket(msgId, flag)` (literal `CNGameSocket::MakePacket`, `ret 8`),
`0x0274CED0` = `AppendData(void* buf, int len)` (`ret 8`, adds len to `[this+0x2BCC2C]`),
`0x0274E6D0` = `SendPacket()` (literal `[2-> COMMAND PACKET] Packet : [%d], Seq : %d, Size : %d, ...`).

### 1.1 S→C — CMD `0x372`, handler `0x0110E1C0` (in `GameCoreNetwork_CMD_Etc.cpp`)

Registered at `0x01118EC3`: `push 0; push 0x110e1c0; push 0x372; call 0x163c4a0`.

Body is a **single fixed 7-byte blob**, `read_bytes(dst, 7)` at `0x0110E1E2`
(target `0x027C5380` = the `read_bytes` primitive; buffer `[ebp-0x10]`, zero-initialised
by `mov [ebp-0x10],eax` / `mov [ebp-0xc],ax` / `mov [ebp-0xa],al`).

| off | type | meaning | proof |
|----|------|---------|-------|
| 0 | u16 | **read but never used** by this handler | no read of `[ebp-0x10]`/`[ebp-0xf]` anywhere in `0x0110E1C0..0x0110E36F` |
| 2 | u16 | server's **good** count | `mov edx,[ebp-0xe]` @`0x0110E340` → arg2; callee reads `movzx eax, word [ebp+0xc]` @`0x00CD0415` |
| 4 | u16 | server's **fail** count | `mov ecx,[ebp-0xc]` @`0x0110E33D` → arg3; callee reads `movzx ecx, word [ebp+0x10]` @`0x00CD040B` |
| 6 | u8 | **result** (0 = fail, non-0 = success) | `mov eax,[ebp-0xa]` @`0x0110E343` → arg1; callee `cmp byte [ebp+8],0` @`0x00CD02E3` |

Dispatch path inside the handler: walk the room's creature list
(`0x1910570` size / `0x1910510` at), require `vt+0x34(0x11)` (kind test), cast via
`0xFCE2D0`, require `vt+0x74()`, then walk that creature's appendage list
(`vt+0x6A0`, size via `0x2394660`), `dynamic_cast` each with type descriptors
`0x0395EAA0` / `0x03970BA4` (`0x02C6386A`), require `vt+0x8C()` true, then
`call 0x00CD0290` — i.e. **the packet is delivered to whichever rhythm appendage on the
local player answers `vt+0x8C`**.

### 1.2 C→S — CMD `0x372`, sender `0x00CD0140` ("sendResult")

`MakePacket(0x372, 0)` @`0xCD01F0`; `AppendData([ebp-0x18], 0x14)` @`0xCD0202`; `SendPacket()`.
**20-byte body**, fully zeroed first (`movq [ebp-0x18],xmm0` / `movq [ebp-0x10],xmm0` /
`mov dword [ebp-8],0`), then three writes:

| off | type | value | proof |
|----|------|-------|-------|
| 0..13 | — | **all zero** (no other write to the buffer in this function) | `0x00CD0140`–`0x00CD01F0` |
| 14 | u16 | `myCharacter->[+0x89C]` | `mov word [ebp-0xa], ax` @`0xCD01BE`, `ax` = `0x0198C870` = `return *(int*)(this+0x89C)` |
| 16 | u16 | **client good total** = `Σ guard[0..3]` (`this+0x23C`, stride 8) | `mov word [ebp-8], bx` @`0xCD01B1`, `bx` accumulated in the 4-iteration loop at `0xCD0164` |
| 18 | u16 | **client fail total** = `bad(this+0x25C) + miss(this+0x264)` | `mov ax,[ebx]` (miss) `add ax, di` (bad) `mov word [ebp-6], ax` @`0xCD01DB`–`0xCD01EC` |

After sending: `this->0x1A4 = 1` (already-sent latch), `this->0x1BC = 5`,
combo state `0x9B4/0x9AC/0x9B0` cleared, `full_level_effect` UI stopped.

### 1.3 C→S — CMD `0x371`, three sites

`MakePacket(0x371, 0)` then `AppendData(buf, 0x16)` — **22-byte body**, fully zeroed then:

| off | type | value |
|----|------|-------|
| 0..13 | — | zero |
| 14 | u16 | `myCharacter->[+0x89C]` (same as above) |
| 16 | u32 | `1` at the **good** site (`0xCD313C`), `0` at the **bad** (`0xCD357E`) and **miss** (`0xCD2584`/`0xCD259A`) sites |
| 20 | u16 | the note's guarded value `note->[+0x30]` (decrypted with key `0x30`, sub-index 2) |

Sites: `0xCD25F9` (miss path, note state set to 3), `0xCD319B` (good path, note state set
to 1), `0xCD35DC` (bad path, note state set to 2).

### 1.4 Packet-name cross-check (weak evidence — flagging provenance)

`packet-names.txt` in SCRATCH maps `0x371 → ENUM_CMDPACKET_EVENT_DUNGEON_DESTORY_OBJECT`
and `0x372 → ENUM_CMDPACKET_EVENT_DUNGEON_CLEAR_ROOM`. That table's provenance is not this
image, **but** both names fit the observed behaviour exactly (0x371 fires once per
destroyed luggage object; 0x372 carries the end-of-round tally), and the neighbouring
registration `0x36D → 0x110E370` matches `EVENT_DNFTREND_GET_REWARD` whose literal
`CMDFUNC_ENUM_CMDPACKET_EVENT_DNFTREND_GET_REWARD` is present at `0x03241998` inside the
same handler blob. Treat the names as corroborated, the ids and layouts as proven.

### 1.5 What a server must mirror

* Reply to C→S `0x372` with S→C `0x372` = `{u16 pad0, u16 goodCount, u16 failCount, u8 ok}`.
  The client **does not trust its own tally for the outcome** — `this->0x1BC` becomes 6
  (success) or 7 (fail) purely from byte 6 of the reply (`0xCD030B` / `0xCD0374`).
* The client's own tally is logged next to the server's:
  `Rhythm : %s 체크 -> %s good[s:%d,c:%d] fail[s:%d,c:%d] allCount[s:%d,c:%d]`
  (`0x031B16F0`, emitted at `0xCD0431`). `s:` = server args, `c:` = client-computed
  (`good` = `Σ guard[0..3]`, `fail` = `bad+miss`, `allCount` = sum). A server that returns
  mismatching numbers still drives the outcome, but the log makes the divergence visible.
* Per-note `0x371` gives the server one message per luggage with its score value and a
  0/1 discriminator, so the server can recompute the tally itself.
* `setUID` (below) is the C←S binding of a spawned luggage object's UID to a pattern slot.

---

## 2. Scoring formulas (exact, from the disassembly)

Every counter is an anti-cheat **guarded int**: an 8-byte object `{void** vptr; int value}`
whose value is decrypted by `0x02C4D110(&value, 4, key, subIdx)` and re-encrypted by
`0x02C4D070(&value, 4, key, subIdx)`. Appendage-owned guards use key `0x31`; note-owned
guards use key `0x30`.

**Good hit** (`0x00CD308C`–`0x00CD30F3`):
```
noteValue = decrypt(note + 0x30, key=0x30, idx=2)
lane      = decrypt(note + 0x20, key=0x30, idx=0)
decrypt(this + 0x23C + lane*8, key=0x31, idx=0)
*(this + 0x23C + lane*8) += noteValue          ; lea edi,[ebx+edi*8+0x23c] @0x00CD30D8
encrypt(this + 0x23C + lane*8, key=0x31, idx=0)
note->state(+0xC) = 1 ; send 0x371 with off16 = 1 ; showJudge(1) -> good.ani
this->0x9B4++ ; if (this->0x9B4 >= 3) this->0x9AC = 1     ; @0x00CD33C0-0x00CD33D9
this->0x9C8 = GetTickCount()
```

**Bad hit** (`0x00CD34BB`–`0x00CD3514`):
```
note->state(+0xC) = 2
this->0x25C += decrypt(note + 0x30)            ; the "bad" accumulator, key 0x31 idx 1
play GBL_CREATURE_01 / GBL_CREATURE_02 on this->0x960 (alternating, 0x22D2DD0 & 0x80000001)
send 0x371 with off16 = 0 ; showJudge(2) -> bad.ani
this->0x9B4 = 0 ; this->0x9AC = 0 ; this->0x9B0 = 0   ; combo reset
```

**Miss** (`0x00CD2524`–`0x00CD2579`, inside the AABB sweep `0x00CD20D0`):
```
note->state(+0xC) = 3
this->0x264 += decrypt(note + 0x30)            ; the "miss" accumulator, key 0x31 idx 2
send 0x371 with off16 = 0 ; showJudge(3) -> miss.ani
```

**Totals used in the C→S 0x372 and in the log**
```
good  = guard[0] + guard[1] + guard[2] + guard[3]    (this+0x23C, +0x244, +0x24C, +0x254)
fail  = this->0x25C (bad) + this->0x264 (miss)
all   = good + fail
```

**End-of-round triggers for `sendResult()` (`0x00CD0140`)** — three, all gated on
`this->0x1A4 == 0`:
1. `0x00CD06B3` — after the last note in `0x00CD0480` resolves.
2. `0x00CD26E3` — end of the AABB sweep: last note state != 0, no live note, and
   `this->0x1BC == 4`.
3. `0x00CD36D4` — timeout in `proc`: `GetTickCount() - this->0x9A8 > this->0x988 + this->0x984 + 0x2710` (10000 ms grace).

**Hit test** (`0x00CD20D0`): axis-aligned box. Center offset = `this->0x284/0x288/0x28C`
(floats) added to the note object's `getX/getY/getZ` (`vt+0xC4/0xC8/0xCC`); half-extents =
`this->0x290/0x294/0x298`. All three axes must be inside (`0x00CD2421`–`0x00CD2458`,
`byte [ebp+eax-0x44]` per axis, all three required non-zero).

---

## 3. State machine — `this + 0x1BC`

| value | set at | meaning |
|------|--------|---------|
| (init) | `0x00CD521F` (`mov [esi+0x1BC], ebx`, in the resource-load tail) | idle |
| 4 | `0x00CD0DB5` (`startRound`, `0x00CD0DB0`) | round running |
| 5 | `0x00CD0220` (in `sendResult`) | tally sent, waiting for server |
| 6 | `0x00CD030B` (`setResult`, arg1 != 0) | server said success — plays `MISSION_COMP`, `++myChar->[0x898]`, then `openPopupWindow` path via `0x23010C0(0x2A1)` |
| 7 | `0x00CD0374` (`setResult`, arg1 == 0) | server said fail — plays `STRENGTHEN_FAIL` |

Read back at `0x00CD037E` (picks the `"success"`/`"fail"` client-side string),
`0x00CD0A55`, `0x00CD26D8`.

`setResult` also resets and restarts timer `this+0x218` (`0x27C3410` then
`0x27C3B60(0,0)` at `0x00CD02CF`/`0x00CD02DE`); `proc` destroys the appendage 5000 ms later
(`0x00CD28C0 cmp eax,0x1388` → `vt+0x88(0)`, logging `warning!! already Destroyed!` if
`this->0x234` was already 1).

---

## 4. Object layout — `CNEventRhythmAction`, size `0xA4C`

Constructor `0x00CD4240` (called from `AppendageFactory` at `0x00B0ACD4`:
`push 0xA4C; call operator new; push classId; call 0xCD4240`).
Base ctor `0x01A00330` stores the class id at `+0x11C` (via `0x01A000D0`).
**vptr `[this+0] = 0x031B1B9C`** (`mov dword [esi], 0x31b1b9c` @`0x00CD4278`).

| offset | type | proof |
|--------|------|-------|
| `0x000` | vptr = `0x031B1B9C` | `0x00CD4278` |
| `0x11C` | int classId = 323 | base ctor `0x01A000D0` |
| `0x138` | list/queue head (8-byte node alloc) | `0x00CD427E`–`0x00CD42E4` |
| `0x160` | `std::vector<Note>` proxy | `lea ebx,[esi+0x160]`, `0x00CD42E7` |
| `0x164` | `Note* _Myfirst` | `0x00CCF74D`, `0x00CD04BC`, `0x00CD4AE0` |
| `0x168` | `Note* _Mylast` | `0x00CCF746`; `+= 0x34` on push_back @`0x00CD4B2C` |
| `0x16C` | `Note* _Myend` | `0x00CD4AC2`, `0x00CD4AF7` |
| `0x174` | u32 tick base of a deferred call | `0x00CD292D` |
| `0x178` | int arg of that deferred call | `0x00CD293B`; written `= patternIdx` @`0x00CD0847` |
| `0x17C` | u32 delay ms | `0x00CD2933` |
| `0x180` | int, cleared on round start | `0x00CD0DDE` |
| `0x184` | int, elapsed captured when a uid arrives | `0x00CCF7DF`; cleared `0x00CD0684`, `0x00CD3F43` |
| `0x188` | timer/stopwatch object, `0x1C` bytes (class ctor `0x27C3A90`) | `0x00CCF7BF`, `0x00CD067E` |
| `0x1A4` | u8 "result already sent" latch | set `0x00CD0219`; tested `0x00CD06A8`, `0x00CD26B2`, `0x00CD369E`; cleared `0x00CD3F55` |
| `0x1AC`–`0x1B8` | int[4] from table params 4..7 | `mov [esi+eax+0x19C], ecx` loop @`0x00CD496F`, `eax = 0x10..0x1C` |
| `0x1BC` | int state (see §3) | |
| `0x1C0` | u8 flag set when `[this+0xA21]` non-zero, and on box-overlap | `0x00CD29FB`, `0x00CD24AC` |
| `0x1C1` | u8 input-repeat latch | `0x00CD2A68`, `0x00CD2B0A`, cleared `0x00CD3F91` |
| `0x1C4`,`0x1D4`,`0x1E4` | 3 × `0x10`-byte objects (ctor `0x28377A0`), each passed to `vt+0x2C` of the singleton returned by `0x1648E70(0x1A0, "CNEventRhythmAction::proc")`; result byte per index drives the 3 lanes | ctor loop `0x00CD4330`–`0x00CD434D`; use loop `0x00CD2A20`–`0x00CD2A50` |
| `0x1F4` | int **patternCount** = table param[0] | `0x00CD48D7`, loop bound `0x00CD4B97` |
| `0x1F8` | int, table param[3]; used as `elapsed >= this->0x1F8 + 0x1220` | `0x00CD4937`, `0x00CD288F` |
| `0x1FC` | timer, `0x1C` bytes — fires `startRound` (`0xCD0DB0`) | `0x00CD2875`–`0x00CD28A1` |
| `0x218` | timer, `0x1C` bytes — 5000 ms post-result self-destroy | `0x00CD28A6`–`0x00CD2917` |
| `0x234` | u8 "destroy already requested" | `0x00CD28C7`, `0x00CD2917` |
| `0x238` | guarded-int **array[4]**, stride 8, vtable `0x031B1460` — the per-lane **good** counters (values at `0x23C/0x244/0x24C/0x254`) | `__ehvec_ctor(this+0x238, size=8, count=4, ...)` @`0x00CD4377`–`0x00CD4382` |
| `0x258` | guarded int, vtable `0x031B1468` — **bad** total, value at `0x25C` | `0x00CD438B` |
| `0x260` | guarded int, vtable `0x031B1470` — **miss** total, value at `0x264` | `0x00CD439E` |
| `0x268` | guarded int, vtable `0x031B1478` — table param[1], value at `0x26C` | `0x00CD43B1`, written `0x00CD4903` |
| `0x270` | int, count of notes whose `kind < 4` | `inc` @`0x00CD4B3C`, cleared `0x00CD3F4F` |
| `0x274` | int, table param[B+0] | `0x00CD4BC7` |
| `0x278` | int, table param[B+1] | `0x00CD4BEA` |
| `0x284`,`0x288`,`0x28C` | float hit-box center offset (x,y,z), from params B+2..B+4 | `0x00CD4C2A`, `0x00CD4C6E`, `0x00CD4CB2`; used `0x00CD2269`, `0x00CD22B5`, `0x00CD230D` |
| `0x290`,`0x294`,`0x298` | float hit-box half-extents, from params B+5..B+7 | `0x00CD4CF6`, `0x00CD4D3A`, `0x00CD4D7E`; used `0x00CD2356`, `0x00CD237B`, `0x00CD23AC` |
| `0x2B4` | int[42], from the next 42 params | `mov [esi+edx*4+0x2B4], ecx` @`0x00CD4DDA`, bound `cmp ecx,0x2A` |
| `0x35C` | int[42], from the next 42 params | `mov [esi+ebx*4+0x35C], edx` @`0x00CD4E1F` |
| `0x404` | string[42], each `0x20` bytes, from the **second** argument vector | `__ehvec_ctor(this+0x404, size=0x20, count=0x2A, ...)` @`0x00CD43DD`; filled `0x00CD4E7B` |
| `0x948` | int, current animation/motion id (reset to 4) | `0x00CD2AC0`, reset `0x00CD3F49` |
| `0x95C` | int, table param[B+8] | `0x00CD4DA8` |
| `0x960` | creature ptr for the judge/countdown sound source | `0x00CCFC02`, `0x00CD3524`, `0x00CD348E` |
| `0x964` | list head (8-byte node) | `0x00CD43E9` |
| `0x98C` | timer object | `0x00CD4424` |
| `0x9A8` | u32 round-start tick | `0x00CD0DD8`, `0x00CD36A7` |
| `0x9AC` | u8 "full-level" flag (combo ≥ 3) | `0x00CD33D9` |
| `0x9B0` | int | cleared `0x00CD0237`, `0x00CD361B` |
| `0x9B4` | int combo counter | `inc` `0x00CD33C0`, cleared `0x00CD360E` |
| `0x9C8` | u32 tick of last good hit | `0x00CD33CD` |
| `0x9CC`–`0x9CF` | u8[4] "lane enabled", reset to `1,1,1,0` | `0x00CD3F2B`–`0x00CD3F43`; consulted per note kind at `0x00CD4A5E` |
| `0x9D0` | `CNRDAnimation*` — the `startCount.ani` countdown | `0x00CCFBE6` |
| `0x9D8`,`0x9D9`,`0x9DA`,`0x9DB` | u8 one-shot latches for the countdown cues | `0x00CCFBF1`/`0x9DB`, `0x00CCFC37`/`0x9DA`, `0x00CCFC7A`/`0x9D9`, `0x00CCFCBD`/`0x9D8` |
| `0xA04` | timer object | `0x00CD446F` |
| `0xA21` | u8, forces `0x1C0` | `0x00CD29F2` |
| `0xA28` | u8, cleared on reset | `0x00CD3FE9` |

### 4.1 The Note ("luggage") element — `0x34` bytes

Constructor `0x00CD1200`, destructor `0x00CD12C0`, copy-ctor `0x00CCFAB0`,
range-copy helper `0x00CD2790`, range-destroy helper `0x00CD12C0`.

| offset | type | init | meaning / proof |
|--------|------|------|-----------------|
| `+0x00` | int | 0 | `(kind >= 4) ? 1 : 0` — `setge cl` @`0x00CD49C7`; tested `0x00CD04EE` (`!=0` → alternate branch), `0x00CD4B33` |
| `+0x04` | int | `-1` | **server object UID**; `-1` = unassigned. Assigned by `setUID` @`0x00CCF787`; compared against `obj->vt+0x4C()` @`0x00CD2251`, `0x00CD329D` |
| `+0x08` | u8 | 0 | "entered hit box" — set 1 @`0x00CD24A4`, tested `0x00CD24DC` |
| `+0x0C` | int | 0 | **judge state**: 0 = pending, 1 = good (`0x00CD312B`), 2 = bad (`0x00CD34BB`), 3 = miss (`0x00CD2524`) |
| `+0x10` | int | 0 | compared against an animation handle @`0x00CD2CF5`, and against a counter @`0x00CD305E` |
| `+0x14` | u8 | 0 | `0x00CD1298` |
| `+0x18` | int | 0 | `0x00CD12A5` |
| `+0x1C` | vptr | `0x031B1448` | guarded int #0, value at `+0x20` = **kind / lane** (table param `p[8+3i+0]`) |
| `+0x24` | vptr | `0x031B1450` | guarded int #1, value at `+0x28` = table param `p[8+3i+1]` |
| `+0x2C` | vptr | `0x031B1458` | guarded int #2, value at `+0x30` = **score value** (table param `p[8+3i+2]`), added to the good/bad/miss accumulators |

---

## 5. Table layout — `setParam` (`0x00CD4890`)

Signature: `setParam(std::vector<int>& ints /*[ebp+8]*/, std::vector<std::string /*0x20*/>& strs /*[ebp+0xC]*/)`.
Every access is bounds-checked against `(end-begin)>>2`, so the index map is exact.
Let `N = p[0]` and `B = 8 + 3*N` (`lea ebx,[eax+eax*2+8]` @`0x00CD4BAF`).

```
p[0]                = patternCount N            -> this+0x1F4
p[1]                                            -> guarded this+0x26C
p[2]                = bool flag                 -> gates the per-lane enable at 0x9CC
p[3]                                            -> this+0x1F8
p[4..7]                                         -> this+0x1AC, 0x1B0, 0x1B4, 0x1B8
for i in 0..N-1:                                   ; one Note pushed per iteration
    p[8+3i+0] = kind   -> note+0x20 (guarded), note+0x00 = (kind>=4)
    p[8+3i+1]          -> note+0x28 (guarded)
    p[8+3i+2] = score  -> note+0x30 (guarded)
    if kind's lane byte this+0x9CC[kind] == 1 and !p[2]: this+0x9CC[kind] = 0
    if note+0x00 == 0: this+0x270++
p[B+0]              -> this+0x274
p[B+1]              -> this+0x278
p[B+2..B+4]  int->float (0x27BBF40) -> this+0x284, 0x288, 0x28C   ; box center offset
p[B+5..B+7]  int->float             -> this+0x290, 0x294, 0x298   ; box half-extents
p[B+8]              -> this+0x95C
p[B+9  .. B+50]     -> this+0x2B4[0..41]        ; written 3 at a time, 14 groups
p[B+51 .. B+92]     -> this+0x35C[0..41]        ; same shape
strs[0..41]         -> this+0x404[0..41]        ; 0x20-byte string each
```
The 42-entry tables are indexed at use time by `laneIdx + 3 * K` where `laneIdx ∈ {0,1,2}`
and `K` comes from the owner creature's `vt+0x5E0` (`0x00CD2B35`, `0x00CD2B84`,
`0x00CD2BE0`, `0x00CD2C39`), so 42 = 3 lanes × 14 values of `K`.
`this+0x404[i]` is passed to the creature's `vt+0x1B8` (play-by-name) at `0x00CD2C65`, and
compared with `0x00423CD0` at `0x00CD2C0F`.

---

## 6. Public surface (functions called from outside the module)

| VA | callers | what it does |
|----|---------|--------------|
| `0x00CCF740` | `0x01476B40` = `CNConveyer::createLuggage` (`CNConveyer.cpp`) | **`setUID(int patternIdx, int uid)`** — `ret 8`. Bounds-checks `patternIdx` against `(0x168-0x164)/0x34`; if `note[i].uid == -1` writes it and logs `list update pattern[%d] uid[%d]` (line 0x773 = 1907), else logs `set ID error pattern[%d] old uid[%d] now uid[%d]` (line 0x77E = 1918). Then, if timer `0x188` is running, captures its elapsed into `this+0x184` and restarts it. The conveyer only calls this after `obj->[+0x74] == 0x14A (330)` and a `dynamic_cast` with type descriptors `0x0395EAA0`/`0x03970BA4` succeeds — i.e. **the rhythm appendage's data-table index is 330** (the factory class id, stored at `+0x11C`, is 323 — two different id spaces). |
| `0x00CD0290` | `0x0110E1C0` (packet `0x372` handler) | **`setResult(bool ok, u16 srvGood, u16 srvFail)`** — `ret 0xC`. See §1.1/§3. Logs at line `0x62C` = 1580. |
| `0x00CD3DC0` | `0x01A0ED50` | small helper (176 bytes), 4 callees |
| `0x00CD4240` | `0x00B076B0` = `AppendageFactory` case `0x00B0ACD4` | **constructor**, appendage class id 323, `sizeof = 0xA4C`, vptr `0x031B1B9C` |
| `0x00CD4890` | `0x0267DFA0` | **`setParam(vector<int>&, vector<string>&)`** — §5 |
| `0x00CD56B0` | `0x00B076B0` (factory case for id 324) | constructor of a **different, small companion appendage class** (`sizeof = 0x138`, vtable `0x031B1E9C`) — see span caveat |
| `0x00CD5700` | `0x015FB460` | 480 bytes; belongs with `0x00CD56B0` |

### Virtual overrides (vtable `0x031B1B9C`, decoded from the image)
```
vt[0]  +0x00 = 0x00CD4860   (deleting destructor thunk)
vt[1]  +0x04 = 0x00CD2840   CNEventRhythmAction::proc      <- the tick entry
vt[5]  +0x14 = 0x00CD15F0
vt[8]  +0x20 = 0x00CCFCE0   (draw: calls base 0x1A004A0 then the effect draws)
vt[9]  +0x24 = 0x00CD0E10
```
(all other slots point outside this module = inherited)

### Internal functions worth naming
| VA | role |
|----|------|
| `0x00CD2840` | `proc()` — the per-frame state machine (log lines 0x165=357 … 0x293=659) |
| `0x00CD0140` | `sendResult()` — builds and sends C→S `0x372`, latches `0x1A4`, state → 5 |
| `0x00CD0DB0` | `startRound()` — state → 4, `0x9A8 = GetTickCount()`, calls `0x00CD0830(0)` |
| `0x00CD0830` | per-pattern setup; iterates `0..this->0x1F4`, writes `this->0x178` |
| `0x00CD0480` | resolves one note by index (`ret 4`), then `sendResult()` if `0x1A4 == 0` |
| `0x00CD1E60` | `showJudge(int type)` — **1 → good.ani, 2 → bad.ani, 3 → miss.ani**, spawned at the owner's x/y/z (`vt+0xC4/0xC8/0xCC`) |
| `0x00CD20D0` | AABB sweep over the room's objects; drives the **miss** path and sends `0x371` |
| `0x00CD3E70` | `reset()` — zeroes all guarded counters and the `0x9xx` block, sets `0x9CC..0x9CE = 1`, `0x9CF = 0`, `0x948 = 4` |
| `0x00CD1200` / `0x00CD12C0` / `0x00CCFAB0` / `0x00CD2790` | Note ctor / dtor / copy-ctor / uninitialized_copy (stride `0x34`) |
| `0x00CCF8C0`–`0x00CCFAB0` | the 7 guarded-int template thunks (vtables `0x031B1448`…`0x031B1478`) |
| `0x00CCFB90`+ | countdown cue driver: when the `startCount.ani` frame index passes 3 / 7 / 11 / 15, plays `HK1_COUNT_3`, `HK1_COUNT_2`, `HK1_COUNT_1` on `this->0x960` (**these are 3-2-1 countdown voice cues, not hotkey names**) |
| `0x00CD4890` tail (`0x00CD4F25`…`0x00CD544A`) | resource load: `Monster/Conveyer/rhythm.img`, `Map/Event/2011_Event_Dungeon/tutorialKey.img`, `Common/Etc/chn_EventMissionTitle.img`, `Map/Event/2011_Event_Dungeon/zone.img`, `…/rhythm/animation/startCount.ani`; also `mov [esi+0x1BC], ebx` at `0x00CD521F` |

---

## 7. Span correction

**The briefed span `0x00CCF740`–`0x00CD2790` is too narrow at the high end.**

* `functions.csv` treats `0x00CD2790` as one 5680-byte function; it is really at least three
  (`0x00CD2790` = the `0x34`-stride `uninitialized_copy` helper, ending `0x00CD2830`;
  `0x00CD2840` = `proc`, which is where the `CNEventRhythmAction::proc` literal is used).
* The file's literal block runs contiguously `0x031B13C8` → `0x031B1E58`. The last three
  literal groups (`0x031B1CB0` `startCount.ani`, `0x031B1D40` `zone.img`, `0x031B1D98`
  `chn_EventMissionTitle.img`, `0x031B1DF0` `tutorialKey.img`, `0x031B1E58` `rhythm.img`)
  are attributed to `0x00CD4890`, **past the briefed `hi`**, and are unmistakably rhythm
  assets. The next file's literals (`CNRDAppendageCreator.cpp`) do not start until
  `0x031B1FB0`, at function `0x00CD58E0`.
* Functions `0x00CD3DC0`, `0x00CD3E70`, `0x00CD4010`, `0x00CD41B0`, `0x00CD4240` (ctor),
  `0x00CD4510`, `0x00CD4890` (setParam) all touch this class's fields
  (`0x1BC`, `0x1A4`, `0x270`, the guarded counters) and are therefore part of this TU.
* **Real module ≈ `0x00CCF740` – `0x00CD56B0`.** The two trailing functions `0x00CD56B0`
  (ctor of appendage class 324, `sizeof 0x138`, vtable `0x031B1E9C`) and `0x00CD5700`
  reference no literal of this file; they are a small companion class that may or may not
  live in the same `.cpp`. I could not prove either way.

The briefing's `hi` is off because the extraction anchors on `__FILE__` *references*, and
the constructor/setParam half of the file happens to contain no `MSG(...)` call — the log
statements are concentrated in `setUID`, `setResult` and `proc`.

## 8. Things I could not determine

* The meaning of bytes 0..13 of both C→S bodies (`0x371` 22 B, `0x372` 20 B). They are
  provably zero-filled and never written. Likely a shared event-packet header used by other
  event types; nothing in this unit reveals it.
* Byte 0..1 of the S→C `0x372` body: read into the stack buffer, never referenced.
* The name/meaning of `CNCharacter+0x89C` (put on the wire at off 14 of both packets) and
  `+0x898` (incremented on rhythm success at `0x00CD0329`).
* What the creature's `vt+0x5E0` returns (the `K` in `laneIdx + 3*K`); range 0..13 is
  implied by the 42-entry tables but not proven from this unit.
* Whether appendage class 324 (`0x00CD56B0`) is declared in this `.cpp`.
