# Completion Evidence: The stored appearance decoder is pinned

Verified against the worktree at
`E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character`, branch
`every-frame-draws-the-current-character`, on top of accepted baseline `addbeb52`.

## The run

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**2398 passed / 2 skipped / 0 failed**, exit 0.

| Assembly | Passed | Skipped | Baseline |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 60 |
| Dfo.Server.Networking.Tests | 52 | 0 | 52 |
| Dfo.Server.Protocol.Tests | 612 | 0 | 612 |
| Dfo.Server.Host.Tests | 6 | 0 | 6 |
| Dfo.Server.Persistence.Tests | 333 | 0 | 333 |
| Dfo.Server.Infrastructure.Tests | 259 | 2 | 259 (+2) |
| Dfo.Server.Application.Tests | 1076 | 0 | 1070 |

Every assembly equals its baseline except Application, which is 1070 + 6. The new file declares exactly six
`[Fact]` methods. The two skips are
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` and
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`; their
being skipped is the sign the archive loaded.

Output retained at `scratchpad/efdcc/spec001-verification.txt`. Searching it for `[FAIL]`, `error CS` and
`MSB<digit>` returns **0** matches, so no failure needed classifying by exception type and stack root.

**One run in this attempt did not happen and had to be redone.** An earlier invocation was launched from
the attempt's own folder rather than the worktree root, so MSBuild answered `MSB1009: project file does not
exist` and no test executed -- while the surrounding shell line still exited 0. It was caught by reading the
output file instead of the exit code. The run recorded above is the replacement, launched with an absolute
solution path.

## Condition by condition

| ID | Outcome | Evidence |
|---|---|---|
| C-1 | met | `Every_part_of_an_entry_comes_back_as_it_was_written` asserts all eight members individually; values satisfy the Spec's Distinguishability list. `Two_entries_come_back_in_the_order_they_were_written` covers ordering. |
| C-2 | met | `A_blob_that_promises_more_than_it_carries_is_refused_as_truncated` and `The_shortfall_is_found_after_the_entries_that_do_fit_have_been_read` assert the exact truncated message, not merely that something threw. |
| C-3 | met | `A_blob_carrying_more_than_it_declared_is_refused_for_its_trailing_bytes` asserts the exact trailing-bytes message. |
| C-4 | met | Five mutations recorded in `../coverage-record.md`, each restored with the file re-hashed to `e3bb9182e1f2175922625b95ee797358d18254c60e4dbaae04607ae84f1af6a2`. M1 reddened 3, M2 reddened 2, M3 reddened 1, R3 reddened 1 through a value mismatch, R2 reddened 0 and is recorded as the negative result it is. Independently reproduced by the reviewer. |
| C-5 | met | `../coverage-record.md` states the two gaps separately and names `TheRosterCarriesStoredAppearancePartsAndCopesWithNone` and `TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`, with the deleted case's blob and its three assertions read from `git show 35aaedf3^`. |
| C-6 | met | The run above. |

## Boundaries held

- **No production change.** `git diff --stat` is empty and `git status --short` lists only two untracked
  paths: this attempt's artifacts and the new check file.
  `Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs` hashes to
  `e3bb9182e1f2175922625b95ee797358d18254c60e4dbaae04607ae84f1af6a2`, matching
  `git show addbeb52:` for the same path.
- **No existing check altered.** No tracked file under `Server/tests` changed.
- Byte anchor `SelectCharacterAcceptedTranscriptTests` and both architecture check files are tracked and
  unmodified, and all pass in the run above.
- No new protocol frame; no layering or slice change.
- No git restoring command was used at any point. Mutations used file-level backups with `sha256sum`
  restoration.

## Review

`../reviews/change-review.md`, `verdict: approved` on re-review. Its first round returned
`changes_required` with two blocking findings; both were remediated and independently reproduced before the
re-review. Two of its four non-blocking observations were also acted on, because the root contract forbids a
count stated in prose that a reader cannot check.
