# TU-003 — `DNFClient\RDAR\BlueMarble\CNRDBlueMarble.cpp`

Anchored span `0x01308A20`–`0x01309B30`. Image base 0x00400000.

## Verdict in one line

This file implements **`CNRDBlueMarble`, the client-side singleton controller for the
"Blue Marble" (Monopoly-style board) event**: the player-slot table, the game state
machine, the two named server-push handlers (`onActionInfo`, `onDiceResult`), the
progress-ranking rebuild, and the load of `Event/BlueMarble/BlueMarble.blu` /
`BlueMarbleMap.lst` / `BlueMarbleTile.lst`.

Proof it is not a source-prefix guess — the class name is in its own log strings:

| VA | decrypted literal |
| --- | --- |
| 0x01308A20 | `CNRDBlueMarble::onActionInfo`, `Dungeon: %d item : %d ` |
| 0x01309B30 | `CNRDBlueMarble::onDiceResult`, `Dice result - slot: %d count : %d ` |
| 0x01309050 | `Event/BlueMarble/`, `BlueMarble.blu`, `BlueMarbleMap.lst`, `BlueMarbleTile.lst` |

---

## 1. Span correction (read this first)

**The briefing's span is too NARROW at the bottom, and one function inside it is not this file's code.**

* **Too narrow**: `0x01308510`–`0x01308A20` is unmistakably the same class — same `this`,
  same `vec@+0x20` slot idiom, same `0x2bb1870()->[0x28]` bound, all reached through the
  same `getInstance()` (0x013096F0). `0x013087A0` (`getMyPlayer`) is *called by*
  `onActionInfo` at 0x01308A28 and is byte-for-byte the same loop as `0x01308CC0`, which
  *is* inside the span. `func-source-file.csv` has only two anchors in the whole
  0x1300000–0x1320000 window, so the anchor-bounded span understates the module by ~1.3 KB.
  Real class body: **`0x01308510`–`0x01309B30`** (plus the window/net glue at
  0x01309BF0–0x0130D8xx which has no anchor and is probably a sibling `.cpp`).

* **Not this file**: `0x01309300` (544 B, **61 callers spread across the entire image** —
  0x004CF830, 0x006A38F0, 0x008EBD40, 0x00AC71F0 …) is a COMDAT-folded
  `std::map<K,V>::operator[]` template instantiation that the linker parked here.
  Evidence: it returns `lea eax,[edi+0x10]` (address of the node's mapped value),
  does `lower_bound` (0xd3d670) → `end()` (0x293b470) → compare → allocate node → insert
  (0xed51f0), and `ret 4`. The briefing lists it as the unit's #1 entry point purely on
  in-degree; that is the base-rate trap. Its only *local* user is 0x01309A4E.

* `0x013096F0` (55 callers) is genuine: it is `CNRDBlueMarble::getInstance()`.

---

## 2. Server-relevant: the wire protocol

### 2.1 S→C opcodes (registration table at `0x0130C480`)

Registration idiom is `push 0; push OPCODE; push HANDLER; call 0x1102c30 /*net mgr*/;
mov ecx,eax; call 0x1637720 /*registerHandler*/`.

Read primitives seen in the stubs: `0x027C5230(ptr,len)` = read N bytes (len 1 → u8),
`0x027C52A0(ptr)` = u16, `0x027C5310(ptr)` = u32.
(Note: the task brief lists these as 0x023C5230/52A0/5310; the addresses actually
executed here are **0x027C5230/52A0/5310**, and `parsers.csv` counts them, so the CSV
uses the 0x027C form.)

| Opcode | stub | payload (wire order) | dispatched to |
| ---: | --- | --- | --- |
| **0x1AF** (431) | 0x0130C100 | `u16 sessionId; u8 count; count × { u8 slot; u16 param }` | roster sync (see 2.2) |
| **0x1B0** (432) | 0x0130BFA0 | `u8 slot; s8 count` (**signed**, `movsx` @0x0130BFC2) | `CNRDBlueMarble::onDiceResult(slot,count)` @0x01309B30 |
| **0x1B1** (433) | 0x0130BF60 | `u8 mapIndex` | 0x0130ABD0 → `m_pMapList->0x130b090(v)`; `setState(3)` |
| **0x1B2** (434) | 0x0130C020 | `u8 slot` | 0x0130ABF0 → turn start (see 2.3) |
| **0x1B3** (435) | 0x0130BFE0 | `u32 dungeonId; u32 itemId` | `CNRDBlueMarble::onActionInfo(dungeon,item)` @0x01308A20 |
| **0x1B4** (436) | 0x0130C050 | `u8 slot` | 0x01308B30 → `vec[slot]->0x13125B0()` |
| **0x1B5** (437) | 0x0130C080 | `u8 usedEnters; u8 usedTicketEnters` | `[this+0x64]=a` (0x1983070); `[this+0x68]=b` (0x1A6D810) |

Also, `GameCoreNetwork_NOTI_Town.cpp` (0x0118C3A0) calls into this class at
0x01191C78: on a town user-state notify for the local character it calls
`getInstance()->0x01308AD0(myChar->[+0x1C])`, which does UI work only when
`gameState()==3 && arg==7`.

### 2.2 Opcode 0x1AF in detail (0x0130C100)

```
u16 sessionId              -> this->[+0x78]              (0x013086D0)
u8  count
if (this->0x14318C0() < 3) {                 // first/full sync
    clearAllPlayers()                        // 0x01308C60
    repeat count: { u8 slot; u16 param; addPlayer(slot,param) }   // 0x01308830
} else {                                     // delta sync
    repeat count: { u8 slot; u16 param; addPlayer(slot,param); keep.push(slot) }
    for j in 0..playerMax-1: if j not in keep -> removePlayer(j)  // 0x01308990
}
// then, if event 15 is enabled (0x13db470(0xF)->0x13db490()):
n = number of slots whose player passes isMine()      // 0x1308760 + 0x1308570
if (n == 0) send 0x1FC { u8 1; u16 sessionId }
else if (n >= 2) send 0x1FC { u8 0; u16 sessionId }
else /* n == 1 */ send nothing
getInstance()->0x130AAA0()
```

### 2.3 C→S opcodes emitted by the module

Send idiom: `push flag; push OPCODE; call 0x274CA50 /*stream*/; mov ecx,eax;
call 0x274D1E0 /*beginPacket(u16 op, u8 flag)*/`, then
`0x274D2E0` = write u8, `0x274D310` = write u16, `0x274D340` = write u32,
`0x274E6D0` = flush/send. (Widths verified: 0x274D2E0 adds 1 to the byte counter at
[ecx+0x2BCC2C], 0x274D310 adds 2, 0x274D340 adds 4.)

**Opcode 0x1F0 (496) is the multiplexed BlueMarble request: `{ u8 sub; u8 arg }`.**

| sub | arg | emitter | context |
| ---: | ---: | --- | --- |
| 0 | 1 | 0x0130BC30 | guarded by one-shot global `0x3A3DCA4`; reached from state 2 tick when `amIFirstPlayer()` — **start game** |
| 1 | `!x` | 0x0130BC80(bool x) | called from `CNRDBlueMarble::0x01308600` with `[this+4]->0x130dff0()`; plays sound `DICE_CLICK` |
| 2 | 1 | 0x0130BCD0 | followed by opcode 0x0F `{u32 dungeonId}` and opcode 0x10 (dungeon enter, see below) |
| 3 | 1 | 0x0130BDD0 | event-15 gated |
| 4 | 1 | 0x0130BE20 | then focuses the board window (0x01308640) |
| 5 | 1 | 0x0130BE60 | then opens window id 0x89 |
| 6 | 1 | 0x0130BEC0 | event-15 gated |
| 7 | 1 | 0x0130BF10 | event-15 gated |

Other sends from the module:

* **0x62** `{ u8 2 }` — 0x0130BC00.
* **0x0F** `{ u32 dungeonId }` — 0x0130BD08.
* **0x10** `{ u32 dungeonId; u8 difficulty; u8 0; u8 0; u16 0; u32 0; u8 0; u8 0 }` —
  0x0130BD3B; `difficulty` comes from `dataMgr(+0x28 sub-struct)->0x2bafd60(dungeonId)`.
* **0x2A** — 0x0130A685, sent once per turn when the **10 s turn timer expires**
  (see 3.2); `[this+0x74]` is the "already sent" latch.
* **0x1FC** `{ u8 flag; u16 sessionId }` — see 2.2.

### 2.4 The two named handlers

**`onActionInfo(int dungeonId, int itemId)` @0x01308A20** (`ret 8`, thiscall):

```
p = getMyPlayer();                       // 0x013087A0
if (!p) return;
if (!isMine(p)) return;                  // getUser(p->[+8]) == g_0x3A5C980->getMyUser()
log("Dungeon: %d item : %d", dungeonId, itemId);   // line 0x1E6 = 486
if (dungeonId > 0) p->0x1312B00(2, dungeonId);     // queue event kind 2 = dungeon
if (itemId    > 0) { p->0x1312B00(3, itemId); this->[+0x6C] = itemId; }
```

**`onDiceResult(int slot, int count)` @0x01309B30** (`ret 8`, thiscall):

```
if (slot < 0 || slot >= dataMgr->[0x28] /* [player max] */) return;
log("Dice result - slot: %d count : %d", slot, count);   // line 0x1D5 = 469
p = m_vecPlayer[slot];                                   // bounds-checked
if (p && isMine(p)) {
    if (!this->[+4]->0x130dfe0())  this->[+4]->0x130df70(0);   // open the dice panel
}
this->0x01309AD0(p, count);        // -> if isMine(p) same panel poke; p->0x1312B00(0,count)
```

`0x01312B00(kind, value)` allocates a 0x18-byte event node (`0x013080E0(kind,value)`)
and pushes it onto the player's queue at `player+0x3C`. Observed kinds:
**0 = dice/move count, 2 = dungeon id, 3 = item id.**

---

## 3. Server-relevant: the state machine (must be mirrored)

`m_nState` lives at **`this+0x44`**. `0x0130A410 = setState(int)`; it early-outs when
unchanged, otherwise stores and **restarts the stopwatch at `this+0x48`**
(`0x27C3410` reset, `0x27C3B60(0,0)`).

### 3.1 setState side effects (0x0130A410)

| new state | action |
| ---: | --- |
| 0 | unlock key-group 0xE; `clearAllPlayers()` (0x01308C60); close windows 1 and 2 of group 1; if `g_0x3B3DFB8` → `0x01309BF0(g)` |
| 1 | unlock key-group 0xE; `0x0130A000(g_0x3B3DFB8)` |
| 4 | `[this+0x74] = 0` (clear the turn-timeout latch) |
| 5 | play sound `BATTLE_VICTORY` (literal 0x30BD6D4, decrypted) |
| any | `[this+4]->0x130e130()` |

### 3.2 update() tick (0x0130AC80, jump table at **0x0130ACE0** indexed by `[this+0x44]`)

Every tick first calls `0x01308E80` (visibility refresh, below).

| state | tick behaviour | target |
| ---: | --- | --- |
| 0 | nothing | 0x0130ACDD |
| 1 | `if (allSlotsFilled()) setState(2)` | 0x0130AC97 |
| 2 | `if (!allSlotsFilled()) setState(1); else if ([this+4]->0x130e190() && amIFirstPlayer()) enableStartButton()` | 0x0130ACAD → 0x0130A5D0 |
| 3 | reset each player's per-turn UI (0x0130AD00); if `getMyPlayer()->0x28a2f20() < 1` → 0x0130AAA0() + chat notice UI-string **0xB650** | 0x0130ACB5 → 0x0130AC20 |
| 4 | if `gameState()==1` → `setState(myTurn ? 5 : 6)`; **else if not my turn && stopwatch > 0x2710 (10 000 ms) && gameState()==3 && !latch → send opcode 0x2A; latch=1** | 0x0130ACBD → 0x0130A610 |
| 5 | `if (stopwatch > 0x1388 / 5 000 ms) setState(6)` | 0x0130ACC5 |
| 6 | nothing | 0x0130ACDD |

**Two hard constants a server must agree with: turn timeout = 10 000 ms, state-5
(post-victory) hold = 5 000 ms.**

Also `0x013086B0 = getRemainTurnTimeMs()`:
`if (state != 4) return 0; r = 10000 - stopwatch.elapsed(); return r < 0 ? 0 : r;`

State-dependent packet semantics worth mirroring:
`removePlayer(slot)` (0x01308990) **does not free the player when `state >= 3`** — it
calls `player->0xEE15A0(0)` (mark left) and keeps the slot occupied. Below state 3 it
deletes the object and nulls the slot.

---

## 4. Object layout: `CNRDBlueMarble`, **sizeof = 0x7C**

`operator new(0x7C)` at 0x01309723; ctor 0x013095D0; dtor 0x01309520; the vtable
at **0x03285D58 has exactly one slot** (0x013096C0, the scalar-deleting destructor) —
so the class has no other virtuals.

| off | field | evidence |
| ---: | --- | --- |
| +0x00 | vptr = 0x03285D58 | `mov [esi], 0x3285d58` @0x013095FD and @0x0130954D |
| +0x04 | main board window*, `new(0xEC)` ctor 0x013123B0 | 0x013090F2–0x01309120 |
| +0x08 | map-list holder*, `new(0x18)` ctor 0x02B6E7C0; holds a vector at its +4/+8, size via 0x0130ADF0 | 0x0130911B–0x01309149 |
| +0x0C | request-sender*, `new(4)` ctor 0x0130C530 | 0x0130916D–0x01309175; used at 0x01308612, 0x0130A5FE |
| +0x10 | secondary widget*, `new(0x1C)` ctor 0x0130D1A0 (held via shared_ptr, 0x0263AEC0) | 0x01309144–0x0130916D |
| +0x14 | `new(0x3C)` ctor 0x0130BB40 | 0x01309196–0x0130919B |
| +0x18 | `new(0x10)` ctor 0x01313650 | 0x013091BF–0x013091EB |
| +0x1C | `std::vector<Player*>::_Myproxy` (8-byte heap node, `p->_Mycont = &this->_Myproxy`) | 0x01309603–0x01309627 (alloc/init), 0x013095A4 (free) |
| **+0x20** | `_Myfirst` — **`Player* m_vecPlayer[]`, index = player slot** | every accessor: 0x01308760, 0x013087BF, 0x01308B48, 0x01309B8B … |
| +0x24 | `_Mylast` (count = `(_Mylast-_Myfirst)>>2`) | 0x01309B8E |
| +0x28 | `_Myend` | 0x013095AE |
| +0x30 | `std::map<int, std::vector<Player*>>` container base (`_Myproxy`); `_Myhead` at **+0x38**, `_Mysize` at **+0x3C** | ctor 0x01083200 @0x01309636; `clear()` @0x013097BA–0x013097DA; **public getter `0x013085B0` returns `this+0x30`** |
| **+0x44** | `int m_nState` | 0x013085C0 (`!=0`), 0x0130A43C/0x0130A448, jump table 0x0130ACE0, 0x013089A8 |
| +0x48 | stopwatch (ctor 0x027C3A90, dtor 0x027C33F0, reset 0x027C3410 / 0x027C3B60, elapsed-ms 0x027C3660), 0x1C bytes | 0x0130A445–0x0130A457, 0x013086A0, 0x0130A662 |
| +0x64 | `int` **used** dungeon entries (S→C 0x1B5 byte 0) | setter 0x01983070; `0x013086F0` returns `dataMgr[0x34] - [this+0x64]`, clamped ≥0 |
| +0x68 | `int` **used** ticket entries (S→C 0x1B5 byte 1) | setter 0x01A6D810; `0x01308710` returns `dataMgr[0x3C] - [this+0x68]`, clamped ≥0 |
| +0x6C | `int` last reward item id | `mov [eax+0x6C], esi` @0x01308ABB in `onActionInfo` |
| **+0x70** | `int` current-turn slot | written @0x0130AC00 from S→C 0x1B2; read by 0x01308730 and 0x01308CC0 and 0x0130A636 |
| +0x74 | `bool` turn-timeout packet already sent | cleared @0x0130A5AF on `setState(4)`; tested 0x0130A67F, set 0x0130A6B7 |
| +0x78 | `u16` session/room id | setter 0x013086D0; written from S→C 0x1AF @0x0130C145; echoed in C→S 0x1FC @0x0130C3F1 |

### Sibling: player object, **sizeof = 0x64**

`new(0x64)` @0x01308895, ctor `0x01312D30(int slot, u16 param)`.

| off | field | evidence |
| ---: | --- | --- |
| +0x04 | slot index | getter 0x02846D50 (`mov eax,[ecx+4]`); compared to owner's `+0x70` @0x01308749 |
| +0x08 | owner user id (key into `CNUserManager::getUser`, 0x019E2EF0) | getter 0x01A182F0; used 0x01308581, 0x013087D7, 0x01309ADE |
| +0x14 | current map/stage index (1-based, clamped to map count) | getter 0x011D36C0; `0x013125B0` sets it to 1 and clamps to `getInstance()->[+8]->size()` |
| +0x38 | countdown; `--` reaching 0 triggers `getInstance()->0x01308570(this)` | 0x013125A0 (`<=0` test), 0x013125B3 |
| +0x3C | event queue (vector), pushed by `0x01312B00(kind,value)` | 0x01312B58 |
| — | display kind {0=me, 1=first other, 2=rest}, set 0x01312F60, read 0x014317C0 | 0x013088DA–0x01308966 |

**Careful — folded getters.** `0x01A182F0` is a shared `return [this+8]` COMDAT: on a
player it yields the user id, but at 0x013125D9 the same call is made on the
*CNRDBlueMarble singleton*, where it yields the map-list holder at `+8`. Same for
`0x02846D50` (`return [this+4]`) and `0x0130ADF0` (`(([ecx+8]-[ecx+4])>>2)`).

---

## 5. Table layout: `Event/BlueMarble/BlueMarble.blu`

Loaded by `0x01309050` via `dataMgr->0x02BB0CD0(dir, file)`; the tile and map lists go
through `0x02BB18F0` (`BlueMarbleTile.lst`) and `0x02BB2470` (`BlueMarbleMap.lst`).
Inside the parser `ebx = dataMgr + 0x28`, so absolute offset = `0x28 + ebx-offset`.
Values are read with `0x28A9310(&field, 1)`.

| `.blu` tag | dataMgr offset(s) |
| --- | --- |
| `[player max]` | **0x28** — this is the bound every slot loop in TU-003 uses |
| `[enter level]` | 0x2C |
| `[enter max level]` | 0x30 |
| `[enter count]` | **0x34** — cap for `this+0x64` |
| `[enter ticket count]` | 0x38, **0x3C** (2 ints; 0x3C is the cap for `this+0x68`) |
| `[decrease coin count]` | 0x40, 0x44 |
| `[monster drop routine]` | 0x48 |
| `[monster appear routine]` | 0x4C |
| `[die count]` | 0x50 |
| `[dice value]` | 0x54, 0x58 |
| `[item list]` | 0xF4 (container); count accumulated into 0x5C |
| `[dungeon difficulty]` | 0x60 |
| `[dungeon difficulty list]` | 0x64 (container) |
| `[super armor time]` | 0x78 |
| `[buff max value]` | 0x7C |
| `[gold base]` | 0x80 |
| `[gold multi]` | 0x84 |
| `[equipment upgrade max]` | 0x88 |
| `[ticket ipg]` | 0x8C |
| `[dungeon list]` | 0x90 / 0x98 / 0xA4 |
| `[boss dungeon list]` | 0xB8 |
| `[unique dungeon list]` | 0xCC |
| `[end dungeon list]` | 0xE0 |
| `[reward item]` | 0x108 |
| `[tile pos]` | 0x11C |

(All from tag-string → `lea [ebx+N]` pairs in 0x02BB0CD0; tag strings decrypted from
`literals.csv` rows for that function.)

---

## 6. Public surface of this unit

### Called from outside

| VA | callers | what it is |
| --- | --- | --- |
| `0x013096F0` | 55 | **`getInstance()`** — one-shot flag at 0x3A3DCA0 bit 0, instance ptr at **0x3A3DC9C**, `operator new(0x7C)` → ctor 0x013095D0. Returns 0 if allocation fails. |
| `0x01308A20` | 1 (stub 0x0130BFE0) | `onActionInfo(u32 dungeon, u32 item)` — S→C **0x1B3** |
| `0x01309B30` | 1 (stub 0x0130BFA0) | `onDiceResult(u8 slot, s8 count)` — S→C **0x1B0** |
| `0x01308B30` | 1 (stub 0x0130C050) | S→C **0x1B4**: bounds-checked `m_vecPlayer[slot]->0x013125B0()` |
| `0x01308AD0` | 1 (0x0118C3A0, `GameCoreNetwork_NOTI_Town.cpp`) | town-state hook; acts only when `gameState()==3 && arg==7` |
| `0x01308BA0` | 2 (0x0130A5D0, 0x0130AC80) | `allSlotsFilled()` — every slot 0..playerMax-1 non-null |
| `0x01308BE0` | 1 (0x0130A5D0) | `amIFirstPlayer()` — `m_vecPlayer[0] != 0 && isMine(m_vecPlayer[0])` (gates the start button / C→S 0x1F0 sub 0) |
| `0x01308C60` | 2 (0x0130A410 state 0, stub 0x0130C100) | `clearAllPlayers()` — virtual-delete each slot, null it |
| `0x01308CC0` | 1 (0x0130B250) | `getCurrentTurnPlayer()` — first player whose `+0x04` equals `this->[+0x70]` |
| `0x01308DE0` | 1 (0x013132C0) | chat/notice line: `format(UIstring[0xB641], player->0x1312620(), player->[+0x14])` into a 0x204-byte wide buffer, pushed to `g_0x3A5C9B8` |
| `0x01308E80` | 1 (0x0130AC80 tick) | visibility refresh: hides `[+0x10]`; `state==6` → show `[+0x10]`; shows `[+4]` when `state!=0`, hides it again unless `gameState()==1` |
| `0x01309790` | 1 (0x0130ABF0) | **rebuild progress ranking**: `map@+0x30` ← key 1 = {me}, then keys 2.. = players grouped by `player->[+0x14]` (map index) descending from `[+8]->size()`; skips finished players (`0x013125A0`) |
| `0x01309300` | 61 | **not this file** — folded `std::map::operator[]` (see §1) |

### Internal only

`0x01309050` init/load (ctor→), `0x013095D0` ctor, `0x01309520` dtor,
`0x01308D40` `releaseChildren()` (deletes +4, +8, +0x10, +0xC, +0x14, +0x18 then tail-jumps
to `clearAllPlayers`), `0x01309AD0` (`onDiceResult` tail helper).

### Below the anchor but same class (see §1)

| VA | what |
| --- | --- |
| 0x01308570 | `isMine(Player*)` — `getUser(p->[+8]) == g_0x3A5C980->getMyUser()` |
| 0x013085B0 | `getRankMap()` → `this+0x30` |
| 0x013085C0 | `isRunning()` → `[+0x44] != 0` |
| 0x013085D0 | `canShow()` → `[+0x44]!=0 && (gameState()==1 \|\| gameState()==2)` |
| 0x01308600 | dice click: `[+0xC]->0x130BC80([+4]->0x130DFF0())` (C→S 0x1F0 sub 1) + sound `DICE_CLICK` |
| 0x01308640 | focus board window |
| 0x01308680 | open window id **0x297** |
| 0x013086A0 | `stopwatch.elapsed()` |
| 0x013086B0 | `getRemainTurnTimeMs()` — `state==4 ? max(0, 10000-elapsed) : 0` |
| 0x013086D0 | `setSessionId(u16)` → `[+0x78]` |
| 0x013086E0 | `getSlotCount()` → `dataMgr->[0x28]` (= `[player max]`) |
| 0x013086F0 | `getRemainEnterCount()` → `max(0, dataMgr[0x34] - [+0x64])` |
| 0x01308710 | `getRemainTicketEnterCount()` → `max(0, dataMgr[0x3C] - [+0x68])` |
| 0x01308730 | `isCurrentTurn(Player*)` → `p->[+4] == [+0x70]` |
| 0x01308760 | `getPlayer(slot)` — bounds-checked |
| 0x013087A0 | `getMyPlayer()` |
| 0x01308830 | `addPlayer(slot, u16)` — refuses if the slot is taken; `new(0x64)`; then assigns display kind 0/1/2 |
| 0x01308990 | `removePlayer(slot)` — state≥3 keeps the object (`0xEE15A0(0)`), otherwise deletes and nulls |

---

## 7. Confidence and gaps

**High** for: the class identity, the S→C opcode table (0x1AF–0x1B5) and the two named
handlers' payloads, the C→S 0x1F0 sub-opcode table, the state field/jump table and the
10 000 / 5 000 ms constants, the `CNRDBlueMarble` field offsets, the `.blu` tag → offset map.

**Unknown / not claimed:**

* The *semantics* of C→S 0x1F0 subs 3, 4, 6, 7 — I have the emitters and the payload but
  no string or handler that names them.
* Which state constant is "2" in the server's own enum — the client only ever assigns 2
  from its own tick (0x0130ACA2), never from a packet.
* `0x0130A410`'s state 5 is only entered locally (0x0130A63B), never pushed by the server
  in this module, so a server may not need to model it.
* `0x14318C0` (the `< 3` test that picks full vs. delta roster sync in opcode 0x1AF) is
  outside the module and I did not resolve it.
* `[enter ticket count]` writes two ints (dataMgr 0x38 and 0x3C); only 0x3C is consumed by
  this class. What 0x38 means is unknown.
