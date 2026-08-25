# TU 43 — `DNFClient\RDAR\CashShop\Localization\CNRDCashShop_CHN.cpp`

Briefing span `0x00B23320`–`0x00B35950`, 73 functions, 0 parsers.
Image base `0x00400000`, dump `DNF-memory-rva.bin`.

## What it is

The **China-localized half of `CNRDCashShop`** — the in-game cash shop (商城) window
class. `parsers = 0` is correct: this unit never touches the inbound stream itself.
It is the *consumer* of two cash-shop NOTI packets that are decoded in
`GameCoreNetwork_NOTI_Etc.cpp`, plus the client-side purchase gates, the goods
visibility filter, the goods-cell renderer and the six sub-panel refresh methods
that `CNRDCashShop::setState` drives.

Source line numbers leak from the log macros: `addDisableRestrictedData` is at
line `0x3DD5` = **15829**, `openRestrictedPopupWindow` at `0x3E29`/`0x3E2E` =
**15913/15918**. It is a ~16k-line file.

Localization is proved by the assets it names: `Interface/windowcommon_cn.img`,
`/etc/Chn_ChangeCharacName.etc`, and (from the sibling initializer `0xB48010`)
`Etc/chn_CeraShopHideList.etc`, `Etc/MileageShop_chn.etc`, `Etc/CeraShop.etc`.

---

## 1. Server-relevant

### 1.1 `ENUM_NOTIPACKET_BUY_RESTRICT_ITEM_LIST` (NOTI **0x344**)

Handler `0x01161800` (in `GameCoreNetwork_NOTI_Etc.cpp`), registered at
`0x01188EB6`: `push 0; push 0x1161800; push 0x344; call 0x163C4A0`.
Read primitive used: `0x027C5310` = `read_u32` (cursor `[0x3BAAE38]`,
remaining `[0x3BAAE3C]`).

Wire format (all little-endian):

```
u32 count                                        ; logged " Restrict Total Count [%d]"
count x {
    u32 restrictItemId
    u32 restrictedEndTime      ; unix seconds, or the sentinel below
    u32 flag
}
```

Exact client effect, in order (`cs` = `InterfaceManager[0x3A5C9B8]->getCashShop()`,
`getCashShop` = `0x02300D10` → `[this+0x8FAC]`):

| step | VA | call | effect |
|---|---|---|---|
| 1 | `0x1161817` | `0x00B2FBF0` | `clear()` on the vector at `cs+0x21300` (thunk: `add ecx,0x21300; jmp 0x2B6E930`) |
| 2 | `0x116181E` | `0x00B23270` | for every node of the map at `cs+0x212C0`: `node[0x1A] = node[0x19]` (restore default flag) |
| 3 | `0x116182C` | read_u32 | `count` |
| 4 | `0x116187D` | `0x00B294A0` | `clear()` on the vector at `cs+0x2140C` (thunk: `add ecx,0x2140C; jmp 0x1BC80E0`) |
| 5 | loop | | see below |

Per entry (`0x11618EB`–`0x1161941`):

```c
if (flag == 0) {
    if (endTime == 0x7FFF53F0)        cs->addPermanentlyDisabled(restrictItemId);   // 0x00B29340
    else if (cs->0xB22610(restrictItemId)) cs->addPermanentlyDisabled(restrictItemId);
    else cs->addDisableRestrictedData(restrictItemId, endTime);                     // 0x00B35950
}
cs->0xB231B0(restrictItemId, (u8)flag);        // set node[0x1A] = flag in map cs+0x212C0
```

* `0x7FFF53F0` = **2147439600** = 1970-01-01 + 24854 d + 54000 s = **2038-01-18
  15:00:00 UTC** (= 2038-01-19 00:00 KST). The usual "max date / never" sentinel.
* `flag != 0` ⇒ purchasable now. `flag == 0` ⇒ blocked; `endTime` then carries the
  moment it unblocks, or the sentinel for "never".
* `addDisableRestrictedData` is **idempotent**: it linear-scans the vector first and
  returns without inserting (and without logging) if `restrictItemId` is already
  present (`0xB359AF je 0xB35AEA`).
* `0x00B22610(id)` returns true iff a node in `cs+0x212C0` has `node[0x10]==id`
  **and** `node[0x18]==0`; entries whose `[0x18]` is set are skipped and the walk
  continues (`0xB2265C`–`0xB22679`).

### 1.2 `ENUM_NOTIPACKET_CASH_SHOP_PURCHASE_LIMIT_INFO` (NOTI **0x2C2**)

Handler `0x01163020`, registered at `0x0118952B`
(`push 0; push 0x1163020; push 0x2C2; call 0x163C4A0`).

```
u8  a
u8  n
n x { u32 key ; u32 remainCount }
```

```c
cs->0xB22C60(a);                       // walks map cs+0xE44 with `a`
for (i=0; i<n; i++)
    cs->0xB2A530(key, remainCount);    // 0x00B2A530
```

`0x00B2A530(key, value)`: find `key` in the map at `cs+0xE44` (via `0x00B29970`,
the map find); on hit set `node[0x1C] = value` and return true. On miss, repeat
against the map at `cs+0xEF8`; on hit set `node[0x1C] = value`, return true.
Otherwise return false. So **`node[0x1C]` is the remaining-purchase counter** of a
goods entry, and there are two goods tables (`+0xE44`, `+0xEF8`).

### 1.3 Restriction countdown formula — `0x00B1C320`

Called only from `0x00B23320` (`openRestrictedPopupWindow`) and `0x00B33D73`
(inside the goods-cell renderer). Signature `int f(int endTime, int* outUnit)`,
`ret 8`.

```c
delta = endTime - now();          // now() = 0x0164F710, seconds since epoch
days  = delta / 86400;            // magic 0xC22E4507, add, sar 16
hours = delta / 3600;             // magic 0x91A2B3C5, add, sar 11
mins  = delta / 60;               // magic 0x88888889, add, sar 5
if (days  > 0) { *outUnit = 0; return days;  }
if (hours > 0) { *outUnit = 1; return hours; }
if (mins  > 0) { *outUnit = 2; return mins;  }
if (mins == 0) { *outUnit = 2; return 1;     }   // "<1 min" is shown as 1 minute
return -1;                                        // already expired
```

Divisors recovered from the reciprocal magics (all use the `imul; add edx,ecx; sar`
form): `2^48 / (2^32 - 1037359865) = 86400`, `2^43 / (2^32 - 1850913851) = 3600`,
`2^37 / (2^32 - 2004318071) = 60`.

`now()` = `0x0164F710`, which reads the guarded/encrypted global `0x03A5B024`
through the `0x27BC0F0`/`0x27BC160`/`0x27BC1D0`/`0x27BC240`/`0x27BC390` accessor
family (the client's protected-variable readers).

Unit → localized string id: **0 → `getStr(0x11430)` (days), 1 → `0x11536` (hours),
2 → `0x11537` (minutes)** (`0xB2340D`–`0xB2347C`). The same three ids reappear in
the goods-cell renderer at `0xB33D4D`+, so the tooltip renders the same countdown.

### 1.4 `CNRDCashShop::openRestrictedPopupWindow(int restrictItemId)` — `0x00B23320`

```c
if (!isRestricted(id))               // 0x00B22490, membership test on vector cs+0x21300
    { popup(0x209, getStr(0x1142C)); return; }
end = getRestrictedEndTime(id);      // 0x00B22540, returns -1 if absent
LOG("  openRestrictedPopupWindow - restrictedEndTime : [%d])", end);
if (end == -1) return;
remain = calcRemain(end, &unit);     // 0x00B1C320
LOG("  openRestrictedPopupWindow - restrictedRemainTime : [%d])", remain);
if (remain < 0) return;
swprintf(buf /*wchar[0x400]*/, 0x400, getStr(unitStrId), remain);
InterfaceManager[0x3A5C9B8]->openPopupWindow(0x209, buf, 0);   // 0x02304C70
```

Popup type `0x209` is the system message box (used 24× in this unit); `0x263` and
`0x155` also appear (`0xB291FA`, `0xB2BA36`, `0xB2BB14`).

### 1.5 Client-side purchase gates

These are refusals the client applies **before sending anything** — a server will
never see the corresponding request unless the client is patched.

`0x00B33F50` (pre-buy gate, called from `0xB46AB0`/`0xB46D20`) and `0x00B2BB50`
(buy-click, 11 callers). `g` = the goods record (see §2.3).

| gate | VA | rule |
|---|---|---|
| restricted list | `0xB340A1` / `0xB2C2C9` | `rid = 0xB2A620(g->[8])`; if `rid != -1` → `openRestrictedPopupWindow(rid)`, refuse |
| level cap | `0xB340C0` / `0xB2BE1D` | `item = ItemManager[0x3A5C914]->getItem(g->[0x14], 1)` (`0x2752A80`); if `item[0x34C] != 0 && myLevel > item[0x34C]` → refuse, `getStr(0x116C9)`. `item+0x34C` is the **maximum buyer level** |
| rename card | `0xB3410E` / `0xB2BB81` | `g->[0x14] == 0x28EDB2`: parse `/etc/Chn_ChangeCharacName.etc` (`0x2B1D9F0`) into `{minLevel = 0x1E default, maxLevel = 0xB4 default}`; if `minLevel > myLevel` → refuse, `getStr(0x4E2)`. Second gate on `0x11038E0` + `GameCoreSub 0x163D0C0` / `0x163E1E0` → refuse, `getStr(0x55D5)` |
| special product | `0xB2BC6D` | `g->[0x14] == 0x28EAFA`: opens window `0xF674` with a 4-part message `"%s %s %s %s"` from `getStr(0x11457,0x11458,0x11455,0x11456)` |
| item-type 46 | `0xB33FDF` | if the resolved item's `[+0x398] == 0x2E`, require `0xB1E0E0(category)`; else refuse, `getStr(0x88E4)` |
| state-0 whitelist | `0xB2BEBE` | if `cs->state (+8) == 0`, only these `g->[0x14]` may be bought: `3`, `0x28EC41`, `0x28F058`, `0x28F0A6`, `0x28F1BC`, `0x28F416`, `0x1D3F7D78`–`0x1D3F7D7E`. Anything else silently returns |
| two blocked ids | `0xB2BF3B` / `0xB2BF78` | if `0x1833F50() == 1` and `g->[0x14]` is `0x335` or `0x346` → refuse, `getStr(0xB417)` |

Passing all gates: `cs[0xD8C] = g; cs[0xD85] = bGift; cs[0xD84] = 0;` then either the
gift path `0xB1D1A0(1)` or `0xB1B520(g,1)` → `0xB1D230(getStr(0x5699))` on failure,
else `0xB2B510` (the confirm/commit path). **No packet write primitive
(`0x274CA50`/`0x274D1E0`/`0x274E6D0`) is reached from anywhere inside this span** —
the actual buy packet is emitted from a sibling TU.

### 1.6 Visibility filter — `0x00B2C270` (15 external callers)

`bool isGoodsVisible(GoodsRecord* g)`; hides a goods entry when:

* `0xB22400(0xB2A620(g->[8]))` → the restrict id is in the permanently-disabled
  vector at `cs+0x2140C`;
* `0xB21F50(g->[8])` → the key is in the vector at `cs+0x23D40` (the list loaded
  from `Etc/chn_CeraShopHideList.etc` by `0xB47500`);
* special cases `g->[8] == 0x19084` (requires global `[0x39AC2E0] == 1`) and
  `g->[8] == 0x1ADBD`;
* level comparisons against `g->[0x14]` via `0xB1C590` / `0xB1BC80`.

Side effect: sets `g->[0x8C] = 1` when `0xB1DC50(g->[0x10]) == 1 && 0x29B4C60(g)`.

---

## 2. Object layout

### 2.1 `CNRDCashShop` (≥ `0x23D84` bytes; reached via `InterfaceManager[0x3A5C9B8] + 0x8FAC`)

| offset | type | proof |
|---|---|---|
| `+0x08` | `int state` | `0xB44A2D cmp [esi+8], edi` in `CNRDCashShop::setState`; `0xB2BEBE cmp [esi+8],0` |
| `+0x24` | sub-object | `0xB44AEB lea ecx,[esi+0x24]; call 0x2583150` |
| `+0x0E44` | `std::map<int, GoodsSlot>` (table A) | `0xB2A542 lea ecx,[edi+0xE44]; call 0xB29970`; `0xB22C69` |
| `+0x0EF8` | `std::map<int, GoodsSlot>` (table B) | `0xB2A5A9 lea ecx,[edi+0xEF8]` |
| `+0x0D84` | `bool` pending-purchase state | `0xB2BF9B mov byte [esi+0xD84], 0` |
| `+0x0D85` | `bool` isGift (2nd arg of buy) | `0xB2BF95 mov byte [esi+0xD85], al` |
| `+0x0D8C` | `GoodsRecord*` pending | `0xB2BF8F mov [esi+0xD8C], ebx` |
| `+0x0D94` | `int` (per-job value) | `0xB2C026` |
| `+0x0D98` | `int` | `0xB2C040` |
| `+0x1450`, `+0x5B5C` | widget pointers | `0xB45AAC`, `0xB45AB9` |
| `+0x20C20` | `bool` | `0xB44AD0 cmp byte [esi+0x20C20], bl` |
| `+0x212C0` | `std::map<int key, RestrictEntry>` | `0xB2A62C`, `0xB231B9`, `0xB23279`, `0xB22619` |
| `+0x21300` | `std::vector<std::pair<int,int>>` `m_disableRestrictedData` | `0xB35959`, `0xB2FBF0`, `0xB22499`, `0xB22549` |
| `+0x2140C` | `std::vector<int>` permanently-disabled restrict ids | `0xB29349`, `0xB294A0`, `0xB22409` |
| `+0x23D40` | `std::vector<int>` hide list (`chn_CeraShopHideList.etc`) | `0xB21F59` |
| `+0x23D70` | `bool` | `0xB44B63 mov byte [esi+0x23D70], bl` |

Additional widget-block offsets used by exactly one refresh method each (so they
are almost certainly that panel's controls): `+0x5B9C..+0x5C20` (`0xB2FD60`),
`+0x5C60..+0x5CB0` (`0xB30C20`), `+0x20E90..+0x20EC4` (`0xB27270`),
`+0x20CA4..+0x20CC0` (`0xB242D0`), `+0x1B5F0` (`0xB25EC0`), `+0x23D74..+0x23D80`
(`0xB2C690`).

### 2.2 Container element layouts

**`vector<pair<int,int>>` at `+0x21300`** — MSVC `_SECURE_SCL` vector:
`+0x00 _Myproxy`, `+0x04 _Myfirst`, `+0x08 _Mylast`, `+0x0C _Myend`; element stride
**8** (`add esi,8` / `sar esi,3` at `0xB359BE`/`0xB359EC`), `elem[0] = restrictItemId`,
`elem[4] = restrictedEndTime` (`0xB35AA4`/`0xB35AA6`). Same shape for the `vector<int>`
at `+0x2140C` with stride 4 (`0xB293AE`, `0xB293D0`).

**map node at `+0x212C0`** — MSVC `_Tree` node `{_Left@0, _Parent@4, _Right@8, _Myval@0xC}`
with `_Myval = pair<const int, RestrictEntry>`:

| node offset | meaning | proof |
|---|---|---|
| `+0x0C` | map key (= `GoodsRecord[+8]`) | `0x4F1C20` find in `0xB2A620` keyed on the arg |
| `+0x10` | `int restrictItemId` (the id the packet carries) | `0xB23228 cmp [edi+0x10], edx`; `0xB2265C`; returned by `0xB2A69D` |
| `+0x18` | `bool` (skips the `0xB22610` match when set) | `0xB22661` |
| `+0x19` | `bool` default flag | `0xB232EE mov dl, [esi+0x19]` |
| `+0x1A` | `bool` current flag (written by the packet) | `0xB2325A mov [edi+0x1a], al`; `0xB232F1` |
| `+0x1D` | `_Isnil` (std lib, **not** a data field) | `0xB2266B`/`0xB23231` guard before `operator++` |

**map node at `+0xE44` / `+0xEF8`**: `+0x1C` = remaining purchase count
(`0xB2A595 mov [ecx+0x1C], eax`).

### 2.3 `GoodsRecord` — 0xD8 bytes

Copy-ctor `0x00B29CF0` (SEH-guarded, 10 callers), `operator=` `0x00B29F60`
(24 external callers, all in the sibling CashShop code `0xB35D30`–`0xB47500`).
Field widths read straight off the member-by-member copy at `0xB29F6A`–`0xB2A12A`:

```
+0x00 u8            +0x04 +0x08 +0x0C +0x10 +0x14 +0x18 +0x1C +0x20  int x8
+0x24 u8            +0x26 u16
+0x28 +0x2C +0x30 +0x34 +0x38  int x5
+0x3C u8
+0x40 std::string   (0x20 bytes; assigned via 0x00424180 with (ptr, 0, -1))
+0x60 +0x61 +0x62 +0x63 +0x64 +0x65  u8 x6
+0x68 +0x6C +0x70   int x3
+0x74 u8            +0x76 u16       +0x78 u16
+0x7C +0x80 +0x84 +0x88  int x4
+0x8C u8
+0x90 int           +0x94 u8
+0x98 +0x9C +0xA0 +0xA4 +0xA8  int x5
+0xAC u8
+0xB0 +0xB8 +0xC0   8-byte each (copied with movq)
+0xC8 +0xCC +0xD0   int x3
+0xD4 u8            -> sizeof == 0xD8
```

Semantics recovered:

| field | meaning | proof |
|---|---|---|
| `+0x00` | category/type byte | `0xB33F8B movzx eax, byte [esi]; call 0xB1B5F0`; `0xB2BDF1 cmp byte [ebx],0x11` |
| `+0x08` | restrict/group key — the map key into `cs+0x212C0` and the hide-list key | `0xB2A0A1`(caller), `0xB2C2C3`, `0xB340A1` |
| `+0x10` | passed to `0xB1DC50` in the visibility filter | `0xB2C2A4` |
| `+0x14` | **item / product id** | `0xB2BE1D`: `getItem([ebx+0x14], 1)` via `ItemManager 0x3A5C914` |
| `+0x8C` | byte flag set by the visibility filter | `0xB2C2BC` |

---

## 3. Public surface

**Caveat that matters:** the four highest-in-degree functions in the span are
COMDAT-folded template instantiations that merely landed in this TU's section —
they are *not* this module's API. `0x00B29970` (30 callers) is a map `find`
(callers span `0x556140`, `0x5B7510`, `0x636BC0`, `0x979300`, `0x10CCFE0` …);
`0x00B236E0`, `0x00B237A0`, `0x00B23860`, `0x00B23650`, `0x00B2A6B0`, `0x00B2A710`,
`0x00B2A910` are the same kind of thing. `0x00B29CF0`/`0x00B29F60` are the
`GoodsRecord` copy-ctor/assign.

Genuine `CNRDCashShop` members called from outside the span:

| VA | callers | role |
|---|---|---|
| `0x00B2C270` | 15 (`0xB42A20`, `0xB43110`, `0xB43540`, `0xB43630`, `0xB43720`, `0xB43B30`, `0xB44120`, `0x5ECBF0`, `0x12AB6B0`, + 6 internal) | `isGoodsVisible(GoodsRecord*)` |
| `0x00B2BB50` | 11 | buy-click handler `(GoodsRecord*, bool isGift)` |
| `0x00B33F50` | `0xB46AB0`, `0xB46D20` | pre-purchase gate; ends with the `COMMAND_SELECT` sound (`0x1648A20`) |
| `0x00B2F900` | 8 (`0xB37AB0`, `0xB3BF60`, `0xB3F6F0`, `0xB41200`, `0xB461B0`, + 3 internal) | popup helper (types `0x209`, string ids `0x197A`, `0x7969`) |
| `0x00B27270` | `0xB44A00` (setState), `0xB44710`, `0xB47500`, `0x14196B0` | panel rebuild (2384 B, widget block `+0x20E90`) |
| `0x00B2F260`, `0x00B2FC30`, `0x00B2FD60`, `0x00B30C20`, `0x00B313E0`, `0x00B34410` | `0xB44A00` + `0xB45AA0` | the six sub-panel refresh methods, always invoked as one block with a single `bool` arg — `0xB44CD3`–`0xB44D02`, `0xB45ACA`–`0xB45AFE` |
| `0x00B2C690` | `0xB48010` | 11 KB window construction from `Interface2/cs_shop/cs_shop.img` + `Interface/windowcommon_cn.img` |
| `0x00B294B0` | `0xB48010` | loads `Interface2/cs_shop/cs_test.img` + `cs_promotion.img` |
| `0x00B24BD0`, `0x00B25EC0`, `0x00B26310`, `0x00B26740`, `0x00B26860` | `0xB48010` | further init blocks |
| `0x00B23320` | `0xB40CF0` | `openRestrictedPopupWindow` |
| `0x00B35950`, `0x00B29340`, `0x00B294A0`, `0x00B2FBF0`, `0x00B2A530` | NOTI handlers `0x1160280` / `0x1163020` | the packet sinks of §1.1–§1.2 |
| `0x00B35070` | `0xB3DB10`, `0x11972E0` | goods-cell driver; calls `0xB31910` and `0xB2A950` |
| `0x00B31910` | `0xB35070` | 9.8 KB goods-cell renderer: `"%d → %d %s"` (old→new price + unit `getStr(0x8D62)`), `"%.1f"`, and the restriction countdown re-using `0xB22540` + `0xB1C320` at `0xB33D4D`/`0xB33D73` |
| `0x00B2A950` | `0xB35070` | purchase-result banner `"%d  %s %d  %s %d  %s"` with `getStr(0x11D6F/0x1136C/0x378)`, shown through `0x2300930` (popup `0x210`) |

The `setState` method itself (`0x00B44A00`, `"CNRDCashShop::setState"`, in-degree 45)
lives just above the span, as does the initializer `0x00B48010`.

61 distinct localized-string ids are pulled through `getStr` (`0x28C14C0`); the full
per-function map is in the working file `SCRATCH\u43.ann.asm`.

---

## 4. Span assessment — the low bound is wrong

**The briefing's span is too narrow at the bottom, and its two anchors are not in
source order, so neither bound can be trusted as tight.**

1. The upper anchor `0x00B35950` reports source line **15829**; the lower anchor
   `0x00B23320` reports **15913/15918**. The later source line has the *lower*
   address, so the "outermost `__FILE__` anchors" ordering is not monotone in this
   TU and cannot bound it.

2. Nine `CNRDCashShop` methods immediately below `lo` touch the exact same private
   fields and are called by the same NOTI handler as the anchored
   `addDisableRestrictedData`:

   | VA | field | role |
   |---|---|---|
   | `0x00B21F50` | `+0x23D40` | hide-list membership |
   | `0x00B22400` | `+0x2140C` | permanently-disabled membership |
   | `0x00B22490` | `+0x21300` | `isRestricted(id)` — called by the anchor `0xB23320` |
   | `0x00B22540` | `+0x21300` | `getRestrictedEndTime(id)` — called by the anchor |
   | `0x00B22610` | `+0x212C0` | called by NOTI `0x344` |
   | `0x00B22C60` | `+0x0E44` | called by NOTI `0x2C2` |
   | `0x00B231B0` | `+0x212C0` | called by NOTI `0x344` |
   | `0x00B23270` | `+0x212C0` | called by NOTI `0x344` |
   | `0x00B1C320` | — | the countdown formula, called **only** from `0xB23320` and `0xB33D73` |

3. Hard `.rdata` evidence: this TU's private literal run is the contiguous block
   `0x03165C80`–`0x031660F0`, and every function that references it lies in
   `[0x00B22ED0, 0x00B35950]`. `0x00B22ED0` is **below** the briefing's `lo` yet uses
   `0x03165C80` (`"%d → %d %s"`), the first string in the run. The literal
   immediately below the run (`0x03165C18`) belongs to `0xB21670`; the literal
   immediately above (`0x03166180`) belongs to `0xB41AB0`.

So the true translation unit is approximately **`0x00B21700`–`0x00B35B00`**, possibly
extending up to ~`0x00B41AB0` (functions there carry no literals of their own).
Note also that this is only *one* of the files defining `CNRDCashShop`: a base
`CNRDCashShop.cpp` clearly exists (`"CNRDCashShop::setState"` at `0xB44A00` with no
`__FILE__` companion) and is invisible to the anchor method. `tu-index.csv` has no
entry covering `0x00B18790`–`0x00B23320` or `0x00B35950`–`0x00B4B000`.

Everything reported in §1 and §2 was verified against functions inside or
immediately adjacent to the span and is unaffected by the boundary question.

---

## 5. Unknowns

* The `getStr` ids stay numeric — the string table lives in an external `.str`
  resource, not in the image (`0x28C14C0` falls back to a `MessageBoxW`
  `"getStr(%d) == none"` fatal error).
* The exact meaning of `RestrictEntry[+0x18]` (the byte that makes `0xB22610`
  route an id to the *permanently-disabled* vector instead of the countdown
  vector) is not determined; only its effect is.
* Whether `GoodsRecord[+0x14]` values like `0x1D3F7D78`–`0x1D3F7D7E` are item ids
  or product numbers is not settled — `0x28EDB2` is passed to `ItemManager::getItem`
  at `0xB2BE29`, so at least that path treats `+0x14` as an item id.
* `0xB2FD60` / `0xB30C20` / `0xB313E0` / `0xB34410` / `0xB2F260` / `0xB2FC30` are
  identified as the refresh block but not individually named.
