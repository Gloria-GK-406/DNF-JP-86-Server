# TU 27 — `DNFClient\RDAR\CNRDSkillManager.cpp`

Briefing span `0x019D72A0`–`0x019D8540`. **The span is too narrow — see "Span correction".**

The unit is the client-side **skill-slot / quickslot book** for one character: a flat
array of `CSkill*` slots, two saved presets of that array, a skillIndex→slot reverse
map, per-frame quickslot key polling, and the **two client→server requests** that this
subsystem originates (opcode `0x1C` and opcode `0x1FD`).

---

## 1. Span correction

The `__FILE__` anchors only bracket `0x019D72A0`..`0x019D8540`, but the module is much
wider. Evidence:

| VA | evidence it is the same file |
| --- | --- |
| `0x019D5F50` | literal `CNRDSkillManager::isExpendQuickEmpty`, and it passes a `CNRDSkillManager.cpp` file literal (`0x32F6618`) + line `0xAC1` (2753) to the instance getter |
| `0x019D5FB0` | literal `CNRDSkillManager::getEmptyQuickSlot`, file literal `0x32F6670`, lines `0xACC`/`0xAD5` (2764 / 2773) |
| `0x019D8540` | body runs to `0x019D8940` (anchor is the *start*, briefing `hi` cuts the function in half) |
| `0x019D8940`, `0x019D8B90`, `0x019D8D40`, `0x019D8EF0`, `0x019D90F0` | all operate on the same object (`+0x18`, `+0x44`, `+0x5C`, `+0x84`, `+0xB0`, `+0xBC`) |
| `0x019DA9E0` | the constructor: `new(0xC4)` at `0x0164C624`, then builds `+0x1C`, `+0x5C`, `+0x70`, `+0x84`, `+0xAC..+0xBC` and tail-calls `0x019D8D40` |
| `0x019D6430` | sends packet `0x1FD`; indexes `this+0x1C+20*n` with `n = myChar->[0x54F0]` — the same preset idiom `0x019D8200`/`0x019D80E0` use on `this+0x84` |

Recorded line numbers span 1287 (`0x019D7647`) → 1751 (`0x019D873A`) → 2773
(`0x019D6010`), so the source file is ≥ ~2800 lines. The real module is roughly
`0x019D4000`–`0x019DAB60`; the 10-function/4 KB slice in the briefing is about 15 % of it.

`0x019D8990` (`Skill/SkillAttackData/`, `skill/SkillAttackDataList.lst`) sits inside that
range; whether skill-attack-data loading lives in this file too is **unknown**.

---

## 2. Server-relevant: outbound packet `0x1C` — `CNRDSkillManager::requestChangeSkillSlot`

`0x019D8540`, `__thiscall`, `ret 0x10`.
The literal `CNRDSkillManager::requestChangeSkillSlot` is bound to this VA.

**Opcode `0x1C` (28) is sent from exactly one site in the whole image** — call site
`0x019D877F` (cross-checked against the precomputed `MakePacket` opcode table
`_allsend.pkl`: 1060 send sites, 578 distinct opcodes, `0x1C` appears once).

### Signature

```c
bool requestChangeSkillSlot(int slot1, int slot2, int reason, int maxChangeCount);
//   [ebp+8]     [ebp+0xC]   [ebp+0x10]  [ebp+0x14]
```

### Preconditions, in execution order (all fail → return false, nothing sent)

| VA | test |
| --- | --- |
| `0x019D856B` | `m_changeCount (this+0xAC) >= maxChangeCount` → fail |
| `0x019D858B` | `slot1 >= slotCount` → fail |
| `0x019D8596` | `slot2 >= slotCount` → fail |
| `0x019D859E` | if `slot1` is an equip slot (`<6` or `0xC6..0xCB`) and `m_slot[slot2] != NULL`: decrypt `m_slot[slot2]->field_0x100`; **`!= 0` → fail** (`0x019D862B`) |
| `0x019D8635` | mirror: if `slot2` is an equip slot and `m_slot[slot1] != NULL`: `m_slot[slot1]->field_0x100 != 0` → fail (`0x019D86CD`) |
| `0x019D86D7` | `slot2 - 0x8A <= 0xB`, i.e. **`slot2` in `[138,149]` → fail** (a reserved block) |
| `0x019D86E6` | `g_myCharacter (0x3B3DFB8) == NULL` → fail |

Then a **warning-only** check (`0x019D86F3`):

```c
if (!isEquipSlot(slot2) && !(reason == 4 || reason == 7))
    LOG("유효하지 않은 스킬슬롯 장착해제 시도(slot1 : %d, slot2: %d, Reason:%d)",
        slot1, slot2, reason);   // CNRDSkillManager.cpp line 0x6D7 = 1751
```

* `0x019D5AC0` = `isEquipSlot(slot)` — jump table decoded from `0x019D5AF0` (0xCC entries):
  **true exactly for `{0,1,2,3,4,5, 198,199,200,201,202,203}`**, false for 6..197 and >203.
* `0x019D5BC0` = `isLegitUnequipReason(reason)` — **true only for `reason == 4` and `reason == 7`**.

The message says "invalid skill-slot *unequip* attempt", so moving a skill out of an
equip slot into the book is only sanctioned with reason 4 or 7. **It logs but still sends** —
a server must not assume the client filtered this.

### Wire format (`0x019D8774`–`0x019D8869`)

```
CNGameSocket::MakePacket(0x1C, 0)          ; 0x0274D1E0 (WORD type, BYTE flag)
WriteBYTE ( g_myCharacter->field_0x54F0 )  ; 0x0274D2E0 — active slot-preset index
WriteBYTE ( slot1 )
WriteBYTE ( slot2 )
WriteDWORD( memberIndex )                  ; 0x0274D340
CNGameSocket::SendPacket()                 ; 0x0274E6D0
```

`memberIndex` (`0x019D87B6`–`0x019D885A`) starts at `-1` and is only replaced when
`g_myCharacter` is found in a 3-entry table:

```c
u = CNUserManager(0x3A5C980)->get(0x019E3290);   // cached at userMgr+0x54
if (u && u->field_0x478 && u->field_0x478->field_0xA4 != g_myCharacter)
    for (i = 0; i < 3; ++i)
        if (u->field_0x478->field_0x88[i] == g_myCharacter) { memberIndex = i; break; }
```

Accessors: `0x019DAC30` = `+0x478`, `0x01447AF0` = `+0xA4`, `0x00551640(i)` = `+0x88 + i*4`.
So the normal single-character case sends `-1`.

### After a successful send

```
0x019D887B  m_changeCount (this+0xAC) += 1
0x019D8871  lock(0x3A5AFD4) ; read encrypted bool at 0x3A5AFCC/0x3A5AFD0 ; unlock
0x019D88BE  if (that bool)  this->applySwapLocally(slot1, slot2)   ; 0x05294605 (.vmp1)
0x019D88C3  return true
```

That global gates **client-side prediction of the swap**. When it is false the client
waits for the server's echo, so the server must send the resulting slot state back.

### `maxChangeCount` / `m_changeCount` budget

`this+0xAC` is a monotonically incremented counter, reset/set by
`0x019D6150`: `m_changeCount = (arg < 0) ? 0 : arg` (callers `0x01119280`, `0x0160F4D0`).
Every UI call site passes `maxChangeCount = 1`, so at most one change per reset window.
`0x019D8940` is the compound helper — it calls `requestChangeSkillSlot(a,b,reason,1)`
and, if that succeeded, `requestChangeSkillSlot(b,c,reason,2)`, i.e. a two-step move
that deliberately raises its own budget.

### `reason` values seen at call sites

| reason | caller |
| ---: | --- |
| 0, 1, 2 | `CNGameCore::setDebugTestValue` (`0x01630A00`) |
| 3, 4 | `CNRDBaseInterface::onLeftButtonUp` (`0x0135AD60`) |
| 5, 6, 7 | `CNRDSkillShop::onCheckSkillDrag` (`0x013CDE80`) |
| 0xA (10) | `CNComboSlotGridImpl::setDragSourceSkillSlot` (`0x015F11B0`) |
| 0xC, 0xD (12,13) | `CNRDSkillInventoryWindow::onDragEvent` (`0x0238B960`) |
| 0x13, 0x14 (19,20) | `CNComboSlotGridImpl::addSkill` (`0x015F33F0`), `0x019D8BF0` |

`reason` is a UI-provenance enum with at least 0..20; only 4 and 7 mean "legitimate unequip".

---

## 3. Server-relevant: outbound packet `0x1FD` — commit pending skill changes

`0x019D6430` (also the only `0x1FD` sender in the image; call site `0x019D6473`).
External callers: **`CNRDSkillShop::onClose` (`0x013C1E50`)** and
`CNComboGridController::onEvent` (`0x015CC580`).

```c
n = g_myCharacter->field_0x54F0;              // preset index
v = &this->m_changed[n];                      // this + 0x1C + 20*n
if (v->_Myfirst == v->_Mylast) return;        // nothing pending

MakePacket(0x1FD, 0);
WriteBYTE ( n );
WriteBYTE ( count = (v->_Mylast - v->_Myfirst)/4 );
for (i = 0; i < count; ++i) {
    CSkill *s = v->_Myfirst[i];
    if (!s) continue;
    if (!(s->field_0x74 && s->byte_0x9A0)) continue;   // 0x019AB7B0 "is dirty"
    WriteWORD( decrypt(s->field_0x74) );               // skill index
    m = (s->[0x9C0] - s->[0x9BC]) / 8;                 // 0x019ABAD0
    WriteBYTE ( m );
    for (j = 0; j < m; ++j)
        WriteWORD( decrypt(s->[0x9BC] + j*8) );        // 0x019B07A0(j)
}
SendPacket();
```

**Protocol quirk a server must handle:** the `count` byte is the *raw vector size*, written
before the per-skill filter runs. Entries whose `s == NULL` or whose dirty byte
`s->[0x9A0]` is 0 are silently skipped, so the actual number of skill records can be
**fewer** than the declared `count`. A parser that reads exactly `count` records will
desync. Parse to the frame boundary, or treat `count` as an upper bound.

The meaning of the trailing WORD array is **unconfirmed**. `0x019B07A0(i)` defaults to
`i = skill->field_0x60` when `i < 0`, and each element is 8 bytes (an encrypted int),
so `skill+0x9B8` is a vector of encrypted ints indexed by something the skill also
tracks at `+0x60`. Given the caller is `CNRDSkillShop::onClose`, this is the
"apply what I bought/leveled in the skill window" commit, but I did not prove the
element semantics.

---

## 4. Slot index space (the layout a server must mirror)

The slot array is one flat `CSkill*[N]`; `init()` sizes it from a ctor argument
(`0x200` for the no-character fallback instance, `0x019D8D40` ← `0x019DA9E0`).
Index constants that appear in code:

| range | role | evidence |
| --- | --- | --- |
| `0..5` | equipped skill row (6 keys) | `0x019D5AC0` table; `0x019D7BE0` emits `[0, N)`; `proc` loop 1 polls 6 keys |
| `6..197` | the skill book / inventory (not equipped) | `0x019D7AF0` iterates `[6, 0xC6)` when the "include equip rows" flag is 0 |
| `138..149` (`0x8A..0x95`) | **rejected as `slot2`** by `requestChangeSkillSlot` | `0x019D86D7` |
| `198..203` (`0xC6..0xCB`) | second equipped row — `isExpendQuickEmpty` scans exactly this range, so it is the *expendable/consumable* quick row | `0x019D5F51`..`0x019D5F99`; `0x019D5AC0` table |
| `204` (`0xCC`) | exclusive upper bound of the whole index space used by every scan | `0x019D7B08`, `0x019D5F93`, `0x019D6043` |

Row width is 6 normally, but **5** when the controlled entity's `vtable+0x5E0` getter
returns `0xA` (`0x019D7BE0` at `0x019D7C10`–`0x019D7C1D`).

---

## 5. Object layout — `CNRDSkillManager`, `sizeof == 0xC4`

`sizeof` proven by `push 0xC4 ; call operator new` at `0x0164C61F`; every field below is
written by the constructor `0x019DA9E0` or read by a function cited.

| offset | size | field | proof |
| --- | --- | --- | --- |
| `+0x00` | 4 | zeroed in ctor | `0x019DAADF` |
| `+0x04` | ~0x14 | sub-object, ctor `0x01083200` | `0x019DAA0D` |
| `+0x18` | 1 | "ready" flag, set to 1 in ctor; `0x019D90F0` returns `-1` when it is 0 | `0x019DAADB`, `0x019D911A` |
| `+0x1C` | 2 × 0x14 | `m_changed[2]` — per-preset list of dirty `CSkill*`, source of packet `0x1FD` | ctor `0x019DAA15` (`elemSize=0x14, count=2`); `0x019D644F` |
| `+0x44` | 6 | `m_prevPressed[6]` — skill-row key state last frame | `0x019D840D`, `0x019D8447` |
| `+0x4A` | 6 | `m_triggered[6]` — skill-row key edge this frame | `0x019D8421`, `0x019D843C` |
| `+0x50` | 6 | `m_prevPressed2[6]` — expend-row key state | `0x019D84AF`, `0x019D84F2` |
| `+0x56` | 6 | `m_triggered2[6]` — expend-row key edge | `0x019D84C2`, `0x019D84EA` |
| `+0x5C` | 0x14 | `m_slot` — the live slot vector. `_Myproxy@+0x5C`, `_Myfirst@+0x60`, `_Mylast@+0x64`, `_Myend@+0x68`; `size = (_Mylast-_Myfirst)/4` | ctor `0x019DAA31`; `0x019D857A`; `operator[]` `0x004E6900` reads `[ecx+4]`/`[ecx+8]` with `this=+0x5C` |
| `+0x70` | 0x14 | `m_skillIndexToSlot` — `vector<WORD>`, `_Myfirst@+0x74`, `_Mylast@+0x78`; `0xFFFF` = absent | ctor `0x019DAA64`; `0x019D73EE`, `0x019D7547`, `0x019D7751` |
| `+0x84` | 2 × 0x14 | `m_slotSet[2]` — the two saved slot presets | ctor `0x019DAA97`; `0x019D822B`, `0x019D7FCE`, `0x019D8DAD` |
| `+0xAC` | 4 | `m_changeCount` — the `requestChangeSkillSlot` budget counter | `0x019D856B`, `0x019D887B`, setter `0x019D6150` |
| `+0xB0` | 4 | override character (`NULL` ⇒ use `g_myCharacter`); compared against `0x3B3DFB8` | `0x019D80F0`, `0x019D8209`, `0x019D9137` |
| `+0xB4` | 4 | key-input object; `vtable+0x2C(keyDesc)->bool` is "is key down" | `0x019D838D`, `0x019D7A61`, `0x019D8155` |
| `+0xB8` | 1 | one-time-init flag (`init()` only zeroes the key arrays when it is 0, then sets it) | `0x019D8DEF`, `0x019D8E2E`, ctor `0x019DAAE1` (as WORD 0) |
| `+0xB9` | 1 | bool flag | `0x019D8CA9`, `0x019D8CBF`, `0x019D5C00` |
| `+0xBC` | 4 | int, `-1` in ctor, reset to `-1` at `0x019D93B1` | ctor `0x019DAAD1` |
| `+0xC0` | 4 | pointer | `0x019D8EC9`, `0x019DA8FB`, `0x019DA92E` |

Every "vector" in the arrays at `+0x1C` / `+0x84` has stride **`0x14`** with
`_Myfirst` at `+4` and `_Mylast` at `+8` (ctor passes `elemSize = 0x14`, `count = 2`;
`0x019D8DE4` steps `edi += 0x14`).

### `CSkill` fields this unit touches

| offset | how it is read | meaning |
| --- | --- | --- |
| `+0x74` | accessor `0x019AB760` (`lea eax,[ecx+0x74]`), then the 5-way encrypted-read dispatch (`0x027BC0F0/160/1D0/240/390`) selected by `addr & 0xF` | **skill index** — the value put in `m_skillIndexToSlot` and written as the WORD in packet `0x1FD` |
| `+0x100` | same encrypted-read; `0x019AEC50` returns `(value == 1)` | skill kind. **`requestChangeSkillSlot` refuses to place a skill with `!= 0` into an equip slot** (`0x019D85D0`, `0x019D8672`) |
| `+0x9A0` | plain byte, `0x019AB7B0` | "dirty / changed" — gates inclusion in packet `0x1FD` |
| `+0x9B8` | vector of 8-byte encrypted ints; count `0x019ABAD0` = `([0x9C0]-[0x9BC])/8`, element read `0x019B07A0(i)` | per-index values sent in packet `0x1FD`; semantics unconfirmed |
| `+0x60` | default index for `0x019B07A0` | unknown (level-like) |

The encrypted fields are 8 bytes each — key/tag at `+0`, ciphertext at `+4`; the decryptor
variant is chosen by `fieldAddress & 0xF` (that is what the repeated
`and eax,0xf / cmp eax,0xc / jmp table` blocks are, not a switch on data).

---

## 6. `rebuildSkillIndexMap` — `0x019D72A0`

Named by its own literal. Two passes over `m_slot`:

1. `max = max(decrypt(skill->[0x74]))` over all non-null slots; `assert(max <= 0xFFFF)`
   (`int3` at `0x019D73E0`); `resize(m_skillIndexToSlot, max+1)` (`0x019D71B0`) and
   `memset(_Myfirst, 0xFF, count*2)` (`0x019D7409`, `memset` `0x02C62020`).
2. For every non-null slot: `m_skillIndexToSlot[skillIndex] = (WORD)slotIndex`
   (store at `0x019D7751`).

The duplicate check is **conditional**: only when
`g_myCharacter->vtable_0x9DC()` returns a non-null object whose first dword is `0xA`
(`0x019D747E`–`0x019D74B1`) does it test `m_skillIndexToSlot[idx] != 0xFFFF` first and,
on collision, log
`CNRDSkillManager::rebuildSkillIndexMap() 스킬인덱스가 중복되었습니다. 확인부탁드립니다`
at line `0x507` = 1287 (`0x019D7647`). Outside that case (`0x019D7691`) it overwrites silently.

**Server consequence:** the client assumes skill index → slot is a *function*. If a server
hands the same skill index to two slots, one mapping is lost silently for most characters.

---

## 7. `proc()` — `0x019D8250`, per-frame quickslot input

Literal `CNRDSkillManager::proc` is bound here. Called from `CNRDStage::proc`
(`0x019A4EC0`), `IRDAICharacter::procAppend` (`0x01829F50`),
`CNRDVirtualCharacter::procAppend` (`0x017A5880`).

* One-time init behind bit 1 of `0x3B3DE60`, constructing a named object at `0x3B3DE18`
  from the literal `"CNRDSkillManager::proc"`; entered/left per call via `0x027AB200` /
  `0x027AA8C0` (RAII scope — profiler or lock).
* Returns immediately if `this+0xB4` (key input object) is NULL.
* **Loop 1**, 6 iterations, key-descriptor table `0x3A5D0A8` stride 0x10 (or global input
  manager `0x3BAB0B0` + table `0x3A5CFE8` when `this+0xB0` has no `vtable_0x3C`):
  writes `m_prevPressed[i]` (`+0x44`) and `m_triggered[i]` (`+0x4A`).
* **Loop 2**, 6 iterations, table `0x3A5D048` stride 0x10, replay key ids `0xD8 + i`:
  writes `+0x50` and `+0x56`.
* Edge rule (`0x019D840D`–`0x019D8441`): a slot is triggered only when it was *not*
  pressed last frame, is pressed now, **and no slot has been triggered yet this call** —
  the `[ebp-0x32]` "already fired" flag is shared across both loops, so **at most one
  quickslot key edge is accepted per frame**, across all 12 keys.
* Replay override: `CNRDReplayManager` (`0x01A6FCA0`, `0x01A6D7B0`, `0x01A6DEF0`,
  `0x01A70040`) can both *supply* the pressed state during playback and *record* it during
  capture. A chat/UI-focus object at `0x3A5C9B8` (`0x02300730`) forces every key to
  not-pressed.

---

## 8. Public surface (functions called from outside the module)

| VA | signature I recovered | external callers |
| --- | --- | --- |
| `0x019D8540` | `bool requestChangeSkillSlot(slot1, slot2, reason, maxChanges)` — §2 | `CNRDBaseInterface::onLeftButtonUp`, `CNRDSkillShop::onCheckSkillDrag`, `CNComboSlotGridImpl::setDragSourceSkillSlot`, `CNComboSlotGridImpl::addSkill`, `CNRDSkillInventoryWindow::onDragEvent`, `CNGameCore::setDebugTestValue` |
| `0x019D8940` | `bool moveSkillSlot(a, b, c, reason)` — two chained requests (§2) | 3 callers |
| `0x019D6430` | `void commitChangedSkills()` — packet `0x1FD` (§3) | `CNRDSkillShop::onClose`, `CNComboGridController::onEvent` |
| `0x019D8250` | `void proc()` — §7 | `CNRDStage::proc`, `IRDAICharacter::procAppend`, `CNRDVirtualCharacter::procAppend` |
| `0x019D8200` | `void reloadFromCharacterPreset()` — `n = char->[0x54F0]`; `m_slot = m_slotSet[n]` (`0x14EA890`), then `rebuildSkillIndexMap` | 8 callers incl. `CNGameCore::setDebugTestValue` |
| `0x019D80E0` | `void switchPreset(int n)` — unregisters every skill of preset `!n` from the key object (`0x019B38F0`), copies `m_slotSet[n]` into `m_slot`, registers each (`0x019B3830`), rebuilds. When `this+0xB0` is a *foreign* character it only copies + rebuilds. | `IRDCharacter.cpp` ×2 (`0x0183DDA0`, `0x01885340`) |
| `0x019D7BE0` | `void getQuickSlotSkills(vector<CSkill*>& out, bool expendRowFirst)` — emits `[0,N)` and `[0xC6,0xC6+N)`, order flipped by the flag, `N` = 6 or 5 | `CNRDSkillShop::updateQuickSlotSkill`, `::updateCoolTimeAlarmEnable`, `::isAutoNotifiableSkill`, `IRDCharacter.cpp`, +3 |
| `0x019D7AF0` | `void collectSkills(bool want, bool includeEquipRows, vector<CSkill*>& out)` — scans `[0,0xCC)` when `includeEquipRows`, else `[6,0xC6)`; keeps a skill when `want == (skill->field_0x100 != 1)` (`0x019AEC50`) | `LearnedSkillWindow::updateConvSkillGrid`, `LearnedSkillWindow::updateLearnedSkillGrid` |
| `0x019D7F90` | `vector<CSkill*>& getSlotVector(vector& out)` — copy of `m_slot` | `CNRDSkillRefundWindow::getMySkillList`, `CNRDPVPPracticeCombo::onCombo` |
| `0x019D7FC0` | `vector<CSkill*>& getPresetVector(vector& out, int preset)` — copy of `m_slotSet[preset]` | `TimeTrainingManager::sendMySkillInfo`, `0x00BE9740` |
| `0x019D7A20` | `void clearAll()` — for both presets, unregister and null every entry, null `m_slot`, rebuild | `IRDCharacter.cpp` (`0x0184E7B0`), `0x01821BF0` |
| `0x019D72A0` | `void rebuildSkillIndexMap()` — §6 | `0x019D8D40` (`init`), plus internal |
| `0x019D5DD0` | `CSkill* getSkill(int slot)` — bounds-checked `m_slot[slot]`, NULL when out of range | **33 callers** — the class's hottest accessor |
| `0x019D5F50` | `bool isExpendQuickEmpty()` — static; true iff slots 198..203 are all NULL | — |
| `0x019D5FB0` | `int getEmptyQuickSlot()` — static; first empty in 198..203, else first empty in 0..5, else `-1` | — |
| `0x019D5AC0` | `bool isEquipSlot(int)` — the `{0..5, 198..203}` table | — |
| `0x019D5BC0` | `bool isLegitUnequipReason(int)` — `reason == 4 \|\| reason == 7` | — |

### Instance access

`0x0164C5D0(const char* file, int line)` is the singleton getter. The two arguments are
ignored in this build (`cdecl`, callers do `add esp, 8`) — they are debug provenance.

```c
CNRDSkillManager* getSkillManager(file, line) {
    if (g_myCharacter /*0x3B3DFB8*/) return g_myCharacter->vtable_0xC04();   // per-character
    if (!g_fallback /*0x3A5C8F0*/)
        g_fallback = (new(0xC4) CNRDSkillManager)(0x200);                    // 512 slots
    return g_fallback;
}
```

So **the manager is owned by the character object** (`vtable+0xC04`), not global; the
`0x3A5C8F0` instance is only a null-character fallback.

### Globals used

| VA | role |
| --- | --- |
| `0x3B3DFB8` | `g_myCharacter` (the locally controlled character) |
| `0x3A5C980` | `CNUserManager` singleton |
| `0x3B7CEE4` | `CNGameSocket` singleton (`0x0274CA50` returns it) |
| `0x3A5C8F0` | fallback `CNRDSkillManager` |
| `0x3A5AFCC/0x3A5AFD0/0x3A5AFD4` | encrypted bool + its lock — gates local prediction of the slot swap |
| `0x3A5CFE8`, `0x3A5D048`, `0x3A5D0A8` | key-descriptor tables, stride `0x10`, 6 entries each |
| `0x3B3DE18` / `0x3B3DE60` | the `"CNRDSkillManager::proc"` scope object and its init-once bit |

### `CNGameSocket` primitives (TU 5) confirmed here

`0x0274CA50` get-socket · `0x0274D1E0` `MakePacket(WORD,BYTE)` ·
`0x0274D2E0` WriteBYTE · `0x0274D310` WriteWORD · `0x0274D340` WriteDWORD ·
`0x0274D370` Write 8 bytes · `0x0274D3A0` Write length-prefixed buffer (DWORD len + bytes) ·
`0x0274E6D0` `SendPacket`. Each writer adds its width to the size counter at
`socket + 0x2BCC2C` and refuses when `socket->byte_0x8 == 0` (returns `0x80000054`).

---

## 9. Not established

* The meaning of the WORD array in packet `0x1FD` (§3) — element count and encryption
  are proven, semantics are not.
* Whether `field_0x100 == 0` means "active skill" specifically. Proven: only `0` may
  enter an equip slot, and `0x019AEC50` singles out `1`.
* Why slots `138..149` are refused as a swap destination.
* What `vtable_0x5E0() == 0xA` / `vtable_0x9DC()[0] == 0xA` identify (both reduce or
  alter behaviour for one character kind).
* Who resets `m_changeCount` in the normal UI flow (`0x019D6150`'s two callers
  `0x01119280`, `0x0160F4D0` were not analysed).
* `0x019D90F0` — same object, `ret 8`, **480 callers**, the module's hottest entry point.
  Beyond the briefing span and not analysed.
* No parser functions: this unit reads nothing from the inbound stream
  (`parsers = 0` for all 10 briefing functions). Whatever the server replies to `0x1C`
  and `0x1FD` is decoded elsewhere.
