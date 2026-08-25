# GameCoreNetwork_NOTI_Dungeon.cpp - handlers, wire formats, dispatch

DNF S4A21 client, unpacked-from-memory image, image base 0x00400000.
All addresses are VAs in that image.

## 1. Where the file is

`func-source-file.csv` anchors for `DNFClient\RDAR\GameCoreNetwork_NOTI_Dungeon.cpp`:
0x01146000, 0x0114D900, 0x0114F380, 0x01151CC0, 0x01154810, 0x01157A70, 0x0115CAA0.

Neighbours: `GameCoreNetwork_CMD_Town.cpp` ends with its registration function at
0x01144AF0, whose `ret` is at **0x01145E3B**; `GameCoreNetwork_NOTI_Etc.cpp` starts
at **0x01160090**. The NOTI_Dungeon .obj therefore occupies
**0x01145E40 - 0x0116008F**, ending with its own registration function at
0x0115F540 (`pop esi; ret` at 0x01160088).

Handler 0x01145E40 (type 0x5) sits below the lowest `__FILE__` anchor but inside
that range, and is registered by this file's registration function, so it is
counted as part of the file.

Caution for anyone re-deriving this: `functions.csv` spans in this region are
merged blobs - the entry at 0x01146000 claims 26576 bytes but the real function
ends at 0x01146130. Real function starts were recovered from 0xCC padding runs +
call-rel32 targets + the registration table. `parsers.csv` read counts in this
region are per-blob, not per-handler.

## 2. THE DISPATCH

There is no jump table and no array of function pointers. Dispatch is a
`std::map<u16, {handler, flag}>` filled by six registration functions, one per
`GameCoreNetwork_*.cpp`.

```
0x0163C4A0  Register(this=map, u16 type [ebp+8], void* handler [ebp+0xC], int flag [ebp+0x10])
            allocates an 8-byte node {handler, flag}, inserts under `type` via the
            map-insert at 0x0163C2C0, then node[+4] = flag.  943 direct call sites
            image-wide - essentially every packet handler in the client.

0x0163C410 / global 0x03A5BE68 : map A  ("CMD" side)
0x0163C550 / global 0x03A5BE74 : map B  ("NOTI" side)   <-- ours

0x0163A850  ctor -> 0x0163C190 (fills map A) and 0x0163C4F0 (fills map B)
  0x0163C190: 0x01144AF0 CMD_Town(269) -> 0x0110C2C0 CMD_Dungeon(47) -> 0x01118BC0 CMD_Etc
  0x0163C4F0: 0x0119A780 NOTI_Town(156) -> 0x0115F540 NOTI_Dungeon(156) -> 0x011885F0 NOTI_Etc(228)

*** 0x0115F540 = GameCoreNetwork_NOTI_Dungeon.cpp registration function ***
    156 consecutive Register() calls, call sites 0x0115F54C .. 0x01160082.
    Per entry:   push 0 ; push <handler> ; push <type> ; mov ecx,esi ; call 0x0163C4A0
    Stride 0x10 bytes when the type fits in imm8, 0x13 bytes for imm32 types.
```

### Lookup / invoke

```
0x01637680  map A (CMD) : movzx esi, word ptr [pkt+1] -> 0x0163C410 -> find 0x0163C510
                          call [node+0] with (type, a, b, flag) ; add esp,0x10
0x016377E0  map B (NOTI): same key                    -> 0x0163C550 -> find 0x0163C510
                          call [node+0] with (type, flag)        ; add esp,8    <-- ours
```

Every NOTI_Dungeon handler is therefore `void __cdecl f(int type, int flag)`, and
the registered flag is 0 for all 156. The payload is NOT an argument - handlers
read it from the global stream cursor at 0x03BAAE38, remaining at 0x03BAAE3C.

### Frame, and where the handler cursor starts

Receive path, 0x0163BBD0:

```
  0x0163BD63  movzx edx, word ptr [edi+1]   ; u16 packet type      (header + 1)
  0x0163BC16  mov   ecx, [edi+3]            ; u32 total length     (header + 3)
  0x0163BD72  lea   eax, [edi+0xF]          ; body starts at header + 0x0F
  0x0163BEB2  call  0x027C4350              ; set_remaining(totalLen - 0x0F)
  0x0163BECC  movzx ecx, byte ptr [edi]     ; header byte 0 selects the map:
                == 1 -> 0x0163C078  call 0x01637680   (CMD,  map A)
                == 0 -> 0x0163C13D  call 0x016377E0   (NOTI, map B)
```

On the CMD path the framework consumes 1-2 body bytes itself first (read_u8 at
0x0163BF30, plus a second read_u8 at 0x0163BF73 when the first byte is 0).
On the **NOTI path it does not**, so every wire format below starts at body
offset 0.

## 3. Read primitives - corrected addresses

The task brief gave RVAs. The real VAs in this image are +0x400000. Using the
brief's numbers verbatim yields zero reads for every handler.

| brief (RVA) | actual VA | signature | effect |
|---|---|---|---|
| 0x023C5230 | **0x027C5230** | `read_u8(void* dst [, int 1])` | 1 byte, remaining -= 1 |
| 0x023C52A0 | **0x027C52A0** | `read_u16(void* dst)` | 2 bytes |
| 0x023C5310 | **0x027C5310** | `read_u32(void* dst)` | 4 bytes |
| 0x023C5380 | **0x027C5380** | `read_bytes(void* dst, int len)` | len bytes |
| 0x023C4350 | **0x027C4350** | `set_remaining(int)` | writes 0x03BAAE3C |

read_u8 is usually called with a redundant second argument (`push 1; push dst`);
the body only ever moves one byte. No handler in this file calls set_remaining.

## 4. Handlers - 156 registrations, wire format of each

Notation:

* `N(u8@VA) x { ... }` - loop whose trip count is the value read at VA, i.e. a
  length-prefixed array. The count read is listed inline just before it.
* `N? x { ... }` - a real back-edge loop whose bound could not be traced to an
  earlier read. Count unknown, array present.
* `bytes[n]` - `read_bytes` with literal length n (a fixed-size struct blob).
* `[sub VA: ...]` - reads performed inside a callee, inlined one level.

Method: recursive-descent walk of each handler (conditional branches and MSVC
switch jump tables followed), reads emitted in address order, loops from
back-edges (edges sharing a head merged, partially-overlapping ranges merged),
trip count resolved by matching the loop `cmp` operand stack slot to the slot an
earlier read wrote.

| type | handler | name / purpose | wire format (body offset 0 onward) |
|---|---|---|---|
| 0x5 | 0x01145E40 |  | `u16 N(u16@01145E51) x { u32 u8 }` |
| 0x1C | 0x01151E10 | NOTIFUNC_ENUM_NOTIPACKET_DUNGEON_INFO | `u32 u8 u8 u8 u8 N(u8@01151FA0) x { u8 u8 u8 u8 N(u8@01152522) x { u8 N(u8@01152555) x { u8 u8 } } } u16 u16 u8 u8 u32 u8 u8 u8 u8 u8 u8 u8 N(u8@011532D2) x { u32 u8 u8 } u8` |
| 0x1D | 0x01157B30 | NOTIFUNC_ENUM_NOTIPACKET_START_MAP | `u8 u8 u8 u32 u8 u8 u32 u8 u32 u8 u16 u32 u16 u32 u8 u8 u8 u8 u32 u8 u8 N(u8@0115843A) x { u8 u16 u32 u32 u16 u8 u16 u16 u8 } u8 u8 N(u8@011585FB) x { u8 N(u8@01158624) x { u32 u32 u32 u32 u32 } } u8` |
| 0x1E | 0x0114CAF0 |  | `u32 u8` |
| 0x1F | 0x01149950 | NOTIFUNC_ENUM_NOTIPACKET_ENABLE_CLEAR_DUNGEON | `(no reads - zero-length body)` |
| 0x20 | 0x0114F420 | NOTIFUNC_ENUM_NOTIPACKET_DIE_STATE | `u16 u8 u8` |
| 0x21 | 0x011485B0 |  | `u8` |
| 0x22 | 0x01149C30 |  | `u8 u32 u8 u8 u8 u8 N(u8@01149D35) x { u16 u32 u8 }` |
| 0x23 | 0x0115CF80 |  | `u32 u32 u32 u32 u8 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u32 u8 N(u8@0115D415) x { u32 u32 } u8 N(u8@0115D4B1) x { u32 u32 } u32 u32 u32 u32 u32 u32 u32 N(u32@0115D643) x { u32 } u32 N(u32@0115D835) x { u32 u32 } u8 N(u8@0115D898) x { u16 u32 u32 u16 u8 u16 } N(u16@0115D903) x { u8 N(u8@0115D9FF) x { u32 u32 } } u32 N(u32@0115DAC2) x { u8 N(u8@0115DAEB) x { u32 u32 } } N(u32@0115DB16) x { u8 N(u8@0115DB89) x { u32 u32 } } u32 u32 u8 u8 u32 u32 u32` |
| 0x26 | 0x01154840 | NOTIFUNC_ENUM_NOTIPACKET_DIE_MONSTER | `u16 u8 N(u8@011548D1) x { u16 u32 u8 u32 u16 u8 u16 u32 [sub 01637A80: u8 N(u8@01637A98) x { u32 }] [sub 012612F0: u16] [sub 01088BC0: u8 N? x { u8 u8 u8 } u8 u8 u8 u8 u8 u8] u8 u8 u8 u16 u8 u8 u8 u8 u8 u8 u8 u16 u16 } u8 u16 u8 u8 u8 u8 u8 u16 u16` |
| 0x57 | 0x0114CFB0 |  | `u16 N(u16@0114CFEC) x { u16 u8 u8 u8 u8 u32 N(u32@0114D072) x { u8 u16 } }` |
| 0x58 | 0x01146140 |  | `u16 N(u16@0114615A) x { u16 u8 u16 }` |
| 0x59 | 0x01149F00 |  | `u16 u8 N(u8@01149F20) x { u8 u8 u16 }` |
| 0x5A | 0x01146260 |  | `(no reads - zero-length body)` |
| 0x5B | 0x01146270 |  | `u8 u16 N(u16@0114628F) x { u16 u8 u8 u8 u8 u16 }` |
| 0x5C | 0x01146600 |  | `u8 u8 u32 u32 u32 u32 u32 u32 u32 u32` |
| 0x5D | 0x0114A1A0 | ITEM_PICK | `u16 u16 u32 u16` |
| 0x5F | 0x0114DCE0 | COIN_INSERT | `u8 u8 u8 u16 u16` |
| 0x60 | 0x01146770 |  | `u8 N(u8@01146787) x { u8 u8 } u8 N(u8@011467E6) x { u8 N(u8@011467E6) x { u32 u32 } }` |
| 0x64 | 0x0114F130 |  | `u16` |
| 0x67 | 0x01146850 |  | `u32 u32` |
| 0x6A | 0x01146890 |  | `u16 u16` |
| 0x6B | 0x01146950 |  | `u16` |
| 0x73 | 0x0114D450 |  | `u8 u8 u16 u8` |
| 0x78 | 0x0114D940 |  | `u8 u8` |
| 0x7A | 0x01153600 |  | `u8 u32 u32 u16 N(u16@011536BD) x { u16 u16 u32 u32 }` |
| 0x80 | 0x011595B0 |  | `u32 u8 u8 u8 u16 u16 u8 N(u8@0115965B) x { u8 u8 u16 } u8 N(u8@0115975D) x { u16 }` |
| 0x81 | 0x01146A30 |  | `u16` |
| 0x82 | 0x01146A70 |  | `(no reads - zero-length body)` |
| 0x83 | 0x01146B50 |  | `u16` |
| 0x84 | 0x01146BA0 |  | `u8 u8 u16` |
| 0x85 | 0x0114A710 |  | `u16` |
| 0x86 | 0x01146C70 |  | `u16 N? x { u32 u8 u16 u16 u16 N(u16@01146CDD) x { u16 u8 } }` |
| 0x87 | 0x01146DA0 |  | `u16` |
| 0x88 | 0x0114A800 |  | `u16 u32` |
| 0x89 | 0x011486B0 | 무법성향 = %d | `u8` |
| 0x8E | 0x0114A8F0 |  | `u32 u16 u8 u8` |
| 0x8F | 0x01159850 |  | `u16 u32 u32 u8 u32 u16 u32 u8 u8 u8 u8 u8 N(u8@01159A71) x { u8 u16 u32 u32 u16 u8 u16 u16 }` |
| 0x90 | 0x01153A40 |  | `u8 u32 u32 u8 u32 u8 N(u8@01153B40) x { u8 u8 } u16 u32 u32 N(u16@01153C97) x { N(u32@01153CAF) x { u8 u8 } u16 u32 u32 }` |
| 0x91 | 0x01148850 |  | `u32 N(u32@0114887D) x { u8 N(u8@011488D1) x { u32 u32 } }` |
| 0x92 | 0x01146DE0 |  | `u8` |
| 0x98 | 0x0114AEA0 |  | `u8 u16 u16 N? x { u8 } u8` |
| 0x9B | 0x01146E30 |  | `u32 u32` |
| 0x9F | 0x00681CF0 |  | `(no reads - zero-length body)` |
| 0xA0 | 0x0114B320 | STAMINA_RECOVER | `u8 u16 N(u16@0114B33F) x { u16 }` |
| 0xA1 | 0x01146E70 |  | `u8 u8 u8 u8` |
| 0xA2 | 0x01146ED0 |  | `u8 u32 u32` |
| 0xA3 | 0x01146F20 |  | `u32` |
| 0xAA | 0x011489A0 |  | `u16 u8 u8` |
| 0xB0 | 0x01146F50 |  | `u8` |
| 0xB6 | 0x0114B480 |  | `u8 N(u8@0114B4A6) x { u8 u8 u8 u32 N(u8@0114B57C) x { u32 } }` |
| 0xBC | 0x01146F90 |  | `u8` |
| 0xC8 | 0x01146FD0 |  | `u8` |
| 0xC9 | 0x01147000 |  | `u32 u32 u32 u32 u32` |
| 0xCF | 0x0114B910 |  | `u32` |
| 0xD0 | 0x01147070 |  | `u32 u32 u32 u32 u32 u32` |
| 0xD1 | 0x01148A60 |  | `u8 u8 N(u8@01148AB0) x { u8 N(u8@01148AF7) x { u32 u32 } }` |
| 0xD2 | 0x01147150 |  | `u16 N(u16@01147164) x { u8 u16 u32 u8 u8 u16 u16 u16 u16 } u16` |
| 0xD3 | 0x01159ED0 |  | `u8 u8 u32 u8 u8 u32 u8 N(u8@0115A1C9) x { u16 u8 u8 u16 u16 u16 u8 u16 u32 u8 u8 u8 u8 } u8 N(u8@0115A662) x { u8 u16 u32 u32 u16 u8 u16 u16 } u8` |
| 0xD5 | 0x011472E0 |  | `u32 u8` |
| 0xD6 | 0x01147330 |  | `u8 u8 u32 u32` |
| 0xD7 | 0x011473C0 |  | `u8 u32` |
| 0xD8 | 0x01147420 |  | `u8 u16 N(u16@01147447) x { u16 }` |
| 0xDC | 0x011474A0 |  | `u32` |
| 0xDD | 0x011474D0 |  | `u32 u32` |
| 0xDF | 0x01147510 |  | `u8 u32 u32 u32 u32 u32 u32` |
| 0xE0 | 0x01156410 |  | `u8 u32 u8` |
| 0xE3 | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0xE6 | 0x011475A0 |  | `u8` |
| 0xEB | 0x011475D0 |  | `u16` |
| 0xFE | 0x01147600 |  | `u8 u8` |
| 0xFF | 0x01148B90 |  | `u32` |
| 0x103 | 0x011476D0 |  | `(no reads - zero-length body)` |
| 0x109 | 0x01147700 |  | `u32 u32 u32 u32` |
| 0x10C | 0x01147760 |  | `u16` |
| 0x114 | 0x01147790 |  | `(no reads - zero-length body)` |
| 0x115 | 0x011477A0 |  | `(no reads - zero-length body)` |
| 0x117 | 0x011477E0 |  | `u32` |
| 0x118 | 0x01153F30 |  | `u32 N(u32@01154063) x { u32 u8 u32 u32 u32 u32 u8 }` |
| 0x11D | 0x01148DB0 |  | `u8 u8 u32 u8 u32 u8 u16` |
| 0x125 | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0x126 | 0x0114BAB0 |  | `u32 u32 u16 u16` |
| 0x12A | 0x01147830 |  | `u32 u8` |
| 0x12D | 0x011478B0 |  | `(no reads - zero-length body)` |
| 0x154 | 0x01146430 |  | `u32 u16 u32 u16` |
| 0x159 | 0x01146390 |  | `u8 N(u8@011463AC) x { u8 u32 }` |
| 0x15D | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0x161 | 0x01149450 | NOTIFUNC_ENUM_NOTIPACKET_CHANGE_HOST_WARROOM | `u8` |
| 0x17D | 0x0115CB10 | NOTIFUNC_ENUM_NOTIPACKET_SECURITY_REWARD | `u8 u16 N(u16@0115CB61) x { u16 u32 u32 }` |
| 0x183 | 0x01156690 |  | `u32 u8 u8 N(u8@011566DF) x { u8 u8 N(u8@0115670C) x { u8 N(u8@011566DF) x { u32 u16 } } } N(u8@011566DF) x { u8 u16 u32 u8 u8 }` |
| 0x184 | 0x0115AB20 |  | `u8 u8 u32 u8 u8 u32 u8 N(u8@0115AC55) x { u16 u32 u16 u16 u8 u8 u8 u8 } u8 N(u8@0115ADC3) x { u8 u16 u32 u32 u16 u8 u16 u16 } u8` |
| 0x185 | 0x01149030 |  | `u8 u32 u8 N? x { u16 u32 u32 u16 u8 u8 } u8 N(u8@01149102) x { u32 u32 u16 } u8 N(u8@01149157) x { u32 u32 u16 }` |
| 0x187 | 0x011478F0 |  | `(no reads - zero-length body)` |
| 0x193 | 0x01147900 |  | `(no reads - zero-length body)` |
| 0x1C0 | 0x0115B0C0 |  | `u8 u8 N(u8@0115B122) x { u8 u8 N(u8@0115B1BE) x { u16 u8 N(u8@0115B270) x { u16 } } }` |
| 0x1D5 | 0x0115CDC0 |  | `u8 N(u8@0115CDF7) x { u32 u32 }` |
| 0x1D6 | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0x1D7 | 0x01147F80 |  | `(no reads - zero-length body)` |
| 0x1D8 | 0x01147E80 |  | `u8 N(u8@01147E8D) x { u16 u8 u32 }` |
| 0x1D9 | 0x01147EE0 |  | `u8 N(u8@01147EED) x { u16 u8 u32 }` |
| 0x1DA | 0x01147F60 |  | `u8` |
| 0x214 | 0x01147FB0 |  | `u8` |
| 0x224 | 0x0115E070 | errorName | `u32 N(u32@0115E0AC) x { u16 u8 u8 u8 u32 } u8 u32` |
| 0x22E | 0x0114C430 |  | `u16` |
| 0x22F | 0x0114C580 |  | `u16 N(u16@0114C59D) x { u8 u8 u32 }` |
| 0x238 | 0x01150E20 |  | `u32 u8 u32` |
| 0x247 | 0x01150FD0 |  | `u32 u16` |
| 0x248 | 0x01148040 |  | `u8 N(u8@0114804D) x { u32 u32 }` |
| 0x249 | 0x011515B0 |  | `u8 N(u8@01151633) x { u32 u8 u8 N(u8@01151673) x { u16 } }` |
| 0x24A | 0x011480B0 |  | `u8 u8 N(u8@011480C8) x { u32 u8 } u32` |
| 0x24B | 0x01149520 |  | `u32 u8 u8` |
| 0x24C | 0x0115E9A0 |  | `u8 u8` |
| 0x24D | 0x01148180 |  | `u8` |
| 0x24E | 0x01148200 |  | `u16 u8` |
| 0x255 | 0x0114EFE0 |  | `u8 u32 u32 u32` |
| 0x256 | 0x01148140 |  | `u8 u32` |
| 0x25C | 0x01148240 |  | `u32 u32` |
| 0x266 | 0x01151170 |  | `u8 N(u8@011511B6) x { u32 }` |
| 0x267 | 0x011512F0 |  | `u8 N(u8@01151336) x { u8 } u8 u8` |
| 0x276 | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0x278 | 0x01148390 |  | `u32` |
| 0x27A | 0x0115EEA0 |  | `u8 N(u8@0115EECE) x { u8 u8 N(u8@0115EF01) x { u16 u16 u32 u32 } }` |
| 0x27B | 0x0115F1D0 |  | `u8 N(u8@0115F1FE) x { u16 u8 N(u8@0115F26F) x { u16 } u32 u8 N(u8@0115F3DD) x { u32 } }` |
| 0x282 | 0x011495C0 |  | `u32 u32` |
| 0x2A7 | 0x011483C0 | NOTIFUNC_ENUM_NOTIPACKET_HELL_PARTY_MONSTER_INFO | `u32 N(u32@011483D1) x { u32 u32 }` |
| 0x2B1 | 0x01149620 |  | `u8 u32 u32 u32 u32 u16 u16 u16 u16` |
| 0x2B2 | 0x011541D0 |  | `u8 u32 u32 N(u32@011542FB) x { u32 }` |
| 0x2E8 | 0x011484F0 | NOTIFUNC_ENUM_NOTIPACKET_EPIC_PIECE_EXCHANGE_NPC | `u8` |
| 0x2F7 | 0x011575D0 |  | `u16 N(u16@011575FE) x { u32 u32 u32 }` |
| 0x2F8 | 0x01148460 |  | `u8` |
| 0x2F9 | 0x011497B0 |  | `u8 u32 u32 u32 u32 u32` |
| 0x2FA | 0x01148490 |  | `u8` |
| 0x2FB | 0x011484C0 |  | `u8` |
| 0x30E | 0x01148580 |  | `(no reads - zero-length body)` |
| 0x319 | 0x01157720 |  | `u32 N(u32@01157782) x { u16 u8 u32 u32 u32 }` |
| 0x321 | 0x01149240 |  | `u8 N(u8@0114929A) x { u8 u32 bytes[var] }` |
| 0x323 | 0x0115B560 |  | `u8 N(u8@0115B5B8) x { u8 u8 u8 N(u8@0115B606) x { u8 u8 N(u8@0115B6A6) x { u16 u8 N(u8@0115B757) x { u16 } } } }` |
| 0x32B | 0x0114C2B0 | NOTIFUNC_ENUM_NOTIPACKET_EVENT_DNFTREND_REWARD_STATE | `bytes[1]` |
| 0x32C | 0x01149340 | NOTIFUNC_ENUM_NOTIPACKET_EVENT_DNFTREND_ALL_REWARD_STATE | `bytes[24]` |
| 0x333 | 0x0114E590 | NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_INFO | `u32 u16 u8 u8` |
| 0x334 | 0x0115BA20 | NOTIFUNC_ENUM_NOTIPACKET_START_BOSS_TOWER_MAP | `u16 u32 u32 u8 N(u8@0115BB1C) x { u32 u16 u32 u8 u8 u8 u8 } u8 N(u8@0115BE5C) x { u8 u16 u32 u32 u16 u8 u16 u16 }` |
| 0x335 | 0x01147910 | NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_STATE_REWARD | `u32 N? x { u8 N(u8@01147A6C) x { u32 u32 } }` |
| 0x336 | 0x01147C00 | NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_STATE_EPLP | `u8` |
| 0x35B | 0x0114BE30 | NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_STAGE_INFO | `bytes[8]` |
| 0x35C | 0x0115C180 | NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_START_MAP | `bytes[20] u32 N(u32@0115C1D0) x { bytes[12] } u32 N(u32@0115C4E9) x { bytes[13] }` |
| 0x35D | 0x0114BCC0 | NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_SPAWN_UNIT | `bytes[38]` |
| 0x35E | 0x02231720 | shared no-op stub (6 retired types point here) | `(no reads - zero-length body)` |
| 0x35F | 0x01147CC0 |  | `bytes[8]` |
| 0x360 | 0x01147D10 | NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_EPLP_STATE | `bytes[5]` |
| 0x361 | 0x01150820 | NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_CLEAR_STAGE | `bytes[25] u32 N(u32@01150902) x { bytes[12] } u32 N(u32@01150956) x { bytes[12] } u32 N(u32@011509AA) x { bytes[5] } u32 N(u32@01150C55) x { bytes[12] } u32 N(u32@01150CA9) x { bytes[12] } u32 N(u32@01150D05) x { bytes[5] }` |
| 0x362 | 0x01156CA0 |  | `bytes[1] bytes[33] u32 N(u32@01156D77) x { bytes[8] } u32 N(u32@01156EBB) x { bytes[12] u32 N(u32@01157061) x { bytes[12] } u32 N(u32@011571FC) x { bytes[12] } u32 N(u32@0115737C) x { bytes[10] } u32 N(u32@011573C1) x { bytes[10] } u32 bytes[5] }` |
| 0x3BD | 0x01147FF0 |  | `u8 u32` |
| 0x3F6 | 0x011482B0 |  | `u16 u16 u16` |
| 0x3F7 | 0x01148340 |  | `u8` |
| 0x4B3 | 0x01149870 |  | `(no reads - zero-length body)` |

## 5. Independent confirmation of the type codes

Not derived from the registration table: 20 of these handlers embed their own log
tag literal (the client encrypts literals; these are the decrypted forms from
`literals.csv`). The tag name matches the type each is registered under, so the
mapping is not an artifact of my decoding of 0x0115F540.

| type | handler | embedded literal |
|---|---|---|
| 0x1C | 0x01151E10 | `NOTIFUNC_ENUM_NOTIPACKET_DUNGEON_INFO` |
| 0x1D | 0x01157B30 | `NOTIFUNC_ENUM_NOTIPACKET_START_MAP` |
| 0x1F | 0x01149950 | `NOTIFUNC_ENUM_NOTIPACKET_ENABLE_CLEAR_DUNGEON` |
| 0x20 | 0x0114F420 | `NOTIFUNC_ENUM_NOTIPACKET_DIE_STATE` |
| 0x26 | 0x01154840 | `NOTIFUNC_ENUM_NOTIPACKET_DIE_MONSTER` |
| 0x161 | 0x01149450 | `NOTIFUNC_ENUM_NOTIPACKET_CHANGE_HOST_WARROOM` |
| 0x17D | 0x0115CB10 | `NOTIFUNC_ENUM_NOTIPACKET_SECURITY_REWARD` |
| 0x2A7 | 0x011483C0 | `NOTIFUNC_ENUM_NOTIPACKET_HELL_PARTY_MONSTER_INFO` |
| 0x2E8 | 0x011484F0 | `NOTIFUNC_ENUM_NOTIPACKET_EPIC_PIECE_EXCHANGE_NPC` |
| 0x32B | 0x0114C2B0 | `NOTIFUNC_ENUM_NOTIPACKET_EVENT_DNFTREND_REWARD_STATE` |
| 0x32C | 0x01149340 | `NOTIFUNC_ENUM_NOTIPACKET_EVENT_DNFTREND_ALL_REWARD_STATE` |
| 0x333 | 0x0114E590 | `NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_INFO` |
| 0x334 | 0x0115BA20 | `NOTIFUNC_ENUM_NOTIPACKET_START_BOSS_TOWER_MAP` |
| 0x335 | 0x01147910 | `NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_STATE_REWARD` |
| 0x336 | 0x01147C00 | `NOTIFUNC_ENUM_NOTIPACKET_BOSS_TOWER_STATE_EPLP` |
| 0x35B | 0x0114BE30 | `NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_STAGE_INFO` |
| 0x35C | 0x0115C180 | `NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_START_MAP` |
| 0x35D | 0x0114BCC0 | `NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_SPAWN_UNIT` |
| 0x360 | 0x01147D10 | `NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_EPLP_STATE` |
| 0x361 | 0x01150820 | `NOTIFUNC_ENUM_NOTIPACKET_ADVANCE_ALTAR_CLEAR_STAGE` |

## 6. Caveats - read before building on this

* These type codes are keys into **map B (NOTI) only**. The same number means
  something else entirely in map A (CMD): 0x1C is ENUM_NOTIPACKET_DUNGEON_INFO
  -> 0x01151E10 in map B, and CMD_Town handler 0x01119CD0 in map A. Do not merge
  the two namespaces.
* Register() overwrites, and NOTI_Etc registers after NOTI_Dungeon, so a later
  registration could silently replace one of these. I checked: **no type**
  registered by 0x0115F540 is re-registered by NOTI_Town (0x0119A780), NOTI_Etc
  (0x011885F0), or by any of the 177 statically decodable dynamic
  `RegisterNotiHandler` (0x01637720) call sites. 125 of the 302 dynamic sites did
  not decode statically, so a collision there is not fully excluded.
* Type 0x255 -> 0x0114EFE0 is registered twice by this same function
  (call sites 0x0115FEA7 and 0x0115FEE0). Harmless duplicate in the source.
* 0x02231720 is a shared do-nothing stub reused for retired packet ids
  (0xE3, 0x125, 0x15D, 0x1D6, 0x276, 0x35E) - not a real handler.
* "(no reads)" means the walk reached no read primitive: a zero-length body, e.g.
  0x1F ENABLE_CLEAR_DUNGEON. Those functions do have bodies and call UI code; the
  packet simply carries no payload.
* The per-type name tables at 0x03B53230 (CMD, used at 0x0163C059) and 0x03B51F20
  (NOTI, used at 0x0163C11F), both indexed `[type*4]`, are all-zero in this dump -
  they are populated at runtime. Dumping them from a live client would name all
  156 types, not just the 20 with embedded tags.
* Loop counts marked `N?` (types 0x86, 0x98, 0x185, 0x335) and the single
  `bytes[var]` (type 0x321, resolved by hand below) are the only unresolved
  structure. For 0x321 the hand read is:
  `u8 count @0x0114929A; count x { u8 @0x011492AF; u32 len @0x0114930C; bytes[len] @0x01149316 }`.
