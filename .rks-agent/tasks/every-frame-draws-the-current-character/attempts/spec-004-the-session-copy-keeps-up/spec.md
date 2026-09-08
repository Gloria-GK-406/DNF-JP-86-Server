---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: The session copy keeps up

## Why and Outcome

The session holds the whole prepared entry result, taken when the character was selected, and nothing
refreshes it after an item move. Two of its sixteen readers take the character's equipment out of it, so
two screens draw a character wearing what it wore at selection: the dungeon selection screen, and -- on
other players' screens -- the PvP lobby.

The outcome is that the copy's inventory follows a move, and that the fourteen readers which take members
a move cannot change are handed exactly what they are handed today.

## What the Enumeration Found

Full result in `reader-enumeration.md`. Three facts decide this Spec:

1. The copy is a whole `SelectCharacterResult`, not an inventory. Sixteen readers, twelve files.
2. Exactly two go stale, and both take the same member -- the Equipment container of `CharacterInventory`.
   The second, the PvP lobby, was not previously recorded and is on **another player's** screen.
3. The other fourteen read members a move cannot change, so replacing only `CharacterInventory` leaves
   them holding the identical instances they hold now.

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | After a move that succeeded, the resident copy's `CharacterInventory` is what the store now holds. |
| RQ-002 | Every other member of the copy is the same instance it was. The refresh replaces one member, not the result. |
| RQ-003 | Nothing the refresh can raise costs the player the acknowledgement of a move that has already been committed. |
| RQ-004 | A move that was refused, or that changed nothing, does not refresh the copy. |
| RQ-005 | Where the appearance frame is also being built, the inventory is read once and used for both. |
| RQ-006 | The session's position-persistence flag is unaffected. |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | Given a character whose equipment changes by a move, when the dungeon selection screen is asked afterwards, it names the equipment the character has now. Asserted on the frame's contents, not on the absence of an old value. |
| AC-002 | RQ-001, RQ-002 | After a move, the resident copy's `CharacterInventory` differs from the one taken at selection, and `Character`, `CharacterEntry`, `World`, `Session`, `Account`, `AccountInventory`, `Roster` and `RosterEntry` are reference-equal to what they were. |
| AC-003 | RQ-003 | When the refresh raises -- including `ReplaceExact`'s `InvalidOperationException` when the session generation has moved on -- the acknowledgement is still sent and the failure is logged. |
| AC-004 | RQ-004 | A refused move leaves the copy as it was. |
| AC-005 | RQ-005 | A move that changes the drawn appearance reads the inventory once, not twice. |
| AC-006 | RQ-006 | The flag `TryGet` reports alongside the copy is the same before and after a refresh. |
| AC-007 | RQ-001..RQ-006 | Mutations, including one that strikes the clause this outcome's correctness rests on -- that only `CharacterInventory` is replaced -- and at least one that reddens through a value mismatch rather than a raised exception. |
| AC-008 | all | The suite passes against the 2409 / 2 / 0 baseline, output retained and searched for `[FAIL]`, `error CS` and `MSB<digit>`. |

## What This Does Not Do

- It does not make the copy authoritative. The store remains the truth; the copy remains a copy that is now
  maintained after a move as it already is after a dungeon clear, a monster death, a quest and a tutorial
  level-up.
- It does not touch the six existing `ReplaceExact` call sites.
- It does not refresh on anything but a move.

## Success Evidence

- EV-001 (AC-001, AC-004): checks over the dungeon selection frame before and after a move.
- EV-002 (AC-002, AC-006): a check reading the resident copy directly.
- EV-003 (AC-003): a check with a failing refresh.
- EV-004 (AC-007): the mutation record, saying which cases reddened and how.
- EV-005 (AC-008): the retained run output.
- EV-006 (AC-005): inventory load count plus the immediate and subsequent screen contents.

## Assumptions

- The PvP lobby staleness is derived from the code and has not been observed live. Repairing it is a
  consequence of repairing the copy, not a separately verified fix.
- `ReplaceExact` preserves everything on the entry except the result -- read from
  `SelectedSessionRuntimeDirectory.cs:27`, `entry with { Result = result }` -- which is why RQ-006 is
  expected to hold rather than needing new code.


## Evidence boundary clarified after review

RQ-002 preserves the eight members captured by the read; it is not an atomic aggregate merge.
The unchanged directory checks generation only. Concurrent refreshes can overwrite one another,
and a failed reload leaves the copy stale. No production serialization guarantee or live PvP
observation is claimed. The contract leaves the other six refresh sites unchanged; this limitation
is recorded for independent adjudication rather than silently asserted away.
