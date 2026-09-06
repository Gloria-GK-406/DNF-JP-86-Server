
# GameCoreNetwork_CMD_Town.cpp — handlers, wire formats, and dispatch

Client: DNF S4A21, unpacked-from-memory image, base `0x00400000`.
All addresses below are **VAs in that image**.

> **Address-space note.** The read primitives named in the task brief
> (`0x023C5230` read_u8 etc.) are RVA-style. In this image's VA space they live
> at `+0x400000`: `read_u8 = 0x027C5230`, `read_u16 = 0x027C52A0`,
> `read_u32 = 0x027C5310`, `read_bytes = 0x027C5380`,
> `set_remaining = 0x027C4350`. Verified by disassembly: `0x027C52A0` does
> `mov cx, word ptr [eax]` / `add eax,2` / `sub dword ptr [0x3BAAE3C],2` against
> the global stream cursor `0x3BAAE38` and remaining-counter `0x3BAAE3C`.
> `0x023C5230` is not a function entry at all — it is mid-body inside a blob
> that starts at `0x023BD910`.

---

## 1. Scope of the file

`func-source-file.csv` gives 11 `__FILE__` anchors for
`DNFClient\RDAR\GameCoreNetwork_CMD_Town.cpp`:

```
0x01119280 0x0111F3F0 0x01120970 0x0112B470 0x0112D150 0x01130350
0x011345C0 0x011356E0 0x0113C5A0 0x0113F6D0 0x01144220
```

Neighbouring translation units are laid out contiguously — the last
`GameCoreNetwork_CMD_Etc.cpp` anchor is `0x01118180` and the first
`GameCoreNetwork_NOTI_Dungeon.cpp` anchor is `0x01146000` — so the TU occupies
approximately **`0x01119280` – `0x01145FFF`**.

That range is *not* 11 functions. `functions.csv` merged hundreds of real
functions into a handful of giant blobs, because the handlers have **no direct
callers**: they are reachable only through a runtime-built handler registry
(section 3). Recovering entry points from the registry yields **245 distinct
handler functions** in the range, carrying **269 command codes**.

---

## 2. Framing — what the framework consumes before a handler runs

`CNGameCore::NetworkProc`, `GameCoreNetwork.cpp` @ **`0x0163BBD0`**
(file literal `D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork.cpp` pushed at
`0x0163BEFF`; function-name literal `CNGameCore::NetworkProc` at `0x0163BEF1`).

The packet header is 15 (`0x0F`) bytes; payload length = `*(u32*)(pkt+3) - 0x0F`
(`0x0163BC23`, `0x0163BCF9`, `0x0163BFD8`).

```
pkt+0  u8   channel selector  (1 = CMD path; 0 = log-only path;
                               anything else -> "HACK>> Network Packet
                               Type Error [ %d ]"  @ 0x0163BEDE)
pkt+1  u16  COMMAND CODE      <-- the dispatch key
pkt+3  u32  total size (header included)
pkt+7  ...  8 further header bytes
```

On the CMD path (`pkt[0] == 1`, decided at `0x0163BEDB`) the framework itself
consumes the first one or two **payload** bytes before calling the handler:

```
0163BF30  call read_u8   -> bResult                  ; ALWAYS
0163BF63  cmp  bResult, 0
0163BF6D  jne  skip                                  ; bResult != 0 == "Ok"
0163BF73  call read_u8   -> bErrCode                 ; ONLY when bResult == 0
```

Evidence literals decrypted from the same block: `[CMD RECV] %d (Size : %d) Result : %s`
(`0x0163BF35`), ` ErrCode : %d` (`0x0163BF78`), `Ok` (`0x0163BFAE`),
`Error` (`0x0163BFB5`).

So **every** wire format in the table below is implicitly prefixed by

```
u8 bResult ; if (bResult == 0) u8 bErrCode
```

The handler is then invoked as (`0x0163770C`):

```
handler(WORD cmdCode /*[ebp+8]*/, BYTE bResult /*[ebp+0xC]*/,
        BYTE bErrCode /*[ebp+0x10]*/, void* ctx /*[ebp+0x14]*/)
```

`ctx` is `0` for every CMD_Town registration. This is why many handlers read
nothing at all: they are pure ack/error handlers that only switch on `bErrCode`.
In the table, `ok:` marks reads reachable only when `bResult != 0`, `err:` marks
reads reachable only when `bResult == 0`.

---

## 3. THE DISPATCH

It is **not** a static jump table and **not** a static array of function
pointers. It is a hash map keyed on the u16 command code, populated at startup
by straight-line registration code — one registration function per source file.

### 3.1 The registry

| role | VA | notes |
|---|---|---|
| singleton accessor (map A) | `0x0163C410` | lazy init; object pointer cached in `0x03A5BE68`, init flag `0x03A5BE6C`, object size `0x1C` |
| **insert** `Register(u16 code, PFN fn, void* ctx)` | **`0x0163C4A0`** | `__thiscall`, `ecx` = map |
| find → entry | `0x0163C510` | returns `entry*` or 0; entry is `{ PFN fn; void* ctx; }`, 8 bytes |
| **dispatch** | **`0x01637680`** | `movzx esi, word ptr [eax+1]` (cmd code) → `0x0163C410` → `0x0163C510` → `call [entry+0]` |

Dispatch body (`0x016376EC` – `0x0163770E`):

```
016376EC  push esi                 ; esi = *(u16*)(pkt+1)
016376ED  call 0x163C410           ; map singleton
016376F4  call 0x163C510           ; lookup -> eax = entry
016376FB  je   0x1637711           ; unregistered -> silently ignored
016376FD  mov  ecx,[eax+4]         ; ctx
01637708  mov  edx,[eax]           ; fn
0163770C  call edx
```

Two thin wrappers exist for registration from other modules into the same map
(`0x01637660`, 272 call sites) and into a second map obtained from `0x0163C550`
(`0x01637720`, 302 call sites). No CMD_Town handler is registered through
either — I checked all 501 decodable wrapper sites.

### 3.2 The CMD_Town dispatch table — registration function `0x01144AF0`

This is the type → handler mapping for this file. It is a flat run of **268**
registrations, call sites `0x01144B12` … `0x01145E35`, every one of the shape:

```
push 0                ; ctx
push <handler VA>     ; fn
push <command code>   ; u16 key
mov  ecx, esi         ; the map
call 0x0163C4A0
```

There is no stride to quote because it is code, not data: each record is three
`push imm8|imm32` + `mov ecx,esi` + `call rel32`, 12–21 bytes depending on
whether the code and the handler encode as imm8.

`0x01144AF0` is called exactly once, from `0x0163C193` (GameCoreNetwork.cpp
init). The sibling registration functions, for orientation:

| source file | registration fn | records |
|---|---|---|
| GameCoreNetwork_CMD_Dungeon.cpp | `0x0110C2C0` | 46 |
| GameCoreNetwork_CMD_Etc.cpp | `0x01118180` | 84 |
| **GameCoreNetwork_CMD_Town.cpp** | **`0x01144AF0`** | **268** |
| GameCoreNetwork_NOTI_Dungeon.cpp | `0x0115F540` | 155 |
| GameCoreNetwork_NOTI_Etc.cpp | `0x01187FF0` | 227 |
| GameCoreNetwork_NOTI_Town.cpp | `0x0119A780` | 155 |

935 registrations were decoded image-wide out of 943 `call 0x0163C4A0` sites
(the 8 undecoded ones are the first record of each run, which uses
`mov esi,ecx` instead of `mov ecx,esi`, plus the two wrappers).

Of the 268 CMD_Town records, **17 point at `0x02231720`**, which is a bare `ret`
— the shared "registered but ignored" stub. One extra Town-range handler
(`0x01134670`, code `0x008B`) is registered from the CMD_Etc run at
`0x01118DE5`, which is why the table below has 269 rows.

### 3.3 Independent cross-check of the mapping

29 handlers push their own profiler-scope literal
`CMDFUNC_ENUM_CMDPACKET_<NAME>`. Those names line up with the codes the
registration run assigns — a check on the mapping that does not depend on my
decode of the registration run at all:

| code | handler | self-declared name |
|---|---|---|
| 0x000C | `0x01119990` | ENUM_CMDPACKET_SET_PARTY_INFO |
| 0x000D | `0x01119A20` | ENUM_CMDPACKET_LEAVE_PARTY |
| 0x000E | `0x01119A90` | ENUM_CMDPACKET_WALKOUT_PARTY_MEMBER |
| 0x0012 | `0x011303E0` | ENUM_CMDPACKET_DELETE_ITEM |
| 0x0013 | `0x0113C620` | ENUM_CMDPACKET_MOVE_ITEMSPACE |
| 0x0014 | `0x01119B10` | ENUM_CMDPACKET_SORT_ITEM |
| 0x0015 | `0x01135760` | ENUM_CMDPACKET_BUY_ITEM |
| 0x0016 | `0x01122240` | ENUM_CMDPACKET_SELL_ITEM |
| 0x001B | `0x0113F780` | ENUM_CMDPACKET_USE_LOTTERY_ITEM |
| 0x001C | `0x01119CD0` | ENUM_CMDPACKET_CHANGE_SKILLSLOT |
| 0x001D | `0x0112D4C0` | ENUM_CMDPACKET_BUY_SKILL |
| 0x0022 | `0x01140810` | ENUM_CMDPACKET_FINISH_QUEST |
| 0x0024 | `0x0112B620` | ENUM_CMDPACKET_SET_USER_AREA |
| 0x002F | `0x01123AC0` | ENUM_CMDPACKET_DROP_ITEM |
| 0x0063 | `0x0112BBA0` | ENUM_CMDPACKET_COMPOUND_AVATAR |
| 0x00BA | `0x0111F970` | ENUM_CMDPACKET_AUCTION_SEARCH_BY_ITEMKEY |
| 0x00C9 | `0x0112D1C0` | ENUM_CMDPACKET_USE_EMBLEM |
| 0x00F7 | `0x01127E20` | ENUM_CMDPACKET_GUILD_CARGO_PUSH_ITEM |
| 0x00F8 | `0x01128030` | ENUM_CMDPACKET_GUILD_CARGO_POP_ITEM |
| 0x012F | `0x0111D800` | ENUM_CMDPACKET_BREAK_GUILD |
| 0x0183 | `0x01134680` | ENUM_CMDPACKET_REQUEST_DUNGEON_PARTY_LIST |
| 0x019C | `0x0111CD40` | ENUM_CMDPACKET_TITLE_BOOK_PUT |
| 0x019D | `0x0111CFF0` | ENUM_CMDPACKET_TITLE_BOOK_GET |
| 0x01AB | `0x01129240` | ENUM_CMDPACKET_REFUND_SKILL |
| 0x0234 | `0x011442B0` | ENUM_CMDPACKET_MINORITY_DETAIL_RESULT |
| 0x02F0 | `0x0111E160` | ENUM_CMDPACKET_SET_USER_AREA_PRE_CHECK |
| 0x0329 | `0x01133BE0` | ENUM_CMDPACKET_REQ_REPRESENT_CHARACTER |
| 0x033A | `0x01119E30` | ENUM_CMDPACKET_USE_GEM |
| 0x03BA | `0x01131FD0` | ENUM_CMDPACKET_UPGRADE_CARRY_GOLD |

There is also a per-code **name table at `0x03B53230`**
(`mov eax, dword ptr [edx*4 + 0x3B53230]` @ `0x0163C059`, `edx` = the u16 code,
stride 4, feeding a profiler-scope ctor at `0x027A56E0`). In this memory dump it
is entirely zero — it is only populated when the profiling build flag is on — so
it could not be used to recover the remaining names.

---

## 4. Wire formats

Notation:

* every row is implicitly prefixed by `u8 bResult; if (bResult == 0) u8 bErrCode`
* `u8 / u16 / u32 / bytes` = one call to the corresponding read primitive
* `ok:` / `err:` = read reachable only on the `bResult != 0` / `bResult == 0` branch
* `opt` = a path exists from that branch's entry to a `ret` that skips this read
  (conditional / optional field)
* `N x { ... }` = the reads sit on a cycle in the CFG — a **length-prefixed
  array**, whose count is the immediately preceding scalar read. Verified
  instruction-by-instruction on `0x011303E0` (DELETE_ITEM):
  `read_u8 -> [ebp-0xD]`, `read_u8 -> [ebp+0xF]`,
  `cmp byte ptr [ebp+0xF],0 / jbe` guarding a body of
  `read_u16, read_u32, read_u16`.
* an empty wire format means the handler makes no read calls at all: the packet
  body is exactly the framework prefix.

Reads reached through called sub-functions are inlined (depth ≤ 3). Reads are
listed in address order, which for MSVC straight-line parsers is source order.

| code | handler VA | name (self-declared) | wire format after the prefix | nearby literals |
|---|---|---|---|---|
| 0x0009 | `0x011209D0` |  | ok:u32 | STAMINA_RECOVER |
| 0x000A | `0x01120C30` |  | err:u8, err:opt u32 |  |
| 0x000B | `0x01121620` |  | opt u16, 3 x opt u8 |  |
| 0x000C | `0x01119990` | ENUM_CMDPACKET_SET_PARTY_INFO | *(no reads - ack/error only)* |  |
| 0x000D | `0x01119A20` | ENUM_CMDPACKET_LEAVE_PARTY | *(no reads - ack/error only)* |  |
| 0x000E | `0x01119A90` | ENUM_CMDPACKET_WALKOUT_PARTY_MEMBER | *(no reads - ack/error only)* |  |
| 0x0011 | `0x0111B480` |  | *(no reads - ack/error only)* |  |
| 0x0012 | `0x011303E0` | ENUM_CMDPACKET_DELETE_ITEM | 2 x ok:u8, ok:N x {opt u16, opt u32, opt u16}, opt u8 | item type bErrorCode; this item cannot be deleted |
| 0x0013 | `0x0113C620` | ENUM_CMDPACKET_MOVE_ITEMSPACE | opt u8, opt u16, opt u32, opt u8, opt u16, 2 x opt u8 | [%s][%d] -> [%s][%d] : moveNumber(%d); %s %s |
| 0x0014 | `0x01119B10` | ENUM_CMDPACKET_SORT_ITEM | ok:u8, err:u8 | ARRANGE_FINISH |
| 0x0015 | `0x01135760` | ENUM_CMDPACKET_BUY_ITEM | 4 x opt u32, opt bytes, opt u8, N x {opt u32, opt u32} | costItemSlot %d, costItem %x |
| 0x0016 | `0x01122240` | ENUM_CMDPACKET_SELL_ITEM | ok:u32, ok:u8, 2 x ok:u16 | SELL ITEM START; SELL ITEM 1 |
| 0x0017 | `0x01122C40` |  | ok:u32, ok:u8, 2 x ok:u16 | ITEM_REPAIR |
| 0x0018 | `0x01119BE0` |  | *(no reads - ack/error only)* |  |
| 0x0019 | `0x01136A30` |  | opt u8, N x {opt u8, opt u16, opt u32}, opt u8, N x {opt u8, opt u16, opt u32, opt u32, opt u8, opt u16, opt u8, opt u16, opt u8, opt u32, opt u8, opt u32, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8} |  |
| 0x001A | `0x011376D0` |  | opt u16, 2 x opt u8, N x {opt u16, opt u32, opt u32} |  |
| 0x001B | `0x0113F780` | ENUM_CMDPACKET_USE_LOTTERY_ITEM | 2 x opt u16, 2 x opt u32, opt u16, 2 x opt u8, opt u16, opt u32, opt u8, N x {opt u32}, opt u16, opt u8, N x {opt u8, opt u8, opt u8}, 9 x opt u8, opt u16, 6 x opt u8, opt u32 | stackableScript == nullptr [%s(%d)]; COKE_ITEM_OUT |
| 0x001C | `0x01119CD0` | ENUM_CMDPACKET_CHANGE_SKILLSLOT | ok:u8, 2 x ok:opt u8 |  |
| 0x001D | `0x0112D4C0` | ENUM_CMDPACKET_BUY_SKILL | ok:u8, 2 x ok:opt u16, ok:opt u8, ok:N x {opt u8, opt u16, opt u8, opt u8, opt u8, opt u8} |  |
| 0x001E | `0x0112DFB0` |  | ok:u16, ok:u8, ok:u32, 2 x ok:u16 | SP_GET; QUEST_AUTO |
| 0x001F | `0x01138230` |  | opt u16, opt u32, opt u8, N x {opt u16, opt u32, opt u32} | QUEST_ACCEPT |
| 0x0020 | `0x01129A90` |  | ok:u16 |  |
| 0x0021 | `0x011200A0` |  | ok:u16, ok:u32 |  |
| 0x0022 | `0x01140810` | ENUM_CMDPACKET_FINISH_QUEST | ok:u16, ok:u8, 2 x ok:u32, ok:opt u8, ok:N x {opt u8, opt u16, opt u32}, ok:u8, ok:opt u8, ok:N x {opt u16, opt u32, opt u32, opt u8, opt u16, opt u32, opt u8, opt u8}, 2 x ok:opt u8, ok:opt u32, ok:opt u8, ok:N x {opt u8, opt u8, opt u16, opt u8} | QUEST_COMPLETE2; //autoclear 종료. |
| 0x0024 | `0x0112B620` | ENUM_CMDPACKET_SET_USER_AREA | 2 x opt u8 |  |
| 0x002C | `0x011235B0` |  | opt u16, opt u8, 2 x opt u32, u8, 2 x u32 |  |
| 0x002F | `0x01123AC0` | ENUM_CMDPACKET_DROP_ITEM | ok:u8, ok:u16, ok:u32, err:u8 | item type bErrorCode; this item cannot be deleted |
| 0x0032 | `0x01123E60` |  | *(no reads - ack/error only)* |  |
| 0x0033 | `0x011240B0` |  | ok:N x {u8} |  |
| 0x003C | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x003D | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0040 | `0x01124550` |  | opt u8, 5 x opt u32, opt u16, N x {opt u32, opt u32}, opt u8, 5 x opt u32 | bErrorCode = %d |
| 0x0041 | `0x0111A0F0` |  | ok:opt u32, ok:opt bytes, ok:u32 | ERROR!ERROR! |
| 0x0043 | `0x01134670` |  | *(no reads - ack/error only)* |  |
| 0x0044 | `0x0111A3A0` |  | *(no reads - ack/error only)* |  |
| 0x0049 | `0x01125020` |  | *(no reads - ack/error only)* |  |
| 0x0050 | `0x0113D950` |  | opt u8, opt u16, opt u32, opt u16, 5 x opt u8, 2 x opt u16, opt u8, N x {opt u16, opt u32, opt u32} |  |
| 0x0051 | `0x011255B0` |  | ok:u16, ok:u32, ok:u16 |  |
| 0x0052 | `0x01125B40` |  | 3 x opt u32, opt u16, 2 x opt u32, opt u8, opt u16, opt u32, 2 x opt u8, opt u16, opt u32, opt u8, N x {opt u32}, opt u16, opt u8, N x {opt u8, opt u8, opt u8}, 9 x opt u8, opt u16, 6 x opt u8 |  |
| 0x0053 | `0x01126280` |  | opt u16, opt u8, N x {opt u16, opt u32, opt u32, opt u32, opt u8, opt u16, opt u32, opt u8, opt u8, opt u16, opt u32, opt u8, opt u32, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8} |  |
| 0x0054 | `0x0111AE40` |  | *(no reads - ack/error only)* |  |
| 0x0055 | `0x0111AE50` |  | *(no reads - ack/error only)* |  |
| 0x0056 | `0x0111AE90` |  | *(no reads - ack/error only)* |  |
| 0x0057 | `0x0111AED0` |  | ok:opt u8 |  |
| 0x0058 | `0x0111AE40` |  | *(no reads - ack/error only)* |  |
| 0x005A | `0x01126A00` |  | ok:u32, ok:u16 |  |
| 0x005E | `0x0111F950` |  | *(no reads - ack/error only)* |  |
| 0x005F | `0x0111B340` |  | ok:u32 |  |
| 0x0060 | `0x0111B390` |  | ok:u16 |  |
| 0x0062 | `0x01126750` |  | *(no reads - ack/error only)* |  |
| 0x0063 | `0x0112BBA0` | ENUM_CMDPACKET_COMPOUND_AVATAR | opt u8, N x {opt u8, opt u16, opt u32}, N x {opt u16, opt u32, opt u32, opt u16} | Compound avatar ok!!; %d, %u, %u, %u |
| 0x0064 | `0x0112CA20` |  | opt u16, opt u8 |  |
| 0x0066 | `0x0111B420` |  | *(no reads - ack/error only)* |  |
| 0x0071 | `0x0111DC20` |  | *(no reads - ack/error only)* |  |
| 0x0076 | `0x0111F630` |  | ok:opt u32, ok:opt bytes |  |
| 0x0077 | `0x011243E0` |  | ok:opt u32, ok:opt bytes |  |
| 0x0078 | `0x0111F800` |  | opt u8, N x {opt u32, opt bytes, opt u8, opt u8, opt u8} |  |
| 0x007E | `0x01131170` |  | opt u8, opt u32, opt bytes, opt u8 |  |
| 0x0086 | `0x0111B3D0` |  | ok:u32, ok:u16 |  |
| 0x008B | `0x01134670` |  | *(no reads - ack/error only)* |  |
| 0x008C | `0x01134670` |  | *(no reads - ack/error only)* |  |
| 0x008D | `0x01130B60` |  | ok:u32, ok:opt u32, ok:opt bytes, ok:opt u32, 2 x ok:opt u16, ok:N x {opt u32, opt u32, opt bytes, opt u32, opt bytes, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u8, opt u32, opt u32, opt u8, opt u32, opt bytes}, ok:opt u32 |  |
| 0x0094 | `0x0111B590` |  | err:u32 |  |
| 0x0097 | `0x0111A770` |  | *(no reads - ack/error only)* |  |
| 0x0098 | `0x0111A640` |  | *(no reads - ack/error only)* |  |
| 0x0099 | `0x01125340` |  | opt u8, opt u32, opt bytes, opt u32, opt bytes |  |
| 0x009A | `0x0111A5E0` |  | *(no reads - ack/error only)* |  |
| 0x009B | `0x0111A550` |  | *(no reads - ack/error only)* |  |
| 0x009C | `0x0111A2C0` |  | *(no reads - ack/error only)* |  |
| 0x009D | `0x0111A340` |  | *(no reads - ack/error only)* |  |
| 0x00A0 | `0x0113A830` |  | ok:u16, 2 x ok:u32, ok:u16, ok:N x {opt u32, opt u32} |  |
| 0x00B1 | `0x0111B5D0` |  | ok:u8 |  |
| 0x00B2 | `0x0111B6D0` |  | *(no reads - ack/error only)* |  |
| 0x00B3 | `0x0111BCB0` |  | *(no reads - ack/error only)* | GUILDNAME_CHANGE |
| 0x00B6 | `0x0112CBB0` |  | ok:u8, 2 x ok:u32, 2 x ok:N x {opt u32}, opt u8 |  |
| 0x00B7 | `0x01126CD0` |  | opt u8, 3 x opt u32 |  |
| 0x00B8 | `0x01126E50` |  | opt u8 |  |
| 0x00B9 | `0x01126FB0` |  | opt u8 |  |
| 0x00BA | `0x0111F970` | ENUM_CMDPACKET_AUCTION_SEARCH_BY_ITEMKEY | ok:u8, ok:u32, ok:u16, ok:N x {opt bytes}, err:opt u8 | [DEBUG] There are exist 0 Stackable item Error! |
| 0x00BC | `0x0111FC90` |  | 2 x ok:u8, ok:N x {opt bytes}, err:opt u8 |  |
| 0x00BD | `0x0111FE70` |  | ok:u8, ok:u16, ok:N x {opt bytes}, err:opt u8 |  |
| 0x00C0 | `0x0111B620` |  | ok:u8 |  |
| 0x00C3 | `0x01119FB0` |  | opt u32, opt u8, N x {opt u32, opt u32, opt u32} |  |
| 0x00C4 | `0x01119F60` |  | *(no reads - ack/error only)* |  |
| 0x00C8 | `0x01124E20` |  | 2 x ok:u32 |  |
| 0x00C9 | `0x0112D1C0` | ENUM_CMDPACKET_USE_EMBLEM | ok:u8, ok:N x {opt u16, opt u32} | NAVATA_EMBLEM_OK; Error %d |
| 0x00CA | `0x01137D70` |  | 2 x opt u16, N x {opt u16, opt u32, opt u32} |  |
| 0x00CC | `0x011274E0` |  | ok:u16, ok:u32, ok:u16, ok:u8, ok:u16 |  |
| 0x00CD | `0x0113EAD0` |  | ok:u8, ok:u16, ok:u32, ok:u16, ok:u8, ok:u16, ok:opt u8 |  |
| 0x00CE | `0x01123450` |  | 2 x ok:u16 | NAVATA_EMBLEM_OK |
| 0x00CF | `0x01127230` |  | ok:u8 |  |
| 0x00D2 | `0x01127700` |  | *(no reads - ack/error only)* |  |
| 0x00D5 | `0x0111A190` |  | ok:u8, ok:N x {opt u32, opt bytes, opt u16, opt u8, opt u8, opt u8, opt u8, opt u8} |  |
| 0x00D6 | `0x01124EE0` |  | ok:u8 |  |
| 0x00D7 | `0x011435F0` |  | opt u16, 5 x opt u32, opt u16, N x {opt u8, opt u32, opt u32, opt bytes, opt u16} |  |
| 0x00DA | `0x0111B730` |  | ok:u8 |  |
| 0x00DB | `0x01127810` |  | 2 x opt u8 |  |
| 0x00DE | `0x01122640` |  | opt u8, opt u16, 3 x opt u8, opt u16, 2 x opt u32, 4 x opt u8, opt u16, 2 x opt u32, opt u8 | FAVOR_LV_UP; FAVOR_LV_DOWN |
| 0x00E0 | `0x0111B6F0` |  | *(no reads - ack/error only)* |  |
| 0x00E3 | `0x0111B790` |  | *(no reads - ack/error only)* |  |
| 0x00E4 | `0x0111B8A0` |  | *(no reads - ack/error only)* |  |
| 0x00E5 | `0x0111BA00` |  | *(no reads - ack/error only)* |  |
| 0x00E6 | `0x01131690` |  | opt u16, 2 x opt u32, opt bytes, 6 x opt u8 |  |
| 0x00E7 | `0x0111BB50` |  | *(no reads - ack/error only)* |  |
| 0x00EA | `0x0111AFC0` |  | *(no reads - ack/error only)* |  |
| 0x00EB | `0x0113A120` |  | ok:u16, ok:u8, ok:opt u8, ok:N x {opt u16, opt u32, opt u32}, 2 x ok:opt u32 |  |
| 0x00EC | `0x0111B010` |  | 2 x ok:u32 | ITEM_REPAIR |
| 0x00ED | `0x0111AF30` |  | ok:u16, ok:u32 |  |
| 0x00EE | `0x011398D0` |  | ok:u8, ok:N x {opt u32, opt u32}, 2 x ok:opt u32, ok:opt u8 |  |
| 0x00EF | `0x0112F0E0` |  | ok:u32, 2 x ok:u8 |  |
| 0x00F0 | `0x011265D0` |  | 3 x ok:u32 | LEVEL_UP |
| 0x00F1 | `0x01131080` |  | ok:u8, 2 x ok:u32 |  |
| 0x00F7 | `0x01127E20` | ENUM_CMDPACKET_GUILD_CARGO_PUSH_ITEM | opt u8, opt u16, opt u32, opt u16 |  |
| 0x00F8 | `0x01128030` | ENUM_CMDPACKET_GUILD_CARGO_POP_ITEM | opt u16, opt u8, opt u16, opt u32 |  |
| 0x00F9 | `0x0111C150` |  | 2 x ok:u16 |  |
| 0x00FB | `0x01127910` |  | ok:u16, ok:u8 |  |
| 0x00FC | `0x0111BEA0` |  | 3 x ok:u32 |  |
| 0x00FE | `0x01127BD0` |  | ok:u16 |  |
| 0x0100 | `0x01135050` |  | opt u8, N x {opt u32, opt u32} |  |
| 0x0103 | `0x0113ADD0` |  | 3 x opt u8, N x {opt u32, opt u32} |  |
| 0x0104 | `0x0111C030` |  | ok:opt u8 |  |
| 0x0105 | `0x0113AFB0` |  | ok:u32, ok:u16, ok:N x {opt bytes} |  |
| 0x0106 | `0x01142020` |  | ok:u32, ok:N x {opt bytes} |  |
| 0x0107 | `0x0111C220` |  | *(no reads - ack/error only)* |  |
| 0x0109 | `0x01128230` |  | *(no reads - ack/error only)* |  |
| 0x010B | `0x0111C260` |  | ok:u8, ok:u16 |  |
| 0x010C | `0x011282F0` |  | opt u8, opt u16, 2 x opt u32 |  |
| 0x010D | `0x0111C340` |  | ok:u8, ok:u16 |  |
| 0x010F | `0x0111A950` |  | ok:u8, ok:opt u8, ok:N x {opt u8, opt u16, opt u32} |  |
| 0x0110 | `0x0111B110` |  | opt u8, opt u16 |  |
| 0x0111 | `0x01120030` |  | u16, u8 |  |
| 0x011C | `0x0111C400` |  | u16, u32 |  |
| 0x011E | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x011F | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0120 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0122 | `0x01119880` |  | *(no reads - ack/error only)* |  |
| 0x0123 | `0x01119950` |  | *(no reads - ack/error only)* |  |
| 0x012F | `0x0111D800` | ENUM_CMDPACKET_BREAK_GUILD | *(no reads - ack/error only)* | Error %d |
| 0x0131 | `0x01128480` |  | *(no reads - ack/error only)* |  |
| 0x0132 | `0x01128570` |  | *(no reads - ack/error only)* |  |
| 0x0133 | `0x0111C440` |  | opt u32 |  |
| 0x0134 | `0x0111C530` |  | opt u32 |  |
| 0x0136 | `0x01128790` |  | ok:u16 |  |
| 0x013B | `0x0111F950` |  | *(no reads - ack/error only)* |  |
| 0x0144 | `0x0111B250` |  | ok:opt u32, ok:opt bytes, ok:u8, ok:u16, 4 x ok:u8 |  |
| 0x0145 | `0x0112C2F0` |  | N x {opt u8, opt u16, opt u32}, opt u16, 2 x opt u32, opt u8, opt u16, opt u8, opt u32, 2 x opt u8, opt u16, opt u32, opt u8, N x {opt u32}, opt u16, opt u8, N x {opt u8, opt u8, opt u8}, 9 x opt u8, opt u16, 6 x opt u8 |  |
| 0x014A | `0x0111C6E0` |  | u32, ok:opt u32, ok:opt bytes |  |
| 0x014B | `0x0111C620` |  | *(no reads - ack/error only)* |  |
| 0x014C | `0x011289F0` |  | *(no reads - ack/error only)* |  |
| 0x014E | `0x01127130` |  | *(no reads - ack/error only)* |  |
| 0x014F | `0x01128A70` |  | 2 x ok:u8 |  |
| 0x0152 | `0x0111F4C0` |  | opt u32, opt u16, opt u32, opt u8 |  |
| 0x0154 | `0x01134660` |  | *(no reads - ack/error only)* |  |
| 0x0155 | `0x01130960` |  | 2 x opt u8, N x {opt u8, opt u8, opt u8, opt u32, opt bytes, opt u32, opt u32, opt u32, opt bytes} |  |
| 0x015A | `0x0111C770` |  | *(no reads - ack/error only)* |  |
| 0x015B | `0x01131950` |  | opt u32, opt bytes, opt u32, opt bytes, 2 x opt u32 |  |
| 0x015C | `0x0111C880` |  | ok:u32, ok:opt u32, ok:opt bytes, ok:opt u32, ok:opt bytes, ok:u32 |  |
| 0x015E | `0x0113B490` |  | ok:u32, err:u32 |  |
| 0x0160 | `0x0113F040` |  | opt u32, N x {opt u32, opt u32, opt bytes, opt u8, opt u8, opt u8, opt u8, opt u32, opt bytes, opt u32} |  |
| 0x0169 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x016A | `0x0111CCA0` |  | *(no reads - ack/error only)* |  |
| 0x016B | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x016C | `0x0111CCD0` |  | *(no reads - ack/error only)* |  |
| 0x0172 | `0x01138EA0` |  | 2 x opt u16, 2 x opt u8, opt u16, opt u8, N x {opt u16, opt u32, opt u32} |  |
| 0x0183 | `0x01134680` | ENUM_CMDPACKET_REQUEST_DUNGEON_PARTY_LIST | N x {opt u8, opt u32} | Test: Observer Packet Recieve Test |
| 0x018B | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x018F | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0190 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0195 | `0x011392E0` |  | opt u16, 3 x opt u32, opt u8, opt u16, opt u32, 2 x opt u8, opt u16, opt u32, opt u8, N x {opt u32}, opt u16, opt u8, N x {opt u8, opt u8, opt u8}, 9 x opt u8, opt u16, 6 x opt u8 |  |
| 0x0198 | `0x0111CCF0` |  | *(no reads - ack/error only)* |  |
| 0x019C | `0x0111CD40` | ENUM_CMDPACKET_TITLE_BOOK_PUT | 6 x opt u32 |  |
| 0x019D | `0x0111CFF0` | ENUM_CMDPACKET_TITLE_BOOK_GET | 6 x opt u32 | %s %s |
| 0x01A0 | `0x0113B810` |  | opt u8, opt u16, opt u8, N x {opt u16, opt u32, opt u32} |  |
| 0x01A1 | `0x0112FC70` |  | u32, 3 x ok:u16 |  |
| 0x01A2 | `0x01128C50` |  | opt u16, opt u8, opt u32, 2 x opt u16 | SP_GET |
| 0x01A9 | `0x0111B480` |  | *(no reads - ack/error only)* |  |
| 0x01AB | `0x01129240` | ENUM_CMDPACKET_REFUND_SKILL | 2 x opt u8, opt u16, opt u8, opt u16, opt u8, opt u32, opt u8 | ERROR : character %x, skillManager %x |
| 0x01AD | `0x0111D380` |  | ok:u16, err:u16 |  |
| 0x01B3 | `0x0111D3E0` |  | err:u8, err:u32, err:u8 |  |
| 0x01B9 | `0x0111D5F0` |  | opt u8, 3 x opt u32, opt u8 |  |
| 0x01BB | `0x0111D610` |  | opt u32, 2 x opt u8 |  |
| 0x01C6 | `0x0111D630` |  | ok:u16 |  |
| 0x01C9 | `0x011295A0` |  | ok:u16, ok:N x {opt u16} |  |
| 0x01D2 | `0x0111D6E0` |  | ok:opt u32, ok:opt bytes, ok:u16, 2 x ok:u8, ok:u32 |  |
| 0x01DC | `0x0111D770` |  | 2 x ok:u32 |  |
| 0x01ED | `0x0111D7D0` |  | *(no reads - ack/error only)* |  |
| 0x01FE | `0x0111B4D0` |  | *(no reads - ack/error only)* |  |
| 0x0208 | `0x0111DAF0` |  | *(no reads - ack/error only)* |  |
| 0x0212 | `0x0111D880` |  | *(no reads - ack/error only)* |  |
| 0x0213 | `0x0111DA20` |  | *(no reads - ack/error only)* |  |
| 0x0214 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0215 | `0x0111DA60` |  | *(no reads - ack/error only)* |  |
| 0x022C | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0234 | `0x011442B0` | ENUM_CMDPACKET_MINORITY_DETAIL_RESULT | opt u8, opt u16, N x {opt u16, opt u16, opt u16, opt u32, opt u8, opt u8} |  |
| 0x0236 | `0x01119590` |  | *(no reads - ack/error only)* |  |
| 0x0237 | `0x01119720` |  | *(no reads - ack/error only)* |  |
| 0x0257 | `0x0111DC50` |  | *(no reads - ack/error only)* |  |
| 0x0258 | `0x01131CF0` |  | opt u8, opt u16, opt u32, opt u8, 2 x opt u32 |  |
| 0x025A | `0x0111DCF0` |  | opt u8, opt u32, opt u8, opt u32 |  |
| 0x025E | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x025F | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0264 | `0x0111DEF0` |  | *(no reads - ack/error only)* | ITEM_REPAIR |
| 0x027A | `0x0111E040` |  | *(no reads - ack/error only)* |  |
| 0x0283 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0287 | `0x0111E050` |  | *(no reads - ack/error only)* |  |
| 0x02CC | `0x0111E090` |  | opt u32, N x {opt u32, opt u32}, opt u32, N x {opt u32, opt u32} |  |
| 0x02E2 | `0x0111C9D0` |  | *(no reads - ack/error only)* |  |
| 0x02E3 | `0x0111CA20` |  | *(no reads - ack/error only)* |  |
| 0x02E4 | `0x0111CA70` |  | *(no reads - ack/error only)* |  |
| 0x02E5 | `0x0111CBA0` |  | *(no reads - ack/error only)* |  |
| 0x02E7 | `0x011322B0` |  | opt u8, N x {opt u32, opt bytes, opt u32, opt bytes} |  |
| 0x02E8 | `0x01132600` |  | 2 x opt u32, opt bytes, opt u16, opt u32, opt bytes, opt u32, opt u8, N x {opt u8} |  |
| 0x02EA | `0x01120600` |  | opt u32, N x {opt bytes} |  |
| 0x02EB | `0x0111E230` |  | *(no reads - ack/error only)* |  |
| 0x02EC | `0x0111E280` |  | *(no reads - ack/error only)* |  |
| 0x02F0 | `0x0111E160` | ENUM_CMDPACKET_SET_USER_AREA_PRE_CHECK | err:u8 | [ERROR] ENUM_CMDPACKET_SET_USER_AREA_PRE_CHECK %d |
| 0x02F1 | `0x0111E2E0` |  | *(no reads - ack/error only)* |  |
| 0x02F2 | `0x0111E530` |  | opt u8, N x {opt u32, opt u32, opt bytes, opt u32, opt bytes, opt u32} |  |
| 0x02F3 | `0x0111E6E0` |  | *(no reads - ack/error only)* |  |
| 0x02F4 | `0x011328C0` |  | opt u8, N x {opt u32, opt u32, opt bytes, opt u32, opt bytes, opt u32} |  |
| 0x02F5 | `0x0111E770` |  | *(no reads - ack/error only)* |  |
| 0x02F6 | `0x0111E900` |  | *(no reads - ack/error only)* |  |
| 0x02F7 | `0x0111E950` |  | *(no reads - ack/error only)* |  |
| 0x02F8 | `0x0111EB00` |  | *(no reads - ack/error only)* |  |
| 0x02F9 | `0x01133180` |  | opt u8, N x {opt u32, opt u32, opt bytes, opt u32, opt bytes} |  |
| 0x02FA | `0x0111EA20` |  | *(no reads - ack/error only)* |  |
| 0x02FB | `0x01132C30` |  | opt u8, N x {opt u32, opt u32, opt bytes, opt u16, opt u32} |  |
| 0x02FC | `0x01132E90` |  | opt u8, N x {opt u32, opt u32, opt bytes, opt u16, opt u32} |  |
| 0x02FD | `0x0111E9B0` |  | *(no reads - ack/error only)* |  |
| 0x0304 | `0x011330F0` |  | ok:u8, ok:opt u32, ok:opt bytes, ok:u32 | ERROR!ERROR! |
| 0x0309 | `0x0111ED10` |  | 2 x opt u8, opt u32, opt u8 |  |
| 0x030A | `0x011334E0` |  | opt u32, opt u8 |  |
| 0x030B | `0x0111ECF0` |  | *(no reads - ack/error only)* |  |
| 0x030C | `0x01133650` |  | 2 x ok:u8, ok:opt u32, ok:opt bytes |  |
| 0x030D | `0x0111EE20` |  | *(no reads - ack/error only)* |  |
| 0x030E | `0x0111EEA0` |  | ok:u8, 3 x ok:opt u8, ok:opt u32, 3 x ok:opt u8, 2 x ok:N x {opt u32} |  |
| 0x030F | `0x0111ECF0` |  | *(no reads - ack/error only)* |  |
| 0x0312 | `0x0111ECA0` |  | *(no reads - ack/error only)* |  |
| 0x0313 | `0x0113C1D0` |  | *(no reads - ack/error only)* | EPIC_MAKE_SUCCESS |
| 0x0315 | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x0317 | `0x011339C0` |  | *(no reads - ack/error only)* |  |
| 0x0318 | `0x0111F0A0` |  | ok:u8, ok:N x {opt u32} |  |
| 0x0319 | `0x01133AC0` |  | *(no reads - ack/error only)* |  |
| 0x031A | `0x01142650` |  | 2 x opt u8 |  |
| 0x031B | `0x01133810` |  | *(no reads - ack/error only)* | %s : %d |
| 0x031F | `0x0111F100` |  | *(no reads - ack/error only)* |  |
| 0x0320 | `0x01134920` |  | opt u8, N x {opt u8, opt u16, opt u8}, opt u16, 2 x opt u32, opt u16 |  |
| 0x0328 | `0x0111F180` |  | ok:bytes |  |
| 0x0329 | `0x01133BE0` | ENUM_CMDPACKET_REQ_REPRESENT_CHARACTER | *(no reads - ack/error only)* |  |
| 0x032A | `0x01133D90` |  | ok:u8, ok:u32, ok:opt u8, ok:N x {opt u8, opt u8}, ok:opt u16, ok:opt u32, ok:opt u8 | FAVOR_NPC_SUCCESS_01; FAVOR_NPC_SUCCESS_02 |
| 0x0332 | `0x0111F1F0` |  | *(no reads - ack/error only)* |  |
| 0x0333 | `0x0111F1F0` |  | *(no reads - ack/error only)* |  |
| 0x0337 | `0x01141D30` |  | opt u8 |  |
| 0x0338 | `0x01134B00` |  | ok:u16, ok:u32, 2 x ok:u8, ok:u16, ok:u8 |  |
| 0x033A | `0x01119E30` | ENUM_CMDPACKET_USE_GEM | *(no reads - ack/error only)* |  |
| 0x033B | `0x0111F340` |  | *(no reads - ack/error only)* |  |
| 0x0340 | `0x01138B60` |  | ok:u32 |  |
| 0x0357 | `0x011341A0` |  | *(no reads - ack/error only)* |  |
| 0x0358 | `0x01138CE0` |  | ok:u32 |  |
| 0x0362 | `0x01129D60` |  | ok:u16 |  |
| 0x0379 | `0x0112F2A0` |  | opt u8, N x {opt u16, opt u32} | NAVATA_EMBLEM_OK |
| 0x037A | `0x01129720` |  | 2 x ok:u16 | NAVATA_EMBLEM_OK |
| 0x03AA | `0x01129940` |  | ok:bytes |  |
| 0x03AB | `0x02231720` | | *(null handler - bare `ret`, registered but ignored)* | |
| 0x03B0 | `0x01125930` |  | opt u8, 2 x opt u32 |  |
| 0x03BA | `0x01131FD0` | ENUM_CMDPACKET_UPGRADE_CARRY_GOLD | 2 x opt bytes | gold_upgrade_exception 1 |
| 0x03DC | `0x01120230` |  | ok:bytes |  |
| 0x0407 | `0x0113F320` |  | opt u8, N x {opt u32, opt bytes, opt u8, opt u8, opt u8, opt u8, opt u32}, 2 x opt u32 | NoName |
| 0x0455 | `0x0113BDE0` |  | opt bytes |  |

### Aliased codes (several codes share one handler)

* `0x0111AE40` <- 0x0054, 0x0058
* `0x0111B480` <- 0x0011, 0x01A9
* `0x0111ECF0` <- 0x030B, 0x030F
* `0x0111F1F0` <- 0x0332, 0x0333
* `0x0111F950` <- 0x005E, 0x013B
* `0x01134670` <- 0x0043, 0x008B, 0x008C

---

## 5. Method

1. `func-source-file.csv` → 11 anchors → TU range, bounded by the neighbouring
   TUs' anchors.
2. `functions.csv` boundaries are useless inside that range (handlers have no
   direct callers, so entry detection failed and merged them). Real entries were
   recovered from the dispatch registry instead.
3. All `call 0x0163C4A0` sites found by scanning the image for `E8 rel32`
   resolving to that target (943 sites); each site decoded backwards over
   `mov ecx,r32` + three `push imm8|imm32|reg`.
4. Per handler, a CFG was built by recursive-descent disassembly from the entry
   (following `jcc`, `jmp`, and indirect `jmp dword ptr [reg*4 + table]` switch
   tables), terminating at `ret`/`int3`. This is what recovers correct function
   extents — e.g. handler `0x01120C30` really spans `0x01120C30`–`0x01121608`,
   not the 348 bytes the naive next-entry heuristic gives.
5. Reads = calls/jmps to the five primitives; loop membership = Tarjan SCC over
   the CFG; optionality = "does a path from the branch root to a `ret` exist that
   avoids this node".
6. String evidence decrypted with the image's literal cipher
   (`0x2CA1` plaintext marker, else seed = `hdr[1] & 0xFE`,
   key = `seed | 0x9A714CA0`, `p = c ^ key; key = key*0x1003F + p`).

## 6. What is NOT established

* Field **meanings** inside each handler are not claimed — only widths and order.
* The `N x {...}` loop bound was hand-verified on `0x011303E0` only; elsewhere it
  is the structural claim "these reads sit on a CFG cycle immediately after a
  scalar read", which is the length-prefixed-array idiom but is not individually
  proven for all ~60 loops.
* The 216 handlers with no `CMDFUNC_` literal have **no name**, only a code.
  I left them unnamed rather than guess; the `0x03B53230` name table that would
  have named them is zero-filled in this dump.
* `read_bytes(dst, len)` shows only as `bytes`; the length argument is a
  register or immediate at the call site and was not resolved per site.
* Ordering is address order, not symbolic execution. A handler that parses
  different layouts on different branches shows all branches' reads merged in
  address order; `ok:` / `err:` / `opt` are the only branch information carried.
* The 17 codes mapped to `0x02231720` are *registered* — the server may send
  them and the client will not complain — but the client does nothing with them.
  Unregistered codes are dropped silently at `0x016376FB`.
