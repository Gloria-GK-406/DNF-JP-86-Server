---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: >-
  Child spec-001-the-stored-appearance-decoder-is-pinned created at
  attempts/spec-001-the-stored-appearance-decoder-is-pinned/ inside the task root, with a ready
  TaskContract (revision 1, C-1..C-6, parent ../../TaskContract.md, contributes_to SC-1/SC-2/SC-11,
  depends_on empty) and a routing decision whose Controller Selection is left for single-spec-workflow.
  No attempt row existed before this one and no child was active. Preceding safe-successor evidence --
  bounded route: pin the behaviour of SelectCharacterPreparationService.DecodeAppearances with checks, and
  record which half of the missing coverage was always missing and which half the previous round removed.
  It closes SC-1 and SC-2, needs no production change, and is the owner-fixed prerequisite for the attempt
  that replaces that decoder's only reader. It sits wholly inside the root task's authorized scope (issue
  #19, first in the owner's order), so the root task Authorization row covers it with no expansion.
  Preceding transitions:
  workspace-ready was established first: worktree created from addbeb52 at
  .worktrees/every-frame-draws-the-current-character on branch every-frame-draws-the-current-character;
  startup artifacts moved with matching sha256 on both ends (TaskContract.md 8eb5c334...,
  routing-decision.md 4c34fa68...) and the source copies removed; the main checkout is still on main at
  addbeb52 with an empty status; the worktree's only change is the untracked task folder; the baseline
  suite passed in the worktree. Root evaluation against that accepted baseline then found every one of
  SC-1..SC-11 at not_started with no accepted evidence, since addbeb52 is the commit that created these
  gaps rather than one that closed any. All eleven constraints are aligned: the baseline run is green, the
  byte anchor is intact, and nothing has been changed yet.
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character/.rks-agent/tasks/every-frame-draws-the-current-character
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/every-frame-draws-the-current-character
  branch: every-frame-draws-the-current-character
  baseline: addbeb52bffdd329a85f292f6784200d1146bb28
  baseline_evidence: >-
    In the worktree at addbeb52:
    `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
    exited 0 with 2392 passed, 2 skipped, 0 failed -- Pvf 60, Networking 52, Protocol 612, Host 6,
    Persistence 333, Infrastructure 259 (+2 skipped), Application 1070. The two skips are
    `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` and
    `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`;
    their being skipped is the sign the archive loaded. Output kept at
    scratchpad/efdcc/baseline.txt. No `Dfo.Server.Host` process was running.
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
| SC-1 | Blob decoding and both refusals proven by checks that fail when the behaviour is altered | not_started | none | All of it | aligned |
| SC-2 | The record separates the pre-existing coverage gap from the coverage the previous round removed | not_started | none | All of it | aligned |
| SC-3 | Entry roster frame and selection frame draw appearance from the same source | not_started | none | All of it | aligned |
| SC-4 | Every moved byte-anchor frame named with its new value and reason; others byte-identical | not_started | none | All of it | aligned |
| SC-5 | `appearance_blob` and its decoder kept or retired by whether a reader remains, with the search recorded | not_started | none | All of it | aligned |
| SC-6 | A restated take-off does not put the costume back on, or the finding that the two cannot be told apart | not_started | none | All of it | aligned |
| SC-7 | Every altered refusal and admission path listed in both directions, derived from the code | not_started | none | All of it | aligned |
| SC-8 | Readers of the session's inventory copy enumerated completely, across dispatch and registry frontiers | not_started | none | All of it | aligned |
| SC-9 | A screen reading that copy shows what the character has after a move, by the justified treatment | not_started | none | All of it | aligned |
| SC-10 | Falsified repository records corrected, including counts stated in prose | not_started | none | All of it | aligned |
| SC-11 | The check suite passes on the accepted result, with counts stated and any failure classified | not_started | none | All of it | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-the-stored-appearance-decoder-is-pinned/TaskContract.md` | The behaviour of `DecodeAppearances` is pinned by checks, and the record separates the pre-existing coverage gap from the one the previous round removed | `SC-1, SC-2, SC-11` | `running` | `pending` | `active` |
