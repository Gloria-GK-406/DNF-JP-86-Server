# DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp - handlers, wire formats, dispatch

S4A21 client, unpacked from memory, image base 0x00400000.
Every claim below carries the VA it came off.

---

## 1. File extent

`func-source-file.csv` gives 14 anchors carrying the `__FILE__` literal
`D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp` (literal VA
**0x03246628**), running 0x01160090 .. 0x01187FF0.

The real object-file block is **0x01160090 - 0x0118965B**. The upper bound is pinned,
not guessed: the block's last function is this file's handler-registration routine,
which ends `0118965A ret` / `0118965B int3`; the very next function, **0x01189660**,
is registered by the *NOTI_Town* registrar (site 0x0119A79C). So 0x01189660 is where
NOTI_Town.cpp's code starts. Consistent with that, the chunk at 0x01187FF0 already
references the `GameCoreNetwork_NOTI_Town.cpp` literal (0x03249BB0) - the two files
abut inside that chunk.

`functions.csv` reports only 68 "functions" in the range and `parsers.csv` only 19
parsers. That is a chunking artifact: entries such as 0x0117C520 (span 25200 bytes)
actually contain dozens of separate functions separated by `int3` padding. The true
count is **201 distinct handler functions**, recovered from the dispatch registration
below rather than from function-boundary heuristics.

---

## 2. THE DISPATCH

### 2.1 Two maps, not one

`GameCoreNetworkInit` at **0x0163A850** builds both:

```
0163ABD8  call 0x0163C410   ; CMD  map singleton  -> instance [0x03A5BE68], once-flag [0x03A5BE6C]
0163ABDD  mov  ecx, eax
0163ABDF  call 0x0163C190   ; CMD  map fill
0163ABE4  call 0x0163C550   ; NOTI map singleton  -> instance [0x03A5BE74], once-flag [0x03A5BE78]
0163ABE9  mov  ecx, eax
0163ABEB  call 0x0163C4F0   ; NOTI map fill
```

`0x0163C4F0` (NOTI fill) is three tail-chained per-file registrars:

```
0163C4F3  call 0x0119A780   ; GameCoreNetwork_NOTI_Town.cpp
0163C4FA  call 0x0115F540   ; GameCoreNetwork_NOTI_Dungeon.cpp
0163C502  jmp  0x011885F0   ; GameCoreNetwork_NOTI_Etc.cpp   <-- THIS FILE
```

(the CMD fill 0x0163C190 chains 0x01144AF0, 0x0110C2C0, 0x01118BC0 - the CMD_* files.)

So every type code in this document is an **ENUM_NOTIPACKET_\*** value, not a CMD id.

### 2.2 Independent confirmation of the namespace split

`CheckPacketEndIndex` at **0x01160090** - the first function in this file, holding the
literals "Different from server's cmdpacket count" (0x03246710) and "Different from
server's notipacket count" (0x032466B0) - hardcodes the two enum terminators:

```
01160093  mov eax,[ebp+8]            ; arg1 = server's CMD packet count
01160096  cmp dword ptr [eax],0x4F7
011600A0  je  0x011601B4
011600A6  cmp dword ptr [esi],0x4C2  ; esi = [ebp+0xC] = server's NOTI packet count
011600AE  je  0x01160140             ; -> prints "cmdpacket count"  (arg1 was wrong)
011601B4  cmp dword ptr [esi],0x4C2
011601BA  je  0x01160235             ; ok
011601BE  push 0x032466B0            ; -> prints "notipacket count" (arg2 was wrong)
```

Branch targets tie each message to its argument, so:

* **ENUM_CMDPACKET_END  = 0x4F7 (1271)**
* **ENUM_NOTIPACKET_END = 0x4C2 (1218)**

Cross-check across all 943 registration sites in the image: the 539 registered on the
NOTI singleton max out at **0x4BD (< 0x4C2)**; the 399 on the CMD singleton max out at
**0x4EB (< 0x4F7)**. Each set fits its own terminator and neither fits the other's.

### 2.3 It is a map, not a jump table and not a pointer array

There is no static function-pointer array for noti handlers. A whole-image scan for
runs of >= 32 consecutive DWORDs pointing into .text returned 4011 runs, and exactly
one had >= 3 entries landing in this file: 0x01181A50, a 34-entry `switch` jump table
*inside* the function at 0x0117C520. That is a local switch, not the dispatch.

Registration primitive - **`Register(this, int type, void *fn, int extra)`**, thiscall,
`ret 0xC`, at **0x0163C4A0**:

```
0163C4A5  push 8 ; call 0x00423460          ; operator new(8)
0163C4B5  mov ecx,[ebp+0xC] ; mov edx,[ebp+0x10]
0163C4BB  mov [eax],ecx                     ; node.fn    = arg2
0163C4BD  mov [eax+4],edx                   ; node.extra = arg3
0163C4C2  lea eax,[ebp+8] ; call 0x0163C2C0 ; map insert keyed on arg1 (the type)
0163C4CE  mov [eax+4],esi                   ; map slot -> node
```

Call-site shape, repeated 228 times inside 0x011885F0 (example: type 0xC7):

```
01188B70  push 0            ; extra
01188B72  push 0x01160FA0   ; fn
01188B77  push 0xC7         ; key = ENUM_NOTIPACKET_DISCONNECT_REASON
01188B7C  mov  ecx, esi     ; the NOTI map singleton
01188B7E  call 0x0163C4A0
```

Lookup - **`Find(int type)`** at **0x0163C510**, returns the 8-byte node or NULL.

Runtime NOTI dispatcher - **0x016377E0**, reached from 0x0163BBD0 (GameCoreNetwork.cpp):

```
016377F3  mov  eax,0xC7
016377F8  cmp  di,ax                ; di = the noti type (u16)
016377FB  jne  0x01637834           ; 0xC7 gets an early, unconditional path
...
01637850  push esi                  ; esi = type
01637851  call 0x0163C550           ; NOTI map singleton
01637858  call 0x0163C510           ; Find(type)
0163785D  test eax,eax ; je ...     ; unregistered type -> silently dropped
01637861  mov  ecx,[eax+4]          ; extra
01637864  mov  edx,[eax]            ; fn
01637866  push ecx ; push esi
01637868  call edx                  ; handler(type, extra)  __cdecl, 2 args
0163786A  add  esp,8
0163786D  push ebx ; push edi ; call 0x01437F10   ; post-dispatch notify
```

Type **0xC7** (DISCONNECT_REASON) is special-cased at 0x016377F3: it is dispatched even
when the readiness check at 0x0163783B fails.

For contrast, the CMD dispatcher is **0x01637680**: it takes the id as
`movzx esi, word ptr [eax+1]` (u16 at header offset +1), looks it up in the *CMD*
singleton (0x0163C410), and calls `fn(type, a, b, extra)` - 4 args, `add esp,0x10`.

### 2.4 Where the payload comes from

Handlers take no buffer argument. The five read primitives walk one global cursor.

| primitive | VA in this dump | effect |
|---|---|---|
| read_u8       | 0x027C5230 | `*(u8*)dst = *cursor; cursor += 1; remaining -= 1` |
| read_u16      | 0x027C52A0 | 2 bytes |
| read_u32      | 0x027C5310 | 4 bytes |
| read_bytes    | 0x027C5380 | `memcpy(dst, cursor, len)`; `len` is the 2nd arg ([ebp+0xC]) |
| set_remaining | 0x027C4350 | `remaining = arg` (frame boundary) |

cursor = **[0x03BAAE38]**, remaining = **[0x03BAAE3C]**.

ADDRESS NOTE: the task brief lists these at 0x023C5230 / 0x023C52A0 / 0x023C5310 /
0x023C5380 / 0x023C4350. In the disassembled image the call targets are
0x027C5230 / 0x027C52A0 / 0x027C5310 / 0x027C5380 / 0x027C4350 - a constant +0x400000
difference. Same five functions; the scratchpad tooling accepts both spellings.

`read_u8` is frequently called as `push 1; push dst; call read_u8`. The callee only
reads `[ebp+8]`, so the extra `1` is ignored and the read is still exactly one byte
(verified at 0x0116528C-0x0116528F against the primitive body at 0x027C5230).

---

## 3. The registration routine

**`SetNotiFunc_Etc(this)` = 0x011885F0 .. 0x0118965A.** 228 `Register` calls:
201 distinct real handlers, plus **0x02231720** - a bare `ret` at 0x02231720 - used
27 times for types the client accepts but deliberately ignores.

Registration order is source order, not numeric order (new packet types were appended
over time). No type is registered twice inside this file; the 201 handler VAs are all
distinct, so the type -> handler map here is a bijection.

The 27 types wired to the bare `ret` at 0x02231720 - accepted by this client build and
then discarded - are:

```
0x019 0x043 0x06F 0x074 0x079 0x09A 0x0A7 0x0B2 0x0C1 0x0C6 0x0CC 0x0E5 0x102
0x10E 0x10F 0x110 0x116 0x11E 0x11F 0x120 0x12C 0x13C 0x15A 0x1A4 0x1A5 0x1CA 0x314
```

They are still *registered*, so the dispatcher will not warn on them; they consume no
payload, which means the server must not put a body on them for this build.

---

## 4. Handler table

How to read the format column:

* Items are the read-primitive calls in **address order** inside the handler.
  `3*u8` = three consecutive one-byte reads.
* `LOOP{...}` marks reads inside a backward-branch region. Where the loop bound is the
  read immediately before it, that read is the count and the body is a length-prefixed
  array: `u16 LOOP{ u16, u8, u16 }` means `u16 count; count x { u16, u8, u16 }`.
  (Verified by hand at 0x01165220: `READ_U16 -> [ebp-0x10]`, then
  `cmp cx, [ebp-0x10] / jae end`, body `READ_U16, READ_U8, READ_U16`.)
* `LOOP^n` distinguishes separate loop regions inside one handler. n is an index, not
  a nesting depth.
* `bytes[N]` is a fixed raw block of N bytes (N taken from the `push imm` that supplies
  read_bytes' length arg; e.g. `0117CC51 push 0x65 / push ecx / call read_bytes`).
  `bytes[var]` means that argument was not a constant at the call site.
* **[SWITCH]** = the handler contains a jump table or is branch-dense. For those rows
  address order enumerates reads across *all* branches, so the row is an inventory of
  fields, not a literal linear layout. Do not build a struct from a [SWITCH] row.
* The name column is filled only where the handler body references its own
  `NOTIFUNC_ENUM_NOTIPACKET_*` log literal. Blank means the name is unknown - the
  **type code is still exact**, it is read off the `push <type>` at the registration
  site.

| type | handler VA | name | wire format |
|---|---|---|---|
| 0x000 | 0x01160240 |  | (no read-primitive calls) |
| 0x001 | 0x01169530 | ENUM_NOTIPACKET_CHANNELINFO | **[SWITCH]** 2*u8, bytes[var], 2*u32, 3*u8, 5*u32, 2*u8, 2*u32 |
| 0x009 | 0x01171FD0 |  | u16 LOOP{ u16, 4*u8, u32, 3*u8, u16 } LOOP^2{ u16, 3*u8 } LOOP{ 5*u8, u32 } LOOP^2{ 2*u32 } |
| 0x00A | 0x01164EA0 |  | 2*u8 |
| 0x00D | 0x0117C670 | ENUM_NOTIPACKET_ITEM_LIST | **[SWITCH]** u8, 2*u16, u32, 2*u16, u8, u16, u8, u16 LOOP{ bytes[101] } |
| 0x00E | 0x0117D720 |  | u8, u16 LOOP{ bytes[101] } |
| 0x00F | 0x0117F820 |  | bytes[101] |
| 0x010 | 0x01165220 |  | u16 LOOP{ u16, u8, u16 } |
| 0x011 | 0x01165550 |  | u16, u8 |
| 0x012 | 0x0116E710 |  | u16 LOOP{ 2*u16 } |
| 0x013 | 0x011783A0 | ENUM_NOTIPACKET_SKILLINFO | **[SWITCH]** LOOP{ u16, u8 } LOOP^2{ u8, u16, 2*u8 } LOOP^3{ u8 } 2*u16 |
| 0x015 | 0x0117B470 |  | u8, u16 LOOP{ u16 } |
| 0x019 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x01A | 0x01165640 |  | u8 |
| 0x024 | 0x01185070 |  | **[SWITCH]** 5*u16 |
| 0x025 | 0x01178CD0 | ENUM_NOTIPACKET_EXP | u8, 2*u32, 4*u16, 2*u32, u8, 5*u32, u8 LOOP{ 2*u32 } 8*u32 |
| 0x027 | 0x01185970 | ENUM_NOTIPACKET_GET_ITEM | **[SWITCH]** 2*u16, u8 LOOP{ u8 } 2*u16, u8 LOOP{ u8, u32, u8 } LOOP^2{ u32 } LOOP{ u32, u8 } LOOP^2{ u32 } LOOP{ u32 } |
| 0x028 | 0x0117FE50 | ENUM_NOTIPACKET_DROP_ITEM | 4*u16, bytes[101], u8, u16 |
| 0x029 | 0x01173790 |  | **[SWITCH]** u16 LOOP{ u16, 3*u8, u16, u8 } LOOP^2{ u8, u16 } LOOP{ u8, u32 } |
| 0x02A | 0x01160360 |  | u16, 2*u8, u16, u8, u32 |
| 0x02B | 0x01165950 |  | u16, 2*u8 LOOP{ 2*u8, u16 } |
| 0x02C | 0x01160520 |  | 2*u8 |
| 0x02D | 0x01173BD0 | ENUM_NOTIPACKET_START_PVP | 2*u8 |
| 0x02E | 0x0117A200 | ENUM_NOTIPACKET_DIE_PVP_CHARACTER | 2*u8 |
| 0x02F | 0x01170310 |  | **[SWITCH]** u8, u32, u8, u32, u8 LOOP{ u16, u32, u8, 3*u32 } 2*u16, u32, 2*u8, u32, u8 LOOP{ u32, u16, u8 } |
| 0x030 | 0x01165CB0 | ENUM_NOTIPACKET_PVP_RECORD | **[SWITCH]** 5*u32, 2*u8, 4*u32, u8, 3*u32 LOOP{ 2*u32, 2*u8, 4*u32 } |
| 0x031 | 0x0116C3A0 |  | (no read-primitive calls) |
| 0x032 | 0x01160690 |  | u8 |
| 0x033 | 0x011664F0 |  | u8, 2*u32 |
| 0x039 | 0x011606E0 |  | u32 |
| 0x041 | 0x01173D70 |  | 3*u8 |
| 0x042 | 0x0116EA50 |  | **[SWITCH]** 2*u16, u8, bytes[8], u8, 2*u16 |
| 0x043 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x056 | 0x011803E0 | ENUM_NOTIPACKET_SERVER_BROADCAST | **[SWITCH]** 2*u8, u16, u32, u8 LOOP^5{ u8, u16, u32, 2*u8 } u8, u16, u32, u8 LOOP^2{ u8, u16, u32, u8, u32 } u8, u16, u32, u8 LOOP^6{ 2*u32 } LOOP^7{ u8, u16, u32, u8 } LOOP^6{ u16 } LOOP^4{ u16, 2*u32, u8, 2*u16, 2*u32 } LOOP^8{ u16, 2*u32, u8 } LOOP^7{ u16, u32 } LOOP^5{ u16, u32, u8 } LOOP^3{ u16, u32 } LOOP{ 2*u16, u8, u32, u16, u32 } 3*u32 LOOP{ 2*u32 } |
| 0x05E | 0x01160720 |  | u8, u16 |
| 0x066 | 0x01166750 |  | 2*u8, u32, 2*u8, u32 |
| 0x069 | 0x01163430 |  | u8 LOOP{ u32, 2*u8, u32, 4*u8 } |
| 0x06F | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x070 | 0x01160880 |  | u8 LOOP{ 2*u8 } LOOP^2{ u8, u16 } |
| 0x071 | 0x011609B0 |  | u8 |
| 0x074 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x076 | 0x01166B60 |  | 4*u8, u16 |
| 0x079 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x07D | 0x01160A20 |  | 2*u8, u16, u8, u16, u8 |
| 0x08A | 0x01160BA0 |  | 3*u16, u8 |
| 0x08B | 0x01160C30 |  | (no read-primitive calls) |
| 0x09A | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0A4 | 0x01160CD0 |  | u8 |
| 0x0A5 | 0x01167670 | ENUM_NOTIPACKET_AA_STARTINFO | 2*u8 |
| 0x0A6 | 0x01167820 | ENUM_NOTIPACKET_AA_TIME | u8 |
| 0x0A7 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0A8 | 0x01163600 |  | u16 |
| 0x0AC | 0x01181AE0 |  | u8 LOOP{ 2*u32 } |
| 0x0AD | 0x01160D20 |  | (no read-primitive calls) |
| 0x0AE | 0x01181BE0 |  | u8 LOOP{ 2*u32 } |
| 0x0AF | 0x01160DA0 |  | u8 |
| 0x0B2 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0B3 | 0x01167930 |  | u32 |
| 0x0B4 | 0x01167A10 |  | 2*u8 |
| 0x0B5 | 0x01167AF0 |  | u8, u32 LOOP{ 3*u32, u8 } |
| 0x0BA | 0x01160DF0 |  | 2*u8 |
| 0x0BB | 0x01160E80 |  | 3*u8 |
| 0x0C0 | 0x01160EB0 |  | 2*u8 LOOP{ u8 } |
| 0x0C1 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0C4 | 0x01167FF0 |  | u8 |
| 0x0C6 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0C7 | 0x01160FA0 | ENUM_NOTIPACKET_DISCONNECT_REASON | 2*u32 |
| 0x0CC | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0D4 | 0x01174AD0 |  | **[SWITCH]** u32, 8*u8 |
| 0x0DB | 0x0116D870 |  | u16, u32, u8 |
| 0x0DE | 0x01163700 | ENUM_NOTIPACKET_PUNISH_END_TIME | 2*u32, 3*u16, u8 |
| 0x0E1 | 0x01161130 |  | 2*u8 LOOP{ 3*u32, u8, 7*u32, u8, 2*u16, 5*u32 } |
| 0x0E4 | 0x011612D0 |  | (no read-primitive calls) |
| 0x0E5 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x0E9 | 0x01161300 |  | 2*u8 |
| 0x0EA | 0x01161370 |  | u16, 2*u8 |
| 0x0F4 | 0x01161400 |  | u32 |
| 0x0F5 | 0x01161430 |  | (no read-primitive calls) |
| 0x0FB | 0x01161450 |  | u16 LOOP{ u8, u16, u8, u32 } |
| 0x0FC | 0x01161500 |  | u8, u16 |
| 0x101 | 0x01181E10 |  | u8 LOOP{ 2*u32 } |
| 0x102 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x104 | 0x01183790 |  | u16, u32 |
| 0x105 | 0x01163830 |  | u8 |
| 0x108 | 0x01161550 |  | (no read-primitive calls) |
| 0x10A | 0x0117A940 |  | **[SWITCH]** u8, u32, 2*u8 |
| 0x10D | 0x0117AE30 |  | u8 |
| 0x10E | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x10F | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x110 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x111 | 0x01161580 | ENUM_NOTIPACKET_UNITED_SERVER_FRIEND_INFO | u32 |
| 0x112 | 0x0116F000 |  | u16, 2*u8 |
| 0x113 | 0x01161650 |  | u32 |
| 0x116 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x119 | 0x01161660 |  | u16 |
| 0x11A | 0x011616B0 |  | (no read-primitive calls) |
| 0x11B | 0x011682A0 |  | u16, u32 |
| 0x11C | 0x011616E0 |  | u32 |
| 0x11E | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x11F | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x120 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x122 | 0x01168410 |  | 3*u32 |
| 0x123 | 0x01161720 |  | u16 LOOP{ u16, u32 } |
| 0x124 | 0x01186930 |  | u8 LOOP{ u32, bytes[101] } |
| 0x128 | 0x01163BC0 |  | 5*u32 |
| 0x12C | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x12F | 0x011617E0 |  | 2*u16 |
| 0x13C | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x142 | 0x01161A70 |  | 2*u16 |
| 0x156 | 0x01184160 |  | u16 LOOP{ u8 } |
| 0x158 | 0x01185880 |  | u8 LOOP{ 2*u32 } |
| 0x15A | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x164 | 0x011641F0 |  | (no read-primitive calls) |
| 0x165 | 0x01162170 |  | u16, u8 |
| 0x166 | 0x01181F70 |  | u8, u16, 2*u32 LOOP{ u16, 2*u32, u8, u16, u8, u32, 2*u8, u16 } |
| 0x167 | 0x0116F4A0 | ENUM_NOTIPACKET_ACHIEVEMENT_LIST | u32 LOOP{ u32, 4*u16 } |
| 0x168 | 0x011822D0 |  | 4*u32, u16 |
| 0x169 | 0x0116BFB0 |  | 2*u32, u16 |
| 0x172 | 0x01162200 |  | u8 |
| 0x17B | 0x01161AC0 |  | u8 LOOP{ u8, u32 } |
| 0x17C | 0x0116F970 |  | **[SWITCH]** 2*u8 LOOP{ 3*u16 } |
| 0x17E | 0x01168560 |  | 2*u32 |
| 0x1A3 | 0x01168670 |  | **[SWITCH]** u8, u16, u32 |
| 0x1A4 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x1A5 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x1A7 | 0x01161B70 |  | u8 |
| 0x1AA | 0x01161B40 |  | (no read-primitive calls) |
| 0x1AC | 0x0116B1E0 |  | u8, 2*u32 |
| 0x1B7 | 0x011689E0 |  | u16 |
| 0x1BF | 0x011759A0 |  | (no read-primitive calls) |
| 0x1C1 | 0x01161CA0 |  | u8 |
| 0x1C7 | 0x01160D70 |  | (no read-primitive calls); parsing delegated to 0x01389E50 |
| 0x1C9 | 0x01161D10 |  | (no read-primitive calls) |
| 0x1CA | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x1CB | 0x01175A90 | ENUM_NOTIPACKET_END_SCRIPT_RELOAD | u8 LOOP{ u8, u32 } |
| 0x1DC | 0x01161BF0 | ENUM_NOTIPACKET_GET_ITEMBOX | u32 |
| 0x1DD | 0x01163CA0 | ENUM_NOTIPACKET_GENERATE_ITEMBOX | 2*u32, 5*u16 |
| 0x1E1 | 0x01188130 | ENUM_NOTIPACKET_GROWTH_ITEM_RENEW_DATA | u8, 3*u16, u8 |
| 0x1E2 | 0x0116B800 | ENUM_NOTIPACKET_GROWTH_ITEM_CHANGE_DATA | 2*u8 LOOP{ u8, u16, u32, u16, u8 } |
| 0x1EC | 0x01161D80 | ENUM_NOTIPACKET_MINORITY_STATE | 4*u8 |
| 0x1F7 | 0x011838C0 |  | **[SWITCH]** 2*u8 LOOP{ 2*u32 } |
| 0x1F8 | 0x01175E10 |  | u16 |
| 0x210 | 0x01183BE0 |  | u8 LOOP{ u16, 4*u32, u8 } |
| 0x21A | 0x011752A0 |  | u8, 3*u32 |
| 0x21E | 0x01185740 |  | u8 LOOP{ u32, u8 } |
| 0x222 | 0x01162230 |  | u8 |
| 0x227 | 0x01164370 |  | u32, u8 |
| 0x22C | 0x0116E030 | ENUM_NOTIPACKET_FIGHT_DOLL_LIST | u8 LOOP{ u16, u8 } LOOP^2{ u32 } |
| 0x22D | 0x01161D20 |  | u32 |
| 0x233 | 0x01175EF0 | ENUM_NOTIPACKET_AVATAR_SPEC_EVENT_INFO | 2*u8 |
| 0x235 | 0x01160800 |  | u32, u8, 2*u32 |
| 0x237 | 0x01162330 | ENUM_NOTIPACKET_RETURN_USER_REWARD_STATE | u8 |
| 0x23E | 0x01168C10 |  | u8 |
| 0x23F | 0x01162440 |  | u32 LOOP{ u16, u32 } |
| 0x24F | 0x011834B0 |  | u32 LOOP{ u8 } |
| 0x250 | 0x01184BD0 |  | **[SWITCH]** 2*u32, u8 |
| 0x251 | 0x01164860 |  | u16 LOOP{ u16, u8 } |
| 0x252 | 0x01164A60 |  | u8, u16, u8 |
| 0x253 | 0x011734B0 |  | (no read-primitive calls) |
| 0x257 | 0x011602E0 |  | u32 LOOP{ u16, u8, u16 } |
| 0x258 | 0x0116C2C0 |  | (no read-primitive calls) |
| 0x259 | 0x01186EC0 |  | 2*u8 LOOP{ u16, 2*u8, u32, u16 } |
| 0x25A | 0x011764F0 |  | 2*u8, u32, u16, 2*u8 |
| 0x25B | 0x01162630 |  | u32, u8, u32 |
| 0x262 | 0x01160560 |  | u16, 4*u8, u32, 4*u8, u32 |
| 0x263 | 0x0117A820 |  | u32 LOOP{ 3*u32 } |
| 0x265 | 0x01162600 |  | (no read-primitive calls) |
| 0x270 | 0x01162D90 |  | u32, 8*u8 |
| 0x271 | 0x01162C00 |  | 3*u8 |
| 0x272 | 0x01162CF0 |  | u16, 3*u8 |
| 0x273 | 0x01168C70 |  | (no read-primitive calls); parsing delegated to 0x00442570 |
| 0x274 | 0x01162750 |  | u8 LOOP{ 2*u32 } |
| 0x279 | 0x01162B60 |  | 2*u32 |
| 0x286 | 0x01170F00 |  | **[SWITCH]** 2*u32 LOOP{ 2*u32 } LOOP^2{ 3*u32 } u32 LOOP{ u8 } |
| 0x287 | 0x01162BA0 |  | u32 |
| 0x28A | 0x01176C50 |  | u32 |
| 0x2A2 | 0x01162BC0 |  | u8 |
| 0x2BF | 0x01162F00 | ENUM_NOTIPACKET_STORY_BOOK_INFO | u32 LOOP{ bytes[48] } |
| 0x2C2 | 0x01163020 |  | 2*u8 LOOP{ 2*u32 } |
| 0x2DE | 0x01163160 |  | u8 |
| 0x2E7 | 0x01164480 |  | u8 LOOP{ 2*u32, 2*u8 } LOOP^2{ 2*u8 } |
| 0x2EA | 0x0116A9D0 |  | **[SWITCH]** 2*u8, u16 |
| 0x2EB | 0x0116AD70 |  | u32 LOOP{ u16, 2*u8, u16, 2*u8, bytes[1], bytes[30] } |
| 0x2EC | 0x011835F0 |  | u32 LOOP{ u8 } |
| 0x2F4 | 0x011782D0 |  | u16, u8 |
| 0x2F5 | 0x01173700 |  | u32 LOOP{ u16, u8 } |
| 0x2F6 | 0x01164DD0 |  | bytes[40] |
| 0x2FC | 0x01168E80 |  | u32 |
| 0x313 | 0x01163310 |  | bytes[var] |
| 0x314 | 0x02231720 |  | *(bare `ret` at 0x02231720 - registered but not implemented)* |
| 0x31A | 0x01176E30 | ENUM_NOTIPACKET_PVP_TOTAL_MATCH_CHARACTER_INFO | **[SWITCH]** u8 LOOP{ u16, u8 } LOOP^2{ u8 } |
| 0x31B | 0x01164630 |  | 3*u8 |
| 0x31C | 0x01177B90 |  | u8 LOOP{ u32 } LOOP^2{ 2*u8 } LOOP{ u32 } |
| 0x31D | 0x01168F60 |  | LOOP{ u32, u8 } |
| 0x31E | 0x011632A0 |  | (no read-primitive calls) |
| 0x322 | 0x0116E2C0 |  | LOOP{ u8 } LOOP^2{ u8, u16, 2*u8 } LOOP^3{ u8 } |
| 0x327 | 0x01185510 |  | u32 |
| 0x328 | 0x0116FE00 |  | u32, u16 |
| 0x329 | 0x01163EE0 | ENUM_NOTIPACKET_EVENT_ONTIME | u8, 2*u16 |
| 0x32A | 0x01164010 | ENUM_NOTIPACKET_EVENT_DNFTREND_STATE | bytes[14] |
| 0x33B | 0x01163E80 |  | u32 |
| 0x33D | 0x011617B0 |  | u32 |
| 0x340 | 0x01168090 | ENUM_NOTIPACKET_SAFE_MODE | u8, u32 |
| 0x343 | 0x01161960 | ENUM_NOTIPACKET_SERVER_BROADCAST_TIME_MESSAGE | u32 |
| 0x344 | 0x01161800 | ENUM_NOTIPACKET_BUY_RESTRICT_ITEM_LIST | u32 LOOP{ 3*u32 } |
| 0x366 | 0x01161CE0 |  | u32 |
| 0x367 | 0x01163EB0 |  | u32 |
| 0x371 | 0x011620F0 |  | u8, 4*u32 |
| 0x383 | 0x01161D50 |  | (no read-primitive calls) |
| 0x385 | 0x01183780 |  | u8, u16 LOOP{ bytes[var] } |
| 0x386 | 0x01168B30 |  | u32 |
| 0x38C | 0x01183ED0 | ENUM_NOTIPACKET_INSPECTION_NOTICE | 2*u8 |
| 0x38D | 0x01162220 |  | (no read-primitive calls) |
| 0x3A3 | 0x011643E0 |  | u8, u32 |
| 0x3C7 | 0x011760F0 |  | 3*u32, bytes[var], u32, bytes[var] |
| 0x3E9 | 0x01184200 | ENUM_NOTIPACKET_SAO_CURRENT_FLOOR | 2*u32 |
| 0x3F3 | 0x01162540 |  | u32 |
| 0x3F5 | 0x011763C0 |  | 2*u32 |
| 0x3FE | 0x01162510 |  | u32 |
| 0x400 | 0x011626D0 |  | u8 |
| 0x401 | 0x01168D60 |  | u16 |
| 0x40B | 0x01168DF0 |  | bytes[4] |
| 0x41D | 0x01162700 |  | bytes[4] |
| 0x432 | 0x01162270 |  | u8 LOOP{ u32, 2*u16 } |
| 0x434 | 0x011627E0 |  | 5*u32 |
| 0x440 | 0x01162B30 |  | u32 |
| 0x465 | 0x011630B0 |  | u8, u32 LOOP{ 2*u32 } |
| 0x49A | 0x01163260 |  | u32, u8 |

---

## 5. What is NOT pinned

* 167 of the 201 real handlers carry no `NOTIFUNC_*` literal, so their symbolic ENUM
  name is unknown. Their type codes are exact regardless.
* 20 of the 201 make no read-primitive call at all.
* 19 of the 201 are flagged **[SWITCH]**.
* No field *semantics* are claimed anywhere in this document. Widths and order only.
* For `[SWITCH]` rows the linear order is not trustworthy (see above).
* Of the 20 zero-read handlers, two visibly delegate
  (0x01160D70 -> 0x01389E50, 0x01168C70 -> 0x00442570); the rest are zero-payload
  notifications.
* `bytes[var]` lengths were not resolved to constants.
* `set_remaining` (0x027C4350) is called by no handler in this file.

## 6. Reproduction

Scripts left in the scratchpad:

* `_reg.py 0x0163C4A0 out.txt` - decode every `Register(type, fn, 0)` call site in the
  image (943 of them, all files).
* `_reg_etc.txt` - the 228 rows belonging to 0x011885F0.
* `_p1w.py` - flow-following disassembler: follows conditional targets, jump tables
  and tail `jmp`s, records read-primitive calls and backward-branch regions.
* `_final.py` / `_final.json` - the joined table rendered above.
* `_names.py` / `_names.txt` - `NOTIFUNC_*` literal -> owning function.
* `_etc_scan.py` - the negative result in 2.3 (no static pointer array).
