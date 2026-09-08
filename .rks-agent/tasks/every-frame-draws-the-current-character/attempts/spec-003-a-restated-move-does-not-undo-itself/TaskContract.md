---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-6, SC-7, SC-10, SC-11]
depends_on: []
---

# TaskContract: A restated move does not undo itself

## Context

### Source Request

The root task's third outcome, in the order the owner fixed: `#19 -> #16 -> #14 -> #15`. Issue #14 reads:
「服务端：客户端重述一次已完成的脱下，会把装扮自己穿回身上」. Asked whether to proceed with it after
being told it is the only one of the four never observed live and that repairing it adds a refusal path,
the owner said 「可以」.

### Current Situation

Since `addbeb52` a move reads whichever of its two named ends holds something. That rule is what made
taking a costume off work at all, and it is also what makes a restated take-off read as the reverse move.

Read from this repository at `19fa03d6`:

- `MoveItemSpaceRequest` carries an instance value on **both** ends (`MoveItemSpaceSlice.cs:33` and `:37`),
  decoded from `body[3..7]` and `body[14..18]`, the second guarded by `body.Length >= 18`.
- `MoveItemSpaceCommand` carries only `SourceInstanceValue` (`:249`). The destination one is decoded and
  then dropped at the command boundary (`:805`).
- `PutOnAsync` already guards one direction: when the source slot is empty, the destination is worn, and
  the request named `SourceInstanceValue` equal to the worn item's id, it answers
  `ItemMoveStatus.MoveAlreadyMade`. It was copied from upstream
  `InventoryMoveService.IsEquipmentClientEchoWhenSourceMissing`, whose condition is written for one
  direction only, so both servers miss the other.

**The restated take-off does not reach that guard at all**, and this is the fact the repair turns on. After
a take-off has been carried out, the source end -- the avatar page slot the player let go over -- genuinely
holds the costume. A verbatim restatement of the same frame therefore finds a non-null piece at the source
and proceeds down the ordinary put-on path, wearing it again.

Observed live, and the two observations point opposite ways:

- 2026-09-06, eleven put-ons: the item's identity rode on the **first** end (`0:182->3:0` and the like).
- 2026-09-08, one take-off: the item's identity rode on the **second** end -- `1:0->3:1`, instance values
  `0 / 413560000`, where `413560000` was the hair costume then worn in equipment slot 1 and avatar slot 0
  was the empty cell the player released over.

So a fresh drag names the item on the end it is coming **from**, and a take-off names it on the end it is
coming **from** as well -- which for a take-off is the destination field, because the client puts the cell
the player released over in the source field.

## Goal

A take-off the client restates leaves the character wearing what the player took off -- or, if a
restatement genuinely cannot be told apart from a fresh drag, that is established and written down instead
of guessed at.

## Scope

### In

- Telling a restated move apart from a fresh drag, using what the client itself names, or establishing
  that it cannot be done.
- Whatever is needed to carry the destination end's instance value to the point where the decision is
  made, if the decision needs it.
- Every refusal and admission path this alters, enumerated forward and reverse from the code.
- Correcting repository records this falsifies.

### Out

- The session's inventory copy (issue #15). It is the next outcome.
- Issues #17 and #18, which the owner placed outside this batch.
- The creature-and-earring collision recorded as issue #13. A judgement was once added at this exact spot
  and taken out again because it reached that case too; this attempt must not reintroduce it by accident.
- A new protocol frame, and any change to a frame's field layout.
- Changing what a fresh drag does. This outcome only concerns frames that describe a move already made.

## Constraints

- **A guard added here must be unable to refuse a legitimate fresh drag.** Its signature has to be the
  client's own naming contradicting the store, not a heuristic about what a move looks like. If no such
  signature can be found, the outcome is the finding, not a guess.
- Layering: Data -> Abstractions -> Application -> {Infrastructure, Persistence, Networking} -> Protocol ->
  Host.
- Slice architecture (`AGENTS.md`): one directory per `Protocol/<Endpoint>/<Protocol>/`, no cross-protocol
  references inside a slice, one wiring point in `ProtocolModule` per protocol, no nested dispatch in a
  command handler, and every command that can reach a durable write is transactional.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` keep passing; neither their
  predicate nor their named exemption list is relaxed or extended.
- `TreatWarningsAsErrors` is on and CS9113 is an error.
- No existing check's expected value or assertion is rewritten to accommodate an implementation. A rewrite
  that corresponds to this authorized behaviour change is allowed, and each one says which change it
  corresponds to.
- Byte anchor `SelectCharacterAcceptedTranscriptTests` stays byte-identical. This outcome is not authorized
  to move it.
- A check run that fails is classified by exception type and stack root before it is judged. Run output is
  kept in a file; a summary line is not evidence, and neither is an exit code.
- At least one mutation must redden a check through a **value mismatch** rather than a raised exception.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Mutation checks use file-level
  backups plus `sha256sum`, restored and re-hashed immediately after each use.
- Files are written with binary I/O, and `git diff --stat` after each edit confirms no line-ending rewrite.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main checkout's
  `ClientHack/` are not touched.
- `E:/Project/DNF/ServerS4A21` is a read-only reference implementation and is never written to.
- A record never claims more than its evidence: "read from upstream source", "read from this repository",
  and "observed live" are marked apart. **In particular, no record may present the shape of a restatement
  as observed.** One take-off has been observed; no restatement of one has.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | A frame that names an item's identity on one end while the store holds that item on the other end, with the named end's counterpart empty, does not carry out the move it describes. | A check exercising that frame, asserting the store is unchanged and the answer says the move was already made. |
| C-2 | A fresh drag with the same two cells is carried out. The check that proves C-1 must fail if the guard is widened to catch it. | A check for the fresh drag, plus a mutation of the guard that reddens it. |
| C-3 | The guard cannot fire on any frame whose naming agrees with the store, including a swap onto an occupied slot, whatever the instance values say. | Checks covering the swap and the occupied-destination cases. |
| C-4 | Whether a restatement carries the same bytes as the original is stated as an assumption rather than as an observation, together with what the guard does if it does not. | The record. |
| C-5 | Every refusal and admission path this work adds, removes, or changes is listed, in both directions, derived from the code rather than from the list of intended edits, naming the frontiers the walk crossed. | The change list with its method. |
| C-6 | The creature-and-earring case recorded as issue #13 behaves exactly as it did before this change. | A check or a recorded reading proving the guard does not reach it. |
| C-7 | Repository records this work falsifies are corrected, including the remarks that describe the reverse direction as unguarded. | The corrected records, each naming what falsified it. |
| C-8 | The check suite passes, with counts stated against the 2401 / 2 / 0 baseline, and any failure classified by exception type and stack root before it is judged. | The run output retained and searched for `[FAIL]`, `error CS` and `MSB<digit>`. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Inherited from the root TaskContract's `task` row: 「先扫尾巴：#14–#19 一起」 then 「#19 → #16 → #14 → #15（推荐）」; and 「可以」 in answer to being told this outcome is the only one never observed live and that repairing it adds a refusal path. | Telling a restated move apart from a fresh drag inside `MoveItemSpaceSlice`, carrying the destination instance value to where that decision is made, and the records that follow -- in the shared root task worktree, with one accepted-result commit after review and fresh verification. |

## Assumptions

- **That a restatement carries the original frame's bytes is an assumption, not an observation.** It is the
  premise the whole repair rests on. If the client instead rebuilds the frame from its new state, the
  restatement names the item on the source end, is byte-identical to a fresh drag, and nothing here can
  tell them apart -- the defect would remain and the guard would simply never fire.
- The eleven put-ons and the one take-off are the whole of the live evidence about which end carries the
  identity. No restatement has been observed at all.
