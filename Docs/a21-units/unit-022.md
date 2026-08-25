# Unit 022 — `DNFClient\RDAR\CNRDObject.cpp`

**Verdict:** this TU implements `CNRDObject`, the **root base class of every in-world
entity** in the client (character, monster, NPC, active object, passive object, item
object), plus `CNRDPooledObject`. It owns the object **type-tag taxonomy**, the object
**identity triple (type / gID / uID)**, the **position + movement-unit binding**, the
**animation-pause state machine**, and the client's **2.5D distance and direction
formulas**. It contains **zero packet parsing** (`tu-index.csv` parsers=0, confirmed —
no read-primitive callers in range).

---

## 1. The briefing's span is wrong — by ~17x

`tu-index.csv` row 22 gives `0x0164F14B – 0x017703A0`, 1156 KB, 1951 functions. That is
an artefact of one bad anchor.

| anchor | verdict |
| --- | --- |
| `0x0164F14B` | **BOGUS.** Not a function. Odd-aligned, `ends_in_ret=0`, disassembles to VMProtect-mutated code (`0164F246 call 0x5223442 ; -> .vmp1`, `0164F254 jmp 0x52242ff`). Carries the `CNRDObject.cpp` / `CNRDObject::proc` / `not valid movementUnit_ pointer` literals because `decrypt_all_literals.py` attributes an immediate to the nearest preceding function entry, and VMP kept the constants inline. |
| `0x0176C6F0` | real |
| `0x0176F110` | real |
| `0x017703A0` | real |

Because the `lo` bound is a VMP false positive, the span swallows at least nine other
translation units — `CNRDDungeon.cpp`, `CNRDItemManager.cpp`, `CNRDItemObject.cpp`,
`CNRDEquipment.cpp`, `CNRDStackable.cpp`, `CNRDNpc.cpp`, `CNRDFirstLoadingManager.cpp`,
`RDCommon.cpp` — every one of them visible in the briefing's own literal list.

### The real extent

Two independent measurements agree:

* **Literal-block ordering.** The `.rdata` string block for this TU is
  `0x032DE0D0 – 0x032DEFC0`, bracketed by `CNRDNpc.cpp`'s block (ends `0x032DDD58`) and
  `CNRDPassiveObject.cpp`'s (starts `0x032DF978`). Literal-VA order tracks function-VA
  order monotonically here, and every function using this block sits in
  `0x01766E70 – 0x01770770`.
* **Vtable reachability.** `CNRDObject`'s primary vtable `0x032DE8DC` has **183 slots**
  (`+0x000 … +0x2D8`); **131 of them** resolve into `0x017621F0 – 0x01770850`.

Adding the constructors/destructor (`0x0176FDC0`, `0x01771470`, `0x01771840`,
`0x01771E30`) and stopping at the next TU (`CNRDPassiveObject.cpp` @ `0x01772BB0`):

> **`CNRDObject.cpp` ≈ `0x01761A60 – 0x01772BB0`, ~68 KB, ~205 functions.**

Source line numbers leak from the log calls: the file is at least **3559 lines**
(`0xDE7`), so it is a big file — but its *code* is 68 KB, not 1156 KB.

`CNRDObject::proc` itself is **VMProtect-virtualized** and no longer lives with the TU;
its stub is the `0x0164F14B` region. Its logic is not statically recoverable.

---

## 2. Object type taxonomy — a bitmask subset test *(highest-value finding)*

`vtbl+0x34` on `CNRDObject` is `0x017621F0`, and it is four instructions:

```
017621F0  push  ebp
017621F1  mov   ebp, esp
017621F3  mov   eax, dword ptr [ebp + 8]      ; query tag
017621F6  mov   ecx, dword ptr [ecx + 0x94]   ; this->typeMask_
017621FC  and   ecx, eax
017621FE  xor   edx, edx
01762200  cmp   ecx, eax
01762202  sete  al
01762205  pop   ebp
01762206  ret   4
```

```c
bool CNRDObject::isMemberOf(int tag) { return (this->typeMask_ & tag) == tag; }
```

**`[this+0x94]` is a type BITMASK, not an enum ordinal.** A query passes when the
object's bits are a superset of the query's bits. This is the client's whole RTTI
substitute — 1000+ call sites image-wide.

### Tags proved by direct disassembly

`CNRDObject::checkAnimationPointer` (`0x0176F7D0`) walks the hierarchy and prints each
level, which pins the tags to names exactly:

| tag | meaning | proof site | literal |
| --- | --- | --- | --- |
| `0x021` | passive object | `0176F8DC push 0x21` | `-- is passive object` (line 2541) |
| `0x011` | active object | `0176F97F push 0x11` | `-- is active object` (line 2553) |
| `0x111` | character | `0176F9CE push 0x111` | `-- is character` (line 2556) |
| `0x211` | monster | `0176FA8F push 0x211` | `-- is monster` (line 2566) |
| `0x081` | (unnamed family) | `017635BC`, `0176F2CE push 0x81` | — |

Consistency check: character `0x111 & 0x011 == 0x011` → a character *is* an active
object. `0x111 & 0x211 == 0x011 ≠ 0x211` → a character is *not* a monster. Passive
`0x021 & 0x011 == 0x001 ≠ 0x011` → correctly not active. The encoding is
nibble-hierarchical: nibble0 = "is an RD object", nibble1 = family, nibble2 = subtype.

### Tags seen elsewhere in the image

Scan of `push imm32` within ±20 bytes of `mov r32,[r32+0x34]` across `.text`:

| tag | sites | tag | sites |
| --- | ---: | --- | ---: |
| `0x0111` character | 434 | `0x0221` | 7 |
| `0x0211` monster | 416 | `0x0821` | 3 |
| `0x1211` monster subtype | 25 | `0x0421` | 3 |
| `0x0121` passive subtype | 17 | `0x0202` / `0x0102` / `0x0802` / `0x0402` / `0x0082` | 2–6 each |
| `0x0081` | 13 | `0x2711` | 1 |

(Counts are noisy — the window also catches line-number pushes — but the two dominant
values are exactly the two names the disassembly proves.)

---

## 3. Object layout — `CNRDObject`, every offset with its witness

Recovered from the constructor `0x01771840`, the destructor `0x0176FDC0` (both install
the **same** vptr pair `0x032DE8DC` / `0x032DE88C`, which is what identifies them), and
the accessors.

| offset | field | proof |
| --- | --- | --- |
| `+0x00` | vptr #1 → `0x032DE8DC` | `0177189E`, `0176FDED` |
| `+0x08` | vptr #2, second base | `01771897 mov [esi+8], 0x32de88c` |
| `+0x10` | third base subobject | ctor `0x2C426E0`; RTTI base array gives `mdisp=16` |
| `+0x14` | std::vector (proxy) | `017718A1` proxy alloc, `+0x18/+0x1C/+0x20` = first/last/end |
| `+0x3C` | timer/clock (0x1C bytes) | ctor `0x27C3A90`; `0176F176 call 0x27c3660` = elapsed |
| `+0x58` | timer/clock | ctor `0x27C3A90`; paused `0x27C3CB0` / resumed `0x27C3D30` |
| `+0x74` | timer/clock | ctor `0x27C3A90`; pause **level ≥ 3** only (`0176F72F`) |
| **`+0x94`** | **type bitmask** | `017621F6`; printed as `type:%d` at `017705EA` |
| **`+0x98`** | **gID** | printed as `gID:%d` at `017705E4`; `vtbl+0x30` = `lea eax,[ecx+0x98]; ret` |
| `+0x9C` | encrypted-int wrapper (key) | `01765337 lea ecx,[esi+0x9c]` |
| `+0xA0` | encrypted-int ciphertext | `0176533F and edx,0xf` → alignment-dispatched decryptors `0x27BC0F0/160/1D0/240/390` |
| `+0xA4` | ID-struct `+0xC` | `01763E8D mov [eax+0xc], ecx` where `eax = vtbl[0x30]()` = `this+0x98` |
| **`+0xA8`** | **`movementUnit_`** | `0176F2EC`; matches literal `ERROR]CNRDObject::proc, not valid movementUnit_ pointer` |
| `+0xB8` | optional wrapper | `01763793 cmp [esi+0xb8], 0` |
| `+0xC0` | byte flag | `01763420 mov byte [ecx+0xc0], 0` |
| **`+0xC8`** | **primary animation player** | `0176F19E`; `isPause` `0x264B030`, `setPause` `0x264BBE0` |
| `+0xD0` | std::vector of 0x1C-byte elements, anim ref at elem`+4` | `0176F304` begin/end; `0176F484 add ebx,0x1c` |
| `+0xE4` | sub-object, ctor `0x8DA400` | `0177194F` |
| `+0xFC` | weak/shared ref | `01766C1C add edi, 0xfc` |
| `+0x104`, `+0x118`, `+0x15C`, `+0x170` | four more containers | proxy-alloc pattern in ctor |
| `+0x12C`, `+0x184` | ctor `0x13BEF90` | `017719D2`, `01771A49` |
| `+0x198` | ctor `0x27BCAE0` | `01771A5D` |
| `+0x1B8` | ctor `0x27B6C60` | `01771A82` |
| **`+0x1D0`** | **pause level (int)** | `0176F163 mov [edi+0x1d0], esi` |
| `+0x1D4` | previous pause level | `0176F15D` |
| `+0x1D8` | pause owner ptr | `0176F170` |
| `+0x1DC` | pause start time | `0176F17E` (= timer`+0x3C` elapsed − arg3) |
| `+0x1E0` | byte: anim pause state saved across pause | `0176F22D`, restored at `0176F49D` |
| `+0x1E8` | flag word — values | `0176667A or [esi+0x1e8], edi` |
| `+0x1EC` | flag word — override/dirty mask | `01766663 test [esi+0x1ec], edi` |
| `+0x238` | wchar_t string (proxy) | `01771A87`; buf `+0x23C`, size `+0x24C`, res `+0x250` |
| `+0x264` | sub-object, dtor `0x2871B40` | `0176FE83` |
| `+0x274` | std::vector (proxy) | `0176FE62`; first/last/end `+0x278/+0x27C/+0x280` |
| **`+0x2A8`** | **wstring: current animation file name** (proxy) | `01771AD9` ctor / `0176FDC0` dtor |
| `+0x2AC` | ↳ `_Bx` union (16 B SSO buffer, or ptr) | `0176F88F cmp [esi+0x2c0],8 → mov eax,[esi+0x2ac]` else `lea eax,[esi+0x2ac]` |
| `+0x2BC` | ↳ `_Mysize` | `0176FE2D` |
| `+0x2C0` | ↳ `_Myres` (reset to 7 ⇒ `wchar_t`, `_BUF_SIZE`=8) | `0176FE27` |

**`CNRDPooledObject` adds** (dtor `0x017703A0`, vptrs `0x032DF03C` / `0x032DEFEC`):

| offset | field | proof |
| --- | --- | --- |
| `+0x2C9` | bool: pool tracking enabled | `017703DD cmp byte [ebx+0x2c9], 0` |
| `+0x2CC` | int: **pool key** | `017703F1`; printed as `key:%d`; used as map key |

### Object identity — `uID` packs `gID`

`0x01765330` (called by the `CNRDPooledObject` dtor to produce `uID:%d`) is an
alignment-dispatched jump table; **all five branches end identically**:

```
mov eax, dword ptr [esi + 0x98]   ; gID
shl eax, 0x10
or  eax, dword ptr [ebp - 4]      ; decrypted 16-bit low from [this+0xA0]
ret
```

```c
uID = (gID << 16) | (decrypt_int(this+0xA0) & 0xFFFF);
```

The low half is stored **encrypted** (anti-cheat value obfuscation): a key at `+0x9C`,
ciphertext at `+0xA0`, and one of five decryptors chosen by `(this+0x9C) & 0xF`.

### Structural note

Every container in this class carries an 8-byte `operator new` proxy allocated in the
constructor and self-linked (`push 8 ; call 0x423460 ; [edi]=eax ; [eax]->=edi`), and
iterators bounds-check against `[container+4]`/`[container+8]` before dereference
(`0176F38E`, `0176F399`). **The client is built with `_ITERATOR_DEBUG_LEVEL ≥ 1`.** This
explains the `_invalid_parameter` calls (`0x2C623E1`) littering the RDAR code and is
worth knowing before reading any container layout in this image.

---

## 4. Pause state machine — `CNRDObject::setPause` (`0x0176F110`)

```c
void __thiscall CNRDObject::setPause(int level, void* owner, int startOffset); // ret 0xC
```

Signature and thresholds, all from the disassembly:

```
0176F147  if (level != 0 && owner == NULL) level = 0;   // null owner downgrades to 0
0176F15D  prevLevel_(+0x1D4) = level_(+0x1D0);
0176F163  level_(+0x1D0)     = level;
0176F16D  if (level) { owner_(+0x1D8) = owner;
                       pauseStart_(+0x1DC) = timer(+0x3C).elapsed() - startOffset; }
0176F184  if (prev == level) return;                    // no transition
```

Two thresholds, **1** and **3**:

| transition | effect |
| --- | --- |
| `prev < 1 → level ≥ 1` (`0176F19E`) | save `[+0x1E0] = anim->isPause()`, then `anim(+0xC8)->setPause(true)` (or the `+0x144` container); pause the `+0x58` timer; `movementUnit_(+0xA8)->setPause(true)`; walk vector `+0xD0` and pause every attached animation |
| `prev ≥ 1 → level < 1` (`0176F49D`) | **only** un-pauses if `[+0x1E0] == 0` — i.e. an animation that was already paused before stays paused. Save/restore, not a blind resume. |
| `prev < 3 → level ≥ 3` (`0176F6DA`) | additionally, if `isMemberOf(0x11)`: `vtbl[0x6A0](true)`, then `for (i = 0; i < 0x12; ++i) if (vtbl[0x6FC](i)) vtbl[0x6F8](i)->vtbl[0x28](true);` then pause timer `+0x74` |
| `prev ≥ 3 → level < 3` (`0176F74B`) | exact mirror with `false` |

The **18** (`0x12`) indexed sub-objects on an active object are a fixed-size slot array;
what they are is **unknown** from this TU.

> Note: `vtbl` offsets `0x5D0`, `0x5E0`, `0x6A0`, `0x6F8`, `0x6FC`, `0x774` are **beyond
> `CNRDObject`'s own 183-slot table** (which ends at `+0x2D8`). They live on the derived
> active-object/character vtable and are only reached after `isMemberOf(0x11)` passes.

### `CNRDObject::checkAnimationPointer` (`0x0176F7D0`)

A diagnostic. On a dangling animation reference it dumps
`error!! dangling pointer?! [current animation file name : %s]` (from the `+0x2AC`
wstring) then the type cascade of §2. `job`/`isAPC`/`currentSkill` come from
`vtbl[0x5E0]()`, `(BYTE)vtbl[0x3C]()` and non-virtual `0x17B0A60(this)` respectively —
argument order proved by `add esp, 0x14` (5 dwords) at `0176FA51`, cross-checked against
the 1-arg `-- index : %d` call (`add esp, 0xc`, `0176F944`) which uses `vtbl[0x44]()`.

---

## 5. Geometry — the two formulas a server would have to mirror

### Distance `0x017645C0` — 98 external callers

```c
int getDistance(int x1, int y1, int x2, int y2, bool ignorePerspective)
{
    float s  = ignorePerspective ? 1.0f          // [0x0304396C] = 1.0f (verified)
                                 : g_yScale;     // [0x03B55940], runtime-initialised
    int dx = x1 - x2;
    int dy = (int)((float)(y1 - y2) / s);
    int d  = (int)sqrt((double)(dx*dx + dy*dy));
    return d < 0 ? 2100000000 : d;               // 0x7D2B7500 overflow sentinel
}
```

The **Y axis is divided by a perspective scale before the Euclidean norm** — this is
DNF's 2.5D metric. A server doing range checks in raw `(x,y)` will not match the client.

### Direction `0x01762630` — 39 external callers

```c
int getDegree(int x1, int y1, int x2, int y2, bool ignorePerspective)
{
    float fx = (float)(x2 - x1);
    float fy = (float)(y2 - y1);
    if (!ignorePerspective) fy = fy * [0x030440E8] / (float)(int)[0x03991304];
    int deg = (int)(atan2(fy, fx) * (180.0/PI));   // [0x03148810]
    return (360 - deg) % 360;                       // 0x168 = 360
}
```

Note the **`360 - deg`** — the client's angle runs clockwise from the mathematical one.

Both are free functions (no `this`), reachable from all over the image, so they are the
client's canonical geometry primitives, not `CNRDObject` internals.

---

## 6. Pooled-object registry and the "delete violation" check

`CNRDPooledObject::~CNRDPooledObject` (`0x017703A0`, line **3559**) does this:

```
if (this->tracked_(+0x2C9) && this->poolKey_(+0x2CC) >= 0) {
    it = g_pool(0x03AA234C).find(&poolKey_);        // 0x1A6D330
    if (it == g_pool.end())                         // 0x293B470
        _invalid_parameter();                       // 0x2C623E1
    rec = it->second;                               // [node+0x10]
    rec->count(+0x14)--;
    ... scan rec->bucket(+4) for an entry whose [+8] == this ...
    if (not found)
        LOG("[PooledObject] [delete violation] [%s] key:%d idx:%d type:%d gID:%d uID:%d");
}
CNRDObject::~CNRDObject(this);                      // 0x0176FDC0
```

* **Global pooled-object registry at `0x03AA234C`**, a map keyed by `[obj+0x2CC]`.
* Format args, push order verified against `add esp, 0x1c` (7 dwords) at `0177061A`:
  `key = [+0x2CC]`, `idx = -1` (hard-coded constant, not a field), `type = [+0x94]`,
  `gID = [+0x98]`, `uID = 0x1765330(this)`.

`0x0176C6F0` (literal `false && _XTT("remove revision fail")`) is a refcounted-map
release emitted from this TU as a COMDAT: `find` → `--*it` → erase when `*it < 1`.

---

## 7. Public surface

**6887 distinct external callers** overall. Ranked:

| VA | in-deg | what it is | evidence |
| --- | ---: | --- | --- |
| `0x017623A0` | **1958** | predicate: `vtbl[0x230]()` compared against global `0x03B3DFB8`, with a global-state short-circuit via `0x1A6FCA0`/`0x1A6D7D0`. Reads as "is this object mine / locally controlled". **Name is inferred**, the mechanism is not. | full 80-byte disasm |
| `0x01771E30` | 510 | `CNRDObject` ctor overload → chains to `0x01771840` | `01771E83 call 0x1771840` |
| `0x017639F0` | 470 | `bool get() { return *(BYTE*)(this+0x30); }` | `mov al,[ecx+0x30]; ret` |
| `0x01763ED0` | 263 | default-false virtual (vtbl `+0x3C` and `+0x40`) | `xor al,al; ret` |
| `0x0176B740` | 138 | ctor of a small value type (`lower_bound` over a 40-dword table `0x0309F238–0x0309F2D8` for key 11, `+0x14 = 1.0f`); backs the lazily-initialised static at `0x039A8174` | `0176B760 call 0x65dbf0` |
| `0x017645C0` | 98 | **`getDistance`** (§5) | — |
| `0x0176FDC0` | 87 | **`CNRDObject::~CNRDObject`** — every derived dtor chains here | installs the ctor's exact vptr pair |
| `0x01771470` | 80 | `CNRDObject` ctor overload | same vptr pair, same init sequence |
| `0x01763E70` | 61 | `if (vtbl[0x30]()) vtbl[0x30]()->[0xC] = arg` (writes `this+0xA4`) | — |
| `0x01766650` | 58 | `setFlags(mask, value)` over `+0x1E8`/`+0x1EC`; a full-mask set commits and pokes the animation at `+0xC8` (`0x265EF60`) | — |
| `0x01771840` | 52 | `CNRDObject` ctor, 4 args (`ret 0x10`) — the layout document | — |
| `0x0176AFA0` | 45 | base `CNRDObject::procAppend` — registers the profiler label `CNRDObject::procAppend` in `0x03A90380` once, then a scoped marker. Does no work. | full disasm |
| `0x01762630` | 39 | **`getDegree`** (§5) | — |
| `0x0176D4D0` | 38 | `CNRDObject::drawAppend` | literal `0x032DE264` |
| `0x01765450` | 36 | single-mask variant of `0x01766650` | — |
| `0x01769F80` | 7 | `CNRDObject::draw` — 4128 bytes, 38 callees, the real render path | literal `0x032DE138` |

**Virtual interface** (`0x032DE8DC`, 183 slots, `+0x000 … +0x2D8`; 131 slots own-code):

| slot | function | role |
| --- | --- | --- |
| `+0x030` | `0x01763A00` | `&this->m_id` — `lea eax,[ecx+0x98]; ret` |
| `+0x034` | `0x017621F0` | **`isMemberOf(tag)`** (§2) |
| `+0x03C` | `0x01763ED0` | `isAPC()` — base returns false |
| `+0x040` | `0x01763ED0` | second false predicate |
| `+0x044` | `0x0048E890` | `getIndex()` — inherited, **outside** this TU |
| `+0x0A0` | `0x01762230` | `setPosition(x,y,z)` → `movementUnit_(+0xA8)` slots 0/1/2 |
| `+0x0AC` | `0x017622A0` | position getter triple → `0x1922740` slots 0/1/2 |
| `+0x0C4` | `0x01763B00` | used by `0x01763790` |
| `+0x1B8` | `0x017624A0` | 5-arg virtual; `0x01763EF0` wraps it as `(arg, -1, 0, 0, 0)` |

The **position/movement unit at `+0xA8`** is confirmed by its accessors:
`0x01923180(axis, value)` sets, `0x01922740(axis, value)` takes an **int** and
`cvtdq2ps`-converts it to **float** before storing, and `0x019213A0(bool)` pauses it.
Three axes only (0, 1, 2). Its implementation lives in
`DNFClient\RDAR\CNRDObjectMoveSystem.cpp` (`func-source-file.csv`, `0x00E470B0`).

---

## 8. What a server implementer should take from this

1. **The object type field is a bitmask, not an ordinal.** Any S→C field that carries an
   object type must be interpreted as `(objType & query) == query`. Confirmed values:
   `0x011` active, `0x021` passive, `0x111` character, `0x211` monster.
2. **`uID = (gID << 16) | (low & 0xFFFF)`.** If the server assigns object IDs, the top
   16 bits are the gID and the client will unpack them that way.
3. **Range checks must use the perspective metric** (§5): Y divided by the scale before
   the norm, `2100000000` as the overflow sentinel.
4. **Direction is `(360 − atan2_degrees) mod 360`**, clockwise.
5. **Pause is a level with thresholds at 1 and 3**, not a boolean, and level 0 is forced
   when the owner pointer is null. If a server-driven pause (cutscene, death, dialogue)
   is modelled, it must carry a level and an owner.

---

## 9. Unknowns — stated as unknown

* `CNRDObject::proc` is **VMProtect-virtualized**; its per-frame logic is not recoverable
  statically from this dump.
* The RTTI type-descriptor names are **numerically stripped by the packer** (`.` followed
  by a little-endian index: `.\xa7\x02`, `.\x04`…`.\x08`), so class names cannot be read
  from RTTI. The base-class *structure* survives: 6 entries, base subobjects at `mdisp`
  0, 8 and 16 — matching the two vptrs written by the ctor plus the `+0x10` base.
* The 18-slot (`0x12`) indexed sub-object array reached at pause level ≥ 3 is unidentified.
* Tag `0x081` is real (13 sites, and `setPause` branches on it at `0176F2CE`) but unnamed.
* `0x01761A60` — a **factory over 21 concrete subclasses**, `operator new` sizes
  `0x4D0`–`0x5B4`, one ctor (`0x51D7B35`) itself VMProtect'd. It sits between
  `CNRDNpc.cpp`'s last function and `CNRDObject`'s proven code. **TU membership is
  inferred by locality, not proven** — no vtable slot or literal ties it here. The size
  range is still useful: every concrete in-world object is 1232–1460 bytes, versus
  `CNRDObject`'s ~716-byte base.
* `0x017623A0`'s name ("is mine") is inferred from the global comparison, not proved.

---

## 10. Log-call ABI (reusable across the image)

```
log(int level, const char* file, const char* func, int line, const char* fmt, ...)
```
Each string argument is `push cipher_va ; call 0x27BACC0 ; add esp,4 ; push eax`.
`0x27BACC0` is the literal decryptor; `0x27BE270` is the sink; `0x27BDC10` is the
`Format(&out, fmt, ...)` helper. The `%d` in these messages is usually `__LINE__`, which
is how §1's line numbers were recovered.

**Line numbers in `CNRDObject.cpp`:** 1494 / 1544 / 1588 (`setPause` animation-pointer
checks), 2538 (`checkAnimationPointer` dangling), 2541–2576 (the type cascade), 3559
(pooled delete violation).
