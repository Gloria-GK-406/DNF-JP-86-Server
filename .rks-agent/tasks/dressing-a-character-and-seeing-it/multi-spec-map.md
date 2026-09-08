---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: pending
active_attempt: spec-001-a-move-reads-both-of-its-ends
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
  for single-spec-workflow.
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/dressing-a-character-and-seeing-it/.rks-agent/tasks/dressing-a-character-and-seeing-it
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/dressing-a-character-and-seeing-it
  branch: dressing-a-character-and-seeing-it
  baseline: 8247182c6e555fd22204490272b6d2ada8008508
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
| SC-1 | A move naming one empty end and one occupied end is carried out as the move it describes, for every served pair of lists, in both orders | not_started | none | The three cross-list paths still read only their own source end | aligned |
| SC-2 | A move naming two empty ends is still refused as "both slots empty" | not_started | none | Has to be preserved through SC-1's change rather than assumed | aligned |
| SC-3 | Every production refusal and admission path this work changes is listed forward and reverse, from the code rather than from the change's origin | not_started | none | Whole enumeration outstanding | aligned |
| SC-4 | After a successful move that changed what the character wears, the client is told, without re-entry | not_started | none | The handler sends only its acknowledgement | aligned |
| SC-5 | A successful move that changed nothing worn does not send that frame | not_started | none | No condition exists to decide it | aligned |
| SC-6 | The character-selection screen shows what the character currently wears | not_started | none | The roster reads a stored blob nobody updates | aligned |
| SC-7 | A move's log line names list and slot at both ends, worded as ordinary operational logging | not_started | none | Today it names slots only; the diagnostic wording was removed from the main checkout and is not to be restored as it stands | aligned |
| SC-8 | No protocol frame added; frames not authorized to change are unchanged | not_started | none | Held by the byte anchor once changes exist | aligned |
| SC-9 | The full suite is no weaker than baseline | not_started | none; the baseline itself measured at 2361 passed / 2 skipped / 0 failed, exit 0, in this worktree at 8247182c | Must be proved again at each accepted result | aligned |
| SC-10 | Repository records this work falsifies are corrected | not_started | none | Search set not yet stated or run | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-a-move-reads-both-of-its-ends/TaskContract.md` | A move is carried out whenever either of the two ends it names holds something, and its record says which lists it was between | `SC-1`, `SC-2`, `SC-3`, `SC-7` | `running` | `pending` | `active` |
