# proto-6 — `DNFClient\RDAR\CNRDUserInfoPacketHandler.cpp`

S4A21 client, image base 0x00400000, memory dump `dnf-dump-20260822-232754-263-26888`.

> **Address note.** The read primitives listed in the task brief are stated 0x400000 low.
> The real call targets in this image are
> `read_u8 = 0x027C5230`, `read_u16 = 0x027C52A0`, `read_u32 = 0x027C5310`,
> `read_bytes = 0x027C5380`, `set_remaining = 0x027C4350`.
> Verified from `callgraph.csv` (e.g. `0x013E8AF0,0x027C5230,16`) and from the
> disassembly at `013E8B7A call 0x27c5380`. Every VA below is a real image VA.

## 1. File extent

`func-source-file.csv` gives 9 `__FILE__` anchors:

```
0x013E1FD0 0x013E2150 0x013E2CA0 0x013E4070 0x013E4470
0x013E6370 0x013E6CD0 0x013E7340 0x013E8AF0
```

Previous source file in address order is `CNRDHttpImgManager.cpp` (0x013DA360), next is
`EquipmentFactory.cpp` (0x013F6AA0). The last anchor 0x013E8AF0 has span 3456 (0xD80), ending
exactly at 0x013E9870, and every literal above 0x013E9870 belongs to unrelated classes
(`CNAquajello`, `CraneMinigame`, `CNIgnis`). So the file occupies

**`[0x013E1FD0, 0x013E9870)`** — 74 functions in `functions.csv`, 32 of them in `parsers.csv`.

Note: `functions.csv` merges three real functions into the 0x013E8AF0 entry. The `int3`
padding shows the true boundaries:

| VA | ends | name (from its own `__FUNCTION__` literal) |
|---|---|---|
| 0x013E8AF0 | 0x013E91DA `ret` | `CNRDUserInfoPacketHandler::readOtherUserInfoMinimum` |
| 0x013E91E0 | 0x013E982C `ret` | `CNRDUserInfoPacketHandler::onNotiPacket` (+ `writeAnimationLog` inlined at 0x013E95E7) |
| 0x013E9830 | 0x013E986A `ret` | unnamed |

Named functions (literal evidence, `literals.csv`):

| VA | name |
|---|---|
| 0x013E1FD0 | `setDrawLoadingMode` |
| 0x013E2150 | `createCharacter` |
| 0x013E2CA0 | `readPremiumPcRoom` |
| 0x013E4070 | `writeMinimumInfoLog` |
| 0x013E4470 | `readHardcoreMinimum` |
| 0x013E46E0 | `createVirtualCharacter` |
| 0x013E6370 | `readUserInfoAddition` |
| 0x013E6CD0 | `readOtherUserInfoOtherCharacter` |
| 0x013E7340 | `readUserInfoMinimum` |
| 0x013E8AF0 | `readOtherUserInfoMinimum` |
| 0x013E91E0 | `onNotiPacket` |

---

## 2. THE DISPATCH — packet type → handler

**It is not a jump table.** It is a `std::map<int, {fnptr, ctx}>` singleton, populated by
static initializers and looked up per packet.

### 2.1 Machinery

| VA | role | evidence |
|---|---|---|
| 0x0163C550 | map singleton accessor; instance ptr in **`0x03A5BE74`** | `mov eax, [0x3a5be74]` at 0163C5CB; one-time `test byte [0x3a5be78],1` guard |
| 0x0163C4A0 | insert: `node = new{fn,ctx}; map[key].second = node` | `mov [eax],ecx` / `mov [eax+4],edx` from `[ebp+0xC]`/`[ebp+0x10]`, then `0x0163C2C0` (`map::operator[]` on `&[ebp+8]`) |
| 0x0163C510 | find: returns `{fn,ctx}*` or NULL | `call 0x2b1e410` (tree find) → `mov eax,[eax+4]` |
| **0x01637720** | **registrar** `Register(type, fn, ctx)`, `ret 0xC` | pushes its 3 stack args, `call 0x163C550`, `mov ecx,eax`, `call 0x163C4A0` |
| **0x016377E0** | **dispatcher** | see below |

Dispatcher body (0x016377E0), packet type arrives in `di` (16-bit):

```
016377F3  mov  eax, 0xC7
016377F8  cmp  di, ax                 ; 0xC7 short-circuited first (unrelated to this file)
...
01637850  push esi                    ; esi = packet type
01637851  call 0x163C550              ; handler map
01637856  mov  ecx, eax
01637858  call 0x163C510              ; find(type)
0163785D  test eax, eax
0163785F  je   0x163786D              ; unregistered -> nothing
01637861  mov  ecx, [eax+4]           ; ctx   (3rd registration arg)
01637864  mov  edx, [eax]             ; fnptr (2nd registration arg)
01637866  push ecx
01637867  push esi
01637868  call edx                    ; handler(u16 type, void* ctx)   -- cdecl, 2 args
```

So handler signature is `void __cdecl h(int type, void* ctx)`. Both handlers in this file
ignore both arguments (neither touches `[ebp+8]`).

### 2.2 The two registration sites owned by this file

Both live in the CRT static-init aggregate at 0x02DF3903:

```
02F47FA0  push 0                      ; ctx  = NULL
02F47FA2  push 0x013E91E0             ; fn   = CNRDUserInfoPacketHandler::onNotiPacket
02F47FA7  push 2                      ; TYPE = 0x0002
02F47FA9  call 0x1102C30              ; -> 0x1637C60 (result unused by the registrar)
02F47FAE  mov  ecx, eax
02F47FB0  call 0x1637720              ; Register(0x0002, 0x013E91E0, NULL)

02F47FB5  push 0
02F47FB7  push 0x013E9830
02F47FBC  push 0x2ED                  ; TYPE = 0x02ED
02F47FC1  call 0x1102C30
02F47FC6  mov  ecx, eax
02F47FC8  call 0x1637720              ; Register(0x02ED, 0x013E9830, NULL)
```

A whole-image pointer scan for `0x013E91E0` and `0x013E9830` returns exactly one hit each
(0x02F47FA3, 0x02F47FB8) — these are the *only* two entry points into this file from the
packet layer.

### 2.3 Type codes — CONFIRMED

The client carries an index → name table at **`0x03B51F20`**, filled by initializers of the
form `push <enc lit>; call 0x27BACC0; push <next enc lit>; mov [T+i*4], eax` (the next push is
hoisted between the call and the store). Decoding the pairing manually:

```
02F91A73  call 0x27bacc0
02F91A78  push 0x356ff78              ; "ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_USERINFO"
02F91A7D  mov  [0x3b52ad0], eax       ; index (0x3B52AD0-0x3B51F20)/4 = 0x2EC
02F91A82  call 0x27bacc0              ; <- consumes 0x356ff78
02F91A87  push 0x356ff10
02F91A8C  mov  [0x3b52ad4], eax       ; index 0x2ED  <== gets 0x356ff78's text
```

```
02F8EE33  call 0x27bacc0              ; <- consumes 0x357f548 = "ENUM_NOTIPACKET_USERINFO"
02F8EE38  push 0x357f508
02F8EE3D  mov  [0x3b51f28], eax       ; index (0x3B51F28-0x3B51F20)/4 = 2
```

| type | name | handler |
|---|---|---|
| **0x0002** | `ENUM_NOTIPACKET_USERINFO` | 0x013E91E0 `onNotiPacket` |
| **0x02ED** | `ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_USERINFO` | 0x013E9830 |

Both names are self-consistent with the source file name (`UserInfoPacketHandler`) and with the
0x2ED neighbours (0x2EB `..._WAITING_LIST`, 0x2EC `..._OTHER_CHANNEL_LIST`, 0x2EE
`..._OTHER_CHANNEL_REQUEST_JOIN`), all of which I decoded from the same initializer run.

---

## 3. Wire formats

Notation: `u8/u16/u32` = one `read_u8/read_u16/read_u32`; `bytes[n]` = `read_bytes(dst, n)`.
`[VA]` marks a read performed inside a callee, inlined here at its call position.
All sequences are in **address order**, which for these functions is also execution order —
they are straight-line except where a loop or a bail-out is called out.

### 3.1 `0x013E91E0` — NOTI **0x0002** `ENUM_NOTIPACKET_USERINFO` (top-level)

```
u8   subtype                                   ; 013E9222 -> [ebp-0x59]
```
then an if/else-if chain on `subtype` (**not** a jump table — a linear `cmp al, k / jne` chain):

| subtype | body | branch VA |
|---|---|---|
| 0 | `u16 N` ; `N ×` **UserInfoMinimum** (0x013E7340) | 0x013E9244 / loop 0x013E9293 |
| 1 | `u16 N` ; `N ×` **UserInfoAddition** (0x013E6370) | 0x013E942F / loop 0x013E9450 |
| 2 | `u16 slotTotal` ; `u16 slotUsed` ; `u8 a` ; `u32 b` ; `u16 c` ; `u32 d` ; `u16 N` ; `N ×` **SelectCharacter** (0x013E8600) | 0x013E92AB / loop 0x013E93B8 |
| 3 | `u16 N` ; `N ×` **record@0x013E69C0** | 0x013E9463 / loop 0x013E9481 |
| 4 | `u16 e` ; `u16 N` ; `N ×` **SelectCharacter** (0x013E8600) | 0x013E9494 / loop 0x013E94D4 |
| 5 | *(no branch — reads nothing)* | — |
| 6 | `u16 N` ; `N ×` **record@0x013E50E0** | 0x013E94FA / loop 0x013E9514 |
| 7 | **record@0x013E53A0**, exactly once, **no count prefix** | 0x013E956B |
| other | reads nothing | falls to 0x013E9574 |

Every `u16 N` above is a genuine length prefix: `xor esi,esi; cmp <reg>, word [ebp-0x58]; jae end`
then `call <handler>; inc esi; cmp esi, eax; jl <head>`.

Subtype 2 detail (0x013E92AB…0x013E93C6):
```
u16 slotTotal        ; 013E92FE -> [ebp-0x6c]
u16 slotUsed         ; 013E9307 -> [ebp-0x60]   ; client calls f(slotTotal, slotTotal-slotUsed)
u8                   ; 013E933C -> stored at [obj+0]
u32                  ; 013E9345 -> stored at [obj+4]
u16                  ; 013E935D -> 0x26DBA10 (select-char module setter)
u32                  ; 013E9383 -> 0x26DB5E0
u16 N ; N × 0x013E8600
```
Subtype 4 detail: `u16` (013E949F, into the same 0x26DBA10 setter), then `u16 N`, then N records.

Subtype 2 and 4 both end in `CNSelectCharacterModule.cpp` calls (0x026ED550) — this is the
character-select list.

### 3.2 `0x013E9830` — NOTI **0x02ED** `ENUM_NOTIPACKET_RAID_OTHER_CHANNEL_USERINFO`

```
u8  flag                          ; 013E9844 -> [ebp-4]
u16 (READ AND DISCARDED)          ; 013E984D -> [ebp-8], never referenced again
<readOtherUserInfoMinimum(flag)>        ; 013E9856 -> 0x013E8AF0
<readOtherUserInfoOtherCharacter(flag)> ; 013E985F -> 0x013E6CD0
```
No loop. The u16 looks like a count the client forgot to honour — the server must send
exactly one pair of records or the client desyncs.

### 3.3 `0x013E7340` `readUserInfoMinimum` (subtype 0 element)

```
bytes[0x26]   -> global 0x0397FA18          ; 013E73C1  (push 0x26; push 0x397FA18)
u16           userId/charId                 ; 013E73CD  (-> 0x013E27E0 lookup)
u8            "job"                         ; 013E741F  ([ebp-0x434], later cmp 0x0B)
u8            (signed)                      ; 013E743A  (-> 0x019DAFB0)
u8   [0x013E2930]                           ; 013E7487
u8   [0x013E29E0]                           ; 013E74A0
u8   [0x013E2A90]                           ; 013E74AC
u8                                          ; 013E74C1
u32                                         ; 013E74EC
u8                                          ; 013E750D
u8                                          ; 013E752F
u8                                          ; 013E7551
u8                                          ; 013E7569
bytes[8]  [0x013E6F60]  = {u32 itemId, u32}  ; 013E7576
u8   [0x013E2B40]                           ; 013E7582
u32  [0x013E2BF0]                           ; 013E758E
u8                                          ; 013E75A3
u8   [0x013E2CA0 readPremiumPcRoom]         ; 013E75CB
u8   [0x013E2DB0]                           ; 013E75D7
u32                                         ; 013E75E9
u32  [0x013E2E60]                           ; 013E7601
u32                                         ; 013E7613
u8                                          ; 013E7661
u8, u8   [0x013E51B0]                       ; 013E767E
u8, u32  [0x013E3030]                       ; 013E768B
u8                                          ; 013E76A8
u32                                         ; 013E76B4
u16                                         ; 013E76EB
u8, u8, u16  [0x013E4470 readHardcoreMinimum]; 013E7706
u32, u32     [0x013E22A0]                   ; 013E7713
u32                                         ; 013E7C71
u8                                          ; 013E7C86
u16                                         ; 013E7CB1
u8                                          ; 013E7CE7
u16                                         ; 013E7D12
u8                                          ; 013E7D20
u16                                         ; 013E7D79
u8   [0x013E3CC0]                           ; 013E7DB9
u8                                          ; 013E7DCE
u8                                          ; 013E7DFB
```
No loops. The large conditional block 0x013E7742..0x013E7BD1 (`test esi,esi; je 0x13E7BD6`)
contains **no** reads, so the layout is unconditional.

Cross-check: direct counts 15×u8 / 5×u16 / 5×u32 / 1×bytes — exactly `parsers.csv` row
`0x013E7340,15,5,5,1,0,26`.

### 3.4 `0x013E8AF0` `readOtherUserInfoMinimum(u8 flag)`

```
bytes[0x26]   -> global 0x0397FA18          ; 013E8B7A
u16           userId                        ; 013E8B86
    ** if 0x00C271D0(userId, flag) == NULL: logs
       "클라에 존재하지 않는 유저 - serverId : %d, channel : %d" and RETURNS at 013E8BEB,
       leaving the remainder of the record UNREAD. **
u8   [0x013E2930]                           ; 013E8C39
u8   [0x013E29E0]                           ; 013E8C52
u8   [0x013E2A90]                           ; 013E8C5E
u8                                          ; 013E8C72
u32                                         ; 013E8C9D
u8                                          ; 013E8CBD
u8                                          ; 013E8CDE
u8                                          ; 013E8CFF
u8                                          ; 013E8D16
bytes[8]  [0x013E6F60]                      ; 013E8D23
u8   [0x013E2B40]                           ; 013E8D2F
u32  [0x013E2BF0]                           ; 013E8D3B
u8                                          ; 013E8D4F
u8   [0x013E2CA0 readPremiumPcRoom]         ; 013E8D77
u8   [0x013E2DB0]                           ; 013E8D83
u32                                         ; 013E8D95
u32  [0x013E2E60]                           ; 013E8DAD
u8                                          ; 013E8DCF
u8, u8   [0x013E51B0]                       ; 013E8DEE
u8, u32  [0x013E3030]                       ; 013E8DFB
u8                                          ; 013E8E15
u32                                         ; 013E8E21
u16                                         ; 013E8E58
u8, u8, u16  [0x013E4470]                   ; 013E8E77
u32, u32     [0x013E22A0]                   ; 013E8E84
u8                                          ; 013E8EEF
u32                                         ; 013E8F01
u8                                          ; 013E8F15
u16                                         ; 013E8F4A
u8                                          ; 013E8F7F
u8   [0x013E3CC0]                           ; 013E8FA8
u8                                          ; 013E8FBC
u8                                          ; 013E8FE7
```
Note it is **not** byte-identical to `readUserInfoMinimum`: it lacks the two u8 immediately
after the u16, and its tail differs (`u8,u32,u8,u16,u8` vs `u32,u8,u16,u8,u16,u8,u16`).

### 3.5 `0x013E6370` `readUserInfoAddition` (subtype 1 element)

```
bytes[0x0F]   -> global 0x03A419E0          ; 013E63DF (push 0xF; push 0x3A419E0)
u16           charId                        ; 013E63EB
u32  [0x013E4160]                           ; 013E6517
u8                                          ; 013E6532
bytes[8]  [0x013E6050]                      ; 013E6564
<equip block> [0x013E2540]                  ; 013E656A
u8                                          ; 013E6578
u8                                          ; 013E65FC
u8                                          ; 013E6640
```

### 3.6 `0x013E69C0` (subtype 3 element) — unnamed

```
bytes[0x07]   -> global 0x03A419F0          ; 013E6A24 (push 7; push 0x3A419F0)
u16           charId                        ; 013E6A30
u32  [0x013E4160]                           ; 013E6A7F
u8                                          ; 013E6AE8
bytes[8]  [0x013E6050]                      ; 013E6B22
<equip block> [0x013E2540]                  ; 013E6B28
u8                                          ; 013E6B36
u32                                         ; 013E6BAA
u16, u32, u8, u8  [0x013E4B20]              ; 013E6BB0
u8                                          ; 013E6BC5
<talisman/rune block> [0x013E56D0]          ; 013E6BEA
```
*Inference only, not evidence:* this is almost certainly `readUserInfoOtherCharacter` — it is
the fourth member of the naming quartet and is structurally the self-side twin of
`readOtherUserInfoOtherCharacter` (0x013E6CD0). It carries no `__FUNCTION__` literal, so the
name is unproven; the wire format above is.

### 3.7 `0x013E6CD0` `readOtherUserInfoOtherCharacter(u8 flag)`

```
bytes[0x07]   -> global 0x03A419F0          ; 013E6CF2
u16           userId                        ; 013E6CFB
    ** if 0x00C271D0(userId, flag) == NULL: logs the same
       "클라에 존재하지 않는 유저 - serverId : %d, channel : %d" and RETURNS at 013E6D5C,
       leaving the rest UNREAD. **
u32  [0x013E4160]                           ; 013E6DC6
u8                                          ; 013E6DDB
bytes[8]  [0x013E6050]                      ; 013E6E06
<equip block> [0x013E2540]                  ; 013E6E0C
u8                                          ; 013E6E17
<block> [0x013E5490]                        ; 013E6E69
u32                                         ; 013E6E79
u16, u32, u8, u8  [0x013E4B20]              ; 013E6E7F
u8                                          ; 013E6E8E
<talisman/rune block> [0x013E56D0]          ; 013E6EB0
```

### 3.8 `0x013E8600` (subtypes 2 & 4 element — select-character record)

```
u16   key/charId                            ; 013E8635
      (looked up in 0x026DBDF0/0x026DBD50; on miss allocates a 0x1104-byte object
       from CNMemoryPool 0x027C42E0)
u8, u8  [0x013E5340]                        ; 013E871C
u8                                          ; 013E8736
u8                                          ; 013E8751
u8   [0x013E2930]                           ; 013E8791
u8   [0x013E29E0]                           ; 013E879D
u8                                          ; 013E87B1
u32                                         ; 013E87CA
u32                                         ; 013E87F2
u32                                         ; 013E882C
u8                                          ; 013E884C
u8                                          ; 013E886D
u8                                          ; 013E888E
u8                                          ; 013E88A2
bytes[8]  [0x013E6F60]                      ; 013E88AF
u8   [0x013E2B40]                           ; 013E88BB
u32  [0x013E2BF0]                           ; 013E88C7
u8                                          ; 013E88DB
u8   [0x013E2CA0 readPremiumPcRoom]         ; 013E88FA
u8   [0x013E2DB0]                           ; 013E8906
u8                                          ; 013E895E
u8                                          ; 013E897D
u8                                          ; 013E898B
u8                                          ; 013E89AB
u8, u8, u8  [0x013E43E0]                    ; 013E89C6
u8                                          ; 013E89DA
u8                                          ; 013E89FA
u8                                          ; 013E8A1D
u8                                          ; 013E8A3D
```
Straight-line, no loops (verified over 0x013E8920..0x013E8A50). Direct counts
16×u8 / 1×u16 / 3×u32 match `parsers.csv 0x013E8600,16,1,3,0,0,20`.

### 3.9 `0x013E50E0` (subtype 6 element)

```
u16 charId    ; 013E50EC (-> 0x013E27E0)
u32           ; 013E5105
u32           ; 013E510E
u32           ; 013E5120
u32           ; 013E5129
u32           ; 013E5132
```

### 3.10 `0x013E53A0` (subtype 7 payload)

```
u16 charId    ; 013E53CC (-> CNUserManager 0x019E2EF0)
u32           ; 013E53FE
u32           ; 013E5407
u32           ; 013E5410
u8            ; 013E541B
```

---

## 4. Sub-readers (reusable field groups)

| VA | wire format | notes |
|---|---|---|
| 0x013E20A0 | `u8 N` ; `N × { u16, u8 }` | length-prefixed array; the "no target object" variant of the 0x013E2540 inner loop |
| 0x013E22A0 | `u32, u32` | |
| 0x013E2540 | `u8` ; **2 ×** `{ u8 M ; M × { u16, u8 } }` | equip/avatar. Outer count is the constant **2** (`inc edi; cmp edi,2; jl 0x13E2580` at 013E2630), *not* wire-driven. Inner is length-prefixed (`cmp [ebp-1],bl; jbe`, `inc ebx; cmp ebx,eax; jl 0x13E25B0`). When the target object is absent the same bytes are consumed via 0x013E20A0, so the wire shape is identical either way. |
| 0x013E2690 | `u8 N` ; `u8` ; `N × { u8, u16, u8 }` | **not reachable from this file's handlers** — only caller is 0x01607C20 |
| 0x013E2930 / 0x013E29E0 / 0x013E2A90 / 0x013E2B40 / 0x013E2DB0 | `u8` | one field each + a `std::string` name for logging |
| 0x013E2BF0 | `u32` | |
| 0x013E2CA0 `readPremiumPcRoom` | `u8` | logs `"%s is Premium Pc Room = %s"` |
| 0x013E2E60 | `u32` | |
| 0x013E3030 | `u8`, `u32` | |
| 0x013E3CC0 | `u8` | |
| 0x013E4160 | `u32` | |
| 0x013E43E0 | `u8, u8, u8` | three bools |
| 0x013E4470 `readHardcoreMinimum` | `u8, u8, u16` | |
| 0x013E4B20 | `u16, u32, u8, u8` | |
| 0x013E51B0 | `u8, u8` | |
| 0x013E5340 | `u8, u8` | |
| 0x013E5490 | `u32, u32, u32, u8, u8, u8, u8, u8 K` ; `K × u32` | last u8 (`[ebp-0xD]`, 013E5515) is the array count; loop head 0x013E5530, back-edge 013E561D |
| 0x013E56D0 | `u8 N` ; `N × { u8, u8, u32, u8, u8 M ; M × u32 }` | outer count 013E570F (`[ebp-0x961]`), loop head 0x013E5730 / back-edge 013E5AFE; inner count is the 5th field (`[ebp-0x962]`, 013E57DD), loop head 0x013E5880 / back-edge 013E59AC |
| 0x013E6050, 0x013E6F60 | `bytes[8]` | consumed as `{u32 id, u32}` (`[ebp-0x198]`, `[ebp-0x194]`) |

## 5. Fixed blobs read with `read_bytes`

These are raw fixed-size structs, not length-prefixed. Offsets are the ones the code actually
touches; gaps are on the wire but unused by these handlers.

**`0x0397FA18`, 0x26 = 38 bytes** (used by `readUserInfoMinimum` 013E738D and
`readOtherUserInfoMinimum` 013E8B48):

```
+0x00 u8    +0x02 u16   +0x04 u8    +0x05 u16   +0x07 u8
+0x08 u8    +0x09 u8    +0x0A u8    +0x0B u8    +0x0C u8   +0x0D u8   +0x0E u8
+0x0F u32
+0x17 u8    +0x18 u8    +0x19 u32
+0x25 u8
```

**`0x03A419E0`, 0x0F = 15 bytes** (`readUserInfoAddition` 013E63B9):

```
+0x00 u32   +0x04 u16   +0x06 u32   +0x0A u32   +0x0E u8
```

**`0x03A419F0`, 7 bytes** (0x013E69C0 at 013E6A0D, `readOtherUserInfoOtherCharacter` at 013E6CDB):

```
+0x00 u32   +0x04 u16   +0x06 u8
```

---

## 6. Things deliberately left unpinned

* The **sub-type enum names** for NOTI 0x0002 subtypes 0..7 — I found no name table for them
  and no literal near the branch chain. Only the numeric codes and their handlers are stated.
* **0x013E69C0's function name** — see 3.6, inference flagged as inference.
* The two encrypted literals 0x03294C18 (used at 013E4644 in `readHardcoreMinimum`) and
  0x03294E28 (used at 013E6418 in `readUserInfoAddition`) do not decode under the standard
  cipher; their text is unknown.
* `set_remaining` (0x027C4350) is **never** called anywhere in this file — no handler here
  establishes a sub-frame boundary.

## 7. Operational notes for the server team

1. NOTI 0x0002 sub-type is a **u8**, not a u16.
2. NOTI 0x0002 subtype **5 is unhandled** — the client reads the sub-type byte and then nothing.
   Anything after it in the frame is silently ignored.
3. NOTI 0x0002 subtype **7 carries no count** — exactly one 0x013E53A0 record.
4. NOTI 0x02ED reads a u16 immediately after the flag byte and **discards it**; it then reads
   exactly one `OtherUserInfoMinimum` + one `OtherUserInfoOtherCharacter`. Sending N>1 there
   will desync the client.
5. Both `readOtherUserInfoMinimum` and `readOtherUserInfoOtherCharacter` **abort mid-record**
   (leaving the rest of the bytes in the stream) if the u16 user id is not already known to the
   client. Any batch that references an unknown user corrupts everything after it in the frame.
