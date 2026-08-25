# TU 17 — `DNFClient\RDAR\CNRDItemManager.cpp`  (0x016ECB40 – 0x01701CE0)

Span from `tu-index.csv` is `0x016ECB40..0x017012D0`; the last anchored function
`0x017012D0` is 2576 bytes long, so the real module tail is **0x01701CE0**.
192 functions, 0 parser functions.

## Verdict on the span

**The span is correct and clean.** Evidence:

* Every literal referenced from inside it lives in one contiguous string block
  `0x032D9298 .. 0x032D9C38`, and the block is bracketed by the neighbours'
  blocks: `0x032D9018` `CNRDStackable Script Find Fail : %d` (used by
  `0x016EB3C0`, below the span) and `0x032DA130`
  `…\RDAR\CNRDDungeon.cpp` (used by `0x01723D70`, above it).
* `func-source-file.csv` has no function inside the span attributed to any other
  file. The nearest foreign anchors are `0x016EB3C0` (CNRDStackable.cpp) below
  and `0x01723D70` (CNRDDungeon.cpp) above.

Caveat: a handful of functions in the span are **MSVC template COMDATs**, not
ItemManager logic (see "Not this class" at the end). They are physically here
because the linker put them here, and their high external caller counts are
misleading.

---

## 1. What this unit is

`CNRDItemManager` is the client-side **owner of one item space** (inventory,
equipment, avatar, vault tab, …) for one actor, and it is the **outbound request
side of the whole item protocol**. It holds a fixed-size slot vector, a secure
(encrypted-in-memory) slot count and space-kind, an in-flight-request queue, and
it builds and sends ~30 distinct C→S message types.

It contains **zero** inbound parsers (`parsers.csv` count = 0 for the span).
Inbound handling lives elsewhere; those handlers call back into this unit to
clear the in-flight marker (see §4).

There are ~9 global singleton instances plus a 5-element vault array:

| global | uses as `this` into this TU | note |
| --- | ---: | --- |
| `0x3A5C9BC` | 1228 | dominant — the character inventory |
| `0x3A5C9D0` | 105 | |
| `0x3A5C9D4` | 72 | |
| `0x3A5C9CC` | 27 | |
| `0x3A5C9DC` | 25 | |
| `0x3A5C9C0` / `0x3A5C9C4` / `0x3A5C9D8` / `0x3A5C9E4` | 24/20/17/17 | |
| `0x3A5C8DC[0..4]` | — | the 5 vault/bank managers, scanned by `0x016ECC20` |

Other globals it uses: `0x3B3DFB8` = my character (`CNRDMyCharacter*`),
`0x3A5C9B8` = the system/chat message window, `0x3A5C980` = party manager,
`0x3A5C9E8` = a "busy" gate (`0x1705320`), `0x3A8F1D0` = stackable-moving depth
counter.

---

## 2. What a server implementer needs

### 2.1 The outbound frame the whole unit uses

Not defined in this TU but every send here goes through it, so it is the frame
the server must decode. Writer family (in the network TU):

| VA | role |
| --- | --- |
| `0x0274CA50` | `getSendStream()` → global `[0x3B7CEE4]` |
| `0x0274D1E0` | `beginPacket(u16 msgType, u8 flag)` — `ret 8` |
| `0x0274D2E0` | `write_u8`  (`+= 1` to len at `stream+0x2BCC2C`) |
| `0x0274D310` | `write_u16` (`+= 2`) |
| `0x0274D340` | `write_u32` (`+= 4`) |
| `0x0274D370` | `write_u64` (`+= 8`) |
| `0x0274D3A0` | `write_blob(void*, u32 len)` — u32 length prefix + bytes |
| `0x0274D410` | `write_cstring(char*)` — u32 strlen prefix + bytes, no NUL |
| `0x0274D4A0` | `write_cstring(decrypt(lit))` |
| `0x0274CED0` | `write_raw(void*, u32 len)` — **no** length prefix |
| `0x0274E6D0` | `sendPacket()` — finalises header and hands to the socket |

**14-byte header** (built at `0x0274D264..0x0274D2AF`, patched at
`0x0274E8FD..0x0274E947`):

```
off 0    u8   0x01                      (constant, set at 0274D278)
off 1-2  u16  msgType                   (0274D267: mov [ebp-0x13], di)
off 3-6  u32  totalLen = payloadLen+14  (0274E907: mov [esi+3], eax)
off 7-10 u32  checksum                  (0274E944: mov [esi+7], eax)
                 = 0x274C9C0(&hdr[12], payloadLen+2)  -- covers seq + payload
off 11   u8   flag                      (0274D283; 0 at every call site in this TU)
off 12-13 u16 sequence                  (0274E91A, from stream+0x2BCC38)
off 14.. payload
```

### 2.2 Message types built in this unit

Extracted by scanning every `call 0x274D1E0` in the span and reading the two
pushes that precede the `getSendStream` call. Layouts below are the literal
write-call sequence.

| msgType | builder VA | layout after the 14-byte header |
| ---: | --- | --- |
| **0x12** | `0x016FDFC0` `requestDeleteItems` | see §2.3 |
| **0x13** | `0x016FA5F0`, `0x016F6520`, `0x016EE820`, `0x017012D0`, `0x016FB1A4` | see §2.4 — **move / swap item** |
| 0x14 | `0x016F77F0` | `u8 kind, u8 a, u8 b` |
| 0x1A | `0x016F7B80` | `u16 x, u8 kind, u16 word[obj+0x98], u32 arg1` |
| 0x1E | `0x01700821` (inside `requestUseWasteStackable`) | `u16 v` |
| 0x29 | `0x016F62D0`, `0x016FF630` | `u16 arg0` |
| **0x2B** | `0x016FC920` `sendGetItemPacket` | see §2.5 — **pick up ground item** |
| 0x2C | `0x016F91A0`, `0x01700910` | `u16 a, u32 f(b), u32 b` |
| 0x50 | `0x016FD060`, `0x016FD7F0` | `u8, u8(0x26 at 016FD963), u16, u32 itemId, u16, u16(0xFFFF or v), …, cstring` |
| 0xA3 | `0x016ED190`, `0x016FFE30` | **empty body** — abort/cancel |
| 0xCA | `0x016F83F0` | `u16 a, u32 itemId` |
| 0xCC | `0x016FBAD0` | `u16 arg0, u32 itemId, u16 b, u32 [obj+0x2C]` |
| 0xEB | `0x016F80A0` | `u16 arg1, u16 arg0` |
| 0xF7 | `0x016F53F0` | `u8 kind, u16 slot, u32 itemId, u32 count, u16 arg` |
| 0xF8 | `0x016F5740` | `u16 slot, u32 itemId, u32 count, u8 dstKind` |
| 0xF9 | `0x016F59F0` | `u16 a, u32 b, u16 c, u32 d` |
| 0xFB | `0x016F9070` | `u32 arg0, u16 arg1` |
| 0x133 | `0x016ED150` | `u32 v` (also cached to global `0x3B772EC`) |
| 0x134 | `0x016ED190` | `u32 v` (also cached to global `0x3B772F0`) |
| 0x191 | `0x016EEEF0` | `u16 arg0, u16 word[this+0x9C]` |
| **0x19C** | `0x017012D0` | inventory → vault, see §2.4 |
| **0x19D** | `0x017012D0` | vault → inventory, see §2.4 |
| 0x1A0 | `0x016F5C10` | `u8 arg0, u16 arg1, …, u16 arg[+0x10]` |
| 0x1B6 | `0x016EF020` | `u16 arg0, u16 0, u8 arg1` |
| 0x1B7 | `0x016FDA70` | `u8 3, u16 v, u32 [obj+0xC], u16 word[obj+0x10], cstring, u8 arg1` |
| 0x1C8 | `0x016F8BF0` | `u16, u16, u16` |
| 0x1CC | `0x016F8AA0` | `u16, u32, u16, u32, u8` |
| 0x25E | `0x01700AE3` | `u32 v` |
| 0x2A0 | `0x016EF140` | `u16 arg0, …, u8 secure` |
| 0x37B | `0x016F8920` | `u32 secureId, u16 slot, u32 secureId2, u16 slot2` |
| 0x39F | `0x016EF2E0` | `write_raw(buf, 0x16)` — see note |
| 0x3B0 | `0x016FB8B0` | `u16 arg0, u16 arg1` |
| 0x3B8 | `0x016F6130` | `write_raw(buf, 0x1A)` |
| 0x455 | `0x016F7B80` | `write_raw(buf, 0x13)` |

Note on 0x39F (`0x016EF2E0`): the 22-byte buffer is at `ebp-0x1C`, but only
`[ebp-0xE]` and `[ebp-0xA]` are assigned (the two dword args). **Bytes 0..13 of
that record are uninitialised stack.** A server must not read them.

### 2.3 Packet 0x12 — `CNRDItemManager::requestDeleteItems` (`0x016FDFC0`, line 0x695)

Signature: `bool __thiscall (std::vector<{u32 slot; u32 count}>* items, int spaceKind)`
(`ret 8`; element stride 8 proven by `sar eax,3` at `0x016FE1C2`).

Preconditions, in order:
1. `spaceKind == 2` → `canRequest(0, max=6)` else `canRequest(0x65, max=1)`
   (`0x016F9360`, §4). Fail → return false.
2. `0x1705320(g_0x3A5C9E8)` true → system message **0x111AD**, return false.
3. For every element: `getItemAt(slot)` must be non-null and
   `item->vtbl[0x48]() >= count` (`0x016FE0F2`). Fail → return false.
4. `this->m_hasExplicitOwner (+0x7C)` must be 0 (`0x016FE117`).

Payload:
```
u8   spaceKindOfThisManager      ; decrypt(this+0x34) via 0x016F26F0   (016FE1A5)
u8   itemCount = vec.size()                                            (016FE1CD)
repeat itemCount:
    u16  spaceKind   (= the arg1 value, written per element)            (016FE234)
    u16  slotIndex   (low 16 bits of element[0])                        (016FE268)
    u32  itemId      (decrypt(item->getInfo()+0x18, tag 0x32), 0 if slot empty) (016FE34C)
    u32  count       (element[4])                                       (016FE3C0)
u32  0                                                                  (016FE3E1)
u8   safeModeSkipIndex   ; 0x1102A10() on singleton 0x1102C30()         (016FE400)
```
After `sendPacket`, the client calls `setCurSafeModeSkipIndex(SAFE_MODE_SKIP_NONE)`
(`0x1102A20(0)`, literal at `0x032D9430`) and pushes **0** onto the pending queue.

### 2.4 Packet 0x13 / 0x19C / 0x19D — `CNRDItemManager::requestMoveItemSpace` (`0x017012D0`)

Signature: `bool __thiscall (int srcSlot, int count, CNRDItemManager* dst, int dstSlot, bool flag)`
(`ret 0x14`).

**Type selection** (`0x0170158A..0x017015C3`):
```
if  isVaultManager(dst,&vaultIdx)  -> msgType = 0x19C   (inventory -> vault)
elif isVaultManager(this,&vaultIdx)-> msgType = 0x19D   (vault -> inventory)
else                                -> msgType = 0x13
```
`isVaultManager` = `0x016ECC20`: linear scan of `dword[0x3A5C8DC + i*4]`, i in 0..4,
returns the index through the out-param.

**Guards before any send**
* `dst->getKind()==2` and item is trade-locked (`0x016ED3C0`) → message **0xADC7**.
* `dst->getKind()==0xC` and trade-locked → message **0xADC5**.
* `this->m_pending.size() >= 1` **or** `dst->m_pending.size() >= 1` → abort
  (`0x01701412..0x0170142C`, `and eax,~3; cmp eax,4; jge fail`).
* `srcSlot < decrypt(this+0x44)` and `dstSlot < dst->getSlotCount()`.
* if the source slot holds an item, `item->vtbl[0x48]() >= count`.
* `0x1705320(g_0x3A5C9E8)` → message **0x111AD**.
* `this->m_hasExplicitOwner (+0x7C)` must be 0.
* vault path only: `0x016ED290(invSlot, 0xD)` must pass, and if the inventory
  manager `[0x3A5C9BC]` has an item there with `info->byte[0x5C] != 0` →
  message **0x2AFF**, abort.

**0x13 payload** (identical in `0x016FA5F0`, `0x016F6520`, `0x016EE820`, `0x016FB1A4`):
```
u8   srcSpaceKind        ; decrypt(this+0x34)                       (017017B8)
u16  srcSlot                                                        (017017D0)
u32  srcItemId           ; 0 if slot empty                          (0170183C)
u32  count                                                          (01701893)
u8   dstSpaceKind        ; dst->getKind()  (0x016F0590)             (017018A8)
u16  dstSlot                                                        (017018B8)
u32  dstItemId           ; 0 if slot empty                          (01701921)
u32  0                                                              (01701973)
u32  partyMemberIndex    ; 0..2 if my char is party slot i, else -1 (01701A04)
u8   0                                                              (01701A12)
u8   0                                                              (01701A20)
```

**0x19C / 0x19D payload**:
```
u32  0                                                              (01701653)
u32  inventorySlot       ; ebx — always the inventory-side index    (01701660)
u32  srcItemId           ; from this->getItemAt(srcSlot), 0 if empty(017016C8)
u32  vaultTabIndex       ; out-param of isVaultManager, else -1     (01701719)
u32  vaultSlot           ; the vault-side index                     (01701729)
```

After every branch: `sendPacket()` then **push 4** onto `this->m_pending`
(`0x01701A2A..0x01701A36`).

### 2.5 Packet 0x2B — `CNRDItemManager::sendGetItemPacket` (`0x016FC920`, line 0x88C)

Picks up a ground item. Argument is the ground drop object (`[ebp+8]`, has a
byte at `+0x1181`, a vtbl with `+0x4C = getUid`, `+0xC4 = getX`, `+0xC8 = getY`).

```
u16  uid            ; drop->vtbl[0x4C]()   -- logged "[Hell][SEND][GetItem] Uid : %d"
u8   0
u8   b              ; [drop->vtbl[0x28]() + 0x158], 0 if no info
u16  charX          ; owner->vtbl[0xC4]()
u16  charY          ; owner->vtbl[0xC8]()
u16  flagA          ; see below                                     (016FCB83)
u16  itemX          ; drop->vtbl[0xC4]()                            (016FCB9C)
u16  itemY          ; drop->vtbl[0xC8]()                            (016FCBB5)
u16  flagB          ; see below                                     (016FCBE2)
u16  flagC          ; see below (call obfuscated through .vmp1)
```

**`flagA`/`flagB`/`flagC` are obfuscated booleans.** Each starts as a 16-bit
value from `0x027C2E60` (169 callers, pure state-machine on globals `0x3B89BB8..
0x3B89BCC` — a PRNG). When the corresponding client-side check *passes*, the
value is rounded down to a multiple of 7:

```
016FCB6B  cdq ; mov ebx,7 ; idiv ebx ; sub ecx,edx      ; v -= v % 7
```

* `flagA` — passes when the my-character checks at `0x016FCAEF..0x016FCB63`
  (`0x119B940/0x119B470/0x1912DE0`, or `0x1834080` path) succeed.
* `flagB` — `0x016ECF30(drop)`: **drop-position sanity**. True iff
  `|curX - originX| <= 200` (`0xC8`) and `|curY - originY| <= 70` (`0x46`),
  where origin comes from `0x174CF10`/`0x174CF20`. This is an anti-teleport-loot
  check the client performs and encodes into the packet.
* `flagC` — `0x174D010(drop)`.

Server-side reading is `v % 7 == 0` ⇒ "client asserts the condition holds".
Note the 1-in-7 false-positive rate for a raw random value; treat it as a
cheat *signal*, not a reliable boolean.

The function also asserts on tracking mismatch: it stamps `drop+0x1181 = 1`
and `dropInfo+0x37D = 1`, and if it finds an already-flagged object it logs
`[ERROR] trackking changed! (wrong object)` with `fsIdx`, `Index`, `uid`,
`(1)/(2) &ItemObj / &Item` (literals `0x032D9728..0x032D98C0`).

### 2.6 Data files this unit owns

`CNRDItemManager::create` (`0x01700E70`) loads two `.etc` scripts into the
manager:

| field | script | file | fail literal / line |
| --- | --- | --- | --- |
| `this+0xE4` | `LetheUseOrderScript` | `Etc/chn_lethe_use_order.etc` | line 0x32F / 0x336 |
| `this+0xE8` | `StaminaPotionScript` | `Etc/staminaPotionList_chn.etc` | line 0x341 / 0x348 |

(Loader entry points: `0x2BE0FF0` for lethe, `0x2910C90` for stamina potions —
both outside this TU.)

---

## 3. Object layout — `CNRDItemManager`

All offsets proven by the cited instruction.

| off | type | evidence |
| ---: | --- | --- |
| `+0x34` | secure int — **space kind** | set in `create` from arg0 (`01700EA3`); read by `getKind` `0x016F0590` (`016F0594 add ecx,0x34`) |
| `+0x3C` | secure int | getter `0x016EE580` (`016EE584 add ecx,0x3C`) |
| `+0x44` | secure int — **slot count** | set in `create` from arg1 (`01700F13`); read by `getSlotCount` `0x016EE780`; bound-check in `getItemAt` `0x016EE6C7` |
| `+0x4C` | secure int | getter `0x016EE620` (`016EE624 add ecx,0x4C`) |
| `+0x54` | `std::vector<CNRDItem*>` proxy (IDL=2) | `create` `01700F81 lea ecx,[ebx+0x54]; call resize` |
| `+0x58` | vector `_Myfirst` | `016ED580 mov eax,[ecx+0x58]` |
| `+0x5C` | vector `_Mylast` | `016ED583`, size = `(0x5C-0x58)>>2` |
| `+0x60` | vector `_Myend` | implied by MSVC layout |
| `+0x68` | `std::vector<int> m_pendingRequests` proxy | `0x016F4F40 add ecx,0x68`; `01701A33 lea ecx,[edi+0x68]` |
| `+0x6C` | pending `_Myfirst` | `016ED893 sub eax,[ecx+0x6C]` |
| `+0x70` | pending `_Mylast` | `016ED890 mov eax,[ecx+0x70]` |
| `+0x74` | pending `_Myend` | `016FE544 mov ecx,[esi+0x74]` (growth path) |
| `+0x7C` | `bool m_hasExplicitOwner` | `016ED4B0 cmp byte [ecx+0x7C],0` |
| `+0x80` | `CActor* m_owner` | `016ED4B6 mov eax,[ecx+0x80]`; setter `0x016ED240` |
| `+0x84` | `std::map` | `0x016FF040 add ecx,0x84` → erase; `0x016FDC80 add ecx,0x84` → find |
| `+0x90` | ptr/int, tested non-zero | `016ED9C2 cmp eax,[ecx+0x90]` |
| `+0x9C` | u16 | `016EEFB? movzx ecx, word [esi+0x9C]` (packet 0x191) |
| `+0xB4` | bool | getter `0x016ED260` |
| `+0xCC` | bool | get `0x016ED3B0`, set `0x016ED3A0`; gates the read path in `0x016F26F0` (`016F2720`) |
| `+0xD4` | heap buffer | freed at `016FF1F2` |
| `+0xE4` | `LetheUseOrderScript*` | `017010EB mov [ebx+0xE4],eax` |
| `+0xE8` | `StaminaPotionScript*` | `017011C5 mov [ebx+0xE8],eax` |
| `+0xEC/0xF0/0xF4/0xF8` | a second vector | freed at `016FF1CE..016FF1E7` |

`create(int kind, int slotCount)` fills every slot with NULL, then **if
`kind == 0 || kind == 4`** it constructs three fixed objects:

* `slot[0]` = `new` 0x514-byte object, ctor `0x16DC2C0(0)`   (`01700FBB..01701001`)
* `slot[1]` = `0x1A85220(1, x, 1, 0)`                          (`01701031`)
* `slot[2]` = `0x1A85220(2, x, 1, 0)`                          (`0170107C`)

matching the three hard-coded accessors `0x016ED580` (slot 0), `0x016ED5C0`
(slot 1), `0x016ED5A0` (slot 2).

### Item / item-info offsets used from here

`item->vtbl`: `+0x08` bool, `+0x28` `getInfo()`, `+0x2C` `bool filter(int)`,
`+0x48` `getCount()`, `+0x4C` `getUid()`, `+0xC4` `getX()`, `+0xC8` `getY()`,
`+0xD0` byte kind, `+0x13C` secure-value accessor.

`info = item->vtbl[0x28]()`:

| off | meaning | evidence |
| ---: | --- | --- |
| `+0x14` | item-id struct; `0x444CD0` converts to the u32 written into packets | `01701688`, `016FBD14`, `016FD637` |
| `+0x18` | **secure u32 item id**, decrypted with `0x2C4D110(ptr, 4, 0x32, 1)` | `016EF4D5`, `016FE2DE`, `016FFF27` |
| `+0x54` | int, `== 5` is special-cased | `01701353` |
| `+0x5C` | byte; non-zero blocks the vault move (msg 0x2AFF) | `01701629` |
| `+0x158` | u8 written into packet 0x2B and the UX log | `016FC991`, `016FC34F` |

---

## 4. The in-flight-request state machine (mirror this on the server)

`this+0x68` is a `std::vector<int>` of **request kind markers**. The client
pushes a marker when it sends a request and will not send another until the
marker is removed by the matching S→C handler.

| VA | ext callers | role |
| --- | ---: | --- |
| `0x016F4F40` | 128 | `pushPending(int kind)` — `push_back` |
| `0x016FB9D0` | 106 | `removePending(int kind)` — pops if it is the only/last one, otherwise rebuilds the vector without it |
| `0x016ED890` | 74 | `pendingCount()` |
| `0x016F3360` | 39 | `popPending()` — `pop_back` |
| `0x016F32D0` | 17 | `hasPending(int kind)` |
| `0x016F9300` | 30 | `lastPendingKind()` — returns `0x65` when empty |
| `0x016F34D0` | 20 | pop + slot-count-dependent follow-up |
| `0x016F9360` | 5 | `bool canRequest(int kind, int maxPending)` |

`canRequest` (`0x016F9360`, line 0x21AD):
```
if (pendingCount() < maxPending) return true;
log("type %d | last type %d | count %d", kind, lastPendingKind(), pendingCount());
return false;                       // and pops up a message box (0x285C720/0x285B800)
```

**15 of the 106 `removePending` callers are inbound-stream parsers**
(`0x995AA0`, `0xC7E290`, `0x11040A0`, `0x1119280`, `0x111F3F0`, `0x1120970`,
`0x113C5A0`, `0x1185710`, `0x125A7E0`, `0x1275B40`, `0x127ADF0`, `0x12BAF50`,
`0x14F7910`, `0x14FBD70`, `0x15065C0`). That is the proof that the marker is
cleared on the server's reply. **If the server never answers, the client
silently refuses the next request of that class** — a common source of
"the client stopped responding to clicks".

Markers observed at call sites (image-wide scan of the immediate pushed before
the call). Most frequent: `pushPending 4` (41 sites — item move), `0x11` (15),
`0` (13 — delete items), `0xF` (10), `0x3C` (9), `0x5A` (9); `removePending
0x5C` (34) and `hasPending 0x5C` (34) dominate the read side. Concrete
confirmations from this TU: **move → 4** (`01701A2E`), **delete items → 0**
(`016FE530`).

Also gated:
* `requestDeleteItems` allows up to 6 outstanding when `spaceKind == 2`, else 1.
* `requestMoveItemSpace` allows **0** outstanding on both source and destination
  managers.

Two more hard gates the server should be aware of:
* **`this+0x7C != 0` suppresses every send.** A manager bound to another actor
  (`getActor()` = `0x016ED4B0`: returns `this+0x80` if `+0x7C`, else global
  `0x3B3DFB8`) never talks to the server. Present at `016EEF9E`, `016EF0CE`,
  `016F5B5E`, `016F5E2E`, `016F82D2`, `016F8B4E`, `016F8C9E`, `016F9132`,
  `016FB95A`, `016FD947`, `016FDB74`, `016FE117`, `0170157F`, …
* The **safe-mode skip index** (`0x1102C30` singleton, `0x1102A10` get /
  `0x1102A20` set) rides in packet 0x12 and is reset to NONE right after.

---

## 5. Public surface (called from outside the unit)

1485 distinct external callers. The genuinely-ItemManager entries:

| VA | ext callers | signature / role |
| --- | ---: | --- |
| `0x016EE6C0` | 443 | `CNRDItem* getItemAt(int idx)` — bounds-checked against `decrypt(this+0x44)` **and** the vector size; returns NULL out of range. `ret 4` |
| `0x016EF580` | 241 | `int findSlotByItemId(u32 id)` — scans slots, decrypts `info+0x18` (tag 0x32), returns slot index or -1 |
| `0x016EF980` | 130 | `int findSlotByPointer(CNRDItem*)` — returns index or -1 |
| `0x016F4F40` | 128 | `pushPending(int)` |
| `0x016FB9D0` | 106 | `removePending(int)` |
| `0x016ED580` | 94 | `slot0()` (asserts size > 0) |
| `0x016EF410` | 86 | `int countByItemId(u32 id)` — counts slots whose `info+0x18` matches and `vtbl[0x2C](1)` is false |
| `0x016ED890` | 74 | `pendingCount()` |
| `0x016FF040` | 65 | `map(+0x84).erase(key)` |
| `0x016FDC80` | 52 | `map(+0x84).find(key)` |
| `0x016F3360` | 39 | `popPending()` |
| `0x016EE780` | 29 | `int getSlotCount()` = `decrypt(this+0x44)` |
| `0x016F9300` | 26 | `lastPendingKind()` |
| `0x016F34D0` | 20 | pop-pending + slot-count follow-up |
| `0x016F32D0` | 17 | `hasPending(int)` |
| `0x016FF180` | 16 | destructor / teardown (frees `+0xEC` vector and `+0xD4`) |
| `0x016ED240` | 15 | `setOwner(CActor*)` → `this+0x80` |
| `0x016F0FD0` | 14 | message-box helper (msg id `0x98`); arg `-1` means "slot count" |
| `0x016FA5F0` | 14 | 4800-byte drag-and-drop handler; emits 0x13 and one variable-type packet |
| `0x016F0590` | 12 | `int getKind()` = `decrypt(this+0x34)` |
| `0x016ED5C0` / `0x016ED5A0` | 10 / 9 | `slot1()` / `slot2()` |
| `0x016ED4B0` | 6 | `CActor* getActor()` |
| `0x016ED3B0` / `0x016ED3A0` | 6 / 3 | get/set `this+0xCC` |
| `0x017012D0` | 9 | `requestMoveItemSpace` |
| `0x016FD060` | 7 | packet 0x50 builder (item link into chat, with trailing string) |
| `0x016FE6A0` | 7 | ground-item pickup wrapper, gated on `pendingCount() < 4` |
| `0x016FDFC0` | 4 | `requestDeleteItems` |
| `0x016F5740` | 4 | packet 0xF8 builder |
| `0x016ECB40` | 2 | `setIsStackableMoving(bool)` — increments/decrements the global depth counter `0x3A8F1D0`; underflow logs `Stackable Moving Error : missmatch` (line 0x2F0) |

Named-by-literal functions (the `__FILE__` anchors):
`0x016ECB40 setIsStackableMoving` (line 0x2F0), `0x016F9360` (line 0x21AD, the
throttle log), `0x016FC920 sendGetItemPacket` (line 0x88C), `0x016FDFC0
requestDeleteItems` (line 0x695), `0x016FFE30 requestUseWasteStackable`,
`0x01700E70 create` (lines 0x32F/0x336/0x341/0x348), `0x017012D0
requestMoveItemSpace`.

---

## 6. Telemetry (not protocol) — do not confuse with tables

Two functions in the span format tab-separated **UX behaviour-log** lines, not
`.etc` table rows:

* `0x016FC180` → `UX_breakup` + `"%d\t%d\t…"` (9 numbers). A switch on arg1
  (1..0x12) selects the category string from `none`, `disjoint`, `portable`,
  `exDisjoint`, `exEnchanter`, `exAlchemist`, `exDoolctrl` (jump table at
  `0x016FC48C`, index map at `0x016FC4A8`). Fields pushed: itemId
  (`info+0x14`→`0x444CD0`), `info+0x158`, five booleans from the five byte args,
  a value from `0x2350570`, and `partyMember(0)+0x360`. Emitted only if UX log
  channel 9 is enabled (`0x14BF180/0x14BF090/0x14BF500`).
* `0x016FC4C0` → `UX_move_item` + `"%d\t%d\t%d\t%d"`, categories
  `exJobMaterial`, `material`, `waste`.

`ALERT_1` (`0x03146978`, used at `0x016ECC50` and `0x016F1850`) is a shared
sound/effect name, not specific to this unit.

---

## 7. Not this class — template COMDATs that landed in the span

These have large external caller counts and **must not** be described as
ItemManager methods:

* `0x016F6420` (408 ext callers, `ret 0x1C`) — `std::_Tree::erase(first, last)`;
  the debug-iterator proxy validation (`[eax+4] <= p < [eax+8]`) and the tail
  call into `0x016F2D30` give it away.
* `0x016F2D30` (35 ext) — `std::_Tree::_Erase(node)`; manipulates
  `node[0]=_Parent, node[4]=_Left, node[8]=_Right, node[0x11]=_Isnil`.
* `0x016F2FB0`, `0x016F1300`, `0x016F1FD0`, `0x016F00A0` — same family
  (rebalance / insert / iterator helpers).
* `0x016FDEF0` (21 ext) — the range-erase adaptor that calls `0x016F6420`.

The decrypt-dispatch idiom seen everywhere in this unit
(`and eax,0xF; cmp eax,0xC; ja …; jmp [table]` → one of
`0x27BC0F0 / 0x27BC160 / 0x27BC1D0 / 0x27BC240 / 0x27BC390`) is the client's
**secure-integer read, selected by the storage address's 16-byte alignment**.
The matching writes are `0x27BBBE0 / 0x27BBCA0 / 0x27BBD50 / 0x27BBE10 /
0x27BBA60`. `0x2C4D110(ptr, len, tag, 1)` decrypts a raw secure field in place
(tag `0x32` for item ids, `0x2F` for slot indices).

---

## 8. Unknowns

* The meaning of the secure ints at `+0x3C` and `+0x4C`, and of `+0x90`, `+0xB4`.
* The key/value types of the `std::map` at `+0x84`.
* The exact field meaning of most single-purpose packets in the §2.2 table
  (0xCA, 0xCC, 0xEB, 0x1C8, 0x1CC, 0x37B, 0x3B0, 0x455, 0x3B8, 0x39F) — the
  wire layout is recovered, the semantics are not.
* Whether the `flag` byte at header offset 11 is ever non-zero elsewhere in the
  image; every call site in this TU passes 0.
