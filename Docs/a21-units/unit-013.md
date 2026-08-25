# TU 13 — `DNFClient\RDAR\CNRDConfigFile.cpp`

Briefing span `0x0137FA90`–`0x01388420`. **The span is too short at the top end** — see
§4. Everything below is from the A21 memory dump, image base `0x00400000`.

`CNRDConfigFile` is the client's settings store. It is *two* stores glued into one class:

1. a **local** store backed by the `./DNF.cfg` Windows INI file, keyed by wide-string
   names `CONFIG_*` (90 of them, §1.7);
2. an **account/server** store keyed by small integer *option ids* (0–105) that travel on
   the wire as the DB keys `I00`..`I99`, `C00`, `C01`, `J00`..`J02`, `K00`, `K01`. The
   client uploads these to the server as a binary array and receives them back as
   `KEY=VALUE` text.

The second half is the part a server implementer needs, and it is fully recoverable.

---

## 1. Server-relevant surface

### 1.1 Three C→S packets are emitted from this unit

Send primitives (established by disassembly, not by name): `0x0274CA50` returns the
send-packet singleton (`[0x03B7CEE4]`; `[obj+8]` = "session live" byte,
`[obj+0x2BCC2C]` = byte counter). `0x0274D1E0(cmd, x)` starts a packet — the command is a
**u16** (`0274D218 mov di, word ptr [ebp+8]`). `0x0274D2E0` writes 1 byte, `0x0274D310`
writes 2, `0x0274D3A0(ptr,len)` writes raw bytes, `0x0274E6D0` sends.

| VA | cmd | payload | meaning |
|---|---|---|---|
| `0x0137EF40` | **0x00C5** | `writeBytes(this+0x88, 0x96)` | 150 bytes = **75 little-endian u16 option values**, option id 0..74 in array order |
| `0x0137F310` | **0x01C0** | `writeBytes(this+0x268, 0x200)` | 512 bytes = **256 u16 hotkey/quickslot table** |
| `0x0137F390` | **0x0170** | `u8 index; writeBytes(this+0x468 \| this+0x594, 0x12C)` | sent **twice**, index 0 then 1; each 300 bytes = 5 × 60-byte char slots |

`0x0137EF40` in full (that is the entire function):

```
0137EF40  call 0x274ca50                    ; getSendPacket()
0137EF48  cmp  byte ptr [eax+8], 0
0137EF4C  je   0137EF7F                     ; not connected -> drop
0137EF4E  push 0 / push 0xc5
0137EF55  call 0x274ca50 / call 0x274d1e0   ; begin(0x00C5, 0)
0137EF61  push 0x96 / add esi,0x88 / push esi
0137EF6D  call 0x274ca50 / call 0x274d3a0   ; writeBytes(this+0x88, 150)
0137EF7A  jmp  0x274e6d0                    ; send
```

`0x0137EF40` has **15 call sites across the client** — `0x1178B69` in
`GameCoreNetwork_NOTI_Etc.cpp`, `0x1389384`/`0x13893E2`/`0x13893F3` inside this unit, plus
option-window code at `0x53538E`, `0x536452`, `0xED51CE`, `0xEF9BC9`, `0x104AD67`,
`0x1299786`, `0x132165E`, `0x15015D8`, `0x1A69757`, `0x23AE8BD`, `0x2566BD5`. So 0x00C5 is
the generic "my options changed, persist them" upload.

`0x0137F310` (cmd 0x01C0) is gated: it sends only when `0x163D0C0()` is true, a vtable call
`[[0x1A6BFD0()]+0x30]()` is true, and `0x163E1E0() != 4` — the hotkey table is uploaded only
in a particular game state.

### 1.2 S→C: the account option blob arrives as `KEY=VALUE` text

`0x01389680` — `recvHotkeyOption2`, named by its own error literal `0x0328E3D4`
`"recvHotkeyOption2 - Fail"` — walks a text buffer, splits it into key/value wide strings,
and for each pair:

```
01389C56  call 0x1384e20     ; optionId = convertDBKeyToEnum(keyString)   (key in ecx)
01389C73  call 0x2c65a29     ; value    = _wtoi(valueString)
01389C83  call 0x1387f70     ; this->applyOption(optionId, value)
```

If the value token is the literal `"NONE"` (`0x030BCF8C`) it calls `this->0x1384F10`
instead (`0x1389A27`–`0x1389A67`).

* `0x01384E20` = `int convertDBKeyToEnum(const wchar_t* key)` — key arrives in `ecx` (LTCG
  custom convention), builds a wstring, looks it up in the hash map at `0x03A40AD8`.
* `0x01384B20` = `convertEnumToDBKey(id)` — the reverse, by linear scan; on failure logs
  `ERROR!! - convertEnumToDBKey` (`0x0328DFB8`).

### 1.3 The DB key ↔ option id table (COMPLETE)

Built by `0x013837A0` into an `unordered_map<wstring,int>` at `0x03A40AD8`, guard byte
`0x03A3F60C`. Each entry is `push <literal>; call 0x27BACC0 (decrypt); mov [ebp-0x3c],eax;
mov [ebp-0x38],<id>; call 0x2AF57E0 (insert)`. 109 inserts, extracted mechanically:

```
I00 -> -1     (sentinel / invalid)
I01 ->  0     I02 ->  1     ...   I75 -> 74           (I<N> -> N-2, contiguous)
I76 -> 75     I77 -> 76     ...   I90 -> 89
C00 -> 90     J00 -> 91     J01 -> 92     C01 -> 93    (interleaved into the gap)
I91 -> 94     I92 -> 95     I93 -> 96     I94 -> 97    I95 -> 98
I96 -> 99     l96 -> 99                                (lower-case L, duplicate alias)
I97 -> 100    I98 -> 101    I99 -> 102
J02 -> 103    K00 -> 104    K01 -> 105
I94 -> 106    (second insert of the SAME key, at 0x01384926 — see §5)
```

So **option ids 0..74 are exactly DB keys `I01`..`I75`**, and those 75 ids are exactly the
u16 array at `this+0x88` that packet 0x00C5 uploads. Ids 75..105 exist only in the
key/value text channel and are dispatched by `applyOption`.

After the inserts, `0x013837A0` runs a verification loop (`0x01384952`–`0x01384AD1`): for
`i = 0 .. 0x69` it scans the map for an entry whose value == i. The client therefore
asserts that **every id 0..105 has a key**. Id 106 falls outside that loop.

### 1.4 Per-option default / min / max (option-id space)

`0x01380930` fills `std::map<int, Entry>` at `metadata+0x14` through `operator[]` at
`0x013801F0`. Each iteration writes 16 bytes as two `movq`s, so
`Entry = { int f0; int def; int min; int max; }`. The read side (`0x01382F70`) fixes the
field roles: `[&mapped+4]` is the fallback, `[&mapped+8]` the lower bound, `[&mapped+0xC]`
the upper bound.

70 of the 75 ids are registered. **Ids 2, 15, 21, 23, 54 are absent** — `0x01382F70`
returns 0 for them (map find misses → `xor eax,eax`).

```
id  f0  def   min      max         id  f0  def   min  max
 0   1    1     0        1         41   1    2     0    2
 1   1    0     0        1         42   1    1     0    1
 3   1    3     0        3         43   1    0     0    1
 4   1  100     0      100         44   1    0     0   10
 5   1  100     0      100         45   1    0     0   10
 6   1    0     0        2         46   1    0     0   10
 7   1    0     0        1         47   1    0     0   10
 8   1    0     0        1         48   1    0     0    1
 9   1    0     0        1         49   1    0     0    1
10   1    0     0        1         50   1    0     0    1
11   1    1     0        2         51   1    0     0    1
12   1    1     0        1         52   1    0     0    1
13   1    1     0        1         53   1    0     0    1
14   1    1     0        1         55   1    1     0    1
16   1    0  -30000   30000        56   1    1     0    1
17   1    1     0        1         57   1 1000     0 1000
18   0    3     0       26         58   1    1     0    1
19   0    0     0        1         59   1  100     0  100
20   1    1     0       10         60   1    0     0    1
22   1 1000     0     1000         61   1    0     0    3
24   1    1     0        1         62   1    0     0    4
25   1  100     0      100         63   1    0     0    1
26   1    1     0        1         64   1    0     0    1
27   1    0     0        3         65   1    1     0    1
28   1    0     0        1         66   1    1     0    1
29   1    0     0        1         67   1    1     0    1
30   1    0     0        1         68   1  100     0  100
31   1    0     0        1         69   1    0     0    1
32   1    1     0        1         70   1    1     0    1
33   1    1     0        1         71   1  100     0  100
34   1    1     0        1         72   1    1     0    1
35   0    1     0        3         73   1    1     0    1
36   1  100     0      100         74   1    1     0    1
37   1    0     0        1
38   1    0     0        1
39   1    0     0        1
40   1    0     0        1
```

`f0` is 1 for every entry except ids **18, 19, 35**; no read of that field was located
(§6). Id 16 is the only signed-range option: `[-30000, +30000]`.

### 1.5 The read path a server must mirror

`0x01382F70` = `int CNRDConfigFile::getOption(int id)` (99 callers):

```
if (id >= 0x4B) return 0;                                    // 01382F86 cmp edi,0x4b / jge
entry = metadata.intMap.find(id);
if (entry == end)  return 0;                                 // 01382FCC
if (id == 0x0E && g_world && g_world->vf_0x9DC()->[0] == 0x0A) return 0;   // 01382FD8
if (id == 0x18 && 0x27E9FE0(g_0x3BAB0B0, 7, 0))               return 1;    // 01383001
v = (int16_t)this->values[id];                               // 01383026, via 0x29162D0
return (v < entry.min || v > entry.max) ? entry.def : v;     // 01383032
```

`0x01385C50` = `resetOptionsToDefault()` writes hardcoded values into the array first
(`0x29162F0(index, value)`; the index is the *last* push):

```
[0]=1 [1]=0 [2]=1 [3]=3 [4]=70 [5]=25 [22]=1000 [6]=0 [7]=0 [8]=0 [9]=0
[10]=0 [11]=1 [12]=1 [13]=1 [14]=1 [16]=0 [17]=1   … then name-keyed reads follow
```

Note `[4]=70` and `[5]=25` differ from the map defaults (100/100) — the reset baseline and
the clamp fallback are not the same numbers.

### 1.6 The DNF.cfg INI side

* **Path.** The ctor decrypts `./DNF.cfg` (`0x0328E138`) and passes it through
  `0x028377C0`; if that returns 0 it falls back to `decrypt(0x03043960)`. The result is
  stored in `this->path` (`this+4`).
* **Section.** `0x01383050` = `const wchar_t* getSectionName(name)`, whose only literal is
  `DNF COMMON` (`0x0328D8D4`). Both `0x01385870` (read struct) and `0x01387EA0` (write
  struct) bail out when the resolved section equals `decrypt(0x03043960)` — that literal is
  the "no section" sentinel.
* **INI object** at `this+0x84`, created by `0x02744C20(path)`. Methods resolved through
  the IAT: `0x02744AF0` → `WritePrivateProfileStructW` (`[0x03029284]`), `0x02744B20` →
  `GetPrivateProfileIntW` (`[0x03029288]`), `0x02744B50` → `[0x0302928C]` (the struct
  **read**; both struct slots resolve to the same address in this dump, so the name is
  inferred from call role), `0x02744B80` → `WritePrivateProfileStringW`.
* **Config format version = 3.** Ctor `0x013867A2`–`0x013867ED`: read
  `CONFIG_COMMON_CONFIG_VERSION`; if it is not 3, `DeleteFileW(path)` and write 3 back.
* `0x01385510` = `bool getByName(const wchar_t* name, int* out)` contains a hard override:
  if `byte [0x03B84158] != 0` **and** the name is `CONFIG_COMMON_SCREEN_MODE`, it returns
  `*out = 3` without touching the file (`0x01385557`–`0x013855A5`).
* `0x01385300` = `bool setByName(name, value)`: clamps to the named entry's `[min,max]`
  (falling back to `def`), then `WritePrivateProfileStringW` through `this+0x84`, then
  caches into `this+0x254`.

Named blobs written with the struct API:

| function | literal / source line | global buffer | size |
|---|---|---|---|
| `0x013860D0` `importSkillEtcValue` | `SkillEtcLoad ---- Fail!!!`, line 0xB64 | `0x0397F0A0` | 8 (2 ints, preset −1) |
| `0x01388280` `setSkillEtcValue(i,v)` | `SkillEtcSave ---- Fail!!!`, line 0xB7C | `0x0397F0A0`, `i <= 1` | 8 |
| `0x01386210` `importChattingWindowSize` | `ChattingWindowLoad ---- Fail!!!`, line 0xEF6 | `0x0397F0A8` | 12 (3 ints, preset −1) |
| `0x01388420` `exportChattingWindowSize` | `ChattingWindowSave ---- Fail!!!`, line 0xEFF | `0x0397F0A8` | 12 |
| `0x013882F0` `setIGAFileVersion` | `IGAFileVersionwriteFailed` | via `0x01387540` | — |

Key names come from globals `0x03A3FE18` = `CONFIG_SKILL_ETC_VALUE`, `0x03A3FE20` =
`CONFIG_CHATTING_WINDOW_SIZE`. Getter `0x0137EEB0` returns `0x0397F0A8/AC/B0` for
arg 0/1/2 (the chat-window rect).

### 1.7 The 90 `CONFIG_*` names, in declaration order

Name pointers live in a contiguous global table at `0x03A3FD18` (index *i* at
`0x03A3FD18 + 4*i`), filled by the initializer at `0x02F439AD`.. from decrypted literals in
`0x03046D28`–`0x03048620`. Recovered by matching
`push <lit>; call 0x27BACC0; add esp,4; mov [<global>], eax`:

```
  0 CONFIG_COMMON_RESIZE_WIDTH              45 CONFIG_WORLD_MINIMAP_LASTPOS_Y
  1 CONFIG_COMMON_RESIZE_HEIGHT             46 CONFIG_WORLD_MAP_ALPHA_MODE
  2 CONFIG_COMMON_WINPOS_X                  47 CONFIG_OPEN_PLAY_GUIDE
  3 CONFIG_COMMON_WINPOS_Y                  48 CONFIG_CHANNEL_SELECT_CHANNEL_INDEX
  4 CONFIG_COMMON_SCREEN_MODE               49 CONFIG_CHARACTER_AVATAR_CODY_1
  5 CONFIG_COMMON_CONFIG_VERSION            50 CONFIG_CHARACTER_AVATAR_CODY_2
  6 CONFIG_COMMON_FULLSCREEN_V_SYNC_MODE    51 CONFIG_CHARACTER_AVATAR_CODY_3
  7 CONFIG_TEXTURE_POST_PROCESSING          52 CONFIG_CHARACTER_AVATAR_CODY_4
  8 COFNIG_LIMIT_FRAME_RATE   [sic]         53 CONFIG_CHARACTER_SKILL_FUNCTION_SEAL
  9 CONFIG_PVP_SCREEN_MODE                  54 CONFIG_CHARACTER_ESCALADE_QUEST_GUIDE_1
 10 CONFIG_PVP_SCREEN_WIDTH                 55 CONFIG_CHARACTER_ESCALADE_QUEST_GUIDE_2
 11 CONFIG_PVP_SCREEN_HEIGHT                56 CONFIG_CHARACTER_ESCALADE_QUEST_GUIDE_3
 12 CONFIG_PVP_TEXTURE_UPSCALING            57 CONFIG_CHARACTER_ESCALADE_QUEST_GUIDE_4
 13 CONFIG_ORG_SCREEN_MODE                  58 CONFIG_CHARACTER_ESCALADE_QUEST_GUIDE_5
 14 CONFIG_ORG_SCREEN_WIDTH                 59 CONFIG_CHARACTER_GOGUIDE_QUEST
 15 CONFIG_ORG_SCREEN_HEIGHT                60 CONFIG_CHARACTER_GOGUIDE_ISACCEPTED
 16 CONFIG_ORG_TEXTURE_UPSCALING            61 CONFIG_WORLD_MINIMAP_LASTSIZE_X
 17 CONFIG_COMMON_IGA_FILEVERSION           62 CONFIG_WORLD_MINIMAP_LASTSIZE_Y
 18 CONFIG_SELECT_CHARACTER_SLOT            63 CONFIG_QUEST_GUIDE_WINDOW_STYLE
 19 CONFIG_CHANNEL_SELECT_SERVER            64 CONFIG_SKILL_ETC_VALUE
 20 CONFIG_CHANNEL_SELECT_SERVER_FIRST      65 CONFIG_USE_FLUSH_FILE
 21 CONFIG_CHANNEL_SELECT_CHANNEL_TYPE      66 CONFIG_CHATTING_WINDOW_SIZE
 22 CONFIG_CHANNEL_SELECT_CHANNEL_PAGE      67 CONFIG_UPDATE_PARTY_LIST_MODE
 23 CONFIG_SELECT_EVENT_SERVER              68 CONFIG_DPS_WINDOW_LASTPOS_X
 24 CONFIG_LAST_CONNECT_DATE                69 CONFIG_DPS_WINDOW_LASTPOS_Y
 25 CONFIG_CHARACTER_NAME_SEND_MAIL         70 CONFIG_UPDATE_TIME_TRAINING_SHORT_CUT
 26 CONFIG_CHARACTER_NAME_SEND_MAIL_V1      71 CONFIG_OPTIMIZATION_DUNGEON_IMAGE_FORCE_RELEASE
 27 CONFIG_CHARACTER_CHATTING_FILTER        72 CONFIG_OPTIMIZATION_IMAGE_RELEASE_SPEED
 28 CONFIG_PARTY_LIST_WINDOW_AUTO_OPEN      73 CONFIG_OPEN_MOVE_CRACKCHANNEL
 29 CONFIG_COMMON_CHAT_WINDOW_MODE          74 CONFIG_OPEN_MOVE_TAUCHANNEL
 30 CONFIG_FULL_QUESTLIST_SHOWING_MODE      75 CONFIG_OPEN_MOVE_GUILDCHANNEL
 31 CONFIG_QUESTLIST_FILTER_1               76 CONFIG_ITEM_TOOLTIP_IS_DETAIL
 32 CONFIG_QUESTLIST_FILTER_2               77 CONFIG_ITEM_DIJOINT_FILTER_1
 33 CONFIG_QUESTLIST_FILTER_3               78 CONFIG_ITEM_DIJOINT_FILTER_2
 34 CONFIG_QUESTLIST_FILTER_4               79 CONFIG_ITEM_DIJOINT_FILTER_3
 35 CONFIG_QUESTLIST_FILTER_5               80 CONFIG_ITEM_DIJOINT_FILTER_4
 36 CONFIG_QUESTLIST_FILTER_6               81 CONFIG_CHATTING_TRANSPARENCY
 37 CONFIG_QUESTLIST_FILTER_7               82 CONFIG_SPACE_RULES_VIOLATION
 38 CONFIG_QUESTLIST_FILTER_8               83 CONFIG_DAILY_COLLECT_NOTICE_ON_
 39 CONFIG_QUESTLIST_FILTER_9               84 CONFIG_MERCENARY_COMPETITION_INFO
 40 CONFIG_QUESTLIST_FILTER_10              85 CONFIG_LAST_JOIN_INFO_ON_CLIENT_CRASH
 41 CONFIG_ONGOING_QUESTLIST_SHOWING_MODE   86 CONFIG_LAST_RAID_CHANNEL_ON_CLIENT_CRASH
 42 CONFIG_DUNGEON_MINIMAP_LASTPOS_X        87 CONFIG_CHARACTER_PLAY_GUIDE_ALARM_LIST
 43 CONFIG_DUNGEON_MINIMAP_LASTPOS_Y        88 CONFIG_CHARACTER_LATELY_GAMESERVER_INFO
 44 CONFIG_WORLD_MINIMAP_LASTPOS_X          89 CONFIG_SEASON_SERVER_SELECTED
```

**This index is NOT the option id.** The two registries are disjoint: the int-keyed map is
written only by `0x01380930` (via `0x013801F0`, in-degree 1) and the name-keyed map only by
`0x01382080` (via `0x01381E10`, in-degree 1). Treat "CONFIG_* name" and "option id / DB key"
as two separate namespaces unless a pairing is proven at a specific call site.

### 1.8 Where a NOTI drives this

`0x01160D20`, inside `GameCoreNetwork_NOTI_Etc.cpp` (TU 63), is a bare handler:

```
01160D20  call 0x13868f0 / call 0x1389310    ; getInstance()->applyAccountConfig()
01160D2C  call 0x13868f0 / call 0x137ede0
01160D38  cmp byte [0x3b84158], 0 / je …
01160D41  push 3 / push [0x3a39e84]          ; CONFIG_COMMON_SCREEN_MODE
01160D50  call 0x1385300                     ; setByName(name, 3)
01160D5C  jmp  0x137ef40                     ; -> send packet 0x00C5
```

`0x01160D70` calls `getInstance()->0x1389E50`, the actual one-byte parser:
`read_u8(&b); this->[0x250] = b; this->0x1388560();` — the server sends a single flag byte
that lands at `this+0x250` and triggers a hotkey-config reload from
`ClientOnly/HotKeySystem_ver2.co` / `ClientOnly/HotKeySystemForCreator_ver2.co`
(literals of `0x01388560`).

`0x01389310` (the handler at `0x01160D27`) first validates `size(this+0x88) == 0x96`
(`01389374 cmp eax,0x96`) and resets the array plus re-sends 0x00C5 if it is not.

---

## 2. Object layout

### `CNRDConfigFile` — singleton `0x03A3F610`, `getInstance` `0x013868F0` (396 callers), ctor `0x01386490`, atexit dtor thunk `0x03000F00`, init guard bit 1 of `0x03A3FCD0`

| off | type | evidence |
|---|---|---|
| `+0x000` | u8 flag | `013864C0 mov byte ptr [esi],0` |
| `+0x004` | `wstring` config-file path (`./DNF.cfg`) | ctor `013864C3 lea ebx,[esi+4]`, `[ebx+0x18]=7`, `[ebx+0x14]=0`; layout: proxy@+0, `_Bx`@+4, `_Mysize`@+0x14, `_Myres`@+0x18; stride 0x20 |
| `+0x024` | `wstring` | `01386502 lea edi,[esi+0x24]` |
| `+0x044` | `wstring` | `01386538 lea edi,[esi+0x44]` |
| `+0x064` | `wstring` | `01386570 lea edi,[esi+0x64]` |
| `+0x084` | `CIniFile*` | `013865B4 mov [esi+0x84],0`; `01386797 mov [esi+0x84],eax` = `0x02744C20(path)`; used as `mov ecx,[esi+0x84]` before every profile API |
| `+0x088` | `uint16 optionValue[75]` (0x96 B) | ctor `013865AA lea ecx,[esi+0x88]; call 0x2916BA0`; get `0x029162D0` (`cmp eax,0x4a; ja`), set `0x029162F0`; size assert `01389374 cmp eax,0x96` |
| `+0x11E` | sub-object, ctor `0x0164CC40` | `013865BF lea ecx,[esi+0x11e]` |
| `+0x1F4` | sub-object, ctor `0x027B13E0` | `013865CA`; seeded at `0138669F` with an 0x80-char string built from `0x699A` → `0x28C14C0` → `0x27BADD0` → `_snprintf` |
| `+0x23C` | `map<int,int>`, ctor `0x013BEF90` | `013865D5`; lookup `0x0137FD20` returns **0x86** when the key is absent |
| `+0x250` | int, server-set | ctor `013867F2 mov [esi+0x250],0`; `01389E66 mov [esi+0x250], <u8 from stream>` |
| `+0x254` | `map<wstring,int>` INI value cache, ctor `0x00C82010` | `013865E4`; find `0x02BEC010` at `013855CD`, insert at `013854A0` |
| `+0x268` | `uint16 hotkey[256]` (0x200 B), memset to 0xFFFF | ctor `013865F3 … call 0x2916650` = `memset(this+0x268, -1, 0x200)` |
| `+0x468` | `char slotA[5][60]` (0x12C B), memset 0 | ctor `01386602 … call 0x2916BB0` |
| `+0x594` | `char slotB[5][60]` (0x12C B), memset 0 | ctor `0138660D … call 0x2916BB0` |

Object size ≥ `0x6C0`.

Accessors for `+0x268`:

* `0x02916710` `int get(idx)` — `cmp eax,0x7f; ja → −1`; `mov ax,[ecx+eax*2]`
* `0x029166B0` `bool set(idx, u16)` — same bound; `mov [ecx+eax*2],dx`
* `0x02916730` `int get2(row,col)` — `if (row*64 + col >= 0x80) return -1;
  return [(row+2)*64 + col]`

so the table is 4 rows × 64 u16: rows 0–1 are the flat-index space (`get`/`set`), rows 2–3
are the two-dimensional space used by `0x0137F6A0` when it rebuilds the reverse lookup at
`0x03A3FE80 + row*0x200`.

Accessors for `+0x468`/`+0x594` (10 slots × 60 bytes, index 0..9):

* `0x0137EE10` `set(i, str)` — `i < 5` → `+0x468`, else `i−5` → `+0x594`; the string is run
  through `0x27BADD0` (encrypt) before `0x02916530` copies it
* `0x0137EE70` `get(i)` — mirror: `0x02916510` then `0x27BAD50` (decrypt)

so those slot contents are **stored obfuscated in memory**.

### Option-metadata singleton — `0x03A3F5C0`, `getInstance` `0x01382EE0`, guard bit 1 of `0x03A3F5E8`

| off | type | filled by |
|---|---|---|
| `+0x00` | `std::map<wstring, Entry>` | `0x01382410` → `0x01382080` → `0x01381E10` |
| `+0x14` | `std::map<int, Entry>` | `0x01380930` → `0x013801F0` |

`Entry = { int f0; int def; int min; int max; }` (16 bytes, written as two `movq`).

Node layouts — MSVC `_Tree_node` with `_Myval` at `+0xC`, confirmed by
`01380254 cmp edx, [ecx+0xc]` comparing the search key against the node:

* int-keyed: node 0x40 bytes, key at `node+0xC`, `&mapped = node+0x10`
  (`01382FD1 lea esi,[ecx+0x10]`, then `[esi+4]=def, [esi+8]=min, [esi+0xC]=max`)
* name-keyed: `wstring` key (0x20) at `node+0xC`, `&mapped = node+0x2C`
  (`013853D7 add ecx,0x2c`, then `[eax+4]=def, [eax+8]=min, [eax+0xC]=max`)

`0x01382080` signature: `add(int f0, const wchar_t* name, int def, int min, int max)`; a
NULL name is a no-op. `0x01382410` uses it for the runtime-derived entries: screen
width/height from `[0x0399AE98]`/`[0x0399AE9C]` with `max = 0xFFFF`, and window X/Y from
`SystemParametersInfoW(SPI_GETWORKAREA = 0x30)` with a symmetric `[−w, +w]` range.

### DB-key map — `0x03A40AD8`

`unordered_map<wstring,int>`: std::list node `_Next`@0, `_Prev`@4, `_Myval`@+8; key wstring
0x20 bytes; the int at `node+0x28`, i.e. `pair+0x20` (`01384C07 lea edi,[ecx+8]` then
`01384C4D mov edi,[edi+0x20]`). begin `0x022FC2D0`, end `0x01CA4BB0`, find `0x008B39F0`,
insert `0x02AF57E0`.

---

## 3. Public surface (called from outside the unit)

| VA | callers | what it is |
|---|---|---|
| `0x013868F0` | 396 | `CNRDConfigFile::getInstance()` — lazy ctor + `atexit(0x03000F00)`, returns `0x03A3F610` |
| `0x01382F70` | 99 | `int getOption(int id)` — clamped/defaulted read of `values[id]`, id < 75 |
| `0x01385830` | 63 | `int getByNameOrDefault(const wchar_t* name)` |
| `0x01385B40` | 57 | bool predicate over a named value; 26-case jump table; name from `[0x03A3FD28]` = `CONFIG_COMMON_SCREEN_MODE` |
| `0x01385300` | 45 | `bool setByName(name, value)` — clamp, `WritePrivateProfileStringW`, cache |
| `0x01387F30` | 32 | `bool setOption(id, value)` — writes `values[id]` (skipped when `byte [0x03B7B69C] != 0`) then `0x01387120` to apply it |
| `0x01382C90` | 23 | shared `std::map` ctor |
| `0x01382EE0` | 16 | metadata `getInstance()` |
| `0x01385510` | 15 | `bool getByName(name, int* out)` + the `CONFIG_COMMON_SCREEN_MODE == 3` override |
| `0x01385870` | 15 | `bool readStruct(name, buf, size)` via `GetPrivateProfileStruct` |
| `0x01387EA0` | 13 | `bool writeStruct(name, buf, size)` via `WritePrivateProfileStructW`, then `0x01387540` |
| `0x0137FED0` | 12 | `int getDefaultByName(name)` off the metadata name map |
| `0x01387F70` | 8 (+`0x1389C83`) | `applyOption(id, value)` — value remap 5→4, 8→7, 12→10; jump table over ids `0x2C`..`0x3F`; large id exclusion list |
| `0x01386490` | 7 | ctor |
| `0x01383050` | 6 | `const wchar_t* getSectionName(name)` (`DNF COMMON`) |
| `0x0137FA90` | 2 (`0x0053606E`, `0x005989C0`) | `exportScenarioDifficulty(int d)` — rejects `d<0 \|\| d>=2` with the `%d` log (line 0x956), writes `hotkey[0x2B] = d`, then sends packet 0x01C0 |
| `0x0137EF40` | 15 | send packet **0x00C5** |
| `0x0137F310` | 3 | send packet **0x01C0** |
| `0x0137EE10` / `0x0137EE70` | ext. | obfuscated 60-byte slot set/get |
| `0x01388560` | 4 | hotkey-config (re)load from `HotKeySystem_ver2.co` |

---

## 4. Span correction

**The briefing's `hi = 0x01388420` is short by roughly 0x1A30 bytes.** The unit's decrypted
literal block is contiguous from `0x0328D788` (the `__FILE__` string) to `0x0328E3D4`, and
the last four literals in that block belong to functions *past* the briefed boundary:

```
0x0328E318  0x01388560  '[key]'
0x0328E330  0x01388560  'ClientOnly/HotKeySystem_ver2.co'
0x0328E378  0x01388560  'ClientOnly/HotKeySystemForCreator_ver2.co'
0x0328E3D4  0x01389310  'recvHotkeyOption2 - Fail'
```

The next literal in address order, `0x0328E450`, belongs to `0x0138A320`
(`CNRDInformationButtonManager`). Corroborating: `0x01389310` uses `edi+0x88` (the option
array) and calls `0x01385510`, `0x01385C50`, `0x01387F30`, `0x01382EE0`, `0x0137EF40` — all
this unit's internals — and `0x01388A70`/`0x01388AF8` call `0x01384E20`/`0x01387F70`.

**Real span: `0x0137FA90` – `0x01389E7A`** (the last function `0x01389E80` tail-jumps into
`recvHotkeyOption2`). That extension is where the whole `KEY=VALUE` account-config parser
lives — i.e. the most server-relevant code in the module sat outside the briefed range.

The *low* end is arguably short too: `0x0137ED70`–`0x0137FA90` (`0x0137EDE0`, `0x0137EE10`,
`0x0137EE70`, `0x0137EEB0`, `0x0137EF40`, `0x0137F310`, `0x0137F390`, `0x0137F6A0`,
`0x0137EF90`) are all `CNRDConfigFile` methods over `this+0x88`, `this+0x268`, `this+0x468`,
`this+0x594` — they simply never reference `__FILE__`. **All three packet senders are in
that region.**

---

## 5. Oddities worth flagging

* **Duplicate DB key.** `I94` is inserted twice: at `0x013846EB` with id `0x61` (97) and
  again at `0x01384926` with id `0x6A` (106). With `unordered_map::insert` semantics the
  second is a no-op, so id 106 has no reachable key; if `0x02AF57E0` overwrites instead,
  id 97 loses its key and the client's own 0..0x69 verification loop would trip. Either
  way one of the two is a client bug. A server should not expect `I94` twice.
* **`l96` (lower-case L) is an alias for `I96`**, both mapping to id 99 (`01384755 mov
  esi,0x63` reused for both inserts). Almost certainly a legacy typo kept for
  compatibility — a server writing `l96` will still be understood.
* **`COFNIG_LIMIT_FRAME_RATE`** (name index 8) is misspelled in the client; the INI key is
  the misspelled string.
* `0x0137FB40` reads `u32 len; if (len != 0x200) return false; read_bytes(this+0x268,
  0x200)` from the inbound stream — the S→C counterpart of packet 0x01C0. **It has zero
  references in the image** (no `E8` call, no dword reference). `0x01389680`
  (`recvHotkeyOption2`) is likewise unreferenced. Both are either dead code or reached
  through a dispatch table built at runtime; do not assume the raw-blob download path is
  live.
* `parsers.csv` attributes the read primitives of `0x0137FB40` to `0x0137FB10`. Those are
  two different functions. `0x0137FB10` is just `!PathFileExistsW(this->path)` (string
  capacity at `this+0x1C`, buffer at `this+8`) and is the one actually called — from
  `0x01637DA5` in `GameCoreInit.cpp`, to decide whether to auto-detect graphics settings on
  first run.
* The task brief's read-primitive addresses are 0x400000 low. The real primitives are
  `0x027C5230` u8, `0x027C52A0` u16, `0x027C5310` u32, `0x027C5380` bytes, `0x027C4350`
  set_remaining; the stream cursor is `[0x03BAAE38]`, remaining `[0x03BAAE3C]`.

## 6. Unknowns

* `Entry.f0` (offset 0 of the 16-byte metadata entry): 0 for option ids 18/19/35 and for
  the screen width/height named entries, 1 everywhere else. No read of that field was
  located; its meaning is unknown.
* The fallback config-path literal / "no section" sentinel at `0x03043960` could not be
  recovered: the dump captured the post-decryption cache pointer rather than the
  ciphertext, so `dec()` returns nothing.
* Sub-objects at `+0x11E` (`0x0164CC40`) and `+0x1F4` (`0x027B13E0`) were not analysed. The
  latter is seeded with a generated 128-char string and is plausibly a cipher context for
  the cfg file, but that is not proven.
* Whether option id *N* corresponds to `CONFIG_*` name index *N*: **not established**, and
  the two registries are populated by disjoint code paths. Do not assume it.
* The per-id semantics behind `applyOption`'s jump table (ids `0x2C`..`0x3F`) and behind
  `0x01387120` (1056 bytes, 36 callees) were not enumerated.
