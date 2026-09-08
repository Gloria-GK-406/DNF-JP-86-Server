---
kind: rks-spec
state: ready
contract:
  path: ./TaskContract.md
  revision: 1
---

# Spec: A move reads both of its ends

## Purpose

`MOVE_ITEMSPACE` names two ends and does not say which of them holds the item. The client puts the
item on the end it is coming from and the cell the player let go over on the other, and it does this
in whichever order the drag happened to take. This server reads only the first-named end whenever
the move crosses two lists, so every drag that comes the other way round is refused as though the
inventory were empty.

The rule that answers this is already in the repository, one layer down, applied to moves within a
single list. This work applies the same rule to moves between two lists.

## The failure

Observed live on 2026-09-08, taking a costume off:

```
Move item space Game:01-00-00-00 character=1: item 0 1:0->3:1 refused as BothSlotsEmpty asked=1 iv=0/413560000
```

- `1:0` -- avatar page, cell 0. Empty. This is the cell the owner let go over.
- `3:1` -- equipment slot 1. Holds template `413560000`, the hair costume, which the same session
  had put there.
- The moving item's identifier arrives on the **second** end.

Observed live on 2026-09-06, putting eleven costumes on, the item arrives on the **first** end
(`0:182->3:0` and ten more of the same shape). Both orders are ordinary client behaviour.

`MoveItemSpaceCommandHandler.PutOnAsync`, `TakeOffAsync` and `CarryAcrossAsync` each read their own
source end, find nothing, and return `ItemMoveResult.Refused(ItemMoveStatus.BothSlotsEmpty)` -- the
one-argument overload, which is why the line above names `item 0`. Neither ever reads the other end,
although that status is documented in its own enum as "Neither named slot holds anything".

`StoreBackedItemContainer.MoveToAsync`
(`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:533`, read from this repository)
already does the right thing for a move inside one list, and has since before this defect existed.

This is not a decoding error: the upstream reference parser assigns the same bytes to the same
fields, and its list numbers are ours (read from
`ServerS4A21/Server/DfoServer/Network/Handlers/InventoryHandler.Move.cs`).

## The pairs this is about

Read from `MoveItemSpaceCommandHandler.HandleAsync` in this repository. `CarriedListFor` admits only
Main (0) and Avatar (1); Equipment is 3.

| First-named list | Second-named list | Branch today | Reverses today |
|---|---|---|---|
| Main | Main | `MoveToAsync` | yes |
| Main | Avatar | `CarryAcrossAsync` | **no** |
| Main | Equipment | `PutOnAsync` | **no** |
| Avatar | Main | `CarryAcrossAsync` | **no** |
| Avatar | Avatar | `MoveToAsync` | yes |
| Avatar | Equipment | `PutOnAsync` | **no** |
| Equipment | Main | `TakeOffAsync` | **no** |
| Equipment | Avatar | `TakeOffAsync` | **no** |
| Equipment | Equipment | `ListNotServed` | n/a |
| anything else | anything | `ListNotServed` | n/a |

Eight ordered pairs are served. Six of them lack the rule. `Equipment`-to-`Equipment` is not served
and stays unserved.

Reversing a served pair yields another served pair, always: carrying across becomes carrying across
the other way, putting on becomes taking off, and taking off becomes putting on.

## Requirements

### RQ-001 -- the occupied end is the one that moves

**Given** a move naming two ends, both belonging to lists this server serves for moves,
**when** the first-named end holds nothing and the second-named end holds something,
**then** the item at the second-named end is moved to the first-named end, and the result is
`Moved`, naming that item.

The direction is decided by which end holds the item, never by which field the request called
"source".

### RQ-002 -- the rules that decide a move are applied to the real direction

**Given** a move that RQ-001 reverses,
**when** the server decides whether it is allowed,
**then** every rule that would have decided the same move written the other way round decides it
here, unchanged and in the same order, and the refusal it produces is the same refusal.

A costume taken off into a bag slot that will not admit it is refused exactly as it would be if the
client had named the ends the other way; a piece put on a slot its archive entry does not name is
refused exactly as it would be. This work adds no admission rule and removes none.

### RQ-003 -- two empty ends are still nothing

**Given** a move naming two ends,
**when** neither holds anything,
**then** it is refused as `BothSlotsEmpty`, as it is today.

### RQ-004 -- a request that already worked still works the same way

**Given** a move whose first-named end holds something,
**when** it is served,
**then** its outcome, its refusal reason, and the order in which its refusals are decided are
exactly what they are before this change. In particular `ListNotServed` is still decided for a list
this server does not serve, and `NoBoundContainer` is still decided before either end is read.

### RQ-005 -- the acknowledgement stays applicable

**Given** a move that RQ-001 reverses and carries out,
**when** the acknowledgement is built,
**then** it names the request's own two ends and reports one piece moved.

Upstream does the same and is explicit about it: on the reverse branch it sets
`result.DestinationListType = request.DestinationListType` and
`result.DestinationSlotIndex = request.DestinationSlotIndex`
(`ServerS4A21/Server/DfoServer/Game/Inventory/Services/InventoryMoveService.cs:139-140`, read from
source). A client that applied the acknowledgement as a swap of the two named cells reaches the
state the store holds, because a move into an empty cell and a swap with an empty cell are the same
thing.

### RQ-006 -- a client restating a move it already saw does not undo it

**Given** a move whose first-named end holds nothing and whose second-named end is a worn slot,
**when** the request carries a source instance value greater than zero that is the identifier of the
item worn at that slot,
**then** the move is not carried out, and the player is shown nothing.

This is a request the client sends after a move it has already applied, restating it. Without this,
RQ-001 would read it as "the worn thing should go back to the bag cell it came from" and quietly
take off what was just put on. Upstream guards exactly this case and refuses it as a client echo
(`IsEquipmentClientEchoWhenSourceMissing`, `InventoryMoveService.cs:899-906`, read from source).

The take-off measured live is not this case: it carried a source instance value of zero.

This is the one new refusal path in this work. It is here because RQ-001 creates the hazard it
guards against, not because upstream has it.

### RQ-007 -- a move's record says which lists it was between

**Given** any move that reaches a decision,
**when** it is logged,
**then** the line names the list and the slot at both ends.

The line names slots only today. That is what made this defect take three live rounds to locate: the
same pair of numbers belongs to several different paths, and the line did not say which.

The wording is ordinary service logging. The owner asked for it in those terms:
「日志如果有参考价值就保留。但是别有特殊的监控语义。也就是不是一眼就是排障的log就行」. A reader
should take the line for a record of what the server did, not for instrumentation somebody left
behind after a diagnosis.

### RQ-008 -- the served pairs are read from the dispatch

**Given** the checks that prove RQ-001 across "every pair this server serves",
**when** they enumerate those pairs,
**then** the enumeration is derived from the same list numbers the dispatch itself branches on, so
that a pair added to or removed from the dispatch changes the enumeration rather than leaving a
hand-written list behind.

### RQ-009 -- nothing else moves

**Given** this change,
**then** no protocol frame is added, no frame's bytes change, and nothing is sent after a move that
is not sent today. What a successful move should tell the client beyond its acknowledgement is
defect B of the root contract and belongs to its own Spec.

## Acceptance

| ID | Condition | Proves |
|---|---|---|
| AC-001 | For each of the six served pairs that lack the rule, a check seeds the second-named end and leaves the first empty, and the item ends at the first-named end with a `Moved` result naming it. | RQ-001 |
| AC-002 | The pair enumeration those checks iterate is built from the container types the dispatch branches on, not from a literal list; a check asserts the enumeration contains exactly the served pairs. | RQ-008 |
| AC-003 | For each cross-list path, a check seeds the second-named end with something the first-named end's rules refuse, and the refusal is the same status the equivalent forward request produces. At minimum: a costume aimed at a bag band that will not take it, and a piece whose archive entry names a different worn slot. | RQ-002 |
| AC-004 | A check names a pair where both ends are empty and asserts `BothSlotsEmpty`, with an item identifier of zero. | RQ-003 |
| AC-005 | The existing checks for forward moves pass unchanged, and a check asserts `ListNotServed` for an unserved list and for Equipment-to-Equipment. | RQ-004 |
| AC-006 | After a reversed move, a check compares the acknowledgement's two ends and its move value against the store. | RQ-005 |
| AC-007 | A check sends a request with an empty first end, a worn second end, and a source instance value equal to the worn item's identifier, and asserts the store is unchanged and the client is shown nothing. A second check, differing only in that the instance value is zero, asserts the move happens. | RQ-006 |
| AC-008 | The rendered log line is asserted for a success and for a refusal, and names both lists and both slots. | RQ-007 |
| AC-009 | `SelectCharacterAcceptedTranscriptTests` passes with no expected byte changed, and no message type is registered that was not registered before. | RQ-009 |
| AC-010 | Every production refusal and admission path this change adds, removes, or alters is listed forward and reverse in a change list, arrived at by reading the dispatch and the three paths rather than by following this Spec's own headings. | Contract C-5 |
| AC-011 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` returns 2361 passed / 2 skipped / 0 failed or better, exit code 0, with no `Dfo.Server.Host` process running. | Contract C-8 |

## Evidence

- EV-001 -- live take-off, 2026-09-08: `1:0->3:1`, `iv=0/413560000`, refused `BothSlotsEmpty`.
- EV-002 -- live equips, 2026-09-06: eleven of the form `0:182->3:0`, item on the first end.
- EV-003 -- store state at the time of EV-001, read from the running database: `item_slots` for
  character 1 holds `list_type=3` slots 0..10 (uids 38..48) and no `list_type=1` row at all. The
  store's own read query, run by hand against that database, returns the hair costume for
  `list_type=3, slot 1`, so the refusal was not a failed read.
- EV-004 -- upstream reverse branch, read from source:
  `InventoryMoveService.cs:97-141`. Source empty, second end read as `reverseSource`, both empty
  gives `SourceNotFound`, otherwise validate and apply with the ends swapped, then report the
  request's own destination.
- EV-005 -- upstream client-echo guard, read from source: `InventoryMoveService.cs:899-906`.
- EV-006 -- the dispatch table above, read from `MoveItemSpaceCommandHandler.HandleAsync` in this
  repository.

## Not in this Spec

- NA-001 -- What a successful move sends beyond its acknowledgement. Root defect B, its own Spec.
- NA-002 -- The character-selection roster's appearance. Root defect C, its own Spec.
- NA-003 -- Upstream's `IsEmptyTitleNameClientEcho` (`InventoryMoveService.cs:908-917`, read from
  source), which turns a Main-to-title-slot request with both ends empty into a silent success. It
  is a title-slot client quirk, it only fires where RQ-003 says the answer is a refusal, and nothing
  observed here depends on it. Adopting it would be a separate decision about titles.
- NA-004 -- Widening the set of lists this server serves for moves. Cargo, pet and the
  knight-shield catalogue stay unserved.

## Assumptions

- AS-001 -- Which end holds the item is chosen per request by the client. Two live measurements
  point opposite ways, and the within-list move was already written for it. Every requirement above
  is stated symmetrically, so none of them depends on finding a further pattern.
- AS-002 -- RQ-006 rests on upstream source and on the hazard RQ-001 creates, not on a client echo
  observed here. A check pins its exact trigger so that a later reader can see how narrow it is:
  first end empty, second end worn, instance value greater than zero and equal to the worn item.
