# TU 11 — `DNFClient\RDAR\CNRDBaseInterface.cpp`

Briefed span `0x01335D40`–`0x01367F70` (266 fns, 0 parsers). Image base 0x00400000, DNF S4A21.

## 1. What this unit implements

`CNRDBaseInterface` is the **in-game HUD / base interface layer** — the always-on widget
layer drawn over the game field. It owns:

* the main control bar (`drawMainControl` @ `0x01373B00`, whose profiler literal is
  literally `CNRDBaseInterface::drawMainControl#sp표시, 피로게이지` — "SP display, fatigue gauge"),
* the three quick-slot strips (item / base-skill / EX-skill) and their swap UI,
* minimap, party-member frames (town + dungeon variants), buff icons (own + party),
* a large family of one-off notice/countdown widgets (fatigue, freecoin, PVP-EXP,
  broken equipment, anti-evil warning, village-attack shortkey, dungeon start count,
  respawn time, cooltime notifier, aura/avatar ability),
* mouse + hotkey routing for that layer (`procMouseClick`, `procHotKey`,
  `onLeftButtonDown/Up`, `onRightButtonUp`, `procCheckQuickSlotHotkey`),
* the client's on-screen developer overlays (FPS/memory/imagepack/lag stats and the
  TCP/UDP packet-handler profiler top-10 dumps),
* per-frame entry points `proc` (`0x01373050`) and `DrawProc` (`0x01374EF0`).

Method names are recoverable directly: each method opens a scope-timer whose ctor
(`0x027AB200`) is handed a `std::wstring` built once from an encrypted literal
`CNRDBaseInterface::<method>` — e.g. `0x0134A8E2 push 0x3289C68` → decrypts to
`CNRDBaseInterface::drawPingStateData`, stored to global `0x03A3E838`, then
`0x0134A957 push 0x03A3E838 / lea ecx,[ebp-0x70] / call 0x027AB200`. 46 such literals exist.

**Singleton:** `CNRDBaseInterface* = *(void**)((*(char**)0x03A5C9B8) + 0x48)`.
Getter `0x02300930` is `mov eax,[ecx+0x48]; ret`. Used at `0x01362C58`, `0x013735B9`.
`0x03A5C9BC` holds the quick-slot manager (`0x016EE6C0` = getQuickSlotItem(idx),
`0x016EF580` = findQuickSlotIndex(item)).

## 2. SPAN IS TOO NARROW (both ends) — not too wide

Nothing inside `0x01335D40`–`0x01367F70` contradicts the file name. But the class body
extends well past both anchors. `CNRDBaseInterface::*` scope literals occur from
**`0x0132FC80`** to **`0x01374EF0`**:

| below briefed `lo` | | above briefed `hi` | |
|---|---|---|---|
| `0x0132FC80` | procCheckQuickSlotHotkey | `0x0136C5F0` | checkEnemyActiveStatusPopup |
| `0x01330BD0` | isEmptyExSkillSlot | `0x0136EBB0` | drawBuffIcon |
| `0x013340D0` | onRightButtonUp | `0x0136F4F0` | drawPartyBuffIcon |
| | | `0x013727D0` | drawBuffIcons |
| | | `0x01373050` | proc |
| | | `0x01373B00` | drawMainControl |
| | | `0x01374EF0` | DrawProc |

Neither gap is claimed by another TU in `tu-index.csv` (previous anchor is CNHitGauge at
`0x01329D20`; next is CNRDConfigFile at `0x0137FA90`). Real extent ≈ `0x0132FC80`–`0x01375400`.
The briefed hull drops the buff-icon subsystem, `proc`, `drawMainControl` and `DrawProc` —
i.e. the module's two per-frame entry points and its most server-relevant packet sends.

## 3. Server-relevant: C→S packets sent from this HUD

Send primitives (in `CNGameSocket.cpp`), identified from disassembly:

| VA | role | evidence |
|---|---|---|
| `0x0274CA50` | `getGameSocket()` | `mov eax,[0x03B7CEE4]; ret` |
| `0x0274D1E0` | `beginPacket(WORD msgType, x)` | `ret 8`; `mov di, word [ebp+8]`; returns `0x80000054` if `[esi+8]==0` |
| `0x0274D2E0` | write u8 | `inc dword [ecx+0x2BCC2C]`, `push 1` into the writer |
| `0x0274D340` | write u32 | `add dword [ecx+0x2BCC2C], 4`, `push 4` |
| `0x0274E6D0` | send / flush | |

These four sends are the **only** C→S traffic in `0x0132FC80`–`0x01375400`:

| site | enclosing fn | msgType | body | gate |
|---|---|---|---|---|
| `0x013361AC` | `startAdvanceAltar` `0x01335D40` | **0x392** (914) | none | `[ebp-0x418]==8 && ebx==4` (`0x01336188`) |
| `0x013665CA` | `drawTopLayerControl` `0x013664A0` | **0x42** (66) | none | `mgr->state[1]==0xE` and `this+0xBE0==0` (`0x013665A3`–`0x013665BD`) |
| `0x013735F6` | `proc` `0x01373050` | **0x233** (563) | `u8 = 1` | see below |
| `0x013736C9` | `proc` `0x01373050` | **0x233** (563) | `u8 = 0` | see below |
| `0x01332BE3` | `0x01332B00` | **0x2D6** (726) | `u32 = 0` (`lea edx,[esi-3]`, `esi=3` const) | quick-slot index from `0x016EF580` must satisfy `idx-3 <= 0x169` |

`proc`'s 0x233 is a **boolean toggle state the server must mirror**. Same code path
enables/disables the widget at `this+0xDB0` (`0x0282D3A0(bool)`), and on the refusal
branch shows system notice id `0x209` through `0x02304C70` on the interface manager
(`0x013735BF`) plus formats string-table id `0x11C79` into a 0x200-wchar buffer
(`0x01373563`). Gating chain reads content flags `0x73` (`0x0141A170`/`0x014172D0`),
`0x6D` (`0x01102C30`/`0x0163E4D0`), config id `0x17` (`0x01648EB0`), and pushes `1` only
when `0x0163E1E0()` is `3` or `0x12`. I could not pin the exact feature name — the
strongest circumstantial candidate in this unit's literal set is the fatigue accelerator
(`active_fatigue_accel` / `fatigue_accel_checkbox` at `0x013474D0`,
`Event/chn_event/chn_fatigueSpeedUp.evt`) but that is **not proven**; treat the semantic
as unknown and the wire shape (`0x233` + one u8) as solid.

`0x2D6`'s body is a literal `0` on this path (`esi` is `mov esi, 3` at `0x01332B2D` and
never reassigned before `lea edx,[esi-3]` at `0x01332BE8`), i.e. item-quick-slot ordinal 0.

## 4. Server-relevant: NOTI handlers that drive this HUD

From `callgraph.csv`, callers outside the region whose TU is a `GameCoreNetwork_*`:

| packet TU | caller | callee in HUD | what the callee does |
|---|---|---|---|
| `NOTI_Dungeon.cpp` | `0x0114F380` | **`0x01343D30`** | `setHudVisible(bool)`: `mov [esi+0x11D0], bl` (`0x01343D5D`), then vtbl+0xC on **5** handles at `this+0x138` stride 8 (`0x01343D63`–`0x01343D80`) and **2** at `this+0x12F4` stride 8 (`0x01343D82`–`0x01343DA0`) |
| `NOTI_Dungeon.cpp` | `0x01146000` | `0x01344170`, `0x0134DA40` | map/container accessors on `this+0x10F4` |
| `NOTI_Etc.cpp` | `0x01160280`, `0x01164800` | `0x01347DF0` | large draw over an array at `[edi + esi*8 + 0x418]` |
| `NOTI_Etc.cpp` | `0x011781D0` | **`0x01355540`** | `setGuideState(int)` — see below |
| `CMD_Town.cpp` | `0x0112D150` | **`0x01355540`** | same |
| `NOTI_Town.cpp` | `0x01191D80` | `0x01344740` | gated on content flag `0x829` (`0x0141A170`/`0x014172D0`) |
| `NOTI_Etc.cpp` | `0x01171F50` | `0x01344170` | |

`0x01355540 setGuideState(int id)` is a small dispatcher (`ret 4`):
`id == 0x56` → touch widget `this+0x1148` (`0x0135554F`);
`id == 0x55` → unless `0x011038E0()` is true, set widget `this+0x10E4` frame `(3, 0)`
via `0x0142C630` (`0x01355581`);
both then tail into `0x01353AD0`, which special-cases `id == 0x32` (set `this+0x10E4`
frame `(1,0)`) and otherwise resolves an image and installs it on `this+0x10E4`.
So the server drives a small **guide/notice state machine with ids {0x32, 0x55, 0x56}**.

## 5. Server-relevant: quick-slot index model

| strip | array | count | index space | UI id | evidence |
|---|---|---|---|---|---|
| item | `this+0x60`, stride 8 | 6 | **3..8** | — | `0x0134C859 lea esi,[edi+0x60]`; `0x0134C85C mov [ebp-0x38],3`; `0x0134C866 mov [ebp-0x50],6`; body calls `0x016EE6C0([0x03A5C9BC], idx)` |
| base skill | `this+0x90`, stride 8 | 6 | 0..5 | **0x59E5** | `0x01365CE8 lea esi,[ecx+0x90]`; `0x01366018 cmp eax,6 / jl`; `0x01365D88 push 0x59E5` |
| EX skill | `this+0xF0`, stride 8 | 6 | **0xC6..0xCB (198..203)** | **0x5A3F** | `0x0133B7FC lea ebx,[esi+0xF0]`; `0x0133B802 mov [ebp-8],0xC6`; `0x0133B92E cmp eax,0xCC / jl`; `0x0133B870 push 0x5A3F` |

The whole-index space is bounded by the check in `0x01332B00`: `lea ecx,[edi-3]; cmp ecx,0x169; ja`
→ valid quick-slot index range **3..0x16C (3..364)**.

Widget lookup helpers used everywhere here: `0x01648E70(uiId, callerName)` and
`0x0164C5D0(callerName, uiId)` followed by `0x019D5DD0(subIndex)`. Concrete ids seen:
`0x422C`, `0x422D`, `0x4233`, `0x4234` (dungeon start countdown, `drawDungeonStartCount`
`0x0134A620`), plus `0x59E5` / `0x5A3F` above. Tutorial guide arrow is drawn on the item
slot whose index equals `this+0xFF0` when `this+0xFEC != 0` (`0x0134C9FE`, `0x0134CA0E`,
resource `Interface/tutorial_interface_guide.img`).

## 6. Table layout: APPLY_AS_* status-icon id map (`0x01353D30`)

Lazy `std::map<const wchar_t*, int>` in global **`0x03A3EC34`**, magic-static guard byte
`0x03A3EC48`, node insert `0x004CB700`, lookup `0x004CA980` keyed by the argument's
`wstring::c_str()` (`0x00423CC0` = SSO c_str: capacity at `+0x18`, buffer at `+4`).
Each entry is a `{const wchar_t* name, int id}` pair built on the stack, e.g.
`0x01353EE7 push 0x0328AE60 / call 0x027BACC0` (decryptor) → `[ebp-0xC8]`,
`0x01353F0D mov [ebp-0xC4], 0x6E04`. 26 entries:

| id (dec) | id (hex) | name |
|---:|---|---|
| 28164 | 0x6E04 | APPLY_AS_ICON_FINISH_REWARD_BUFF |
| 28167 | 0x6E07 | APPLY_AS_ICON_DEVIL_CONTRACT |
| 28171 | 0x6E0B | APPLY_AS_ICON_MAX_LEVEL_EFFECT_ATFIGHTER |
| 28174 | 0x6E0E | APPLY_AS_ICON_LUCKYBOY_BLESS |
| 28176 | 0x6E10 | APPLY_AS_ICON_UNKNOWN_1_CHN |
| 28178 | 0x6E12 | APPLY_AS_ICON_UNKNOWN_2_CHN |
| 28181 | 0x6E15 | APPLY_AS_ICON_EVENT_LET_ME_HELP_YOU_FRIEND |
| 28185 | 0x6E19 | APPLY_AS_ICON_EVENT_DNF_WITH_FRIENDS |
| 28189 | 0x6E1D | APPLY_AS_ICON_DRAGON_DUNGEON_CLEAR_BUFF |
| 28192 | 0x6E20 | APPLY_AS_ICON_WEDDING |
| 28195 | 0x6E23 | APPLY_AS_ICON_HELP_GROWTH_EVENT |
| 28199 | 0x6E27 | APPLY_AS_ICON_FISH_FEVER_TIME_ |
| 28203 | 0x6E2B | APPLY_AS_WOMAN_DAY_BUFF |
| 28208 | 0x6E30 | APPLY_AS_ANTIEVIL_BLESS_BUFF |
| 28213 | 0x6E35 | APPLY_AS_ICON_ACTIVATION_CHARACTER |
| 28222 | 0x6E3E | APPLY_AS_ICON_PHOENIX_BUFF_LV2 |
| 28224 | 0x6E40 | APPLY_AS_ICON_PHOENIX_BUFF_LV1 |
| 28233 | 0x6E49 | APPLY_AS_SOLODAY_BUFF |
| 28237 | 0x6E4D | APPLY_AS_LUCKY_RULLET_PINK |
| 28239 | 0x6E4F | APPLY_AS_LUCKY_RULLET_PURPLE |
| 28241 | 0x6E51 | APPLY_AS_LUCKY_RULLET_RED |
| 28243 | 0x6E53 | APPLY_AS_LUCKY_RULLET_YELLO |
| 28245 | 0x6E55 | APPLY_AS_LUCKY_RULLET_BLUE |
| 28249 | 0x6E59 | APPLY_AS_ICON_BURNING_TO_60LV_BUFF_VER2 |
| 28251 | 0x6E5B | APPLY_AS_ICON_BURNING_TO_60LV_BUFF_VER1 |
| 28255 | 0x6E5F | APPLY_AS_ICON_AVATAR_CONDITION_DROP_BUFF |

(name/id pairing recovered by matching the stack slot written with `eax` from the string
decryptor against the immediate written at slot+4; names cross-check against
`literals.csv` for this function, which lists them in exactly descending-id order.)

**Caveat, do not over-claim:** `0x01353D30` carries no `CNRDBaseInterface::` literal and
its only caller is `0x00C6F820`, outside the region. It sits inside the anchor hull but may
be a neighbouring TU's COMDAT. Report it as "a table located inside TU 11's span", not as a
`CNRDBaseInterface` member.

## 7. Table layout: `importEventNoticeScript` (`0x01367F70`, `ret 0xC`)

Text-script reader. Line reader `0x028AD2B0(&state, &ctx, &tok, 1)`; int token
`0x028AB5D0(&ok, 1)` (returns the int in `eax`, sets `ok` byref); string token `0x028AC0F0`.
Two section keywords, both decrypted at runtime: `[setup]` (`0x0328A6DC`) and `[event]`
(`0x0324B8E0`).

* `[setup]`: reads 4 ints, stored to `arg0+0x0C`, `arg0+0x00`, `arg0+0x04`, `arg0+0x08`
  (`0x01368189`, `0x013681AF`, `0x013681CB`, `0x013681FA`).
* `[event]`: repeated record of **5 ints then 1 string**, staged at `[ebp-0x264]`
  (first int pre-initialised to `-1` at `0x0136827A`), appended to a vector via
  `0x01367470` (`0x01368398`).
* Parse failure logs `"%s"` + `__FILE__` + `"\n"` through `0x027BE370`.
* Sole caller `0x01391810`. The matching data file is in this unit's literal set:
  `Event/chn_event/chn_fatigueSpeedUp.evt` (with its `... 읽기 오류` = "read error" message).

## 8. Object layout — `CNRDBaseInterface` (≥ 0x1320 bytes)

Each row cites the instruction that proves it. `this` tracked from `ecx` at entry.

| offset | type / role | proof |
|---|---|---|
| +0x003C | `CNRDInterfaceManager*` (ctor arg) | `0x0135CF8F mov [esi+0x3C], eax` (arg0 of `create`); read `0x013340D3`, `0x01352E42`, `0x0134E5AB`, `0x0134B217`, `0x013665A3`, `0x013731F8`. Accessor on it: `0x02300E00 = mov eax,[ecx + idx*4 + 0xA0]` |
| +0x0054 | ptr | `0x0135D95D` (write), `0x0135341D`, `0x013749C1` |
| +0x0060 | item quick-slot handle[6], stride 8 | `0x0134C859`, `0x0133DBFF`, `0x0135AD99` |
| +0x0090 | base-skill quick-slot handle[6], stride 8 | `0x01365CE8`, `0x01367094`, `0x0133FBE9`, `0x0133DC62` |
| +0x00F0 | EX-skill quick-slot handle[6], stride 8 | `0x0133B7FC`, `0x0133DC85` |
| +0x0138 | widget handle[5], stride 8 (bulk show/hide) | `0x01343D63`+`0x01343D7A`; individually `0x01352C7F/0x140/0x148/0x150/0x158` |
| +0x0388 / +0x0389 / +0x0390 | party-frame sub-object | `0x0135E999`, `0x0135E9AF mov byte [esi+0x389],1`, `0x0135E9A9` |
| +0x0458 | party-frame widget | `0x0134A564` |
| +0x0568 | bool gate (ping overlay) | `0x0134A95C cmp byte [edi+0x568],0`; also `0x013668A1` |
| +0x0A60 | bool gate | `0x01362C8F`, `0x01373C4C`, `0x01373756` |
| +0x0A61 | bool gate | `0x01362CA7`, `0x01373C59`, `0x01352F75`, `0x0134E50C`, `0x01351A8D`, `0x01366858` |
| +0x0A62 | bool (buff icons) | `0x01372800` |
| +0x0A84 | u8 alpha/colour | `0x0133B836 movzx ecx, byte [esi+0xA84]`, `0x0134C8AD`, `0x01373BF8` |
| +0x0ABC | dungeon start-count object | `0x0134A66D` (`drawDungeonStartCount`) |
| +0x0BE0 | bool (gates the 0x42 send) | `0x013665B6` |
| +0x0DB0 | widget toggled by the 0x233 send | `0x01373491`, `0x013735DB`, `0x013736B0` |
| +0x0E14 | int (cooltime notifier gate) | `0x0134D202` |
| +0x0E5C / +0x0E60 / +0x0E64 / +0x0E68 / +0x0E6C | buff-popup state (int, 2×qword, 2×int) | `0x0134DB4E`, `0x0134DB46 movq xmm0,[esi+0xE60]`, `0x0134DB89`, `0x0134DB5E`, `0x0134DB95` |
| +0x0ECC..+0x0EE8 | PvP hit-object display state | `0x01350B4C`, `0x01350BD2`, `0x01350BDC`, `0x01350BE4`, `0x01350B58`, `0x01350C02`, `0x01350C0C`, `0x01350C14` |
| +0x0F10 | widget ptr | `0x0134C63A`, `0x0134CDA9`, `0x01373C94` |
| +0x0F20 | sub-object with paired accessors | `0x01352AD3 add ecx,0xF20`, `0x0134BEFE` |
| +0x0F34 / +0x0F58 | village-attack shortkey notice | `0x013490EC`, `0x013490D8` |
| +0x0F5C / +0x0F78 / +0x0F80 / +0x0F81 / +0x0F84 | fatigue-notice state | `0x01363DC7`, `0x01363DEA`, `0x01363DAA`, `0x01363DBE`, `0x01363F0D` |
| +0x0F88 / +0x0FA4 | freecoin notice (obj, u16) | `0x01364958`, `0x0136492E movzx eax, word [esi+0xFA4]` |
| +0x0FEC / +0x0FF0 | tutorial-guide active flag + target slot index | `0x0134C9FE`, `0x0134CA0E` |
| +0x1044 / +0x1048 | minimap widgets | `0x0134E654`, `0x0134E6B9` |
| +0x1084 | bool hotkey gate | `0x013522AD` |
| +0x10E4 | guide/notice image widget | `0x01353AF8`, `0x01355581` |
| +0x10F4 | container reached from NOTI_Dungeon | `0x0134417E lea esi,[ecx+0x10F4]`, `0x0134DA4E` |
| +0x1108 / +0x110C / +0x1110 / +0x1120 / +0x1121 | anti-evil warning state | `0x01349E2C`, `0x01349EA1`, `0x01349DEE`, `0x01349D0F`, `0x01349CFA` |
| +0x1124 | widget ptr | `0x0134DBA2`, `0x013746CE`, `0x0136C6ED` |
| +0x1144 / +0x1145 | bool pair | `0x013736E1`, `0x0137372B`, `0x01374A3C` |
| +0x1148 | widget (guide state 0x56) | `0x0135554F`, `0x01360B10` |
| +0x11D0 | **bool "HUD visible"** — set by the NOTI-driven setter | `0x01343D5D mov byte [esi+0x11D0], bl`; read `0x01362C76`, `0x013532C7` |
| +0x12F4 | widget handle[2], stride 8 (bulk show/hide) | `0x01343D82`+`0x01343D9A`; also `0x01352D5E` |
| +0x1304 | slot-swap state | `0x0134F17E` (`startSlotSwap`) |
| +0x1318 | skill-slot-swap button | `0x0133783C` |

`create` (`0x0135CF20`, 16 KB) is the constructor/loader; it writes 174 distinct offsets —
the full list is in `zz_u11_fields.txt` in the scratchpad. It also has a side effect worth
recording: at `0x0135CF5A` it does `LoadLibraryW(L"Kernel32.dll")` +
`GetProcAddress(hMod, "GetThreadTimes")` and caches the pointer in global **`0x03A3E068`**
(guarded on that global being 0), then `FreeLibrary`.

## 9. Public surface — and a trap in the briefing's "entry points" table

Real API of this module (called from outside, genuinely this class):

| VA | name / role | external callers |
|---|---|---|
| `0x01362C30` | multi-line **tooltip/balloon box renderer** — the client-wide one | 42 |
| `0x01343D30` | `setHudVisible(bool)` (NOTI_Dungeon) | 3 |
| `0x01355540` | `setGuideState(int)` (CMD_Town, NOTI_Etc) | 2 |
| `0x01335D40` | `startAdvanceAltar` (sends 0x392) | 2 |
| `0x0134B0A0` | `onClickMiniMapButton` | 4 |
| `0x0134A000` | `drawPartyMemberInTown` | 2 |
| `0x01347DF0` | draw over `[edi+esi*8+0x418]` (NOTI_Etc ×2) | 2 |

`0x01362C30` detail: refuses unless singleton exists, `+0x11D0` set, `+0xA60` clear,
`+0xA61` set (`0x01362C76`–`0x01362CAE`). Splits the text via the font/text object at
`[0x04C2D5E8]` (`vtbl+0x34` = split into `vector<wstring>` elem size 0x20; `vtbl+0x24` =
measure width; `vtbl+0x18` = line height), computes box size, then clamps against screen
globals `[0x0399AE98]` (width) and `[0x0399AE9C]` (height), applying an anchor enum through
jump tables at `0x01363020` and `0x01363034`.

**Trap:** several of the highest in-degree functions the briefing lists as "entry points"
are STL COMDAT template code that merely landed in this TU, not this class's API:

* `0x0133D5A0` / `0x0133D650` / `0x0133D5F0` — `vector<T>::iterator` helpers, element sizes
  **0x54** and **0x14** (`0x0133D5E2 add esi,0x54`, `0x0133D695 add esi,0x14`,
  `0x0133D618 imul eax, eax, 0x54`).
* `0x01348C10` / `0x01348C70` — `_Destroy_range` loops releasing a refcounted handle at
  `+0x4C` (stride 0x54) and `+0x0C` (stride 0x14) via the `lock xadd` / vtbl+0/vtbl+4 idiom.
* `0x013555E0` — 64-byte `push_back`-style wrapper onto `this+0x40`.
* `0x01344650` — a POD ctor zeroing `+0x00..+0x4C` with `[+0x24]=0xE`, `[+0x08]=[+0x28]=[+0x3C]=[+0x44]=-1`.

Ranking "the module's API" by external caller count without checking the body will
mis-label these as HUD entry points.

## 10. Also inside the region (evidence, minimal interpretation)

* Developer overlays at `0x01331590`–`0x01333390` and `0x0134BF10`:
  `■ IMAGEPACKMANAGER INFORMATION ■`, `▶ Doubt Degree : %d`, `▶ Virtual character : %d개`,
  `rag rate : %.2f`, `performance weight : %.2f`, `texture size : %0.3f MB/%dms`,
  `IImage Count (Pack) : %d ( %d )`, `equipmentparameterinfo count and size: %d / %d`.
* Network profiler dumps at `0x01346560` / `0x01346760` / `0x01346D70` / `0x01347120`:
  `CLIENT PROC HANDLER MAX|AVG TOP 10`, `TCP PACKET HANDLER MAX TOP 10`,
  `TCP PACKET HANDLER AVG WITH COUNT TOP 10`, `UDP PACKET HANDLER MAX|COUNT TOP 10`,
  formatted as `[%s] : %dms` and `[%s] : %d회, [totalWasteTime] : %dms`. The client keeps
  per-packet-handler timing keyed by **handler name**; the snapshot comes from `0x027A7F60`
  and the name table itself is **not** in this unit — but it exists somewhere, and it would
  be a direct source of message-id → handler-name mappings.
* `0x0136A7BB` region (`0x01368FA0`) formats `HP:%d/%d` / `MP:%d/%d` — the numeric HP/MP
  readout on the main bar.

## 11. Unknowns / not established

* Semantic of C→S `0x233`, `0x42`, `0x2D6`, `0x392` beyond the wire shapes above.
* Whether `0x01353D30` (the APPLY_AS table) belongs to this TU or a neighbour.
* The `[event]` record's field meanings in `importEventNoticeScript` (only the shape —
  5 ints + string — is established).
* Buff-icon drawing internals (`0x0136EBB0`, `0x0136F4F0`, `0x013727D0`) were not
  disassembled; they sit outside the briefed span and are the most likely place to find the
  client-side buff/status list layout that the server feeds.

## Files written during this analysis (scratchpad)

`zz_u11_send.py` (send-site scanner), `zz_u11_dis.py` (range disassembler),
`zz_u11_off.py` (offset cross-ref), `zz_u11_fields.txt` (full `this`-offset dump),
`zz_u11_proc.txt`.
