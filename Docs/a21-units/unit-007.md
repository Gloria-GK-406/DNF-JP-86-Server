# TU 007 — `DNFClient\RDAR\CNPVPModule.cpp`

**What it is:** the client-side PVP-arena (决斗场 / Fair PVP) *game module* — one `CNModule`
subclass that owns the whole arena session: the arena map + object manager, the 8 player
slots, the 17-state match state machine, the count-down/result timers, and the handful of
C→S packets the arena sends. It is not a UI file and not a network file; it is the arena
session driver that the packet handlers and the arena UI both talk to.

Everything below is from the image at base `0x00400000`.

---

## 0. The briefing's span is WRONG — too narrow, not too wide

`tu-index.csv` row 7 says `0x027026A0 – 0x027037C0`, 3 functions. That is only the two
functions that happen to reference the `__FILE__` literal. The real translation unit is

```
CNPVPModule.cpp  =  0x026FCA50 .. 0x0270644F      (~40 KB, 24 blocks in functions.csv)
```

Three independent proofs:

1. **Own-vtable containment.** The class vftable is at `0x035E473C` (RTTI COL at
   `0x035E4738` → `0x03761978`, offset 0, type-descriptor name stripped). Of its 161 slots,
   **103 point into `[0x026FCA50, 0x02706450)` and zero point anywhere else in that
   neighbourhood**; every remaining slot is a base-class method living far away
   (`0x00E1xxxx`, `0x0176xxxx`, `0x0223xxxx`, …). A dword scan of the whole image shows each
   of those 103 addresses is referenced **exactly once**, from this vtable only.
2. **Boundaries are sharp.** `0x026FC540` / `0x026FC660` are referenced only from
   `0x035E3BF0` / `0x035E3C00` — the *CNMainGameModule* vtable — and carry the literals
   `CNMainGameModule::DrawProc` / `CNMainGameModule::Proc`. The block immediately after
   `0x026FC660` is `0x026FCA50`, whose sub-function `0x026FCAE0` sits at CNPVPModule vt+0xC4.
   At the top end, `0x027061C0` is at vt+0x8C; `0x02706450` is in no vtable and is called
   from `CNTownModule.cpp`.
3. **Literal-pool contiguity.** `0x035E3EFC .. 0x035E46F4` is one unbroken run of literals,
   all referenced from functions in `0x026FD9B0 .. 0x027037C0`, sandwiched between
   CNMainGameModule's pool (ends `0x035E3EC4`) and CNTownModule's (starts `0x035E4A08`).

So the briefing describes 3 of ~50+ real functions. The `parsers: 0` figure is correct
though — **this module never touches the read primitives**; the server-driven state changes
arrive through other TUs (`GameCoreNetwork_NOTI_*`) that call into its public surface.

`__FILE__` = `D:\Work\1229\Source\DNFClient\RDAR\CNPVPModule.cpp` at `0x035E4180`.
Recovered source line numbers (from the `(funcName, line)` debug-hook macro at `0x01648E70`):
`Proc` ≈ lines 888–981, `setState` = line 1185, its `STATE_COUNT` arm = line 1542,
`procSTATE_PREPAREPRACTICE` ≈ lines 3877–3916. The file is ≥ 4000 lines.

### Class shape

* Primary vftable `0x035E473C` (161 slots), `Proc` at **vt+0x04**, `setState` at **vt+0xA4**.
* Secondary vftable `0x035E472C` (COL `0x03761A30`, **offset 80 = +0x50**), slot 0 =
  `0x02701C30` = `CNPVPModule::DrawProc`. So the object carries a second vptr at `this+0x50`.
* Type-descriptor name is stripped (`0x03996028` decodes to garbage), so the class name
  comes from the literals, not RTTI.

---

## 1. THE STATE MACHINE (the server-relevant core)

### 1.1 The enum — recovered exactly

`0x027037C0` is `std::string CNPVPModule::getStateName(std::string* out, int state)`:
`cmp ebx,0x10 / ja` then `jmp [ebx*4 + 0x2703900]`. Walking that 17-entry jump table and
decrypting each pushed literal (UTF-16, cipher per `dec.py`) gives the enum **in order**:

| # | name | literal VA |
|---|------|-----------|
| 0 | `STATE_LOADING` | `0x035E4670` |
| 1 | `STATE_WAIT` | `0x035E4650` |
| 2 | `STATE_PREPARE_BATTLE` | `0x035E461C` |
| 3 | `STATE_COUNT` | `0x035E45FC` |
| 4 | `STATE_BATTLE` | `0x035E45D8` |
| 5 | `STATE_RESULT` | `0x035E45B4` |
| 6 | `STATE_PREPAREWAIT` | `0x035E4588` |
| 7 | `STATE_PREPARE_RELAY_BATTLE` | `0x035E4548` |
| 8 | `STATE_RELAY_BATTLE_FIRST_COUNT` | `0x035E4500` |
| 9 | `STATE_TRAINING` | `0x035E43E8` |
| 10 | `STATE_PREPARE_PRACTICE` | `0x035E43B0` |
| 11 | `STATE_PREPARE_DOLLMODE` | `0x035E4378` |
| 12 | `STATE_SELECT_DOLL` | `0x035E434C` |
| 13 | `STATE_COMPLETE_MAKE_DOLLS` | `0x035E4310` |
| 14 | `STATE_PREPARE_TOTAL_MATCH_BATTLE` | `0x035E44B0` |
| 15 | `STATE_TOTAL_MATCH_BATTLE_COUNT` | `0x035E4468` |
| 16 | `STATE_TOTAL_MATCH_BATTLE_COUNT_READY` | `0x035E4410` |

**Cross-check that makes this certain, not a guess:** the `Proc` dispatch table sends index 0
to `vt+0x268` = `0x02702F80`, index 2 to `vt+0x26C` = `0x02701D50`, index 7 to `vt+0x270` =
`0x02702280`, index 14 to `vt+0x274` = `0x02702490`, index 10 to a direct call of
`0x027026A0` — and those five functions carry, respectively, the literals
`CNPVPModule::procLoading`, `procSTATE_PREPAREBATTLE`, `procSTATE_PREPARE_RELAY_BATTLE`,
`procSTATE_PREPARE_TOTAL_MATCH_BATTLE`, `procSTATE_PREPAREPRACTICE`. Five independent
name↔index confirmations.

### 1.2 `CNPVPModule::Proc` — `0x02703950` (vt+0x04)

`switch (this->m_state /* +0x5C */)`, `cmp eax,0xF / ja default / jmp [eax*4+0x0270421C]`.
**State 16 has no entry in the Proc table (only 0..15)** — it falls through to the shared
tail, i.e. `STATE_TOTAL_MATCH_BATTLE_COUNT_READY` has no per-frame body.

| state | handler VA | what it does |
|---|---|---|
| 0 LOADING | `0x02703A8D` | `vt+0x268` → `procLoading` |
| 1 WAIT | `0x02703A9E` | for slot 0..7: read char pos via `vt+0x3DC`/`0x3D8`, if they differ force-set via `vt+0x3D4`; same for `vt+0x3E4`/`0x3E0` → `vt+0x6D8` (spawn-position clamp). Then clear `+0x36C`, call `procOutlineObject` (`0x02702D40`), `0x026FDDF0`, `vt+0x27C` |
| 2 PREPARE_BATTLE | `0x02703EDA` | `vt+0x26C` → `procSTATE_PREPAREBATTLE` |
| 3 COUNT | `0x02703D3E` | when `timer(+0x60).elapsed >= this->+0x288` → **`setState(STATE_BATTLE, elapsed - +0x288)`** (`0x02703D8C`); then UDP module kick-off `0x022E5A10`/`0x022E6DA0` |
| 4 BATTLE | `0x02703DC7` | `+0x280 = max(0, +0x284 - elapsed)` (count-down remaining); `m_rule->vt[0x24]()` && `!+0x384` → `0x026FE190`; always `0x026FE090`; if `!+0x184` && elapsed ≥ `+0x284` → `vt+0xE0` (time-up) |
| 5 RESULT | `0x02703E7D` | polls the in-game-event queue (`0x01A182F0`, `CNRDInGameEvent.cpp`); when empty → **`setState(STATE_PREPAREWAIT, 0)`** (`0x02703EA5`) |
| 6 PREPAREWAIT | `0x02703EEB` | fade-out/in (see §3); once faded, **sends C→S `0x39`** exactly once and latches `+0x28C`; when `+0x28C && +0x28D` → **`setState(STATE_WAIT, 0)`** (`0x02704012`) |
| 7 PREPARE_RELAY | `0x0270401B` | `vt+0x270` → `procSTATE_PREPARE_RELAY_BATTLE` |
| 8 RELAY_FIRST_COUNT | `0x02703B73` | `+0x280 = max(0, +0x284 - elapsed)` |
| 9 TRAINING | `0x02704146` | no body (falls to tail) |
| 10 PREPARE_PRACTICE | `0x0270403D` | direct call `0x027026A0` = `procSTATE_PREPAREPRACTICE` |
| 11 PREPARE_DOLLMODE | `0x02704049` | when `0x01045390()` is true and `+0x407` not yet set: open UI window **id `0xA6`** via `CNRDInterfaceManager::0x02304C70(0xA6,0,0)`, latch `+0x407` |
| 12 SELECT_DOLL | `0x02704083` | for slot 0..7: doll object registered in `m_rule`? (`0x01A6CF70`). If **all 8** are → **`setState(STATE_COMPLETE_MAKE_DOLLS, 0)`** |
| 13 COMPLETE_MAKE_DOLLS | `0x027040E1` | same 8-slot sweep against `0x01A6C950`; if all → **`setState(STATE_COUNT, 0)`** |
| 14 PREPARE_TOTAL_MATCH | `0x0270402C` | `vt+0x274` → `procSTATE_PREPARE_TOTAL_MATCH_BATTLE` |
| 15 TOTAL_MATCH_COUNT | `0x02703BEF` | `+0x280 = max(0, +0x284 - elapsed)`; separately drives the `+0x2F0/+0x2F4` "ready" timer and sets `+0x2EC = 1` when it hits 0 |
| 16 …COUNT_READY | *(none)* | — |

**Shared tail** (`0x02704146`, runs every frame regardless of state):
* if `+0x36D` and timer `+0x22C` expired and `+0x36E` → **send C→S `0x6E`**, restart timer
  `+0x22C` with 10000 ms (`0x2710`).
* if `m_rule->vt[0x20]() == 1` (relay battle) → `vt+0x278` = `0x026FD520`
  (which sends C→S `0x70`, once, latched on `+0x2DC`).
* if `m_rule->vt[0x30]() == 1` (total match) → `0x027006E0`
  (which sends C→S `0x361` with a u8 body, once, latched on `+0x2E4`).
* once only: `+0x29C = 1` and `CNRDUdpModule::0x022E55C0()`.

### 1.3 `CNPVPModule::setState` — `0x02704260` (vt+0xA4), source line 1185

```
void __thiscall setState(int newState /*[ebp+8]*/, int timerArg /*[ebp+0xC]*/)
```
Argument order is proven by `mov [esi+0x5C], edi` at `0x02704389` where `edi = [ebp+8]`.
Call sites push `timerArg` first, `newState` last.

Body, in order:
1. Log `"PVP STATE : %s -> %s"` (`0x035E46F4`) with `getStateName(old)`, `getStateName(new)`
   through logger `0x027BE270(0, __FILE__, "CNPVPModule::setState", 1185, fmt, …)`.
2. `m_state (+0x5C) = newState`; `timer(+0x60).reset()` (`0x027C3410`) and
   `timer(+0x60).setAlarm(0, timerArg)` (`0x027C3B60`).
3. If a "PVP observed character" (`g_pPvpChar = [0x03B3DFB8]`) exists, notify it via
   `vt+0xB38(newState)`.
4. **Old-state exit actions:** `old == 8` or `old == 15` → `+0x280 = 0`;
   `old == 9` (TRAINING) → `((CNRDPVPPractice*)+0x3F4)->0x025C99A0()`.
5. **New-state entry actions:** `cmp eax,0x10 / ja / jmp [eax*4 + 0x027055C8]` — a **17-entry**
   table (unlike Proc's 16), so state 16 *does* have an entry action.

| new state | entry handler | notable entry action |
|---|---|---|
| 0 LOADING | `0x02704438` | `+0x3FC = 0`, `+0x21C = 0`, `+0x21E = 0`, `+0x404 = 0` |
| 1 WAIT | `0x02704459` | `+0x404 = 0`; 8-slot sweep that swaps the observed character (`g_pPvpChar`) and saves/restores 4 per-character fields into globals `0x03A5C9BC / 0x03A5C9D0 / 0x03A5C9D4 / 0x03A5C9D8` (getters `0x01833EB0/EC0/ED0/EE0`, setters `0x01833E00/E10/E20/E30`) |
| 2 PREPARE_BATTLE | `0x027047CE` | closes UI windows **`0x227`, `0x106`, `0x107`, `0x8A`** (`CNRDInterfaceManager::0x02304790(id,-1,0)`); `+0x290 = 0xFFFFFFFF`; zeroes qword `+0x294`; `vt+0x258`, `vt+0x1D8` |
| 3 COUNT | `0x02704C69` | line 1542; for each of 8 chars: `vt+0x4C0(0)`, `vt+0x4B8(0)` (reset action/AI), then `0x027010D0(slot,char,i)`; `0x026FD740` |
| 4 BATTLE | `0x02704E39` | `0x02740DD0()`; `+0x160 = 0` |
| 5 RESULT | `0x02704E4B` | `+0x160 = +0x284 - +0x280` (elapsed battle ms); `+0x280 = 0`; large observed-character swap block |
| 6 PREPAREWAIT | `0x027055A7` | (falls straight to epilogue) |
| 7 PREPARE_RELAY | `0x027052E9` | |
| 8 RELAY_FIRST_COUNT | `0x0270493D` | `+0x280 = +0x284 = 5000` (`0x1388`); per-slot team accumulation into `this + 20*team + 0x344` |
| 9 TRAINING | `0x027052FD` | |
| 10 PREPARE_PRACTICE | `0x027051FA` | |
| 11 PREPARE_DOLLMODE | `0x027054B9` | **finds inventory slot of item id `0x0099EEE5`** via `CNRDItemManager::0x016EF580`; if `-1` → error path `0x02740D30`; else **sends C→S `0x272` with u16 = that inventory index**; then per-slot `0x018AD610(1)` / `0x01832260(0)`; `+0x408`, `+0x405 = 1` |
| 12 SELECT_DOLL | `0x027055A7` | none |
| 13 COMPLETE_MAKE_DOLLS | `0x02705567` | |
| 14 PREPARE_TOTAL_MATCH | `0x027055A7` | none |
| 15 TOTAL_MATCH_COUNT | `0x02704A46` | `+0x280 = +0x284 = 5000`; `+0x2F4 = elapsed + 150`, `+0x2F0 = 150`, `+0x2EC = 0`, then `+0x2F4 += 0x02648730(...)` (a UI-dependent extra) |
| 16 …COUNT_READY | `0x02704BEF` | clears word `+0x2E4` (both `+0x2E4` and `+0x2E5`), `+0x2EC = 0`, `+0x2F4 = elapsed + 150`, `+0x2F0 = 150` |

### 1.4 Complete transition table (every `vt+0xA4` call site in the module)

Recovered by scanning for `mov r,[this]; mov r2,[r+0xA4]; call r2` with the last two pushes.

| call site | in | → state | timerArg |
|---|---|---|---|
| `0x026FCB01` | `0x026FCA50` | `WAIT` | (register) |
| `0x026FDC10` | `0x026FD9B0` blk | `PREPARE_PRACTICE` | 0 |
| `0x026FDE86` | `0x026FDE50` (vt+0x25C) | `LOADING` | 0 |
| `0x027006BB`, `0x027006CB` | `0x02700670` (vt+0x114) | `COUNT` then `BATTLE` | 0, 0 |
| `0x027006DB` | `0x02700670` | `PREPARE_TOTAL_MATCH_BATTLE` | 0 |
| `0x027017FB`, `0x0270180B`, `0x0270181B` | `0x027017B0` (vt+0x110) | `COUNT` / `BATTLE` / `PREPARE_RELAY_BATTLE` | 0 |
| `0x02701F00` | `procSTATE_PREPAREBATTLE` | `PREPARE_DOLLMODE` | 0 |
| `0x02701F26` | `procSTATE_PREPAREBATTLE` | `RELAY_BATTLE_FIRST_COUNT` | 0 |
| `0x02701F4C` | `procSTATE_PREPAREBATTLE` | `TOTAL_MATCH_BATTLE_COUNT_READY` | 0 |
| `0x0270241D` | `procSTATE_PREPARE_RELAY_BATTLE` | `COUNT` | 0 |
| `0x0270262D` | `procSTATE_PREPARE_TOTAL_MATCH_BATTLE` | `COUNT` | 0 |
| `0x02702846` | `procSTATE_PREPAREPRACTICE` | `TRAINING` | 0 |
| `0x027030C4`, `0x0270313D` | `procLoading` | `WAIT` | 0 |
| `0x02703D90` | `Proc` STATE_COUNT | `BATTLE` | `elapsed - +0x288` |
| `0x02703EA5` | `Proc` STATE_RESULT | `PREPAREWAIT` | 0 |
| `0x02704012` | `Proc` STATE_PREPAREWAIT | `WAIT` | 0 |
| `0x027040DB` | `Proc` STATE_SELECT_DOLL | `COMPLETE_MAKE_DOLLS` | 0 |
| `0x02704136` | `Proc` STATE_COMPLETE_MAKE_DOLLS | `COUNT` | 0 |

### 1.5 The sub-mode selector (what a server must decide and the client mirrors)

`procSTATE_PREPAREBATTLE` (`0x02701D50`), after the fade completes and `+0x29D == 1`
(`0x02701EE9` onward):

```
if (CNRoom::0x01A6BA40())                 -> setState(STATE_PREPARE_DOLLMODE)      // doll match
else if (m_rule->vt[0x20]() == 1)         -> setState(STATE_RELAY_BATTLE_FIRST_COUNT)
else if (m_rule->vt[0x30]() == 1)         -> setState(STATE_TOTAL_MATCH_BATTLE_COUNT_READY),
                                             *(u16*)(this+0x2E4) = 0
else                                      -> setState(STATE_COUNT)                 // plain match
```

So `m_rule` (`this+0x338`) is the PVP-rule object; `vt+0x20` = *isRelayBattle*,
`vt+0x30` = *isTotalMatch*, `vt+0x34` = *getCharacter(slot)*, `vt+0x3C` = *getSlot(i)*.

Round advance (`0x02700670`, vt+0x114): `++this->+0x2B8` (round counter), `+0x29D = 0`,
zero the 8-byte slot-ready array `+0x2B0`; then if the interface mode isn't 5, either
`setState(COUNT); setState(BATTLE)` or `setState(PREPARE_TOTAL_MATCH_BATTLE)`.

---

## 2. PACKETS THIS MODULE SENDS (C→S)

Send idiom, verified: `push arg2; push <u16 id>; call 0x0274CA50` (= `mov eax,[0x03B7CEE4]; ret`,
returns the `CNGameSocket` singleton) → `call 0x0274D1E0` = `beginPacket(u16 id, int)` →
body writers → `call 0x0274E6D0` = `sendPacket()`.

Body writers (all `CNGameSocket`, all `add [ecx+0x2BCC2C], n` then memcpy):
`0x0274D2E0` = u8, `0x0274D310` = u16, `0x0274D340` = u32, `0x0274D370` = u64.

| msg id | site | body | trigger |
|---|---|---|---|
| **`0x12A` (298)** | `0x026FCC77` in `0x026FCC40` (vt+0x240) | *empty* | called from `procLoading`; guarded by hold-timer `vt+0x22C`/`vt+0x230`; one-shot latch `+0x21D`. "arena load complete" |
| **`0x12B` (299)** | `0x026FCCF0` in `0x026FCCA0` (vt+0x244) | `u8 8` then **8×{ u8 slotIndex, u8 udpPeerReady }** where ready = `CNRDUdpModule::0x022E7080(i) != 0` | one-shot latch `+0x21E`. UDP peer-mesh status report |
| **`0x70` (112)** | `0x026FD5EB` in `0x026FD520` (vt+0x278) | *empty* | relay-battle only; fires when the local char's slot ≠ `this->+0x340` and the char is not dead; latch `+0x2DC`, then arms timer `+0x2C0` |
| **`0x39` (57)** | `0x02703FE0` in `Proc`/`STATE_PREPAREWAIT` | *empty* | after the post-result fade; latch `+0x28C`. **The client then waits for a server reply that sets `+0x28D` before leaving `STATE_PREPAREWAIT`** |
| **`0x6E` (110)** | `0x0270416B` in `Proc` tail | *empty* | while `+0x36D && +0x36E`, every 10 s (timer `+0x22C`, `0x2710` ms) |
| **`0x272` (626)** | `0x027054E1` in `setState`/`STATE_PREPARE_DOLLMODE` | `u16 inventoryIndex` | index of item id **`0x0099EEE5`** in the player's inventory; `-1` → local error, no send |
| **`0x361` (865)** | `0x02700AA3` in `0x027006E0` | `u8 this->+0x2E8` | total-match only; requires `+0x2EC == 1` (ready timer expired) and the room check `CNRoom::0x01A6B760(+0x340)` false; latch `+0x2E4` |

**The one S→C ack this module observably needs:** `0x026FE450` (vt+0xF0) is
`if (m_state == 6 || m_state == 5) this->+0x28D = 1;` — a NOTI handler entry point. Without
it the client is stuck in `STATE_PREPAREWAIT` forever after sending `0x39`.

Also observed: `procLoading` (`0x02702F80`) has a second path (`0x027030EF`) that waits on
`CNRDUdpModule::0x022E76D0()` (UDP mesh established) instead of the loading window.

---

## 3. TIMING CONSTANTS AND FORMULAS

* **Fade / transition envelope** (identical in `Proc`/`STATE_PREPAREWAIT` at `0x02703F34`,
  in `procSTATE_PREPAREPRACTICE` at `0x027027A6`, in `procSTATE_PREPAREBATTLE` at `0x02701E60`):
  `elapsed < 1000 (0x3E8)` → screen alpha = lerp(0 → 255) over 1000 ms;
  `1000 ≤ elapsed < 1700 (0x6A4)` → alpha = lerp(255 → 0) over 700 ms (`0x2BC`);
  `elapsed ≥ 1700` **and** `+0x29D == 1` → perform the transition.
  Lerp helper `0x027C4660(from, to, cur, dur)`; alpha applied via
  `CNRDInterfaceManager::0x02301190`.
* **Count-down length**: `+0x284 = +0x280 = 5000 ms` (`0x1388`) on entering
  `STATE_RELAY_BATTLE_FIRST_COUNT` (`0x027049C1`) and `STATE_TOTAL_MATCH_BATTLE_COUNT`
  (`0x02704A81`). Displayed remaining = `max(0, +0x284 - timer.elapsed)`.
* **"Ready" sub-timer**: `+0x2F4 = elapsed + 150 (0x96)`, `+0x2F0 = 150`, `+0x2EC = 0` on
  entering states 15 and 16; `Proc` decrements `+0x2F0` and sets `+0x2EC = 1` at zero.
* **Battle length shown on result**: `+0x160 = +0x284 - +0x280` at `0x02704E4B`.
* **Heartbeat**: msg `0x6E` every 10000 ms (`0x2710`, `0x02704189`).
* **PVP damage scaling — a real formula.** `0x026FF4B2`–`0x026FF4F1` (inside block
  `0x026FF290`, the same block that holds `CNPVPModule::onDiePvpCharacter`):

  ```
  float scale(float dmg) {
      if (getRoom() && getRoom()->vt[0x24]() == 1 && esi == 0)
          return dmg * Config::getFloat("pvp deathmatch attack power rate");
      return dmg;
  }
  ```
  literal `"pvp deathmatch attack power rate"` at `0x035E3F88`, config table object at
  `0x03A5B0E8`, lookup `0x022D0B60`, multiply `fmul [ebp+8]` at `0x026FF4E9`.
  A server that computes PVP damage must apply the same named multiplier in deathmatch.
* **Practice arena map**: `"Fair_PVP/Hendon_Pub_PVP.map"` (`0x035E3F48`), loaded at
  `0x026FE721` into a 0x1AA0-byte `CNRDMap` allocated from the pool (`0x027C42E0`).

---

## 4. OBJECT LAYOUT (`CNPVPModule`, offsets proven by the cited access)

| offset | type | meaning | proof |
|---|---|---|---|
| `+0x00` | vptr | primary vftable `0x035E473C` | `mov edx,[esi]; call [edx+0xA4]` throughout |
| `+0x50` | vptr | secondary base vftable `0x035E472C` (RTTI COL offset 80) | written at `0x02705993`; COL `0x03761A30` |
| `+0x54` | `CNRDStage*` | arena stage | `0x026FE6ED` → `CNRDStage::0x0198C6D0`; `0x026FE777` → `0x0198CE50`; `0x027043D1` → `0x0198BFF0` |
| `+0x58` | ptr | stage-owned object | `0x026FE6F8` |
| `+0x5C` | `int` | **m_state** | `mov [esi+0x5C],edi` `0x02704389`; `mov eax,[esi+0x5C]` `0x02703A7A` (switch) |
| `+0x60` | timer | state timer (`0x027C3410` reset / `0x027C3660` elapsed / `0x027C3B60` alarm / `0x027C3780` expired) | `0x0270438C`, `0x02703B7D` |
| `+0x160` | `int` | battle elapsed ms for the result screen | `0x02704E59` |
| `+0x184` | `bool` | time-up already fired | `0x02703E19` R, `0x026FEEDE` W |
| `+0x20C`/`+0x210` | `int` | hold-timer A/B durations (ms) | `0x026FCE13`, `0x026FCE33` |
| `+0x214`/`+0x218` | `u32` | hold-timer A/B start ticks (`[0x03029608]`, a tick import) | `0x026FCE22`, `0x026FCE42`; expiry test `0x026FCE73` |
| `+0x21C` | `bool` | flag, setter vt+0x23C `0x026FE680`, getter vt+0x238 `0x026FCC30` | `0x026FE686` |
| `+0x21D` | `bool` | msg `0x12A` sent latch | `0x026FCC43` R, `0x026FCC88` W |
| `+0x21E` | `bool` | msg `0x12B` sent latch | `0x026FCCB1` R, `0x026FCD82` W |
| `+0x220` | `CNRDMap*` | arena map (`Fair_PVP/Hendon_Pub_PVP.map`) | `0x026FE746` |
| `+0x224` | `CNRDMap*` | second/practice map (0x1AA0 bytes) | `0x026FE74C` W, `0x027028ED` W |
| `+0x228` | `CNRDObjectManager*` | 0x1C8 bytes, `new` at `0x026FE756`, ctor `0x019188E0` | getter vt+0x1E0 `0x026FD990`, setter vt+0x1E4 `0x026FD9A0` |
| `+0x22C` | timer | 10 s heartbeat timer for msg `0x6E` | `0x02704155`, `0x02704194` |
| `+0x280` | `int` | count-down **remaining** ms | `0x02703BA1`, `0x027049C8` |
| `+0x284` | `int` | count-down **total** ms | `0x02703B82` R, `0x027049CE` W |
| `+0x288` | `int` | `STATE_COUNT` duration ms | `0x02703D69` R |
| `+0x28C` | `bool` | msg `0x39` sent latch | `0x02703FF1` W, `0x02703FBB` R |
| `+0x28D` | `bool` | **server ack for `0x39`** (set by vt+0xF0 `0x026FE450`) | `0x02704005` R |
| `+0x290` | `u32` | `GetTickCount()` at practice start; `0xFFFFFFFF` on PREPARE_BATTLE | `0x02702869` W, `0x027047A7` W |
| `+0x294` | `u64` | zeroed on PREPARE_BATTLE (`movq`) | `0x027047B5` |
| `+0x29C` | `bool` | UDP module started once | `0x027041D6` |
| `+0x29D` | `bool` | fade/loading complete gate for every `procSTATE_PREPARE*` | `0x02701EBD`, `0x02702679` |
| `+0x2B0..+0x2B7` | `bool[8]` | per-slot ready flags; cleared as a qword | `0x02700684` W, getter vt+0x13C `0x026FE4A0` `mov al,[ecx+eax+0x2B0]`, toggle vt+0x138 `0x026FDA40` |
| `+0x2B8` | `int` | round counter | `inc dword [esi+0x2B8]` `0x02700673` |
| `+0x2C0` | timer | armed after msg `0x70` | `0x026FD5FE`, probe vt+0x134 `0x026FE470` |
| `+0x2DC` | `bool` | msg `0x70` sent latch | `0x026FD5DF` R, `0x026FD604` W |
| `+0x2E4` | `bool` | msg `0x361` sent latch (getter vt+0x118 `0x026FD4D0`) | `0x02700AC8` W |
| `+0x2E5` | `bool` | getter vt+0x11C `0x026FD4E0` | `0x026FD4E0` |
| `+0x2E8` | `int`/`u8` | **payload of msg `0x361`** (getter vt+0x120 `0x026FD4F0`) | `movzx edx, byte [esi+0x2E8]` `0x02700AAF` |
| `+0x2EC` | `bool` | ready-timer expired | `0x02703D0A` W, `0x02700A67` R |
| `+0x2F0` | `int` | ready-timer remaining ms | `0x02703CDF` |
| `+0x2F4` | `u32` | ready-timer deadline | `0x02703CD7` R, `0x02704B11` W |
| `+0x338` | ptr | **PVP rule/mode object** — the most-used member (91 accesses) | `0x026FCA60`, `m_rule->vt[0x20/0x30/0x34/0x3C]` everywhere |
| `+0x33C` | `CNRoom*` | getter vt+0x160 `0x026FE560` (`mov eax,[ecx+0x33C]`), 10 external callers | `0x026FD584`, `CNRoom::0x01A6B760` |
| `+0x340` | `int` | my slot index | compared at `0x026FD5C7` |
| `+0x344 + 20*team` | struct[2] | per-team accumulator, 20 bytes each, filled by `0x01BF01E0` | `lea ecx,[esi+ecx*4+0x344]` with `ecx = 5*team` at `0x02704A29` / `0x02704AE9` |
| `+0x36C` | `bool` | cleared each `STATE_WAIT` frame | `0x02703B4D` |
| `+0x36D` | `bool` | heartbeat enable (get vt+0x148 `0x026FE4C0`, set vt+0x150 `0x026FE4E0`) | `0x02704146` |
| `+0x36E` | `bool` | heartbeat enable 2 (get vt+0x14C `0x026FE4D0`, set vt+0x154 `0x026FE510`) | `0x0270415E` |
| `+0x370` | struct | address handed out by vt+0xD8 `0x026FE440` (`lea eax,[ecx+0x370]`) | `0x026FE440` |
| `+0x384` | `bool` | | `0x02703E02` |
| `+0x388/+0x390/+0x398/+0x3A0/+0x3A8/+0x3B0/+0x3BC/+0x3C4/+0x3CC/+0x3D4` | `int[2]` each | **per-team scoreboard counters** — accessors take a `[ebp+8]` index and scale by 4 | getters `0x026FE570/0x026FE590/0x026FE5B0/0x026FE5D0`, setters `0x026FE5F0/0x026FE610/0x026FE630/0x026FE660`; bulk init `0x026FCB6A`–`0x026FCBE2` |
| `+0x3B8` | `int` | scalar, setter vt+0x184 `0x026FE650` (3 external callers) | `0x026FE656` |
| `+0x3F4` | `CNRDPVPPractice*` | `PVP\CNRDPVPPractice.cpp` (`0x025C99A0`, `0x025D08E0`) | `0x026FE7A9` W, `0x02704431` R |
| `+0x3F8` | ptr | outline-highlighted object (procOutlineObject) | `0x02702E49`, `0x02702E7B` |
| `+0x3FC` | `bool` | preloading complete gate in `procLoading` | `0x02703095` R, `0x026FF08E` W |
| `+0x404..+0x408` | `bool` | misc one-shot latches (`+0x407` = doll UI opened) | `0x02704056`, `0x02704067` |

Note the arrays at `+0x388…` are **2 entries** each (team 0 / team 1); `+0x388+4*1 = +0x38C`
and `+0x388+4*2` would collide with the `+0x390` array, and the team index in
`this + 20*team + 0x344` comes from `pChar->vt[0x32C]()`.

---

## 5. PUBLIC SURFACE

The module is reached almost entirely **virtually** — its `Proc`/`DrawProc` are called by the
module manager, and NOTI handlers call it through the vtable. The direct-call surface in
`callgraph.csv` is therefore tiny: only 6 functions in the range have non-module callers.

| entry | kind | callers |
|---|---|---|
| `0x026FE560` (vt+0x160) | `getRoom()` → `+0x33C` | 10 external (`CNRDSkillBuyWindow.cpp`, `0x0181C2B0`, …) |
| `0x026FD990` (vt+0x1E0) | `getObjectManager()` → `+0x228` | 5 external, incl. `MovieDirector.cpp` |
| `0x026FE650` (vt+0x184) | setter for `+0x3B8` | 3 external |
| `0x026FE440` (vt+0xD8) | `&this->+0x370` | `0x00FC3A90` |
| `0x026FD4F0` (vt+0x120) | `get +0x2E8` (the `0x361` payload) | `0x00ED1A50`, `0x02352430` |
| `0x02705DC0` | (block, contains vt+0x8C/0x94 helpers) | `GameCoreSub.cpp` `0x0163C870` |

Named methods, from literals + vtable slots:

| VA | slot | name |
|---|---|---|
| `0x02703950` | vt+0x04 | `CNPVPModule::Proc` |
| `0x02701C30` | secondary vt slot 0 | `CNPVPModule::DrawProc` |
| `0x02704260` | vt+0xA4 | `CNPVPModule::setState` |
| `0x027037C0` | — (static helper) | `getStateName(std::string*, int)` |
| `0x02703170` | vt+0x264 | `CNPVPModule::procPreloading` |
| `0x02702F80` | vt+0x268 | `CNPVPModule::procLoading` |
| `0x02701D50` | vt+0x26C | `CNPVPModule::procSTATE_PREPAREBATTLE` |
| `0x02702280` | vt+0x270 | `CNPVPModule::procSTATE_PREPARE_RELAY_BATTLE` |
| `0x02702490` | vt+0x274 | `CNPVPModule::procSTATE_PREPARE_TOTAL_MATCH_BATTLE` |
| `0x027026A0` | — (direct call from Proc) | `CNPVPModule::procSTATE_PREPAREPRACTICE` |
| `0x02702D40` | — (direct call from Proc) | `CNPVPModule::procOutlineObject` |
| `0x026FF290` blk | vt+0x7C `0x026FF310`, vt+0xF4 `0x026FF550` | contains `CNPVPModule::onDiePvpCharacter` and the deathmatch damage scaler |
| `0x026FCC40` | vt+0x240 | send msg `0x12A` |
| `0x026FCCA0` | vt+0x244 | send msg `0x12B` |
| `0x026FD520` | vt+0x278 | relay-battle tick, sends msg `0x70` |
| `0x026FDE50` | vt+0x25C | close arena UI, `setState(STATE_LOADING, 0)` |
| `0x026FE450` | vt+0xF0 | **server-ack sink**: if state ∈ {RESULT, PREPAREWAIT} → `+0x28D = 1` |
| `0x02700670` | vt+0x114 | next-round advance |
| `0x026FE520` / `0x026FE540` | vt+0x158 / vt+0x15C | forward to `m_rule->vt[0x20]` / `m_rule->vt[0x0C]` |

Globals it depends on: `0x03A5C9B8` = CNRDInterfaceManager, `0x03A5C9B0` = CNRDUdpModule,
`0x03A5C980` = CNUserManager, `0x03B3DFB8` = currently-observed PVP character,
`0x03A5B0E8` = config table, `0x03B7CEE4` = CNGameSocket singleton,
`0x03A5C9BC / 0x03A5C9D0 / 0x03A5C9D4 / 0x03A5C9D8` = 4 saved per-character PVP fields.

---

## 6. What I could NOT determine

* The numeric meaning of the message ids (`0x39`, `0x6E`, `0x70`, `0x12A`, `0x12B`, `0x272`,
  `0x361`) in a named enum — no such table exists in this repo. They are raw
  `CNGameSocket::beginPacket` ids and are certain; the names are not.
* The identity of `m_rule` (`this+0x338`) — a distinct class whose vtable I did not chase.
  Only its slots `0x0C/0x20/0x24/0x30/0x34/0x3C` are exercised here.
* The semantics of the 10 per-team counter arrays at `+0x388…+0x3DB` (kills/deaths/score/
  round-wins are plausible but unproven — the setters are called from UI/handler code
  outside this TU).
* The 4-tuple table at `0x035E49C0` (`{0xE0,0x73,0x18,0xD8}`, `{0xE0,0xD8,0x18,0x13D}`,
  `{0xE0,0x13D,0x18,0x1A2}`, `{0xE0,0x1A2,0,0}` — y steps of 0x65) sits in this TU's data
  block; I found no code reference to it.
* The item `0x0099EEE5` required for doll mode — the id is certain, its name is not in this
  image's literals.

## Confidence

**High** for: module extent, class vtable, state enum and its index mapping, the Proc and
setState dispatch tables, the transition table, the packet ids and their bodies, the timing
constants, and the offsets listed with a proof address.
**Medium** for: the field *names* I gave (the offsets and their access patterns are proven;
the labels are inference), and for the per-team scoreboard interpretation of `+0x388…`.
