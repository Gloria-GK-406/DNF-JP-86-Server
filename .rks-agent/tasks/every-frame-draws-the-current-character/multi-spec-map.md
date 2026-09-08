---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: >-
  Document/root-review.md approved and Document/root-verification.md passed on accepted 0476c922; root-full-suite.txt records 2417/2/0. Four unique sealed rows, no active child, all root conditions evidenced and constraints aligned.
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character/.rks-agent/tasks/every-frame-draws-the-current-character
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character
  branch: every-frame-draws-the-current-character
  baseline: 0476c922
  baseline_evidence: >-
    Accepted commit 0476c922 is the direct child of 1d887c59, after independent approved re-review and
    fresh full-suite verification: 2417 passed / 2 skipped / 0 failed. Original output is
    attempts/spec-004-the-session-copy-keeps-up/Document/full-suite-final.txt (7 assemblies;
    no FAIL, error CS or MSB-number markers). Acceptance proved a clean worktree and main addbeb52 unchanged.
---

# Multi-Spec Map

## Multi-Spec Boundary

- The four outcomes are accepted by different evidence and can be rolled back without each other: checks
  over a decoder; a byte-anchor movement with a frame-by-frame account; a refusal path with a
  forward-and-reverse change list; and an enumeration whose result then selects among three treatments with
  different costs.
- Sealing them together would let one rollback discard three accepted results, and would let the last
  outcome's undecided direction hold the first outcome's finished work.
- The order is the owner's: `#19 -> #16 -> #14 -> #15`. It is not a preference. The decoder must be pinned
  before the frame that reads it is changed, or the checks are written for code that is about to die.

## Overall Direction

Two of these outcomes touch how a character's appearance reaches the client and two touch how a change to a
character reaches the screens that draw it. They share one shared projection, `DrawnAppearance`, introduced
by `35aaedf3`, and one shared byte anchor, `SelectCharacterAcceptedTranscriptTests`.

The connection that matters between attempts: attempt 1 pins the behaviour of
`SelectCharacterPreparationService.DecodeAppearances`, attempt 2 removes that decoder's only production
reader, and attempt 2's own success condition then decides whether the decoder and
`characters.appearance_blob` survive. Attempt 1's checks are therefore expected to be the thing that proves
attempt 2 did not change behaviour it did not mean to -- and possibly the thing attempt 2 deletes. Both are
acceptable outcomes; which one happens is evidence, not preference.

Attempts 3 and 4 have no code dependency on each other, but both read
`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`, so they run serially in the
owner's order rather than in parallel.

Nothing here pre-creates a later Spec. Each is modelled from the accepted baseline when its turn comes.

## Environment Facts That Apply To Every Attempt

- Check command:
  `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
- **Before every check run, confirm no `Dfo.Server.Host` process is running.** A running host holds
  `Dfo.Server.Protocol.dll` under `Dfo.Server.Host/bin`, the build fails with MSB3027, and it looks like a
  broken suite when it is not.
- The 2 skips are `A_missing_archive_*`. Their being skipped is the sign the PVF archive did load.
- One known flake, judged by exception type and stack root and a green rerun, **never by test name**:
  `ObjectDisposedException: 'SQLitePCL.sqlite3'`, stack root
  `CharacterDatabaseSchema.KeepWritersAndReadersApart -> SqliteCommand.ExecuteScalar`.
- **Any other failure is classified by exception type and stack root before it is judged.** The previous
  round let two failures go without capturing their identity because the command grepped summary lines
  instead of keeping output. A green rerun does not excuse an unidentified failure. Keep run output in a
  file.

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial
  mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | Blob decoding and both refusals proven by checks that fail when the behaviour is altered | satisfied | `spec-001` at `77ff3236`; six cases plus five mutations in its `coverage-record.md`, one of them reddening through a value mismatch | none | aligned |
| SC-2 | The record separates the pre-existing coverage gap from the coverage the previous round removed | satisfied | `spec-001` at `77ff3236`; `coverage-record.md`, with the deleted case's blob and its three assertions read from `git show 35aaedf3^` | none | aligned |
| SC-3 | Entry roster frame and selection frame draw appearance from the same source | satisfied | `spec-002` at `19fa03d6`; mutation M-B on the shared projection reddens 6 cases across all three sides that draw a character | none | aligned |
| SC-4 | Every moved byte-anchor frame named with its new value and reason; others byte-identical | satisfied | `spec-002` at `19fa03d6`; all 62 hashes unchanged, and the frame is not in the anchor at all -- established, not assumed | none | aligned |
| SC-5 | `appearance_blob` and its decoder kept or retired by whether a reader remains, with the search recorded | satisfied | `spec-002` at `19fa03d6`; both kept as one unretired unit, decision and enumeration in its `behavior-change-list.md` D-1/D-2 | none; retiring the unit whole is successor work | aligned |
| SC-6 | A restated take-off does not put the costume back on, or the finding that the two cannot be told apart | satisfied | `spec-003` at `1d887c59`; the two are told apart by the request contradicting the store, and the one shape that could misfire is recorded rather than argued away | none in code; whether a restatement is byte-identical is unobserved and recorded as a premise | aligned |
| SC-7 | Every altered refusal and admission path listed in both directions, derived from the code | satisfied | Forward/reverse change inventories in spec-002, spec-003 and spec-004; latest independently reviewed at 0476c922 | none; concurrent aggregate overwrite and stale-session appearance suppression disclosed | aligned |
| SC-8 | Readers of the session's inventory copy enumerated completely, across dispatch and registry frontiers | satisfied | spec-004 reader-enumeration.md at baseline 1d887c59; 16 readers/12 files, dispatch and factory frontiers; independently checked | none | aligned |
| SC-9 | A screen reading that copy shows what the character has after a move, by the justified treatment | satisfied | 0476c922: real EnterSelectDungeonHandler after putting on and taking off; P2 removal causes wrong screen equipment | none in this boundary; PvP code-derived, concurrent freshness not guaranteed | aligned |
| SC-10 | Falsified repository records corrected, including counts stated in prose | satisfied | Corrections from all three behavior-changing attempts; latest AGENTS/call count, dungeon comment and historical source notes checked at 0476c922 | none | aligned |
| SC-11 | The check suite passes on the accepted result, with counts stated and any failure classified | satisfied | 0476c922: 2417 passed / 2 skipped / 0 failed, fresh raw full-suite-final.txt retained; accepted source hashes unchanged | none; root verification passed at 0476c922 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-the-stored-appearance-decoder-is-pinned/TaskContract.md` | The behaviour of `DecodeAppearances` is pinned by checks, and the record separates the pre-existing coverage gap from the one the previous round removed | `SC-1, SC-2, SC-11` | `sealed` | `completed` | `77ff3236` |
| `2` | `spec-002` | `attempts/spec-002-both-rosters-draw-what-is-worn/TaskContract.md` | Both roster frames draw a character's appearance from what it wears, and the stored column's fate is decided by whether a reader remains | `SC-3, SC-4, SC-5, SC-7, SC-10, SC-11` | `sealed` | `completed` | `19fa03d6` |
| `3` | `spec-003` | `attempts/spec-003-a-restated-move-does-not-undo-itself/TaskContract.md` | A move the client restates does not carry itself out again, or the finding that a restatement cannot be told apart from a fresh drag | `SC-6, SC-7, SC-10, SC-11` | `sealed` | `completed` | `1d887c59` |
| `4` | `spec-004` | `attempts/spec-004-the-session-copy-keeps-up/TaskContract.md` | The readers of the session's inventory copy are known, and a screen that reads it shows what the character has after a move | `SC-8, SC-9, SC-7, SC-10, SC-11` | `sealed` | `completed` | `0476c922` |

## Root Evaluation — 2026-09-08

Accepted baseline 0476c922 includes all four ordered results: 77ff3236, 19fa03d6, 1d887c59, 0476c922.
Root TaskContract revision 1 reloaded; SC-1..SC-11 have accepted code or recorded-finding evidence.
The later changes do not alter the stored decoder, shared appearance projection, move replay guard,
or byte-anchor and architecture predicates accepted earlier. Latest suite includes their checks.
Constraints remain aligned: same task worktree, untouched outside scopes and old six refresh sites,
no new frame/layout/registration, no relaxed guard tests, authorized expectations only, raw failure
identities retained, binary writes and mutation restore hashes. Concurrent aggregate overwrite,
replay semantics without live observation, and PvP only supported by source remain disclosed
limitations, not claims of live proof. Root reviewing-code review and fresh verification follow.

## Root Review

`Document/root-review.md`: independent reviewing-code, approved at accepted 0476c922. Disclosed concurrency and live-observation limitations are retained; no blocking finding remains.

## Root Verification

`Document/root-verification.md`: pass at accepted 0476c922, root-full-suite.txt 2417/2/0. Terminal validation passed; product unchanged, main addbeb52 unchanged. Final disposition: task branch/worktree preserved pending owner integration choice.
