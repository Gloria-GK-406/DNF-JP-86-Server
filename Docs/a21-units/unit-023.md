# TU 23 — `DNFClient\RDAR\CNRDObjectManager.cpp`

Briefing span `0x0191B990`–`0x0191BF20`. Image base `0x00400000`.

## Verdict in one line

This is the **scene object container / per-frame object driver** for a running
map: it owns `objects_` (every live `CNRDObject` on the stage), the 8 draw
layers, `collisionObjects_` and its spatial index, and it is the thing that
calls each object's `proc()` once per frame, runs hit-collision, sweeps dead
objects, pauses the world, and (once per second) emits one client→server
position-correction packet. It parses **nothing** inbound (`parsers = 0`) — its
server relevance is one outbound packet plus a pause state the server has to
mirror.

---

## 1. The briefing's span is WRONG — too narrow, not too wide

The span is bounded by the only two functions that carry the assert macro with
`__FILE__`. The file itself is far bigger:

* `0x0191B990` pushes line `0x1487` = **5255**; `0x0191BF20` pushes line `0xAB2`
  = **2738** (both immediately before the `not valid object pointer` log call).
  A 5255-line source file cannot be the 0x680 bytes the span covers.
* Nine more functions carry decrypted `CNRDObjectManager::...` scope literals,
  and they sit **outside** the span (`literals.csv`, all with prefix
  `CNRDObjectManager::`):

| VA | literal |
| --- | --- |
| `0x01917FF0` | `CNRDObjectManager::checkHitCollision` |
| `0x01919320` | `CNRDObjectManager::removeObject` (+ 8 sub-scopes, see §3) |
| `0x0191AA90` | `CNRDObjectManager::removeObject` |
| `0x0191AE10` | `CNRDObjectManager::removeNotValidObject` |
| `0x0191E450` | `CNRDObjectManager::addObject` |
| `0x01920990` | `CNRDObjectManager::addObject` |

* `0x0190FF10`, `0x01916A10`, `0x0191AC60`, `0x01920D30` use the *same* `this`
  offsets (`+0x0C/+0x10`, `+0xC0/+0xC4`, `+0xD0`, `+0xE4`, `+0x114`) and are
  reached with `mov ecx, esi` from `proc`.

**Real module extent: roughly `0x0190FF10` – `0x01920D30`** (the gap between
TU22 `CNRDObject.cpp`, which ends `0x01770BE0`, and TU24 `CNRDMovementUnit.cpp`,
which starts `0x01921000`). Everything I label "outside span" below is still
this class, and the literals prove the class name.

Also: `0x0191BB84` (listed by the briefing as a function, 76 bytes, 1 caller,
0 callees) is **not a function**. It starts `pop esi` and runs into
`jmp 0x529c1d8` / `call 0x52d3c4c` (`.vmp1`). It is VMProtect'd filler between
`setPauseWorld` and the next real function. Ignore it.

---

## 2. The six in-span functions

### `0x0191B990` — `CNRDObjectManager::setPauseWorld(bool bPause)` — `ret 4`

Literals: `CNRDObjectManager::setPauseWorld`,
`ERROR]CNRDObjectManager::setPauseWorld, not valid object pointer`, file
literal, line 5255. **12 external callers.**

Body:

1. Iterates `objects_` (`lea esi,[ecx+8]`, begin `0xE56100` / end `0x293B470`,
   `operator++` `0x1988070`).
2. Per element, validates the raw pointer against the global object registry at
   `0x039A7614` (lazy-init guard bit at `0x039A764C`, ctor `0x176B740`,
   lookup `0x28003E0` with hash key `p + (p>>3)`); on miss it logs the ERROR
   literal with file+line (`0x0191BA7C`–`0x0191BAB2`).
3. Calls the object's **virtual at vtable `+0x1C8`**, which is
   `CNRDObject::setPause` (`0x0176F110`, proved by its own literal
   `CNRDObject::setPause` + `ERROR]CNRDObject::setPause %d, not valid animation
   pointer`):
   * pausing:   `setPause(3, -1, 0)`  (`0x0191BACB`: push 0, push -1, push 3)
   * unpausing: `setPause(0, -1, 0)`  (`0x0191BAE1`)
4. Then pauses/resumes **three global clock objects** in the same order:
   `0x03A2A024`, `0x03B3DCF8`, `0x03A29690` — `0x27C3CB0` when pausing (sets
   bit `0x4` in the timer's flag word at `+0x0C`, snapshots time into `+0x14`),
   `0x27C3D30` when resuming (clears bit `0x4`). Confirmed by disassembly of
   both helpers.

`CNRDObject::setPause(mode, a2, a3)` (`0x0176F110`) writes:
`[obj+0x1D4] = [obj+0x1D0]` (previous mode), `[obj+0x1D0] = mode`, and when
mode != 0: `[obj+0x1D8] = a2`, `[obj+0x1DC] = now - a3`. It also normalises
`if (mode != 0 && a2 == 0) mode = 0`.

**Pause level 3 = "world pause"** is the only value this unit produces.

Callers (via `peek.py --callers`, named by their own literals):
`halloween::HalloweenDungeon::setPlayPause` (`0x004CFC10`),
`CNScreamTowerModule::setPlayPause` (`0x00C4FCD0`),
`CNDespairTowerModule::setPlayPause` (`0x00E14620`), `CNRDStage.cpp`
(`0x01995A00`, TU29), `CNRDDungeonTool.cpp` (`0x0279F780`, TU124) and three
more tool entry points, plus `0x0063E570/0x0063E5A0`, `0x00E1ABC0/0x00E1AD10`.

### `0x0191BF20` — `CNRDObjectManager::proc()` — `ret 0`

Literals: `CNRDObjectManager::proc`, `@ObjProc`,
`ERROR]CNRDObjectManager::proc, not valid object pointer`, line 2738.
**4 external callers**: `CNRDStage::proc` (`0x019A4EC0`, literal-confirmed),
`CNSelectCharacterModule::procModeSelectCharacter` (`0x026F09D0`, TU36),
`0x00C497D0`, `0x02584CD0`.

Exact frame order (this is the state machine a reimplementation has to keep):

| VA | step |
| --- | --- |
| `0x0191BF4B` | open profiler scope `@ObjProc` (node `0x03AA2768`, lazy-init at `0x03AA27B0`) |
| `0x0191BFD6`–`0x0191C0F9` | **for i in objects_** (`[esi+0x0C] .. [esi+0x10]`, `sar 2`, index re-read every iteration): if `obj->vt[0x74]()` → validate pointer against registry `0x039A7614` (else log ERROR + line 2738) → `obj->vt[0x28]()` |
| `0x0191C0FF` | close `@ObjProc` |
| `0x0191C110` | `this->removeNotValidObject()`  (`0x0191AE10`) |
| `0x0191C115` | `now = [0x03029608]()` (imported tick source, **not** `GetTickCount`; unresolved import) |
| `0x0191C11C` | `if (this->timer@+0xFC.check(now))` (`0x02747A20`) → `this->0x01916A10()` (rebuild the `+0xE4` collision index) |
| `0x0191C134` | `this->checkHitCollision()` (`0x01917FF0`) |
| `0x0191C139`–`0x0191C179` | **for i in collisionObjects_** (`[esi+0xC0] .. [esi+0xC4]`): `obj->vt[0x480]()` |
| `0x0191C17B` | `if (GetTickCount() >= this->[0x114] + 1000)` → `this->0x0190FF10()` (**the 1 Hz packet, §4**), then two calls through global `[0x03A5AD44]` (`0x019462C0`, `0x052F19AF` in `.vmp1`) |
| `0x0191C1B6`–`0x0191C1E8` | three lazily-created singletons get a tick: `0xE46170`→`0xE46680`, `0x881450`→`0x87F0D0`, `0x22BD860`→`0x22BC730(this)` |

Note `vt[0x74]` gates `vt[0x28]`: an object whose "valid" bit is false is
skipped this frame and reaped by `removeNotValidObject` on the *next* frame.

### `0x0191BBD0` — remove-all-by-object-type — `ret 8`, args `(type, flag)`

Walks `objects_` (`this+8`) and then the pending list (`this+0xD0`); for each
element compares `obj->vt[0x48]()` against arg1 and, on match, calls
`this->0x01919320(&iter, obj, arg2)` (the unlink core), resuming from the
returned iterator. `vt[0x48]` base impl is `0x01A36490`:
`mov eax,[ecx+0x98]; ret` → the object-type dword at `obj+0x98`.

One caller: `0x01920D30` (same class, town-object refresh) which calls it as
`0x0191BBD0(0x32, 0)` at `0x01920D5B` — i.e. *"drop every object of type 50"*
before repopulating from `[arg+0xA4]`. `0x01920D30`'s own caller is
`0x0270E010` in `CNTownModule.cpp` (TU38).

### `0x0191BD60` — remove-all-of-one-C++-class — `ret 0`

For each element of `objects_` it runs `__RTDynamicCast(obj, 0,
SrcType=0x0395E110, TargetType=0x03985274, 0)` (`0x2C6386A`, 5 args, cdecl,
`add esp,0x14`) and destroys the ones that cast (`this->0x0191AC60(&iter, obj)`).

* `0x0395E110` is referenced as *SrcType* by **341** dynamic_casts image-wide —
  it is the common base (the `CNRDObject` type descriptor).
* `0x03985274` is referenced only 5 times. Its complete-object-locators are
  `0x03758240` / `0x0375829C` → vftables `0x03561AEC` / `0x03561A9C`, and the
  only code that writes those vftables is `0x022B92F0`, whose decrypted literal
  is `CNRDAssaultIcon::procAppend`. The other in-image dynamic_cast to this
  type is at `0x0270D795` (CNTownModule region).
* RTTI **names are stripped** in this build (type descriptor name field is
  `"." + 0x04` / `"." + 0xC2 0x0B`, not `.?AV...`), so the class name comes from
  the constructor's literal, not from RTTI.

Single caller: `CNTownModule::setCurrentTownArea` (`0x0270FF60`, TU38,
literal-confirmed) — changing town area wipes these objects.

### `0x0191BE50` — remove-all-matching-a-flag-mask — `ret 4`, arg `(mask)`

For each element of `objects_`: `if (obj->vt[0x34](mask))` →
`this->0x0191AC60(&iter, obj)`. `vt[0x34]` base impl is `0x017621F0`:

```
mov eax,[ebp+8]          ; mask
mov ecx,[ecx+0x94]       ; obj flag word
and ecx,eax
cmp ecx,eax
sete al                  ; (flags & mask) == mask
```

Single caller: `0x00C44480` in `CNRDMap.cpp` (TU20).

### `0x0191BB84` — not a function (VMProtect filler). See §1.

---

## 3. Object layout recovered

### `CNRDObjectManager` (`this`)

STL containers are the `_SECURE_SCL` checked layout: `{_Myproxy, _Myfirst,
_Mylast, _Myend}`; iterators are `{_Myproxy, _Mynextiter, _Ptr}` (12 bytes).
The internal names come from the decrypted profiler scopes inside
`removeObject` (`0x01919320`).

| offset | member | proof |
| --- | --- | --- |
| `+0x08` | `objects_` — `vector<CNRDObject*>` (`_Myfirst +0x0C`, `_Mylast +0x10`, `_Myend +0x14`) | scope literal `CNRDObjectManager::removeObject#objects_.erase` at `0x0191A05B`, then the linear scan over `[+0x0C,+0x10)` at `0x0191A12D` and `lea ecx,[esi+8]; call begin` at `0x0191A160`. `proc` reads the same pair at `0x0191BFD6` |
| `+0x1C` | `drawObjects` — **array of 8** `vector<CNRDObject*>`, **stride 0x14** (16-byte vector + 4 spare) | scope literal `#drawObjects.erase` at `0x0191A1E6`; then `0x0191A2A9`: `call obj->vt[0x30]` → `mov eax,[eax+0xC]` (= `obj+0xA4`, the layer) → `cmp eax,8; je skip` → `lea ecx,[eax+eax*4]` (×5) → `lea esi,[edx+ecx*4+0x1C]`, `mov eax,[edx+ecx*4+0x24]`. 8 layers × 0x14 = 0xA0, so the array ends exactly at `0xBC` |
| `+0xBC` | `collisionObjects_` — `vector<CNRDObject*>` (`_Myfirst +0xC0`, `_Mylast +0xC4`) | scope literal `#collisionObjects_.erase` at `0x0191A36B`, then `add esi,0xBC` at `0x0191A4AE` + `std::find` (`0xBA92F0`) + erase (`0x26DF610`). `proc` iterates `[+0xC0,+0xC4)` at `0x0191C139`; `0x0190FF10` reads the same pair |
| `+0xD0` | deferred-removal queue — `vector<CNRDObject*>` | `removeNotValidObject` `lea ecx,[esi+0xD0]` at `0x0191AECF`, destroys each via `0x0191AC60`, then clears it (`0x1BC80E0` at `0x0191AF66`). `0x0191BBD0` also scans it |
| `+0xE4` | spatial/collision index over `collisionObjects_` (node-based; `begin = 0x65F2A0`, not the vector `begin`) | `collisionObjects_` erase also erases here (`add ecx,0xE4` at `0x0191A566`); rebuilt wholesale by `0x01916A10` (`lea ecx,[edi+0xE4]`); walked by `checkHitCollision` (`add esi,0xE4` at `0x019180B6`). Value nodes hold two sub-lists at `+0x00` and `+0x38` |
| `+0xF8` | DWORD pass counter | `inc dword ptr [esi+0xF8]` at `0x019180B0` (first thing `checkHitCollision` does) |
| `+0xFC` | interval-timer object | `lea ecx,[esi+0xFC]; call 0x02747A20` at `0x0191C11C`; the timer struct has flags at `+0x0A`, period at `+0x04`, next-fire at `+0x0C`, counter at `+0x10` |
| `+0x110` | pointer to an owning manager (nullable) | `mov ecx,[edi+0x110]; test; je` at `0x019198E5`, then `0x0198C6E0` |
| `+0x114` | DWORD last 1-Hz tick | `[esi+0x114] + 0x3E8` vs `GetTickCount()` at `0x0191C181`; written `[ebx+0x114] = GetTickCount()` at `0x0190FF21` |
| `+0x18C` | vector, erased from first in `removeObject` | `add ebx,0x18C` at `0x019193A1` |
| `+0x1A0` | vector, erased from second | `add ebx,0x1A0` at `0x01919465` |

### `CNRDObject` (the element type)

| offset | member | proof |
| --- | --- | --- |
| `+0x94` | DWORD flag bitmask | `vt[0x34]` impl `0x017621F0` |
| `+0x98` | DWORD object type (`OBJECTTYPE_*`) | `vt[0x48]` impl `0x01A36490` `mov eax,[ecx+0x98]`; setter `0x01A364A0` writes it and special-cases value `5` by arming a 1000 ms timer at `obj+0x114`. The literal `CNRDObjectManager::removeObject#OBJECTTYPE_VIRTUALCHARACTER` confirms the enum family |
| `+0x98`.. | sub-struct returned by `vt[0x30]` (`lea eax,[ecx+0x98]`, `0x01763A00`); its `+0x0C` (= `obj+0xA4`) is the **draw layer, 0..7, with 8 meaning "not drawn"** | `0x0191A2A9`–`0x0191A2CB` |
| `+0x9C`/`+0xA0` | guarded (obfuscated) value read by `vt[0x4C]` (`0x01765290`) | wrapper is `{header at +0, value at +4}`, decoded by a jump table selected on `addr & 0xF` (`0x0176530C`) |
| `+0x198`/`+0x19C` | guarded bool "is valid/alive", read by `vt[0x74]` (`0x01765430`) | this is the flag `proc` tests and `removeNotValidObject` sweeps on |
| `+0x1D0` | pause mode | `CNRDObject::setPause` `0x0176F163` |
| `+0x1D4` | previous pause mode | `0x0176F15D` |
| `+0x1D8` | pause arg2 (`-1` from `setPauseWorld`) | `0x0176F170` |
| `+0x1DC` | pause start time (`now - arg3`) | `0x0176F17E` |
| `+0x2CC` | int key looked up in the global map `0x03AA234C` during destroy (skipped when `< 0`) | `0x0191ACC8` |
| `+0x3A8C` / `+0x3A90` | last-reported X / Y (only on the large character-sized subclass) | accessors `0x018C99C0` / `0x018C99D0` |

### Virtual slots (base-rated)

I took the 142 vtables in the image that carry `CNRDObject::setPause`
(`0x0176F110`) at `+0x1C8` and counted each slot. Unanimous slots are the
`CNRDObject` base implementations:

| slot | impl | count | meaning |
| --- | --- | --- | --- |
| `+0x28` | `0x0176E130` | 122/142 | per-frame `proc()` |
| `+0x30` | `0x01763A00` | 142/142 | `return this + 0x98` |
| `+0x34` | `0x017621F0` | 142/142 | `hasFlags(mask)` → `(this->[0x94] & mask) == mask` |
| `+0x48` | `0x01A36490` | 142/142 | `getType()` → `this->[0x98]` |
| `+0x4C` | `0x01765290` | 142/142 | guarded getter (`this+0x9C`) |
| `+0x74` | `0x01765430` | 142/142 | `isValid()` → guarded bool (`this+0x198`) |
| `+0xE4` | `0x0048E890` | 139/142 | (unnamed; must return 0 for the 1 Hz report) |
| `+0x1C8` | `0x0176F110` | by construction | `CNRDObject::setPause(mode, a2, a3)` |

`+0x480` and `+0xA30` are **not** uniform (top impl only 17/142 and 5/142) —
they are subclass-specific slots. Do not read `0x01763170` as the meaning of
`vt[0x480]` in `proc`; that was a same-slot coincidence in one vtable.

---

## 4. What a server implementer needs

### 4.1 One outbound packet: opcode `0x22`, emitted at 1 Hz from `proc`

`0x0190FF10` (outside span, same class — writes `this+0x114`, reads
`this+0xC0/0xC4`; reached only via `mov ecx,esi; call` from `proc` at
`0x0191C193`). Fires when `GetTickCount() >= this->[0x114] + 1000`.

```
[0x0190FF21] this->[0x114] = GetTickCount()
[0x0190FF27] stream = *(void**)0x03A5C9B0            ; the global outbound packet stream
[0x0190FF32] stream->vt[0x08](0x22, 2)               ; push 2; push 0x22   -> begin packet
             for (i = 0; i < (collisionObjects_.size()); ++i) {
[0x0190FF7E]     if (!obj->vt[0x34](0x211)) continue;         ; flag mask 0x211 on obj+0x94
[0x0190FFAC]     if (!0x017623A0(obj))     continue;          ; "is owned by my client" (see below)
[0x0190FFD7]     if ( obj->vt[0xE4]()   )  continue;          ; must be 0
[0x0190FFEB]     ox = obj->0x018C99C0();  oy = obj->0x018C99D0();   ; obj+0x3A8C / +0x3A90
[0x0190FFFF]     obj->vt[0xA30](&nx, &ny);
[0x01910014]     if (ox == nx && oy == ny) continue;
[0x0191002A]     stream->vt[0x24]( obj->vt[0x48]() );          ; object type / id  (16-bit writer)
[0x01910046]     stream->vt[0x24]( obj->vt[0x4C]() );          ; second id (guarded field obj+0xA0)
[0x01910064]     stream->vt[0x20]( nx );
[0x01910075]     stream->vt[0x20]( ny );
                 ++count;
             }
[0x0191009E] if (count) { stream->vt[0x24](0xFFFF);           ; terminator
[0x019100AC]              0x022E56F0(1, 0); }                 ; flush/send
```

Supporting evidence for the stream API:

* `0x03A5C9B0` is referenced **3116** times image-wide; users include
  `SyncRequest::sendSyncRequestPacket` (`0x00733850`), `CNEntryDungeonPartyManager`
  (`0x00732AE0`, literal `[ENTRY] 던전 동기화 정보 송신 : 파티원 (%d)`) and
  `MazeMapViewManager::sendPartyMasterEpicQuestInfo` (`0x006C6300`). It is the
  global client→server packet writer.
* `vt[0x08](opcode, n)` convention confirmed on other senders:
  `(0x6E, 1)` at `0x007338B9`, `(0x6F, 0)` at `0x00732B18`, `(0x73, 0)` at
  `0x00732EB2`, `(0x82, 2)` at `0x006C6407`, `(0x4C, 1)` at `0x0073E9BD`.
  The second argument is small (0/1/2) and varies independently of the opcode;
  its meaning is **unknown**.
* `0x022E56F0(a, b)` is the shared flush (`net = *(void**)0x03B55D60;
  net->vt[0x44](a, -1, b, 3)`), used by 201 call sites.
* `0x017623A0` (in `CNRDObject.cpp`, TU22) returns true when
  `this->vt[0x230]()` equals the global at `0x03B3DFB8` (my-character/owner
  pointer), or when a prior owner lookup succeeds — i.e. "this object belongs
  to me".

So: **once a second the client sends opcode `0x22` containing a variable-length
list of `{type, id, x, y}` records for its own collision objects whose position
drifted from the last reported value, terminated by `0xFFFF`.** The concrete
field widths (`vt[0x24]` writes a value that can be `0xFFFF`; `vt[0x20]` writes
the coordinates) are inferred from the sentinel, not measured — treat widths as
medium confidence, the shape and the ordering as high confidence.

Nothing else in this unit touches the network.

### 4.2 Pause state the server may need to mirror

`setPauseWorld(true)` sets every live object to pause mode **3** with an
infinite duration (`a2 = -1`) and freezes three global clocks. `false` restores
mode **0**. The client entry points are `setPlayPause` on the Halloween dungeon,
Scream Tower and Despair Tower modules, plus `CNRDStage` and the dungeon tool —
i.e. this is the "minigame paused" state, not a network stall.

### 4.3 Object lifecycle (relevant if you model object ids)

* `addObject` — `0x0191E450` / `0x01920990` (outside span).
* `removeObject` — `0x01919320`, 5600 bytes, **recursive** (it calls itself at
  `0x01919823` to remove children). Its decrypted sub-scopes give the exact
  teardown order:
  `#applyEquipmentForcedGrade` → `#OBJECTTYPE_VIRTUALCHARACTER` → `#1072` →
  `#onDestroyObject` → `#onDestroyObject2` → `#objects_.erase` →
  `#drawObjects.erase` → `#collisionObjects_.erase` → `#checkClearMap`.
* `0x0191AC60` = unlink + destroy: calls `0x01919320`, then if the object is a
  movement-unit owner (`obj->vt[0x54]()`), unregisters it from the global
  movement-unit manager `0x03AA2854` (`0x019281E0`) and destroys the unit
  (`0x01922910`, `CNRDMovementUnit.cpp` TU21), then either defers the delete
  (`0xAA6B90`/`0xAA3280`) or calls the scalar deleting destructor
  `obj->vt[0](1)` depending on a runtime check `0x13DB470(0x17)/0x13DB490`.
* `removeNotValidObject` (`0x0191AE10`, called from `proc` every frame):
  first drains the `+0xD0` queue through `0x0191AC60` and clears it, then
  sweeps `objects_` and removes anything whose `vt[0x74]()` is false.

---

## 5. Public surface (what is called from outside this unit)

| function | signature | external callers | what it does |
| --- | --- | --- | --- |
| `0x0191B990` | `setPauseWorld(bool)` `ret 4` | 12 — dungeon/tower `setPlayPause`, `CNRDStage.cpp`, `CNRDDungeonTool.cpp` | §2 |
| `0x0191BF20` | `proc()` `ret 0` | 4 — `CNRDStage::proc` (`0x019A4EC0`), `CNSelectCharacterModule::procModeSelectCharacter` (`0x026F09D0`), `0x00C497D0`, `0x02584CD0` | §2 |
| `0x0191BE50` | `removeObjectsWithFlags(mask)` `ret 4` | 1 — `0x00C44480`, `CNRDMap.cpp` TU20 | §2 |
| `0x0191BD60` | `removeObjectsOfClass<CNRDAssaultIcon-ish>()` `ret 0` | 1 — `CNTownModule::setCurrentTownArea` `0x0270FF60` TU38 | §2 |
| `0x0191BBD0` | `removeObjectsOfType(type, flag)` `ret 8` | 1 — `0x01920D30` (same class), ultimately `CNTownModule.cpp` `0x0270E010` | §2 |
| `0x0191BB84` | — | — | VMProtect filler, not code of this unit |

---

## 6. Things I could not resolve

* `[0x03029608]` — the tick source `proc` feeds to the `+0xFC` timer. It is a
  real import (slot holds `0x6BA47860` at dump time) but it is not in
  `wrapper-apis-a21.csv` and not `kernel32!GetTickCount` (that is `0x0302915C`,
  used two instructions later). Likely `winmm!timeGetTime`; **unverified**.
* The meaning of opcode `0x22`'s second `vt[0x08]` argument (`2`), and the
  human name of opcode `0x22`. Not derivable from this image alone.
* Identity of the three per-frame singletons at the tail of `proc`
  (`0xE46170`, `0x881450`, `0x22BD860`) — no literals on them.
* The three global clocks `0x03A2A024`, `0x03B3DCF8`, `0x03A29690` — 16–19
  refs each, no naming literal. All data globals read as 0 in the dump, so
  runtime identification through the pointer values was not possible.
* Meaning of the two vectors at `+0x18C` and `+0x1A0`.
* `+0x1C` bucket stride is 0x14 but a `vector` is 0x10 — the trailing dword per
  bucket is unread in the code paths I disassembled.
