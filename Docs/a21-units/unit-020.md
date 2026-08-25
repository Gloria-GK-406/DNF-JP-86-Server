# TU 020 — `DNFClient\RDAR\CNRDMap.cpp`

Briefing span `0x00C37250`–`0x00C44C50`, 140 functions, "0 parsers".
Verdict: **the span is too NARROW, and "0 parsers" is correct but misleading — this unit
is an outbound (C→S) sender, not a receiver.**

---

## 1. What this unit implements

`CNRDMap` is the client's **single loaded room** — one map/screen of a dungeon or town.
It is not the world map and not the map list. Three responsibilities, all provable:

1. **A\* / movement-attribute grid** — builds and maintains the byte-per-cell walkability
   grid for the room, re-stamping moving obstacles into it every tick, and hands
   `(grid, W, H)` to the global A\* solver.
2. **Special-summon triggers** — owns the room's monster-spawn trigger table, rolls the
   probabilistic spawn rules, and sends **exactly one C→S packet in the entire client**
   (`msgId 0x0064`, category 2) when a trigger fires.
3. **Map-change (door/portal) presentation** — the `ChangeMapInfo` state block, the
   fade-in/fade-out tint applied to every object, freezing objects and hiding HUD
   windows during the transition.

`CNRDMap` is polymorphic with **two vptrs** (multiple inheritance):
`0xC48860` writes `[this+0] = 0x0319D874` and `[this+8] = 0x0319D824`, both of which are
pointer tables (`0x0319D874[0] = 0x00C497A0`, `0x0319D874[2] = 0x018A7E20`,
`[3] = 0x01764D30`, `[4] = 0x01763CE0` — the `0x0176xxxx` slots live in TU 22
`CNRDObject.cpp`, so the base is in the CNRDObject family). No RTTI name for the class
survives in the image (a `.?AV…` scan finds nothing containing `RDMap`).

---

## 2. Server-relevant #1 — the one packet: `msgId 0x0064`, category 2

### 2.1 The send path (generic, reusable for every other C→S packet in this client)

Singleton: `g = *(void**)0x03A5C9B0`.
Created at `0x016674FD`: `malloc(0x20A604)` → ctor `0x022E8760` → vptr `0x035B9264`.
(The pointer reads 0 in the memory dump — the object had not been created at dump time.)

vtable `0x035B9264`, decoded from the dump:

| slot | VA | meaning (from disassembly) |
|---|---|---|
| +0x08 | `0x022E4BA0` | `beginPacket(u16 msgId, u8 category)` |
| +0x0C | `0x022E52C0` | `setCursor(p)` |
| +0x14 | `0x022E52E0` | `resetCursor()` → `base = this+0x1C` |
| +0x1C | `0x022E5300` | `getCursor()` → `[this+0x10001C]` |
| +0x20 | `0x022E5310` | `writeU8` |
| +0x24 | `0x022E5330` | `writeU16` |
| +0x28 / +0x2C | `0x022E5350` | `writeU32` |
| +0x30 | `0x022E5370` | write 2×u32 |
| +0x34 | `0x022E5390` | `writeRaw(p,len)` — `memcpy`, no prefix |
| +0x38 | `0x022E53C0` | `writeBlock(p,len)` — **`writeU32(len)` then `writeRaw(p,len)`** |
| +0x40 | `0x022E53F0` | `writeCString` — `strlen` then `writeBlock` |

Buffer starts at `obj+0x1C`, write cursor is `obj+0x10001C` (1 MB buffer).

`beginPacket` (`0x022E4BA6`, after a VMProtect stub) writes a **7-byte prologue**:

```
[cursor+0]   = BYTE  category      ; mov byte ptr [eax], dl   (dl = [ebp+0xc])
[cursor+1..2]= WORD  msgId         ; mov word ptr [eax+1], cx (cx = [ebp+8])
[cursor+3..6]= 4 bytes left blank  ; setCursor(cursor + 7)
ret 8                              ; two dword args
```

Flush: `0x022E56F0(a,b)` → `(*(void***)0x03B55D60)[0]->vtbl[0x44](a, -1, b, 3)`.

### 2.2 `CNRDMap::sendSpecialSummonPacket` — `0x00C37250`

Literals owned by this function (decrypted): `CNRDMap::sendSpecialSummonPacket`,
`summon key error : %d`, `----- time %d : key %d -----`.

Signature (thiscall, `ret 0xC`):
`void sendSpecialSummonPacket(int tick /*[ebp+8]*/, SummonSpot* spot /*[ebp+0xC]*/, int subIndex /*[ebp+0x10]*/)`

Send sequence at `0x00C37572`–`0x00C375AB`:

```
g->beginPacket(0x0064, 2);          ; vtbl+0x08
g->writeBlock(&body, 0x28);         ; vtbl+0x38 -> u32 0x28, then 40 bytes
g->writeU32(spot->[0x20]);          ; vtbl+0x2C
sendPacket(1, 0);                   ; 0x022E56F0
```

**A whole-image scan of every `[0x3A5C9B0]->vtbl[8]` call site (250 sites) finds
`msgId 0x64` at exactly one place: `0x00C37572`.** This packet has one sender.

Payload after the 7-byte header: `u32 0x28` + 40 bytes + `u32` = **48 bytes**.

40-byte body, assembled at `[ebp-0x38]` (`0x00C37448`–`0x00C374D3`):

| off | source | note |
|---|---|---|
| +0x00 | `spot->[0x04]` | summon key (also the spot's own index) |
| +0x04 | `spot->[0x00]` | spot type — always 1 on this path (see gate below) |
| +0x08 | `spot->[0x1C]` | |
| +0x0C | X | base `spot->[0x08]`, see adjustment below |
| +0x10 | Y | `spot->[0x0C]` |
| +0x14 | Z | `spot->[0x10]` |
| +0x18 | `spot->[0x18]` | |
| +0x1C | `tick` (arg0) | |
| +0x20 | `spot->[0x2C]` | |
| +0x24 | `spot->[0x28]` | |
| trailing u32 | `spot->[0x20]` | written separately by `writeU32` |

X adjustment (`0x00C37477`–`0x00C374A8`): switch on `spot->[0x24]`
* `2,3,4` → `X = lerp(spot->[0x08], spot->[0x08]+spot->[0x14], tick-spot->[0x2C], spot->[0x30]-tick)` via `0x027C4660`
* `5` → `X = spot->[0x08] + spot->[0x34]`
* otherwise unchanged

Random-position override (`0x00C374AB`–`0x00C37563`): if `spot->[0xAD] != 0` **and**
`this->[0x10B8] != this->[0x10BC]`, X/Y/Z (+0x0C/+0x10/+0x14) are replaced by a
`rand()`-selected 12-byte entry from `this->[0x10B4]` (a `vector<int[3]>`; count derived
by `/12` at `0x00C37503`). If `spot->[0xB4] != spot->[0xB8]` the random index is first
looked up through the spot's own index vector at `spot->[0xB0]`.

**Gates a server must know (all client-side pre-checks — cf. "client refuses before sending"):**
* `subIndex` must be in `[0, spot->[0x5C])` and the sub-trigger bit must not already be set
  (`0x00C372BD`–`0x00C372D8`); on fire it does `*(u32*)elem[8] |= 1 << elem[0xC]`,
  `spot->[0x3C] -= 1`, and clears `spot->[0x8C]` when the counter hits 0.
* `spot->[0x04]` (the key) is bounds-checked against the spot count
  `(this->[0xED8] - this->[0xED4]) / 196`; failure logs `summon key error : %d`
  (`0x00C373DA`, assert line `0x1073`; the earlier trace line is `0x106B`).
* **The packet is emitted only when `spot->[0x00] == 1`.** `0x00C37566`: `type == 5`
  jumps past the send while still consuming the trigger — type 5 spots run
  client-locally and produce no network traffic.

### 2.3 The trigger machinery that produces the packet

`0x00C375E0` — `checkSpecialSummon(int tick, SummonSpot* spot)`, `ret 8`.
Switch on `spot->[0x24]`:
* **1** (`0x00C3774D`): `tick >= spot->[0x2C]` and `spot->[0x8C] != 0` → fire.
* **2** (`0x00C376D2`): threshold `spot->[0x30] - spot->[0x88]*tick`, requires
  `spot->[0x5C] > 0`, `0 <= subIndex < spot->[0x5C]`, and the sub-trigger not already set.
* **5** (`0x00C3762A`): on-screen test — takes the scroll origin from `this->[0x12D4]`
  or `this->[0x11DC]` (chosen by `this->[0xC98]`), then requires
  `edi <= spot->[0x34] <= edi + screenWidth(*(int*)0x0399AEA0)`.

`0x00C38530` — the probabilistic roller, and the other caller of `sendSpecialSummonPacket`:

```
for each 16-byte rule r in this->[0x10A4] .. this->[0x10A8]:      ; stride 16 (sar 4)
    n = min(elapsed / r[0x00], 10)                                 ; 0x00C38608 caps at 10
    repeat n times:
        if (rand() % 10000) <= r[0x04]:                            ; 0x00C3866F idiv 0x2710
            for each spot s in this->[0xED0]:
                if s->[0x04] == r[0x08]:
                    repeat r[0x0C] times:
                        sendSpecialSummonPacket(idx, s, s->[0x40]-s->[0x3C])
```

So **spawn probability is expressed in 1/10000**, the per-rule tick budget is capped at
10 rolls per pass, and a per-rule timer vector of 28-byte records lives at
`this->[0x136C]..[0x1370]` (`0x92492493`/`sar 4` ⇒ stride 28, `0x00C3856E`).

---

## 3. Server-relevant #2 — the A\* / movement attribute grid

### 3.1 Geometry (all constants recovered from magic-division sequences)

* **Cell size: 16 world units in X, 12 in Y.**
  `0x00C417DE`: `x` → `(x + (x>>31 & 0xF)) >> 4` (÷16);
  `0x00C417F9`: `0x2AAAAAAB` + `sar edx,1` ⇒ ÷12. Same pair at `0x00C42B79` / `0x00C42B94`.
* **Grid width `W` = `14 * N`**, where `N = (this->[0x10EC] - this->[0x10E8]) / 4`
  = number of map sections (`0x00C42893`: `eax*8 - eax` then `add esi,esi` ⇒ ×14).
* **Grid height `H` = `H0 + 10 * R`** (`0x00C2F690`), where
  `H0 = (sec0->[0x20] - sec0->[0x1C]) / 20` and `R = ((this->[0x1114]-this->[0x1110])/4) / N`.
* ⇒ **a map section is 14 columns × 10 rows of A\* cells** (the first section row has a
  data-derived height `H0` instead of 10).
* Indexing is `grid[row * W + col]`, one **byte** per cell (`0x00C4293A`, `0x00C42CF4`).

### 3.2 Static attribute source and default

`0x00C358B0` — `getStaticAttr(int x, int y)`:
* cached in a keyed map at `this->[0x1120]` (key = the `(x,y)` pair at `[ebp-0xC]`,
  value at `node+0x14`).
* section column = `x / 14`, local column = `x % 14` (`0x00C35929`, magic `0x92492493`/`sar 3`).
* row `y < H0` ⇒ section `[edi + col*4]`; otherwise `y -= H0`, section row = `y / 10`.
* value = first dword of `section->[0x18][localRow][localCol]` (`0x00C35A47`).
* **Out of range returns 2** (`0x00C359ED: mov eax, 2`) — 2 is the blocked/no-map attribute.

### 3.3 Build / rebuild

`0x00C42840` — `buildAStarAttributeMap(arg)`:
1. frees the two old buffers, recomputes `W` and `H`,
2. `malloc(W*H)` twice → `this->[0x11F4]` (working) and `this->[0x11F8]` (baseline),
3. fills `[0x11F4]` cell by cell from `getStaticAttr(col,row)`,
4. `memcpy(this->[0x11F8], this->[0x11F4], W*H)` (`0x00C42963`) — **`[0x11F8]` is the pristine
   terrain copy**,
5. `this->proc(arg)` (`0x00C41700`),
6. `AStarSolver(0x03A5D2D0)->setMap(&this->[0x11F4], W, H)` via `0x02746060`, which stores
   `grid → +0x10090`, `W → +0x10088`, `H → +0x1008C`.

`0x00C41700` — `CNRDMap::proc(objMgr)` (literal `CNRDMap::proc`). Walks every object in the
manager and stamps the movable ones into the working grid:
* object filter: `obj->vtbl[0x34](0x21)` (type tag **0x21**), `vtbl[0x390]() != 0`,
  `vtbl[0x394]() != 0`, `vtbl[0x2EC]()->[0x18] != 0`.
* rect: columns `vtbl[0x3A4]()/16 - 1` … `vtbl[0x3A8]()/16 + 1`;
  rows `vtbl[0x3AC]()/12 - 1` … `vtbl[0x3B0]()/12`.
* value written: `(BYTE)obj->vtbl[0x2EC]()->[0x18]`.

`0x00C429A0` — `CNRDMap::recalcAStarAttributesByMovableObject(CNRDObject* obj, bool add)`
(own literal). Highest external in-degree of any real method (9 callers).
* `memcpy(this->[0x11F4], this->[0x11F8], W*H)` (`0x00C42C4C`) — restore terrain,
* then replays **every** entry of the per-object rect map `this->[0x11FC]`:
  for each node, `col = node[0x0C]..node[0x14]`, `row = node[0x10]..node[0x18]`,
  `grid[row*W + col] = (BYTE)node[0x1C]`, clipped to `[0,W)` × `[0,H)`.
* The record inserted for `obj` (`0x00C42BFD`) is **`{ int x1, y1, x2, y2; BYTE attr }`**
  (20 bytes), keyed by `obj->vtbl[0x50]()`.
* Pre-conditions before touching the grid (`0x00C42A97`): `this->[0x11F4] != NULL`, and two
  session predicates read off the network singleton (`0x0119B4E0`, `0x0119B440`).

`0x00C40930` — `isObjectInsideMap(CNRDObject*)`, `ret 4`, 3 external callers.
Type filter `0x11` / `0x81` / `0x21` (+ `vtbl[0x5C8]()->[0x2D]`), then compares
`vtbl[0xC4]()` against the map width from `0x00C2F6F0` and `vtbl[0xC8]()` against the
height. Returns bool.

---

## 4. Map-change state block (`this->[0x137C]`)

`0x00C3E190` — `startChangeMap(...)`, the largest anchored function (4272 B).
It `malloc(0x38)`s a **`ChangeMapInfo` of 56 bytes** and copies its stack arguments in
one field at a time (`0x00C3E249`–`0x00C3E2DD`) — the cleanest layout evidence in the unit:

| info off | type | stack arg |
|---|---|---|
| +0x00 | BYTE | `[ebp+0x08]` |
| +0x04 | int  | `[ebp+0x0C]` — **map index; `-1` means "cancel", handled by an early-out at `0x00C3E1C2`** |
| +0x08 | BYTE | `[ebp+0x10]` — "freeze objects / play door FX" (read at `0x00C38217`) |
| +0x09 | BYTE | `[ebp+0x11]` — "run the per-object prepare loop" (`0x00C382D2`) |
| +0x0A | BYTE | `[ebp+0x12]` — forces the object tint to `0xFFFFFFFF` (`0x00C37D12`) |
| +0x0B | BYTE | `[ebp+0x13]` — triggers `0x00C37910(1,0)` (`0x00C38202`) |
| +0x0C | int  | `[ebp+0x14]` — fade duration (denominator at `0x00C37C96`/`0x00C37CB6`) |
| +0x10 | int  | `[ebp+0x18]` — fade step count (`0x00C37CB1`) |
| +0x14 | int  | `[ebp+0x1C]` — tint component |
| +0x18 | int  | `[ebp+0x20]` — tint component |
| +0x1C | int  | `[ebp+0x24]` — tint component |
| +0x20 | int  | `[ebp+0x28]` — tint base (`0x00C37D94`) |
| +0x24.. | | a by-value vector triple arrives in `[ebp+0x30..0x38]` and is freed on the `-1` path |

Then `0x00C35B60(1)` starts the fade and `0x028A84B0(0x04E1F754, mapIndex)` looks the map up.

`0x00C37C50` — `CNRDMap::procChangeMap()` (own literal). Per-frame fade driver: computes a
0..255 alpha from the `[0x1990]` timer against `info->[0x0C]`/`info->[0x10]`, packs an ARGB
into `this->[0x19AC]` (objects) and `this->[0x19B0]` (background), then walks two object
collections applying `0x01765450(-1)` / `0x01766650(-1, color)` and `0x0264ECA0(-1, color)`.

`0x00C381B0` — `CNRDMap::prepareChangeMapObjects()` (own literal), called from
CNRDStage `0x0199FB10`. Guarded by `this->[0x19B5] != 1` and `info->[0x04] != -1`.
Hides UI interfaces **0x198F / 0x1998 / 0x1999** (looked up via `0x01648E70(name, id)`),
then for every object with type tag **0x211**: `vtbl[0x334](1)`, `vtbl[0x258](0)`,
`vtbl[0x9D8]()->[0x1EC] = 1`, `0x017B0BA0()`.

---

## 5. Map lookup / load chain

```
CNRDMap::setMap(mapIndex, a, b)          0x00C496C0
  -> clear()                             0x00C3FCD0
  -> MapScriptTable(0x04E1F754).find(mapIndex)   0x028A84B0   ; std::map<int,T*>, value at node+0x10
  -> if found: setMapData(script, a)     0x00C44C50
```

`0x028A84B0` lives in TU 127 `DNFShared\GameScript\RDARScript.cpp`, so `0x04E1F754` is the
**script-side map table keyed by map index** — the same table a server implementation must
mirror when it tells the client which map to enter.

`0x00C44C50` — `setMapData(MapScript*, x)`. 9904 bytes, and it really is one function
(no `int3` padding anywhere between `0x00C44C50` and `0x00C47300`). It:
* `(*(void**)0x03A5C91C)->load(script, 1)` (`0x027524F0`) → the map resource object,
* `this->[0x11F0] = mapObj`, `this->[0xB10] = mapObj->[0x60]`, `this->[0xB14] = mapObj->[0x190]`,
  and copies 24 bytes from `mapObj->[0x1D0]` to `this->[0xB18]`,
* iterates `mapObj->[0x1E8]`,
* owns the `UI/SelectDungeon/dungeon_title.ani` and `keyFrame != nullptr` literals,
* calls `startChangeMap` (`0x00C3E190`).

`0x00C3FCD0` — `clear()`: zeroes `[0x1134]`, `[0x1138..0x1144]`, `[0x1148]`, `[0x11DC..0x11E8]`,
`[0x11EC]`, `[0x11F0]`, `[0x11F4]`, `[0x11F8]`, `[0x137C]`, `[0x1A84]`, resets the `[0x11FC]`
rect map and the `[0x1234]` timer to a **1000 ms** period (`0x00C3FD61: push 0x3E8`).

---

## 6. Object layout recovered (`CNRDMap`)

| offset | meaning | proof |
|---|---|---|
| +0x000 | vptr #1 = `0x0319D874` | `0x00C4889D` |
| +0x008 | vptr #2 = `0x0319D824` | `0x00C488A3` |
| +0x0B8 | CNRDObjectManager* | `0x00C38221` → `0x0526AB41` (count) / `0x01910540` (at) |
| +0xAD0, +0xAF0, +0x10E4, +0x10F8, +0x110C | embedded containers built in ctor | `0x00C48894`… |
| +0xB10 | = `mapObj->[0x60]` | `0x00C44CA6` |
| +0xB14 | = `mapObj->[0x190]` | `0x00C44CAF` |
| +0xB18 | 24 bytes copied from `mapObj->[0x1D0]` | `0x00C44CC1` loop, 2×3 dwords |
| +0xC98 | selects scroll-origin source | `0x00C37634` |
| +0xED0 | `vector<SummonSpot>` header; +0xED4 `_Myfirst`, +0xED8 `_Mylast`, **elem = 196 B** | `0x00C373C1` (`/196`), `0x00C3869C` |
| +0x10A4 / +0x10A8 | `vector<SummonRule>`, **elem = 16 B** | `0x00C38548` (`sar 4`) |
| +0x10B4 / +0x10B8 / +0x10BC | `vector<int[3]>` alt spawn positions, **elem = 12 B** | `0x00C37503` (`/12`) |
| +0x10E8 / +0x10EC | `vector<MapSection*>`, N sections | `0x00C2F690` |
| +0x1110 / +0x1114 | `vector<dword>`; `count/N` = section rows | `0x00C2F6D3` |
| +0x1120 | `(x,y)` → static-attribute cache | `0x00C358C2`, value at `node+0x14` |
| +0x1134 | current dungeon/room object | `0x00C32270`, `0x00C37FB5` |
| +0x11DC..+0x11E8 | scroll bounds | `0x00C37644` |
| +0x11EC | BYTE "map data unusable" early-out | `0x00C32242` |
| +0x11F0 | loaded map resource | `0x00C44CA0` |
| +0x11F4 | `BYTE* aStarGrid` (working) | `0x00C428F6` |
| +0x11F8 | `BYTE* aStarGridBase` (pristine terrain) | `0x00C42906`, restored at `0x00C42C4C` |
| +0x11FC | map<objKey, `{int x1,y1,x2,y2; BYTE attr}`> | `0x00C42BFD` write, `0x00C42CB9` replay |
| +0x1234 | periodic timer, 1000 ms | `0x00C3FD61` |
| +0x1258 | int, reset to `-1` | `0x00C3FD9F` |
| +0x12D4 | alternate scroll origin | `0x00C3763C` |
| +0x1348 | container supplying the summon sub-index | `0x00C38769` |
| +0x1366 | BYTE flag on the rule-timer path | `0x00C38589` |
| +0x136C / +0x1370 | per-rule timer vector, **elem = 28 B** | `0x00C3856E` (`0x92492493`/`sar 4`) |
| +0x137C | `ChangeMapInfo*` (0x38 B) | `0x00C3E229` `malloc(0x38)` |
| +0x1990 | fade timer | `0x00C37C7D`, `0x00C3E1FE` |
| +0x19AC | ARGB tint applied to objects | `0x00C37D39` |
| +0x19B0 | ARGB tint applied to background | `0x00C37D0C` |
| +0x19B4 | BYTE fade direction (in/out) | `0x00C35BA5` |
| +0x19B5 | BYTE "change already running" | `0x00C381DD` |
| +0x1A84 | BYTE | `0x00C3FD0E` |

`SummonSpot` — 196 (0xC4) bytes:

| offset | meaning | proof |
|---|---|---|
| +0x00 | type; **1 ⇒ packet is sent, 5 ⇒ processed locally, no packet** | `0x00C37566` |
| +0x04 | summon key / own index | `0x00C373BE`, matched against rule at `0x00C3872E` |
| +0x08/+0x0C/+0x10 | X / Y / Z | `0x00C3744B`, `0x00C374B2`, `0x00C374BB` |
| +0x14 | X-interpolation delta | `0x00C3749C` |
| +0x18, +0x1C, +0x20 | packet fields (`+0x20` is the trailing `writeU32`) | `0x00C3759E` |
| +0x24 | trigger mode ∈ {1,2,5} | `0x00C3760F` |
| +0x28, +0x2C | packet fields; `+0x2C` also the start time | `0x00C3774D` |
| +0x30 | end time | `0x00C376D8` |
| +0x34 | trigger X (on-screen test) | `0x00C37689` |
| +0x3C / +0x40 | sub-index range (`count = [0x40]-[0x3C]`) | `0x00C37603` |
| +0x44 | per-sub-trigger fired-state container | `0x00C372C2` (`0x004C0660` = `operator[]`) |
| +0x5C | sub-trigger count | `0x00C372A2` |
| +0x88 | rate / step | `0x00C376D2` |
| +0x8C | BYTE enable, cleared when `[0x3C]` reaches 0 | `0x00C37321` |
| +0xAD | BYTE "use random position" | `0x00C374AB` |
| +0xB0/+0xB4/+0xB8 | index vector for the random position | `0x00C374ED` |

`SummonRule` — 16 bytes, in `this->[0x10A4]`:

| offset | meaning | proof |
|---|---|---|
| +0x00 | period (elapsed/period = number of rolls, capped at 10) | `0x00C38603`, `0x00C3860D` |
| +0x04 | probability threshold, `rand()%10000 <= p` | `0x00C3868E` |
| +0x08 | summon key to match | `0x00C38734` |
| +0x0C | repeat count per hit | `0x00C3875D` |

---

## 7. Public surface — and a warning about the briefing's "68 external callers"

**Most of the briefing's external-caller edges are COMDAT/ICF noise, not API.** The
highest-in-degree entries are folded STL template instantiations that happen to have been
emitted into this object file:

* `0x00C38D90` (10 callers) — `_Destroy_range` over a 24-byte element (`add esi,0x24`),
  takes `(first,last)` in `[ebp+8]/[ebp+0xC]`, **no `this`**.
* `0x00C3A1F0` (10), `0x00C3A240` (6) — same shape, strides `0x2C` and `0x3C`.
* `0x00C38FF0` (8) — single-element destroy.
* `0x00C38800` (5) — `vector::insert` helper, element stride 28.
* `0x00C378B0` (4, incl. `GameCoreNetwork_NOTI_Etc.cpp`) — a two-pointer destructor.
* `0x00C38AB0` (1, from `CNRDUdpModule.cpp`) and `0x00C38DE0` (1, from
  `GameCoreNetwork_CMD_Dungeon.cpp`) — a node ctor and a plain field-by-field copy ctor.
  **Neither is a CNRDMap method; there is no network→CNRDMap hook here.**

The real, load-bearing entry points:

| VA | what | external callers |
|---|---|---|
| `0x00C42840` | `buildAStarAttributeMap` | 7 — `CNRDStage 0x0199E020`, `CNPVPModule 0x027026A0`, `0x026D25C0`, `0x027012D0`, `0x02705960`, `0x0273D870`, `0x0273E9F0` |
| `0x00C429A0` | `recalcAStarAttributesByMovableObject` | 9 — `0x00470690`, `0x00472750`, `0x00AD6500`, `0x00AD6980`, `0x018A93F0`, `0x01919320`, … |
| `0x00C40930` | `isObjectInsideMap` | 3 — `0x00FFBF70`, `0x018A9C20`, `0x01ACD960` |
| `0x00C381B0` | `prepareChangeMapObjects` | 1 — `CNRDStage 0x0199FB10` |
| `0x00C3B5C0` | small helper | 2 — incl. `CNRDDungeon 0x01741320` |
| `0x00C48860` / `0x00C48D40` | constructors (2 overloads) | `0x017467F0`, `0x0273FED0`, `0x00E1A470`, `0x026CF740`, `0x026FE6A0`, `0x027012D0`, `0x027026A0`, `0x0270BA60` |
| `0x00C496C0` | `setMap(mapIndex, …)` | `0x011FDC50`, `0x017467F0` |

---

## 8. Span correction

The briefing's span `0x00C37250`–`0x00C44C50` is a **proper subset** of the translation
unit. `CNRDMap` methods provably live outside it in both directions:

* **Below `0x00C37250`:** `0x00C2F690` (grid height), `0x00C2F6F0`, `0x00C2FC60`, `0x00C30050`,
  `0x00C34430`, `0x00C358B0` (per-cell static attribute), `0x00C35B60` (fade start),
  `0x00C31790`, `0x00C31CD0`, `0x00C32210` — all dereference the same member offsets
  (`+0x10E8/+0x10EC/+0x1110/+0x1114/+0x1120/+0x137C/+0x1990/+0x19AC`) and are called only
  from the anchored functions. The class body starts around **`0x00C2F1A0`**.
* **Above `0x00C44C50`:** `0x00C47300`, `0x00C474E0`, `0x00C47860`, `0x00C48860`, `0x00C48D40`,
  `0x00C49210`, `0x00C496C0`, `0x00C497A0` (slot 0 of the class vtable). The unit runs to
  at least **`0x00C497D0`**.

Two extra decrypted literals sit in the un-anchored part, at `0x00C32210`
(a ~2.9 KB map-move permission check):

* `0x0319D478` — `[TCOMBAT] 상대편 시작맵에는 이동 불가능` ("cannot move onto the opponent's start map")
* `0x0319D4B8` — `[GM맵이동] 개별맵이동 예외처리 강제통과` ("GM map move: per-map exception forced through", emitted through the GM log `0x00A9A9B0` when `0x01763ED0` — the GM test — returns true)

`0x00C32210` takes a `vector<int>` of candidate map indices (`[ebp+0x0C]`, `_First` at
`+0x10`, `_Last` at `+0x14`), rejects when `this->[0x11EC]` is set or `this->[0x1134]` is
null, has a 2000 ms re-notify throttle (`cmp eax, 0x7D0` at `0x00C3237D`, using
`GetTickCount` cached at `0x039AD550`), and special-cases dungeon type `9` and
`0x17`. This is the client's local gate on "may I walk to the next room" — the server
never sees the refused case.

---

## 9. Confidence and open questions

**High confidence:** the send-stream vtable decode; `msgId 0x0064` / category 2 being
unique to `0x00C37572`; the 7-byte header; the length-prefixed 40-byte body and its field
sources; the 16×12 cell size; `W = 14N`; the `+0x11F4`/`+0x11F8` working/baseline pair and
the `memcpy`-then-restamp algorithm; the `ChangeMapInfo` 56-byte layout; the span being too
narrow; the COMDAT-noise diagnosis of the briefing's caller list.

**Unknown / not established:**
* What category `2` selects. The image uses `0`, `1`, and `2` across 250 call sites; I did
  not determine the discriminator, and I did not find a symbol for it.
* What `0x022E56F0(1, 0)` — `vtbl[0x44](1, -1, 0, 3)` on `[0x03B55D60]` — means. The `3` and
  the `-1` are unexplained.
* The exact semantics of body fields `+0x08`, `+0x18`, `+0x20`, `+0x24` and the trailing
  `u32` beyond their source offsets in `SummonSpot`.
* The A\* attribute byte values other than the out-of-range default `2`.
* The `H0 = (sec->[0x20] - sec->[0x1C]) / 20` divisor 20 does not obviously match the
  12-unit cell height; I could not reconcile the two units and did not guess.
* `0x00C44C50`'s later 8 KB (past the header assignment) was not read; the
  `keyFrame != nullptr` assert and `dungeon_title.ani` load live somewhere in it.
