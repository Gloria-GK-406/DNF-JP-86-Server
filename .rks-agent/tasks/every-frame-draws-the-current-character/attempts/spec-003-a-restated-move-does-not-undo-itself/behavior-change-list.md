# What this change makes possible, and what it stops making possible

Derived from the code at the changed revision, not from the list of edits that were intended.

## Method

Walk out from every changed symbol and ask at each frontier **what the next caller up actually writes
down**: an ordinary method gives its own name; a command handler gives the **command record's** type name;
a message handler gives a type resolved in `ProtocolModule`; "nothing -- a registry resolves it" means going
and reading that registry. Then walk **down**: a value constructed here reaches something that runs later.

Changed symbols: `MoveItemSpaceCommand` (a new member), `MoveItemSpaceCommandHandler.HandleMessageAsync`
(passing it), `Reversed`, the new `AlreadyCarriedOut`, and the four paths `HandleAsync` dispatches to: `PutOnAsync`,
`TakeOffAsync`, `CarryAcrossAsync`, and the same-list branch that hands to `bag.MoveToAsync`.

## Forward: what can now happen that could not before

**F-1. `MoveAlreadyMade` is reachable from seven conditions that could not produce it before.**

This is the entry a count makes honest, and the count is not one. Before this change there was **one**
guard: `PutOnAsync`, forward direction only -- the request names the item on the source end, the source is
empty, the worn slot holds it. `HandleAsync` dispatches to **four** paths, and the same sentence is now
applied twice on each, so there are **eight**, of which **seven are new**:

| Path | Forward (names the source end) | Reverse (names the destination end) |
|---|---|---|
| `PutOnAsync` | was already there | **new** |
| `TakeOffAsync` | **new** | **new** |
| `CarryAcrossAsync` | **new** | **new** |
| the same-list branch, before `bag.MoveToAsync` | **new** | **new** |

Counting method: `grep -c "AlreadyCarriedOut(command"` over the slice gives 8; on the baseline, the single
`MoveAlreadyMade` production point is in `PutOnAsync`.

The one the defect was reported about is `PutOnAsync`'s reverse: a restated take-off arrives on the put-on
path, because the client puts the cell the player released over in the source field, and after the take-off
that cell is the one holding the costume.

**The other six were not asked for by the issue, and the fourth path was missed on the first pass.** The
rule is one sentence, and a sentence that holds on three of four paths does not hold. The same-list branch
hands the move to `StoreBackedItemContainer.MoveToAsync`, which decides by the very rule this is about --
whichever end holds something is the end that moves -- so a restated drag inside the bag undid itself
exactly as the cross-list ones did. The container is never told what the client named, so the judgement is
made in the slice before handing over. **It was found by an independent review, not by the walk**, because
the walk started from the paths the report named instead of from the dispatch. That is the same failure
this project has recorded before, in a new place.

`A_restated_carry_across_does_not_carry_it_back` and `A_restated_drag_inside_one_list_does_not_drag_it_back`
cover two of them. The two on `TakeOffAsync` are reachable only from a frame shaped `equipment -> bag`,
which no observation has shown the client sending, and they are listed here rather than left to be found.

**Reached from**: `MoveItemSpaceCommand` across the dispatch boundary -- that is the symbol the handler
writes down, not the method name -- and from `MoveItemSpaceRequest` through
`ProtocolModule`'s decoder registration above it.

**F-2. A refused move now reads one slot it did not read before, on every path that can refuse early.**
`PutOnAsync`, `TakeOffAsync` and `CarryAcrossAsync` all used to reach their slot-admission refusals --
`NotWearable`, `ItemCannotOccupySlot` -- **before** reading the destination end at all. On those branches
the baseline read the destination zero times; it now reads it once. The same-list branch gains two reads it
did not make, since it used to hand straight to the container.

An earlier draft of this entry said the extra read applied to "two of the three paths" and that
`PutOnAsync`'s count was unchanged. That was wrong, and wrong by misreading the baseline: `PutOnAsync` read
the destination on both branches of `if (piece is null)`, which is not the same as reading it on both
branches of the refusals that return before that point. The correction came from review.

No answer changes and the store is not written. Nothing writes between the old read position and the new
one either -- `moves.WornSlotForAsync` resolves through `IEquipmentSlotDefinitionSource` and
`moves.MayOccupySlotAsync` through `PlacementForAsync` plus a synchronous `CanOccupySlot`, both archive
queries -- so the value read is the one that would have been read.

**F-3. `MoveItemSpaceCommand` carries `DestinationInstanceValue`.**
Defaulted, so every existing construction site compiles unedited. Counting method:
`grep -rn "new MoveItemSpaceCommand" Server` -- the handler, `Reversed`'s `with` expression, and the check
harness.

## Reverse: what can no longer happen that could before

**R-1. A restated move is no longer carried out, on any of the four paths.**
That is the change, stated as the removal it also is: eight shapes of frame that used to move an item now
do not.

**R-2. Nothing else was removed.** No refusal was deleted, no admission narrowed. Every move whose naming
agrees with the store behaves exactly as it did, which is what `The_same_two_cells_named_the_other_way_round_are_a_fresh_put_on`,
`Naming_the_worn_piece_on_the_end_that_holds_it_still_takes_it_off` and
`A_move_naming_neither_end_is_carried_out` hold it to.

## What does not reach the wire

A `MoveAlreadyMade` result is not `Moved`, so `ChangesWhatTheCharacterLooksLike` never runs for it and no
appearance frame is pushed. That is the whole point of the guard: the client is already showing the state
it is describing, and answering it with a picture would be the second half of the defect.

The client-facing code for `MoveAlreadyMade` is unchanged and is the same one `BothSlotsEmpty` uses
(`0x02`, which draws no message). Nothing new appears on the wire.

## Issue #13 is not reached

The creature-and-earring collision is a **successful swap**: both ends hold something. `AlreadyCarriedOut`
requires `atTheEndItNames is null`, so it cannot fire when both ends are occupied, whatever the instance
values say. A judgement was added at this exact spot once before and taken out again because it reached
that case; this one cannot, by the shape of its first clause rather than by an exemption.

**That is no longer only a reading.** `A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names`
is that shape -- both ends occupied, both instance values naming a piece -- and it answers `Moved`. Mutation
N4 removes the occupancy clause and turns it red, so the clause that keeps issue #13 out of reach is held by
a check rather than by an argument. An earlier draft of this section claimed less than the evidence
supports, which the review pointed out.

## Mutations

Each applied to a `sha256sum`-verified copy of `MoveItemSpaceSlice.cs` and restored immediately, the file
re-hashed after each to
`9f602eca49c6ca5eb9bf10141b5807a1ce7d2ff785819f0707aa286e9bda0150`.

**That hash is the file as delivered**, so a reader can compute it on the change under review and get the
same value. An earlier draft recorded a hash taken at the moment of an earlier mutation round, before the
remarks in this file were edited; the restoration was real but the number pinned a moment nobody else could
reach, which is the same as not pinning anything.

| Mutation | What was changed | Red | Reddened by | Which |
|---|---|---:|---|---|
| N1 | the reverse half of the rule removed from all four paths | 3 | **value mismatch**, `Expected: MoveAlreadyMade, Actual: Moved` | `A_restated_take_off_does_not_put_the_piece_back_on`, `A_restated_carry_across_does_not_carry_it_back`, `A_restated_drag_inside_one_list_does_not_drag_it_back` |
| N2 | the forward half told to look at the wrong end | 2 | **value mismatch**, both directions | `A_request_naming_the_worn_piece_on_its_empty_end_does_not_take_it_off`, `The_same_two_cells_named_the_other_way_round_are_a_fresh_put_on` |
| N3 | the identity equality dropped, leaving "one end empty, the other not" | 1 | **value mismatch**, `Expected: Moved, Actual: MoveAlreadyMade` | `A_request_naming_some_other_piece_on_its_empty_end_takes_the_piece_off` |
| N4 | the occupancy clause `atTheEndItNames is null` dropped | 2 | **value mismatch** on both | `A_swap_onto_an_occupied_worn_slot_is_carried_out_whatever_either_end_names`, `Dropping_a_piece_back_where_it_already_is_changes_nothing` |

**N4 was written by the reviewer, not by this attempt, and that is why it is here.** The three mutations
delivered all struck the naming clauses; none struck `atTheEndItNames is null`, which is the clause the
whole safety argument rests on and the one the new swap case exists to hold. A mutation set can be
load-bearing about the thing it was aimed at and silent about the thing that matters most, and this one
was. N4 also settles by measurement what had only been reasoned: occupancy is judged before identity, so
two pieces of one template at the two ends cannot read as the item having moved.

**N3 is the one that matters most among the three that were delivered, and it is not about the repair.** It shows what the guard would do if the
identity check were lost: refuse a legitimate move. The case it reddens is an existing one, written before
this attempt, and it going red is the evidence that the guard's safety rests on the contradiction rather
than on a resemblance. N2 shows the same thing from the other side -- a guard that ignores which end names
what refuses a fresh drag.

Run output kept in `scratchpad/efdcc/N1.txt`, `N2.txt`, `N3.txt`.

## Records corrected

| Record | What it said | What falsified it |
|---|---|---|
| `ItemMoveStatus.MoveAlreadyMade` remarks | the condition is a move naming the worn piece on its empty end | F-1: that is one of eight, and describes only the direction that was guarded |
| `MoveItemSpaceCommand` remarks | `SourceInstanceValue` "is here for one decision" and is the only one named | F-3: both ends' values are carried now, and the destination one used to be decoded and dropped |
| `Reversed` remarks | "the identity" belonged to the source end and does not travel | Both are zeroed now, and the reason is stated: carrying them over would judge the same frame twice with its ends swapped |
| `protocol/game/client-to-server/0x0013.md` | upstream's one-direction guard, "本仓照办"; then, in this attempt's own first correction of it, "three paths" and the universal claim | This server no longer copies only that half. The first correction was itself corrected: there are four paths, and the claim that a fresh drag never names an item on an end that does not hold it is evidence rather than proof |

## The one shape that could misfire

The claim that a fresh drag never names an item on an end that does not hold it is **not universally
true, and an earlier draft asserted it as though it were.** There is one frame that would fire the guard
while being a legitimate move: a client that names the identity of **the item it is moving** on an **empty
destination end**. All four clauses would then hold, and a genuine put-on would be refused.

Within the evidence there is, this does not happen -- the eleven observed put-ons all had an empty
destination end with `dstIV = 0`. But that is evidence, not a proof, and the difference matters here
because the whole safety argument was written as a proof.

**There is a second reading of the frame under which the misfire is impossible**, and the two readings
cannot be told apart from the twelve observations, both fitting all of them:

| Reading | `srcIV` | `dstIV` |
|---|---|---|
| A -- each end names the item coming **from** it | the item, on a put-on | the item, on a take-off |
| B -- each end names **its own occupant**, zero when empty | the item in the source cell | the item in the destination cell |

Reading B makes the guard impossible to misfire, because a named end that is empty would carry zero by
definition. This attempt's code remarks state reading A; `spec.md`'s AC-005 describes the swap case in
reading B's terms. **Both are in the delivered artefacts and they are not the same model.** Neither is
established, and settling it needs a capture the owner would have to make at a running client.

## Decided and not done

- **The same-list guard reads the two slots with two `ReadSlotAsync` calls rather than the container's
  `ReadSlotsAsync(first, second)`.** Raised in review and then withdrawn there: both reads are inside the
  same transaction, both are pure reads, and the values are the ones the combined call would return, so the
  only gain is tidiness. It was left alone rather than touching a production file that three rounds of
  checks and four mutations had already settled. Recorded so the question is closed rather than open.

## Known and left

- **That a restatement carries the original frame's bytes has never been observed.** It is the premise the
  repair rests on. If the client rebuilds the frame from its new state, a restatement is byte-identical to
  a fresh drag, nothing distinguishes them, the guard never fires and the defect stands. This costs
  nothing, because the guard refuses only a self-contradictory frame -- but it means **this work may have
  fixed nothing observable**, and no check here can tell.
- Issue #14 says "上游同样不设防，所以照抄不算错". That remains true of upstream and is why the omission was
  not a mistake at the time. It is no longer true of this server.
- The two new conditions on `TakeOffAsync` are reachable only from an `equipment -> bag` frame. No
  observation shows the client sending one.
