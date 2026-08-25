# Unit 005 — `DNFClient\RDAR\CNGameSocket.cpp`

Briefing span `0x0274D1E0`–`0x0274F380`. Image base `0x00400000`, S4A21 memory dump.

**What it is:** the client's one and only TCP game socket. It owns the connection, the
`WSAAsyncSelect` message pump, the three 700 KB ring buffers, the outbound packet
*builder* (the write-side mirror of the `read_u8/u16/u32/bytes` primitives), the CRC-32
frame checksum, the dispatch into the packet cipher, the inbound frame reassembler, and
the disconnect-reason → popup table.

Every C→S packet in the game is constructed by three functions in this file. This is the
highest-value unit in the image for a server implementer.

---

## 0. Span correction

The anchor-derived span is **too narrow at both ends**, not too wide. Functions
immediately below `lo` and above `hi` are unambiguously the same translation unit — they
touch the same private offsets (`+0xAF000/+0xAF004/+0xAF008`, `+0x2BCC2C`, `+0x2BCC34`)
and are called only from inside the span or from `CNGameCore::NetworkProc`:

| VA | belongs to this TU | role |
| --- | --- | --- |
| `0x0274C9C0` | below `lo` | CRC-32 wrapper (`crc32(0,NULL,0)` then `crc32(seed,buf,len)`) |
| `0x0274C9F0`–`0x0274CA60` | below `lo` | inline accessors + ring `Reset()` |
| `0x0274CC00` / `0x0274CCF0` / `0x0274CD60` | below `lo` | ring `Peek` / `CanReadContiguous` / `Skip` |
| `0x0274CF10` / `0x0274CF40` / `0x0274CF70` | below `lo` | ring `GetTail` / `GetHead` / `GetHeadPtr` |
| `0x0274CFA0` | below `lo` | MT19937 `genrand_int32` (custom tempering) |
| `0x0274D100` | below `lo` | MT19937 state ctor |
| `0x0274F5A0` | above `hi` | CRC-32 table builder |
| `0x0274FA40` | above `hi` | CRC-32 core loop |

Also: `peek.py` reports `0x0274DDD0` as one 2304-byte function. It is **two** functions —
`0x0274DDD0` (map counter add, ends `ret 8` at `0x0274DE82`) and `0x0274DE90`
(`CNGameSocket::Proc`, the socket window-proc). All the FD_* logic is in the second.

---

## 1. Wire format (the deliverable)

### 1.1 C→S frame — built entirely by this unit

Proven by `MakePacket` (`0x0274D1E0`), the header fixups in `TCP_SEND` (`0x0274E6D0`),
and the argument order of the log format
`" [2-> COMMAND PACKET] Packet : [%d], Seq : %d,  Size : %d, CheckSum : %d (%2d%2d-%2d:%2d:%2d)"`
(literal `0x035E8520`, pushed at `0x0274EA99`–`0x0274EAA2` as `type, seq, size, checksum`).

```
off   size  field        written at
0x00   1    class = 1    0274D278  mov byte [ebp-0x14], 1        (always 1 = CMD)
0x01   2    type         0274D267  mov word [ebp-0x13], di       (MakePacket arg1)
0x03   4    size         0274E907  mov [esi+3], eax = bodyPadded + 0x0E
0x07   4    checksum     0274E944  mov [esi+7], eax = CRC-32
0x0B   1    flag         0274D283  mov byte [ebp-9], cl          (MakePacket arg2)
0x0C   2    seq          0274E91A  mov word [esi+0xC], dx        (this+0x2BCC38)
0x0E   ..   body         ciphertext, length = size - 0x0E
```

Header is **14 (0x0E) bytes**; `MakePacket` appends exactly `0x0E` bytes
(`0274D2A1 push 0xE`) and the body pointer is `lea ebx, [esi+0xE]` (`0274E7E8`).

### 1.2 S→C frame — consumed by `CNGameCore::NetworkProc` (`0x0163BBD0`, `GameCoreNetwork.cpp`)

```
off   size  field        evidence
0x00   1    class        0163BECC movzx ecx, byte [edi]
                         0 => "[NOTI RECV] %d (Size : %d)"   (0x032CB198)
                         1 => "[CMD RECV] %d (Size : %d) Result : %s" (0x032CB220)
                         else => "HACK>> Network Packet Type Error [ %d ]" -> return 0x80000000
0x01   2    type         0163BC35 / 0163BD63 / 0163BF4B  movzx ..., word [edi+1]
0x03   4    size         0163BC16 mov ecx, [edi+3]
0x07   4    checksum     NOT VERIFIED here — see 1.4
0x0C   2    seq          (position from the C->S layout; not read at this site)
0x0E   1    unaccounted  see note
0x0F   ..   body         0163BD72 / 0163BDDA  lea .., [edi+0xF]
                         body length = size - 0x0F  (appears 8x: 0163BC25, BCF9,
                         BD4F, BD9E, BDD0, BE24, BE50, C0AE)
```

**The S→C body starts at 0x0F, the C→S body starts at 0x0E.** The `-0x0F` /
`+0x0F` pair is consistent at eight independent sites, so the one-byte difference is real,
but I could not identify what byte `0x0E` carries — it is never read in `NetworkProc`.
Say unknown.

The Proc read loop enforces the same 15: `cmp eax, 0xF; jb <need more>` (`0x0274E1D1`) and
`Peek(0x0F, &hdr)` (`0x0274E1DE`), then takes the size from `hdr+3` (`0x0274E1EF
mov esi, [ebp-0x11]`, where the peek buffer is at `ebp-0x14`).

### 1.3 Checksum — CRC-32, domain `[0x0C, size)`

`0x0274C9C0`:

```c
uint32 Checksum(const void* buf, int len) {
    if (len < 0) return 0;
    uint32 seed = crc32(0, NULL, 0);     // == 0
    return crc32(seed, buf, len);        // 0x0274FA40
}
```

That is the literal zlib idiom. `0x0274FA40` is a byte-at-a-time reflected CRC-32:
`crc = ~seed`; per byte `crc = (crc>>8) ^ table[(b ^ crc) & 0xFF]`; `return ~crc`
(`0274FAE3 not ebx`, `0274FAF4 xor eax,ebx`, `0274FAF6 and eax,0xFF`,
`0274FB8D shr ebx,8`, `0274FB90 xor ebx,esi`, `0274FB9E not eax`).
`0x0274F5A0` builds the 256-entry table with zlib's `make_crc_table` sparse loop
(`c=1; for n=0x80..1: c = (c&1) ? poly ^ (c>>1) : (c>>1); for k in 0,2n,4n..0x100: table[n+k] = c ^ table[k]`).

**Caveat:** the table lives at `0x3B7CF08` as 256 × 12-byte obfuscated entries
`{value, key, lock}` and the polynomial is stored as two obfuscated addends at
`0x3B7CEF0` and `0x3B7CEFC` (`0274F7F5 lea eax,[esi+ebx]`). The *structure* is
unmistakably zlib CRC-32; I could **not** prove statically that the polynomial is
0xEDB88320. Verify against a live capture before relying on it.

**Domain** — the strongest evidence is `0x02769310` (the anti-cheat packet wrapper),
which builds a frame by hand:

```
0276933E  mov  [esi+3], edi            ; size
02769341  mov  word [esi+0xC], 0       ; seq = 0
02769344  call 0x274C9C0               ; crc32(esi+0xC, size - 0xC)
02769349  mov  [esi+7], eax
```

and again at `0276944C`: `crc32(buf+0xC, total-0xC)`.
`TCP_SEND` matches: `push (bodyLen + pad + 2); push (esi+0xC); call 0x274C9C0`
(`0274E929`–`0274E935`).

> **CRC-32 is computed over `frame[0x0C .. size)` — the 2-byte seq plus the
> zero-padded PLAINTEXT body — and only then is the body encrypted.**
> A server must decrypt first, then CRC.

### 1.4 The client does not check the S→C CRC

`0x0163BC30` calls the CRC wrapper with `(edi + 0xE1, size - 0xF)` — note `0xE1`, the raw
bytes are `8d 87 e1 00 00 00`, not a `0x0F` misdecode — and the result is discarded two
instructions later by `0163BC35 movzx ecx, word [edi+1]`. There is no comparison against
`[edi+7]` anywhere in `NetworkProc`. Treat the S→C checksum field as advisory.

### 1.5 Body encryption — 14 ciphers, selected by `type % 14`

Codec object at `CNGameSocket + 0x2BCC4C`. Its three entry points:

| VA | signature | what |
| --- | --- | --- |
| `0x026B7D20` | `AlignLength(WORD type, int* pLen)` | rounds `*pLen` up to the cipher's block size |
| `0x026B7D90` | `Encrypt(WORD type, const void* src, int srcLen, void* dst, int* pDstLen)` | returns bool |
| `0x026B7E50` | `Decrypt(...)` same shape | called from `NetworkProc` at `0163BE46` |

Index derivation (`0x026B7D28`–`0x026B7D51`): `eax=0x92492493; imul; add edx,dividend;
sar edx,3` is the signed magic for **divide by 14**; then `edx = 14*q`, `esi = type - edx`.
So `idx = type % 14`. The codec holds a `vector<ICipher*>` at `[codec+8]..[codec+0xC]`
with a bounds check (`0x026B7D5D call 0x2C623E1` = `std::_Xout_of_range`-ish trap).
`vtbl[0x20]` = block size, `vtbl[0x0C]` = encrypt, `vtbl[0x0C]` on the decrypt object.
Success sentinel is `0x6FFFFFFF` (`0x026B7E2F`).

Padding: `TCP_SEND` asks the codec for the padded length **before** the CRC
(`0274E8C6 call 0x26B7D20`), zero-fills `body[bodyLen .. padded)` (`0274E8F0 memset`),
writes `size = padded + 0x0E`, CRCs `seq+padded`, then encrypts `bodyLen` (not `padded`)
bytes and the codec re-pads internally. On-wire body length = padded length.

### 1.6 NOTI type 1 is plaintext

`0x0163BD60`: `if (frame[0] == 0 && type == 1)` the body is `memcpy`'d straight into
`CNGameCore + 0x1022` with **no** `Decrypt` call. Every other frame goes through
`Decrypt`. This is the pre-handshake channel.

---

## 2. The checksum-XOR obfuscation and its MT19937 (server must mirror)

Tail of `TCP_SEND`, `0x0274ECDC`–`0x0274ED2D`:

```c
p = sendQueue.GetHeadPtr();                       // 0x0274CF70
WORD type = *(WORD*)(p+1);
if (type != 0x4F5 && type != 0x4F6 && type != 0x4F4
    && g_pGameSocket->m_bXorChecksum /*+0x2BCC20*/) {
    *(DWORD*)(p+7) ^= MT_next(&g_pGameSocket->m_mt /*+0x2BC258*/);   // 0x0274CFA0
}
```

So once enabled, **every** C→S checksum is XORed with the next word of a per-connection
MT19937 stream. Types `0x4F4`, `0x4F5`, `0x4F6` are exempt (`0x4F5` is the anti-cheat
report wrapper built by `0x02769310`).

`0x0274CFA0` is MT19937 tempering with **non-standard constants**:

```
y ^= y >> 11
y ^= (y & 0xFF3A58AD) << 7      // stock MT uses 0x9D2C5680
y ^= (y & 0xFFFFDF8C) << 15     // stock MT uses 0xEFC60000
y ^= y >> 18
```

Shifts 11/7/15/18 are stock; the two masks are custom. State is 624 words at
`this+0x2BC258`, index/count at `+0x2BC258+0x9C4`, cursor pointer at `+0x2BC258+0x9C0`,
twist at `0x01163360`.

**Where the seed comes from** — `0x0116A44D`, inside `0x01169500`
(`DNFClient\RDAR\GameCoreNetwork_NOTI_Etc.cpp`, a NOTI handler with 45 stream reads):

```
0116A44D  push 1
0116A44F  call 0x274CA50            ; GetGameSocket()
0116A454  add  eax, 0x2BCC20        ; &m_bXorChecksum
0116A45A  call 0x27C5230            ; set it to 1
0116A468  call 0x274CA50
0116A478  mov  [eax+0x2BC258], edi  ; mt[0] = seed from the packet
0116A480  loop: mt[i] = 0x6C078965 * (mt[i-1] ^ (mt[i-1] >> 30)) + i, i < 0x270
0116A4A6  call 0x1163360            ; twist
```

`0x6C078965` = 1812433253, `0x270` = 624 — **stock `init_genrand(seed)`**. The server
sends the 32-bit seed in a NOTI packet; identifying which NOTI id belongs to unit
`GameCoreNetwork_NOTI_Etc.cpp`, not here.

---

## 3. The three ring buffers

`CNGameSocket` embeds three identical `0xAF020`-byte ring buffers, back to back:

| member offset | role | arithmetic |
| --- | --- | --- |
| `+0x001D8` | **recv** | `0x0274E199 add ebx, 0x1D8` before the read loop |
| `+0x0AF1F8` | **send queue** (bytes handed to `send()`) | `0x1D8 + 0xAF020 = 0xAF1F8` ✓ |
| `+0x15E218` | **packet build scratch** | `0xAF1F8 + 0xAF020 = 0x15E218` ✓ |

Internal layout (identical for all three):

```
+0x00000  BYTE data[0xAF000]          ; 716,800 bytes
+0xAF000  DWORD tail  (write cursor)
+0xAF004  DWORD head  (read cursor)
+0xAF008  CRITICAL_SECTION            ; 0x18 bytes -> object size 0xAF020
```

Proof of which cursor is which: `Peek` (`0x0274CC32`) loads `ecx=[+0xAF000]`,
`eax=[+0xAF004]`, and on the non-wrapped branch computes `available = ecx - eax` then
`memcpy(dst, base + eax, n)` — so `+0xAF004` is the read head. `Skip` (`0x0274CD60`)
advances **`+0xAF004`** and wraps it to 0 at `0xAF000` (`0x0274CDE6`).

Methods (all take the CS):

| VA | signature | notes |
| --- | --- | --- |
| `0x0274CA60` | `Reset()` | head = tail = 0, `memset(data, 0, 0xAF000)` |
| `0x0274CC00` | `bool Peek(int n, void* dst)` | handles wrap, does not advance |
| `0x0274CCF0` | `bool CanReadContiguous(int n)` | n bytes available *without* wrapping |
| `0x0274CD60` | `int Skip(int n)` | 0 ok, −1 n≤0, −2/−3 not enough |
| `0x0274CF10` | `DWORD GetTail()` | `[+0xAF000]` |
| `0x0274CF40` | `DWORD GetHead()` | `[+0xAF004]` |
| `0x0274CF70` | `void* GetHeadPtr()` | `base + [+0xAF004]` |
| `0x0163D700` | `int GetAvailable()` | outside TU |
| `0x0163D7A0` | `bool Pop(int n, void* dst)` | outside TU |
| `0x051DADEC` | `bool IsEmpty()` | .vmp1 |
| `0x05790FE9` | `Append(const void* p, int n)` | .vmp1, the write primitive backend |
| `0x0577254B` | `Append(const void* p, int n)` | .vmp1, used on send queue |

---

## 4. Object layout — `CNGameSocket`

`sizeof(CNGameSocket) = 0x2BD0BC` (2,872,996 bytes) — `0x0274F3BD push 0x2BD0BC` into
`operator new` inside `InitGameSocket`. Singleton pointer at **`0x03B7CEE4`**;
accessor `0x0274CA50` (`mov eax,[0x3B7CEE4]; ret`).

Derives from an `ISocket`-like base (`0x02870CD0` base ctor at `0x0274EDFD`).

| offset | type | field | proof |
| --- | --- | --- | --- |
| `+0x0000` | vftable | `mov [esi], 0x35E8754` | `0274EE0D` |
| `+0x0008` | `bool` | **connected** — every write primitive gates on it | `0274D1F3 cmp byte [esi+8],0` → `0x80000054` |
| `+0x01C0` | ? | passed to the connect-callback list | `0274DF08 lea ecx,[ebx+0x1C0]` |
| `+0x01C4` | `int` | last socket-close code, logged `"SOCKET CLOSE %d"` | `0274E52B mov ecx,[ebx+0x1C4]` |
| `+0x01D8` | ring | **recv buffer** (0xAF020) | `0274E199` |
| `+0x0AF1F8` | ring | **send queue** (0xAF020) | `0274E01E`, `0274E99F` |
| `+0x15E218` | ring | **packet build buffer** (0xAF020) | `0274D26B`, `0274EC53` |
| `+0x2BC258` | `MT19937` | 624-word state + cursor (`+0x9C0`) + count (`+0x9C4`) | `0274EE04` ctor, `0116A478` seed |
| `+0x2BCC20` | `bool` | **m_bXorChecksum** | `0274ED19`, set at `0116A455`, cleared `0274F0DA` |
| `+0x2BCC24` | protected `bool` | value `+0x2BCC24`, key `+0x2BCC28` | `0274EE18`, `0274F0C5` |
| `+0x2BCC2C` | `int` | **m_nBodyLen** — bytes written into the current packet | every write primitive `add [ecx+0x2BCC2C], n` |
| `+0x2BCC34` | `BYTE*` | **m_pCurPacket** — header start in the build buffer; NULL ⇒ no packet open | `0274D2A9`, `0274E78E`, `0274ECB4` |
| `+0x2BCC38` | `WORD` | **m_wSeq** — per-connection sequence; 0 on Connect, ++ after each `TCP_SEND` | `0274D779`, `0274E805`, `0274EDAA` |
| `+0x2BCC3C` | protected `DWORD` | value/key pair, seeded from `0x27C2E60` | `0274EE3E`, `0274F002` |
| `+0x2BCC44` | protected `DWORD` | second copy of the same value (tamper redundancy) | `0274EEB8`, `0274F065`, setter `0274D4C0` writes both |
| `+0x2BCC4C` | codec | **packet cipher bank** (14 ciphers, `type % 14`) | `0274E8C0`, `0274E97A`, `0163BE40` |
| `+0x2BD064` | `int` | **m_nCloseReason** (0..0x13) — switch selector for the disconnect popup | `0274D9C2`, `0274D68B`, accessors `0274C9F0`/`0274CA00` |
| `+0x2BD068` | `bool` | **m_bSendLocked** — `TCP_SEND` refuses while set | `0274E6EB`, set by `0274EAE7` when type==3, accessors `0274CA10`/`0274CA20` |
| `+0x2BD06C` | `std::list` | callback list | `0274EF41` |
| `+0x2BD080` | `std::list` | connect/disconnect observers; `0x1BF01E0` = notify, `0x1BC80E0` = clear | `0274EF77`, `0274DF0F`, `0274E4E5` |
| `+0x2BD094` | `std::map<WORD,int>` | **outstanding-request count per packet type** — `TCP_SEND` `++`, CMD reply `--` | `0274EFAD` ctor, `0274EDA3 inc [eax]`, decrement `0274D880` called from `0163BF5E` |
| `+0x2BD0A8` | `std::map<WORD,int>` | **result/error count per packet type** | `0274EFBC` ctor, add `0274DDD0` from `0163BFA3`, get `0274D7A0` |

Both maps use `[node+0x10]` as the counter (`0274D7E9`, `0274D8F1`, `0274DE58`).

---

## 5. Public surface

### 5.1 The send API — 1062 + 959 external callers

The idiom, verified at `0x00427B40` (a typical caller):

```
push 0                  ; flag   -> header[0x0B]
push 0x355              ; type   -> header[0x01]
call 0x0274CA50         ; GetGameSocket()
mov  ecx, eax
call 0x0274D1E0         ; MakePacket(type, flag)
   ... Write*(...) ...
call 0x0274E6D0         ; TCP_SEND()
```

| VA | callers | signature | body bytes added |
| --- | ---: | --- | --- |
| `0x0274D1E0` | 1062 | `HRESULT MakePacket(WORD type, BYTE flag)` `ret 8` | header only (0x0E), body counter reset |
| `0x0274D2E0` | 458 | `Write(BYTE/bool)` `ret 4` | +1 |
| `0x0274D310` | 325 | `Write(WORD/short)` `ret 4` | +2 |
| `0x0274D340` | 383 | `Write(DWORD/int/float)` `ret 4` | +4 |
| `0x0274D370` | 2 | `Write(INT64/double)` `ret 8` | +8 |
| `0x0274D3A0` | 11 | `WriteBlob(const void* p, int len)` `ret 8` | +4 (`len` as u32) then +len |
| `0x0274D410` | 2 | `WriteString(const char* s)` `ret 4` | +4 (`strlen`, **no NUL**) then +strlen bytes |
| `0x0274D4A0` | 80 | `WriteString(const wchar_t* s)` `ret 4` | `WideCharToMultiByte` (`0x27BADD0`) then `0x0274D410` |
| `0x0274E6D0` | 959 | `HRESULT TCP_SEND()` — free function, no args | finalize + enqueue |

Notes that matter:

- `WriteString` writes a `u32` length that is `strlen` **excluding** the terminator, then
  exactly that many bytes (`0274D42F sub eax,edx` yields `strlen`). Length 0 writes only
  the u32 and returns. A NULL pointer is replaced by the literal at `0x03614F4E`.
- `WriteBlob` writes `len` first (from `[ebp+0xC]`) then `len` bytes from `[ebp+0x8]`.
  `len == 0` writes only the length. `NULL` buffer with `len != 0` returns `0x80000008`.
- Every write primitive silently no-ops (returns `0x80000054`) when the socket is
  disconnected. **The body-length counter is not advanced in that case**, so a
  disconnect mid-build produces a truncated but self-consistent frame.
- `MakePacket` refuses re-entry: if `m_pCurPacket != 0` or `m_nBodyLen != 0` it logs
  `"!!!이미 패킷을 만들던 중입니다!!! try make packet is %d"` (line 607) — but it then
  **proceeds anyway**, overwriting the in-flight packet.

### 5.2 `TCP_SEND` — full sequence

```c
HRESULT TCP_SEND(void) {                                     // 0x0274E6D0, no args
    if (g->m_bSendLocked)   return 0x80000057;               // ENUM_NEOPLE_FAULT_SEND_LOCK
    if (!g->connected)      return 0x80000054;               // ENUM_NEOPLE_FAULT_NOT_CONNECT
    BYTE* p = g->m_pCurPacket;
    if (!p) return 0x80000003;                               // ENUM_NEOPLE_FAULT_MEMORY_LOST
    body = p + 0x0E;
    if (AntiCheatWrap(p, g->m_nBodyLen + 0x0E, g->m_wSeq, &out))   // 0x02769310
        { free(out); buildBuf.Reset(); return buildBuf.GetHeadPtr(); }  // replaced path

    int pad = 0;
    if (g->m_nBodyLen) {
        int n = g->m_nBodyLen;
        codec.AlignLength(type, &n);                         // 0x026B7D20
        pad = n - g->m_nBodyLen;
        if (pad > 0) memset(body + g->m_nBodyLen, 0, pad);
    }
    *(DWORD*)(p+3)  = g->m_nBodyLen + pad + 0x0E;            // size
    *(WORD*)(p+0xC) = g->m_wSeq;                             // seq
    *(DWORD*)(p+7)  = CRC32(p + 0x0C, g->m_nBodyLen + pad + 2);

    if (g->m_nBodyLen) {
        int outLen = 0x40000;                                 // 256 KB stack buffer
        codec.Encrypt(type, body, g->m_nBodyLen, encBuf, &outLen);   // 0x026B7D90
        sendQueue.Append(buildBuf.GetHeadPtr(), 0x0E);        // plaintext header
        sendQueue.Append(encBuf, outLen);                     // ciphertext body
        if (type == 3) g->m_bSendLocked = true;               // 0x0274EAE7
        buildBuf.Reset();
    }
    log("[2-> COMMAND PACKET] Packet:[%d] Seq:%d Size:%d CheckSum:%d ...");
    buildBuf.Reset(); g->m_pCurPacket = NULL; g->m_nBodyLen = 0;

    if (!sendQueue.IsEmpty()) {
        BYTE* q = sendQueue.GetHeadPtr();  WORD t = *(WORD*)(q+1);
        if (t != 0x4F5 && t != 0x4F6 && t != 0x4F4 && g->m_bXorChecksum)
            *(DWORD*)(q+7) ^= MT_next(&g->m_mt);
    }
    HRESULT hr = g->FlushSend();                              // 0x02870230
    if (hr == 0x80000054) { MessageBox(str 0x96); g->Disconnect(); return hr; }
    g->m_pendingByType[type]++;                               // 0x2BD094
    g->m_wSeq++;
    return hr;
}
```

**Packet type 3 latches the send lock.** After sending type 3 the client will never send
again on this connection (`m_bSendLocked` is only cleared by `Connect` at `0274D780` and
the ctor). Almost certainly the logout/quit command.

### 5.3 Error codes returned by this unit

| value | name (from the decrypted literals) | site |
| --- | --- | --- |
| `0x80000003` | `ENUM_NEOPLE_FAULT_MEMORY_LOST` | `0x0274E7D1` (`m_pCurPacket == NULL`) |
| `0x80000054` | `ENUM_NEOPLE_FAULT_NOT_CONNECT` | `0x0274E77A`, every write primitive |
| `0x80000057` | `ENUM_NEOPLE_FAULT_SEND_LOCK` | `0x0274E72B` |
| `0x80000008` | (unnamed) NULL buffer | `0x0274D3DC`, `0x0274D46A` |
| `0x80000000` | connect refused, state already busy | `0x0274D71B` |

### 5.4 `CNGameSocket::Connect` — `0x0274D680`

```c
HRESULT Connect(const char* ip, int port)   // ret 8; log "Connect[%s][%d]" line 492
{
    if (m_nCloseReason != 2) m_nCloseReason = 0;
    if (<observer list at +0x2BD080 is non-empty / iterators differ>) {
        m_nCloseReason = 0x11;  return 0x80000000;
    }
    hr = DoConnect(ip, port);                 // 0x028708E0
    if (hr >= 0) { m_wSeq = 0; m_bSendLocked = false; return 1; }
    return hr;
}
```

**The sequence counter restarts at 0 for every connection.**

### 5.5 `CNGameSocket::Proc` — `0x0274DE90` (the message pump)

`WSAAsyncSelect` window-proc: `Proc(HWND, UINT msg, WPARAM sock, LPARAM lp)` `ret 0x10`.

```
err   = WSAGETSELECTERROR(lp)   = lp >> 16
event = WSAGETSELECTEVENT(lp)   = lp & 0xFFFF
```

Error path (`err != 0`):
- `event == FD_CONNECT (0x10)`, `err == 10060` (`WSAETIMEDOUT`, `0x274C`) →
  `" -> Connect Fail 다시 커넥트 시도할 수 있다"` (line 720), notify observers, **return 1** (retryable)
- `event == FD_CONNECT`, other err → `" -> Disconnect() connect"` (line 728), `Disconnect()`, return 0
- any other event → `"SELECTERROR"` + `"SOCKET ERROR OCCUR: %d"` (line 739), `Disconnect()`, return 0

Success path — jump table at `0x0274E5B4`, index byte table at `0x0274E5C8` over `event-1 ∈ [0,0x1F]`:

| event | handler | behaviour |
| --- | --- | --- |
| `FD_READ` (1) | `0x0274E0F4` | `" ========== READ Process start =========="` (line 777), `RecvToBuffer()` (`0x028704A0`), then the reassembly loop |
| `FD_WRITE` (2) | `0x0274E01E` | if send queue empty → `"FD_WRITE 로 들어왔지만 쓸려는 데이터가 없다"` (line 751); else `FlushSend()` (`0x02870230`), `"1. Send : nLen:ISocket : %d"` (line 756) |
| `FD_CONNECT` (0x10) | `0x0274E4E5` | notify observer list `+0x2BD080` |
| `FD_CLOSE` (0x20) | `0x0274E4F5` | `"++++++++++ Close Process start!!"` (line 972), `"SOCKET CLOSE %d"` with `[this+0x1C4]`, `ShowDisconnectPopup()`, `"Close Process End!!"` (line 1177) |
| anything else | `0x0274E29F` | falls through to `DefaultProc` (`0x02870720`) |

### 5.6 Reassembly loop (the part a server's framing must satisfy)

Two paths, both keyed off `size = *(int*)(frame+3)`:

**Fast, zero-copy** (`0x0274E2E7`) — taken when ≥ 15 bytes are contiguous from the head:
read `size` in place, `CanReadContiguous(size)` (`0x0274CCF0`); if the whole frame is
contiguous, dispatch straight from the ring and `Skip(size)`.

**Slow, copying** (`0x0274E1CA`) — `GetAvailable() >= 0x0F`, `Peek(0x0F)`, read `size`,
`GetAvailable() >= size`, `malloc(size)`, `Pop(size, buf)`, dispatch, `free`.

Both dispatch as `GameCore()->NetworkProc(hWnd, frame, size)` (`0x01102C30` is
`GameCore()`, a 16-byte getter; `0x0163BBD0` is `CNGameCore::NetworkProc`).

Validation and its consequences — **a server that violates any of these kills the client**:

| condition | log (line) | action |
| --- | --- | --- |
| `size > 0xAF000` | string id `0x94` (768 / 885) | `Disconnect()` |
| `size < 0` | string id `0x94` | `Disconnect()` |
| `size == 0` | `"1./2.recv critical error occure!"` (864 / 919) | `Disconnect()` |
| `size > available` | `"1./2.need more body data : messagesize=%d"` (857 / 913) | break, wait for more |
| `available < 0x0F` | (literal `0x035E8064`, undecoded) line 873 | break, wait |
| `Pop` fails | `"1./2./3.여기서 pop error 가 떨어지면 안된다."` (830 / 848 / 904) | `Disconnect()` |
| `frame[0] > 1` | `"HACK>> Network Packet Type Error [ %d ]"` (in `NetworkProc`) | return `0x80000000` |

**`0xAF000` (716,800) is the hard per-frame size ceiling** — checked in Proc twice and
again in `NetworkProc` (`0163BC76`, `0163BD58`, `0163BD23`).

### 5.7 `CNGameSocket::ShowDisconnectPopup` — `0x0274D980`

`"SOCKET CLOSE REASON %d"` (line 1312), then a 20-entry switch on `m_nCloseReason`
(`+0x2BD064`); jump table at `0x0274DD74`:

| reason | target | result |
| ---: | --- | --- |
| 0 | `0x0274DA24` | `"Current Module Type %d"` (1319) / `"Request Exit Number %d"` (1353); module type 5 → silent; exit number 0 → string `0x96`, severity 2, notify observers |
| 1 | `0x0274DB4D` | string `0x97`, severity 2 |
| 2 | `0x0274DB6D` | string `0x98`, severity 2 |
| 3 | `0x0274DB74` | string `0x99` |
| 4 | `0x0274DB8F` | string `0x111BC` |
| 5 | `0x0274DBCF` | string `0x9B` |
| 6 | `0x0274DBEA` | string `0x9C` |
| 7 | `0x0274DC05` | string `0x9D`, severity 2 |
| 8 | `0x0274DC0F` | string `0x9E` |
| 9 | `0x0274DC2A` | string `0x9F` |
| 10 | `0x0274DC45` | `byte[0x03A5CA15]` ? string `0xDB6` : string `0xA0` |
| 11 | `0x0274DC7E` | string `0xA1`, severity 2 |
| 12 | `0x0274DB24` | literal `"error : 6846"` |
| 13 | `0x0274DB3F` | literal `"error : 7909"` |
| 14 | `0x0274DBAA` | string `0x1217` → popup `0x132`, severity 1 |
| 15 | `0x0274DA7B` | message cleared, severity 0 (**silent close**) |
| 16 | `0x0274DB46` | literal `"error : 2848"` |
| 17 | `0x0274DBEA` | string `0x9C` (same as 6) — this is the value `Connect` sets on refusal |
| 18, 19 | `0x0274DC88` | string `0x96` |
| >19 | `0x0274DCA0` | string `0xA2` |

Popup is `0x02304C70(0x209, text, severity)` on `[0x03A5C9B8]`.

### 5.8 `InitGameSocket` — `0x0274F380`

```c
bool InitGameSocket(void) {
    if (g_pGameSocket) return false;                 // 0x03B7CEE4
    void* p = operator new(0x2BD0BC);
    if (p) p = CNGameSocket::CNGameSocket(p);        // 0x0274EDD0
    if (!p) return false;
    g_pGameSocket = p;
    log(level 4, "InitGameSocket", line 23, "INIT>> Game Socket is Finished.");
    return true;
}
```

Destructor chain: `0x0274F440` (scalar deleting) → `0x0274F1F0` (dtor).

---

## 6. Anti-cheat hook on the send path — `0x02769310`

`TCP_SEND` calls it before framing (`0274E81E`) as
`AntiCheat::Wrap(this=[0x03B7F09C], BYTE* frame, int size, WORD seq, void** ppOut)`.
It reaches through a vtable on `[this+0x0C]`: slot 0 `ShouldWrap(type)`, slot 4
`Process(frame, size)`, slot 8 `GetPayload(&buf, &len)`. When it yields a payload it
allocates a **new frame of type `0x4F5`**:

```
buf[0x00] = 1
buf[0x01] = 0x4F5                 (WORD)
buf[0x03] = payloadLen + 0x12     (DWORD, total)
buf[0x07] = CRC32(buf+0x0C, total-0x0C)
buf[0x0C] = seq                   (WORD)
buf[0x0E] = payloadLen            (DWORD)   <- inner length
buf[0x12] = payload
```

So `0x4F5` = client→server anti-cheat report, body = `{u32 len; byte data[len]}`. Its
sibling exemptions `0x4F4` and `0x4F6` are the other two anti-cheat channel types.
A related check `0x02768F30(type)` in `NetworkProc` (`0163BC43`) and an S→C unwrap
`0x02769470` (`0163BC60`) close the loop on the receive side.

---

## 7. Confidence

**High** — C→S header layout and the 14-byte size; write-primitive semantics and their
per-type body-byte cost; the three ring buffers and their offsets/cursor roles;
`sizeof(CNGameSocket)`; the singleton at `0x03B7CEE4`; the `0xAF000` frame ceiling;
seq reset on Connect and increment after `TCP_SEND`; the CRC domain `[0x0C, size)`;
`type % 14` cipher selection; the MT19937 seeding site and its custom tempering masks;
the FD_* jump table; the disconnect-reason table.

**Medium** — that the CRC polynomial is 0xEDB88320 (the *algorithm* is provably zlib's,
but the constant is stored obfuscated at `0x3B7CEF0`/`0x3B7CEFC` and cannot be read
statically); the field at `+0x2BCC3C`/`+0x2BCC44` being a nonce rather than something else.

**Unknown** — what byte `0x0E` of an S→C frame carries (the body demonstrably starts at
`0x0F`, but nothing reads `0x0E`); which NOTI id delivers the MT seed; the meanings of
close-reason string ids `0x96`–`0xA2`.
