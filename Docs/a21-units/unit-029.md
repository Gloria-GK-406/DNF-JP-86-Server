# Unit 029 — `DNFClient\RDAR\CNRDStage.cpp`

Briefing span `0x0198BF60`–`0x0199FB10`. **Corrected span: `0x0198BF60`–`0x019A5940`** (see
"Span correction" below). Image base `0x00400000`, A21 memory dump.

---

## 1. What this translation unit is

`CNRDStage` is the client's **in-battle world object** — the thing that owns "the room you are
standing in". One `CNRDStage` binds together:

* the current `CNRDMap*` (`this+0x8C`),
* the current `CNRDDungeon*` (`this+0xCC`),
* the `CNRDObjectManager*` that holds every live object in the room (`this+0xC8`),
* the current **map coordinate** (room index) `this+0xD0 / this+0xD4`.

Its public surface is driven almost entirely by the *Module* layer — `CNMainGameModule.cpp`,
`CNTownModule.cpp`, `CNAssaultModule.cpp`, `CNBossTowerModule.cpp`, `CNTournamentModule.cpp`,
`CNAdvanceAltarModule.cpp` all call `CNRDStage::proc` (`0x019A4EC0`, 11 callers) and
`CNRDStage::setCurrentMapPos` (`0x0199FB10`). So: **the game mode modules tick the stage; the
stage ticks the room.**

On top of that world plumbing the file carries three presentation subsystems that are *not* in
separate files:

1. **Combo / hit-rating ("HK") system** — 3 combo kinds, per-kind timers, style bonus,
   the `HK1_*/HK2_*/HK3_*` voice cues, the `R_HK2_nGOOD` combo announcer,
   and the `Score_*` debug readouts used by PVP-practice mode.
2. **Screen quake (화면 흔들림)** — `setQuake(power, durationMs)` at `0x0198CEF0` is the single most
   called function in the whole unit (**227 external callers**), plus `procQuake` at `0x0198D1A0`.
3. **Camera zoom / stage draw** — `setZoom` (`0x0198F580`, 28 ext callers), `resetZoom`
   (`0x0198EE20`), and `draw()` (`0x0199CC40`) which walks map layers 5→2.

**Networking: none.** Zero calls to the inbound-stream read primitives anywhere in the corrected
span (grep over the full disassembly of `0x0198BF60`–`0x019A5940`: 0 hits on the read
primitives). Nothing in the unit reaches a winsock API. `CNRDStage` is a pure consumer of state
that `GameCoreNetwork_NOTI_*` has already parsed.

> **Note on the read-primitive addresses in the task prompt.** The prompt lists
> `0x023C5230 read_u8 / 0x023C52A0 read_u16 / 0x023C5310 read_u32 / 0x023C5380 read_bytes /
> 0x023C4350 set_remaining`. Those are **RVAs, not VAs**. In this image the primitives are at
> `0x027C5230` (u8), `0x027C52A0` (u16), `0x027C5310` (u32), `0x027C5380` (bytes) — verified by
> disassembly: `0x027C5230` does `cl = *g_cursor(0x3BAAE38); g_cursor++; g_remaining(0x3BAAE3C)--`,
> `0x027C5310` does the same with a dword. Add `0x400000` to the prompt's numbers.

Source-line evidence from the `__FILE__` asserts: `setMap` at line **3362**, addCombo assert at
**3665**, `getCurrentCombo` at **3928**, `setCurrentMapPos` at **7262/7263**, `getPVPStartPos` at
**7429/7431**. The file is ≥ 7431 lines.

---

## 2. Span correction — the briefing is too NARROW, not too wide

The briefing's hull ends at `0x0199FB10` because that is the last function referencing the
`__FILE__` literal. Five more functions belong to this TU:

| VA | size | evidence it is CNRDStage.cpp |
| --- | ---: | --- |
| `0x019A0100` | 6400 | uses the private string block (`Interface2/UI/ComboEffect/Combo_bonus.img`, `dungeon_score.img`, `dungeon_rank.img`, `Common/Etc/Combo/*.ani`) |
| `0x019A1A00` | 3744 | `HK_STYLE`, `HK1_AIRIAL`, `HK2_AIRIAL`; reads `[ebx+edi*4+0x168]` and writes `[ebx+edi*4+0x258]` — the CNRDStage combo/style-bonus fields |
| `0x019A2F00` | 1024 | sole caller of `CNRDStage::draw` (`0x0199CC40`) |
| `0x019A3B00` | 512 | literal `CNRDStage::checkBestDamage` |
| `0x019A3D00` | 4544 | literal `CNRDStage::procMove` |
| `0x019A4EC0` | 2688 | literal `CNRDStage::proc`, plus `@SendingCombo` / `@PlaySounds` profiler tags |

Two independent proofs:

* **Private string block.** `0x032F2A80`–`0x032F40B8` is referenced *only* by functions in
  `0x0198BF60`–`0x019A4EC0`, and every string in it is a CNRDStage string. The block immediately
  before (`0x032F299C` = `IRDAIManager::procAppend`) belongs to `0x01988EF0`; the block immediately
  after (`0x032F40E0` = `Interface/newstyle/windows/SkillShop.img`) belongs to `0x019B4CF0`
  (CNRDSkill*).
* **Field layout.** `0x019A1A00` and `0x019A4EC0` index `this+0x168` (combo array),
  `this+0x258` (style bonus) and `this+0x8E0` (combo window) exactly as `0x0199B9F0` does.

End of TU = `0x019A5940` (= `0x019A4EC0 + 2688`). `0x019A5940` itself is a skill-table accessor
(`[eax + ecx + 0x7530]`, index bound `0x752E`) — a different class.

---

## 3. Server-relevant findings

### 3.1 PVP start position — `CNRDStage::getPVPStartPos` (`0x0198FAB0`)

`getPVPStartPos(IRDCharacter* ch, int* outX, int* outY)`; log format
`" getPVPStartPos team %d, x %d, y %d randSeed %d uId %d "` (line 7429).

Team selection:

* If game-mode predicate `0x013DB490(0x10)` is true → `team = 1` or `2`, chosen by comparing two
  values from the mode manager (`0x011BA990`/`0x011BA6D0`/`0x011B94E0` vs `0x019FC7B0`).
* Else if `map->getData()->[0x4BC] != 0` → scan `CNUserManager(0x3A5C990)` slots `0..7`
  (`0x019E7770(i)`) and take the slot index whose user == this character. So **team == user slot,
  0..7**.
* Else `team = 0`.

Seed:

```
seed = ch->vf[0x4C]()            // user/char id
     + ch->vf[0x3D8]()           // (secondary id / index)
     + posComponent              // from ch->vf[0x9DC]() -> pair at (+0x38, +0x40)
if (isMode0x10) seed = ch->vf[0x4C]()     // id only
```

Early-out: `mapData[0x280 + team*36 + {0,4,8,0xC}]` all zero → no spawn rect for this team, return
without writing outX/outY.

Then `CNRDMap::getStartPos` at **`0x00C2E380`**, `ret 0x14` — args
`(team, int* outX, int* outY, IRDObject* obj, int seed)`:

```
rec = (int*)((char*)map + 0xD90 + team*36);   // == mapData + 0x280, since mapData = map+0xB10
                                              // (0x00C2E5F0: lea eax,[ecx+0xB10])
for (try = 0; try < 1000; ++try) {
    if (obj->vf[0x34](1)) {                   // "is character"
        dx = dy = 0;
        w = rec[2] - obj->vf[0x390]() + 1;    // vf 0x390 = obj width
        if (w > 0)  dx = rand_r(&seed) % w;
        h = rec[3] - obj->vf[0x394]() + 1;    // vf 0x394 = obj height
        if (h > 0)  dy = rand_r(&seed) % h;
        *outX = obj->vf[0x390]()/2 + rec[0] + dx;
        *outY = obj->vf[0x394]()/2 + rec[1] + dy;
    } else {
        *outX = rand_r(&seed) % rec[2] + rec[0];
        *outY = rand_r(&seed) % rec[3] + rec[1];
    }
    if (obj->vf[0x118](*outX, *outY, 0) == 1) break;   // position accepted
}
```

**Spawn-rect record: 36 bytes (9 dwords), 8 records, at `CNRDMap + 0xD90`
(= mapData + 0x280).** `rec[0]=x`, `rec[1]=y`, `rec[2]=width`, `rec[3]=height`; the remaining 5
dwords are not touched by this routine.

`rand_r` is `0x022D2DD0`, the classic DNF LCG — worth mirroring exactly if the server ever has to
reproduce a client-computed position:

```
s = s*0x41C64E6D + 0x3039;  a = (s>>16) & 0x7FF;
s = s*0x41C64E6D + 0x3039;  b = (s>>16) & 0x3FF;
s = s*0x41C64E6D + 0x3039;  c = (s>>16) & 0x3FF;   // s written back
return ((a<<10) ^ b) << 10 ^ c;                     // 31-bit
```
With a NULL seed pointer it falls back to the global seed at `0x039912B0`, mixed with
`GetTickCount`-family (`[0x3029608]`).

Caveat: this is the **client's** local placement. Nothing here sends the result. If the server also
places PVP spawns it must either send them or reproduce this formula; I have no evidence which.

### 3.2 Room transition — `CNRDStage::setCurrentMapPos` (`0x0199FB10`, 7 external callers)

`setCurrentMapPos(int x, int y)`, line 7262.

```
if (!this->dungeon(+0xCC)) return;
idx = dungeon->0x171F460(x, y);                 // linear room index
if (idx < size(g_roomVec@0x3A8F5D0, stride 0x30) && g_roomVec[idx].f0 == 0) return;
save old (+0xD0,+0xD4,+0x8C,+0xC8)
this->+0xD0 = x;  this->+0xD4 = y;              // logged: 새로운 좌표 / 이전 좌표
n = dungeon->0x1720290(x, y);
if (dungeon->0x1728330(x, y, n)) {              // multi-map room
    r   = dungeon->0x1728380(x, y);
    map = ((CNRDMap**)r[0x18])[n];              // vector at r+0x18..r+0x1C
    this->setMap(map);                          // 0x01991010
    v   = ((int*)r[0x2C])[n];                   // parallel vector at r+0x2C..r+0x30
} else {
    this->setMap(dungeon->0x1720870(x, y));
    v = dungeon->0x17205D0(x, y);
}
this->0x0198CE50(v);
map->vf[0x68](v);
if (mapData[0xB8C] && mapData[0xB90] != -1) map->0xC381B0();
// build list of users to relocate (up to 8 slots from CNUserManager 0x3A5C990)
this->0x0199E020(&userList, oldObjMgr, oldMap, this->objMgr, oldX, oldY);
dungeon->0x171C3D0(1);
InterfaceManager(0x3A5C9B8)->...->0x10D0FA0()   // HUD refresh, twice (2nd with mapData[0x6D0],[0x6D4])
map->vf[0x24C]()
```

The room-runtime record vector at `[0x3A8F5D0]..[0x3A8F5D4]` has **stride 0x30 (48 bytes)** and
field `+0x2C` is read as an int (passed into `0x0199E020` as the "arrival" parameter).

`0x0199E020` (6896 bytes) is the warp-in / user-relocation routine. It is called from exactly two
places: `setCurrentMapPos` and **`0x01157A70` in `GameCoreNetwork_NOTI_Dungeon.cpp`** (a real
parser — `parsers.csv`: 84×u8, 42×u16, 38×u32, 3×bytes). So the S→C dungeon notification that moves
the party between rooms lands here. I did not fully reverse `0x01157A70`'s field order; if you need
the wire layout of that NOTI, that function is the one to open.

### 3.3 Combo state machine (`this+0x168`, 3 kinds)

`COMBO_TYPE_COUNT <= MAX_COMBO_KIND` with `MAX_COMBO_KIND = 3` (assert at `0x0199BC5F`, line 3665;
bound check `esi < 3` in `getCurrentCombo`, `0x0198BF67`).

`CNRDStage::getCurrentCombo(int comboType)` — `0x0198BF60`:
```
if (comboType >= 3) { log("Invalid comboType - %d"); return 0; }
return *(int*)(this + 0x168 + comboType*4);
```

**Add / increment** — `0x0199B9F0(int upTo, bool bSilent, X* ctx)`, `ret 0xC`:
* Computes the combo window into `this+0x8E0` / `this+0x8E4` by summing entries of a script table
  owned by `[0x3A5C628]` (vector at `+0x7C`/`+0x80`, 16-byte records `{key0, ?, dur0@+8, dur1@+0xC}`
  matched against `myChar->vf[0x9DC]()` fields `[0]` and `[0xC]`), plus `[0x3A5C628+0x8C]` twice when
  the mode check `0x02300940() == 9` passes, plus `0x019B9F50(x)` from equip-option id `0x82`.
* `for (k = 0; k < upTo+1; ++k)`: reset the per-kind timer `this + k*0x1C + 0x174` and start it for
  `this->+0x8E4` ms; `++combo[k]`; when `combo[k] >= LATCH[k]` (`LATCH = {2,1,1}` at `0x032F2A24`),
  latch `this+0x1D4+k*4 = combo[k]`, start the second timer `this + k*0x1C + 0x1E0` for `this->+0x8E0`
  ms and call the number-display helper `0x01995070((k==0)?4:0, 1, latched, 0, +0x8E0)`.
* After the loop, combo announcer (sound via `GameCoreSub::playSound` `0x01648A20`, 6 args
  `(name, -1, 0, 0, -1, -1)`):
  ```
  if (combo[0] >= 20 && combo[1] >= 8) "R_HK2_1GOOD"
  else if (combo[0] >= 10 && combo[1] >= 4) "R_HK2_2GOOD"
  else if (combo[0] >=  2 && combo[1] >= 2) "R_HK2_3GOOD"
  ```
  and a 50 %-chance "HK3_AERIAL".
* `if (this->+0x28 == 1) this->+0x2C = combo[0]` (max-combo snapshot).

**Expire / flush** — inside `CNRDStage::proc`, profiler scope `@SendingCombo` (`0x019A5443`–
`0x019A56BF`):
```
for (k = 0; k < 3; ++k) {
    if (suppressed) continue;                        // PVP-practice or mode 9 w/ vf[0x1BC]
    if (!g_myChar(0x3B3DFB8)) continue;
    if (combo[k] < 1) continue;
    t = timer(this + k*0x1C + 0x174);
    if (!t.isRunning() || t.elapsed() < this->+0x8E0) continue;
    if (combo[k] >= STYLE[k]) {                      // STYLE = {5,1,1} at 0x032F2A18
        this->+0x258 + k*4 = (combo[k] - STYLE[k]) * 5 + 5;      // style bonus value
        timer(this + k*0x1C + 0x264).reset().start(3000);
        playSound("HK_STYLE");
        scoreHud(0x3A5AD44)->0x52D8D44(combo[k], k, myChar->vf[0x4C]());
    }
    scoreHud->0x019410D0(combo[k]); scoreHud->0x01942E40();
    if (isNormalDungeon()) { scoreHud->0x01942660(combo[k], k); scoreHud->0x019468C0(); }
    combo[k] = 0;
}
```

> **`@SendingCombo` does NOT send a packet.** The sinks (`0x01940AF0`, `0x019410D0`, `0x01942660`,
> `0x01942E40`, `0x019468C0`) belong to the local score/combo HUD manager at global `0x3A5AD44` —
> the same object that owns the literals `Score_Combo : [%d]` / `Score_Aerial : [%d]`
> (`0x01940AF0`) and `Score_MonsterKill : [%d]` (`0x01946630`). `func-api-reach.csv` gives
> `0x01940AF0` and `0x019410D0` **0 reachable APIs**. The profiler label means "hand the combo to
> the score system", not "send to server".

### 3.4 Hit-rating grade formula (`0x0198C1D0` → `0x0198FD60`)

`0x0198FD60` fires when the timer `this+0x584` has been running > **2000 ms** (`0x7D0`):

```
grade = this->0x0198C1D0(this->+0x5A0);            // stored to this->+0x580
switch (grade) { 0:"HK3_GOOD" 1:"HK3_NICE" 2:"HK3_GREAT" 3:"HK3_EXCELLENT" 4:"HK3_PERFECT" }
                                                    // jump table at 0x0198FEE0
```
`0x0198C1D0(int n)`:
```
scoreHud(0x3A5AD44)->0x0193F220(&c[5]);            // 5 counters
total = c[0]+c[1]+c[2]+c[3]+c[4];
if (total == 0 || n <= 0) -> degenerate path (0x0198C256)
q = n / total;
return q==1?0 : q==2?1 : q==3?2 : q==4?3 : 4;      // clamp at PERFECT
```

### 3.5 Score-effect dispatcher `0x019975F0` (3936 bytes, 12 external callers)

An if-else chain on `scoreType` (arg1). Recovered mapping (each branch spawns an animation, sets a
timer, sets a broadcast-sound flag, and — only when the PVP-practice debug flag
`0x3A5AD44->0x0193F210()` is set — prints `Score_XXX : [%d]` into the chat window via
`InterfaceManager(0x3A5C9B8)->0xA9AF50()->0x01A60620(text, 0xFF32FF32, 30, 0,0,0,0)`):

| type | branch VA | meaning |
| ---: | --- | --- |
| 0 | `0x01997640` | (base / overkill; sets `this->+0x788 = 1`) |
| 1 | `0x019977A1` | `Score_BackAttack` — sets `this->+0x789 = 1` |
| 2 | `0x01997A07` | `Score_counter` |
| 3,4,5 | `0x01997C3C/0x01997C69/0x01997C97` | (chained tests) |
| 6 | `0x01997E5C` | `HK_STYLE` |
| 7 | `0x01997E20` | `HK_TECHNIC` |
| 8 | `0x019982E2` | (small branch) |
| 9 | `0x019980A6` | `Score_Combo_NearMiss` / `HK3_NMISS` |
| 0xA | `0x019981C4` | `Score_Combo_GhostFrame` / `HK3_GFRAME` |
| 0xB, 0xC | `0x01998336/0x0199833F` | `HK3_HOLD` |
| 0xD | `0x01998433` | `HK3_SHOLD` |
| 0xE | `0x01997E98` | `Score_ArmorBreak` / `R_HK3_SBREAK` |
| 0xF | `0x01997FAC` | `Score_Critical` |

### 3.6 `CNRDStage::playBroadCastSound` (`0x01999D80`)

Six pending-sound slots. Slot `i` fires when timer `this + i*0x1C + 0x5C8` reports done
(`0x027C3780`) **and** the flag byte `this + 0x788 + i == 1`. Jump table at `0x0199A164`:

| slot | dungeon cue | PVP cue (HK1 vs HK2 chosen by `GameCoreSub 0x0163E7D0(-1)`) |
| ---: | --- | --- |
| 0 | `HK3_OKILL` (50 % roll) | `HK1_OVERKILL` / `HK2_OVERKILL` |
| 1 | `HK3_BATK` (roll) | `HK1_BACK_ATK` / `HK2_BACK_ATK` |
| 2 | `HK3_COUNTER` | `HK1_COUNTER` / `HK2_COUNTER` |
| 3 | — (skips) | — |
| 4 | — | `HK1_FINISH_01` / `HK2_FINISH_01` |
| 5 | — | `R_HK1_KILL` / `R_HK2_KILL`, or `ROBOT_SUMMON_MONSTER` when `CNRoom(0x01A6BFD0)->vf[0x24]() == 1` |

### 3.7 Screen quake (`setQuake` `0x0198CEF0`, 227 callers — the unit's hottest API)

```
setQuake(int power, int durationMs):
    this->+0x118 = this->+0x11C = power;
    this->+0x120 = durationMs;
    timer(this+0x124).reset(); .start(0, 0);
    if (power == 0) this->+0x140 = this->+0x144 = 0;
    if (this->map && !(power == 0 && durationMs == 0))
        g_obj(0x3A42DB0)->0x014412B0(1, power, durationMs, 1);   // fields +9, +0x38, +0x3C, +0xE4
```
Observed argument pairs at call sites: `power` ∈ 1..0x0F, `duration` ∈ {0x32, 0x3C, 0x50, 0x64,
0xA0, 0xC8, 0x12C, 0x2EE} ms.

`procQuake` — `0x0198D1A0` (called from `proc` at `0x019A5436`):
```
if (!(+0x118 || +0x11C)) return;
if (this->+0x7E4 != 0) return;                 // the other timed camera effect owns the screen
s = camera(0x39A439C)->0x554530();             // zoom scale; if |s-1| < |s|*K use 0.5f
this->+0x140 = rand() % (2*+0x118*s + 1) - (2*+0x118*s)/2;   // uniform in [-p, +p]
this->+0x144 = rand() % (2*+0x11C*s + 1) - (2*+0x11C*s)/2;
if (timer(this+0x124).elapsed() >= this->+0x120) {          // expired
    +0x118 = +0x11C = +0x120 = 0; timer.reset(); .start(0,0); +0x140 = +0x144 = 0;
}
```
`proc` then composes the final draw offset into `+0x110/+0x114`:
`+0x114 = +0x144 + mapOffset + (0x79 in raid-mode-4-with-hitgauge, else 0x50)`, `+0x110 = +0x140`.

---

## 4. Object layout — `CNRDStage` (≥ `0x97C` bytes)

Every row has an instruction that proves it. `esi`/`ecx` = `this`.

| offset | type | evidence |
| --- | --- | --- |
| `+0x24` | int | `0x0198C1AA mov [esi+0x24], ecx` |
| `+0x28` | bool | `0x0199BE4B cmp byte [esi+0x28], 1` |
| `+0x2C` | int (max combo snapshot) | `0x0199BE51/57 mov [esi+0x2C], [esi+0x168]` |
| **`+0x8C`** | **`CNRDMap*`** | getter `0x0198C6E0` (111 ext callers); `0x0199109D` in `setMap`; used as `ecx` for `0xC2E5F0`,`0xC2E760`,`0xC47860`,`0xC381B0` |
| `+0x90` | bool | `0x019910AD mov byte [esi+0x90], 0` (setMap) |
| `+0x94` | `IRDObject*` (focus/camera target) | setter `0x0198BFF0` (61 ext callers) forwards to `CNRDMap 0xC30C10`; draw does `[0x94]->vf[0x34](0x11)`; `0x0198C2D9` uses `vf[0x164]` |
| **`+0xC8`** | **`CNRDObjectManager*`** | `0x0199FF72`; `ecx` into `0x1913230`,`0x190EDB0`,`0x1917F50`,`0x190EE00`,`0x1910540` |
| **`+0xCC`** | **`CNRDDungeon*`** | setter `0x0198C6D0` (14 ext); `0x0198EDB0` → `0x171C570()->[0x34]` = dungeon index; `0x0198F22F` compares that index to `0xE7A` |
| **`+0xD0` / `+0xD4`** | **int curMapX / curMapY** | `0x0199FC35 / 0x0199FC3B`, logged as `새로운 좌표 (%d,%d)` |
| `+0xF0` | CNTimer (0x1C) | `0x0198C750/0x0198C760/0x0198C780` |
| `+0x110` / `+0x114` | int draw offset X / Y | `0x019A57AE / 0x019A57A8` |
| `+0x118` / `+0x11C` | int quake power X / Y | `0x0198CF04/0x0198CF0A` set, `0x0198D21B/0x0198D221` read |
| `+0x120` | int quake duration (ms) | `0x0198CF10`; compared `0x0198D294` |
| `+0x124` | CNTimer (0x1C) quake timer | `0x0198CEFE`, `0x0198D289` |
| `+0x140` / `+0x144` | int quake offset X / Y | `0x0198D264 / 0x0198D283` |
| `+0x148` | int | `0x0198D182 mov [esi+0x148], 0xFFFFFFDD` |
| `+0x154` | (sub-object) | `0x019A5ED2 lea ecx,[edi+0x154]` → `CNRoom 0x01A6D330` |
| `+0x155` | byte | `0x0198FCFE movzx edx, byte [esi+0x155]` |
| **`+0x168[3]`** | **int comboCount[3]** | `0x0198BFC0 mov eax,[ecx+esi*4+0x168]` (bound `<3`); `0x0199BCD8 inc`; `0x019A5674 = 0` |
| `+0x174 + k*0x1C` | CNTimer comboTimer[3] | `0x0199BCAC`, `0x019A5576` |
| `+0x1D4[3]` | int comboLatched[3] | `0x0199BD2E` |
| `+0x1E0 + k*0x1C` | CNTimer[3] | `0x0199BD28`, `0x0199BD43` |
| `+0x258[3]` | int styleBonus[3] | `0x019A55BE`, `0x019A1A9F` |
| `+0x264 + k*0x1C` | CNTimer[3] (3000 ms) | `0x019A55B8`, `0x019A55D1` |
| `+0x304 + i*0x1C` | CNTimer[] score-effect | `0x019979B7` (0x304), `0x0199775C` (0x33C) |
| `+0x424` | CNTimer | `0x0199BDDB`, started 1000 ms `0x0199BDE8` |
| `+0x440` / `+0x441` | bool | `0x0199BDF8 = 1` / `0x0199BDD2 cmp` |
| `+0x544` | struct (character ref) | `0x0198FDB5 lea edi,[esi+0x544]`, released via `IRDCharacter 0x18A8570` |
| `+0x57C` | int accumulated damage/score | `0x0198FDA7`, cleared `0x0198FE0B` |
| `+0x580` | int grade 0..4 | `0x0198FDCF` |
| `+0x584` | CNTimer (2000 ms gate) | `0x0198FD7E`, `0x0198FD91`, reset `0x0198FDEF` |
| `+0x5A0` | int hit count | `0x0198FDC1`, cleared `0x0198FDF5` |
| `+0x5C8 + i*0x1C` | CNTimer broadcastSound[6] | `0x01999E4B`; `0x01997779` (0x5C8), `0x019979DF` (0x5E4) |
| `+0x788 .. +0x78D` | byte broadcastPending[6] | `0x01999E62 cmp byte [esi+edi+0x788], 1`; set `0x01997795` (`+0x788`), `0x01997999` (`+0x789`) |
| `+0x7A0` | int | `0x0198F60A / 0x0198F622` (zoom) |
| `+0x7A4` | int (−1 sentinel) | `0x0198C260` |
| `+0x7A8` / `+0x7AC` | int, int (−1 sentinel; camera target x,y) | `0x0198C293 / 0x0198C29E` → `0x190EE00` |
| `+0x7B0` | bool zoom active | `0x0198F58D`, `0x0198F5AF` |
| `+0x7B4` | int zoom mode | `0x0198F5EF`; cleared by resetZoom `0x0198EE24` |
| `+0x7B8` | bool | `0x0198F5E1`; cleared `0x0198EE2E` |
| `+0x7BC` | struct& | `0x0198C7B0 lea eax,[ecx+0x7BC]` |
| `+0x7DC` | bool (set) | `0x0198BFD0` |
| `+0x7DD` | bool (get) | `0x0198BFE0`; tested `0x0198EE5B` |
| `+0x7E0` | float saved zoom | `0x0198EE64 / 0x0198EE98` |
| `+0x7E4` / `+0x7E8` | DWORD startTick / endTick | `0x0198C186` `call [0x302915C]` (GetTickCount) then `+0x7E4 = eax`, `+0x7E8 = eax + dur` |
| `+0x7EC` | int | `0x0198C1A4` |
| `+0x800` | int | `0x01997720 = 0`, `0x019979BD = 1` |
| `+0x804` | int state (`== 5` test) | `0x0198C8C0`; `0x01998F40` sets 5; getter `0x0198C020` |
| `+0x824` | struct& | `0x0198C8A0` (7 ext callers) |
| `+0x85C` / `+0x860` / `+0x864` | int getters | `0x0198C7D0 / 0x0198C810 / 0x0198C7E0` |
| `+0x868` | CNTimer | `0x0198C7F0 / 0x0198C800` |
| `+0x894` / `+0x898` / `+0x89C` | int counters | `inc` at `0x0198C840 / 0x0198C860 / 0x0198C880`; getters `0x0198C850 / 0x0198C870` |
| `+0x8A0` | bool | `0x0198C3F0` |
| `+0x8C2` | bool | `0x0198C890` |
| `+0x8C4` | int | `0x0198C8B0` |
| `+0x8C8` | bool | `0x0198C9F0` |
| `+0x8C9` | bool | `0x019A4F76 cmp byte [esi+0x8C9], 0` (proc) |
| `+0x8D1` | bool (debug overlay) | `0x0199D7A6` |
| `+0x8DC` | int | get `0x0198CA00` (7 ext) / set `0x0198CA10` |
| **`+0x8E0` / `+0x8E4`** | **int combo window (ms), two variants** | computed `0x0199BA6C…0x0199BC44`; consumed `0x019A5594`, `0x0199BCBA`, `0x0199BD3A` |
| `+0x920` | bool | `0x0198C480` |
| **`+0x944`** | **bool cinematic mode** | getter `0x0198CA20` (18 ext); setter `0x0198CA30`/`0x01990C40` (26 ext, **20 of them from `PassiveObject\Cinematic\MovieDirector.cpp`**, 5 from `CNFighterSecondAwakeningEventStatueNpc.cpp`) |
| `+0x948[8]` | int per-user-slot value | `0x0198C646 mov eax,[edi+esi*4+0x948]`, guarded `esi < 8` and the slot's `CNUserManager` user exists |
| `+0x968` | bool | set `0x0198CCF0`, get `0x0198CD00`, recomputed from `0x0198CA30` (map type 4/5) |
| `+0x96C` / `+0x970` / `+0x974` | int triple (−1,−1,−1 = "unset") | set3 `0x0198CDC0`, test `0x0198CDF0` |

Composite predicate `0x0198CDA0` (12 ext callers) = `(+0x944 != 0) || (+0x968 != 0)`.
Composite predicate `0x0198C790` (5 ext callers) = `(+0x118 != 0) || (+0x11C != 0)` — "is quaking".

**CNTimer helper class** used pervasively (methods live near `CNMemoryPool.cpp`):
`0x027C3410` reset, `0x027C3B60(durationMs, flag)` start, `0x027C3660` elapsed(),
`0x027C3500` isRunning(), `0x027C3780` isDone(), `0x027C3CB0`/`0x027C3D30`/`0x027C36F0`/`0x027C3660`
accessors. Instance size 0x1C.

---

## 5. Public surface (126 functions in the briefing span have external callers; 730 distinct
external caller functions)

| VA | ext callers | what it is |
| --- | ---: | --- |
| `0x0198CEF0` | 227 | `setQuake(power, durationMs)` |
| `0x0198C6E0` | 111 | `getMap()` → `+0x8C` |
| `0x0198BFF0` | 61 | `setFocusObject(obj, bool bNotifyMap)` → `+0x94` |
| `0x0198D6E0` | 33 | camera-set wrapper → `camera(0x39A439C)->0x554AE0(a,b,fx,fy)` |
| `0x01992130` | 30 | effect-spawn wrapper (14 args incl. 6 floats) → `0x0198D710` |
| `0x0198F580` | 28 | `setZoom(float scale, …, bool)` (`ret 0x28`) |
| `0x01990C40` | 26 | `setCinematicMode(bool)` → `+0x944` |
| `0x0198CA20` | 18 | `isCinematicMode()` |
| `0x01995950` | 17 | animation-handle lookup (`RDARScriptAnimation 0x293B470`) |
| `0x0198FF00` | 16 | "is this object an enemy / not on my team" (`CNUserManager 0x3A5C980` + `GameCoreSub 0x16434F0`) |
| `0x0198E7A0` | 15 | world→screen transform through the camera scale |
| `0x01991010` | 14 | `CNRDStage::setMap(CNRDMap*)` |
| `0x0198C6D0` | 14 | `setDungeon(CNRDDungeon*)` → `+0xCC` |
| `0x0198CDA0` | 12 | `isCinematicOrX()` |
| `0x019975F0` | 12 | score-effect dispatcher (§3.5) |
| `0x0198EDB0` | 11 | `getDungeonIndex()` (−1 if no dungeon) |
| `0x0198CF70` | 10 | `setQuake` overload |
| `0x0199FB10` | 7 | `CNRDStage::setCurrentMapPos(x, y)` |
| `0x0198BF60` | 8 | `CNRDStage::getCurrentCombo(int)` |
| `0x019A4EC0` | 11 | **`CNRDStage::proc()`** — the world tick (outside the briefing span) |

`proc`, `setCurrentMapPos`, `draw` and the timer-reset `0x0198C760` are all called from the game
**Module** files (`CNMainGameModule`, `CNTownModule`, `CNAssaultModule`, `CNBossTowerModule`,
`CNTournamentModule`, `CNAdvanceAltarModule`, `CNRDMap`, `GameCoreDebug`) — that is the ownership
edge to record.

## 6. Globals this unit depends on

| global | identity (evidence) |
| --- | --- |
| `0x3A5C990` | `CNUserManager` — slot API `0x019E7750(i)` / `0x019E7770(i)`, always looped `i < 8` |
| `0x3A5C980` | user lookup by character index (`0x019E2A40`) |
| `0x3A5C9B8` | `CNRDInterfaceManager` (`0x02300930`, `0xA9AF50` chat window) |
| `0x3A5AD44` | combo/score HUD manager (owns `Score_Combo`, `Score_Aerial`, `Score_MonsterKill`) |
| `0x3B3DFB8` | my `IRDCharacter*` |
| `0x39A439C` | camera (`0x554510/0x554530/0x554540/0x554560/0x554580/0x554AE0`) |
| `0x3A42DB0` | receives the quake forward (`0x014412B0`: `+9`, `+0x38`, `+0x3C`, `+0xE4`) |
| `0x3A5C628` | combo-duration script table owner (vector at `+0x7C`/`+0x80`, 16-byte records) |
| `0x3AA33D0` | global combo/score UI slot array, stride 68 (`0x44`), 12 slots × 2 sub-slots (`0x01995070`) |
| `0x3A8F5D0/D4` | room-runtime vector, stride 0x30, field `+0x2C` used on room change |

## 7. Constant tables (verified by reading the image)

| address | contents | use |
| --- | --- | --- |
| `0x032F2A18` | `{5, 1, 1}` | style-bonus threshold per combo kind (`0x019A55A7`) |
| `0x032F2A24` | `{2, 1, 1}` | combo-latch threshold per kind (`0x0199BD1C`) |
| `0x032F2A30` | `{20, 10, 5}` | adjacent 3-entry table (not used by the announcer code) |
| `0x032F2A3C` | `{8, 4, 2}` | adjacent 3-entry table |
| `0x032F2A70` | `1.2f` | camera scale constant |
| `0x032F2A74` | `0.5f` | quake fallback scale |
| `0x0198FEE0` | 5 targets | grade → `HK3_GOOD/NICE/GREAT/EXCELLENT/PERFECT` |
| `0x0199A164` | 6 targets | broadcast-sound slot → cue family |
| `0x013DB87C` / `0x013DB8B8` | jump table + index byte array | game-mode predicate `0x013DB490(modeId)`; `modeId 0x10` → `0x013DB64D` = `0x011BA990()->0x011BA650()` |

## 8. What I did NOT establish

* `0x0199E020` (6896 bytes, the warp-in routine) — only the first ~100 instructions read; the
  per-user relocation branches for Assault / AdvanceAltar / raid modes were not enumerated.
* `0x019A3D00` `CNRDStage::procMove` (4544 bytes, 105 callees) and `0x019A0100` (6400 bytes,
  the combo-bonus / dungeon-score UI) — identified but not reversed.
* The wire layout of `GameCoreNetwork_NOTI_Dungeon.cpp:0x01157A70`, the NOTI that drives
  `0x0199E020`. That is the right next target if you want the S→C room-change packet.
* Meaning of `+0x804` (state, `== 5` tested), `+0x968`, `+0x96C/70/74`, `+0x948[8]`,
  `+0x85C/60/64`, `+0x894/98/9C`. Offsets and access sites are proven; names are not.
* Whether the client's PVP spawn position is authoritative or merely a local prediction.
