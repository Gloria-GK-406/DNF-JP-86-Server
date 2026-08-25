# TU 004 — `DNFClient\RDAR\CNEntryDungeonPartyManager.cpp`

Span (briefing): `0x00731460` – `0x00738790`, 69 functions.
**Real extent `0x00731460` – `0x00738D60`.** The `hi` in `tu-index.csv` is the *start* of the last
anchor function; `functions.csv` gives `0x00738790` a length of 1488 bytes, so the module body runs
to `0x00738D60`. Everything from `0x00738790` to `0x00738D36` — including the packet-handler
registration table at `0x00738C10` — belongs to this file.

Span is otherwise **correct**. Every decrypted literal in `0x030BBC84`–`0x030BCF8C` is this file's;
`0x030BCFB0` onward is `Interface2/pvp/pvp_practice/*` (next TU). Image base `0x00400000`.

---

## 1. What this unit implements

Client-side manager for the **two ways a character joins a dungeon that is already running**:

* **난입 / "barge-in"** — joining a party mid-dungeon (`ENTRY_INTO_PARTY`, `ENTRY_INTO_PARTY_FINISH`)
* **재합류 / "rejoin"** — coming back into the dungeon you were disconnected from
  (`REJOIN_DUNGEON`, `CANCEL_REJOIN_DUNGEON`, `DISCONN_DUNGEON_INFO`)

and, critically, the **peer-to-peer dungeon-state synchronisation handshake** that follows: the
late-joining client asks an already-present party member for the dungeon / map / object state, and
that member serialises it back. The manager drives a 7-state machine, a retrying request queue, and
a time-budgeted response queue.

Class is a singleton reachable through `0xA2ADD0` (`getEntryDungeonPartyManager`, gated on global
`[0x3A5C990]`). Constructor `0x00738380`, destructor `0x007384C0`, vptr constant `0x030BCE50`
(only slot 0 populated). Loads its table from **`Etc/EntryParty.etc`**
(`0x00738449 push 0x30BBC84` → `0x2A97D30(name, &this->m_etc /*+0x04*/)`).

---

## 2. Packet table — registration at `0x00738C10`

This is the highest-value item. `0x1637660` registers a **CMD response** handler, `0x1637720`
registers a **NOTI** handler. Both take `(u16 packetId, void* fn, 0)` after a `0x1102C30` (config
singleton) fetch.

### CMD response handlers — `call 0x1637660`

| id | dec | handler VA | `__FUNCTION__` literal proving it |
|---|---|---|---|
| `0x2C2` | 706 | `0x007352E0` | `CNEntryDungeonPartyManager::CMDFUNC_ENUM_CMDPACKET_ENTRY_INTO_PARTY` (`0x030BCB20`) |
| `0x2C3` | 707 | `0x00731490` | `...CMDFUNC_ENUM_CMDPACKET_ENTRY_INTO_PARTY_FINISH` (`0x030BBD48`) |
| `0x2E0` | 736 | `0x007316D0` | `...CMDFUNC_ENUM_CMDPACKET_REJOIN_DUNGEON` (`0x030BC120`) |
| `0x2E1` | 737 | `0x00731AA0` | `...CMDFUNC_ENUM_CMDPACKET_CANCEL_REJOIN_DUNGEON` (`0x030BC1F8`) |

Evidence: `0x00738C13 push 0x7352E0 / 0x00738C18 push 0x2C2 / 0x00738C26 call 0x1637660`, and the
three identical blocks that follow.

**Handler ABI** (`__cdecl`, callee does not clean — `0x0073158B ret`, `0x00731C9B ret`):

```
void CmdFunc(void* pParam /*[ebp+0x08]*/, bool bSuccess /*[ebp+0x0C]*/, u8 nErrorCode /*[ebp+0x10]*/)
```
Proof: `0x00731493 cmp byte ptr [ebp+0xC], 0` then `0x0073149D mov al, byte ptr [ebp+0x10]`.

### NOTI handlers — `call 0x1637720`

| id | dec | handler VA | wire body read by the handler |
|---|---|---|---|
| `0x291` | 657 | `0x007347F0` | `u32 count; count × { u32 mapX; u32 mapY; u32 key }` |
| `0x292` | 658 | `0x00735F30` | `u16 subjectCharIdx; u32 count; count × { u16 memberIdx; u8 f1; u8 f2; u32 mapX; u32 mapY }` |
| `0x293` | 659 | `0x00738A90` | `u16 charIdx; u32 mapX; u32 mapY` |
| `0x294` | 660 | `0x00736130` | `u32 count; count × { u32 objId; u8 flag }` |
| `0x2A8` | 680 | `0x00731590` | `u32 partyIndex; u32 dungeonKey; u8 flag` — `NOTIPACKET_DISCONN_DUNGEON_INFO` (`0x030BBE68`) |
| `0x2A9` | 681 | `0x00734910` | `u16 charIdx` → `addReservedEntryInfo` |
| `0x2AA` | 682 | `0x007371E0` | `u16 charIdx` — `NOTIPACKET_CANCEL_REJOIN_DUNGEON` (`0x030BCD78`) |
| `0x2AB` | 683 | `0x00733E10` | `u32 charIdx` → string-table message + window |

Read primitives used are `0x027C5230` (u8), `0x027C52A0` (u16), `0x027C5310` (u32) — a *different*
set from the `0x023C5xxx` addresses in the task brief; these read a global cursor at `[0x3BAAE38]`
with remaining at `[0x3BAAE3C]` (`0x027C5310`: `mov eax,[0x3BAAE38]; mov ecx,[eax]; ... sub dword ptr [0x3BAAE3C], 4`).

### Outbound packets sent directly by this unit

Send stream is the singleton at `[0x3A5C9B0]`; class ctor `0x022E8760`, vtable **`0x035B9264`**
(recovered statically — the global is NULL in the dump). Slot semantics verified by disassembling
each thunk:

| vt slot | VA | meaning |
|---|---|---|
| `+0x08` | `0x022E4BA0` | `beginPacket(u16 id, u8 flag)` — writes `byte[0]=flag; word[1]=id`, cursor += 7 (**7-byte header**) |
| `+0x20` | `0x022E5310` | `write_u8` |
| `+0x24` | `0x022E5330` | `write_u16` |
| `+0x28/0x2C` | `0x022E5350` | `write_u32` |
| `+0x30` | `0x022E5370` | `write_u64` |
| `+0x34` | `0x022E5390` | `write_bytes(ptr, len)` |
| `+0x38` | `0x022E53C0` | `write_u32(len); write_bytes(ptr, len)` — length-prefixed blob |

Dispatch: `0x022E56F0(id, x)` → `netMgr->vt+0x44(id, -1, x, 3)` = **undirected/broadcast**;
`0x022E56D0(id, target)` → `netMgr->vt+0x44(id, target, 0, 3)` = **directed at one peer**.

| id | dec | sender | direction | body |
|---|---|---|---|---|
| `0x6E` | 110 | `SyncRequest::sendSyncRequestPacket` `0x00733850` | broadcast (`0x22E56F0(1,0)`) | `u16 a; u16 b; u16 c; u16 seat; bytes[4] tick; u16 count; count × u32` |
| `0x6F` | 111 | `SyncRequest::sendSyncResponsePacket` `0x00732AE0` | to member `SR+0x08` | `u16 type; u16 b; u16 member; u16 d` — header only, sent at `0x00732B6B` *before* the type switch |
| `0x72` | 114 | ditto, `type == 0 (DUNGEON)` | to member | length-prefixed blob (0x400-byte serialiser at `[ebp-0x28]`, `0x119D420`/`0x2868650`/`0x171CF30`) via vt+0x38 |
| `0x73` | 115 | ditto, `type == 1 (MAP)` | to member | `u16 count; count × { u16 obj->vt[0x48](); u16 obj->vt[0x4C]() }` |
| — | — | ditto, `type == 2 (OBJECT)` | to member | serialised via `0x1764E00(serializer, buf, member)` / `0x17B1250`; no direct `beginPacket` in this branch |

Note the literal names of the string constant pushed to `beginPacket` are absent; ids are raw
immediates (`0x007338BB push 0x6E`, `0x00732B1A push 0x6F`, `0x007330B6 push 0x72`,
`0x00732EB4 push 0x73`).

---

## 3. Reject-code tables (server must mirror these enum values)

Every failure branch logs a Korean reason string via `0x27BE270(0, __FILE__, __FUNCTION__, line, msg)`.
Line numbers come from the immediate pushed just before the `__FUNCTION__` literal.

### `0x2C2` ENTRY_INTO_PARTY — barge-in attempt (`난입 시도실패`), handler `0x007352E0`
Chain at `0x00735480`: `sub eax,3 / je` → `sub eax,0x10 / je` → `dec eax / jne`.

| code | line | message |
|---|---|---|
| `0x03` | 1135 | 요청자 정보 오류 — requester info error |
| `0x14` | 1138 | 파티장 정보 오류 — party-leader info error |
| `0x13` | 1141 | 목표 파티가 없음 — target party missing |

Success path (`bSuccess != 0`): consumes **two u32** off the stream (`0x00735322`, `0x0073532B`) —
both discarded — then sets `m_entryState = 3 (ENTER_DGN_REQUEST)` at `0x00735371`.

### `0x2C3` ENTRY_INTO_PARTY_FINISH — barge-in completion (`난입 완료실패`), handler `0x00731490`
Only acts on failure (`0x00731497 jne` returns on success). No stream reads.

| code | line | message |
|---|---|---|
| `0x03` | 1158 | 요청자 정보 오류 |
| `0x15` | 1161 | 목표 파티가 없음 |

### `0x2E0` REJOIN_DUNGEON (`재합류 요청실패`), handler `0x007316D0`
Jump table: index byte at `0x00731A84`, targets at `0x00731A64`, domain `code ∈ [3, 0x1E]`.

| code | line | message |
|---|---|---|
| `0x03` | 1322 | 요청자 정보 오류 |
| `0x15` | 1325 | 재합류 목표 파티가 존재하지 않음 — rejoin target party does not exist |
| `0x12` | 1328 | 이미 재합류 중인 유저가 존재 — a user is already rejoining |
| `0x16` | 1331 | 요청 던전이 존재하지 않음 — requested dungeon does not exist |
| `0x17` | 1334 | 요청 던전에 재합류가 불가능 — rejoin not allowed for that dungeon |
| `0x08` | 1337 | 보스방을 플레이 중 — boss room in progress |
| `0x1E` | 1345 | 내가 튕겼던 던전이 아님 — not the dungeon I disconnected from |
| all others in `[3,0x1E]` | — | default: tail-jump to UI window `vt+0x78` (`0x0073179B`) |

Success path: `0x007316F3 jmp 0x9C7100` on the window-`0x22` object. No stream reads.

### `0x2E1` CANCEL_REJOIN_DUNGEON (`재합류취소 요청실패`), handler `0x00731AA0`
Jump table: index byte at `0x00731CB0`, targets at `0x00731C9C`, domain `code ∈ [3, 0x16]`.

| code | line | message |
|---|---|---|
| `0x03` | 1373 | 요청자 정보 오류 |
| `0x15` | 1376 | 재합류 목표 파티가 존재하지 않음 |
| `0x12` | 1379 | 목표 파티에 재합류 요청자가 존재하지 않음 — no rejoin requester in target party |
| `0x16` | 1382 | 목표 파티의 재합류 요청자와 자신이 같지 않음 — requester is not me |
| all others | — | default `vt+0x78` |

**Caveat worth flagging:** "target party missing" is `0x15` in `0x2C3` / `0x2E0` / `0x2E1` but `0x13`
in `0x2C2`, where `0x14` is a separate "leader info error". Reported as measured, not harmonised.

---

## 4. State machine — `setEntryState` @ `0x00735120`

`m_entryState` is at **`this+0x48`** (`0x00735194 mov dword ptr [edi+0x48], esi`), domain 0–6
(`0x0073514C cmp esi, 6 / ja`), jump table at `0x007352C0`.

Name array base **`0x039A7AF8`, stride `0x20`** (std::wstring, `+0x04` = 8-wchar SSO buffer or heap
ptr, `+0x14` = size, `+0x18` = capacity; `cap >= 8` ⇒ heap). Mapping proven by the static
initialiser at `0x02EFFB08`–`0x02EFFE04`, which assigns each decrypted literal to a fixed address:

| val | slot VA | name |
|---|---|---|
| 0 | `0x039A7AF8` | `NONE` |
| 1 | `0x039A7B18` | `PARTY_REQUEST` |
| 2 | `0x039A7B38` | `PARTY_SUCCESS` |
| 3 | `0x039A7B58` | `ENTER_DGN_REQUEST` |
| 4 | `0x039A7B78` | `ENTER_DGN_SUCCESS` |
| 5 | `0x039A7B98` | `SYNC_DGN_REQUEST` |
| 6 | `0x039A7BB8` | `SYNC_DGN_SUCCESS` |

Side effects per target state (`0x007352C0` table):

| state | target | action |
|---|---|---|
| 0 NONE | `0x0073517F` | `0x289E620(0)->0x289F440()` (unblock input) + `closeEntrySyncWaitNotice` |
| 1,2,3 | `0x00735158` | `0x289E620(0)->0x289F3E0()` (block input) |
| 4 ENTER_DGN_SUCCESS | `0x00735168` | `0x289E620(0)->0x289F440()` |
| 5 SYNC_DGN_REQUEST | `0x00735178` | `openEntrySyncWaitNotice` (`0x00734A60`) |
| 6 SYNC_DGN_SUCCESS | `0x0073518F` | unblock + `closeEntrySyncWaitNotice` |

Every write logs `[ENTRY] state : %s` (line 446 / `0x1BE`).

### Observed transitions

| where | transition |
|---|---|
| `0x00735371` (CMD `0x2C2` ok) | → 3 `ENTER_DGN_REQUEST` |
| `0x007380CF` (`onChangeModule`, `newModule ∈ {3,9,0x0A,0x0D,0x13,0x14}`, only if state == 3) | 3 → 4 `ENTER_DGN_SUCCESS` |
| `0x007381D5` (same function, after `openEntrySyncWaitNotice`) | 4 → 5 `SYNC_DGN_REQUEST` |
| `0x00736B9E` (in `0x00736910`, called from `proc` when request queue drains) | 5 → 6 `SYNC_DGN_SUCCESS`, logs `[ENTRY] 동기화 완료` line 1097 |
| `0x0073885A` (in `0x00738790`, member-entered callback, only if state == 6) | 6 → 0 `NONE` |
| `0x007370DC` (in `proc`, no sync target available) | → 0 `NONE`, forced dungeon abandon |
| `0x007367A0` (`0x00736760`, cancel path) | → 0 `NONE` |

---

## 5. `proc()` @ `0x00736D10` — the loop the server's pacing must tolerate

```
now = tick()                                        ; 0x164F710
if (this->m_requestCount /*+0x24*/ != 0)
    for node in this->m_requestMap /*+0x18*/:
        if (node->[+0x20] + node->[+0x38] < now)     ; lastSendTick + retryInterval
            SyncRequest::sendSyncRequestPacket(node + 0x10)   ; CMD 0x6E, resend

if (this->m_responseCount /*+0x38*/ != 0)
    freq = QueryPerformanceFrequency()   ; [0x030291E0]
    t0   = QueryPerformanceCounter()     ; [0x030291DC]
    for node in this->m_responseMap /*+0x2C*/:
        SyncRequest::sendSyncResponsePacket(node + 0x10)      ; CMD 0x6F (+0x72/0x73)
        erase(node)                                           ; 0x00735680
        if ((QPC() - t0) / freq > 0.001)                      ; double @ 0x030BBC68 == 0.001
            log "[ENTRY] 동기화 응답 proc 당 최대시간 초과"  (line 1087); break

if (this->m_entryState == 5 /*SYNC_DGN_REQUEST*/):
    if (m_requestCount == 0):
        log "[ENTRY] 동기화 완료" (line 1097); -> state 6
    if (pickSyncTargetSeat() == -1):                          ; 0x007336D0
        log "[ENTRY] 동기화 요청 대상 없음, 강제로 던전 포기" (line 1103)
        msgBox(stringId 0xBD1B); leave dungeon (0x274CA50(0x2A,0) / 0x274D1E0 / 0x274E6D0)
        closeEntrySyncWaitNotice(); state = 0 NONE
```

**Per-`proc` response budget is a hard 1.0 ms** (`fld qword ptr [0x030BBC68]` = `0.001`,
`fcompi` at `0x00736EB9`). A server that dumps a large response set will see it drip out one
`proc` at a time.

`0x007336D0` (`pickSyncTargetSeat`) walks **8 party seats** (`and esi, 0x80000007` from
`[0x03965B98] + i + 1`, so it round-robins starting after a global cursor), tests each against the
alive-flag map at `0x039A7BD8`, returns a seat or `-1`. Party size 8 is confirmed again at
`0x00736990 cmp esi, 8 / jl`.

---

## 6. Object layout

### `CNEntryDungeonPartyManager` — from ctor `0x00738380` + reset `0x007375C0`

| off | type | evidence |
|---|---|---|
| `+0x00` | vptr = `0x030BCE50` | `0x007383B0 mov dword ptr [esi], 0x30BCE50`; `[0x030BCE50] = 0x007384C0` (dtor) |
| `+0x04` | `.etc` record object | `0x007383AD lea ecx,[esi+4]; call 0x2BAFCE0`; `0x00738449` registers `Etc/EntryParty.etc` into it |
| `+0x18` | map/list — **pending sync requests** | ctor `0x105B3E0` @ `0x007383C3`; iterated in `proc` `0x00736D50` |
| `+0x20` | sentinel node ptr for `+0x18` | `0x007375F7 mov eax,[esi+0x20]` … `mov [eax],eax` |
| `+0x24` | `u32` request count | `0x00737617 mov [esi+0x24], 0`; `0x00736D45 mov eax,[ebx+0x24]` |
| `+0x2C` | map/list — **pending sync responses** | ctor `0x105B3E0` @ `0x007383CF`; iterated in `proc` `0x00736DE5 add ebx,0x2C` |
| `+0x34` | sentinel node ptr for `+0x2C` | `0x0073761A mov edx,[esi+0x34]` |
| `+0x38` | `u32` response count | `0x0073763A mov [esi+0x38], 0`; `0x00736DC6 mov eax,[ebx+0x38]` |
| `+0x40` | `u32` = 0 | `0x00737640` |
| `+0x44` | `u32` = 0 | `0x00737643` |
| `+0x48` | `u32` **m_entryState** (0..6) | `0x00735194`, `0x00736F47 cmp [ebx+0x48], 5` |
| `+0x4C` | container of `{u32 key, u32 mapX, u32 mapY}` — **sync target list** | ctor `0x007383D4` (8-byte sentinel + `+0x50/+0x54/+0x58` zeroed); filled by NOTI `0x291` @ `0x007348E7`; walked at `0x00735E4B lea ecx,[esi+0x4C]`, stride `0xC` (`0x00735F0F add esi, 0xC`) |
| `+0x60` | set of `u16` — **reserved barge-in (난입 예약) char indices** | ctor `0x00738407`; `0x0073461F lea ecx,[esi+0x60]; call 0x155D110` (insert) inside `addReservedEntryInfo` |
| `+0x74` | `u16` = `0xFFFF` on reset | `0x00737653 mov ecx, 0xFFFF; mov word ptr [esi+0x74], cx` |
| `+0x76` | `u8` flag = 0 | `0x00737660`; tested `0x00736944 cmp byte ptr [edi+0x76], bl` |
| `+0x78` | `u32` | `0x00737683` |
| `+0x7C` | ptr, freed on reset | `0x0073766E mov edi,[esi+0x7C]` |

`sizeof >= 0x80`.

### `SyncRequest` (nested helper, same .cpp) — the map's mapped_type, living at `node + 0x10`

| off | type | evidence |
|---|---|---|
| `+0x00` | `u16` **syncType** 0=DUNGEON / 1=MAP / 2=OBJECT | `0x00732B24 movzx edx, word ptr [esi]` written first; switch `0x00732B75 sub eax,0 / dec / dec` |
| `+0x04` | `u16` | `0x00732B35` |
| `+0x08` | `u16` **target party member** | `0x00732B47`; used as the directed-send target `0x00732B65 mov eax,[esi+8]; push 1; call 0x22E56D0`, and printed as `파티원 (%d)` |
| `+0x0C` | `u16` **target seat** | `0x00733899 mov [ebx+0xC], eax` ← `pickSyncTargetSeat()` or the forced seat from `+0x2C` |
| `+0x10` | `u32` **lastSendTick** | `0x007338A4 lea esi,[ebx+0x10]; call 0x164F710; mov [esi],eax`; also sent raw as 4 bytes |
| `+0x14` | heap ptr | freed at `0x00732AC3` |
| `+0x18`/`+0x1C`/`+0x20` | `std::vector<u32>` begin/end/cap — payload keys | `0x00733916 mov edx,[ebx+0x1C]; sub edx,[ebx+0x18]; sar edx,2` |
| `+0x28` | `u32` **retryInterval** | `proc`: `node[+0x38] == SR[+0x28]` |
| `+0x2C` | `u32` forced-seat / handle, `-1` = none | `0x00733880 mov eax,[ebx+0x2C]; cmp eax,-1` |

Free helper `0x00732AA0` releases `+0x18` then `+0x14` and zeroes `+0x14..+0x20`.

### Sync-type name array

Base **`0x039A7A98`, stride `0x20`** (same wstring layout). Static init `0x02EFF8D7`–`0x02EFF9D1`:
`[0] 0x039A7A98 = DUNGEON`, `[1] 0x039A7AB8 = MAP`, `[2] 0x039A7AD8 = OBJECT`.

### Global map `0x039A7BD8`

`map<charKey, u8>` of per-member liveness. Cleared at `0x00735F3D` at the top of NOTI `0x292`,
written at `0x0073606E mov [eax], esi` with the *second* u8 of each member record, read back by
`pickSyncTargetSeat` (`0x00733726`).

---

## 7. Public surface (called from outside the unit)

| VA | ext callers | what it is |
|---|---|---|
| `0x00738380` | `0x019E8950` | **constructor** — the singleton is built here |
| `0x00736D10` | `0x01373050` | **`proc()`** — driven from the frame loop |
| `0x00738040` | `0x01644210` (×2) | **`onChangeModule(int oldModule, int newModule)`** — caller is `CNGameCore::onChangeModule` (literal `change module : [%s(%d)] -> [%s(%d)]` @ `0x032CDF30`, line 1061). If `oldModule ∈ {3, 9, 0x0A, 0x0D, 0x13, 0x14}` → `0x00736760` (cancel, state → NONE) at `0x0073808F`. If `newModule` is in that same set → `0x007380B8 cmp [esi+0x48], 3`, and on match runs `ENTER_DGN_REQUEST → ENTER_DGN_SUCCESS → SYNC_DGN_REQUEST` |
| `0x00735120` | `0x0118C3A0`, `0x019E7CA0` | **`setEntryState(u32)`** |
| `0x00736760` | `0x01120970`, `0x0118C3A0` | cancel entry: `closeEntrySyncWaitNotice` + state → NONE |
| `0x00735E00` | `0x0271FC30` | re-materialises sync-target maps from `+0x4C` |
| `0x00734750` | `0x01157A70` | (blob containing NOTI `0x291`/`0x2A9` handlers) |
| `0x00732870`, `0x00733310`, `0x007327D0`, `0x00732AA0`, `0x007337C0`, `0x00735D60`, `0x007379A0`, `0x00737DC0`, `0x007345B0`, `0x00731CF0`, `0x00731D70`, `0x00731DD0`, `0x00732080`, `0x00733BE0`, `0x00734E50` | 1–3 each | small accessors / container helpers |
| `0x00731460` | `0x019E7CA0` | `bool f(idx)` → `0x10CDCA0(idx)` then `0x171C900` |

**Base-rate warning about the briefing's "entry points" table.** Its top two rows —
`0x00731CD0` (19 callers) and `0x00731D10` (21 callers) — are **not this module's API**. They are
COMDAT-folded STL red-black-tree iterator step helpers (`0x00731CD8 cmp byte ptr [ecx+0x41], 0` is
the `_Isnil` test; `0x00731D12` walks `[eax+8]`/`[ecx+4]` = `_Right`/`_Left`). Their callers are 18
and 20 unrelated functions across the image. Same caution for `0x007355F0` (5 callers) — a
`std::pair` copy shim.

Similarly, `0x00734070`'s literals `Common/Etc/MonoLayer.img` (`0x0304662C`) and
`Interface2/Tutorial.img` (`0x0309D8AC`) live **outside** this file's literal block
(`0x030BBC84`–`0x030BCF8C`). Treat that function as shared/folded UI code, not evidence about
entry-party behaviour.

---

## 8. Named members recovered (from `__FUNCTION__` literals)

| VA | name |
|---|---|
| `0x00738380` | ctor (loads `Etc/EntryParty.etc`) |
| `0x007375C0` | reset/clear (called from ctor tail `0x00738440`) |
| `0x007320B0` | `updateCharacter(u16 memberIdx, bool bQuiet, bool bAlive, u32 mapX, u32 mapY)` — `ret 0x14` |
| `0x00732AE0` | `SyncRequest::sendSyncResponsePacket` |
| `0x00733850` | `SyncRequest::sendSyncRequestPacket` |
| `0x007336D0` | pick sync-target seat (returns seat or `-1`) |
| `0x007345E0` | `addReservedEntryInfo(u16 charIdx, u32 flag)` — inserts into `+0x60`, logs `[ENTRY] 난입 예약 : %s` |
| `0x00734A60` | `openEntrySyncWaitNotice` (UI `EntrySyncWaitNotice`, `0x030BCA5C`) |
| `0x00734C00` | `closeEntrySyncWaitNotice` |
| `0x00735120` | `setEntryState` |
| `0x00736D10` | `proc` |
| `0x00736910` | drain check → `SYNC_DGN_SUCCESS` |
| `0x00738790` | member-entered callback `f(u16 charIdx)`; on state 6 → NONE |
| `0x00738C10` | packet-handler registration |

`updateCharacter` argument mapping is proven by the call site at `0x007360BC`–`0x007360CC` (pushes
in reverse order from the NOTI `0x292` locals) and by the three log formats
`[ENTRY] 캐릭터 갱신 (%d) : 생성/실패/기존 (%d, %d) 생존 (%d)` at lines around `0x2D3` (723).
`0x007321B7` pushes `[ebp+0x14]`, `ebx=[ebp+0x18]`, and `setne` on `[ebp+0x10]` into that format.
`0x00732152` calls `0x17205D0([ebp+0x14], [ebp+0x18])` = map lookup by (x, y), which is what fixes
those two u32s as map coordinates.

---

## 9. Message / string-table ids touched

`0xBD0A`, `0xBD18` (`0x00731632`, `0x00731663`, DISCONN_DUNGEON_INFO error paths),
`0xBD1B` (`0x00737076`, forced dungeon abandon). All via `0x28C14C0(id, 0)` → `0x2304C70(0x209, s)`.
UI window id **`0x22`** is the rejoin/disconnect dialog (`0x2301E60(0x22, 0)` /
`0x2304C70(0x22, 0)`); window id `0x2A` is used on the abandon path (`0x274CA50(0x2A, 0)`).

---

## 10. Unknowns / not established

* The `type == 2 (OBJECT)` response branch serialises through `0x1764E00` / `0x17B1250`; I did not
  identify the packet id it emits (no `beginPacket` immediate in that branch within the range read).
* `SyncRequest+0x04` and `+0x0C` semantics beyond "written as u16 #2 and #4 of the header".
* The 12-byte sync-target element's `+0x00` field: copied to a freshly created map object's `+0x00`
  at `0x00735EDD`; a dungeon/map instance key, but I did not chase `0x01720580` far enough to name it.
* Whether the `u16` at `+0x00` of NOTI `0x292` is the requester's char index or party index; the
  handler only compares it against a value-obfuscated global (`0x03B3E190`/`0x03B3E194`, loaded via
  `0x27BC0F0`) and against each `memberIdx`, then passes the result as the `bQuiet` flag.
* `Etc/EntryParty.etc` field layout — only the registration call is in this TU; parsing happens in
  the `.etc` framework at `0x2A97D30`.
