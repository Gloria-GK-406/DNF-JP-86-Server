# TU 48 — `DNFClient\RDAR\ClientArgumentParser.cpp`

Briefing span `0x00E0CCE0`–`0x00E0D850` (15 functions, 0 parsers).
**Real module span is wider: `0x00E0CBD0` – `0x00E0EE80`** (see "Span is too narrow").

Image base `0x00400000`. All literals below were recovered with `decstr.dec()`;
every literal in this file is **UTF-16 (`D2`)**, so every string member of the
class is `std::wstring`, not `std::string`.

---

## 1. What this unit implements

`ClientArgumentParser` is the client's **launcher command-line parser and the
holder of everything the launcher hands the client**: the channel-server
IP/port to connect to, the account id and login ticket, and a set of policy
flags (age rating, sex, web-portal brand, pass-pad type, pre-download).

The command line is **not** `argc/argv`. It is a single string split on `'?'`
into a **fixed 16-slot vector** of wide strings. Slot *n* has a fixed meaning.
Every consumer in the client reaches it through the singleton
`getInstance()` at `0x00E0EA60` (**101 callers**) and never touches members
directly (verified: a scan of all 101 callers found zero direct member reads).

There is a second, China/Tencent-only path: when the DebugConfig key
`connect_by_tcls_launcher` is set, the 16 slots are **not** taken from the
command line at all — they are rebuilt from `TenProxy.dll` through
`DNFClient\RDAR\TCLS\TCLSWapper.cpp`.

This TU contains **no packet parsing** (`parsers: 0` in the index is correct),
but it is squarely server-relevant: it decides *where the client connects* and
*what credentials it presents*.

---

## 2. Server-implementer payload

### 2.1 The 16-slot launcher record

Built by `parseCommandLine` (`0x00E0ECD0`):

```
0x00E0ED36  push 0x31eaf1c            ; L"?"          <- the delimiter
0x00E0ED45  push esi                  ; esi = this+0xC0 (the vector)
0x00E0ED46  call 0x16306b0            ; Common.cpp!getSeparatedTextUnits
0x00E0ED50  call 0xe0ead0             ; pad the vector out to exactly 16 slots
0x00E0ED5B  call 0xe0d010             ; parseCommandLineChina (TCLS override)
0x00E0ED77  call 0xe0e550             ; apply slots -> members  (part A)
0x00E0ED82  call 0xe0e770             ; apply slots -> members  (part B)
0x00E0ED89  call 0xe0d850             ; loggingArgument
```

`0x00E0EAD0` pads with the empty wide literal `0x3043960` (= `L""`) until
`(_Mylast-_Myfirst)/4 >= 0x10` (`cmp ecx, 0x10; jae` at `0x00E0EB54`). That is
why every later index up to 15 is unconditional.

| slot | meaning | evidence |
| ---: | --- | --- |
| 0 | **connect type** | `getArgInt(0)`; `CNSelectChannelSocket::getAddress` logs `CHANNELSERVER FROM LAUNCHER: CONNECT TYPE (%d) - %s (%d)` at `0x010BF226`. `==99` → take address+port from the launcher (`0x010BF1C1 cmp eax,0x63`). Also drives `connectChannelServer` / `skipLogin` (below). |
| 1 | **channel-server IP** (wide) | `CNGameSocket::OnDisconnectSocket` logs `접속종료 : ip %s port %s moduleType %d` with `getArg(1)` as ip, `getArg(2)` as port (`0x0274E62C`/`0x0274E63B`, literal at `0x35e84d0`). Also copied to wstring `@+0x04`. |
| 2 | **port** (wide) | same log line; `_wtoi` → int `@+0x24` at `0x00E0E63D`. |
| 3 | **account id** | → wstring `@+0x28` (`0x00E0DE66`). Also `wcstoul` base 10 → CQQRSInfo tag `0x21` (`0x00E0E860`–`0x00E0E87B`). Length ≥ 31 ⇒ replaced with `L"invalid_id"` (`0x00E0DDF4 cmp eax,0x1f`). |
| 4 | **login ticket / password** | → wstring `@+0x48` (`0x00E0DEA5`). Read together with the id by `GameCoreSub.cpp` `0x01641DC0`. |
| 5 | int → `@+0x88` | `0x00E0E83D`. Default `0x109` (265). No reader found. |
| 6 | unused | never read; TCLS sets it to `L"1"`. |
| 7 | **web site** 0..2 | `0x00E0E677`, clamped `0 <= v < 3` → `@+0x8C`. `0=DFIGHTER 1=HANGAME 2=NEXON`. |
| 8 | **age** (int) | `0x00E0E6AE` → `setAge` (`0x00E0CF70`) → guarded int `@+0x90`. Negative rejected. |
| 9 | **sex** 0..1 | `0x00E0E6EA`, clamped `0 <= v < 2` → `@+0x98`. `0=Male`, nonzero prints `Female`. |
| 10 | unused | TCLS sets `L"0"`. |
| 11 | **channelServerType** 0..5 | `0x00E0E761`, clamped `0 <= v < 6` → `@+0x9C`. |
| 12 | digit/letter pair sequence | `0x00E0E714` → `0x00E0DF60` → `0x02745E60` on global `[0x3B7CE7C]`. `0x02745E60` walks `'0'-'9'` followed by `'a'-'z'` pairs into a table at `[obj+0x44]`. Purpose **unknown** (see §6). |
| 13 | unused | not written even by the TCLS path. |
| 14 | **preDownLoad** bool | `0x00E0E951 cmp eax,ebx; setne al` → `@+0xBD`. |
| 15 | **jump signature** (wide) | `0x00E0E996` → wstring `@+0x68`. Consumed by `PopupWindow\China\WebHelperWindow.cpp` (`0x012AC080`). |

### 2.2 Two derived booleans from slot 0 (`0x00E0E550`)

```
n = _wtoi(argv[0])                     ; missing/empty => treated as "true" branch
this->connectChannelServer(+0x00) = (n < 10 || n == 99)        ; 0x00E0E57B..0x00E0E58E
this->skipLogin           (+0x01) = (n != 99 && n > 20)        ; 0x00E0E5B8..0x00E0E5CB
```

So `argv[0] = 99` ⇒ connect through the channel server **and** do not skip
login — and it is exactly the value the China/TCLS path hardcodes.

### 2.3 `parseCommandLineChina` (`0x00E0D010`) — the TCLS override

Gate: `0x00E0D040 call 0x0285FCE0` (DebugConfig singleton, object `0x4C6D518`,
built by `0x0285F230` from `DebugConfig.txt` / `_DevConfig.xml`) then
`0x00E0D045 cmp byte ptr [eax+0xEC], 0; je <return true>`.
**`[cfg+0xEC]` is the key `connect_by_tcls_launcher`** — proved by
`0x0285F9EC lea edx,[esi+0xec]` sitting under that literal inside `0x0285F230`.

When enabled it sets `this->connectChannelServer = 1` (`0x00E0D057
mov byte ptr [esi], 1`), logs `MODE - TCLS`, constructs a `TCLSWapper`
(`operator new(0xC)` + `0x12E8630`, which carries the literal `TenProxy.dll`),
and calls `0x12E86A0` to load it. On failure:
`MessageBoxW(0, L"TenProxy.dll load Failed", L"Run Error", 0x10)` at
`0x00E0D0E5`, return false. If any getter fails:
`L"tcls Parsing Failed"` at `0x00E0D5F6`, return false.

Slot fill (index derived from the `cmp <size>, N; ja` bound-check that precedes
each `lea [eax+N*4]`; verified programmatically):

| slot | source |
| ---: | --- |
| 0 | `L"99"` (`0x31EABB8`) |
| 1 | `TCLSWapper::getServerinfo` out#1 (`0x12E9970`, 3 out-params) — the IP |
| 2 | `TCLSWapper::getServerinfo` out#2 — the port |
| 3 | `0x12E87A0(2, &out)` — vtable slot `[+0x24]` on the DLL object, number formatted to wide |
| 4 | `TCLSWapper::getGameSignature` (`0x12E8880`; literals `timeStamp = '%s'`, `dnfchina`) |
| 5 | `TCLSWapper::getServerinfo` out#3 (that function logs `selectServerIndex : %d`) |
| 6 | `L"1"` |
| 7,8,9,10 | `L"0"` → DFIGHTER / age 0 / Male / — |
| 11 | `L"2"` → channelServerType = `RELEASE` |
| 12 | `0x12E8E10` — a **randomly generated** 4..7 × (unique digit, random `a-z`) string, seeded `srand(GetTickCount())` |
| 14 | `L"0"` → preDownLoad = false |
| 15 | `TCLSWapper::getJumpSignature` (`0x12E8CA0`) |

Slot 13 is left as whatever the `'?'` split produced.

### 2.4 The `run_client_alone` shortcut (private-server relevant)

`0x00E0EAD0` reads `[cfg+0x0A]` (`0x00E0EAFF cmp byte ptr [eax+0xa],0`) — that
offset is the DebugConfig key **`run_client_alone`** (`0x0285F6B0 lea edx,[esi+0xa]`).
If set it raises a global bool at `0x3A5AFCC` under the CS at `0x3A5AFD4`.
`0x00E0DCF0` reads that same bool (`0x00E0DD58 call 0x27bc2b0`) and, when true,
forces **id `@+0x28` = `L"test"` and ticket `@+0x48` = `L"test"`**
(`0x00E0DD75`/`0x00E0DDA2`, literal `0x31EAF00`), bypassing slots 3 and 4
entirely.

### 2.5 Enum tables (verbatim, from the jump tables)

`channelServerType` — `getChannelServerTypeName()` `0x00E0CDA0`, jump table at
`0x00E0CE0C` = `{E0CDB2, E0CDC0, E0CDCE, E0CDDC, E0CDEA, E0CDF8}`:

| value | name |
| ---: | --- |
| 0 | `DEVZONE` |
| 1 | `FIRST` |
| 2 | `RELEASE` |
| 3 | `RELEASE_TEST` |
| 4 | `TEST` |
| 5 | `BROADCAST_PVP` |
| 6 | **constructor default** — out of range, getter returns NULL |

`passPad` — `setMouseRegisterState()` `0x00E0CCE0`: `0=PASSPAD_NONE`,
`1=PASSPAD_MOUSEREGISTER`, `2=PASSPAD_GOBLIN` (logs
`used Pass Pad Type : [ PASSPAD_xxx ]`, lines 0x26F/0x272/0x275).

`webSite` — from `loggingArgument` `0x00E0D950`: `0=DFIGHTER`, `1=HANGAME`,
`2=NEXON`. Constructor default is **3** (out of range).

`ENUM_AGE_TYPE` (byte at `+0x02`) — validated by
`GameCoreSub.cpp!ENUM_AGE_TYPE::operator=` (`0x0163D8B0`), legal range
`[-1, 2]`, else logs `invalid enum value ENUM_AGE_TYPE [ %d ]`. `0x00E0CC70`
maps it to a tag string used elsewhere: `0 → L"18"` (`0x31EA840`),
`1 → L""` (`0x3043960`), `2 → L"TN"` (`0x31EA850`). Constructor default = 2.

---

## 3. Object layout (`ClientArgumentParser`, singleton at `0x039B0618`)

`getInstance()` = `0x00E0EA60`: guard bit0 of `[0x39B06EC]`, ctor `0x00E0E000`
on `0x39B0618`, `atexit` dtor `0x2FFB7B0`, returns `0x39B0618`.

`std::wstring` here is the `_SECURE_SCL` 28-byte layout:
`+0x00 _Myproxy, +0x04 _Bx[16], +0x14 _Mysize, +0x18 _Myres`
(proved by the ctor writing `_Myres = 7` — i.e. `16/2 - 1`, wide — at
`0x00E0E05D`, and by every `c_str()` thunk testing `_Myres >= 8`).

| off | type | proof |
| --- | --- | --- |
| `+0x00` | `bool connectChannelServer` | `0x00E0D057 mov byte ptr [esi],1`; printed as `connect channel server : %s` at `0x00E0D859`; set at `0x00E0E58E` |
| `+0x01` | `bool skipLogin` | `0x00E0D8AE cmp byte ptr [ebx+1],0` → `skip Login : %s`; set at `0x00E0E5CB` |
| `+0x02` | `ENUM_AGE_TYPE` (byte) | ctor `0x00E0E02D call 0x163c5e0`; default 2 via `0x00E0E1F2 call 0x163c5f0(2)`; setter `0x00E0CC70 add ecx,2; mov [ecx],al` |
| `+0x04` | `std::wstring` — channel-server address | ctor `0x00E0E035 lea ebx,[esi+4]`; `c_str()` = `0x00E0CF20` (`cmp [ecx+0x1c],8 / [ecx+8]`); filled from slot 1 at `0x00E0E606` |
| `+0x20` | dword — **unknown** | inside the 0x20..0x27 gap; nothing writes it in this module |
| `+0x24` | `int port` | ctor `0x00E0E213 mov [esi+0x24],edi`; set `0x00E0E63D` from `_wtoi(argv[2])`; read by the folded getter `0x005CCAB0 (mov eax,[ecx+0x24]; ret)` at `0x010BF204` |
| `+0x28` | `std::wstring id` | ctor `0x00E0E06B lea ebx,[esi+0x28]`; `c_str()` = `0x00E0CF30` (`cmp [ecx+0x40],8 / [ecx+0x2c]`) — **9 external callers, the hottest export of this file** |
| `+0x48` | `std::wstring` — login ticket | ctor `0x00E0E0A4`; `c_str()` = `0x00E0CF40` (`[ecx+0x60]`/`[ecx+0x4c]`) |
| `+0x68` | `std::wstring` — jump signature | ctor `0x00E0E0DC`; `c_str()` = `0x00E0CF50` (`[ecx+0x80]`/`[ecx+0x6c]`) |
| `+0x88` | `int` — **unknown**, default `0x109` | ctor `0x00E0E260`; set from `_wtoi(argv[5])` at `0x00E0E83D`; no reader found |
| `+0x8C` | `int webSite` | ctor `0x00E0E26A` = 3; set `0x00E0E677`; printed `0x00E0D947` |
| `+0x90` | **guarded int `age`** (8 bytes: header `+0x90`, storage `+0x94`) | ctor `0x00E0E123 call 0x27bcae0`; setter `0x00E0CF70`; getter dispatch at `0x00E0D9B1`; `&obj` accessor `0x00E0CBD0 (lea eax,[ecx+0x90])` |
| `+0x98` | `int sex` | ctor `0x00E0E2CD` = 2; set `0x00E0E6EA`; printed `0x00E0DA4C` (`0 → Male`) |
| `+0x9C` | `int channelServerType` | ctor `0x00E0E2DD` = 6; setter `0x00E0CD70` (clamps `<= 5`); name via `0x00E0CDA0` |
| `+0xA0` | `int passPadType` | ctor `0x00E0E2D7` = 0; setter `0x00E0CCE0` (clamps `<= 2`); compare helper `0x00E0CC40` |
| `+0xA4` | `bool` — **unknown** | ctor `0x00E0E2F3` = 0; setter `0x00E0CCD0`; re-zeroed at `0x00E0E91D` |
| `+0xA8` | `std::map<int,int>` serverId → charCount | ctor `0x00E0E19C call 0x13bef90`; `operator[]` write `0x00E0EA30`; lookup `0x00E0DC90`; iterated in `loggingArgument` printing `server:%d\tcharCount:%d` (`0x00E0DBB8`, key at node`+0xC`, value at node`+0x10`, `_Isnil` at node`+0x15`) |
| `+0xBC` | `bool` — **unknown** | ctor `0x00E0E2EC mov word ptr [esi+0xbc],di` clears both; getter `0x00E0CD90` (caller `0x02540CB0`, which then calls `getCharCount`) |
| `+0xBD` | `bool preDownLoad` | same clear; getter `0x00E0CC60`; set `0x00E0E956`; printed `0x00E0DC0E` |
| `+0xC0` | `std::vector<wstr>::_Myproxy` | ctor `0x00E0E1A1 lea edi,[esi+0xc0]` |
| `+0xC4` | `_Myfirst` (element size **4**) | every access does `sub; sar eax,2` |
| `+0xC8` | `_Mylast` | " |
| `+0xCC` | `_Myend` | `0x00E0EB9F mov ecx,[esi+0xcc]` in the grow path |

**Vector element type.** `(_Mylast-_Myfirst) >> 2` everywhere, and
`0x00E0CE30` returns the raw dword which is then handed straight to `_wtoi`
(`0x02C65A29`) and walked as `wchar_t*` (`0x00E0E982 mov cx, word ptr [eax]`).
So the element is a **4-byte wide-string class holding the character pointer
directly** (assignment via thiscall `0x027BFC10 (const wchar_t*)` and
`0x027BFBA0 (const std::wstring&)`), not `std::wstring`.

---

## 4. Public surface of the TU (called from outside)

| VA | role | external callers |
| --- | --- | --- |
| `0x00E0CF30` | `getId()` → `wstring@+0x28` c_str | **9**: `CNSelectChannelSocket.cpp`, `SnapShot.cpp`, `GameCoreInit.cpp`, `GameCoreSub.cpp` ×2, `CCrashReporter.cpp` ×2, `0x012D1BD0`, `0x01386970`, `0x017536D0` |
| `0x00E0CCE0` | `setMouseRegisterState(int)` + log | `GameCoreNetwork_CMD_Etc.cpp` (`0x0110DE10`), `0x02610030` |
| `0x00E0CEF0` | `setAgeType(int)` (validate then `0x00E0CC70`) | `GameCoreNetwork_NOTI_Etc.cpp`, `GameCoreDebug.cpp` |
| `0x00E0CF20` | `getAddress()` → `wstring@+0x04` c_str | `CNSelectChannelSocket.cpp`, `GameCoreSub.cpp` |
| `0x00E0CD70` | `setChannelServerType(int)` (clamp ≤5) | `0x016388D0` |
| `0x00E0CD90` | `get<bool @+0xBC>()` | `0x02540CB0` |
| `0x00E0CE30` | `getArg(idx)` → raw `wchar_t*`, idx ≤ 15 | `CNGameSocket.cpp` |
| `0x00E0CEC0` | `hasArg(idx)` → bool | `CNGameSocket.cpp` |
| `0x00E0CE70` | `getArgInt(idx)` = `_wtoi(getArg(idx))` | `CNSelectChannelSocket.cpp` |
| `0x00E0CF40` | `wstring@+0x48` c_str (ticket) | `GameCoreSub.cpp` `0x01641DC0` |
| `0x00E0CF50` | `wstring@+0x68` c_str (jump sig) | `PopupWindow\China\WebHelperWindow.cpp` |
| `0x00E0CF70` | `setAge(int)` (guarded store) | `0x00E0E550` (in-module) |
| `0x00E0CDA0` | `getChannelServerTypeName()` | `loggingArgument` only |
| `0x00E0D010` | `parseCommandLineChina(wstring&)` | `0x00E0ECD0` (in-module) |
| `0x00E0D850` | `loggingArgument()` | `0x00E0ECD0` (in-module) |

Outside the briefing span but part of the same class and reached from
elsewhere: `getInstance` `0x00E0EA60` (101 callers), `parseCommandLine`
`0x00E0ECD0` (caller `0x0163A850`), `getCharCount` `0x00E0DC90`,
`setCharCount` `0x00E0EA30`, `isPassPadType` `0x00E0CC40` (9 callers),
`getPreDownLoad` `0x00E0CC60`, `isAgeType` `0x00E0CBF0` (33 callers),
`getAgeType` `0x00E0CBE0`, `&age` `0x00E0CBD0`.
Also note **`0x005CCAB0` is a COMDAT-folded `return [this+0x24]`** shared with
28 other classes; used as this class's `getPort()` at `0x010BF204`.

`loggingArgument` (`0x00E0D850`) is a pure trace dump — source lines
0x55E, 0x55F, 0x56F, 0x573, 0x574, 0x575, 0x576, 0x577, 0x58D, 0x58F — and is
the cleanest field-name evidence in the unit.

---

## 5. Span is too narrow

The briefing's `hi` is the *start* of the last `__FILE__` anchor, so it clips
the file. Two independent lines of evidence extend it:

1. **Literal-pool contiguity.** The pool `0x031EA840`–`0x031EAF00` is one
   uninterrupted run belonging to this file (it begins with `L"18"`/`L"TN"`,
   contains the `__FILE__` at `0x031EA860`, and ends with `L"invalid_id"` /
   `L"test"`). The next file's `__FILE__` (`CNAdvanceAltarModule.cpp`) starts
   at `0x031EAF28`. `0x00E0CC70` uses `0x31EA840`/`0x31EA850`; `0x00E0DCF0`
   uses `0x31EAEE0`/`0x31EAF00`. Both are outside the briefed span.
2. **Object identity.** Every function in `0x00E0CBD0`–`0x00E0EE80` accesses
   the same layout (`+0x02`, `+0x28`, `+0x90`, `+0xA8`, `+0xC4`). The
   functions immediately *below* `0x00E0CBD0` (`0x00E0C1D0`–`0x00E0CAF0`)
   touch `word [+0x6C]` / `byte [+0x88]`, which is a different class — that is
   the real lower boundary.

Corrected span: **`0x00E0CBD0` – `0x00E0EE80`** (≈ 34 functions), with
`0x00E0EF30` = `CNAdvanceAltarModule.cpp` as the next TU.

---

## 6. Explicitly unknown

* `+0x20` (dword), `+0x88` (int, default 265, from slot 5), `+0xA4` (bool),
  `+0xBC` (bool) — set but no reader identified anywhere in the image.
* Slots 6, 10, 13 — written by the TCLS path (`L"1"`, `L"0"`, nothing) but no
  code reads them.
* Slot 12's purpose. Mechanism is proved: `0x12E8E10` builds a random string of
  4–7 pairs, each pair = a *unique* digit `0-9` (tracked in a 10-slot bitmap)
  followed by a random `a-z`; `0x02745E60` re-walks those digit/letter pairs
  into a table at `[0x3B7CE7C]+0x44`. It is a randomized digit→letter mapping,
  which is *consistent with* the pass-pad theme of this file
  (`PASSPAD_MOUSEREGISTER` / `PASSPAD_GOBLIN`), but I found no literal or call
  that names it. Do not record this as established.
* `0x031EAF14` (pushed at `0x00E0E8F5` into `CQQRSInfo::appendString`) uses the
  *other* decryptor `0x027BACA0`, not `0x027BACC0`, and does not decode with
  the known cipher. Unknown.
* `0x12E87A0` (slot 3 in TCLS mode) is a TenProxy vtable call `[+0x24]` whose
  export name is not in the image's literals.
