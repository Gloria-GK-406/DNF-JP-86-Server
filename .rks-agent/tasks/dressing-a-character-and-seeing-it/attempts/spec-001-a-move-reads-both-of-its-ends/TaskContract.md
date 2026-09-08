---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-2, SC-3, SC-7, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: A move reads both of its ends

## Context

### Source Request

Derived from the root TaskContract `dressing-a-character-and-seeing-it` (revision 1), defect A. The
owner's grant covers all three defects and leaves the division of work to the executor; this is the
first of the three bounded outcomes recorded in the root map.

### Current Situation

A `MOVE_ITEMSPACE` request names two ends. Which of them holds the moving item is not fixed by the
protocol: the client puts the item on whichever end it is coming from, and puts the cell the player
let go over on the other.

- Eleven costume equips measured live on 2026-09-06 arrive with the item on the first end
  (`0:182->3:0` and ten more like it).
- One costume take-off measured live on 2026-09-08 arrives with the item on the **second** end:
  `1:0->3:1`, `iv=0/413560000`. Template `413560000` is the hair costume then worn at equipment slot
  1, and avatar-page cell 0 is where the owner said they let go.

The within-one-list move already answers this. `StoreBackedItemContainer.MoveToAsync`
(`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:533`, read from this repository)
takes whichever end holds something, moves it to the other, and refuses only when **neither** does:

```csharp
var (moving, displaced, targetSlot) = slots.First is not null
    ? (slots.First, slots.Second, toSlotIndex)
    : (slots.Second, null, fromSlotIndex);
if (moving is null) return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);
```

The three cross-list paths in
`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` -- `PutOnAsync`,
`TakeOffAsync`, `CarryAcrossAsync` -- were written later and did not carry that rule across. Each
reads only its own source end, finds null, and returns `Refused(ItemMoveStatus.BothSlotsEmpty)`
without ever reading the other. That status is documented in its own enum as "Neither named slot
holds anything", so the single-ended check contradicts the meaning the code already declares.

This is not a decoding error. The upstream reference parser
(`ServerS4A21/Server/DfoServer/Network/Handlers/InventoryHandler.Move.cs`, read from source) assigns
the same bytes to the same fields as ours, its `InventoryListType` carries the same numbers, and its
`IsAvatarEquipMutation` accepts `1->3` and `3->1` alike.

Live consequence: every attempt to take a costume off is refused as `BothSlotsEmpty` naming no item.

Separately, the move's log line names both slots but neither list. That is what made this diagnosis
take three live rounds: the same pair of numbers can belong to several different paths and the line
did not say which. Two diagnostic log edits were made during the investigation, backed up under the
session scratchpad at `log-edits/` and then removed from the main checkout. The owner asked for
whatever has lasting value to be kept, but worded as ordinary logging: 「日志如果有参考价值就保留。
但是别有特殊的监控语义。也就是不是一眼就是排障的log就行」. The backed-up wording
(`asked=... iv=.../...`) does not meet that; it is reference material, not a patch to reapply.

## Goal

A move is carried out as the move it describes whenever one of the two ends it names holds
something, whichever end that is, across every pair of lists this server serves -- and the record it
leaves says which lists it was between.

## Scope

### In

- The rule the within-list move already applies, applied to moves between two different lists,
  including onto and off the worn list.
- The acknowledgement continuing to describe the move that actually happened, in terms the client
  can apply to its own picture.
- A change list covering every production refusal and admission path this change adds, removes, or
  alters, forward and reverse.
- The move's log line naming the list as well as the slot at both ends, worded as ordinary
  operational logging.
- Correcting repository records this change falsifies.

### Out

- What the server sends after a successful move beyond the acknowledgement. That is root defect B
  and its own Spec; this work may make more moves succeed but adds no new frame.
- The character-selection roster's appearance. That is root defect C and its own Spec.
- The rules of any container this server does not already serve for moves. Cargo, pet and the
  knight-shield catalogue stay unanswered exactly as they are now.
- The orphan row recorded in the root contract (`item_slots` main slot 3 naming `core_uid 8474`
  with no `items` row).
- Re-measuring the eleven costume slots. They were measured in an earlier result and are recorded in
  `PvfEquipmentSlots.ByName`.

## Constraints

Every constraint in the root TaskContract applies unchanged. The ones that bear hardest here:

- No existing check's expected value or assertion is rewritten to accommodate an implementation.
  `RefusedEquipIsRecordedWithItsTemplateTests` pins the rendered log line word for word, so the
  logging change rewrites it; that rewrite says which authorized change it corresponds to, and no
  other assertion in that file moves with it.
- Byte anchor `SelectCharacterAcceptedTranscriptTests` must stay byte-identical unless a frame is
  named, explained, and given its new values. No new protocol frame.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` keep passing with
  neither predicate nor exemption list relaxed.
- No git restoring command. Mutation checks use file-level backups plus `sha256sum`, restored and
  re-hashed immediately after each use.

## Success Conditions

| ID | Observable condition | Required evidence | Root |
|---|---|---|---|
| C-1 | A move naming one empty end and one occupied end moves the item to the empty end, for every pair of lists this server serves for moves, in both orders. | Automated checks enumerating the served pairs and covering each in both orders, with the enumeration itself derived from the dispatch rather than from a list written by hand. | SC-1 |
| C-2 | A move naming two empty ends is still refused, and still as "both slots empty". | Automated checks, including at least one pair where the reversal rule would otherwise have applied. | SC-2 |
| C-3 | Every rule that decided whether a move was allowed before this change still decides it after, for the end that actually holds the item. | Automated checks showing a reversed move refused for each admission rule that applies to it, not merely accepted for the cases that pass. | SC-1, SC-3 |
| C-4 | The acknowledgement describes the move that happened, so a client applying it reaches the state the store holds. | Automated checks comparing the acknowledgement's two ends against the store after a reversed move. | SC-1 |
| C-5 | Every production refusal and admission path this change adds, removes, or alters is listed, forward and reverse, arrived at by reading the code. | A change list, plus an independent review that scanned the code itself rather than checking the list's entries against it. | SC-3 |
| C-6 | A move's log line names the list and the slot at both ends and reads as ordinary operational logging. | The rendered line asserted in a check; wording that a reader would take for normal service logging rather than instrumentation left behind by a diagnosis. | SC-7 |
| C-7 | No protocol frame is added, and no frame's bytes change. | The byte anchor unchanged, and no new message type registered. | SC-8 |
| C-8 | The full check suite is no weaker than its baseline. | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` at 2361 passed / 2 skipped / 0 failed or better, exit code 0, with no `Dfo.Server.Host` process running. | SC-9 |
| C-9 | Repository records this change falsifies are corrected. | The search set stated, actually executed, its output read, and the corrections listed. | SC-10 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root TaskContract `dressing-a-character-and-seeing-it` revision 1, Authorization row `task`: 「日志如果有参考价值就保留。但是别有特殊的监控语义。也就是不是一眼就是排障的log就行。然后现在是能直接全处理掉了？如果行你就看着启动修复吧。修完了我再测」 | This bounded outcome inside the root's authorized scope: execution and verification in the shared root worktree, and one local accepted-result commit after review and fresh verification. Merging, pushing, opening a pull request, deleting a branch or worktree, and cleanup remain outside it. |

## Assumptions

- Which end of a request holds the item is decided per request by the client, not fixed per
  direction. This rests on two live measurements pointing opposite ways and on the within-list
  move already being written for it. The conditions above are stated symmetrically, so they hold
  whether or not any further pattern is found in how the client chooses.
- The set of list pairs this server serves for moves is whatever the dispatch admits today; this
  work widens no list's admission. Cargo, pet and the knight-shield catalogue stay unserved.
- This change turns requests that are refused today into requests that succeed and write to the
  store. That is the direction in which this repository has under-reported before -- a previous
  change list missed thirteen new production refusals, and a full green suite is exactly what a path
  no check reaches looks like. C-3 and C-5 exist because of that.
