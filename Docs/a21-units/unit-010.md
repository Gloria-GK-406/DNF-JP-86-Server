# TU 010 — `DNFClient\RDAR\CNRDAdvanceAltarReward.cpp`

Briefing span `0x010C3060`–`0x010C3E00`, 14 functions, 0 parsers.
Image base `0x00400000`. All VAs below are image VAs in the memory dump.

---

## 0. What this unit is

`CNRDAdvanceAltarReward` is the **result / reward window of the "Advance Altar"
(고급 제단) dungeon**. Its job is the *post-clear choice panel*: after the altar run
ends, the panel shows a small set of buttons ("what next?"), it reports every
mouse interaction on those buttons to the server as an **EPLP** event, the server
authoritatively decides which button is lit/selected for the whole party and
echoes the state back, and the client then remembers the chosen action and
performs it when the player next lands in town.

This is a **party-synchronised vote/selection widget**, not a local UI. The
button highlight state on your screen is driven by the server round trip
(`서버로 EPLP 전송` → `서버에서 받음`), not by your own click. That is the one
thing a server implementer must mirror.

Evidence anchors:

| VA | decrypted literal |
| --- | --- |
| `0x010C3060` | `D:\Work\1229\Source\DNFClient\RDAR\CNRDAdvanceAltarReward.cpp` |
| `0x010C3060` | `CNRDAdvanceAltarReward::sendEplpMouseEvent` (line `0x510` = 1296) |
| `0x010C3060` | `서버로 EPLP 전송 btnIndex %d event %d` ("sending EPLP to server") |
| `0x010C3E00` | `CNRDAdvanceAltarReward::setEplpButtonState` (line `0x527` = 1319) |
| `0x010C3E00` | `서버에서 받음 btnIndex %d event %d` ("received from server") |
| `0x010C3A30` | `CLICK_BUTTON2` (sound cue on the confirmed click) |

There is an **exact sibling class** built from the same source template:
`CNRDBossTowerReward` (TU 12, `0x010C8EE0`–`0x010C97F0`) — byte-identical code
shape, different field offsets, same packet. Anything below about the wire format
applies to both.

---

## 1. Server-relevant: packet `0x48` (EPLP button event), both directions

### Registration (proof of the packet id)

`0x0110C354`, inside `GameCoreNetwork_CMD_Dungeon.cpp`:

```
0110C354  push 0                 ; flag
0110C356  push 0x1106c00         ; handler
0110C35B  push 0x48              ; CMD packet id  <<<<
0110C35D  mov  ecx, esi
0110C35F  call 0x163c4a0         ; registerCmdHandler(id, fn, flag)
```

Neighbours in the same table: id `0x2D`→`0x1105E10`, `0x30`→`0x1106AB0`,
`0x12C`→`0x1106050`.

### C → S — `CNRDAdvanceAltarReward::sendEplpMouseEvent(int btnIndex, int event)` @ `0x010C3060`

```
010C30A6  push 0
010C30A8  push 0x48
010C30B6  call 0x274CA50          ; CNGameSocket* g_socket = [0x3B7CEE4]
010C30BD  call 0x274D1E0          ; CNGameSocket::MakePacket(u16 type=0x48, u8 flag=0)
010C30C2  push ebx                ; event   (arg2)
010C30D7  call 0x274D2E0          ; write_u8
010C30CF  push edi                ; btnIndex (arg1)
010C30D7  call 0x274D2E0          ; write_u8
010C30DC  call 0x274E6D0          ; CNGameSocket::SendPacket
```

**Wire body: `u8 event; u8 btnIndex;`** — event first.

Writer family identified from `CNGameSocket.cpp` (TU 5) by the byte count they
add to `[this+0x2BCC2C]` and pass to the vmp'd writer `0x5790FE9`:

| VA | width |
| --- | --- |
| `0x0274D2E0` | 1 byte |
| `0x0274D310` | 2 bytes |
| `0x0274D340` | 4 bytes |
| `0x0274D370` | 8 bytes |
| `0x0274D3A0` | u32 length + bytes |

`MakePacket` at `0x0274D1E0` emits a 14-byte header (`push 0xE` at `0x0274D2A1`)
of shape `{u8 1; u16 type; u32 0; …; u8 flag}`.

### Client-side send gates (a server sees these as *absent* traffic)

```
010C306E  cmp  ebx, 2                       ; event == 2 (hover) ?
010C3073  cmp  [esi+0x2578], edi            ;   and same btnIndex as last time ?
010C3085  call 0x27C3660                    ;   elapsed(this->timer @ +0x2580)
010C308A  cmp  eax, 0x1F4                   ;   < 500 ms  -> DROP, send nothing
010C3095  cmp  [esi+0x25B4], 9              ; mode != 9
010C309E  test edi, edi                     ;   and btnIndex == 0  -> DROP
```

* Repeated **hover (event 2) on the same button is throttled to one packet per
  500 ms**. Timer reset after each send (`0x27C3410` at `0x010C30E7`,
  `0x27C3B60(0,0)` at `0x010C30F6`).
* **`btnIndex == 0` is only ever sent while `mode(+0x25B4) == 9`.** So the client
  refuses locally; a server test for "client sends btn 0 in mode 5" is not
  constructible from the UI.

### S → C — handler `0x01106C00` (in `GameCoreNetwork_CMD_Dungeon.cpp`)

```
01106C2E  cmp  byte [ebp+0xC], 0
01106C34  je   0x1106E1F                    ; other branch (reads 1 u8, tests [ebp+0x10]==0x85) — not analysed
01106C3A  lea  eax, [ebp-0xD]  ; push; push 1
01106C46  call 0x27C5230                    ; read_u8  -> byte0
01106C4B  lea  ecx, [ebp+0xF]  ; push 1; push
01106C51  call 0x27C5230                    ; read_u8  -> byte1
```

**Wire body S→C: `u8 event; u8 btnIndex;` — identical layout to C→S.**

Then it routes on the current dungeon type
(`getWorld()->getDungeon()->vtbl[0x2C]()`, wrapper `0x0163E1E0` in
`GameCoreSub.cpp`):

| dungeon type | target | note |
| ---: | --- | --- |
| `9` | `[ifMgr+0x8F70]` → `0x0261D710` | another reward window |
| `0xD` | `[ifMgr+0x8F84]` → `0x010C97F0` | `CNRDBossTowerReward::setEplpButtonState` |
| **`0xE`** | **`[ifMgr+0x8F8C]` → `0x010C3E00`** | **this unit** |
| `0xA` | `0x023057B0` → `0x023DA2E0` | |
| else | `[ifMgr+0x8F68]` → `0x025F1E90` | |

so **dungeon type `0x0E` == Advance Altar**. `ifMgr` is the global
`CNRDInterfaceManager*` at `[0x03A5C9B8]`.

Argument order at the call (`0x01106D74`–`0x01106D8B`): the two bytes are pushed
`byte0` then `byte1`, then `0x02300CD0` (`return [ecx+0x8F8C]`, `ret 0`) leaves
them in place for the `__thiscall`, so
`setEplpButtonState(arg1 = byte1 = btnIndex, arg2 = byte0 = event)`.
Argument naming is proven by the log varargs at `0x010C3E0F`
(`push esi; push ebx; push fmt("… btnIndex %d event %d")` → `ebx`=arg1=btnIndex).

### `event` values

| value | meaning | proof |
| ---: | --- | --- |
| `2` | hover / highlight | `0x010C3AE7` `call 0x27DCC90` → `sendEplpMouseEvent(i, 2)`; throttled 500 ms |
| `1` | **confirmed click / commit** | `0x010C3AFC` `call 0x27DCC30` → `sendEplpMouseEvent(i, 1)` + plays `CLICK_BUTTON2` (`0x010C3B27`); receive side commits only on `event == 1` (`0x010C3F27 cmp esi,1`) |
| `0`, `3` | idle / reset visual only | present in the frame table, never emitted by this unit |

`0x027DCC90` / `0x027DCC30` are widget-state predicates:
`if (!w->enabled@+0x31) return false; return g_state == w->id@+0xD4;` against
globals `[0x3998E0C]` and `[0x3998E00]` respectively.

---

## 2. Server-relevant: what the confirmed click commits to

`setEplpButtonState`, `0x010C3F27` onward — **runs only when `event == 1`**:

```
010C3F30  mov  [edi+0x259C], ebx            ; pendingAction = btnIndex
010C3F36  call 0x1102C30                    ; getWorld()   (jmp 0x1637C60)
010C3F3B..0x010C3F4A                        ; ...->0x171C570 (returns this+0x140)
010C3F4F  mov  edx, [eax+0x34]
010C3F52  mov  [edi+0x25A0], edx            ; dungeonId
010C3F58  call 0x10CBF20                    ; current difficulty
010C3F5D  mov  [edi+0x25A4], eax            ; difficulty
          switch (btnIndex) {
010C4013:   case 0: systemMessage(getStr(0x1DF)); leaveSequence();
010C3FAC:   case 1: systemMessage(getStr(0x1E0)); leaveSequence();
                    [0x3A42F94]->setTutorialType(5)   ; 0x1465FB0 =
                    ; advancealtar::AdvanceAltarMode::setTutorialType
010C3F78:   case 2: systemMessage(getStr(0x1E1));      ; no leave sequence
          }
```

`leaveSequence()` = `0x1A0FFB0(0xF)` + `0x27B5BD0()->0x5744276` +
`0x27B5BD0()->0x27B23A0`. `getStr` is `0x028C14C0` (`getStr(%d) == none` literal);
messages `0x1DF/0x1E0/0x1E1` are pushed with colour `0xFF9BC8E6`, size `0x1E`.

### The deferred action — `0x010C3220`, called from `CNTownModule::onEnterModule`

Call site `0x02709B7A`, guarded by `cmp ebx, 0xE` (only for the altar dungeon).
Jump table at `0x010C3328`, index = `[this+0x259C]`, 5 entries:

| `+0x259C` | target | behaviour |
| ---: | --- | --- |
| `0` | `0x010C325A` | `world->…->0x2706900(0)`; if `0x19E7810()` then **`MakePacket(0x0F,0); write_u32(this->dungeonId@+0x25A0); Send`**; then `CNSelectDungeonModule::sendSelectDungeonPacket(dungeonId, difficulty, 0)` (`0x0272D990`) — *re-enter the same dungeon* |
| `1` | `0x010C32C9` | `world->…->0x2706900(1)`; if `0x19E7810()` then **`MakePacket(0x0F,0); write_u32(0); Send`** — *go to dungeon select* |
| `2` | `0x010C3239` | `world->0xA9AEE0()->vtbl[0xA4](1, 0)` — *stay / town* |
| `3` | `0x010C330A` | no-op |
| `4` | `0x010C3239` | same as 2 (4 is the reset/idle value) |

Tail always resets: `+0x259C = 4`, `+0x25A0 = -1`, `+0x25A4 = -1`
(`0x010C330D`–`0x010C331D`).

`sendSelectDungeonPacket(int dungeonId, int difficulty, bool)` — `ret 0xC`,
and it **validates `difficulty <= 4`** (`0x0272D9D1 cmp [ebp+0xC],4; ja fail`) and
`dungeonId >= 0`. So the retry path re-sends the *same* dungeon id + difficulty
the player just finished.

**Packet `0x0F` carries a single `u32`** (dungeon id, or `0` for "none"). It has 40
send sites image-wide; neighbours include `[TCOMBAT] 던전입장시도` (dungeon-entry
attempt) at `0x00AAAD7C` and `CNTownModule::Proc`, i.e. it is the generic
"leave / move-to-dungeon" command, not altar-specific.

---

## 3. Object layout — `CNRDAdvanceAltarReward` (vftable `0x0322EA20`)

Every row has the instruction that proves it.

| offset | type | meaning | proof |
| ---: | --- | --- | --- |
| `+0x04` | `int` | window/interface kind, compared to `8` | `0x010C3343 cmp [esi+4],8` |
| `+0x28` | `Widget*` | panel A, `vtbl[8](bool)` = show/hide | `0x010C3356` |
| `+0x30` | `Widget*` | panel B / background; event source that sends nothing | `0x010C3367`, `0x010C3A87 cmp [ebp+8],[edi+0x30]` |
| `+0x90` | `u8` | set from NOTI `ADVANCE_ALTAR_CLEAR_STAGE` | setter `0x010C31C0`, caller `0x01150DA3` |
| `+0x94` | `int` | non-zero ⇒ extra draw branch | `0x010C3461` |
| `+0x98`,`+0x9C` | `int,int` | pair set from same NOTI | setter `0x010C3040`, caller `0x01150D8D` |
| `+0xA0` | `int` | `== 1` ⇒ victory layout, else defeat layout | `0x010C33C8`, `0x010C3455` |
| `+0xA8`,`+0xAC` | `T*`,`T*` | vector of **12-byte** elements (per-stage rows) | `0x010C3677`; `imul 0x2AAAAAAB; sar edx,1` = ÷12 |
| `+0xBC`,`+0xC0` | `T*`,`T*` | second vector of 12-byte elements | `0x010C38B9` |
| `+0x134` | `void*` | sprite/atlas handle passed to `0x27D7F30` | `0x010C342C`, `0x010C3EB5` |
| `+0x138` | `RefCounted*` | deferred task, acquired via `lock cmpxchg [esi+4]` | `0x010C3EAF`–`0x010C3ED8` |
| **`+0x2568`** | `T[2]`, stride 8 | **the two EPLP button widgets** | `0x010C3AD1 lea ebx,[edi+0x2568]` + `mov [ebp-0x10],2`; dtor `0x010C53AE` calls the array-dtor helper `0x2C63A54` with **count 2, element size 8** |
| `+0x2578` | `int` | last btnIndex (init `-1`) | `0x010C30AA`, `0x010C3E18`, `0x010C52AC` |
| `+0x257C` | `int` | last event (init `0`) | `0x010C30B0`, `0x010C3E1E`, `0x010C52B2` |
| `+0x2580` | `CTimer` | hover throttle clock | `0x010C307F`/`0x010C30E1`/`0x010C30F0`, ctor `0x010C5396` |
| `+0x259C` | `int` | pending action (`0..4`, `4` = none) | `0x010C3143`, `0x010C3223`, `0x010C3F30` |
| `+0x25A0` | `int` | dungeon id to re-enter (init `-1`) | `0x010C314D`, `0x010C3F52` |
| `+0x25A4` | `int` | difficulty `0..4` (init `-1`) | `0x010C3153`, `0x010C3F5D` |
| `+0x25A8` | `Object*` | cached altar prop, **object index `0xBB9A` (48026)** | `0x010C3C03 cmp eax,0xBB9A`, `0x010C3C1C` |
| `+0x25AC` | `Sprite*` | `Common/Etc/MonoLayer.img` frame for the dim mask | `0x010C3C84`, assigned `0x010C4CA2`/`0x010C5E13` |
| `+0x25B0` | `Sprite*` | companion of `+0x25AC` | `0x010C4C8F`, `0x010C5E0D` |
| `+0x25B4` | `int` | reward-window mode; `{0,1,5,9}` observed | setter `0x010C3200`, getter `0x010C3210`; set to `9` at `0x01148D6F` and `0x01163B5A`, to `5` at `0x01163B82` |

Static table used by the receive path, `0x0322E81C`, **2 rows × 4 columns of `u32`**
(loop bound `cmp eax, 0x322E83C`):

```
btn 0 :  25 26 27 25        (0x19 0x1A 0x1B 0x19)
btn 1 :  28 29 30 28        (0x1C 0x1D 0x1E 0x1C)
         ^event 0..3
```

`setEplpButtonState` writes `table[i][ btnIndex==i ? event : 0 ]` into
`widget[i]` via `0x027F2240`, pushing the value **four times** (`0x010C3E99`–
`0x010C3E9C`) — i.e. all four button-state frames are forced to one image index,
which is what makes the *server* the sole owner of the highlight.

---

## 4. Anomaly worth flagging: btnIndex base mismatch

* **Send** (`0x010C3A30`): `mov esi, 1` at `0x010C3ABB`, then
  `sendEplpMouseEvent(esi, …)` for `widget[0]`, `widget[1]` → emits
  **btnIndex 1 and 2**.
* **Receive** (`0x010C3E00`): loop counter starts at `0` and compares
  `i == btnIndex` over `i ∈ {0,1}` → only **btnIndex 0 and 1** ever light a widget.
* The receive-side *action* switch handles **btnIndex 0, 1, 2**.

The sibling `CNRDBossTowerReward` does not have this: at `0x010CA81E` it is
`xor esi, esi` (0-based) over 4 widgets, `cmp esi, 4`, and its table
`0x035D9288`–`0x035D92C8` is 4 rows × 4 — fully symmetric.

So in the Advance Altar copy, the send is 1-based and the highlight loop is
0-based. A server that simply echoes the received `btnIndex` will make the
altar panel highlight the *wrong* widget for btn 1 and *no* widget for btn 2,
while still committing the correct action. I am reporting this as observed
disassembly, not as a claim about intended design — but the BossTower
comparison makes a client-side off-by-one the most likely explanation.

---

## 5. Public surface (called from outside this unit)

| VA | signature | external caller | what it does |
| --- | --- | --- | --- |
| `0x010C3040`* | `set(int,int)` | `0x01150D8D` (`NOTI ADVANCE_ALTAR_CLEAR_STAGE`) | `+0x98`,`+0x9C` |
| `0x010C3140` | `void reset()` | `0x01105DA6` (`CMDFUNC_ENUM_CMDPACKET_GIVEUP_GAME`, `<Exit Dungeon - Give Up>`) | `+0x259C=4`, `+0x25A0=-1`, `+0x25A4=-1` |
| `0x010C31C0` | `void set(u8)` | `0x01150DA3` (`NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_CLEAR_STAGE`) | `+0x90` |
| `0x010C31D0` | `int get()` | `0x00E10A65` (`CNAdvanceAltarModule.cpp`) | `+0x259C` |
| `0x010C31E0` | `void set(int)` | `0x00E0F40B` (`CNAdvanceAltarModule.cpp`) | `+0x259C` |
| `0x010C31F0` | `int get()` | `0x00E10B1B` (`CNAdvanceAltarModule.cpp`) | `+0x25A4` (difficulty) |
| `0x010C3200` | `void setMode(int)` | `0x01148D6F` (`NOTI_Dungeon`, val 9), `0x01163B5A` (val 9), `0x01163B82` (val 5) — `NOTI_Etc` near `PUNISH_END_TIME` | `+0x25B4` |
| `0x010C3210` | `int getMode()` | `0x01163B41/4C/68/74` (`GameCoreNetwork_NOTI_Etc.cpp`) | `+0x25B4` |
| `0x010C3220` | `void executePending()` | `0x02709B7A` (`CNTownModule::onEnterModule`, guarded `type==0xE`) | see §2 |
| `0x010C3340` | `void syncVisibility()` | `0x0231BD60`, `0x0231BD6B` (`CNRDInterfaceManager::proc`) | if `this->[4]==8`, show/hide `+0x28` and `+0x30` according to `ifMgr->modeSlot[1] == 0xE` (`0x02300E00` = `[ecx + idx*4 + 0xA0]`) |
| `0x010C3B90` | `void drawMask(bool)` | `0x0199D25C` (`CNRDStage.cpp`, TU 29) | see §6 |
| `0x010C3E00` | `void setEplpButtonState(int btnIndex, int event)` | `0x01106D8B` (`GameCoreNetwork_CMD_Dungeon.cpp`, packet `0x48`) | see §1 |

\* `0x010C3040` sits 0x20 bytes *below* the briefing's low anchor but is clearly
part of this class (same `this` from `ifMgr+0x8F8C`).

Internal-only: `0x010C3060` (called twice from `0x010C3A30`), `0x010C3390` and
`0x010C3A30` (both called from `0x010C5C40`, same class, above the span).

---

## 6. Non-protocol members, briefly

* `0x010C3390` (1696 B) — pure draw. Blits stage-grid / result rows via
  `0x27D7F30` (set colour+scale) and `0x27C7D50` (blit at x,y) on the atlas
  `[this+0x134]`, iterating the two 12-byte-element vectors at `+0xA8` and
  `+0xBC`. Branches on `+0xA0 == 1` (victory vs defeat layout). No packets.
* `0x010C3B90` (624 B) — the **spotlight/vignette**. Finds the object in the
  current stage whose `vtbl[0x44]()` returns `0xBB9A`, caches it at `+0x25A8`,
  takes its screen x/y (`0x190EEF0` / `0x190EFD0`), then tiles the `MonoLayer`
  sprite at `+0x25AC` in 50×50 (`0x32`) steps over the whole screen
  (`[0x399AE98]` × `[0x399AE9C]`) **except** a 500×500 (`0x1F4`) box around the
  altar. Arg is a bool selecting the z-offset.

---

## 7. Span verdict — the briefing span is TOO NARROW

The anchor-bounded span `0x010C3060`–`0x010C3E00` is a real module boundary in
the sense that everything inside it belongs to this class, but the class does
**not** end there:

* `0x010C3E00` itself runs to `0x010C4070`.
* Field accesses to the same `+0x25xx` block continue to `0x010C7E33`
  (`0x010C4C83`, `0x010C52AC` (ctor), `0x010C535D` (dtor), `0x010C58E1`,
  `0x010C5E07`, `0x010C6744`, `0x010C7E0E`, `0x010C7E2D`).
* `0x010C5C40` holds this window's resource literals — `UI/AdvanceAltar/Result/`,
  `AdvanceAltar_result_victory.act`, `AdvanceAltar_result_defeated.act`,
  `Interface/AdvanceAltar/Result/AdvanceAltar_Result.img`,
  `AdvanceAltar_reward_newrecord.ani`, `REWARD_OPEN1`, `AMBOX_RESULT`,
  `SACRED_SONG` — and it calls `0x010C3390` and `0x010C3A30` directly.
* `0x010C3040` is below the low anchor and is a member of the same class.

Practical extent: roughly **`0x010C3040` – `0x010C86A0`** (the next class,
`UI/GBossTower/Result.xui` at `0x010C86A0`, then TU 12 at `0x010C8EE0`).
The `__FILE__` anchors only appear in the two logging functions, which happen to
sit at the two ends of a much smaller sub-range.

---

## 8. Data-set correction

The task prompt lists the inbound read primitives as
`0x023C5230 / 0x023C52A0 / 0x023C5310 / 0x023C5380 / 0x023C4350`.
Those addresses are wrong for this image — `0x023C5230` lands mid-body in an
unrelated 16 KB factory function starting at `0x023BD910`, and no `E8` call site
in the image targets them.

The real primitives are at **`0x027C____`**, verified by disassembly:

| VA | body |
| --- | --- |
| `0x027C5230` | `read_u8`  — `mov cl,[cursor]; inc cursor; dec remaining@0x3BAAE3C` |
| `0x027C52A0` | `read_u16` — `add cursor,2; sub remaining,2` |
| `0x027C5310` | `read_u32` — `add cursor,4; sub remaining,4` |
| `0x027C5380` | `read_bytes(dst,len)` — memcpy from cursor, then `0x27C51D0` |

Stream cursor = `[0x03BAAE38]`, remaining = `[0x03BAAE3C]`.
This means `parsers.csv` / the `parsers` column of `tu-index.csv` may be keyed on
the wrong addresses; TU 010's "0 parsers" is nevertheless correct — this unit
does no parsing of its own, its inbound fields arrive pre-parsed from
`GameCoreNetwork_CMD_Dungeon.cpp`.

---

## 9. Bonus: full C→S packet-id census (side effect, verified method)

Scanning all 1411 `E8` call sites of `CNGameSocket::MakePacket` (`0x0274D1E0`)
and decoding the two immediately preceding `push`es recovers the packet type for
1148 of them. Relevant here: `0x48` has 22 send sites (this unit at `0x010C30BD`,
`CNRDBossTowerReward` at `0x010C8F3D` / `0x010C8FEF`, and ~19 more, nearly all in
functions carrying the `CLICK_BUTTON2` / `RESULT_ON` literals) — so **`0x48` is
the generic "result-panel button event" packet shared by every EPLP-style result
window**, not an altar-specific one. `0x0F` has 40 send sites.

The full census is reproducible with `xref.py` (written to SCRATCH) + capstone;
say the word and it can be dumped to CSV.
