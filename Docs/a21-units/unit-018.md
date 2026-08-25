# Unit 018 — `DNFClient\RDAR\CNRDItemObject.cpp`

**Image**: DNF S4A21 unpacked, base 0x00400000.
**Briefing span**: 0x0174D200 – 0x0174E820 (10 functions).
**Actual span (corrected)**: **0x0174CE20 – 0x0174FA6F** — see "Span is wrong" below.

## What this unit implements

`CNRDItemObject` is the **world entity for an item lying on the dungeon floor** — the thing you
see fly out of a monster, arc, bounce, land, glow, and then get picked up. It is a thin wrapper
around a `CNRDItem*` (the actual item) plus:

1. the **drop-flight state machine** (throw angle / impulse / landing-spot solve / land / settle),
2. the **rarity-tiered drop VFX+SFX table**,
3. **client-side loot protection** (owner key + expiry timer + the refusal message),
4. the mouse hit-test box and the "near me" highlight flag.

Every string in the span is an item-drop asset path, a `CNRDItemObject::` method tag, or a
`[Hell][ItemObject]` trace line, so the file name and the content agree.

---

## 1. Server-implementer-relevant facts

### 1.1 Loot protection (the one thing a server must mirror)

`CNRDItemObject::setOwner(int ownerKey, int durationMs)` — **0x0174D140**, `ret 8`

```
0174D149  cmp  eax, 0xFFFF          ; arg1 == 0xFFFF -> no owner, do nothing
0174D14E  je   0x174D1D5
0174D154  lea  ecx, [esi+0x1160]    ; guarded-int SET of m_owner = arg1
   ... 0x27BBBE0 / 0x27BBCA0 / 0x27BBD50 / 0x27BBE10 / 0x27BBA60 (setter family, dispatched on ptr&0xF)
0174D1B2  add  esi, 0x1144          ; the protection timer
0174D1BC  call 0x27C3450 (1)        ; init
0174D1C3  call 0x27C3410            ; reset/start
0174D1D0  call 0x27C3B60 (arg2, 0)  ; setDuration(durationMs)
```

Only caller: **0x017EB8C0** (the drop-spawn path):

```
017EBBEC  push 0xC ; call 0x13DB470 ; call 0x13DB490   ; feature/option #12 enabled?
017EBBFC  je   skip
017EBBFE  movzx ecx, word ptr [esi+0x14]                ; owner key = u16 from the drop descriptor
017EBC02  push 0x2710                                   ; 10000 ms  <-- HARD-CODED CLIENT SIDE
017EBC07  push ecx
017EBC0A  call 0x174D140                                ; setOwner(ownerKey, 10000)
```

* The **owner key is a u16 character/slot index** — sourced from `[dropDescriptor + 0x14]`.
  The value it is compared against on pickup is `myCharacter->u16@+8` (`0x00CBCEC0`),
  reached via `[0x03A5C980] -> 0x19E3290 -> 0xCBCEC0`.
* The **protection window is 10 000 ms and is a client constant**, not a packet field.
  A server that wants a different window cannot set it through this path.
* `0xFFFF` is the sentinel for "no owner / free-for-all". It is also the value the ctor
  installs (`0x0174DBF6: mov [ebp-0x10], 0xFFFF` then the guarded SET).

`bool CNRDItemObject::canPickUp(int charKey)` — **0x0174D530**, `ret 4`

```
reads guarded m_owner (0x1160) twice via 0x27BC0F0/160/1D0/240/390 (getter family)
  if (owner == 0xFFFF) return true;
  if (owner == charKey) return true;
  // refusal path:
  secs = Timer(this+0x1144).getDuration() / 1000            ; 0x27C36F0, then imul 0x10624DD3 / sar 6
  swprintf_s(buf, 0x400, GetString(0x7DF /*2015*/), secs)    ; 0x28C14C0 then 0x442570
  ChatMgr([0x3A5C9B8])->push(buf, colour 0xFF9BC8E6, channel 0x1B, 0,0,0,0)   ; 0xA9AF50 -> 0x1A60620
  return false;
```

* **String-table id 2015 (0x7DF)** is the "item is protected for N seconds" line; it is formatted
  with `duration/1000`, i.e. **10** with the constant above (it prints the *total* window, not the remaining).
* Called from **0x016ED5E0** in `CNRDItemManager.cpp` — the pickup attempt. If it returns false
  the manager aborts immediately (`0x016ED674: je -> xor al,al; ret`).
* The manager's other refusal (state blocked) uses **string id 0x111AD (69997)**, channel 0x1E.

**Expiry is client-driven.** `procAppend` (0x0174F040), every frame:

```
0174F99F  Timer(this+0x1144).isRunning()   ; 0x27C34D0 (bit0 of [t+0xC])
0174F9B4  Timer(this+0x1144).isOver()      ; 0x27C3780 (elapsed >= duration)
0174F9BD  -> guarded SET m_owner = 0xFFFF  ; local preset to 0xFFFF then setter family
```

So the client unlocks the item on its own after the timer runs out. A server must not assume it
is told; it should enforce the same 10 s window server-side.

### 1.2 Drop-spawn entry point (what the drop packet ends up calling)

**ctor 0x0174EE90**, `ret 0x14` — `CNRDItemObject(CNRDItem* pItem, int* pRandSeed, int horizAngle, int forceScale, CNRDObject* pSource)`

```
0174EEBD  call 0x177E870                       ; base ctor
0174EECD  mov  [esi],   0x032DC624              ; primary vftable
0174EED3  mov  [esi+8], 0x032DC5D4              ; secondary vftable (base at +8)
0174EEDA  Timer(this+0x1144) ctor  (0x27C3A90)
0174EEEE  guarded-int(this+0x1160) ctor (0x27BCAE0) then SET 0
0174EF59  shared_ptr(this+0x1178/+0x117C) = null
0174EF6B  call 0x174DB90                        ; field init (below)
0174EF86  call 0x174EB00 (arg1..arg5)           ; launch
```

**init 0x0174DB90**
```
[this+0x94]    = 0x121        ; <-- object type id for CNRDItemObject (289)
[this+0x1134]  = 0            ; CNRDItem*
[this+0x1138]  = 0 (word)     ; highlight + flag@0x1139
[this+0x113C]  = 0            ; next settle-effect deadline
[this+0x1140]  = 0 (byte)
Timer(this+0x1144).ctor(0)
guarded SET (this+0x1160) = 0xFFFF     ; no owner
[this+0x116C]  = 0 ; [this+0x1130] = 0 ; [this+0x1168] = 0
shared_ptr(this+0x1178) reset
[this+0x1180]  = 0 (byte)
```
`0x121` is corroborated by the caller: `0x017EBBD7  push 0x121` immediately before the
create/register call in the drop handler.

**launch 0x0174EB00**, `ret 0x14`

```
guarded SET [this+0x378] = 1 ; guarded SET [this+0x380] = 1     ; two base-class flags
call 0x174DF90 (pItem)                                          ; setItem
[this+0xB04] = 0x100 (word) ; [this+0xA4] = 1                    ; motion state 1 = airborne
[this+0x113C] = Timer(this+0x74).elapsed() + 1500 + rand()%1000  ; first settle-effect deadline

; ---- throw parameter block, built in the global scratch at 0x03A8FF08 ----
P.z          (+0x08) = 0x3C  (60)          ; set later with setPosition
P.flag       (+0x0C) = 1  (u16)
P.speedA     (+0x12) = forceScale*400/100 = forceScale*4
P.speedB     (+0x16) = same
P.duration   (+0x78) = 0x5DC (1500 ms)
P.horizAngle (+0x7D) = horizAngle          ; or rand()%1000 when there is no source object
P.zImpulse   (+0x81) = 0x41 + rand(seed)%20
P.unk        (+0xA6) = 2  (u16)
```

* **`arg2` is a pointer to an LCG state**, not a value. `0x22D2DD0(pState)` is
  `x = x*0x41C64E6D + 0x3039` (ANSI-C LCG) three times, `pState==0` means the global at
  0x039912B0. When there is no source object the client falls back to a *local* seed
  initialised to 400 (`0x174EC97: mov [ebp+0x18], 0x190`). **If the server wants every
  client to scatter a drop identically, it must supply that seed** — this parameter is the hook.
* Landing spot solve (only when a source object is given):
  `x,y = pSource->getX(),getY()` clamped to `[0, mapW] x [0, mapH]`
  (`0x0C2F6F0`/`0x0C30050` off the map object reached through `0x119B940 -> 0x119B900`), then
  `this->vt[0x11C](&x, &y, x, y, 0x140 /*320*/, -1, 3)`; if that fails, retry with
  `0x1869F` (99999). Result stored to `[this+0x1170]/[0x1174]` and applied via
  `vt[0xAC](x, y, 0x3C)`.
* Finally `0x0174CF50(&P)` installs the throw motion.

**startThrowMotion 0x0174CF50**, `ret 4`
```
[param+0x78] = 0x5DC ; [param+0xA6] = 2 (u16)
obj = operator new(0x15C) ; ctor 0x22E0D90
obj->vt[0xC](param) ; obj->vt[4](0)
0x1922FC0([this+0xA8], obj)   ; attach to the motion controller
0x11D3700([this+0xA8], 1)
```
Also called externally from **0x0117C520** (`GameCoreNetwork_NOTI_Etc.cpp`) — a server
notification can re-throw an already-spawned item object.

### 1.3 Rarity → drop effect table (recovered exactly)

Rank remap `0x02916890(rawRarity)` — a pure 7-entry jump table:

| raw (`itemData+0x158`) | 0 | 1 | 2 | 3 | 4 | 5 | 6 | else |
|---|---|---|---|---|---|---|---|---|
| **rank** | 0 | 1 | 2 | 3 | **6** | **4** | **5** | 7 |

(Consistent with the DNF rarity enum COMMON/UNCOMMON/RARE/UNIQUE/CHRONICLE/LEGENDARY/EPIC where
CHRONICLE is sorted last — that reading is inference; the table itself is the evidence.)

Thresholds, all on the **rank**:

| condition | effect | site |
|---|---|---|
| rank >= 5, at `setItem` | load `Common/CommonEffect/Animation/ItemdropEffect/drop_start_lightbar.ani` into the shared_ptr at `+0x1178`, register with the effect mgr `[0x03A5C9B8]` | 0x0174E3D4 |
| rank == 6 (+ option 3 via `0x4CD610`) | extra call `0x13454B0` on the effect mgr | 0x0174E473 |
| rank >= 5, while airborne | keep the lightbar glued to the item's screen pos | 0x0174F167 |
| first airborne frame, rank >= 5 | sound **`LEGENDARY_EPIC_ITEM_FALL`** | 0x0174F26A |
| first airborne frame, otherwise | sound **`ITEM_DROP`** | 0x0174F279 |
| `itemData+0x250 != 0` (event item) | anim `.../ItemdropEffect/event_dropeffect.ani`, speed `[0x30BDD78]` | 0x0174F3CA |
| item kind == 2 and rank == 3 | `.../drop_floor.ani` (speed `[0x30439E8]`) then `.../Dropafter_loop.ani` looped | 0x0174F48C |
| item kind == 2 and rank >= 5 | remove the lightbar, then `drop_floor.ani` + `Dropafter_loop.ani` looped; if the removal reported false, an extra screen effect `0x164BAE0(1)` gated on `0x1A16B20` | 0x0174F586 |
| rank == 6 (+ option 3) | `epic_beam0.ani` pillar (**0x0174DA00**) + sound `EPIC_LEGENDARY_ITEM_DROP` | 0x0174F712 |
| 3 <= rank < 5 | sound **`UNIQUE_DROP`** | 0x0174F744 / 0x0174F776 |
| rank >= 5 | sound **`EPIC_LEGENDARY_ITEM_DROP`** | 0x0174F767 |
| rank < 3 | the item's own sound, `pItem->vt[0x54](-1,0,0,0)` | 0x0174F79F |

The chosen name is played through `this->vt[0x1B8](name)`.

### 1.4 Gate-area exclusion (drop never lands in a doorway)

`bool CNRDItemObject::isInGateArea(int x, int y)` — **0x0174E820**, `ret 8`
```
if ([this+0x1140] != 0) return false;                      ; already landed
if (m_pItem && m_pItem->getData() && data[0x214] != 0) return false;  ; flagged items exempt
room = 0x198C6E0( 0x119B500( 0x119B940([this+0xB8]) ) );
for (i = 0; i < 4; ++i) {
    g = 0xC2E720(room, i);   if (!g) continue;             ; gate i rect
    r = {g[0], g[4], g[8], g[0xC]};                        ; x, y, w, h
    r.x -= 30; r.y -= 30; r.w += 60; r.h += 60;            ; inflate 30 all round
    if (0x2906ED0(x, y, &r)) return true;
}
return false;
```
Wired in as vtable slot **+0x118** (`0x0174F000`): calls the base test `0x1773770(x,y,z)` and
then vetoes it if `isInGateArea(x,y)` — so the landing-spot solver never picks a doorway.

`CNRDItemObject::setExcludeGateHorizonAngle(int x, int y, int /*unused*/)` — **0x0174D200**, `ret 0xC`
```
g = 0x0174D0A0(x, y, 0x104 /*260*/)      ; index 0..3 of the first gate within 260 units, else 4
if (y >= mapBottom - 92.0f * 600.0f / (float)[0x0399AE9C])   ; [0x399AE9C] currently 600
    g = 3;
if (g >= 4) return;
switch (g) {                              ; jump table at 0x0174D4A0
  case 0: a = rand()%60 -  30; break;     ; [-30, +29]
  case 1: a = rand()%60 + 150; break;     ; [150, 209]
  case 2: a = rand()%60 +  60; break;     ; [ 60, 119]
  case 3: a = rand()%60 + 240; break;     ; [240, 299]
}
P = motionCtl([this+0xA8])->m_cur(+0x7C)->vt[0x10]();
P[+0x81] = 0x41 + rand()%5;               ; vertical impulse
P[+0x7D] = a;                             ; horizontal angle
P[+0x12] = 0x2BC (700);                   ; speed
motion->vt[4](0);                          ; re-apply
log "  버(%d), 호(%d) X0(%d)" @ line 1066
```
`0x0174D0A0` is a private helper of this file (single caller): loops gate 0..3, gets each gate
object with `0xC2E7B0(room, i)`, computes `0x17645C0(gateX, gateY, x, y, 0)` and returns the
first index whose distance `< maxDist`, else 4.

Reached from `setObjectManager` -> `0x0174D4B0` -> `0x0174D200`: when the object is attached to a
room's object manager and it is sitting inside a gate mouth, it re-throws itself away from that gate.

### 1.5 Pickup / removal — the server side of the loop

`CNRDItem* CNRDItemObject::removeItem()` — **0x0174E660**
```
edi = [this+0x1134];  this->setItem(NULL);  return edi;
```
Callers are **packet-notification bodies**, not local code:
* **0x01146000** in `GameCoreNetwork_NOTI_Dungeon.cpp` (literals include `ITEM_PICK`)
* **0x01185710** in `GameCoreNetwork_NOTI_Etc.cpp` (literals include `ITEM_PICK`, `ITEM`,
  `ERROR INFO>> Is Money Item: %s`, `IT'S CNRDMONEY`)

So the client only detaches an item from its floor object **when the server says so**;
`canPickUp` is purely a local pre-filter that avoids sending the request.

---

## 2. Object layout (each offset with the access that proves it)

### `CNRDItemObject`
Primary vftable **0x032DC624**, secondary **0x032DC5D4** installed at `this+8`
(`0x0174EECD/0x0174EED3`, and again in the dtor at `0x0174E5CA/0x0174E5D0`).
RTTI is name-stripped; `rtti-vtables.csv` shows 8 base descriptors, secondary at offset 8.

| offset | type | evidence | meaning |
|---|---|---|---|
| +0x1130 | byte | `0x0174CE73 mov byte[esi+0x1130],1`; re-tested at `0x0174F96C` | "local player is in reach" sticky flag |
| +0x1134 | `CNRDItem*` | `0x0174CEA0 mov eax,[ecx+0x1134]; ret` (23 callers) | the item; written only by `setItem` `0x0174E203` |
| +0x1138 | byte | `0x0174CEB0` setter also calls `vt[0x204](f, 0xFF00F0FF, 0, 1)`; `0x0174CEE0` getter | highlight/outline flag (colour 0xFF00F0FF) |
| +0x1139 | byte | `0x0174CEF0` set / `0x0174CF00` get | unnamed bool |
| +0x113C | int | `0x0174EC22`, `0x0174F2B8`, `0x0174F358` | next settle-effect deadline (ms on timer +0x74) |
| +0x1140 | byte | `0x0174F28F` set 1 on first airborne frame, `0x0174F7DA` cleared on land, read at `0x0174E85D` | "was airborne / drop sound played" |
| +0x1144 | CTimer (28 B) | ctor `0x0174EEC4->0x27C3A90`, dtor `0x0174E62F->0x27C33F0`, `operator new(0x1C)` sibling at `0x0174F858` | loot-protection timer |
| +0x1160 | guarded int | ctor `0x0174EEDF->0x27BCAE0`, dtor `0x0174E61A->0x27BC0E0`, SET/GET families | **loot owner key** (0xFFFF = none) |
| +0x1168 | byte | `0x0174CE80` set / `0x0174CE90` get | unnamed bool |
| +0x116C | `CTimer*` | `operator new(0x1C)` `0x0174F858`, `0x27C3B60(0x3E8,0)` = 1000 ms, freed in `destroy` `0x0174DEA8` | post-landing re-settle timer |
| +0x1170 | int | `0x0174CF10` get / `0x0174CF30` set; written `0x0174F8A4` | landed X |
| +0x1174 | int | `0x0174CF20` get / `0x0174CF40` set; written `0x0174F8B6` | landed Y |
| +0x1178 | `shared_ptr<Effect>` ptr | `0x0174EF59`, `lock xadd` on `+4`/`+8` of the ctrl block | rarity beam / lightbar effect |
| +0x117C | ctrl block ptr | `0x0174E5E3 mov edi,[esi+0x117C]` then refcount dec | (same shared_ptr) |
| +0x1180 | byte | `0x0174D000` set / `0x0174D010` get; cleared with +0x1181 by `mov word[esi+0x1180],0` at `0x0174EE1C` | unnamed bool |
| +0x1181 | byte | read-only gate at `0x0174DFC0`, `0x0174DD2F`, `0x0174E68C`, `0x0174E9EF` | `[Hell]` debug-trace enable. **Never set to 1 anywhere in this unit** |

Base-class fields this unit touches (they belong to `CNRDObject`, not to this file):

| offset | evidence | meaning |
|---|---|---|
| +0x58 | `0x0174F1AB lea ecx,[esi+0x58]; call 0x27C3660` | timer used as the spin phase |
| +0x74 | `0x0174EBDD`, `0x0174F2B0` | drop-lifecycle timer |
| +0x94 | `0x0174DBC3 mov [esi+0x94],0x121` | object type id = **0x121** |
| +0xA4 | `0x0174EBE1`=1, `0x0174F120`/`0x0174F29B` compare 1 / 2 | motion state: 1 airborne, 2 grounded |
| +0xA8 | `0x19216F0` = `mov eax,[ecx+0x7C]` on it | motion controller (`->m_cur->vt[0x10]()` = throw params) |
| +0xB8 | `0x0174D4B3`, `0x0174E84A`, `0x0174F100` | `CNRDObjectManager*` |
| +0x104 | `0x0174F3E9 lea ecx,[esi+0x104]` | animation player used for the landing anims |
| +0x378, +0x380 | guarded SET 1 at `0x0174EB08`/`0x0174EB6C` | two guarded flags set on launch |
| +0xB04 | `0x0174EBD8 mov word[esi+0xB04],0x100` | u16 set on launch |

### `CNRDItem` (the object passed to `setItem`)
| slot / offset | evidence | meaning |
|---|---|---|
| `vt[0x00]` | `0x0174E3B0 cmp eax,2`, `0x0174F478 cmp eax,2` | kind; 2 = the branch that gets the floor/loop anims |
| `vt[0x0C]` | `0x0174DE94 push 1; call` in `destroy` | release/destroy(bool) |
| `vt[0x28]` | used everywhere | `ItemData* getData()` |
| `vt[0x54]` | `0x0174F7B4 (-1,0,0,0)` | default drop sound name |
| `vt[0xE0]` | `0x0174E4D9` | bool predicate |

### `ItemData` (what `vt[0x28]` returns)
| offset | evidence | meaning |
|---|---|---|
| +0x18 | `0x2C4D110(&f, 4, 0x32, 1)` then `mov ebx,[ebx]`, printed as `Idx : %d` | guarded item index |
| +0x13C | `0x0174E2C8 cmp [eax+0x13C],0`; width `0x27C6F70`, height `0x27C6F80`, then `-w/2` -> `0xEE15A0`, `-h/2` -> `0x10B1630` | drop sprite handle; centred on the object |
| +0x144 | `0x0174E521 add eax,0x144` (fallback path) | second handle |
| +0x158 | `push [eax+0x158]; call 0x2916890` at 4 sites | raw rarity/grade |
| +0x214 | `0x0174E895 cmp byte[eax+0x214],0` and `0x0174CE50` | bool: exempts from gate exclusion, enables the near-player flag |
| +0x250 | `0x0174F3BD cmp byte[eax+0x250],0` | bool: "event drop" -> `event_dropeffect.ani` |

### Throw-parameter block (global scratch at **0x03A8FF08**, same layout as `motion->vt[0x10]()`)
| offset | value / evidence |
|---|---|
| +0x00 | int x — `0x0174EDF9` |
| +0x04 | int y — `0x0174EDFF` |
| +0x08 | int z — `0x0174EE05` = 0x3C |
| +0x0C | u16 = 1 — `0x0174EC28` |
| +0x12 | int speed — `0x0174EC44` (`force*4`) / `0x0174D3F1` (=0x2BC) |
| +0x16 | int speed2 — `0x0174EC49` |
| +0x78 | int duration = 0x5DC (1500 ms) — `0x0174EC4E`, `0x0174CF8A` |
| +0x7D | int horizontal angle deg — `0x0174ECC4`, `0x0174D3DA`; read back at `0x0174F1EA` |
| +0x81 | int vertical impulse = 0x41 + rand%N — `0x0174ECCD`, `0x0174D3C0` |
| +0xA6 | u16 = 2 — `0x0174EC58`, `0x0174CF91` |

Spin rule (`procAppend`, `0x0174F1E5`): if `P[+0x7D]` is in `[0x5A, 0x10E]` (90°..270°) the sprite
spins one way, otherwise the sign is flipped (`xorps` with the sign-mask at `0x03046500`);
rate = `elapsed(this+0x58) * [0x0304A878]`, applied via `vt[0x1F8](1, angle)`.

---

## 3. Public surface

### Virtual overrides (primary vftable 0x032DC624)
| slot | VA | what |
|---|---|---|
| +0x000 | 0x0174EFD0 | scalar-deleting destructor (`0x0174E5A0` + `operator delete 0x27C4030`) |
| +0x018 | 0x0174D020 | reset: base `0x17757A0`, outline off (`vt[0x204](0,0xFF00F0FF,0,1)`), clears +0x1138/+0x1140, re-derives motion state from `getZ()`, frees the +0x116C timer |
| +0x068 | 0x0174E9C0 | `setObjectManager(mgr)` — trace @ line 977, base `0x1764C80`, then `0x0174D4B0` (gate re-throw) |
| +0x0D8 | 0x0174D730 | `bool hitTest(int sx, int sy)` — box `[cx-w/2, cx+w/2) x [cy-h, cy+10)`; w/h from the current frame (`0x27C6F30`/`0x27C6F40`), screen pos from `0x190EEF0`/`0x190EFD0` with a camera-rotation correction |
| +0x118 | 0x0174F000 | movable/valid-position test = base `0x1773770` AND NOT `isInGateArea` |
| +0x280 | 0x0174F040 | **`procAppend()`** — the per-frame state machine |

### Non-virtual methods called from outside the unit
| VA | signature | caller |
|---|---|---|
| 0x0174CEA0 | `CNRDItem* getItem()` | 23 callers image-wide |
| 0x0174CE80 / 0x0174CE90 | set/get bool @+0x1168 | 4 / 1 |
| 0x0174CEB0 / 0x0174CEE0 | set/get highlight @+0x1138 | 6 / 3 |
| 0x0174CEF0 / 0x0174CF00 | set/get bool @+0x1139 | 5 / 7 |
| 0x0174CF10..0x0174CF40 | get/set landed X,Y @+0x1170/+0x1174 | 1 each |
| 0x0174CF50 | `startThrowMotion(Param*)` | `0x0117C520` (NOTI_Etc), `0x0174EB00` |
| 0x0174D010 | `bool get_1180()` | `0x016FC920` (CNRDItemManager) |
| 0x0174D140 | `setOwner(u16 key, int ms)` | `0x017EB8C0` |
| 0x0174D530 | `bool canPickUp(u16 key)` | `0x016ED5E0` (CNRDItemManager) |
| 0x0174E660 | `CNRDItem* removeItem()` | `0x01146000` (NOTI_Dungeon), `0x01185710` (NOTI_Etc) |
| 0x0174EE90 | ctor | the drop-spawn path |

### Internal only
`0x0174D200` setExcludeGateHorizonAngle, `0x0174D0A0` nearest-gate, `0x0174D4B0` gate re-throw wrapper,
`0x0174DA00` epic beam, `0x0174DB90` field init, `0x0174DD00` destroy, `0x0174DF90` setItem,
`0x0174E5A0` dtor, `0x0174E820` isInGateArea, `0x0174CE20` near-player flag update, `0x0174EB00` launch.

---

## 4. Trace lines (useful for correlating a live capture)

Format strings, all gated on `[this+0x1181]`, emitted via `0x27BE270(level 6, __FILE__, "Func(line)")`:

| line | function | format |
|---|---|---|
| 251 | `destroy` | `[Hell][DELETE][ItemObject] Uid : %d, addr : %08x ` + ` \t\t[Delete Item]: Idx %d, Addr : %08x ` |
| 329 | `setItem` | `[Hell][ItemObject] Uid : %d, addr : %08x` + `[Old]`/`[New] Idx : %d, addr %08x)` + ` \t(caller:0x%08x) ` |
| 799 | `removeItem` | `[Hell][ItemObject] Uid : %d, addr : %08x` + ` \t[remove Item]: Idx : %d, addr %08x` |
| 977 | `setObjectManager` | `[Hell][ItemObject][setObjectManager] Uid : %d, addr : %08x, oldMgr [%08x] newMgr [%08x]` |
| 1066 | `setExcludeGateHorizonAngle` | `  버(%d), 호(%d) X0(%d)` (the decrypted text is a Korean fragment; likely a CP949 decode artefact of a longer label — treat the text as unreliable, the three `%d` are `P[+0x12]`, `P[+0x7D]`, `P[+0x81]`) |

`Uid` is `this->vt[0x4C]()`. `Idx` is the guarded `ItemData+0x18`.

---

## 5. The briefing's span is wrong (too narrow at both ends)

`tu-index.csv` gives `0x0174D200 – 0x0174E820` / 10 functions. That is the anchor hull, and the
anchor hull is short because **`functions.csv` merged two real functions into one row**:
`0x0174E820` is listed as 736 bytes (0x0174E820–0x0174EB00), but `0x0174E820` ends at
`0x0174E9BB` (`ret 8`) and a *separate* function `setObjectManager` starts at `0x0174E9C0`
(`ret 4`). The `__FILE__`/`setObjectManager` literals attributed to `0x0174E820` actually
belong to `0x0174E9C0`, so the hull's upper bound is one function too low.

The real translation unit is **0x0174CE20 – 0x0174FA6F**:

* **Below 0x0174D200**: `0x0174CE20`, `0x0174CE80`–`0x0174CF4D`, `0x0174CF50`, `0x0174D000`,
  `0x0174D010`, `0x0174D020`, `0x0174D0A0`, `0x0174D140` all read/write
  `this+0x1130/0x1134/0x1138/0x1139/0x1160/0x1168/0x1170/0x1174/0x1180`, i.e. this class's own
  fields. `0x0174D0A0` and `0x0174D020` are reachable only from inside this unit / its vftable.
* **Above 0x0174E820**: `0x0174EB00` (launch), `0x0174EE90` (ctor), `0x0174EFD0` (deleting dtor),
  `0x0174F000` (vtable +0x118), `0x0174F040` (`procAppend`, `CNRDItemObject::procAppend`
  literal at 0x032DD024, vtable +0x280). `procAppend` ends at `0x0174FA49`.
* **Not in it**: `0x0174CBC0` — a free function (no `this`, 7 callers spread across the image,
  touches no `0x11xx` field). Probably `CNRDObject.cpp` or earlier; unresolved.
  `0x0174FA70` onward touches `this+0x2D4/0x320/0x324`, a different class.

Net effect: the briefing describes 10 of the ~24 functions in the file, and misses the
constructor, the launch routine, and `procAppend` — which is where most of the
server-relevant behaviour lives.

## 6. Unknowns

* The bool fields `+0x1139`, `+0x1168`, `+0x1180` have getters/setters but no use inside this
  unit; their meaning is unknown.
* `+0x1181` (`[Hell]` trace enable) is never written in this image region — whatever sets it
  is elsewhere.
* `ItemData+0x214` is a bool with two observed consumers (gate exemption, near-player flag);
  its name is unknown.
* The mapping from raw rarity 0..6 to the DNF rarity names is inference; the remap table itself
  is directly recovered.
* The struct passed to `0x017EB8C0` (the drop descriptor) is only partly read here:
  `+0x00` byte, `+0x04` dword, `+0x0C` u16, `+0x14` u16 (owner), `+0x16` u16 (compared against a
  global guarded value at `0x03B3E190`, and the whole call bails on mismatch). Its full wire
  layout belongs to whichever unit owns `0x017EB8C0` (unanchored gap).
* No inbound-stream read primitives are called anywhere in this unit — it has **0 parsers**,
  consistent with the briefing. It consumes already-parsed data.
