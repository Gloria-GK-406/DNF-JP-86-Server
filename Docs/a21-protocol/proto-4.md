# DNFClient\RDAR\GameCoreNetwork_CMD_Dungeon.cpp — handlers, wire formats, dispatch

Image: A21 memory dump `DNF-memory-rva.bin`, base 0x00400000.
All VAs below verified by disassembling the dump directly (`_p4_raw.py`), not from CSV spans.

---

## 0. Correction to the task brief: the read-primitive VAs are off by one hex digit

The brief gives `0x023C5230 / 0x023C52A0 / 0x023C5310 / 0x023C5380 / 0x023C4350`.
There are **zero** `E8 rel32` calls to any of those five addresses anywhere in the image,
and disassembling them lands mid-instruction (garbage / epilogue tails).

The real primitives are **0x027Cxxxx**:

| VA | signature (cdecl) | body evidence |
|---|---|---|
| `0x027C5230` | `read_u8(void* dst /*, int 1*/)` | `mov cl,[0x3BAAE38]; inc eax; dec [0x3BAAE3C]; mov [edx],cl` |
| `0x027C52A0` | `read_u16(void* dst)` | `mov cx,[eax]; add eax,2; sub [0x3BAAE3C],2` |
| `0x027C5310` | `read_u32(void* dst)` | `mov ecx,[eax]; add eax,4; sub [0x3BAAE3C],4` |
| `0x027C5380` | `read_bytes(void* dst, int len)` | memcpy from cursor, `add edi,esi` |
| `0x027C4350` | `set_remaining(int n)` | `mov [0x3BAAE3C], eax` |
| `0x027C4360` | `remaining_lt(int n)` | `cmp [0x3BAAE3C], arg; setl al` |
| `0x027C4370` | `get_cursor()` | `mov eax,[0x3BAAE38]` |

Globals: **cursor = `0x03BAAE38`**, **remaining = `0x03BAAE3C`**. The stream is a *global*
cursor, not a per-call object — handlers just pull fields in order.

Cross-check: counting distinct owner-functions that directly call the 0x027Cxxxx set gives
**459**, exactly the row count of `parsers.csv`. So `parsers.csv` was built from these five;
the brief's addresses are a transcription slip.

---

## 1. File extent

`func-source-file.csv` anchors: `0x011040A0`, `0x011070B0`, `0x01108360`, `0x0110A760`
(all four carry `D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork_CMD_Dungeon.cpp`,
literal `0x03240BA8`).

Real extent, from the registration function's contents plus literal xrefs:

**`0x011040A0` .. `0x0110C61E`** (Dungeon.cpp), then `GameCoreNetwork_CMD_Etc.cpp` starts at
`0x0110C620`.

`func-source-file.csv` attributes `0x0110C2C0` to `Etc.cpp` — that is a **function-boundary
artifact**: the 6880-byte "function" at 0x0110C2C0 actually contains Dungeon.cpp's registration
routine (0x0110C2C0–0x0110C61E) *followed by* Etc.cpp code (0x0110C620+, which carries the
Etc.cpp `__FILE__`). Proof: the registrar at 0x0110C2C0 registers only handlers ≤ 0x0110C150,
and Etc.cpp's own registrar (0x01118BC0) registers only handlers ≥ 0x0110C670.

The gigantic "functions" in `functions.csv` (0x011040A0 span 12304, 0x01108360 span 7280,
0x0110A760 span 7008) are **merged runs of many small handlers** — the handlers are reachable
only through the dispatch map, so no direct call edge exists and boundary detection glued them
together. Only 26 addresses in the whole range are `E8` call targets.

---

## 2. THE DISPATCH — it is a hash map, not a jump table

### 2.1 Registrar

```
0x0163C4A0   __thiscall Register(this, u32 packetType, void* handler, u32 flag)   ; ret 0xC
             node = malloc(8);  node[0] = handler;  node[1] = flag;
             slot = MapInsert(this, &packetType)   ; 0x0163C2C0
             slot[+4] = node
```

There is **no contiguous array of function pointers** and **no `jmp [reg*4+table]` on the
packet type**. The only enumeration of type -> handler in the binary is the straight-line
sequence of `Register` calls.

### 2.2 Dungeon.cpp's registration function — **`0x0110C2C0`**

`void __thiscall RegisterCmdFunc_Dungeon(this)`, 47 consecutive `Register` calls,
`0x0110C2C0` .. `0x0110C61E` (`ret`).

Call-site stride is **0x10 bytes** when the type fits in imm8 and **0x13 bytes** when it needs
imm32 (`push 0` / `push <handler>` / `push <type>` / `mov ecx,esi` / `call 0x163C4A0`).

Chained from `0x0163C190`:
```
0163C193  call 0x01144AF0     ; core/common registrations
0163C19A  call 0x0110C2C0     ; <-- GameCoreNetwork_CMD_Dungeon.cpp
0163C1A2  jmp  0x01118BC0     ; Etc.cpp + Town.cpp registrations
```
Image-wide there are **941** real `Register` call sites (943 raw `E8` hits, minus 2 generic
wrappers at 0x01637676 / 0x01637736).

### 2.3 Lookup + invoke — **`0x01637680`**

```
01637683  mov  eax,[ebp+8]                 ; frame buffer
01637687  movzx esi, word ptr [eax+1]      ; PACKET TYPE = u16 at frame offset +1
016376ED  push esi ; call 0x0163C410       ; get the singleton map
016376F4  call 0x0163C510                  ; find(type) -> slot
016376FD  mov  ecx,[eax+4]                 ; node[1] = regFlag
01637708  mov  edx,[eax]                   ; node[0] = handler
0163770C  call edx                         ; H(type, a, b, flag)   cdecl, esp += 0x10
```

### 2.4 Frame + payload header (from `CNGameCore::NetworkProc`, `0x0163BBD0`)

```
frame[0]        u8    channel        ; 1 => CMD reply path (this file). 0 => log-only branch.
                                     ;   anything else => "HACK>> Network Packet Type Error [ %d ]"
frame[1..2]     u16   packet type    ; the map key
frame[3..6]     u32   frame length   ; payload length = length - 0x0F
frame[0x0F..]         payload        ; set_remaining(len-0xF) @0163BD15
```

The **dispatcher itself consumes the first 1–2 payload bytes** before calling the handler:

```
0163BF30  read_u8(&result)                       ; payload[0]
0163BF63  if (result == 0)                       ; "Error"
0163BF73      read_u8(&errCode)                  ; payload[1]  -- ONLY on the error path
```
Log literal `0x032CB220` = `"[CMD RECV] %d (Size : %d) Result : %s"`, with `0x032CB1EC`=`"Ok"`,
`0x032CB1D8`=`"Error"`, `0x032CB1FC`=`" ErrCode : %d"`.

**Handler ABI:**
```
void __cdecl Handler(u16 type /*[ebp+8]*/, u8 result /*[ebp+0xC]*/,
                     u8 errCode /*[ebp+0x10]*/, u32 regFlag /*[ebp+0x14]*/)
```
Every wire format below therefore starts **after** `result` (+ `errCode` when result==0).
Nearly all handlers open with `cmp byte ptr [ebp+0xC], 0` and branch — the success and failure
branches read **different** field sets. That is captured per handler.

Packet-name table `0x03B53230[type]` (referenced at `0x0163C059`) is **all zeros in this dump** —
it is populated at runtime, so no `ENUM_CMDPACKET_*` names can be read out of it. Names below
come only from `__FUNCTION__` literals actually pushed inside a handler's own body.

---

## 3. Type -> handler map for this file (43 registrations, 40 distinct handlers)

All from the `0x0110C2C0` registrar unless noted. `flag` is 0 for every one of them.

| type | handler VA | registered at | name evidence |
|---|---|---|---|
| 0x010 | 0x0110C080 | 0x0110C2CC | |
| 0x025 | 0x01105060 | 0x0110C3CE | |
| 0x027 | 0x01104EE0 | 0x0110C2EF | |
| 0x029 | 0x011062E0 | 0x0110C30F | |
| 0x02A | 0x01105CC0 | 0x0110C31F | **CMDFUNC_ENUM_CMDPACKET_GIVEUP_GAME** (lit 0x03240C38 pushed @0x01105CE0; also `<Exit Dungeon - Give Up>` 0x03240C88 @0x01105CCD) |
| 0x02B | 0x01106750 | 0x0110C32F | |
| 0x02D | 0x01105E10 | 0x0110C33F | |
| 0x030 | 0x01106AB0 | 0x0110C34F | |
| 0x03E | 0x011059D0 | **0x01118D7C** (Etc.cpp registrar) | |
| 0x047 | 0x01105250 | 0x0110C3F1 | |
| 0x048 | 0x01106C00 | 0x0110C35F | |
| 0x079 | 0x011051E0 | **0x01118F1C** (Etc.cpp registrar) | |
| 0x0B0 | 0x011059D0 | **0x01118D6C** (Etc.cpp registrar) | |
| 0x128 | 0x01109660 | 0x0110C2DF | **CMDFUNC_ENUM_CMDPACKET_SECRET_SHOP_BUY_ITEM** (lit 0x03241028 @0x01109ADE, 0x01109C50, 0x01109C93, 0x01109CDD) |
| 0x12C | 0x01106050 | 0x0110C372 | |
| 0x13D | 0x011060F0 | 0x0110C385 | |
| 0x1BC | 0x0110C150 | 0x0110C3BE | |
| 0x1CA | 0x01104F70 | 0x0110C398 | |
| 0x1CB | 0x01104FB0 | 0x0110C3AB | |
| 0x1FF | 0x01105220 | 0x0110C3E1 | |
| 0x211 | 0x01109050 | 0x0110C4E8 | respawn/revive result — `[RESPAWN] ACCEPTED - req_uid : %d` (0x03240E28 @0x011090B5), `[RESPAWN] REJECTED - req_uid : %d` (0x03240DD8 @0x01109252) |
| 0x265 | 0x01109E90 | 0x0110C547 | |
| 0x26B | 0x01107110 | 0x0110C4FB | **CMDFUNC_ENUM_CMDPACKET_TIMER_MODIFY_INFO** (lit 0x03240D28 @0x01107192; format `타이머: 조건%d 시간%d` 0x03240D84 @0x01107164) |
| 0x26C | 0x011092F0 | 0x0110C50E | **CMDFUNC_ENUM_CMDPACKET_SUMMON_TIME_LINE** (lit 0x03240E78 @0x01109618; `summon packet error : map error` 0x03240ED0 @0x01109605) |
| 0x26D | 0x011059D0 | 0x0110C521 | |
| 0x2B4 | 0x011081A0 | 0x0110C580 | |
| 0x2B6 | 0x0110A1A0 | 0x0110C593 | |
| 0x2D1 | 0x01105C10 | 0x0110C5B9 | |
| 0x308 | 0x01105C70 | 0x0110C5CC | |
| 0x392 | 0x01108400 | 0x0110C404 | `M_AD_ALTAR_NORMAL` (0x03240DAC @0x01108F25) |
| 0x393 | 0x011053A0 | 0x0110C417 | |
| 0x394 | 0x011054B0 | 0x0110C42A | |
| 0x395 | 0x01105610 | 0x0110C43D | |
| 0x396 | 0x01106F00 | 0x0110C450 | `CHARACTER_ENTRY` (0x030E7CD4 @0x01107040) |
| 0x397 | 0x01105710 | 0x0110C463 | |
| 0x398 | 0x01106160 | 0x0110C476 | **CMDFUNC_ENUM_CMDPACKET_ADVANCE_ALTAR_PAUSE** (lit 0x03240CC8 @0x011061C9, 0x011061E8) |
| 0x399 | 0x01105880 | 0x0110C489 | |
| 0x39A | 0x01105960 | 0x0110C49C | |
| 0x3CC | 0x011059F0 | 0x0110C4AF | |
| 0x408 | 0x01107500 | 0x0110C4C2 **and again** 0x0110C56D (duplicate registration, identical args) | |
| 0x413 | 0x01105B10 | 0x0110C4D5 | |
| 0x4EB | 0x01106220 | 0x0110C618 | |

### Dungeon-owned types wired to the shared no-op `0x02231720`

These are registered by the *same* Dungeon registrar but point at the global stub, so the
client build has no handler for them. They are still Dungeon-namespace type codes:

`0x028` (@0x0110C2FF), `0x270` (@0x0110C534), `0x27C` (@0x0110C55A), `0x2D0` (@0x0110C5A6),
`0x32C` (@0x0110C5F2), `0x32D` (@0x0110C605), `0x4BE` (@0x0110C5DF).

---

## 4. Wire formats

Notation: fields listed in the exact order of the `call 0x027Cxxxx` sites. `read_bytes(N)` is a
single blob read of N bytes (a fixed-size struct copied verbatim). Everything is **after** the
dispatcher-consumed `result` (and `errCode` on the error branch).

### Handlers that read nothing (payload is just result/errCode)

| type | handler | note |
|---|---|---|
| 0x025 | 0x01105060 | acts on `result != 0` only |
| 0x02A | 0x01105CC0 | GIVEUP_GAME |
| 0x02B | 0x01106750 | |
| 0x02D | 0x01105E10 | |
| 0x079 | 0x011051E0 | only fires when `result==0 && errCode==0x5F` |
| 0x12C | 0x01106050 | error-only; switches on errCode 0x13 / 0x01 / ... |
| 0x1FF | 0x01105220 | |
| 0x4EB | 0x01106220 | |

(Verified: every indirect `jmp [reg*4+tbl]` inside the file range was enumerated and its targets
re-scanned — none of them hides an extra read. Tables checked: 0x011055FC, 0x01105868,
0x011066E0, 0x01106704, 0x01106728, 0x01106BD4, 0x01109040, 0x01109E0C, 0x01109E48, 0x01109E6C,
0x0110BF04.)

### Single-field / flat handlers

| type | handler | wire format |
|---|---|---|
| 0x027 | 0x01104EE0 | **error branch only** (`result == 0`): `u16` @0x01104EF9 |
| 0x03E / 0x0B0 / 0x26D | 0x011059D0 | success branch: `u8` @0x011059E3 |
| 0x13D | 0x011060F0 | success branch: `u8` @0x01106103 |
| 0x030 | 0x01106AB0 | success branch: `u8` @0x01106ACB |
| 0x265 | 0x01109E90 | success branch: `u8` @0x01109ED6 |
| 0x029 | 0x011062E0 | success branch: `u16` @0x011062FB, then `u8` @0x0110630A |
| 0x26B | 0x01107110 | success branch: `u8 condition` @0x01107147, `u8 time` @0x01107152 — field names from `타이머: 조건%d 시간%d` |
| 0x408 | 0x01107500 | success branch: `u8` @0x01107575, `u8` @0x01107580, `u8` @0x0110758B |
| 0x2B4 | 0x011081A0 | success branch: `u8` @0x011081EA, `u16` @0x011081F3 (the u16 is a monster/object id — fed to 0x019E2EF0) |
| 0x2D1 | 0x01105C10 | success branch: `u32` @0x01105C20, `u32` @0x01105C29, `u32` @0x01105C32, `u32` @0x01105C3B |
| 0x308 | 0x01105C70 | success branch: `u32` @0x01105C8C, `u32` @0x01105C95 |
| 0x26C | 0x011092F0 | success branch: `u32` @0x01109343, `u32` @0x0110934F, `u32` @0x0110935B (SUMMON_TIME_LINE) |

### Branch-dependent handlers

**type 0x048 — `0x01106C00`**
```
if (result != 0) {  u8 @0x01106C46 ;  u8 @0x01106C51  }
else             {  u8 @0x01106E27 }            ; then tests errCode == 0x85
```

**type 0x2B6 — `0x0110A1A0`**
```
if (result != 0)  u32 @0x0110A1B4              ; pre-set to 0xFFFFFFFF, treated as "none"
else              u8  @0x0110A078  (inside SelectDungeonErrorHandler 0x01109FD0, tail-called with errCode)
```

**type 0x010 — `0x0110C080`**
```
if (result != 0)  (no reads)
else              u8  @0x0110A7DA  (inside SelectDungeonErrorHandler 0x0110A760, called with errCode)
```

**type 0x1BC — `0x0110C150`**
```
if (result == 0)  u8  @0x0110A7DA  (SelectDungeonErrorHandler 0x0110A760); then errCode switch (0x12, 0xEB, ...)
else              u32 @0x0110C223 ;  u32 @0x0110C22C
```

> `0x0110A760` and `0x01109FD0` are the shared **`SelectDungeonErrorHandler`** bodies
> (literal `0x03241210` = `"SelectDungeonErrorHandler"` pushed at 0x0110A8D0 / 0x0110ACA7 /
> 0x0110B868 / 0x0110B8B4). Each reads exactly one trailing `u8` and then runs a 34-case switch
> (jump table `0x0110BF04`) on errCode. Also carries
> `"CRITICAL ERROR : 마지막으로 입장 신청한 던전이 존재하지 않음! %d"` (0x03241148) and
> `"Required Item [%s]: %d"` (0x03241110).

### Length-prefixed / looping handlers

**type 0x1CA — `0x01104F70`** (no result check; reads unconditionally)
```
8 x { u8 }                       ; FIXED count 8 (mov esi,8 @0x01104F8B; dec esi/jne @0x01104F9F)
                                 ; read_u8 @0x01104F96
```

**type 0x1CB — `0x01104FB0`** (success branch only)
```
u8  n1                           @0x01104FCB
n1 x { u8 }                      @0x01104FE6      ; length-prefixed by n1 (cmp esi,eax; jl @0x0110500E)
u8  n2                           @0x01105016
n2 x { u8 }                      @0x0110502C      ; length-prefixed by n2 (jl @0x01105054)
```

**type 0x047 — `0x01105250`** (no result check)
```
8 x {                            ; FIXED outer count 8  (inc esi; cmp esi,8; jl 0x01105270 @0x0110538F)
   u8  a                         @0x01105276
   u8  b                         @0x0110529B      ; category/slot, later compared <= 7
   u8  n                         @0x011052A6      ; element count for the inner array
   n x { u32 x @0x011052DB ; u32 y @0x011052E4 }  ; length-prefixed by n (jl 0x011052D7 @0x01105342)
   u8  flag                      @0x01105365
}
```

**type 0x211 — `0x01109050`** (respawn / revive result)
```
if (result != 0) {
   u32 req_uid                   @0x0110908C      ; "[RESPAWN] ACCEPTED - req_uid : %d"
   u8  n                         @0x0110912B
   n x {                                          ; length-prefixed by n (cmp esi,edx; jl 0x01109170 @0x011091BC)
        u16                      @0x01109184
        u32                      @0x0110918D
        u8                       @0x01109198
        u16                      @0x011091A1
   }
} else {
   u32 req_uid                   @0x0110922D      ; "[RESPAWN] REJECTED - req_uid : %d"
}
```

**type 0x128 — `0x01109660`** (SECRET_SHOP_BUY_ITEM)
```
if (result != 0) {
   u32                           @0x011096D2
   u16                           @0x011096DE
   u32                           @0x011096EA      ; item id -> 0x00EE6D10 lookup
   u32                           @0x011096F6
   u8                            @0x01109704
   u16                           @0x01109710
   u32                           @0x0110972C      ; pre-set to 0xFFFFFFFF ("none" sentinel)
   u32                           @0x01109738
   ... item/inventory work, switches (tables 0x01109E0C / 0x01109E48 / 0x01109E6C) ...
   u32                           @0x01109A14      ; trailing field, all switch cases converge here
} else {
   (no reads) — switch on errCode:
      0x04 -> msg 0x2EA
      0x0A -> msg 0x2EB
      0x11 -> "존재하지 않는 아이템 인덱스(혹은 팔지 않고 있는 물건을 구입하려고 함)" (0x03240FC8)
      also: "구입 불가 상태 (게임중일 때, 트레이드 중일 때 등)" (0x03240F80),
            "자신이 개인상점을 개설중으로 명령을 처리할수 없다." (0x03240F38),
            "구매 갯수 초과" (0x03240F18)
}
```
> Caveat: an early bail at `0x01109758 je 0x01109DEE` returns before reaching the trailing
> `u32` when the item lookup fails. The field is still on the wire on the accepted path;
> the client just stops draining. Flagging it rather than asserting it is always consumed.

**type 0x392 — `0x01108400`** (the big one; `M_AD_ALTAR_NORMAL`)
```
if (result == 0) return;
read_bytes(4)  -> u32 mode          @0x0110844D
switch (mode) {
  case 0: goto LIST                  ; 0x0110858B
  case 1..4: message only, no reads  ; jump table 0x01109040 -> 0x01108471/0x011084AE/0x011084EB/0x01108528
  default:  message only             ; 0x01108FF1
}
LIST:
u32 outerCount                       @0x01108592
outerCount x {                       ; outer loop back-edge 0x01108F15 -> 0x011085B0
    read_bytes(0x21)   record        @0x0110862B     ; 33-byte fixed record
    u32 n1                           @0x01108685
    n1 x read_bytes(0x08)            @0x011086A5     ; loop 0x011087AD -> 0x01108696
    u32 n2                           @0x011087B7
    n2 x read_bytes(0x0C)            @0x011087E6     ; loop 0x01108956 -> 0x011087D0
    u32 n3                           @0x01108960
    n3 x read_bytes(0x0C)            @0x0110898F     ; loop 0x01108AFD -> 0x01108979
    u32 n4                           @0x01108B07
    n4 x read_bytes(0x0C)            @0x01108B36     ; loop 0x01108CA4 -> 0x01108B20
    u32 n5                           @0x01108CAE
    n5 x read_bytes(0x0A)            @0x01108CDC     ; loop 0x01108CFA -> 0x01108CC0
    u32 n6                           @0x01108D00
    n6 x read_bytes(0x0A)            @0x01108D2C     ; loop 0x01108D47 -> 0x01108D10
    u32 n7                           @0x01108D4D
    n7 x read_bytes(0x05)            @0x01108D6F     ; loop 0x01108EB2 -> 0x01108D61
}
```
Every one of the seven inner arrays is **length-prefixed by the immediately preceding `u32`**
(each loop's exit test is `cmp <idx>, [ebp+0xC]` where `[ebp+0xC]` holds the just-read count).
The in-range calls at 0x0110843C / 0x011085E2 / 0x01108EF4 / 0x01108FF7 (to 0x01107C70,
0x01107A20, 0x011080B0, 0x01107F60) are container ctor/dtor/copy helpers — verified to contain
no stream reads.

### The 0x392–0x39A "fixed struct" family

Every handler in this block does one `read_bytes(N)` into a zeroed local whose **first dword is a
sub-result code** (0 = OK; 1..4 select an error message id), then branches on it.

| type | handler | read | sub-structure evidence |
|---|---|---|---|
| 0x393 | 0x011053A0 | `read_bytes(0x18)` @0x011053C9 | first `u32` switched 0/1/2/3 @0x011053E5 |
| 0x394 | 0x011054B0 | `read_bytes(0x12)` @0x011054E4 | first `u32` switched @0x011054E9 (jump table 0x011055FC); tail field is a `u16` (`mov word [ebp-8],0`) |
| 0x395 | 0x01105610 | `read_bytes(0x10)` @0x01105634 | first `u32` switched 0/1/2/3 @0x01105639 |
| 0x396 | 0x01106F00 | `read_bytes(0x0D)` @0x01106F49 | zero-init shows `u32,u32,u32,u8` layout; `CHARACTER_ENTRY` |
| 0x397 | 0x01105710 | `read_bytes(0x1A)` @0x01105734 | jump table 0x01105868; tail field `u16` |
| 0x398 | 0x01106160 | `read_bytes(0x05)` @0x01106174 | **read unconditionally, before the result check**; layout `u32 + u8`, the `u8` is consumed at 0x011061A2. ADVANCE_ALTAR_PAUSE |
| 0x399 | 0x01105880 | `read_bytes(0x08)` @0x0110589F | first `u32` tested @0x011058A4 |
| 0x39A | 0x01105960 | `read_bytes(0x08)` @0x0110597B | first `u32` tested @0x01105980 |
| 0x3CC | 0x011059F0 | `read_bytes(0x08)` @0x01105A0F | `cmp dword [ebp-0x10],0` @0x01105A17 |
| 0x413 | 0x01105B10 | `read_bytes(0x0C)` @0x01105B36 | `cmp dword [ebp-8],0` @0x01105B3E — second dword is the code |

---

## 5. Things deliberately left unknown

* **Symbolic `ENUM_CMDPACKET_*` names for 35 of the 43 types.** The runtime name table
  `0x03B53230[type]` is entirely zero in this dump, and only 6 handlers push their own
  `__FUNCTION__` literal. I am not guessing the rest — a wrong name leads to a wrong type code.
* **Semantic field names.** Only 0x26B (condition/time) and 0x211 (req_uid) have literal-backed
  field names. Everything else is width-and-order only.
* **Sub-field breakdown inside `read_bytes(N)` blobs** beyond what the zero-init / first-field
  tests prove. The blobs are memcpy'd whole; the client never decomposes most of them in the
  handler itself.
* The `regFlag` 4th handler argument is 0 for every registration in this file, so its meaning
  is not observable here.

## 6. Reproduction scripts (in SCRATCH)

`_p4_raw.py` (raw disasm at any VA), `_p4_reg.py` (extract all 941 registrations →
`_p4_reg.txt`), `_p4_seq2.py` / `_p4_seq3.py` (flow-following read-sequence + loop extractor),
`_p4_direct.py` (owner → direct read-primitive calls), `_p4_ind.py` + `_p4_tbl.py` (indirect
jump tables), `_p4_lits.py` + `_p4_dec.py` (precise literal xrefs with decryption).
