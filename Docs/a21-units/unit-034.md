# TU 34 — `DNFClient\RDAR\CNSelectChannelModule.cpp`

Briefing span `0x01328710`–`0x01329980`. **The span is too narrow at both ends** (see §0).
Real extent: **`0x01328250` – `0x01329C1F`**.

Full `__FILE__` literal: `D:\Work\1229\Source\DNFClient\RDAR\CNSelectChannelModule.cpp` @ `0x032873A0`.

---

## 0. Span correction (evidence)

The briefing's `lo`/`hi` come from the two functions that reference the `__FILE__`
literal (`0x01328710`, `0x01329980`). Four more functions belong to the same class:

| VA | why it is in this TU |
| --- | --- |
| `0x01328250` | Function entry in `functions.csv` covering `0x01328250..0x0132870F`. It actually holds **three** functions (`0x01328250`, `0x01328290`, `0x013282D0`); `0x01328290` and `0x013282D0` are slots `+0x28` / `+0x24` of this class's vftable `0x03286C2C`. All three start with `add ecx,0x58 / call 0x27C3410` = stop the timer member at `this+0x58` that this class's ctor constructs. |
| `0x01329890` | vftable `0x03286C2C[+0x00]` = scalar-deleting dtor; writes `[esi]=0x3286C2C` and `[esi+0x50]=0x3286C1C` — this class's two vptrs. |
| `0x01329B00` | `jmp 0x01329980` — thunk to `AutoSelectChannel`. |
| `0x01329B10`, `0x01329BA0` | `0x01329BA0` is vftable `0x03286C2C[+0x04]`; it calls `0x01329B10`, which calls `AutoSelectChannel` with the same `this`. |

Extra confirmation from `dataxrefs.csv`: the eleven file-static handles
`0x03A3DEB8, BC, C8, D4, DC, E0, E4, E8, EC, F0, F4` are referenced by **exactly two**
producers — `0x01328250` (i.e. `0x013282D0`) and the static-initializer blob
`0x02DF3903`, which also references `0x03A3DE40`, this module's singleton instance.
Nothing else in the 67 k functions touches them.

Functions `0x01329C20 / 0x01329C50 / 0x01329CC0` (immediately before TU 6
`CNHitGauge.cpp` at `0x01329D20`) are a **different** class — layout
`[0]=0x45, [8]=-1, timer@+0x1C`, no shared globals. Not this TU.

---

## 1. What this unit implements

`CNSelectChannelModule` — the client-side **channel picker / auto-connect driver** on the
channel-select screen. It does not parse packets itself (`parsers = 0`); it consumes the
channel list that `CNSelectChannelSocket.cpp` (TU 35) fills from the channel server, ranks
it, and drives repeated `connectChannel()` attempts with a retry budget and a UI error path.

Two public behaviours:

* `MinimumUserSelectChannel()` (`0x01328710`, bool) — pick one channel and connect to it.
* `AutoSelectChannel()` (`0x01329980`, void) — per-frame retry driver around it.

Class name is not recoverable: RTTI at `0x037252D8` decodes to garbage
(`rtti-full-a21.csv` row `0x03286C2C,0,.í\t,...`) — the client obfuscates RTTI names.
The names above come from the module's own decrypted log literals
(`CNSelectChannelModule::MinimumUserSelectChannel` @ `0x03287428`,
`CNSelectChannelModule::AutoSelectChannel` @ `0x03287560`).

---

## 2. Server-relevant: the channel-selection algorithm

`MinimumUserSelectChannel` — `0x01328710`, returns `bool` in `al`.
Source lines are recoverable from the log calls (`log(0, file, func, LINE, fmt, ...)`,
logger = `0x027BE270`, literal decryptor = `0x027BACC0`).

### 2.1 Inputs

```
ChannelManager singleton   = 0x00D00A40()            -> 0x039AF5C0   (TU 44 ChannelManager.cpp)
channel list               = 0x00CEF9B0()            -> 0x039AF42C   std::vector<ChannelInfo*>
"lately" record            = CNRDConfigFile 0x013868F0() -> 0x01386410(&rec)
```

`0x0137EF30` initialises the record to `{ -1, -1 }`; `0x01386410` loads 8 bytes from the
config file and returns true only if both fields `!= -1`:

```
rec.f0  @ ebp-0x60   (compared against ChannelInfo vt[0x98]())
rec.f4  @ ebp-0x5c   (channel number; compared against ChannelInfo vt[0x48]())
```
Log line 774: `[latelyGameServerInfo] 채널선택창 정보 (%d, %d - %d)` with
`(rec.f0, rec.f4, prev->vt[0x80]())`.

The load is gated: `if (0x0274CA50() && 0x0274CA50()->[+8] == 0 && 0x013868F0())`.
`0x01386410` can also be overridden when `0x0285FCE0()->[+0xF1] == 1`, in which case
`rec.f0 = 0x00E0EA60()->[+0x88]` and `rec.f4 = atoi(cfg string @ +0xF8)` — i.e. a
command-line / launcher override of the target world+channel.

### 2.2 Pass 1 — build two candidate pools (loop `0x013287B0`–`0x01328B20`)

For every `ChannelInfo* info` in the list, in list order:

```
0x0132882E  LOG line 658  "CHANNEL INFORM>>>> try channel list : CH. %d"  (info->vt[0x48]())
0x01328878  s = info->vt[0x88](&tmp)                     // wide address string
0x013288AB  if (s->compare(0, s->size(), L"127.0.0.1", 9) == 0) -> SKIP THIS CHANNEL
```

**A channel advertised as `127.0.0.1` is never auto-selected.** (literal `0x03287380`).

Then the "previous channel" candidate (`edi`, kept in `[ebp-0x7c]`), accepted when **all**
hold:

```
!info->vt[0x04]()                       // not full
 info->vt[0x48]() == rec.f4             // same channel number as last time
!info->vt[0x10]()
!info->vt[0x1C]()
( info->vt[0x98]() == rec.f0            // same world/server
  || GameCoreSub 0x0163D130( info->vt[0x30]() ) )   // type in {0x16,0x17}
```

Then the pool split on `info->vt[0x00]()`:

| `vt[0x00]()` | screening | pool |
| --- | --- | --- |
| **true** | reject if `GameCoreSub::isSpecialRegionIntegrated(info)` (`0x01647020`, log line 710); reject if `vt[0x04]() == 1` (log line 723, literal `CHANNEL INFORM>>>> iinfo->isFull()`); reject if `vt[0x2C]()` | map at `ebp-0x74` → later tried as **"normal channel"** |
| **false** | reject if `vt[0x04]()`; reject unless `0x0163D130(vt[0x30]())` (type ∈ {0x16,0x17}); reject if `0x0163D180(vt[0x30]())` (type ∈ {0x17,0x19}) ⇒ **net effect: type == 0x16 only**; logs lines 695/699 `canRandomSelected() 1` / `2` | map at `ebp-0x58` → later tried as **"Integrate channel"** |

Both pools are `std::map< int grade, std::vector<ChannelInfo*> >`
(`operator[]` = `0x01309300`, `push_back` = `0x01BF01E0`).

### 2.3 The grade key — `ChannelManager::grade(info)` @ `0x00CEF9C0`

```
if (info->vt[0x04]())            return 4;          // isFull
float f = info->vt[0x7C]();                          // load ratio, returned in st0
if (f <  *(float*)0x039AF484)    return 0;
if (f <  *(float*)0x039AF488)    return 1;
if (f <  *(float*)0x039AF48C)    return 2;
                                 return 3;
```
The three thresholds read as `0.0` in the dump ⇒ they are **runtime-configured**, not
`.rdata` constants. Their values are **unknown** from this image.

Because `std::map` iterates ascending, grade 0 (least loaded) is tried first — that is
literally what "MinimumUser" means here.

### 2.4 Pass 2 — try the previous channel (`0x01328B25`)

```
line 774  log latelyGameServerInfo
line 781  "CHANNEL INFORM>>>> try connect previous channel : CH. %d"   (rec.f4)
0x01328BD6  ok = CNSelectChannelSocket::connectChannel( info->vt[0x74](), (u16)info->vt[0x78]() )
line 789  ...success  -> return true
line 794  ...fail     -> fall through to pass 3
```

### 2.5 Pass 3 — walk the pools (`0x01328CC9` then `0x01329145`)

Identical loop, run twice: pool `ebp-0x74` first ("normal", lines 818/830/837), then pool
`ebp-0x58` ("Integrate", lines 862/869/876).

```
for (node = pool.begin(); node != pool.end(); ++node)          // ascending grade
    while (node->vec is not empty) {
        n   = (node->vec._Mylast - node->vec._Myfirst) / 4
        i   = rand() % n                                       // 0x027C2E60 = rand
        e   = *(node->vec.begin() + i)
        if (e == NULL) { vec.erase(it); continue; }
        LOG("try {normal|Integrate} channel : CH. %d", e->vt[0x48]())
        ok = connectChannel(e->vt[0x74](), (u16)e->vt[0x78]())
        if (ok)  { LOG("...success"); return true; }
        LOG("...fail");
        vec.erase(it);                                          // 0x026DF610
    }
return false;                                                   // 0x013296C6  xor al,al
```

**Uniform random pick inside a congestion grade, with removal-on-failure.** A server that
reports every channel at the same load will get a uniformly random channel; a server that
spreads the ratio will get least-loaded-first with random tie-break.

`connectChannel` is `0x010BE9F0` in `CNSelectChannelSocket.cpp` — literals
`try connectChannel`, `CHANNEL>> channel is not exists (%s : %d)`,
`Connection Failed socket error code [ %d ]`, `%d.%d.%d.%d`. Its args are
`(u32 ipv4_be, u16 port)`; `0x010BCE20` proves the pair layout
(`[0x03A38A1C]:u32 / [0x03A38A20]:u16`, and `[0x03A38A24]:u32 / [0x03A38A28]:u16`).

---

## 3. Server-relevant: the retry state machine

`AutoSelectChannel` — `0x01329980`, `__thiscall`, void. Thunk at `0x01329B00`.

Two globals form the machine (both written only through `0x01102C40`,
`SetSelectResult(int state, int channel)`):

```
0x03A5BE80   state
0x039846A0   selected channel index   (initial value in the dump = 0xFFFFFFFF)
```

Body:

```
if (this->tryCountConnect >= 6) {                       // this+0x54
    SetSelectResult(0, -1);
    UIMgr->0x02304760( 0x010A1010() );                  // close/detach current window
    msg = StringTable(0x12B7C);                         // 0x028C14C0(76668, 0)
    UIMgr->Open(0x366, msg, 0);                         // 0x02304C70
    this->tryCountConnect = 0;
    return;
}
if (this->timer(+0x58).elapsed()) {                     // 0x027C3780
    this->timer.restart(3000 /*0xBB8*/, 0);             // 0x027C3C40 -> [timer+0x18] = 3000
    ++this->tryCountConnect;
    LOG line 573 "CHANNEL INFORM>>>> tryCountConnect++ at (%s)"
    if (this->tryCountConnect == 2 && !UIMgr->IsOpen(0x271))
        UIMgr->Open(0x271, 0, 0);                       // retry-confirm popup
}
if (state == 0 && selectedChannel == -1) {
    if (MinimumUserSelectChannel()) {
        ch = selectedChannel;
        SetSelectResult(1, ch);
        0x01102C30()->[+0x134] = ch;                    // 0x01102940
    } else {
        0x004232D0(0);                                  // g_byte[0x0399FFC0] = 0
    }
}
if (state == 4) {
    SetSelectResult(0, -1);
    UIMgr->0x02304760( 0x010A1010() );
    UIMgr->Open(0x15A, StringTable(0x12B7C), 0);
}
```

**Budget: 6 ticks × 3000 ms ≈ 18 s** before the client gives up and shows the failure
popup. The popup at `0x271` appears on tick 2 (~6 s). `0x271` is
`CNRDRetryConnectChannelConfirmWindow` — proven by `SelectServerWindow.cpp` literals on
`0x0253DDB0`, which is the only other site logging
`CHANNEL INFORM>>>> CNSelectChannelSocket::tryCountConnect++ at (%s)` and which calls both
`getInstance()` (`0x01329910`) and the `AutoSelectChannel` thunk (`0x01329B00`).

State values observed (partial):

| state | set by | meaning |
| --- | --- | --- |
| 0 | this unit, `CNSelectChannelSocket::retryConnectFailprevChannel` | idle / nothing selected |
| 1 | this unit after a successful pick | channel chosen, connecting |
| 2 | `0x010BF905` when the old state was 3 | — |
| 3 | elsewhere | tested at `0x010BF8F9` |
| 4 | elsewhere | connect failed → error popup `0x15A` |

`0x010BF840` = `CNSelectChannelSocket::retryConnectFailprevChannel()` (literal
`0x0322D8A0`). Its failure literals name the terminal outcome:
`EXIT>> shutdown dnf.exe because it cannot connect to channel servers. reason : port == 0`
and `... reason : didn't get tcls info`. That is the path taken when
`MinimumUserSelectChannel()` returns false.

---

## 4. Object layout — `CNSelectChannelModule`

Instance is a Meyers singleton at **`0x03A3DE40`**, guard byte **`0x03A3DEB4`**,
accessor `0x01329910` (ctor `0x013296F0`, `atexit` via `0x02C621BD`).

| offset | type | proof |
| --- | --- | --- |
| `+0x00` | vptr → `0x03286C2C` | `0x01329744 mov [esi],0x3286C2C`; RTTI offset 0 |
| `+0x04..+0x4F` | base subobject, ctor `0x01554290` (vptr `0x032BCACC`) | `0x0132971D call 0x1554290` before any field write |
| `+0x50` | vptr → `0x03286C1C` (second base) | `0x0132974A mov [edi],0x3286C1C`, `edi = esi+0x50`; `rtti-full-a21.csv`: `0x03286C1C, offset 80` |
| `+0x54` | `int tryCountConnect` | ctor calls empty ctor `0x02C426E0` (`mov eax,ecx; ret`) on `esi+0x54`; `0x01329983 cmp [esi+0x54],6`, `0x013299F1 inc [esi+0x54]`, `0x013299C8 mov [esi+0x54],0`. Name from literal `0x032874F8` `tryCountConnect++` |
| `+0x58` | timer object (vptr `0x0304E790`) | ctor `0x027C3A90` @ `0x01329750`; dtor `0x027C33F0` @ `0x013298D0`; stopped by `0x027C3410` at `0x01328250/0x01328290` |
| timer `+0x18` | `int intervalMs` | `0x027C3CA6 mov [esi+0x18],ecx` (=3000); read at `0x027C3789` and compared `elapsed >= interval` |

File-scope statics (config-key handles, written by the static init blob `0x02DF3903`,
read only by `0x013282D0`):
`0x03A3DEB8, 0x03A3DEBC, 0x03A3DEC8, 0x03A3DED4, 0x03A3DEDC, 0x03A3DEE0,
0x03A3DEE4, 0x03A3DEE8, 0x03A3DEEC, 0x03A3DEF0, 0x03A3DEF4`.

### `ChannelInfo` vftable slots used by this unit

Every one is evidenced by a `mov eax,[vt+N]; mov ecx,info; call eax` in `0x01328710`.

| slot | shape | use here |
| --- | --- | --- |
| `+0x00` | `bool` | pool discriminator: true → "normal" pool, false → "Integrate" pool (`0x01328965`) |
| `+0x04` | `bool` | **isFull** — literal `iinfo->isFull()` on the `cmp al,1` reject (`0x01328AAB`); also `return 4` in the grade fn |
| `+0x10` | `bool` | must be false for the "previous channel" candidate |
| `+0x1C` | `bool` | must be false for the "previous channel" candidate |
| `+0x2C` | `bool` | must be false to enter the "normal" pool |
| `+0x30` | `int` | region/type enum; tested against `{0x16,0x17}` and `{0x17,0x19}` |
| `+0x34` | ? | fed to `0x0042B800` to build a wstring inside `isSpecialRegionIntegrated` |
| `+0x48` | `int` | **channel number** — the `%d` in every `CH. %d` log |
| `+0x74` | `u32` | **IPv4** passed to `connectChannel` |
| `+0x78` | `u16` | **port** (`movzx eax,ax`) passed to `connectChannel` |
| `+0x7C` | `float` | **load ratio** used by the grade function |
| `+0x80` | `int` | third `%d` of the latelyGameServerInfo log |
| `+0x88` | `std::wstring*` (out-param) | address string, compared with `L"127.0.0.1"` |
| `+0x98` | `int` | world/server id, compared with `rec.f0` |

### `CNSelectChannelModule` vftable `0x03286C2C`

| slot | target | note |
| --- | --- | --- |
| `+0x00` | `0x01329890` | scalar deleting dtor |
| `+0x04` | `0x01329BA0` | per-frame `Process`/`Run` (see §5) |
| `+0x08` | `0x026FAC00` | base |
| `+0x0C` | `0x015527E0` | base |
| `+0x10`,`+0x14` | `0x00E13B70` | `mov al,1; ret` — constant `true` |
| `+0x18`,`+0x1C`,`+0x20` | `0x02231720` | base no-op |
| `+0x24` | `0x013282D0` | **this unit** — config roll-forward on leave (see §5) |
| `+0x28` | `0x01328290` | **this unit** — stop timer, close windows `0x46` and `0x209`, `return true` |
| `+0x2C`… | shared base stubs (`0x0048E890`, `0x01763ED0`, …) | |

Second vftable `0x03286C1C` = `{ 0x01032870, 0x02231720 }` (a refcount-release helper and
a base no-op).

### The two candidate maps — recovered node layout

`std::map<int, std::vector<ChannelInfo*>>` node, from `0x01328D3B` / `0x0132944D`:

```
node+0x00  _Left        node+0x04  _Parent      node+0x08  _Right
node+0x0C  key (grade, int)
node+0x10  vector       { +0x10 _Myproxy, +0x14 _Myfirst, +0x18 _Mylast, +0x1C _Myend }
node+0x24  _Color       node+0x25  _Isnil
```
Iterators are `_SECURE_SCL` triples `{ _Myproxy@0, _Mynextiter@4, _Ptr@8 }`
(`0x0065F2A0` = `begin`, `0x0293B470` = `end` — the same code is folded for
`vector::end` and `map::end`).

`std::basic_string` in this build is `{ _Myproxy@0, _Bx@4 (16 bytes), _Mysize@0x14,
_Myres@0x18 }` — proven by `0x013288B5 cmp [ebp-0x18],8` / `0x013288A3 mov eax,[edi+0x14]`.

---

## 5. Public surface (called from outside the unit)

| VA | role | external callers |
| --- | --- | --- |
| `0x01329910` | `getInstance()` — Meyers singleton, returns `0x03A3DE40` | `0x010BF840` (`CNSelectChannelSocket::retryConnectFailprevChannel`, TU 35), `0x0163C870` (TU 65 `GameCoreSub.cpp`), `0x0253DDB0` (`CNRDRetryConnectChannelConfirmWindow::proc`, `SelectServerWindow.cpp`) |
| `0x01328710` | `MinimumUserSelectChannel()` → `bool` | `0x010BF840` (TU 35), and `AutoSelectChannel` internally |
| `0x01329B00` → `0x01329980` | `AutoSelectChannel()` thunk | `0x010A2060`, `0x0253DDB0` |
| `0x01328250` | stop timer; if window `0x271` is open, `UIMgr->0x023060F0(0x271)` then tail-jump `0x0253E030` (close the retry-confirm popup) | `0x01641DC0` (TU 65 `GameCoreSub.cpp`) |
| `0x01329BA0` | vftable `+0x04`, per-frame driver | via vtable |
| `0x013282D0` | vftable `+0x24` | via vtable |
| `0x01328290` | vftable `+0x28`, `ret 4` | via vtable |

### `0x01329BA0` — per-frame driver

```
this->CNRDObject 0x01656250()                    // TU 22
sock = 0x010BD0E0()                              // CNSelectChannelSocket singleton @0x03A38EE0
if (sock->hasAddress())  sock->0x010BED40()      // 0x010BCE20 = hasAddress
else                     this->0x01329B10()
if (!( [0x03BAB0B0]->vt[0x14](0x54, 2) )) {
    if (!0x01102C30()->0x011029C0()) return;
}
0x012D0930(0x03B84690, 0);
0x01102C30()->0x0163DB20(0);
0x01102C30()-> tail-jump 0x05238824              // target outside .text
```

### `0x01329B10`

```
0x00E0EA60()->0x00CEF930()                       // read [+0x88], result discarded
if (UIMgr->IsOpen(0x15A) || UIMgr->IsOpen(0x46)) {   // 0x023016F0
    if (0x0109EE90())  { 0x010A2060(win); 0x0109EE80(0); }   // g_bool @0x03A2E350
    return true;
}
if (0x00E13B70())  { this->AutoSelectChannel(); return true; }   // always true in this build
UIMgr->Open(0x46, 0, 0);  return true;
```

### `0x013282D0` — config roll-forward on leaving the screen

Runs only when `CNRDConfigFile 0x01385B40()` is true, `GameCoreSub 0x0163D0C0()` is false
and `CNSelectChannelSocket 0x010BCE60()` is true. It then performs a chain of
`Set(keyB, Get(keyA))` on `CNRDConfigFile` (`0x01385830` = get int, `0x01385300` = set int,
`0x01387F30` = set with literal `0x12`), shifting the last-used world/channel handles
`0x03A3DEB8/BC → 0x03A3DEF0/F4`, then `0x03A3DEE0/E4 → 0x03A3DEB8/BC`, etc. A second
block runs when `0x0163D0C0()` is true, `0x010BCE60()` is false and
`Get(0x03A3DEEC) >= 10`. This is the persistence side of the "previous channel"
(`latelyGameServerInfo`) that §2.1 reads back.

---

## 6. What a server implementer should take away

1. **`127.0.0.1` in a channel's address field disables that channel for auto-select.**
   Do not hand out loopback in the channel list even for local test rigs — the client will
   silently skip it and eventually hit the 6-attempt shutdown path.
2. **Load ratio drives ordering, not the channel number.** `vt[0x7C]` (float) is bucketed
   into 0–3 by three runtime thresholds, and `isFull` forces bucket 4. Within a bucket the
   pick is `rand() % size`. To make the client spread players you must vary the ratio; equal
   ratios ⇒ uniform random.
3. **A channel that reports `isFull` is still listed but is placed in bucket 4** and, in the
   "normal" pool, is rejected outright with the `iinfo->isFull()` trace.
4. **The "Integrate" pool only accepts type `0x16`** (`vt[0x30]() == 0x16`), and only for
   channels where `vt[0x00]()` is false. Everything else lands in the "normal" pool.
5. **Retry budget: 6 attempts, one every 3000 ms.** A "please retry?" popup (`0x271`) shows
   after the 2nd. After the 6th the client resets the selection and shows a fatal message
   (string-table id `0x12B7C` = 76668) in window `0x366`, and
   `CNSelectChannelSocket::retryConnectFailprevChannel` will log
   `EXIT>> shutdown dnf.exe because it cannot connect to channel servers`.
6. **Sticky reconnect.** The client persists `{worldId, channelNo}` and, on the next visit,
   tries that exact channel first — before any load-based ranking. A server that renumbers
   channels between sessions will burn one attempt per login.
7. The connect itself is `CNSelectChannelSocket::connectChannel(u32 ip, u16 port)` at
   `0x010BE9F0`; this unit only supplies `(vt[0x74](), vt[0x78]())`.

---

## 7. Unknowns / low confidence

* The three grade thresholds at `0x039AF484/488/48C` read `0.0` in the dump — runtime
  configured, values not recoverable from this image.
* Semantics of `ChannelInfo` slots `+0x00`, `+0x10`, `+0x1C`, `+0x2C`, `+0x80`, `+0x98`.
  The pool naming ("normal" for `vt[0x00]()==true`, "Integrate" for false) is read off the
  log literals in each loop, not off a symbol; I checked the map↔loop pairing twice
  (`0x01328CD0` uses `ebp-0x74`; `0x0132914C` uses `ebp-0x58`) but the naming still reads
  inverted from intuition — treat the *labels* as tentative, the *control flow* as solid.
* States 2 and 3 of `[0x03A5BE80]` are set outside this unit; only the 3→2 transition at
  `0x010BF8F9` was inspected.
* The class's own RTTI name is obfuscated; "CNSelectChannelModule" comes from its log
  literals, which is strong but is a string, not a symbol.
* `0x01329BA0`'s tail jump to `0x05238824` leaves `.text`; not followed.
