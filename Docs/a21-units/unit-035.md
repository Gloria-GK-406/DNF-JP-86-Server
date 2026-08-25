# TU 35 — `DNFClient\RDAR\CNSelectChannelSocket.cpp`

Briefing span `0x010BCF50`–`0x010BFE10`, 26 functions, 0 parsers.
Confidence: **high** on the wire protocol, layouts and crypto; medium on a few
field-name labels (marked inline).

---

## 0. What this translation unit is

It is the client's **channel-list ("TCLS") client socket** — a *separate,
short-lived, blocking TCP session*, distinct from the game socket
(`CNGameSocket.cpp`). It does exactly two jobs:

1. **`recvChannels()` (`0x010BF320`)** — open a socket to the channel-select
   server, run a 3-request / 3-response handshake, close the socket.
   The three exchanges are: negotiate a Rijndael session key, fetch the
   *channel script*, fetch the *channel info* (the live channel table).
2. **`applyChannelInfo()` (`0x010BFE10`)** — parse the fixed-width channel
   record stream out of the received blob and push each record into
   `ChannelManager` (TU 44, `DNFClient\RDAR\ChannelManager.cpp`).

Everything else in the file is the **channel-move state machine**: which
channel to connect to next, the retry cadence, the give-up timer, the
"return to previous channel" path, and the channel-move cutscene.

The **five inbound read primitives are not used at all** (`parsers = 0`) —
this socket does raw `ws2_32!recv` with its own 11-byte header, not the
game-protocol stream reader. A server implementer must treat this as a
second protocol.

### Span correction

The briefing's `hi` is **too low, not too wide**. `hi = 0x010BFE10` is the
*start* of `applyChannelInfo`, which runs to `0x010C04B0`; and its only caller
`0x010C04B0`–`0x010C054E` (the load driver, below) is also part of this file —
it calls nothing but `recvChannels`, `applyChannelInfo` and UI helpers.
Next function `0x010C0550` serializes an unrelated object (offsets
`0x138`..`0x154`) — that is the real file end.

Cross-check that it is all one file: the `__FILE__`+line pairs recovered run
215, 236, 453, 497, 510, 687, 970, 971, 990, 1003, 1072, 1131, 1139,
1847, 1900, 1932, 1991, 2104, 2113 — one ~2.2 kloc .cpp, no gaps that suggest
a second unit. Literal blob is contiguous `0x0322C4B4`–`0x0322DD38`.

---

## 1. Wire protocol (highest value for a server implementer)

### 1.1 Packet header — 11 bytes, both directions

Built by `0x010BCDC0` (header only) and `0x010BCDE0` (header + 20-byte body
prefilled with `0x01`).

| off | size | meaning | evidence |
|----|----|----|----|
| +0x00 | u8 | always 0, never validated | ctor `mov word [eax], 0` @`0x010BCDC2`; no reader |
| +0x01 | u8 | **message type** | `mov byte [ebp-0x63], 0x0B` @`0x010BD195`; checked `cmp al, 0x0C` @`0x010BD70E`, `cmp al, 0x12` @`0x010BE229` |
| +0x02 | u32 | **total length, header included** | `mov dword [ebp-0x62], 0x2B` @`0x010BD199` (11 hdr + 32 body = 43); reader does `size - 0x0B` @`0x010BD73C`, `0x010BE260` |
| +0x06 | u32 | always 0, never validated | ctor `mov dword [eax+6], 0` |
| +0x0A | u8 | **ACK / result**, signed | ctor sets 1 on requests; replies must satisfy `>= 1` — `cmp al,1 / jge` @`0x010BD6BE`, `0x010BD8D6`, `0x010BE1F2`, else `INVALID ACK(%d)` |

### 1.2 The three exchanges, in wire order

`recvChannels` sends **all three requests back-to-back, then reads all three
replies in order** (`0x010BF73C` → `0x010BF77F`). The server must be able to
handle pipelined requests, or at minimum must not require read-before-write.

| step | fn | type out | type in | body out | body in |
|---|---|---|---|---|---|
| 1 | `requestCryptKey` `0x010BD150` | `0x0B` | `0x0C` (checked) | 32 B encrypted | 36 B |
| 2 | `requestChannelScript` `0x010BD340` | `0x09` | **not checked** | — | zlib+AES blob |
| 3 | `requestChannelInfo` `0x010BDB20` | `0x11` | `0x12` (checked) | 24 B | zlib+AES blob |

#### Step 1 — `requestCryptKey` / `recvCryptKey` (`0x010BD670`)

Out: header(type `0x0B`, len `0x2B`) + 32 bytes =
`Rijndael128_ECB_Encrypt(key = bootstrap, plaintext = "dnf_game" zero-padded to 32)`.
- `"dnf_game"` literal `0x0322C62C`, copied into a 32-byte zeroed buffer @`0x010BD240`.
- Padded to the 16-byte block boundary by `0x02773940` @`0x010BD264`, then
  **32 bytes** (2 blocks) encrypted by `0x027AEAC0` @`0x010BD298`.

In: header(type must be `0x0C`; `len - 11` must be exactly **`0x24` = 36**,
else `INVALID SIZE(%d)` @`0x010BD73F`) then
- `u32 keyRet` — must be 0 or 1, else `INVALID KEY RET(%d)` @`0x010BD78E`
- `u8 key[32]`
If `keyRet == 0` the client re-keys the shared cipher with those 32 bytes
(`0x027AD770` @`0x010BD827`). If `keyRet == 1` it keeps the bootstrap key.

#### Step 2 — `requestChannelScript` / `recvChannelScript` (`0x010BD850`)

Out: header only, type `0x09`, len `0x0B`. Skipped entirely (returns true
without sending) if `ChannelManager::getInstance()->0xCEFBA0()` is already true
(`0x010BD372`) — i.e. the script is cached.

In: header (ACK checked, **type byte NOT checked**), `payload = len - 11`, must
satisfy `1 <= payload <= 0x20000` (`INVALID SIZE(%d)` @`0x010BD913`).
Then `payload` bytes are read and processed:
1. `zlib uncompress(dst[0x40000], &dstLen=0x40000, src, payload)` — `0x02C46650`
   is confirmed zlib `uncompress` (calls `inflateInit_` with ZLIB_VERSION at
   `0x0329FF6C`, `inflate(...,Z_FINISH=4)`, `inflateEnd`). Non-zero →
   `FAILED TO UNCOMPRESS(%d)`.
2. `dstLen % 16 != 0` → `INVALID COMP SIZE(%d)`.
3. `Rijndael decrypt(src, dst, dstLen)` — must return `0x6FFFFFFF`, else
   `FAILED TO DECRYPT(%d)`.
4. `ChannelManager::0xD00C90(plaintext)` parses it; false → `FAILED TO PARSE SCRIPT`.

**Server side ordering: plaintext → AES-encrypt → zlib-compress → send.**

#### Step 3 — `requestChannelInfo` / `recvChannelInfo` (`0x010BE190`)

Out: **35 bytes** (`0x23`), header + 24-byte body:

```
+0x00 u8   0
+0x01 u8   0x11
+0x02 u32  0x23              ; total 35
+0x06 u32  0
+0x0A u8   1
+0x0B char serverGroupName[20]   ; NUL-terminated, max 19 chars,
                                 ; UNWRITTEN TAIL BYTES ARE 0x01, not 0x00
+0x1F u32  serverGroupType
```
Evidence: `0x010BCDE0` prefills `+0x0B..+0x1E` with `0x01010101`;
`mov byte [ebp-0x33], 0x11` / `mov dword [ebp-0x32], 0x23` @`0x010BDB51`;
`mov [ebp-0x15], eax` writes the type at +0x1F @`0x010BDB69`; the ≤20-byte
copy loop into `[ebp-0x29]` = +0x0B @`0x010BDB90`; and the name comes from
`0x01649D80(serverGroupType)`, the enum→string table (§4).

In: header (type must be `0x12`), same size bound, then the exact same
**uncompress → block-size check → decrypt** chain as step 2. The plaintext is
then:
- `crc32(0, plaintext, len)` (`0x027C1550`, standard CRC-32, table at
  `0x03B897B0`) compared with global `[0x3A38898]`; the *changed* flag
  (`setne`) is handed to `ChannelManager::0xCEF9A0(bool)` @`0x010BE3FF`, and
  the new CRC stored. This is how the client detects "channel list changed".
- written into the global `std::stringstream` at `0x03979EC0` via
  `ostream::write` @`0x010BE441`; stream badbit/failbit → `FAILED TO WRITE()`;
  then `seekg(0)` @`0x010BE48C`.

### 1.3 The channel-info blob format (parsed by `applyChannelInfo`)

Read out of the stringstream with `std::istream::read` (`0x00E78380`), all
fields **fixed width, no delimiters, native little-endian**:

```
u16  fallbackServerGroupType          ; 0x010BFF19: read 2 bytes
repeat 1 time:                        ; loop bound is the literal 1
                                      ; (`cmp eax,1 / jl` @0x010C01E5)
  u32 channelCount                    ; 0x010BFF92: read 4 bytes
  repeat channelCount times:
    char  name[20]                    ; 0x010C0073: read 0x14
    u32   maxUsers                    ; 0x010C00D3: read 4
    u32   currentUsers                ; 0x010C00E4: read 4
    char  ip[16]                      ; 0x010C00F5: read 0x10, ASCII dotted-quad
    u32   port                        ; 0x010C0129: read 4, ONLY LOW 16 BITS USED
```

Per record = 20 + 4 + 4 + 16 + 4 = **48 bytes**. Total blob for N channels =
2 + 4 + 48N.

Notes a server must honour:
- `ip[16]` is a **text** dotted-quad; the client runs `ws2_32!inet_addr` on it
  (`0x010C0122`). Result is stored in network byte order.
- `port` is sent as u32 but only `word [ebp-0xB8]` is kept (`0x010C0145`), so
  the high 16 bits are ignored. Port is stored **host order** (no `htons` here;
  `htons` is applied later at connect time).
- `name[20]` is ANSI; the client converts to wide (`0x027BAD50`) and then
  **derives the channel number by concatenating the ASCII digits in the name**
  (`0x010BE500`: keeps chars in `'0'..'9'`, then `_wtoi`). So `"Channel 12"` →
  channel number 12. This number is the lookup key in `ChannelManager`.
- `maxUsers` / `currentUsers` labelling is evidence-based, not a guess:
  `ChannelManager::addInfo` logs `CHANNEL>> [%s (%d)] %d/%d` with
  `(name, channelNo, info+0x2C, info+0x28)` @`0x00CFE9B0`, and the CChannel
  accessors resolve to `capacity = this[0x98]` (`0x00CF0960`, vtable slot
  `0x6C`) and `current = this[0xA0]` (`0x00CF0A00`, slot `0x70`), with
  `loadPercent = current * 100.0f / max(capacity, 1)` (`0x00CEFD90`).
- `fallbackServerGroupType` (the leading u16) is passed to
  `ChannelManager::addInfo` as its second argument and is used **only when the
  per-record `serverGroupType` is not found in the local channel DB**
  (`0x00CFE79F`). Failure logs `[ %s, %d ch ] has no data in DB.`

The per-record `serverGroupType` is **not on the wire** — it is taken from the
client's own config for every record (`0x010BFF77`), so the server's records
are implicitly all one server group.

### 1.4 Guards / limits the server must stay inside

| limit | value | site |
|---|---|---|
| overall recv timeout | **10 000 ms** | `cmp eax, 0x2710` @`0x010BD47C` |
| recv chunk size | 256 B (`MSG_PEEK` 1-byte poll before each) | `0x010BD4A5`, `0x010BD486` |
| compressed payload | `1 .. 0x20000` (128 KiB) | `0x010BD913`, `0x010BE266` |
| decompressed payload | ≤ `0x40000` (256 KiB), must be a multiple of 16 | `0x010BD980`, `0x010BE2D2` |
| `applyChannelInfo` loop watchdog | 5 000 ms per iteration → `InfinitLoop_ServerCount` / `InfinitLoop_ChannelCount` | `cmp eax, 0x1388` @`0x010BFF64`, `0x010C0019` |

---

## 2. Cryptography (exact, reproducible)

The cipher object is the **global at `0x03A38A48`**, shared with the rest of the
client. `0x027AD770` is textbook **Rijndael** key expansion: S-box at
`0x035FDDA8`, four T-tables at `0x035FFFA8 / 0x036003A8 / 0x036007A8 /
0x03600BA8`, rcon at `0x03600FA8`, key and block each restricted to 16/24/32,
rounds 10/12/14, success sentinel `0x6FFFFFFF`, errors `0x70000001`..`0x7000000A`.

Init call from this unit (constructor `0x010BD020` @`0x010BD087`, and again in
`recvCryptKey` @`0x010BD827`) is:

```
MakeKey(keyBuf, keyBufLen = 0x20, chainBlock = 0x03601028,
        keyLen = 0x10, blockLen = 0x10, mode = 0, pad = 0)
```

- **key = the first 16 bytes** of the 32-byte buffer (`localKey[i] = buf[i % 0x20]`
  for i < 16 — `0x027AD840`).
- **block = 16 bytes**, i.e. AES-128.
- **mode = 0 → ECB.** `0x027AEAC0` branches: mode 1 = CBC (xor-then-encrypt),
  mode 2 = CFB, **anything else = raw per-block encrypt with no chaining**
  (`0x027AEBCA`). The chain block at `0x03601028` reads as 32 zero bytes in the
  dump anyway.
- Input length must be ≥ 1 and a multiple of 16, else `0x7000000A`.

**Bootstrap key** (before the server supplies one): the constructor copies the
literal `"Neople"` (`0x0322C5A0`) into a zeroed 32-byte buffer @`0x010BD069`,
so the initial AES-128 key is `4E 65 6F 70 6C 65 00 00 00 00 00 00 00 00 00 00`
(`"Neople"` + 10 zero bytes).

So the very first packet body a server sees is deterministic:
`AES128-ECB("Neople\0*10", "dnf_game" + 24 zero bytes)` — 32 bytes.

---

## 3. Object layouts

### 3.1 `CNSelectChannelSocket` singleton — instance at `0x03A38EE0`

Meyers singleton: `getInstance()` = **`0x010BD0E0`** (34 external callers),
once-flag bit 0 of `[0x03A38EF8]`, ctor `0x010BD020`, `atexit` thunk `0x02FFDB20`.

| off | size | init | meaning | evidence |
|---|---|---|---|---|
| +0x00 | u32 | 8 | **connectType** | `mov [esi],8` @`0x010BD0AD`; written from arg1 of `0x010BE6D0` @`0x010BE756`; set to 8 in `retryConnectFailprevChannel` @`0x010BF8D6`; compared `==6`/`==8` @`0x010BEBF5`,`0x010BEBFA` |
| +0x04 | u32 | 8 | unknown | `0x010BD0B3` |
| +0x08 | u32 | -1 | unknown | `0x010BD0BA` |
| +0x0C | u8 | **not set by ctor** | suppress channel-move cutscene (0 = play it) | `cmp byte [ebx+0xC], 0` @`0x010BE007`; writer not in this unit |
| +0x0E | u16 | 0 | pending "enter channel" target — arg2 of setter `0x010BCFD0` | `mov word [ecx+0xE], dx` @`0x010BCFDD`; consumed @`0x010BDC79`, `0x010BDD83` |
| +0x10 | u8 | 0 | pending "enter channel" expected value — arg1 of `0x010BCFD0`, compared against a locked global registry value | `mov byte [ecx+0x10], al` @`0x010BCFDA`; `cmp eax, edi` @`0x010BDD3C` |
| +0x14 | u32 | 0 | **tryCountConnect** (retry counter) | `add [esi+0x14], 1` @`0x010BEFC7` with log `tryCountConnect++`; reset to 0 @`0x010BDF43`; `cmp [esi+0x14], 2` @`0x010BEDEE` |

### 3.2 The stack struct handed to `ChannelManager::addInfo`

Built at `[ebp-0x98]` in `applyChannelInfo`; consumed by `0x00CFE740`.

| off | type | value | proof of the offset |
|---|---|---|---|
| +0x00 | u32 | serverGroupType (from config, clamped to ≤0xFA in addInfo) | `mov [ebp-0x98], edi` @`0x010C0156`; `mov byte[ebp-0x3c],al; cmp al,0xFA` @`0x00CFE77A` |
| +0x04 | u32 | channelNo (digits of the name) | `mov [ebp-0x94], eax` @`0x010C016F`; used as lookup key + logged as `%d ch` |
| +0x08 | `std::wstring` (0x1C) | channel display name | proxy `[ebp-0x90]`, `_Bx` `[ebp-0x8C]`, `_Mysize` `[ebp-0x7C]`, `_Myres` `[ebp-0x78]`=7 |
| +0x24 | — | padding / unused | never written |
| +0x28 | u32 | maxUsers | `[ebp-0x70]`; → `CChannel+0x98` @`0x00CFE8B3` |
| +0x2C | u32 | currentUsers | `[ebp-0x6C]`; → `CChannel+0xA0` @`0x00CFE91D` |
| +0x30 | u32 | IP (network order, from `inet_addr`) | `[ebp-0x68]`; → `CChannel+0xA8` @`0x00CFE984` |
| +0x34 | u16 | port (host order) | `[ebp-0x64]`; → `CChannel+0xAC` @`0x00CFE98E` |

### 3.3 `CChannel` (vtable `0x031B48F4`, 0xD0 bytes, ctor `0x00CF2D30`)

Only the parts this unit depends on:

| member / slot | meaning | evidence |
|---|---|---|
| `+0x04` | serverGroupType, default `0x109` | `0x00CF2D63` |
| `+0x98` | maxUsers (capacity) | vf `0x6C` = `0x00CF0960` |
| `+0xA0` | currentUsers | vf `0x70` = `0x00CF0A00` |
| `+0xA8` | IP | vf `0x74` = `0x028D6CF0` (`mov eax,[ecx+0xA8]`) |
| `+0xAC` | port (u16) | vf `0x78` = `0x00CEF940` |
| vf `0x04` | **isFull** | `0x00CEF790`: `capacity == 0 → true`, else `loadPercent > [0x039AF480] → true` |
| vf `0x7C` | loadPercent | `0x00CEFD90`: `current * 100.0f / max(capacity,1)` |
| vf `0x80` | load bucket | `0x00CEF950`: full→4, `>75%`→3, … |

### 3.4 `std::string` / `std::wstring` in this build

Iterator-debug layout, size **0x1C**: `+0x00` `_Myproxy`, `+0x04` `_Bx`
(16 bytes SSO), `+0x14` `_Mysize`, `+0x18` `_Myres` (0xF for char, 7 for wchar).
Proven by `applyChannelInfo` @`0x010BFE86`–`0x010BFEB0` and
`0x010C0061`–`0x010C006B`.

---

## 4. `SERVER_GROUP_TYPE` enum — complete, recovered

`0x01649D80` is `serverGroupTypeToString(int)`. Body: `dec eax; cmp eax,0x108;
ja default; jmp [eax*4 + 0x0164AD30]` — valid inputs **1..265**, everything else
→ `UNKNOWN_SERVER_GROUP_TYPE`. Table decoded directly from the image:

```
1  cain          6  hilder      10..259  china_1 .. china_250
2  diregie       7  ruke        260 roxy
3  siroco        8  seria       261 bakal
4  prey          9  anton       262 ozma
5  casillas                     263 khazan
                                264 starter_server
                                265 SERVER_GROUP_TYPE_MAX
                              other UNKNOWN_SERVER_GROUP_TYPE
```

`applyChannelInfo` aborts with `CHANNEL>> INVALID SERVER_GROUP_TYPE`
(`0x010BFF85`: `cmp edi, 0x109`) when the configured type equals **265 =
`SERVER_GROUP_TYPE_MAX`**. The count matches exactly (266 literals in the
function, 265 table slots + 1 default), so this identification is not a guess.

---

## 5. Channel-select server address — `getAddress` (`0x010BF020`)

`__fastcall(std::wstring* outAddr /*ecx*/, int* outPort)`. Default port
**`0x1B59` = 7001** (`0x010BF04D`). Switches on `config->0x00C71CC0()`, jump
table at `0x010BF304` (raw dwords read from the image):

| value | address | port |
|---|---|---|
| 0 | launcher-supplied address if `config->0x0279A8E0()`, else: local address starts with `"192.168.30."` → `"192.168.20.57"` port **8001** (`0x1F41`); otherwise `"192.168.20.8"` port 7001 | see cell |
| 1 | `"175.207.24.110"` | 7001 |
| 2 | `"175.207.24.70"` | 7001 |
| 3 | `"175.207.24.160"` | 7001 |
| >3 | `CHANNEL>> INVALID CHANNEL TYPE (%d)` → fail | — |

Then unconditionally: if `config->0x00E0CE70(0) == 0x63` (99), the launcher's
address/port override everything (`0x010BF1C1`), logged as
`CHANNELSERVER FROM LAUNCHER: CONNECT TYPE (%d) - %s (%d)`.

Socket options set by `recvChannels` before `connect`:
`setsockopt(SOL_SOCKET=0xFFFF, SO_DONTLINGER=0xFFFFFF7F, 0)`,
`setsockopt(0xFFFF, 1, 1)` — **the log calls this `TCP_NODELAY` but the level is
`SOL_SOCKET`, so it actually sets `SO_DEBUG`; a client bug, harmless**,
`setsockopt(0xFFFF, SO_REUSEADDR=4, 1)`.

---

## 6. Channel-move state machine (the server must mirror the timing)

Timers are the two objects `0x03A38A2C` (retry) and `0x03A38EC4` (give-up):
`0x027C3450(reset)`, `0x027C3C40(setInterval)`, `0x027C34D0(isEnabled)`,
`0x027C3780(isExpired)`.

| global | meaning |
|---|---|
| `0x03A38A1C` / `0x03A38A20` | last **successfully connected** channel ip / port |
| `0x03A38A24` / `0x03A38A28` | **pending** (requested) channel ip / port |
| `0x03A3889C` | "permit return to previous channel" flag |
| `0x03A38898` | CRC-32 of the last channel-info blob |
| `0x03A388A4` / `0x03A388A8` | tick at connect / tick at end of `recvChannels` |
| `0x03A388B0` | cumulative retry counter |
| `0x03A38EFC` | latch: infinite-loop already reported |
| `0x03979EB9` | cutscene-allowed flag |
| `0x03979EC0` | global `std::stringstream` holding the channel-info blob (streambuf at `0x03979ED8`) |

**`connectToChannel(type, ip, port)` — `0x010BE6D0`** (19 external callers, the
main public entry): stores pending ip/port, arms the **2 000 ms retry timer**
and the **25 000 ms give-up timer** (`0x7D0` @`0x010BE719`, `0x61A8`
@`0x010BE73B`), resets `tryCount`, sets `connectType`. If `type != 6` and the
cutscene flag is set, it allocates a 0x30-byte deferred-connect object
(vtable `0x0322CE60`, `+0x24 type / +0x28 ip / +0x2C port`) and replays
`connectToChannel` after the cutscene ends (`0x010BE900`).

**`connectChannel(ip, port)` — `0x010BE9F0`**: looks the channel up in
`ChannelManager`; missing → `CHANNEL>> channel is not exists (%s : %d)`;
`isFull()` → opens UI `0x271` and re-arms with `type = 3`; otherwise hands the
dotted-quad + port to `CNGameSocket` (`0x0274CA50` → `0x0274D680`, must return 1,
else `Connection Failed socket error code [ %d ]`). On success it promotes
pending → last, clears `0x03A3889C`, and opens the loading UI `0x209` unless
`connectType` is 6 or 8.

**`tryAutoConnecting()` — `0x010BED40`**: the per-frame pump.
Order: skip if already connected/connecting → if the 25 s timer expired, give up
(UI `0x209` string `0xAD98`, then `ChannelManager::0x00D00AB0()`) → if the 2 s
timer has not expired, do nothing → at `tryCount == 2` show UI `0x271` → prefer
the *previous* channel when `0x03A3889C` is set, otherwise the pending one →
on failure re-arm 2 s, `tryCount++`, log
`CHANNEL INFORM>>>> CNSelectChannelSocket::tryCountConnect++ at (%s)`.

**`setPermitReturnToPrev(bool)` — `0x010BDED0`**: sets `0x03A3889C`, re-arms both
timers, zeroes `tryCount`, logs `CHANNEL>> return to (%s : %d)`, and (unless
`this+0x0C`) starts the channel-move cutscene,
`CHANNEL>> beginCutSceneDuringChannelMove(%d)`.

**`retryConnectFailprevChannel()` — `0x010BF840`**: forces `connectType = 8`,
clears the pending endpoint, and re-resolves the channel-select address. If
`getAddress` fails →
`EXIT>> shutdown dnf.exe because it cannot connect to channel servers. reason : didn't get tcls info`;
if it returns port 0 → `... reason : port == 0`. Both paths **terminate the
process** (`0x012D0930(0)` then a `.vmp1` tail call at `0x05238824`).
A server that hands back a bad/blank channel-select endpoint kills the client.

---

## 7. Public surface (what is called from outside this unit)

| VA | name (from its own literals) | ext callers | notes |
|---|---|---|---|
| `0x010BD0E0` | `getInstance()` | 34 | Meyers singleton for `0x03A38EE0` |
| `0x010BE6D0` | `connectToChannel(type, ip, port)` | 19 | `ret 0xC`; the real channel-move entry. Callers include `MatchChannelMoveManager.cpp`, `GameCoreNetwork_NOTI_Etc.cpp`, `CNSelectCharacterModule.cpp` |
| `0x010BE9F0` | `connectChannel(ip, port)` | 3 | `ret 8`; one caller is `CNSelectChannelModule.cpp` (`0x01328710`) |
| `0x010BE9D0` | `connectToLastChannel(type)` | 2 | thin wrapper: `connectToChannel(type, [0x3A38A1C], [0x3A38A20])` |
| `0x010BED40` | `tryAutoConnecting()` | 1 | per-frame pump, called from `0x01329B10` |
| `0x010BDED0` | `setPermitReturnToPrev(bool)` | 1 | from `GameCoreNetwork_NOTI_Etc.cpp` |
| `0x010BDC50` | pending-channel-enter commit | 1 | from `GameCoreNetwork_NOTI_Town.cpp`; validates `+0x10` against a lock-guarded global, then drives `CNGameSocket` (`0x0274D1E0(0x0B,0)`, `0x0274D310(+0x0E)`, `0x0274D2E0(0x0A)`, `0x0274D340(0)`, send) |
| `0x010BCFD0` | setter for `+0x0E` / `+0x10` | 1 | `ret 8` |
| `0x010BF840` | `retryConnectFailprevChannel()` | 1 | free function; can terminate the process |
| `0x010C04B0` | **load driver** (outside the briefing span) | 1 | `recvChannels()` → close UI `0x209` → `applyChannelInfo()`; failure strings `0x1B59` / `0xADAC` |

Internal-only: `writeLog` `0x010BCF50` (10 internal callers; log category
`"NETWORK"`, tag `"RecvChannalFail"`, line 990 — takes its message pointer in
**ESI**, a non-standard convention, same as `recvPacketData` taking its length
in **EDI**), the ctor `0x010BD020`, `recvPacketData` `0x010BD430`, the six
request/recv functions, `getAddress` `0x010BF020`, `applyChannelInfo`
`0x010BFE10`, `nameToChannelNo` `0x010BE500`, and the `stringstream::str()`
helpers `0x010BFDE0` / `0x010BFB80`.

---

## 8. Explicitly unknown

- Header `+0x00` (u8) and `+0x06` (u32): always zero, never read. Purpose unknown.
- `CNSelectChannelSocket +0x04` (init 8) and `+0x08` (init -1): no reader found
  in this unit.
- `+0x0C` (cutscene-suppress byte): read but never written inside this unit and
  not initialised by the constructor.
- The `connectType` values in use are 3, 6, 8 (observed); the full enum is
  outside this unit.
- The `isFull` threshold float at `0x039AF480` reads **0.0** in this memory dump;
  that is very likely an uninitialised/not-yet-loaded config value, so do not
  treat 0.0 as the shipped threshold.
- The reply message-type byte for `requestChannelScript` — the client never
  checks it, so it cannot be recovered from this unit (`0x0A` by symmetry with
  `0x0B→0x0C` and `0x11→0x12`, but that is inference, not evidence).
