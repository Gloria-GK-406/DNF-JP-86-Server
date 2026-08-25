# Unit 011 — `DNFClient\RDAR\CNRDBaseInterface.cpp`

Briefing span: `0x01335D40 – 0x01367F70` (266 functions, 0 parsers).
**Corrected span (see §0): `0x0132FC80 – ~0x01375500`** (482 functions, still 0 parsers).

---

## 0. The briefing's span is too NARROW at both ends

The span was cut at the three `__FILE__` anchors, which are the only three functions
in the module that expand an assert/report macro carrying the source path:

```
func-source-file.csv:  0x01335D40, 0x0135CF20, 0x01367F70  ->  CNRDBaseInterface.cpp
```

But the module also carries a *profiling-scope* macro that emits the fully qualified
method name as an encrypted literal. `literals.csv` has **46 distinct
`CNRDBaseInterface::<method>` name literals**, and they run from **0x0132FC80** to
**0x01374EF0** — 6 methods below the span floor and 6 above the ceiling:

| below floor | above ceiling |
| --- | --- |
| `0x0132FC80 procCheckQuickSlotHotkey` | `0x0136C5F0 checkEnemyActiveStatusPopup` |
| `0x01330BD0 isEmptyExSkillSlot` | `0x0136EBB0 drawBuffIcon` |
| `0x013340D0 onRightButtonUp` | `0x0136F4F0 drawPartyBuffIcon` |
| | `0x013727D0 drawBuffIcons` |
| | `0x01373050 proc` |
| | `0x01373B00 drawMainControl` (literal is `drawMainControl#sp표시, 피로게이지`) |
| | `0x01374EF0 DrawProc` |

`tu-index.csv` assigns nothing else to `0x0132FC80–0x01335D40` or
`0x01367F70–0x0137FA90` (next claimed TU is `CNRDConfigFile.cpp` at `0x0137FA90`),
so nothing is being stolen from a neighbour. Everything below is against the
corrected span.

Two more scale facts: the `report()` call in `importEventNoticeScript` passes
**line 0x56F5 = 22261** (`0x013683B0 push 0x56f5`), and `procHotKey` passes
lines 0x4971 / 0x498D (18801 / 18829). The file is >22 000 lines.

---

## 1. What this unit is

`CNRDBaseInterface` is **the in-game HUD**: the always-on overlay drawn on top of
town and dungeon play. It owns the quick-slot bars, the minimap, the SP/HP/fatigue
gauges, the party-member panels, the status/buff-icon strip, the countdown and
notice popups, the debug/performance overlay, and it is the mouse+hotkey router
for everything on that overlay.

It is driven by `CNRDInterfaceManager`:

```
0x0231B6D0  CNRDInterfaceManager::proc  ->  0x01373050  CNRDBaseInterface::proc
0x01377570                              ->  0x0135CF20  CNRDBaseInterface::create
0x01375640                              ->  0x01374EF0  CNRDBaseInterface::DrawProc
```

The instance is reachable globally as `*(void**)((*(char**)0x03A5C9B8) + 0x48)`
— proved by `0x02300930` (`mov eax,[ecx+0x48]; ret`) being called on
`[0x03A5C9B8]` and the result then used as `this` for `0x01353D30`
(`0x00C6F865`) and field-checked at `+0x11D0/+0xA60/+0xA61` (`0x01362C7C`).

### Server relevance: near zero at the wire, one real table

**`parser_calls == 0` for all 482 functions in the corrected span.** No inbound
frame is decoded here. Everything reaches this unit already parsed. So a server
implementer gets nothing about packet framing from this file. What it *does* give:

1. the **status/buff icon id table** (§2),
2. the **argument contract of the buff-icon add/remove API** — the server's buff
   broadcast must supply *remaining ms* and *total ms*, because the HUD gauge is
   computed from both (§3),
3. confirmation that dungeon-start countdown, respawn timer, PvP hit display,
   ping display and fatigue notices are **client-local rendering** of already-known
   state, not separately negotiated protocol.

---

## 2. `APPLY_AS_*` name → status-icon id (`0x01353D30`)

A thread-safe lazily-built `std::map<const char*, int>` with a `strcmp`-style
comparator. Statics:

| VA | role | proof |
| --- | --- | --- |
| `0x03A3EC30` | CRITICAL_SECTION (double-checked lock) | `0x01354519 mov ecx,0x3a3ec30; call 0x27be090` (Enter) / `0x01354503 … 0x27be0c0` (Leave) |
| `0x03A3EC2C` | `built` flag | `mov byte ptr [0x3a3ec2c], 1` at `0x0135450D` |
| `0x03A3EC34` | map object (`_Myproxy`) | `mov ecx, 0x3a3ec34` before every `insert` |
| `0x03A3EC3C` | `_Myhead` (node 0x18 B: `_Left/_Parent/_Right/_Myval@0xC/_Color@0x14/_Isnil@0x15`) | `0x01353DAB push 0x18 … [eax+0x14]=1; [eax+0x15]=1` |
| `0x03A3EC40` | `_Mysize` | `0x01353DB1` |
| `0x03A3EC48` | MSVC magic-static guard bitmask | `test byte ptr [0x3a3ec48], 1` |

Signature: `__thiscall int CNRDBaseInterface::?(const String& name)` — `ret 4`,
`this` is passed but unused. The string class is *not* MSVC `std::string`:
`0x00423CC0` reads capacity at `+0x18` and returns `this+4` inline when `cap < 8`,
else `*(char**)(this+4)`.

Body = map lookup → `switch(it->second)` (jump table: index bytes at `0x013548C0`
(0x59 entries, input − 0x6E07), targets at `0x01354858` (26 slots)) → returns a
small constant. Full recovered table:

| APPLY_AS name | map value | returned icon id |
| --- | ---: | ---: |
| `APPLY_AS_ICON_FINISH_REWARD_BUFF` | 0x6E04 | 3001 |
| `APPLY_AS_ICON_DEVIL_CONTRACT` | 0x6E07 | 3002 |
| `APPLY_AS_ICON_MAX_LEVEL_EFFECT_ATFIGHTER` | 0x6E0B | 3003 |
| `APPLY_AS_ICON_LUCKYBOY_BLESS` | 0x6E0E | 3004 |
| `APPLY_AS_ICON_UNKNOWN_1_CHN` | 0x6E10 | 3005 |
| `APPLY_AS_ICON_UNKNOWN_2_CHN` | 0x6E12 | 3006 |
| `APPLY_AS_ICON_EVENT_LET_ME_HELP_YOU_FRIEND` | 0x6E15 | 3007 |
| `APPLY_AS_ICON_EVENT_DNF_WITH_FRIENDS` | 0x6E19 | 3008 |
| `APPLY_AS_ICON_DRAGON_DUNGEON_CLEAR_BUFF` | 0x6E1D | 3009 |
| `APPLY_AS_ICON_WEDDING` | 0x6E20 | 3010 |
| `APPLY_AS_ICON_HELP_GROWTH_EVENT` | 0x6E23 | 3011 |
| `APPLY_AS_ICON_FISH_FEVER_TIME_` | 0x6E27 | 3012 |
| `APPLY_AS_WOMAN_DAY_BUFF` | 0x6E2B | 3013 |
| `APPLY_AS_ANTIEVIL_BLESS_BUFF` | 0x6E30 | 3015 |
| `APPLY_AS_ICON_ACTIVATION_CHARACTER` | 0x6E35 | 3017 |
| `APPLY_AS_ICON_PHOENIX_BUFF_LV1` | 0x6E3E | 3027 |
| `APPLY_AS_ICON_PHOENIX_BUFF_LV2` | 0x6E40 | 3028 |
| `APPLY_AS_SOLODAY_BUFF` | 0x6E49 | 3032 |
| `APPLY_AS_LUCKY_RULLET_PINK` | 0x6E4D | 3035 |
| `APPLY_AS_LUCKY_RULLET_PURPLE` | 0x6E4F | 3036 |
| `APPLY_AS_LUCKY_RULLET_RED` | 0x6E51 | 3037 |
| `APPLY_AS_LUCKY_RULLET_YELLO` | 0x6E53 | 3038 |
| `APPLY_AS_LUCKY_RULLET_BLUE` | 0x6E55 | 3039 |
| `APPLY_AS_ICON_BURNING_TO_60LV_BUFF_VER1` | 0x6E59 | 3040 |
| `APPLY_AS_ICON_BURNING_TO_60LV_BUFF_VER2` | 0x6E5B | 3041 |
| `APPLY_AS_ICON_AVATAR_CONDITION_DROP_BUFF` | 0x6E5F | 3049 |

Two independent id spaces (the gaps differ), so a server that emits either one must
not assume they are the same enum.

The returned 30xx value is a **HUD status-icon id**, the same space that
`addStatusIcon` takes: a call-site scan of every `E8` reaching `0x01370AA0`
found literal ids `0xBF7 (3063)` and `0xBFD (3069)`, in-range and adjacent to
this table. Only caller: `0x00C6F820`, inside the
`DNFClient\RDAR\Localization\china\…` band, which stores the result to `[esi]`
and whose parent `0x00C70C10` also calls `0x00C6EFD0` / `0x00C6FD10` — both of
which are direct callers of `addStatusIcon`.

---

## 3. Status / buff icon list — the state machine the server drives

```
0x01370AA0   addStatusIcon(int iconId, int remainMs, int totalMs)   __thiscall, ret 0xC   (47 external callers)
0x01370BB0   removeStatusIcon(int iconId)                           __thiscall, ret 0xC   (53 external callers)
0x0136E440   query/iterate over this+0xE48                                                (12 external callers)
0x0136E560   find index of a record by key, returns -1 if absent
0x01344650   record constructor (0x54 bytes)
```

`addStatusIcon` (`0x01370AA0`), instruction by instruction:

```
01370ACA  mov  edi, [0x3B3DFB8]      ; local-player/world ptr; bail if null
01370ADE  cmp  ebx, 0x1869f          ; ebx = iconId; 99999 = "no icon" sentinel -> bail
01370AED  call 0x01344650            ; ctor a 0x54-byte record on the stack
01370AF2  lea  ecx, [esi+0xE70]      ; HUD clock
01370AF6  call 0x027C3660            ; -> eax = now (ms)
01370B0B  add  eax, [ebp+0x0C]       ; now + remainMs
01370B10  mov  [rec+0x10], eax       ;   -> expiry tick
01370B13  sub  eax, [ebp+0x10]       ; (now + remainMs) - totalMs
01370B16  mov  [rec+0x08], ebx       ;   -> iconId
01370B19  mov  [rec+0x0C], eax       ;   -> start tick
01370AFF  mov  [rec+0x04], 3         ;   -> kind = 3
01370B21  call 0x0136E560            ; already present?
01370B4B  ... 0x0136CD40 / 0x01355AB0 ; if not, insert into this+0xE48
```

**Contract for a server implementer:** the client needs *both* the remaining
duration and the total duration to draw the icon's depletion gauge. A server that
sends only "remaining" makes `rec+0x0C == rec+0x10 - remain` land wrong and the
gauge starts at the wrong fill. `99999` is a reserved "suppress" id.

`removeStatusIcon` (`0x01370BB0`) erases by `(kind=3, iconId)`, and on success
resets the buff-tooltip popup block:

```
01370C97  cmp  dword ptr [esi+0xE5C], 0
01370CA3  mov  dword ptr [esi+0xE5C], 2
01370CAD  mov  [esi+0xE60] = [esi+0xE64] = [esi+0xE68] = [esi+0xE6C] = -1
```

### Record layout (0x54 bytes), from ctor `0x01344650`

| off | init | meaning | proof |
| ---: | --- | --- | --- |
| +0x00 | 0 | | `0x01344692` |
| +0x04 | 0 | **kind** | set to 1/2/3/4 at call sites (below) |
| +0x08 | −1 | **iconId** | `0x01370B16 mov [rec+8], iconId` |
| +0x0C | 0 | **start tick (ms)** | `0x01370B19` |
| +0x10 | 0 | **expiry tick (ms)** | `0x01370B10` |
| +0x14 | 0 | | `0x013446BE` |
| +0x18 | 0.0f | float | `movss [esi+0x18], xmm0` @ `0x013446A0` |
| +0x1C | 0 | byte | `0x013446A5` |
| +0x20 | 0 | | `0x013446A8` |
| +0x24 | 0x0E | default 14 | `0x013446AB` |
| +0x28 | −1 | | `0x013446B2` |
| +0x2C..+0x38 | 0 | | `0x013446B5`–`0x013446C1` |
| +0x3C | −1 | | `0x013446C7` |
| +0x40 | 0 | byte | `0x013446C4` |
| +0x44 | −1 | | `0x013446CA` |
| +0x48, +0x4C | 0 | | `0x0134467F`, `0x01344682` |
| +0x50 | | pointer / ref-counted handle | `mov [rec+0x50], …` at `0x01370Bxx`, `0x014E621x` |

Element size 0x54 is independently confirmed by the reciprocal division in
`0x0136E440`: `mov eax, 0x30C30C31; imul; sar edx,4` → divisor 84.

**`kind` values observed** (scan of all 15 callers of the ctor):

| kind | set at |
| ---: | --- |
| 1 | `0x01372510`, `0x01372670` |
| 2 | `0x00D90BE0` (outside this unit) |
| 3 | `0x01370AA0`, `0x01370BB0`, `0x014E6200` |
| 4 | `0x01370D10` |

Who feeds `addStatusIcon` / `removeStatusIcon` (file attribution via `tu-index.csv`):
`IRDCharacter.cpp` (7), `CNRDObject.cpp` (3), `CNRDQuickPartyManager.cpp` (2),
`CNRDDungeon.cpp`, `CNRDGrowthPowerModule.cpp`, `Localization\china\DevilContract.cpp` (2),
and — the direct server link — **`GameCoreNetwork_NOTI_Etc.cpp` (`0x01160280`)**
and **`GameCoreNetwork_NOTI_Town.cpp` (`0x0118C3A0`)**, both of which call
`removeStatusIcon`. So server NOTI packets do clear HUD status icons directly.

---

## 4. `importEventNoticeScript` (`0x01367F70`) — client data-file schema

`__stdcall bool (OutSetup* /*ebp+8*/, ? /*ebp+0xC*/, ? /*ebp+0x10*/)`, `ret 0xC`.
Reads a **plain-text, whitespace-token** script with two sections
(literals decrypted at `0x0136811D` / `0x01368229`):

```
[setup]     -> repeating 4-int record, written to out+0x0C, out+0x00, out+0x04, out+0x08 (in that order)
[event]     -> repeating record { int; int; int; int; int; String }  (0x30 bytes), push_back'd via 0x01367470
```

Token readers: `0x028AB5D0(&okFlag, 1)` returns the next int, `0x028AC0F0(&str, 1)`
returns the next string, `0x028AD2B0` advances to the next section header.
On malformed input it calls `report(0, "…CNRDBaseInterface.cpp", 22261,
"CNRDBaseInterface::importEventNoticeScript", <msg>)` at `0x013683A2`.

The scripts this reads are named in `create()`:
`Event/chn_event/chn_fatigueSpeedUp.evt` (+ its `읽기 오류` = "read error" message),
`Event/Fatigue/` + `EventFatigue.etc`.
A server that runs fatigue-boost events has to agree with these files' contents —
the client renders the schedule locally from them.

---

## 5. `CNRDBaseInterface` object layout (recovered offsets)

`ecx = this` for every method. Offsets below are only those where I can cite a
method whose name explains the field. (A full 448-offset dump is in
`compensate/_layout.txt`; the low offsets there are noisy — alias leakage into
inlined helpers — and should not be trusted without a per-site check.)

| off | type | meaning | proof |
| ---: | --- | --- | --- |
| +0x003C | ptr | owning interface/scene root; `create()` stores its arg0 here | `0x0135CF8C mov eax,[ebp+8]; mov [esi+0x3C],eax`. Used by the 133-caller hub `0x0132FE60`: `mov ecx,[ecx+0x3C]; call 0x023010B0` (= `[+0x91D8]`) then tail-jmp `0x014D08F0` with 0x2C bytes of args — a message/notice sink |
| +0x0060 | obj | item quick-slot bar | `drawItemQuickSlot 0x0134C859 lea esi,[edi+0x60]`; also `onLeftButtonDown`, `onLeftButtonUp` |
| +0x0090 | obj | skill quick-slot bar | `drawBaseSkillQuickSlot 0x01365CE8 lea esi,[ecx+0x90]`; `drawSkillQuickSlot`, `procShowSkillInfo` |
| +0x00F0 | obj | EX-skill quick-slot bar | `drawExSkillQuickSlot 0x0133B7FC lea ebx,[esi+0xF0]` |
| +0x0224 | ptr | minimap widget | `minimap`, `drawMainControl`, `procMouseClick` |
| +0x0388 | obj | text style / font state handed to the text renderer | `drawPingStateData 0x0134A9EA add edi,0x388; push edi; call [[0x03BAAE70]+0x54]` |
| +0x03A8 | byte[18] | per-hotkey **current** state | `procHotKey 0x01352327 cmp byte [esi+eax+0x3A8],0`, `0x01352354 mov byte [esi+ecx+0x3A8],bl` (esi = index 0..17, eax/ecx = `this` from `[ebp-4]`) |
| +0x03BA | byte[18] | per-hotkey **edge/consumed** flag | `procHotKey 0x0135234C mov byte [esi+edx+0x3BA],1` (0x3BA−0x3A8 = 18) |
| +0x0568 | bool | show ping-state overlay | `drawPingStateData 0x0134A95C cmp byte [edi+0x568],0`; `drawTopLayerControl 0x013668A1 cmp byte [esi+0x568],1` |
| +0x0A60 | bool | mode flag A | `drawMainControl 0x01374681`; `proc 0x01373756`; gates `drawBuffIcons` |
| +0x0A61 | bool | mode flag B — "in main game (dungeon)" | `drawPartyMemberInMainGame 0x01351A8D` checks it, `drawPartyMemberInTown` does not; also `minimap 0x0134E50C`, `drawTopLayerControl 0x01366858`, `procMouseClick 0x01352F75`, tooltip helper `0x01362CA7` |
| +0x0A62 | bool | buff-icon strip enabled | `drawBuffIcons 0x01372800` |
| +0x0ABC | obj | dungeon start countdown | `drawDungeonStartCount 0x0134A66D` |
| +0x0CBC | ptr | | `proc 0x013731D3` |
| +0x0E14 | ptr | cooltime notifier | `drawCooltimeNotifier 0x0134D202` |
| **+0x0E48** | container | **status/buff icon list** (0x54-byte records) | `0x01370B45`, `0x01370C31`, `0x0136E465 lea edi,[ecx+0xE48]` |
| +0x0E5C | int | buff-tooltip popup state (0 / 2) | `0x01370C97`, `0x01370CA3`; read by `checkBuffPopupInfo 0x0134DB4E` and `checkEnemyActiveStatusPopup 0x0136C6CC` |
| +0x0E60/64/68/6C | int×4 | popup anchor rect (all set to −1 on reset) | `0x01370CAD`–`0x01370CBF`; `checkBuffPopupInfo` reads +0xE60/+0xE68 with `movq xmm0` |
| **+0x0E70** | obj | HUD clock — `0x027C3660` returns current ms | `0x01370AF2` |
| +0x0ECC..+0x0EE8 | 2×{ptr,int,int,int} | PvP current-hit-object trackers | `drawCurrentHitObjectInPvp 0x01350B4C`, `0x01350BD2`–`0x01350C14` |
| +0x0F10 | ptr | aura / coin widget | `drawAuraAvatarAbilityUI 0x0134CDA9`, `drawUseCoinNumber 0x0134C63A` |
| +0x0F34/+0x0F58 | obj/bool | village-attack shortcut notice | `drawVillageAttackShortkeyNotice 0x013490EC / 0x013490D8` |
| +0x0F5C | CNTimer | fatigue-notice timer | `drawFatigueNotice 0x01363DC7 lea ecx,[esi+0xF5C]; call 0x027C3450/3410/38B0/3B60/34D0/3780/3660` |
| +0x0F78 | int | fatigue-notice duration (ms) | `0x01363DEA push [esi+0xF78]` into the timer's setDuration |
| +0x0F80 | bool | fatigue notice enabled | `0x01363DAA` |
| +0x0F81 | bool | fatigue notice needs restart (plays `DINGDONG`, `0x01363E12`) | `0x01363DBE` / `0x01363E2C` |
| +0x0F88/+0x0FA4 | obj/u16 | free-coin notice | `drawFreecoinNotice 0x01364958 / 0x0136492E` |
| +0x0FEC/+0x0FF0 | bool/int | item quick-slot drag state | `drawItemQuickSlot 0x0134C9FE / 0x0134CA0E` |
| +0x1044/+0x1048 | ptr | minimap sub-widgets | `minimap 0x0134E654 / 0x0134E6B9` |
| +0x1084 | bool | **hotkeys suppressed when == 1** | `procHotKey 0x013522AD cmp byte [esi+0x1084],1` |
| +0x1108/+0x110C/+0x1110/+0x1120/+0x1121 | ptr/int/bool | anti-evil warning | `drawAntiEvilWarning 0x01349E2C / 0x01349EA1 / 0x01349DEE / 0x01349D0F / 0x01349CFA` |
| +0x1124 | container | active-object popup list | setter/adder `0x01330160`: `mov ecx,[edi+0x1124]; push esi; call 0x010D6890`; read by `checkBuffPopupInfo`, `checkEnemyActiveStatusPopup`, `drawMainControl 0x013746CE` |
| +0x11C4 | ptr | plain setter `0x013302F0` (`mov [ecx+0x11C4], arg; ret 4`, 32 external callers) | `0x013302F6` |
| +0x11D0 | bool | HUD-active gate used by the tooltip helper | `0x01362C76 mov al,[eax+0x11D0]; test al,al` |
| +0x1274 / +0x12E6 | bool | drawMainControl sub-panel gates | `0x0137461D`, `0x01374636` |
| +0x1304 | obj | slot-swap widget | `startSlotSwap 0x0134F17E lea esi,[ecx+0x1304]` |
| +0x1318 | obj | skill-slot-swap button | `drawSkillSlotSwapButton 0x0133783C` |

Object is at least `0x1320` bytes.

---

## 6. Public surface

262 functions in the corrected span have external callers, but **most are not
`CNRDBaseInterface` methods** — the linker folded template/inline COMDATs into this
address range (e.g. `0x013555E0` is an 8-byte-return iterator adaptor, `0x0134FDD0`
is a hash-bucket helper with `shr edi,3; add edi,eax`, `0x01346560` a checked-cast
wrapper). The real exported API, ranked by external fan-in:

| VA | ext callers | what it is |
| --- | ---: | --- |
| `0x0132FE60` | 133 | thinnest wrapper in the file: `this->[0x3C]->[0x91D8]` then tail-`jmp 0x014D08F0` with 11 dword args — the shared "post a HUD message/notice" entry |
| `0x01370BB0` | 53 | `removeStatusIcon(iconId)` — §3 |
| `0x01370AA0` | 47 | `addStatusIcon(iconId, remainMs, totalMs)` — §3 |
| `0x01362C30` | 40 | multi-line tooltip/balloon renderer. Gated on the HUD instance (`+0x11D0 != 0 && +0xA60 == 0 && +0xA61 != 0`), asks the text engine `[0x04C2D5E8]` vtbl+0x34 to word-wrap into a vector of 0x20-byte strings, then vtbl+0x24 (`measureText(str,-1)`) and vtbl+0x18 (`lineHeight`) per line to size the box |
| `0x013302F0` | 32 | setter for `this+0x11C4` |
| `0x0136E440` | 12 | iterate/query the status-icon list at `this+0xE48` |
| `0x01330160` | 11 | conditionally push an object into `this+0x1124` (skipped when `obj->vf[0x5A0]()` returns true) |
| `0x0134B0A0` | 4 (+3 int) | `onClickMiniMapButton` |
| `0x01352BC0` | — | `procMouseClick` — 33 distinct `this` fields touched; the HUD's hit-test router |
| `0x01352250` | — | `procHotKey` — polls 18 hotkey descriptors at `0x03A5D110` (stride 0x10, record base `edi-8`, key code at record+8, code 0x86 skipped, slots 9 and 11 skipped) |
| `0x0135CF20` | 1 | `create()` — 22 KB, 93 callees, ~174 field initialisations |
| `0x01373050` | 1 | `proc()` — per-frame update, called by `CNRDInterfaceManager::proc` |
| `0x01373B00` | 1 | `drawMainControl()` — master draw dispatcher (calls `minimap`, `drawBrokenEquipments`, `drawAntiEvilWarning`, `drawBuffIcons`, `drawCurrentHitObjectInPvp`, …) |
| `0x01374EF0` | 2 | `DrawProc()` |

---

## 7. Odds and ends worth recording

* **`create()` dynamically resolves `GetThreadTimes`.** `0x0135CF5A` decrypts
  `"Kernel32.dll"` (`0x03287C14`), `LoadLibrary` via `[0x030291C0]`,
  `GetProcAddress` via `[0x030291C8]` for the plaintext name `GetThreadTimes` at
  `0x03287C04`, caches it in `0x03A3E068`, then `FreeLibrary` via `[0x030291C4]`.
  It feeds the perf overlay (`performance weight : %.2f`, `fps : %.3f, avg fps : %.3f`,
  `memory usage : %d MB` at `0x0134BF10`).
* **Client-side profiling HUD for packet handlers** — `0x01346760`
  (`CLIENT PROC HANDLER MAX/AVG TOP 10`), `0x01346D70` (`TCP PACKET HANDLER
  MAX / AVG WITH COUNT TOP 10`), `0x01347120` (`UDP PACKET HANDLER MAX / COUNT
  TOP 10`). Stats come from singleton `[0x03BAAE70]` vtbl+0x54 and are drawn via
  vtbl+0x64/+0x68 at x=10 with colour `0xFFFF64A0`. Handlers are keyed by **name
  string**, not by packet id, so this yields no opcode table.
* **Game-module state enum** — `0x01102C30` → `0x0163E1E0` returns a small int.
  `procHotKey` disables hotkeys when it is **0, 5 or 7** (`0x0135229C`:
  `sub eax,0/je; sub eax,5/je; sub eax,2/jne`); `drawMainControl` skips
  `drawBrokenEquipments` when it is **0xE** (`0x01374667`). Values not named here.
* Two more globals used as HUD gates: `0x03B3DFB8` (must be non-null for
  add/removeStatusIcon — local player/world) and `0x03A42F94` (checked with
  `[+0x1F1]` before `drawBuffIcons`, `0x0137468A`).

---

## Confidence

* **High** — span correction, `parser_calls == 0`, the APPLY_AS table and both
  its jump tables, the add/removeStatusIcon signatures and the 0x54-byte record
  layout, the `[setup]`/`[event]` script schema, the `GetThreadTimes` resolution,
  the listed field offsets (each has a cited instruction).
* **Medium** — the semantics attached to `+0xA60`/`+0xA61` (town vs main-game);
  the identification of `0x01362C30` as a tooltip renderer (mechanism is proven,
  the *name* is inference); the claim that `0x01353D30`'s 30xx return is
  consumed by `addStatusIcon` (range match + shared parent function, not a
  traced dataflow).
* **Unknown** — the meaning of `kind` values 1/2/4; the game-module state enum
  values; what `0x01340CF0` (20 external callers, lazy static at `0x03A3E660`) is.
