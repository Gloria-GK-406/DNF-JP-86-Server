---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-8, SC-9, SC-7, SC-10, SC-11]
depends_on: []
---

# TaskContract: The session copy keeps up

## Context

### Source Request

The root task's fourth and last outcome, in the order the owner fixed: `#19 -> #16 -> #14 -> #15`. Issue
#15 reads: 「服务端：会话的背包快照在物品移动后不刷新」. The owner's choice of that order said what the
first step is: 「#15 的第一步是枚举快照读者，枚举完再定三选一」.

### Current Situation

The session holds a copy of the character's inventory, taken when the character was selected. Read from
this repository at `1d887c59`:

- The copy is replaced through `ISelectedSessionRuntimeDirectory.ReplaceExact`, which `Server/src` calls
  from six places -- `SelectCharacterLifecycleServices`, `DungeonClearSlice`, `MonsterDeathSlice`,
  `AcceptQuestHandler`, `FinishQuestHandler`, `TutorialLevelUpSlice` -- plus its interface and
  implementation. **None of them follows an item move.**
- `MoveItemSpaceSlice.cs:814` carries a remark that states that count in prose. It says "eight places",
  counting the interface and implementation with the call sites, and this work is expected to falsify it.
- The one reader visible today is the dungeon selection screen, which takes its equipment from the copy.

`ce6839ed` built its appearance frame from the store rather than from the copy, because building it from
the copy would have sent the client the picture it already had -- which is the defect that work existed to
remove. So the frame and the copy now disagree in the open, deliberately, and nothing has closed that.

**The visible reader is not the set of readers.** That distinction is what the previous outcome got wrong
in a different place: it enumerated from the paths its report named instead of from the dispatch, and
missed one of four. Here the report names one screen.

## Goal

The readers of the session's inventory copy are known rather than assumed, and a screen that reads it shows
what the character has after a move -- by whichever treatment that enumeration justifies, with the reason
it was chosen over the others written down.

## Scope

### In

- The complete set of readers of the session's inventory copy, derived from the code.
- A screen that reads the copy showing what the character has after a move.
- The choice among the available treatments, made from the enumeration and recorded with what was found.
- Every refusal and admission path this alters, enumerated forward and reverse from the code.
- Correcting repository records this falsifies, including the count stated in prose at
  `MoveItemSpaceSlice.cs:814`.

### Out

- Issues #17 and #18, which the owner placed outside this batch.
- A new protocol frame, and any change to a frame's field layout.
- Making the copy authoritative, or removing it. Either is a storage-shaped outcome with its own boundary.
- Refreshing the copy from anything other than a move. The other six call sites are as they are.

## Constraints

- **The enumeration comes before the choice, and the choice cites it.** A treatment picked first and
  justified afterwards does not satisfy this contract even if it is the same treatment.
- **Enumerate from the dispatch, not from the reader the report names.** A reader reached across a command
  or message boundary is named by the record's type, not by a method; a reader resolved through a registry
  means going and reading that registry; and a value constructed here may reach something that runs later.
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
- Byte anchor `SelectCharacterAcceptedTranscriptTests` stays byte-identical.
- A check run that fails is classified by exception type and stack root before it is judged. Run output is
  kept in a file; a summary line is not evidence, and neither is an exit code.
- **At least one mutation must strike the clause the outcome's own safety or correctness argument rests
  on**, not only the clauses the change was aimed at. The previous outcome's mutation set was silent about
  exactly that clause until a reviewer wrote the missing one.
- A check asserts the outcome, not the absence of one outcome. A negative assertion let a refusal
  impersonate a success in the previous outcome, and it passed for two rounds.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Mutation checks use file-level
  backups plus `sha256sum`, restored and re-hashed immediately after each use, and the hash a record states
  is the one a reader can compute on the delivered file.
- Files are written with binary I/O, and `git diff --stat` after each edit confirms no line-ending rewrite.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main checkout's
  `ClientHack/` are not touched.
- `E:/Project/DNF/ServerS4A21` is a read-only reference implementation and is never written to.
- A record never claims more than its evidence: "read from upstream source", "read from this repository",
  and "observed live" are marked apart. No count a reader cannot check.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | Every reader of the session's inventory copy is named, with the method that found them and the frontiers the walk crossed. | The enumeration, with a command a reader can rerun. |
| C-2 | The enumeration distinguishes what each reader takes from the copy: a reader that only wants the character's identity is not the same as one that draws its equipment. | The enumeration. |
| C-3 | A screen that reads the copy shows what the character has after a move. | A check that performs a move and then reads that screen, asserting the outcome rather than the absence of one. |
| C-4 | The treatment is chosen from the enumeration, and the record says why it was chosen over each of the others -- including what each would have cost the readers found in C-1. | The recorded decision. |
| C-5 | Readers that were not meant to change do not change. | Checks or a recorded reading covering each reader the treatment touches. |
| C-6 | Every refusal and admission path this work adds, removes, or changes is listed in both directions, derived from the code. | The change list with its method. |
| C-7 | The checks are load-bearing, and at least one mutation strikes the clause the correctness argument rests on rather than only the clause the change was aimed at. | The mutation record, saying which cases reddened and **how** each reddened. |
| C-8 | Repository records this work falsifies are corrected, including the prose count at `MoveItemSpaceSlice.cs:814`. | The corrected records, each naming what falsified it. |
| C-9 | The check suite passes, with counts stated against the 2409 / 2 / 0 baseline, and any failure classified by exception type and stack root before it is judged. | The run output retained and searched for `[FAIL]`, `error CS` and `MSB<digit>`. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Inherited from the root TaskContract's `task` row: 「先扫尾巴：#14–#19 一起」 then 「#19 → #16 → #14 → #15（推荐）」, whose option text named this outcome's first step as the enumeration. | Enumerating the readers of the session's inventory copy and making a screen that reads it show what the character has after a move, by the justified treatment -- in the shared root task worktree, with one accepted-result commit after review and fresh verification. |

## Assumptions

- The three treatments recorded in issue #15 -- reload and `ReplaceExact` after a move; keep in the copy
  only what a move cannot change; repair the one visible screen -- are candidates, not a closed set. If the
  enumeration shows a fourth, it is considered on the same terms.
- The count at `MoveItemSpaceSlice.cs:814` counts the interface and implementation among its "eight
  places". Whether that is the count worth stating is part of what C-8 settles.
