# DNFClient\RDAR\GameCoreNetwork_CMD_Etc.cpp — handlers, wire formats, dispatch

Image: DNF S4A21 memory dump, base 0x00400000. All VAs are image VAs.

---

## 0. Correction to the tooling constants (affects everyone using parsers.csv)

The read primitives handed out in the task brief are wrong by 0x40000. The real
ones, verified by disassembly:

| brief said | actual | body |
|---|---|---|
| 0x023C5230 | **0x027C5230** | `read_u8(void* dst)`  — `mov cl,[g_cur]; inc g_cur; dec g_rem; *dst=cl` |
| 0x023C52A0 | **0x027C52A0** | `read_u16(void* dst)` — `mov cx,[g_cur]; g_cur+=2; g_rem-=2` |
| 0x023C5310 | **0x027C5310** | `read_u32(void* dst)` — `mov ecx,[g_cur]; g_cur+=4; g_rem-=4` |
| 0x023C5380 | **0x027C5380** | `read_bytes(void* dst, u32 n)` — memcpy from cursor, advance n |
| 0x023C4350 | **0x027C4350** | `set_remaining(u32 n)` — `g_rem = n` |

Cursor globals: **`g_cursor = 0x03BAAE38`**, **`g_remaining = 0x03BAAE3C`**
(read_u8 @0x027C5233 / @0x027C523E). The cursor is a *process global*, not a
per-call stream object, so **the textual order of read calls is the wire
order**.

Sanity check: a whole-image scan for `E8 rel32 -> {those five}` yields 4753
call sites in exactly **459** functions.csv entries — identical to the row
count of parsers.csv. So those are the addresses parsers.csv was built from.
The 0x023Cxxxx addresses land mid-instruction inside an unrelated factory
switch near 0x023BD910 and read nothing (a raw scan of the whole Etc.cpp range
for calls to them returns 0 hits).

---

## 1. Which functions are this file's

func-source-file.csv anchors for the path:

```
0x0110C2C0 0x0110DE10 0x01110B50 0x01111040 0x01112730 0x01112E30 0x011152C0 0x01118180
```

Anchors are span-derived (a `__FILE__` reference anywhere inside a
functions.csv entry tags the whole entry) and functions.csv entries here are
merged runs of many real functions — e.g. entry 0x0110C2C0 spans 6880 bytes and
contains at least 4 distinct functions. Precise membership comes from the
file's own registrar instead (§2): **CMD_Etc.cpp owns 0x0110C670 .. 0x01118BC0**.

One anchor must be re-attributed:

* **0x0110C2C0 is not CMD_Etc.cpp.** It is a registrar (ends 0x0110C61E) whose
  47 registrations all point at handlers in **0x01104EE0..0x0110C150** — the
  body of `GameCoreNetwork_CMD_Dungeon.cpp` (anchors 0x011040A0..0x0110A760).
  The CMD_Etc handler `CHECK_DOUBLE_CHARACTER_NAME` starts at 0x0110C670, and
  its log call at 0x0110C74D is what dragged the `...CMD_Etc.cpp` literal
  (0x03241290) onto the 0x0110C2C0 entry.
  Confidence: high (locality of all 47 targets) but this is an inference, not a
  literal.

The file's own registrar is **0x01118BC0** (inside functions.csv entry
0x01118180). It registers 85 types; 73 of its targets sit inside
0x0110C670..0x01118910.

---

## 2. THE DISPATCH

Not a jump table and not a function-pointer array. It is a
**`std::map<u32 type, {handler_fn, extra}>`** built at startup.

### 2.1 Singleton holding the map

`0x0163C410` — MSVC local-static accessor.
```
0163C437  test byte [0x03A5BE6C], al     ; init-once flag
0163C445  push 0x1C                      ; operator new(28)
0163C45C  call 0x0163C3A0                ; ctor
0163C461  mov [0x03A5BE68], eax          ; <-- THE CMD MAP INSTANCE
```
* **CMD map instance pointer: global `0x03A5BE68`** (init flag `0x03A5BE6C`).
* The NOTI side is the twin accessor `0x0163C550`; its registrars (0x01187FF0 /
  0x0115F540 / 0x0119A780) are reached via 0x0163C4F0. Different map, out of
  scope here.

### 2.2 Insert primitive

`0x0163C4A0` — `__stdcall Register(u32 key, void* fn, u32 extra)`, `this` = map.
```
0163C4A5  push 8                 ; malloc(8)
0163C4BB  mov [eax],   ecx       ; +0 = fn     (arg2, [ebp+0Ch])
0163C4BD  mov [eax+4], edx       ; +4 = extra  (arg3, [ebp+10h])
0163C4C8  call 0x0163C2C0        ; map insert, key = [ebp+8]
0163C4CE  mov [eax+4], esi       ; node->value = the 8-byte record
```
Every registration site is `push extra; push fn; push key; mov ecx,this; call 0x0163C4A0`.
Whole-image extraction of that pattern gives **941 registrations across 6
registrars**, one per GameCoreNetwork_*.cpp — counts match the callgraph edge
counts to 0x0163C4A0 exactly (47+85+269+156+228+156 = 941):

| registrar | n | file (by target locality) |
|---|---|---|
| 0x0110C2C0 | 47 | CMD_Dungeon.cpp |
| **0x01118BC0** | **85** | **CMD_Etc.cpp — this file** |
| 0x01144AF0 | 269 | CMD_Town.cpp |
| 0x0115F540 | 156 | NOTI_Dungeon.cpp |
| 0x01187FF0 | 228 | NOTI_Etc.cpp |
| 0x0119A780 | 156 | NOTI_Town.cpp |

The three CMD registrars are chained from `0x0163C190`:
`call 0x01144AF0; call 0x0110C2C0; jmp 0x01118BC0` — all three insert into the
same `0x03A5BE68` map, so **the CMD type space is flat and shared across
Town/Dungeon/Etc**. (0x0163C190 itself is called from 0x0163A850 at 0x0163ABDF.)

### 2.3 Lookup + indirect call

`0x01637680` — `__stdcall Dispatch(u8* pkt, u8 bSuccess, u8 errorCode)`
```
01637687  movzx esi, word [eax+1]      ; esi = packet type  (u16 at pkt+1)
016376E4  call 0x02231720              ; probe; that function is a bare `ret`
016376ED  call 0x0163C410              ; map singleton
016376F4  call 0x0163C510              ; find(type) -> record*, 0 if absent
016376F9  test eax,eax ; je out        ; unknown type -> silently ignored
016376FD  mov  ecx,[eax+4]             ; extra
01637700  mov  edx,[ebp+10h]           ; errorCode
01637704  mov  ecx,[ebp+0Ch]           ; bSuccess
0163770C  call [eax]                   ; fn(type, bSuccess, errorCode, extra)
```
`0x0163C510` is the find: `if (this->size==0) return 0; ... call 0x02B1E410
(rb-tree lookup); return node->value`.

**Handler prototype (all 85):**
```c
void __cdecl handler(int type      /*[ebp+08h]*/,
                     u8  bSuccess  /*[ebp+0Ch]*/,
                     u8  errorCode /*[ebp+10h]*/,
                     int extra     /*[ebp+14h]*/);   // extra == 0 for all 85
```
Nearly every handler opens with `cmp byte [ebp+0Ch],0 / je out` — **the body is
parsed only when bSuccess != 0**; the failure path consumes nothing from the
stream and switches on `errorCode` instead (e.g. 0x0110C670 at 0x0110C70D:
`movzx eax, byte [ebp+10h]; add eax,-2; cmp eax,9Dh; ja default;
jmp [eax*4+0x0110C794]` — a 0x9E-entry byte-index + jump table, but that is an
*error-code* table, not a packet-type table).

### 2.4 Frame layout feeding the dispatch

Caller `0x0163BBD0` (entry is VMProtect-stubbed at +0; body readable from
0x0163BBD5). Header, `edi` = packet buffer:

| off | width | meaning | evidence |
|---|---|---|---|
| +0 | u8 | frame kind. `1` = CMD frame -> this dispatch; `0` = plain text/log frame | 0163BECC `movzx ecx,byte [edi]`, then `sub eax,0` / `dec eax` two-way |
| +1 | u16 | **packet type (ENUM_CMDPACKET_*)** | 0163BC35 / 0163BD63 / 0163C055 `movzx ..,word [edi+1]` |
| +3 | u32 | total length; body length = value − 0x0F | 0163BCF9 `mov eax,[edi+3]; sub eax,0Fh` |
| +0Fh | — | payload start | 0163BD72 `lea eax,[edi+0Fh]` |

Then on the kind==1 path (0x0163BF26):
```
set_remaining(bodyLen)          ; 0x0163BD15 / 0x0163BEB2  -> g_remaining
read_u8  -> bSuccess            ; 0x0163BF30
if (bSuccess == 0)              ; 0163BF63 cmp / 0163BF6D jne skips the read
    read_u8 -> errorCode        ; 0x0163BF73
Dispatch(pkt, bSuccess, errorCode)   ; 0x0163C078
```
So **the two leading payload bytes are frame-level, not handler-level**: byte0
always present; byte1 (errorCode) present **only when byte0 == 0**. Every wire
format in §4 begins *after* those.

Also at 0x0163C059: `mov eax,[edx*4 + 0x03B53230]` with `edx` = packet type — a
**type -> name-pointer array at 0x03B53230, stride 4, in .data**. In this dump
the whole array (indices 0..0x5FF) is zero, i.e. populated at runtime. Recorded
for anyone who can dump it live; **not usable statically**.

---

## 3. Type -> handler map registered by 0x01118BC0 (all 85 rows)

`extra` is 0 for every row. `0x02231720` is a bare `ret` — a deliberate
"accept and discard" stub. Names in the last column are the handler's **own**
`CMDFUNC_ENUM_CMDPACKET_*` log literal, pushed inside that handler's body —
these 18 are self-identifying and independently confirm the type mapping.

| type | dec | handler | note | self-reported name |
|---|---|---|---|---|
| 0x001 | 1 | 0x0110F1F0 | | CMDFUNC_ENUM_CMDPACKET_LOGIN |
| 0x002 | 2 | 0x0110CB00 | | |
| 0x003 | 3 | 0x0110CAA0 | | CMDFUNC_ENUM_CMDPACKET_EXIT |
| 0x004 | 4 | 0x01111070 | | CMDFUNC_ENUM_CMDPACKET_SELECT_CHARACTER |
| 0x005 | 5 | 0x01112F10 | | CMDFUNC_ENUM_CMDPACKET_CREATE_CHARACTER |
| 0x006 | 6 | 0x0110E720 | | CMDFUNC_ENUM_CMDPACKET_DELETE_CHARACTER |
| 0x007 | 7 | 0x01118280 | | |
| 0x00F | 15 | 0x01118420 | | |
| 0x035 | 53 | 0x0110CCE0 | | |
| 0x03E | 62 | 0x011059D0 | target outside file | |
| 0x045 | 69 | 0x0110E410 | | |
| 0x046 | 70 | 0x01110C30 | | CMDFUNC_ENUM_CMDPACKET_CARD_SELECT_RIGHT_STATE |
| 0x04B | 75 | 0x0110EDE0 | | |
| 0x06B | 107 | 0x02231720 | stub (ret) | |
| 0x06D | 109 | 0x0110CEB0 | | |
| 0x06F | 111 | 0x0110C850 | | |
| 0x079 | 121 | 0x011051E0 | target outside file | |
| 0x07D | 125 | 0x0110CE30 | | |
| 0x087 | 135 | 0x0110C870 | | |
| 0x088 | 136 | 0x01113650 | | |
| 0x089 | 137 | 0x0110EA50 | | |
| 0x08B | 139 | 0x01134670 | target outside file | |
| 0x08E | 142 | 0x0110CB40 | | |
| 0x0A1 | 161 | 0x01110B40 | 9-byte thunk -> 0x0110EB00 | |
| 0x0A2 | 162 | 0x0110CC80 | | |
| 0x0A3 | 163 | 0x01112120 | | |
| 0x0A4 | 164 | 0x01112310 | | |
| 0x0A5 | 165 | 0x0110CB60 | | |
| 0x0B0 | 176 | 0x011059D0 | target outside file (same as 0x03E) | |
| 0x0C1 | 193 | 0x02231720 | stub (ret) | |
| 0x0C7 | 199 | 0x0110EB00 | shared with 0x0A1; branches on `type` at 0x0110EB84 | |
| 0x0D0 | 208 | 0x011156C0 | | CMDFUNC_ENUM_CMDPACKET_USE_RANDOMBOX_ITEM |
| 0x0DF | 223 | 0x02231720 | stub (ret) | |
| 0x124 | 292 | 0x02231720 | stub (ret) | |
| 0x127 | 295 | 0x0110D3B0 | | |
| 0x138 | 312 | 0x0110C9C0 | | |
| 0x139 | 313 | 0x0110CA20 | | |
| 0x175 | 373 | 0x0110CE80 | | |
| 0x18D | 397 | 0x0110D200 | | CMDFUNC_ENUM_CMDPACKET_EQUIPSLOT_SWITCH |
| 0x18E | 398 | 0x0110D100 | | |
| 0x1AE | 430 | 0x02231720 | stub (ret) | |
| 0x1BA | 442 | 0x0110CA80 | 0x15-byte thunk -> 0x01585590 | |
| 0x25B | 603 | 0x00EE5940 | target outside file | |
| 0x271 | 625 | 0x0110CD90 | | |
| 0x273 | 627 | 0x011127B0 | | CMDFUNC_ENUM_CMDPACKET_SELECT_FIGHT_DOLL |
| 0x274 | 628 | 0x0110CD40 | | |
| 0x27D | 637 | 0x0110CDF0 | | |
| 0x280 | 640 | 0x0110D250 | | CMDFUNC_ENUM_CMDPACKET_RETURN_USER_REWARD_REQUEST |
| 0x286 | 646 | 0x011138A0 | | |
| 0x292 | 658 | 0x0110D300 | | |
| 0x293 | 659 | 0x02231720 | stub (ret) | |
| 0x294 | 660 | 0x02231720 | stub (ret) | |
| 0x295 | 661 | 0x011144D0 | | |
| 0x299 | 665 | 0x01113AC0 | | |
| 0x29A | 666 | 0x01114150 | | |
| 0x29B | 667 | 0x01113D90 | | |
| 0x29C | 668 | 0x011143B0 | | |
| 0x29D | 669 | 0x01113F90 | | |
| 0x29E | 670 | 0x02231720 | stub (ret) | |
| 0x2A8 | 680 | 0x0110D3F0 | | |
| 0x2B2 | 690 | 0x01114F60 | | |
| 0x2B5 | 693 | 0x0110C670 | | CMDFUNC_ENUM_CMDPACKET_CHECK_DOUBLE_CHARACTER_NAME |
| 0x2BC | 700 | 0x0110D410 | | |
| 0x2C1 | 705 | 0x0110D580 | | CMDFUNC_ENUM_CMDPACKET_CONTENTS_PLAY_INFO |
| 0x2CA | 714 | 0x0110E510 | | |
| 0x2CB | 715 | 0x0110E570 | | |
| 0x2E9 | 745 | 0x01114630 | | |
| 0x34E | 846 | 0x0110DB70 | | |
| 0x351 | 849 | 0x0110E6D0 | | |
| 0x35A | 858 | 0x0110DBE0 | | |
| 0x35C | 860 | 0x0110DC20 | | CMDFUNC_ENUM_CMDPACKET_SYNC_ITEM_SPACE |
| 0x35E | 862 | 0x0110D890 | | |
| 0x35F | 863 | 0x0110D6A0 | | CMDFUNC_ENUM_CMDPACKET_CHECK_PVP_TOTAL_MATCH_TEAM_NAME |
| 0x360 | 864 | 0x0110DAE0 | | |
| 0x364 | 868 | 0x0110DD50 | | |
| 0x36D | 877 | 0x0110E370 | | CMDFUNC_ENUM_CMDPACKET_EVENT_DNFTREND_GET_REWARD |
| 0x36F | 879 | 0x0110DF80 | | CMDFUNC_ENUM_CMDPACKET_PREMIUM_SERVICE |
| 0x371 | 881 | 0x02231720 | stub (ret) | |
| 0x372 | 882 | 0x0110E1C0 | | |
| 0x39D | 925 | 0x01118910 | | CMDFUNC_ENUM_CMDPACKET_REQ_IGA_VER |
| 0x39E | 926 | 0x02231720 | stub (ret) | |
| 0x39F | 927 | 0x0110EEE0 | | |
| 0x3DD | 989 | 0x0110CFB0 | | |
| 0x450 | 1104 | 0x011170C0 | | CMDFUNC_ENUM_CMDPACKET_USE_RANDOMBOX_ITEM_EXPAND |
| 0x4C6 | 1222 | 0x0110E5D0 | | |

Registration sites for each row are in `_p5_regs.csv` (column `site`).

---

## 4. Wire formats

Convention: everything below is the payload **after** the frame-level
`bSuccess` (and, when bSuccess==0, `errorCode`) bytes described in §2.4, and
applies to the `bSuccess != 0` path unless stated. `L{n}` marks a loop bounded
by a previously read field. Addresses in parentheses are the read call sites.

The 46 registered handlers not listed here perform **zero** stream reads — they
act only on `type` / `bSuccess` / `errorCode` (0x2B5, 0x3, 0x18D, 0x280, 0x35F,
0x36D, 0x2, 0x6F, 0x87, 0x138, 0x139, 0x35, 0x7D, 0x175, 0x127, 0x2A8, 0x89,
0xA3, 0xC7/0xA1, 0x45, 0x35A, 0x360, 0x29C, 0x295, 0x7, ... plus the 10 stubs).

### Simple / fixed-layout

| type | handler | wire format |
|---|---|---|
| 0x271 | 0x0110CD90 | `u32` (0110CDA4) |
| 0x274 | 0x0110CD40 | `u32`, `u32` (0110CD55, 0110CD65) |
| 0x27D | 0x0110CDF0 | `u8` (0110CE03) |
| 0x18E | 0x0110D100 | `u8` mode; then value-checked `1..2` (0110D117) |
| 0x3DD | 0x0110CFB0 | `bytes[4]` read as one u32 result code; then `switch(v-1)` over 5 cases via jump table at 0x0110D0E0 (0110CFC6) |
| 0x2BC | 0x0110D410 | `u32`, `u32` (0110D452, 0110D45B) |
| 0x2C1 | 0x0110D580 | `u8`, `u8`, `u32`, `u32`, `u32`, `u32`, `u32`, `u32` — the six u32 are the `[DAILY CONTENTS] despair/layer/dimension/mirror/darkelf` counters + 1 (0110D5A2..0110D5E3) |
| 0x34E | 0x0110DB70 | `bytes[0xA4]` — one fixed 164-byte blob straight into a stack struct (0110DBAB) |
| 0x35C | 0x0110DC20 | `u8` flags; bit test `& 8` at 0110DC97 (0110DC8F) |
| 0x364 | 0x0110DD50 | `u32` (0110DD64) |
| 0x372 | 0x0110E1C0 | `bytes[7]` (0110E1E2) |
| 0x4C6 | 0x0110E5D0 | `bytes[8]` (two u32; first one tested at 0110E5F4) (0110E5EF) |
| 0x351 | 0x0110E6D0 | `bytes[0x13]` (19 bytes) (0110E703) |
| 0x6 (DELETE_CHARACTER) | 0x0110E720 | `u8`, `u16` (0110E75E, 0110E78C) |
| 0x4B | 0x0110EDE0 | `u16`, `u32` (0110EE04, 0110EE10) |
| 0x39F | 0x0110EEE0 | `bytes[0x10]` (16 bytes; dword at +8 used at 0110EF1B) (0110EF16) |
| 0x46 (CARD_SELECT_RIGHT_STATE) | 0x01110C30 | **L8**: 8 × `u16` (fixed count 8, `cmp esi,8` at 0x01110D77) (0x01110D26) |
| 0xA4 | 0x01112310 | `u8` — only on one branch (0x01112516) |
| 0x273 (SELECT_FIGHT_DOLL) | 0x011127B0 | `u32`, `u16` (011127FC, 01112808) |
| 0x5 (CREATE_CHARACTER) | 0x01112F10 | `u16` (01112F57) |
| 0x299 | 0x01113AC0 | `u32` (01113AF8) |
| 0x29A | 0x01114150 | `u8` (0111418E) |
| 0x2E9 | 0x01114630 | `u16` (01114676) |
| 0xF | 0x01118420 | `u8` — only when `type==0x16` per `cmp al,16h` at 0x01118489 on arg3 (011184A1) |

### Length-prefixed arrays

**0x6D — handler 0x0110CEB0**
```
u8   count                       (0110CEDE)
count x {                        loop 0110CF10..0110CF94, `cmp esi,count; jl`
   u32  a                        (0110CF17)
   u32  b                        (0110CF23)
   u16  c                        (0110CF2F)
}
```
The per-element string passed to the consumer at 0110CF85 is a *constant*
(literal 0x03241400 `"bErrorCode : get guild name from recv buffer"` formatted
into a 0x200-wide buffer) — **not read from the stream**.

**0x292 — handler 0x0110D300**
```
u8   flag                        (0110D366)
5 x  u32                         fixed 5 iterations, `cmp esi,5; jl` @0110D395  (0110D374)
```

**0x35E — handler 0x0110D890**
```
3 x u8   (into [ebp+0Ch..0Eh])   fixed 3 iterations, `cmp esi,3; jl` @0110D8C4  (0110D8BB)
```

**0x286 — handler 0x011138A0**
```
u8   count                       (011138E6)
count x {                        loop 01113900..01113A17, `cmp cl,al; jb`
   u16  a                        (01113915)
   u8   b                        (01113960)
   u8   c                        (0111396E)
}
```
(the `call 0x01112E30` at 01113906 inside the loop is a struct ctor — it reads nothing)

**0x88 — handler 0x01113650**
```
u16  a          (011136CB)
u8   b          (011136D6)
u8   c          (011136E1)
u8   d          (011136EC)
u8   e          (011136F7)
u8   f          (0111372B)
u8   g          (01113739)
```

**0x29B — handler 0x01113D90** — two mutually exclusive branches keyed on
`errorCode` (`al`), each a u32-counted u32 array:
```
if errorCode == 0x11:   u32 n (01113EA3); L{n}: u32   (01113EB9)
if errorCode == 0xD9:   u32 n (01113EEA); L{n}: u32   (01113F07)
otherwise:              nothing
```

**0x2B2 — handler 0x01114F60**
```
u32  magic       (01114F9D)   ; must equal 0xDE, else jumps to 0x01115141
...  (no further reads on the success path inside this function)
u8               (01115223)   ; only on the bSuccess==0 path (`cmp byte [ebp+0Ch],bl; jne`)
```

**0x39D (REQ_IGA_VER) — handler 0x01118910**
```
bytes[0x0A]      (01118960)   ; 10-byte header; dword at +6 ([ebp-456h]) = count
L{count} x u32   (01118A6B)   ; loop 01118A64..01118B24
```

**0x1BA — handler 0x0110CA80** — 0x15-byte thunk, tail-calls `0x01585590`:
```
u8   count       (015855EC)
L{count} x {                  loop 01585623..01585833
   u32  a        (0158562A)
   u8   b        (01585666)
   u32  c        (01585672)
   u8   d        (01585680)
   u8   e        (0158568E)
   u8   f        (0158569C)
}
```

### 0x36F — PREMIUM_SERVICE, handler 0x0110DF80
```
u16  serviceType                 (0110DFA6)
switch (serviceType):
   case 4 (`dec eax; sub eax,3` path):  bytes[0x4A]   (0110E00A)
   case 1 (`dec eax; je 0110E05A`):     bytes[0x4A]   (0110E06D)
   default: nothing
```
and on the `bSuccess == 0` path (label 0x0110E156): a single `u8` (0110E160).

### 0x2CA / 0x2CB — handlers 0x0110E510 / 0x0110E570 (identical shape)
```
if bSuccess != 0:   u8 slot (0110E52B / 0110E588), u16 id (0110E534 / 0110E591)
else:               u8       (0110E55E / 0110E5BB)
```
Then `0x01648BA0(slot, id)` looks the entity up and sets/clears `+0x27E`.

### 0x1 — LOGIN, handler 0x0110F1F0 (0x1950 bytes, heavily branched)

29 read sites. Listed in **address order**; the function has many
early-outs and `switch` arms, so this is the maximal linear path, not a
guaranteed single wire layout. Treat the first block (up to 0x0110FE42) as
reliable — it is straight-line — and the tail (from 0x0111006A) as
branch-dependent.

```
u8        (0110F30A)   logged
u8        (0110F361)   flag; `cmp ...,0` at 0110F369
u8        (0110F3CC)
u8        (0110F3DA)   -> setne, passed to 0x01102A40
bytes[4]  (0110F40B)   u32 X; also fed to 0x0289E790 and 0x0143E970
u32       (0110F47F)
u32       (0110F48B)
u8 -> [0x03A5CA08]   (0110F4C1)   ; note: destination is a GLOBAL, not a local
u8 -> [0x03A5CA0B]   (0110F4CD)   ; ditto
u8        (0110F4E9)   -> bool
u8        (0110F4F7)   -> bool
u8        (0110F53C)
u8        (0110F588)   -> stored to byte [0x03A5CA0F]
u8        (0110F5E5)
u8        (0110FB7B)   -> stored to +0xB0022 of the session object
u8        (0110FBA5)   -> switch selector (`sub eax,0` ... at 0110FBB4)
u8        (0110FBEC)
u8        (0110FBFA)
u32       (0110FC76)   -> into a std::string-ish slot at 0x03B3E1B8
u32       (0110FD2C)
u16       (0110FD7D)
u8        (0110FD9A)   -> `test al,al`
u8        (0110FE34)   -> `cmp ...,1` at 0110FE4A
u8        (0110FE42)
--- branch-dependent tail ---
u8        (0111006A)
u16       (011101C6)
u32       (011101D2)
u32       (011101DE)   -> magic-divided by 0x91A2B3C5 (time conversion)
u32       (011105B9)
```
The literal `CMDFUNC_ENUM_CMDPACKET_LOGIN` (0x03241DD8) is pushed at 0x0110FC4C,
which pins this function to type 0x1 independently of the registrar.

### 0x4 — SELECT_CHARACTER, handler 0x01111070 (0x10B0 bytes)

```
u32  a                            (011111C1)
u32  b                            (011111CA)
u16  c                            (011111D3)
u16  d                            (011111DC)
u16  e                            (011111E5)
u16  f                            (011111EE)
u8   n                            (011111F9)
L{n} x {                          loop 01111210..011112C7 (`cmp esi,n; jl`)
    u8   premiumType              (01111216)
    bytes[8]  premiumEndTime      (01111221)      ; u64, matches the
}                                                 ; "premium %d : premiumType %d, premiumEndTime %llu" literal
u32                               (011112D1)
L30 x {                           fixed 30 iterations (`mov esi,0x1E` @01111593, `dec esi; jne` @011115D5)
    u16  slotId                   (0111159F)
    u32  value                    (011115AB)
}
[nested 0x00E67590] 4 x u32       (011115E8)      ; fixed 4 iterations
u8                                (0111160F)      ; `cmp al,8`
[nested 0x026CB700]               (0111180F)      ; u8, u8 n2, L{min(n2,0x65)} x u8
u16                               (0111186A)
u16                               (01111901)
u8                                (011119D7)
u16                               (01111A31)
```
Literals in this function: `ENUM_CMDPACKET_SELECT_CHARACTER - myServerId_(%d)`,
`createdTime %d MyServerId %d, Fatigue %d, FatigueMax %d, usedFatigueMax %d Cash %d`.

### 0xD0 — USE_RANDOMBOX_ITEM, handler 0x011156C0 (0x1A00 bytes)

```
u8   mode        (01115730)   ; `cmp eax,5` at 01115753 -> multi-way switch
u8   flag2       (01115744)
-- per switch arm, a (u16,u16) pair then an item list; the arms are:
   arm A: u16,u16 (011157EB,011157F7)  then [ITEMLIST] (01115A33)
   arm B: u16,u16 (01115B62,01115B6E)  then [ITEMLIST] (01115D70)
   arm C: u16,u16 (01115E20,01115E2C)
   arm D: u16,u16 (011160BD,011160C9)
   arm E: u16,u16 (01116441,0111644D)  then [ITEMLIST] (011165B6)
   arm F: u16,u16 (011166FF,0111670B)
-- then, common tail:
u16  rewardCount        (01116912)
L{rewardCount} x {      loop 0111699F..01116E2D
   u16  a               (011169C4)
   u32  b               (011169D0)
   u32  c               (011169DC)
   u16  d               (011169E8)
   u8   e               (011169F6)
   u32  f               (01116A08)
   [ARR32]              (01116A19)  ; u8 n + n x u32   (helper 0x01637A80)
   u16                  (01116A28)  ; helper 0x012612F0, stored to obj+0x44
   [APPEND]             (01116A34)  ; helper 0x01088BC0, see below
   u8   g               (01116A48)
}
```
Its own debug literals name the fields:
`SlotIndex %d, ItemIndex %d, Data %d, Durability %d, EnchantGrade %d`,
`[DEBUG] Recieve Item count is [%d]!!`,
`breathSlotindex : [%d]  rustyBindSpearSlotIndex : [%d]`,
`RandomBoxSlotIndex : [%d]  HammerSlotIndex : [%d]`.

### 0x450 — USE_RANDOMBOX_ITEM_EXPAND, handler 0x011170C0 (0x1006 bytes)
```
u8               (01117114)
u8               (01117122)
u16              (0111712E)
u16              (0111714C)
u16              (01117158)
[ITEMLIST]       (011173AE)
u16              (01117759)
[ITEMLIST]       (011177B5)
```

---

## 5. Shared sub-readers (called from the handlers above)

**`[ITEMLIST]` = 0x011152C0** — `readItemList(?, u16* out, char* flagOut)`.
Called 3x from 0xD0 and 2x from 0x450. Note: 0x011152C0 is a func-source-file
anchor for CMD_Etc.cpp, so this reader lives in this file.
```
u16  count                (01115318)
L{count} x {              loop 01115342..0111569E  (`cmp eax,count; jl`)
   u16  itemKind          (01115367)   ; compared against 0x16D at 01115537
   u32                    (01115373)
   u32                    (0111537F)
   u16                    (0111538B)
   u8                     (01115399)
   u32                    (01115409)
   [ARR32]                (01115417)   ; 0x01637A80: u8 n + n x u32
   u16                    (01115426)   ; 0x012612F0 -> obj+0x44
   [APPEND]               (01115432)   ; 0x01088BC0
   u8                     (01115446)
   u8                     (0111545A)
   u8                     (0111546E)
   u16                    (0111547A)
   u8                     (01115488)
   u8                     (01115496)
   u8                     (011154C0)
   u8                     (011154D4)
   u8                     (011154E8)
   <UNKNOWN>              (01115502)   ; call 0x0274FBE0 -- VMProtect stub
   <UNKNOWN>              (01115516)   ; call 0x0274FBE0 -- VMProtect stub
   u8                     (0111552A)
}
```

**`[APPEND]` = 0x01088BC0** — item appendage/enchant block, `__thiscall`.
```
u8   n                    (01088C0A)
L{n} x { u8, u8, u8 }     (01088C29, 01088C34, 01088C3F)  loop -> 01088C23
if (n != 0) {
   u8   kind              (01088CB3)
   u8   idx               (01088CD2)   ; 0xFF sentinel checked at 01088CDD
   u8, u8, u8, u8         (01088D1E, 01088D29, 01088D34, 01088D3F)
}
```

**`[ARR32]` = 0x01637A80** — `readU32Array(u32* dst)`
```
u8   n                    (01637A98)
L{n} x u32                (01637AB4)   ; dst[i]
```

**0x012612F0** — `__thiscall`: `u16` -> `this->+0x44` (01261302).

**0x00E67590** — `__thiscall`: fixed **4 x u32** (00E675BB), each non-zero one
appended via 0x00E673B0.

**0x026CB700**
```
u8                        (026CB719)
u8   n                    (026CB724)   ; clamped to 0x65 at 026CB733
L{min(n,0x65)} x u8       (026CB77D)   ; loop -> 026CB777
```

**0x0274FC10** — `readCStr(char* dst, u32 maxlen, int truncFlag)`; a plain
**u32 length prefix followed by that many raw bytes**, NUL-terminated by the
reader:
```
0274FC2F  read_u32 -> len
0274FC3A  if (len >= maxlen || len == 0) -> error path (no bytes consumed)
0274FC47  read_bytes(dst, len);  dst[len] = 0
```
This is the generic length-prefixed string on this stream. Useful reference for
any `<UNKNOWN>` string slot.

---

## 6. Unknowns / explicit non-claims

* **0x0274FBE0** (two calls inside the ITEMLIST per-item record) is a
  VMProtect stub (`jmp 0x0572B79D` into `.vmp1`). Its stream consumption is
  **unknown** from static analysis. It is *adjacent to* the plain string reader
  0x0274FC10 and is called with a single `char*` argument, so a
  length-prefixed string is the obvious guess — **but it is a guess and is not
  asserted here**. Two item fields in the RANDOMBOX item record are therefore
  unresolved.
* **Type-name table 0x03B53230** (stride 4, indexed by u16 packet type,
  referenced at 0x0163C059) is all zeros in this dump. Live capture would give
  the full `ENUM_CMDPACKET_*` name for all 85 types.
* The **LOGIN (0x1)** tail (from 0x0111006A) and the **USE_RANDOMBOX (0xD0)**
  switch arms are control-flow dependent; the sequences given are address-order
  listings with the branch conditions noted, not a proven single layout.
* Handlers with targets **outside** 0x0110C670..0x01118BC0 (types 0x03E, 0x079,
  0x08B, 0x0B0, 0x25B) are registered by this file but implemented elsewhere;
  their wire formats were not analysed here.
* Types **0x06B, 0x0C1, 0x0DF, 0x124, 0x1AE, 0x293, 0x294, 0x29E, 0x371, 0x39E**
  map to `0x02231720`, which is a single `ret`. The client accepts and
  discards these; a server may send them but nothing will happen client-side.

---

## 7. Reproduction artifacts (in SCRATCH)

* `_p5_regs.csv` — all 941 registrations image-wide: `registrar, site, key, handler, flag`
* `_p5_ends.json` — exact sub-function end for each of the 70 in-file handlers
* `_p5_w2.txt` — annotated read-windows for every handler and helper
* `_p5_reg.py`, `_p5_seq2.py`, `_p5_lib.py`, `_p5_win.py`, `_p5_name.py` — the scripts
