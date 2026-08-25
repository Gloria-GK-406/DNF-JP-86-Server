# TU 042 — `DNFClient\RDAR\CashShop\CouponSystemManager.cpp`

Briefing span: `0x00B186B0` – `0x00B18790`, 2 functions, 0 parsers.
**The span is far too narrow.** Real module extent (argued below): `0x00B18050` – `0x00B18E4E`,
13 functions, plus one shared COMDAT setter at `0x00C60CC0`.

---

## 1. What this unit implements

A **cash-shop discount-coupon (割引券) engine**. It is a pure *client-side pricing/UI* layer
that sits on top of the cash-shop buy list. It does three things:

1. Holds a pointer to the currently-open buy list (the cash-shop cart) and remembers, per cart
   line, which coupon item the user attached to it.
2. Queries a global **coupon table** (`0x04E2203C`) that maps
   `couponIndex -> [(originalItemId, discountedItemId), ...]` and
   `itemId -> [couponIndex, ...]`.
3. Computes the displayed discount as
   `price(originalItem) - price(discountedItemVariant)`.

**There is no packet parse and no packet write inside this unit** (`parsers = 0`, no
`0x274Dxxx` write primitives). Its output is consumed by
`CashShopBuyWindow::sendBuyPacket` (`0x025B99F0`), which is where the coupon reaches the wire.
That is the server-relevant part and it is documented in §4.

Key mechanism, and the single most important fact for a server implementer:

> **A coupon is expressed on the wire as an item-id substitution.** The client does not send
> "coupon X applied, discount Y". It sends the *discounted variant item id* in the normal item-id
> field, plus the coupon item's UID and inventory slot as separate fields. The mapping
> `(couponIndex, originalItemId) -> discountedItemId` lives in a client data table; the server must
> hold the same table and re-derive/validate it, otherwise a client can substitute an arbitrary
> cheaper item id.

---

## 2. Evidence that the briefing span is wrong

* Both anchored functions carry the **same** function-name literal and the **same** line numbers:

  | VA | literal @ | text | line pushed |
  |---|---|---|---|
  | `0x00B186B0` / `0x00B18790` | `0x031634C0` | `D:\Work\1229\Source\DNFClient\RDAR\CashShop\CouponSystemManager.cpp` | — |
  | `0x00B186B0` / `0x00B18790` | `0x03163550` | `CouponSystemManager::getDiscountCash` | — |
  | `0x00B186B0` / `0x00B18790` | `0x031635A4` | `Coupon Index Get Error` | `0xBB` = **187** |
  | `0x00B186B0` / `0x00B18790` | `0x03163478` | `Coupon discounted item error` | `0xC1` = **193** |

  Identical name + identical line numbers in two distinct functions means `0x00B18790`
  **inlined** `0x00B186B0`. Only these two log, so only these two carry `__FILE__`; every other
  method of the class is silent and therefore invisible to the anchor heuristic.

* The neighbouring functions share the class `this` layout (`[this+0]` = array base,
  `[this+4]` = count, element stride `0xD8`) and the same singleton `0x039AC2C4`. They are the
  same class. Concretely: `0x00B18050`, `0x00B18080`, `0x00B180B0`, `0x00B180E0`, `0x00B18110`,
  `0x00B18150`, `0x00B18350`, `0x00B184A0`, `0x00B18510`, `0x00B186B0`, `0x00B18790`,
  `0x00B18890`, `0x00B18A80`.
* Lower bound: `0x00B17E90`/`0x00B17FD0` reference `Etc/AssaultParameter.etc` and singleton
  `0x039AC2C0` — a different module. So the class starts at `0x00B18050`.
* Upper bound: `0x00B18A80` really ends at `0x00B18E4E` (`ret 8`); `0x00B18E60` is a new prologue
  using `0x019FFD20`/`0x01A00160` (avatar code). `functions.csv` merged the two into one
  1520-byte row.
* One member lives outside the module entirely: `0x00C60CC0` is a shared 2-word setter COMDAT
  (`this[0]=a; this[1]=b; ret 8`) with **12** callers across unrelated classes. It is the
  `setBuyItemList` used on this singleton — see §5.

---

## 3. Object layout

### 3.1 `CouponSystemManager` — 8 bytes, static singleton at `0x039AC2C4`

| off | type | meaning | proof |
|---|---|---|---|
| `+0x00` | `CashItemRecord*` | base of the cash-shop buy list (cart) array | `0x00B186BF mov eax,[edi]` then `[esi+eax+0xCC]` with `esi = idx*0xD8` |
| `+0x04` | `int` | number of cart lines | `0x00B186BA cmp esi,[edi+4]` bounds-check; `0x00B18080` loop bound |

Init guard byte at `0x039AC2CC`. `getInstance` = `0x00B18110`:

```
00B18110 mov  eax, 1
00B18115 test byte [0x39AC2CC], al      ; once-flag
00B1811D or   dword [0x39AC2CC], eax
00B1812A mov  dword [0x39AC2C4], 0      ; m_list  = NULL
00B1812F mov  dword [0x39AC2C8], 0      ; m_count = 0
00B18134 call 0x2C621BD                 ; atexit(dtor @0x2FF9DE0)
00B1813C mov  eax, 0x39AC2C4            ; <- returns &singleton
```

`getInstance` does **not clean the stack** (`ret`, no `ret N`). The client exploits this all over:
`push arg; call getInstance; mov ecx,eax; call method` — the pushed arg is consumed by the
method's own `ret N`. Same trick with `0x0274CA50` (packet), `0x02300D10`, `0x02B1D280`.

### 3.2 Cash-shop item record — **0xD8 (216) bytes**

The cart entries and the cash-shop item-table entries are the **same struct**. Stride proof from
three independent sites: `imul esi,esi,0xD8` (`0x00B186C1`), `add esi,0xD8` (`0x00B18830`), and
the reciprocal-multiply `0x4BDA12F7 / sar 6` = divide-by-216 in the table scanner `0x00B1C960`.

| off | width | meaning | proof |
|---|---|---|---|
| `+0x08` | `u32` | **item id** (cash-shop goods id) | `0x00B186F1 mov edx,[esi+ecx+8]` fed to the coupon lookup as key; `0x00B1C9AE cmp [edx],itemId` where `edx = elem+8` is the table search key; written to the buy packet at `0x025B9D97` |
| `+0x14` | `u32` | opaque handle passed to `0x00B1B850(x,-1)` and `0x00B1C160` | `0x025B9D37`, `0x025B9DB7` |
| `+0x1C` | `i32`/`u8` | quantity-ish; clamped to 0 when `<0` and `0x00B1B850(...)==0xA`; sent as **u8** | `0x025B9D32`–`0x025B9D5B` |
| `+0x26` | `i16` | duration kind: `1 -> 7`, `2 -> 30`, `3 -> 0` days | `0x025C114D movsx ecx,word[edi+0x26]` then `dec/dec/dec` switch to `7 / 0x1E / 0` |
| `+0x38` | `i32` | **price** | `0x00B18779 sub eax,[ecx+0x38]` is returned as a *cash discount*; `0x00B182AE/0x00B182B2` picks the entry with the **largest** `+0x38`; `0x023A2B78`/`0x023A2C06` compare `[rec+0x38]` against the player's obfuscated currency balances (`0x03B3E198`, `0x03B3E1B8`) |
| `+0x68` | `i32` | sentinel `-2` selects the multi-line buy path | `0x029B4C60 cmp [ecx+0x68],-2 / setne al` |
| `+0xAC` | `u8` | per-line flag; a line with this set **and** `0x0143F770(gInv,line)==1` is excluded from the sent count | `0x025B9BFE cmp byte[esi],al`, `0x025B9CC3 cmp byte[esi+0x90],0` |
| `+0xCC` | `u32` | **attached coupon item's UID** (0 = no coupon). Client scratch field inside the record. | written by `applyCoupon` `0x00B184F2`; cleared by `0x00B18067`/`0x00B18092`; tested everywhere |
| `+0xD0` | ptr | detail/sub-object; `0x00CEE9B0` does a guarded read of it, and the real price table hangs off it (`0x01685180`, rows of 0x20 bytes at `sub+0x125C`: `[+0]=days, [+4]/[+8]/[+0xC] = three price columns`) | `0x00CEE9B0`, `0x01685180` |

Uncertain: I could not prove which currency `+0x38` is (it is compared against both
`0x03B3E198` and `0x03B3E1B8`, which look like cera-point and cera). It is a single scalar, so a
coupon discounts exactly **one** price column, while the displayed price comes from the 3-column
table at `sub+0x125C`. Flagging the dissonance rather than guessing.

### 3.3 Coupon table — global singleton at `0x04E2203C`

Lazy getter `0x02B1D280` (`ret`, no cleanup; ctors three sub-maps at `+0x00`, `+0x14`, `+0x28`).

| member | key | value | accessor |
|---|---|---|---|
| `+0x00` | `couponIndex` | `vector<pair<u32 originalItemId, u32 discountedItemId>>` (8-byte elements — `add esi,8` at `0x00B185EF`, `0x00B189D6`) | `0x02B1CA00(key, &out)` `ret 8` |
| `+0x14` | `itemId` | `vector<u32 couponIndex>` (4-byte elements — `add esi,4` at `0x00B1828D`, `0x00B18BE1`) | `0x02B1C940(0, key, &out)` `ret 0xC` |
| `+0x28` | ? | same shape, selector `1` | `0x02B1C940(1, key, &out)` — not reached from this unit |

The **coupon index of an item template** is `itemInfo[+0xA80]`, obtained via the item-info
provider `[0x03A5C914]` → `0x02752A80(itemUID, 1)`:

```
00B180E0  ; getCouponIndexByUID(u32 itemUID) -> int
00B180E6  mov  ecx, [0x3A5C914]
00B180EC  push 1 ; push itemUID
00B180EF  call 0x2752A80          ; itemInfo = ItemInfoProvider::get(uid, 1)
00B180F8  mov  eax, [eax+0xA80]   ; <- coupon index field of the item template
00B18102  or   eax, -1            ; -1 if no such item
```

So **item-template field `+0xA80` = coupon index**. A server needs the same per-item field to
know that an inventory item is a coupon and which coupon table row it keys.

---

## 4. Packet impact (via `CashShopBuyWindow::sendBuyPacket` @ `0x025B99F0`)

Write primitives (all use the "getter that doesn't clean the stack" idiom;
`0x0274CA50` returns the send-packet object at `[0x03B7CEE4]`):

| VA | op |
|---|---|
| `0x0274D1E0` | `begin(u16 cmd, u8 flag)` |
| `0x0274D2E0` | `write_u8` (`+1` to `[ecx+0x2BCC2C]`) |
| `0x0274D310` | `write_u16` (`+2`) |
| `0x0274D340` | `write_u32` (`+4`) |
| `0x0274D4A0` | `write_string` |
| `0x0274E6D0` | `send` |

`sendBuyPacket` has two branches, selected by `cart[0].+0x68 != -2` (`0x029B4C60`):

* **cmd `0x00FC`, flag 0** — single-line path (`0x025B9A3C`).
* **cmd `0x0040`, flag `byte[win+0x2834]`** — multi-line path (`0x025B9B70`). This is the branch
  that logs `" ENUM_CMDPACKET_BUY_CERASHOP_ITEM - totalCount[%d]"`
  (literal `0x035D7758`, file `0x035D7670`, line `0x13AB` = 5035), so **`0x0040` is
  `ENUM_CMDPACKET_BUY_CERASHOP_ITEM`**.

### `0x0040` body, in write order

```
u8   byte[win+0x282C]
u8   byte[win+0x2828]                       ; mode; ==1 -> "gift" style, also gates the string
str  [win+0x2830]                           ; only if [win+0x2828]==1
u8   totalCount                             ; [win+0x217C] - excludedCount + flaggedCount
u8   CouponSystemManager::getInstance()->hasAnyCoupon()   ; <-- THIS UNIT, 0x00B180B0
repeat per surviving cart line:
  u8   isSpecialKind                        ; 1 if [win+0x20D8 + i*4] in [6..0xB] or ==0xE
  u8   line.+0x1C                           ; quantity (clamped)
  u8   kindCode                             ; 1/2/3 mapped from the above, else word[win+0x20D8+i*4]
  u32  itemId          <-- COUPON SUBSTITUTION, see below
  <two nested lists>                        ; if 0x00B1EAD0(line): 0x00B1C160 -> 0x01A11280
                                            ;   list A: u8 count, then per entry ... (0x01A11170)
                                            ;   list B: u8 count, then {u32,u16,u16} per entry (0x01A111F0)
                                            ; else: u8 0, u8 0   (both lists empty)
  u32  couponItemUID   = line.+0xCC, or 0
  u16  couponSlot      = InventoryMgr[0x03A5C9BC]->findSlotByUID(line.+0xCC), or 0   (0x016EF580)
send()
```

The substitution, verbatim (`0x025B9D6D`–`0x025B9DA2`):

```
025B9D6D  mov  eax, [esi+0xB0]        ; line.+0xCC  = coupon item UID
025B9D75  je   0x25B9D97              ; no coupon -> plain item id
025B9D78  call 0x00B18110             ; CouponSystemManager::getInstance()
025B9D7F  call 0x00B180E0             ;   couponIndex = getCouponIndexByUID(uid)
025B9D84  mov  edx, [esi-0x14]        ; line.+0x08 = original item id
025B9D89  call 0x00B18110
025B9D90  call 0x00B18510             ;   itemId = getDiscountedItemId(couponIndex, itemId)
025B9D97  mov  eax, [esi-0x14]        ; (no-coupon path)
025B9D9A  push eax
025B9DA2  call 0x0274D340             ; write_u32(itemId)
```

**Server checklist**

1. Load the same `couponIndex -> [(origItemId, discountedItemId)]` table.
2. For each cart line with `couponItemUID != 0`: verify the UID is a real inventory item owned by
   the buyer, at the claimed slot; read that item's coupon-index field; verify the received
   `itemId` equals `discountedItemId` for that `(couponIndex, expectedOriginalItemId)` pair.
   The original item id is **not sent** — the server must recover it from the reverse map or
   from the client's UI state, which means it must validate the pair, not trust the id.
3. Consume the coupon item.
4. The `hasAnyCoupon` u8 is redundant with the per-line UIDs; do not trust it, but note that a
   client sending `0` here with per-line coupons set is malformed.
5. `MAX` cart lines is **33 (0x21)** — see §6.

---

## 5. Public surface (all 13 methods)

`this` = the singleton unless noted. All are thiscall.

| VA | signature (recovered) | behaviour | external callers |
|---|---|---|---|
| `0x00B18050` | `bool clearCoupon(int index)` | bounds-checks `index` against `[this+4]`, sets `line[index].+0xCC = 0` | `0x0125BC10` |
| `0x00B18080` | `void clearAllCoupons()` | zeroes `+0xCC` on every line | `0x0125B0A0`, `0x023A2AA0`, `0x025B9450` (buy-window reset) |
| `0x00B180B0` | `bool hasAnyCoupon()` | true if any line has `+0xCC != 0` | `0x025B99F0` → **packet u8** |
| `0x00B180E0` | `int getCouponIndexByUID(u32 itemUID)` | `itemInfo[+0xA80]`, or `-1` | `0x025B99F0` |
| `0x00B18110` | `static CouponSystemManager* getInstance()` | lazy static, returns `0x039AC2C4` | 13 sites |
| `0x00B18150` | `int findBestTargetLine(int couponIndex)` | over lines without a coupon, keeps those whose item is in `couponIndex`'s applicable set, returns the index of the one with the **largest `+0x38` (price)**; `-1` if none. **This is the auto-apply rule: coupon goes to the most expensive eligible line.** | internal only (`0x00B184A0`) |
| `0x00B18350` | `int countApplicableLines()` | `0` if `m_list==NULL`, `m_count==0`, or **`m_count > 0x21`**; else counts lines whose item id has any coupon | `0x0125C5F0` |
| `0x00B184A0` | `int applyCoupon(CashItem* couponItem)` | `couponIndex = couponItem->vf[0x148]()->[0xA80]`; `idx = findBestTargetLine(couponIndex)`; if `idx<0` return `-1`; reads the coupon's UID through a guarded read (`0x02C4D110(p,4,0x32,1)` then `[p]`, `p = couponItem->vf[0x28]() + 0x18`) and stores it into `line[idx].+0xCC`; returns `idx` | `0x0125BC10`, `0x023A2AA0` |
| `0x00B18510` | `u32 getDiscountedItemId(int couponIndex, u32 itemId)` | table row `couponIndex`, linear scan of `(orig,disc)` pairs for `orig == itemId`, returns `disc`; `0` if absent | `0x00B186B0`, `0x00B18790`, `0x023A2AA0`, **`0x025B99F0` (packet)** |
| `0x00B186B0` | `int getDiscountCash(int index)` | see §6 | `0x025C10C0` (×5) |
| `0x00B18790` | `int getTotalDiscountCash()` | `getDiscountCash` inlined over all lines, summed | `0x0125BC10`, `0x0125C5F0`, `0x025C2530` (cs_shop.img UI) |
| `0x00B18890` | `static bool getCouponTargetItemIds(int couponIndex, vector<u32>& out)` | pushes every `pair.first` of the row into `out`; used to highlight eligible goods | `0x012AB6B0`, `0x015DCBB0`, `0x023A2AA0` |
| `0x00B18A80` | `void collectApplicableCouponIndices(vector<int>& out, bool skipLinesWithCoupon)` | dedups through a `std::set` (`0x008DA400` ctor, `0x00456BE0` insert) then copies out | `0x0125B3B0`, `0x0125C5F0`, `0x025BBB50` |
| `0x00C60CC0` | `void setBuyItemList(CashItemRecord* base, int count)` | shared COMDAT `this[0]=a; this[1]=b`. Bound to this class at `0x025BBB9B` (`push [win+0x217C]; lea ecx,[win+0x4FC]; getInstance; call`) and `0x0125D5A5` (`push [x+0x1CC]; push [x+0x1C4]`) | 12 (shared) |

---

## 6. The formula

`CouponSystemManager::getDiscountCash(int index)` — `0x00B186B0`, `ret 4`:

```c
int getDiscountCash(int index) {
    if (index >= m_count) return 0;
    Line* L = &m_list[index];                       // stride 0xD8
    u32 uid = L->couponUID;                         // +0xCC
    if (uid == 0) return 0;

    int couponIdx = getCouponIndexByUID(uid);       // itemInfo[+0xA80], else -1
    u32 discId = getDiscountedItemId(couponIdx, L->itemId);   // 0x00B18510, L->+0x08
    if (discId == 0) {
        LOG(0, __FILE__, "CouponSystemManager::getDiscountCash", 187, "Coupon Index Get Error");
        return 0;
    }
    Rec* r = CashItemTable[0x3A5C9B8]->[0x8FAC]->find(-1, discId);   // 0x00B1D3B0
    if (r == NULL) {
        LOG(0, __FILE__, "CouponSystemManager::getDiscountCash", 193, "Coupon discounted item error");
        return 0;
    }
    return L->price - r->price;                     // [L+0x38] - [r+0x38]
}
```

`getTotalDiscountCash()` — `0x00B18790`, `ret 0` — is the same body inlined in a
`for (i=0; i<m_count; i++)` loop with the results accumulated in `[ebp-4]`. Note the
**failure semantics: any failing line contributes 0 and the loop continues** (`xor ecx,ecx`
at `0x025B9C27`… i.e. `0x00B18827`), so a broken table row silently under-discounts rather
than aborting the purchase.

Consumption (`0x025C10C0`, the buy-window total recalculation):
`finalPrice = getPrice(kind, days, ...) - getDiscountCash(i)` — e.g. `0x025C1231 sub eax,[ebp-0x20]`,
`0x025C15EE sub ebx,eax`.

The item table it resolves against, `0x00B1D3B0`:

```
00B1D3B3  cmp  [ecx+0x0C], 4          ; service-type selector
00B1D3C1  lea  eax, [ecx+0xB68]       ;   ==4 -> category array A
00B1D3D9  lea  edx, [ecx+0x94C]       ;   else -> category array B
          call 0x00B1C960(base, category, itemId)
```
`0x00B1C960`: `itemId == -1` → NULL; `category < 0` → scan all **27 (0x1B)** categories, each a
0x14-byte vector header, elements 0xD8, key at `element+0x08`. Called from this unit with
`category = -1` (scan everything).

### Constants worth mirroring server-side
* Cash-shop item / cart record size: **0xD8 = 216 bytes**.
* Cart capacity: **33 (0x21)** — `0x025B94F5 push 0x1BD8` (`memset` of the array at `win+0x4FC`;
  `0x1BD8 / 0xD8 = 33`), the `mov ebx,0x21` construction loop at `0x025B950B`, and the
  `cmp eax,0x21 / jg -> return 0` guard in `countApplicableLines` at `0x00B1838F`.
* Item-template coupon-index field: **`+0xA80`**.
* Item-table categories: **27**.

---

## 7. Globals touched

| VA | role | proof |
|---|---|---|
| `0x039AC2C4` | the `CouponSystemManager` singleton (8 bytes) | `0x00B1813C` |
| `0x039AC2CC` | its once-guard | `0x00B18115` |
| `0x04E2203C` | coupon table singleton (3 maps at `+0`, `+0x14`, `+0x28`) | `0x02B1D2FA` |
| `0x03A5C914` | item-info provider (`get(uid, flag)` = `0x02752A80`) | `0x00B186D2`, `0x00B180E6` |
| `0x03A5C9B8` | UI/root object; `+0x8FAC` = cash-shop item table | `0x00B18743` + `0x02300D10` |
| `0x03A5C9BC` | inventory manager (`findSlotByUID` = `0x016EF580`) | `0x025B9E01` |
| `0x03B7CEE4` | outbound packet object | `0x0274CA50` |
| `0x03B3E198` / `0x03B3E1B8` | obfuscated currency balances compared against `rec+0x38` | `0x023A2B78`, `0x023A2C06` |

---

## 8. Unknowns / not claimed

* Which currency `record+0x38` denominates (cera vs cera-point) — compared against both.
* Meaning of `record+0x1C` beyond "sent as u8, looks like quantity".
* Meaning of `record+0x68` beyond "`-2` selects the multi-line packet path".
* What `cmd 0x00FC` is. It is the single-line sibling of `0x0040`; only `0x0040` is named by a
  literal.
* The third coupon map at table `+0x28` (selector `1` of `0x02B1C940`) is never reached from this
  unit.
* Where the coupon table is populated from (no `.lst`/`.cfg` literal reachable from these
  functions; the loader is elsewhere and was not traced).
