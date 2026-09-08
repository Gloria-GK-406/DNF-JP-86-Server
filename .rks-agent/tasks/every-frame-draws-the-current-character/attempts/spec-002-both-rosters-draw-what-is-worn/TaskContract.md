---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-3, SC-4, SC-5, SC-7, SC-10, SC-11]
depends_on: []
---

# TaskContract: Both rosters draw what is worn

## Context

### Source Request

The root task's second outcome, in the order the owner fixed: `#19 -> #16 -> #14 -> #15`. Issue #16 reads:
「服务端：两个角色列表帧的外观来源不一致 —— 入场那一帧仍读创建时冻住的 blob」.

### Current Situation

Two frames draw an account's characters and they take their appearance from different places. Read from
this repository at `77ff3236`:

- The character-selection frame and the roster pushed after a character is created ask
  `CharacterRosterService.ListByAccountWithAppearanceAsync`, which reads what each character wears
  (`ICharacterItemTable.ReadWornByAccountAsync`) and hands it to `DrawnAppearance.Of`.
- The entry burst's roster frame is built by
  `SelectCharacterPreparationService.BuildRosterEntryAsync`, which calls
  `DecodeAppearances(entry.Public.Appearance.EncodedEntries)` -- the `characters.appearance_blob` column.

That column is written by exactly one statement in `Server/src`, `CharacterEntryTables.InsertAsync`, at
character creation, and by no `UPDATE` at all. So a character that has been dressed since it was made is
drawn wearing its current clothes on the selection screen and wearing what it was created in on the entry
roster.

`35aaedf3` drew the boundary there deliberately: the entry frame's bytes are inside the byte anchor
`SelectCharacterAcceptedTranscriptTests`, which pins 62 frames by SHA-256, and the selection frame's are
not. That was a decision to defer, not a finding that the blob is right, and `CharacterRosterService`'s own
remarks say the two disagree.

`spec-001` (`77ff3236`) put six checks on `DecodeAppearances` so that this attempt can tell whether it
changed behaviour it did not mean to.

**Whether the anchored bytes actually move is not known and must not be assumed.** If the transcript's
character wears nothing and carries an empty blob, both sources produce the same empty appearance and no
hash changes. If it wears something, hashes change. The premise carried in from issue #16 -- that this
work moves the anchor -- is a prediction, and this attempt measures it.

## Goal

A character is drawn wearing what it wears in both roster frames, from one projection rather than two
sources; and `characters.appearance_blob` together with its decoder is kept or retired according to
whether anything still reads it, with the search that decided it written down.

## Scope

### In

- The entry burst's roster frame taking its appearance from what each character wears, through the same
  projection the character-selection frame uses.
- Measuring what that does to the byte anchor, and accounting for every frame whose bytes move.
- Deciding the fate of `characters.appearance_blob` and `SelectCharacterPreparationService.DecodeAppearances`
  from whether a reader remains, and recording which readers were searched for and what was found.
- Every refusal and admission path this change alters, enumerated forward and reverse from the code.
- Correcting repository records this work falsifies, including counts stated in prose and the remarks in
  `CharacterRosterService` and `DrawnAppearance` that describe the two frames as disagreeing.

### Out

- Which slots count as appearance (issue #17). `DrawnAppearance` already answers that for both frames, and
  changing the answer is a different outcome the owner placed outside this batch.
- The session's inventory copy (issue #15). It is the next outcome and has its own enumeration.
- The client-echo guard (issue #14).
- A new protocol frame, and any change to a frame's field layout. Values inside the existing appearance
  block may change; its shape may not.
- A schema migration of existing saves. If retiring the column turns out to need one, that is a storage
  outcome this attempt is not authorized to deliver -- stop and return to the root map.

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
  that corresponds to this authorized behaviour change is allowed, and each one says which change it
  corresponds to.
- **Byte anchor**: whatever the measurement shows, the account is frame by frame. Every frame whose hash
  changes is named with its old and new value and the reason it moved; every other frame is byte-identical,
  and that is stated as a measured fact rather than an expectation.
- The entry burst must not become able to fail for a reason it could not fail for before. Reading what a
  character wears can fail on its own; `CharacterRosterService.ListByAccountAsync` was deliberately kept
  cheap for exactly that reason, and this attempt must not undo that reasoning by accident.
- A check run that fails is classified by exception type and stack root before it is judged, and a rerun
  that comes back green does not by itself excuse it. Run output is kept in a file; a summary line is not
  evidence, and neither is an exit code.
- At least one mutation must redden a check through a **value mismatch** rather than a raised exception.
  A mutation set that only alters control flow proves less than its red count suggests; `spec-001`
  established this the hard way.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Mutation checks use file-level
  backups plus `sha256sum`, restored and re-hashed immediately after each use.
- Files are written with binary I/O, and `git diff --stat` after each edit confirms no line-ending rewrite.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main checkout's
  `ClientHack/` are not touched.
- `E:/Project/DNF/ServerS4A21` is a read-only reference implementation and is never written to.
- A record never claims more than its evidence: "read from upstream source", "read from this repository",
  and "observed live" are marked apart. No count a reader cannot check.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | A character wearing something is drawn wearing it in the entry burst's roster frame, not wearing what it was created in. | A check that dresses a character after creation and reads that frame. |
| C-2 | The two roster frames cannot drift apart again: a change to the single projection they share moves both, and a check fails if they are fed from different sources. | A convergent check plus a mutation of the shared projection that reddens both sides. Two checks that each pin the same expected bytes do not satisfy this. |
| C-3 | The effect on the byte anchor is measured, not predicted. Every frame whose hash changes is named with its old value, its new value and the reason; every other frame is byte-identical. | The frame-by-frame account and the anchor run. If no hash changes, that is stated as a measured result together with why the transcript's character does not exercise the difference. |
| C-4 | `characters.appearance_blob` and `DecodeAppearances` are kept or retired according to whether a reader remains, and the decision names the readers searched for, the search method, and what was found. | The reader enumeration and the recorded decision, whichever way it went. |
| C-5 | Entering the world does not become able to fail for a reason it could not fail for before, or, if it does, that is stated with the reason it is acceptable. | The account of what the entry path now reads and what happens when that read fails. |
| C-6 | Every refusal and admission path this work adds, removes, or changes is listed, in both directions, derived from the code rather than from the list of intended edits, and naming the frontiers the walk crossed. | The change list with its method. |
| C-7 | Repository records this work falsifies are corrected, including the remarks that describe the two frames as disagreeing and any count stated in prose. | The corrected records, each naming what falsified it. |
| C-8 | The check suite passes, with its pass and skip counts stated against the 2398 / 2 / 0 baseline, and any failure classified by exception type and stack root before it is judged. | The run output retained, searched for `[FAIL]`, `error CS` and `MSB<digit>`, not only its summary line. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Inherited from the root TaskContract's `task` row: the owner chose 「先扫尾巴：#14–#19 一起」 and then 「#19 → #16 → #14 → #15（推荐）」. Issue #16 is the second item in that order. | Making the entry burst's roster frame draw from what is worn, deciding the stored column's fate by evidence, and the records that follow -- inside the shared root task worktree, with one accepted-result commit after review and fresh verification. |

## Assumptions

- `ICharacterItemTable.ReadWornByAccountAsync` already exists and is what the selection frame uses, so the
  read this needs is present rather than new. Read from this repository at `77ff3236`.
- `spec-001`'s checks describe `DecodeAppearances` as it behaves now. If this attempt retires the decoder,
  removing those checks with it is a result rather than a regression; if it keeps the decoder, they must
  stay green.
