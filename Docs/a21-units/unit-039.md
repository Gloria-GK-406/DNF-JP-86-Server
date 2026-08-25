# Unit 039 — `DNFClient\RDAR\CNUser.cpp`

Anchored span `0x019DE9D0`–`0x019E1550` (38 functions). Image base `0x00400000`.

## Verdict in one paragraph

This file implements **`CNUser`** — the client's per-player *account/session* object, one
instance per player visible to this client, keyed by a 16-bit **serverId**. It is not the
character: it *owns* two character pointers, holds a large embedded **user-info record**
(name / job / equipment / avatar, filled straight from `CNRDUserInfoPacketHandler`),
publishes the local player's character into two hot globals, draws the player's
portrait, hosts the **전문직업 (expert-job) street shop** placement, and implements a
**"my character" object cache** that survives a channel/scene transition so the client can
reuse the same `CNCharacter` instance when the player re-enters the same character on the
same server. The class vftable `0x032F6CBC` is emitted in this file's own data block, and
both constructors here write it — that is the proof the class is `CNUser` and that this
file is its definition, not a user of it.

## The span is TOO NARROW (report this upward)

The `__FILE__`-anchored span captures only the tail of the module.

* Log call sites carry `__LINE__`: `setCharacter` = line **0x4A4 (1188)** at the span's
  *low* edge `0x019DE9D0`; `getMyCharacterCacheData` = lines **0xDD5/0xDE7/0xDEB
  (3541/3559/3563)** at the *high* edge. So ~1187 source lines precede `0x019DE9D0`.
* Functions in `0x019DABE0`–`0x019DE9D0` (≈115 of them) are `CNUser` inline accessors:
  they read/write `[ecx+0x4C0]`, `[ecx+0x478]`, `[ecx+0x4DC]`, `[ecx+0x568]`,
  `[ecx+0x56C..0x57C]`, and several are called with `ecx = this` from inside the anchored
  span (`0x019DD920`, `0x019DB790`, `0x019DAE30`, `0x019DB2E0`, `0x019DCF40`).
  Examples: `0x019DABE0 cmp dword[ecx+0x4C0],-1 / setne al`;
  `0x019DAC20 mov [ecx+0x478], arg`; `0x019DAC30 mov eax,[ecx+0x478]`.
* Below ~`0x019DA9E0` the literals still belong to `CNRDSkillManager.cpp`
  (`0x019D8990` → `skill/SkillAttackDataList.lst`), so the module's real low bound is
  between `0x019DA9E0` and `0x019DABE0`. **Treat CNUser.cpp as ≈`0x019DAB50`–`0x019E18B0`.**

Note also: the tail function `0x019E1550` actually runs to `0x019E18B0`, past the
briefing's `hi`.

## Object layout — `CNUser`, sizeof = **0x580**

Size proof: `0x01819E50`: `push 0x580; call 0x423460 (operator new); … call 0x019E0E80`
(the derived ctor, which adds no members). Highest field seen is `+0x57C`.

Layout recovered from the two constructors (`0x019E0790` default, `0x019E0A30(u16)`),
the init helper `0x019E03C0` and the destructor `0x019E0CD0`:

| off | type | access that proves it |
|---|---|---|
| `+0x000` | vftable = `0x032F6CBC` | `019E07C0 mov dword[esi], 0x32f6cbc` (both ctors + dtor) |
| `+0x004` | protected/obfuscated sub-object | ctor `0x27BEA30`, dtor `0x27BEC00` |
| `+0x008` | **u16 serverId_** | `019E0C6E mov word[esi+8], dx` from ctor arg; default ctor writes `0xFFFF` (`019E09CF`) |
| `+0x00C`,`+0x010` | dword, dword | `019E03E9/019E03EC` zeroed |
| `+0x014` | **embedded user-info record, 0x3E8 bytes** (`+0x14`..`+0x3FB`) | ctor `0x2A497D0`, dtor `0x551700`, copy-assign `0x551940`; accessor `0x027C6F20 = lea eax,[ecx+0x14]; ret` |
| `+0x3FC` | sub-object, 0x30 B: `{dword, dword id(=1), dword, vector@+0x0C, vector@+0x20}` | ctor `0x019DFDD0`, dtor `0x019DFEC0`, reset `0x019E00C0` |
| `+0x400` | table key, default 1 | `019E044A mov dword[esi+0x400],1` |
| `+0x404` | value looked up in table #3 | `push 3; call 0x19E5E60; call 0x2B62500([esi+0x400]); mov [esi+0x404],[eax+4]` |
| `+0x430`,`+0x434` | dword | `019E07E7/019E07ED` |
| `+0x438` w, `+0x43A` b, `+0x43C`,`+0x440`,`+0x444` d, `+0x448` w, `+0x44A` b, `+0x44C`,`+0x450`,`+0x454`,`+0x458`,`+0x45C`,`+0x460` d, `+0x464` b, `+0x468`,`+0x46C` d | scalars zeroed in ctor | `019E07F3`–`019E085D` |
| `+0x470` | **CNCharacter\* (world avatar)** | getter `0x019DF190` (74 callers), setter `0x019DF0B0`, created `0x019DF5C0`, released `0x019DEF90` |
| `+0x474` | **CNCharacter\* (bound character)** | `setCharacter` `019DE9FB mov [edi+0x474], ecx`; released `0x019E0F80` |
| `+0x478` | heap copy of the user-info record | freed as `0x552FA0` dtor + `free` in `0x019DF3A0`; setter `0x019DAC20`, getter `0x019DAC30` |
| `+0x47C`/`+0x480` | obfuscated dword (value/key pair) | `019E03CC lea ecx,[esi+0x47c]` then `0x27BBBE0` family |
| `+0x484`..`+0x498` | scalars | accessors `0x019DB040`–`0x019DB0A0`, `0x019DB520` |
| `+0x49C` | sub-object w/ 7-arg draw | ctor `0x1151B70`, thunks `0x019E0560`, `0x019E0770 → 0x2B868E0` |
| `+0x4A0`,`+0x4A4`,`+0x4A8` | vector guts of the `+0x49C` object | freed in dtor `019E0DC8`–`019E0DF1` |
| `+0x4B0` b, `+0x4B4` float, `+0x4B8` d(=-1), `+0x4BC` d(=2) | scalars | `019E0444`, `019E0491`, `019E04A4`, `019E04B4` |
| `+0x4C0`,`+0x4C4` | **int pair, both init -1** | `0x019DABF0` sets both, `0x019DABE0` tests `+0x4C0 != -1`; rendered in tooltip `0x019E0570` with string ids `0xB70C`/`0xB70D` |
| `+0x4C8` | container of progress entries `{+0x0C id, +0x10 cur, +0x14 max, +0x19 flag}` | ctor `0xBDEE70`, iterated by `0x019E00F0` formatting `%s %d/%d  ` |
| `+0x4DC` | **expertJobShop_** | `0x019DF8C0` dereferences it and logs `expertJobShop_->getOwner()` / `->isMine()` |
| `+0x4E0` | dword | accessors `0x019DADA0/0x019DADB0` |
| `+0x4E4` | object\* (released `0x22B0810` + virtual dtor) | `019DF56C`–`019DF593` |
| `+0x4E8` | object\* (released via vtbl+0xC) | `019DF58D`–`019DF5A7` |
| `+0x4EC` b, `+0x4F0`,`+0x4F4`,`+0x4F8` d(=-1) | scalars | `019E0921`–`019E0933` |
| `+0x4FC` | bool | `019DF5AD mov byte[esi+0x4fc],0` |
| `+0x500`,`+0x514` | containers | ctor `0x1076DD0`, dtor `0x2231720` |
| `+0x528`,`+0x52C` | dwords | accessors `0x019DB290`–`0x019DB2C0` |
| `+0x530` | keyed map of `{dword,dword}` pairs | set `0x019E0F10`, get `0x019E0F40`/`0x019E0F60`, all via `0x7DF780(this+0x530, &key)` |
| `+0x544` | list of records with an id at `+0x10` | ctor `0x13BEF90`, insert `0x019E0E50`, clear `0x019DFF20 → 0x104B5B0`, lookup `0x019E0300` |
| `+0x558`/`+0x55C` | **obfuscated bool** | set `0x019DF2A0`, get `0x019DF320` |
| `+0x560`,`+0x561` | bytes | `019E0502/019E04D5` |
| `+0x564` | protected sub-object | ctor `0x27BEA30` |
| `+0x568` | bool, gates the anti-tamper path | `0x019DD920 cmp byte[edi+0x568],0` |
| `+0x56C`(=1), `+0x570`(=0), `+0x574`(=-1), `+0x578`(=-1), `+0x57C` | 5-dword block set as a unit | `0x019DB340` copies a 0x14-byte struct into `+0x56C..+0x57C` |

### The embedded user-info record (`CNUser+0x14`)

Type shared with `CNRDUserInfoPacketHandler`'s stack temp (`0x013E7340`:
`sub esp,0x44C; lea ecx,[ebp-0x438]; call 0x2A497D0` — same ctor).

* `+0x00` — **character name**, a refcounted immutable `wchar_t*` handle.
  `0x00E46B20 = mov eax,[ecx]; ret` is its `c_str()`; `operator=` `0x027BF7C0`
  does `InterlockedIncrement/Decrement` on `ptr[-8]` and frees `ptr-8` at zero.
* `+0x04` — int, used as the frame index into `character/defaultfaces.img`
  (`0x019DFF30`, arg 5). Cache default is **14** (`0x019E11C0`: `mov [0x39855F4],0xE`).
* `+0x08` — int (cached to `0x03B3E008`).
* `+0x0C`,`+0x10`(byte),`+0x14`(obfuscated),`+0x1C` — copied by `0x551940`.
* `+0x1C` — int; in `CNUser` terms `+0x30`. `0x019DFB20` branches on it: values `0` or `6`
  use the `+0x470` character, anything else uses `+0x474`.
* `+0x20`..`+0x11F`, `+0x120`..`+0x19F`, `+0x1A0`..`+0x21F` — three 32-entry dword arrays
  (`0x551A46`, `0x551A66`, `0x551A80` copy loops with `edx = 0x20`) — equipment/avatar slots.
* `+0x220`,`+0x224`,`+0x228`,`+0x22C`(byte), `+0x22D`..`+0x2AC` (32 dwords),
  `+0x2B0` (obfuscated), `+0x2B8` (obfuscated, read by `0x019DBA10`),
  `+0x3E0` (written by the packet handler at `013E7729`).
* Packet-handler field writes observed in `0x013E7340` (record-relative):
  `+0x04, +0x08, +0x0C, +0x1C, +0x220, +0x224, +0x228, +0x2D0, +0x2F4, +0x2F8, +0x2FC,
  +0x368, +0x36C, +0x370, +0x372, +0x3E0`.

## Globals this file owns

| VA | meaning | proof |
|---|---|---|
| `0x03B3DFB8` | **g_myCharacter** (1740 data xrefs image-wide) | written only after the serverId check in `setCharacter` `019DEB17 mov [0x3b3dfb8], edi` where `edi = this->+0x474` |
| `0x03B3DFBC` | secondary "current" character (253 xrefs) | `0x019DF0B0` `019DF15B mov [0x3b3dfbc], edx` from `+0x470`; cleared in `0x019DF3A0` |
| `0x03B3E190`/`0x03B3E194` | **obfuscated `myServerId_`** | read through the 5-way dispatch `0x27BC0F0/160/1D0/240/390` in `setCharacter`, `getCharacter`, `getMyCharacterCacheData`, and `CNUserManager::getMyUser` |
| `0x03B3E000` | cached `CNCharacter*` | `saveMyCharacterCacheData` `019E13E8` |
| `0x03B3E210` | cached character name (`std::wstring`, `_Bx@+4`, `_Mysize@+0x14`, `_Myres@+0x18`) | `019E1401 mov ecx,0x3b3e210; call 0x43C5B0` |
| `0x03B3E004` | cached `record+0x1C` | `019E141A` |
| `0x039855F4` | cached `record+0x04`, reset value **0xE** | `019E1427`, `019E1280` |
| `0x03B3E008` | cached `record+0x08` | `019E143B` |
| `0x03B3E180` | lazily loaded `character/defaultfaces.img` | `0x019DFF30` |
| `0x03A5C980` | `CNUserManager*` singleton | `mov ecx,[0x3a5c980]; call 0x19E3290` = `getMyUser()` |
| `0x03A5C9BC/9D0/9D4/9D8` | four values carried from the old character to the new one across `setCharacter` | `0x1833EB0/EC0/ED0/EE0` read → globals; `0x1833E00/E10/E20/E30` write into the new one; `0x1833E70/E80/E90/EA0` read back |

## Key functions

| VA | name (evidence) | what it does |
|---|---|---|
| `0x019DE9D0` | **`CNUser::setCharacter`** (literal at `0x032F6A24`) | `this->+0x474 = c`; stamps `this->serverId_` into `c`'s obfuscated serverId slot (`c->vtbl[0x30]()`); refuses and logs `Error: serverId_(%d) != myServerId_(%d)` (line 1188) unless `serverId_ == myServerId_`; on success publishes `g_myCharacter = c`, allocates a 0xB8-byte and a 0xC4-byte companion, calls `c->vtbl[0xC10/0xC14/0xC04/0xC00/0xB84]`, refreshes UI window id `0x89`, and migrates the 4 values above from the previous character. `ret 4`. |
| `0x019DF800` | `CNUser::setCharacter(c, bool publish)` | `publish` → tail-calls `0x019DE9D0`; else the quiet path: `[ecx+0x474]=c` plus the obfuscated serverId stamp, **no global publish, no checks**. |
| `0x019DF190` | **`CNUser::getCharacter`** (74 callers, the module's hot getter) | returns `[this+0x470]`; on the way it reports to `CNUserManager` (`0x019E3050` / `0x019E30E0`) when `serverId_` disagrees with `myServerId_` — anti-tamper telemetry, no functional effect. |
| `0x019DF0B0` | `CNUser::setCurrentCharacter` | `[this+0x470]=c`; mirrors to `0x03B3DFBC` only when `serverId_ == myServerId_ && !this->0x019DD920()`. `ret 4`. |
| `0x019DF5C0` | **`CNUser::createCharacter`** (31 callers) | if `+0x470 == 0`: `operator new(0x1104)` → ctor `0x0179FDB0` (vftables `0x032E3AA4` / `0x032E3A54@+8`, RTTI `TD#a70b`; ctor lives just after the `CNRDVirtualCharacter.cpp` anchor `0x01799F20`), then `setCurrentCharacter`, then **`c->vtbl[0x2E8](&this->m_info /*+0x14*/, 0)`** — the character is *built from the user-info record* — then `c->setOwner(this)` (`0x1783850`, writes `character+0x578`), `0x1783D50(c)`, `0x1794270(c, this+0x56C)`. Returns `[this+0x470]`. |
| `0x019DEF90` | `CNUser::releaseCharacter470` | unregisters `+0x470` from the world/scene, virtual-deletes it, `[this+0x470]=0`. |
| `0x019E0F80` | `CNUser::releaseCharacter474` | same for `+0x474`; only if the pointer is in the live-object registry `0x039A7614` and is **not** `g_myCharacter`; also clears up to 3 slots in the `+0x478` record copy (`0x551640` / `0x551610`, loop `edi<3`). |
| `0x019DF3A0` | `CNUser::clear` | frees `+0x478`, clears `0x03B3DFBC`, releases `+0x470` and `+0x474`, releases `+0x4E4`/`+0x4E8`, `+0x4FC=0`. |
| `0x019DFB10` | **`CNUser::setUserInfo(const Info&)`** (19 callers) | `this->m_info = src` via `0x551940`. Fed directly by `CNRDUserInfoPacketHandler` parsers `0x013E7340` (26 stream reads) and `0x013E8AF0` (37), plus `GameCoreNetwork_NOTI_Etc` `0x01164800`/`0x011751C0` and `CNRDTagSystem` `0x01614FC0`. |
| `0x019DF8C0` | **`CNUser::startExpertJobShop`** (literal `0x032F6B18`) | places the 전문직업 street shop held at `+0x4DC`. Reads `(a,b,x,y)` from the shop via `0xEC9CA0`; if `a == 8` substitutes the current map id. `0xEC9EE0()` returns a placement kind: kind **2** → resolve owner id (`0xEC9ED0` → `0x22B0820`), get the UI object via `this->0x019DAE30(owner)`, position it with `obj->vtbl[0xA0](x,y,0)`, `0x22B1E60`, `0x22B07F0(obj, owner)`, then `this->0x019DB790()`. Other kinds → require current map == `(a,b)`, then `shop->getOwner()` (`0x2335A20`) → `getCharacter()`, `shop->vtbl[0xAC](x,y,0)`, `character->vtbl[0x300](x,y)`, `0xEC9C40(shop,1)`. Failure logs `전문직업 상점 생성 실패 : expertJobShop_->getOwner() == %p` (line 2108) and `… ->isMine() == %s` (line 2111, `isMine` = `0xEC9C20`). Callers are all packet parsers: `GameCoreNetwork_CMD_Town 0x01119280`, `NOTI_Etc 0x01187FF0`, `NOTI_Town 0x01191D80`. |
| `0x019DFB20` | `CNUser::playGrowUpEffect` | plays `Common/CommonEffect/Animation/growup00.ani` at the character's `(x, y, z+h)` and `Common/CommonEffect/Animation/growup_weaponeff.ani` at a height scaled by the float at `0x030ACEF8`; each effect is a `new(0x2D4)` object. Chooses `getCharacter()` vs `+0x474` on `record+0x1C ∈ {0,6}`. Callers: `GameCoreNetwork_CMD_Town 0x01119280`, `NOTI_Etc 0x01169500`. Grow-up = the job/awakening growth cue. |
| `0x019DFF30` | `drawDefaultFace(x, y, dx, dy, faceIdx, surface, alpha)` | lazily loads `character/defaultfaces.img` into `0x03B3E180` and draws frame `faceIdx`; centres with `(0x18 - dx)/2`. |
| `0x019E0240` | **virtual slot 1** of `CNUser` | draws the player portrait: if `+0x470` exists use `char->vtbl[0x30C]/[0x308]`, else fall back to `drawDefaultFace(..., this->+0x18, ...)`. `ret 0x1C` (7 args). Overridden by `0x019DAC40` in the subclass. |
| `0x019E00F0` | tooltip progress list | walks `+0x4C8`, appends `%s %d/%d  ` (name from `0x10CDCA0(id)→0x171C570→0x28A2F20`, then `+0x10`/`+0x14`) into the caller's wstring. Called with `0x019E0570` from `0x01368FA0`. |
| `0x019E0570` | tooltip header | appends string-table entries `0xB70B`, `0xB70C(+0x4C0)`, `0xB70D(+0x4C4)`, separated by literal `0x03048840`. |
| `0x019E0300` | `hasDungeonEntry(idx)` | maps `idx` through the current map (`0x271C7B0`) and scans the `+0x544` list for `entry->+0x10 == id`. Callers: `CNUIControlWorldmapBalloonForRDAR.cpp`, `CNSelectDungeonModule.cpp` — this list drives the world-map balloon. It is filled by the 195-read `NOTI_Etc` parser `0x01160280` (insert `0x019E0E50`, clear `0x019DFF20`). |
| `0x019E0790` / `0x019E0A30` | **ctors** (`CNUser()` → `serverId_=0xFFFF`; `CNUser(u16 serverId)`) | both write vftable `0x032F6CBC` then call the shared init `0x019E03C0`. |
| `0x019E0CD0` | **dtor**; `0x019E0EA0` scalar-deleting dtor (vslot 0) | |
| `0x019E0E80` / `0x019E0ED0` | ctor/dtor of a **`CNUser` subclass**, vftable `0x032F6CD8` | adds no data members; overrides only vslot 0 and vslot 1 (`0x019DAC40`, which draws the `+0x474` character's own portrait). Instantiated once, at `0x01819E50`, by a large object that wraps a character in a synthetic user (`new 0x580`, `setUserInfo(rec)`, `setCharacter(character)`). |

### `CNUser` vftable `0x032F6CBC` (6 slots, RTTI COL at `0x032F6CB8` → `0x03730640`)

`0x019E0EA0` (deleting dtor), `0x019E0240` (portrait draw, 7 args),
`0x019DCFB0`, `0x019DAEF0`, `0x019DAF00`, `0x019DB8A0`.
Subclass vftable `0x032F6CD8`: `0x019E0ED0`, `0x019DAC40`, then the same four.

## MyCharacterCache — the one real state machine here

Three free functions plus a predicate. Purpose: keep the `CNCharacter` object alive across
a scene/channel transition so re-entering the *same* character on the *same* server skips
a full rebuild.

* `0x019E12A0` **`CNUser::saveMyCharacterCacheData()`** (line 3507/3530)
  1. `CNGame::getState()` (`0x01102C30 → 0x01637C60`, `0x0163D610 = mov eax,[ecx+0xF48]`).
     States **{4–10, 13–16, 27, 31, 36}** abort; everything else proceeds
     (jump table `0x019E1520`, index table `0x019E1528`, base = state−4).
  2. Requires `CNUserManager::getMyUser()` non-null and `myUser->+0x474 != 0`.
  3. Snapshots: `0x03B3E000 = myUser->+0x474`, `0x03B3E210 = name` (`record+0`),
     `0x03B3E004 = record+0x1C`, `0x039855F4 = record+0x04`, `0x03B3E008 = record+0x08`.
     Logs `MyCharacterCache(Save) - %s`.
  4. Then **`myUser->setCharacter(NULL)`** — the object is detached, not destroyed.
  * Failure path logs `MyCharacterCache(Save Failed) - %d` with the map/room id from
    `0x0163E1E0`, and calls the reset.
  * Callers: `CNUserManager.cpp` `0x019E3440`, `0x019E4CB0`.
* `0x019E1550` **`CNUser::getMyCharacterCacheData(const Info* info, u16 serverId)`**
  (`__cdecl`, 2 args). Returns the cached `CNCharacter*` only if **all five** match:
  1. cached name is non-empty, and 2. cached name `==` `info->name`;
  3. `0x03B3E004 == info->+0x1C`; 4. `myServerId_ == serverId` (arg, `word [ebp+0xC]`);
  5. `0x039855F4 == info->+0x04`; 6. `0x03B3E008 == info->+0x08`.
  Match → `MyCharacterCache(Get) - %s`, return `0x03B3E000`.
  Any mismatch → `MyCharacterCache(Reset) - %s`, run the reset, return 0.
  `info == NULL` → `MyCharacterCache(Get Failed) - null`, return 0.
  Same state gate as the save (`0x019E1860`). Single caller: `0x013E1EC0`, which also calls
  `setCharacter` — the enter-game path.
* `0x019E11C0` **`resetMyCharacterCacheData()`** — clears the name, `0x03B3E004=0`, and if
  the cached pointer is still in the live-object registry `0x039A7614`, invokes its
  **virtual deleting destructor** (`vtbl[0](1)`); then `0x03B3E000=0`, `0x039855F4=0xE`,
  `0x03B3E008=0`.
* `0x019E0710` — same clear **without** deleting the object. Called from `Main.cpp`.
* `0x019DB2E0` `isMyCharacterCached()` — `myUser->+0x474 == 0x03B3E000`.

## Public surface — who calls into this module

162 external callers. Grouped by the caller's own `__FILE__` (from `func-source-file.csv`;
`[P]` = the caller is in `parsers.csv`, i.e. it reads the inbound stream):

| entry | notable external callers |
|---|---|
| `setCharacter 0x019DE9D0` | `CNRDUserInfoPacketHandler.cpp` ×3 `[P]` (`0x013E6370`, `0x013E69C0`, `0x013E6CD0`, 6 reads each), `0x013E1EC0` (enter-game), `CNEntryDungeonPartyManager.cpp`, `CNRDUdpModule.cpp`, `GameCoreDebug.cpp` |
| `getCharacter 0x019DF190` | 72 callers, ~26 of them parsers: `GameCoreNetwork_NOTI_Etc.cpp` ×10 `[P]`, `_NOTI_Town` ×3 `[P]`, `_NOTI_Dungeon` ×2 `[P]`, `CNRDUserInfoPacketHandler` ×5, `CNRDMatchingUIManager`, `CNTownModule`, `CNRDObject`, `NewNPCDialogWindow` |
| `createCharacter 0x019DF5C0` | 30 callers incl. `NOTI_Town 0x0118AC60` (**dispatch msg id `0x26A`**), `NOTI_Town 0x01191D80`, `NOTI_Etc 0x01163410`/`0x011751C0`, `CNRDMatchingUIManager 0x01211080`, `CNUserManager 0x019E30E0` |
| `setUserInfo 0x019DFB10` | `CNRDUserInfoPacketHandler 0x013E7340`/`0x013E8AF0` `[P]`, `NOTI_Etc 0x01164800`/`0x011751C0` `[P]`, `CNRDTagSystem 0x01614FC0` `[P]`, `CNRoom`, `CNPVPModule`, `GameCoreSub` |
| `setCurrentCharacter 0x019DF0B0` | `NOTI_Etc 0x01164800` `[P]`, `CNRoom`, `CNPVPModule` |
| `startExpertJobShop 0x019DF8C0` | `CMD_Town 0x01119280` `[P]`, `NOTI_Etc 0x01187FF0` `[P]`, `NOTI_Town 0x01191D80` `[P]` |
| `playGrowUpEffect 0x019DFB20` | `CMD_Town 0x01119280` `[P]`, `NOTI_Etc 0x01169500` `[P]` |
| obf-bool set/get `0x019DF2A0`/`0x019DF320` | `GameCoreNetwork_CMD_Dungeon.cpp` `[P]`, `NOTI_Etc` `[P]`, `CNRDBaseInterface.cpp` |
| `+0x544` list insert/clear + `+0x530` set | `NOTI_Etc 0x01160280` `[P]` (195 stream reads) |
| `+0x49C` accessors `0x019E0560`/`0x019E0770` | `NOTI_Dungeon 0x01151CC0` `[P]` |
| `ctor(serverId) 0x019E0A30` | `CNUserManager 0x019E5490`, `NOTI_Etc 0x011751C0` `[P]`, `CNRDTagSystem 0x01614FC0` `[P]` |
| `saveMyCharacterCacheData` | `CNUserManager.cpp` only |
| `getMyCharacterCacheData` | `0x013E1EC0` only |

## What a server implementer should take from this

1. **`serverId` is a first-class field on every player object and is validated locally.**
   The client stores its own `myServerId_` in an obfuscated global and *refuses* to bind a
   character to a `CNUser` whose `serverId_` differs (`setCharacter` bails and logs). A
   server that sends a user record with the wrong serverId gets a silent no-op plus a
   tamper report to `CNUserManager`, not a visible error.
2. **The user-info record is the wire payload.** `CNUser+0x14` is the exact struct that
   `CNRDUserInfoPacketHandler` deserializes; `setUserInfo` is a plain struct copy. Its
   fields the client keys on: name (`+0x00`), face/appearance index (`+0x04`), `+0x08`,
   and `+0x1C` — those four plus serverId form the cache identity below.
3. **Character-object reuse across scene transitions.** On leaving, the client caches the
   character object and detaches it; on entering it re-adopts the *same* object when
   `(name, info+0x1C, info+0x04, info+0x08, serverId)` all match. If the server changes any
   of those between the leave and the re-enter, the client destroys and rebuilds. There is
   no server-visible handshake — but a server that mutates e.g. the appearance field during
   a channel move will force a rebuild path it may not otherwise exercise.
4. **`createCharacter` is where the record becomes an avatar** — `c->vtbl[0x2E8](record, 0)`.
   Anything the server wants rendered on the character must be in the record before the
   packet handler calls `setUserInfo`/`createCharacter`.
5. **Expert-job shop placement** needs `{kind, mapId(a), b, x, y, ownerId}`; kind `2` places
   without a map check, other kinds require the viewer to be on the same `(a,b)` map, and
   `a == 8` means "current map". A shop whose owner is not resolvable produces the
   `getOwner() == %p` log and no stall appears.

## Uncertain / not established

* No RTTI class *names* survive (`TD#a70b`, `TD#f806` are anonymized), so "CNUser" comes
  from the `__FILE__` literal and the log strings `CNUser::setCharacter`,
  `CNUser::startExpertJobShop`, `CNUser::saveMyCharacterCacheData`,
  `CNUser::getMyCharacterCacheData` — not from a demangled symbol.
* Which of `+0x470` / `+0x474` is "the" character is asymmetric but not fully named:
  `+0x470` is client-constructed from the record and is the hot getter; `+0x474` is
  assigned from outside and is what `g_myCharacter (0x03B3DFB8)` mirrors.
* The `CNGame` state enum values in the cache gate are raw integers; the enum is not in
  this file.
* Semantic names for `record+0x04` ("face index" is inferred from
  `character/defaultfaces.img` frame use, default 14) and for the `+0x4C0/+0x4C4` pair
  (rendered with string ids `0xB70C/0xB70D`, values not resolved) are **unknown**.
* `0x027D7F30` is used both as an image-draw and as a two-argument format-ish call
  (`0x019DEE20`); I did not pin its signature, so `0x019DEE20` / `0x019DEEE0`
  ("produce a value derived from `record+0x2B8` via resource-table entry `0x24` at
  `0x03A5AD40`, write into the caller's buffer, return it") stay unnamed.
