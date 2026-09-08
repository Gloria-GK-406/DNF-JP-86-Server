---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-2, SC-11]
depends_on: []
---

# TaskContract: The stored appearance decoder is pinned

## Context

### Source Request

The root task's first outcome, in the order the owner fixed: `#19 -> #16 -> #14 -> #15`. Issue #19 reads:
「服务端：DecodeAppearances 的解码与两条拒绝均无判据覆盖」.

### Current Situation

`SelectCharacterPreparationService.DecodeAppearances(byte[] blob)` is an `internal static` method in the
Application project (`Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`).
Read from this repository at `addbeb52`, it does this: a null or empty blob yields an empty list; otherwise
it reads one byte as a count, and for each of that many entries first refuses with
`InvalidDataException("Character appearance blob is truncated.")` when fewer than 23 bytes remain, then
reads `byte, int32, int32, byte[4], byte, int32, uint32, byte`; after the loop it refuses with
`InvalidDataException("Character appearance blob contains trailing bytes.")` when the stream is not
exhausted.

Nothing under `Server/tests` names it. The count comes from `grep -rn "DecodeAppearances"` over
`Server/src` and `Server/tests`, where the only matches under `tests` are compiled assemblies in `bin`.

The absence has two separate origins and the record has to keep them apart:

- **The two refusals have never had a check.** That gap predates all of this work.
- **The decoding of a non-empty blob used to be covered indirectly.**
  `SqliteCharacterLifecycleTests.TheRosterCarriesStoredAppearancePartsAndCopesWithNone` wrote a part into
  `characters.appearance_blob` and asserted the roster handed it back. `35aaedf3` replaced it with
  `TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`, because the old assertion was a statement of
  the defect -- the column is written once at creation and by nothing afterwards, so asserting it comes
  back out is asserting that the screen draws the character the player made rather than the one they have.
  Replacing it was right; the coverage it incidentally provided went with it, and that half was not
  replaced.

The method still has a production reader: the roster frame in the entry burst, through the call at
`SelectCharacterPreparationService.cs:226`. The root task's next outcome is expected to remove that reader,
which is why this one comes first -- checks written now are what shows whether that change altered
behaviour it did not mean to.

`Dfo.Server.Application.csproj` already declares `InternalsVisibleTo` for `Dfo.Server.Application.Tests`,
so the method is reachable from a check without changing visibility.

## Goal

The behaviour of `DecodeAppearances` is written down as checks that fail when that behaviour changes, and
the record says which part of the missing coverage was always missing and which part the previous round
removed.

## Scope

### In

- Checks covering the decoding of a non-empty blob, the truncated-blob refusal, and the trailing-bytes
  refusal.
- A record separating the pre-existing gap from the coverage removed by `35aaedf3`, naming the replaced
  case and what it used to cover.

### Out

- Any change to production code. This outcome adds checks to behaviour that already exists.
- Changing the visibility of `DecodeAppearances` or moving it.
- Whether the decoder or `characters.appearance_blob` should survive at all. That is decided by the next
  outcome, from whether a reader remains.
- The empty-blob and null-blob cases as a separate deliverable. Covering them is welcome and cheap, but
  they are not what is missing.

## Constraints

- Layering: Data -> Abstractions -> Application -> {Infrastructure, Persistence, Networking} -> Protocol ->
  Host.
- `TreatWarningsAsErrors` is on and CS9113 is an error.
- No existing check's expected value or assertion is rewritten. This outcome changes no behaviour, so there
  is nothing an existing check could legitimately be adjusted for.
- The byte anchor `SelectCharacterAcceptedTranscriptTests` stays byte-identical. This outcome is not the
  one authorized to move it.
- A check run that fails is classified by exception type and stack root before it is judged; a green rerun
  does not by itself excuse it.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Mutation checks use file-level
  backups plus `sha256sum`, restored and re-hashed immediately after each use.
- Files are written with binary I/O, and `git diff --stat` after each edit confirms no line-ending rewrite.
- A record never claims more than its evidence: "read from upstream source", "read from this repository",
  and "observed live" are marked apart. No count a reader cannot check.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | A non-empty stored blob decodes to the entries it describes, field by field, with values that would expose a field read in the wrong order or at the wrong width. | The check, using distinguishable values rather than zeros. |
| C-2 | A blob whose entry count promises more than the bytes carry is refused with `InvalidDataException`. | The check, asserting the refusal rather than only that something threw. |
| C-3 | A blob carrying bytes beyond its declared entries is refused with `InvalidDataException`. | The check. |
| C-4 | Altering the decoder reddens these checks. | A mutation of `DecodeAppearances`, applied and reverted with `sha256sum` proof, with the reddened cases named and the count of them stated. |
| C-5 | The record states which half of the missing coverage was always missing and which half `35aaedf3` removed, and names the replaced case and its replacement. | The record. |
| C-6 | The check suite passes, with its pass and skip counts stated against the 2392 / 2 / 0 baseline, and any failure classified by exception type and stack root before it is judged. | The run output retained, not only its summary line. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Inherited from the root TaskContract's `task` row: the owner chose 「先扫尾巴：#14–#19 一起」 and then 「#19 → #16 → #14 → #15（推荐）」. Issue #19 is the first item in that order. | Checks over `DecodeAppearances` and the record separating the two coverage gaps, inside the shared root task worktree, with one accepted-result commit after review and fresh verification. No production change. |

## Assumptions

- `DecodeAppearances` still has a production reader at this baseline, so these checks describe live
  behaviour rather than dead code. If the next outcome removes that reader, deleting these checks along
  with the method is a legitimate result of that outcome and not a defect of this one.
