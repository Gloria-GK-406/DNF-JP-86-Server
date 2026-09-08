---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: pending
active_attempt: spec-003-the-selection-screen-shows-what-is-worn
last_transition: child-created
transition_evidence: >-
  workspace-ready: worktree created from 8247182c at
  E:/Project/DNF/DNF-JP-86-Server/.worktrees/dressing-a-character-and-seeing-it on branch
  dressing-a-character-and-seeing-it; git-dir and common-dir differ and superproject is empty, so it
  is a linked worktree of the main checkout and not a submodule. Startup artifacts copied to
  identical repository-relative paths and hashed equal on both sides (TaskContract.md
  77224dc8fbbde638eb16b9904b89d097063e81f5688a34d87a3201d430b15e21, routing-decision.md
  a74ae39684480fad4d02321cb468a87e7863c2a79157e0e9f92201b253e9bb5c); source copies then removed and
  the main checkout proved clean at 8247182c on main; the two unrelated worktrees untouched.
  Baseline suite inside the worktree returned 2361 passed / 2 skipped / 0 failed, exit code 0.
  gaps-remain: contract revision 1 read against the accepted baseline; SC-1 through SC-8 and SC-10
  have no accepted evidence and SC-9 holds only at the baseline, so nine of ten conditions carry a
  remaining gap; every constraint classified aligned, none at risk or violated, because no product
  change exists yet. safe-successor: the gaps divide into three bounded outcomes with separate
  acceptance boundaries (recorded under Multi-Spec Boundary), all inside the root task
  authorization, and the first of them -- a move that reads both of the ends it was given -- is the
  one the other two are written against. child-created: identity spec-001, folder
  attempts/spec-001-a-move-reads-both-of-its-ends/, both paths canonicalised and proved inside the
  durable task root; depends_on is an empty sequence, so the linkage check returns no error; no
  active child and no running row existed before this one; the child TaskContract is state ready
  with C-1..C-9 each carrying observable evidence, and its routing decision has SDD readiness,
  marginal control value and Single structural selection filled with only Controller Selection left
  for single-spec-workflow. child-sealed: spec-001 returned outcome completed with terminal stage
  verification. child-accepted: its verification artifact reads decision pass with 2375 passed / 2
  skipped / 0 failed, exit code 0; the one gated commit e766d97152a6147323d3279248858ffb1ef4918c has
  parent 8247182c6e555fd22204490272b6d2ada8008508, the branch advanced only to it, the task worktree
  was clean afterwards, and main is still 8247182c. gaps-remain: SC-4, SC-5 and SC-6 have no
  accepted evidence and SC-3, SC-8, SC-9 and SC-10 are in progress; every constraint still aligned.
  safe-successor: a read-only survey of both this repository and the upstream reference established
  that defect B needs no projection extracted -- the subtype-0 encoder already derives the
  appearance from the equipment it is handed -- so B remains one bounded outcome and the extraction
  stays with C, which needs the projection as a value. child-created: identity spec-002, folder
  attempts/spec-002-the-character-changes-as-you-dress-it/, both paths canonicalised and proved
  inside the durable task root; depends_on is an empty sequence; no active child and no running row
  existed; the child contract is ready with C-1..C-8 each carrying observable evidence and its
  routing decision has everything but Controller Selection, which single-spec-workflow completed.
  child-sealed: spec-002 returned completed at terminal stage verification, after an independent
  review returned changes_required on one blocking defect (the appearance was built before the
  acknowledgement was sent, so a raise took the acknowledgement with it) and then approved the fix.
  child-accepted: verification reads decision pass with 2384 passed / 2 skipped / 0 failed, exit
  code 0; the one gated commit ce6839ed1730747d81a71efcb7342d0b8668965b has parent
  e766d97152a6147323d3279248858ffb1ef4918c, the branch advanced only to it, the worktree was clean
  afterwards, and main is still 8247182c. gaps-remain: SC-6 has no accepted evidence and SC-3, SC-8,
  SC-9, SC-10 remain in progress; every constraint still aligned. safe-successor: one bounded outcome
  is left -- the character-selection screen -- and a checkable fact bounds it: that screen's frame is
  outside SelectCharacterAcceptedTranscriptTests while the entry burst's roster frame is inside it,
  so the reported screen can be answered without moving an anchored byte. child-created: identity
  spec-003, folder attempts/spec-003-the-selection-screen-shows-what-is-worn/, both paths
  canonicalised and proved inside the durable task root; depends_on is an empty sequence; no active
  child and no running row existed; the child contract is ready with C-1..C-8 and its routing
  decision is complete, its Controller Selection naming sdd-standard for the first time in this task
  with the coupled decisions that earn the Plan.
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/dressing-a-character-and-seeing-it/.rks-agent/tasks/dressing-a-character-and-seeing-it
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/dressing-a-character-and-seeing-it
  branch: dressing-a-character-and-seeing-it
  baseline: ce6839ed1730747d81a71efcb7342d0b8668965b
  baseline_evidence: >-
    PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
    Server/86JP.Server.slnx --nologo, run in the task worktree with no Dfo.Server.Host process
    running: Pvf 60, Protocol 585, Networking 52, Persistence 329, Host 6, Infrastructure 259 (+2
    skipped), Application 1070 -- 2361 passed / 2 skipped / 0 failed, exit code 0. The two skips are
    A_missing_archive_*, and their being skipped is the sign that the archive did load.
---

# Multi-Spec Map

## Multi-Spec Boundary

Three defects stand between a player and a costume, and no two share an acceptance boundary.

- **A** is judged by which moves are carried out and what the store then holds.
- **B** is judged by which frames leave the server after a move that already succeeded.
- **C** is judged by what one unrelated frame contains at character selection, in a code path the
  other two never touch.

Each can be accepted or rolled back on its own without leaving the other two half-done, which is
what makes them separate Specs rather than three stages of one. The reasoning is recorded in
`routing-decision.md`.

## Overall Direction

The three divide by where the answer has to arrive: in the store (A), on the wire during play (B),
and on the wire before play (C). A is first because it is the only one that changes what the store
ends up holding, and because it widens the set of checks the other two can be written against: until
a costume can come off, B can only exercise the putting-on direction. C touches neither and is last
only because it is the most independent.

One shared fact runs through all three: what a character wears lives in `item_slots` at
`list_type = 3`, slots 0..11 for the costume bar. A moves rows there, B reports them mid-session,
and C must read them instead of the stale blob. Nothing here fixes a shared abstraction into place
in advance -- each Spec decides its own local design.

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when
  partial mutation cannot be classified.

### Standing facts for every child

- Check command:
  `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
- **Confirm no `Dfo.Server.Host` process is running before every suite run.** A running host locks
  `Dfo.Server.Protocol.dll` under `Dfo.Server.Host/bin`, the build fails with MSB3027, and it looks
  like a broken suite when it is not. One was killed (PID 46856) during setup.
- A known flaky failure is judged by exception type plus stack root plus passing on a rerun, never by
  test name: `ObjectDisposedException: 'SQLitePCL.sqlite3'`, stack root
  `CharacterDatabaseSchema.KeepWritersAndReadersApart -> SqliteCommand.ExecuteScalar`.
- `.worktrees/client-debug-lab`, `.worktrees/entries-are-what-the-client-reads`, and the main
  checkout's `ClientHack/` are not touched. `E:/Project/DNF/ServerS4A21` is read-only.
- No git restoring command (`git checkout --`, `git restore`, `git stash`). Mutation checks use
  file-level backups plus `sha256sum`, restored and re-hashed immediately after each use.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | A move naming one empty end and one occupied end is carried out as the move it describes, for every served pair of lists, in both orders | satisfied | spec-001, accepted at `e766d971`; the served pairs are derived by probing the dispatch, and mutation M1 reddened 8 cases and nothing else in the suite | The owner has not re-tested live yet | aligned |
| SC-2 | A move naming two empty ends is still refused as "both slots empty" | satisfied | spec-001, `e766d971`; asserted over the same derived pairs | none | aligned |
| SC-3 | Every production refusal and admission path this work changes is listed forward and reverse, from the code rather than from the change's origin | in_progress | Both sealed results carry a change list; in spec-002 the independent review found one missing reverse path (a raise between commit and send) and it was added with that provenance | Defect C has its own list to write | aligned |
| SC-4 | After a successful move that changed what the character wears, the client is told, without re-entry | satisfied | spec-002, accepted at `ce6839ed`; mutation M1 reddened 3 cases and nothing else, and the guard that keeps a failed appearance from costing the acknowledgement is pinned by M3 | The owner has not re-tested live yet | aligned |
| SC-5 | A successful move that changed nothing worn does not send that frame | satisfied | spec-002, `ce6839ed`; four cases, and the reviewer's own always-true mutation showed those moves really do succeed | none | aligned |
| SC-6 | The character-selection screen shows what the character currently wears | not_started | none | The roster reads a stored blob nobody updates | aligned |
| SC-7 | A move's log line names list and slot at both ends, worded as ordinary operational logging | satisfied | spec-001, `e766d971`; the rendered line is pinned in three checks and mutation M3 reddened exactly those three | Judged against the owner's own stated criterion, not their acceptance; they can overturn it in a word | aligned |
| SC-8 | No protocol frame added; frames not authorized to change are unchanged | in_progress | spec-001 and spec-002: byte anchor untouched and passing at both, and neither production diff names a message-type enum | Defect C reconciles the appearance slot filters, which changes bytes -- that is where this is really tested | aligned |
| SC-9 | The full suite is no weaker than baseline | in_progress | 2384 passed / 2 skipped / 0 failed, exit 0, at `ce6839ed` | Must be proved again at the remaining accepted result | aligned |
| SC-10 | Repository records this work falsifies are corrected | in_progress | spec-001 corrected `AGENTS.md` and `0x0013.md`; spec-002 annotated `0x0013.md` and the 2026-08-26 trace where a true observation would otherwise read as a general rule | Defect C has its own records to check | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-a-move-reads-both-of-its-ends/TaskContract.md` | A move is carried out whenever either of the two ends it names holds something, and its record says which lists it was between | `SC-1`, `SC-2`, `SC-3`, `SC-7` | `sealed` | `completed` | `e766d971` |
| `2` | `spec-002` | `attempts/spec-002-the-character-changes-as-you-dress-it/TaskContract.md` | A move that changed what the character wears tells the client so, then and there | `SC-4`, `SC-5` | `sealed` | `completed` | `ce6839ed` |
| `3` | `spec-003` | `attempts/spec-003-the-selection-screen-shows-what-is-worn/TaskContract.md` | The character-selection screen draws each character wearing what it wears | `SC-6` | `running` | `pending` | `active` |
