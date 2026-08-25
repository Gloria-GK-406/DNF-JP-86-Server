# TU 019 — `DNFClient\RDAR\CNRDMainHudMenuManager.cpp`

Image base 0x00400000, S4A21 unpacked-from-memory dump.

## Verdict in one paragraph

This file implements **`MainHudMenuManager`**, the singleton behind the main HUD's
button bar / menu strip: it (a) parses the client-only script
`ClientOnly/MainHudMenu_Ver2.co` into three `std::map<int, MenuInfo>` tables,
(b) holds a 41-entry menu-slot layout that is pushed in from outside over a
serialization stream, and (c) contains **`MainHudSpace::behavior*` — a 41-case
dispatcher (`0x013AA930`) that turns a menu button's `MainHudMenuAction` number
into a concrete UI action** (open window N, toggle window N, start a logout
countdown, open the QQ customer-service URL, …). There is **no packet parsing in
this unit** (`parsers = 0` for every function in it, and none of the five inbound
read primitives is reached).

## The briefing's span is too narrow — correct extent

`tu-index.csv` gives `0x013A8360 – 0x013AA560`. That is the outermost pair of
`__FILE__`-referencing functions, not the module. The real translation unit is
approximately **`0x013A61E0 – 0x013AB2C0`**, and the evidence is direct:

* `0x013AA930` (the behaviour dispatcher, 2448 bytes + a 41-entry jump table at
  `0x013AB218`) sits *above* the stated `hi` yet calls five in-span functions
  (`0x13A8360`, `0x13A8950`, `0x13A8AB0`, `0x13AA540`, `0x13AA560`) and nothing
  else calls those.
* `0x013A7FA0` (the `MenuInfo` constructor) sits *below* the stated `lo`, has
  exactly one caller — `0x013A9CE0`, the in-span script parser.
* `0x013A8090` sits below `lo` but is dispatcher case `0x17`, and the receiver it
  allocates (`0x013A92A0`, vtable `0x32925E0`) is the one that logs
  `MainHudSpace::behaviorCHANNEL_SELECT_WINDOW::AgreeEnchantReceiver::onQuickConfirmResult`.
* `0x013A6EF0 / 0x013A6F00 / 0x013A6F20` (below `lo`) are member accessors that
  touch `this+0xE0` and `this+0x3C` of the same object `0x013AA4A0` returns.
* No other TU in `tu-index.csv` claims any address in `0x1390000–0x13C0000`.

Source-line numbers baked into the logging calls bound the file at ≥ 2140 lines
and place the pieces consistently: script parsing at lines 274–349, behaviours at
1056–2140.

## Object layout — `MainHudMenuManager`

Static instance at **`0x03A41108`**; MSVC magic-static guard bit at `0x03A411EC`.
Every reference to `0x03A41108/0x03A4111C/0x03A41130/0x03A41144/0x03A411E8` in
the whole image lies inside `getInstance` (`0x013AA4A0`) plus the atexit unwind
table at `0x2D99B21` — nothing reaches the object except through `getInstance`.

| off | type | size | evidence |
| --- | --- | ---: | --- |
| `+0x00` | `std::map<int, MenuInfo>` — **`[info]`** table, keys 1000+ | 20 | ctor `0x105B3E0` called on `0x3A41108` at `0x13AA4DE`; getter `0x2C426E0` = `mov eax,ecx; ret` |
| `+0x14` | `std::map<int, MenuInfo>` — **`[info_alt]`** table, key = `MenuInfo.slot` | 20 | ctor at `0x13AA4EC` on `0x3A4111C`; getter `0x27C6F20` = `lea eax,[ecx+0x14]` |
| `+0x28` | `std::map<int, MenuInfo>` — **`[info_static]`** table, keys 2000+ | 20 | ctor at `0x13AA4FA` on `0x3A41130`; getter `0x27C6310` = `lea eax,[ecx+0x28]` |
| `+0x3C` | `int32 slotTable[41]` (0xA4 bytes) | 164 | ctor `0x2A431F0` (zero-fills exactly 0xA4) at `0x13AA508` on `0x3A41144`; `0x13A6F20` does `lea edi,[ecx+0x3C]; mov ecx,0x29; rep movsd` |
| `+0xE0` | `bool` — "exit confirm already shown" | 1 | `mov byte [eax+0xE0],0` at `0x13AA5C8`/`0x13AA8E1`; `=1` at `0x13AA557`; setter `0x13A6EF0` = `mov byte [ecx+0xE0], al` |
| `+0xE4` | (magic-static guard, `0x3A411EC`) | — | `test byte [0x3A411EC],1` at `0x13AA4C2` |

`0x03A41144 + 0xA4 == 0x03A411E8` — the 0xA4 block ends exactly where the bool
begins, so there is no gap.

### `MenuInfo` — 48 bytes (0x30), ctor `0x013A7FA0`

| off | type | set by | evidence |
| --- | --- | --- | --- |
| `+0x00` | `std::wstring name` (MSVC 28-byte form: `_Myproxy@0`, `_Bx@4` 16B, `_Mysize@0x14`, `_Myres@0x18`) | script field 2 | `[esi+0x18]=7`, `[esi+0x14]=0`, `word[esi+4]=0` in ctor; `0x424180(this=rec,&tok,0,-1)` at `0x13A889E` |
| `+0x20` | `int` | script field 3 | `mov [esi+0x20], eax` at `0x13A88B1` |
| `+0x24` | `int` | script field 4 | `mov [esi+0x24], eax` at `0x13A88D9` |
| `+0x28` | `int` | script field 5 | `mov [esi+0x28], eax` at `0x13A8901` |
| `+0x2C` | `int slot` — HUD slot index, valid range `[0,41)`, **default 41 = none** | script field 1 | `mov dword [esi+0x2C], 0x29` in ctor; `mov [esi+0x2C], eax` at `0x13A8851`; `cmp [ebp-0x34],0x29 / jge skip` at `0x13A9E9E`, `0x13A9F7C`, `0x13AA06B` |

Cross-check on the map node: `std::_Tree` head node is allocated with size
`0x44` (`push 0x44; call operator new` at `0x105B42D`), `_Color`/`_Isnil` at
`+0x40/+0x41`. `0x44 = 12 (_Left/_Parent/_Right) + 52 (pair<int,MenuInfo>) + 2`,
and `sizeof(pair<int,MenuInfo>) = 4 + 48 = 52`. Independently, `0x013A8700`
reads the found value as `[node + 0x3C]` = node`+0x0C`(pair) `+0x04`(int key)
`+0x2C`(MenuInfo.slot). Both agree.

## The script format — `ClientOnly/MainHudMenu_Ver2.co`

Loaded once from `getInstance()` (`0x13AA519: call 0x13AA190`). Parser is
`0x013A9CE0(const wchar_t* path, map* info, map* infoAlt, map* infoStatic)`
(cdecl); record reader is `0x013A8770(MenuInfo* out)`. Script primitives come
from `DNFShared\GameScript\RDARScript.cpp` (`0x28A91A0` open, `0x28A9A70` next
token, `0x28AC0F0` next token w/ flag, `0x28AB5D0` next int, `0x423CD0` token
compare, `0x27B3900` close).

Grammar, exactly as the code walks it:

```
[add menu info]                 <- required first token, else error 349
  [info]         <slot> <name> <i1> <i2> <i3>     -> map@+0x00,  key = 1000,1001,...
  [info_static]  <slot> <name> <i1> <i2> <i3>     -> map@+0x28,  key = 2000,2001,...
  [info_alt]     <slot> <name> <i1> <i2> <i3>     -> map@+0x14,  key = <slot>
  ...            (unknown tokens are skipped)
[/add menu info]                <- EOF without it -> error 316
```

Auto-key seeds: `mov edi, 0x3E8` (1000) at `0x13A9D8B` and
`mov [ebp-0x9C], 0x7D0` (2000) at `0x13A9D90`; both post-increment per record.
Records whose `slot >= 41` are parsed but **not inserted**.

Field order inside a record is `slot` first, then `name`, then three ints —
`0x13A8770` reads them in that order at `0x13A8846` → `+0x2C`,
`0x13A889E` → `+0x00`, then `+0x20`, `+0x24`, `+0x28`.

The parser returns a **C++ source line number** on failure (274 / 278 / 283 /
287 / 291 from the record reader; 309 / 316 / 349 from the block loop) and
`0x13AA190` formats it into the decrypted literal
`"ClientOnly/MainHudMenu.co Script err : line %d"` — note the message names the
old `.co` file while the code actually loads `MainHudMenu_Ver2.co`.

## Server-relevant: the slot table arrives over the wire

`MainHudMenuManager::setSlotTable` (`0x013A6F20`, `rep movsd` of 41 dwords into
`this+0x3C`) has exactly two call sites in the image:

* `0x0110DBC1`, inside handler **`0x0110DB70`** in
  `DNFClient\RDAR\GameCoreNetwork_CMD_Etc.cpp` (TU 60). That handler does
  `ctor(0xA4 buf)` → `memset 0xA4` → `0x27C5380(buf, 0xA4)` → `getInstance()` →
  `setSlotTable(buf)`. `0x27C5380` is the generic **sequential stream reader**
  (`memcpy(dst, [0x3BAAE38], n); [0x3BAAE38] += n`) with 154 callers across the
  image.
  The handler is registered at `0x01119144` as
  **`register(key = 0x34E, handler = 0x110DB70, ctx = 0)`** via `0x163C4A0`
  (which stores `{fn, ctx}` into a map keyed by the first argument).
* `0x0052671A`, which reads the same 0xA4 blob out of the client's local
  settings store (`0x274CA50/0x274CED0/0x274E6D0`), and the *same* key `0x34E`
  appears at `0x005266DF`.

So: **the HUD menu layout is a 164-byte (41 × u32) blob synchronised under
id 0x34E**, copied verbatim into the manager. A server implementer that wants to
control which buttons appear where on the main HUD has to emit that blob.
Caveat stated honestly: I confirmed the key/handler wiring and the payload size
and shape; I did **not** confirm that `0x34E` is a raw wire opcode rather than a
sub-id inside a settings-sync command — the same id is reused against the local
settings store.

`0x013A6F00` reads element `i` as `word [this + 0x3C + i*4 + 2]` and returns
"empty" when that half-word is zero, so each 4-byte slot entry is at least
two 16-bit fields and the *upper* half is the occupancy/id discriminator.

## The behaviour dispatcher — `0x013AA930`

`bool MainHudMenuManager::doBehavior(int behaviorId)` — `ecx = this`,
`ret 4`, returns `al` (true unless a case falls into `xor bl,bl` at `0x13AB183`).
Jump table of **41 entries** at `0x013AB218`, bounds check `cmp eax,0x28 / ja`.

Where the behaviour id comes from (`0x005280B0`, the HUD window's message
handler, case msg == 13): it fetches a widget by control id, reads the widget
string property whose decrypted name is **`"MainHudMenuAction"`**
(literal `0x30717DC`), converts it to a number, and calls
`doBehavior(that)`; if it returns true the HUD menu window `0x369` is closed.
The related literal `0x3067ADC` decrypts to `"CLICK_BUTTON2"`.

Most cases end with `EVT(1, 0x0E, k)` — `0x00C2E090(1, 0xE, k)` on the singleton
at `0x3A5AE54`, i.e. a client-side event/trigger notification with category 0x0E
and a per-behaviour `k`.

Legend: `IM` = `CNRDInterfaceManager` singleton at `0x03A5C9B8` (its methods are
attributed to `DNFClient\RDAR\CNRDInterfaceManager.cpp` in
`func-source-file.csv`): `open = 0x2304C70(id, param, recv)`,
`close = 0x2304790(id, …)`, `isOpen = 0x2301820(id)`, `toggleQuery = 0x23016F0(id)`.

| id | case addr | what it does | evt k |
| ---: | --- | --- | ---: |
| 0x00 | 0x13AA94C | toggle window `0xA7` (query, then close or open) | 2 |
| 0x01 | 0x13AA9A3 | `IM.0x2308560(0x98,0,0)`; `g[0x3A5CA00].0x279B580(5,0)` | 3 |
| 0x02 | 0x13AA9E7 | toggle window `0x2E`; `0x2305FA0(1)`; `IM.0x2302420` chain; `…(8,0)` | 4 |
| 0x03 | 0x13AAA65 | open `0x19D`; `…(3,0)` | 5 |
| 0x04 | 0x13AAAA9 | `IM.0x2309EE0()` | 6 |
| 0x05 | 0x13AAAD5 | `IM.0x2306050()` | 7 |
| 0x06 | 0x13AAB01 | `IM.0x23060A0()` | 8 |
| 0x07 | 0x13AAB2D | open `0x09`, then `0x2410970(0)` on it | 9 |
| 0x08 | 0x13AAB6C | open `0x10`; `…(0xC,0)` | 0xA |
| 0x09 | 0x13AABAD | open `0x36`, then `0x2482740(0)`; `…(0xA,0)` | 0xB |
| 0x0A | 0x13AABFB | if `state==1` and `0x163D250()` false → `0x52D15FE` (.vmp1) | 0xC |
| 0x0B | 0x13AAC43 | `0x13A6F40()` — emits notice `0x209` with msg 0x11343/0x1134A/0x1139C/0x542/0x116F9 | 0xD |
| 0x0C | 0x13AAC69 | if `state==1` and `[0x3B3DFBC]!=0` → `0x1335D40()` (CNRDBaseInterface.cpp) | — |
| 0x0D | 0x13AAC98 | `IM.0x2307A50(7,1,1)` | 0xF |
| 0x0E | 0x13AACCA | `IM.0x2307A50(8,1,1)` | — |
| 0x0F | 0x13AACFC | open `0xC8` | — |
| 0x10 | 0x13AAD18 | open `0xA8` | 0x10 |
| 0x11 | 0x13AAD4D | `0x279EB70()` → `0x279F010()` | 0x11 |
| 0x12 | 0x13AAD7A | open `0x20B`; then notice `0x209` with msg `0xEB0E` | 0x12 |
| 0x13 | 0x13AADCF | open `0x106`; `…(6,0)` | 0x15 |
| 0x14 | 0x13AAE13 | open `0x107`; `…(6,0)` | 0x16 |
| 0x15 | 0x13AAE57 | open `0x109` | — |
| 0x16 | 0x13AAE73 | **`0x13A8AB0`** — confirm-dialog flow, receiver vtable `0x32925D4` → `0x13A90F0` | 0x18 |
| 0x17 | 0x13AAE99 | **`0x13A8090` = `MainHudSpace::behaviorCHANNEL_SELECT_WINDOW`**, receiver vtable `0x32925E0` → `0x13A92A0` | 0x19 |
| 0x18 | 0x13AAEE5 | `0x13A71C0()` (3344 B; notices `0x253`, msgs 0x956E/0x81C9/0x57C9/0x18DB) | 0x1A |
| 0x19 | 0x13AAF0B | **exit / quit** — see below | 0x1B |
| 0x1A | 0x13AACE3 | open `0x53` | — |
| 0x1B | 0x13AAF6C | open `0xCE` | 0x13 |
| 0x1C | 0x13AAFA1 | open `0xD4` | 0x14 |
| 0x1D | 0x13AAEBF | `0x13A8950` — if `g[0x3A5C99C].0xC23830()` open `0x360`, else run receiver vtable `0x3292604` → `0x13A8A00` immediately | 0x17 |
| 0x1E | 0x13AAFD6 | open `0x107`; `…(6,0)` | — |
| 0x1F | 0x13AB001 | gated: `0x63D780/0x63BB70`, `0xE61ED0(0x321D)`, contents-flag `0xB5`, `IM.isOpen(0x349)`, `0xC23760`, `0x608850/0x608BB0/0x608AC0` → notices `0x209`(msg 0x7D2C/0x7D2D), `0x360`, `0x349`, or `0x107` | — |
| 0x20 | 0x13AB0E1 | open `0x5B` | — |
| 0x21 | 0x13AB0FA | **open the QQ customer-service page** — `ShellExecute`-style `0x5BC8A8F("open", url)`; url literal `0x3292A70` decrypts to `http://crm2.qq.com/page/portalpage/wpa.php?uin=40012345&f=1&ty=1&ap=000011:400994:|m:11|f:Gdnf8`, verb literal `0x306E9A4` = `"open"` | — |
| 0x22 | 0x13AAEE5 | same target as 0x18 | 0x1A |
| 0x23 | 0x13AB12C | open `0xCB` | — |
| 0x24 | 0x13AB164 | if `CNUserManager.0x19E3D10()` → `0x109C040()->vt[4](0x10)` and **return false**; else open `0xFF` | — |
| 0x25 | 0x13AB1A9 | open `0x94` | — |
| 0x26 | 0x13AB1C5 | `CNUserManager.0x19E3360()` ? open `0x31` : open `0x34` | — |
| 0x27 | 0x13AB148 | open `0x1CC` | — |
| 0x28 | 0x13AB1FC | open `0x32` with third arg 5 | — |

### Case 0x19 — the exit / quit state machine (the named behaviours)

```
0x13AAF0B:  if (this->flagE0 == 0) call 0x13AA540      ; may set flagE0 = 1
            if (this->flagE0 != 0) call 0x13AA560      ; MainHudSpace::behaviorBEFORE_EXIT_GAME
            else                   call 0x13A8360      ; MainHudSpace::behaviorEXIT_GAME
            EVT(1,0x0E,0x1B); EVT(5,3,-1); 0x14BF180()->0x14BF150()
```

* `0x013AA540` — `if (Singleton@0x39AF630->has(0x42)) getInstance()->flagE0 = true;`
* `0x013AA560` — `MainHudSpace::behaviorBEFORE_EXIT_GAME` (logs
  `"Exit Button Clicked 1"` @ line 2060 and `"BE HAVIOR BEFORE EXIT GAME END"`
  @ line 2140). Clears `flagE0`, checks registry entry `0x74`, walks a
  `0x2846D50/0x19E9E60(>=60)/0x11D20F0` guard and either opens window `0x1AE`
  or falls through to `behaviorEXIT_GAME`. Then, gated on registry entry
  `0x80`, opens window `0x30D` or `0x2F9`; on the `0x2F9` path it allocates a
  36-byte confirm receiver with vtable `0x32925F8` (`0x13AA71B`), formats
  message `0x4C97`, opens confirm window `0x263`, and lays out the dialog.
* `0x013A8360` — `MainHudSpace::behaviorEXIT_GAME` (logs
  `"Exit Button Clicked 2"` @ line 1686 and `"BE HAVIOR EXIT GAME START"`
  @ line 1743). Registry gates `0x92B`, `0x80`, `0x9A`, `0x929`; window gates
  `0x2F9`, `0x2FA`, `0x370`, `0x30D`, `0x36D`; then a receiver with vtable
  `0x32925EC` (`0x13A85C2`), message `0x79BC`, confirm window `0x33B`, and on
  the fallback path `0x19ED2E0(g[0x3A5C9A8], 0)`.

### The confirm-dialog callback contract (reusable)

Five 12-byte vtables live back to back, each `{ onQuickConfirmResult, 0x2231720, <rtti> }`:

| vtable | `onQuickConfirmResult` | created at | on **OK** (`arg == 0`) |
| --- | --- | --- | --- |
| `0x32925D4` | `0x13A90F0` | `0x13A8AFC` (case 0x16) | `IM.0x2305870(2, 0)` when restricted, else the leave sequence |
| `0x32925E0` | `0x13A92A0` | `0x13A80DB` (case 0x17, CHANNEL_SELECT_WINDOW) | `IM.0x2305870(1, 0)`, else `0x51C6D67` (.vmp1) on the local player; logs `"confirm channel select window"` @ line 1056 |
| `0x32925EC` | `0x13A9410` | `0x13A85C2` (behaviorEXIT_GAME) | `IM.0x2305870(3, 0)`, else `0x2304890(IM,0)` + `0x12D0930` on `0x3B84690` + `0x5238824` (.vmp1) |
| `0x32925F8` | `0x13AA870` | `0x13AA71B` (behaviorBEFORE_EXIT_GAME) | re-runs the `0x33B` confirm, then `0x13A6C70` (the shared "actually quit" tail). On **Cancel** (`arg == 1`) it clears `flagE0`. |
| `0x3292604` | `0x13A8A00` | `0x13A89CA` (case 0x1D) | `0x51C6D67` on the player, `0x10A1010(1)` → `0x1A97CF0` |

Calling convention: the object is `operator new(0x24)`, `0x23DCF30(this)` fills
the base, the vptr is stored at `+0x00`; `+0x04` / `+0x08` are strong/weak
refcounts (`lock xadd` at `0x13AA7C8` / `0x13AA7DC` and every parallel site).
The receiver is handed to the interface manager as the **third** argument of
`openWindow(0x263, wchar_t* text, receiver)` (e.g. `0x13A8DEA:
push esi; push eax; push 0x263; call 0x2304C70`). The callback takes one `int`
(`ret 4`): **0 = OK / agree, 1 = cancel**.

`CNRDInterfaceManager::0x2305870(int mode, int overrideMs)` is the logout
countdown: mode `0` cancels (`[IM+0x91AC]=0`, `[IM+0x91CC]=0`), non-zero opens
window `0x170` and writes a randomised deadline into `[IM+0x91CC]`
(`rand % 10`-derived, `* 1000`, overridable by arg 2). Modes seen here:
`1` (channel select), `2` (case 0x16), `3` (exit game / `0x13A6C70`).

## Public surface

Only **two** functions are genuinely called from other modules, plus three
inline-ish member accessors. The briefing's "33 distinct external callers"
overcounts badly: `0x13A94D0`, `0x13A9590`, `0x13A9AE0`, `0x13A9B50`,
`0x13A9CB0`, `0x13AA220`, `0x13AA340` are `std::map<int, MenuInfo>` template
bodies (`_Insert`, `_Erase`, `erase`, `clear`, copy-ctor) that the linker
COMDAT-folded with identical instantiations from other files — e.g. `0x13A9AE0`
is "called" from `LoadScriptClientOnly.cpp`, which has nothing to do with the
HUD menu.

| VA | signature | notes |
| --- | --- | --- |
| `0x013AA4A0` | `MainHudMenuManager* getInstance()` | 21 callers — the only door into the object. Magic-static; on first call constructs the three maps + the 0xA4 slot block, zeroes `flagE0`, calls `loadScript` (`0x13AA190`), registers dtor `0x30012F0` via atexit. |
| `0x013AA930` | `bool doBehavior(int behaviorId)` | 4 callers (`0x527FB0`/`0x5280B0`, `0xF5ADB0`, `0x126CF10`, `0x1352BC0` in `CNRDBaseInterface.cpp`). The 41-case table above. |
| `0x013A8700` | `int getSlotOfMenu(int menuId)` | `map::find` on `this+0x00`, returns `MenuInfo.slot` (`[node+0x3C]`) or **41** when absent. Called from `0x5282C4` to resolve a HUD control to a slot, which then feeds `0x13A6CD0`. |
| `0x013A6F20` | `void setSlotTable(const int src[41])` | `rep movsd 0x29` into `this+0x3C`. Callers: the `0x34E` stream handler and the settings restore. |
| `0x013A6F00` | `bool isSlotEmpty(int i)` | `word [this+0x3C+i*4+2] == 0`. 5 call sites, all in the HUD window class `0x525xxx–0x528xxx`. |
| `0x013A6EF0` | `void setFlagE0(bool)` | 6 call sites (`0xC53520`, `0xF5B6AE/6D0/6FC/BB81`, `0x11D21FE`). |
| `0x013A8760` | thunk → `0x013A8090` | one caller, `0x1350000` in `CNRDBaseInterface.cpp` — lets the HUD open the channel-select confirm directly. |
| `0x013AA3D0` | destructor (clears all three maps) | reached only through the atexit entry `0x30012F0`. |

### Side table: slot → id (`0x013A6CD0`, jump table at `0x013A6DC0`, 39 entries)

Called right after `getSlotOfMenu` at `0x5282D5`; result feeds `0x1655AF0`
(`CNRDObject.cpp`/`RDCommon.cpp`) to build a string that is drawn as a tooltip
next to the HUD button — consistent with a hotkey-name lookup, though I did not
prove the id space.

```
slot : 0→0x09 1→0x0B 2→0x13 3→0x0A 4→0x40 5→0x54 6→0x41 7→0x32 8→0x33 9→0x34
      10→0x57 11→0x31 12→-1 13→0x3A 14→0x5B 15→-1 16→0x55 17→0x2B 18→0x3B
      19→0x0C 20→0x3C 21→-1 22→-1 23→0x65 24..31→-1 32→0x33 33,34→-1 35→0x61
      36→-1 37→0x64 38→0x35        (39,40 fall through to default -1)
```

## Things I deliberately did not claim

* The semantics of individual `CNRDInterfaceManager` window ids (`0x263`,
  `0x209`, `0x33B`, `0x369`, …) beyond what the call shape proves: `0x263` takes
  a text + a confirm receiver, `0x209` takes text only, `0x369` is the HUD menu
  window itself (closed by `0x528261` when `doBehavior` returns true).
* What `MenuInfo` fields `+0x20/+0x24/+0x28` mean. They are written by the script
  parser and I found no read site in the image that I could attribute with
  confidence.
* Whether `0x34E` is a wire opcode or a settings sub-id (see above).
* `0x0141A170()->has(id)` and `0x00D0B370()->has(id)` are two distinct global
  id-keyed registries used as feature gates (ids seen here: `0x74`, `0x80`,
  `0x9A`, `0xB5`, `0x92B`, `0x929` for the first; `0x42` for the second). I did
  not identify what populates them.
