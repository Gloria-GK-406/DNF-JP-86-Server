# GameCoreNetwork_NOTI_Town.cpp - handlers, wire formats, and dispatch

DNF S4A21 client, unpacked from memory, image base `0x00400000`.
Source file (the client's own `__FILE__`, encrypted literal at `0x03249BB0`):
`D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork_NOTI_Town.cpp`

---

## 0. Correction to the brief: the read-primitive VAs

The primitive VAs I was handed are low by exactly `0x00400000`. In this image they are:

| primitive | brief said | actual VA here | body |
|---|---|---|---|
| `read_u8` | 0x023C5230 | **0x027C5230** | `cl=[cursor]; [dst]=cl; cursor+=1; remaining-=1` |
| `read_u16` | 0x023C52A0 | **0x027C52A0** | `cx=[cursor]; cursor+=2; remaining-=2` |
| `read_u32` | 0x023C5310 | **0x027C5310** | `ecx=[cursor]; cursor+=4; remaining-=4` |
| `read_bytes` | 0x023C5380 | **0x027C5380** | `memcpy(dst,cursor,len); cursor+=len` - 2 args `(dst,len)` |
| `set_remaining` | 0x023C4350 | **0x027C4350** | `remaining = arg` |

Evidence: none of `0x023C5230..0x023C5380` is an entry in `functions.csv`; every one of
`0x027C5230..0x027C5380` is. Raw bytes at `0x01189AF8` are `E8 13 B8 63 01`, i.e.
`0x01189AFD + 0x0163B813 = 0x027C5310`. Stream cursor is the global `0x03BAAE38`,
remaining-byte counter `0x03BAAE3C`. Everything below uses the corrected VAs.

---

## 1. THE DISPATCH

**There is no jump table and no switch.** Dispatch is a runtime-built type->handler map,
and `GameCoreNetwork_NOTI_Town.cpp` carries its own registrar function.

| item | value |
|---|---|
| **Registrar (builds the type -> handler mapping)** | **`0x0119A780`** - body `0x0119A780 .. 0x0119B286` (`ret` at `0x0119B286`) |
| Registration primitive | `0x0163C4A0`, thiscall: `Register(this=map, u32 type, void(*fn)(), 0)` |
| Registrations emitted | **156**, all distinct type codes |
| Distinct handler functions | 142 (a few codes share a handler) |
| Called by | `0x0163C4F0`, which calls `0x0115F540` (NOTI_Dungeon registrar) then `0x0119A780` |
| Caller of that | `0x0163A850` |

Emitted shape, repeated 156 times. Example at `0x0119A8C1` (type `0x3C`):

```
0119A8C1  push  0                  ; 4th arg, 0 at all 156 sites
0119A8C3  push  0x1189af0          ; handler function
0119A8C8  push  0x3c               ; ENUM_NOTIPACKET_* type code
0119A8CA  mov   ecx, esi           ; this = the noti-func map
0119A8CC  call  0x163c4a0          ; Register(type, fn, 0)
```

Sibling registrars, found the same way (these also bound this file - see section 2):

| registrar VA | registrations | handler VA range | translation unit |
|---|---|---|---|
| `0x01144AF0` | 269 | - | earlier NOTI file |
| `0x0115F540` | 156 | - | `GameCoreNetwork_NOTI_Dungeon.cpp` |
| `0x011885F0` | 228 | `0x01160240 .. 0x011885xx` | `GameCoreNetwork_NOTI_Etc.cpp` |
| **`0x0119A780`** | **156** | **`0x01189660 .. 0x011996E0`** | **`GameCoreNetwork_NOTI_Town.cpp`** |
| `0x01118180` / `0x0110C2C0` | 85 / 47 | - | `GameCoreNetwork_CMD_Etc.cpp` |

### The type-code names are independently confirmed

There is a decrypted, enum-indexed name table at **`0x3B51F20`** (2047 entries of
`ENUM_NOTIPACKET_*`), built by an initializer doing
`push <enc lit>; call 0x27BACC0; mov [0x3B51F20 + i*4], eax`.

I cross-validated it against the 14 handlers in this file that name *themselves*: each
pushes a decrypted literal `NOTIFUNC_ENUM_NOTIPACKET_<X>` into its own debug-log call.
Taking the **type code purely from the registrar** and the **name purely from the
in-handler literal**, all **14 of 14 agree with `0x3B51F20[code]`**. That is what makes
the names on the remaining 142 rows trustworthy.

| type | handler VA | in-handler literal | `0x3B51F20[type]` | agree |
|---|---|---|---|---|
| `0x006` | `0x0118C420` | NOTIFUNC_ENUM_NOTIPACKET_USER_LEAVE | ENUM_NOTIPACKET_USER_LEAVE | yes |
| `0x016` | `0x0118D150` | NOTIFUNC_ENUM_NOTIPACKET_USER_POSITION | ENUM_NOTIPACKET_USER_POSITION | yes |
| `0x017` | `0x011922A0` | NOTIFUNC_ENUM_NOTIPACKET_USER_AREA | ENUM_NOTIPACKET_USER_AREA | yes |
| `0x018` | `0x01193590` | NOTIFUNC_ENUM_NOTIPACKET_AREA_USERS | ENUM_NOTIPACKET_AREA_USERS | yes |
| `0x035` | `0x0118D3D0` | NOTIFUNC_ENUM_NOTIPACKET_CERA | ENUM_NOTIPACKET_CERA | yes |
| `0x03C` | `0x01189AF0` | NOTIFUNC_ENUM_NOTIPACKET_GUILD_MARK_CHANGE | ENUM_NOTIPACKET_GUILD_MARK_CHANGE | yes |
| `0x07C` | `0x01194220` | NOTIFUNC_ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE | ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE | yes |
| `0x0EE` | `0x0118A460` | NOTIFUNC_ENUM_NOTIPACKET_VILLAGE_MONSTER_LIST | ENUM_NOTIPACKET_VILLAGE_MONSTER_LIST | yes |
| `0x0F0` | `0x0118A610` | NOTIFUNC_ENUM_NOTIPACKET_UPDATE_VILLAGE_MONSTER_LIST | ENUM_NOTIPACKET_UPDATE_VILLAGE_MONSTER_LIST | yes |
| `0x2D4` | `0x011996E0` | NOTIFUNC_ENUM_NOTIPACKET_TERRITORY_GUILD_INFO | ENUM_NOTIPACKET_TERRITORY_GUILD_INFO | yes |
| `0x32D` | `0x01198700` | NOTIFUNC_ENUM_NOTIPACKET_AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM | ENUM_NOTIPACKET_AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM | yes |
| `0x33C` | `0x0118B200` | NOTIFUNC_ENUM_NOTIPACKET_CERASHOP_BONUS_ITEM | ENUM_NOTIPACKET_CERASHOP_BONUS_ITEM | yes |
| `0x353` | `0x0118B340` | NOTIFUNC_ENUM_NOTIPACKET_PERSONAL_INFO | ENUM_NOTIPACKET_PERSONAL_INFO | yes |
| `0x411` | `0x0118BC00` | NOTIFUNC_ENUM_NOTIPACKET_GM_ITEM_SIMULATION | ENUM_NOTIPACKET_GM_ITEM_SIMULATION | yes |

---

## 2. File extent - how I bounded the translation unit

`func-source-file.csv` gives 5 anchors: `0x0118AC60`, `0x0118C3A0`, `0x01191D80`,
`0x01198310`, `0x01198C80`. `functions.csv` spans are **not usable** here: its entry
`0x01187FF0` (span 11376) straddles two translation units and carries **both** the
`NOTI_Etc.cpp` and the `NOTI_Town.cpp` `__FILE__` literal. The handlers are table-only
(nothing `call`s or `jmp`s to them), so the boundary sweep merged them into blobs.

Three independent lines pin the file start at **`0x01189660`**:

1. `__FILE__` crossover: the last reference to the `NOTI_Etc.cpp` literal `0x03246628`
   is at `0x01188215`; the first reference to the `NOTI_Town.cpp` literal `0x03249BB0`
   is at `0x01189B26`.
2. The Etc registrar occupies `0x011885F0 .. 0x01189654` - it ends **six bytes** before
   `0x01189660` - and registers no handler at or above `0x01189660`.
3. The Town registrar `0x0119A780` registers 141 in-file handlers whose minimum VA is
   exactly `0x01189660` and maximum `0x011996E0`.

**TU code range: `[0x01189660, 0x0119B290)`** - handlers `0x01189660..0x011996E0`,
registrar `0x0119A780..0x0119B286`, then shared COMDAT thunks.
The file's literal pool is `[0x03249BB0, 0x0324AB34)`.

---

## 3. Method for the wire formats, and its validation

For each of the 141 in-file handlers I walked the CFG from the registered entry
(following `jcc`, `jmp`, and `jmp [table + idx*4]`; stopping at `ret`; refusing to follow
a tail `jmp` into another known entry) and recorded every call to a read primitive in
address order. Loops come from back edges. Where a `cmp` inside the loop reads the same
`[ebp-X]` slot an earlier read wrote, the loop is labelled `<width>_count x { ... }` -
that is the length-prefixed-array case.

**Validation:** an entirely independent linear `E8` sweep of the TU's bytes finds 527
primitive call sites. Partitioned by handler extent, the linear counts match the CFG
counts for **141 of 141** handlers - zero mismatches. So no handler body was truncated
and none over-reached into a neighbour.

### Notation

- `u8` / `u16` / `u32` - one call to that primitive.
- `bytes[N]` - `read_bytes(dst, N)` with `N` recovered from the adjacent pushed immediate.
- `str` - a call to **`0x0274FCB0`**, verified to be the string reader: it calls
  `0x0274FC10`, which is `read_u32(&len)` followed by `read_bytes(dst, len)` bounded by a
  caller-supplied max. On the wire `str` = **`u32 len` + `len` raw bytes**.
- `<w>_count x { ... }` - loop whose trip count is the preceding read of width `<w>`.
- `N x { ... }` - a loop whose bound I could **not** tie to a read. Do not assume it is
  length-prefixed.
- `sub(0xVA: ...)` - a called deserializer whose own reads I resolved.

### Which sub-calls I inlined, and which I refused

Transitive inlining is where this kind of analysis goes wrong, so the rule is narrow:
a callee contributes to the wire format only if it **directly** calls a read primitive
(or `0x0274FCB0`) inside a body whose extent I can trust.

Verified readers actually reached from this file:

| callee | reads | used by |
|---|---|---|
| `0x0274FCB0` -> `0x0274FC10` | `u32 len` + `bytes[len]` = `str` | 49 call sites |
| `0x01088BC0` | `u8 N x {u8 u8 u8} u8 u8 u8 u8 u8 u8` | `0x055`, `0x061`, `0x0A9` |
| `0x01637A80` | `u8 N x {u32}` | `0x055`, `0x061` |
| `0x012612F0` | `u16` | `0x055`, `0x061` |
| `0x024A9910` | `u32 u8` | `0x139` - the handler is a 3-instruction delegate |
| `0x0112B470` | `u8 u8` (own body; the rest of its blob is other functions) | `0x093` |

Refused, and why - these look like readers to a naive transitive scan and are not:

- **`0x014172D0`** (in-degree 323, 112 bytes, `ret 4`) is a container lookup. A walk that
  runs past its `ret` picks up `u8 u16 u8 u16 u32 x12` from the *next* function. Excluded.
- **`0x00442570`** (reached from 34 handlers) is a `StringCch*`-style copy: `shr ecx,1`,
  `mov eax,0x80070057` (`E_INVALIDARG`). Its two apparent `read_bytes` sites are byte-scan
  artifacts - a CFG walk finds none. Excluded.
- **`0x011F3FF0`, `0x013868F0`, `0x00AA6B90`, `0x01213B40`** and friends are MSVC
  function-local-static guards (`test byte [flag],al / jne / or [flag],eax / call ctor /
  atexit`). The reads live far down the constructor chain, not on the packet path.
  Excluded - this is why `0x07C` and `0x2E2` show no `str`.
- **No in-file helper reads.** I checked every direct call from a handler to a non-handler
  address inside `[0x01189660, 0x0119B290)`: **zero** of them touch a read primitive. So
  for in-file code the direct extraction is complete, not merely a lower bound.

### Caveats - read before building on this

- The sequence is **address order**, which equals wire order for straight-line code.
  Where a handler branches, some listed reads are alternatives on mutually exclusive
  paths, not consecutive fields. Two adjacent `str` entries are usually this.
- `bytes[?]` means the length immediate was not adjacent - treat the length as unknown.
- A read can sit behind a branch. `0x33C` is the clearest case in this file: its
  `bytes[12]` only happens when the leading `u8` is non-zero. I did not annotate this
  per-row; if a format matters, read the handler.
- *(empty)* means the handler executes no read primitive at all: a zero-payload
  notification. `ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE` (`0x07C`) is one, confirmed
  by reading its disassembly end to end.
- 14 codes map to `0x02231720` and one (`0x26D`) to `0x02740AA0`; both are outside this
  file, so their formats are not in scope here.

---

## 4. The 14 self-named handlers (highest confidence - the file names them)

These handlers push their own `NOTIFUNC_ENUM_NOTIPACKET_*` literal, so the name is
ground truth from the binary rather than a table lookup.

| type | name | handler VA | wire format |
|---|---|---|---|
| `0x006` | ENUM_NOTIPACKET_USER_LEAVE | `0x0118C420` | `u16` |
| `0x016` | ENUM_NOTIPACKET_USER_POSITION | `0x0118D150` | `u16 u16 u16 u8 u16` |
| `0x017` | ENUM_NOTIPACKET_USER_AREA | `0x011922A0` | `u16 u8 u8 u16 u16 u8 u8` |
| `0x018` | ENUM_NOTIPACKET_AREA_USERS | `0x01193590` | `u8 u8 u16 N x { u16 u16 u16 u8 u8 }` |
| `0x035` | ENUM_NOTIPACKET_CERA | `0x0118D3D0` | `u8 u32 u32 u32` |
| `0x03C` | ENUM_NOTIPACKET_GUILD_MARK_CHANGE | `0x01189AF0` | `u32` |
| `0x07C` | ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE | `0x01194220` | *(empty - executes no read)* |
| `0x0EE` | ENUM_NOTIPACKET_VILLAGE_MONSTER_LIST | `0x0118A460` | `u8 u8_count x { u16 u32 u8 u16 u16 u16 u16 u16 u16 u8 u16 }` |
| `0x0F0` | ENUM_NOTIPACKET_UPDATE_VILLAGE_MONSTER_LIST | `0x0118A610` | `u8 u16 u16 u16 u8 u16` |
| `0x2D4` | ENUM_NOTIPACKET_TERRITORY_GUILD_INFO | `0x011996E0` | `N x { u8 u8 u32 str u8 }` |
| `0x32D` | ENUM_NOTIPACKET_AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM | `0x01198700` | `bytes[4]` |
| `0x33C` | ENUM_NOTIPACKET_CERASHOP_BONUS_ITEM | `0x0118B200` | `u8 bytes[12]` |
| `0x353` | ENUM_NOTIPACKET_PERSONAL_INFO | `0x0118B340` | `bytes[2]` |
| `0x411` | ENUM_NOTIPACKET_GM_ITEM_SIMULATION | `0x0118BC00` | `u32 u32 u32 u32 u32_count x { u32 u32 u32 }` |

### Notes on these 14

- **`0x0EE` VILLAGE_MONSTER_LIST** - textbook length-prefixed array. `u8 count` into
  `[ebp-1]` at `0x0118A4BB`; `cmp byte ptr [ebp-1],0 / jbe 0x118A5EF` at `0x0118A4C5`;
  loop body `0x0118A4D0..0x0118A5E9` reads 11 fields per element. The handler's own log
  line ` - id : %d\tindex : %d\ttype : %d` prints `[ebp-0x20]` (element field 1, u16),
  `[ebp-0x1c]` (field 2, u32) and `[ebp-0xa]` (field 10, u8), so element =
  `u16 id, u32 index, ... u8 type, u16`.
- **`0x0F0` UPDATE_VILLAGE_MONSTER_LIST** - despite the `..._COUT : %d` log string there
  is **no loop**. It is one fixed record `u8 u16 u16 u16 u8 u16` at
  `0x0118A62F..0x0118A65E`, then the log, then a client-side update.
- **`0x03C` GUILD_MARK_CHANGE** - a single `u32 key` at `0x01189AF8`, then
  `mov ecx,[0x03A5C9AC]` and a guild lookup. Log: ` ENUM_NOTIPACKET_GUILD_MARK_CHANGE [key : %d]`.
- **`0x018` AREA_USERS** - `u8 u8 u16` at `0x01193654 / 0x0119365F / 0x01193668`, then a
  loop `0x01193BF0..0x01193E55` of `{u16 u16 u16 u8 u8}`. The loop bound could not be
  tied to a stack slot by `cmp`, so I am **not** claiming the leading `u16` is the count,
  even though that is the obvious reading. The function really runs to `0x011941AF`;
  `0x011941B0` onward is its jump table, not code.
- **`0x046` GUILD_INFO** (not self-named, but the largest) begins `str` at `0x01195A00`
  (`readString(dst, 0x32)`), then `u8 u8 u16 u8 u32 ...` - a good worked example of the
  `str` notation.
- **`0x33C` CERASHOP_BONUS_ITEM** - `u8 flag` at `0x0118B21A`; `cmp byte [ebp-0x11],0 /
  je 0x118B29A`, so the 12-byte block at `0x0118B22E` (`read_bytes(&[ebp-0x10], 0xC)`) is
  **conditional on that flag**. The log line prints `[ebp-0xc]` as `itemIndex` and
  `[ebp-8]` as `itemNumber`, so `bytes[12]` = three dwords `{u32 ?, u32 itemIndex,
  u32 itemNumber}`.
- **`0x353` PERSONAL_INFO** - `read_bytes(&[ebp-4], 2)` at `0x0118B352`, then
  `movsx edx,al / movsx eax,ah`: two signed bytes, used as a pair.
- **`0x32D` AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM** - `read_bytes(&[ebp-0xd0], 4)` at
  `0x0119873E` into a dword; its log line is ` item index = %d`, so this is a `u32
  itemIndex` fetched via the 4-byte primitive rather than `read_u32`.
- **`0x07C` ENTER_GAMEWORLD_COMPLETE** - zero payload. Its body executes no read
  primitive; the three calls a transitive scan flags (`0x011F3FF0`, `0x01213B40`,
  `0x013868F0`) are all MSVC magic-static guards, checked by disassembly.
- **`0x139` BREAK_AWAY_QUEST_DATA** - the handler at `0x0118B780` is a three-instruction
  delegate: `call 0x24A9910` then a UI call. `0x024A9910` opens with `read_u32` at
  `0x024A991A` and `read_u8` at `0x024A9925`, unconditional, so the packet is `u32 u8`.

---

## 5. Full dispatch table - all 156 registrations

Type code, name from `0x3B51F20`, handler VA, the registration site inside `0x0119A780`,
and the direct wire format. Sorted by type code.

| type | dec | name | handler VA | reg site | wire format |
|---|---|---|---|---|---|
| `0x003` | 3 | ENUM_NOTIPACKET_USER_STATE | `0x01191B30` | `0x0119A781` | `u8 u8_count x { u16 u8 }` |
| `0x004` | 4 | ENUM_NOTIPACKET_STAMINA | `0x01189660` | `0x0119A791` | `u8` |
| `0x006` | 6 | ENUM_NOTIPACKET_USER_LEAVE | `0x0118C420` | `0x0119A7A1` | `u16` |
| `0x007` | 7 | ENUM_NOTIPACKET_REQUEST_PEER | `0x0118C640` | `0x0119A7E1` | `u16 u8 u32 u32 u16 u16 u16 u16 u16 u16` |
| `0x008` | 8 | ENUM_NOTIPACKET_RESPONSE_PEER | `0x0118CA10` | `0x0119A7F1` | `u16 u8 u32 u8 u32` |
| `0x00B` | 11 | ENUM_NOTIPACKET_USER_UDP_IP_PORT | `0x011896E0` | `0x0119A801` | `u8 u8_count x { u16 u32 u32 u16 bytes[4] u8 u32 u8 }` |
| `0x00C` | 12 | ENUM_NOTIPACKET_MESSAGE | `0x01189860` | `0x0119A811` | `u8 u16 u8 str` |
| `0x016` | 22 | ENUM_NOTIPACKET_USER_POSITION | `0x0118D150` | `0x0119A821` | `u16 u16 u16 u8 u16` |
| `0x017` | 23 | ENUM_NOTIPACKET_USER_AREA | `0x011922A0` | `0x0119A831` | `u16 u8 u8 u16 u16 u8 u8` |
| `0x018` | 24 | ENUM_NOTIPACKET_AREA_USERS | `0x01193590` | `0x0119A841` | `u8 u8 u16 N x { u16 u16 u16 u8 u8 }` |
| `0x01B` | 27 | ENUM_NOTIPACKET_ENTER_SELECT_DUNGEON | `0x01190190` | `0x0119A7D1` | `u32 u32 u8 N x { u16 } u8 N x { u16 } u8 N x { u16 u8 u32 u32 u32 } u32 u16 u16 u32 u8 u8 u8_count x { u32 u16 u16 } u8 u32 u32 u8` |
| `0x034` | 52 | ENUM_NOTIPACKET_FRIEND_INFO | `0x02231720` | `0x0119A851` | *handler outside this file* |
| `0x035` | 53 | ENUM_NOTIPACKET_CERA | `0x0118D3D0` | `0x0119A861` | `u8 u32 u32 u32` |
| `0x036` | 54 | ENUM_NOTIPACKET_BUDDY_IN | `0x011899B0` | `0x0119A871` | `u8 u32 str u8` |
| `0x037` | 55 | ENUM_NOTIPACKET_BUDDY_OUT | `0x01189A50` | `0x0119A881` | `u8 u32 str u8` |
| `0x038` | 56 | ENUM_NOTIPACKET_BUDDY_CONN_LIST | `0x02231720` | `0x0119A891` | *handler outside this file* |
| `0x03A` | 58 | ENUM_NOTIPACKET_GUILD_ENTER | `0x0118DE40` | `0x0119A8A1` | `u32 u32 str str` |
| `0x03B` | 59 | ENUM_NOTIPACKET_GUILD_SECEDE | `0x0118DFF0` | `0x0119A8B1` | `u32 u32 u8 str str` |
| `0x03C` | 60 | ENUM_NOTIPACKET_GUILD_MARK_CHANGE | `0x01189AF0` | `0x0119A8C1` | `u32` |
| `0x03D` | 61 | ENUM_NOTIPACKET_GUILD_DISMISS | `0x0118E0F0` | `0x0119A8D1` | *(empty)* |
| `0x03E` | 62 | ENUM_NOTIPACKET_GUILD_CHAT_MSG | `0x02231720` | `0x0119A8E1` | *handler outside this file* |
| `0x03F` | 63 | ENUM_NOTIPACKET_GUILD_CHAR_NAMES | `0x02231720` | `0x0119A8F1` | *handler outside this file* |
| `0x040` | 64 | ENUM_NOTIPACKET_GUILD_MEMBER_LOGIN_OUT | `0x0118E200` | `0x0119A901` | `u8 u8 str` |
| `0x044` | 68 | ENUM_NOTIPACKET_CERA_GROUP_HISTORY | `0x01197400` | `0x0119A911` | `u8 u16 u16_count x { u32 }` |
| `0x045` | 69 | ENUM_NOTIPACKET_SERVEREVENT | `0x01189B60` | `0x0119A921` | `u8 u16 u16` |
| `0x046` | 70 | ENUM_NOTIPACKET_GUILD_INFO | `0x011959B0` | `0x0119A931` | `str u8 u8 u16 u8 u32 u8 str u8 u8 u8 u8 u32 str str u16 str bytes[168] u8 u8_count x { u8 u32 u8 u32 } u32 u8 u8_count x { u32 str u8 } u32 u8 u32 u8 u32 u32 u32 u32 u32 u32 u32 u32` |
| `0x047` | 71 | ENUM_NOTIPACKET_GUILD_CREATE | `0x0118E350` | `0x0119A941` | `u32 str` |
| `0x04E` | 78 | ENUM_NOTIPACKET_CREATE_PRIVATE_STORE | `0x0118B3F0` | `0x0119A7B1` | `u16 u8 u8 u16 u16 u8` |
| `0x04F` | 79 | ENUM_NOTIPACKET_REMOVE_PRIVATE_STORE | `0x0118B4C0` | `0x0119A7C1` | `u16` |
| `0x050` | 80 | ENUM_NOTIPACKET_FROCE_REMOVE_PRIVATE_STORE | `0x0118B540` | `0x0119A951` | *(empty)* |
| `0x051` | 81 | ENUM_NOTIPACKET_COMPLETE_DISPLAY | `0x0118B550` | `0x0119A961` | `u16 str` |
| `0x052` | 82 | ENUM_NOTIPACKET_BUY_PRIVATE_STORE_ITEM | `0x01190FD0` | `0x0119A971` | `u8 u32 u32 u16 u32 str u32 u32 u8 u8` |
| `0x053` | 83 | ENUM_NOTIPACKET_PRIVATE_STORE_ITEM | `0x0118B610` | `0x0119A981` | `u16 u32` |
| `0x054` | 84 | ENUM_NOTIPACKET_PRIVATE_STORE_LIST | `0x0118B650` | `0x0119A991` | `u8 u8_count x { u16 str u8 u8 u8 u16 u16 u8 }` |
| `0x055` | 85 | ENUM_NOTIPACKET_PRIVATE_STORE_INFO | `0x0118E4E0` | `0x0119A9A1` | `u8 u8 N x { u16 u32 u32 u32 u8 u16 u32 u8 u8 u16 u32 sub(0x01637A80: u8 u32) sub(0x012612F0: u16) sub(0x01088BC0: u8 u8 u8 u8 u8 u8 u8 u8 u8 u8) u8 u8 u8 u16 u8 u8 u8 u8 u8 u8 }` |
| `0x061` | 97 | ENUM_NOTIPACKET_MAILBOX_MAIL_LIST | `0x01198E70` | `0x0119A9B1` | `u8 u8 u8_count x { u32 u32 str u32 u32 u8 u32 u16 u8 u32 u8 u8 u16 u32 u8 u32 u32 u32 u32 sub(0x01637A80: u8 u32) sub(0x012612F0: u16) sub(0x01088BC0: u8 u8 u8 u8 u8 u8 u8 u8 u8 u8) u8 u8 u8 u16 u8 u8 u8 u8 u8 u8 u32 u32 u8 } u16 u16 u16_count x { u32 u32 str str u32 u16 u8 }` |
| `0x062` | 98 | ENUM_NOTIPACKET_MAILBOX_REMOVE_MAIL | `0x01189C60` | `0x0119A9C1` | `u32 u32_count x { u32 }` |
| `0x063` | 99 | ENUM_NOTIPACKET_MAILBOX_ALARM | `0x01189CC0` | `0x0119A9D1` | `u16` |
| `0x065` | 101 | ENUM_NOTIPACKET_RENAME_CREATURE | `0x0118E840` | `0x0119A9E1` | `u16 str` |
| `0x068` | 104 | ENUM_NOTIPACKET_RESPONSE_CREATURE | `0x01189D70` | `0x0119A9F1` | `u16` |
| `0x06D` | 109 | ENUM_NOTIPACKET_GUILD_WAR_END | `0x0118EA00` | `0x0119AA01` | `u8` |
| `0x06E` | 110 | ENUM_NOTIPACKET_GUILD_WAR_RESULT | `0x0118EA80` | `0x0119AA11` | `u8` |
| `0x072` | 114 | ENUM_NOTIPACKET_CREATURE_MESSAGE | `0x011928C0` | `0x0119AA21` | *(empty)* |
| `0x075` | 117 | ENUM_NOTIPACKET_GUILD_WAR_START | `0x02231720` | `0x0119AA31` | *handler outside this file* |
| `0x077` | 119 | ENUM_NOTIPACKET_CREATURE_SCRIPT_MESSAGE | `0x01191DD0` | `0x0119AA41` | `u8 u16 u8 str u16 u16_count x { u16 u16 }` |
| `0x07C` | 124 | ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE | `0x01194220` | `0x0119AA51` | *(empty)* |
| `0x07E` | 126 | ENUM_NOTIPACKET_GM_REQUEST_MID | `0x0118EB40` | `0x0119AA61` | `u32 str` |
| `0x07F` | 127 | ENUM_NOTIPACKET_GUILD_MEMBER_INFO | `0x0118EC10` | `0x0119AA71` | `u8` |
| `0x08C` | 140 | ENUM_NOTIPACKET_CHANGE_GUILD_MEMBER_GRADE | `0x01196B10` | `0x0119AA81` | `u8` |
| `0x08D` | 141 | ENUM_NOTIPACKET_GUILD_MESSAGE_TO_GUILD_MEMBER | `0x0118ED10` | `0x0119AA94` | `str` |
| `0x093` | 147 | ENUM_NOTIPACKET_CALL_GUILD_INVITE_TO_INVITED | `0x01196CD0` | `0x0119AAA7` | `str str str u32 u16 u8 u8_count x { u8 } sub(0x0112B470: u8 u8 ...)` |
| `0x094` | 148 | ENUM_NOTIPACKET_REPLY_GUILD_INVITE_TO_CALLER | `0x0118EE40` | `0x0119AABA` | `u8 str` |
| `0x095` | 149 | ENUM_NOTIPACKET_GUILD_MASTER_DELEGATE | `0x0118EF30` | `0x0119AACD` | `str` |
| `0x096` | 150 | ENUM_NOTIPACKET_HAS_BEEN_GUILD_MASTER | `0x01189DE0` | `0x0119AAE0` | *(empty)* |
| `0x097` | 151 | ENUM_NOTIPACKET_GUILD_SECEDE_TO_USER | `0x0118EFB0` | `0x0119AAF3` | *(empty)* |
| `0x099` | 153 | ENUM_NOTIPACKET_PARTY_MEMBER_REALTIME_INFO | `0x01189E10` | `0x0119AB06` | `u8 u8_count x { u16 u8 u8 u8 }` |
| `0x0A9` | 169 | ENUM_NOTIPACKET_AUCTION_ALARM | `0x01189EA0` | `0x0119AB19` | `u8 u8 u32 u16 sub(0x01088BC0: u8 u8 u8 u8 u8 u8 u8 u8 u8 u8)` |
| `0x0AB` | 171 | ENUM_NOTIPACKET_MOUSE_REGIST_DISCARD | `0x02231720` | `0x0119AB2C` | *handler outside this file* |
| `0x0B1` | 177 | ENUM_NOTIPACKET_SHOP_COIN_EVENT | `0x01189F50` | `0x0119AB3F` | `u8` |
| `0x0B7` | 183 | ENUM_NOTIPACKET_AUCTION_NOTIFY_AUCTION_SERVICE | `0x01189FB0` | `0x0119AB52` | `u8 u8` |
| `0x0B8` | 184 | ENUM_NOTIPACKET_MINIINFO_DUNGEON_TO_VILLAGE | `0x02231720` | `0x0119AB65` | *handler outside this file* |
| `0x0B9` | 185 | ENUM_NOTIPACKET_MINIINFO_GUILD_INFO | `0x02231720` | `0x0119AB78` | *handler outside this file* |
| `0x0BD` | 189 | ENUM_NOTIPACKET_CREATE_GUILD_AGIT | `0x0118A0B0` | `0x0119AB8B` | *(empty)* |
| `0x0BE` | 190 | ENUM_NOTIPACKET_DELETE_GUILD_AGIT | `0x0118F100` | `0x0119AB9E` | *(empty)* |
| `0x0BF` | 191 | ENUM_NOTIPACKET_GUILD_AGIT_INFO | `0x0118F230` | `0x0119ABB1` | `u8` |
| `0x0C2` | 194 | ENUM_NOTIPACKET_NPC_FAVOR | `0x01192040` | `0x0119ABC4` | `u16 u16_count x { u16 u16 }` |
| `0x0C3` | 195 | ENUM_NOTIPACKET_NPC_MOOD | `0x0118A0E0` | `0x0119ABD7` | `u16 u16_count x { u16 u8 }` |
| `0x0C5` | 197 | ENUM_NOTIPACKET_GUILD_MEMBER_MEMO | `0x0118F390` | `0x0119ABEA` | `str str` |
| `0x0CA` | 202 | ENUM_NOTIPACKET_VILLAGE_OBJECT_LIST | `0x01194CD0` | `0x0119ABFD` | `u8 u8_count x { u8 u16 str u8 u8 u16 u16 u32 u8 u16 str u8 u8 u8 u16 u16 u8 u8 u16 str u8 u8 u16 u16 u32 u8 u8 u8 } u8 u16 str u8 u8 u16 u16 u32 u8 u8 u8_count x { u8 }` |
| `0x0CB` | 203 | ENUM_NOTIPACKET_REQUEST_DISJOINT_ITEM | `0x0118A150` | `0x0119AC10` | `u8 u32 u32 u8` |
| `0x0CD` | 205 | ENUM_NOTIPACKET_EXPERT_JOB_INFO | `0x011928D0` | `0x0119AC23` | `u8 u8 u8 u32 u32 u8_count x { u32 } u8 u8_count x { u8 } u32 u32 u32 u32 u32 u32` |
| `0x0CE` | 206 | ENUM_NOTIPACKET_CREATE_DISJOINT_STORE | `0x0118A2A0` | `0x0119AC36` | `u16 str u8 u8 u16 u16 u32` |
| `0x0D9` | 217 | ENUM_NOTIPACKET_CLOSE_DISJOINT_STORE | `0x0118B800` | `0x0119AC49` | *(empty)* |
| `0x0DA` | 218 | ENUM_NOTIPACKET_CHANGE_GUILD_NAME_TO_GUILD_MEMBERS | `0x0118F430` | `0x0119AC5C` | `str` |
| `0x0E2` | 226 | ENUM_NOTIPACKET_POWER_WAR_BAN_ABUSER | `0x02231720` | `0x0119AC6F` | *handler outside this file* |
| `0x0E7` | 231 | ENUM_NOTIPACKET_POWER_WAR_USER_JOIN_INFO | `0x0118A400` | `0x0119AC82` | `u32 u32` |
| `0x0E8` | 232 | ENUM_NOTIPACKET_LOCK_IN_SERIA_ROOM_MESSAGE | `0x0118A430` | `0x0119AC95` | *(empty)* |
| `0x0EC` | 236 | ENUM_NOTIPACKET_BLACKLIST_PROBLEM_CHILD_TIME | `0x0118F660` | `0x0119ACA8` | `u32 u32` |
| `0x0ED` | 237 | ENUM_NOTIPACKET_GUILD_CARGO | `0x01198360` | `0x0119ACBB` | `u32 u16 u16_count x { bytes[101] }` |
| `0x0EE` | 238 | ENUM_NOTIPACKET_VILLAGE_MONSTER_LIST | `0x0118A460` | `0x0119ACCE` | `u8 u8_count x { u16 u32 u8 u16 u16 u16 u16 u16 u16 u8 u16 }` |
| `0x0EF` | 239 | ENUM_NOTIPACKET_VILLAGE_MONSTER_POSITION | `0x0118C140` | `0x0119ACE1` | `u8 u8_count x { u16 u16 u16 }` |
| `0x0F0` | 240 | ENUM_NOTIPACKET_UPDATE_VILLAGE_MONSTER_LIST | `0x0118A610` | `0x0119ACF4` | `u8 u16 u16 u16 u8 u16` |
| `0x0F1` | 241 | ENUM_NOTIPACKET_START_VILLAGE_ATTACKED | `0x0118A770` | `0x0119AD07` | `u32 u32 u32 u32 u32` |
| `0x0F2` | 242 | ENUM_NOTIPACKET_END_VILLAGE_ATTACKED | `0x0118A840` | `0x0119AD1A` | *(empty)* |
| `0x0F3` | 243 | ENUM_NOTIPACKET_FIGHT_VILLAGE_MONSTER | `0x0118A860` | `0x0119AD2D` | *(empty)* |
| `0x0F6` | 246 | ENUM_NOTIPACKET_CREATE_VILLAGE_MONSTER | `0x0118A880` | `0x0119AD40` | `u16 u32 u8 u16 u16 u16 u16 u16 u16 u8` |
| `0x0F7` | 247 | ENUM_NOTIPACKET_UPDATE_VILLAGE_ATTACKED | `0x0118A930` | `0x0119AD53` | `u32 u32 u32` |
| `0x0F8` | 248 | ENUM_NOTIPACKET_STARTED_VILLAGE_ATTACKED | `0x0118A9A0` | `0x0119AD66` | `u32 u32 u32 u32` |
| `0x0F9` | 249 | ENUM_NOTIPACKET_FINISH_VILLAGE_MONSTER_FIGHTING | `0x0118AA60` | `0x0119AD8C` | `u8 u32 u8 u32` |
| `0x0FA` | 250 | ENUM_NOTIPACKET_VILLAGE_ATTACKED_REWARD | `0x02231720` | `0x0119AD9F` | *handler outside this file* |
| `0x10B` | 267 | ENUM_NOTIPACKET_VICTORY_INFO | `0x0118AB20` | `0x0119ADC5` | `u32 u32` |
| `0x121` | 289 | ENUM_NOTIPACKET_BREAK_GUILD | `0x0118F800` | `0x0119ADD8` | `u32 u16` |
| `0x127` | 295 | ENUM_NOTIPACKET_DISCONNECT_LOW_LEVEL_LINK_CHARAC | `0x0118FCB0` | `0x0119ADEB` | `u8` |
| `0x129` | 297 | ENUM_NOTIPACKET_ULTIMATE_DIFFICULTY_UI | `0x0118C1F0` | `0x0119ADFE` | `u8 u8 u16` |
| `0x12B` | 299 | ENUM_NOTIPACKET_MEMBER_BONUS_FATIGUE_LIMIT | `0x0118FD50` | `0x0119AE11` | *(empty)* |
| `0x130` | 304 | ENUM_NOTIPACKET_RETURN_USER | `0x0118AB70` | `0x0119AE24` | `u32` |
| `0x133` | 307 | ENUM_NOTIPACKET_TODAY_GUILD_MEMBER | `0x0118AC60` | `0x0119AE37` | `u8` |
| `0x134` | 308 | ENUM_NOTIPACKET_JOIN_GUILD_INFO | `0x0118AC80` | `0x0119AE4A` | `u8 u32 str str u32 u32` |
| `0x137` | 311 | ENUM_NOTIPACKET_ACHIEVE_GUILD_ATTENDANCE | `0x02231720` | `0x0119AE96` | *handler outside this file* |
| `0x138` | 312 | ENUM_NOTIPACKET_COMPLETE_CONDITION_PASS_GATE | `0x01193290` | `0x0119AFA0` | `u32 u8` |
| `0x139` | 313 | ENUM_NOTIPACKET_BREAK_AWAY_QUEST_DATA | `0x0118B780` | `0x0119AFB3` | `sub(0x024A9910: u32 u8)` |
| `0x13B` | 315 | ENUM_NOTIPACKET_SPECIFIC_EVENT_NOTIFICATION | `0x02231720` | `0x0119AFC6` | *handler outside this file* |
| `0x141` | 321 | ENUM_NOTIPACKET_DEBUG_FILE_PRINT | `0x02231720` | `0x0119AFD9` | *handler outside this file* |
| `0x177` | 375 | ENUM_NOTIPACKET_EVENT_INIT_INFO | `0x0118B040` | `0x0119AEA9` | `u32 u8 u8_count x { u16 }` |
| `0x178` | 376 | ENUM_NOTIPACKET_EVENT_HISTORY_INFO | `0x0118B0B0` | `0x0119AEBC` | `u8 u8_count x { u16 }` |
| `0x17A` | 378 | ENUM_NOTIPACKET_PARTY_TELEPORT_STATUS | `0x0118FDD0` | `0x0119AECF` | `u8 u8 u8 u16 N x { u16 u8 } u8` |
| `0x195` | 405 | ENUM_NOTIPACKET_CHRISTMAS_TREE_INFO | `0x0118B3D0` | `0x0119AF8D` | `u8` |
| `0x1A1` | 417 | ENUM_NOTIPACKET_INFORM_NOTICE | `0x0118B100` | `0x0119AEE2` | `u8 u8_count x { u8 }` |
| `0x1A2` | 418 | ENUM_NOTIPACKET_VERIFY_PVP_LAG_USER | `0x0118B1C0` | `0x0119AF08` | *(empty)* |
| `0x1A6` | 422 | ENUM_NOTIPACKET_GM_CHECK_KICK_OUT_ROOM_COUNT | `0x02231720` | `0x0119AF1B` | *handler outside this file* |
| `0x1A9` | 425 | ENUM_NOTIPACKET_PC_ROOM_PLAY_TIME | `0x0118B1E0` | `0x0119AF2E` | *(empty)* |
| `0x1B9` | 441 | ENUM_NOTIPACKET_INFORM_NOTICE_2ND | `0x0118B160` | `0x0119AEF5` | `u8 u8_count x { u8 }` |
| `0x215` | 533 | ENUM_NOTIPACKET_VILLAGE_ATTACKED_MY_INFO | `0x0118AA20` | `0x0119AD79` | `u32` |
| `0x216` | 534 | ENUM_NOTIPACKET_USE_ENCHANT_STORE | `0x0118B810` | `0x0119B04B` | `u32 u32` |
| `0x21B` | 539 | ENUM_NOTIPACKET_CREATE_EXPERT_JOB_STORE | `0x01195270` | `0x0119B025` | `u8 u16 str u8 u8 u16 u16 u32 u8 u8 u8_count x { u8 }` |
| `0x21C` | 540 | ENUM_NOTIPACKET_CLOSE_EXPERT_JOB_STORE | `0x0118B800` | `0x0119B038` | *(empty)* |
| `0x221` | 545 | ENUM_NOTIPACKET_UPDATE_EXPERT_JOB_STORE | `0x01195510` | `0x0119B05E` | `u8 u16 str u8 u8_count x { u8 }` |
| `0x226` | 550 | ENUM_NOTIPACKET_CONDITION_PASS_GATE_INFO | `0x01197640` | `0x0119B071` | `u32` |
| `0x22B` | 555 | ENUM_NOTIPACKET_GIVE_FIGHT_DOLL_TICKET | `0x0118B8C0` | `0x0119B084` | *(empty)* |
| `0x230` | 560 | ENUM_NOTIPACKET_USER_STATE_MOTION | `0x01191270` | `0x0119B097` | `u16 u16 u16 u8 u16 u8 u16 u8 u16` |
| `0x23C` | 572 | ENUM_NOTIPACKET_EXPECT_PARTY_LIST | `0x0118B8E0` | `0x0119B0D0` | `u8 u8 u8_count x { u16 u16 u16 u8 u8 u32 u8 u8 u8 u32 u32 }` |
| `0x245` | 581 | ENUM_NOTIPACKET_SEND_DECK_INFO | `0x0118BB60` | `0x0119B0E3` | `N x { u32 }` |
| `0x246` | 582 | ENUM_NOTIPACKET_HARDCORE_STATUE | `0x01191560` | `0x0119B0F6` | `u8 u8_count x { str u8 u8 u8 u8 u8 u32 }` |
| `0x26D` | 621 | ENUM_NOTIPACKET_EVENT_END | `0x02740AA0` | `0x0119B11C` | *handler outside this file* |
| `0x28C` | 652 | ENUM_NOTIPACKET_USE_COIN_LIMIT_EVENT_INFO | `0x01192120` | `0x0119B155` | `u8 u16 u8 u8_count x { u32 } u16 u8 u8 u16 u16 u8 u8` |
| `0x28F` | 655 | ENUM_NOTIPACKET_ITEMTRADE_REG_ITEM_FINISH | `0x0118BEA0` | `0x0119B142` | *(empty)* |
| `0x2A0` | 672 | ENUM_NOTIPACKET_SHOP_PURCHASE_COUNT | `0x0118BEB0` | `0x0119B168` | *(empty)* |
| `0x2AD` | 685 | ENUM_NOTIPACKET_REQUEST_GUILD_CREATE_PERMIT | `0x0118AEF0` | `0x0119AE70` | `str str str` |
| `0x2AE` | 686 | ENUM_NOTIPACKET_REPLY_GUILD_CREATE_PERMIT | `0x0118AE20` | `0x0119AE5D` | `u8` |
| `0x2AF` | 687 | ENUM_NOTIPACKET_CANCEL_GUILD_CREATE | `0x0118AFD0` | `0x0119AE83` | *(empty)* |
| `0x2CA` | 714 | ENUM_NOTIPACKET_TERRITORY_COMBAT_DECLARATION_LIST | `0x01199980` | `0x0119B17B` | `u8 N x { u8 u8 u32 str u8 u8 }` |
| `0x2CE` | 718 | ENUM_NOTIPACKET_SERVER_CHARACTER_LIST | `0x01189BD0` | `0x0119B18E` | `u8 u8 u8_count x { u32 str }` |
| `0x2D3` | 723 | ENUM_NOTIPACKET_TERRITORY_COMBAT_INFO_TREASURE_HOUSE | `0x0118BED0` | `0x0119B1A1` | `u8 u8` |
| `0x2D4` | 724 | ENUM_NOTIPACKET_TERRITORY_GUILD_INFO | `0x011996E0` | `0x0119B1B4` | `N x { u8 u8 u32 str u8 }` |
| `0x2D8` | 728 | ENUM_NOTIPACKET_GROWTYPE_CHANGE_JOB_INFO | `0x0118BF10` | `0x0119B1C7` | `u8` |
| `0x2E2` | 738 | ENUM_NOTIPACKET_TERRITORY_COMBAT_EVENT_INFO | `0x0118BF40` | `0x0119B1DA` | `u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32` |
| `0x2EE` | 750 | ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_REQUEST_JOIN | `0x01191870` | `0x0119B1ED` | `bytes[50]` |
| `0x2EF` | 751 | ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_RESPONSE_JOIN | `0x0118C2A0` | `0x0119B200` | `bytes[5]` |
| `0x2F2` | 754 | ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_USER_TITLE_BOOK | `0x01198830` | `0x0119B213` | `u8 u16 u8 u8 u8_count x { u8 u32 u16 u32 u32 u8 u16 u8 u32 u8 u8 u16 }` |
| `0x310` | 784 | ENUM_NOTIPACKET_PROPER_DUNGEON_EVENT | `0x0118C060` | `0x0119B24C` | *(empty)* |
| `0x312` | 786 | ENUM_NOTIPACKET_CREATE_CHARAC_FIRST_LOGIN | `0x0118C090` | `0x0119B25F` | *(empty)* |
| `0x32D` | 813 | ENUM_NOTIPACKET_AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM | `0x01198700` | `0x0119AFEC` | `bytes[4]` |
| `0x331` | 817 | ENUM_NOTIPACKET_LIMIT_IPGNO | `0x0118B7A0` | `0x0119AFFF` | `u8 u8_count x { u32 }` |
| `0x332` | 818 | ENUM_NOTIPACKET_CHARAC_PREMIUM_NAME_TAG | `0x01191170` | `0x0119B012` | `bytes[8]` |
| `0x33C` | 828 | ENUM_NOTIPACKET_CERASHOP_BONUS_ITEM | `0x0118B200` | `0x0119AF41` | `u8 bytes[12]` |
| `0x33F` | 831 | ENUM_NOTIPACKET_DAILYSCHEDULE_CONTENTS_STATE | `0x01192C10` | `0x0119AF54` | `u32 u32 u32 u32` |
| `0x342` | 834 | ENUM_NOTIPACKET_LIMIT_ITEM_INFO | `0x0118AAE0` | `0x0119ADB2` | `u32 u32 u32` |
| `0x353` | 851 | ENUM_NOTIPACKET_PERSONAL_INFO | `0x0118B340` | `0x0119AF7A` | `bytes[2]` |
| `0x377` | 887 | ENUM_NOTIPACKET_PREMIUM_EXPIRE_NOTI | `0x0118B2B0` | `0x0119AF67` | `u32 u32_count x { u32 u32 }` |
| `0x396` | 918 | ENUM_NOTIPACKET_FIRST_LOGIN_INFO | `0x0118BB10` | `0x0119B0BD` | `bytes[2]` |
| `0x39B` | 923 | ENUM_NOTIPACKET_UPGRADE_CARRY_GOLD | `0x01191440` | `0x0119B0AA` | `u8` |
| `0x3FB` | 1019 | ENUM_NOTIPACKET_SKILL_INDEX | `0x01197B10` | `0x0119B109` | `u8 u8_count x { u8 u16 u16 u8 }` |
| `0x411` | 1041 | ENUM_NOTIPACKET_GM_ITEM_SIMULATION | `0x0118BC00` | `0x0119B12F` | `u32 u32 u32 u32 u32_count x { u32 u32 u32 }` |
| `0x484` | 1156 | ENUM_NOTIPACKET_CHECK_TERRITORY_COMBAT_DECIDED_LIST | `0x0118C020` | `0x0119B226` | `u8` |
| `0x4A4` | 1188 | ENUM_NOTIPACKET_TERRITORY_COMBAT_COMPULSORY_LEAVING_IN_EXERCISE_MODE | `0x0118C050` | `0x0119B239` | *(empty)* |
| `0x4BD` | 1213 | ENUM_NOTIPACKET_RAID_USER_FINISH_LOAD | `0x0118C0B0` | `0x0119B272` | *(empty)* |

---

## 6. Handlers with no payload

These registered handlers execute no read primitive at all - the packet body is empty.

- `0x03D` ENUM_NOTIPACKET_GUILD_DISMISS @ `0x0118E0F0`
- `0x050` ENUM_NOTIPACKET_FROCE_REMOVE_PRIVATE_STORE @ `0x0118B540`
- `0x072` ENUM_NOTIPACKET_CREATURE_MESSAGE @ `0x011928C0`
- `0x07C` ENUM_NOTIPACKET_ENTER_GAMEWORLD_COMPLETE @ `0x01194220`
- `0x096` ENUM_NOTIPACKET_HAS_BEEN_GUILD_MASTER @ `0x01189DE0`
- `0x097` ENUM_NOTIPACKET_GUILD_SECEDE_TO_USER @ `0x0118EFB0`
- `0x0BD` ENUM_NOTIPACKET_CREATE_GUILD_AGIT @ `0x0118A0B0`
- `0x0BE` ENUM_NOTIPACKET_DELETE_GUILD_AGIT @ `0x0118F100`
- `0x0D9` ENUM_NOTIPACKET_CLOSE_DISJOINT_STORE @ `0x0118B800`
- `0x0E8` ENUM_NOTIPACKET_LOCK_IN_SERIA_ROOM_MESSAGE @ `0x0118A430`
- `0x0F2` ENUM_NOTIPACKET_END_VILLAGE_ATTACKED @ `0x0118A840`
- `0x0F3` ENUM_NOTIPACKET_FIGHT_VILLAGE_MONSTER @ `0x0118A860`
- `0x12B` ENUM_NOTIPACKET_MEMBER_BONUS_FATIGUE_LIMIT @ `0x0118FD50`
- `0x1A2` ENUM_NOTIPACKET_VERIFY_PVP_LAG_USER @ `0x0118B1C0`
- `0x1A9` ENUM_NOTIPACKET_PC_ROOM_PLAY_TIME @ `0x0118B1E0`
- `0x21C` ENUM_NOTIPACKET_CLOSE_EXPERT_JOB_STORE @ `0x0118B800`
- `0x22B` ENUM_NOTIPACKET_GIVE_FIGHT_DOLL_TICKET @ `0x0118B8C0`
- `0x28F` ENUM_NOTIPACKET_ITEMTRADE_REG_ITEM_FINISH @ `0x0118BEA0`
- `0x2A0` ENUM_NOTIPACKET_SHOP_PURCHASE_COUNT @ `0x0118BEB0`
- `0x2AF` ENUM_NOTIPACKET_CANCEL_GUILD_CREATE @ `0x0118AFD0`
- `0x310` ENUM_NOTIPACKET_PROPER_DUNGEON_EVENT @ `0x0118C060`
- `0x312` ENUM_NOTIPACKET_CREATE_CHARAC_FIRST_LOGIN @ `0x0118C090`
- `0x4A4` ENUM_NOTIPACKET_TERRITORY_COMBAT_COMPULSORY_LEAVING_IN_EXERCISE_MODE @ `0x0118C050`
- `0x4BD` ENUM_NOTIPACKET_RAID_USER_FINISH_LOAD @ `0x0118C0B0`

## 7. Codes whose handler lives outside this file

- `0x034` ENUM_NOTIPACKET_FRIEND_INFO -> `0x02231720` (shared stub / other TU)
- `0x038` ENUM_NOTIPACKET_BUDDY_CONN_LIST -> `0x02231720` (shared stub / other TU)
- `0x03E` ENUM_NOTIPACKET_GUILD_CHAT_MSG -> `0x02231720` (shared stub / other TU)
- `0x03F` ENUM_NOTIPACKET_GUILD_CHAR_NAMES -> `0x02231720` (shared stub / other TU)
- `0x075` ENUM_NOTIPACKET_GUILD_WAR_START -> `0x02231720` (shared stub / other TU)
- `0x0AB` ENUM_NOTIPACKET_MOUSE_REGIST_DISCARD -> `0x02231720` (shared stub / other TU)
- `0x0B8` ENUM_NOTIPACKET_MINIINFO_DUNGEON_TO_VILLAGE -> `0x02231720` (shared stub / other TU)
- `0x0B9` ENUM_NOTIPACKET_MINIINFO_GUILD_INFO -> `0x02231720` (shared stub / other TU)
- `0x0E2` ENUM_NOTIPACKET_POWER_WAR_BAN_ABUSER -> `0x02231720` (shared stub / other TU)
- `0x0FA` ENUM_NOTIPACKET_VILLAGE_ATTACKED_REWARD -> `0x02231720` (shared stub / other TU)
- `0x137` ENUM_NOTIPACKET_ACHIEVE_GUILD_ATTENDANCE -> `0x02231720` (shared stub / other TU)
- `0x13B` ENUM_NOTIPACKET_SPECIFIC_EVENT_NOTIFICATION -> `0x02231720` (shared stub / other TU)
- `0x141` ENUM_NOTIPACKET_DEBUG_FILE_PRINT -> `0x02231720` (shared stub / other TU)
- `0x1A6` ENUM_NOTIPACKET_GM_CHECK_KICK_OUT_ROOM_COUNT -> `0x02231720` (shared stub / other TU)
- `0x26D` ENUM_NOTIPACKET_EVENT_END -> `0x02740AA0` (shared stub / other TU)

---

## 8. What is NOT established

- **Field semantics.** Only widths and order are evidence-backed. Where a handler's own
  log string names fields (`0x0EE`, `0x0F0`, `0x03C`, `0x2D4`) I quoted it; elsewhere I
  did not guess.
- Loops printed as `N x {` are **not** confirmed length-prefixed.
- Branch-alternative reads: see the caveat in section 3.
- What the 4th `Register` argument means. It is `0` at all 156 sites, so nothing about it
  is observable here.
- Nothing was recovered for the 15 codes whose handlers are outside this TU.
