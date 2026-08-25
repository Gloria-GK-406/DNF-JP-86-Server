# TU 14 — `DNFClient\RDAR\CNRDDungeon.cpp`

Span from briefing: `0x01723D70`–`0x017467F0`, 347 functions, 0 parsers.
**Real extent is `0x01723D70`–`0x017484F0`**: the `hi` anchor `0x017467F0` is the *entry*
of `CNRDDungeon::createMap`, whose body runs to `0x017484F0` (`ret 0x18` at `0x017483DC`).

## Verdict on the span

The span is honest. Evidence:

* All four `__FILE__` anchors inside it name this file, and `func-source-file.csv` places
  **no other file's anchor** anywhere in `0x01720D70`–`0x017497F0`.
* Mid-span functions are provably methods of the same object:
  * `0x01746170` writes `*this = 0x032DA414` (vptr) — constructor.
  * `0x0173F3A0` writes the *same* vptr `0x032DA414` — destructor.
  * `0x01736A20` (called by the ctor at `0x017466B9`) sets `this+0xA40 = this+0xA44 = -1`.
  * `0x0172BA90` reads `this+0x174` and re-queries the script loader `[0x3A5C940]`.
* TU 22 (`CNRDObject.cpp`, `0x0164F14B`–`0x017703A0`) engulfs this whole TU. **That** span is
  the broken one, not this one.

Caveats worth recording:

* `0x0172A070`–`0x0172A17F` inside the span is a **VMProtect-mutated stub** (`jmp 0x51C6D7D`,
  then garbage). Do not try to read it as x86.
* 28 direct calls from this TU land in `.vmp1` (18 distinct targets). The hottest,
  `0x0520FE26` (12 call sites), is a virtualized `__thiscall` accessor on the dungeon.

## What this unit implements

`CNRDDungeon` — the **client-side instance of one dungeon run**. It is built by copying a
parsed dungeon *script* record into itself, owns the current map coordinate, drives per-map
construction (monsters, objects, ambience, hell rooms), holds the server-pushed per-run state
(spawned items, named-monster queue), and renders the dungeon title card between maps.

Source line numbers recovered from log calls: `create` ≈ L630/L650, `prepareSetDrawTitle` ≈ L1729,
`drawNextTitle` ≈ L1907, `createMap` ≈ L2243–L2538. The file is ≥ ~2540 lines.

---

## 1. Things a server implementer needs

### 1.1 `CNRDDungeon::createMap` — `0x017467F0`

```
__thiscall createMap(int mapX, int mapY, bool a3, int a4, void* a5, bool a6)   // ret 0x18
```

Arg count proven by `ret 0x18` at `0x017483DC`; `[ebp+0x14]`, `[ebp+0x18]`, `[ebp+0x1C]` are all
read (`0x017483A9`, `0x01748226`, `0x01747C44`).

**The linear map index formula the server must match:**

```
cols  = vmp_0x0520FE26(dungeon)          // virtualized accessor, map-grid row stride
index = cols * mapY + mapX
```

Proven at `0x0174690F` (`imul esi,[ebp-0x80]` = arg2; `add esi,ebx` = arg1) and independently in the
free function `getMap(x,y)` at `0x01720870`:

```
01720883  mov eax,[0x3A8F5E4]      ; grid.begin
01720888  cmp eax,[0x3A8F5E8]      ; grid.end   -> empty => NULL
01720890  call 0x520FE26           ; cols
01720895  imul eax, esi            ; * mapY
017208A6  add  eax, edi            ; + mapX
017208AF  mov  eax,[ecx+eax*4]     ; CNRDMap*
```
Negative x or y, or index >= size, returns NULL. Same formula at `0x01725B5E`, `0x01728334`,
`0x01728383`, `0x01728679/8E/B8`, `0x01737146`, `0x01746AE7`, `0x01746B17`, `0x017470F8`, `0x01747883`.

**Global per-map parallel arrays** (all `vector<T*>`, indexed by that same linear index):

| begin / end | used for |
| --- | --- |
| `0x3A8F5E4` / `0x3A8F5E8` | the map objects themselves (`getMap`) |
| `0x3A8F5F8` / `0x3A8F5FC` | a per-map side object; `0x017284B0` requires it non-null |
| `0x3A8F620` / `0x3A8F624` | third per-map array, touched in `createMap` |
| `0x3A8F608` (+ count `0x3A8F614`) | keyed container (`0x44FBE0` lookup) |

All are constructed by the static initialiser `0x02DF3903`. They are *globals*, not members —
the client only ever has one dungeon grid live.

**Callers of createMap** (`peek --callers 0x017467F0`): `0x01157A70` ×4 — the
`GameCoreNetwork_NOTI_Dungeon.cpp` handler for
`NOTIFUNC_ENUM_NOTIPACKET_START_MAP` / `ENUM_NOTIPACKET_ADVANCE_ALTAR_START_MAP` /
`ENUM_NOTIPACKET_START_BOSS_TOWER_MAP`; plus `0x00735E00` (`CNEntryDungeonPartyManager.cpp`),
`0x00C4F450`, `0x00E105E0`, `0x00E11710`, `0x01630A00` ×2, `0x026D4A80`, `0x026FBDB0`, `0x0271FC30`.

At the START_MAP call site `0x01158827`, `mapX`/`mapY` are the **first two `u8` fields** of the
packet, read at `0x01157C5E` and `0x01157C8A` (`read_u8` = `0x027C5230` in this dump) into
`[ebp-0x412]` / `[ebp-0x411]`, then pushed as arg1/arg2 at `0x01158824`/`0x01158823`.

`createMap` also:
* loads the APC difficulty table (below);
* spawns AI characters — `0x02118F60` (`createAICharacter`, `AICharacterFactory.cpp`) at `0x01747DDC`,
  registered via `CNRDObjectManager::addObject` `0x0191E450` at `0x0174796C` / `0x01747EBD`;
* runs the hell-room evaluator `0x01745520` at `0x0174820E`;
* plays `EVILTOWER_WALK_LOOP` (`0x01747035`, sound API `0x01648A20`) when the game-module id
  (`0x0163E1E0`, `GameCoreSub.cpp`) is `9` or `0xD`;
* logs `Map Script Ting : [Map Pos (%d, %d)] [IsValidMapLayer = %d]` at source line `0x9EA`.

### 1.2 Difficulty / bonus tables loaded by this unit

| file | loaded at | destination |
| --- | --- | --- |
| `Monster/MonsterApcDifficultyBonus.tbl` | `createMap` `0x01746882` → `0x02A655A0` | table loader whose section literals are `[normal]`, `[slayer]`, `[king]` |
| `Etc/DifficultPenalty.etc` | `create` `0x01743A49` → `0x029BBF20` | **global `int[5]` at `0x3A391C0`** (address returned by `0x010CBBB0`) |
| `Etc/RandomHell.etc` | `0x01745520` `0x01745781` → `0x02A12130` | hell-group table; loader literals `[level]`, `[apc list]`, `HELLPARTY_LOG : GROUP INDEX NOT FOUND : %d` |

`createMap` overrides the `.tbl` name from `*(char**)(this+0x6D4)` when that string is non-empty
(`0x0174683E`–`0x01746871`).

**Dissonance, recorded not resolved:** the `Etc/DifficultPenalty.etc` reader `0x029BBF20` scans the
file for a line equal to the literal at `0x0363C064`, which `literals.csv` decrypts to
`[COMMON TRAP EXPLOSION]`, then parses the next five ints into the caller's `int[5]`. The tag does
not obviously belong to a "difficulty penalty" file. Either the file carries that section, or the
decryption of `0x0363C064` is wrong. I did not resolve it. The reader returns error `0x224B` when
the file cannot be opened.

### 1.3 Hell mode — `0x01745520` (`ret 0x14`, 5 stack args + `this`)

Called only from `createMap` (`0x0174820E`). Gate chain, in order:

```
01745685  secure_get(this+0xAF4[0]) == this->m_mapX (+0xA40)   ; else bail to 0x1745D57
017456EF  secure_get(this+0xAF4[1]) == this->m_mapY (+0xA44)   ; else bail
017456FE  this->+0x8D4 != 0 || this->+0x9FD != 0               ; else bail
01745714  global byte [0x3A5CA0A] != 0                          ; else bail
0174576A  load "Etc/RandomHell.etc" -> 0x2A12130
01745786  level = (0x19E7BA0 on singleton [0x3A5C990])
01745792  group = lookup(table, level)   ; 0x29C0FC0 ; NULL => 0x1745CC8
017457B9  map   = getMap(this->+0xA40, this->+0xA44)   ; 0x1720870
```

So the current map is a hell room **iff the protected coordinate pair at `this+0xAF4` equals the
current map coordinate**, the dungeon-level hell flags `+0x8D4`/`+0x9FD` are set (both copied from
the script, see §2), and the global hell enable at `0x3A5CA0A` is on. Group selection is by
character level against `RandomHell.etc`.

The randomised spawn position uses `0xC30050(0xC8 + 0xC8*k, -1, 3)` and
`mulsd xmm0,[0x32DA570]` then `>>1` — a bounded retry loop probing successive 200-unit bands
(`0x017457C2`–`0x0174586B`) against a validity predicate `vtbl+0x11C` of `[0x3B3DFB8]`.

### 1.4 Server-driven state written into the dungeon

| entry point | signature | container | fed by |
| --- | --- | --- | --- |
| `0x01728460` | `add(u32 a,b,c,d,e)` `ret 0x14` | `vector<20-byte>` at `this+0xA60`, `push_back` = `0x029472D0` (elem size `0x14`) | `NOTI_Dungeon 0x01157A70` (START_MAP item loop) and `NOTI_Etc 0x01163410` |
| `0x017288B0` | `add(struct[0x5C] byval)` `ret 0x5C` | container at `this+0xC14`, stride `0x5C` | `NOTI_Dungeon 0x01146000` (the 287-read master dispatcher) |
| `0x0172A180` | `ret 0xC` | drains list at `this+0xC30` | `NOTI_Dungeon 0x01154810` (`NOTIPACKET_DIE_MONSTER`) |
| `0x0172A040` | `ret` | clears `this+0x4`, `+0x18`, `+0x2C` | `NOTI_Dungeon 0x01151CC0` (`DUNGEON_INFO`), `0x01157A70` |
| `0x01726D30` | `add ecx,0xA88; jmp 0x1725DF0` | container at `this+0xA88` | 7 callers, mostly `NOTI_Dungeon` |

**The 20-byte record at `this+0xA60`** is stored verbatim in wire order. In the START_MAP handler
the five `u32` are read consecutively at `0x01158665`, `0x01158671`, `0x0115867D`, `0x01158689`,
`0x01158695` and pushed in the same order at `0x011586AF`–`0x011586BF`; the loop is gated on a `u8`
flag (`[ebp-0x424] > 0`) then a `u8` item count (`[ebp-0x41B]`). The handler's log format is
`서버생성아이템 : ScriptLineNum %d, ItemUniqueId %d, ItemIndex %d, Data %d, Durability %d, amplify type %d, amplify value %d`
— five stored fields against seven names, so the mapping to the first five names is *inference*,
not proof.

**The `0x5C`-byte record at `this+0xC14`** — first three fields proven by the scan in `0x017259B0`
(`rep movsd ecx=0x17` copies `0x5C` bytes, then):

```
+0x00  int   kind      ; 0x01725B2E  compared to 0
+0x04  int   mapX      ; 0x01725B10  compared to this->+0xA40
+0x08  int   mapY      ; 0x01725B22  compared to this->+0xA44
...            (total 0x5C)
```
When a record matches the current map and `kind == 0`, `0x017259B0` fetches the map object,
calls its `vtbl+0x6C`, and if that returns true wakes four UI singletons
(`0x3A5C908`, `0x3A5C920`, `0x3A5C924`, `0x3A5C90C`, each via `0x01764130(1)`) and then calls
the map's `vtbl+0x4A8`.

**Named-monster queue `this+0xC30`** — `0x0172A180` walks it, for each entry reads `+0x0C`/`+0x10`
and calls `0x02123BC0(+0xC, +0x10, 0, 1)`; also reads `+0x44`. Entries are **erased as consumed**
(`0x01728CC0` at `0x0172A4B3`). On success it plays sound `NAMED_APPEAR` (`0x0172A4EC`, sound API
`0x01648A20`) and runs a stage effect `0x0198CEF0(4, 0x7D0)` (2000 ms).

### 1.5 Map lookup exposed to the network layer

`0x017284B0` — `ret 0xC`, `f(int mode, int key, void** out)`:
* `mode == 4`: linear scan of the map grid; for each non-null map call `vtbl+0x4C`; when it equals
  `key` **and** the parallel array `0x3A8F5F8` has a non-null entry at that index, write that entry
  to `*out` and return the map.
* otherwise: walk `0x3A8F608` indices `0 .. [0x3A8F614]` and test each with `this->0x01722CB0`.

Callers: `CNEntryDungeonPartyManager.cpp 0x00732AE0`, `NOTI_Dungeon 0x01154810`,
`NOTI_Etc 0x01185710`, `CNRDItemManager.cpp`.

---

## 2. `CNRDDungeon::create` — the script→instance field map

```
0x01746170  CNRDDungeon::CNRDDungeon(int dungeonIndex)          // ret 4
  0174619F  *this = 0x032DA414                                   // vptr
  ...       member construction (see §3)
  017466B9  this->reset()            = 0x01736A20
  017466C2  name = ((container*)0x4E1F208)->find(dungeonIndex)   // 0x28A84B0, RDARScript.cpp
  017466D3  if (name) this->create(name)                          // 0x01741320
```

```
0x01741320  CNRDDungeon::create(scriptKey)                       // 16896 bytes
  0174135C  script = ((CNScriptLoader*)[0x3A5C940])->getScriptByFileName(scriptKey, 0)  // 0x27524F0
  0174136B  this->+0xBC0 = script
  ...       ~164 field copies
```

`0x027524F0`'s own literal is `CNScriptLoader::getScriptByFileName`. `[0x3A5C940]` is the loader
singleton (also used by `0x0172BA90` at `0x0172BAC0` with `this+0x174` as the key).

Selected copies (`ebx = this`, `esi = script`; full list is in the raw extract, 164 writes):

| this | ← script | at |
| --- | --- | --- |
| `+0xBC0` | `script*` itself | `0174136B` |
| `+0x174` | `+0x44` (int, script file key) | `01741377` |
| `+0x178` | `+0x48` (string) | `01741384` |
| `+0x17C` | `+0x68` (string) | `01741393` |
| `+0x1C8,+0x1CC,+0x1D0,+0x1D4,+0x1D8` | `+0x194,+0x1AC,+0x1B0,+0x1B4,+0x1B8` | `0174139E`–`017413CE` |
| `+0x154` vector\<int\> | `+0xB10..+0xB14` vector\<int\> (element-wise) | `01741400`–`017414A7` |
| `+0x1DC` | `+0x19C..+0x1A0`, 12-byte elements (`0x9D8EF0` push_back) | `017414D6`–`01741516` |
| `+0x1F0,+0x1F4` | `+0x20C,+0x210` | `0174151E,0174152A` |
| protected `+0x1F8` | **protected** `+0x214` | `01741530`–`017415A0` |
| `+0x140..+0x150` | `+0x220..+0x230` | `01741611`–`01741641` |
| `+0x474,+0x475` | bytes `+0x2CC,+0x2D8` | `0174166B,01741677` |
| `+0x49C` | `+0x310` | `01741683` |
| `+0x674` | `+0x838` | `0174168F` |
| `+0x30,+0x34,+0x38` | `+0x20,+0x24,+0x28` | `01742565`–`01742571` |
| `+0x168,+0x16C,+0x170` | `+0x2C,+0x30,+0x34` | `0174264B`–`0174265D` |
| `+0x4A0,+0x4A4` | bytes `+0x5B1`, sbyte `+0x5B3` | `01743107,01743114` |
| `+0x448,+0x44C,+0x450,+0x454,+0x455,+0x457` | `+0x500,+0x2D0,+0x2D4,+0x501,+0x5B0,+0x503` | `01743140`–`0174317F` |
| `+0x578,+0x580,+0x58C,+0x594` | **bools** `!= 0` of bytes `+0x51D,+0x51E,+0x520,+0x521` | `01743931`–`01743961` |
| `+0x588` | byte `+0x51F` | `0174396E` |
| `+0x72C` | `+0x61C` | `0174397A` |
| `+0x55C` | `+0x604` | `0174398F` |
| `+0x574` | protected read of `+0x6D4` | `01743A0F` |
| `+0xA38` | `-1` (constant) | `01743A2B` |
| `+0x6DC,+0x6E0,+0x6E4,+0x6E8` | `+0x5C8(sbyte),+0x878,+0x880,+0x87C` | `0174453D`–`01744562` |
| `+0x8D4` | byte `+0x506` — **hell flag** | `01744F5D` |
| `+0x9FD` | byte `+0xB80` — **hell party flag** | `01745015` |
| `+0x9E0..+0x9E5` | bytes `+0xAF5..+0xAF9` folded through a min/max at `+0x9D8`/`+0x9DC` | `01744E09`–`01744F50` |
| `+0x958,+0x960,+0x8B8,+0x8B9` | `+0xA11,+0xA28,+0xA30,+0xA31` | `01744C10`–`01744C6A` |
| `+0xBF0` | `+0xA88` | `01744D18` |
| `+0xA04` | `+0xBFC` | `01745062` |

The dungeon title card is armed near the end of `create`:
`+0xB7A=1`, `+0xB76=0x1F6`, `+0xB75=1`, `+0xB78=1`, `+0xB7B=8`, `+0xB7C = <animation from
"UI/SelectDungeon/dungeon_title.ani">` (`0x01744AA7`–`0x01744AE1`). Failure paths log
`Entering Title Setting Error - %sfile` (L630) and `Entering Next Title Setting Error - %sfile` (L650).

### 2.1 Tamper-protected members (this is a reusable primitive)

`create` and the hell path use an obfuscated-integer type. Layout is **8 bytes**:

```
+0x00  DWORD handle   ; page = handle>>16, slot = handle & 0xFFFF
+0x04  DWORD cipher
```

Read (`0x027BC0F0` and its 4 alignment clones `…160/…1D0/…240/…390`):

```
key32 = *(DWORD*)( ((DWORD*)[0x3B89618])[9 + page] + 0x2114 + slot*4 )
k16   = (WORD)key32
plain = cipher ^ ((k16<<16) | k16)
check = ((WORD)(plain>>16) - k16 + (WORD)plain) ^ k16
if (check != (WORD)(key32>>16)) call [0x3601AB8]     // tamper callback
```

Write (`0x027BBBE0` + clones `…CA0/…D50/…E10`, and `0x027BBA60`): bumps counter `[0x3B89678]`,
derives a fresh `k16` from the two 256-entry word tables `0x36016B8` / `0x36018B8`, re-encrypts,
and rewrites the registry slot. Destructor is `0x027BC0E0` (thunked as `0x02075700`).
The `and eax,0xF; cmp eax,0xC; jmp [tbl]` prologue is a **decoy dispatch** — the key address is not
always the variable address, so do not infer offsets from it for the *setter* sites.

Protected members of `CNRDDungeon`, proven from the destructor's `__ehvec_dtor(ptr, 8, count, 0x2075700)`
calls (`0x2C63A54`):

| offset | count | evidence |
| --- | --- | --- |
| `+0xACC` | 1 | `0173F66C` direct `0x27BC0E0` |
| `+0xAD4` | 2 | `0173F65D` |
| `+0xAE4` | 2 | `0173F644` |
| `+0xAF4` | 2 | `0173F62B` |
| `+0xB04` | 2 | `0173F612` |

`+0xAF4[0]` and `+0xAF4[1]` (i.e. `+0xAF4` and `+0xAFC`) are the **hell room coordinates**, read at
`0x01745627` and `0x01745694` and compared against `this+0xA40` / `this+0xA44`.

---

## 3. Object layout — `CNRDDungeon`

vptr `0x032DA414`; ctor `0x01746170`; dtor `0x0173F3A0`; reset `0x01736A20`; size ≥ `0xC34`.

| offset | type | how proven |
| --- | --- | --- |
| `+0x00` | vptr = `0x032DA414` | `0174619F` (ctor), `0173F3CD` (dtor) |
| `+0x08`,`+0x0C` | pair written by `reset` | `01736AD1`, `01736ADD` |
| `+0x10` | ref-counted string; `reset` assigns the decrypted literal at `0x3043960` | `01736AB1` (`0x42ACB0` = assign) |
| `+0x3C` | sub-object, ctor `0x017331E0` (list, `0x58`-byte head node) | `017461E7` |
| `+0x50`,`+0x64`,`+0x7C`,`+0x90` | sub-objects, ctors `0x27118F0`, `0x13BEF90`, `0xBDEE70` ×2 | `017461F3`–`0174621A` |
| `+0xA4`,`+0xB8`,`+0xD0` | vector heads `{ptr, first, last, end}` | `0174621F`–`017462B8`, dtor `0173F885`/`0173F837`/`0173F819` |
| `+0xE4` | sub-object, ctor `0x1A15BA0` / dtor `0x1A15A70` | `017462C5`, `0173F801` |
| `+0x140` | title/loading drawer, ctor `0x173FB30` / dtor `0x173E700`; `+0x140+0x5F0/0x5F8/0x5FC` read by `prepareSetDrawTitle` | `017462D4`, `0173F7F2`, `01723D7F` |
| `+0x154` | `vector<int>` (`first/last/end` at `+0x158/+0x15C/+0x160`) | `01741429`–`01741499` |
| `+0x174` | int, script file key | `01741377`, reused `0172BABA` |
| `+0x178` | string* — **dungeon name**; logged after `" DungeonName : "` | `01741384`, `017476FC` (`0xE46B20` = c_str) |
| `+0x17C` | string* | `01741393` |
| `+0x180` | drawable, released by `setDrawTitle` | `01724DB3` |
| `+0x1DC` | container of 12-byte records | `01741509` |
| `+0x1F8` | protected int (from script `+0x214`) | `0174158E` |
| `+0x578`,`+0x580`,`+0x58C`,`+0x594` | bool flags gating map-entry post-processing in `createMap` | `01746917`, `017469B3`, `create` `01743931`+ |
| `+0x6D4` | string* — `.tbl` name override | `0174683E` (`0x27BE5F0`/`0x27BE600` = c_str/len) |
| `+0x720` | bool gate in `createMap` | `01747074` |
| `+0x8D4` | bool — hell enabled | `01744F5D`, `017456FE` |
| `+0x9FD` | bool — hell party | `01745015`, `01745707` |
| `+0xA08` | sub-object (dtor `0x27C33F0`) | `017462E3` |
| `+0xA38` | int, `-1` after `create` | `01743A2B` |
| **`+0xA40`** | **int current map X**, `-1` when no map | `01736A85` (reset), `01725B0A`, `017457B1` |
| **`+0xA44`** | **int current map Y**, `-1` when no map | `01736A8B` (reset), `01725B1C`, `017457AB` |
| `+0xA48` | sub-object, ctor `0x1083200` | `017462F2` |
| `+0xA60` | vector of 20-byte records (`{first,last,end}` at `+0xA64/+0xA68/+0xA6C`) | `01728460` → `0x29472D0`; dtor `0173F788` |
| `+0xA74` | vector head | `0174632B`, dtor `0173F74E` |
| `+0xA88` | vector head; wrapper `0x01726D30` | dtor `0173F714` |
| `+0xAA0`,`+0xAB4` | vector heads (elem dtor `0x2A57EC0`) | `0173F6B6`, `0173F671` |
| `+0xACC` | protected int | `0173F66C` |
| `+0xAD4`,`+0xAE4`,`+0xAF4`,`+0xB04` | protected int[2] each | `0173F65D/644/62B/612` |
| `+0xAF4[0..1]` | **hell room (X,Y)** | `01745685`, `017456EF` |
| `+0xB14` | bool draw-title flag | `01724DE6` |
| `+0xB18`,`+0xB38` | int params of `setDrawTitle` | `01724DEC`, `01724DF2` |
| `+0xB1C` | sub-object (dtor `0x27C33F0`, reset `0x27C3410`) | `01724DDB`, `0173F5F9` |
| `+0xB44`,`+0xB58`,`+0xB74`,`+0xB98`,`+0xB9C`,`+0xBA0` | vector / sub-objects / string | dtor `0173F5A7`–`0173F54F` |
| `+0xB75`,`+0xB76`,`+0xB78`,`+0xB7A`,`+0xB7B`,`+0xB7C` | title-card animation state (`1`, `0x1F6`, `1`, `1`, `8`, anim*) | `01744AA7`–`01744AE1` |
| `+0xB94` | drawable released by `setDrawTitle` | `01724DC4` |
| `+0xBC0` | `RDARScriptDungeon*` (the parsed script) | `0174136B` |
| `+0xBC8`,`+0xBE4`,`+0xBF8` | vector head + two refcounted objects (`lock xadd` release) | dtor `0173F4E5`, `0173F49B`, `0173F461` |
| `+0xC00`,`+0xC14` | vector heads; `+0xC14` holds the `0x5C`-byte server records | dtor `0173F43D`/`0173F403`, add `017288B0` |
| `+0xC30` | list of pending NAMED_APPEAR spawns (dtor `0x1728C30`) | `0173F3E9`, `0172A1FA` |

### Vtable at `0x032DA414` — exactly 13 slots

(slot 13 onward is the encrypted literal blob starting at `0x032DA448` = `Etc/DifficultPenalty.etc`)

| slot | target | in this TU? |
| ---: | --- | --- |
| 0 | `0x01032870` | no |
| 1 | `0x01730310` | yes |
| 2 | `0x019F88E0` | no |
| 3 | `0x017370B0` | yes |
| 4 | `0x0172AE60` | yes |
| 5 | `0x0171E220` | no |
| 6 | `0x0171C750` | no |
| 7 | `0x00BB82B0` | no |
| 8 | `0x0171F3D0` | no |
| 9 | `0x0171C760` | no |
| 10 | `0x02231720` | no |
| 11 | `0x0173B690` | yes |
| 12 | `0x0173A270` | yes |

RTTI/complete-object-locator pointer at `0x032DA410` → `0x0372F3EC` (`.code`), not followed.

---

## 4. Public surface (called from outside the TU)

120 of the 347 functions are reachable from outside; 246 distinct external callers. The
highest-degree ones (`0x017291F0` ×44, `0x017278F0` ×28, `0x01729160`, `0x01729AD0`, `0x01729240`,
`0x017292D0`) are **compiler-generated `_Destroy_range` helpers**, not API — they only tell you the
element sizes of vectors this TU instantiates: `0x1C`, `0x60`, `0x30`, `0x54`, `0x34`.

The real surface, by caller module:

| function | signature / role | external callers |
| --- | --- | --- |
| `0x01746170` | ctor `CNRDDungeon(int dungeonIndex)` | `0x004D0FA0`, `0x004D3C00`, `0x00C4D360`, `0x012E9C10`, `0x022C6CB0`, `0x022C7300`, `0x0273DE80` |
| `0x0173F3A0` | dtor | 13 |
| `0x017467F0` | `createMap(x, y, …)` — see §1.1 | 9, incl. `NOTI_Dungeon` |
| `0x017284B0` | `findMap(mode, key, out)` — §1.5 | `EntryDungeonPartyManager`, `NOTI_Dungeon`, `NOTI_Etc`, `CNRDItemManager` |
| `0x01728460` | append 5×u32 record to `+0xA60` | `NOTI_Dungeon 0x01157A70`, `NOTI_Etc 0x01163410` |
| `0x017288B0` | append `0x5C`-byte record to `+0xC14` | `NOTI_Dungeon 0x01146000` |
| `0x0172A180` | drain NAMED_APPEAR queue, `ret 0xC` | `NOTI_Dungeon 0x01154810` (DIE_MONSTER) |
| `0x0172A040` | clear `+0x4`/`+0x18`/`+0x2C` | `NOTI_Dungeon 0x01151CC0` (DUNGEON_INFO), `0x01157A70` |
| `0x01726D30` | `add ecx,0xA88; jmp 0x1725DF0` — operate on `+0xA88` | `NOTI_Dungeon` ×3, `NOTI_Etc`, `CNRDStage` |
| `0x017259B0` | scan `+0xC14` against current map, wake 4 UI singletons | `NOTI_Dungeon 0x01146000` |
| `0x01728380` | `f(x, y, …)` → element of container `0x3A8F608` | `NOTI_Dungeon 0x01157A70`, `CNRDStage 0x0199FB10` |
| `0x01728330` | same index formula, different container | `CNRDStage 0x0199FB10` |
| `0x0172B970` | `f(x, y)` | `NOTI_Dungeon 0x01157A70`, `MovieDirector`, one more |
| `0x01724DA0` | `setDrawTitle(bool, int, int)` `ret 0xC` — writes `+0xB14/+0xB18/+0xB38`, and when the flag is set calls `prepareSetDrawTitle` and releases `+0x180`, `+0xB94`, `+0xB1C` | `NOTI_Town 0x01191D80`, `CNTownModule`, `CNRDBaseInterface` ×2 |
| `0x01727480`, `0x01729F00` | — | `CMD_Dungeon 0x011040A0` (GIVEUP_GAME / ADVANCE_ALTAR_PAUSE) |
| `0x01730DB0`, `0x01729B30`, `0x017339E0`, `0x01735690` | — | `CNSelectDungeonModule.cpp` |
| `0x01735C60`, `0x01738FD0` | — | `CNRDStage 0x0199B120` |
| `0x0172FD40`, `0x01724B80`, `0x01735580`, `0x017248B0` | — | `DNFShared\GameScript\RDARScript*.cpp` |

## 5. Title-card / cosmetic subsystem (client-only, no server bearing)

* `0x01723D70` `prepareSetDrawTitle` (L1729) — picks frame `getTick()`-indexed from the animation
  vector at `this+0x140+0x5F8..0x5FC` (8-byte elements); logs `error!! - cannot find title animation`.
* `0x0172CEF0` `drawNextTitle` (L1907) — same failure literal.
* `0x0172E930` — loads `Town/Title/chn_golden_silver_dungeon.ani`.
* `0x01723E90` — picks the "season server" monster nameplate animation:
  `Etc/SeasonServer/animation/{Named,Nomal}_{S,M}.ani`. It **early-returns for the index set**
  `{0xF15F, 0xF162, 0xF164, 0xF167, 0x1006E, 0x10E5D, 0x11055}` returned by the object's `vtbl+0x44`
  (`0x01723ECE`–`0x01723F0F`). `_M` vs `_S` is chosen by a width test `> 0x41` at `0x01723F83`;
  Named vs Nomal by `0x017B0C20` on the object.

## 6. Confidence

High for: identity of the class, ctor/dtor/vptr, the map-index formula, `+0xA40`/`+0xA44`,
the protected-value primitive and its five members, the vtable extent, the external call surface,
and the fact that `create` is a bulk copy from a `CNScriptLoader`-resolved dungeon script.

Medium for: the semantic *names* of individual script fields (offsets are proven, meanings mostly
are not), the field names of the 20-byte item record, and the `RandomHell` retry geometry.

Unknown: what `0x0520FE26` actually reads out of the object (it is virtualized — only its role as
the grid row stride is established); the meaning of `createMap` args 4–6; whether
`Etc/DifficultPenalty.etc` really contains a `[COMMON TRAP EXPLOSION]` section.
