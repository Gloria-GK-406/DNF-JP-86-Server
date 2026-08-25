# TU 26 — `DNFClient\RDAR\CNRDSkillBuyWindow.cpp` (class `CNRDSkillShop`)

Briefing span `0x013C1AB0 – 0x013CF5D0`. **The real module is `0x013C1AB0 – 0x013D9F80`** (see
"Span correction"). Class name is confirmed by RTTI, not guessed.

---

## 0. Corrections to the inputs (read these first)

### 0.1 The read-primitive VAs in the task prompt are wrong by one image base

The prompt lists `read_u8 0x023C5230 … read_bytes 0x023C5380, set_remaining 0x023C4350`.
Those addresses are not the primitives. The real ones are `+0x400000`:

| primitive | real VA | proof |
|---|---|---|
| `read_u8`  | `0x027C5230` | `mov eax,[0x3BAAE38]; mov cl,[eax]; mov edx,[ebp+8]; inc eax; dec dword [0x3BAAE3C]; mov [edx],cl` |
| `read_u16` | `0x027C52A0` | same shape, word |
| `read_u32` | `0x027C5310` | same shape, dword |
| `read_bytes` | `0x027C5380` | `memcpy(dst, [0x3BAAE38], n)` @`0x2C61C00`, then `[0x3BAAE38] += n`, then `0x27C51D0(n)` |
| `set_remaining` | `0x027C4350` | `mov eax,[ebp+8]; mov [0x3BAAE3C], eax` — 16-byte function, nothing else |

`parsers.csv` is correct; only the prompt's address list is off. Inbound-stream globals:

* `0x03BAAE38` — read cursor (advances on every read)
* `0x03BAAE3C` — bytes remaining (set by `set_remaining`, decremented per read)
* `0x03BAAE40 / 0x03BAAE44 / 0x03BAAE58` — underflow refill path taken when remaining goes negative

### 0.2 Span correction — the briefing's `hi` is ~60% of the module

`tu-index.csv` bounds this TU at `0x013CF5D0`, but that is only where the *second* `__FILE__`
anchor lands. Evidence the module runs to `0x013D9F80`:

* The module's literal pool is contiguous at `0x032936F0 – 0x03294180` and ends exactly where
  `CNRDHttpImgManager.cpp`'s pool begins (`0x032941D0`). Functions referencing that pool span
  `0x013C1AB0 … 0x013D9A80` with no gap-owner in between.
* No TU in `tu-index.csv` claims `0x013CF5D0 – 0x013E1FD0`.
* Nearly every "external caller" the briefing counted (59) is at `0x013Dxxxx` — i.e. the *same
  class's* own methods: `0x13D0290, 0x13D08D0, 0x13D0B30, 0x13D12D0, 0x13D1DE0, 0x13D38A0,
  0x13D3B20, 0x13D4190, 0x13D4220, 0x13D42B0, 0x13D4500, 0x13D5A50, 0x13D5FB0, 0x13D6C80,
  0x13D8070, 0x13D9200, 0x13D9A80`.
* The **constructor is at `0x013D9A80`** — it references `__FILE__`-pool strings
  `0x3293F18 "UI/Skill/SkillShopWindow_w.xui"` and `0x3294180 "ClientOnly/SkillShopTextGuide.co"`,
  and installs the class vtables `0x0329409C` / `0x03294090`.

Literals only visible in the extended range (missing from the briefing):
`CNRDSkillShop::onEventClick`, `CNRDSkillShop::onEventLeftButtonDown`,
`UI/Skill/SkillShopWindow_w.xui`, `Interface2/UI/SkillShop/Skillshop_Tip_R.img`,
`UI/Skill/Animation/TP_skill_able/`, `Tp_skill_able.act`,
`ClientOnly/SkillShopTreeSpIndex.co`, `ClientOnly/SkillShopTreeTpIndex.co`,
`ClientOnly/SkillShopTextGuide.co`, and the tooltip tag family
`[skill info] [/skill info] [icon pos] [cell pos] [mouse marker] [next skill]`.

### 0.3 Class identity (hard evidence)

`rtti-vtables.csv`: `0x0329409C` and `0x03294090` are the two vtables of TD#6601 (primary at
offset 0, secondary base at offset 380 = `0x17C` — matches the ctor writing
`[esi+0x17C] = 0x3294090`).
`rtti-full-a21.csv` @ `0x032938B8` carries the undecorated mangled name:

```
_Callable_pmf@Q8CNRDSkillShop@@AEXW4TYPE@control_event@@HH@Z
```

i.e. a `std::tr1::bind` of the **private** member `void CNRDSkillShop::onControlEvent(control_event::TYPE, int, int)`
with three placeholders. Referenced from `0x013C3D30` and `0x013C5620`.

---

## 1. What this unit implements

`CNRDSkillShop` — the **skill window / skill-purchase (SP·TP spend) UI**. It owns:

* a skill-tree preview model (`this+0x230`) holding *pending* level deltas the player clicks up/down
  before committing;
* the SP and TP tree layouts loaded from `ClientOnly/SkillShopTreeSpIndex.co` /
  `SkillShopTreeTpIndex.co`;
* skill→quickslot registration (drag-drop, auto-register, quickslot sort);
* skill sealing (SEAL_ON/SEAL_OFF), cooltime alarms, tooltip/explain panel, skill replay video;
* three inbound CMD-result handlers.

**The module never constructs an outbound packet.** It contains zero references to the net-send
global `0x3A5C9B0` across all 155 functions of the extended span, and `sendscan.txt` (1372
`MakePacket` sites image-wide) lists none in `0x013Cxxxx`/`0x013Dxxxx`. All requests are issued by
the skill-tree model object (`this+0x230`, ctor `0x015E7710`) and by the quickslot manager.
This unit is **receive + UI only**.

---

## 2. Server-relevant: the packet framework this unit plugs into

Followed outward from this unit's three handlers. Ground truth is
`CNGameCore::NetworkProc` in `D:\Work\1229\Source\DNFClient\RDAR\GameCoreNetwork.cpp`
(decrypted literals at `0x32CB274` / `0x32CB050`), function `0x0163BBD0`.

### 2.1 Wire header — 15 bytes

```
+0x00  u8    packet type
+0x01  u16   command / notification id      <-- the dispatch key
+0x03  u32   total length (15 + body)
+0x07  8 bytes (remainder of header)
+0x0F  body
```

Evidence: `movzx ecx, word ptr [edi+1]` (`0x0163BC35`, `0x0163BF4B`, `0x0163C055`, and the
dispatcher `0x01637687`); `mov ebx,[edi+3]` then `lea ecx,[ebx+0xF]` bounded against `0xAF000`
(`0x0163BC70`); body start = `edi+0xF` (`0x0163BD72`); and
`push (totalLen - 0xF); call 0x27C4350 /*set_remaining*/` at `0x0163BEB2`. Max packet `0xAF000`.

`[edi+0]` is a 3-way switch at `0x0163BECC`:

| type | meaning | log format | dispatch |
|---|---|---|---|
| `0` | **NOTI** (server push) | `"[NOTI RECV] %d (Size : %d)"` @`0x32CB198` | `0x016377E0` → map **B** |
| `1` | **CMD result** | `"[CMD RECV] %d (Size : %d) Result : %s"` @`0x32CB220` | `0x01637680` → map **A** |
| else | rejected | `"HACK>> Network Packet Type Error [ %d ]"` @`0x32CB2B0` | returns `0x80000000` |

### 2.2 CMD-result body prefix (type 1) — this is what makes my three handlers make sense

At `0x0163BF26`–`0x0163BF89`, unconditionally on the type-1 path:

```c
u8 bResult = read_u8();                 // 0x0163BF30
if (bResult == 0) u8 nErrCode = read_u8();  // 0x0163BF73
// log: Result : "Ok" (0x32CB1EC) | "Error" (0x32CB1D8) + " ErrCode : %d" (0x32CB1FC)
```

Then `0x01637680(pPacket, bResult, nErrCode)`, which looks up map A by the u16 id and calls:

```c
void __cdecl handler(unsigned short nCmdId, bool bResult, unsigned char nErrCode, void* pContext);
```

(`0x016376FD`–`0x0163770E`: `push [node+4] /*ctx*/; push arg3; push arg2; push esi /*cmdId*/;
call [node+0]; add esp,0x10`.)

So the per-command payload starts **after** `bResult` (and after `nErrCode` when `bResult == 0`).
This exactly explains why all three SkillShop handlers gate their reads on `[ebp+0xC] != 0`.

For NOTI (type 0) the callback is 2-arg: `void __cdecl f(u16 nId, void* pContext)` and it parses
the body itself (`0x016377E0` → `0x0163C510` lookup → `push ctx; push id; call [node]`).

### 2.3 Handler registration

Two separate registries, both `std::map`-style keyed by u16 id, value = heap `{fn, ctx}` 8-byte node
(`0x0163C4A0`):

| helper | singleton | flag | map object | used for |
|---|---|---|---|---|
| `0x01637660(map, id, fn, ctx)` | `0x0163C410` | `0x3A5BE6C` | `0x3A5BE68` | **CMD-result listeners** |
| `0x01637720(map, id, fn, ctx)` | `0x0163C550` | `0x3A5BE78` | `0x3A5BE74` | **NOTI parsers** |

Both are reached as `call 0x1102C30 /*thunk → 0x1637C60, gets the 0xB0208-byte core singleton at
0x3A5B348*/ ; mov ecx,eax ; call 0x1637660|0x1637720`. There are **1315 such registration sites**
image-wide (extracted to `_u26_regs.json` in SCRATCH) — a complete `id → handler` map for the whole
client, which is probably the single most valuable artefact this pass produced.

### 2.4 Two runtime id→name tables (empty in this dump — capture live)

* `0x03B51F20[id*4]` — NOTI id → name (`0x0163C11F`)
* `0x03B53230[id*4]` — CMD id → name (`0x0163C059`)

Both read all-zero in `DNF-memory-rva.bin`, so they are populated later than the capture point or
not in a dumped section. A live read of these two arrays yields the whole `ENUM_CMDPACKET` /
`ENUM_NOTIPACKET` enum by number. Command id `0x2D2` is special-cased out of the trace log
(`0x0163C00D`).

---

## 3. Server-relevant: this unit's three inbound handlers

Registered in the constructor `0x013D9A80` at `0x013D9CF8`–`0x013D9D3C`, all via `0x1637660`
(CMD-result listeners) with `pContext = NULL`:

| CMD id | handler VA | body on success | error codes handled |
|---|---|---|---|
| `0x1EB` (491) | `0x013C1CE0` | *(none)* | `0x15` distinguished; all others share one message |
| `0x1EC` (492) | `0x013CA9B0` | `u8` | `0x07`, `0x0C`, `0x13` |
| `0x311` (785) | `0x013C1C50` | `u32 errorCode` | *(none — logs the payload)* |

### 3.1 `0x311` = `CMDPACKET_SKILL_QUICK_SLOT_SORT` — name proven, not joined

The enum name is pushed **inside the handler itself** at `0x013C1C97`
(`push 0x32937C0` → `"CMDFUNC_ENUM_CMDPACKET_SKILL_QUICK_SLOT_SORT"`), and the handler is the one
registered for `0x311` at `0x013D9D19`. So `0x311 = CMDPACKET_SKILL_QUICK_SLOT_SORT`.

> Caveat worth recording: a naive `literals.csv` join says `0x1EB` is also
> SKILL_QUICK_SLOT_SORT. That is **false** — an artefact of `functions.csv` merging
> `0x013C1AB0/0x013C1C50/0x013C1CE0` into one row, so all three share the literal attribution.
> Only the disassembly distinguishes them.

```c
// 0x013C1C50 __cdecl (id, bResult, nErrCode, ctx)
if (bResult) {                                  // 0x013C1C53
    u32 errorCode = read_u32_into(&slot, 4);    // 0x013C1C66  read_bytes(&x, 4)
    0x5BEA10(0x5BF6F0(6, -1, 0));               // 0x013C1C74
    WriteLog(0, __FILE__, "CMDFUNC_ENUM_CMDPACKET_SKILL_QUICK_SLOT_SORT", 5791,
             "\nCMD SkillQuickSlotSort errorCode=%d", errorCode);   // 0x013C1CB5
}
if (0x63D780()) 0x63B860(that);                 // always: rebuild quickslot from server state
```

**Server note:** the success body of this response carries a `u32` that the client itself calls
`errorCode`, i.e. the server sends an application-level result code *inside* a `bResult == Ok`
response. The client only logs it and then unconditionally re-reads the quickslot state — so a
server that returns anything non-zero here will still see the client resync rather than error out.

### 3.2 `0x1EC` — SP acquisition / grant (handler `0x013CA9B0`)

```c
if (bResult) {                                     // 0x013CA9D9
    u8 v = read_u8();                              // 0x013CA9ED
    if (g_pMyChar /*0x3B3DFB8*/) 0x18327B0(v);     // debug-only
    InterfaceMgr(0x3A5C9B8)->show(0x209, getStr(0x8D82), 0);   // 0x013CAA24
    PlaySound(decrypt("SP_GET"), -1, 0, 0, -1, -1);            // 0x013CAA41 → 0x1648A20
    0x11F27D0(0x11F3FF0(7));
    0x5BEA10(0x5BF6F0(5, -1, 0));
} else {
    switch (nErrCode) {                            // 0x013CAA84, arg at [ebp+0x10]
      case 0x07: msg = getStr(0x8D83); break;      // 0x013CAA9A
      case 0x0C: msg = getStr(0x8D85); break;      // 0x013CAABD
      case 0x13: msg = getStr(0x8D84); break;      // 0x013CAADD
      default:   msg = L"";                        // 0x013CAB09
    }
    InterfaceMgr->show(0x209, msg, 0);
}
```

`0x28C14C0` is confirmed to be `getStr(int)` by its own literal `"getStr(%d) == none"`. So
`0x8D82/0x8D83/0x8D84/0x8D85` are string-table ids — resolving them against the client `.str`
gives the human meaning of error codes 7 / 0x0C / 0x13.

### 3.3 `0x1EB` — pure ack, no body (handler `0x013C1CE0`)

```c
InterfaceMgr(0x3A5C9B8)->closeWindow(0x20A, -1, 0);       // 0x013C1CF2, entry
if (bResult) {                                            // 0x013C1CF7
    InterfaceMgr->show(0x209, getStr(0x8E09), 0);
    0xE75B80(guideMgr /*0x3A5C9EC*/, 0x11, 0x2B37, 1, 1, 1);   // guide/tutorial step
} else {
    msg = (nErrCode == 0x15) ? getStr(0x2BBC)              // 0x013C1D3B
                             : getStr(0x8E0A);
    InterfaceMgr->show(0x209, msg, 0);
}
```

`0x1EB` is **also** registered by an unrelated class at `0x011D85F0` — but via `0x1637720`
(the NOTI/parser map), with handler `0x011D83E0` which does `read_u16(); read_u8();`.
Different registry, no conflict. The exact enum name of `0x1EB`/`0x1EC` is **unknown** — neither
handler logs its name and the runtime name table is empty in the dump.

---

## 4. Server-relevant: recovered formulas / table layouts

### 4.1 Quickslot bank assignment — `0x013C26D0`, feeding `getEmptySlotIndex` `0x013C2D50`

```c
int bankOf(Skill* s) {                       // 0x013C26D0
    if (!s) return 0;
    T* t = s->tpl();                         // 0x19AB760
    if (t->b_8EC != 0)             return 3; // 0x013C26EA  byte at tpl+0x8EC (TP-skill flag)
    if (protected_read(t + 0x94) == 4) return 2;  // 0x013C2760
    if (((t->p_788 - t->p_784) >> 3) <= 2) return 1;  // 0x013C2778, vector<8-byte>
    return 0;
}

int CNRDSkillShop::getEmptySlotIndex(Skill* s) {   // 0x013C2D50
    if (!s) return -1;
    int base = (bank==0) ?   6 :                   // 0x013C2D6E
               (bank==1) ?  54 :                   // 0x013C2D78  (0x35+1)
               (bank==2) ? 102 :                   // 0x013C2D82  (0x64+2)
               (bank==3) ? 150 : 0;                // 0x013C2D92  (-1 & 0x96)
    for (int i = 0; i < 0x30; ++i, ++base)         // 0x013C2DC9  48 slots per bank
        if (!QuickSlotMgr()->getSlot(base)) break; // 0x164C5D0 → 0x19D5DD0
    return base;
}
```

**Quickslot index space: `[0..5]` reserved, then four contiguous banks of 48 at 6 / 54 / 102 / 150
(next boundary 198).** The same `0x30` bound and the same `getEmptySlotIndex` call reappear in
`onCheckSkillDrag` (`0x013CE07A`, `0x013CE2BE`). A server validating `SKILL_QUICK_SLOT_SORT` must
use this index layout.

The `0x27BC0F0 / 0x27BC160 / 0x27BC1D0 / 0x27BC240 / 0x27BC390` family selected by `ptr & 0xF`
(`cmp edx,0xC; ja …; jmp [edx*4 + table]`) is the client's **obfuscated/protected scalar read** —
any field loaded through it is stored split, not as a plain int. `tpl+0x8C` and `tpl+0x94` are both
protected.

### 4.2 Auto-quickslot eligibility — `CNRDSkillShop::isAutoNotifiableSkill` `0x013C6DA0`

```c
bool isAutoNotifiableSkill(Skill* s) {
    if (protected_read(s->tpl() + 0x8C) != 0) return false;      // 0x013C6E2F
    QuickSlotMgr(__FUNCTION__, 3300)->collectRegistered(&v, 1);  // 0x013C6E85 → 0x19D7BE0
    for (Skill* q : v) if (q == s) return false;                 // 0x013C6EAD
    vector<int> types = read(s->tpl() + 0x9F8);                  // 0x013C6EC2 → 0xFABA60
    bool has10 = false, has456 = false;
    for (int t : types) {
        if (t == 10) has10 = true;                               // 0x013C6EF5
        if (t == 4 || t == 5 || t == 6) has456 = true;           // 0x013C6EFC-0x013C6F09
    }
    return has10 && !has456;                                     // 0x013C6F14/0x013C6F1C
}
```

### 4.3 Auto skill-tree replay — `CNRDSkillShop::setAutoSkillTree` `0x013CF5D0`

Takes a `vector<pair<int,int>>` of `(skillIndex, targetLevel)` (element stride 8, `sar edx,3` at
`0x013CF650`), dedups it, then for each entry loops:

```c
base    = skill->getLevel(0);                     // 0x19AE100(obj, 0)
delta   = tree->pendingDelta(skillIndex);         // 0x15DACB0
total   = base + delta;
while (total != target) {
    op = (total < target) ? 2 /*level up*/ : 3 /*level down*/;   // 0x013CF945 / 0x013CF9BC
    mode = f([this+0x2C0]);                                     // 0x282BDA0 → 0/1/2
    node->apply(&tmp, 0, op, mode);                             // 0x15D59C0
    tree->recalc();                                             // 0x15E5260
    total = base + tree->pendingDelta(skillIndex);
}
```

If it cannot converge it logs
`"skill error - index [%d] original[%d] modified[%d]"` (`0x3293CE0`, line 6692, `0x013CFA2A`) with
`(skillIndex, base, base+delta)`. **Op codes 2 = increase, 3 = decrease** on the pending-delta model.

Guards before applying: `tree->isMaxed(skillIndex)` (`0x15D9E80`, `0x013CF8C6`),
`g_pMyChar->vf[0xACC](skillIndex, 1)` returns the live skill (`0x013CF875`),
and `skill->tpl()->b_794 != 0` blocks it (`0x013CF8F2`).

### 4.4 Skill seal toggle — `0x013C6D20` (client side of SEAL_ON/SEAL_OFF)

```c
if (!s || !isValid(s) /*0x19B99B0*/) return;
bool sealed = 0x19AE5A0(s);
s->setSeal(sealed ? 0 : 1, 1);                     // 0x19BA750
PlaySound(sealed ? "SEAL_OFF" : "SEAL_ON");        // 0x3293AD8 / 0x3293AC0 → 0x1648A20
this->refreshIcon(s);                              // 0x013C63F0
```

No packet here. `0x19BA750` (which touches protected field `tpl+0xAC8`) is where a server
implementer should look for the seal-state sync.

### 4.5 Event gate for the free-SP UI — `0x013C61F0`

```c
if (EventTable(0x884)->isActive() /*0x141A170 → 0x14172D0*/
    && CoreSingleton()->0x163D0C0() && !CoreSingleton()->0x163E260()
    && this->m_pFreeSpAni /*+0x470*/ == NULL)
        ResMgr(0x3BAB2D8)->vf[0x1C](&out, "Interface2/event/event_free_sp_init.img");
```

Event/flag id **`0x884` (2180)** gates the free-SP-reset UI.

---

## 5. Object layout — `CNRDSkillShop`

Recovered from the constructor `0x013D9A80` (allocation sizes and array ctor/dtor calls are
unambiguous) plus `this`-relative accesses across the module.

| offset | size | evidence | meaning |
|---|---|---|---|
| `0x000` | 4 | `mov [esi],0x329409C` @`0x13D9AF1` | primary vtable |
| `0x17C` | 4 | `mov [esi+0x17C],0x3294090` @`0x13D9AF7` | secondary base vtable (RTTI offset 380) |
| `0x1A8` | 2 × 0x14 | `__ehvec_ctor(&esi[0x1A8], 0x14, 2, 0x99ECD0, 0xC0E220)` @`0x13D9B01` | array[2] of 0x14-byte objects |
| `0x1D0` | 4 | `= -1` @`0x13D9D44`; read in `0x13C1DC0/0x13C1E50/0x13C2160/0x13C22B0/0x13C24B0` | selected index (-1 = none) |
| `0x1D4` | 4 | `= 0xE` @`0x13D9D5D` | count/limit, read in `0x13C6140` |
| `0x1D8` | 4 | `= -1` @`0x13D9D50` | second index (-1 = none) |
| `0x1DC` | 0x30 | ctor `0x15D2D10` @`0x13D9B1C`; `0x15D2C10(this+0x1DC, id, 0xA)` in `onEventClick` | control-id → widget table |
| `0x20C` | 0x28 | ctor `0x15D9CF0` @`0x13D9B2B` | — |
| `0x230` | 4 | `=0` in ctor; allocated `0x164` bytes + `0x15E7710` @`0x013D6D7A-0x013D6DA7` | **skill-tree / pending-delta model** (22 reads in the narrow span, 116 in the module) |
| `0x234` | 0x2C | ctor `0x15F1E50` @`0x13D9B3A`; `0x121D970(&tmp, &this[0x234], id, 0xA)` in `onEventClick` | callback slot table |
| `0x260` | 0x2C | ctor `0x15F1E50` @`0x13D9B49` | callback slot table |
| `0x28C` | 0x2C | ctor `0x15F1E50` @`0x13D9B58` | callback slot table |
| `0x2B8` `0x2BC` | 4+4 | zeroed; assigned as a refcounted pair from `0x15CD6E0` @`0x13D9DCE` | smart-ptr {obj, refblock} |
| `0x2C0` | 4 | `0x282BDA0([this+0x2C0])` → 0/1/2 in `setAutoSkillTree`, 15 reads | SP/TP mode combo |
| `0x2C4` | 4 | assigned @`0x13D6DED` | smart-ptr |
| `0x2C8` | — | 12 reads in `0x13C37F0` | — |
| `0x2D0` | 4 | `0x17639F0`/`0x15C7460`/`0x15C7490` in `onEventClick` | drag/tooltip control |
| `0x2D8/0x2E0/0x2E8` | — | 22 reads in `0x13C7AF0` | — |
| `0x2F0` | 2 × 8 | `__ehvec_ctor(…, 8, 2, 0x42D970, 0x4EC340)` @`0x13D9BCA` | array[2] of 8-byte UI smart-ptr |
| `0x318` | 9 × 8 | `__ehvec_ctor(…, 8, 9, …)` @`0x13D9C0C` | array[9] of UI smart-ptr |
| `0x360` | 4 × 8 | `__ehvec_ctor(…, 8, 4, …)` @`0x13D9C2A` | array[4] of UI smart-ptr |
| `0x380` | 4 × 8 | `__ehvec_ctor(…, 8, 4, …)` @`0x13D9C48` | array[4] of UI smart-ptr |
| `0x3A0` | 0x14 | 8-byte node self-linked circular @`0x13D9C4D-0x13D9C7E` | `std::list` head |
| `0x3B4` | 0x14 | ctor `0x1083200` @`0x13D9C8B` | — |
| `0x3C8` | 0x30 | ctor `0x8DA400` @`0x13D9C9A` | — |
| `0x3DC…0x3EC` | — | read in `0x13C2DE0` | — |
| `0x3F1` | 1 | `cmp byte [esi+0x3F1],0` / `mov [esi+0x3F1],al` @`0x13C1B7F`/`0x13C1BE8` | key-edge latch in `procKeyBoardInput` |
| `0x3F2` | 1 | `= 0` @`0x13D9D4A` | flag |
| `0x3F8` | 0x14 | ctor `0x27118F0` @`0x13D9CA9`; `0x11B8320(&this[0x3F8], "ClientOnly/SkillShopTextGuide.co")` @`0x13D9D7B` | text-guide table from `.co` |
| `0x40C` | 0x20 | ctor `0x27C3A90` @`0x13D9CB8`, then `0x27C3410`/`0x27C3CB0` | timer |
| `0x428` | 4 | `= 0xC8` @`0x13D9D9D` (inside the `0x40C` object) | 200 ms interval |
| `0x42C` | 0x20 | ctor `0x27C3A90` @`0x13D9CC7` | timer |
| `0x448` | — | read in `0x13C7F20` | — |
| `0x44C` | 4 | `0x423460(0x7C)` + `0x2843800(obj,0)` then `0x670D50(obj,1)` @`0x13D9E99-0x13D9ECC` | owned 0x7C-byte object |
| `0x450` | — | read in `0x13C37F0` | — |
| `0x460/0x464/0x468` | — | read in `0x13C2C60` | — |
| `0x46C` | 1 | `= 0` @`0x13D9ED4` | flag |
| `0x470` `0x474` | 4+4 | zeroed @`0x13D9CCC`; assigned from `0x498CF0` @`0x13D9E56/0x13D9E66`; null-checked in `0x013C61F0` | free-SP event animation smart-ptr |
| `0x484` | 0x20 | ctor `0x27C3A90` @`0x13D9CE2` | timer |

Related **skill-template** offsets (class reached via `0x19AB760`), used by this unit:

| offset | access | meaning |
|---|---|---|
| `+0x8C` | protected read, `0x013C6DD2` | must be 0 for auto-quickslot |
| `+0x94` | protected read, `0x013C2705` | category; `== 4` ⇒ quickslot bank 2 |
| `+0x784/+0x788` | `0x013C2778` | `vector<8-byte>`; size ≤ 2 ⇒ bank 1 |
| `+0x794` | byte, `0x013CF8F2` | blocks the auto skill-tree apply |
| `+0x870[0..0x14]` | byte[21], `0x013CA8A8` | per-attribute-row presence; the explain panel skips rows 0,1,2,6 |
| `+0x8EC` | byte, `0x013C26EA` | TP-skill flag ⇒ quickslot bank 3 |
| `+0x8ED` | byte, `0x013CA78D` | second level line visible |
| `+0x9F8` | vector<int>, `0x013C6EC2` | input/command types (10 required, 4/5/6 disqualify) |
| `+0xAC8` | protected, `0x019BA7C0` | seal state |

---

## 6. Public surface (what is called from outside `CNRDSkillShop`)

Once the span is corrected, only a handful of functions are genuinely external entry points; the
rest of the "59 external callers" are the class's own `0x013Dxxxx` methods.

**Genuinely external (callers outside `0x013C1AB0–0x013D9F80`):**

| VA | external callers | what it is |
|---|---|---|
| `0x013C2250` | `0x81D8D0, 0xE1CF80, 0x132B2F0, 0x17DB130, 0x280EFC0, 0x2B3CCE0` | small leaf accessor (96 B, 0 callees) — most-shared entry point |
| `0x013C21B0` | `0x142ED50, 0x1654710, 0x16568C0, 0x1861500, 0x2895220, 0x28A8ED0` | accessor (160 B) |
| `0x013C7F00` | `0x15F0E50, 0x15F1B40, 0x19D4D10, 0x19D4F10` | 32-byte thunk |
| `0x013C5490` | `0xC0E8C0, 0xC282C0, 0x13D4500, 0x2B30CB0` | 192 B |
| `0x013C27C0` | `0x13D42B0, 0x13D5A50, 0x13D5FB0, 0x2543B60` | 64 B |
| `0x013C2350` | `0x1119280, 0x1157A70, 0x11781D0` | 32 B |
| `0x013C2320` | `0x112D150, 0x11781D0, 0x15E6910` | 48 B |
| `0x013C2D50` | `0x135AD60, 0x1630A00` | `getEmptySlotIndex` (§4.1) |
| `0x013CF5D0` | `0x12A7860` | `setAutoSkillTree` (§4.3) |
| `0x013CDE80` | `0x1014100` | `onCheckSkillDrag` |
| `0x013C29D0` | `0x1721E20, 0x1745520` | 176 B |
| `0x013C26D0` | `0x15F0D60, 0x15F11B0` | bank classifier (§4.1) |
| `0x013C7F20` | `0x112D150` | uses `Interface/WindowCommon.img` |
| `0x013C6D20` | *(only `0x13D5FB0`)* | seal toggle — internal |
| `0x013C2850` | `0x1A85A60` | 240 B |
| `0x013C4070` | `0x1840890` | 48 B |
| `0x013C2160` | `0x251D880` | 80 B |
| `0x013C1E40` | `0x15E7110` | 16 B |
| `0x013C7050`, `0x013C5A80`, `0x013C5BA0`, `0x013C2490` | `0x2543B60` | called as a group by one outside module |

**Named methods (from the client's own `__FUNCTION__` literals):**

`procKeyBoardInput` `0x013C1AB0` · `onClose` `0x013C1E50` · `getEmptySlotIndex` `0x013C2D50` ·
`updateQuickSlotSkill` `0x013C40A0` · `updateCoolTimeAlarmEnable` `0x013C4480` ·
`isAutoNotifiableSkill` `0x013C6DA0` · `onCheckSkillDrag` `0x013CDE80` ·
`setAutoSkillTree` `0x013CF5D0` · `onEventClick` `0x013D5FB0` · `onEventLeftButtonDown` `0x013D8F80` ·
ctor `0x013D9A80` · scalar-deleting dtor `0x013D9F50` (→ `0x013D95D0`).

**UI component ids requested via `0x01A26BB0(this, &smartPtr, id)`** (2-dword refcounted handle;
`[out]` = raw ptr, `[out+4]` = refblock, released with `0x4EC340`):

`0x0A, 0x11, 0x12, 0x13, 0x15, 0x17, 0x1B, 0x65, 0xC9, 0x3E8, 0x3E9, 0x3EC, 0x3ED, 0x3EE,
0xBB8, 0xBB9, 0xBBA, 0xBBD, 0xBBE, 0xFA0, 0xFA1, 0xFA2, 0xFAC, 0xFAD, 0xFAE, 0x1F3D, 0x1F40,
0x2330, 0x2331` (plus `0x64`, `0x19D`, `0x1` from the ctor/open path).

`onEventClick` `0x013D5FB0` is the control-id switch: jump tables at `0x13D6A78` (ids `0x3E8..0x3EE`)
and comparisons against `0xBB8`, `0xBBA`, `0xBBD`. Window ids it drives:
`0x2C1` (`procKeyBoardInput`, `0x013C1AC1`), `0x301` (`onEventClick`, `0x013D5FE6`),
`0x20A` (closed by the `0x1EB` handler), `0x209` (system-message sink for all three handlers).
`0x2301820(id)` = isOpen, `0x2304790(id,-1,0)` = close, `0x2304C70(0x209, str, 0)` = post message
— all on `g_InterfaceManager` at `0x03A5C9B8` (`CNRDInterfaceManager.cpp`).

**Resources this module loads:**
`UI/Skill/SkillShopWindow_w.xui` · `Interface2/UI/SkillShop/SkillShop.img` /`Skillshop.img` ·
`Interface2/UI/SkillShop/classchange_skill.img` · `Interface2/UI/SkillShop/SkillExplainIcons.img` ·
`Interface2/UI/SkillShop/Skillshop_Tip_R.img` · `Interface2/event/event_free_sp_init.img` ·
`Interface/WindowCommon.img` · `UI/Skill/Animation/TP_skill_able/Tp_skill_able.act` ·
`ClientOnly/CommonSkillList.co` (`0x013CE7E0`) ·
`ClientOnly/SkillShopTreeSpIndex.co` + `ClientOnly/SkillShopTreeTpIndex.co` (`0x013D4500`) ·
`ClientOnly/SkillShopTextGuide.co` (loaded into `this+0x3F8` by the ctor) ·
`Replay/SkillReplay/%s/` + `%s.rep` (`0x013CC1C0` — skill demo videos).

---

## 7. Useful helper identifications (reusable by other analysts)

| VA | signature | proof |
|---|---|---|
| `0x027BACC0` | `wchar_t* decryptLiteral(void*)` | every literal push is wrapped in it |
| `0x027BE270` / `0x027BE370` | `WriteLog(level, __FILE__, __FUNCTION__, __LINE__, fmt, …)` cdecl, `add esp` matches arg count | `0x013C1CB5`, `0x0163BF0F` |
| `0x028C14C0` | `const wchar_t* getStr(int id)` | own literal `"getStr(%d) == none"` |
| `0x01648E70` / `0x0164C5D0` | `Singleton* get(const char* __FUNCTION__, int __LINE__)` | `push 0x15C4 /*line*/; push nameLit; decrypt; push; call` — the 0x15xx values are **line numbers, not ids** |
| `0x01A26BB0` | `SmartPtr& CNRDWindow::getComponent(SmartPtr& out, int id)` | `mov ecx,[eax]; mov edx,[ecx]; call [edx+0xC](bool)` = SetVisible |
| `0x019AB760` | `SkillTemplate* Skill::tpl()` | 18 users in this unit |
| `0x019AE100` | `int Skill::getLevel(int which)` | `which` ∈ {0,1} → SP / TP level |
| `0x027BC0F0/160/1D0/240/390` | protected scalar read, dispatched on `ptr & 0xF` | `cmp edx,0xC; ja …; movzx edx,[tbl]; jmp [edx*4+tbl2]` |
| `0x00423460` | `operator new(size)` | 21 users |
| `0x02C61C00` / `0x02C62020` | `memcpy` / `memset` | `0x0163BCBB` / `0x0163BCD2` |

Working files left in SCRATCH: `u26.asm` (narrow span, per-function), `u26x.asm` (extended module
`0x013C1AB0–0x013D9F80`), `_u26_regs.json` (all 1315 CMD/NOTI registration sites image-wide as
`[regFuncVA, siteVA, id, handler, ctx]`).

---

## 8. Explicitly unknown

* The enum names of CMD `0x1EB` and `0x1EC` — the runtime name tables `0x03B51F20` / `0x03B53230`
  are zero in this dump. `0x311` is named only because its handler logs the string itself.
* What string ids `0x8D82–0x8D85`, `0x8E09`, `0x8E0A`, `0x2BBC` say — needs the client `.str` table.
* The class name of the `this+0x230` skill-tree model (0x164 bytes, ctor `0x015E7710`) — no TU
  claims that address range and it references no `__FILE__` of its own.
* Where the skill level-up commit is actually sent. It is **not** in this module. The nearest
  candidates from `sendscan.txt` are `0x015FC3D0` (many `MakePacket` with id `0x204`, some `0x201`)
  and `0x015E6910` (id `0x1D`) — unverified, flagged for a follow-up.
