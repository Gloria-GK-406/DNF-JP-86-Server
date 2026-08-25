# Unit 001 — `DNFClient\RDAR\Appendage\CNBoosterAppendage.cpp`

DNF S4A21 client, image base 0x00400000. All VAs are absolute.

---

## 0. The briefing span is wrong (too narrow by ~18 KB)

`tu-index.csv` row 1 gives `0x012F86C0 – 0x012F87E0`, 3 functions. That is only the three
`__FILE__`-referencing range-check helpers. The real module is

**`0x012F81E0` – `~0x012FC8E0`** (~18 KB, ~40 functions).

Two independent proofs:

1. **Function-name literals** (`literals.csv`) inside the wider range name this class and
   nothing else:
   | func | literal |
   | --- | --- |
   | `0x012F83F0` block | `CNBoosterAppendage::onEnd` (`0x03284778`) |
   | `0x012F86C0` | `CNBoosterAppendage::isToDrawDefaultAniOver` (`0x032848C8`) |
   | `0x012F8750` | `CNBoosterAppendage::isToDrawBoostingAniOver` (`0x032849A8`) |
   | `0x012F87E0` | `CNBoosterAppendage::isToDrawReleaseAniOver` (`0x03284A88`) |
   | `0x012F9640` block | `CNBoosterAppendage::onStart` (`0x03284AE8`) |
   | `0x012F9C50` block | `CNBoosterAppendage::proc` (`0x03284B28`) |

   The immediate neighbours are other files: `0x012F5400` = `CNRDTournamentManager.cpp`,
   `0x012F7970/0x012F7DD0` = TowerOfDespair `.etc` parsers, `0x012FE960+` =
   `UI/TopSecretArea/` action loaders, `0x01302540+` = `CNStraightMoveAppendage`.

2. **The class vtable is at `0x03284B6C`** (immediately after this file's literal pool at
   `0x03284778–0x03284B48`). I scanned `0x02E00000–0x03C00000` for every dword pointing into
   `0x012F81E0–0x012FC900`: **exactly one vtable matches**, `0x03284B6C`. Its overrides are
   `0x012F8410, 0x012F8570, 0x012F8BD0, 0x012F9080, 0x012F96C0, 0x012F9D60, 0x012FB760,
   0x012FBA60, 0x012FC080` — bracketing the module.

`parsers.csv` correctly reports **0 parsers**: nothing here touches the inbound read
primitives at `0x023C52xx`. The unit is C→S / peer-replication only.

---

## 1. What this unit implements

`CNBoosterAppendage` is a `CNAppendage` subclass that converts its owner character into a
**lane-based "booster" ride**: the player holds one game key to boost, taps left/right to
change discrete lanes, burns and refills a boost gauge, and the appendage drives three
separate animation sets (default / boosting / release), takes over the camera and HUD, and
plays a looped sound. **Every accepted state change is broadcast to the other clients as a
3-byte message `msgType 0x45` through `CNRDUdpModule`**, and the same module applies inbound
state changes back into this class.

Driver from outside: `MovieDirector.cpp` (TU 102, `0x00FD84B0`) reads the current lane, sets a
target lane and forces states — i.e. the ride is scripted by a cinematic/movie director.

### Class identity
| fact | value | evidence |
| --- | --- | --- |
| vtable | `0x03284B6C` | data scan above |
| RTTI | `TD#df09` → `TD#29` → `TD#07` (`rtti-vtables.csv`); type-descriptor name strings at `0x0397EBA0` / `0x0395EAA0` are **encrypted in memory**, so the mangled name is not recoverable that way | |
| appendage type id | **`0x144` (324)** | `0x022F6B74 call 0x28BB380` (`getTypeId(){return [this+0x74];}`) then `0x022F6B79 cmp eax, 0x144`, followed by `__RTDynamicCast` to `TD#df09` |

### vtable overrides (slot → VA → role)
| slot | VA | role | evidence |
| --- | --- | --- | --- |
| +0x00 | `0x012FB760` | destructor | slot 0 |
| +0x04 | `0x012F9D60` | **`proc`** | uses literal `CNBoosterAppendage::proc` (`0x03284B28`) at `0x012F9E3D` |
| +0x14 | `0x012F9080` | **`draw`** | dispatches the 3 ani-set draws by state |
| +0x20 | `0x012F96C0` | **`onStart`** | literal `…::onStart` at `0x012F97E1` |
| +0x24 | `0x012F8410` | **`onEnd`** | literal `…::onEnd` at `0x012F84AF` |
| +0x60 | `0x012F8570` | teardown hook (`this->vf[0x88](0)` then camera restore `0xC2E620/0xC2E630`) |
| +0xFC | `0x012F8BD0` | **serialize(stream)** |
| +0x100 | `0x012FBA60` | **deserialize(stream)** |
| +0x104 | `0x012FC080` | refresh/rebind of the 3 `CNRDAnimation` vectors (`[this+0x250]…`) |

---

## 2. Server-relevant: the outbound state packet (`msgType 0x45`, 3 bytes)

`0x012F81E0` — called from `0x012F9603` (tail of `setState`) and `0x012FA314`:

```
012F81E3  ecx = *(Writer**)0x3A5C9B0
012F81EE  push 1 ; push 0x45 ; call [vtbl+0x08]      ; beginPacket(msgType=0x45, kind=1)
012F8202  push arg1          ; call [vtbl+0x20]      ; write_u8
012F8214  push arg2          ; call [vtbl+0x20]      ; write_u8
012F8225  push arg3          ; call [vtbl+0x20]      ; write_u8
012F8229  push 1 ; push 0    ; call 0x022E56F0       ; send
012F8234  ret 0xC
```

**Verified in the image, not assumed:**
* Writer vtable `0x035B9264` — I dumped it: `[+0x08] = 0x022E4BA0` (beginPacket),
  `[+0x20] = 0x022E5310`, `[+0x24] = 0x022E5330`, `[+0x28]/[+0x2C] = 0x022E5350`.
* `0x022E5310` disassembles to a **u8 writer**: `eax=[ecx+0x10001C]; [eax]=arg_lo8;
  inc [ecx+0x10001C]`. So the three writes are **three single bytes**, not dwords.
* `0x022E56F0(1, 0)` → `(*(Net**)0x3B55D60)->vf[0x44](1, -1, 0, 3)` — the
  **`CNRDUdpModule`** broadcast helper (target seat `-1`). Corroborated by
  `compensate/unit-004.md` §3 and `compensate/unit-023.md` §2, which independently identify
  `0x3A5C9B0` as the outbound writer, `beginPacket(u16 msgType, u8 kind)` laying a 7-byte
  header (`buf[0]=kind`, `buf[1..2]=msgType LE`), and `0x3B55D60` as `CNRDUdpModule`.

**Payload, in wire order** (from the call site at `0x012F95F1`):

| # | byte | value |
| --- | --- | --- |
| 1 | u8 | `owner->vf[0xB68](newState, lane)` — computed via the appendage-base helper `0x01AFA010`, which resolves `getOwner()`, checks `vf[0x34](0x111)`, casts with `0xFCE360`, and tail-jumps `vf[0xB68]`. Returns `-1` when the owner is not a character. |
| 2 | u8 | `newState` (the value just written to `[this+0x1DC]`) |
| 3 | u8 | `lane` = `[this+0x1E8]` (current lane) |

`0x01AFA010` does **not** clean its own stack args (error path `0x01AFA03E` is a bare `ret`),
which is what leaves 3 dwords on the stack for `0x012F81E0`'s `ret 0xC`.

### Inbound counterpart — `CNRDUdpModule.cpp` applies it
`0x022F6B60`–`0x022F6BC5` (inside TU 31, `DNFClient\RDAR\CNRDUdpModule.cpp`):

```
for i in 0..count:
    a = target->getAppendage(i)                  ; 0x01A01970
    if (!a) continue
    if (a->getTypeId() != 0x144) continue        ; 0x28BB380 -> [a+0x74]
    b = dynamic_cast<CNBoosterAppendage*>(a)     ; 0x2C6386A, dstType 0x0397EBA0
    if (!b) continue
    if (b->vf[0x8C]() != 1) continue
    b->setState( (u8)[ebp-0x1ADC] )              ; 0x012F9200
```

So the state byte is peer-replicated: **a server that relays or validates UDP object sync
must understand `0x45` = booster state change (actor-resolved, state, lane).**

For reference, the client's own inbound tables (`r20-packet-idmap.csv`) list
`CMD 0x0045 → 0x0110E410 (CMD_Etc)` and `NOTI 0x0045 → 0x01189B60 (NOTI_Town)`. I did **not**
establish which of those two channels `kind = 1` selects — treat that mapping as unknown.

---

## 3. State machine (what a server/peer must mirror)

`setState` = **`0x012F9200`**, `__thiscall void(int newState)`, `ret 4`.

Preconditions (all must hold or it silently calls `this->vf[0x88](0)` and returns):
* `newState <= 7` (`0x012F9206 cmp [ebp+8],7 ; ja`)
* owner exists, `owner->vf[0x34](0x111)` true, cast `0xFCE360` non-null
* `owner->vf[0x54]()` (the **`CNRDMovementUnit`**, per `tu-index` TU 21
  `CNRDMovementUnit.cpp` owning `0x01921710`) non-null

Then unconditionally: `[this+0x1E0] = [this+0x1DC]` (prev), `[this+0x1DC] = newState`, and a
7-entry jump table at **`0x012F961C`** (dumped from the image):

| state | handler | entry action |
| --- | --- | --- |
| 0 | `0x012F92A8` | z += (prev==1 ? −`[0x164]` : `[0x16C]`); `owner->vf[0x564](0, [0x154])`; `lane = targetLane` |
| 1 | `0x012F9329` | `enterBoost()` = `0x012F9120`; `lane = targetLane` |
| 2 | `0x012F9352` | if prev==3 nothing; else `startLaneMove()` = `0x012F82D0` (aborts the whole call if it returns false) |
| 3 | `0x012F9384` | prev==1 → `startLaneMove()`; prev==2 → `enterBoost()`; otherwise nothing |
| 4 | `0x012F95B8` | no entry action (falls straight to the common tail) |
| 5 | `0x012F93B5` | **release/finish**: `owner->vf[0x4C0](0)`; restore camera; zero movement axes 0/1/2 (`0x19232D0`), zero accel (`0x1925040`); `owner->vf[0x564](2,0)`; `owner->vf[0x470](1,0,0,0,0,1,0)` (back to normal character state 1); start timer `[this+0x234]` at 1000 ms |
| 6 | `0x012F94F8` | gated on `[this+0x17D]==0`; `[this+0x1F8] = movement->0x1922890() − *(int*)0x399AEA4`; z += (prev∈{1,3} ? −`[0x164]` : `[0x16C]`); zero axes 0/1; `owner->vf[0x564](0,[0x154])` |
| 7 | — | `cmp eax,6; ja` skips the switch; still runs the common tail |

**Common tail `0x012F95B8`** (only if `0x017623A0(owner) == 1`, a `CNRDObject.cpp` liveness
gate used 1958× image-wide):
```
if (newState != 1 && newState != 3) {          ; i.e. not boosting
    gauge = (100 - [this+0x1F0]) * [this+0x160] / 100     ; 0x012F8690
    timer(this+0x1FC).set(gauge)                          ; 0x27C3C40
    [this+0x1F4] = [this+0x1F0]
}
sendStatePacket( owner->vf[0xB68](newState, [this+0x1E8]), newState, [this+0x1E8] )  ; §2
movement->0x1922CA0(1, 1)
```

### Transitions driven by `proc` (`0x012F9D60`)
Input key id **`0x4E`** through the input manager `*(void**)0x3BAB0B0`:
`vf[0x14](0x4E, 0)` = held, `vf[0x20](0x4E, 0)` = released. A per-key latch lives in the
`std::map<int,bool>` at `[this+0x28C]` (helper `0x012F9A20` reads it, `0x012F9640` is its
`operator[]`).

* `owner->vf[0x330]()==1` and state ∉ {5,6} → `setState(6)` (`0x012F9ED0`)
* gated on `[this+0x174]==1` and `owner->vf[0x330]()==0`:
  * key `0x4E` newly held, `[this+0x1E4]!=1`: state 0 → **1**, state 2 → **3**
  * otherwise, direction keys `0x013868F0(1,0)` / `0x013868F0(3,0)` → `0x0137EC80` → input
    `vf[0x14]`, producing delta `-1` / `+1`, fed to `nextLane()` `0x012F8280`
  * lane changed (`[0x1E8] == [0x1EC]` and new != current): state 0 → **2**, state 1 → **3**
  * key `0x4E` released (latch clear): state 1 → **0**, state 3 → **2**; both first call
    `0x01330FA0(0)` on the HUD object and `0x01648AA0(0x71C27EAF)` (stop the looped sound)

### Rendering by state — `draw` (`0x012F9080`, `ret 0x10`)
```
Base::draw(a,b,c,d)                 ; 0x1A00440
if (!this->vf[0x8C]()) return
s = [this+0x1DC]
if (s >= 0 && (s <= 3 || s == 7))  drawDefaultAnis (0x012F8990)
if (s == 1 || s == 3)              drawBoostingAnis(0x012F8A30)
else if (s == 5)                   drawReleaseAnis (0x012F8AD0)
```
So: **0/2 = grounded, 1/3 = boosting, 2/3 = lane in motion, 5 = releasing, 6 = ended,
4/7 = inert.** `draw`'s first argument is a *pass* flag (below), not a coordinate.

---

## 4. The three range-checked accessors (the briefing's anchors)

All three are byte-identical modulo offsets; `__thiscall bool(int index)`, `ret 4`:

```
012F86C0  esi = [this+0x1A4]                       ; _Myfirst
012F86CA  ecx = [this+0x1A8]                       ; _Mylast
012F86D2  n   = (ecx-esi) / 36                     ; magic 0x38E38E39, sar edx,3 → /0x24
012F86E6  if (index >= n) { LOG(...); return false; }
012F873D  if ((unsigned)index >= (unsigned)n) __report_rangecheckfailure()   ; 0x2C623E1
012F8744  return *(bool*)(esi + index*36 + 0x20)
```

| VA | vector base (`_Myfirst`/`_Mylast`) | log line | error text |
| --- | --- | --- | --- |
| `0x012F86C0` `isToDrawDefaultAniOver` | `0x1A4` / `0x1A8` | 1099 (`0x44B`) | `ERROR!! invalid index to access defaultAnis vector[%s(%d)]` |
| `0x012F8750` `isToDrawBoostingAniOver` | `0x1B8` / `0x1BC` | 1113 (`0x459`) | `…boostingAnis…` |
| `0x012F87E0` `isToDrawReleaseAniOver` | `0x1CC` / `0x1D0` | 1127 (`0x467`) | `…releaseAnis…` |

The log call is `0x027BE270(0, file, func, line, fmt, func, line)` with every string produced
by the literal decryptor `0x027BACC0` (in-degree 9843 — the image-wide decryptor).
The `jl` / `jb` pair means a **negative index falls into `__report_rangecheckfailure`**, not
into the log path.

Each accessor is called from exactly one place — the matching per-set draw loop:

```
0x012F8990  drawDefaultAnis (bool overPass, x, y, z)   ret 0x10   ; anis at [this+0x254]
0x012F8A30  drawBoostingAnis(...)                      ret 0x10   ; anis at [this+0x268]
0x012F8AD0  drawReleaseAnis (...)                      ret 0x10   ; anis at [this+0x27C]

for i in 0 .. (last-first)/8 :
    if (isToDraw…AniOver(i) != overPass) continue
    ani = ((CNRDAnimation**)first)[i]
    if (!ani) continue
    ani->Proc()                                          ; 0x026602A0 = CNRDAnimation::Proc
    ani->drawCurrentFrame(*(void**)0x3BAAE70, x, y, z, 1) ; 0x026617B0 = CNRDAnimation::drawCurrentFrame
```
(`0x012F8AD0` additionally calls `0x02652180(ani, ownerChar)` before drawing.)
So `isToDraw*AniOver(i)` = "this animation belongs to the **over** (post-character) draw pass",
and `draw` is invoked once per pass.

---

## 5. Object layout

Recovered from the **serializer `0x012F8BD0`** and its exact mirror the **deserializer
`0x012FBA60`** (identical field order and widths), cross-checked against real uses.
Stream primitives (plain little-endian byte stream, buffer `[s+0xC]`, cursor `[s+0x10]`):

| | read | write |
| --- | --- | --- |
| u8 | `0x0119D060` | `0x0119D250` |
| u32 | `0x0119D0A0` | `0x0119D290` |
| raw N | `0x0119D0F0` | `0x0119D2D0` |
| wstring | `0x0119D390` | `0x0119D340` |

These are **not** the inbound packet primitives (`0x023C52xx`); this is the general object
serialization stream (in-degree 341–975).

### Serialized field order

| offset | type | meaning / proof |
| --- | --- | --- |
| — | base | `0x019FDD20` (write) / `0x019FE500` (read) |
| `0x138` | 12 raw bytes = 3 × int | `onStart 0x012F984D–0x012F986C`: `owner->vf[0xA0]([0x138],[0x13C],[0x140])` → a spawn position (x,y,z). `0x012F8624` uses `[0x138]` as the lane-0 base coordinate. |
| `0x144` | u32 | serializer only |
| `0x148` | u32 | **lane spacing** — `0x012F8638` `pos = [0x138] + lane*[0x148]` |
| `0x14C` | u32 | **lane count** — `0x012F8297` clamps `abs(lane±1) > [0x14C]/2` |
| `0x150` | u32 | boost speed bonus — `0x012F915B` `[0x154]+[0x150]` |
| `0x154` | u32 | **base forward speed** — `owner->vf[0x564](0, [0x154])` at `0x012F9303`, `0x012F95A3` |
| `0x158` | u32 | **lane-move duration (ms)** — `0x012F864F`, `0x012F82EF` |
| `0x15C` | u32 | boost-gauge capacity — `0x012F83CD`, `0x012F91B3` |
| `0x160` | u32 | recovery-gauge capacity — `0x012F83D3`, `0x012F86A0` |
| `0x164` | float | z lift applied on boost enter (negated on exit) — `0x012F92CD`, `0x012F9131` |
| `0x168` | float | z clamp/limit — `0x012F8319 comiss xmm0,[esi+0x168]` |
| `0x16C` | float | z offset on idle/end — `0x012F92BE`, `0x012F9512` |
| `0x170` | float | serializer only |
| `0x174` | u8 | input enabled — `proc 0x012F9ED9 cmp …,1` |
| `0x175` | u8 | camera override enabled — `0x012F8879`, `onStart 0x012F9760` |
| `0x178` | u32 | serializer only |
| `0x17C` | u8 | teleport-on-start — `onStart 0x012F9844` → `owner->vf[0xA0](pos)` |
| `0x17D` | u8 | suppresses HUD hide (`onStart 0x012F97AB`) and state-6 entry (`0x012F94F8`) |
| `0x17E` | u8 | hard bypass — checked first in `onStart 0x012F9753` and `onEnd 0x012F847A` |
| `0x180` | `std::wstring` | **looped sound name** — played at `0x012F91A8` as `0x01648A20(&str, 0x71C27EAF, 0, 0, -1, -1)`; stopped by `0x01648AA0(0x71C27EAF)` |
| `0x1A0` | `vector<AniInfo>` | **defaultAnis** (count u32 + elements) |
| `0x1B4` | `vector<AniInfo>` | **boostingAnis** |
| `0x1C8` | `vector<AniInfo>` | **releaseAnis** |
| `0x1DC` | int, serialized as **u8** | **state** (read as dword at `0x012F9EC0`, `0x012F90AF`) |
| `0x1E0` | int, serialized as u8 | **prevState** |
| `0x1E4` | u8 | blocks boost start — `proc 0x012F9F55` |
| `0x1E8` | int | **currentLane** — public getter `0x012F8270` |
| `0x1EC` | int | **targetLane** — public setter `0x012F82C0` |
| `0x1F0` | int | gauge % (set to 100 in `onStart 0x012F98D9`) |
| `0x1F4` | int | previous gauge % (set to 100 in `onStart 0x012F98DF`) |
| `0x1F8` | int | movement snapshot captured on state 6 (`0x012F951A`) |
| `0x1FC` | timer, 0x1C bytes | **boost gauge timer** — `cur 0x27C3660`, `max 0x27C36F0`, `set 0x27C3C40`, `setMax 0x27C37C0`; both halves serialized |
| `0x218` | timer, 0x1C bytes | **lane-move timer** — started with `[0x158]` at `0x012F82F6` |
| `0x234` | timer, 0x1C bytes | started with 1000 on state 5 (`0x012F94E3`) |
| `0x2A0` | u8 | camera-applied latch, set at `0x012F897B`; **last serialized field** |

### Not serialized (runtime only)
| offset | type | proof |
| --- | --- | --- |
| `0x250` | `vector<shared_ptr<CNRDAnimation>>` defaultAnimations | `_Myfirst 0x254 / _Mylast 0x258`, `sar 3`, `[first + i*8]` is the raw `CNRDAnimation*` |
| `0x264` | same, boostingAnimations | `0x268 / 0x26C` |
| `0x278` | same, releaseAnimations | `0x27C / 0x280` |
| `0x28C` | `std::map<int,bool>` key latch | `lea esi,[ecx+0x28C]` in `0x012F9A28`, `0x012F9F36`, `0x012FA043` |

### Element type `AniInfo` — 36 bytes (0x24)
```
+0x00  std::wstring  aniName     ; _Myproxy +0, _Bx +4 (8 wchar), _Mysize +0x14, _Myres +0x18 (init 7)
+0x1C  (container allocator slot — never read or copied)
+0x20  bool          drawOver    ; returned by isToDraw*AniOver
```
Proof: copy helper `0x012F9AD0` allocates the 8-byte `_Container_proxy` (`0x00423460`), writes
`_Myres=7`, `_Mysize=0`, `*(u16*)(p+4)=0`, calls `basic_string::assign 0x00424180`, then copies
only `[+0x20]`; stride `add …,0x24`. Serializer `0x012F8D97–0x012F8DEE` writes exactly
`{wstring, u8}` per element.

### Container ABI in this build (consistent across both containers)
* `std::vector<T>` = **20 bytes**: `_Myproxy +0`, `_Myfirst +4`, `_Mylast +8`, `_Myend +0xC`,
  allocator `+0x10`. Confirmed by the uniform 0x14 stride `0x1A0 / 0x1B4 / 0x1C8` and
  `0x250 / 0x264 / 0x278`.
* `std::wstring` = **32 bytes**: `_Myproxy +0`, `_Bx +4` (8 wchar SSO), `_Mysize +0x14`,
  `_Myres +0x18` (SSO test is `_Myres < 8`, e.g. `0x012F8D0F`, `0x012F8D9A`), allocator `+0x1C`.

---

## 6. Public surface (called from outside the module)

From `callgraph.csv` restricted to `0x012F81E0–0x012FA620`, plus an exact `E8` call-site scan
of the image:

| VA | signature | external call sites |
| --- | --- | --- |
| `0x012F8270` | `int getCurrentLane()` → `[this+0x1E8]` | `0x00FD8568` (MovieDirector.cpp), `0x0120A16B` |
| `0x012F82C0` | `void setTargetLane(int)` → `[this+0x1EC]` | `0x00FD859E` (MovieDirector.cpp) |
| `0x012F9200` | `void setState(int)` — §3 | `0x00FD85C2` (MovieDirector.cpp), **`0x022F6BC5` (CNRDUdpModule.cpp)** |

Everything else reaches this class through the vtable at `0x03284B6C`.
`0x012F9A90 / 0x012F9AD0 / 0x012F9B90 / 0x012F9C50` are compiler-generated
`vector<AniInfo>` copy/grow/uninitialized-copy helpers; they are called from the class's own
setup functions (`0x012FA670`, `0x012FA750`, `0x012FB790`, `0x012FB900`) and are not API.

### Internal helpers worth naming
| VA | role |
| --- | --- |
| `0x012F81E0` | `sendStatePacket(u8,u8,u8)` — §2 |
| `0x012F8240` | `CNCharacter* getOwnerChar()` — `getOwner(0x019FFD20)` + `vf[0x34](0x111)` + cast `0xFCE360` |
| `0x012F8280` | `int nextLane(int delta)` — `abs(cur+delta) > [0x14C]/2 ? cur : cur+delta` |
| `0x012F82D0` | `bool startLaneMove()` — false (and `vf[0x88](0)`) when `cur == target`, else starts timer `[0x218]` with `[0x158]` |
| `0x012F8310` | clamps movement z against `[this+0x168]` |
| `0x012F8390` | `int gaugePercent(bool boosting)` = `min(timer[0x1FC].cur, .max) * 100 / (boosting ? [0x15C] : [0x160])` |
| `0x012F85E0` | time helper using `owner->vf[0xCC]()` + `[0x144] − [0x140]`, clamped ≥ 0 |
| `0x012F8620` | `bool lerpToTargetLane(...)` — `lerp([0x138]+cur*[0x148], [0x138]+tgt*[0x148], t)` (`0x27C4EC0`) → `movement->0x1922740(0, r)`; returns "arrived" |
| `0x012F8690` | `int recoveryTicks()` = `(100 − [0x1F0]) * [0x160] / 100` |
| `0x012F8870` | camera range takeover (`0xC2E270/0xC2E6A0/0xC2E2D0/0xC2E6C0` getters → `0xC2E670`), gated on `[0x175]`, latches `[0x2A0]=1` |
| `0x012F9120` | `enterBoost(owner, movement)` — z += `[0x164]`, speed = `[0x154]+[0x150]`, hide HUD, play looped sound, reset gauge timer |
| `0x012F9A20` | `bool keyLatch(int key)` over the map at `[0x28C]` |

### Cross-module objects this class relies on
| VA / global | identity | proof |
| --- | --- | --- |
| `0x019FFD20` | `CNAppendage::getOwner()` — returns `[this+4]` only if it is present in the guarded-pointer registry at `0x039A7614`; else 0 | in-degree 945 |
| `0x017623A0` | `CNRDObject` liveness/validity gate (`== 1`) | `tu-index` TU 22 `CNRDObject.cpp`; in-degree 1958 |
| `owner->vf[0x54]()` | **`CNRDMovementUnit`** | its methods `0x01921710` (position vector), `0x019232D0` (set float axis), `0x01922740` (set int axis), `0x01925040` (zero accel axis), `0x01923180`, `0x01922890`, `0x01922CA0` sit in `tu-index` TU 21 `CNRDMovementUnit.cpp` |
| `0x026602A0` / `0x026617B0` | `CNRDAnimation::Proc` / `CNRDAnimation::drawCurrentFrame` | own function-name literals |
| `0x03BAAE70` | render device passed as `drawCurrentFrame` arg 1 | |
| `0x03A5C9B0` | outbound packet writer (vtable `0x035B9264`) | §2 |
| `0x03B55D60` | `CNRDUdpModule` | §2 |
| `0x03BAB0B0` | input manager (`vf[0x14]` held, `vf[0x20]` released) | `0x012F9F17`, `0x012FA032` |
| `0x03A5C9B8` → `[+0x48]` (`0x02300930`) | HUD/UI object hidden on boost (`0x01335900(1)`, `0x01330FB0(0)`, `0x01330FA0(0)`) and restored in `onEnd` | |

---

## 7. Honest unknowns

* **Which game feature this is.** The file is named `CNBoosterAppendage`, the driver is
  `MovieDirector.cpp`, and the assets are lane-based with a boost gauge — but no literal in
  this unit names the content (no dungeon/quest string). Do not guess.
* **`kind = 1`.** The header byte selects a channel; I did not establish the mapping from
  `kind` to the `CMD` / `NOTI` families in `r20-packet-idmap.csv`.
* **`0x71C27EAF`** — the constant passed to `0x01648A20` (play) and `0x01648AA0` (stop). It is
  a fixed sound/channel id; I did not resolve its string.
* **Key `0x4E`** is the *game* key id fed to `0x03BAB0B0->vf[0x14]`, not a Win32 VK. Not
  resolved to a key name.
* **`[0x144]`, `[0x170]`, `[0x178]`** appear only in serialize/deserialize; no read site found.
* **RTTI names** — `TD#df09` / `TD#29` type-descriptor name strings are encrypted in the dump,
  so the mangled class name could not be read directly. The identification rests on the
  function-name literals plus the sole-owning vtable.
* The upper module bound `~0x012FC8E0` is inferred (last vtable target `0x012FC080` lies inside
  the merged function span `0x012FBD90 + 0x0B50`); functions from `0x012FC910` onward have no
  evidence tying them here.
