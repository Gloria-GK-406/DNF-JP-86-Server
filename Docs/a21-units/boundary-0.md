# boundary-0: which `.text` functions are protector runtime rather than game code?

*Independent re-run. Every number below was re-measured from the dump and the CSVs in this
session; nothing is carried over on trust. Where I reproduce an earlier run's figure I say so.*

## Answer

**No VA range of `.text` is protector runtime.** The protector's runtime body lives entirely
in the appended sections above `0x04F69000`. What it owns *inside* `.text` is a scatter of
small VM-entry stubs — 66 of them are mis-registered as functions in `functions.csv`, plus
19 more that sit inside a game function's partition and so never got their own row. Total
~20 KB out of 46.2 MB (0.04 %). They are singletons, never adjacent, never a range.

The three anchors the task names are all refuted as discriminators, each for a base-rate
reason. `func-source-file.csv` is genuine ground truth but only in the **positive**
direction: `__FILE__` ⇒ game. The negative direction is worthless — 98.9 % of `.text`
functions have no `__FILE__` and are still game code.

---

## Section map (re-parsed from the on-disk PE header of `DNF.exe`, ImageBase `0x00400000`)

| section | VA range | characteristics | executable? | role |
|---|---|---|---|---|
| `.text` | `0x00401000`–`0x03029000` | `0xE0000020` | **yes** | game code, 46.2 MB, all 67,074 functions |
| `.code` | `0x03029000`–`0x0395E000` | `0xC0000040` | **no** | data (encrypted literal blob) |
| `.data` | `0x0395E000`–`0x04E2A000` | `0xC0000040` | no | data |
| `.rdata` | `0x04E2A000`–`0x04E5D000` | `0xC0000040` | no | data |
| `.eI4` | `0x04E5D000`–`0x04F69000` | `0x40000040` | no | data |
| `.rsrc` (1st) | `0x04F69000`–`0x04F79000` | `0xE00000E0` | **yes** | protector |
| `.tls` | `0x04F79000`–`0x051BB000` | `0xE00000E0` | **yes** | protector runtime (2.3 MB) |
| `.vmp0` | `0x051BB000`–`0x051BF000` | `0xE0000060` | **yes** | protector |
| `.vmp1` | `0x051BF000`–`0x0598B000` | `0xE0000020` | **yes** | **VM bytecode + mutated blocks (7.8 MB)** |
| `.bss` | `0x0598B000`–`0x05994000` | `0xE0000020` | **yes** | protector |
| `.mackt` | `0x05994000`–`0x05998000` | `0xE0000060` | **yes** | protector |
| `.-wI` | `0x05998000`–`0x05D57D15` | `0x60000020` | yes (RX) | **import wrappers** |
| `."2A` | `0x05D58000`–`0x05D8B800` | `0xC0000040` | no | IAT |
| `.#\|U` | `0x05D8C000`–`0x07827800` | `0x60000020` | yes (RX) | packer stub; PE EntryPoint RVA `0x07213FBC` |
| `.rsrc` (2nd) | `0x07828000`–`0x0786E000` | `0x40000040` | no | resources |

Confirming the standing rule: `.code`/`.data` are **not** executable; `.tls`/`.bss`/`.rsrc`
**are**. Protector runtime = `0x04F69000`–`0x05D57D15` ∪ `0x05D8C000`–`0x07827800`.

All 67,074 rows of `functions.csv` fall in `.text` (`0x00401000` … `0x02DF3903`). There is
no protector-section function in the table to find.

---

## Anchor A — "functions reached only from `.tls` / `.-wI` / `.mackt`" → **the set is empty**

I byte-scanned every non-`.text` section for `E8`/`E9` rel32 landing in `.text`, and split
hits into "exact `functions.csv` entry" vs "mid-function":

| section | rel32 → `.text` **entry** | rel32 → mid-function |
|---|---|---|
| `.tls` (2.3 MB) | **0** | 98 |
| `.mackt` | **0** | 2 |
| `.vmp0` | **0** | 4 |
| `.bss` | **0** | 4 |
| `.rsrc` (exec) | 17 | 40 |
| `.-wI` | 2 | 435 |
| `.vmp1` | 13,179 (byte-scan; `.vmp1` is bytecode, mostly noise) | 9,589 |
| `.#\|U` | 1 | 2,507 |

`.tls` and `.mackt` **never** enter a `.text` function. Their `.text` targets are all
mid-body — e.g. `.tls 0x0500D065` → `0x004696C5` = func `0x004694A0` + `0x225` — the
*resume* edge of an injected check, not a call to a callee.

**`.-wI` is not a caller — it is the import-wrapper section.** All 1,654 rows of
`ClientHack/tools/wrapper-apis-a21.csv` have `entry_va` inside `.-wI`
(`0x059B535E` → `kernel32!IsDebuggerPresent`, `0x05ACB332`, `0x05BEA621`, …). "This `.text`
function calls `.-wI`" means only "it calls a Windows API"; 400 `.text` functions do.
Treating that as a protector marker is exactly the base-rate trap.

Across the whole image only **763** `.text` function entries are targeted by a rel32 from a
protector section, and **757 of them have ordinary `.text` callers**. The 6 that do not are
all game code by literal: `0x008D05A0` (`WZ_ANTIGRAVITY_STARTER`), `0x01BDE910`
(`CNDragonHeadSide::procAppend`), `0x01BF28F0`, `0x02177210`, `0x02191E60`
(`R_WRAITH_KISS`), `0x0226C630` (`CNSuccubus::procAppend`).

**Inverted, the anchor is decisive against itself.** 29 `.text` functions contain a direct
call into `.tls` and 6 into `.mackt`. Eleven of the 29 carry a `__FILE__` from the game tree:
`CNGameSocket.cpp` (`0x0274E6D0`), `IRDCharacter.cpp` (`0x01898380`), `IRDMonster.cpp`
(`0x018E09F0`), `IRDCollisionObject.cpp` (`0x018B27D0`), `IRDActiveObject.cpp` (`0x017DDD90`),
`CNRDSecurityMessage.cpp` (`0x026181C0`), `AICharacterFactory.cpp` (`0x02118F60`),
`CNRDUdpModule.cpp` (`0x022EA720`), `CNSelectDungeonModule.cpp` (`0x0272D990`),
`CNRDUserInfoPacketHandler.cpp` (`0x013E6370`), `GameCoreNetwork_CMD_Etc.cpp` (`0x0110DE10`).
Touching `.tls` is a *host of an injected check*, i.e. game code.

## Anchor B — "no API reach and no decrypted literals" → base rate 74 %

| property | count | share of 67,074 |
|---|---|---|
| carries a `__FILE__` literal | 759 | **1.1 %** |
| carries any decrypted literal | 8,798 | 13.1 % |
| reaches any API within 4 calls | 11,745 | 17.5 % |
| **neither literal nor API** | **49,727** | **74.1 %** |

"No API, no literals" describes three quarters of the game. Worse, it does not even
concentrate: the 2 MB band `0x01E00000`–`0x02000000` has 1,430 functions, exactly **one**
`__FILE__`, only 141 literal-carriers and 105 API-reachers — and it is unambiguously game
code (literals `Effect/Animation/ATSpire/spire_shockwave_glow_dodge.ani`,
`Character/Fighter/at_custom/ATFlameLegsFoots.equ`, `MFIRE_LOOP`, `CSPEAR_READY_LOOP`).

The `__FILE__` ground truth spans `0x004CC660`–`0x02BE7230` with a largest interior gap of
1.75 MB (`0x01DBC950` → `0x01F7D4A0`). Those gaps are *not* foreign regions; they are the
modules Neople compiled without the logging macro. No gap is protector.

Searching all 36,678 decrypted literals for protector vendor names
(`TenProtect|tss_|QQSafe|HackShield|nProtect|VMProtect|Themida|XTrap|GameGuard|XignCode|
AhnLab|Wellbia|antibot|TenProxy`) returns **23 functions, 20 of which carry a game-tree
`__FILE__`**: `DNFClient\RDAR\Library\CNAntiBot.cpp` (6 functions, `0x02768E20`–`0x0276A4E0`),
`DNFClient\RDAR\Library\SafeStorage.cpp` (5, `0x0143E970`–`0x0143EDA0`),
`library3rd\chinalib\antibot\include\AntiBotImport.h` (`0x02768F50`, `AntiBot::CreateAntiObj`),
`ClientArgumentParser.cpp` (`TenProxy.dll load Failed`). This is Neople's *integration* code
calling a vendor SDK, not the vendor's runtime. The vendor runtime carries no literals in
`.text` because it is not in `.text`.

## Anchor C — the `0010_DisableGameGuard` patch neighbourhoods → **11 game, 1 CRT, 1 data**

The A21 table values are **RVAs** (VA = RVA + `0x400000`). I verified all 13 `expected` byte
strings against the dump at RVA — 13/13 match, 0/13 match if read as VAs.

| patch | RVA | VA | enclosing func | `__FILE__` / evidence | verdict |
|---|---|---|---|---|---|
| n5-check-1 | `0x00A0CBF0` | `0x00E0CBF0` | itself, 80 B, in-deg 33 | none, but body is `call 0x163D8B0` (`ENUM_AGE_TYPE::operator=`) → `call 0x163C670` → `call 0x163C650`, returns bool | **game** |
| n5-check-2 | `0x0123C650` | `0x0163C650` | itself, 32 B | `mov al,[ecx]; cmp al,[edx]; sete al; ret 4` — an enum `operator==` | **game** |
| n5-check-3 | `0x0123D8B0` | `0x0163D8B0` | itself, 181 B | `GameCoreSub.cpp`, `ENUM_AGE_TYPE::operator =`, `invalid enum value ENUM_AGE_TYPE [ %d ]` | **game** |
| skip-tcls-mode-init | `0x00A0D04C` | `0x00E0D04C` | `0x00E0D010` | `ClientArgumentParser.cpp`, `MODE - TCLS`, `TenProxy.dll load Failed` | **game** |
| skip-encrypted-channel-key | `0x00D69661` | `0x01169661` | `0x01169500` | `GameCoreNetwork_NOTI_Etc.cpp` | **game** |
| bypass-safe-storage-init | `0x0103E97A` | `0x0143E97A` | `0x0143E970` | `Library\SafeStorage.cpp`, `tss_skb::QQSafeStorage::Init` | **game** |
| fix-wwinmain-check | `0x02393BF2` | `0x02793BF2` | `0x02793870` | `Main\Main.cpp`, `wWinMain`, `INIT>> Background Validator is Finished.` | **game** |
| fix-exception-filter | `0x011479E3` | `0x015479E3` | `0x01547850` | Neople crash dumper: `<CrashDump version='1'>`, `fulldump.dmp`, `dbghelp.dll` | **game** |
| bypass-version-signature | `0x01673BBC` | `0x01A73BBC` | `0x01A73920` | no `__FILE__`; callers carry `creature`/`npc`/`monster`/`USE_DEBUFF`; callees include `AppendageFactory.cpp` | **game** |
| skip-game-state-verification | `0x01F1467D` | `0x0231467D` | `0x02312480` | `CNRDInterfaceManager.cpp`, `tryCheckRequestItem` | **game** |
| restore-prologue | `0x0234E6D8` | `0x0274E6D8` | `0x0274E6D0`, in-deg 959 | `CNGameSocket.cpp`, `[2-> COMMAND PACKET] Packet : [%d]`, `TCP_SEND` | **game (VMP-mutated prologue)** |
| bypass-startup-validation | `0x0286C8D4` | `0x02C6C8D4` | `0x02C6C8B9` | `EncodePointer`, one caller `0x02C6EF00`, CRT band | **CRT** |
| decrypt-az-module-name | `0x031E997C` | `0x035E997C` | none — address is in **`.code`**, a non-executable data section | encrypted string, not code | **data** |

`restore-prologue` is the only patch whose *bytes* are protector-owned: the packer replaced
`E8 33 55 51 00 A1 E0 CE 99 03 …` with `54 89 24 24 68 4A E0 DC 01 E8 BC 1F 8B 02 …`
(`push esp; mov [esp],esp; push 0x01DCE04A; call …`). Still a game function; only its entry
was rewritten. The "n5 check" label is upstream's, not the client's.

---

## What actually *is* protector code inside `.text`

VMProtect VM-entry stubs. Their shape is unmistakable and, unlike every anchor above, it has
a real base rate: an obfuscated register/flag spill built out of
`lea esp,[esp±k]` / `mov dword ptr [esp],reg` / `xchg dword ptr [esp],reg` / `pushfd`,
interleaved with junk immediates, no `ret`, terminating in a transfer into `.vmp1`.

Scoring every `functions.csv` entry (linear decode from its entry to the first
`ret`/`int3`/`leave`, max 80 instructions): let `sig` = count of those three VMP-specific
forms, `stacky` = fraction of stack-manipulating instructions.

| filter | entries | with `__FILE__` | with literals | with API reach |
|---|---|---|---|---|
| `sig ≥ 1 ∧ stacky ≥ 0.7` | 79 | **0** | **0** | 3 |
| `sig ≥ 2 ∧ stacky ≥ 0.7` | **66** | **0** | **0** | 3 |
| `sig ≥ 3 ∧ stacky ≥ 0.7` | 57 | **0** | **0** | 1 |

**Base rate: 79 / 60,319 decodable entries = 0.13 %.** That is a discriminator. And the
`__FILE__` ground truth agrees with it perfectly in the direction it can: zero of the 79
carries a source path, while 759 game functions do.

### The 66 protector-stub entries (full list; also `scratchpad/_v0_protector_stubs.csv`)

```
0x0163D965 0x016F8D7C 0x0189274C 0x01BEA2E7 0x01BEBBCA 0x01BEBC33 0x01BFF867 0x01C04646
0x01C0C9C7 0x01C0CA24 0x01C19ADA 0x01C20106 0x01C20B96 0x01C20C3D 0x01C4352A 0x01C569AB
0x01C9ACD5 0x01C9B382 0x01C9B575 0x01CC4898 0x01CC5BC1 0x01CC65A8 0x01CC66AE 0x01CC68C8
0x01CC723C 0x01CC7955 0x01CC7B07 0x01EBF172 0x01EC02B8 0x01EE6F98 0x01EE752E 0x01EE7F17
0x01EED231 0x01FABCF2 0x0204E2E5 0x0204FA00 0x02108E21 0x02113255 0x02114022 0x02114488
0x02169914 0x02176479 0x02181657 0x02192EE7 0x02193A55 0x021AACAB 0x021C729C 0x02201C82
0x0223328C 0x0225472B 0x0225F9EC 0x0226A385 0x0226AC95 0x0226B827 0x0228B132 0x02736AB4
0x0287067D 0x0289EADB 0x028A045D 0x029B03FF 0x02A327A5 0x02A87E38 0x02A87E7A 0x02BDA9D5
0x02BDAA2F 0x02BDAAD7
```

Total 7,020 bytes of `.text` partition. Every one has `in_degree == 1`. Spread:
`0x018–0x01C` 2, `0x01C–0x020` 27, `0x020–0x024` 21, `0x024–0x028` 5, `0x028–0x02C` 10,
`0x0163D965` alone below that. **Not one pair is adjacent** — there is no range to give.

Worked example, `0x0289EADB` (`sig` 7, `stacky` 0.90, 69 B, one caller `0x01923940`):
```
0289EADB  pushfd
0289EADC  push  ecx
0289EADD  pushfd
0289EADE  mov   dword ptr [esp+4], eax
0289EAE5  popfd
0289EAE6  push  0x3E2FA466          ; junk immediate, immediately overwritten
0289EAEB  mov   dword ptr [esp], edi
0289EAEE  mov   dword ptr [esp], ecx
0289EAF1  push  0x6AB2479B
0289EAF6  mov   dword ptr [esp], edx
...
0289EB0E  lea   esp, [esp-4]
0289EB15  pushfd
0289EB16  jmp   0x0579815D          ; -> .vmp1
```

### ~20 further stub blocks that never got a `functions.csv` row

Segmenting `.text` on runs of ≥4 `int3` and applying the same test to block starts yields 19
blocks (13,096 bytes); walking backward from each protector-targeting rel32 to its block
boundary yields 19 as well, overlapping in 18 — **20 distinct across the two methods, 0 of
which are `functions.csv` entries and 0 of which carry `__FILE__` or literals**:

```
0x00CB0EC0-0x00CB1089  0x01BD50E0-0x01BD59EB  0x01BD59F0-0x01BD5E23  0x01BDEF40-0x01BDF19C
0x01BEBB20-0x01BEBDC1  0x01BEFB10-0x01BEFD63  0x01BEFF80-0x01BF0131  0x01C1CD90-0x01C1CEBB
0x01CBB850-…(a)        0x01D9E1C0-0x01D9E669  0x02243010-0x02243142  0x0226A380-0x0226AC39
0x02279A80-0x02279B38  0x023664E0-0x023667D3  0x026CF630-0x026CF73B  0x02739FB0-0x0273A063
0x02870600-0x028708D1  0x0289EA80-0x0289EB1C  0x028A4DE0-0x028A4ED6  0x02BDA9D0-0x02BDAB65
```
(a) `0x01CBB850` came only from the backward-walk census, `0x02739FB0` only from the forward
segmentation; the other 18 agree. Several overlap the entry list — e.g. `0x02BDA9D0` is the
true start of the block whose `functions.csv` row begins 5 bytes in at `0x02BDA9D5`.

**Grand total protector code inside `.text`: ~20 KB of 46.2 MB = 0.04 %.**

### Everything else that touches `.vmp1` is *mutated game code*, not protector code

13,538 rel32 sites in `.text` point into a protector section. Walking each back to its
enclosing `int3`-delimited block gives 3,241 distinct blocks. **19** match the stub shape;
**3,222 do not** — and of those, 1,555 are `functions.csv` entries, **56 carry a `__FILE__`**
and 416 carry literals. That is VMProtect *mutation*: basic blocks lifted out of ordinary
game functions and replaced by an exit into the VM. The host stays game code.

---

## Cross-checks that refuse to isolate any protector island

- **Connectivity.** Undirected `.text` call graph from `callgraph.csv` (526,525 edges,
  self-loops dropped): **3 weakly connected components** — one of 67,072 containing *all 759*
  `__FILE__` functions, plus two singletons `0x02C4D48E` (`mov edi,edi` hot-patch prologue)
  and `0x02C8FF10` (`push -1; … fs:[0]` SEH frame), both plain MSVC. No protector subgraph.
- **Hop distance to the nearest `__FILE__` function**: 0→759, 1→13,672, 2→44,943, 3→6,842,
  4→684, 5→146, 6→23, 7→3, unreachable→2. The 858 at distance ≥4 are diffuse (105 in
  `0x0040xxxx`, 204 in the CRT band, the rest 1–50 per MB). **All 66 stubs sit at distance
  1–5** — the protector calls them *from* game code, so transitive `__FILE__` reachability
  cannot separate them. This is why the shape test, not the graph, is the discriminator.
- **Anti-debug surface.** `wrapper-apis-a21.csv` contains exactly **2** anti-debug rows, both
  `kernel32!IsDebuggerPresent`, call sites `0x00D95296` (func `0x00D94920`, literal
  `Common/CommonEffect/BuffIcon.img`) and `0x02D5DDE9` (CRT). No
  `NtQueryInformationProcess`, `ZwSetInformationThread`, `CreateToolhelp32Snapshot`,
  `WriteProcessMemory`, or `CreateRemoteThread` wrapper is reachable from `.text` at all.
- **The CRT band `0x02C00000`–`0x03029000`** (1,512 rows, 0 `__FILE__`) is the statically
  linked MSVC CRT plus the NPK/PVF loader, not protector: `DecodePointer` 626, `HeapFree` 578,
  `TerminateProcess`+`GetCurrentProcess` 454, `SetUnhandledExceptionFilter` 296,
  `GetStdHandle` 357; literals `%s : failed to file open`, `[BUILDER][PLAIN] %s : failed to
  unpack`, `[ancient]`/`[dimension]`/`[pvp]`. Its tail `0x03000000`–`0x03029000` is a table of
  16-byte MSVC static-dtor thunks (`mov ecx,<&global>; jmp <dtor>` + 6× `int3`), e.g.
  `0x03028000: mov ecx,0x04E273F0 ; jmp 0x02231720`.

## Dataset caveats found while checking

1. **`span_bytes` is not a function length.** For all 67,073 consecutive pairs,
   `span == next_start - start` exactly — `functions.csv` is a *partition* of `.text`, so a
   span includes trailing `int3` padding and any stub parked in it. Do not read it as a body
   size.
2. **Function discovery stops at `0x02DF3903`.** That last row has `span = 2,316,029`,
   swallowing all of `0x02DF3903`–`0x03029000`. That 2.3 MB contains ordinary `int3`-padded
   MSVC code and the dtor-thunk table — it is *unenumerated*, not foreign.

## Confidence and limits

**Confidence: high** on the negative (no protector VA range in `.text`) and on the patch-site
attributions — each read off `func-source-file.csv` + `literals.csv` + disassembly, with the
RVA-vs-VA question settled by 13/13 byte matches. **High** on the 66-entry stub list (0.13 %
base rate, zero `__FILE__` collisions, hand-verified disassembly on 8 of them).

Limits: the inbound scan catches **direct rel32 only**. `.vmp1` is a VM interpreter that
dispatches indirectly, and `.#|U` (27.6 MB) was byte-scanned rather than capstone-swept, so a
protector→`.text` edge through a computed target would be missed. What I *did* find of that
shape (98 `.tls` mid-function targets, one hit each) matches "resume after an injected check",
not "protector body lives here". The 19-block figure is a lower bound: my segmentation only
finds stubs that *start* an `int3`-delimited block, and 3,361 of the 13,538 rel32 sites had no
`int3` boundary within 4 KB behind them.

**What would falsify this:** a contiguous `.text` VA range whose functions (a) reach `.-wI`
wrappers for `NtQueryInformationProcess` / `ZwSetInformationThread` /
`CreateToolhelp32Snapshot` / `WriteProcessMemory`, (b) form their own connected component or
sit ≥5 hops from every `__FILE__` function, and (c) are entered from `.tls` or `.vmp1`. None
of the three holds anywhere in the image. A single counter-example — one `.text` function with
no `__FILE__` in its transitive neighbourhood that `.tls` *calls at its entry* — would
overturn the negative; the exhaustive scan found zero such entries.

## Artifacts (all under `scratchpad/`)

- `_v0_env.py` — shared loader (sections, functions, callgraph, literals, `__FILE__`, APIs)
- `_v0_in.py` / `_v0_in.pkl` — protector→`.text` rel32 scan, per section
- `_v0_out.py` / `_v0_out.pkl` — `.text`→protector transfers, capstone-validated
- `_v0_bfs.py` / `_v0_dist.pkl` — hop distance to nearest `__FILE__` function; base rates
- `_v0_sig2.py` / `_v0_sig2.pkl` — VMP-signature metrics for every entry
- `_v0_protector_stubs.csv` — **the 66-entry answer**, with sig/stacky/target/in-degree
- `_v0_chunk.py` / `_v0_chunk.pkl` — `int3`-delimited block segmentation, the 19 extra stubs
- `_v0_foot.py` / `_v0_foot.pkl` — 3,241-block census: 19 stub vs 3,222 mutated-game
