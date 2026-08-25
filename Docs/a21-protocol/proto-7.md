# proto-7 — `Library\Include\Nengine\ISocket.cpp`

**Headline: this file contains ZERO packet handlers and ZERO packet-type dispatch.**
It is the raw Winsock transport layer (`nengine::ISocket`). Every byte that later
becomes a field is read *above* it, in `CNGameSocket.cpp` (framing) and
`GameCoreNetwork.cpp` (type dispatch). Reported below with evidence so nobody
re-spends the hours.

---

## 1. Extent of the file

`func-source-file.csv` anchors (8):

```
0x0286E6D0 0x0286FDE0 0x02870080 0x02870190
0x02870230 0x028704A0 0x02870720 0x028708E0
```

Span, using `functions.csv` spans: **0x0286E6D0 .. 0x02870CD0**
(last anchor 0x028708E0 + 1008 bytes).

Neighbours confirm the band is clean — no other source file interleaves:

```
0x02868FF0  Library\Include\Nengine\DeflateHelper.cpp   (before)
0x02874040  Library\Include\Nengine\NEPackFile.cpp      (after)
```

Two functions sit between the last ISocket anchor and NEPackFile — `0x02870CD0`
and `0x02870E30`. Both call `ISocket::InitBuffer` (0x0286FDE0) so they are
probably ISocket ctor/reset, but they carry **no `__FILE__` literal**, so I do
not claim them. Unknown.

### Function roster (named from decrypted literals in `literals.csv`)

| VA | Name / evidence | Notes |
|---|---|---|
| 0x0286E6D0 | `DistributeNetworkError` (lit 0x036162A0) | 5904 B; WSA errno -> log string |
| 0x0286FDE0 | `nengine::ISocket::InitBuffer` (lit 0x036165D0) | zeroes 4 ring buffers |
| 0x0286FF10 | unknown (no literals) | only callee is `memset` 0x02C62020 |
| 0x02870010 | `STARTUP` (lit 0x03616648) | WSAStartup wrapper; calls InitBuffer |
| 0x02870080 | `nengine::ISocket::Disconnect` (lit 0x036166F8) | `CLOSESOCKET` |
| 0x02870190 | `nengine::ISocket::SendToNetwork` (lit 0x03616790) | raw `send()` |
| 0x02870230 | `nengine::ISocket::Send` (lit 0x036168F4) | drains send ring |
| 0x028704A0 | `nengine::ISocket::Recv` (lit 0x036169D4) | raw `recv()` loop |
| 0x02870720 | `nengine::ISocket::DefProc` (lit 0x03616C20) | FD_CONNECT / FD_CLOSE |
| 0x02870850 | `WSACLEANUP` (lit 0x03616D40) | teardown; calls InitBuffer |
| 0x028708E0 | `nengine::ISocket::Connect` (lit 0x03617004) | socket/setsockopt/connect |

Every one of these also carries `D:\Work\1229\Source\Library\Include\Nengine\ISocket.cpp`
(0x03616228) — that is the ground-truth anchor.

---

## 2. Packet handlers: none

Cross of the band **0x0286E6D0 .. 0x02870CD0** against `parsers.csv`:

```
awk -F, 'NR>1{v=strtonum($1); if(v>=0x0286E000 && v<=0x02875000) print}' parsers.csv
   -> no rows
```

`functions.csv` agrees independently: the `parser_calls` column is **0** for all
12 functions in the band. Not one call to `read_u8 / read_u16 / read_u32 /
read_bytes / set_remaining`.

So there is **no wire format to report for this file**. ISocket moves opaque
bytes; it never looks inside them.

What it actually does with the bytes:

* **`ISocket::Recv` @ 0x028704A0** — loop of `ws2_32!recv(sock, stackbuf, 0x80, 0)`
  into a 128-byte stack buffer, each chunk appended to the ring buffer at
  `this+0x1D8` via 0x0577254B (.vmp1 thunk). Returns total bytes.
  Guards: `[this+4]==0` -> `0x80000001` "ERROR : recv : Not Initialized";
  `[this+8]==0` -> `0x80000054` "ERROR : recv : Not Connected";
  `WSAGetLastError()==0x2733` (WSAEWOULDBLOCK) is swallowed.
* **`ISocket::Send` @ 0x02870230** — drains the ring at `this+0xAF1F8`
  (0x028702CE `add edi, 0xaf1f8`) and hands slices to `SendToNetwork`.
* **`ISocket::SendToNetwork` @ 0x02870190** — `ws2_32!send(sock, buf, len, 0)`.

### ISocket object layout (from InitBuffer 0x0286FDE0 + call sites)

```
+0x0004   u8    initialized flag
+0x0008   u8    connected flag
+0x000C   u32   hWnd  (passed to WSAAsyncSelect at 0x02870776)
+0x01C4   SOCKET
+0x01D8   ring #0  <- RECV ring   (Recv appends here; CNGameSocket drains here)
+0xAF1F8  ring #1  <- SEND ring   (ISocket::Send drains here)
+0x15E218 ring #2
+0x20D238 ring #3
```

Each ring object is **0xAF020** bytes: `0xAF000` data, `+0xAF000` u32 head,
`+0xAF004` u32 tail, `+0xAF008` CRITICAL_SECTION. Stride proven by
`0x1D8 + 0xAF020 == 0xAF1F8 == 0x15E218 - 0xAF020` etc. in InitBuffer.

`ISocket::DefProc` @ 0x02870720 handles only FD_CONNECT (0x10) and FD_CLOSE
(0x20) via a two-way `cmp`/`je` chain (0x0287072D, 0x02870732) — no table.
On FD_CONNECT success it calls `WSAAsyncSelect(sock, hWnd, 0x464, 0x23)`
(0x02870778): message **0x464**, event mask **0x23 = FD_READ|FD_WRITE|FD_CLOSE**.

---

## 3. Dispatch inside ISocket.cpp

There is exactly one indirect jump in the whole band:

```
0286E9A1  movzx ecx, byte ptr [ecx + 0x286FD5C]     ; byte index table, stride 1
0286E9A8  jmp   dword ptr [ecx*4 + 0x286FC74]       ; target table,     stride 4
```

* **Index table VA 0x0286FD5C**, stride 1, 0x68 (104) entries
  (`0286E992 lea ecx,[eax-0x2719]` / `0286E998 cmp ecx,0x67` / `ja` default).
* **Target table VA 0x0286FC74**, stride 4.
* Index expression: `wsaErrorCode - 0x2719`, i.e. codes **10009 (WSAEBADF) ..
  10112**.

**This is NOT a packet dispatch.** It is the WSA-errno switch inside
`DistributeNetworkError(tag, wsaErr)` that picks a log string
("WSAEBADF", "WSAECONNRESET", ...) — see the ~70 `WSAE*` literals attributed to
0x0286E6D0 in `literals.csv`. Flagging it explicitly because it *looks* like a
protocol table and will produce a false positive for the next analyst.
Callers pass a `WSAGetLastError()` result (e.g. 0x028705DB from `Recv`,
0x028701CB from `SendToNetwork`).

No jump table, no switch, no function-pointer array keyed on a packet type
exists anywhere in 0x0286E6D0..0x02870CD0.

---

## 4. Where the real framing and type dispatch live (handoff)

Traced by callers, so the server team is not left empty-handed.

### 4a. `CNGameSocket::Proc` @ **0x0274DE90**

(`DNFClient\RDAR\CNGameSocket.cpp`; `functions.csv` merges it into the row for
0x0274DDD0 — the real prologue is at 0x0274DE90, and literal 0x035E83CC
`CNGameSocket::Proc` belongs to it.)

Winsock async-event jump table:

```
0274E002  movzx eax, cx            ; cx = WSAGETSELECTEVENT(lParam)
0274E005  dec   eax
0274E006  cmp   eax, 0x1F ; ja default
0274E00F  movzx ecx, byte ptr [eax + 0x274E5C8]   ; index table, stride 1, 32 entries
0274E017  jmp   dword ptr [ecx*4 + 0x274E5B4]     ; target table, stride 4, 5 entries
```

| event | value | target |
|---|---|---|
| FD_READ | 0x01 | 0x0274E0F4 |
| FD_WRITE | 0x02 | 0x0274E01E |
| FD_CONNECT | 0x10 | 0x0274E4E5 |
| FD_CLOSE | 0x20 | 0x0274E4F5 |
| default | — | 0x0274E29F |

(Again: Winsock events, **not** packet types.)

### 4b. Inbound framing — FD_READ path @ 0x0274E0F4

1. `ISocket::Recv` (0x0274E12F -> 0x028704A0) fills the recv ring.
2. Loop while ring not empty:
   * require **>= 0x0F bytes** available (`0274E1D1 cmp eax,0xF`);
   * **peek** 15 bytes without consuming (`0274E1E2 call 0x0274CC00`, memcpy-out
     only, head/tail untouched);
   * `messagesize = u32 at header+3` (`0274E1EF mov esi,[ebp-0x11]`, header
     buffer is at `ebp-0x14`);
   * reject if `> 0xAF000` or negative;
   * if ring bytes available `< messagesize` -> log
     `"1./2.need more body data : messagesize=%d"` and stop — **the u32 at +3 is
     the total frame length, header included**;
   * `malloc(messagesize)` (0x00811F60), **pop `messagesize` bytes** (0x0163D7A0)
     — the 15 peeked bytes are part of that pop;
   * `CNGameCore::NetworkProc(outer, buf, messagesize)` — thiscall
     **0x0163BBD0**, instance from 0x01102C30.

There is a second, wrap-around variant of the same loop at 0x0274E2C8 that reads
the length as `[edi+3]` directly out of the ring (0x0274E2F9) when the header
does not straddle the ring seam. Same field, same offset.

### 4c. Type extraction — `CNGameCore::NetworkProc` @ **0x0163BBD0**

(`DNFClient\RDAR\GameCoreNetwork.cpp`; literal 0x032CB274 `CNGameCore::NetworkProc`,
plus 0x032CB198 `[NOTI RECV] %d (Size : %d)` and 0x032CB220
`[CMD RECV] %d (Size : %d) Result : %s`.)

```
0163BC16  mov   ecx, [edi+3]                ; total length, u32 @ +3
0163BC1E  call  0x027C4B60 (buf, len)       ; in-place decrypt
0163BC23  mov   edx, [ebx]  ; sub edx,0xF
0163BC30  call  0x0274C9C0 (edi+0xE1, len-0xF)   ; checksum  (raw: 8D 87 E1 00 00 00)
0163BC35  movzx ecx, word ptr [edi+1]       ; *** PACKET TYPE, u16 @ +1 ***
0163BC43  call  0x02768F30 (type)           ; type -> descriptor lookup
0163BC60  call  0x02769470 (buf, &out)      ; body decode
```

`HACK>> Network Packet Type Error [ %d ]` (0x032CB2B0) is the reject path.
**The S->C type -> handler mapping is downstream of 0x02768F30 / 0x02769470 —
that is GameCoreNetwork.cpp's job, not this file's. Not claimed here.**

### 4d. Outbound header (C->S) — `CNGameSocket` TCP_SEND @ 0x0274E6D0

Field writes, in program order:

```
0274E907  mov   dword ptr [esi+3],   payloadLen + 0x0E   ; u32 total size
0274E91A  mov   word  ptr [esi+0xC], seq                 ; u16  (from g+0x2BCC38)
0274E944  mov   dword ptr [esi+7],   checksum            ; u32  (0x0274C9C0)
0274E954  movzx edx, word ptr [esi+1]                    ; u16 type (written earlier)
0274E7E8  lea   ebx, [esi+0xE]                           ; body starts at +0x0E
```

giving

```
+0x00  u8   unknown
+0x01  u16  packet type
+0x03  u32  total size (payload + 0x0E)
+0x07  u32  checksum
+0x0B  u8   unknown
+0x0C  u16  sequence
+0x0E  body
```

corroborated by log literal 0x035E8520
`" [2-> COMMAND PACKET] Packet : [%d], Seq : %d, Size : %d, CheckSum : %d ..."`
— same field set, same order.

**Unresolved, do not guess:** outbound body starts at **+0x0E** while the inbound
path requires/peeks **0x0F** and checksums `len - 0x0F`. One extra byte on the
S->C side. I did not pin what it is. Also unexplained: the inbound checksum
pointer is `buf + 0xE1`, a literal `0xE1` in the encoding
(`8D 87 E1 00 00 00`), not `0xE`/`0xF`. Both belong to whoever owns
GameCoreNetwork.cpp.

---

## 5. Bottom line for the server team

* `ISocket.cpp` = transport only. Nothing to model, nothing to implement against.
* Frame on the wire, S->C: total length is a **u32 at offset +3**; packet type is
  a **u16 at offset +1**; frame body is decrypted in place by 0x027C4B60 before
  the type is read. Max frame 0xAF000 (716800) bytes — larger is dropped by the
  client before any parsing.
* Type -> handler table: not in this file. Ask the GameCoreNetwork.cpp owner,
  entry point 0x0163BBD0 -> 0x02768F30.

## 6. Negative results worth keeping

* No `packet_type` code can be pinned to any function in ISocket.cpp, because no
  function in ISocket.cpp is selected by a packet type. Left empty deliberately.
* The 0x0286FC74 table is a WSA-errno table. If a later run reports it as a
  protocol dispatch, that report is wrong.
* The `WSAAsyncSelect` message id **0x464** (0x02870771) and event mask **0x23**
  (0x0287076F) are the only two magic numbers in this file, and neither is a
  packet type.
