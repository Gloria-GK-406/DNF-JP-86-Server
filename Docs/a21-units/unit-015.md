# TU 15 — `DNFClient\RDAR\CNRDEquipment.cpp`

Briefing span `0x0169FB30`–`0x016DAD90`. Image base 0x00400000, S4A21 client, unpacked.
Parsers in span: **0**. This unit does not touch the inbound stream at all.

---

## 1. What this translation unit is

`CNRDEquipment` — the client-side **equipped-item entity class**. It is the C++ object the
client instantiates for every piece of equipment (weapon/armour/accessory), constructed from a
parsed Equipment script record. The file is large (asserts reference source lines up to
**14492**), and it implements:

* construction from an equipment script, or by equipment index via a global script table;
* the copy of ~60 script fields into the live object (`create`);
* **set-item** effect activation (`addSetEquipmentAppendages`) with a "set master" concept;
* **part-set** (`EquipmentPartSet.etc`) resolution and status-parameter application;
* **aura appendage** creation from the item's special-ability records
  (`applyEquipmentAppendageAuraAbility`);
* the **equipment-event action** hook (`onApplyEquipmentEventActions`) driven from
  `DNFClient\RDAR\Equipment\CNRDEquipmentEventChecker.cpp` / `EquipmentEventActor.cpp`;
* the stat-application virtual `setEquipmentParameter`;
* lazy-loading of `etc/upgrade.etc` and `etc/chn_high_ranking_upgrade.etc`.

### Decrypted literals owned by this file
Literal block `0x032D7F94`–`0x032D8910` is contiguous and exclusive to this file.

| literal VA | text |
| --- | --- |
| `0x032D7F94` | `CNRDEquipment::procAppend` |
| `0x032D7FE8` | `CNRDEquipment::isUsable` |
| `0x032D8030/50` | `_mask2.img` / `_mask1.img` |
| `0x032D8080` | `D:\Work\1229\Source\DNFClient\RDAR\CNRDEquipment.cpp` |
| `0x032D80F8` | `CNRDEquipment::applyEquipmentAppendageAuraAbility` |
| `0x032D8168` | `ERROR]추가되지 않은 장비 특수 어펜디지 타입` ("unregistered equipment special appendage type") |
| `0x032D81A8` | `ERROR]세트 마스터가 장착되지 않았음에도 불구하고 세트효과 발동하려고 시도` ("tried to fire set effect although the set master is not equipped") |
| `0x032D8208` | `CNRDEquipment::addSetEquipmentAppendages` |
| `0x032D8268` | `ERROR]세트를 장착하지 않았음에도 세트효과 발동하려고 시도 혹은 개발 오류` |
| `0x032D82C8` | `UI/Inventory/EpicEffect_01.ani` |
| `0x032D8310` | `CNRDEquipment::addStatusParameterSetItem` |
| `0x032D8370` | `Not Exist PartSet Index(%d) with [equipment part set](%d) in EquipmentPartSet.etc` |
| `0x032D8688` | `CNRDEquipment::setEquipmentParameter` |
| `0x032D86E0` | `ERROR]setEquipmentParameter에 디폴트 값이 적용되지 않습니다.` |
| `0x032D8748` | `CNRDEquipment::onApplyEquipmentEventActions` |
| `0x032D87A8` | `ERROR]onApplyEquipmentEventActions, %d장비 부모가 없음!` |
| `0x032D8814` | `CNRDEquipment::create` |
| `0x032D8848/8898/88E4/8910` | `CNRDEquipment Script Load Fail : %s`, `etc/chn_high_ranking_upgrade.etc`, `etc/upgrade.etc`, `CNRDEquipment Script Find Fail : %d` |

`0x032D81A8` and `0x032D8168` were **not** in `literals.csv` — the shipped decoder rejects them
on its printability score. They decode cleanly under the same cipher with the score gate removed.

---

## 2. Span correction — the briefing's `lo` is too high

The anchor-derived low bound `0x0169FB30` **cuts off part of the file**. Provable members below it,
all referencing the same exclusive literal block:

| VA | evidence |
| --- | --- |
| `0x016964F0` | references `CNRDEquipment::procAppend` @ `0x032D7F94`; also vtable slot `+0x1D0` |
| `0x0169B8E0` | references `CNRDEquipment::isUsable` @ `0x032D7FE8` and `%s%d%s` @ `0x032D7FD0` |
| `0x01696220` | references `equipment/character/Particle/` @ `0x032D7F50` |
| `0x0169DC70` | references `_mask1.img`/`_mask2.img` @ `0x032D8050`/`0x032D8030` |
| `0x01692C00` | references `+%d ` @ `0x032D7F38` (weaker — block boundary sits right here) |

The high bound looks correct: the next literal sub-block starts at `0x032D8AC0`
(`GOLD_DROP1`, func `0x016DBE10`) which is a different file.

Also note `functions.csv` under-segments this range badly. `0x0169FB30` is listed as one 6784-byte
"function" but actually contains at least three (`0x0169FBD0`, `0x016A0D90`, `0x016A10E0` are all
separate vtable targets). The "124 functions" count is a floor, not a count.

TU 22 (`CNRDObject.cpp`, `0x0164F14B`–`0x017703A0`) swallows this whole span; that span is the one
that is wrong, not this one.

---

## 3. Object layout — `CNRDEquipment`

### 3.1 Hard facts

* **`sizeof(CNRDEquipment) = 0x1CE8` (7400 bytes).**
  Proof: 37 of 40 sampled call sites of the constructor `0x016D95F0` emit
  `push 0x1ce8 ; call <operator new> ; ... ; call 0x016D95F0`.
  Corroborated by the derived class at `0x01A7D060`, which calls `0x016D3C00`
  (CNRDEquipment default ctor) and then initialises its own members from `+0x1CEC`.
* **Primary vftable = `0x032D842C`, 150 slots (0x258 bytes).**
  Written by `0x016AABED`, `0x016D3C3E`, `0x016D962E`, `0x016DA1FE`
  (`mov dword ptr [esi], 0x32d842c`).
* **Secondary vptr at `this+0x380`, vftable `0x032D8424`, 1 slot → `0x0168C8F0`.**
  Written alongside the primary at `+0x6`/`+0xA` of each of those four sites.
  In `0x016D95F0` the intermediate base's value `0x319d29c` is stored first and then
  overwritten, so the `+0x380` base subobject exists in the parent class too.
* **Base class occupies `[0, 0x384)`.** The constructor `0x016D95F0` calls base ctor
  `0x00EF12E0`, sets the two vptrs, then initialises members starting at `+0x388` and running
  to `+0x1CC4`. Everything `< 0x380` written by `create` is base-class state.
* **Embedded item sub-object at `this+8`.** Virtual slot `+0x28` is
  `0x011BA6D0 = lea eax,[ecx+8] ; ret`. `create` uses `lea ecx,[ebx+8]` as `this` for
  `0x00EE8CC0`, `0x00C71130`, `0x00E8C480`, `0x02706C60`.

### 3.2 Field offsets recovered, with the access that proves each

| offset | evidence | reading |
| --- | --- | --- |
| `+0x00` | `mov [esi],0x32d842c` @ `016AABED` | primary vptr |
| `+0x08` | vslot `+0x28` returns `this+8` (`0x011BA6D0`) | embedded item-data sub-object |
| `+0x1C` | `mov edx,[eax+0x14]` where `eax=this+8`, printed by the `%d장비` assert @ `016D1330` | equipment id printed in logs |
| `+0x20` | guarded int, class id `0x32`: `push 1;push 0x32;lea esi,[eax+0x18];push 4;push esi;call 0x2c4d110` @ `016A79C2`, `016B1964`, `016A6892`; written from `script+0x04` @ `016D496E` | equipment/item index (guarded). The `addStatusParameterSetItem` assert prints it as "PartSet Index" — the developer's label, printf order verified: `add esp,0x1c` = 7 dwords |
| `+0x24 / +0x28 / +0x4C / +0x50` | `lea reg,[edi+0x110/0x130/0x158/0x134] ; push ; lea ecx,[ebx+…] ; call 0x27bfba0/0x27bf7c0` @ `016D4978`… | 4-byte copies from script |
| `+0x60` | guarded int, class id `0x22`, from `script+0x0C` @ `016D4A55` | grade/rarity-class value (see §5.3) |
| `+0x68,0x6C,0x74,0x78,0x7C,0x8C` | `mov` from `script+0x18,0x1C,0x20,0x60,0x5C,0x24` @ `016D4A5C`…`016D4A92` | plain int fields |
| `+0xB8 + 8*i`, i=0..13 | loop @ `016D4BF0`–`016D4C19`, `mov dl,[edi+esi+0x80]`, `cmp esi,0xE` | **14-entry byte array** copied from `script+0x80`, each entry guard-registered (`push 1;push 0x24;push 1`), stride 8. Same 14-entry array scanned at `016A3280` |
| `+0x12C,+0x134` | from `script+0xC8` | |
| `+0x130,+0x144,+0x14C` | from `script+0x35C` | |
| `+0x148,+0x150` | from `script+0xD0` | |
| `+0x140` | from `script+0x37C` | |
| `+0x1A4,+0x1A8` | from `script+0xDF0,0xDF4` | |
| `+0x1C4,+0x1D8` | `0x0120E5B0(&script+0x64 / &script+0x2DC)` | container assigns |
| `+0x1EC,+0x200,+0x208` | from `script+0x2F0,0x178,0x17C` | |
| `+0x240,+0x250,+0x254` | from `script+0x10D9,0xF04,0x3C4` | |
| `+0x374` | `mov byte ptr [ebx+0x374],1` @ `016D4895` | "script load failed" flag |
| `+0x375` | `mov byte ptr [esi+0x375],1` @ `016DADDB` | "script index not found" flag |
| `+0x378` | `mov [esi+0x378],edi` @ `016DADA8` | equipment index passed to `create(index,…)` |
| `+0x380` | `mov [esi+0x380],0x32d8424` | secondary vptr |
| `+0x388, +0x39C, +0x3B0` | ctor allocs 8-byte sentinel node each @ `016D963E/9671/96A5`; `create` fills all three via `0x14EA890` from `script+0xF70/0xF84/0xF98` @ `016D722D/723F/7251` | three parallel list containers, stride `0x14` |
| `+0x3C8,0x3DC,0x3F4,0x430,0x4D8,0x528,0x53C,0x550` | ctor `lea ecx,[esi+…]` @ `016D96D9`…`016D9735` | further owned containers |
| `+0x60C` | `create`: `script+0x3C0 → this+0x60C` @ `016D550F`; **virtual slot `+0x144` = `0x0166F000 = lea eax,[ecx+0x60c]; ret`** | equipment **part/type enum**. Range tests in this unit: `cmp [eax],0xB / ja` @ `016C8571`, `cmp [eax],0xB / jbe` @ `016D93EA`, `cmp [eax],0x1D / je` @ `016D93FF` |
| `+0x680` | virtual slot `+0x148` = `0x0166F010 = lea eax,[ecx+0x680]` | |
| `+0x1094/+0x1098` | vslot `+0x170` = `0x0166F030` returns `[ecx+0x1098]` | |
| `+0x10A0` | `create`: vector assign from `script+0xD74` (guarded by `[edi+0xD78]!=[edi+0xD7C]`) @ `016D597C` | |
| `+0x10B4` | ctor zeroes it; `addStatusParameterSetItem` and `0x016A6160` refuse to run when it is NULL; used as the receiver of map `find` (`0x00ACF930`) | **the equipment-part-set map** |
| `+0x10B8/+0x10BC` | from `script+0xDF8/0xDFC` @ `016D5988/599A` | |
| `+0x10C8` | guarded 4-byte read/write via the alignment jump table @ `016D59BD` | |
| `+0x10DC` | from `script+0xDEC` | |
| `+0x10F4,+0x110C,+0x1120,+0x1138,+0x116C,+0x1180` | ctor containers | |
| `+0x1150` | `0x0166EFF0`: `cmp [ecx+0x1150],0xC ; sete al` | a "== 12" predicate on a type field |
| `+0x1160` | from `script+0xDE8` | |
| `+0x1198 / +0x119C` | `create` args 3 and 4: stored only if **both** != -1 (`016D48D7`–`016D48EA`) | an optional (a,b) pair supplied by the 4-arg constructor |
| `+0x11A0` | `create` first action: wide-string assign of the script name, length computed by an inline `wcslen` @ `016D485C`–`016D4878` | script/file name |
| `+0x11C0/+0x11C4` | zeroed on both paths of `0x016DAD90` | |
| `+0x120C` | `applyEquipmentAppendageAuraAbility` inserts each created appendage here via `0x009D8EF0` @ `016A103C` | map of live aura appendages |
| `+0x1234,+0x1238,+0x123C` | from `script+0xFAC,0xFB0,0xFB4` | |
| `+0x125C,+0x1270` | assigned from `script+0xE10,0xE24` via `0x0169E5A0` / `0x008B3660` | |
| `+0x1284,+0x12B0,+0x12B4,+0x12D0` | from `script+0xE38,0xE3C,0xE80,0xE84` | |
| `+0x12DC` | from `script+0x105C` | |
| `+0x12EC / +0x12F0` | `0x016A6160`: `cmp [ebx+0x12ec], [0x3a5be84]` → hit returns `[ebx+0x12f0]`; miss recomputes and stores | **memoisation pair**: cache generation stamp + cached part-set index |
| `+0x12F8,+0x12FC` | from `script+0x10D0` (`movsx`) and `script+0x10D4` | |
| `+0x1CAC` | from `script+0x1200` | |
| `+0x1CB0/+0x1CB4/+0x1CB8/+0x1CBC` | dtor frees `[esi+0x1CB4]` and `[esi+0x1CB0]`, then nulls all four @ `016AAC32`–`016AAC66` | heap buffer + size/cap |
| `+0x1CC4` | `0x016A15B0` (7 external callers): `add ecx,0x1cc4 ; call 0x864f80(&arg) ; [eax]=arg` — push_back; dtor destroys it @ `016AAC09` | growable list, public appender |

### 3.3 Guarded ("protected value") fields

The client stores some integers obfuscated. Access is `push 1 ; push <classId> ; push 4 ;
push <addr> ; call 0x02C4D070` (encode/register) or `0x02C4D110` (decode). A second, distinct
mechanism dispatches on `addr & 0xF` through a 5-way jump table
(`0x27BC0F0/160/1D0/240/390` load, `0x27BBBE0/CA0/D50/E10/A60` store) — the storage layout of
those fields depends on their 16-byte alignment, so **they cannot be read from a memory dump
with a plain 4-byte read**.

Guard class ids seen in this unit: `0x32` on `this+0x20`, `0x22` on `this+0x60`,
`0x24` on the 14 bytes at `this+0xB8+8*i`.

---

## 4. Public surface

53 functions of the span are reached from outside it (194 distinct external caller functions
overall). The ones that matter:

| VA | ext. callers | what it is |
| --- | ---: | --- |
| `0x016D95F0` | **82** | **ctor `CNRDEquipment(script, x)`** — base ctor `0x00EF12E0`, both vptrs, ~50 member containers, then tail-calls `create(script, x, -1, -1)` @ `016D9F4A`. Allocation size 0x1CE8. |
| `0x016DA1C0` | 3 | **ctor `CNRDEquipment(script, x, a, b)`** — same body, calls `create(script, x, a, b)` @ `016DAB1E` |
| `0x016D3C00` | 1 | **default ctor** — sets vptrs, no `create`. Used by the derived class at `0x01A7D060` |
| `0x016AABC0` | 13 | **destructor** — restores both vptrs @ `016AABED`, then tears down `+0x1CC4`, `+0x1CB0`, `+0x1C9C`, … |
| `0x016DAD90` | 2 | **`create(int equipmentIndex, x)`** — stores index at `+0x378`, looks it up in the global equipment-script table `0x04E15BB8` via `0x028A84B0`, then calls `0x016D47C0`. Failure → `+0x375=1`, assert line 594 |
| `0x016D47C0` | 3 (all internal ctors/create) | **`create(scriptName, x, a, b)`** — the script→object loader, §5.1 |
| `0x016A67B0` | 5 | **`addSetEquipmentAppendages`**, §5.2 — every external caller is in `Equipment\EquipmentEventActor.cpp` (`0x00E98FE0`, `0x00EA1C70`, `0x00EA2B30`, `0x00EA3D50`, `0x00EA5D60`) |
| `0x016D12D0` | 2 | **`onApplyEquipmentEventActions`** — called from `Equipment\CNRDEquipmentEventChecker.cpp` (`0x00E96DB0`) and `0x022EA720` |
| `0x016B1810` | 1 direct + vtable | **`setEquipmentParameter`**, virtual slot `+0x1EC` |
| `0x016A3210` | 12 | per-(job, part) lookup, §5.5 |
| `0x016A15B0` | 7 | push_back onto `this+0x1CC4` |
| `0x016C8530` | 3 | 28 KB text/description builder; gated on `*vslot0x144() <= 0xB`; all callers are UI (`0x02462B80`, `0x02463180`, `0x01896900`) |
| `0x016A75A0` | 3 | epic-glow effect; loads `UI/Inventory/EpicEffect_01.ani` |

### Virtual slots this class overrides with its own code
(offset in the 150-slot table at `0x032D842C`; only slots landing inside the file are listed)

`+0x110`→`0x016A10E0`, `+0x134`→`0x016A2F80`, `+0x14C`→`0x016D8090`, `+0x164`→`0x016A3B80`,
`+0x190`→`0x016AF8F0`, `+0x1B4`→`0x016AA2D0`, `+0x1BC`→`0x016A9280`, `+0x1C0`→`0x016AA830`,
`+0x1C4`→`0x016AA860`, `+0x1D4`→`0x016D16F0`, `+0x1D8`→`0x016D17C0`, `+0x1DC`→`0x0169FBD0`,
`+0x1E8`→`0x016CFB30`, **`+0x1EC`→`0x016B1810` `setEquipmentParameter`**,
`+0x1F8`→`0x016A7F90`, **`+0x1FC`→`0x016A0D90` `applyEquipmentAppendageAuraAbility`**,
`+0x210`→`0x016D2280`, `+0x214`→`0x016D27E0`, `+0x220`→`0x016D1560`, `+0x238`→`0x016D09F0`,
plus `+0x1D0`→`0x016964F0` (`procAppend`) and `+0x158`→`0x0169BE40` (inside `isUsable`), both
**below** the briefing's `lo`.

Slots used by this unit but implemented in the base class:
`+0x28` = `this+8` (`0x011BA6D0`), `+0x138` = get-script (`0x01683BB0`),
`+0x144` = `&this[0x60C]` (`0x0166F000`), `+0x148` = `&this[0x680]`,
`+0x188` = get parent/owner (`0x0166F300`), `+0x1B8`, `+0x1C8`, `+0x1E0`, `+0x184`.

---

## 5. Mechanisms worth mirroring

### 5.1 `create` — the script→object field map  (`0x016D47C0`, assert line 644)

Prologue, in order:
1. `if (!table@0x03A8D890.contains(0)) load("etc/upgrade.etc", table)` — `016D47F6`–`016D482E`
2. `if (!table@0x03A8E170.contains(7)) load("etc/chn_high_ranking_upgrade.etc", table)` — `016D4831`–`016D4859`
   (`0x029C28B0` is a `map::find(k) != end()` test; `0x029FAB10` is the `.etc` loader.
   This unit only *triggers* the load; the readers of both tables live elsewhere —
   `0x0166F630`, `0x02401BE0`, `0x02400E20`, `0x0235BC50`, …)
3. store the script name into `this+0x11A0`
4. `script = this->vslot_0x138()`; NULL → `+0x374 = 1`, log `CNRDEquipment Script Load Fail : %s`
5. if `a != -1 && b != -1` → `this+0x1198 = a`, `this+0x119C = b`
6. then ~60 field copies.

Copies extracted from the disassembly (script = `edi`, object = `ebx`, both stable across
`016D4889`–`016D79AB`):

```
&script+0x110 -> this+0x24     (0x27BFBA0)      script+0x18  -> this+0x68
&script+0x130 -> this+0x28     (0x27BF7C0)      script+0x1C  -> this+0x6C
&script+0x158 -> this+0x4C                      script+0x20  -> this+0x74
&script+0x134 -> this+0x50                      script+0x24  -> this+0x8C
&script+0xDF0 -> this+0x1A4                     script+0x5C  -> this+0x7C
&script+0xDF4 -> this+0x1A8                     script+0x60  -> this+0x78
&script+0x64  -> this+0x1C4    (0x0120E5B0)     script+0x2F0 -> this+0x1EC
&script+0x2DC -> this+0x1D8    (0x0120E5B0)     script+0x178 -> this+0x200
&script+0xD4  -> this+0x154                     script+0x17C -> this+0x208
&script+0xD8  -> this+0x158                     script+0x35C -> this+0x130/0x144/0x14C
&script+0xD58 -> this+0x63C    (0x005C0970)     script+0xC8  -> this+0x12C/0x134
&script+0xDF8 -> this+0x10B8                    script+0xD0  -> this+0x148/0x150
&script+0xDFC -> this+0x10BC                    script+0x37C -> this+0x140
&script+0xDD4 -> this+0x10F4   (0x016D3A90)     script+0x14  -> this+0x160
&script+0xF70 -> this+0x388    (0x014EA890)     script+0x1000-> this+0x1F0
&script+0xF84 -> this+0x39C    (0x014EA890)     script+0x1004-> this+0x1F8
&script+0xF98 -> this+0x3B0    (0x014EA890)     script+0x3C0 -> this+0x60C  (part/type)
&script+0xE10 -> this+0x125C   (0x0169E5A0)     script+0x1CC -> this+0x10B4 (part-set map)
&script+0xE24 -> this+0x1270   (0x008B3660)     script+0xDEC -> this+0x10DC
&script+0xD74 -> this+0x10A0   (0x014EA890, only if the vector is non-empty)
script+0x80..0x8D (14 bytes)   -> this+0xB8 + 8*i, each guard-registered id 0x24
script+0xFAC/0xFB0/0xFB4 -> this+0x1234/0x1238/0x123C
script+0xE38 -> this+0x1284    script+0xE3C -> this+0x12B0    script+0xE80 -> this+0x12B4
script+0x105C-> this+0x12DC    script+0x10D8-> this+0x40C     script+0x10D9-> this+0x410 (movsx)
script+0x10D0-> this+0x12F8 (movsx)            script+0x10D4-> this+0x12FC
script+0xDE8 -> this+0x1160    script+0xF04 -> this+0x250     script+0x1200-> this+0x1CAC
script+0x3C4 -> this+0x254     script+0x04  -> this+0x20 (guarded, id 0x32)
script+0x0C  -> this+0x60 (guarded, id 0x22)
```
(The few `script+X → several destinations` rows are register-reuse artifacts of my extractor and
should be re-checked individually before being relied on. The `&script+X` rows are verified
adjacent 4-instruction sequences.)

Script record touched up to `script+0x1204`, so the parsed equipment script object is ≥ 0x1208
bytes. It is the record held in the global table at `0x04E15BB8`, populated at startup from
`Equipment/` + `Equipment.lst` (see `0x01660170`, `INIT>> InitEquipmentScript is passed.`).

### 5.2 Set-item effects — `addSetEquipmentAppendages` (`0x016A67B0`, lines 14462 / 14492)

```
if (!vslot_0x1B8())                     -> assert "set is not equipped ... or dev error"  (line 14462)
if ( vslot_0x1C0())  0x016A64F0(a, b);                       // apply on the item itself
if (!vslot_0x1C4())  return;                                 // not a set master path
setId = vslot_0x1C8();
holder = (0x03A5C910 && setId != -1) ? 0x02752A80(setId, 1) : NULL;
target = vslot_0x188()->vslot_0x724( holder->[0x3C0] );      // find the equipped set-master item
if (target && decodeGuarded(target->vslot_0x28() + 0x18) == setId)
        target->0x016A64F0(a, b);
else    assert "set master is not equipped although set effect is firing"  (line 14492)
```
`vslot_0x1C0` and `vslot_0x1C4` are themselves implemented here (`0x016AA830`, `0x016AA860`).
The guarded compare re-uses guard class `0x32`, i.e. the item-index field — the check is
"the item found in the set-master slot has the expected index".

### 5.3 Aura appendages — `applyEquipmentAppendageAuraAbility` (`0x016A0D90`, vslot `+0x1FC`, line 7723)

Argument `p` carries a vector `[p+0x4B4, p+0x4B8)` of **0x20-byte records**. For each record:

| record offset | use |
| --- | --- |
| `+0x00` | kind. `0` → appendage classId **0x21 (33)**; `1` → classId **0x23 (35)**; anything else → assert `ERROR]추가되지 않은 장비 특수 어펜디지 타입` |
| `+0x04` | int, passed to the appendage setter |
| `+0x08` | id, passed to `0x00C2EB10(id, 0x12, N, 0)` where `N` = `0x64` (kind 1) or `0x1F4` (kind 0) |
| `+0x0C` | int |
| `+0x10` | int |
| `+0x14` | int → `cvtdq2ps` to float |
| `+0x18` | int |
| `+0x1C` | int → `cvtdq2ps` to float |

Kind 1: `new Appendage(0x23)` @ `0x00B076B0`, then `0x019FAAE0(x)`, `0x01BA25F0(a,b)`, `0x01BA5190(c)`,
then a `0x136` duration/param and `getParent()` twice.
Kind 0: `new Appendage(0x21)`, then `0x019FAAE0(x)`, `0x01BA2210(6 args)`.
Both are inserted into the map at `this+0x120C` keyed by a pair derived from `getParent()+8`.

Cross-check against the earlier fleet's `appendage-classid-table.csv`:
classId 33 → ctor `0x01BA2260`, alloc 344; classId 35 → ctor `0x01BA2610`, alloc 364 — and the
setters called here (`0x01BA2210`, `0x01BA25F0`) sit immediately before those ctors, consistent
with being methods of those same classes.

### 5.4 Part sets — `addStatusParameterSetItem` (`0x016A7940`, line 3962)

```
if (!vslot_0x1B8() || this+0x10B4 == NULL) return;
key  = this->partSetIndex();                       // 0x016A6160, memoised
it   = (this+0x10B4)->find(key);                   // 0x00ACF930
if (it == end)  assert("Not Exist PartSet Index(%d) with [equipment part set](%d) in EquipmentPartSet.etc",
                       decodeGuarded(this+0x20), key);
row  = 0x016A7340(this+0x10B4, &key);
if (0x00A75300()) return;                          // some global gate
if (0x0163D0C0(0x01102C30()) && this->0x01670B30()) return;
vslot_0x1E0(row, arg);                             // apply the part-set status parameters
```

`0x016A6160` (the part-set index) is memoised on a global generation counter:
`if (this+0x12EC == *(int*)0x03A5BE84) return this+0x12F0;` else recompute and store both.
The recompute walks a global vector at `0x04E1C224` whose element size is **28 bytes**
(`imul 0x92492493 ; sar 4`), gated on `this+0x1108` and `0x016849F0()`.
`0x04E1C224` is the global equipment-part-set table; it is read from
`0x016A60E0/6160/64F0/6F40/71C0/AA070/AA1A0`, from `create` (9 sites), and from `0x01707D50`.

### 5.5 `0x016A3210` — per-(job, part) equipment table key  (12 external callers)

Scans the 14-entry part array at `arg+0x80`, takes the first non-zero, then computes a key into a
lazily-built global map at `0x03A8EFE0` (init guarded by bit 0 of `0x03A8EFF4`, `atexit 0x03005AB0`):

```
job = arg->[0x3C0];
if (job == 12) key = arg->[0x3C4] + part*10 + 0x1C0;   // 0x1C0 = 448 = 32*14
else           key = part + job*14;                    // lea ecx,[eax*8]; sub ecx,eax; lea edx,[esi+ecx*2]
```
14 parts per job; job 12 is special-cased with a sub-job dimension of stride 10.

### 5.6 `setEquipmentParameter` (`0x016B1810`, vslot `+0x1EC`, assert line 1500)

10-argument virtual. Observed call site `0x016D9479`:
`setEquipmentParameter( *vslot_0x144(), this+0x680, script+0x3D0, X, arg, isHighEquip,
this+0xFD0, 1, 0, -1 )`.

Inside:
* `if (type != 0x20) this->0x01BC80E0()` on `this+0x1CB0`
* `if (type != 0x0D)` → normal path, forwards to `0x00EA87E0`
* `if (type == 0x0D)` → requires arg7 non-null and `0x015488C0()` true, otherwise
  asserts `ERROR]setEquipmentParameter에 디폴트 값이 적용되지 않습니다.`
* the "level band" of the applied parameter is chosen as 5 by default, 4 if `0x0168B470(1)`,
  2 if `this+0x3F0 != 0`, else 5 when `decodeGuarded(this+0x20) - 0x68C1 <= 5`
  (i.e. item index in **26817..26822**), else `0x0166E650(arg4)`.

The `isHighEquip` argument comes from `0x01683AE0(script)`:
```
bool f(script) {
    if (!script) return false;
    if (script->[0x0C] == 5) return true;        // grade/rarity == 5
    return decodeGuardedAligned(script->[0xA4]) >= 60;   // level >= 0x3C
}
```

### 5.7 `onApplyEquipmentEventActions` (`0x016D12D0`, assert line 10243)

```
owner = vslot_0x188();
if (!owner) { log("ERROR]onApplyEquipmentEventActions, %d장비 부모가 없음!", this[0x1C], this[0x20]); return; }
h = this->0x016CF530( evt->[0x08] );
if (!h) return;
for each entry in h->[0x860]:                     // a map
    owner2 = vslot_0x188();
    build( 1.0f, 1, 0, 1, 1, 0, 1, <string at entry> ) via 0x007E52B0
    owner2->vslot_0x184(...)
```
The producer side of this hook is `Equipment\CNRDEquipmentEventChecker.cpp` (`0x00E96DB0`) and
`Equipment\EquipmentEventActor.cpp` — i.e. the growth/event-equipment feature.

---

## 6. Server relevance

Honest summary: **there is nothing here a packet implementer needs directly.** The unit has zero
inbound-parse sites and does not build outbound frames. Its value to a server is indirect:

1. **`etc/upgrade.etc` and `etc/chn_high_ranking_upgrade.etc` are hard requirements of equipment
   construction** — the client will not finish creating an equipment object without them being
   loadable. A server that ships item data must ship those two `.etc` tables (globals
   `0x03A8D890`, `0x03A8E170`; loaded with key `0` and key `7` respectively).
2. **`EquipmentPartSet.etc`** is the source of the part-set table (global `0x04E1C224`, 28-byte
   rows). Part-set index → status parameters is a client-side computation; if the server also
   grants those stats it must use the same table and the same key rule
   (§5.4/§5.5: `part + job*14`, job 12 special).
3. **Set-item ("set master") semantics** — §5.2. The client requires the set master to be the
   item actually occupying the master slot, matched by item index. A server that grants set
   bonuses on a different rule will produce a client that logs errors and shows different stats.
4. **The 14-slot equipment-part model** (`script+0x80..0x8D`, keys `job*14 + part`) and the
   part/type enum at `this+0x60C` with its `<= 0x0B` / `!= 0x1D` bands.
5. **Item index 26817..26822 (`0x68C1..0x68C6`)** is a hard-coded special-case family inside
   `setEquipmentParameter`.
6. If you attach a debugger or read client memory: `sizeof = 0x1CE8`, vftable `0x032D842C`,
   item index at `this+0x20` but **guarded** — decode with `0x02C4D110(addr, 4, 0x32, 1)`, a raw
   read gives garbage.

---

## 7. What I could not determine

* The exact identity of the class of the script record (`vslot_0x138`'s return type) — no RTTI
  survives for it in `rtti-vtables.csv` / `vftables-a21.csv` (both return zero hits for
  "Equipment"). Its field names are unknown; only offsets are evidence.
* Semantics of most of the ~60 copied fields. I recovered the *map*, not the *meaning*.
* Whether the range `0x01692C00`–`0x0169FB30` belongs entirely to this file. Four functions in it
  provably do (§2); the rest is unproven, and the vtable shows this class's own methods are
  scattered as low as `0x01696220`, so a contiguous-TU assumption is not safe here.
* `0x016C8530` (28 KB, 3 UI callers) — I identified only its entry gate
  (`*vslot_0x144() <= 0xB`) and that it reads `arg+0x28`, `arg+0x30`… through the guarded-read
  jump tables. Its body is not analysed.
