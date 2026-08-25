# TU 000 — `DNFClient\RDAR\Appendage\AppendageFactory.cpp`

Span `0x00B076B0` – `0x00B124C0` (the briefing's `hi` of `0x00B11590` is the *start*
of the last anchored function; that function runs to `0x00B124C0`).
41 functions inside the anchor range, 0 parsers.

**Verdict on the span: it is right.** Every literal inside it is appendage/skill-script
vocabulary, and the recovered `__LINE__` constants form one monotone sequence
(927 → 1029…1148 → 1159 → 1516 → 1538 → 1559) consistent with a single ~1600-line
source file. Four functions inside the range are linker-folded shared template/inline
code that merely got laid out here (called from dozens of other TUs) — see §5.

---

## 1. What this unit implements

It is the **appendage factory**: the single place the client turns an *appendage
class id* (a small int, 1..449) into a heap-allocated appendage object, plus a second
entry point that turns an **appendage script node** (parsed from a game script file)
into a fully configured appendage.

"Appendage" in this codebase = a modifier object attached to a character/monster by a
skill or item script — status changes, super armor, gauges, imitations, champion
(awakening-boss) features, animation overlays, etc.

Two overloads, both logging themselves as `"createAppendage"`:

| VA | signature (recovered) | source line of its error log |
|---|---|---|
| `0x00B076B0` | `CAppendage* createAppendage(int classId)` | 927 (`0x39F`) |
| `0x00B11590` | `CAppendage* createAppendage(ScriptNode* node, int arg2)` | 1159 (`0x487`) |

Named helpers (each proven by its own `__FUNCTION__` literal):

| VA | `__FUNCTION__` literal | `__LINE__` of its error log |
|---|---|---|
| `0x00B0CCE0` | `createConcussion` | 1516 (`0x5EC`) |
| `0x00B0CDC0` | `createMarkOfDeathImitation` | 1538 (`0x602`) |
| `0x00B0CE80` | `createShadowBoxerImitation` | 1559 (`0x617`) |

---

## 2. `createAppendage(int classId)` — the complete class table (449 entries)

```
00B076DC  mov  esi, dword ptr [ebp + 8]        ; classId
00B076DF  lea  eax, [esi - 1]
00B076E9  cmp  eax, 0x1c0                      ; 448
00B076EE  ja   0xb0c2c0                        ; -> fail
00B076F4  jmp  dword ptr [eax*4 + 0xb0c31c]    ; 449-entry jump table
```

* Jump table: **`0x00B0C31C`**, 449 dwords (`0x704` bytes), ends exactly at
  `0x00B0CA20` = the next function. Valid `classId` range is **1 … 449**.
* Each live case is the identical 6-instruction shape:
  ```
  push  <sizeof(T)>
  call  0x027C42E0            ; operator new  (Library\Include\Nengine\CNMemoryPool.cpp)
  mov   [ebp+8], eax
  mov   byte [ebp-4], <case#> ; EH unwind state
  test  eax, eax / je fail
  push  esi                   ; <-- the classId is passed to the ctor
  mov   ecx, eax
  call  <ctor>
  ```
  So every appendage constructor takes the class id: `T::T(int classId)`.
* Default case `0x00B0C2C0` logs `"CREATE APPENDAGE FAIL - classId : %d"` (file, func,
  line 927, severity 0) and returns NULL.
* **438 of 449 ids are live; 11 are holes** routed to the default case:
  `67, 176, 177, 188, 298, 322, 328, 344, 348, 368, 437`.
* 438 distinct constructors — no two ids share one. Allocation sizes 312…3008 bytes
  (mode 332; the four big ones are 1636 / 2636 / 3004 / 3008 — ids 16–20 and a few others).

Full machine-extracted table (classId, case VA, `sizeof`, ctor VA) written to
`scratchpad\appendage-classid-table.csv`.

The function is a **hub**: 1093 external callers (1126 total). This is *the* client-wide
appendage constructor. 6 of its callers are on the inbound-packet parse surface
(`parsers.csv`): `0x00666CB0, 0x0097AF90, 0x00C52C00, 0x0114D900, 0x0114F380, 0x01169500`
— i.e. some appendages are instantiated directly from S→C packet handling.

---

## 3. `createAppendage(node, arg2)` — script-name → class id (34 names)

This is the highest-value artefact for anyone mirroring the script format.

### Mechanism (verified two independent ways)

A function-local `std::map<key,int>` at `0x039AC2A8` (once-flag byte `0x039AC2A0`,
lock at `0x039AC2A4`) is built on first call, then the function **restarts**
(`0x00B12019 mov byte [0x39ac2a0],1` / `jmp 0xb115e0`) and does the lookup + dispatch.

* lookup key = the `std::wstring` at **`node+0x44`** (`0x00B116BE cmp [edi+0x5c], 8`
  → `_BUF_SIZE == 8` → `wchar_t`), `map::find` at `0x004CA980`, `map::end` at `0x0293B470`.
* mapped value read at `[node_of_tree+0x10]` → node layout `_Left/_Parent/_Right(0,4,8)`,
  `_Myval = pair<key,int>` at `0xC/0x10`, `_Color/_Isnil` at `0x14/0x15`
  (proved by the 0x18-byte head-node alloc at `0x00B1162A` writing `[edx+0x14]=1`,
  `[eax+0x15]=1`).
* dispatch: `cmp eax,0x405 / je` plus `add eax,-0x40C; cmp eax,0x70; ja fail;
  movzx eax, byte [eax+0xB12448]; jmp dword [eax*4+0xB123C0]` — 0x71-entry byte index
  table + 34-entry jump table.
* The mapped ints are **`__LINE__` values** (1029…1148, strictly inside the function
  whose trailing error log is line 1159, increments of 3–5). The source clearly uses a
  macro that emits both `map[name] = __LINE__;` and `case __LINE__: return maker(...);`
  from one entry, which is why each `case` target lands on the `cmp byte[flag]` guard
  immediately preceding that name's `map::insert`.

Both derivations (jump-table decode, and "guard immediately precedes the insert")
agree on all 34 entries.

### The table

`name` is the literal in the script; `classId` is what actually reaches
`createAppendage(int)`; `creator` is the per-type builder (all internal, in-degree 1,
except the four that are inlined straight into the dispatcher).

| script name | token(`__LINE__`) | classId | ctor | sizeof | creator |
|---|---|---:|---|---:|---|
| dummy | 0x418 | 1 (0x001) | 0x1A00330 | 312 | inline |
| accumulate damage | 0x437 | 70 (0x046) | 0x00AD9440 | 396 | 0x00B0D3E0 |
| active status reflector | 0x421 | 73 (0x049) | 0x011D1980 | 372 | 0x00B0D260 |
| animation to character | 0x44F | 76 (0x04C) | 0x007CF4E0 | 332 | 0x00B0F570 |
| ani set appendage | 0x44B | 77 (0x04D) | 0x008AF300 | 372 | 0x00B10F60 |
| camoflage | 0x41B | 87 (0x057) | 0x00CD81F0 | 352 | inline |
| change basic attack type | 0x40C | 90 (0x05A) | 0x01A8ED00 | 324 | 0x00B0CB90 |
| change status | 0x405 | 99 (0x063) | 0x01AB6030 | 448 | 0x00B0D920 |
| change weapon color | 0x40F | 118 (0x076) | 0x01ABB810 | 416 | 0x00B0DC80 |
| character draw change appendage | 0x440 | 121 (0x079) | 0x00A26F50 | 472 | 0x00B0D510 |
| concussion | 0x424 | 122 (0x07A) | 0x00CD92F0 | 332 | 0x00B0CCE0 |
| element count | 0x43D | 133 (0x085) | 0x00A27880 | 360 | inline |
| gauge | 0x434 | 141 (0x08D) | 0x00ADB710 | 536 | 0x00B0EDB0 |
| immune active status | 0x446 | 170 (0x0AA) | 0x00A2F7B0 | 332 | 0x00B0DBC0 |
| kalla fire imitation | 0x42D | 172 (0x0AC) | 0x00CDA340 | 504 | 0x00B0CF30 |
| mark of death imitation | 0x427 | 175 (0x0AF) | 0x00CDA810 | 340 | 0x00B0CDC0 |
| only strong element | 0x43A | 184 (0x0B8) | 0x00AD8A30 | 332 | 0x00B0DF00 |
| shadow boxer imitation | 0x42A | 193 (0x0C1) | 0x00CDDA50 | 444 | 0x00B0CE80 |
| skill data up | 0x41E | 196 (0x0C4) | 0x01301D50 | 352 | 0x00B0E350 |
| skill level | 0x412 | 197 (0x0C5) | 0x014C22D0 | 332 | 0x00B0CC30 |
| special effect by overlap | 0x430 | 198 (0x0C6) | 0x00B1AA60 | 444 | 0x00B10830 |
| super armor | 0x415 | 202 (0x0CA) | 0x01AD5E30 | 324 | inline |
| rebirth | 0x462 | 287 (0x11F) | 0x011D0170 | 384 | 0x00B0D810 |
| mastery control appendage | 0x443 | 403 (0x193) | 0x00A25990 | 352 | 0x00B0D640 |
| item stack | 0x458 | 434 (0x1B2) | 0x004420F0 | 360 | 0x00B0DA80 |
| empty skill slot count | 0x45D | 435 (0x1B3) | 0x00449850 | 364 | 0x00B0DB20 |
| champion feature inhale | 0x467 | 440 (0x1B8) | 0x004544F0 | 432 | 0x00B0F6C0 |
| champion feature guard bead | 0x46A | 441 (0x1B9) | 0x00451E90 | 436 | 0x00B0F920 |
| champion feature charge | 0x46D | 442 (0x1BA) | 0x00450F30 | 472 | 0x00B0FC60 |
| champion feature back warp | 0x470 | 443 (0x1BB) | 0x0044E620 | 396 | 0x00B0FED0 |
| champion feature nen guard | 0x473 | 444 (0x1BC) | 0x00454F40 | 408 | 0x00B10080 |
| champion feature self heal | 0x476 | 445 (0x1BD) | 0x004557E0 | 388 | 0x00B10240 |
| champion feature harden shield | 0x479 | 446 (0x1BE) | 0x00453880 | 428 | 0x00B103F0 |
| champion feature blood sucking | 0x47C | 447 (0x1BF) | 0x0044FAF0 | 480 | 0x00B105F0 |

Machine-generated copies: `scratchpad\appendage-name-classid.csv`,
`scratchpad\appendage-name-table.csv`.

Note the `champion feature *` block is a contiguous id run **440–447** — `0x1B9`
(guard bead) is only visible because its `push 0x1b9` is separated from the
`call 0xb076b0` by four other pushes (`0x00B0F95F`).

### Where the script node comes from

`0x01A73920` (main driver, calls `createAppendage(node,0)` twice):
```
mov ecx, [0x03A5C954]       ; script-loader singleton
push 1 / push <id>
call 0x027524F0             ; literal: "CNScriptLoader::getScriptByFileName"
push 0 / push eax
call 0x00B11590
```
So appendage definitions are rows of a loaded game script, fetched by id from
`CNScriptLoader`. Other callers: `0x015F6570`, `0x018F31D0`, `0x01A00570`, `0x00B124C0`.

---

## 4. Object layout: the appendage script node (`node`, `[ebp+8]` of `0x00B11590`)

Recovered purely from access patterns in this unit. All three containers are
MSVC `_SECURE_SCL` `std::vector` (`_Myproxy, _Myfirst, _Mylast, _Myend` at +0/+4/+8/+0xC),
so the vector *object* starts 4 bytes before the data pointer.

| offset | type | proof |
|---|---|---|
| `+0x00` | `wchar_t*` / name string (the script "index") | `0x00B1226A mov eax,[edi]` pushed as the `%s` vararg of `"…createAppendage fail (index : %s)"`; also `0x00B12378 mov ecx,[edi]; call 0x19FC5B0` |
| `+0x24` | `std::wstring` — path/prefix root | `0x00B0F5D2 lea ecx,[esi+0x24]` fed to the wstring `operator+` at `0x00B0E250`, concatenated with each string arg |
| `+0x44` | `std::wstring` — **appendage type name** (the factory key) | `0x00B116BA cmp [edi+0x5c],8 / mov ecx,[edi+0x48]`; `_Myproxy 0x44, _Bx 0x48, _Mysize 0x58, _Myres 0x5C` |
| `+0x64` | dword | `0x00B122BE mov eax,[edi+0x64]` → `appendage->vf[0x74](eax)` |
| `+0x70` | `std::wstring` (size at `+0x84`) | `0x00B122F7 cmp [edi+0x84], 0` = "is it empty"; `0x00B122FF lea ecx,[edi+0x70]` → `operator+` |
| `+0xB4` | dword | `0x00B122C9` → `appendage->vf[0x7C](x)` then `0x019FCDE0` |
| `+0xB8` | dword | `0x00B122E0` → `appendage->vf[0x7C](x)` then `0x01103010` |
| `+0xBC` | `std::vector<int>` — **integer arg list**; data `+0xC0`, end `+0xC4` | `sub eax,ecx; sar eax,2` everywhere; `lea ecx,[esi+0xbc]` + begin/end at `0x00B0DA9F` |
| `+0xD0` | `std::vector<float>` — **float arg list**; data `+0xD4`, end `+0xD8` | `0x00B0D9D2 mov edx,[esi+0xd4]; movss xmm0,[edx+edi*4]` |
| `+0xE4` | `std::vector<std::wstring>` — **string arg list**, stride **0x20**; data `+0xE8`, end `+0xEC` | `sar edx,5` at `0x00B0CBCE`; `add ebx,0x20`; `lea ecx,[esi+0xe4]` + begin/end at `0x00B0DBC0` |
| `+0xFC` | int, `> 0` guard | `0x00B0CA33 cmp dword [edi+0xfc], 0 / jg` |
| `+0x100` | byte | `0x00B12369 movzx eax, byte [edi+0x100]` → `0x019FC8A0(appendage, u8)` |
| `+0x104` | dword | `0x00B12382` → `0x025D5E30(appendage, dword)` |

**The three arg vectors are parallel, indexed by the same `i`.** `change status`
(`0x00B0D920`, classId 99) is the clearest proof:
```
for (i = 0; i < intArgs.size(); ++i) {
    statusId = 0x029077F0( &strArgs[i], (intArgs[i] == 1), floatArgs[i], arg2 );
    0x00E98FE0(&tmp, statusId);
    appendage->vf[0x118](&tmp, 1);
}
```
`animation to character` (`0x00B0F570`) shows the other idiom: `concat(node->str24,
strArgs[i])` builds a resource path, and the int args are consumed **three at a time**
(`[edx+edi*4-4]`, `[edx+edi*4]`, `[edx+edi*4+4]`).

### Appendage object — virtual slots exercised from here

| slot | used at | shape |
|---|---|---|
| `+0x74` | `0x00B122C7`, `0x00B0CB56` | `set(dword)` — takes `node+0x64` |
| `+0x7C` | `0x00B122D7`, `0x00B122EE`, `0x00B0D765` | returns a sub-object pointer |
| `+0x114` | `0x00B0CB5A` | `(statusId, bool, float, …)` |
| `+0x118` | `0x00B0DA02`, `0x00B0D748` | apply/append a status entry |

Non-virtual appendage methods called: `0x019FC5B0`, `0x019FC890`, `0x019FC8A0`,
`0x019FE670`, `0x019FCDE0`, `0x01103010`, `0x025D5E30`.
Owner-object virtual slot `+0x6A0` = "get appendage container" (`0x00B0CA44`, `0x00B0CA96`).

---

## 5. Public surface (what is reachable from outside the unit)

| VA | ext. callers | what it is |
|---|---:|---|
| `0x00B076B0` | 1093 | `createAppendage(int classId)` — the hub |
| `0x00B11590` | 5 | `createAppendage(scriptNode*, int)` — script-driven |
| `0x00B0CB00` | 2 | convenience factory: `kind` 0..4 → statusId `0x3B..0x3F`; returns NULL if the amount is negative; builds classId **99** and calls `vf[0x74](owner)` then `vf[0x114](statusId, false, (float)amount, …)` |
| `0x00B0D720` | 2 | convenience factory for classId **136 (0x88)**: `vf[0x118](1, x, flag?3:0)`, optional `0x019FC890(1)`, then stores a value into `vf[0x7C]()+0x28` |
| `0x00B0CA20` | 1 | query over an owner's appendage container; gated on `node+0xFC > 0` |
| `0x00B0CA80` | 1 (CNMage.cpp, TU 45) | iterate an owner's appendage container, call `vf[0x8C]` on each |

Everything else in the range is **internal** (in-degree 1, called only by `0x00B11590`) —
the 30 per-type `create*` builders listed in the §3 table.

### Not actually AppendageFactory code (linker-folded shared code inside the span)

* `0x00B0E250` — `std::wstring operator+(const wstring&, const wstring&)`. Proof:
  `mov [esi+0x18],7` (`_Myres = _BUF_SIZE-1` for `wchar_t`), `mov word [ebx],cx`,
  two `append(s,0,-1)` calls at `0x00B0E301`/`0x00B0E310`. 15 external callers across
  the whole image.
* `0x00B0D000` — dispatch on `this & 0xF` into five store routines
  (`0x027BBA60/BBE0/BCA0/BD50/BE10`). 10 external callers from unrelated TUs.
  Reading of the low-nibble dispatch is **unknown** (alignment-specialised store, or
  DNF's address-keyed value obfuscation — I did not confirm which).
* `0x00B10CE0` / `0x00B10E10` / `0x00B10EA0` — `std::vector<T>::resize` chain with
  `sizeof(T) = 0xD0` (max-size guard `cmp ebx, 0x13B13B1` = `0xFFFFFFFF/208`).
  Called from `DNFShared\GameScript\RDARScriptItem.cpp` (TU 129).

---

## 6. What a server implementer gets from this

1. **The appendage class-id space is 1…449 with 11 dead slots** (`67, 176, 177, 188,
   298, 322, 328, 344, 348, 368, 437`). Any id the server sends outside that set makes
   the client log `CREATE APPENDAGE FAIL - classId : %d` and get a NULL appendage.
2. **34 script tokens map to fixed class ids** (§3). If the server parses the same
   appendage scripts (skill/item scripts naming `"super armor"`, `"change status"`,
   `"rebirth"`, `"item stack"`, `"empty skill slot count"`, the `champion feature *`
   family, …), those are the ids the client will build.
3. **The script row format** the client expects (§4): one wide-string type name, plus
   three *parallel* argument vectors — `int[]`, `float[]`, `wstring[]` — indexed
   together, plus a path prefix string and a couple of scalar flags. Per-type builders
   consume them 1-at-a-time (`change status`, `change basic attack type`) or
   3-at-a-time (`animation to character`).
4. Six inbound-packet parsers call `createAppendage(int)` directly
   (`0x00666CB0, 0x0097AF90, 0x00C52C00, 0x0114D900, 0x0114F380, 0x01169500`), so at
   least some appendages are created from wire data with a server-chosen class id.
   **Which packets those are is not established here** — that belongs to those TUs.

## 7. Explicitly unknown

* Class *names* for the 438 constructors: this image's RTTI type descriptors are
  stripped to `TD#nnnn`, so no `.?AVCAppendage…@@` strings exist to attach.
  Only the 34 script tokens give human-readable identity.
* Meaning of `node+0x64 / +0xB4 / +0xB8 / +0x104` beyond "dword forwarded to a setter".
* The low-nibble dispatch in `0x00B0D000` / `0x00B0D720`.
* No packet field extraction happens in this unit (0 parsers in range) — the
  packet↔appendage binding lives in the callers.

## Artefacts written

* `scratchpad\appendage-classid-table.csv` — 449 rows: classId, case VA, `sizeof`, ctor VA
* `scratchpad\appendage-name-classid.csv` — 34 rows: script name, token, classId, ctor, size, creator
* `scratchpad\appendage-name-table.csv` — 34 rows: token, name, case target, creator, args
* `scratchpad\work000\` — the extraction scripts and raw disassembly dumps
