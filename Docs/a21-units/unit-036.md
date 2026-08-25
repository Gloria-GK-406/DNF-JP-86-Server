# TU 36 — `DNFClient\RDAR\CNSelectCharacterModule.cpp`

Briefing span `0x026DBBF0 – 0x026F5050`. **The span is too narrow — see "Span is wrong" below.**
Real module: approximately `0x026DB1F0 – 0x026FA070`.

---

## 1. What this unit implements

The **character-select / character-create screen** as a game *module* (one of the
`CN*Module` state objects: it has `onEnterModule`, `Proc`, `DrawProc`, and a
`m_mode` field dispatched to `procModeSelectCharacter` / `procModeCreateCharacter`).

It is the **client side of the character list**: it owns the received character
array, drives the paged fetch of that array, renders the slots, validates the
player's choice locally, and emits the packets that start the game
(`SELECT_CHARACTER`) or create a character. It does **not** parse anything —
`parsers = 0` is correct. The character-list packet is parsed in
**TU 32 `CNRDUserInfoPack.cpp` / `CNRDUserInfoPacketHandler.cpp`** (`0x013E8AF0`),
which fills this module's vector, sets its total-count field, and then calls
`onReceiveCharacterInfo` here.

It also owns a secondary feature bolted onto this screen — the "Link System"
(`Etc/LinkSystem/CharacLinkSystem.etc`, `UI/SelectCharacter/Link_system/*.ani`,
sound cues `LICKSYS_ON/_OFF/_LOOP`, `processCharacterLink`) — which has its own
three-opcode conversation with the server.

---

## 2. Packets this unit sends (highest value for a server implementer)

Send primitives (in `DNFClient\RDAR\CNGameSocket.cpp`, TU 5; identified from
their own `__FUNCTION__` literals and body):

| VA | role | proof |
|---|---|---|
| `0x0274CA50` | `GetGameSocket()` → returns `[0x3B7CEE4]` | `mov eax,[0x3b7cee4]; ret` |
| `0x0274D1E0` | `CNGameSocket::MakePacket(WORD cmd, BYTE flag)` | literal `CNGameSocket::MakePacket` at `0x0274D1E0` |
| `0x0274D2E0` | write 1 byte | `inc [ecx+0x2BCC2C]; push 1` |
| `0x0274D310` | write 2 bytes | `add [ecx+0x2BCC2C],2; push 2` |
| `0x0274D340` | write 4 bytes | `add …,4; push 4` |
| `0x0274D370` | write 8 bytes | `add …,8; push 8` |
| `0x0274D3A0` / `0x0274D4A0`→`0x0274D410` | write **string**: `u32 len` (strlen, no NUL) then `len` bytes | `0x0274D42B` strlen loop, `0x0274D439` writes the 4-byte length, `0x0274D474` writes the body |
| `0x0274CED0` | write raw N bytes, **no length prefix** | `add [ecx+0x2BCC2C], len; push len; push buf` |
| `0x0274E6D0` | `Send` | literals `TCP_SEND`, `error : ENUM_NEOPLE_FAULT_NOT_CONNECT` |

Calling convention at every site: `push flag; push cmd; call GetGameSocket; mov ecx,eax; call MakePacket` — so **the last push before `MakePacket` is the command id**.

### Command table

| cmd | sender | body | notes |
|---:|---|---|---|
| **0x0004** | `0x026E61E0` `requestSelectCharacter` | `u16 slotIndex` | The log line immediately before the send is `ENUM_CMDPACKET_SELECT_CHARACTER slotIndex[%d]` (fmt at `0x035E20A0`, pushed at `0x026E6913`), then `MakePacket(4,0)` at `0x026E6957`, `writeWORD(esi=slotIndex)` at `0x026E6964`, `Send` at `0x026E6969`. **This is the only sender of cmd 4 in the image.** |
| **0x0005** | `0x026DD020` (create character) | `u8 job` ; `str name` ; `u8 g` ; `u8 g` ; `u8 flag` ; `u8 0` ; `u8 0` | `MakePacket(5,0)` at `0x026DD032`. Fields, in order: `[this+0x1A4]` (`0x026DD037`); the name from the edit control `[this+0x28C]`→vtbl+0xDC (`0x026DD04B`, written with the **string** writer `0x0274D4A0`); then `byte [0x397D9D4]` **twice** (`0x026DD068` `8A 0D D4 D9 97 03` and `0x026DD081` `A0 D4 D9 97 03` — byte-verified, genuinely the same global twice); then `([0x3A3BF18]+4)!=0` as a bool (`0x026DD099`); then two literal zero bytes. On send it latches `[this+0x2BD]=1`. |
| **0x0127** | helper `0x026DB860`; also `0x026DD480` and inlined at `0x026EDE5C`/`0x026EDEB1` | `u32 a` ; `u32 b` | Link-system step report. In `processCharacterLink` it is sent twice with *different* values: first `(byte[this+0x721], 0)`, then it does `if ([this+0x720] < [this+0x721]) [this+0x720]++` and sends `([this+0x720], 0)`. In `0x026DD480` it is sent **twice with identical payload** `(arg, charCount-1)` — byte-verified at `0x026DD480` (`…6a006827010000…` twice). A server must tolerate the duplicate. |
| **0x0138** | `0x026DD3A0` | `u8 [this+0x720]` ; `u8 [this+0x721]` ; `u8 [0x3A45790]` ; `u8 [0x3A45794]` | Link-system start; sets `[this+0x71C]=2`, `[this+0x727]=0`. |
| **0x0139** | `0x026DE0B0` | `u8 [this+0x720]` ; `u8 [this+0x721]` | Immediately followed by `0x026DD480([this+0x720])` → two more 0x127. |
| **0x019F** | `0x026DF2E0` | *(empty)* | Fired once at the end of the slot-draw loop, latched by `byte [0x3B7AFFC]`. `MakePacket(0x19F,0)` at `0x026DF560`, `Send` at `0x026DF565` with nothing in between. |
| **0x02A8** | `0x026DBE30`, `0x026DD9C0`, `0x026DDA50` | `u16 startIndex` | **Request a page of the character list.** See §3. |
| **0x02B5** | helper `0x026DB830`; caller `0x026E80E0` | `str name` | Check-name-availability. Gated on `[this+0x2BD]==0` and on the local validator `0x026E7E20`; latches `[this+0x2BD]=1`. |
| **0x02C1** | `0x026E9CF0` | `u8 slotIndex` | Sent from the slot UI; guarded by `[this+0x908]==slotIndex`, `slotIndex < charCount`, and the in-flight latch `[this+0x97C]` (set to 1 right after). |
| **0x04D7** | `0x026F5220` (`onEnterModule`) | **raw 22 bytes**, no length prefix | Loading-time telemetry. Buffer is `[ebp-0x1C]`, written with `0x0274CED0(buf, 0x16)`. Only two fields are ever stored: `u32` at offset **0x0E** (value **1** at `0x026F54E0`, value **2** at `0x026F5533`) and `u32` at offset **0x12** = elapsed ticks (`0x026F54D4` / `0x026F5527`). **Bytes 0x00–0x0D are never written in this path — they are uninitialised stack.** Each variant is latched send-once by `[0x3A388A0]` / `[0x3A388A1]`. `CNTownModule.cpp` (`0x027090E0`) sends 0x4D7 too. |

---

## 3. The character list is paged — 36 per page

Two counters:

* `charCount   = ([this+0x8C] - [this+0x88]) >> 2`   (received so far)
* `pendingCount= ([0x3B7B24C] - [0x3B7B248]) / 20`   (magic `0x66666667`, `sar edx,4`)
* `totalCount  = WORD [this+0x876]`                  (**server-reported total**)

`0x026DBE80` is `bool hasAllCharacters()` → `(pendingCount + charCount) >= totalCount`.

`0x026DDA50` is the prefetch loop:

```
026DDA93  mov eax,0x38E38E39 ; imul ecx ; sar edx,3     -> charCount / 36
026DDAA0  lea ecx,[edi-1]    ; same magic, sar 3        -> (totalCount-1) / 36
026DDAC0  lea ecx,[esi+esi*8]; add ecx,ecx; add ecx,ecx -> page * 36
026DDACA  cmp ebx,0x24 ; jb skip                        -> never request index 0
026DDAD4  push 0x2A8 ... writeWORD(page*36) ... Send
```

(`0x38E38E39` with `sar 3` is the signed-divide-by-**36** magic, not by 9.)

So: **the server delivers characters in pages of 36. Page 0 arrives unsolicited;
the client asks for later pages by sending cmd `0x2A8 { u16 startIndex }` where
`startIndex` is a multiple of 36.** `0x026DD9C0` sends the same packet with
`startIndex = charCount`, and `0x026DBE30` with `startIndex = WORD [this+0xAC]`
(the index the user has scrolled to) — both also only when `>= 36`.
All three first clear `[this+0x80]` (a "request pending" gate) and require
`[this+0xB0] == 0` (no selection in flight).

`WORD [this+0x876]` has exactly one writer in the whole image:
`0x026DBA10` (`mov ax,[ebp+8]; mov word ptr [ecx+0x876],ax`), and its **only**
caller is `0x013E8AF0` in TU 32 — the character-list packet handler. So a server
implementer must send the account's total character count in that packet before
the client will stop paging.

---

## 4. `requestSelectCharacter` — the client-side refusal ladder

`0x026E61E0`, `__thiscall`, `ret 0xC` → `(int slotIndex, bool force, bool arg3)`.
Called from `onReceiveCharacterInfo` (rejoin/auto-select), the start-game button
`0x026E6BE0`, and externally from `GameCoreNetwork_CMD_Etc.cpp` / `NOTI_Etc.cpp`.

In order, the client **refuses locally without sending anything**:

| VA | condition | outcome |
|---|---|---|
| `0x026E6210` | `byte [this+0x818] != 0` | silent return (list refresh pending) |
| `0x026E6226` | `!force && byte [this+0xB0] != 0` | silent return (selection already in flight) |
| `0x026E6233` | `slotIndex >= charCount` or `slotIndex < 0` | silent return |
| `0x026E62FC` | protected global `[0x3A5AFFC]/[0x3A5B000] == 2` **and** `slot.level >= 0x33 (51)` | UI window `0x2F7`, no packet |
| `0x026E6399` | `0x026DEFC0(slot.name)` true — name hits the `Etc/InvalidName.etc` / `Etc/SlangName.etc` dictionaries (loaded at `0x026DB760`, iterated from `[0x3B7B194]` / `[0x3B7B180]`) | UI window `0x131` with `(slotIndex, 0)` |
| `0x026E63C5` | `slot.field_0x1C == 8` | UI window `0x131` with `(slotIndex, 2)` |
| `0x026E63D7` | `slot.field_0x3BD != 0 && [this+0x2F4]==0` | stores `[this+0x2F0]=slotIndex`, opens window `0x263` (message id `0xC0F7`), no packet |
| `0x026E6688` / `0x026E68ED` | equipment/inventory sanity walks (`vtbl+0x80 > 2`, `vtbl+0x30 == 0x21`, protected level vs `vtbl+0x58`/`vtbl+0x54`, `slot.field_0x3E0 == 1`) | message ids `0xC178` / `0x7BBA` in window `0x209` |

Only past all of that does it log, send cmd 4, and set state:

```
026E6985  mov edx,[ebx+0x2C4] ; mov [0x3995F2C],edx    ; remember current page
026E6992  mov byte [ebx+0xB0],1                        ; selection in flight
026E6A26  mov [0x39855F0],ecx  where ecx = slot.field_04 ; "current job"
026E6A5E  read protected byte slot+0x39C/+0x3A0 -> if set, pass 0x7FFFFFFF onward
```

This matters operationally: a rejected `SELECT_CHARACTER` path is mostly not
constructible against a server, because the client filters first.

### Rejoin / auto-select (server-visible behaviour)

At the tail of `onReceiveCharacterInfo` (`0x026EDCBD`):

```
if ([0x3A5BE80] == 5 && [0x39846A0] != -1) {
    log "[REJOIN] 캐릭터선택창 - 자동 캐릭터 접속시도 (%d)"
    requestSelectCharacter([0x39846A0], /*force=*/1, 0);     // 0x026EDD29
}
```

So after a disconnect the client re-sends `SELECT_CHARACTER` for the same slot
**immediately** on receiving the character list, with no user input. Earlier in
the same function (`0x026ED71A`) there is a second auto-select path keyed on
`[this+0x9F4]`. Before that, a persisted "disconnect info" triple is read from
the config file and logged as
`[REJOIN] 캐릭터선택창 - 팅 정보 확인 (%d, %d, %d)` (`0x026EDAC0`), and compared
against two protected globals `[0x3A5AFD8]/[0x3A5AFDC]` and `[0x3A5AFE4]/[0x3A5AFE8]`.

---

## 5. Object layouts

### 5.1 `CharacterInfo` — element of `this+0x98`, **sizeof = 0x3E8 (1000)**

Proof of size/stride: `0x026DBAA0` is `vector<CharacterInfo>::at(i)`:
`(end-begin)` divided by 1000 (`0x10624DD3`, `sar edx,6`) for the bounds check,
then `imul eax,eax,0x3E8; add eax,esi`.

| offset | type | access that proves it |
|---:|---|---|
| `+0x000` | `wchar_t*` **name** | `0x026E0320 mov ebx,[eax]` then pushed as the `%s` of `"▲Lv.%d %s"` (`0x026E038B`); `0x026DEFC0` does a `wcslen` on the same pointer |
| `+0x004` | `u32` job/class id | `0x026E6A13 mov ecx,[eax+4]`; stored to global `[0x39855F0]`, which `0x026F523E` compares against `0xE` |
| `+0x008` | `u32` | `0x026E67A0 mov edx,[eax+8]`; passed with `+0x04` to `0x02741BC0(job, x)` |
| `+0x014` | protected-int **key handle** | `0x026E032E lea ecx,[eax+0x14]` |
| `+0x018` | protected-int **ciphertext** = **level** | the `and eax,0xF` / 13-way dispatch to `0x027BC0F0/160/1D0/240/390` reads `[ecx+4]`; decrypted value is the `%d` of `"Lv.%d %s"` (`0x026E0386`) and is compared `>= 0x33` at `0x026E6369` |
| `+0x01C` | `u32` state; **8 = blocked** | `0x026E63C5 cmp dword [eax+0x1c],8`; written `= 8` at `0x026DE7FE` |
| `+0x36C` | ptr | `0x026E9778 mov ecx,[esi+edi+0x36c]` |
| `+0x379`,`+0x37A` | bytes | `0x026EE11C` sets `0xFF`, `0x026EE150` sets `0`; read at `0x026E5ED0` |
| `+0x38E` | byte, sentinel `0xFF` | `0x026EC182 cmp byte [ebx+edi+0x38e],0xff` |
| `+0x39C`/`+0x3A0` | protected **byte** (handle/ciphertext) | `0x026E6A56`–`0x026E6A64` via `0x027BC2B0` |
| `+0x3A4`/`+0x3A8` | protected pair | `0x026EEF17`/`0x026EEF1E` |
| `+0x3BD` | byte flag | `0x026E63D7 cmp byte [eax+0x3bd],0` |
| `+0x3BF` | byte, `==1` | `0x026E2BE7` |
| `+0x3E0` | `u32`, `==1` | `0x026E68E4 cmp dword [eax+0x3e0],1` |

The protected fields use the image-wide encrypted-scalar accessors
(`0x027BC0F0/160/1D0/240/390` for dwords, `0x027BC2B0` for bytes), selected by
`(address & 0xF)` through a jump table; the handle is at `[obj]`, the ciphertext
at `[obj+4]`.

### 5.2 Module (`this`) fields, only those with behavioural proof

| offset | meaning | proof |
|---:|---|---|
| `+0x04` | one-shot init flag | `0x026F507C`, `0x026F55E2` set 1 |
| `+0x70` | **mode**: `0` = select, `1..3` = create | setter `0x026EB86E`; dispatch `0x026F3339` (`==0` → `procModeSelectCharacter 0x026F3170`… via `0x026F3417`) and `0x026F343C`–`0x026F3446` (`1/2/3` → `procModeCreateCharacter 0x026ECBE0`) |
| `+0x80` | byte, "may request next page" | `0x026DBE30`, `0x026DDA29`, set 1 at `0x026ED583` |
| `+0x84` | `std::vector<u32>` header (`+0x88` first, `+0x8C` last) → **character count** | used everywhere as `([+0x8C]-[+0x88])>>2` |
| `+0x98` | `std::vector<CharacterInfo>` header (`+0x9C` first, `+0xA0` last) | `0x026DBAA0` |
| `+0xAC` | current/target character index | `0x026DBE38` (word), `0x026ED649` (dword vs `-1`), `0x026E6C23`, `0x026E6DEF` |
| `+0xB0` | byte, selection in flight | set `0x026E6992`, gate `0x026E6226`, cleared `0x026F5313` |
| `+0x1A4` | byte, selected job for create | `0x026DD037` |
| `+0x28C` | name edit-control ptr | `0x026DD04B`, `0x026E8119` |
| `+0x2BC` | byte, "list already received" | `0x026ED57D`, cleared `0x026F50C5` |
| `+0x2BD` | byte, create/name-check in flight | set `0x026DD0E2` and `0x026E81A3`, gate `0x026E810C` |
| `+0x2C4` | current page index | `0x026E0252`, `0x026E6985` |
| `+0x2F0`/`+0x2F4` | pending slot + gate for window `0x263` | `0x026E63E4`–`0x026E63F1`, cleared `0x026F5469` |
| `+0x60C`,`+0x610` | hovered index / pixel scroll | `0x026E0227`, `0x026E02A9` |
| `+0x71C` | link-system state (`2`,`4`,`7`,`9` observed) | `0x026DD3FF`, `0x026F52B6`, `0x026E84CC`, `0x026EE02B` |
| `+0x720`/`+0x721` | link step current / target (bytes) | `0x026DD415`/`0x026DD429`, `0x026EDE75`/`0x026EDE9B` |
| `+0x722`,`+0x723` | loop-sound latch | `0x026F0019`–`0x026F0066` |
| `+0x724`,`+0x725`,`+0x726` | link visual sub-states | `0x026F52D1`, `0x026E848B` |
| `+0x7AC` | UI root window | `0x026E02C3`, `0x026F39D2` |
| `+0x818` | byte, "list refresh pending", blocks selection | gate `0x026E6210`, cleared `0x026EDA74` |
| `+0x840` | 4 UI element ptrs, stride 8 | `0x026F390D`, loop bound 4 at `0x026F39C9` |
| `+0x876` | **`u16` total characters on the account (server-reported)** | setter `0x026DBA10`; readers `0x026DBEA0`, `0x026DCDAD`, `0x026DD9EA`, `0x026DDA72`, `0x026E6C49`, `0x026EB95E`, `0x026ED677`, `0x026ED7AC` |
| `+0x908` | slot index the UI thinks is armed | `0x026E9D34` |
| `+0x97C` | byte, in-flight latch for cmd `0x2C1` | `0x026E9D6D`, set `0x026E9DE9` |
| `+0x9F4` | auto-select slot | `0x026ED71A`, `0x026ED778` |

### 5.3 Globals this module reads/writes

| VA | meaning | proof |
|---|---|---|
| `0x3B7CEE4` | `CNGameSocket` singleton | `0x0274CA50` |
| `0x3A5C9B8` | interface manager (`CNRDInterfaceManager.cpp`, TU 16) | receiver of `0x02304C70(windowId, p1, p2)` / `0x02301820` / `0x02301E60` |
| `0x3B7B258`/`0x3B7B25C` | protected **max character slots** — getter `0x026DCF50` | `0x026F5360` compares `charCount` against it and, if `charCount < max`, enters create mode; otherwise message id `0x2AA` |
| `0x3A5AFFC`/`0x3A5B000` | protected "server kind"; value `2` triggers the level-51 block | `0x026E62FC`; written from `CNRDUserInfoPacketHandler.cpp` `0x013E3E90` and `GameCoreNetwork_*` |
| `0x3A5BE80` | entry mode: `1` normal, `5` rejoin | `0x026E64ED`, `0x026EDCBD` |
| `0x39846A0` | rejoin slot index (`-1` = none) | `0x026EDCC6`, `0x026EDD1D` |
| `0x39855F0` | current character's job (`slot+0x04`) | written `0x026E6A26`, compared `==0xE` at `0x026F523E` |
| `0x3B7B248`/`0x3B7B24C` | queue of pending characters, 20-byte elements | `0x026DBE86`, `0x026DD9C0`, `0x026DDA54` |
| `0x3995F28`–`0x3995F3C` | scroll/paging state block (flags, saved index, saved count) | `0x026ED5BF`–`0x026ED5E9`, `0x026E6C66`–`0x026E6C91` |
| `0x3A388A0`/`A1`/`A4`/`A8`/`AC` | cmd `0x4D7` send-once latches and tick stamps | `0x026F54C7`–`0x026F5560` |
| `0x3B7AFFC` | cmd `0x19F` send-once latch | `0x026DF549`, `0x026DF56A` |
| `0x397D9D4` | byte fed twice into `CREATE_CHARACTER` | `0x026DD068`, `0x026DD081` |
| `0x3A3BF18` | `+4` = "go straight to create-character" flag | `0x026DD099`, `0x026F5346`, `0x026F541D` |

---

## 6. Two client-side name rules a server should know about

**a) The literal `"stdnf"` blocks a character.** `0x026DE6A0` (called only from
`0x013E8600` in TU 32, i.e. during character-info intake) does
`wcsstr(slot.name, L"stdnf")` (`0x026DE7C9`, callee `0x02C675CF` is a
byte-verified `wcsstr`) and on a hit sets `slot.field_0x1C = 8` (`0x026DE7FE`).
`requestSelectCharacter` then refuses that slot with reason code 2. So any
character whose name *contains* `stdnf` can never be entered from this client.

**b) `0x026E7E20` is the local create-name validator** run before cmd `0x2B5`.
It rejects the name if the edit box is empty (`0x027F51B0`, message id `0x8E`),
if `0x026E13C0` / `0x0274A860` / `0x026DB690` / `0x026DB700` reject it, and if
`wcsstr` finds any of these characters:

`'` (`0x032B7118`), ` ` space (`0x0306ECC0`), `\` (`0x031EDCCC`), `%` (`0x03130B64`),
`<` (`0x032F2808`), `>` (`0x03134DCC`), `"` (`0x032C9E60`), `|` (`0x035D14E0`).
One more literal at `0x03155238` (pushed at `0x026E7EE7`) would not decode.

---

## 7. Public surface (what is called from outside the unit)

347 external callers. The ones that matter:

| VA | called by | what it is |
|---|---|---|
| `0x026ED550` | `0x013E8AF0` (TU 32 char-list parser), `0x01630A00` | `onReceiveCharacterInfo` — the list-arrival callback; runs the rejoin/auto-select logic, rebuilds the slot UI, clears `[this+0x818]` |
| `0x026E61E0` | `GameCoreNetwork_CMD_Etc` `0x0110DE10`, `GameCoreNetwork_NOTI_Etc` `0x01160280`, `0x011D2160`, `0x006A7BD0`, `0x02536BA0`, `0x02606460` | `requestSelectCharacter(slot, force, arg3)` |
| `0x026EC100` | `0x01112E30`, `0x011D2160`, `0x013E8AF0` (+2 internal) | rebuild/refresh slot visuals |
| `0x026DBE30` | `0x013E8AF0` (TU 32) | "scrolled past what we have — fetch the next page" (cmd `0x2A8`) |
| `0x026E8380` | `0x0088ECB0`, `0x011751C0` | tiny map insert into `[this+0x614]` (the *real* `processCharacterDisconnect` is `0x026E83B0`) |
| `0x026DD020` | `0x012A4200` | send `CREATE_CHARACTER` (cmd 5) |
| `0x026DE0B0` | `0x023D4740`, `0x023D4BB0` | link-system step (cmd `0x139` + `0x127`×2) |
| `0x026DD3A0` | `0x01536EB0` | link-system start (cmd `0x138`) |
| `0x026DF610` | **257 external callers** | 112-byte leaf utility — it is a generic helper, not a module entry point; do not read anything into its in-degree |
| `0x026DBD40` / `0x026DBDA0` / `0x026DBD50` | 18 / 26 / 8 external | zero-callee accessors (trivial getters) |
| `0x026E74E0` | `RDARScript*` (TU 130) | script-exposed accessor |
| `0x026E7940` | `PopupWindow\Mak*` (TU 111) | popup callback |

---

## 8. Span is wrong — it is too narrow at both ends (mostly the top)

The briefing bounds the unit by the outermost `__FILE__` anchors,
`0x026DBBF0 … 0x026F5050`. Evidence that the file really runs
**`~0x026DB1F0` … `0x026FA070`**:

**Below `lo`** (functions with no `__FILE__` reference, used only from inside):
* `0x026DB760` loads `Etc/InvalidName.etc` + `Etc/SlangName.etc` — the two
  dictionaries that `0x026DEFC0` (inside the span) iterates.
* `0x026DB830` = `send cmd 0x2B5 { str }`; `0x026DB860` = `send cmd 0x127 { u32,u32 }` —
  the clean helper wrappers for two of this module's own packets.
* `0x026DBA10` = the `[this+0x876]` setter.
* `0x026DBAA0` = `vector<CharacterInfo>::at` — 11 callers, **all** inside the span.
* `0x026DB4D0` — 13 callers, 12 inside the span.

**Above `hi`** — this is the big miss, roughly **19 KB of code**:
* The anchor function `0x026F5050` itself runs to `0x026F55B0`; the span cuts at
  its entry.
* `0x026F5220` is the **real `onEnterModule`** (it holds the `__FUNCTION__`
  literal `0x035E2910` at `0x026F54A1` and the `CHANNEL INFORM>>>> …success` log
  at `0x026F548E`, and sends cmd `0x4D7`). It is entirely outside the span and is
  **missing from `functions.csv`** as a function start.
* `0x026F55B0` is 16 608 bytes — the largest function in the module — and loads
  `Interface2/SelectCharacter/SelectCharacter.img`,
  `Interface2/CharacterCreate/*.img`, `UI/SelectCharacter/Link_system/*.ani`,
  `Etc/LinkSystem/CharacLinkSystem.etc`, `UI/CharacterSlot/character_lock*.act`,
  and both `UI/SelectCharacter/SelectCharacter{Under,Over}.xui`.
* `0x026F9690`, `0x026F9F40`, `0x026F9FB0`, `0x026F9FC0` follow; the next module
  (`CNLoadModule`) starts at `0x026FA070`.
* The module's decrypted-literal block is contiguous from `0x035E1C28` to about
  `0x035E3738`; everything from `0x035E2910` upward is referenced **only** from
  `0x026F5220`/`0x026F55B0`, i.e. from outside the briefing span.

The literals *inside* the span do agree with the file name — the problem is
omission, not mislabelling.

**Function starts missing from `functions.csv`** (each verified to begin with a
`push ebp; mov ebp,esp; push -1; push <SEH>` prologue, each preceded by `int3`
padding):

| recovered start | what it is | evidence |
|---|---|---|
| `0x026E83B0` | `processCharacterDisconnect` | literal `0x035E2170` at `0x026E83EF`; pad at `0x026E83AB` |
| `0x026EDDA0` | `processCharacterLink` | literal `0x035E2498` at `0x026EDDDE`; pad at `0x026EDD91` |
| `0x026F2AA0` | `DrawProc` | literal `0x035E25F8` at `0x026F2AE9`; pad at `0x026F2A9D` |
| `0x026F3170` | `Proc` | literal `0x035E2648` at `0x026F31AF`; pad at `0x026F3162` |
| `0x026F5220` | `onEnterModule` | literals `0x035E2910`/`0x035E2968`; pad at `0x026F521A` |

So the rows `0x026E8380`, `0x026ED550`, `0x026F0DB0`, `0x026F5050` in
`functions.csv` each cover two or three real functions, and their in/out degrees
and literal attributions are merged accordingly.

---

## 9. Confidence

* **High**: the packet table (§2), the 36-per-page paging model (§3), the refusal
  ladder (§4), `CharacterInfo` size/stride and offsets `+0x00`, `+0x14/0x18`,
  `+0x1C` (§5.1), `+0x70` mode dispatch and `+0x876` total-count (§5.2), the
  `stdnf` and name-validator rules (§6), and the span correction (§8) — all read
  directly off the disassembly, several byte-verified.
* **Medium**: the *meaning* (as opposed to the wire shape) of cmds `0x127`,
  `0x138`, `0x139`, `0x19F`, `0x2C1`; the "Link System" grouping (inferred from
  co-located resource literals and the shared `[this+0x71C]`/`[this+0x720/0x721]`
  state); `slot+0x04` = job.
* **Unknown**: what `[0x397D9D4]` and `[0x3A3BF18]+4` actually hold, so the
  semantics of `CREATE_CHARACTER` bytes 3–5; the meaning of value `2` in the
  protected server-kind global (the level-51 gate and the
  `SelectCharacter*_StarterServer_Chn.xui` variants are suggestive but the
  `_StarterServer_` UI is selected by an unrelated check at `0x026F3893`);
  bytes 0x00–0x0D of the `0x4D7` payload; the banned-name literal at `0x03155238`.
