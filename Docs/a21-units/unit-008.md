# TU 008 — `DNFClient\RDAR\CNRDAction.cpp`

Span given by the briefing: `0x0266A410` – `0x02673A10`, 133 functions, 0 parsers.
Anchors: `0x0266A410` and `0x02673A10`, both pushing the same `__FILE__` literal
`0x035DE5C0` = `D:\Work\1229\Source\DNFClient\RDAR\CNRDAction.cpp`.

## Verdict in one line

`CNRDAction` is the **runtime instance of one `[MOTION]` block of DNF's ActionScript
(`.act`) object-behaviour script**. It owns the motion's base animation and its
sub-animation layers, draws them, advances them per frame, and runs the motion's
`[TRIGGER]` / condition list against incoming event ids. It is pure client-side
presentation + local scripting. **No packet parsing, no packet building.**

## Evidence chain for the identity

| Fact | Where |
| --- | --- |
| `__FILE__` = `...\RDAR\CNRDAction.cpp` | `0x0266A410`, `0x02673A10` (literal `0x035DE5C0`) |
| A named method: `CNRDAction::checkTriggerOnEvent` | literal `0x035DE630`, referenced at `0x0267456C` (`push 0xE95` = source line 3733) |
| `this+0x60` is a `boost::shared_ptr<CNRDAnimation>` | `0x0266CBC0` calls `0x026617B0` = literal `CNRDAnimation::drawCurrentFrame`; `0x0266D150` calls `0x026602A0` = literal `CNRDAnimation::Proc` |
| `this+0xD4` is a `CNRDObject*` (the owning game object) | `0x02673A6E` calls `0x017623A0`, which `func-source-file.csv` attributes to `DNFClient\RDAR\CNRDObject.cpp` |
| The script language it serves | sibling TUs `CNRDActionBehavior.cpp` (`0x0268A530`, 16 call edges into this unit) and `CNRDActionTrigger.cpp` (`0x026B4440`); loader tag literals `[MOTION]`, `[BASE ANI]`, `[SUB ANI]`, `[SUB ANI WITH XY]`, `[SUB ANI WITH XYZ]`, `[TRIGGER]`, `[/TRIGGER]`, `[BEHAVIOR]`, `[CHARACTERISTIC]` at `0x02BE8A90`; error strings `ActionScript Load Error! - [TRIGGER]태그의 명령어[태그]가 잘못되었음` (`0x02C05678`), `ActionTrigger Load Error - HP: 비교 기호 오류` (`0x02C041E0`) |
| Callers are game objects, not UI | external callers by TU: `CNRDObject.cpp` (29), `CNRDActionBehavior.cpp` (16), `CNRDBaseInterface.cpp` (13), `IRDCharacter.cpp`, `CNRDPassiveObject.cpp`, `CNRidableObject.cpp`, `CNTileGameManager.cpp`, `MovieDirector.cpp` |

RTTI cannot help here: the type descriptors this unit dynamic-casts through
(`0x0395E0EC`, `0x0395E110`, used by `__RTDynamicCast` at `0x0266D169`) have had
their names replaced with sequential stubs `".\x03"`, `".\x04"`, `".\x05"` — the
image's RTTI names are scrubbed.

## 1. What a server implementer needs from this unit

**Almost nothing, and that is the finding.** Concretely:

* `parsers.csv` lists **zero** read-primitive callers in the span. Every S→C field
  extraction funnels through `read_u8/u16/u32/read_bytes`; none of the 133
  functions calls them, directly or otherwise.
* The API-reach table is CRT/heap only (`HeapFree`, `Interlocked*`, `GetTickCount`,
  `MessageBoxW`, `ShellExecuteW`). No socket, no crypto, no serialisation.
* **Two callgraph edges look like networking and are both artifacts — do not
  build on them:**
  * `0x0266B3E0 -> 0x0274D1E0` (`CNGameSocket::MakePacket`). `functions.csv` gives
    `0x0266B3E0` a 192-byte span, but the real function ends at `0x0266B417`
    (`ret 8`). Bytes `0x0266B420`+ are an obfuscator trampoline
    (`jmp 0x05762C18`, then a chain of `push/pop/lea esp` stack shuffles) that
    ends in `call 0x0274D1E0`. It is a separate protected stub swallowed by the
    span, not code in this unit.
  * `0x02671450 -> 0x01169460`. `0x02671450` is `std::vector<T32>::reserve`
    (`cmp eax,0x7FFFFFF` / `_Xlen` at `0x0266FED0` / copy at `0x0266FFA0` /
    destroy at `0x0266FF40`); `0x01169460` is the allocator's `allocate(n)`,
    a COMDAT that happens to live inside the `GameCoreNetwork_NOTI_Etc.cpp` span.

The only server-facing implication is indirect: the monster/object behaviour that
`CNRDAction` triggers locally (summon, create passive object, damage, appendage,
map change — see the behaviour tag list at `0x02C05678`) is **script-driven on the
client**. A server that wants to predict or validate those effects has to read the
same `.act` data, not mirror this code.

## 2. Object layout — `CNRDAction`

Every row below is an offset proven by a specific instruction. The class extends
to at least `+0x564`.

| Offset | Type | Proof |
| --- | --- | --- |
| `+0x3C` | `std::string` (`_Bx` union; `_Myres` at `+0x50`) | `0x0266B520`: `cmp [ecx+0x50],8 ; jb -> lea eax,[ecx+0x3c]` — the classic MSVC SSO string accessor |
| `+0x60` | `boost::shared_ptr<CNRDAnimation>` — **base animation** (`px` at `+0x60`, `pn` at `+0x64`) | `0x0266BF90` copies `this+0x60` through the shared_ptr copy-ctor `0x0121D970`; `0x0266B4F0` does `mov ecx,[esi+0x60] ; call 0x2648590` |
| `+0x68` | `std::vector<SubAni>` — `_Myproxy +0x68`, `_Myfirst +0x6C`, `_Mylast +0x70`, `_Myend +0x74`; **element stride 0x20** | `0x0266AD20`: `([ecx+0x70]-[ecx+0x6C]) >> 5`; `0x0266AD30` indexes `+0x6C + i*0x20`; begin/end called with `ecx = this+0x68` at `0x0266C456`/`0x0266C464` |
| `+0x7C` | `std::list<DrawLayer>` — `_Myproxy +0x7C`, `_Myhead +0x80`, `_Mysize +0x84` | `0x02672C3A`: `cmp [esi+0x84],edi(0) ; jbe` = empty test; `0x02672C7F` `lea ecx,[esi+0x7c]` then `list::begin 0x21B8250`; deref guard `cmp esi,[eax+4]` = "ptr == _Myhead" |
| `+0xD0` | `int`, reset to `-1` each Proc | `0x02672C30`: `mov dword [esi+0xd0], 0xFFFFFFFF` |
| `+0xD4` | `CNRDObject*` — owner | `0x02673A4C` `mov ecx,[ebx+0xd4]` then `call 0x017623A0` (CNRDObject.cpp); vtable slots `+0xC4/+0xC8/+0xCC` used as x/y/z at `0x0266B09E..0x0266B0C5`; slot `+0x1D0` returns a state compared to 2 and 4 at `0x02673A83` |
| `+0x118` | `std::vector<{int,int}>` (stride 8) — event/tick log | `0x02674980`: `lea esi,[ecx+0x118]`, `([esi+8]-[esi+4])>>3`, entries `{GetTickCount, id}` |
| `+0x140` | `int`, written together with the `+0x16E` flag | `0x0266A560`: `mov [ecx+0x16e],al ; mov [ecx+0x140],edx` |
| `+0x144` | `std::vector<Trigger>` — `_Myfirst +0x148`, `_Mylast +0x14C`, `_Myend +0x150`; **element stride 36 (0x24)** | `0x0266AD60`: `([ecx+0x14C]-[ecx+0x148]) * 0x38E38E39 >> 3` = /36; index math `lea edx,[eax+eax*8] ; ...[esi+edx*4+…]` at `0x0266B3CA` |
| `+0x158` | second `std::vector<Trigger>` (`+0x15C` first, `+0x160` last), same /36 stride | `0x0266AD80`, used by `0x0266D6A0` and `0x0266D9A0` |
| `+0x16D` | `bool` | setter `0x0266A540`, read in `0x0266B4F0` |
| `+0x16E` | `bool` | getter `0x0266A550`, setter `0x0266A560` |
| `+0x12C..+0x130` | five `bool` flags | getters `0x0266A600/610/620/630/660` |
| `+0x3C4`,`+0x3C8` | two `int`, set as a pair | `0x0266A5C0` |
| `+0x3EC` | `std::vector<…>` | `0x0266DD30`: `add ecx,0x3ec` → begin `0x0065F2A0`, end `0x0293B470` |
| `+0x450`,`+0x454` | two `int`, set as a pair, read individually | setter `0x0266A580` (9 external callers); getters `0x0266A5A0` (15) and `0x0266A5B0` (11). `0x0266BFF2` special-cases `[esi+0x450] == 0xA` |
| `+0x4B8` | `int`, cycles 0→1→2→0 | `0x0266A8A0`: `inc [edi+0x4b8] ; cmp …,2 ; jle` else reset to 0 |
| `+0x4BC` | embedded sub-object (address returned) | `0x0266A5E0`: `lea eax,[ecx+0x4bc]` |
| `+0x4E8` | `int` setter | `0x0266A5F0` |
| `+0x4EC` | `int` getter | `0x0266A530` |
| `+0x4F4` | `bool` getter | `0x0266A650` |
| `+0x4F5` | `bool` — "ignore flip" | `0x0266CBC0` head: if set, the caller's flip argument is forced to 0 |
| `+0x508` | pointer to a box/extent struct (`+0x04`,`+0x0C`,`+0x14`,`+0x1C`,`+0x20`,`+0x24`) | `0x0266B080`, `0x02670610`, `0x02671FA0` |
| `+0x564` | field used by `0x0266DD30`, `0x026702B0` | — |

### `Trigger` record — 36 bytes (0x24)

| Offset | Meaning | Proof |
| --- | --- | --- |
| `+0x00` | `std::vector<Condition>` — proxy `+0x00`, first `+0x04`, last `+0x08`, end `+0x0C`; **element stride 0x40** | `0x0266A958`: `ecx=[edi+4] ; eax=[edi+8] ; sub ; sar esi,6` (÷64). In `checkTriggerOnEvent` begin/end are called with `ecx = &trigger` (`0x02673C24`) |
| `+0x14` | `int` fire count | `0x0266B4DF`: `inc dword [esi+eax*4+0x14]` (element-relative `+0x14`) |
| `+0x18` | `int` max fire count (`<=0` = unlimited) | `0x02673B77`–`0x02673B81` |
| `+0x1C` | `bool` enabled | getter `0x0266B3A0` (`mov al,[esi+edx*4+0x1c]`), setter `0x0266B3E0` (`mov [esi+edx*4+0x1c],al`), gate at `0x02673B97` |
| `+0x1D` | `bool` "only fires on event `0x10`" | `0x02673B87`: `if ([esi+0x1d] && event != 0x10) skip` |

### `Condition` record — 64 bytes (0x40)

| Offset | Meaning | Proof |
| --- | --- | --- |
| `+0x00` | `int` condition/trigger **type code**, range 0…0x68 | `0x02673CA7 mov eax,[edi]` → `cmp eax,0x68 ; ja default ; movzx ecx,byte [eax+0x267484C] ; jmp [ecx*4+0x2674810]` |
| `+0x2C` | `std::vector<int>` parameter list — proxy `+0x2C`, first `+0x30`, last `+0x34`, end `+0x38` | `0x02673D41 lea ecx,[edi+0x2c] ; call 0x009B89D0` with an index argument; `0x0266A984` reads `[ecx+…+0x34] - [ecx+…+0x30] >> 2` = param count |

### `SubAni` record — 32 bytes (0x20), vector at `this+0x68`

| Offset | Meaning | Proof |
| --- | --- | --- |
| `+0x00` | `shared_ptr<CNRDAnimation>` (`px +0x00`, `pn +0x04`) | `0x0266EE10` copies element base through `0x0121D970`; destroy loop `0x0266FF40` does `lock xadd [p+4]`, vcall `[vptr+0]`, `lock xadd [p+8]`, vcall `[vptr+4]` — textbook `boost::detail::sp_counted_base` |
| `+0x0C` | `int` start/link frame | `0x0267247F cmp [edi+0xc],eax` compared against the base animation's current frame |
| `+0x1C` | `bool` | `0x02672479`, `0x0267248F`, `0x02672D57` |

### `DrawLayer` list node at `this+0x7C` (node `+0x00 _Next`, `+0x04 _Prev`, value from `+0x08`)

Node-relative offsets as they appear in the disassembly:
`+0x08` int (drawn only when `< 0`), `+0x0C` `CNRDAnimation*`,
`+0x14` int x-offset, `+0x18` int y-offset, `+0x1C`/`+0x1D` bools
(`0x0266CC65`, `0x0266CC9B`, `0x0266CCCF`, `0x0266CCFB`, `0x0266CD0E`).

## 3. The trigger state machine — `CNRDAction::checkTriggerOnEvent` (`0x02673A10`)

Signature recovered from its constant-argument thunks (thiscall, 4 stack args):

```
bool CNRDAction::checkTriggerOnEvent(void* a, int eventId, int c, int d)
```

Thunks immediately after the span pin the event ids:

| Thunk | Pushes | Event id |
| --- | --- | --- |
| `0x02674A70` | `push 0; push 0; push 0x12; push 0` | `0x12` |
| `0x02674A80` | `push 0; push 0; push 0x13; push 0` | `0x13` |
| `0x02674A90` | `push 0; push 0; push 0x14; push 0` | `0x14` |
| `0x02674AA0` | `push edi; push edi; push 0x0B; push eax` | `0x0B` |

**Entry gates** (all cause an immediate `false`):

1. `[this+0xD4] == 0` → bail (`0x02673A66`).
2. `CNRDObject` predicate `0x017623A0` returns 0 → bail.
3. `owner->vf[0x1D0]() == 2` or `== 4` → bail (`0x02673A8B`, `0x02673AA4`) — two
   object states in which triggers never fire.
4. trigger vector empty → bail (`0x02673ADF`).

**Per-trigger gate** (`0x02673B77`–`0x02673BA7`), in order:

```
if (t->maxCount > 0 && t->fireCount >= t->maxCount)  continue;   // +0x18 / +0x14
if (t->onlyEvent10 && eventId != 0x10)               continue;   // +0x1D
if (!t->enabled)                                     continue;   // +0x1C
matched = (eventId == 0x10);                                     // event 0x10 self-satisfies
```

**Per-condition event matching** (`0x02673CA7`–`0x02673D15`): a condition whose
type is one of

`0x07, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x64, 0x77`

is treated as an *event* condition and compared against `eventId`, with two
deliberate cross-matches:

* type `0x0C` also matches `eventId == 0x0B`
* type `0x0B` also matches `eventId == 0x0C`
* otherwise `type == eventId`

On match, `matched = true` (`0x02673D15`).

**Condition dispatch table.** `type` (0…0x68) → byte index at `0x0267484C` →
address at `0x0267484C`'s jump table `0x02674810` (15 cases). Every type not
listed below maps to case 14 = default `0x02674775`.

| case | handler | condition types that reach it |
| ---: | --- | --- |
| 0 | `0x02673DF8` | `0x00`, `0x30`, `0x43` |
| 1 | `0x02674733` | `0x01` |
| 2 | `0x026746F1` | `0x08` |
| 3 | `0x0267429D` | `0x0C` |
| 4 | `0x026742AE` | `0x23` |
| 5 | `0x02674792` | `0x24` |
| 6 | `0x02673D33` | `0x2C` |
| 7 | `0x02674349` | `0x35`, `0x36`, `0x37`, `0x38`, `0x39` |
| 8 | `0x0267430D` | `0x3A`, `0x3B`, `0x3C` |
| 9 | `0x02673DD9` | `0x40` |
| 10 | `0x02674274` | `0x41` |
| 11 | `0x02673D8D` | `0x5F` |
| 12 | `0x026743E9` | `0x64` |
| 13 | `0x02673FC6` | `0x68` |
| 14 | `0x02674775` (default) | everything else in 0…0x68 |

Two handlers I could read far enough to characterise:

* **case 11 / type `0x5F`** (`0x02673D8D`): requires `eventId == 0x10`, then
  `call dword [0x03029608]` and tests `[condition+0x20] == -1` — a time-gated
  condition.
* **case 13 / type `0x68`** (`0x02673FC6`): resolves parameters, then at
  `0x026740F6` formats the key `"%d_trigger_%d%d%d%d"` (literal `0x035DE6B4`)
  from `owner->vf[0x4C]()` plus four resolved values, looks it up in the global
  registry at `0x04C6D2DC` via `0x028568D0`, and if absent allocates a `0x2C8`-byte
  object. i.e. a **per-(object,trigger) singleton keyed by name** so the same
  trigger cannot instantiate twice.

**Debug trace.** `0x0267455E` logs `타겟 %d %d -- 전 %d 새로운 %d` ("target %d %d --
old %d new %d", literal `0x035DE678`) tagged `CNRDAction::checkTriggerOnEvent`,
`CNRDAction.cpp` line `0xE95` = 3733. So the trigger machinery re-targets the
action and reports the transition.

## 4. Two script-language primitives worth writing down

### 4a. Parameter resolver — `0x0266A7B0(paramVector, int* cursor)`

Reads one ActionScript parameter, advancing `*cursor`. `0x02BE1370(vec, i)` is the
raw `param[i]` fetch; `0x022D2DD0(0)` is the RNG.

```
tok = param[(*cursor)++];
tok == 1 : lo = param[(*cursor)++]; hi = param[(*cursor)++];
           if (lo == hi) return lo;
           return  min(lo,hi) + ( rand() % abs(hi-lo) )      ; 0x0266A80A..0x0266A82F
tok == 2 : n = param[(*cursor)++];
           k = rand() % n;  v = param[*cursor + k];  *cursor += n;  return v
tok == 3 : return 0
tok == 4 : obj = cast(this->owner);  if (!obj) return -1;
           v = obj->m4(this->m_cursor4B8);  this->m_cursor4B8 = (m_cursor4B8+1) % 3;  return v
tok == 5 : same, different method (0x018A8460)
```

`tok == 1` is `[RANDOM]` min/max, `tok == 2` is random-pick-from-list. Exact
rounding matters if anything ever has to reproduce it: the range form is
`min + rand() % |hi-lo|`, i.e. **`hi` is exclusive** and a `lo == hi` pair
short-circuits.

### 4b. Integer expression-tree evaluator — `0x0266A410`

The anchor function. Recursive, thiscall, node layout:

```
+0x00 left child, +0x04 right child, +0x08 leaf value, +0x0C opcode
```

Descent (`0x0266A413`–`0x0266A430`) skips single-child chain nodes; a node with
both children null returns `[node+8]`. Opcode dispatch is a direct 5-entry jump
table at `0x0266A510` (verified by reading the raw dwords):

| op | handler | body |
| ---: | --- | --- |
| 0 | `0x0266A446` | `left + right` (`add eax,edi`) |
| 1 | `0x0266A45C` | `left - right` (`sub eax,edi`) |
| 2 | `0x0266A472` | `left * right` (`imul eax,edi`) |
| 3 | `0x0266A489` | evaluates `right`; if `right == 0`, `ASSERT(false)` at CNRDAction.cpp line **4046** (`push 0xFCE`) and return 0 |
| 4 | `0x0266A4A5` | evaluates `right`; if `right == 0`, `ASSERT(false)` at line **4055** (`push 0xFD7`) and return 0; else `cdq ; idiv edi ; mov eax,edx` = `left % right` |

`op > 4` returns `-1` (`0x0266A505`).

**Anomaly, reported as observed, not explained.** Op 3 is unmistakably the
division case — it is the divide-by-zero guard, one `ASSERT` slot above the
modulo case — but after the guard passes it does **`75 B1` = `jne 0x0266A446`**,
which is the *addition* body, not a division. There is no `idiv`-returning-`eax`
body anywhere in the function. Bytes at `0x0266A489`:
`8B 4E 04 | E8 7F FF FF FF | 85 C0 | 75 B1`. Either the shipped client computes
`a + b` for the `/` operator in ActionScript expressions, or op 3 is not division
at all. I could not resolve which; the ActionScript loader that assigns the
opcode is outside this TU.

The node destructor is `0x0266ACD0` (recursively frees `[+0]` and `[+4]`, then
`scalable_free`), wrapped by `0x0266A670`; `0x0266AC90` zero-initialises a
0x48-byte node.

## 5. Public surface

97 of the 133 functions have callers outside the span; 361 distinct external
callers overall. The ones that carry real behaviour:

| VA | distinct external callers | role (with the access that proves it) |
| --- | ---: | --- |
| `0x0266BF90` | 114 | `getBaseAnimation()` → `shared_ptr` copy of `this+0x60` via `0x0121D970`. The single most-used entry point in the unit. |
| `0x02672BF0` | 67 | per-frame `Proc`: pokes the base animation (`0x0265C200`), resets `[this+0xD0] = -1`, walks the `+0x7C` layer list and the `+0x68` sub-ani vector building a working index list |
| `0x0266CBC0` | 51 | `draw(x, y, flip, arg)`: walks the `+0x7C` list, calls `CNRDAnimation::drawCurrentFrame` (`0x026617B0`) per layer at `x ± layer.dx`, `y + layer.dy`; `[this+0x4F5]` forces `flip = false` |
| `0x0266FED0` | 40 | `vector<SubAni>::_Grow` capacity policy (`_Xlen` at `0x1FFFFFFF`, growth `size + size/2`) |
| `0x0266EE10` | 29 | `getSubAnimation(i)` → `shared_ptr` copy from `+0x6C + i*0x20`, empty `shared_ptr` if out of range |
| `0x0266AD20` | 18 | `getSubAnimationCount()` = `([+0x70]-[+0x6C]) >> 5` |
| `0x0266BAC0` | 16 | iterator `operator++` for a 0x38-stride container |
| `0x0266A5A0` / `0x0266A5B0` / `0x0266A580` | 15 / 11 / 9 | get/get/set the `+0x450`,`+0x454` int pair |
| `0x0266B4F0` | 14 | 3-term predicate on the base animation: `!0x2648590() && 0x2648D20() && this->m_16D` |
| `0x02672420` | 13 | `setFrame(f)`: reads the base animation's current frame (`0x02648500`), then re-syncs every `+0x68` sub-ani whose `+0x0C` start-frame and `+0x1C` flag qualify |
| `0x0266D150` | 13 | `setOwner(obj)`: `__RTDynamicCast` the argument, store to `this+0xD4`, push it into the base animation (`0x02655680`), then call `0x0266C430` with a float pulled from `obj->vf[0x28C]()` |
| `0x02673A10` | 11 | `checkTriggerOnEvent` — section 3 |
| `0x0266BD80` | 8 | forwards a value to the base animation (`0x02654E90`) then applies it across the `+0x68` sub-ani vector |
| `0x0266A7B0` | 7 | ActionScript parameter resolver — section 4a |
| `0x0266C660` / `0x0266C430` | 6 / 5 | float setters that fan out to the base animation (`0x02651060`, `0x0265D3F0`) and the sub-ani vector — playback-rate / scale style |
| `0x0266B3A0` / `0x0266B3E0` | 3 / 1 | `isTriggerEnabled(i)` / `setTriggerEnabled(i, b)` on `trigger[i]+0x1C` |
| `0x0266B4A0` | 3 | `increaseTriggerFireCount(i)`: `inc trigger[i]+0x14`, returns `&trigger[i]` |
| `0x0266AD60` / `0x0266AD80` | 2 / 1 | element counts of the two 36-byte trigger vectors (`/36` via `0x38E38E39`) |
| `0x0266A410` | 1 | expression evaluator — section 4b |

Roughly a third of the span is compiler-generated STL support for the unit's own
containers and should not be mistaken for logic: `0x0266FED0` (`_Grow`),
`0x0266FF40` (destroy range of 0x20-byte shared_ptr elements),
`0x0266FFA0` / `0x0266AF80` (uninitialised move/copy), `0x02671450` (`reserve`),
`0x0266BAC0` / `0x0266BB10` (iterator `++` for 0x38- and 0x48-stride containers),
plus a long tail of one-caller vector helpers in `0x02670000`–`0x02671FFF`.

## 6. Span assessment

The lower bound is right: the previous anchor, `CNRDAnimation.cpp`, sits at
`0x02666D20`, and `0x0266A410` is the first `CNRDAction.cpp` function.

**The upper bound is too narrow.** The briefing's `hi` is the *start* of the last
anchor function, which itself runs to `0x026748C0`; and the file plainly continues
past that. The four constant-argument thunks that call `checkTriggerOnEvent`
(`0x02674A70`, `0x02674A80`, `0x02674A90`, `0x02674AA0`) and three larger callers
(`0x02674D80`, `0x02676170`, `0x026766A0`, `0x02676EE0`) all live above the cut and
manipulate the same object (`[ecx+0x118]`, `[ecx+0xD8]`, `[ecx+0x11C]`). The next
anchored TU is `CNRDActionBehavior.cpp` at `0x0268A530`, leaving ~87 KB unclaimed;
CNRDAction.cpp very likely owns at least `0x026748C0`–`0x02677000` of it. The
`__FILE__` line numbers agree the file is large: the two asserts are at lines 4046
and 4055, and the trace at 3733.

Nothing inside the span contradicts the file name — the literals are only the two
`__FILE__` copies plus `false`, `CNRDAction::checkTriggerOnEvent`,
`%d_trigger_%d%d%d%d` and the Korean target-change trace, all of which are
consistent.

## 7. Confidence

**High** on: the class identity, the trigger/condition state machine and its gates,
the dispatch table contents, the parameter resolver, and every offset in the layout
tables (each is backed by a cited instruction).

**Medium** on: the *semantics* of individual condition type codes beyond types
`0x5F` and `0x68` — I read the dispatch, not all 15 handler bodies; and on the
`+0x450`/`+0x454` pair, whose meaning I can locate but not name.

**Explicitly unknown**: what op 3 of the expression evaluator is meant to be; the
scrubbed RTTI class names; the meaning of event ids `0x0B`, `0x10`, `0x12`, `0x13`,
`0x14` in ActionScript's own vocabulary (the tag→code mapping lives in the loader
around `0x02BE8A90` / `0x02C05678`, outside this TU).
