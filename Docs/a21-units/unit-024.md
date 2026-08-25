# Unit 024 — `DNFClient\RDAR\CNRDObjectMoveSystem.cpp`

Anchored span `0x00E470B0`–`0x00E47B90`. Image base `0x00400000`.

> **Address-base note.** The task brief lists the read primitives as `0x023C5230`/`0x023C52A0`/
> `0x023C5310`. In this image (and in `functions.csv`) they are at **`0x027C5230` (read 1 byte),
> `0x027C52A0` (read 2 bytes), `0x027C5310` (read 4 bytes)** — the brief's values are short by
> `0x400000`. Verified by disassembly, not by name:
> `0x027C5238 mov cl, byte [0x3BAAE38]` … `0x027C523E dec dword [0x3BAAE3C]` (1 byte);
> `0x027C52A8 mov cx, word [...]` … `sub dword [0x3BAAE3C], 2` (2 bytes);
> `0x027C5318 mov ecx, dword [...]` … `sub dword [0x3BAAE3C], 4` (4 bytes).
> Stream cursor global = `0x03BAAE38`, remaining-bytes global = `0x03BAAE3C`.

---

## 1. What this unit implements

`CNRDObjectMoveSystem` is the client-side **monster room-transit system** for dungeons: the
subsystem that moves a monster from one dungeon-grid room to another and, on the receiving side,
materialises that monster in the room the player is standing in.

It is a lazily-created singleton (global instance pointer `0x039B1114`) that owns

* a **`std::map<int, CNRDObject*>`** of monsters it is currently tracking, keyed by the 16-bit
  `uId` carried in the packet, and
* a **`std::vector<MoveRec>`** (36-byte records) that acts as a deferred work queue: the network
  handler only parses-and-enqueues; a per-frame `update()` drains the queue and does the actual
  object creation / repositioning.

It owns exactly one packet pair, and the pair is named after this class in the client's own
enum tables:

| dir | id | name (from `noti-names.txt` / `packet-names.txt`) | code |
|---|---|---|---|
| S→C | `0x1E0` (480) | `ENUM_NOTIPACKET_MONSTER_MOVE_SYSTEM` | handler `0x00E47650` |
| C→S | `0x224` (548) | `ENUM_CMDPACKET_MONSTER_MOVE_SYSTEM` | senders `0x00E47980`, `0x00E47810` |

The S→C registration is done in the constructor:

```
00E47574  push ebx            ; ctx = 0
00E47575  push 0xE47650       ; handler = onNotiMonsterMove
00E4757A  push 0x1E0          ; message id
00E4757F  call 0x1102C30      ; getGame()
00E47586  call 0x1637720      ; registerHandler(id, fn, ctx)
```

`0x01637720` → `0x0163C550` (lazy dispatch-table singleton, global `0x03A5BE74`) →
`0x0163C4A0`, which does `table[id].second = new{fn, ctx}` (`0x0163C4BB/0x0163C4BD` store the
two dwords, `0x0163C4CE mov [eax+4], esi`). So `0x1E0` is a genuine dispatch key, not an
internal index. (`dispatch_map.csv` line 92 and `packet_types.csv` line 275 from the earlier
pass agree: site `0x00E47575`, id `0x1E0`, handler `0x00E47650`, map `EVT`.)

---

## 2. Wire formats — the part a server implementer needs

### 2.1 S→C `0x1E0 ENUM_NOTIPACKET_MONSTER_MOVE_SYSTEM` — `onNotiMonsterMove` @ `0x00E47650`

The handler takes **no arguments** and reads from the global inbound stream. Body:

```
u8   count                                  ; 0x00E47682  read1(&count)
repeat count times:                         ; 0x00E476C6 / 0x00E477E8 loop on count
    u16  uId                                ; 0x00E476F8  read2(rec+0x08)
    u32  index        (monster table index) ; 0x00E47701  read4(rec+0x0C)
    u16  level                              ; 0x00E4770A  read2(rec+0x10)
    u32  monsterX                           ; 0x00E47713  read4(rec+0x14)
    u32  monsterY                           ; 0x00E4771C  read4(rec+0x18)
    u32  monsterZ                           ; 0x00E47725  read4(rec+0x1C)
    u8   rate         (client default 100)  ; 0x00E47730  read1(rec+0x20)
    u8   direction                          ; 0x00E4773B  read1(rec+0x21)
```

Fixed size: `1 + count * 22` bytes. There is **no length/terminator** beyond `count`, and the
handler does no bounds or sanity checking whatsoever — every record is unconditionally pushed
onto the queue.

Field naming is the client's own, from the trace at `0x00E4776E` (file line 90):

```
"uId : %d, index : %d, level : %d,\t\t\t\tmonsterX : %d, monsterY : %d, monsterZ : %d, direction : %d "
```

Argument order in the pushes at `0x00E47750`–`0x00E4776D` maps exactly onto the read order
above, so `uId` really is the u16 and `index` really is the u32. Corroborated downstream:
`0x00E47C90/0x00E47C94` feed `[rec+0x0C]` and `[rec+0x10]` into the object factory
`0x02123BC0` as *(index, level)* — and `0x02123BC0` sits directly after the
`AICharacterFactory.cpp` anchor (`0x02118F60`) in the source-file map, i.e. it is the
AI/monster character factory. A second trace at `0x00E4769F` (file line 56) prints
`"count : %d "`.

Post-parse the handler derives one flag before enqueuing:

```
00E47748  cmp al, 5           ; al = direction
00E4774A  setne cl
00E4775C  mov [rec+0x04], ecx ; rec.mode = (direction != 5)
```

**`direction == 5` is the distinguished "absolute reposition" value.** Everything else is a
directional step.

### 2.2 C→S `0x224 ENUM_CMDPACKET_MONSTER_MOVE_SYSTEM`

Both senders build the identical 17-byte body. Send primitives (all writing to the singleton
stream returned by `0x0274CA50` = `mov eax, [0x03B7CEE4]`):

* `0x0274D1E0` — `beginPacket(u16 msgType, u8 flag)`, `ret 8`; emits a 14-byte header and
  resets the byte counter at `[stream+0x2BCC2C]`.
* `0x0274D2E0` — write **1** byte (`add [stream+0x2BCC2C], 1`; `push 1` length to the writer).
* `0x0274D310` — write **2** bytes.
* `0x0274D340` — write **4** bytes.
* `0x0274E6D0` — flush / send.

**`sendCmdMonsterMove(u32 uId, u32 direction)`** @ `0x00E47980`, `__thiscall`, `ret 8`:

```
beginPacket(0x224, 0)                       ; 0x00E47A26
write2( uId          )   [ebp+8]            ; 0x00E47A44
write1( destRoomX    )                      ; 0x00E47A54
write1( destRoomY    )                      ; 0x00E47A64
write4( 0 )                                 ; 0x00E47A72
write4( 0 )                                 ; 0x00E47A80
write4( 0 )                                 ; 0x00E47A8E
write1( entryDirection )                    ; 0x00E47A9B
send()                                      ; 0x00E47AA0
```

**`sendCmdObjectMoveXYZ(u32 uId, u32 roomX, u32 roomY, u32 x, u32 y, u32 z)`** @ `0x00E47810`,
`ret 0x18` — same field order, but the six values come straight from the arguments and the last
field is the constant **5** (`0x00E478A8 push 5`), i.e. the "absolute position" form.

So the on-wire layout of `0x224` is:

```
u16 uId | u8 roomX | u8 roomY | u32 x | u32 y | u32 z | u8 direction
```

Note the asymmetry a server must mirror: the **C→S** packet carries the destination room grid
coordinates but no monster index/level, while the **S→C** packet carries index/level/rate but no
room coordinates (it is implicitly "the room the receiving client is in").

### 2.3 The direction enum and the "opposite" rule (server-visible state machine)

`sendCmdMonsterMove` first walks the current room's grid coordinate, then flips the direction:

```
dir 0 : destRoomX = curRoomX - 1     (0x00E479DF)
dir 1 : destRoomX = curRoomX + 1     (0x00E479E9)
dir 2 : destRoomY = curRoomY - 1     (0x00E479F3)
dir 3 : destRoomY = curRoomY + 1     (0x00E479FF)
dir >= 4 : function aborts, NOTHING is sent  (0x00E479F9 jne 0xE47B7A)
```

and

```
sent direction = opposite(dir):  0<->1, 2<->3
  dir 0 -> 1   (ebx preset 1 at 0x00E47A03)
  dir 1 -> 0   (0x00E47A10 xor ebx,ebx)
  dir 2 -> 3   (0x00E47A19 lea ebx,[edi+1])
  dir 3 -> 2   (0x00E47A23 lea ebx,[edi-1])
```

i.e. the client sends the direction the monster **enters the destination room from**, not the
direction it travelled. `direction` values seen on the wire are therefore `0..3` (room edges),
`5` (absolute XYZ). `4` is never produced by this client; the S→C side treats anything != 5 as a
directional step.

Room grid coordinates are read from the dungeon/map object at
`[map+0xA40]` (X, getter `0x0171C5B0`) and `[map+0xA44]` (Y, getter `0x0171C5C0`); the map object
is `getGame()->0x0119B940()->0x0119B2B0()`, aliased as `0x0163DA50()`.

---

## 3. Object layout

### 3.1 `MoveRec` — the queued move record, **36 bytes (0x24)**

Stride proven twice, independently: the `/36` reciprocal `0x38E38E39` with `sar edx,3` at
`0x00E4744A` and `0x00E47E34`, and the literal `add dword [ebp-4], 0x24` at `0x00E47E69`.

| off | type | written by (parser `0x00E47650`) | read by (`moveObject` `0x00E47B90`) | meaning |
|---|---|---|---|---|
| `+0x00` | u32 | `0` — `0x00E476D8 mov [ebp-0x38], ebx` | `0x00E47C87 cmp dword [esi],0 ; jne bail` | 0 = allowed to spawn an unknown uId; non-0 = update-only |
| `+0x04` | u32 | `(direction != 5)` — `0x00E4775C` | `0x00E47DAD` → `0`: absolute; `1`: step | move mode |
| `+0x08` | u16 | `read2` `0x00E476F8` | map key `0x00E47C30`, `0x00E47CEE`, `0x00E47D8B` | `uId` |
| `+0x0C` | u32 | `read4` `0x00E47701` | factory arg1 `0x00E47C94` | monster table `index` |
| `+0x10` | u16 | `read2` `0x00E4770A` | factory arg2 `0x00E47C90` | `level` |
| `+0x14` | u32 | `read4` `0x00E47713` | `0x00E47CDB`, `0x00E47DBA` | X |
| `+0x18` | u32 | `read4` `0x00E4771C` | `0x00E47CD8`, `0x00E47DB7` | Y |
| `+0x1C` | u32 | `read4` `0x00E47725` | `0x00E47CE5`, `0x00E47DB4` | Z |
| `+0x20` | u8  | `read1` `0x00E47730`; **pre-set to `0x64` (100)** at `0x00E476EF` | `0x00E47CE9 movzx eax, byte [esi+0x20]` → `addMonster` arg3 → virtual `obj->vtbl[0x328](v)` | a percentage-shaped attribute (default 100). **Exact meaning unknown** — it is consumed only through a virtual setter at vtable slot `0x328`. |
| `+0x21` | u8  | `read1` `0x00E4773B`; pre-set to `0` | `0x00E47DCF movzx ecx, byte [esi+0x21]` | direction / mode value; `5` = absolute |
| `+0x22..0x23` | — | — | — | padding to 36 |

The pre-initialisation `mov word ptr [ebp-0x18], 0x64` at `0x00E476EF` writes both `+0x20 = 100`
and `+0x21 = 0` in one store; both are then overwritten from the wire, so the constant only tells
us the field's natural default is 100.

### 3.2 `CNRDObjectMoveSystem` — **40 bytes (`operator new(0x28)`)**

Allocation size `0x28` appears at every inlined `getInstance` site: `0x00E475FC`, `0x00E477AC`,
`0x00E478ED`, `0x00E47AFB`, `0x00E47B42`.

| off | member | evidence |
|---|---|---|
| `+0x00 .. +0x0F` | base **`std::map<int, CNRDObject*>`** | ctor `0x013BEF90` (called with `ecx=this` at `0x00E4752D`) writes `[this+0x00]=_Myproxy` (8-byte alloc), `[this+0x08]=_Myhead` (0x18-byte alloc), `[this+0x0C]=_Mysize=0`; `+0x04` (comparator, empty) never written. Dtor `0x0104B5B0` uses `[this+8]`/`[this+0xC]`. |
| `+0x10 .. +0x13` | **unknown** | never written by the ctor and never referenced by any function I disassembled |
| `+0x14 .. +0x23` | **`std::vector<MoveRec>`** — `_Myproxy +0x14`, `_Myfirst +0x18`, `_Mylast +0x1C`, `_Myend +0x20` | ctor `0x00E47534 lea esi,[edi+0x14]` then `[esi]=proxy`, `[esi+4]=[esi+8]=[esi+0xc]=0`; drain `0x00E47E26 mov edx,[ecx+0x1C]` / `0x00E47E2B mov esi,[ecx+0x18]` / `0x00E47E2F lea edi,[ecx+0x14]`; `push_back` `0x00E4743A` uses `[edi+8]`, `0x00E47441` `[edi+4]`, `0x00E4745B` `[edi+0xC]` |
| `+0x24 .. +0x27` | **unknown** | allocation is `0x28` but the vector ends at `0x24`; nothing observed touches it |

**Red-black tree node layout** (MSVC 8-era, `_Myval` before `_Color`) — recovered from two
independent sites and therefore solid:

| off | field | evidence |
|---|---|---|
| `+0x00` | `_Left` | `0x00E471DC`→`0x005486A0` iterator increment |
| `+0x04` | `_Parent` | head-node self-links at `0x013BEFF4/0x013BEFF9/0x013BEFFF` |
| `+0x08` | `_Right` | `0x0104B5C3 mov ecx,[esi+8]` |
| `+0x0C` | key (`int`) | 8-byte `_Myval` = `pair<const int, CNRDObject*>` |
| `+0x10` | value (`CNRDObject*`) | `0x00E46D92 cmp [ecx+0x10], ebx`; `0x00E47D3A mov edi,[ecx+0x10]`; `0x00E471BF mov ecx,[esi+0x10]` |
| `+0x14` | `_Color` | `0x013BF007 mov [ecx+0x14], 1` |
| `+0x15` | `_Isnil` | `0x013BF00D mov [edx+0x15], 1`; `0x00E46D9B cmp byte [ecx+0x15],0`; `0x0104B5BB` |

**Iterator layout** (`_SECURE_SCL` on): `_Myproxy +0x00`, `_Mynextiter +0x04`, `_Ptr +0x08` —
from `0x00E47C51`(+0), `0x00E47C76`(`[ebp-0x18]` = base `ebp-0x20` + 8), `0x00E47C79 cmp ecx,[eax+8]`.

### 3.3 Foreign fields this unit relies on

| address | expression | meaning |
|---|---|---|
| `0x0171C5B0` | `[map+0xA40]` | current room grid X |
| `0x0171C5C0` | `[map+0xA44]` | current room grid Y |
| `0x017205D0` | `this+0x244/+0x248` room array, `this+0xACC` obfuscated width | `map->getRoom(x, y)` |
| — | `obj->vtbl[0x6C]()` | the object's owning object-manager/room (`0x00E47CA4`, `0x00E47D51`) |
| — | `obj->vtbl[0x4C]()` | the object's key in this map (`0x00E471EE`, `0x00E4726B`) |
| — | `obj->vtbl[0xAC](x,y,z)` / `vtbl[0x11C]` | set position / resolve walkable position (`0x00E46BF1` / `0x00E46BD7`) |
| — | `obj->vtbl[0x228](other)` | notification broadcast on removal (`0x00E471C8`) — meaning unverified |
| — | `obj->vtbl[0x328](rate)` | consumes `MoveRec+0x20` (`0x01724E7D`) |
| — | constant `0x211` | the object-manager key namespace paired with `uId`, used by `find` (`0x00E4712E`) and `register` (`0x00E47DA1`, `0x01724EE4`). A server does not need it, but any reimplementation of the client's tables does. |

---

## 4. Function-by-function

Module code (this file):

| VA | name / role |
|---|---|
| `0x00E470B0` | **`CNRDObjectMoveSystem::removeMonster(roomX, roomY, u16 uId)`** — `ret 0xC`. Traces `"remove object"` (file line **317**, `0x00E470C5 push 0x13D`). `map = 0x0163DA50()`; guarded by `0x0163E3D0()`; `room = map->getRoom(roomX, roomY)` (`0x017205D0` at `0x00E4711C`); `obj = room->find(0x211, uId, 1)` (`0x0190FC20` at `0x00E47135`); then `detach(obj, room)` via `0x00E46DD0` at `0x00E4713E`. |
| `0x00E47410` | destructor — base map dtor `0x0104B5B0` then vector teardown `0x00E472F0` on `this+0x14`. Three callers, all init/shutdown blobs; may be COMDAT-folded with another identically-shaped class. |
| `0x00E47500` | **constructor** — base map ctor, vector init at `+0x14`, then registers handler `0x1E0`. Returns `this`. |
| `0x00E475D0` | **`getInstance()`** — `if (!g_inst) g_inst = (new char[0x28]) ? ctor() : 0; return g_inst;`, global `0x039B1114`. `functions.csv` merges this with the next function; they are separate (`ret` at `0x00E4762E`/`0x00E47644`, `int3` padding to `0x00E4764F`). |
| `0x00E47650` | **`onNotiMonsterMove()`** — the `0x1E0` parser. *Not listed as its own function in `functions.csv`; it is reachable only as the registered callback.* Parses `count` records and `push_back`s each into `inst->queue`. |
| `0x00E47810` | **`sendCmdObjectMoveXYZ(uId, roomX, roomY, x, y, z)`** — `ret 0x18`. Emits `0x224` with `direction = 5`, then `removeMonster(curRoomX, curRoomY, uId)` and `0x00E46C20(uId)`. One caller. |
| `0x00E47980` | **`sendCmdMonsterMove(uId, direction)`** — `ret 8`. Emits `0x224` with the destination room and the reversed direction, traces `"direction %d "` (file line **165**), then `removeMonster(curRoomX, curRoomY, uId)` and `0x00E46C20(uId)`. One caller. |
| `0x00E47B90` | **`moveObject(MoveRec*)`** — `ret 4`. See §5. Traces `"move object"` (file line **194**). |
| `0x00E47150` | map-maintenance hook, `ret 4`, one caller inside the `CNRDObjectManager::removeObject` blob (`0x01919320`). If the object is in the map → erase it by `obj->vtbl[0x4C]()`; if not → iterate the map and call `o->vtbl[0x228](obj)` on every other tracked object. The second branch's purpose is **not verified**. |
| `0x00E47250` | map-maintenance hook, `ret 4`, one caller in `0x0191CB30` (also `CNRDObjectManager.cpp` band). `if (contains(obj)) erase(obj->vtbl[0x4C]())`. |

Out of the anchored span but demonstrably the same class (called `__thiscall` on the singleton
and touching `this+0x14`):

| VA | role |
|---|---|
| `0x00E46BB0` | `setObjectPosition(obj, x, y, z)` — resolves a walkable spot via `obj->vtbl[0x11C](&outX,&outY, x, y, 200, -1, 3)` then `obj->vtbl[0xAC](outX,outY,z)`. `ret 0x10`. |
| `0x00E46C20` | after a monster leaves: reads current room X/Y as **bytes**, then drives the global object at `[0x03A5C9B0]` — `vtbl[0x08](0x52, 1)`, `vtbl[0x20](roomX)`, `vtbl[0x20](roomY)`, `vtbl[0x24](uId)`, `vtbl[0x44](1, -1, 0, 3)`. The global is **null in this dump**, so the receiver class is **unknown**; I will not guess whether `0x52` is a second protocol or a UI/script event. |
| `0x00E46CB0` | `stepObject(obj, direction)` — computes the room-entry position through a VMProtect-virtualised helper (`0x05258858`, `.vmp1`) scaled by `obj->vtbl[0x4C]() * *tileSize`, then calls `0x00E46BB0`. |
| `0x00E46D40` | `mapContains(CNRDObject*)` — linear scan of the map comparing `[node+0x10]`. |
| `0x00E46DD0` | `detach(obj, objectManager)` — wraps `obj` in a smart handle and calls `CNRDObjectManager::removeObject` (`0x01919320`). |
| `0x00E47E20` | **`update()`** — `for (i = 0; i < queue.size(); ++i) moveObject(&queue[i]); queue.clear();`. `ret 0`. Element stride `0x24` visible at `0x00E47E69`. Single caller `0x02715BC0`. |

COMDAT-folded STL, **not** this module's logic (their in-degree comes from unrelated addresses —
`0x01B4BAC0` alone calls `0x00E47430` 19 times):

`0x00E472F0` (`vector<36>::~vector/_Tidy`, 10 callers), `0x00E47380` (capacity growth, `newcap =
cap + cap/2`, `max_size = 0x71C71C7` = `0x7FFFFFFF/36`), `0x00E47430` (`vector<36>::push_back`,
including the MSVC self-aliasing check at `0x00E4743D`/`0x00E47446`), and the below-span helpers
`0x00E46EE0`, `0x00E46FB0`.

---

## 5. `moveObject` — the state machine to mirror

```
moveObject(MoveRec* r):                         ; 0x00E47B90
  trace("move object")                          ; line 194
  map   = getGame()->...->0x0119B2B0()          ; current dungeon map, bail if null
  layer = getGame()->...->0x0119B410()          ; object layer,        bail if null
  it = this->find(r->uId)                       ; 0x00E47C41

  if (it == end()):                             ; --- monster not yet known
      if (r->f00 != 0) return                   ; 0x00E47C87
      obj = AICharacterFactory(r->index, r->level, 0, 1)      ; 0x00E47C9D -> 0x02123BC0
      if (obj->vtbl[0x6C]()) detach(obj, obj->vtbl[0x6C]())   ; 0x00E47CC1
      map->addMonster(obj, r->uId, r->rate, r->x, r->y, r->z,
                      layer, 0, 0, 0)                          ; 0x00E47CF9 -> 0x01724E00
      (*this)[r->uId] = obj                                    ; 0x00E47D0B/0x00E47D10
      return

  else:                                          ; --- monster already tracked
      obj = it->second                           ; 0x00E47D3A  [node+0x10]
      if (layer->0x0190F510(obj)) return          ; already present in this layer -> nothing to do
      if (obj->vtbl[0x6C]()) detach(obj, obj->vtbl[0x6C]())
      layer->register(0, 0x211, r->uId, obj, 0, 0, &handle, 0) ; 0x00E47DA8 -> 0x0191E450
      if (r->mode == 0) setObjectPosition(obj, r->x, r->y, r->z)  ; 0x00E47DC3 -> 0x00E46BB0
      else if (r->mode == 1) stepObject(obj, r->dir)              ; 0x00E47DD7 -> 0x00E46CB0
      if (obj->vtbl[0x19C]()) 0x02672BF0(obj->vtbl[0x19C]())
```

Server-facing consequences:

1. **`0x1E0` doubles as a spawn packet.** A `uId` the client has never seen causes a full
   monster creation from `(index, level)` at `(x, y, z)`. There is no separate "monster appear"
   handshake in this path.
2. **`mode` is derived purely from `direction == 5`.** Sending `direction = 5` means "this is an
   authoritative absolute position, use x/y/z". Sending `0..4` means "step from the room edge",
   and then x/y/z are ignored for an already-tracked monster (they are still used for a first-time
   spawn, since the spawn branch always uses x/y/z).
3. **`f00` (record `+0x00`) is always 0 on the network path**, so from the wire the client can
   always be made to spawn. The non-zero value is only reachable if some other producer enqueues a
   record — I found no such producer that is provably this module's (`push_back` is COMDAT-folded).
4. The client only ever *sends* `0x224` for directions `0..3` (room transitions) or with the
   constant `5` (absolute). Direction `4` is never emitted by this client.

---

## 6. Public surface

`getInstance()` (`0x00E475D0`) is called from 8 distinct external blobs; only the ones with a
resolvable source anchor are listed as identified:

| caller | source | what it reaches |
|---|---|---|
| `0x01919320` | between `IRDMonster.cpp` and `CNRDObjectManager.cpp` anchors; own literals include `CNRDObjectManager::removeObject` | `0x00E47150` — drop a destroyed object from the tracking map |
| `0x0191CB30` | `CNRDObjectManager.cpp` band | `0x00E47250` — same, second entry point |
| `0x022EA720` | **`CNRDUdpModule.cpp`** (literal `CNRDUdpModule::HandlePacket`) | `0x00E470B0 removeMonster` — the peer-to-peer UDP path also drives monster removal |
| `0x0267DFA0` | between `CNRDAction.cpp` and `CNRDActionBehavior.cpp` anchors (unnamed 50 KB blob) | `0x00E47980 sendCmdMonsterMove` **and** `0x00E47810 sendCmdObjectMoveXYZ` — i.e. the monster action/behaviour code is what decides a monster leaves the room |
| `0x02715BC0` | between `CNTownModule.cpp` and `CNSelectDungeonModule.cpp` anchors | `0x00E47E20 update()` — the per-frame queue drain |
| `0x01151CC0` | `GameCoreNetwork_NOTI_Dungeon.cpp` | `getInstance` + `0x00E47410` (dtor) — dungeon enter/leave lifecycle |
| `0x01660170` | `RDCommon.cpp` (`initRDAR`) | `getInstance` + dtor — startup/singleton allocation |
| `0x0199B120` | `CNRDStage.cpp` | `getInstance` + dtor |

Nothing in this unit is reachable from an external caller except through `getInstance()`; there
are no exported free functions.

---

## 7. Assessment of the briefing's span

The span is **anchored correctly but sized wrong in both directions**, and this matters.

* **Too narrow.** The four `__FILE__` traces carry line numbers `56`, `90` (`onNotiMonsterMove`),
  `165` (`sendCmdMonsterMove`), `194` (`moveObject`), `317` (`removeMonster`). The file is at
  least 317 lines and its source order is the reverse of its address order, so the anchors are a
  link-order artifact. `0x00E46BB0`, `0x00E46C20`, `0x00E46CB0`, `0x00E46D40`, `0x00E46DD0`
  (below the span) and `0x00E47E20` (above it) are all invoked `__thiscall` on this singleton and
  operate on `this+0x14` / this class's map — they belong to this file but carry no `__FILE__`
  reference, so the anchor-based bound cut them off. **`update()` at `0x00E47E20` is the most
  important omission**: without it the queue never drains and the unit looks like a parser with
  no consumer.
* **Too inclusive.** Of the 12 functions inside the span, five (`0x00E472F0`, `0x00E47380`,
  `0x00E47430`, and the below-span `0x00E46EE0`/`0x00E46FB0`) are COMDAT-folded
  `std::vector<T,36>` template code shared with at least ten unrelated modules. Describing them
  as CNRDObjectMoveSystem behaviour would be wrong.
* **Function boundaries.** `functions.csv` records `0x00E475D0` as a single 576-byte function.
  It is two: `getInstance()` at `0x00E475D0` (ends `ret` at `0x00E4762E`/`0x00E47644`, then
  `int3` padding) and `onNotiMonsterMove()` at `0x00E47650`. The briefing's "one parser with
  9 read calls and 8 callers" conflates the two — the 8 callers belong to `getInstance`, and the
  parser has *zero* direct callers because it is only ever reached through the dispatch table.

## 8. Explicit unknowns

* `MoveRec+0x20` ("rate", default 100) — consumed only via virtual slot `0x328`; I did not
  resolve the vtable.
* `CNRDObjectMoveSystem+0x10` and `+0x24` — 8 bytes of the 40-byte allocation are never written
  or read by any code I disassembled.
* `0x00E46C20`'s receiver `[0x03A5C9B0]` is null in the dump; the meaning of its `0x52` constant
  is unresolved. It is **not** the `0x0274CA50` game socket stream, so it is not a second
  game-protocol packet from this path.
* The `o->vtbl[0x228](obj)` broadcast in `0x00E47150` — I can state the control flow but not the
  semantics.
