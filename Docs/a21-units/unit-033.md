# TU 33 — `DNFClient\RDAR\CNRoom.cpp`  (0x01A6B240 – 0x01A6D0E0)

**What it implements:** the client-side **PVP room object** — the 8-seat table
(who sits where, in what seat-state), the room's lifecycle state machine, the
"my room" / "dummy room" global, the per-seat ready flags, and the fight-doll
(training-dummy) containers. It also contains the *only* client→server send in
the file: `ENUM_CMDPACKET_SET_PVP_SEAT_STATE` (0x034).

Confidence: **high** for the class layout, the seat model, and the 0x034 send.
Medium for the meaning of individual `seatType` values 3 vs 4 and for fields
still marked "unknown" below.

---

## 1. Span check

The briefing's span `0x01A6B240 – 0x01A6CF70` is **right at the bottom, one
function short at the top**. `hi` is the *start* of the last anchor function
(`getDoll`), which actually runs to **0x01A6D0E0**. Use `0x01A6B240 – 0x01A6D0E0`.

Corroboration that the boundary is real, not an address cut: the decrypted
literal block `0x032FC860 … 0x032FCBC0` is used *only* by functions in this
range, and the next file's block (`CNRDReplayManager.cpp`) starts cleanly at
`0x032FCC20`. Two vtables sit **inside** that literal block —
`0x032FCA8C` and `0x032FCB14` — which is why the literal VAs look "gappy".

The region `0x01A6D0E0 – 0x01A6DEF0` carries **no `__FILE__` anchor and no
literals at all**; `getDoll` calls into it (`0x01A6D330` = the map `find`
helper), so at least part of it is this file's template instantiations. I do
not claim the whole gap.

---

## 2. Classes recovered (RTTI + ctor/dtor evidence)

`rtti-vtables.csv` gives type descriptors but the names are stripped
(`TD#1e0c`, `TD#1f0c`). The **dynamic_cast at 0x01A6BFE0 / 0x01A6C830** nails
the relationship:

```
01A6BFF1  push 0x3986440     ; target TD  -> vtable 0x032FCB14
01A6BFF6  push 0x3986428     ; source TD  -> vtable 0x032FCA8C
01A6BFFE  call 0x2C6386A     ; __RTDynamicCast
01A6C004  call 0x1A6BA90     ; CNPVPRoom::setDummyRoom(result)
```
`rtti-vtables.csv` row for `0x032FCB14` lists `bases: TD#1f0c|TD#1e0c`.

| vtable VA | slots | class (from asserts / use) | evidence |
|---|---|---|---|
| `0x032FCA8C` | 31 | **CNRoom** — abstract base; slot 0 = `~CNRoom` (0x01A6CCD0), slots 1..30 all `_purecall` (0x02C6287B) | dtor sets `[this]=0x032FCA8C` then destroys the sub-object at `+4` |
| `0x032FCB14` | 31 | **CNPVPRoom** | ctor 0x01A6CD00 sets it; every slot is a real function in this file or a neighbour |
| `0x032F82E4` | 1 | **PVPRoomInfo** (base) — the single virtual is `getUserAtSeat` = `0x01A6B680` | ctor 0x01A6B970 writes it; *also constructed by `CNWarRoom.cpp`* |
| `0x032FCB0C` | 1 | **PVPRoomInfo-derived info held by CNPVPRoom** — overrides the one virtual with `0x01A6C9B0` (adds doll seats) | ctor 0x01A6CBE0 writes 0x032F82E4 then overwrites with 0x032FCB0C |

`CNPVPRoom::CNPVPRoom(int roomKind)` = **0x01A6CD00**:
```
[this]      = 0x032FCA8C        ; base vptr
call 0x1A6CBE0 (this+4)         ; info ctor
[this]      = 0x032FCB14        ; CNPVPRoom vptr
ctor container @ this+0xB4      ; 0x13BEF90
ctor container @ this+0xC8      ; 0x008DA400
[this+8]    = arg               ; room kind
```
`~CNPVPRoom` = 0x01A6CD80 (0x01A6CF30 is the scalar-deleting thunk, vtable slot 0).
`operator=` = 0x01A6CF60 → 0x01A6CE60 (info assign).

---

## 3. Object layout — offsets are from the **CNPVPRoom\*** (what the global holds)

Frame note: methods reached through the *primary* vtable take `this = obj`;
the info accessors (`isTeamSeat` etc.) take `this = obj+4`. Every offset below
is normalised to `obj`.

| off | type | name / meaning | proving access |
|---|---|---|---|
| `+0x00` | vptr | CNRoom / CNPVPRoom vtable | `0x01A6CD44 mov [esi],0x32fcb14` |
| `+0x04` | vptr | PVPRoomInfo sub-object vptr | `0x01A6CC0F mov [esi],0x32f82e4`, `0x01A6CC3B … 0x32fcb0c` |
| `+0x08` | int | **room kind** (ctor arg) | `0x01A6CD61 mov [esi+8],eax` |
| `+0x0C` | int[8] | **seatType[8]**, init `0xFF` | `0x01A6B6FB mov eax,[ecx+eax*4+8]` (this=obj+4); init `0x01A6CC22 mov [ecx-0x20],0xff` |
| `+0x2C` | int[8] | **seatUserId[8]**, init `0xFFFF` | `0x01A6C423 mov ecx,[esi+eax]` esi=0x2C..0x4C; init `0x01A6CC25 mov [ecx],0xffff` |
| `+0x4C` | byte | flag (getter returns `!=0`) | `0x01A6B9B2 cmp [ecx+0x48],al` (this=obj+4) |
| `+0x4D` | byte[8] | **deadManObserver[8]** | `0x01A6B7DB mov al,[eax+ecx+0x49]` (this=obj+4) |
| `+0x58` | int | unknown, init 0 | `0x01A6CA7C mov [esi+0x54],edi` |
| `+0x5C` | int | **room-name string id**; 0 ⇒ use the wstring | `0x01A6C150 mov eax,[ecx+0x5c]; jne → [eax*4+0x3B4004C]` |
| `+0x60` | std::wstring | **roomName** (proxy@0x60, `_Buf`@0x64, `_Mysize`@0x74, `_Myres`@0x78) | `0x01A6C15D mov eax,[ecx+0x64]` / `0x01A6C157 cmp [ecx+0x78],8`; ctor `[edi+0x18]=7` |
| `+0x80` | int | **mySeatIndex**, init -1 | `0x01A6C030 mov [esi+0x80],edi` (setter); `0x01A6BA70 mov eax,[ecx+0x80]` |
| `+0x84` | int | unknown, init 0 | `0x01A6CAB5` |
| `+0x88` | int | unknown, init 0 | `0x01A6CABB` |
| `+0x8C` | int | **roomState**, init -1 | `0x01A6B6BE mov [ecx+0x8c],eax` (setter, vtbl+0x10) |
| `+0x90` | int | unknown, init 0 | `0x01A6CAC7` |
| `+0x94` | int | unknown, init 0; setter is vtbl+0x6C | `0x01A6B836 mov [ecx+0x94],eax` |
| `+0x98` | container | **doll/AI seat map** — nodes: `+0x0C` value, `+0x10` seatIndex, `+0x15` byte | `0x01A6C9E9` (begin), `0x01A6CA2C cmp [ecx+0x10],ebx`, `0x01A6CA4F mov eax,[ecx+0xc]` |
| `+0xAC` | int | init 0; vtbl+0x1C returns `!=0` | `0x01A6B9D0 cmp [ecx+0xac],0` |
| `+0xB0` | byte | **all occupied seats have reported in** | set at `0x01A6CB84`, read at `0x01A6B860` |
| `+0xB4` | std::map | **fight-doll map**, `getDoll`/`setDoll` | `0x01A6CF7B lea ebx,[ecx+0xb4]`, `0x01A6CF0A add ecx,0xb4` |
| `+0xC8` | set | keys of seats that have reported (UDP peers) | `0x01A6CB02 lea edi,[esi+0xc8]`, `0x01A6C95A lea esi,[ecx+0xc8]` |

**Global statics (module-level):**

| VA | meaning | evidence |
|---|---|---|
| `0x03B3FED8` | `byte readyFlag[8]` — one per seat | `0x01A6B629 mov [ecx+0x3b3fed8],al`; cleared wholesale by `movq [0x3b3fed8],xmm0` at 0x01A6BB1E / 0x01A6C69A |
| `0x03B3FEE0` | `CNPVPRoom* g_myRoom` | read by ~20 functions; written only at 0x01A6BAF7 and 0x01A6C66D |
| `0x03B3E190 / 0x03B3E194` | obfuscated accessor for **my user id** (VMP-style 13-way switch on `0x3B3E190 & 0xF`) | 0x01A6C1E3 ff, 0x01A6C399 ff |
| `0x03B4004C` | localized room-name string table | `0x01A6C165 mov eax,[eax*4+0x3b4004c]` |

---

## 4. Seat model — what a server has to mirror

### `seatType[i]` enum, fully derived from the predicates

| value | derived meaning | proof |
|---|---|---|
| `0` | **single / free-for-all seat** | `isSingleSeat` bounds-assert at 0x01A6BF5B (line 872); `t==0` returns false from the "is team mode" test at 0x01A6BF50 |
| `1` | team 1 | `isTeamSeat` = `1 <= t <= 3` (0x01A6B6FF); avatar palette 0 |
| `2` | team 2 | avatar palette 1 |
| `3` | team **and** observer | `isTeamSeat` true *and* `isObserverSeat` true (`t==3||t==4`, 0x01A6B76F) |
| `4` | observer | `isObserverSeat` true; excluded from the active-player count |
| `5`, `6` | **doll / AI seat** — the "user" comes from the container at `+0x98`, not from `CNUserManager` | `0x01A6C9C7 cmp eax,5 / cmp eax,6 → je 0x1a6c9de` (map path) vs. the normal `getUser` path |
| `0xFE` | vacated / leaving | the client *sends* this: `setSeatType(mySeat, 0xFE)` at 0x01A6C774 |
| `0xFF` | empty (ctor init); clears that seat's ready flag | `0x01A6CC22`; `0x01A6BD79 cmp eax,0xff → readyFlag[i]=0` |

`0xFE` and `0xFF` are both treated as "no occupant" by the room-empty scan
(`0x01A6BF28`/`0x01A6BF30`).

`seatUserId[i] == 0xFFFF` means the seat is unoccupied; otherwise it is a
**CNUserManager user id** (`0x019E2EF0 = CNUserManager::getUser`, confirmed by
`func-source-file.csv` + its own `CNUserManager::getUser` literal).

### `roomState` (`+0x8C`) — the values the client tests

Setter `0x01A6B6B0` (vtbl+0x10) also fires two UI refreshes. Predicates:

| VA | vtbl slot | test |
|---|---|---|
| `0x01A6B9E0` | +0x20 | `state == 3` |
| `0x01A6B9F0` | +0x24 | `state == 4 \|\| state == 5` |
| `0x01A6BA10` | +0x28 | `state == 5` |
| `0x01A6BA20` | +0x2C | `state == 4` |
| `0x01A6BA30` | +0x30 | `state == 8` |
| `0x01A6BA40` | — (free fn, 38 callers) | `<mode-check> && g_myRoom && state == 7` |

So the client distinguishes at least states **3, 4, 5, 7, 8**; init is `-1`.
I did not recover names for them.

---

## 5. The packet this unit sends — `ENUM_CMDPACKET_SET_PVP_SEAT_STATE` (0x034)

`CNPVPRoom::setSeatType(int seatIndex, int seatType)` = **0x01A6C170**
(assert literal `seatIndex >= 0`, line 497):

```
01A6C174  if (!g_myRoom) return
01A6C189  assert(seatIndex >= 0)                       ; line 0x1F1
01A6C1C8  if (seatIndex >= 8) return
01A6C1D8  if (seatType[seatIndex] == newType) return   ; no-op suppressed CLIENT-SIDE
01A6C1E3..01A6C2EC  myUserId = <obfuscated global>
          proceed only if seatUserId[seatIndex] == myUserId
                      OR seatUserId[mySeatIndex] == myUserId
01A6C2F8  push 0 ; push 0x34 ; CNGameSocket::getInstance()->MakePacket(0x34, 0)
01A6C308  ...->WriteInt(seatIndex)
01A6C315  ...->WriteInt(newType)
01A6C325  ...SendPacket()
```

Callee identification (all cross-checked against `func-source-file.csv`):
* `0x0274CA50` = `CNGameSocket::getInstance()` (1088 callers)
* `0x0274D1E0` = **`CNGameSocket::MakePacket`** — its own decrypted literal says so; signature `ret 8`, `mov di, word[ebp+8]` (WORD msgType) + `mov cl, byte[ebp+0xC]`; builds a 14-byte header `{01, WORD type, DWORD 0, BYTE flag}`
* `0x0274D2E0` = write one dword into the packet (`push 1; lea eax,[ebp+8]`)
* `0x0274E6D0` = send/flush (`CNGameSocket.cpp`)

`packet-names.txt` line 53: `0x034  52  ENUM_CMDPACKET_SET_PVP_SEAT_STATE`.

**Wire form (C→S): `0x034` + `int32 seatIndex` + `int32 seatState`.**

The S→C counterpart is `ENUM_NOTIPACKET_PVP_SEAT_STATE = 0x02B` (from
`noti-names.txt`); its handler lives outside this unit — this file only exposes
the *applier*, `CNPVPRoom::setSeat` (below).

Note for a server implementer: **the client refuses to send when the new type
equals the current one, and only sends for its own seat** (the two-way user-id
check at 0x01A6C1D8/0x01A6C2EC). A "wrong seat" or "no change" request is not
constructible from a stock client.

Guarded wrappers used by the rest of the client:
* `0x01A6C5E0` (5 callers) → `setSeatType` if the manager exists
* `0x01A6C740` → UI teardown, then `setSeatType(getMySeatIndex(), 0xFE)` = **leave my seat**
* `0x01A6C860` → guarded `0x01A6C740`

---

## 6. `CNPVPRoom::setSeat` — the state the server drives (vtbl +0x5C)

`0x01A6BB60`, `void setSeat(int seatIndex, int userId, int seatType)` (`ret 0xC`):

```
if (seatIndex > 7) return
if (seatUserId[i]==userId && seatType[i]==newType) return
old = seatType[i]
seatUserId[i] = userId ; seatType[i] = newType
if (g_myRoom != this) return                       ; dummy rooms update silently
--- occupant changed ---
  userId == 0xFFFF  -> user = getUser(oldUserId); … ; RoomUI->vt[0xD4](i, user)
                       readyFlag[i] = 0   (0x01A6BCF7 -> 0x01A6B620)
  otherwise         -> user = getUser(userId);   RoomUI->vt[0xD0](i, user, 1)
                       and, unless 0x163D320(), play cue "LADDER_DOWN"
                       (0x01A6BD5B, args -1,0,0,-1,-1)
--- seat type changed ---
  newType == 0xFF -> readyFlag[i] = 0                (0x01A6BD80)
  fire two UI refreshes (0x13ADC80, 0x13AEF60)
--- avatar colouring, switch(newType), table at 0x01A6BF04 ---
  0        -> avatar->vt[0x328](seatIndex + 3)          ; per-player colour
  1        -> avatar->vt[0x328](0), and (0) on 3 sub-parts
  2        -> avatar->vt[0x328](1), and (1) on 3 sub-parts
  3 and 4  -> avatar->vt[0x328](2), ghost flag = 1      ; same jump target
  >4       -> nothing
  then avatar->0x183AF70(ghostFlag, 0, 0)
```

`0x01A6B240` (992 B, first anchor) is the same switch applied to **all 8 seats**
— a full re-colour pass, called from `setMyRoom` and `setDummyRoom`.

---

## 7. Room-pointer lifecycle

`CNPVPRoom::setMyRoom(CNPVPRoom*)` = **0x01A6C600** (line 213):
```
if (g_myRoom == p) return
log "LOG>> call setDrawLoadingMode [%s(%d)]"      ; "CNPVPRoom::setMyRoom", 213
<mgr>->0x163F650(1)
g_myRoom = p
<mgr>->getUI()->vt[0xF8](p)
p == NULL:  readyFlag[0..7] = 0 (movq xmm0) ; tail-call <ui>->vt[0xA4]()
p != NULL:  s = p->mySeatIndex
            if (s != -1) { readyFlag[s] = 0 ; … ; 0x289E620()->[0x28] = s }
            <mgr>->0x1646AD0(4)
            <ui>->vt[0xA0](getMySeatIndex())
            recolourAllSeats(!0x163D320())
```
`CNPVPRoom::setDummyRoom` = **0x01A6BA90** (line 83) is the identical shape
minus the seat bookkeeping — same log format string, same global write.
`0x01A6BFE0` / `0x01A6C830` are the free-function entries that `dynamic_cast`
a `CNRoom*` down to `CNPVPRoom*` first.

`CNPVPRoom::setMySeatIndex(int)` = **0x01A6C010** (vtbl +0x58): writes `+0x80`,
and if it actually changed and this is the live room, formats string id
**0x647** with the character's name and posts a system chat line
(`0xA9AF50(buf, 0xFF9BC8E6, 0x17, 0,0,0,0)` → `0x1A60620`), then clears
`readyFlag[newSeat]`.

---

## 8. Fight-doll / UDP-readiness side of the class

* `CNPVPRoom::getDoll(key)` = **0x01A6CF70** (line 988) — map lookup on `+0xB4`;
  on miss logs `FIGHT_DOLL_MODE>> pointer is invalid` and returns 0.
* `setDoll(key, value)` = **0x01A6CF00** — `map[key] = value`; caller is
  `CNRDUdpModule.cpp`.
* **0x01A6CAF0** — `insert(key)` into the set at `+0xC8`, then: if *every* seat
  with a non-null occupant has its key in that set, set `+0xB0 = 1`. Caller is
  `CNRDUdpModule.cpp`. This is the "all P2P peers are present" gate.
* **0x01A6C950** — membership test against `+0xC8` (returns `setne`, i.e. *not*
  found). Caller is `CNPVPModule.cpp`.
* **0x01A6CBA0** — insert a `{first,second}` pair into the `+0x98` container.
* **0x01A6C8A0** — erase entries in `+0x98` whose node `+0x10 == seatIndex`.

---

## 9. Public surface

### Free functions (no `this`) — this is what the rest of the client calls

| VA | callers | what it does |
|---|---|---|
| `0x01A6BFD0` | **129** | `getMyRoomIfIngame()` — `0x1102C30() ? g_myRoom : NULL` |
| `0x01A6B9C0` | 32 | `getMyRoom()` — raw `g_myRoom` |
| `0x01A6BA40` | 38 | `<mode-check>() && g_myRoom && roomState == 7` |
| `0x01A6C5A0` | 20 | guarded `getMySeatIndex()` |
| `0x01A6B8A0` | 15 | guarded `findSeatByUserId(userId)` → index or -1 |
| `0x01A6B8E0` | 8 | guarded `isReady(seatIndex)` → `readyFlag[i]` |
| `0x01A6B930` / `0x01A6B950` | 6 / 2 | forward to `0x01A6D560` / `0x01A6D5D0` on the `[0x3A5C984]` singleton |
| `0x01A6C830` | 5 | `dynamic_cast` + `setMyRoom` |
| `0x01A6C5E0` | 5 | guarded `setSeatType` → **sends 0x034** |
| `0x01A6C5C0` | 3 | guarded `getMySeatType()` |
| `0x01A6C170` | 3 | `setSeatType` (raw) |
| `0x01A6B650` | 1 | `findSeatByUserId` (unguarded) |
| `0x01A6B620` / `0x01A6B900` | | `setReady(seatIndex, bool)` + UI refresh |
| `0x01A6BFE0` | 1 | `dynamic_cast` + `setDummyRoom` |
| `0x01A6C860` | 1 | guarded leave-seat |

External callers by module (from `callgraph.csv` × `tu-index.csv`):
`CNRDBaseInterface.cpp`, `CNPVPModule.cpp`, `CNRDUdpModule.cpp`,
`CNRDStage.cpp`, `GameCoreNetwork_NOTI_Etc.cpp`, `GameCoreNetwork_CMD_Town.cpp`,
`GameCoreNetwork_NOTI_Dungeon.cpp`, `IRDCharacter.cpp`, `GameCoreSub.cpp`,
`CNWarRoom.cpp` (the last one constructs `PVPRoomInfo` via `0x01A6B970` — the
seat model is shared with war rooms).

### CNPVPRoom vtable (`0x032FCB14`, 31 slots) — dumped, not guessed

| slot | VA | role |
|---|---|---|
| +0x00 | 0x01A6CF30 | scalar deleting dtor |
| +0x04 | 0x01A182F0 | (other TU) |
| +0x08 | 0x01A6C150 | `getName()` → table[`+0x5C`] or the wstring at `+0x64` |
| +0x0C | 0x01A6BF20 | first non-empty seat is a team seat (not single, not observer) |
| +0x10 | 0x01A6B6B0 | `setRoomState(int)` |
| +0x14 | 0x0198C6E0 | (other TU) room type; compared to 6 at 0x01A6BFB3 |
| +0x18 | 0x012F3690 | (other TU) |
| +0x1C | 0x01A6B9D0 | `+0xAC != 0` |
| +0x20…+0x30 | 0x01A6B9E0 / 9F0 / A10 / A20 / A30 | roomState == 3 / 4\|5 / 5 / 4 / 8 |
| +0x34 | 0x01A6B480 | `getSeatCharacter(i)` |
| +0x38 | 0x01A6B420 | find the seat whose character matches (vt[0x48], vt[0x4C]) |
| +0x3C | 0x01A6B4B0 | `getSeatUser(i)` = info->vt[0](i) |
| +0x40 | 0x01A6B840 | `getMySeatUser()` |
| +0x44 | 0x019E6CB0 | (other TU) |
| +0x48 | 0x01A6B4D0 | **count active players** — `seatUserId[i]!=0xFFFF && seatType[i]∉{3,4}` |
| +0x4C | 0x01A6B540 | count seats with `seatType != 0xFE` |
| +0x50 | 0x01A6B580 | count seats with `seatType != 0xFE` **and** no user object |
| +0x54 | 0x01A6BA70 | `getMyUserId()` = `seatUserId[mySeatIndex]` else `0xFFFF` |
| +0x58 | 0x01A6C010 | `setMySeatIndex(int)` |
| +0x5C | **0x01A6BB60** | **`setSeat(seatIndex, userId, seatType)`** |
| +0x60 | 0x02231720 | (other TU) |
| +0x64 | 0x01A6BFA0 | `!(field+0x58 == 2 && roomType() != 6)` |
| +0x68 | 0x028D7050 | (other TU) |
| +0x6C | 0x01A6B830 | set `+0x94` |
| +0x70 | 0x01A6C570 | `amIObserver()` = `isObserverSeat(getMySeatIndex())` |
| +0x74 | 0x01A6B870 | `!isState3() && activePlayerCount() <= 2` |
| +0x78 | 0x01A6C790 | first non-observer seat other than mine that has an object |

### Info accessors (`this = obj+4`) with their assert line numbers

| VA | function | body | line |
|---|---|---|---|
| 0x01A6B680 | `PVPRoomInfo::getUserAtSeat(i)` (virtual slot 0) | `seatUserId[i]!=0xFFFF ? CNUserManager::getUser(id) : NULL` | — |
| 0x01A6C9B0 | derived override | same, but `seatType∈{5,6}` → look up the `+0x94` doll container | — |
| 0x01A6BF20* | `PVPRoomInfo::isSingleSeat` (inlined) | `seatType[i] == 0` | 872 |
| 0x01A6B6F0 | `PVPRoomInfo::isTeamSeat` | `1 <= seatType[i] <= 3` | 884 |
| 0x01A6B760 | `PVPRoomInfo::isObserverSeat` | `seatType[i]==3 \|\| ==4` | 896 |
| 0x01A6B7D0 | `PVPRoomInfo::isDeadManObserver` | `byte deadManObserver[i]` | 908 |
| 0x01A6B5C0 | `CNPVPRoom::isReady(i)` | `g_readyFlag[i]` at `0x03B3FED8` | 534 |
| 0x01A6C170 | `setSeatType` | assert `seatIndex >= 0` | 497 |
| 0x01A6BA90 | `CNPVPRoom::setDummyRoom` | | 83 |
| 0x01A6C600 | `CNPVPRoom::setMyRoom` | | 213 |
| 0x01A6CF70 | `CNPVPRoom::getDoll` | | 988 |

\* 0x01A6BF20 is the *enclosing* function (vtbl+0x0C); `isSingleSeat` and
`isObserverSeat` are inlined into it and only the former's assert survived.

---

## 10. What a server implementer should take away

1. **`0x034 ENUM_CMDPACKET_SET_PVP_SEAT_STATE` = `int32 seatIndex, int32 seatState`.**
   The client sends it only for its own seat and only on a real change.
2. The server must broadcast the seat table as **(seatIndex, userId, seatType)**
   triples — that is exactly the shape `setSeat` (vtbl+0x5C) consumes, and it is
   the only path that mutates `seatType[]`/`seatUserId[]`.
   `userId == 0xFFFF` is the "seat now empty" encoding.
3. `seatType` sentinel values matter: **`0xFF` clears the client's ready flag**
   for that seat, `0xFE` is what the client sends to vacate, and `5`/`6` route
   the seat to the fight-doll container instead of the user manager.
4. **Ready flags are a module-level global (`0x03B3FED8`), not part of the room
   object**, and the client resets them locally on: leaving the room, seat type
   → `0xFF`, occupant leaving, and `setMySeatIndex`. A server that assumes the
   client keeps ready state across those events will desync.
5. Max **8 seats**, hard-coded in every loop bound (`cmp …, 8` at 0x01A6B468,
   0x01A6B5AE, 0x01A6C1CB, 0x01A6C42F, 0x01A6CB78) and in the assert
   `index > 7` in every accessor.
6. Room state uses at least the values **3, 4, 5, 7, 8** (init −1); the client
   gates behaviour on each of them, so the server's room FSM must emit them.

---

## 11. Things I could not resolve

* Symbolic names for `roomState` values and for `seatType` 3 vs 4 (both colour
  the avatar identically; 3 additionally satisfies `isTeamSeat`).
* Fields `+0x58`, `+0x84`, `+0x88`, `+0x90`, `+0x94`, `+0xAC` — initialised, read
  by one predicate each, no naming evidence.
* The S→C handler for `ENUM_NOTIPACKET_PVP_SEAT_STATE (0x02B)`; it is outside
  this unit and `packet_types.csv`'s `handler_va` for 0x2B (`0x01106750`) lands
  inside a 12 KB function, so I did not confirm it.
* Whether `0x01A6D0E0 – 0x01A6DEF0` (no anchors, no literals) belongs to this
  file; `getDoll` calls `0x01A6D330` there, so at least part of it does.
