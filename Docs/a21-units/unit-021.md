# TU 21 — `DNFClient\RDAR\CNRDMovementUnit.cpp`  (0x01921000 – 0x01927CF0)

Image base 0x00400000, A21 memory dump. 94 functions. Confidence: **high** for the
serializer / integrator / object layout; **medium** for the track class identity and
for the packet-writer vtable slot meanings.

---

## 0. One-paragraph summary

This TU is the client's **kinematic core**: the per-object movement component
(`CNRDMovementUnit`, one per actor, stored at `actor+0xA8`, fetched by
`actor->vtbl[0x54]()`), the small packed value class that holds its state
(position / velocity / acceleration / accel-duration / direction), the **frame
integrator** that advances that state and resolves wall collisions, the
**auto-move / chase-target steering** state machine, and — most importantly for a
server — the **delta encoder that produces the C→S movement payload** and the two
functions that emit it. A third class lives in the same .cpp: a waypoint
**movement track** (quadratic-Bézier smoothed path) that can override the unit's
coordinates.

The span is **correct** — the literals (`CNRDMovementUnit::setMoveParent`,
`CNRDMovementUnit::proc`, `0 != bufferPtr`) sit at the two ends and the middle,
and the neighbouring TU (idx 23, `CNRDObjectManager.cpp`, 0x0191B990–0x0191BF20)
does not overlap. The TU simply contains **three classes**, not one — see §5.

---

## 1. THE WIRE FORMAT — delta encoder `0x01921000`  ← highest value for a server

```
uint32 __thiscall MovementData::serializeDelta(
        this      = ecx  : MovementData*        // current state
        [ebp+08]        : MovementData* ref     // last-sent snapshot
        [ebp+0C]        : void*  bufferPtr      // out, 4-byte cells, advanced
        [ebp+10]        : int*   outByteCount   // out, zeroed then += 4 per cell
        [ebp+14]        : bool   bAbsolute      // suppress "same as ref" coding
)  -> uint32 fieldMask                          // ret 0x10
```
Assert at 0x0192101B: `"0 != bufferPtr"`, `CNRDMovementUnit.cpp` line **0x48 (72)**.

### 1.1 Returned 32-bit mask (bit positions verified from the shifts)

| bits | field | shift proof |
|---|---|---|
| 0–3 | always 0 | never written |
| 4 | `bAbsolute` | `0x01921045` `neg/sbb/and 0x10` |
| 5–7 | `direction & 7` | `0x01921299` `movsx [ebx+0x34]; and 7; shl 5` |
| 8–9 / 10–11 / 12–13 | accelTime[0] / [1] / [2] code | `ecx` built at 0x01921206–0x01921294, `add ecx,ecx` ×3 then `shl eax,5` |
| 14–15 / 16–17 / 18–19 | accel[0] / [1] / [2] code | `0x01921200` `shl eax,0xE` |
| 20–21 / 22–23 / 24–25 | velocity x / y / z code | `0x0192116D` `shl eax,0x14` |
| 26–27 / 28–29 / 30–31 | position x / y / z code | `0x019210DA` `shl eax,0x1A` |

### 1.2 Per-field 2-bit codes

Float groups (position, velocity):
* `0` — a 32-bit **raw float** follows in the buffer
* `1` — value quantises to zero: `(int)(v * 10.0f) == 0`, i.e. `|v| < 0.1`
* `2` — equal to the reference at 0.1 granularity: `(int)(v*10) == (int)(ref*10)`;
  only emitted when `bAbsolute == false`
(scale constant `0x0343A3C8 = 10.0f`; `0x02C63D30` is the float→int truncation)

Int group `accel[3]` (offsets +0x1C/+0x20/+0x24):
* `0` — a 32-bit **int** follows, `1` — value is 0, `2` — equal to `ref` (only if `!bAbsolute`).
  No `3` case for this group.

Int group `accelTime[3]` (offsets +0x28/+0x2C/+0x30):
* `0` — int follows, `1` — value is 0, `2` — equal to `ref` (only if `!bAbsolute`),
  `3` — value is **−1** (`cmp eax,-1` at 0x01921228 / 0x01921259 / 0x0192128A).

### 1.3 Buffer order (program order of the stores)

```
pos.x, pos.y, pos.z,  vel.x, vel.y, vel.z,  accel.x, accel.y, accel.z,
accelTime.x, accelTime.y, accelTime.z
```
each present **only when its code is 0**. `*outByteCount` = 4 × (number present),
max 48 bytes. Floats are stored raw (unscaled) — the ×10 is only the comparison.

### 1.4 Emitters

**`0x01922CA0` — the main C→S movement report** (73 call sites all over the client;
this is the "I moved" packet).

Gate chain, all must pass, else it silently returns:
`this+0x78` (owner) non-null → `0x0119B940`/`0x0119B470` non-zero →
`0x0119B940`/`0x0119B540` non-zero → `0x017623A0(owner)` ("owner is my character
or is controlled by me", compares against `g_myChar` at `0x03B3DFB8`) →
`0x019DB8D0(owner)` (owner's session id == my character's) →
`(arg1 || arg0)` → `this[0] == 0` (send-suppress byte).

Anti-tamper in the middle: `0x02C4D2B0` + return-address check against the code
range `[0x03687BA8]=0x00401000 .. [0x03687BAC]=0x03029000`, `0x02C4D220(2,0,retaddr)`.

Body, on the global writer `g_writer = [0x03A5C9B0]` (683 functions across the
image use this global — it is *the* packet writer):

```
w->vf[0x08](4, 1)                         // select message. 4 = message id (see caveat)
w->vf[0x20](1)
w->vf[0x24]( owner->vf[0x48]() )          // owner object id, word 1
w->vf[0x24]( owner->vf[0x4C]() )          // owner object id, word 2
w->vf[0x24]( g_myChar->vf[0x48]() )       // g_myChar = [0x03B3DFB8]
w->vf[0x24]( g_myChar->vf[0x4C]() )
w->vf[0x20]( 0x019FC7F0(0x0163E3D0(0x01102C30())) )
w->vf[0x20]( 0x028D6D20(0x0163E3D0(0x01102C30())) )
[0x03AA2854]->0x01928270(this)
mask = serializeDelta(cur, prev, buf, &n, bAbsolute=arg0)
w->vf[0x2C](mask)                         // the 32-bit field mask
w->vf[0x34](&buf, n)                      // the packed payload
0x022E56F0(arg1, owner)                   // -> [0x03B55D60]->vf[0x44](arg1, -1, owner, 3)
copyData(prev <- cur)                     // 0x019212C0, snapshot for the next delta
this+0xC8 = 0x027C3660(this+0xA4)         // stamp last-send tick
guarded_store(this+0xD8, 0)               // clear dirty
```
*Caveat*: `vf[0x08]` takes two args here (`4`,`1`) and (`0x49`,`1`) at 0x019215E0.
The first argument is almost certainly the message id; I did not resolve the
writer class to prove it.

**`0x01925A80`** — same encoding, different header: writes *owner* id **and**
*my character* id, then the same `vf[0x2C](mask)` + `vf[0x34](buf,n)` + snapshot +
clear-dirty. Reached only from `0x01927F50` (outside the TU), which is a throttled
periodic tick (`GetTickCount` via `[0x03029608]`, min interval `[0x0399A7A8]`),
gated on `0x01924EC0` = *"my dirty flag is set and the owner belongs to my
session"*. Reading: the periodic resend for an object I control but that is not my
own character (pet / summon / controlled monster).

**`0x019215E0`** — message **`0x49`**, `w->vf[0x08](0x49,1)`, then owner id word 1,
word 2, and three 16-bit fields from `this+0x88`, `this+0x8C`, `this+0x90`, then
`0x022E56F0(1, owner)`. Its `this` is the **track** object (`this[0]` is the
`CNRDMovementUnit*`, whose `+0x78` is the actor) — a "path blocked / path event"
report. Only caller: `0x019260F0` (the track's proc).

### 1.5 Dirty-flag protocol the server should mirror

`this+0xD8` is a **guarded (obfuscated) int** — set/get through the
alignment-dispatched helper family
(`set`: `0x027BBA60 / 0x027BBBE0 / 0x027BBCA0 / 0x027BBD50 / 0x027BBE10`,
`get`: `0x027BC390 / 0x027BC0F0 / 0x027BC160 / 0x027BC1D0 / 0x027BC240`,
selected by `addr & 0xF`, `<= 0xC` → jump table). It is set to **1** by
`setVelocity` (0x019232D0), `setAcceleration` (0x01925040), `setAccelDuration`
(0x01923410) and `setDirection` (0x01923530), and cleared to **0** by both
emitters. Only state changes flip it — a redundant set with an unchanged value
does not.

---

## 2. THE FORMULAS (what a server must reproduce)

Units, established from the constants: **position in world units, velocity in
units/second (float), acceleration in units/second² (int32), all times in
milliseconds (int32)**. Axis 0 = X, 1 = Y, 2 = Z (height; ground is Z == 0, proven
by `0x01921910` requiring `round(getPos(2)) == 0`).

### 2.1 Per-axis integration — `0x01924850(int axis, int dtMs) -> int`

Called from `proc` for axis 0,1,2; result stored to `this+0xCC+4*axis`.

```
// (axis == 2 only) landing:
if (axis==2 && 0x01921910(this) && accel.z < 0 && vel.z < 0 && owner->vf[0x34](1))
        { owner->vf[0x444](2, vel.z, 0); return 4; }        // "landed"

v = vel[axis];  a = accel[axis];  t = accelTime[axis];
if (v == 0 && a == 0) return 0;                              // nothing to do

if (a == 0) {
        vAvg = v;                                            // constant velocity
} else if (t != -1 && t < dtMs) {                            // accel ends inside this frame
        setVel(axis, v + a * t * 0.001f);
        rem  = dtMs - t;
        vAvg = (vel[axis] + v) * 0.5f;                       // mean over accelerated part
        vAvg = (vel[axis] * rem + t * vAvg) / dtMs;          // time-weighted over the frame
        accelTime[axis] = 0;  accel[axis] = 0;
        if (t > 0 && owner && owner->vf[0x34](1)) owner->vf[0x448](axis);   // "accel ended"
} else {                                                     // accel covers whole frame
        setVel(axis, v + a * dtMs * 0.001f);
        vAvg = (vel[axis] + v) * 0.5f;
        if (t != -1) {
                accelTime[axis] -= dtMs;
                if (accelTime[axis] <= 0) {
                        accel[axis] = 0;
                        if (owner && owner->vf[0x34](1)==1) owner->vf[0x448](axis);
                }
        }
}

delta = dtMs * vAvg * 0.001f;
delta = clamp(delta, -2000.0f, +2000.0f);                    // 0x0318FCC4 / 0x0305B4E4
// collision probe + 1-unit stepping when round(pos+delta) != round(pos) ...
setPos(axis, pos[axis] + delta);
```
Constants: `0x03045E20 = 0.001f` (ms→s), `0x030439E8 = 0.5f`,
`0x03045AC0 = 1.19209e-07` (`FLT_EPSILON`, used as a relative "unchanged" test),
`0x0305B4E4 = 2000.0f`, `0x0318FCC4 = -2000.0f`.

Return codes seen: `0` (no movement), `4` (landed). Others exist in the tail I did
not fully enumerate.

### 2.2 Jump — `0x01924FA0(int height)` (47 callers) and free fn `0x01921360(a,h)`

```
v = sign(height) * sqrt( | 2 * -(accel.z * height) | );   setVelocity(2, v);
```
i.e. the textbook `v = sqrt(2·g·h)` with `g = -accel.z`. Sign flip via the SSE sign
mask at `0x03046500`.

### 2.3 Vertical velocity from a duration — `0x01924FF0(int ms)` (14 callers)

```
if (accel.z < 0 && ms >= 0)  setVelocity(2, -(accel.z * ms) * 0.001f);
```
`v = -g · t` — "reach apex in `ms` milliseconds".

### 2.4 Deceleration time — `0x019224A0(int axis)` (24 callers)

```
a = accel[axis];  v = vel[axis];
if (a * v < 0.0f)                                    // accel opposes velocity
        accelTime[axis] = (int)( v * -1000.0f / a );  // ms until v reaches 0
```
`0x0308455C = -1000.0f`, `0x03045E9C = 0.0f`.

### 2.5 Heading → velocity — `0x019246D0(float angleDeg, int dtMs)`

```
this+0x114 = angleDeg;   d = (int)angleDeg;
setVelocity(0,  moveSpeed * cosTable(d) );                          // 0x027F9310
setVelocity(1,  moveSpeed * sinTable(d) * 29 * 0.01f );             // 0x027F92D0
moveSpeed += moveAccel * dtMs * 0.001f;   if (moveSpeed < 0) moveSpeed = 0;
```
`moveSpeed = this+0xF0`, `moveAccel = this+0xF4`.
**The Y axis is compressed by `[0x03991304] × 0.01f = 29 × 0.01 = 0.29`** —
that is DNF's isometric X:Y ratio, and it is a *global* (`0x03991304`), so a server
mirroring client movement must use the same ratio. `0x0304A878 = 0.01f`.

Optional tail: if `this+0x104`, plays something at `((d+180) % 360) * (1/180) * 3.14`
radians (`0x031FB0B0 = 0.00555556f`, `0x030F4798 = 3.14`).

### 2.6 Turn-rate limiter (inside `proc`, 0x01927689–0x0192786E)

Angles are integers in degrees, always reduced by `(x + 360) % 360` (`0x168 = 360`).
`this+0x114` is the current facing (initialised to `-1.0f`, recomputed from the
velocity vector when negative). Target angle comes from `0x01762630` (an atan2-like
helper returning degrees) on `(targetPos - ownerPos)`. The turn is limited to
`this+0x11C` degrees (default **180.0**, `0x03088DB8`) — shortest-arc selection uses
the `±180 (0xB4)` half-turn test, wrap constant `0x0307F938 = 360.0f`. Result is fed
to `0x019246D0`.

### 2.7 Quadratic Bézier on integer points — `0x019214D0` (free function)

```
Vector3i eval(Vector3i P0, Vector3i P1, Vector3i P2, double t)   // ret 0x30
    if (t >= 1.0) return P2;
    u = 1.0 - t;
    out = (int)( P0*u*u + P1*(2*u*t) + P2*t*t );   // per component, truncated
```
`0x036DEC80 = 1.0`, `0x032ED1F0 = 2.0`. Used by the track class (`0x019260F0`,
`0x01926790`) to smooth waypoint paths.

---

## 3. OBJECT LAYOUTS

### 3.1 `MovementData` — packed, **sizeof = 0x35 (53 bytes)**

Proof of the size: `CNRDMovementUnit` embeds two of them, at `+0x0C` (current,
`0x01921710`) and `+0x41` (last-sent, `0x01921720`); `0x41 - 0x0C = 0x35`, and
`4 + 12 + 12 + 12 + 12 + 1 = 0x35` exactly — the struct is byte-packed.

| off | type | field | proof |
|---|---|---|---|
| +0x00 | vptr | | `mov eax,[ecx]; call [eax+N]` throughout |
| +0x04/08/0C | float[3] | **position** x,y,z | getter `0x01921690` `fld [ecx+eax*4+4]`, setter `0x019216AB` `movss [ecx+eax*4+4],xmm0` |
| +0x10/14/18 | float[3] | **velocity** x,y,z (u/s) | getter `0x019216C0` `fld [ecx+eax*4+0x10]`, setter `0x019216D7` |
| +0x1C/20/24 | int32[3] | **acceleration** x,y,z (u/s²) | `0x01925040` `mov [holder+edi*4+0x1c], value`; `0x01924FA0` reads `+0x24` as gravity |
| +0x28/2C/30 | int32[3] | **accel/decel remaining time, ms**; `-1` = unlimited | `0x01923410` setter, `0x019224A0` `v*-1000/a`, `0x01924850` `-= dt` |
| +0x34 | int8 | **direction**, only 0 and 1 accepted | `0x01922780` and `0x01923530` both `if (d==0||d==1) [+0x34]=d` |

Its vtable (byte offsets): `+0x00 getPos(int)`, `+0x04 setPos(int,float)`,
`+0x08 getVel(int)`, `+0x0C setVel(int,float)`, `+0x10` (no-arg; called on the
local player in `proc`'s tail when the resend interval elapses — purpose unknown).

### 3.2 `CNRDMovementUnit`

Reached as `actor->vtbl[0x54]()` (378+ sites) and directly as `actor+0xA8`
(e.g. `0x00A38B7A`, `0x00FFC0F1`, `0x0177F977`, `0x017EFC1C`).
`+0x00` is a **byte**, not a vptr (`cmp byte ptr [esi],0` at `0x01922D54`) — the
class is non-virtual.

| off | type | field | proof |
|---|---|---|---|
| +0x00 | bool | suppress network send | `0x01922D54` |
| +0x04 | `MovementData*` | override for *current* data, 0 ⇒ use `+0x0C` | `0x01921710` |
| +0x08 | `MovementData*` | override for *last-sent* data, 0 ⇒ use `+0x41` | `0x01921720` |
| +0x0C | `MovementData` | current state (0x35 B) | everywhere |
| +0x41 | `MovementData` | last-sent snapshot (0x35 B) | `0x01921720`, `0x019212C0` copy after send |
| +0x78 | `Object*` | owner actor | `0x017623A0(owner)`, `owner->vf[0xC4/0xC8/0xCC]() = getX/getY/getZ` |
| +0x7C | `Track*` | movement track / path, nullable | `0x01922FC0` setter, `0x019216F0` getter |
| +0x80 | bool | track enabled | `0x01921700` setter |
| +0x84 | int | collision/track mode, compared against 0,1,2 | `0x01927885`, `0x01927996`, `0x01927A96` |
| +0x88 | bool | collision state valid | `0x01922FC0` clears, `0x01927A82` sets |
| +0x89..0x8B | bool[3] | **per-axis blocked flag** | `cmp byte [esi+edi+0x89],0`, edi = 0..2 (`0x01927AC0`) |
| +0x8C | int | id/handle of the blocking object | passed by ref to `owner->vf[0x118]` at `0x01927A1F` |
| +0x90 | bool | position was written this frame | `0x01922740`, `0x01927C57` |
| +0x94 | `Object*` | **move parent** (attach target) | `0x01922640` `setMoveParent` |
| +0x98/9C/A0 | int32[3] | offset from parent = self − parent, x,y,z | `0x01922640`; getter `0x01921750`, setter `0x01921730` |
| +0xA4 | guarded timer object | `0x027C3660` = elapsed ms, `0x027C38B0` set, `0x027C3CB0/0x027C3D30` pause/resume, `0x027C3900` read | `0x019213A0`, `0x019213F0`, `proc` |
| +0xC0 | bool | paused → `proc` returns immediately | `0x01921770` getter, `0x019213A0` setter |
| +0xC4 | int | tick of the previous `proc` (dt source) | `0x01926F17`–`0x01926F37` |
| +0xC8 | int | tick of the last periodic position resend | `proc` tail, `0x01922CA0` |
| +0xCC/D0/D4 | int32[3] | last integration result per axis | `proc` loop `0x01927192`, getter `0x01921780` |
| +0xD8 | guarded int | **dirty flag** (see §1.5) | `0x019232D0`, `0x01925040`, `0x01923410`, `0x01923530`, `0x01924EC0` |
| +0xE0 | int | resend interval ms; `-1` ⇒ global `[0x03985294] = 500` | `proc` tail, setter `0x019217A0` |
| +0xE4 | bool | auto-move / chase active | getter `0x019217D0`, cleared at `0x01927333` |
| +0xE8 | int | = 1 on reset | `0x019219F0` |
| +0xEC | int | — | setter `0x019217E0`, getter `0x01921850` |
| +0xF0 | float | **move speed** (scalar) | `0x019246D0`, setter pair `0x019217F0` |
| +0xF4 | float | **move acceleration** | `0x019246D0`, `0x019217F0` |
| +0xF8 | int | retarget interval ms (default **500** = 0x1F4) | `0x019219F0`, setter `0x01921820`, `proc 0x01927396` |
| +0xFC | int | next retarget tick | `proc` |
| +0x100 | `Object*` | **chase target** | setter `0x01921830`, getter `0x01921840` |
| +0x104 | bool | emit turn effect | setter `0x01921860`, `0x0192478E` |
| +0x108 | float | speed cap used by the chase branch | setter `0x01921870`, `proc 0x01927689` |
| +0x10C / +0x110 | float | last observed target X / Y | `proc 0x01927510 / 0x01927529` |
| +0x114 | float | **current facing angle, degrees**; `-1` = uninitialised | `0x019219F0` (init −1.0), `0x019246D0`, `proc` |
| +0x118 | float | — | setter `0x01921890` |
| +0x11C | float | **max turn rate, deg** (default 180.0) | `0x019219F0`, setter `0x019218B0`, `proc` |
| +0x120 | int | chase duration ms (≤0 = unlimited) | `proc 0x0192733F` |
| +0x124 | int | chase start tick | `proc 0x01927353` |
| +0x128 | int | z-approach enable | setter `0x019218D0`, `proc 0x0192754E` |
| +0x12C | int | X-offset mode: −1 none / 0 add / 1 subtract | setter `0x019218E0`, `proc 0x019273C4` |
| +0x130 | int | that offset distance | `proc` |
| +0x134 | bool | timed per-axis override active | `0x019219B0` |
| +0x138/13C/140 | int32[3] | override value per axis | `0x019219B0` |
| +0x144/148/14C | int32[3] | override expiry tick per axis | `0x019219B0` |
| +0x150 | bool | — | setter `0x019218F0` |
| +0x151 | bool | snap-to-target (1) vs steer-toward-target (0) | `proc 0x019271C3` |
| +0x154/158/15C | float[3] | computed unit vector toward the target | `proc 0x019272B4`, then fed to `setVelocity(0/1/2)` |
| +0x160 | int | if == 1, mirror the move-parent's direction | setter `0x01921900`, `proc 0x01926FC7` |
| +0x168 | bool | — | setter `0x01921420` |
| +0x169 | bool | run the `+0x16C` sub-proc | getter `0x01921440`, `proc 0x01927C5E` |
| +0x16C | ptr | that sub-object | getter `0x01921430` |
| +0x170 | bool | follow parent with one axis pinned | `proc 0x01927015` |
| +0x174 | int | pinned axis (0..2) | `proc` |
| +0x178 | int | pinned value | `proc` |

Owner (`+0x78`) vtable slots this unit depends on:
`0x34(int)->bool` state query · `0x48()/0x4C()` object id words ·
`0x54()` → this movement unit · `0xA0(x,y)` set-position ·
`0xC4()/0xC8()/0xCC()` getX/getY/getZ (int) · `0x114(int)->bool` ·
`0x118(x,y,int* out)->bool` collision test · `0x13C()` direction ·
`0x180()` sound/effect factory · `0x1F8(int,float)` · `0x444(axis,float,flag)`
stop-movement notify · `0x448(axis)` accel-ended notify.

### 3.3 The track / path class (3rd class in this TU)

Instances hang off `CNRDMovementUnit+0x7C` and are also reachable via
`actor->vtbl[0xC00]()`. Its `+0x00` is a back-pointer to the `CNRDMovementUnit`
(`0x019215E0` does `[[this]+0x78]->vf[0x48]()`). Members recovered:
`+0x3C` waypoint vector (`+0x40` begin, `+0x44` end), `+0x50` mode
(`0x01921450`), `+0x54` count, `+0x6C..+0x83` two int3 vectors — position and
range (`0x01921490`, 6 int args, `ret 0x18`), `+0x84` bool (`0x019214C0`),
`+0x88/+0x8C/+0x90` the words reported in message `0x49`, `+0xAC`, `+0xB4/+0xB5`
flags (`0x01921450` writes them, `0x019260F0` tests `+0xB4`).

Vtable slots the movement unit calls on it: `0x00(int)`, `0x08()` commit,
`0x10() -> int[3]` current coords, `0x28(axis,int)` set coord, `0x2C(axis) -> int`
get coord, `0x3C(bool)` pause, `0x48(int ms)` set time. Plus free fn
`0x022E1010(track) -> float` (an angle, forwarded to `owner->vf[0x1F8](1, angle)`).

**These three functions are driven from the behaviour script.** `0x02696AB0`
(`CNRDActionBehavior.cpp`) calls `0x01921450`, `0x01921490`, `0x019214C0` and
`0x019260D0` on the same receiver, and its literals are
`SET MOVABLE AREA`, `[X POS]/[Y POS]/[Z POS]`, `[X RANGE]/[Y RANGE]/[Z RANGE]`,
`[COORDINATE] ABSOLUTE/RELATIVE`, `[MOVE TO POS]`. `0x019260D0` is
`ecx += 0x3C; push_back(&Vector3i)` — **add waypoint** — which is what ties the
receiver to the track's `+0x3C` vector.

---

## 4. PUBLIC SURFACE (what the rest of the client calls)

Counts are direct `E8`-relative call sites found by scanning the image, so they
exceed the briefing's per-function caller counts.

| VA | signature | sites | what it does |
|---|---|---|---|
| `0x019232D0` | `setVelocity(int axis, float v)` | 506 | **local-only** (gated by `0x017623A0`); skips if `\|old−new\| <= \|old\|·FLT_EPSILON`; else sets and marks dirty |
| `0x01922640` | `setMoveParent(Object* p)` | 404 | stores `p` at `+0x94`, caches `self−parent` offsets into `+0x98/9C/A0`; if `p->vf[0xD4]()` (onDestroySkippable) logs the dangling-pointer warning literal |
| `0x01921710` | `getCurrentData()` | 378 | `[+4] ?: this+0x0C` |
| `0x019216F0` | `getTrack()` | 141+ | `[+0x7C]` |
| `0x01922CA0` | `sendMove(bool bAbsolute, bool bSendFlag)` | 73 | §1.4 |
| `0x01925040` | `setAcceleration(int axis, int a)` | 73 | local-only; sets `accel[axis]`, dirty, then `0x01923410(axis, -1)` |
| `0x01924FA0` | `setJumpHeight(int h)` | 47 | §2.2 |
| `0x01921780` | `getStepResult(int axis)` | 44 | `[+0xCC+4*axis]` |
| `0x01922FC0` | `setTrack(Track*)` | 46 | swaps the track, seeds it from the current position, resets `+0x88..+0x8F` |
| `0x019224A0` | `updateDecelTime(int axis)` | 24 | §2.4 |
| `0x01922740` | `setPositionInt(int axis, int v)` | 17 | `setPos(axis,(float)v)` + `+0x90 = 1` |
| `0x01922780` | `setDirectionRaw(int d)` | 16 | writes `+0x34` if `d ∈ {0,1}`, no dirty flag |
| `0x01924FF0` | `setVerticalTime(int ms)` | 14 | §2.3 |
| `0x01921420/30/40` | `+0x168` / `+0x16C` / `+0x169` accessors | 14/2/2 | |
| `0x01923530` | `setDirection(int d)` | — | local-only version of `setDirectionRaw`, always marks dirty |
| `0x01923410` | `setAccelDuration(int axis, int ms)` | — | local-only; fires `owner->vf[0x448](axis)` when an in-flight duration is cut short |
| `0x019213A0` | `setPause(bool)` | 7 | `+0xC0`, pauses the `+0xA4` timer and the track |
| `0x019213F0` | `setTime(int ms)` | — | seeds the `+0xA4` timer and the track |
| `0x01922890` | `getZ()` | 7 | from the track if enabled, else `round(getPos(2))` |
| `0x01926E50` | `proc()` | 3 | §2 / §4.1 — the frame tick |
| `0x019217B0` | `resetSnapshot()` | 2 | copies current data into the last-sent snapshot |
| `0x019212C0` | free `copyData(dst, src)` | 4 | copies pos, vel, accel, accelTime, direction |
| `0x01921360` | free `calcJumpSpeed(int accel, int height) -> int` | 1 | §2.2 as an integer helper |
| `0x019214D0` | free `bezier2(P0,P1,P2,double t) -> Vector3i` | 2 | §2.7 |
| `0x019219B0` | `setAxisOverride(int axis, int value, int durationMs)` | 1 | `+0x138/+0x144/+0x134` |
| `0x019219F0` | `resetChaseParams()` | 2 | zeroes `+0xE4..+0x15C`, `+0x114 = -1.0`, `+0xF8 = 500`, `+0x11C = 180.0`, `+0x12C = -1` |

Plain setters (all `this = movement unit`): `0x019217A0` `+0xE0`,
`0x019217E0` `+0xEC`, `0x019217F0` `+0xF0/+0xF4` (int args, converted to float),
`0x01921820` `+0xF8`, `0x01921830` `+0x100`, `0x01921860` `+0x104`,
`0x01921870` `+0x108`, `0x01921890` `+0x118`, `0x019218B0` `+0x11C`,
`0x019218D0` `+0x128`, `0x019218E0` `+0x12C`, `0x019218F0` `+0x150`,
`0x01921900` `+0x160`, `0x01921700` `+0x80`.

### 4.1 `proc()` — `0x01926E50` — order of operations

1. `INVALID POINTER]CNRDMovementUnit::proc()` assert at line **0x212 (530)** —
   `this` is checked against a live-pointer registry (`0x03A9041C`, `0x028003E0`).
2. `if (this+0xC0) return;` (paused)
3. `dt = now − this+0xC4; this+0xC4 = now` (`now = 0x027C3660(this+0xA4)`)
4. Validate `moveParent` (`+0x94`) against a second live-pointer registry
   (`0x039A7614`); null it if dead. **This is a real dangling-pointer guard, not
   an assert.**
5. If attached to a parent: optionally mirror its direction (`+0x160 == 1`),
   then set own position = parent position + `+0x98/9C/A0`, with the `+0x170`
   variant pinning one axis to `+0x178`.
6. `for axis in 0..2: this+0xCC+4*axis = integrate(axis, dt)`  (§2.1)
7. Chase / auto-move block (`+0xE4` && `+0x100`): either snap onto the target
   (`+0x151`) or steer toward it with the turn-rate limiter (§2.6) and
   `0x019246D0` (§2.5); honours the retarget interval `+0xF8`, the duration
   `+0x120`, and the X-offset mode `+0x12C/+0x130`.
8. Track/collision block: writes the integer position into the track, calls
   `owner->vf[0x118](x, y, &this+0x8C)`; on a block, `owner->vf[0x444](...)` and
   then a **1-unit-at-a-time step-back loop per axis** until the probe clears,
   recording `+0x89+axis`. Z is special-cased through `owner->vf[0x114]`.
9. `if (+0x169) subProc(+0x16C)`, then `0x01923940(this)`, then
   `[0x03AA2854]->0x01927F50()` (the throttled resend tick, §1.4).
10. **Periodic position resend for the local player only**: if `+0xE0 != 0`
    (`-1` ⇒ `[0x03985294] = 500` ms) and `now >= this+0xC8 + interval` and
    `g_myChar->getMovementUnit() == this`, call `data->vtbl[0x10]()` and stamp
    `+0xC8 = now`.

---

## 5. Span verdict / caveats

* The span is **right** and it is a real module boundary; the file simply holds
  **three** classes: `MovementData` (0x35 B, packed), `CNRDMovementUnit`, and the
  waypoint **track**. Attributing `0x01921450 / 0x01921490 / 0x019214C0 /
  0x019260D0 / 0x019260F0 / 0x01926790 / 0x019215E0` to `CNRDMovementUnit` would
  be wrong — their field offsets (`+0x50`, `+0x6C..+0x83`, `+0xB4`) collide with
  the unit's own `+0x41` snapshot / `+0x78` owner / `+0x7C` track. The
  `+0x3C` waypoint-vector match between `0x019260D0` (push_back) and `0x019260F0`
  (`(end−begin)` count) is what pins them to the track class.
* `TU-index` reports **0 parsers** in this unit and that is consistent: nothing
  here reads the inbound stream. The unit only *encodes*. The S→C decoder for
  this same mask format lives elsewhere and I did not locate it.
* I did **not** prove that `w->vf[0x08]`'s first argument is the message id, so
  "`4`" and "`0x49`" are the literal constants passed, not confirmed opcodes.
* Functions in the span I did not analyse: `0x01921AD0` (2512 B, the constructor —
  it calls `copyData`, so it likely initialises both embedded `MovementData`s),
  `0x01923940` (3472 B, run every `proc`), `0x019252C0`, `0x01925F00`,
  `0x01926000`, `0x01926C30`, `0x01926D70`.
* Two globals a server implementer should note: `[0x03991304] = 29` — the
  **X:Y isometric ratio numerator** (velocity Y is scaled by `29 × 0.01`), and
  `[0x03985294] = 500` — the default position-resend interval in ms.
