---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: A restated move does not undo itself

## Delivered

- One rule, written once: *a request names an item's identity on the end that item is coming from; if the
  end it names is empty and the store holds that item at the other end, the move it describes has already
  been carried out.* `AlreadyCarriedOut` in `MoveItemSpaceSlice.cs`.
- Applied twice on each of the four paths `HandleAsync` dispatches to -- putting on, taking off, carrying
  across, and moving inside one list. Eight judgements where the baseline had one.
- `MoveItemSpaceCommand` carries `DestinationInstanceValue`, which the wire had always supplied and the
  command boundary had been dropping.
- Eight checks in `EitherEndMayBeHoldingItTests` (14 -> 22), covering all three restatement shapes and the
  five shapes that must still be carried out.
- Four records corrected, one of them twice.

## Verification

- `verification/completion-evidence.md` -- 2409 passed / 2 skipped / 0 failed against a 2401 / 2 / 0
  baseline, condition-by-condition outcomes for C-1..C-8, and the boundary proofs.

## Repository Result

- Changed paths: four tracked files -- `MoveItemSpaceSlice.cs`, `EitherEndMayBeHoldingItTests.cs`,
  `protocol/game/client-to-server/0x0013.md`, the root map -- plus this attempt's untracked artifact folder.
- Baseline or accepted result: built on `19fa03d6`.
- Parent rollback required: no.

## Successor Guidance

- **Nobody has seen a restatement.** The repair assumes one carries the original frame's bytes. If the
  client rebuilds the frame from its new state, a restatement is byte-identical to a fresh drag and the
  guard never fires. **This work may have fixed nothing observable, and no check here can tell.** The way
  to settle it is a capture at a running client, which needs the owner.
- **What the two instance values mean is not settled either, and two readings are loose in the delivered
  artefacts.** Reading A -- each end names the item coming from it -- is what the code's remarks say.
  Reading B -- each end names its own occupant, zero when empty -- is what `spec.md`'s AC-005 says. Both fit
  all twelve observations. **Under reading B the one misfiring shape cannot exist at all**, so settling this
  would turn the guard's safety from evidence into proof. Same capture, same owner.
- **The one shape that could misfire**: a client naming the identity of the piece it is moving on an empty
  destination end. All four clauses would hold on a legitimate move. The eleven observed put-ons left that
  field zero.
- **Enumerate from the dispatch, not from the paths the report names.** The fourth path was missed exactly
  that way, and the justification written for guarding the other three applied to it word for word.
- **A mutation set can be load-bearing about what it was aimed at and silent about what matters most.** All
  three delivered mutations struck naming clauses; none struck the occupancy clause the whole safety
  argument rests on. The reviewer wrote that one. When a guard's safety rests on a specific clause, mutate
  *that clause* before claiming the guard cannot misfire.
- **A negative assertion can let a refusal impersonate a success.** A check named "a swap is carried out"
  asserted only that the answer was not `MoveAlreadyMade`; the move was actually refused as
  `ItemCannotOccupySlot` and nothing was ever swapped. It passed for two rounds. Assert the outcome, not
  the absence of one outcome.
- **A record that has been corrected can go stale a second time.** `0x0013.md` was corrected in this
  attempt and then corrected again, for a path count this attempt itself changed.

## Artifacts

- `spec.md` -- see "The Rule, Stated Once"
- `behavior-change-list.md` -- including "The one shape that could misfire", "Decided and not done", and
  the four mutations
- `reviews/change-review.md` -- `approved`, after three rounds of `changes_required`
