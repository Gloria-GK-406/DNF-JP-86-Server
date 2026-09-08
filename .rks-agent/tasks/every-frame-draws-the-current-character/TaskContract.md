---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Every frame draws the current character

## Context

### Source Request

The owner was asked what to do next and chose, in two steps:

> 先扫尾巴：#14–#19 一起

and then, when asked how to cut that batch:

> #19 → #16 → #14 → #15（推荐）

The second option read: 四条立一个 Multi Spec 根契约。#15 的第一步是枚举快照读者，枚举完再定三选一。#17 #18 留到批外。

### Current Situation

Four issues opened on 2026-09-08, immediately after the costume work merged at `addbeb52`, name debts that
work left behind. Each was recorded with its evidence type marked, and every citation in them was written
against this same revision.

**A character's appearance now has two sources that disagree.** `35aaedf3` made the character-selection
frame and the refresh pushed after character creation compute appearance from what the character currently
wears. The roster frame inside the entry burst still decodes `characters.appearance_blob`, a value written
once at character creation and never updated -- `Server/src` holds exactly one `INSERT` that names the
column (`CharacterEntryTables.InsertAsync`) and no `UPDATE` of it at all. So one character is drawn wearing
what it wears on the selection screen and wearing what it was created in on the entry roster. The boundary
was drawn there on purpose: the entry frame sits inside the byte anchor `SelectCharacterAcceptedTranscriptTests`
and the selection frame does not, and the owner reported the selection screen. That was a decision to defer,
not a finding that the blob is right.

**The decoder that still serves that stale path has no check on it.**
`SelectCharacterPreparationService.DecodeAppearances` refuses a truncated blob and refuses a blob with
trailing bytes, and neither refusal has ever had a check. Its decoding of a non-empty blob used to be
covered indirectly by a lifecycle check that `35aaedf3` replaced -- the old case wrote a blob by hand and
asserted the roster handed it back, which is asserting the defect itself -- and that half of the coverage
went with it. These two gaps have different origins and must not be recorded as one.

**A move that the client restates can now undo itself.** Since `addbeb52` a move reads whichever of its two
named ends holds something. A take-off the client repeats after it was already answered arrives with the
worn slot empty and the bag slot full, which reads exactly as the reverse move, so the costume goes back on.
The forward direction is guarded (`ItemMoveStatus.MoveAlreadyMade`); that guard was copied from upstream,
whose condition is written for one direction only, so both servers miss the reverse. It has never been
observed live.

**The session still carries the inventory it took at character selection.** `ISelectedSessionRuntimeDirectory.ReplaceExact`
is called from six places and none of them follows an item move, so every screen that reads that copy draws
the equipment the character had when it entered. `ce6839ed` built its appearance frame from the store
instead, precisely because building it from the copy would resend the picture the client already had -- so
frame and copy now disagree in the open. The dungeon selection screen is the one reader where that is
visible today.

## Goal

Every frame this server sends about a character is drawn from what that character is now, and a change the
client restates does not move the character backwards. Where a source of truth is retired, the reasoning
that retired it is recorded, and every behaviour this work changes is written down in both directions.

## Scope

### In

- The decoding of a stored appearance blob, and both of its refusals, covered by checks that fail when that
  decoding changes.
- The roster frame in the entry burst drawn from the same appearance source as the character-selection
  frame.
- The disposition of `characters.appearance_blob` and of the decoder that reads it, decided by whether any
  reader remains after that change and recorded with the evidence for the decision.
- A restated take-off no longer undoing itself -- or, if a restatement cannot be told apart from a genuine
  reverse move, that finding stated plainly with what was tried.
- The complete set of readers of the session's inventory copy, enumerated from the code.
- Those readers drawing what the character has after a move, by whichever of the available treatments the
  enumeration justifies.
- Every refusal and admission path this work alters, enumerated forward and reverse from the code rather
  than from a list of intended edits.
- Correcting repository records this work falsifies, including counts stated in prose.

### Out

- Which of the four disagreeing definitions of "a worn slot that counts as appearance" is the one the client
  draws from (issue #17). It needs the client measured or disassembled first; picking the one that looks
  tidiest would move bytes on an unproven basis.
- The orphan `item_slots` row (issue #18). What needs repair there is how such a row comes to be written,
  which is a diagnosis and not this repair, and the one lead on it is an unverified guess.
- A new protocol frame.
- Merging, pushing, opening a pull request, deleting a branch or worktree, and cleanup.

## Constraints

- Layering: Data -> Abstractions -> Application -> {Infrastructure, Persistence, Networking} -> Protocol ->
  Host.
- Slice architecture (`AGENTS.md`): one directory per `Protocol/<Endpoint>/<Protocol>/`, no cross-protocol
  references inside a slice, one wiring point in `ProtocolModule` per protocol, no nested dispatch in a
  command handler, and every command that can reach a durable write is transactional.
- `CommandHandlerArchitectureTests` and `NotificationHandlerArchitectureTests` keep passing; neither their
  predicate nor their named exemption list is relaxed or extended.
- `TreatWarningsAsErrors` is on and CS9113 is an error.
- No existing check's expected value or assertion is rewritten to accommodate an implementation. A rewrite
  that corresponds to an authorized behaviour change is allowed, and each one says which change it
  corresponds to.
- Byte anchor `SelectCharacterAcceptedTranscriptTests`: this work is expected to move bytes in it. Frames
  that change are named one by one with their new values and the reason; frames that should not change stay
  byte-identical.
- A check run that fails is classified by exception type and stack root before it is judged, and a rerun
  that comes back green does not by itself excuse it. Two failures in the previous round were let go
  without their identity captured; that is the failure this constraint exists to prevent.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Work has been lost in this
  repository that way. Mutation checks use file-level backups plus `sha256sum`, restored and re-hashed
  immediately after each use.
- Files are written with binary I/O. Writing a repository file through a text-mode handle on this platform
  rewrote a whole file's line endings and made a 98-line change appear as 1508 lines; `git diff --stat`
  after each edit is what caught it.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main checkout's
  `ClientHack/` are not touched.
- `E:/Project/DNF/ServerS4A21` is a read-only reference implementation and is never written to.
- A record never claims more than its evidence: "read from upstream source", "read from this repository",
  and "observed live" are marked apart. No count a reader cannot check.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | A non-empty stored appearance blob decodes to the entries it describes, a truncated blob is refused, and a blob with trailing bytes is refused -- each proven by a check that fails when that behaviour is altered. | The checks, plus a mutation of the decoder that reddens them. |
| SC-2 | The record separates the coverage this repository never had from the coverage the previous round removed, and says which is which. | The record, naming the replaced case and what it used to cover. |
| SC-3 | The roster frame in the entry burst and the character-selection frame draw a character's appearance from the same source, so a character wearing something new is drawn wearing it in both. | A check that fails if the two are fed from different sources, rather than two checks that each pin the same expected bytes. |
| SC-4 | Every byte-anchor frame whose bytes move is named with its new value and the reason it moved; every other anchored frame is byte-identical. | The anchor run plus the frame-by-frame account. |
| SC-5 | `characters.appearance_blob` and its decoder are kept or retired according to whether a reader remains, and the decision states which readers were looked for and what was found. | The reader enumeration and the recorded decision, whichever way it went. |
| SC-6 | A take-off the client restates does not put the costume back on -- or, if a restatement cannot be distinguished from a genuine reverse move, that is stated with what was tried and why no guard was added. | Either a check that separates the two cases, or the recorded finding that they cannot be separated. |
| SC-7 | Every refusal and admission path this work adds, removes, or changes is listed, in both directions, derived from the code rather than from the list of intended edits. | The change list, with the method used to derive it and the frontiers it crossed. |
| SC-8 | The readers of the session's inventory copy are enumerated completely, including any reached across a dispatch boundary or through a registry, and including values constructed here that an encoder consumes later. | The enumeration, naming each frontier and the symbol the next caller up actually writes down. |
| SC-9 | A screen that reads the session's inventory copy shows what the character has after a move, by the treatment the enumeration justifies. | A check exercising a move followed by that screen, plus the recorded reason the treatment was chosen over the others. |
| SC-10 | Repository records this work falsifies are corrected, including counts stated in prose rather than in code. | The corrected records, each naming what falsified it. |
| SC-11 | The project's check suite passes on the accepted result, with its pass and skip counts stated, and any failure classified by exception type and stack root before it is judged. | The run output retained, not only its summary line. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | The owner was asked "下一件做哪个？" and chose 「先扫尾巴：#14–#19 一起」, then was asked "这一批的边界怎么划？" and chose 「#19 → #16 → #14 → #15（推荐）」, whose option text read: 四条立一个 Multi Spec 根契约。#15 的第一步是枚举快照读者，枚举完再定三选一。#17 #18 留到批外。 | Issues #19, #16, #14 and #15 in that order: contract creation and revision, routing, one task worktree, configured execution and verification, one local accepted-result commit per outcome after review and fresh verification, and the exact workflow rollback of one unaccepted failed or blocked attempt. |

## Requires Confirmation

- Merging, pushing, opening a pull request, deleting a branch or worktree, and cleanup.
- Taking issue #17 or #18 into this work. The owner placed both outside this batch.

## Assumptions

- The citations carried over from issues #14, #15, #16 and #19 were written against `addbeb52`, which is the
  baseline here, so they are current. Each still gets checked where it decides something.
- `ReplaceExact` has six call sites plus its interface and implementation. A ninth match exists in
  `MoveItemSpaceSlice.cs` and is prose inside a remark that states that count -- that sentence is a record
  this work is expected to falsify, not a call site.
- The restated take-off has never been observed live. It is derived from the rule `addbeb52` introduced and
  from upstream's guard being written for one direction. Whether it is reachable in practice is not
  established, and a guard added for it is a new refusal path with the risks that carries.
