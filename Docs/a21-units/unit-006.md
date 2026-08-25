# TU 006 — `DNFClient\RDAR\CNHitGauge.cpp`

Briefing span `0x01329D20`–`0x01329EA0` (6 fn, 0 parsers). **Span is too narrow — see
"Span correction" below.** Confidence: high on everything marked CONFIRMED.

## What this unit implements

`CNHitGauge` is the **overhead bar drawn above a creature's head** — the floating
HP / hit-count / charge bar. It is a plain 60-byte (`0x3C`) client-side widget: it
holds a current value, a max value, an auto-hide timer, a draw offset, and a
gauge-type enum, and it converts `cur/max` into a **pixel fill width** that the
draw routine turns into a horizontal scissor rect over a foreground sprite.

It owns **no packet handling, no network state, no vtable** (the ctor at
`0x01329C50` never writes a vptr; the timer sub-object at `+0x1C` has one, the
gauge itself does not). `parsers = 0` is correct: nothing in
`0x01329C20`–`0x0132A390` calls any of the five read primitives.

The class name and file are self-reported:
- `0x032875C0` → `D:\Work\1229\Source\DNFClient\RDAR\CNHitGauge.cpp`
- `0x03287694` → `CNHitGauge::setTimeLimit`  (error at line `0xEB` = 235)
- `0x03287804` → `CNHitGauge::canLoadImages` (errors at lines `0xFE` = 254, `0x10D` = 269)
- `0x03287770` → `ERROR! cannot load ImagePack IMAGE_INTERFACE_OVERHEAD_GAUGE [%s(%d)]`
- `0x032876D0` → `ERROR! cannot get image(%d) from IMAGE_INTERFACE_OVERHEAD_GAUGE [%s(%d)]`
- `0x03287630` → `ERROR!! invalid time limit value %d [%s(%d)]`
- `0x03287840` → `Interface/Creature.img`  (used by the draw, `0x0132A0C0`)

## Object layout — `CNHitGauge`, size `0x3C` (CONFIRMED)

Size proof: `0x018A9E0F  push 0x3c ; call 0x423460 (operator new)` immediately
followed by `mov ecx,eax ; call 0x1329c50` (the ctor).

| off | type | meaning | access that proves it |
|---|---|---|---|
| `+0x00` | int | **computed fill width in px** (output of the lerp) | `0x01329E2A mov [esi],eax` after `call 0x27C4770`; consumed at `0x0132A29E mov esi,[ebx]` → becomes the scissor right edge |
| `+0x04` | int | current value | `0x01329E0B mov eax,[esi+4]` / `0x01329E16 dec`-store; read by getter `0x02846D50 mov eax,[ecx+4]; ret` |
| `+0x08` | int | max value | `0x01329E0E mov ecx,[esi+8]`, passed as the lerp denominator |
| `+0x0C` | int | **auto-hide time limit, ms** (0 = never hide) | ctor sets 0; `0x01329DD4 mov eax,[eax+0xc]; call setTimeLimit`; draw gates on it: `0x0132A0EC cmp [ebx+0xc],edi ; je (skip timer check)` |
| `+0x10` | int | X draw offset | `0x0132A24D mov edi,[ebx+0x10]` then `edi = edi - imgWidth/2 + argX` |
| `+0x14` | int | Y draw offset | `0x0132A298 mov eax,[ebx+0x14]; add eax,[ebp-0x2c]` (argY) |
| `+0x18` | int | **gauge type / mode** (0,1,2) | `0x01329DC2` copied from desc; switched on in draw `0x0132A13A..0x0132A149`; gates the setters |
| `+0x1C` | 0x1C bytes | timer sub-object (has a vptr `0x0304E790` at `+0x1C`) | ctor `0x01329C9C lea ecx,[esi+0x1c]; call 0x27C3A90`; that ctor writes `[esi+0]`, `+8`, `+0xC`, `+0x10`, `+0x14`, `+0x18` → occupies `0x1C..0x37` |
| `+0x38` | int | **full bar width in px** — 0x45 (69) for type 0/1, 0x40 (64) for type 2 | `0x01329DCD mov [ecx+0x38],0x45` / `0x01329DE1 mov [ecx+0x38],0x40`; used as arg1 of the lerp at `0x01329E1E`, `0x01329E54`, `0x01329E82` |

`+0x38` is **not** initialised by the ctor — only `initFromDesc` (`0x01329DA0`)
sets it. A `CNHitGauge` that is constructed and drawn without an init has garbage
in `+0x38`.

### The descriptor struct (0x1C bytes) — same first 7 fields

`0x01329C20` is its default ctor: `[0]=0x45, [4]=0, [8]=-1, [0xc]=0, [0x10]=0,
[0x14]=0, [0x18]=0` — identical to the gauge's own field init at `0x01329C80`,
but it does **not** touch `+0x1C`, so it is the standalone descriptor. It is
constructed on the stack by `CNRDActionBehavior` (`0x026A1111 lea ecx,[ebp-0x74];
call 0x1329c20`) and handed to `initFromDesc`.

## The value→pixel formula (CONFIRMED)

Shared helper `0x027C4770`, cdecl `f(int total, int cur, int max)`:

```
if (cur >= max) return total;          // 027C4783 jl / 027C4785 return total
if (cur <= 0)   return 0;              // 027C478F loads 0.0f from 0x03045E9C (verified == 0.0)
return (int)((float)cur * (float)total / (float)max);   // 027C479C..027C47AF, float math
```

Every gauge setter calls it as `f(this->m_fullWidth /*+0x38*/, cur, max)` and
stores the result in `+0x00`. Note it is **float** math truncated to int, and the
`cur >= max` short-circuit returns `total` exactly (not a rounded product).

## Public surface (each entry point, its ABI, and its one external caller)

| VA | signature | what it does | called from |
|---|---|---|---|
| `0x01329C20` | `__thiscall Desc::Desc()` | descriptor default-init (`w=0x45, cur=0, max=-1, time=0, x=0, y=0, type=0`) | `0x026A1117` in `CNRDActionBehavior.cpp` |
| `0x01329C50` | `__thiscall CNHitGauge::CNHitGauge()` | same 7 fields + timer ctor (`0x27C3A90`) + timer reset (`0x27C3410`) | `0x018A9E29` (`IRDCharacter`, after `new(0x3c)`) |
| `0x01329CC0` | `__thiscall ~CNHitGauge()` | timer reset (`0x27C3410`) + timer dtor (`0x27C33F0`) | `0x018C4960` |
| `0x01329D20` | `__thiscall setTimeLimit(int ms)`, `ret 4` | `ms < 0` → log `ERROR!! invalid time limit value %d` and **return without changing anything**; else timer.reset(); if `ms != 0` timer.start(ms, 0) | internal only (from `0x01329DA0`) |
| `0x01329DA0` | `__thiscall initFromDesc(const Desc*)`, `ret 4` | copies desc `[0..0x17]` (3×movq) + `[0x18]` (type), sets `+0x38 = (type==2 ? 0x40 : 0x45)`, then `setTimeLimit(desc->timeLimit)` | `0x018A9E49` (`IRDCharacter::setHitGauge`) |
| `0x01329E00` | `__thiscall onHit()`, no args | **type 1 only**: `if (cur > 0) --cur;` then recompute fill. Other types: no-op (`0x01329E2E mov [esi],ecx` rewrites the old value) | `0x018C39C0`, the damage-taken handler (`ATK_FIRE`/`ATK_WATER`/`STUCK_BLADE`… literals) |
| `0x01329E40` | `__thiscall setValue(int cur, int max)`, `ret 8` | **type 0 only**: recompute fill from the two args (does **not** store cur/max into `+4`/`+8`). Other types: no-op | `0x018B39EF`, reached from `IRDActiveObject::setHp` (`0x017DDD90`) |
| `0x01329E70` | `__thiscall addValue(int delta)`, `ret 4` | `cur += delta` then recompute fill from `+4`/`+8`. **No type gate** | `0x02687DAC` in `CNRDActionBehavior` |
| `0x01329EA0` | `__thiscall canLoadImages(shared_ptr<ImagePack>)`, `ret 8` — `this` (ecx) is loaded over immediately at `0x01329EC8` and unused | `pack == null` → log line 254, return false; then `pack->getImage(0)` and `pack->getImage(1)`; either null → log `cannot get image(%d)` line 269 with the failing index; both ok → true. Releases the by-value shared_ptr on every path | `0x0132A1FB` (the draw) |
| `0x0132A0C0` | `__thiscall draw(int x, int y)`, `ret 8` | see below | `0x018A9898` (`IRDCharacter` draw) |

## `draw` (`0x0132A0C0`) — the part a UI/render reimplementation needs

1. **Visibility gate**: `if (timeLimit != 0) { if (!timer.isRunning(0x27C3500)) return; if (timer.isExpired(0x27C3780)) return; }` — `0x0132A0EC`–`0x0132A10B`. `timeLimit == 0` ⇒ always visible.
2. **Image pack + frame selection**, on `+0x18`:
   - type `0` or `1` (`0x0132A1AA`): pack = `ImagePackManager(0x03A5AD40)->getPack(57)`; bg frame **0**, fg frame **1**; fill-x adjust `+1`; fg draw offset `(0, 0)`.
   - type `2` (`0x0132A14F`): pack loaded by name `"Interface/Creature.img"`; bg frame **0x23 (35)**, fg frame **0x39 (57)**; fill-x adjust `0`; fg draw offset `(+1, +1)`.
   - type `< 0` or `> 2`: falls through with an **empty pack** → `canLoadImages` fails → draws nothing.
   - Pack index **57 = `Sprite/interface2/OverHeadGauge.img`** — CONFIRMED by counting the sequential `registerPack` calls (`0x02802E10`) in the pack-registration function `0x01640940`: the call at `0x01640F3E` that registers `Sprite/interface2/OverHeadGauge.img` is the 58th, i.e. index 57. That matches the `IMAGE_INTERFACE_OVERHEAD_GAUGE` name in this unit's own error strings.
3. `canLoadImages(pack)` — bails silently (returns false, draws nothing) if the pack or frames 0/1 are missing. **Quirk:** for type 2 it still validates frames **0 and 1**, not the 35/57 it actually draws.
4. **Placement**: `x = argX + this->m_xOff(+0x10) - bgImage->getWidth()/2` (width = `WORD [img+0x1c]`, getter `0x027C6F30`); `y = argY + this->m_yOff(+0x14)`. Bar is horizontally centred on the anchor.
5. **Background** frame drawn at `(x, y)` via `0x027C7D50`.
6. **Fill**: `SetClipRect(0, 0, x + this->m_fill(+0x00) + fillXAdjust, screenH)` — `0x0162F920`, clamped to globals `0x0399AE98` (screen W) / `0x0399AE9C` (screen H); the **foreground** frame is then drawn whole at `(x + offX, y + offY)` via `0x027C7B10`; `0x0162F980` restores the full-screen clip. So the fill is a left-anchored horizontal reveal, not a scaled sprite.

## Ownership and the three drive paths

`IRDCharacter` owns it (the two owner functions `0x018A9850` / `0x018A9DE0` sit
between the `IRDCharacter.cpp` anchors `0x01898380` and the `IRDCollisionObject.cpp`
anchor `0x018B27D0`):

- **`owner + 0x356` : BYTE `m_bShowHitGauge`** — `0x018A9E4E mov byte [esi+0x356],1` on set; every use site tests it first (`0x018A9883`, `0x018B39C6`, `0x018C39AD`).
- **`owner + 0x358` : `CNHitGauge*`** — lazily allocated (`0x018A9E06 cmp [esi+0x358],0`); the folded accessor `0x027D8640` is literally `mov eax,[ecx+0x358]; ret` = `getHitGauge()`.
  (Caveat: `0x027D8640` is COMDAT-folded — `0x00ACB336` uses the same body on a
  different class where `+0x358` is an int compared against `-1`. Don't read that
  site as a gauge access.)

Three ways the value moves, all gated on `m_bShowHitGauge && m_pHitGauge`:

1. **type 0 — HP ratio.** `0x018B39D8`: virtual call `[vtbl + 0x3DC]` (max-HP getter) → pushed as `max`, `[ebp+8]` (the new HP) as `cur`, then `setValue`. The caller chain reaches `IRDActiveObject::setHp` (`0x017DDD90`, literal `IRDActiveObject::setHp`). **This is the only server-driven input to the unit.**
2. **type 1 — hit counter.** `0x018C39C0` calls `onHit()` with no args from the damage-taken handler; each hit takes the bar down by exactly one step.
3. **type 2 — accumulator.** `addValue(delta)` from `CNRDActionBehavior` at `0x02687DAC`; the chain is `behavior->[+0x28]->[+0xD4] (owner char) -> [+0x358] (gauge) -> addValue(param[0])` using accessors `0x028D6D20` (`mov eax,[ecx+0xd4]`) and `0x027D8640`.

## Who configures it: the `.ani`/behavior script, not the server

`CNRDActionBehavior::BEHAVIOR_SHOW_HIT_GAUGE_FUNCTION` (the function starting at
`~0x026A10D0` inside the merged blob `0x02696AB0`; blob literals include
`CNRDActionBehavior::BEHAVIOR_SHOW_HIT_GAUGE_FUNCTION` and
`ERROR! invalid SHOW HIT GAUGE param - %s`) builds the descriptor on the stack and
calls `IRDCharacter::setHitGauge` for every object in the behavior's target list
(`0x026A183A`, guarded by a virtual `[vtbl+0x74]` predicate).

Its parameter keys — decrypted string ↔ token id ↔ descriptor field, all CONFIRMED
from the token registrations at `0x026A1362`/`0x13A4`/`0x13E6`/`0x1424`/`0x1463`
and the matching handlers:

| script key | token id | effect on the descriptor |
|---|---|---|
| `[COUNT]` | `0x2604` | `cur = max = N`, `type = 1` (`0x026A14FC`) |
| `[INCREASE MAX]` | `0x260F` | `fill = 0`, `cur = 0`, `max = N`, `type = 2` (`0x026A14D1`) |
| `[X POS]` | `0x261B` | `+0x10` (`0x026A1524`) |
| `[Y POS]` | `0x261E` | `+0x14` (`0x026A155C`) |
| `[TIME]` | `0x2621` | `+0x0C`, the ms auto-hide (`0x026A1542`) |
| *(neither COUNT nor INCREASE MAX given)* | — | descriptor stays at ctor defaults ⇒ `type = 0`, the HP bar |

The behavior opcodes themselves (registered at `0x02C3CEDD`–`0x02C3CF71`, map
`name → parser fn`):

| behavior token | opcode | script parser fn |
|---|---|---|
| `[SHOW HITGAUGE]` | `0x12E` | `0x02C25060` |
| `[HIDE HITGAUGE]` | `0x12F` | `0x02C25E00` |
| `[HITGAUGE OP INCREASE]` | `0x19F` | `0x02C25D60` |
| `[GET HITGAUGE VALUE]` | — | reads `gauge->m_cur` via `0x02846D50` (`mov eax,[ecx+4]; ret`) at `0x02676655` |

## Timer sub-object at `+0x1C` (shared utility, `0x027C3xxx`)

- `0x027C3A90` ctor — vptr `0x0304E790`, zeroes `+8/+0xC/+0x10/+0x14/+0x18`, bumps a global instance counter `0x03B89BF4`.
- `0x027C3410` reset/stop — clears flag bits `1|2` in the obfuscated word at `+0xC` (`and 0xFFFFFFF9`).
- `0x027C3B60 start(duration, x)` `ret 8` — sets flag bit 2 (running), stores `duration` → `+0x18`, `x` → `+0x14` when flag bit 4 is set.
- `0x027C3500 isRunning()` — flag bit 2.
- `0x027C3780 isExpired()` — `elapsed(0x027C3660) >= duration(+0x18)`.
- The `+0xC` flag word is read/written through `0x027BBFD0` / `0x027BBF40`, the client's value-obfuscation pair — a reimplementation only needs the semantics above.

## Span correction

The briefing's `0x01329D20`–`0x01329EA0` covers only the two functions that carry
a `__FILE__` literal. The real translation unit runs at least
**`0x01329C50` – `0x0132A390`**:

- `0x01329C50` (ctor) is `new(0x3c)`'d and then handed to the in-span
  `0x01329DA0`, and it initialises the same 7 fields with the same constants
  (`0x45`, `-1`) that `0x01329C20`/`0x01329DA0` use.
- `0x01329CC0` (dtor) tears down the same `+0x1C` timer the ctor built.
- `0x0132A0C0` (draw, ends `0x0132A390`) calls the in-span `canLoadImages` and is
  the only consumer of `+0x00`, `+0x10`, `+0x14`, `+0x18`, `+0x38`; it owns the
  `Interface/Creature.img` literal.
- `0x01329EA0` itself extends to `0x0132A0C0`, i.e. past the briefing's `hi`.
- Below `0x01329C20` the code belongs to `CNSelectChannelModule.cpp`
  (`0x01329980` carries that `__FILE__`), so `0x01329C20` is the lower edge.
  `0x01329C20` (the descriptor ctor) is only referenced from
  `CNRDActionBehavior.cpp` and may be that TU's COMDAT copy rather than this one's.

No literal inside the corrected span contradicts the file name.

## Server relevance

Thin, but not zero:

- **Nothing here parses or sends a packet.** The unit is presentation-only. A
  server does not have to mirror any of it.
- The only server-controlled input is **HP**: `IRDActiveObject::setHp` → `setValue(hp, maxHp)`
  → fill = `hp * 69 / maxHp` px. Any HP-sync packet the server sends is what the
  overhead bar shows; there is no separate "gauge" packet.
- The `[COUNT]`, `[INCREASE MAX]`, `[TIME]`, `[X POS]`, `[Y POS]` parameters come
  from client-side `.ani` behavior scripts (`CNRDActionBehavior`), **not** from the
  wire. If a monster shows a counter/charge bar, the server does not drive it and
  must not be expected to.
- Practical consequence for a server implementer: a client that shows a
  wrong-looking overhead bar is an HP-sync problem (or a client resource problem —
  a missing `Sprite/interface2/OverHeadGauge.img` frame 0/1 makes the bar silently
  vanish, logging only the two `IMAGE_INTERFACE_OVERHEAD_GAUGE` errors above), not
  a missing protocol message.
