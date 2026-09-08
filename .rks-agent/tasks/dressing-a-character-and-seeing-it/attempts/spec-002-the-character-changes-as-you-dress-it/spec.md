---
kind: rks-spec
state: ready
contract:
  path: ./TaskContract.md
  revision: 1
---

# Spec: The character changes as you dress it

## Purpose

A move that changed what the character is wearing leaves the server with only its acknowledgement,
which says where an item went and nothing about how the character now looks. The client draws the
character from what it was last told, so it goes on drawing what it was told at character selection
— which is why eleven costumes went on and nothing happened until the owner left and came back.

## The failure

Observed live, 2026-09-08: eleven costumes were put on, the acknowledgements all reported success,
the store held them (`item_slots`, `list_type=3`, slots 0..10), and the character on screen did not
change. Leaving to character selection and re-entering drew them, because entry sends the whole
state again.

`MoveItemSpaceHandler.HandleMessageAsync` sends `new GameExplicitMessageDelivery([response], [])`
(read from this repository). Upstream sends the acknowledgement and then, for a move that touched an
appearance slot, a NOTI `0x0002` subtype-0 frame
(`InventoryHandler.Move.cs:112-128`, `InventoryRefreshSender.cs:40-48`, read from source).

## Requirements

### RQ-001 — a move that changed the character's look says so

**Given** a move that succeeded and named the equipment list at one of its two ends,
**when** the slot it named there is one the character's appearance is drawn from,
**then** the same exchange that carries the acknowledgement also carries a NOTI `0x0002` subtype-0
frame describing what the character now wears, and the acknowledgement comes first.

### RQ-002 — and one that did not, does not

**Given** a successful move that named the equipment list at neither end, or named it at a slot the
appearance is not drawn from,
**then** nothing is sent but the acknowledgement.

A move inside the bag, a move between the bag and the avatar page, and a move onto a worn slot the
appearance ignores are all this case.

### RQ-003 — a refusal stays a refusal

**Given** a move that was refused, for any reason,
**then** nothing is sent but the refusal.

### RQ-004 — the appearance is the store's, not the snapshot's

**Given** the frame of RQ-001,
**when** its equipment is assembled,
**then** it is what the store holds after the move.

The session's own copy of the character's inventory is the snapshot taken when the character was
selected. No move refreshes it — `ReplaceExact` has eight call sites in `Server/src` and none is
after a move — so building the frame from it would send the client the same picture it already has,
which is the defect rather than the fix.

### RQ-005 — the snapshot is left exactly as it was

**Given** this work,
**then** the session's inventory snapshot is not replaced.

Refreshing it would change what every other reader of it draws — `DungeonSelectionSlice.cs:419-421`
takes its equipment from there — and that is a different outcome from this one. **The snapshot stays
stale, deliberately**, and this Spec's frame is built beside it rather than from it. That the two
disagree is recorded rather than quietly fixed, because a later reader would otherwise assume they
agree.

### RQ-006 — nothing new goes on the wire

**Given** this work,
**then** no protocol frame is added, no frame's bytes change, and the appearance the subtype-0
encoder derives is derived exactly as it is today.

The three slot filters this repository draws appearances through disagree with each other
(`SlotIndex <= 12 || == 23` in the subtype-0 encoder, `<= 13` when a character is created, `0..11`
for the costume bar). Which is right changes bytes and belongs with defect C. This work sends the
frame the encoder already builds.

## Acceptance

| ID | Condition | Proves |
|---|---|---|
| AC-001 | A check puts a costume on and asserts two frames leave in one exchange, the acknowledgement first, the second a `CurrentUserStateMessage`. A second check takes one off and asserts the same. | RQ-001 |
| AC-002 | A check asserts the appearance the frame carries names the costume that was just put on — and, for the take-off, that it does not. | RQ-001, RQ-004 |
| AC-003 | A check where the session snapshot and the store deliberately disagree asserts the frame follows the store. Without it, an implementation reading the snapshot would pass every other check here. | RQ-004 |
| AC-004 | Checks that a move inside the bag, and a move between bag and avatar page, send the acknowledgement alone. | RQ-002 |
| AC-005 | A check that a refused move sends the refusal alone. | RQ-003 |
| AC-006 | A check that the session's inventory snapshot is the same object after the move as before it. | RQ-005 |
| AC-007 | `SelectCharacterAcceptedTranscriptTests` passes with no expected byte changed, and no message type is registered that was not registered before. | RQ-006 |
| AC-008 | Every production path this work adds or changes is listed forward and reverse in a change list, arrived at by reading the handler rather than by following this Spec's headings. | Contract C-5 |
| AC-009 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` returns 2375 passed / 2 skipped / 0 failed or better, exit code 0, with no `Dfo.Server.Host` process running. | Contract C-7 |

## How the four arguments are obtained

`CurrentUserStateMessage(Character, Public, Equipment, Pets)`. The first, second and fourth come
from the session's runtime result, which a move does not change. The third is loaded after the move
through `ICharacterInventoryEntryDomainService.LoadAsync`, which already returns
`InventoryItemState` carrying the avatar detail the appearance needs.

The alternative — teaching the equipment container to answer a snapshot — was not taken. Its
restriction is deliberate and its reason is written at `ItemContainers.cs:767-784`: an equipment
snapshot needs avatar detail the store's read vocabulary does not carry. Lifting it means adding
that vocabulary, which is a larger change than this outcome needs, and the loader already has it.

The cost is one inventory read per move that changed the character's look, and none for a move that
did not. Ordinary drags inside the bag pay nothing.

## Evidence

- EV-001 — live, 2026-09-08: costumes worn, store correct, character unchanged until re-entry.
- EV-002 — this repository: the handler's single-frame delivery; `CurrentUserStateMessage` and its
  one existing construction site; `EncodeCurrentUserState` deriving the appearance itself.
- EV-003 — this repository: `grep -rn "ReplaceExact" Server/src` gives eight sites, none after a
  move.
- EV-004 — upstream, read from source: the acknowledgement followed by a conditional subtype-0
  refresh, the condition being that a changed slot is in the equipment list and is an appearance
  slot.

## Not in this Spec

- NA-001 — The character-selection roster. Defect C.
- NA-002 — Reconciling the three slot filters. Changes bytes; belongs with defect C.
- NA-003 — Upstream's pet, knight-shield and sort/lock sends. Excluded by the root contract.
- NA-004 — An inventory delta frame after a move. The acknowledgement already places the item and
  upstream sends none.
- NA-005 — Refreshing the session's inventory snapshot. See RQ-005.

## Assumptions

- AS-001 — Whether a move touched the appearance is decided from the request's two ends. The
  appearance is drawn from the equipment list, so a move that changed it named that list at an end.
  spec-001's reversal exchanges the two ends and changes neither list, so this holds in both
  directions. `ItemMoveResult` needs no set of changed slots.
- AS-002 — Which slots the appearance is drawn from is the subtype-0 encoder's own filter, used as
  it stands. If it is wrong, it is wrong for entry too, and that is defect C's to settle.
