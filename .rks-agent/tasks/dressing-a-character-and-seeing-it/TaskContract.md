---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Dressing a character, and seeing it

## Context

### Source Request

> 日志如果有参考价值就保留。但是别有特殊的监控语义。也就是不是一眼就是排障的log就行。然后现在是能直接全处理掉了？如果行你就看着启动修复吧。修完了我再测

The three defects that request answers were found during a live session on 2026-09-08, after the
owner dragged eleven costumes onto the costume bar and reported: the costumes went on, the character
did not visibly change, and none of them could be taken off again.

### Current Situation

Three separate defects stand between a player and a costume. Each was traced to its line; none is a
guess.

**A -- a move reads only one of the two ends it was given.**

The client names two ends. Live observation of one drag (2026-09-08): a costume being taken off
arrives as `1:0->3:1`, with the moving item's identifier on the **second** end
(`iv=0/413560000`, template `413560000` being the hair costume worn at equipment slot 1) and the
first end naming the empty avatar-page cell the player let go over -- cell 0, which is where the
owner said they dropped it. The eleven equips measured on 2026-09-06 arrive the other way round,
with the item on the first end (`0:182->3:0`).

The within-one-list move already answers this. `StoreBackedItemContainer.MoveToAsync`
(`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:533`, read from this repository)
picks whichever end holds something and refuses only when **neither** does:

```csharp
var (moving, displaced, targetSlot) = slots.First is not null
    ? (slots.First, slots.Second, toSlotIndex)
    : (slots.Second, null, fromSlotIndex);
if (moving is null) return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);
```

The three cross-list paths -- `PutOnAsync`, `TakeOffAsync`, `CarryAcrossAsync` in
`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` -- were written later and
did not carry that rule across. Each reads its own source end, finds null, and returns
`Refused(ItemMoveStatus.BothSlotsEmpty)` without ever reading the other end. That status is
documented in its own enum as "Neither named slot holds anything", so the single-ended check
contradicts the meaning the code already declares.

This is not a decoding error. The upstream reference parser
(`ServerS4A21/Server/DfoServer/Network/Handlers/InventoryHandler.Move.cs`, read from source) assigns
the same bytes to the same fields as ours -- `body[0]` source list, `body[1]` source slot,
`body[11]` destination list, `body[12]` destination slot -- its `InventoryListType` carries the same
numbers (Main=0, Avatar=1, PersonalCargo=2, Equipment=3), and its `IsAvatarEquipMutation` accepts
`1->3` and `3->1` alike.

**B -- a successful move tells the client nothing beyond the acknowledgement.**

`MoveItemSpaceHandler.HandleMessageAsync` sends `new GameExplicitMessageDelivery([response], [])`
and nothing else (read from this repository). Upstream sends the acknowledgement and then, for a
move that changed what the character wears, an appearance refresh
(`InventoryHandler.Move.cs:112-128`, read from source). This is why the eleven costumes only became
visible after the owner left to character selection and came back: the appearance the client already
had was never replaced.

**C -- the character-selection screen draws a stored appearance nobody keeps current.**

`CharacterRosterService.cs:130` decodes `row.AppearanceBlob`, and
`AccountCharacterRosterPayloadCodec.WriteAppearance` writes it back out, twenty-three bytes per part
(read from this repository). No path updates that blob when equipment changes. Upstream derives the
same list from the character's currently equipped items at the moment the roster is built
(`AppearanceService.LoadRosterAppearancesFromDb`, read from source). Live observation (2026-09-08):
the selection screen shows the character undressed while the game itself shows the costumes.

## Goal

A player can put a costume on and take it off again, sees the character change as it happens, and
sees the same character wearing the same things on the selection screen -- without leaving and
re-entering the game.

## Scope

### In

- A move carried out as the move it describes whenever exactly one of its two named ends holds
  something, across every pair of lists this server serves, in both orders.
- Every refusal and admission path this change alters, enumerated forward and reverse.
- The client told, without re-entry, when a successful move changed what the character wears.
- The character-selection roster showing the character's current worn appearance.
- Move logging that names both ends' lists and slots, worded as ordinary operational logging.
- Correcting repository records this work falsifies.

### Out

- The three subsystem-specific sends upstream makes after a move alongside the appearance refresh:
  pet runtime, knight-shield deck, and sort/lock signals. Each belongs to a subsystem with its own
  rules and its own owner; sending them because upstream does, without those rules, would be copying
  a shape rather than delivering a behaviour. Nothing in the reported symptoms depends on them.
- A new protocol frame. `ExtendedUserStateMessage` (NOTI `0x0002`) already exists in this repository
  and is already sent from two places, so the frames this work needs are frames it has.
- The orphan row found while diagnosing: `item_slots` main-bag slot 3 names `core_uid 8474`, for
  which `items` holds no row. Recorded as a known risk; not investigated and not repaired here.
- Merging, pushing, opening a pull request, deleting a branch or worktree, and cleanup.

## Constraints

- Layering: Data -> Abstractions -> Application -> {Infrastructure, Persistence, Networking} ->
  Protocol -> Host.
- Slice architecture (`AGENTS.md`): one directory per `Protocol/<Endpoint>/<Protocol>/`, no
  cross-protocol references inside a slice, one wiring point in `ProtocolModule` per protocol, no
  nested dispatch in a command handler, and every command that can reach a durable write is
  transactional.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` keep passing; neither
  their predicate nor their named exemption list is relaxed or extended.
- `TreatWarningsAsErrors` is on and CS9113 is an error.
- No existing check's expected value or assertion is rewritten to accommodate an implementation. A
  rewrite that corresponds to an authorized behaviour change is allowed, and each one says which
  change it corresponds to.
- Byte anchor `SelectCharacterAcceptedTranscriptTests`: frames that change are named one by one with
  their new values and the reason; frames that should not change stay byte-identical.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Work has been lost in
  this repository that way. Mutation checks use file-level backups plus `sha256sum`, restored and
  re-hashed immediately after each use.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main
  checkout's `ClientHack/` are not touched.
- `E:/Project/DNF/ServerS4A21` is a read-only reference implementation and is never written to.
- A record never claims more than its evidence: "read from upstream source", "read from this
  repository", and "observed live" are marked apart. No count a reader cannot check.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A move naming one empty end and one occupied end is carried out as the move it describes -- the item ends up at the empty end -- for every pair of lists this server serves, in both orders. | Automated checks covering each served pair in both orders, plus the owner taking a costume off in the running game. |
| SC-2 | A move naming two empty ends is still refused, and still refused as "both slots empty". | Automated checks. |
| SC-3 | Every production refusal and admission path this work adds, removes, or changes is listed, forward and reverse, arrived at by reading the code rather than by following the change's own origin. | A change list, plus an independent review that scanned the code itself rather than checking the list's entries against it. |
| SC-4 | After a successful move that changed what the character wears, the client is told what the character now wears, without re-entry. | Automated checks that the frame is emitted for such a move, plus the owner seeing the character change as a costume goes on and comes off. |
| SC-5 | A successful move that changed nothing the character wears does not send that frame. | Automated checks naming at least one such move. |
| SC-6 | The character-selection screen shows the character wearing what the character currently wears. | An automated check in which changing worn equipment changes the roster payload, plus the owner seeing the costumes on the selection screen. |
| SC-7 | A move's log line names the list and the slot at both ends, and reads as ordinary operational logging rather than as instrumentation left behind by a diagnosis. | The rendered line asserted in a check, and the owner's acceptance of its wording. |
| SC-8 | No protocol frame is added, and every frame whose bytes were not authorized to change is unchanged. | The byte-anchor checks, plus a per-frame account of every authorized change. |
| SC-9 | The full check suite is no weaker than its baseline. | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` at 2361 passed / 2 skipped / 0 failed or better, exit code 0. |
| SC-10 | Repository records this work falsifies are corrected. | The search set stated, actually executed, its output read, and the resulting corrections listed. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「日志如果有参考价值就保留。但是别有特殊的监控语义。也就是不是一眼就是排障的log就行。然后现在是能直接全处理掉了？如果行你就看着启动修复吧。修完了我再测」 -- answering a report that named exactly these three defects and asked whether to open one task or three. | Repair all three defects; the division of work and the workflow structure are the executor's to choose. Covers contract creation and revision, routing, one task worktree, configured execution and verification, one local accepted-result commit per accepted result after review and fresh verification, and the exact workflow rollback of one unaccepted failed or blocked attempt. |

## Requires Confirmation

- Merging, pushing, opening a pull request, deleting a branch or worktree, and cleanup.
- Changing the wording of any requirement in this contract.

## Assumptions

- The baseline is 2361 passed / 2 skipped / 0 failed at `8247182c`, measured this session by running
  the test projects separately and adding them up. The two skips are `A_missing_archive_*`, and
  their being skipped is the sign that the archive did load.
- A running `Dfo.Server.Host` locks `Dfo.Server.Protocol.dll` under `Dfo.Server.Host/bin`, so the
  build fails with MSB3027 and looks like a broken suite when it is not. One is running now and has
  to be stopped before the suite is run.
- A known flaky failure is judged by exception type plus stack root plus passing on a rerun, never
  by test name: `ObjectDisposedException: 'SQLitePCL.sqlite3'`, stack root
  `CharacterDatabaseSchema.KeepWritersAndReadersApart -> SqliteCommand.ExecuteScalar`.
- Defect A turns a set of requests that are refused today into requests that succeed and write to
  the store. That is the direction in which this repository has repeatedly under-reported: a
  previous result's change list missed thirteen new production refusals because the check followed
  the change's origin downstream and added a full suite run, and a full green suite is exactly what
  a path no check reaches looks like. SC-3 exists because of that.
- The eleven costume slots were measured in an earlier result and are recorded in
  `PvfEquipmentSlots.ByName` (`[hat avatar]`=0 through `[weapon avatar]`=10; the archive names no
  template for slot 11). This work does not re-measure them.
- Two log edits made while diagnosing were backed up under the session scratchpad at
  `log-edits/` (with `log-fields.patch`) and removed from the main checkout, which is clean at
  `8247182c`. Their content is to be redelivered under SC-7 in the owner's requested wording, not
  restored as it stands.
