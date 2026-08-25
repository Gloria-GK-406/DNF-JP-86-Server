# TU 009 — `DNFClient\RDAR\CNRDActionBehavior.cpp`

Image: DNF S4A21 client, unpacked from memory, base `0x00400000`.
Briefing span given: `0x0268A530`–`0x02696AB0`. **That span is wrong (too narrow) — see §1.**

---

## 0. What this unit implements — one paragraph

`CNRDActionBehavior` is the **effect half of the client's dungeon/room "Action" script VM**.
A `CNRDAction` (TU 8) is loaded from a resource file; it owns exactly one
`CNRDActionBehavior` (at `CNRDAction+0x58`) and one `CNRDActionTrigger` (at
`CNRDAction+0x5C`). The trigger side evaluates conditions; **this** unit holds the
**behaviour opcode implementations** — one C++ member function per script opcode,
all of them registered into a process-global `std::map<int, PFN>` at `0x03B7A404`
by the one-time initialiser `0x026AA270`. **295 distinct opcodes, IDs `0x081`–`0x1AD`
(129–429)**, are registered there. Five of them are self-identifying via
`__FUNCTION__` literals: `BEHAVIOR_SUMMON_MONSTER_FUNCTION` (130),
`BEHAVIOR_DESTROY_FUNCTION` (147), `BEHAVIOR_GET_OUT_FUNCTION` (148),
`BEHAVIOR_SET_MOVABLE_AREA_FUNCTION` (301), `BEHAVIOR_SHOW_HIT_GAUGE_FUNCTION` (302).
Each handler reads its arguments from a per-behaviour parameter block through a
shared decoder (`0x0266A7B0`) that supports random ranges / random picks, and
optionally scans a list of text tokens for `[UPPER CASE KEY]` named parameters.

---

## 1. The briefing's span is wrong — it is ~4× too narrow

`functions.csv` only indexes 6 function entries in this whole neighbourhood, so the
briefing's "5 functions / 3 anchors" is an artefact of coarse function boundaries.
Scanning for real prologues (`CC 55 8B EC`) finds **97** functions in the briefing
window alone and ~283 in the true module.

Evidence for the real boundary:

| fact | VA | evidence |
| --- | --- | --- |
| Real `__FILE__` (`0x035DE868`) reference sites — only 6 functions, all mid-module | `0x02693611`, `0x0269371C`, `0x026951DB`, `0x02699531`, `0x0269967D`, `0x02699776`, `0x026998F3`, `0x02699EB0`, `0x02699FFC`, `0x0269A0BA`, `0x0269A1E9`, `0x026A0DB1`, `0x026A0EA8`, `0x026A16E2` | dword scan of the image for `0x035DE868` |
| The file's private literal pool block | `0x035DE710` – `0x035DF984` | sequential `dec()` sweep; bounded below by `CNRDAction.cpp`'s pool (ends `0x035DE6xx`) and above by `CNRDActionTrigger.cpp`'s (`0x035DFE90`) |
| That pool block is referenced from `0x0267DA1E` up to `0x026A7FEF`, and **never** from `0x0266A410`–`0x0267B000` (CNRDAction.cpp) | — | dword scan restricted per range |
| The behaviour registration table `0x026AA270`–`0x026AC440` registers 5 functions whose own `__FUNCTION__` literal reads `CNRDActionBehavior::BEHAVIOR_*_FUNCTION` | `0x026AA270` | disassembly, §4 |
| `0x026AC440` is `CNRDActionTrigger`'s constructor (`new(8)` at `0x0267AAC1`), and `0x026B65F0` is its mirror-image registration table | `0x026AC440`, `0x026B65F0` | `0x0267AADC call 0x26ac440`; `0x026B65F4 mov [ecx], 0x3b7a4a0` |

**Real module extent: ≈ `0x0267B130` – `0x026AC440` (~204 KB).**
The briefing window `0x0268A530`–`0x02696AB0` covers roughly a third of the opcode
handlers and *excludes* the registration table that gives them their IDs — i.e. it
excludes the single most useful artefact in the file.

---

## 2. Server relevance

This is client-side script execution, not packet handling — **there are zero calls to
the inbound read primitives (`parsers` column = 0 for every function here)**. What a
server implementer can take from it:

1. **The opcode ID space of the RD "Action" script format.** Behaviour opcode IDs are
   `129`–`429` (`0x081`–`0x1AD`), 295 of the 301 slots used, table in §9. If the
   server ever compiles, validates, or authors these scripts, this is the opcode
   enumeration.
2. **The argument encoding is not "one int per parameter".** `0x0266A7B0` decodes a
   *tagged* stream (§5): tag 1 = random range, 2 = random pick from N, 3 = constant 0,
   4/5 = dynamic object lookups, anything else = literal in the next slot. A server
   re-implementing the script would desync from the client if it read arguments
   positionally, and the randomness is **client-local `rand()`** — so any behaviour
   using tag 1/2 is *not* deterministic between peers.
3. **Local-ownership gating.** 43 call sites in this module gate their effect on
   `0x017623A0`, which returns true only when the acting object is controlled by this
   client (it compares against the global at `0x03B3DFB8`). Opcodes 147 (DESTROY) and
   148 (GET OUT) accept a `SKIP MASTER CHECK` text token that bypasses that gate
   (`0x0269935F`, `0x02699CE6`). So monster destruction driven by a script is *not*
   uniformly executed on every client; a server that assumes it is will mismatch.
4. **Named parameters are matched by text, not by position** (§6), so the `.act`
   text grammar keys in §9's last column are the actual accepted keywords.

---

## 3. Object layout — `CNRDActionBehavior`, size `0x40`

Allocation: `0x0267AA8C  push 0x40 / call 0x423460` then `call 0x026942F0` (ctor).
Destruction: `0x02678BEC  call 0x0268A530` then `free`.

| offset | size | what | proof |
| --- | --- | --- | --- |
| `+0x00` | `0x1C` | `std::wstring m_strSourceName` — MSVC `_ITERATOR_DEBUG_LEVEL=2` layout: `_Myproxy@+0x00`, `_Bx@+0x04` (8 x wchar_t), `_Mysize@+0x14`, `_Myres@+0x18` | ctor `0x02694342 mov [esi+0x18],7` / `0x02694349 mov [esi+0x14],0` / `0x0269434C mov word [esi+4],0`; dtor `0x0268A5A6 cmp [esi+0x18],8 / jb` — heap-buffer test with `_BUF_SIZE`=8, so `wchar_t` |
| — | — | initialised **from the owner's name**: `assign(*(wstring*)(parent+0x18), 0, npos)` | `0x0269437E mov eax,[ebp+8]` ... `0x02694387 add eax,0x18` ... `0x02694391 call 0x424180` |
| `+0x1C` | 4 | unknown — never touched by ctor or dtor | — |
| `+0x20` | 4 | owned lazily-created object, `0xAC` bytes, ctor `0x028C93A0`, dtor `0x028C8FA0` | created at `0x02697C89 push 0xAC / call 0x423460 / call 0x28c93a0 / 0x02697CB6 mov [esi+0x20],eax`; released at `0x0268A56B` |
| `+0x24` | 4 | `std::map<int, PFN>* m_pFuncMap` — the global at `0x03B7A404` | `0x026AA274 mov dword ptr [ecx+0x24], 0x3b7a404` |
| `+0x28` | 4 | `CNRDAction* m_pOwner` | ctor `0x02694383 mov [esi+0x28], eax`; every handler starts `mov ecx,[this+0x28]` |
| `+0x2C` | `0x10` | `std::vector<T>` — `_Myproxy@+0x2C`, `_Myfirst@+0x30`, `_Mylast@+0x34`, `_Myend@+0x38`; element type not determined | ctor `0x02694350 lea edi,[esi+0x2c]` plus zeroing of `+4/+8/+0xc`; dtor `0x0268A58E` frees `[+0x30]` then `[+0x2C]` |
| `+0x3C` | 4 | unknown | — |

Related facts established while proving the above:

* `CNRDAction+0x58` = `CNRDActionBehavior*`, `CNRDAction+0x5C` = `CNRDActionTrigger*`
  (`0x0267AAB9`, `0x0267AAEE`; destroyed together at `0x02678BE3` / `0x02678BFA`).
* `CNRDAction+0x18` = `std::wstring` holding `path + filename` of the loaded script
  (`0x0267A8A5 push "%s%s"` ... `0x0267A8B3 lea eax,[esi+0x18]`).
* `CNRDAction+0xD4` = the owner-object handle; `0x028D6D20` is its inline getter
  (`mov eax,[ecx+0xD4]; ret`) and is the first thing almost every handler calls.
* `CNRDAction+0x4B8` = a 0..2 rotating index used by argument tag 4/5 (`0x0266A8A0`).
* `CNRDActionTrigger` is only 8 bytes: `+0x00` = `&map` (`0x03B7A4A0`), `+0x04` = owner
  (`0x0267AAC1 push 8`, `0x026B65F4`).

### The parameter block handed to every handler

Handlers are `__thiscall f(this, ParamBlock* p, TokenList* text)` and end in `ret 8`.

| offset in ParamBlock | what | proof |
| --- | --- | --- |
| `+0x1C` / `+0x20` | begin/end of a vector of **32-byte string records** — the behaviour's raw text tokens | `0x026989F2 mov edi,[esi+0x20] / sub edi,[esi+0x1c] / sar edi,5`; `0x026A0905 shl esi,5 / add esi,eax` then `call 0x424180` (`wstring::assign`) |
| `+0x2C`/`+0x30`/`+0x34`/`+0x38` | `std::vector<int>` — the numeric argument stream | `0x02BE1370`: `mov eax,[ecx+0x30] / mov edx,[ecx+0x34] / sub / sar 2 / cmp / mov eax,[eax+ecx*4]`, returns `-1` when out of range |

---

## 4. The opcode registration table — `0x026AA270`

```
026AA270  push ebp / mov ebp,esp / push ecx
026AA274  mov  dword ptr [ecx + 0x24], 0x3b7a404      ; this->m_pFuncMap = &g_map
026AA27B  cmp  dword ptr [0x3b7a410], 0               ; one-shot guard
026AA282  jne  0x26ac1db                              ; already built -> return
026AA291  mov  dword ptr [ebp - 4], 0x82              ; key = 0x082
026AA298  call 0x44fbe0                               ; std::map<int,PFN>::operator[]
026AA2A6  mov  dword ptr [eax], 0x2695050             ; value = BEHAVIOR_SUMMON_MONSTER_FUNCTION
   ...                                                ; x295, up to 0x026AC1D0
```

* `0x03B7A404` = the map object (`_Myproxy@+0`, `_Myhead@+4`, `_Mysize@+8`),
  `0x03B7A410` = its "already initialised" byte. The map address appears at exactly
  one code site in the whole image (this function), so the runtime lookup goes
  through `behaviour->+0x24`.
* Called exactly once, from `0x0267AABC`, immediately after the behaviour object is
  constructed inside `CNRDAction::load` (`0x0267AA50`).
* The trigger side is the identical shape at `0x026B65F0`, map `0x03B7A4A0`,
  guard `0x03B7A4AC` — a separate ID space.

Attribution proof that this table belongs to *this* file: five of the functions it
registers carry their own `__FUNCTION__` literal spelling out the class.

| ID | handler | `__FUNCTION__` literal (decrypted) | literal VA |
| ---: | --- | --- | --- |
| 130 | `0x02695050` | `CNRDActionBehavior::BEHAVIOR_SUMMON_MONSTER_FUNCTION` | `0x035DE908` |
| 147 | `0x026990D0` | `CNRDActionBehavior::BEHAVIOR_DESTROY_FUNCTION` | `0x035DEBB0` |
| 148 | `0x02699A50` | `CNRDActionBehavior::BEHAVIOR_GET_OUT_FUNCTION` | `0x035DEC40` |
| 301 | `0x026A03C0` | `CNRDActionBehavior::BEHAVIOR_SET_MOVABLE_AREA_FUNCTION` | `0x035DF628` |
| 302 | `0x026A10E0` | `CNRDActionBehavior::BEHAVIOR_SHOW_HIT_GAUGE_FUNCTION` | `0x035DF808` |

Source line numbers, recovered from the `__LINE__` pushed alongside the `__FILE__`:
DESTROY 1891/1906/1927/1969, GET OUT 2012/2027/2039/2080, SET MOVABLE AREA 9664/9685,
SHOW HIT GAUGE 9767, SUMMON MONSTER 11900, opcode 412 (`0x026934E0`) 13386/13413.
The source file is therefore at least ~13.4 k lines, and link order does not follow
source order.

---

## 5. Argument decoding — `0x0266A7B0`, `CNRDAction::readArg(ParamBlock*, int& cursor)`

Every handler that takes positional arguments calls this. It is a **tagged** decoder,
not a plain array read. `args[]` is the `vector<int>` at `ParamBlock+0x30`.

```
tag = args[cur++]
tag == 1 : lo = args[cur++]; hi = args[cur++]
           return lo if lo == hi else min(lo,hi) + rand() % |hi - lo|      ; 0x0266A7D2
tag == 2 : n  = args[cur++]; r = rand() % n
           v  = args[cur + r]; cur += n; return v                          ; 0x0266A836
tag == 3 : return 0                                                        ; 0x0266A86C
tag == 4 : obj = resolve(owner->handle @ +0xD4)
           v   = obj->0x2119D00(owner->[+0x4B8])
           owner->[+0x4B8] = (owner->[+0x4B8] + 1) % 3; return v           ; 0x0266A87A
tag == 5 : same shape, different accessor                                  ; 0x0266A8CA
default  : return args[cur++]                                              ; 0x0266A8F2
```

`rand` is `0x022D2DD0`; the element read is `0x02BE1370` (returns `-1` out of range).
**Tags 1 and 2 make a behaviour client-locally random** — a server mirroring these
scripts cannot reproduce the outcome without also replicating the RNG.

The `args` column of the table in §9 is the count of `call 0x0266A7B0` sites inside
each handler; `-` means the handler reads no positional arguments (it uses only
named `[KEY]` tokens, or none at all).

---

## 6. Named `[KEY]` parameters — the string-switch idiom

Keyword matching is a lazily built `std::map<const char*, int>` followed by an integer
switch. Worked example, DESTROY (`0x026990D0`):

```
02699356  cmp  byte ptr [0x3b7a1c4], 0        ; map already built?
0269935F  push 0x35dec14                      ; "SKIP MASTER CHECK"
02699364  call 0x27bacc0                      ; runtime literal decryptor
0269937F  mov  dword ptr [ebp-0x5c], 0x75d    ; value paired with the key = 1885
0269937A  mov  ecx, 0x3b7a1cc                 ; the keyword map
02699386  call 0x4cb700                       ; map::insert
   ...
0269934E  cmp  ecx, 0x75d                     ; switch on the matched keyword id
026993D4  mov  byte ptr [ebp-0x34], 1         ; -> "skip master check" flag set
```

The integer paired with each keyword is its **`__LINE__` in the source**: `0x75D` =
1885, and DESTROY's own `__FILE__`/`__LINE__` asserts in the same function are 1891
and 1906. Same idiom in SET MOVABLE AREA — `[COORDINATE]` maps to `0x25B7` = 9655,
its two siblings to 9649 and 9652 (`0x026A083F`, `0x026A0823`, dispatch chain at
`0x026A08C0`), with that handler's asserts at 9664/9685.

Keyword maps observed: `0x03B7A1CC` (guard `0x03B7A1C4`, lock `0x03B7A1C8`) and
`0x03B7A33C` (guard `0x03B7A334`, init flags `0x03B7A350`).

`0x27BACC0` is the on-demand literal decryptor: every string in this module is
ciphertext in the image and is decrypted at the point of use.

---

## 7. Master / ownership gate

`0x017623A0` is called 43 times inside this module, always right after the owner
handle is resolved, and always as an early-out:

```
017623A0  call 0x1a6fca0                      ; get sub-object
017623A8  je   0x17623c0
017623B3  call 0x1a6d7d0                      ; -> true
017623C2  mov  edx,[eax+0x230] / call edx     ; virtual: controlling player
017623CC  cmp  eax, dword ptr [0x3b3dfb8]     ; == the local player?
017623DA  mov  eax, 1
```

Example: SUMMON MONSTER bails out entirely when it returns false
(`0x02695097 call 0x17623a0 / test al,al / je 0x269592a`). DESTROY (147) and
GET OUT (148) let the script override it with the `SKIP MASTER CHECK` token
(`0x0269935F`, `0x02699CE6`).

---

## 8. Public surface

Call-site scan of the whole `.text` for `E8`/`E9` targets landing on a function start
inside `0x0267B000`–`0x026AA270`, excluding intra-module calls:

| function | called from | what it is |
| --- | --- | --- |
| `0x026942F0` | `0x0267AAA7` (`CNRDAction::load`) | **constructor** `CNRDActionBehavior(CNRDAction*)`; `new(0x40)` at `0x0267AA8C` |
| `0x026AA270` | `0x0267AABC` (`CNRDAction::load`) | **one-time opcode-table registration**, §4 |
| `0x0268A530` | `0x02678BEC` | **destructor**; frees `+0x20`, the `+0x2C` vector and the `+0x00` wstring |
| `0x0267DEC0` | `0x0266DFCE` | helper invoked from a `CNRDAction` event loop when the queue entry's `[+0x10]` == 2 |
| `0x0267DD70` | `0x0266DFEF` | same loop, entry code 3; reads a virtual at `vtable+0x624` on its argument |
| `0x02694280` | `0x01B4B7E8` | not behaviour logic — a `std::vector<T>::erase` instantiation for a 36-byte element that the linker happened to place in this COMDAT (`add dword ptr [esi+8], -0x24`) |

Everything else in the module is reachable **only** through the function-pointer map
at `0x03B7A404`, i.e. only by executing a script opcode. There is no other entry.

Two more in-module template instantiations worth naming so they are not mistaken for
behaviours: `0x02694400` is a bubble sort over a `vector<pair<int,int>>` with an
ascending/descending flag (`sar eax,3`, compares `[x+4]` against `[x+0xC]`), and
`0x026942F0`/`0x0268A530` are the ctor/dtor above.

---

## 9. Opcode table — all 295 registered behaviours

`args` = number of tagged-argument reads (`call 0x0266A7B0`); `-` = none.
`named parameters` = `[KEY]` literals referenced from inside that handler; blank means
the handler references no literals at all.

Handlers whose VA lies in `0x0268A530`–`0x026A2570` are the ones the briefing window
covered; every other row would have been missed at the given span.

IDs run 129 (`0x081`) to 429 (`0x1AD`) with six holes: 142, 146, 153, 157, 175, 208
(`0x8E`, `0x92`, `0x99`, `0x9D`, `0xAF`, `0xD0`) are not registered. Two IDs share one
handler: 395 and 396 both map to `0x00BB82B0` (a stub far outside this module).

| ID | hex | handler | args | named parameters / distinctive literals |
| ---: | --- | --- | ---: | --- |
| 129 | 0x081 | `0x02697F30` | 6 |  |
| 130 | 0x082 | `0x02695050` | 8 | ERROR]%d is not valid monster index; CNRDActionBehavior::BEHAVIOR_SUMMON_MONSTER_FUNCTION |
| 131 | 0x083 | `0x02688360` | 3 |  |
| 132 | 0x084 | `0x02684D70` | - | Character/Mage/Effect/Animation/DisenchantBack2Normal.ani |
| 133 | 0x085 | `0x0267C890` | - |  |
| 134 | 0x086 | `0x0267D8F0` | - | aos_pattern_%d |
| 135 | 0x087 | `0x02688810` | 1 |  |
| 136 | 0x088 | `0x02688920` | 1 |  |
| 137 | 0x089 | `0x02688A00` | 1 |  |
| 138 | 0x08A | `0x0267FEB0` | 3 |  |
| 139 | 0x08B | `0x026A3930` | - | [USE OBJECT DIRECTION]; [X VELOCITY]; [Y VELOCITY]; [Z VELOCITY] |
| 140 | 0x08C | `0x026989B0` | - | [ACCEL REG]; [ACCEL]; [DISTANCE]; [SPEED]; [HEIGHT] |
| 141 | 0x08D | `0x02688AE0` | 4 |  |
| 143 | 0x08F | `0x0267D3E0` | - |  |
| 144 | 0x090 | `0x026800A0` | 1 |  |
| 145 | 0x091 | `0x0268CD20` | 1 |  |
| 147 | 0x093 | `0x026990D0` | - | SKIP MASTER CHECK; CNRDActionBehavior::BEHAVIOR_DESTROY_FUNCTION; ERROR! Invalid DESTROY option [%s(%d)]; monster != NULL; aic != NULL; character != NULL |
| 148 | 0x094 | `0x02699A50` | - | SKIP MASTER CHECK; CNRDActionBehavior::BEHAVIOR_GET_OUT_FUNCTION; ERROR! Invalid DESTROY option [%s(%d)]; monster != NULL; aic != NULL; character != NULL |
| 149 | 0x095 | `0x026803F0` | 1 |  |
| 150 | 0x096 | `0x026805D0` | - |  |
| 151 | 0x097 | `0x026806B0` | - |  |
| 152 | 0x098 | `0x0269A370` | 5 | [DIRECTION]; [HIT DELAY]; [LEFT]; [RIGHT]; [RANDOM]; [MY DIRECTION]; [MY OPPOSITE DIRECTION]; [DAMAGE MOTION]; [HIT TYPE]; [RIGIDITY]; [FORCE HIT STUN TIME]; [KNOCK BACK TYPE]; [KNOCK BACK PIXEL]; [KNOCK]; [SLASH]; [BURN] |
| 154 | 0x09A | `0x02680A00` | - |  |
| 155 | 0x09B | `0x0268A740` | - |  |
| 156 | 0x09C | `0x026A3FC0` | - |  |
| 158 | 0x09E | `0x0267B3F0` | - |  |
| 159 | 0x09F | `0x026A5A30` | 2 | casting_%d |
| 160 | 0x0A0 | `0x026A6E30` | - |  |
| 161 | 0x0A1 | `0x026814F0` | 3 |  |
| 162 | 0x0A2 | `0x0269C4C0` | 6 |  |
| 163 | 0x0A3 | `0x0268CF10` | - |  |
| 164 | 0x0A4 | `0x026945C0` | - |  |
| 165 | 0x0A5 | `0x02682510` | - |  |
| 166 | 0x0A6 | `0x026825A0` | - |  |
| 167 | 0x0A7 | `0x02682650` | - |  |
| 168 | 0x0A8 | `0x0267B530` | - |  |
| 169 | 0x0A9 | `0x0269C8A0` | - |  |
| 170 | 0x0AA | `0x02696C90` | - |  |
| 171 | 0x0AB | `0x02684250` | - |  |
| 172 | 0x0AC | `0x026827B0` | - |  |
| 173 | 0x0AD | `0x02682920` | - |  |
| 174 | 0x0AE | `0x02682A20` | - |  |
| 176 | 0x0B0 | `0x02682C60` | - |  |
| 177 | 0x0B1 | `0x0268ED20` | - |  |
| 178 | 0x0B2 | `0x0268EFB0` | - |  |
| 179 | 0x0B3 | `0x02683930` | - |  |
| 180 | 0x0B4 | `0x0269C960` | - |  |
| 181 | 0x0B5 | `0x0268F520` | - |  |
| 182 | 0x0B6 | `0x0268F960` | - |  |
| 183 | 0x0B7 | `0x0268FC10` | - |  |
| 184 | 0x0B8 | `0x0268FE30` | - |  |
| 185 | 0x0B9 | `0x02683AF0` | - |  |
| 186 | 0x0BA | `0x026900E0` | - |  |
| 187 | 0x0BB | `0x02690810` | - |  |
| 188 | 0x0BC | `0x02690AC0` | - |  |
| 189 | 0x0BD | `0x0269CD50` | - | [AXIS]; [DISTANCE]; [RETURN XPOS]; [RETURN YPOS]; [RETURN ZPOS]; [TIME]; [DAMAGE HP]; [ACCELERATE]; [MOVING MOTION]; [RELEASE MOTION]; [Z CLIPPING USING]; [Z CLIPPING LESS THEN]; [Z CLIPPING OFF SET HEIGHT]; [Z CLIPPING SHOW TITLE AND NAME] |
| 190 | 0x0BE | `0x02690E00` | - |  |
| 191 | 0x0BF | `0x0269D9C0` | - |  |
| 192 | 0x0C0 | `0x0268E980` | - |  |
| 193 | 0x0C1 | `0x02682E30` | - |  |
| 194 | 0x0C2 | `0x02682F00` | - |  |
| 195 | 0x0C3 | `0x02682FD0` | - |  |
| 196 | 0x0C4 | `0x02683110` | - |  |
| 197 | 0x0C5 | `0x026831E0` | - |  |
| 198 | 0x0C6 | `0x02683390` | - |  |
| 199 | 0x0C7 | `0x026835B0` | - |  |
| 200 | 0x0C8 | `0x02683780` | - |  |
| 201 | 0x0C9 | `0x02684810` | - |  |
| 202 | 0x0CA | `0x0267C950` | - |  |
| 203 | 0x0CB | `0x02694830` | - |  |
| 204 | 0x0CC | `0x0267B5B0` | - |  |
| 205 | 0x0CD | `0x0268AFA0` | - |  |
| 206 | 0x0CE | `0x026910A0` | - |  |
| 207 | 0x0CF | `0x02684F50` | - |  |
| 209 | 0x0D1 | `0x0267ED10` | - |  |
| 210 | 0x0D2 | `0x02691960` | - |  |
| 211 | 0x0D3 | `0x02686A10` | 5 |  |
| 212 | 0x0D4 | `0x02691A90` | - |  |
| 213 | 0x0D5 | `0x02691BC0` | - |  |
| 214 | 0x0D6 | `0x02697250` | - |  |
| 215 | 0x0D7 | `0x02691CF0` | 13 |  |
| 216 | 0x0D8 | `0x02684490` | - |  |
| 217 | 0x0D9 | `0x02694700` | - |  |
| 218 | 0x0DA | `0x0267B630` | - |  |
| 219 | 0x0DB | `0x0267C9C0` | - |  |
| 220 | 0x0DC | `0x02686E20` | 6 |  |
| 221 | 0x0DD | `0x0267B6E0` | 4 |  |
| 222 | 0x0DE | `0x0267B770` | - |  |
| 223 | 0x0DF | `0x0267BC70` | - |  |
| 224 | 0x0E0 | `0x0267B7B0` | - |  |
| 225 | 0x0E1 | `0x0267BC30` | - |  |
| 226 | 0x0E2 | `0x0267E3E0` | - |  |
| 227 | 0x0E3 | `0x0267B800` | - |  |
| 228 | 0x0E4 | `0x026858E0` | - |  |
| 229 | 0x0E5 | `0x02685760` | - |  |
| 230 | 0x0E6 | `0x02687060` | - |  |
| 231 | 0x0E7 | `0x0267B5F0` | - |  |
| 232 | 0x0E8 | `0x02687110` | 2 |  |
| 233 | 0x0E9 | `0x02687350` | - |  |
| 234 | 0x0EA | `0x0268AAE0` | - |  |
| 235 | 0x0EB | `0x026843B0` | - |  |
| 236 | 0x0EC | `0x0267D660` | - |  |
| 237 | 0x0ED | `0x02684A40` | - |  |
| 238 | 0x0EE | `0x0269DC50` | - | [LEVEL]; [DAMAGE]; [APPEND DELAY TIME]; [FREQUENCY TIME]; [DURATION TIME]; [FIND NEAR SOURCE]; [NOT STATE CHANGE]; [APPEND TIME]; [DISTANCE] |
| 239 | 0x0EF | `0x02684C70` | - |  |
| 240 | 0x0F0 | `0x0267B850` | - |  |
| 241 | 0x0F1 | `0x0268A5F0` | - |  |
| 242 | 0x0F2 | `0x02687640` | - |  |
| 243 | 0x0F3 | `0x026874C0` | - |  |
| 244 | 0x0F4 | `0x0267BD40` | - |  |
| 245 | 0x0F5 | `0x0268D740` | - |  |
| 246 | 0x0F6 | `0x0267B430` | - |  |
| 247 | 0x0F7 | `0x026816E0` | - |  |
| 248 | 0x0F8 | `0x026818B0` | - |  |
| 249 | 0x0F9 | `0x02681980` | - |  |
| 250 | 0x0FA | `0x02681AE0` | - |  |
| 251 | 0x0FB | `0x02681D40` | - |  |
| 252 | 0x0FC | `0x0268DF60` | - |  |
| 253 | 0x0FD | `0x0268E4D0` | - |  |
| 254 | 0x0FE | `0x0268E740` | - | PassiveObject/Common/Particle/ThrowingObjectParticle%d.ptl |
| 255 | 0x0FF | `0x0267E0C0` | - |  |
| 256 | 0x100 | `0x0267B4B0` | - |  |
| 257 | 0x101 | `0x0267E2C0` | - |  |
| 258 | 0x102 | `0x02682140` | - |  |
| 259 | 0x103 | `0x026A99E0` | - | center_message_%d |
| 260 | 0x104 | `0x0267E5A0` | - |  |
| 261 | 0x105 | `0x0268A850` | - |  |
| 262 | 0x106 | `0x026AA050` | - |  |
| 263 | 0x107 | `0x02694B30` | - |  |
| 264 | 0x108 | `0x02691790` | - |  |
| 265 | 0x109 | `0x026859A0` | - |  |
| 266 | 0x10A | `0x02685A40` | - |  |
| 267 | 0x10B | `0x02686230` | - |  |
| 268 | 0x10C | `0x02686940` | - |  |
| 269 | 0x10D | `0x0267BFF0` | - |  |
| 270 | 0x10E | `0x02696F20` | - |  |
| 271 | 0x10F | `0x02681E30` | - |  |
| 272 | 0x110 | `0x02684130` | - |  |
| 273 | 0x111 | `0x026841C0` | - |  |
| 274 | 0x112 | `0x02684980` | - |  |
| 275 | 0x113 | `0x0267BCC0` | - |  |
| 276 | 0x114 | `0x0267E7A0` | - |  |
| 277 | 0x115 | `0x0267BD00` | - |  |
| 278 | 0x116 | `0x0267BB90` | - |  |
| 279 | 0x117 | `0x026A7530` | - |  |
| 280 | 0x118 | `0x0268B210` | - |  |
| 281 | 0x119 | `0x02685620` | - |  |
| 282 | 0x11A | `0x026856C0` | - |  |
| 283 | 0x11B | `0x0267BBE0` | - |  |
| 284 | 0x11C | `0x0267BC00` | - |  |
| 285 | 0x11D | `0x02685220` | - |  |
| 286 | 0x11E | `0x02685430` | - |  |
| 287 | 0x11F | `0x0269E6E0` | - |  |
| 288 | 0x120 | `0x0268E230` | - |  |
| 289 | 0x121 | `0x02681F10` | - |  |
| 290 | 0x122 | `0x02682070` | - |  |
| 291 | 0x123 | `0x026A7AF0` | - | [BOOSTER VELOCITY]; [BOOSTER MAX VELOCITY]; [BOOSTER ACCEL]; [EVADE TIME]; [GRAVITY]; [FALLING INIT VELOCITY]; [TERMINAL VELOCITY]; [HEIGHT]; [LINE GAP]; [BOOSTER USING TIME]; [BOOSTER RECOVERY TIME]; [DISABLE KEYINPUT]; [ZPOS CAMERA VIEW]; [LIMIT HEIGHT]; [INIT XPOS]; [INIT YPOS] |
| 292 | 0x124 | `0x0269E980` | - | [ON]; [OFF]; [TRACE ZPOS] |
| 293 | 0x125 | `0x02692260` | - |  |
| 294 | 0x126 | `0x026925D0` | - |  |
| 295 | 0x127 | `0x02687800` | - |  |
| 296 | 0x128 | `0x0269F000` | - | [GENERATE AREA]; [CREATE FREQUENCE]; [USE TARGET X POS]; [USE TARGET Y POS]; [USE TARGET Z POS]; [PASSIVE OBJECT INDEX] |
| 297 | 0x129 | `0x0269F870` | - | [TYPE]; [VELOCITY]; [ACCELERATE]; [POS]; [MOVE COUNT]; [STAY TIME]; [LOOP]; [AREA]; [MIN MOVE DISTANCE]; [PUSH CHARACTER]; [MOVE TO POS] |
| 298 | 0x12A | `0x0267E880` | - |  |
| 299 | 0x12B | `0x0268B4B0` | - |  |
| 300 | 0x12C | `0x02687950` | - |  |
| 301 | 0x12D | `0x026A03C0` | - | [X POS]; [Y POS]; [Z POS]; [X RANGE]; [Y RANGE]; [Z RANGE]; [COORDINATE]; RELATIVE; ABSOLUTE; CNRDActionBehavior::BEHAVIOR_SET_MOVABLE_AREA_FUNCTION; ERROR!! SET MOVABLE AREA invalid parameter[%s(%d)]; ERROR!! SET MOVABLE AREA invalid coordinate[%s(%d)] |
| 302 | 0x12E | `0x026A10E0` | - | [COUNT]; [INCREASE MAX]; [X POS]; [Y POS]; [TIME]; CNRDActionBehavior::BEHAVIOR_SHOW_HIT_GAUGE_FUNCTION; ERROR! invalid SHOW HIT GAUGE param - %s [%s(%d)] |
| 303 | 0x12F | `0x0267E930` | - |  |
| 304 | 0x130 | `0x026A1860` | - | [OPERATOR]; [PHYSICAL]; [MAGICAL]; [KNOCK BACK]; [BACK ATTACK]; [TIME]; [SOUND] |
| 305 | 0x131 | `0x02683F50` | - |  |
| 306 | 0x132 | `0x0267EA00` | - |  |
| 307 | 0x133 | `0x0267EAC0` | - |  |
| 308 | 0x134 | `0x0267EC30` | - |  |
| 309 | 0x135 | `0x0267BF60` | - |  |
| 310 | 0x136 | `0x02687DE0` | - |  |
| 311 | 0x137 | `0x02687F90` | - |  |
| 312 | 0x138 | `0x0267EE20` | - |  |
| 313 | 0x139 | `0x0267CA50` | - |  |
| 314 | 0x13A | `0x0267EF30` | - |  |
| 315 | 0x13B | `0x0267D7F0` | - |  |
| 316 | 0x13C | `0x0267D870` | - |  |
| 317 | 0x13D | `0x0267F0F0` | - |  |
| 318 | 0x13E | `0x02697C20` | - |  |
| 319 | 0x13F | `0x026A8F80` | - | @NAME; %s |
| 320 | 0x140 | `0x0267BF90` | - |  |
| 321 | 0x141 | `0x0267BFC0` | - |  |
| 322 | 0x142 | `0x0267F410` | - |  |
| 323 | 0x143 | `0x02697DA0` | - |  |
| 324 | 0x144 | `0x02688190` | - |  |
| 325 | 0x145 | `0x02692940` | - |  |
| 326 | 0x146 | `0x02688CF0` | - |  |
| 327 | 0x147 | `0x02688F10` | - |  |
| 328 | 0x148 | `0x0268B6B0` | - |  |
| 329 | 0x149 | `0x0268B940` | - |  |
| 330 | 0x14A | `0x026A5F10` | - |  |
| 331 | 0x14B | `0x0267CAC0` | - |  |
| 332 | 0x14C | `0x026890F0` | - | Appendage/Character/ap_avenger_awakening.nut |
| 333 | 0x14D | `0x0268BB80` | - |  |
| 334 | 0x14E | `0x0267C010` | - |  |
| 335 | 0x14F | `0x0267CB30` | - |  |
| 336 | 0x150 | `0x0267CBF0` | - |  |
| 337 | 0x151 | `0x0267F500` | - |  |
| 338 | 0x152 | `0x0267F590` | - |  |
| 339 | 0x153 | `0x0267F620` | - |  |
| 340 | 0x154 | `0x0267F7B0` | - |  |
| 341 | 0x155 | `0x0267BA40` | - |  |
| 342 | 0x156 | `0x0267CCB0` | - |  |
| 343 | 0x157 | `0x02689290` | - |  |
| 344 | 0x158 | `0x02689420` | - |  |
| 345 | 0x159 | `0x0267CCF0` | - |  |
| 346 | 0x15A | `0x0267CD50` | - |  |
| 347 | 0x15B | `0x0267CDC0` | - |  |
| 348 | 0x15C | `0x0267C070` | - |  |
| 349 | 0x15D | `0x0267C080` | - |  |
| 350 | 0x15E | `0x0267CE40` | - |  |
| 351 | 0x15F | `0x0267F8D0` | - |  |
| 352 | 0x160 | `0x0267C090` | - |  |
| 353 | 0x161 | `0x0267F970` | - |  |
| 354 | 0x162 | `0x026A5EE0` | - |  |
| 355 | 0x163 | `0x02680E70` | - |  |
| 356 | 0x164 | `0x02695980` | - | NONE; DODGE; LINEARDODGE; DARK; XOR; MONOCHROME; graphic_effect_%d |
| 357 | 0x165 | `0x0267D9B0` | - |  |
| 358 | 0x166 | `0x0267C120` | - |  |
| 359 | 0x167 | `0x0267C190` | - |  |
| 360 | 0x168 | `0x0268BD00` | - |  |
| 361 | 0x169 | `0x026895B0` | - |  |
| 362 | 0x16A | `0x02689690` | - |  |
| 363 | 0x16B | `0x02695EA0` | - |  |
| 364 | 0x16C | `0x02689970` | - |  |
| 365 | 0x16D | `0x02689BF0` | - |  |
| 366 | 0x16E | `0x0267C210` | - |  |
| 367 | 0x16F | `0x0267C2A0` | - |  |
| 368 | 0x170 | `0x02693230` | - |  |
| 369 | 0x171 | `0x0268BE20` | - |  |
| 370 | 0x172 | `0x0268BEF0` | - |  |
| 371 | 0x173 | `0x0268BFE0` | - |  |
| 372 | 0x174 | `0x026A7210` | - |  |
| 373 | 0x175 | `0x0268C110` | - |  |
| 374 | 0x176 | `0x0268C210` | - |  |
| 375 | 0x177 | `0x0268C340` | - |  |
| 376 | 0x178 | `0x026A73D0` | - |  |
| 377 | 0x179 | `0x0267C340` | - |  |
| 378 | 0x17A | `0x0268C5E0` | - |  |
| 379 | 0x17B | `0x0268C6A0` | - |  |
| 380 | 0x17C | `0x0268C8C0` | - |  |
| 381 | 0x17D | `0x0267CE90` | - |  |
| 382 | 0x17E | `0x026822A0` | 5 |  |
| 383 | 0x17F | `0x026821F0` | - |  |
| 384 | 0x180 | `0x0268F160` | - |  |
| 385 | 0x181 | `0x0268F390` | - |  |
| 386 | 0x182 | `0x0267C4A0` | - |  |
| 387 | 0x183 | `0x02693840` | - |  |
| 388 | 0x184 | `0x02693A70` | - |  |
| 389 | 0x185 | `0x0267CF20` | - |  |
| 390 | 0x186 | `0x02689FC0` | - |  |
| 391 | 0x187 | `0x02693BC0` | - | time_attack |
| 392 | 0x188 | `0x0268A1E0` | - |  |
| 393 | 0x189 | `0x0267D0F0` | - |  |
| 394 | 0x18A | `0x0267C5F0` | - |  |
| 395 | 0x18B | `0x00BB82B0` | - |  |
| 396 | 0x18C | `0x00BB82B0` | - |  |
| 397 | 0x18D | `0x0267C4C0` | - |  |
| 398 | 0x18E | `0x0267C510` | - |  |
| 399 | 0x18F | `0x0267C560` | - |  |
| 400 | 0x190 | `0x0267C5B0` | - |  |
| 401 | 0x191 | `0x0267CF80` | - |  |
| 402 | 0x192 | `0x02696D80` | - |  |
| 403 | 0x193 | `0x0267FAF0` | - |  |
| 404 | 0x194 | `0x0267D190` | - |  |
| 405 | 0x195 | `0x0267D280` | - |  |
| 406 | 0x196 | `0x0267EB80` | - |  |
| 407 | 0x197 | `0x0267DAD0` | - |  |
| 408 | 0x198 | `0x0267DC20` | - |  |
| 409 | 0x199 | `0x02689E50` | - |  |
| 410 | 0x19A | `0x0268C480` | - |  |
| 411 | 0x19B | `0x0267C2F0` | - |  |
| 412 | 0x19C | `0x026934E0` | - | monster != NULL; aic != NULL |
| 413 | 0x19D | `0x0267C630` | - |  |
| 414 | 0x19E | `0x0268A290` | - |  |
| 415 | 0x19F | `0x02687CA0` | - |  |
| 416 | 0x1A0 | `0x02696130` | - |  |
| 417 | 0x1A1 | `0x0268C990` | - | casting_%d |
| 418 | 0x1A2 | `0x026A97C0` | - |  |
| 419 | 0x1A3 | `0x02693E80` | - |  |
| 420 | 0x1A4 | `0x026962E0` | - |  |
| 421 | 0x1A5 | `0x0267D6E0` | - |  |
| 422 | 0x1A6 | `0x0268CB50` | - |  |
| 423 | 0x1A7 | `0x0267C660` | - |  |
| 424 | 0x1A8 | `0x0268A470` | - |  |
| 425 | 0x1A9 | `0x026967A0` | - |  |
| 426 | 0x1AA | `0x0267D370` | - |  |
| 427 | 0x1AB | `0x0267FCF0` | - |  |
| 428 | 0x1AC | `0x02683D10` | - |  |
| 429 | 0x1AD | `0x02690470` | - |  |
---

## 10. What is still unknown

* **Opcode names.** Only 5 of 295 IDs are self-naming. The text names
  (`[SUMMON MONSTER]`, `[SET MOVABLE AREA]`, `[GET OUT]` at `0x0322B880`,
  `0x03681B5C`, `0x03682CB8`) exist in the image but are consumed by *text-file*
  parsers elsewhere (`0x02C3A3E0` builds a `map<wstring, node-vtable>`; `0x0109CDE0`
  is `CNAOSPatternProcess.cpp`), and no name-to-ID mapping was found in this image.
  The `.act`-style resource the client loads already carries numeric IDs.
* **The runtime dispatcher.** The map is written only at `0x026AA270` and read through
  `behaviour->+0x24`; no call site that performs the lookup and the indirect call was
  located. It is not in `CNRDAction` (`CNRDAction+0x58` is only stored and deleted
  there) and it does not use `map::operator[]`.
* **`CNRDActionBehavior+0x1C`, `+0x3C`**, and the element type of the `+0x2C` vector.
* **The `0xAC`-byte object at `+0x20`** (ctor `0x028C93A0`) — only its creation and a
  `wstring` written to its `+0x28` (`0x02697CC6`) were observed.
* Whether the pre-`0x0268A530` block (`0x0267B1C0`–`0x0268A4xx`) is literally the same
  `.cpp`. Evidence says yes (its handlers use the same private literal pool and the
  same registration table, and CNRDAction's own range references that pool zero
  times), but no `__FILE__` reference falls inside it, so it is inference, not proof.

## 11. Method

* Function starts recovered by scanning for `CC 55 8B EC` — `functions.csv` indexes
  only 6 entries in this whole neighbourhood, which is why the briefing under-counts.
* Literals attributed per real function by disassembling the module and decrypting
  every immediate operand pointing into the literal window (`decstr.dec`).
* Opcode table extracted by pattern-matching `mov [ebp-4], K` / `call 0x44fbe0` /
  `mov [eax], F` across `0x026AA270`–`0x026AC440`.
* External callers found by a full `.text` scan for `E8`/`E9` relative targets.
* Working files: `scratchpad/work9/` (`reg.txt`, `beh.txt`, `attr_full.txt`,
  `table.md`, `argcount.json`, plus the helper scripts `d9.py`, `xref.py`, `cx.py`,
  `starts.py`, `attr.py`, `ext.py`, `sweep.py`).
