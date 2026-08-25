# Unit 016 — `DNFClient\RDAR\CNRDInterfaceManager.cpp`

Briefing span `0x02304C70`–`0x02312480` (88 funcs, 0 parsers). Image base `0x00400000`.

## Verdict in one line

This is the client's **single global UI window registry / dispatcher**: it owns the 14
full-screen `IRDWindow`s and the 908-entry `IRDPopupWindow` table, and it implements
`openWindow` / `openPopupWindow` / `closePopupWindow` / per-frame `proc` / `checkMouseTask`
/ `onChangeModule`, plus one delayed "leave the game" command that is the unit's only
outbound packet.

It is **not** a packet handler (0 parser calls, confirmed) and it is **not** an
`IRDBaseInterface`/widget implementation (that is TU 11, `CNRDBaseInterface.cpp`).

---

## 1. The briefing's span is WRONG — too narrow at both ends

Evidence, not inference. `CNRDInterfaceManager.cpp` owns a **contiguous encrypted literal
pool `0x035BB0A8` – `0x035BB88C`** (the block below it, `0x035BA030`–`0x035BAFAC`, is the
UDP packet-name table used only by `f=0x02DF3903`; the block above it, from `0x035BB8A8`,
is used only by `0x0231F480`/`0x0231FCE0`). Every function that reads a literal from
`0x035BB0A8`–`0x035BB88C`:

| func | literal | in briefing span? |
| --- | --- | --- |
| `0x023003E0` | `CNRDInterfaceManager::checkMouseTask` @`0x035BB0A8` | **no — below `lo`** |
| `0x02301600` | `TAB_SELECT` @`0x035BB10C` | **no — below `lo`** |
| `0x02303710` | `[window]` @`0x035BB144` | **no — below `lo`** |
| `0x02304C70` … `0x02312480` | (17 funcs) | yes |
| `0x02319670` | `CNRDInterfaceManager::checkMouseTask`, `AVATA_CONVERT_READY` | **no — above `hi`** |
| `0x0231B6D0` | `CNRDInterfaceManager::proc` @`0x035BB858` | **no — above `hi`** |

Second, independent proof: functions *below* `lo` dereference the exact same object layout
as functions *inside* it — `0x023009B0` reads `[esi+edi*4+0x68]`, `0x02302700` reads
`[edi+eax*4+0xA0]`, `0x02301820`/`0x02301E60`/`0x02304790` all index `[ecx+20*type+0xFC]`.
Those are `CNRDInterfaceManager` methods.

Third: `__LINE__` values embedded in the log calls run 4567 (`openWindow` @`0x02307A50`),
6041 (`openPopupWindow` @`0x02304C70`), 13376–13385 (`checkMouseTask` @`0x023003E0`),
13411–13412 (`releaseWaiting` @`0x02305520`), 13423 (`requestUseCoinFromActionCommand`
@`0x023055E0`). Line order and VA order disagree, so the module is COMDAT-scattered and an
anchor-bounded interval cannot capture it.

**Real module ≈ `0x023003E0` – `0x0231CBC0`** (~117 KB, ~330 functions), of which the
briefing covers the middle 54 KB. Everything below is stated with the VA it came from, so
it survives that correction.

Also: `functions.csv` says `0x02312480` is 29168 bytes (ends `0x02319670`); `peek.py`
reports 16384. The switch table decode (§5) reaches `0x02318C8F`, so **29168 is correct**
and peek's length is truncated.

---

## 2. Object layout — `CNRDInterfaceManager`

Global singleton pointer: **`0x03A5C9B8`** (`mov ecx, dword ptr [0x3A5C9B8]` before a
thiscall to `0x02304C70` at `0x0230536B`, `0x02305851`, `0x02305A93`, `0x023058B0`, …).

| offset | type | proof |
| --- | --- | --- |
| `+0x0C` | ptr (renderer/HUD) | `0x02307C5B mov ecx,[esi+0xc]; call 0x17639F0` |
| `+0x24 + 8*i` | array of 8-byte entries | `0x02305C8B lea edx,[esi+eax*8+0x24]` |
| `+0x48` | sub-manager ptr | `0x0230AF26 mov ecx,[esi+0x48]` |
| `+0x4C` | sub-manager ptr (HUD/base-interface) | `0x0230567E`, `0x0230AEF4`, `0x0230A000` |
| `+0x60` | ptr, `vtbl+0x40`/`+0x08` called | `0x02307B6E`, `0x0230AFD5` |
| `+0x68 + 4*t`, t=0..13 | `IRDWindow* m_window[14]` | `0x023009D9 mov eax,[esi+edi*4+0x68]`; loop `cmp edi,0xE; jl` at `0x02311766` and `0x02307CE7` |
| `+0xA0 + 4*g` | current window **type** per group (`0xE` = none) | `0x02307AB6 cmp [esi+eax*4+0xa0],ebx` / `0x02307C19 mov [esi+eax*4+0xa0],ebx`; `0x0230272D` |
| `+0xA8 + t`, t=0..13 | `bool m_windowShown[14]` | `0x02307C34 mov byte [ebx+esi+0xa8],1`; read in the 14-iteration loops at `0x02307CC0` and `0x02311742` |
| `+0xFC + 20*t`, t=0..907 | `std::vector<IRDPopupWindow*> m_popupOpen[908]` — **active instances** | `lea ecx,[ebx+ebx*4]; lea ecx,[esi+ecx*4]` + `0xFC` at `0x02304CCF`; `0x02301E6C`, `0x02301830`, `0x023047AB`, `0x02308569` |
| `+0x47EC + 20*t` | `std::vector<IRDPopupWindow*> m_popupPool[908]` — **recycled/free instances** | `0x02304E08 mov ecx,[eax+0x47f0]`; `0x02304FBA add ecx,0x47ec; call push_back`; `0x02306126` |
| `+0x8EDC` | `std::vector<IRDPopupWindow*>` — **z-order / open-order list** | `0x02304D18`, `0x02304D2C`, `0x02304DB7` (find + insert), `0x0230477F`, `0x0230485C`, `0x02304932` |
| `+0x8F50` | bool | `0x0231B7A5 cmp byte [esi+0x8f50],0` |
| `+0x8F64/+0x8F68/+0x8F90/+0x8F94` | sub-object ptrs (state queries) | `0x023056BD`–`0x023056F0` |
| `+0x9064/+0x9068/+0x906C` | 3 ints — drag/pick context | `0x02308411`/`0x0230841A`/`0x02308424` |
| `+0x9070/+0x9074` | `shared_ptr` (obj, ctrl-block) — dragged object | `0x02308441`/`0x02308447`, released with `lock xadd` |
| `+0x908C`, `+0x9090` | cached window / cached `dynamic_cast` result | `0x02306A03`, `0x02306A2D`, `0x02306A43` |
| `+0x90B4` | sub-object | `0x02305692` |
| `+0x90C4` | string-ish member | `0x0230C22E add ecx,0x90c4; call 0x450DB0` |
| `+0x91A8` | `int m_uiEnabled` — **hard gate on all window opening** | set `0x023012B6`, tested `== 1` at `0x023012C2`, gate at `0x02304C9D` and `0x02307A7A`, set to 1 at `0x0231DC58` |
| `+0x91AC` | `int m_reservedCommand` (0 none / 1 / 2 / 3) | `0x02305884`, `0x023058B5`, `0x023059C3`, `0x02305B93` |
| `+0x91B0` | timer object (elapsed `0x027C3660`, reset `0x027C3410`, start `0x027C3B60`) | `0x023059D1`, `0x0230587E` |
| `+0x91CC` | `int m_reservedDelayMs` | `0x02305986`, `0x023059DC` |
| `+0x925C` | container of loaded resources | `0x02306EAA lea esi,[ecx+0x925c]` |

**The `908` is hard evidence, not a guess.** Three independent confirmations:
`0x02301826 cmp eax, 0x38b; ja` (bounds check), `0x0230AED3 cmp edi, 0x38c; jl` (close-all
loop), `0x023BD937 cmp esi, 0x38b; ja` (factory jump table `0x023CA480`). And
`(0x47EC-0xFC)/20 == (0x8EDC-0x47EC)/20 == 908`.

Sanity check of the stride formula: `0x02305FA3 lea eax,[ecx+0x494]` then, if empty,
`openPopupWindow(0x2E,…)` — and `0xFC + 20*0x2E == 0x494`. Likewise `0x023057C4 lea
edi,[ecx+0x9e4]` pairs with `openPopupWindow(0x72,…)` and `0xFC + 20*0x72 == 0x9E4`.
So constants like `+0x494`/`+0x9E4` in this module are **not separate members** — they are
`m_popupOpen[type]` with the index folded in.

Each 20-byte slot: `+0` `_Myproxy`, `+4` `_Myfirst`, `+8` `_Mylast`, `+0xC` `_Myend`,
`+0x10` unidentified dword. Iterators are 12 bytes (`_Myproxy` at `+0`, `_Ptr` at `+8`) —
this is an `_ITERATOR_DEBUG_LEVEL != 0` build.

Related globals:
- `0x03A5CA0E` — byte flag; when set, `0x023006E0` blocks popup types
  `{0x0A,0x0B,0x0C,0x27,0x28,0x47,0x113,0x114,0x115,0x234}` from opening (`0x02304CAF`).
- `0x03B72648` — map `popupType -> maxInstances`, read by `0x023B8A60` (returns
  `[node+0x10]`, default **1**).
- `0x03B3E190` / `0x03B3E194` — SafeStorage-protected int used as the exit delay seconds.
- `0x03B7CEE4` — `CNGameSocket*` (returned by `0x0274CA50`).

---

## 3. Public surface (called from outside the unit)

| VA | in-deg | signature / behaviour |
| --- | ---: | --- |
| `0x02304C70` | 1791 | **`IRDPopupWindow* openPopupWindow(int type, X p1, Y p2)`** — see §4 |
| `0x02304790` | 691 | **`closePopupWindow(int type, int index = -1, bool force = 0)`**; `type == 0x38C` is a no-op sentinel |
| `0x02301E60` | 500 | **`IRDPopupWindow* getPopupWindow(int type, unsigned idx)`** → `m_popupOpen[type][idx]`, NULL if `idx >= size` |
| `0x02301820` | 465 | **`bool isPopupWindowOpen(int type)`** → `type <= 0x38B && !m_popupOpen[type].empty()` |
| `0x023060F0` | 322 | **`IRDPopupWindow* getOrCreatePopupWindow(int type)`** — active[0] → pool[0] → `createPopupWindow(type,this)` then park in pool |
| `0x023053C0` | 155 | wrapper that calls `openPopupWindow` (+ `0x028C14C0` string-table lookup) |
| `0x02304230` | 98 | iterator-construction helper on the z-order vector |
| `0x02305350` | 37 | `showSystemMessagePopup(int kind, msg, x)` → `openPopupWindow(0x209, msg, 0)` on the singleton; kinds 0/1 popup, kind 2 falls through to `[singleton+0x4C]`-> `0x01A60850` |
| `0x02306CD0` | 39 | `getOrCreatePopupWindow(0x1AF)` + `__RTDynamicCast` |
| `0x02305520` | 26 | `releaseWaiting(cursorId)` — `SetCursorPos` path via `0x027ED530`, resets the waiting cursor |
| `0x02305FA0` | 23 | `getOrOpenPopup(0x2E, …)` |
| `0x023083D0` | 19 | **`beginPick(int pickKind, int a, int b, int c, shared_ptr obj, …)`** — stores `+0x9064/68/6C` and `+0x9070/74`; plays sound `ITEM_PICK` (kind 1) / `ICON_PICK` (kind 2) |
| `0x02308560` | 15 | **`togglePopupWindow(type,p1,p2)`** — open if `m_popupOpen[type]` empty, else `closePopupWindow(type,-1,0)` |
| `0x02307A50` | 13 | **`bool openWindow(int windowType, bool toggle, bool notify)`** — see §4 |
| `0x02309FD0` | 11 | HUD refresh through `[this+0x4C]->vtbl[0x7C]` |
| `0x02307330` | 11 | z-order vector `erase`/`insert` helper |
| `0x023057B0` | 10 | `getOrOpen(type 0x72)` with `__RTDynamicCast` to RTTI `0x03980A58`; closes 0x72 if the cast fails |
| `0x02312480` | 54 | **`tryCheckRequestItem(...)`** — see §5 |
| `0x02308540` | 3 | `openPopupWindow(0x20E, a, b)` |
| `0x0230C220` | 6 | string accessor on `+0x90C4` |
| `0x0230AE90` | 1 (from `0x01644210`) | **`onChangeModule(int oldModule, int newModule)`** — see §6 |
| `0x0231B6D0` | 3 | **`proc()`** — per-frame driver (outside briefing span) |

Internal but load-bearing: `0x023009B0` `getWindow(int type)` (lazy `new` of the 14
`IRDWindow`s via jump table `0x02300B80`, sizes `0x48C`, `0x18C`, `0x1C0`, …),
`0x02302700` `closeWindowInGroup(group, newType)`, `0x02304890` `closeAllPopups(bool)`
(skips types `0x70` and `0x38C`), `0x02307C90` `checkMouseTask()`, `0x02311710`
per-frame window+popup walk, `0x02305870` `reserveCommand()`.

---

## 4. The two open paths (what a re-implementer must mirror)

### `openPopupWindow(int type, X p1, Y p2)` — `0x02304C70`, `ret 0xC`, thiscall

1. `if (m_uiEnabled == 0) return NULL;` (`0x02304C9D`)
2. `if (g_flag@0x03A5CA0E && isBlockedType(type)) return NULL;` — `0x023006E0`, list in §2
3. `max = maxInstances(type)` from map `0x03B72648`, default 1 (`0x02304CCA`)
4. `if (max > m_popupOpen[type].size())` → try to take one from `m_popupPool[type]`
   (`0x02304E05`); otherwise reuse `m_popupOpen[type][0]` (`0x02304CFE`)
5. if still none → `win = createPopupWindow(type, this)` (`0x02304EFB` → `0x023BD910`,
   908-entry jump table at `0x023CA480`)
6. `if (type == 0x8A && !g@0x3A42BB8->0x143EEE0()) { g->0x143EB30(p2,p1); return 0; }`
   (`0x02304DC5`–`0x02304DEF`) — a hard special case for popup type `0x8A`
7. `category = popupCategory(type)` (`0x023B4060`): `type<=0 → -1`, `<=0x36 → 0`,
   `<=0xA7 → 1`, `<=0x1FF → 2`, else `3`; `type == 0x2C5 → 4`.
   Categories **2 and 3** additionally run `0x027C9A00` on the window's name
   (`vtbl+0x7C`) at `0x02304F8F`.
8. **`ok = win->vtbl[0x18](p1, p2)`** (`0x02304FAE`) — the window's `onOpen`
   - `ok == false` → push `win` back into `m_popupPool[type]` (`0x02304FBA`) and **return NULL**
   - `ok == true` → log `"Open IRDPopupWindow Type : %d\r\n"` (file `0x035BB170`, func
     `0x035BB1F0`, line **6041**) and push into `m_popupOpen[type]` (`0x02305054`), then
     insert into the z-order vector `+0x8EDC` if not already present (`0x02304DB7`)
9. return `win`

### `openWindow(int windowType, bool toggle, bool notify)` — `0x02307A50`, `ret 0xC`

1. `if (m_uiEnabled == 0 || windowType == 0xE) return;` — **`0xE` is the "no window" sentinel**
2. `win = getWindow(windowType)` (`0x023009B0`); NULL → return
3. `group = win->vtbl[0x68]()` (`0x02307AB1`)
4. `if (m_currentWindow[group] == windowType)`: if `toggle` → `closeWindowInGroup(group, 0xE)` and return
5. `if (!win->vtbl[0x40]()) return;` (window refuses)
6. log `"Open IRDWindow Type : %d\r\n"` — line **4567**
7. `closeWindowInGroup(group, windowType)`; then **force-close popups `0x24A`, `0x128`, `0x18A`** (`0x02307B42/B52/B62`)
8. hand the window's name (`vtbl+0x64`) to `[this+0x60]->vtbl[0x40]`
9. `m_currentWindow[group] = windowType; m_windowShown[windowType] = 1;` (`0x02307C19`, `0x02307C34`)
10. `win->0x005B6610(windowType)`, play sound **`WINDOW_SHOW`**, and if `notify == 1` call `0x02301200(win)`

### Per-frame / input walks

`checkMouseTask()` `0x02307C90` and `proc`'s helper `0x02311710` share the same two-stage
walk, and it defines the **dispatch order** exactly:

```
for (t = 0; t < 14; ++t)                       // 0x02307CC0 / 0x02311742
    if (m_windowShown[t] && m_window[t])
        if (m_window[t]->vtbl[0x5C]())  break; // 0x50 in the proc variant
for (type = 0; type <= 0x38B; ++type)          // 0x02307CF4 / 0x02311776
    for (i = 0; i < m_popupOpen[type].size(); ++i)
        if (m_popupOpen[type][i]->vtbl[0x40]()) …
```

First consumer wins; iteration is by **ascending window type then ascending index**, i.e.
priority is baked into the type-id numbering, not into a runtime z-order.

---

## 5. `tryCheckRequestItem` — `0x02312480` (in-deg 54)

`thiscall(int requestType /*[ebp+8]*/, IRDItem* item /*[ebp+0xC]*/, ?, int* pOutMsgId
/*[ebp+0x14]*/, ? /*[ebp+0x18]*/, ? /*[ebp+0x1C]*/)`.

Client-side precondition gate: given a UI request type and a target item, it validates and
writes a **message-table id** into `*pOutMsgId` on failure (`0x02312522` writes `0x192`,
`0x0231254A` writes `0x193`, `0x023125D7` writes `0x194`, `0x023125FF` writes `0x8E39`).

Switch: `requestType - 5`, valid `0..0x5F` → request types **5..100**. Byte index table at
`0x02318E0C`, jump table at `0x02318D2C`. Decoded: **55 types get a dedicated check**; the
other **41** (`7,8,9,10,13,14,15,16,17,19,20,21,26,31,32,33,34,35,37,38,40,41,42,43,44,48,
50,51,52,54,55,59,60,75,76,78,80,90,92,93,95`) fall through to the default (allowed).

Call-site constants identify the numbering, e.g. `0x35` = BlueMarble
(`UI/BlueMarble/BlueMarbleInfoWindow.xui` @`0x014FBBB0`), `0x49` = avatar-change-option
(`0x014F5370`), `0x4F/0x55/0x59/0x5B/0x5E` = recipe windows (`RECIPE_OPEN`, `0x010124B0`,
`0x00F68310`), `0x56` = dissolve item (`DISSOLVE_ITEM`, `0x00995AA0`), `0x2E/0x2F/0x64` =
ItemTool window (`0x01511D90`, `0x01515200`), `0x3D/0x62` = inventory / `Interface/ItemSelect.img`
(`0x00EEDAF0`), `0x51` = ItemInfoWindow (`0x02460BA0`).

It also carries `"[creature]"` (`0x032CA18C`) and `"ERROR> equipmentScript == NULL %d"`
(`0x035BB808`), and reads the item's data through `vtbl[0x144]` / `vtbl[0x150]` behind the
alignment-dispatch decryptors `0x027BC0F0/160/1D0/240/390` — the same SafeStorage family
used for the exit delay. **Server relevance: this enumerates what the client refuses
locally, so those refusal paths are not constructible on the wire.**

---

## 6. Module state machine — `onChangeModule` `0x0230AE90`

Called only from `0x0164486D` inside `0x01644210` (GameCoreSub). Byte-level proof of arg
order: `85 c9 74 07 56 53 e8 …` = `test ecx,ecx; je +7; push esi; push ebx; call`, and in
`0x01644210` `ebx = [ebp+8]` (old) / `esi = [ebp+0xC]` (new; `cmp esi,1` at `0x01644277`
gates the *entering-town* work). So **`onChangeModule(int oldModule, int newModule)`**.

Behaviour:
- `if (newModule == 5 /*SELECT_CHANNEL*/)` → `for (t=0; t<0x38C; ++t) closePopupWindow(t,-1,0)` (`0x0230AEC7`)
- `0x012D3000(newModule)` — global module-changed broadcast
- `if (oldModule == 1 /*TOWN*/ || oldModule == 3 /*MAIN_GAME*/)` → `[this+0x4C]->0x01A5AB80()`
- `if (oldModule == 4 /*PVP*/ || oldModule == 6 /*WARROOM*/)` → `[this+0x4C]->0x01A5A870()`
- `if (newModule != 0 && newModule != 5)` → rebuild the UI surfaces via `[this+0x48]`, `[this+0x4C]`, `[this+0x60]`

**`MODULE_TYPE` enum**, recovered from the `getModuleName` jump table at `0x0164B760`
(index `m+1`, `0x0164B5E0`), each case pushing one encrypted literal:

| id | name | id | name |
| ---: | --- | ---: | --- |
| -1 | `MODULE_TYPE_NONE` | 11 | `MODULE_TYPE_DESPAIR_TOWER` |
| 0 | `MODULE_TYPE_SELECT_CHARACTER` | 12 | `MODULE_TYPE_SCREAM_TOWER` |
| 1 | `MODULE_TYPE_TOWN` | 13 | `MODULE_TYPE_BOSS_TOWER` |
| 2 | `MODULE_TYPE_SELECT_DUNGEON` | 14 | `MODULE_TYPE_ADVANCE_ALTAR` |
| 3 | `MODULE_TYPE_MAIN_GAME` | 15 | `MODULE_TYPE_ZOMBIE_MODE` |
| 4 | `MODULE_TYPE_PVP` | 16 | `MODULE_TYPE_BREAKING_OBJECT_MODE` |
| 5 | `MODULE_TYPE_SELECT_CHANNEL` | 17 | `MODULE_TYPE_LOAD` |
| 6 | `MODULE_TYPE_WARROOM` | 18 | `MODULE_TYPE_TOURNAMENT` |
| 7 | `MODULE_TYPE_LOGIN` | 19 | `MODULE_TYPE_SPECIAL_DUNGEON` |
| 8 | `MODULE_TYPE_ASSAULT` | 20 | `MODULE_TYPE_TERRITORY_COMBAT` |
| 9 | `MODULE_TYPE_DEAD_TOWER` | 21 | `MODULE_TYPE_MAX` |
| 10 | `MODULE_TYPE_BLOOD_SYSTEM` | | |

(This table lives in TU 65 `GameCoreSub.cpp`, not here — but this unit consumes it and it
is the only place I found it decoded.)

---

## 7. The one outbound packet: reserved-exit

`reserveCommand(int code, int delayMsOverride)` — `0x02305870`, `ret 8`:

- `code == 0` → cancel: `m_reservedCommand = 0; timer.reset(); m_reservedDelayMs = 0`
- else → `openPopupWindow(0x170, 0, 0)` on the singleton (the countdown popup),
  `m_reservedCommand = code`, timer reset + start, then
  `D = SafeStorageLoad(0x03B3E190/0x03B3E194)` (alignment-dispatch decrypt,
  `0x027BC0F0/160/1D0/240/390`), `V = 0x027C2E60()`, and
  **`m_reservedDelayMs = ((V % 10) + (D % 10)) * 1000`** (`0x02305954`–`0x02305986`;
  the `0x66666667`/`sar 2` pair is a signed `/10`).
  `delayMsOverride != 0` replaces that outright (`0x02305990`).

Observed callers: `0x013A8AB0` (`CNRDMainHudMenuManager.cpp`,
`MainHudSpace::behaviorCHANNEL_SELECT_WINDOW…`, `"confirm channel select window"`) with
codes **1, 2, 3**; `0x00A05B00` and `0x013A6C70` with **3**; `0x0253DDB0`
(`CNRDRetryConnectChannelConfirmWindow::proc`) with **0** (cancel).

`fireReservedCommand()` — `0x023059C0`, called from `proc` `0x0231B6D0` — fires once
`timer.elapsed() > m_reservedDelayMs` (`0x023059DC`) and always clears
`m_reservedCommand = 0` afterwards:

| code | action |
| ---: | --- |
| 1 | if `m_currentWindow[0] == 3` → refuse with popup `0x209` + message id `0x1139B`; else VMP-protected exit `0x051C6D67` on my character |
| **2** | **`MakePacket(0x0007, 0); SendPacket();`** on `CNGameSocket` — `0x02305B81`–`0x02305B8D`; first closes popup `0x62` via `getPopupWindow(0x62,0)->vtbl[0x1C]()` |
| 3 | `0x03B84690->0x012D0930(0)`, then VMP-protected `0x05238824` on my character |

Identification of the two socket calls comes from TU 5's own literals:
`0x0274D1E0` = `CNGameSocket::MakePacket` (`"!!!이미 패킷을 만들던 중입니다!!! try make packet is %d"`),
`0x0274E6D0` = the sender (`" [2-> COMMAND PACKET] Packet : [%d], Seq : %d, Size : %d, CheckSum : %d"`).
`0x0274CA50` is just `return g_pGameSocket /*0x03B7CEE4*/`.

Command **`0x0007` = `ENUM_CMDPACKET_EXIT`**: an image-wide scan of the 1411 `MakePacket`
call sites finds 10 with `cmd == 7`; one of them (`0x0110D944`) sits inside
`GameCoreNetwork_CMD_Etc.cpp`'s function carrying the literals
`CMDFUNC_ENUM_CMDPACKET_EXIT` and `"ENUM_CMDPACKET_EXIT ok"`, and another
(`0x013A909D`) sits in the `"confirm channel select window"` handler.
**Body is empty** — `MakePacket(7,0)` immediately followed by the send, no field writes.

---

## 8. Smaller findings

- `0x023065C0` loads the Global-Navigation extra-feature sprite packs through the sprite
  manager at `0x03BAB2D8`: `Interface2/Global_Navigation/ExtraFeature/ExtraFeature.img`
  then `…/ExtraFeature_cn.img` (CN override loaded second).
- `0x02306E80` builds a path with the `ClientOnly/` prefix into the container at `+0x925C`.
- `0x02307C90` also owns the click feedback: `CLICK_BUTTON1`, `CLICK_MOVE`, and the
  destination marker `Common/Etc/Animation/MoveDestinationMark.ani`.
- `0x0230D5D0` plays the "got a notice" animations
  `UI/Event/Bingo/Animation/get_notice_bingo_chn.ani` and
  `UI/Event/chn_PlayWithFriend/Animation/get_notice_chn_PlayWithFriend.ani`, and references
  `CH_BOOK_01` (also in `0x0230F220`).
- `0x023055E0` `requestUseCoinFromActionCommand` (line 13423) is **a client-side gate only**:
  a chain of ~8 state checks, then either `openPopupWindow(0x262, msgId)` with msg
  `0x11E14` or `0x1E09`, or tail-jump to `0x02301EE0` (the actual request). It does not
  itself touch the socket.
- **RTTI class names are unrecoverable in this image** — every `TypeDescriptor` name is
  rewritten to `"." + <u16 index>` (e.g. `0x0395E7A0` → `.\x18`, `0x0397B1D0` → `.\x8dd`).
  `__RTDynamicCast` (`0x02C6386A`) is used heavily here, but the target classes cannot be
  named from RTTI.

## 9. Unknowns (stated as unknown)

- The `+0x10` dword in each 20-byte popup-table slot.
- How many groups the `+0xA0` array has (2 fits between `0xA0` and `0xA8`, but I did not
  find a write with a group index > 1).
- What codes 1 and 3 of the reserved command mean precisely (the two VMP-protected targets
  `0x051C6D67` and `0x05238824` are in `.vmp1` and were not analysed).
- The meaning of popup type ids beyond the ones named by call sites; the full 908-entry
  factory table at `0x023CA480` was not decoded.
