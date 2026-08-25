# Unit 012 — `DNFClient\RDAR\CNRDBossTowerReward.cpp`

Briefing span `0x010C8EE0`–`0x010C97F0` (14 functions, 0 parsers).
Image base `0x00400000`. All addresses are VAs in the unpacked-from-memory image.

---

## 1. What this unit implements

`CNRDBossTowerReward` is the **client-side controller for the Death/Evil Tower
("Boss Tower") end-of-run reward panel**, and specifically for its **EPLP
sub-protocol** — the shared, server-arbitrated "pick a reward / re-challenge"
vote that every party member sees simultaneously.

The whole point of the EPLP traffic is that **the panel's button highlight is
not local**. A mouse-over or a click does not change the button art directly.
The client sends the (button, event) pair to the server, and only when the
server echoes it back does `setEplpButtonState` repaint the button. So every
member of the party watches the same cursor move over the same button.

Evidence the unit is the Boss/Death Tower panel and not a generic reward UI:

| VA | literal |
| --- | --- |
| `0x010C8EE0` | `D:\Work\1229\Source\DNFClient\RDAR\CNRDBossTowerReward.cpp` |
| `0x010C8EE0` | `CNRDBossTowerReward::sendEplpMouseEvent` |
| `0x010C97F0` | `CNRDBossTowerReward::setEplpButtonState` |
| `0x010C8EE0` | `서버로 EPLP 전송 btnIndex %d event %d` ("sending EPLP to server…") |
| `0x010C97F0` | `서버에서 받음 btnIndex %d event %d` ("received from server…") |
| `0x010CAE40` | `interface2/DeathTower/Rewardlist.img`, `EVILTOWER_UI_FLASH` |
| `0x010C9380` | (uses) `Common/ActiveStatus/Blind1.img` region darkener |

The object is reached from `CNRDInterfaceManager` at field **`+0x8F84`**
(`0x02300C70: mov eax,[ecx+0x8F84]; ret`), and the manager singleton lives at
`0x03A5C9B8`.

---

## 2. Span correction — the briefing's span is TOO NARROW at the top

The briefing's `hi` (`0x010C97F0`) is the *start* of the last anchored
function; that function really ends at `0x010C9A40`. Beyond that, the class
implementation demonstrably continues to at least `0x010CBF9x`:

* `0x010C9A40` restores button image indices out of the **same** table
  `0x035D9288` that the in-span `setEplpButtonState` overrides, and is called
  by the in-span-adjacent `0x010C9F43`.
* `0x010C9E60` (panel open/refresh) touches `+0x2498` (button array) and
  `+0x24C0` (throttle timer) and calls the in-span `0x010C92A0` reset.
* `0x010CAE40` writes `this->[0x38]/[0x3C]`, exactly the shared_ptr pair the
  in-span destructor releases at `0x010C972F`, and it loads
  `interface2/DeathTower/Rewardlist.img`.
* `0x010CBF20` produces the value stored into `this->[0x24E4]` by the in-span
  `setEplpButtonState` at `0x010C994D`.
* No other `__FILE__` literal appears anywhere in `0x010C9A40`–`0x010CC500`,
  so nothing competes for that region.
* Source line numbers agree: the send log is line **866** (`push 0x362`) and
  the receive log is line **889** (`push 0x379`) — 23 lines apart in one file
  that is at least 889 lines long. A 2 KB span cannot hold 889 lines.

**Do NOT annex `0x010C86A0`** (just below the span). It carries
`UI/GBossTower/Result.xui`, `Stair BG`, `Today Round`, `Today Total Round`,
`Today Time`, `Exp FX` — a separate XUI-driven result window with no offset
overlap shown. Likewise `0x010C3060`–`0x010C5C40` is `CNRDAdvanceAltarReward.cpp`
(unit 10), a near-identical *sibling* class, not this one (see §6).

---

## 3. Protocol — what a server implementer needs

### 3.1 C→S `ENUM_CMDPACKET_EPLP_COMMAND` = **0x48 (72)**

Opcode is not guessed from the constant alone; the id was resolved through the
client's own packet-name table:

1. `0x010C8F28  push 0x48` → `0x0274D1E0` = `beginPacket(u16 type, u8 flag)`
   (header is 14 bytes: `byte 1`, `u16 type` at offset 1, `u32 0`, `u8 flag`).
2. The debug logger indexes the CMD name table by the u16 at packet offset 1:
   `0x0163C055  movzx edx, word [edi+1]` /
   `0x0163C059  mov eax, [edx*4 + 0x03B53230]` → **CMD name table base `0x03B53230`**.
3. The literal-decrypt initializer at `0x02F93AAF` pushes the ciphertext of
   `ENUM_CMDPACKET_EPLP_COMMAND`; its decrypted pointer is stored by the next
   store, `0x02F93AC3  mov [0x03B53350], eax`.
4. `(0x03B53350 − 0x03B53230)/4 = 0x48`. ✔ matches the `push 0x48`.

**Payload (after header): `u8 event; u8 btnIndex` — 2 bytes, in that order.**

`0x0274D2E0` is `write_u8` — proved by its siblings, which are byte-for-byte
identical except for the length: `0x0274D310` (+2), `0x0274D340` (+4),
`0x0274D370` (+8). Each advances `[this+0x2BCC2C]` by its own width.

### 3.2 `CNRDBossTowerReward::sendEplpMouseEvent(int btnIndex, int event)` — `0x010C8EE0`

Send gating the server can rely on (a well-behaved client will never send more
than this):

```c
// btnIndex = [ebp+8] (edi), event = [ebp+0xC] (ebx), this = esi
if (event == 2) {                                   // 0x010C8EEE
    if (this->lastBtnIndex /*+0x24B8*/ == btnIndex) return;   // 0x010C8EF3 dedupe
    if (elapsed(&this->timer /*+0x24C0*/) < 500)     return;   // 0x010C8F0A  cmp eax,0x1F4
}
if (this->state /*+0x24F4*/ != 9 && btnIndex == 0)   return;   // 0x010C8F15
this->lastBtnIndex = btnIndex;  this->lastEvent = event;       // 0x010C8F2A/0x010C8F30
beginPacket(0x48, 0);  write_u8(event);  write_u8(btnIndex);  send();  // 0x010C8F28..0x010C8F5C
resetTimer(&this->timer);                                       // 0x010C8F67 (0x027C3410)
log("서버로 EPLP 전송 btnIndex %d event %d", btnIndex, event);   // line 866
```

So: **hover (event 2) is rate-limited to one packet per 500 ms per distinct
button; clicks (event 1) are never throttled.** The 500 unit is inferred from
the reset-after-send pattern on the `+0x24C0` object, not proved to be
milliseconds.

`0x010C8FC0` is the no-argument sibling in the same file (same `__FILE__` /
same function-name literal / same log line 866): if `state == 9` it sends
`(event=1, btnIndex=0)` — a forced "confirm slot 0". Its one external caller is
`0x01589D65` in `CNRDQuickPartyManager.cpp`, i.e. the quick-party flow
auto-answers the EPLP prompt on the player's behalf.

### 3.3 S→C `0x48` — the echo, handled by `setEplpButtonState`

`0x0110C354  push 0x48; push 0x01106C00; ...` registers the inbound handler
`0x01106C00` (in `GameCoreNetwork_CMD_Etc.cpp`, unit 60) for id **0x48**.

`0x01106C00` reads exactly two bytes (`0x01106C46`, `0x01106C51`, then
`add esp,0x10`), then routes on the current dungeon kind
(`0x0163E1E0` on the manager from `0x01102C30`):

| kind | module (CNRDInterfaceManager offset) | call |
| ---: | --- | --- |
| 9 | `+0x8F70` (`0x02300C20`) | `0x0261D710` |
| **0xD (13)** | **`+0x8F84` (`0x02300C70`) — this class** | **`setEplpButtonState` @ `0x01106D4D`** |
| 0xE (14) | `+0x8F8C` (`0x02300CD0`) | — |

Argument mapping at `0x01106D36`: first wire byte → `event`, second → `btnIndex`,
identical to the outbound order. **The server echoes the same 2-byte body it
received.**

### 3.4 `CNRDBossTowerReward::setEplpButtonState(int btnIndex, int event)` — `0x010C97F0`

```c
this->[0x24B8] = btnIndex;  this->[0x24BC] = event;             // 0x010C9808/0x010C980E
log("서버에서 받음 btnIndex %d event %d", btnIndex, event);      // line 889

if (!0x019E7810()) {                                            // 0x010C9848 (global UI-mode flag)
    for (i = 0; i < 4; ++i) {                                   // 0x010C9876..0x010C990E
        int frame = T[i][ (i == btnIndex) ? event : 0 ];
        // all four button states forced to the same frame:
        button[i]->0x027F2240(frame, frame, frame, frame, 0, 0, {0,0});
    }
}
if (event == 1) {                                               // 0x010C9917
    this->selectedButton /*+0x24DC*/ = btnIndex;                // 0x010C9920
    this->[0x24E0] = localChar->[0x34];                         // 0x010C993F
    this->[0x24E4] = 0x010CBF20();                              // 0x010C994D
    switch (btnIndex) {                                          // 0x010C9953
      case 0: sysMsg(strId 479 /*0x1DF*/, colour 0xFF9BC8E6, ttl 30); break;
      case 1: sysMsg(strId 480 /*0x1E0*/, ...); break;
      case 2: sysMsg(strId 481 /*0x1E1*/, ...); break;
      default: /* btnIndex 3 produces no message */ break;
    }
}
```

`T` = `int[4][4]` at **`0x035D9288`** (0x40 bytes, read out of the image):

| button | event 0 | 1 | 2 | 3 |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 5 | 6 | 7 | 8 |
| 1 | 9 | 10 | 11 | 12 |
| 2 | 13 | 14 | 15 | 16 |
| 3 | 33 | 34 | 35 | 36 |

These are **sprite frame indices, not colours** (the values are small integers,
not float bit patterns). The sibling routine `0x010C99C0` — same file, same
table, same 4 buttons — restores the *normal* mapping
`(normal, over, press, disable) = (T[i][0], T[i][1], T[i][2], T[i][3])`.
That fixes the meaning of `event`:

> **event 0 = normal, 1 = pressed/selected, 2 = mouse-over, 3 = disabled.**
> `btnIndex` ∈ 0..3.

`setEplpButtonState` therefore *locks* every button to a single frame and gives
the addressed button the frame for the reported event — a remote cursor.

### 3.5 S→C `ENUM_NOTIPACKET_BOSS_TOWER_STATE_EPLP` = **0x336 (822)**

Registration proved directly: `0x0115FC4C  push 0` / `0x0115FC4E  push 0x01147C00` / `0x0115FC53  push 0x336`.
The NOTI name table base is **`0x03B51F20`**
(`0x0163C11B movzx eax, word [edi+1]` / `0x0163C11F mov ecx,[eax*4+0x3B51F20]`),
and the slot for the decrypted `ENUM_NOTIPACKET_BOSS_TOWER_STATE_EPLP` string
is `0x03B52BF8` → `(0x03B52BF8−0x03B51F20)/4 = 0x336`. ✔ Two independent
methods agree, which validates the arithmetic used for the ids below.

Handler `0x01147C00`, **payload = `u8 isRetriable`**, log
`"Boss Tower State EPLP = isRetriable %d"` (line 10570):

```c
s = obj->getState();                                    // 0x010C9210
obj->setState( (isRetriable && (s == 0 || s == 9)) ? 9 : 5 );   // 0x010C9200
```

**State 9 is the only state in which the client will emit a `0x48`.**
State 5 = closed/idle. This is the state machine the server must mirror: the
server must not accept EPLP commands unless it has itself put the client into
state 9 with this packet (or with 0x105 below).

### 3.6 S→C `ENUM_NOTIPACKET_BOSS_TOWER_STATE_REWARD` = **0x335 (821)** — the reward table

Registration `0x0115FC39  push 0` / `push 0x01147910` / `0x0115FC40  push 0x335`. This is the
packet that **fills** this class's reward grid before the EPLP vote opens.
Format recovered from `0x01147910`–`0x01147B1D` and confirmed by the log format
string `'%d seat(active%d) - [%d] Reward itemindex %d addinfo %d'` (`0x03244B80`,
line 10529):

```
u32 exp                          // logged "Boss Tower State Reward : exp %d", line 10502
for (seat = 0; seat < 8; ++seat) // 0x01147B1A: cmp edi, 8
{
    u8  count                    // 0x01147A6C read_u8
    for (i = 0; i < count; ++i)
    {
        u32 itemIndex            // 0x01147A85 read_u32
        u32 addInfo              // 0x01147A8E read_u32
    }
}
```

Notes for the server:

* **All 8 seats are always on the wire.** The client reads both u32s
  unconditionally and only *stores* them if the seat is locally considered
  active (`0x019E7750` on the manager at `0x03A5C990`). Skipping inactive
  seats would desynchronise the stream.
* Each seat holds **at most 10 entries** (the group's slot array is
  `10 × 0x6C` bytes, constructed at `0x010C9258  push 0xA; push 0x6C`). A
  larger `count` is not bounds-checked at the read site.
* Before parsing, the handler calls `getBossTowerReward()->0x010C9E60(0)` —
  open/refresh the panel (`0x0114798D`).
* After parsing: `->0x0261BE30(exp)` (`0x01147B34`) and
  `->0x00CB7040(activeSeatCount)` (`0x01147B4A`).
* Per seat, if active: `getGroup(seat)->[0x24] = seat` (`0x01147A57` →
  `0x010C91D0`, then `0x010C91C0`), and each pair is appended via
  `getGroup(seat)->0x0261C0F0()->0x0261FA20(itemIndex, addInfo, 0, ...)`.

### 3.7 The other EPLP ids (same table arithmetic, each confirmed by a registration)

| name | id | handler | note |
| --- | ---: | --- | --- |
| `ENUM_NOTIPACKET_DEATH_TOWER_STATE_EPLP` | 0x92 (146) | `0x01146DE0` | |
| `ENUM_NOTIPACKET_EPLP_RECHALLENGE` | 0x105 (261) | `0x01163830` | shared across all reward modules |
| `ENUM_NOTIPACKET_BOSS_TOWER_STATE_REWARD` | 0x335 (821) | `0x01147910` | §3.6 |
| `ENUM_NOTIPACKET_BOSS_TOWER_STATE_EPLP` | 0x336 (822) | `0x01147C00` | §3.5 |
| `ENUM_NOTIPACKET_ADVANCE_ALTAR_EPLP_STATE` | 0x360 (864) | `0x01147D10` | sibling class |

**`0x105 EPLP_RECHALLENGE`** payload = `u8 state` (`0x01163840`). It fans out by
dungeon kind through a 16-entry jump table (`bytemap 0x01163BAC`, targets
`0x01163B90`, biased by −3):

| dungeon kind | target module |
| ---: | --- |
| 3, 11, 12 | `+0x8F68` (`0x02300C00`) |
| 9 | `+0x8F70` (`0x02300C20`) |
| 10 | `0x011639A7` |
| **13** | **`+0x8F84` — this class** (`0x01163AC2`) |
| 14 | `+0x8F8C` (`0x02300CD0`) |
| 18 | `0x01163937` |
| 4–8, 15–17 | ignored |

The kind-13 branch (`0x01163AC2`–`0x01163B1E`) is:

```c
if (state == 9) { s = getState(); if (s == 0 || s == 9) setState(9); else setState(5); }
else            { s = getState(); if (s != 1 && s != 5) setState(5); }
```

Same 9/5 vocabulary as 0x336. **Dungeon kind 13 == Boss Tower** is established
twice independently (the 0x48 CMD dispatch at `0x01106D31` and this table).

---

## 4. Object layout — `CNRDBossTowerReward`

Every offset below is backed by an instruction. `this` is the pointer at
`CNRDInterfaceManager + 0x8F84`.

| offset | size | meaning | proof |
| --- | --- | --- | --- |
| `+0x000` | 4 | vptr → vtable `0x0322F0A4` (`[0]=0x010CB4F0`, `[1]=0x02231720`); RTTI locator at `0x0322F0A0` names 3 bases but the type name is stripped | `0x010C961A  mov [esi], 0x322F0A4` |
| `+0x008` | — | owned sub-object destroyed last | `0x010C97CB  lea ecx,[esi+8]; call 0x027C33F0` |
| `+0x024` | 4 | ptr released via `vtbl[0](1)`, then nulled | `0x010C9620`–`0x010C9636` |
| `+0x02C` | 8 | shared_ptr (obj/ctrl) | `0x010C9797` release pattern |
| `+0x034` | 8 | shared_ptr | `0x010C9763` |
| `+0x038` / `+0x03C` | 8 | shared_ptr — the `interface2/DeathTower/Rewardlist.img` sprite | set `0x010CAE9B/0x010CAE9E`, released `0x010C972F` |
| `+0x044` | 8 | shared_ptr | `0x010C96FB` |
| `+0x048` | — | timer/animator object | `0x010C92C0` reset, `0x010C92CC` set, `0x010C96EF` dtor |
| `+0x064`..`+0x083` | 32 | zeroed block (4× `movq xmm0`) | `0x010C92A8`–`0x010C92BB` |
| `+0x084` | 4 | int, init `0xFF` | `0x010C92F5` |
| `+0x088` | 4 | int, init 0 | `0x010C92FF` |
| **`+0x090`** | **8 × 0x480** | **RewardGroup[8]** — one per party *seat* | vector-dtor `0x010C96D3  push 0x261C080; push 8; push 0x480; lea ecx,[esi+0x90]`; parse loop `0x01147B1A cmp edi,8`; accessor `0x010C91D0` |
| `+0x2490` | 4 | shared_ptr object (UI effect helper) | `0x010C98A5`, `0x010C99C4` |
| `+0x2494` | 4 | its control block (weak lock via `lock cmpxchg [esi+4]`) | `0x010C989F`–`0x010C98E3`, dtor `0x010C969C` |
| **`+0x2498`** | **4 × 8** | **button[4]** (8-byte smart-pointer elements) | vector-dtor `0x010C9683  push 0x4EC340; push 4; push 8; lea eax,[esi+0x2498]`; ends exactly at `+0x24B8` |
| **`+0x24B8`** | 4 | `lastBtnIndex`, init `-1` | `0x010C9347`, read `0x010C8EF3`, written `0x010C8F2A` / `0x010C9808` |
| **`+0x24BC`** | 4 | `lastEvent`, init 0 | `0x010C934D`, `0x010C8F30`, `0x010C980E` |
| **`+0x24C0`** | — | hover throttle timer | `0x010C8EFF` elapsed (`0x027C3660`), `0x010C8F61` reset, dtor `0x010C9674` |
| **`+0x24DC`** | 4 | selected button, init **4** (= "none", since buttons are 0..3) | `0x010C935F`, getter `0x010C91F0`, written `0x010C9920` |
| `+0x24E0` | 4 | init `-1`; set to `localChar->[0x34]` on event 1 | `0x010C9369`, `0x010C9942` |
| `+0x24E4` | 4 | init `-1`; set to `0x010CBF20()` (a table lookup keyed on `localChar->[0x34]`) on event 1 | `0x010C936F`, `0x010C994D` |
| `+0x24E8` | 4 | init 0; cached scene object whose id is **`0xBB9A` (48026)**, used by the spotlight renderer | `0x010C9353`, search `0x010C93F3  cmp eax, 0xBB9A`, store `0x010C940C` |
| `+0x24EC` | 4 | render context passed to the tile draw | `0x010C9474`, `0x010C9565` |
| `+0x24F0` | 8 | shared_ptr | dtor `0x010C963D` |
| **`+0x24F4`** | 4 | **state**, init 0; **9 = EPLP prompt live, 5 = closed** | `0x010C9359`, getter `0x010C9210`, setter `0x010C9200`, gate `0x010C8F15` / `0x010C8FC3` |

### RewardGroup (0x480 bytes) — the per-seat reward column

| offset | meaning | proof |
| --- | --- | --- |
| `+0x000`..`+0x023` | base sub-object | ctor `0x010C9249  call 0x027C3A90` |
| `+0x024` | int; `-1` after reset, set to the seat index by NOTI 0x335; `0x010C9160` returns early (`al=1`) while it is `-1`. Exact role unproven. | `0x010C9075  mov [edi+0x24], -1`; setter `0x010C91C0`; `0x010C9163  cmp [edi+0x24], -1` |
| `+0x028` | `Slot[10]`, each 0x6C bytes | `0x010C9258  push 0xA; push 0x6C; lea eax,[esi+0x28]` (vector-ctor `0x261BFE0`, dtor `0x010C3160`) |
| `+0x460` | UI/colour object | `0x010C90A0  call 0x027F4570`, dtor `0x010C9276` |
| `+0x461` | byte = 1 | `0x010C90C9` |
| `+0x462` | u16 = 200 (`0xC8`) | `0x010C90BB` |
| `+0x466` | byte = 1 | `0x010C90C2` |
| `+0x468` | dword ← global `[0x04C2D5E8]` (0.0f) | `0x010C90B5` |

Slot fields touched here: `+0x00` = int (set to 1) and `+0x04` = `i * [0x0397A028]`
(the global is 100) — a per-index stagger, i.e. an animation delay
(`0x010C9125`–`0x010C9133`).

---

## 5. The unit's functions and its public surface

| VA | role | called from outside |
| --- | --- | --- |
| `0x010C8EE0` | **`sendEplpMouseEvent(btnIndex, event)`** — §3.2 | `0x010CA882` (hover, event 2), `0x010CA897` (click, event 1), `0x010CAA65` (click) — all in the panel's own mouse handler at `0x010CA2F0`, i.e. the same class |
| `0x010C8FC0` | forced `(event=1, btnIndex=0)` send, gated on `state==9` | `0x01589D65` — `CNRDQuickPartyManager.cpp` (unit 96) |
| `0x010C9070` | `RewardGroup::reset()` — slot array reset, `+0x24 = -1`, re-init `+0x460` block | `0x010C50A0`, `0x01A0C980` (see ICF caveat) |
| `0x010C90E0` | arm the per-slot reveal animation for `n` slots (`n` = `localChar->[0x434]` via `0x00E11570`) | `0x010CAE40` (same class) |
| `0x010C9160` | `bool allSlotAnimationsDone()` — `true` immediately if `+0x24 == -1` | `0x010CAE40` (same class) |
| `0x010C91C0` | `group->[0x24] = arg` | 7 sites, most unrelated — **ICF-folded**, see §7 |
| `0x010C91D0` | **`getGroup(int idx)` → `this + 0x90 + idx*0x480`** | `0x01147A57`, `0x01147AB3`, `0x011488BC`, `0x01148910` (NOTI_Dungeon reward parse) |
| `0x010C91F0` | `getSelectedButton()` → `[+0x24DC]` | `0x00E11D65`, `0x0573C538` |
| `0x010C9200` | **`setState(int)`** → `[+0x24F4]` | 8 sites in NOTI_Dungeon / NOTI_Etc |
| `0x010C9210` | **`getState()`** → `[+0x24F4]` | 6 sites, same files |
| `0x010C9220` | `RewardGroup` constructor (10×0x6C slots, `+0x460` init, then `reset`) | `0x2C63AB7` vector-ctor thunks |
| `0x010C92A0` | `reset()` on the outer object — clears `+0x64..+0x88`, resets 4 groups, hides the 4 buttons (`vtbl[8](0)`), restores `+0x24B8/+0x24BC/+0x24DC/+0x24E0/+0x24E4/+0x24E8/+0x24F4` | `0x010C9E60` (same class) |
| `0x010C9380` | **spotlight/blind renderer** — tiles a 50 px darkening quad over the whole 800×600 screen (`[0x0399AE98]=800`, `[0x0399AE9C]=600`) except a 500×500 hole centred on the cached object `0xBB9A` (`x−0xF5`, `y−0x159`, both floored to 50) | `0x0199CC40` — `CNRDStage.cpp` (unit 29) draw hook |
| `0x010C95F0` | destructor (vptr `0x0322F0A4`, releases every pointer in §4 in reverse) | `0x0230DBC0` — `CNRDInterfaceManager.cpp` (unit 16) |
| `0x010C97F0` | **`setEplpButtonState(btnIndex, event)`** — §3.4 | `0x01106D4D` — the 0x48 inbound handler |
| `0x010C99C0` | restore the normal 4-state image mapping on all 4 buttons from `T[i][0..3]` (undoes `setEplpButtonState`) | tail of `0x010C97F0`'s block |

Just past the briefing's span but the same class (§2): `0x010C9A40` (button
image restore variant, called from NOTI_Etc `0x01163987`/`0x0116399B`),
`0x010C9E60` (open/refresh, called by NOTI 0x335 at `0x0114798D`),
`0x010CA2F0` (mouse/`CLICK_BUTTON2` handler — the only producer of outbound
EPLP), `0x010CAE40` (resource load: `Rewardlist.img`, `EVILTOWER_UI_FLASH`),
`0x010CB4F0` (vtable slot 0), `0x010CBF20`.

---

## 6. Sibling warning — `CNRDAdvanceAltarReward` is a near-clone, not this class

`DNFClient\RDAR\CNRDAdvanceAltarReward.cpp` (unit 10, `0x010C3060`+) contains a
byte-for-byte parallel `sendEplpMouseEvent`: **same opcode 0x48, same
`event==2` dedupe, same `cmp eax,0x1F4` throttle, same `state==9` gate** — only
the field offsets differ (`+0x2578/+0x257C/+0x2580/+0x25B4` instead of
`+0x24B8/+0x24BC/+0x24C0/+0x24F4`). Its state packet is a different id
(`ENUM_NOTIPACKET_ADVANCE_ALTAR_EPLP_STATE` = 0x360).

Consequence for a server: **the EPLP command opcode 0x48 is shared by several
reward modules and carries no module discriminator.** The server must
demultiplex it by the player's current dungeon kind, exactly as the client does
at `0x01106CF3`/`0x01106D31`/`0x01106D6F` (9 / 13 / 14) — Boss Tower is kind 13.

---

## 7. Caveats and things I could not prove

* **ICF folding.** The tiny accessors `0x010C91C0`, `0x010C91D0`, `0x010C91F0`,
  `0x010C9200`, `0x010C9210` are 3–7 instruction one-liners. MSVC identical-COMDAT
  folding merges such functions across classes, so their large external caller
  lists (e.g. `0x010C91C0` is reached from `0x01417650`, `0x01466610`,
  `0x02406900`, `0x02793870`) are **not** evidence about this file's public
  surface. Only the call sites that reach them through `0x02300C70`
  (`getBossTowerReward`) are attributable — those are `0x01147A50`,
  `0x01147AAC`, `0x01147C7B`, `0x01147C91`, `0x01147CAE`, `0x01163AC8`.
  Correspondingly, `state` at `+0x24F4` is also the layout of at least one
  sibling module (`+0x8F70`), which is why the same `setState` body serves both.
* The `500` in the hover throttle is compared against the return of
  `0x027C3660`, a handle-resolving wrapper (`[this+0xC]` → `0x027BBFD0`). Reset
  happens immediately after each send, so it is an elapsed-time getter; the
  **unit** (ms) is inference, not proof.
* `+0x24E0` / `+0x24E4` are filled from the **local** character
  (`localChar->[0x34]`) even though `setEplpButtonState` is driven by the
  server and may describe another player's action. Reported as observed; I did
  not determine whether that is intentional or a client bug.
* The RTTI type name is stripped/scrambled (`0x0397A02C` contains no readable
  string), so the class name comes from the `__FILE__` + method-name literals
  only. That is still direct client testimony, not inference.
* `0x019E7810` (the `if (!…)` guard around the whole repaint in
  `setEplpButtonState`) reads a manager at `0x03A5C990`; what mode it selects is
  unknown.
* Whether `getGroup(idx)` is valid for idx 4..7 in every path is unclear: the
  array is 8 elements (dtor + the NOTI 0x335 loop both say 8), yet
  `0x010C92A0` resets only 4 and there are only 4 buttons.
