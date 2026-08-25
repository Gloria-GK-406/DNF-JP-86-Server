# TU 25 — `DNFClient\RDAR\CNRDPassiveObject.cpp`

Span in briefing: `0x01772BB0` – `0x0177FE70`, 100 functions, 0 parsers.
Image base `0x00400000`. All VAs below verified by disassembly unless marked *inferred*.

---

## 1. What this unit implements

`CNRDPassiveObject` — the client class for a **non-character, damageable actor placed in the
world by another object**: skill-spawned objects, summons, monster body-parts, dungeon props.
It derives from `IRDCollisionObject` (TU 70) and is the base of **hundreds** of concrete
classes; that is why its methods have 100–500 in-degree.

Evidence for the derivation and for the "base of many" claim:

* `0x0177A100` (`CNRDPassiveObject::procAppend`, literal at `0x032DFC70`) calls
  `0x018B8630`, which carries the literal `IRDCollisionObject::procAppend` (TU 70).
* Its 364 external callers are the derived overrides, e.g.
  `0x006C2120` = `CNATStrenthFireHandStrike::procAppend`,
  `0x0098F990` = `CNDeadGolemRuins::procAppend`,
  `0x0099C210` = `CNQuattuormanusHand::procAppend` (literals.csv).
* Constructor `0x0177E870` writes `[this]=0x032DFD0C`, `[this+8]=0x032DFCBC`;
  rtti-vtables.csv lists both as the same type descriptor `0x0395E0CC`, offsets 0 and 8
  (multiple inheritance, second base at +8). RTTI names are stripped in this build.
* `0x01779BC8`: `mov dword [esi+0x94], 0x21` — the class stamps object-kind **0x21**.
  Consistent with `setMaster` (`0x01780241`) doing `master->isKindOf(0x21)` before
  down-casting via `0xFCE1E0` and reading the callee's `+0x1070/+0x1078` alpha fields.

Source-line evidence (from `__FILE__`/`__LINE__` assert sites) — the file is ≥ 4874 lines:

| line | function | VA |
|---|---|---|
| 2774 (`0xad6`) | `CNRDPassiveObject::procAppend` | `0x0177A100` |
| 2943 (`0xb7f`) | `CNRDPassiveObject::onCreateObject` | `0x01774570` |
| 3555 (`0xde3`) | (static init inside onDestroyObject) | `0x0177F3D0` |
| 4856 (`0x12f8`) | `CNRDPassiveObject::setAlphaForMaster` | `0x01772BB0`, `0x0178028A` |
| 4874 (`0x130a`) | `CNRDPassiveObject::setAlphaForOthers` | `0x01772C20`, `0x017802F0` |

---

## 2. Server-relevant findings

### 2.1 `setState()` — the state machine, and the damage payload (`0x0177AD60`, vtable +0x3CC)

Signature (from `ret 0x10`): `setState(int state, const std::vector<int>& params, T p3, T p4)`.
116 external callers (derived `setState` overrides). Ends with
`0x0177B693: mov [esi+0xFA4], edi` — **`+0xFA4` is the current state index**, and it is the
value the save-stream writes as a `u16` (§2.2).

Dispatch at entry (`0x0177AD90`): `state == 0` → skip; `state == 1` → **damage block**;
anything else → common tail.

**State 1 parameter layout** (all `int`, `params` is a `std::vector<int>`; bounds-checked with
`0x2C623E1` = `std::_Xout_of_range`):

```
p[0], p[1]   -> __int64 hpDelta      (lo, hi)   -- combined at 0x0177ADD3
p[2], p[3]   -> __int64              (combined at 0x0177AE11, result unused here)
p[4]         -> 0x0177AE32
p[5]         -> 0x0177AE49
p[6]         -> 0x0177AE60
p[7]         -> 0x0177AE77
p[8]         -> 0x0177AE8C
p[9]         -> hitCount             (0x0177AEF5)
                requires size > 2*hitCount + 9  (0x0177AEFD..0x0177AF03)
p[10+2i]     -> per-hit value        (summed into the running total, 0x0177AF44)
p[11+2i]     -> per-hit flag; entry.crit = (value == 1)   (0x0177AF80)
```

HP application (`0x0177AFB4`–`0x0177AFEF`):

```
cur64 = getHP()                       // vtable +0x3D8 -> 0x01772CE0
cur64 -= (__int64)sum(per-hit values) // sign-extended
cur64 -= hpDelta                      // params[0..1]
setHP(cur64.lo, cur64.hi, 0, 1)       // vtable +0x3D4 -> 0x01772CA0
```

The per-hit entries then become floating damage numbers; display flags are picked from
`[esi+0xF54]`, `[esi+0xF60]` and `([0x03B3DFB8] == [esi+0x700])` — `0x03B3DFB8` is the
**local-player pointer global** (same comparison at `0x018B3A3E` and `0x018B3AAF`).

`setHP` forwards to `IRDCollisionObject::setHP` at `0x018B38E0`, which is where "it died"
is decided: `if (newHP <= 0 && getHP() > 0)` → notify the HP-bar/target manager at
`0x03A5C9EC`, and if the killer equals the local player, `0x0063BC30(6)`.

Common tail of `setState` (both branches): `getStateBase()` = vtable `+0x33C`, then
`getAnimationForState(state - base)` = vtable `+0x194`, then `setAnimation` = vtable `+0x198`.
So a `state` index maps 1:1 onto an animation slot offset from a per-class base.

### 2.2 Save / restore stream — exact wire order (`+0x2BC` / `+0x2C0` / `+0x2C4`)

The unit implements a three-call persistence protocol on a byte-stream object
(write cursor at `stream+8`, buffer at `stream+4`; read cursor at `stream+0x10`,
buffer at `stream+0xC`):

primitives (verified by disassembly):
`0x0119D270` writeU16 · `0x0119D290` writeU32 · `0x0119D2D0` writeBytes(ptr,len)
`0x0119D080` readU16 · `0x0119D0A0` readU32 · `0x0119D0F0` readBytes(dst,len)

**`0x017751C0` — serialize (vtable +0x2BC)**, after `IRDObject::save` (`0x018A8980`):

| order | width | field | VA |
|---|---|---|---|
| 1 | u16 | `[+0xAEC]` master object-kind | `0x017751D0` |
| 2 | u16 | `[+0xAF0]` master id (read out of the obfuscated cell) | `0x0177523A` |
| 3 | u16 | `[+0xFA4]` current state | `0x01775245` |
| 4 | u32 | `[+0xAD8]` HP (low dword) | `0x01775254` |
| 5 | u32 | `[+0xAE0]` maxHP (low dword) | `0x01775262` |
| 6 | u16 | `[+0xB08]` | `0x01775270` |
| 7 | u32 | `[+0xFD0]` userData length | `0x0177527F` |
| 8 | raw | `[+0xFD4]`, `[+0xFD0]` bytes of userData | `0x0177529B` |

**`0x017752D0` — deserialize (vtable +0x2C0)**: reads the same eight items in the same
order; the two 64-bit fields are restored with their **high dwords forced to 0**
(`0x0177540F`: `xor eax,eax` → `[+0xADC]=0`, `[+0xAE4]=0`). The blob is parked in
`[+0x1120]` (len) / `[+0x1124]` (heap copy) rather than applied immediately.

**`0x01775460` — post-load fixup (vtable +0x2C4)**: re-resolves the master pointer from the
saved *(kind, id)* pair — `0x0190FC20(world=[+0xB8], kind=[+0xAEC], id=[+0xAF0], 1)` →
cast `0xFCE110` → `this->setMaster(...)` (vtable `+0x21C`); then replays the parked blob via
`setUserData` (vtable `+0x5E4`) and frees it.

This is the client-side round-trip a server must be compatible with when it re-sends
persistent object state; note that the persisted identity of a passive object is
**(objectKind u16, objectId u16)**, not a pointer.

### 2.3 Stage-clear ("R_STAGE_FINALE") — end of `onDestroyObject` (`0x0177FA15`–`0x0177FD3A`)

`0x0177F3D0` is `onDestroyObject(IRDObject*)` (vtable `+0x228`, 215 external callers). Its tail
implements the last-kill / stage-finalize path:

```
if (obfRead(this + 0x34C) == 0x7FF9)         // twice, 0x0177FAAB and 0x0177FB0F
{
    n = obfRead(global 0x03A90D24) ;         // obfuscated global counter, payload 0x03A90D28
    obfWrite(0x03A90D24, n - 1) ;            // 0x0177FB9D
    if (obfRead(0x03A90D24) <= 0) {          // 0x0177FC95
        0x052DC795(this->world[+0xB8], 1);           // vmp thunk – finalize stage
        if (!currentMap[+0x778])
            0x01648A20(dec(L"R_STAGE_FINALE"), -1, 0, 0, -1, -1);   // 0x0177FCF5
        this->vtbl[+0x4C0](0);
        0x019975F0(map);
    }
}
```

`0x031A74C8` decrypts to the wide string `R_STAGE_FINALE` (verified with `dec.py`); it is a
named UI/effect resource, played through the same helper `0x01648A20` that
`IRDCollisionObject::setHP` uses at `0x018B3B14`. `0x7FF9` (32761) is the sentinel in the
obfuscated field `+0x34C` that marks "this object counts toward the stage kill quota".
A server that drives stage progression must keep its own count consistent with which
objects carry that marker.

### 2.4 Master / owner binding and party-follow

`setMaster` = **vtable `+0x21C` → `0x0177FE70`** (33 external callers). It snapshots the
master's identity and a block of its attributes:

```
[+0xAE8] = master
[+0xAEC] = master->vtbl[+0x48]()      // object kind          0x0177FEB4
[+0xAF0] = master->vtbl[+0x4C]()      // object id (obf cell)  0x0177FEC3
[+0x294] = master->vtbl[+0x28C]()     // float                 0x0177FF2D
[+0xAF8] = master->vtbl[+0x494]()                              0x0177FFD4
[+0xE84..+0xEA4] = master->vtbl[+0x360 .. +0x380]() (9 getters) 0x0177FFDA..0x01780076
[+0xF64..+0xF70] = 4 slots from master's avatar/equip chain     0x0178007C loop, i<4
if (master->isKindOf(0x21) && ((CNRDPassiveObject*)master)->[+0x1078] == 1) {
    [+0x1078] = 1;
    [+0x1070] = master[+0x1070];   // range-checked <= 0xFF, else assert setAlphaForMaster
    [+0x1074] = master[+0x1074];   //                        else assert setAlphaForOthers
}
if (master->isKindOf(0x81)) 0x0177F1D0(master);
```

`onCreateObject` (`0x01774570`, vtable `+0x224`) is the deferred-binding counterpart: when any
object is created it checks `newObj->getKind() == [+0xAEC]` **and**
`newObj->getId() == obfRead([+0xAF0])`, and if `[+0xAE8]` is still null, binds it
(`0x0177463D`, logging `IRDCollisionObject::onCreateObject, parent : %x, child : %x`).

**Party-follow mode** (both in `onCreateObject` `0x017746A8` and `procAppend` `0x0177A264`):

* `[+0x1038]` — follow mode; `0` = off, `2` = "die when the followed member is gone".
* `[+0x1040]` — followed party slot; init `-1`, sentinel `0x270F` while searching,
  resolved by scanning `0x03A5C990->getMember(i)` for `i` in **0..7** (`0x0177A2C1`).
* `[+0x1044] / [+0x1048] / [+0x104C]` — int dx/dy/dz added to the member's
  `getX()/getY()/getZ()` (vtable `+0xC4/+0xC8/+0xCC`) when snapping position
  (`0x01774713`, `0x01774726`, `0x01774739`).
* mode 2: if `0x019E7750(slot)` returns 0 the object destroys itself via vtable `+0x234`
  (`0x0177A32B`).

### 2.5 Attack propagation and the script hook (`0x0177DB70`, vtable +0x424)

`onAttack(attacker, attackInfo, char flag)` (`ret 0xC`, 162 external callers):

1. base `IRDCollisionObject::onAttack` = `0x018B4490`; its result is the return value.
2. forwards to the object from vtable `+0x498`, calling that object's vtable `+0x600`
   with the same three args (`0x0177DBB9`).
3. broadcasts to up to **32** sub-objects: `for (i = 0; i < 0x20; ++i)`
   `part = owner->vtbl[+0xA58](i)`; `part->vtbl[+0x1F0](attacker, attackInfo, 0, 1, this->vtbl[+0x300]())`
   (`0x0177DDE0`–`0x0177DE28`), then a second list via `0x017B1E10` (count) / `0x017B1E20` (at).
4. finally resolves and calls the **script hook named `onAttack_PassiveObject`**
   (literal `0x03179E64`, resolver `0x01B909A0` at `0x0177DECE`), cdecl,
   with `(this, attacker, attackInfo, flag)`.

### 2.6 Owner-visibility alpha (`0x01777040`, called from vtable `+0x1C`)

```
if ([+0x1070] == 0xFF && [+0x1074] == 0xFF) return;          // 0x01777068
anim = this->vtbl[+0x180]();                                 // shared_ptr<Animation>
alpha = isOwnedByLocalPlayer(this) ? [+0x1070] : [+0x1074];  // 0x017770F6 -> 0x017623A0
0x0264ECA0(0xFF000000, (alpha << 24) | 0x00FFFFFF);          // 0x0177711B
```

So `+0x1070` = **alphaForMaster** (used when the object belongs to the local player) and
`+0x1074` = **alphaForOthers**. Both default to 255 and are rejected above 255 with the
`ERROR! Invalid alpha value(%d)` assert. `0x017623A0` (CNRDObject.cpp) is the
"belongs to the local player" predicate used throughout this unit.

---

## 3. Object layout

`this` = `CNRDPassiveObject*`. Base `IRDCollisionObject` occupies roughly `0x000`–`0xAD7`;
the class's own storage begins at `+0xAD8`. Constructor `0x0177E870`, field initialiser
`0x01779B90`, destructor `0x0177EDD0`.
Two derived classes defined in this unit allocate **`0x1138`** bytes (`0x0177F5A0`), so
`sizeof(CNRDPassiveObject)` is `0x1130` and the derived adds one byte at `+0x1130`.

### 3.1 Base fields this unit touches

| off | meaning | proof |
|---|---|---|
| +0x94 | object kind, set to `0x21` | `0x01779BC8` |
| +0xA8 | `CNRDMovementUnit*` | `0x0177A21A` → `0x19216F0` (TU 21) |
| +0xB8 | world / dungeon context | `0x017754E2` → `0x0190FC20` |
| +0x108/+0x10C | vector of 8-byte elems, walked in `setMaster` | `0x01780153` |
| +0x294 | float from master vtable +0x28C | `0x0177FF2D` |
| +0x34C | obfuscated cell; `== 0x7FF9` ⇒ counts toward stage clear | `0x0177FAAB` |
| +0x38C | int, refreshed from `[+0x1094]` each `procAppend` | `0x0177A4B3` |
| +0x46C | matrix/anim block | `0x0177E008` |
| +0x700 | `IRDObject*` compared against local player | `0x0177B158` |
| +0x750 | int, default 0xC8 | `0x01779BDE` |

### 3.2 Own fields

Embedded aggregate at **`+0xAD8`** (ctor `0x0177B830`, dtor `0x0177A620`, extent `0xAD8`–`0xC38`;
`0x0177A620` destroys sub-offsets `+0x18`→`0xAF0`, `+0x34`→`0xB0C`, `+0x48..0x54`→`0xB20..0xB2C`,
`+0x60`→`0xB38`, `+0x10C`→`0xBE4`, `+0x138`→`0xC10`, `+0x15C`→`0xC34`):

| off | type | meaning | proof |
|---|---|---|---|
| +0xAD0 | bool | flag; getter `0x01772EB0`, setter `0x01772EC0` | |
| +0xAD1 | byte | | `0x01779BD8` |
| +0xAD4 | int | getter `0x01772E70` | |
| **+0xAD8** | `__int64` | **HP** | set `0x01772CC6`, get `0x01772CE0` |
| **+0xAE0** | `__int64` | **maxHP** | set `0x01772CF9`, get `0x01772D10` |
| +0xAE8 | `IRDObject*` | master/owner | `0x0177FE9D` |
| +0xAEC | int | master object-kind | `0x0177FEB4` |
| +0xAF0 | obf cell (8B) | master object-id | `0x0177FEC3` |
| +0xAF8 | ptr | from master vtable +0x494; getter vtable +0x494 = `0x01772E30`; cleared on destroy `0x0177F6A8` | |
| +0xB08 | u16 | serialized field 6 | `0x01775423` |
| +0xB38 | int | mode, compared 0..4 | `0x0177585B`.. |
| +0xB40/+0xB44/+0xB48 | vector\<int\> | iterated in onDestroyObject | `0x0177F78A` |
| +0xB6C/+0xB70 | vector | | `0x0177DCD9` |
| +0xB80/+0xB84 | vector\<int\> | ids passed to vtable +0x1BC | `0x0177F888` |
| +0xB90 | bool | one-shot latch, set after the loop runs | `0x0177F86C`, `0x0177DCAB` |
| +0xB94/+0xB98/+0xB9C | vector\<int\> | resized to 5 and zeroed in ctor | `0x01779D4C` |
| +0xBAC/+0xBB0/+0xBB4 | int | timers; `+0xBB4 = GetTickCount()` | `0x01775841` |
| +0xBB8 | int | mode (== 1 gate in `onAttack`) | `0x0177DBDC` |
| +0xBC0/+0xBC4 | vector\<int\> | | `0x0177DC0D` |
| +0xC40 | object | ctor `0x29B3120`, dtor `0x5AC120`; `&[+0xC40]` returned by vtable +0x5D8 (`0x01772E10`) and passed to vtable +0x2FC | `0x017757B0` |
| +0xE80 | ptr | setter `0x01772ED0`, getter `0x01772EE0` | |
| +0xE84..+0xEA4 | int[9] | snapshot of master vtable +0x360..+0x380; getters vtable +0x360..+0x380 = `0x01772D70`..`0x01772DE0`; setters vtable +0x5D0 (`0xE88`), +0x5D4 (`0xE98`) | `0x0177FFDA`+ |
| +0xEB0,+0xED0,+0xEF0,+0xF10,+0xF30 | CNString[5] | 0x20 each; `{void* cache; std::wstring s;}` — buf at +4, size at +0x14, cap at +0x18 | ctor `0x01779C87`.. / dtor `0x0177EF17`.. |
| +0xF50 | int | default −1 | `0x01779D54` |
| +0xF54 | bool | damage-number style selector | `0x0177B124` |
| +0xF60 | bool | ditto | `0x0177B1C7` |
| +0xF64..+0xF70 | ptr[4] | from master's avatar chain | `0x0178007C` |
| +0xF74 | CNString | animation name applied once at first `procAppend` | `0x0177A1E5` |
| +0xF94 | bool | "first animation applied" | `0x0177A1D3` / `0x0177A208` |
| +0xF95 | bool | setter `0x01772E80` | |
| +0xF98 | int | default 3 | `0x01779D7C` |
| +0xFA0 | int | default 0xFF | `0x01779D96` |
| **+0xFA4** | int | **current state index**; getter vtable +0xE4 = `0x01772E20` | `0x0177B693` |
| +0xFA8 | obf cell | default 2 | `0x01779DE4` |
| +0xFB0 | CNString | compared against `L""` in onDestroyObject | `0x0177F4F9` |
| +0xFD0 | u32 | userData length; getter `0x01772E40` | |
| +0xFD4 | void* | userData; getter `0x01772E50` | `0x017736D0` |
| +0xFE0 | u32 | `GetTickCount()` at construction | `0x01779DDE` |
| +0xFE4 | object | dtor `0x017795E0` | `0x0177EED6` |
| +0xFE8/+0xFEC | int pair | `((\[+0xFEC\]-\[+0xFE8\]) & ~31) != 0` ⇒ call vtable +0x600 | `0x0177A244` |
| +0xFFC | object | dtor `0x0177BC40` | `0x0177EEC7` |
| +0x1000/+0x1004 | vector of 8-byte elems | | `0x017801CB` |
| +0x1024 | int | getter vtable +0x44 = `0x01772E90` | |
| +0x1028 | int | setter `0x01772EA0` | |
| +0x102C | int | getter `0x01772F00` | |
| +0x1030 | float | getter `0x01772E60` | |
| +0x1034 | bool | default 1; setter `0x01772EF0`; gates the stage/death block | `0x0177F6C4` |
| +0x1038 | int | follow mode (0 off, 2 = die with member) | `0x0177A264` |
| +0x1040 | int | followed party slot, default −1, sentinel 0x270F | `0x0177A288` |
| +0x1044/+0x1048/+0x104C | int | dx/dy/dz relative to followed member | `0x01774713`+ |
| +0x1050 | bool | | `0x0177A347` |
| +0x1054..+0x1060 | int[4] | | `0x01779EEE` |
| +0x1064/+0x1068 | float | two direction values used by the 2-slot loop | `0x0177A3B0` |
| +0x106C | bool | mirror/flip select | `0x0177A37C` |
| **+0x1070** | int | **alphaForMaster**, default 255, must be ≤ 255 | `0x01772BBF` |
| **+0x1074** | int | **alphaForOthers**, default 255, must be ≤ 255 | `0x01772C2F` |
| +0x1078 | bool | "alpha explicitly set"; setter `0x01772C90`; inherited from master | `0x01780268` |
| +0x107C | int | default 4 | `0x01779EAE` |
| +0x1080/+0x1084 | int[2] | last applied direction per slot, default 4 | `0x0177A3E3` |
| +0x1088 / +0x108C | int | defaults 0x18 / 0x22 | `0x01779F39` |
| +0x1094 | int | setter vtable +0x118 = `0x01773740`; mirrored into `+0x38C` | `0x0177375F` |
| +0x1098 | int | default −1 | `0x01779BE8` |
| +0x109C | bool | "owner-stat scaling applied" | `0x01773B25` / `0x01774401` |
| +0x10A8 | double | scaling base | `0x01773DF5`, used `0x01774385` |
| +0x10B0 | float | scaling divisor | `0x01773B42`, used `0x017741DE` |
| +0x10B5 | byte | gates `0x01A0FFB0(0xE)/0x01A0FD00(0xD)` on create & destroy | `0x017747E4`, `0x0177F459` |
| +0x10B8 | CNString | | dtor `0x0177EEA8` |
| +0x10CC | u32 | (= size of the string at +0x10B8) also tested as a flag | `0x017747ED` |
| +0x10D8 | CNString | | ctor `0x01779FE2` |
| +0x10F8 | float | | `0x0177A01E` |
| +0x10FC | u16 | | `0x01779FF9` |
| +0x1100 | object | dtor `0x02231720` | `0x0177EE2D` |
| +0x1120 | int | pending userData length from load | `0x0177A012` |
| +0x1124 | void* | pending userData buffer from load | `0x0177537E` |
| +0x1128 | obf cell | default 0 | `0x0177A012` region / dtor `0x0177EE1E` |
| +0x1130 | byte | *derived class only* | `0x0177F157` |

Obfuscated ("protected") cells are 8 bytes: a 4-byte header at the offset and the payload at
+4. They are read/written through an alignment-dispatch table
(`0x027BC0F0/0x27BC160/0x27BC1D0/0x27BC240/0x27BC390` read,
`0x027BBBE0/0x27BBCA0/0x27BBD50/0x27BBE10/0x27BBA60` write), selected by `addr & 0xF`.

---

## 4. Public surface — the functions called from outside the unit

All of these also occupy vtable slots, i.e. derived classes call
`CNRDPassiveObject::X` explicitly from their own override.

| VA | vtable slot | external callers | what it is |
|---|---|---|---|
| `0x0177EDD0` | — | 509 | destructor |
| `0x0177A100` | +0x280 | 364 | `procAppend` (per-frame post-update) |
| `0x017736D0` | +0x5E4 | 265 | `setUserData(const void*, int)` → `[+0xFD4]/[+0xFD0]` |
| `0x0177F3D0` | +0x228 | 215 | `onDestroyObject(IRDObject*)` + stage-clear |
| `0x0177DB70` | +0x424 | 162 | `onAttack(...)` + `onAttack_PassiveObject` hook |
| `0x0177AD60` | +0x3CC | 116 | `setState(int, vector<int>&, ., .)` + damage |
| `0x01778AD0` | +0x1C | 64 | draw path → `0x01778320`, `0x01777040` (alpha) |
| `0x01774570` | +0x224 | 52 | `onCreateObject(IRDObject*)` |
| `0x017757A0` | +0x18 | 52 | per-frame/update entry (*inferred*) |
| `0x01775F30` | +0x278 | 46 | 4-arg position/move wrapper over `0x018A9850` (*inferred*) |
| `0x01773AA0` | +0x420 | 46 | owner-stat scaling setup (`+0x109C/+0x10A8/+0x10B0`) |
| `0x01773F70` | +0x428 | 37 | consumer of that scaling |
| `0x0177A950` | +0x430 | 34 | damage-gate; requires `getMaxHP() > 0` |
| `0x0177FE70` | +0x21C | 33 | `setMaster(IRDObject*)` |
| `0x017763C0` | +0x444 | 27 | render helper (float scale `+0x1030`, alpha) |
| `0x01775460` | +0x2C4 | 10 | post-load fixup |
| `0x0177DF00` | +0x2FC | 9 | animation/appearance apply (takes `&[+0xC40]`) |
| `0x017751C0` | +0x2BC | 7 | serialize |
| `0x017752D0` | +0x2C0 | 7 | deserialize |
| `0x017739D0` / `0x017739E0` | — | 6 / 7 | inc/dec of the global guard `0x03A906EC` |
| `0x0177F100` | — | 5 | constructor of a derived class in this file |

Non-virtual accessors also called from outside: `0x01772E40` (`+0xFD0`),
`0x01772E50` (`+0xFD4`), `0x01772E70` (`+0xAD4`), `0x01772EB0` (`+0xAD0`).

Constructors/destructors emitted here:
`0x0177E870` = `CNRDPassiveObject::CNRDPassiveObject()` (vtables `0x032DFD0C`/`0x032DFCBC`),
tail-calling the field initialiser `0x01779B90`;
`0x0177FE10` and `0x0177FE40` are scalar-deleting destructors for two derived classes with
vtables `0x032E0374`/`0x032E0324` and `0x032E09DC`/`0x032E098C`, both chaining to `0x0177EDD0`.

Globals this unit reads:
`0x03B3DFB8` local player · `0x03A5C990` party manager (`getMember(i)`, 8 slots, `0x019E7770`)
· `0x03A5C9EC` HP-bar/target manager · `0x03A90D24` obfuscated stage kill counter
· `0x03A906EC` reentrancy/active counter · `0x03A5CA98` / `0x03A5CA9C` construct/destroy counters.

---

## 5. Span check

The briefing's **lower** bound is right; the **upper** bound is slightly short:

* `0x0177FE70` is itself 1360 bytes long and runs to `0x017803C0`, past the quoted `hi`.
* A byte-identical second emission of the constructor lives at **`0x01782C40`**: same SEH
  scope table `0x2DD1B88`, same `mov [esi],0x032DFD0C` / `mov [esi+8],0x032DFCBC`, same
  calls to `0x0177B830` (the `+0xAD8` member ctor) and `0x01779B90` (the field initialiser).
  This is most likely an inline instantiation pulled into a neighbouring TU rather than a
  second copy of this file — the next `__FILE__`-anchored code above it is
  `CNRDVirtualCharacter` (`0x017840A0`) — but it is CNRDPassiveObject constructor code and
  anyone walking the class must know about it.

Everything inside the quoted span does belong to this file: every literal in the range is a
`CNRDPassiveObject::` name, the file path itself, or one of the two named resources
(`onAttack_PassiveObject`, `R_STAGE_FINALE`) that its handlers invoke.

## 6. Unknowns

* Vtable slot semantics for `0x017757A0` (+0x18) and `0x01775F30` (+0x278) are inferred from
  their callees, not proven.
* The meaning of `[+0xB08]`, `[+0x1024]`, `[+0x1088]=0x18`, `[+0x108C]=0x22` is unknown.
* Object-kind constants observed in `isKindOf` calls here: `0x11`, `0x21` (this class),
  `0x81`, `0x111`, `0x211`. Only `0x21` is proven; the others are used but not decoded.
* `params[2..3]` of the state-1 damage vector is combined into an `__int64` and then not
  stored anywhere in this function — its consumer, if any, is outside the unit.
