# TU 030 — `DNFClient\RDAR\CNRDUdpIdentify.cpp`

Briefing span `0x022E3300`–`0x022E4080` (6 anchored functions).
File literal: `0x35B5EC8` = `D:\Work\1229\Source\DNFClient\RDAR\CNRDUdpIdentify.cpp`.

**Confidence: high** for everything below except where marked INFERENCE.

---

## 1. What this unit implements

`CNRDUdpIdentify` is the client's **NAT-type discovery probe**: a hand-rolled,
7-byte-message mini-STUN spoken over UDP to a set of "스턴서버" (STUN server)
addresses that the *game server* pushes down over TCP. It

1. opens one non-blocking UDP socket bound to `INADDR_ANY:5063`,
2. measures the path MTU to the STUN server with `icmp.dll` + DF-bit binary search,
3. runs a 4-step probe state machine (steps 1→2→3→4) against two STUN server IPs
   and two ports, classifying the NAT into one of 6 codes,
4. then parks in step 5 sending a 1-byte keepalive every 10 s to hold the NAT
   mapping open,
5. and publishes the discovered **public (mapped) IP + port**, which
   `CNRDUserInfoPacketHandler.cpp` writes into a per-user record alongside the
   private IP — i.e. this is the endpoint-exchange half of DNF's UDP P2P /
   hole-punching path.

This TU has **0 parsers** — it never touches the TCP inbound stream. Its wire I/O
is raw `sendto`/`recvfrom`. The TCP-side configuration arrives in
`GameCoreNetwork_NOTI_Etc.cpp` (see §5).

---

## 2. The UDP probe wire protocol (server-implementable)

### Client → STUN server: exactly 1 byte

`sendto(sock, &this->m_reqByte /*+0x58*/, 1, 0, dest, 16)` at every send site.

| byte | sent by | destination | evidence |
| --- | --- | --- | --- |
| `0x01` | step 1 (NAT binding) | `sa1` = STUN1 : portA | `0x022E3F6B`, byte preset to 1 at `0x022E4699` |
| `0x01` | step 2 (NAT CHECK) | `sa2` = STUN1 : portB | `0x022E3F88` (does not rewrite `+0x58`) |
| `0x05` | step 3 (PORT RESTRICT CHECK) | `sa1` = STUN1 : portA | `0x022E3FC4 mov byte [eax],5` |
| `0x06` | step 4 (IP DEPENDENT SYMMETRIC CHECK) | `sa3` = STUN2 : portB | `0x022E3FF9 mov byte [eax],6` |
| `0x00` | step 5 (keepalive, every 10 s) | `sa2` = STUN1 : portB | `0x022E3D83 mov byte [eax],0` |

Caveat: `+0x58` is a persistent field, not reset per step. Steps 1 and 2 send
whatever is there. On a cold run that is `0x01` (init), but a re-entry into step 1/2
after step 3/4 would send the stale `0x05`/`0x06`.

**Semantics the server must implement:**
- `0x01` → reply from the port the request arrived on (plain binding response).
- `0x05` → reply from a **different port** than the request arrived on. If the client
  gets no answer within its retry budget it concludes "port-restricted" (type 4).
- `0x06` → binding response from the *second* STUN server IP.
- `0x00` → keepalive; no reply is parsed (client never reads it in step 5).

### STUN server → client: 7 bytes

Parsed at `0x022E418D`–`0x022E41CB` (and identically at `0x022E42AC`, `0x022E444E`):

```
offset  size  field
 +0      1    0x02   magic/opcode — anything else is dropped (cmp byte [edi],2)
 +1      4    mapped IPv4, HOST byte order   (client does htonl -> stores net order)
 +5      2    mapped port,  HOST byte order  (client does htons -> stores net order)
```

Receive buffer is 0x100 bytes at `this+0x59`; `recvfrom` with a 16-byte `from`
sockaddr at `this+0x15C`. Only the first 7 bytes are ever read.

### Retransmit / timeout

- After each of steps 1–4: `m_waiting(+0x1A4)=1`, timer `+0x184` restarted with
  **500 ms** (`0x022E4050 push 0x1F4`).
- Step 5 keepalive timer: **10000 ms** (`0x022E3D99 push 0x2710`).
- `proc()` does one non-blocking `recvfrom` per tick. On `-1` or wrong magic and
  `m_step != 3`: `m_54 = 0`, `sendMessage(m_step)` (a retry), and log
  `"\nSTUN Server로 부터 응답 없음"` (line 852).
- `sendMessage(same step)` bumps `m_retry(+0x1A0)`. At **>= 10** retries it calls
  `resetIPAddress()` to roll to the next STUN server in the list. If no server is
  left it re-marks **every** entry in both availability bitsets as available,
  rolls again, then reports **type 5 (Connect to Relay)** with the *local* IP.

### Socket setup (`create()` @ `0x022E3300`, `OpenStunSocket()` @ `0x022E3AB0`)

- `gethostname` → `gethostbyname` → `h_addr_list[0]` formatted `"%d.%d.%d.%d"`,
  fallback literal `"127.0.0.1"` → `inet_addr` → **`this+0x08` = local/private IP
  (network order)**.
- `socket(AF_INET, SOCK_DGRAM, 0)` → `this+0x0C`; `ioctlsocket(FIONBIO, 1)`.
- `bind(INADDR_ANY, htons(0x13C7))` → **local UDP port 5063** (`0x022E34A1`).
  Return value is not checked.
- `setsockopt` triple at `0x022E3CC9`–`0x022E451B`:
  `SO_RCVTIMEO(0x1006)=10 ms`, `SO_RCVBUF(0x1002)=0x100000`, `SO_SNDBUF(0x1001)=0x100000`.
- `this+0x244 = 1` marks the socket open.

---

## 3. The NAT-classification state machine

`m_step` = `this+0x50`. `proc()` (`0x022E4080`) is the tick; `sendMessage(step)`
(`0x022E3D30`) is the transmitter. Result codes go to a VMProtect-virtualised
sink `0x057018B9` (see §6) as `f(void* endpointRecord, int code)`.

```
                 external: sendMessage(1)  <- CNSelectCharacterModule 0x026F3330
                                 |
  STEP 1  send 0x01 -> STUN1:portA
      resp -> mapped[0] = {port1@0x16C, ip1@0x170}
      log "CHECK IP : [%s]" (line 645) / "->[%s:%d]" (line 647)
      if  ip1 == localIP (this+8)   -> RESULT 1            (0x022E4286)
      else                          -> sendMessage(2)      (0x022E4295)

  STEP 2  send 0x01 -> STUN1:portB          "SEND NAT CHECK %d" (line 541)
      resp -> mapped[1] = {port2@0x174, ip2@0x178}
      log "CHECK IP : [%s:%d]" (676) / "->[%s:%d]" (678)
      if  ip1==ip2 && port1==port2   -> sendMessage(3)     (0x022E43C0)
      else                           -> RESULT 3           (0x022E43D0)

  STEP 3  send 0x05 -> STUN1:portA   "SEND PORT RESTRICT CHECK %d" (line 552)
      ANY valid reply (server answered from a different port):
          if stunList2 empty                     -> RESULT 2
          else if resolve(list2 current) == resolve("0.0.0.0") -> RESULT 2
          else                                   -> sendMessage(4)
      NO reply (recvfrom == -1 or magic != 2):    -> RESULT 4   (0x022E4583)
        ^ this is the ONLY step whose timeout is a verdict, not a retry.

  STEP 4  send 0x06 -> STUN2:portB
          "SEND IP DEPENDENT SYMMETRIC CHECK %d" (line 561)
      resp -> mapped[2] = {port3@0x17C, ip3@0x180}
      log "STEP_IP_DEPENDENT_SYMMETRIC_CHECK IP : [%s:%d]" (816) / "->[%s:%d]" (818)
      if  ip2==ip3 && port2==port3   -> RESULT 2           (0x022E455D)
      else                           -> RESULT 6           (0x022E4574)

  retry budget exhausted in sendMessage -> RESULT 5, "Connect to Relay" (line 500),
      payload = {garbage port, localIP}, m_retry reset to 0     (0x022E3EF7)

  STEP 5  (terminal) if m_54 == 1 -> idle; else keepalive 0x00 -> STUN1:portB / 10 s
```

Result-code reading (INFERENCE from the branch conditions, the log text, and the
`m_54==1 ⇒ no keepalive` rule):

| code | branch condition | most likely meaning |
| --- | --- | --- |
| 1 | mapped IP == local IP | no NAT / public host — step 5 skips keepalive for this one |
| 2 | change-port reply received, or mapping identical across two server IPs | cone (full/restricted) |
| 3 | mapping changed between two ports of the *same* server IP | port-dependent symmetric |
| 4 | no reply to the `0x05` change-port probe | port-restricted cone |
| 5 | retry budget exhausted, all STUN servers tried | fall back to relay |
| 6 | mapping changed between two *different* server IPs | address-dependent symmetric |

The dispatch table for steps 1–4 is a real jump table at **`0x022E4068`**:
`{0x022E3F6B, 0x022E3F88, 0x022E3FB6, 0x022E3FE7}` indexed by `step-1`
(`0x022E3F64 jmp [eax*4+0x22E4068]`, guarded by `cmp eax,3 / ja`).

### `proc()` entry gates (`0x022E4089`–`0x022E40DB`)
All must pass or `proc()` returns immediately:
`*(void**)0x03B7CEE4 != 0`; `0x01102C30() != 0`; `(*0x03B7CEE4)->byte[8] != 0`;
`g->[0xAC] != 0`; `g->[0xAC]->byte[0x74] != 0`.
Then lazily: if `m_244==0` and stunList1 non-empty → `OpenStunSocket()`;
if `m_mtu == -1` → `FindMTUSize()`.

---

## 4. Object layout (`CNRDUdpIdentify`, size >= 0x245)

Every offset below is proved by a named instruction.

| offset | type | meaning | proof |
| --- | --- | --- | --- |
| `+0x08` | u32 | local/private IPv4, net order | `0x022E341A mov [esi+8],eax` (after `inet_addr`) |
| `+0x0C` | SOCKET | the UDP socket | `0x022E3430 mov [esi+0xC],eax` (after `socket`) |
| `+0x10` | sockaddr_in | `sa1` = STUN1 : **portA** | `0x022E3C43 [+0x10]=2`, `+0x12`=htons(m_portA), `+0x14`=inet_addr |
| `+0x20` | sockaddr_in | `sa2` = STUN1 : **portB** | `0x022E3C7B/0x022E3C92/0x022E3C97` |
| `+0x40` | sockaddr_in | `sa3` = STUN2 : **portB** | `0x022E3CA0/0x022E3CBE/0x022E3CD4` |
| `+0x50` | i32 | `m_step` (0 idle, 1–4 probes, 5 terminal) | `0x022E3F55 mov [esi+0x50],ecx` |
| `+0x54` | i32 | NAT-type result / "determined" flag (0 = unknown) | getter `0x027C0830`; cleared on timeout `0x022E459C`; gate `0x022E4120 cmp ...,1` |
| `+0x58` | u8 | 1-byte request opcode buffer (see §2) | `0x022E4699 mov byte [esi+0x58],1` |
| `+0x59` | u8[0x100] | recv buffer | `0x022E4145 lea edi,[esi+0x59]`; `recvfrom(...,0x100,...)` |
| `+0x15C` | sockaddr | `from` for `recvfrom` (len local = 0x10) | `0x022E415A lea eax,[esi+0x15C]` |
| `+0x16C` | u16 | mapped port #1 (net order) | `0x022E41D1 mov [ebx],ax` after `htons` |
| `+0x170` | u32 | mapped IP #1 (net order) | `0x022E41B4 mov [esi+0x170],eax` after `htonl` |
| `+0x174` | u16 | mapped port #2 | `0x022E42E0` |
| `+0x178` | u32 | mapped IP #2 | `0x022E42B6` |
| `+0x17C` | u16 | mapped port #3 | `0x022E4487` |
| `+0x180` | u32 | mapped IP #3 | `0x022E4458` |
| `+0x184` | Timer (0x28) | retransmit / keepalive timer | ctor `0x022E47E3`→`0x027C3A90`; `isExpired 0x027C3780`, `restart 0x027C3410`, `setInterval 0x027C3B60` |
| `+0x1A0` | i32 | retry counter, threshold **10** | `0x022E3DC2 cmp eax,0xA` |
| `+0x1A4` | u8 | awaiting-reply flag | set `0x022E403C`, cleared on reply `0x022E419A` |
| `+0x1A8` | i32 | **MTU/payload size**, `-1` = unmeasured | `0x022E36AA` etc. |
| `+0x1AC` | vector (16B) | parallel `vector<bool>::reference` cache for list 1 | ctor `0x022E47EA`, push at `0x022E49F7` |
| `+0x1C0` | vector (16B) | **STUN server list 1** — `_Myfirst +0x1C4`, `_Mylast +0x1C8` | `0x022E3AC1 sub edi,[esi+0x1C4]`, `sar edi,5` |
| `+0x1D4` | vector\<bool\> | availability bitset for list 1 | `0x022E3AD6 lea ecx,[esi+0x1D4]` → `0x004C0660` |
| `+0x1F0` | char* | c_str of the *currently selected* STUN1 IP | `0x022E3B3D mov [esi+0x1F0],eax` |
| `+0x1F4` | vector (16B) | reference cache for list 2 | `0x022E4AF7` |
| `+0x208` | vector (16B) | **STUN server list 2** — `_Myfirst +0x20C`, `_Mylast +0x210` | `0x022E3B7A` |
| `+0x21C` | vector\<bool\> | availability bitset for list 2 | `0x022E3B8F` |
| `+0x238` | char* | c_str of currently selected STUN2 IP | `0x022E3C00` |
| `+0x23C` | u32 | **STUN port A** | `0x022E3019 mov [ecx+0x23C],eax` |
| `+0x240` | u32 | **STUN port B** | `0x022E301F mov [ecx+0x240],edx` |
| `+0x244` | u8 | socket-opened flag | `0x022E3D1E`, tested `0x022E40E1` |

### Mapped-endpoint record (the thing handed to the result sink)
8 bytes, three of them at `+0x16C`, `+0x174`, `+0x17C`:
`{ u16 port (net order); u16 pad; u32 ip (net order) }`.
Confirmed by the relay path building the same shape on the stack:
`0x022E3EF4 mov [ebp-4], this->m_08` with the pointer `&[ebp-8]` passed as arg —
IP at `+4`, port at `+0`.

### STUN-server vector element
Stride **0x20** (`sar reg,5` at `0x022E3797`, `0x022E3AC7`, `0x022E3B83`, …).
Bytes `+0x00..+0x1B` are an MSVC `std::wstring` with iterator-debug layout:
`_Myproxy@+0`, `_Bx@+4` (16B union), `_Mysize@+0x14`, `_Myres@+0x18`.
Proof of the SSO test: `cmp dword [elem+0x18], 8 ; jb → lea eax,[elem+4] (inline)
; else mov eax,[elem+4] (heap ptr)` at `0x022E382D`, `0x022E3B27`, `0x022E39AF`.
`_BUF_SIZE == 8` ⇒ `wchar_t`. The remaining 4 bytes at `+0x1C` are never touched
by any code I read — **unknown**.

The build is a **debug-iterator build** (`_ITERATOR_DEBUG_LEVEL >= 1`): every
vector carries a `_Container_proxy*` at `+0` allocated with `operator new(8)`
(`0x022E47F0 push 8 ; call 0x00423460`).

### Server-selection allocator
`resetIPAddress` (`0x022E3780`) scans the bitset for the first **set** bit, clears
it, and uses that index; returns `false` (al=0) when none is left.
`OpenStunSocket` (`0x022E3AB0`) picks `rand() % count` (`0x027C2E60`, then `div`)
and clears that bit instead. When `sendMessage` exhausts both lists it **re-sets
every bit** (`or [edx],eax` loops at `0x022E3E30` and `0x022E3E93`) and retries.

---

## 5. MTU discovery (`FindMTUSize` @ `0x022E3500`)

- `LoadLibraryW(L"icmp.dll")`; `GetProcAddress` for the plain-ASCII names
  `IcmpCreateFile` (`0x35B61D4`), `IcmpCloseHandle` (`0x35B61C4`),
  `IcmpSendEcho` (`0x35B61B4`).
- Target = `inet_addr(list1[0].c_str())` — the first STUN server.
- `IP_OPTION_INFORMATION` from `GlobalAlloc(GPTR,8)`: `Ttl=0xFF`, **`Flags=0x02`
  (IP_FLAG_DF)** (`0x022E35C4/0x022E35C8`).
- `IcmpSendEcho(h, dst, data(0xAA-filled, size), size, opts, reply, size+0x1C, 1000)`
  — binary search on `size` between `lo=0x200` and `hi=0x5C0`, seeded at `0x5C0`
  (= 1472 = 1500 − 28).
- Result **bucketed** into `m_mtu (+0x1A8)`:

  | measured | stored |
  | --- | --- |
  | >= 1472 (0x5C0) | **1472** |
  | >= 1280 (0x500) | **1280** |
  | >= 1024 (0x400) | **1024** |
  | else | **512** |

- Either DLL or proc load failure ⇒ `m_mtu = 512` (`0x022E3710`, `0x022E372F`).
- Logs `"[UDP MODULE] Client MTU Size is [%d]"` (line 426).

A server that fragments its own UDP payloads should assume the client will only
ever advertise one of **{512, 1024, 1280, 1472}**.

---

## 6. Public surface

The class is a **singleton at global `0x03A5C9B4`**. Every external touch found by
scanning the image for that immediate:

| caller | source file (TU) | what it calls |
| --- | --- | --- |
| `0x01660170` | `RDCommon.cpp` (116) | ctor `0x022E47B0` — creates the singleton |
| `0x01169500` | `GameCoreNetwork_NOTI_Etc.cpp` (63) | **S→C config**, see below |
| `0x0110DE10` | `GameCoreNetwork_CMD_Etc.cpp` (60) | `0x022E4600` (arm / re-report) |
| `0x026F0DB0` @ `0x026F3330` | `CNSelectCharacterModule.cpp` (36) | **`sendMessage(1)`** — kicks off discovery at character select, once-guarded by `[edi+0x75]` |
| `0x01644210` | `GameCoreSub.cpp` (65) | `proc()` — per-frame tick |
| `0x01656250` | `RDCommon.cpp` (116) | `proc()` — per-frame tick |
| `0x013E27E0` | `CNRDUserInfoPacketHandler.cpp` (32) | `isStep5`/`getMappedIP`/`getMappedPort` |
| `0x022E7B40`, `0x022E8E10` | `CNRDUdpModule.cpp` (31) | consumers of the identify result |
| `0x0289FB20` | (unattributed) | `getState(+0x54)`, `getMappedIP`, `getMappedPort` |
| `0x01202AD0` | `Localization\UDPSuccessRateChecker.cpp` (77) | telemetry |

### Methods (the 6 anchored + the un-anchored rest of the class)

| VA | name (from its own literal) | signature |
| --- | --- | --- |
| `0x022E3010` | — | `void setStunPorts(u32 portA, u32 portB)` → `+0x23C`,`+0x240`; `ret 8` |
| `0x022E3030` | — | `bool isStep5()` → `m_step == 5` |
| `0x022E3040` | — | `u32 getMappedIP()` → `+0x170` |
| `0x022E3050` | — | `u16 getMappedPort()` → `+0x16C` |
| `0x022E3060` | — | `u32 resolve(const wchar_t*)` = `inet_addr(narrow(s))`; `ret 4` |
| `0x022E3300` | **`CNRDUdpIdentify::create`** | local IP + socket + FIONBIO + bind :5063 |
| `0x022E3500` | **`CNRDUdpIdentify::FindMTUSize`** | §5 |
| `0x022E3780` | **`CNRDUdpIdentify::resetIPAddress`** | `bool` — roll to next STUN pair, rebuild `sa1/sa2/sa3` |
| `0x022E3AB0` | **`CNRDUdpIdentify::OpenStunSocket`** | random STUN pair + sockopts + `m_244=1` |
| `0x022E3D30` | **`CNRDUdpIdentify::sendMessage`** | `void __thiscall(int step)`, `ret 4` — §2/§3 |
| `0x022E4080` | **`CNRDUdpIdentify::proc`** | tick: gates, lazy open/MTU, one `recvfrom`, classify |
| `0x022E4600` | — | `bool` — if `m_54==0` lazily `OpenStunSocket` and return false; else `FindMTUSize` if needed, re-invoke the result sink with `m_54`, return true |
| `0x022E4650` | — | `init()` — zeroes everything, `m_58=1`, `m_mtu=-1` |
| `0x022E4750` | — | `clear()` — empties both server lists + bitsets, `m_mtu=-1` unless already 512 |
| `0x022E47B0` | — | **ctor** — builds 4 vectors + 2 vector\<bool\>, then `init()` + `create()` |
| `0x022E4970` | — | `addStunServer1(const wchar_t* ip)` — push_back + availability bit `true` |
| `0x022E4A70` | — | `addStunServer2(const wchar_t* ip)` — ditto for list 2 |
| `0x057018B9` (.vmp1) | — | **result sink**, `__thiscall(void* endpointRecord, int code)`. VMProtect-virtualised (`push 0x45ECF03A ; jmp vm`), body unreadable. A thunk `0x022E3080 jmp 0x057018B9` exists inside the class range. INFERENCE: it stores the code into `m_54` and advances `m_step` to 5, since nothing else in readable code ever writes either to a non-zero value. |

### S→C configuration packet (from `GameCoreNetwork_NOTI_Etc.cpp` @ `0x01169A24`)

Read primitives used: `read_u32 = 0x027C5310`, `read_wstring = 0x0274FCB0(dst, maxChars, fallback)`.
(Note: the read primitives are at **`0x027C5xxx`**, i.e. the task prompt's
`0x023C5xxx` addresses plus the `0x00400000` image base.)

```
u32   count1
clear()                              ; 0x01169A32 -> 0x022E4750
wstring[count1]  -> addStunServer1   ; 0x01169A63
u32   portA
u32   portB
u32   count2
wstring[count2]  -> addStunServer2   ; 0x01169AC4
setStunPorts(portA, portB)           ; 0x01169AE6 -> 0x022E3010
if (count1 == 0) addStunServer1(L"0.0.0.0")   ; 0x01169B07
if (count2 == 0) addStunServer2(L"0.0.0.0")   ; 0x01169B28
```

The `"0.0.0.0"` sentinel is exactly what `proc()` step 3 tests against
(`0x022E43F9`, literal `0x3247A5C`) to decide whether step 4 is even possible.

### What the client reports back (`CNRDUserInfoPacketHandler.cpp` @ `0x013E28A3`)

```
if (udpIdentify->isStep5()) {
    rec[+0] = udpIdentify->m_08;                  // private IPv4  (0x01A182F0)
    rec[+4] = udpIdentify->getMappedIP();         // public IPv4, net order
    rec[+8] = htons(udpIdentify->getMappedPort()) // public port, HOST order (u16)
}
```

---

## 7. Log-line map (`log(level, __FILE__, func, __LINE__, fmt, ...)` = `0x027BE270`)

The decryptor is `0x027BACC0`; wide→narrow is `0x027BADD0`; `inet_ntoa` result
copy is `0x027BAD50`.

| line | function | text |
| ---: | --- | --- |
| 135 | create | `SOCKET ERROR : %d` |
| 170 | create | `Socket Error!` |
| 310 | FindMTUSize | `[MTU] ICMP Function load Error` |
| 426 | FindMTUSize | `[UDP MODULE] Client MTU Size is [%d]` |
| 432 | FindMTUSize | `[MTU] ICMP Library load Error` |
| 474 | sendMessage | `CNRDUdpIdentify::sendMessage retryCount 오류로 아이피를 reset합니다.` |
| 500 | sendMessage | `Connect to Relay` |
| 541 | sendMessage | `SEND NAT CHECK %d` |
| 552 | sendMessage | `SEND PORT RESTRICT CHECK %d` |
| 561 | sendMessage | `SEND IP DEPENDENT SYMMETRIC CHECK %d` |
| 645 / 647 | proc (step 1) | `CHECK IP : [%s]` / `->[%s:%d]` |
| 676 / 678 | proc (step 2) | `CHECK IP : [%s:%d]` / `->[%s:%d]` |
| 816 / 818 | proc (step 4) | `STEP_IP_DEPENDENT_SYMMETRIC_CHECK IP : [%s:%d]` / `->[%s:%d]` |
| 852 | proc (timeout) | `\nSTUN Server로 부터 응답 없음` (via `0x027BE370`, a second log entry point) |
| 950 / 986 / 988 | resetIPAddress | `\n스턴서버%s ,%d,%d\n` / same / `\n스턴서버2%s ,%d,%d\n` |
| 1063 / 1070 | OpenStunSocket | `UDP SERVER CONNECT : [%s]` / `UDP SERVER2 CONNECT : [%s]` |

The `resetIPAddress` and `OpenStunSocket` logs are gated on a global debug byte
`0x03B55961`; the rest are unconditional. Line ordering is self-consistent with a
single ~1100-line source file (create < FindMTUSize < sendMessage < proc <
resetIPAddress < OpenStunSocket), which independently corroborates the file
attribution. The one oddity is FindMTUSize's line 310 for the proc-load error
sitting far below its own 426/432 — reported as observed, not explained.

---

## 8. Span assessment

**The briefing's span is correct in name but too NARROW, not too wide.**

Every literal inside `0x022E3300`–`0x022E4080` names `CNRDUdpIdentify::*` — the
file attribution is solid. But the anchor method (outermost `__FILE__` reference)
misses the members that contain no log call:

- **below `lo`**: `0x022E3010`, `0x022E3030`, `0x022E3040`, `0x022E3050`,
  `0x022E3060`, `0x022E3080` (vmp thunk) — the accessors, all provably operating
  on this class's field offsets.
- **above `hi`**: `0x022E4600`, `0x022E4650` (init), `0x022E4750` (clear),
  `0x022E47B0` (ctor), `0x022E4970` / `0x022E4A70` (addStunServer1/2) — all
  provably this class (ctor at `0x022E48E8` calls `create()` at `0x022E3300`;
  init/clear touch `+0x1A8`, `+0x1C0`, `+0x21C`, `+0x244`).

The real module runs roughly `0x022E3010`–`0x022E4B6C`. `0x022E4B70` onward is a
different class (touches `[esi+0x20A330]`, `[esi+0x10001C]`) despite
`functions.csv` merging it into the `0x022E4A70` span. `0x022E30BC`
(556 B, VMP-mutated, calls `0x022E32E8`) is inside the range but I could not read
it — **unknown**.

---

## 9. What a server implementer needs

1. **Stand up a STUN responder** on two IPs × two ports. Protocol is 1 request
   byte in / 7 response bytes out (§2). Ports and IP lists are entirely
   server-chosen and pushed via the NOTI in §5 — nothing is hardcoded except the
   client's own bind port **5063**.
2. `0x05` must be answered **from a different source port** and `0x06` from the
   **second server IP**, or the client will classify as port-restricted /
   symmetric and fall back to relay.
3. Send `count1 = 0` / `count2 = 0` (or the literal `0.0.0.0`) to deliberately
   short-circuit the probe — the client substitutes `"0.0.0.0"` and step 3 then
   terminates at result 2 without ever running step 4.
4. Expect a **1-byte `0x00` keepalive every 10 s per client** for the whole
   session once discovery completes (unless result was type 1).
5. The client's advertised MTU is one of **{512, 1024, 1280, 1472}**.
6. The endpoint the client publishes to peers is
   `{private IP, public IP (net order), public port (host order u16)}`.
