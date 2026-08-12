# TASK-04 — Workflow and Checkpoint Verification

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Prove content failure preserves the old active character, reconcile final coverage, and complete implementation-stage verification for the child.
- Referenced spec requirements: RQ-007, RQ-008; AC-006, AC-007; EV-004 through EV-006.
- Referenced plan sections: T-004; BD-001, BD-006, BD-009; CK-007 through CK-010.
- Owned boundary: Select-character workflow regression, final G5 documentation reconciliation and implementation evidence.
- Permitted files or interfaces: `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs`; `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`; directly required Application test fixtures; append-only execution evidence in this Task file.
- Dependencies: TASK-03.
- Required dependency outputs: Accepted complete eligibility projection and neutral definitions.
- Acceptance conditions: Mandatory content failure propagates before publication; prior active state remains; documentation matches implementation; all focused/full checks pass with real PVF; no G6/G7 code is introduced.
- Task-specific authorization: Inherited from the root authorization and this ready child contract.
- Required methods: Add a behavior regression only if existing coverage does not already faithfully exercise the expanded failure; otherwise adapt existing coverage and record why no new test is needed.
- Required checks: CK-007 through CK-010.
- Required evidence: Test-value decision, focused/full outputs, dependency/diff evidence, owned diff identity and executor self-check.

## Execution Result — TASK-04-R1-20260812

- Result: PASS. The select-character workflow still completes every mandatory content,
  placement and session-adjacent answer before publishing the new active character; the
  final G5 coverage now matches the accepted real-archive and exact legacy-parser evidence.
- Test-value gate: the existing
  `CharacterEntry_FailsBeforePublicationWhenARequiredSourceIsUnavailable` theory already
  has a faithful `content` branch. It executes the real `SelectCharacterCommandHandler`,
  including construction of the expanded `WorldContentSubject`, then makes the mandatory
  `IWorldContentEntryService` boundary fail. The regression observes propagation and the
  unchanged previously published character, so a new collaborator-call or constructor-shape
  test would add no behavioral protection. The existing case was adapted only to assert
  `PublishCount == 0` explicitly for every required-source failure.
- Documentation reconciliation: row 6 no longer claims that `TrainingQuest.lst` supplies
  level-to-NPC pairs or that training is merely vacuous because no quest has training grade.
  It records TASK-02 CD1's direct evidence: each `[level]` block contains one scalar and no
  NPC operand, the exact legacy loader yields zero mappings, production therefore loads an
  empty exact mapping, and training quests fail closed on this archive. The prior raw-token
  statistics were replaced with the accepted production projection: 2,740 unique in-range
  file-backed entries, 2,719 retained definitions, 341 explicit exposure values, 306 event
  declarations, 15 creature restrictions, six expert-job restrictions and 156 retained
  target restrictions.
- CK-007: `CharacterEntryInventoryTests` passed 12/12 with zero failed or skipped. The
  mandatory content branch preserves the prior active character and publishes zero times.
- CK-008: the complete solution with `PVF_ARCHIVE_PATH=$PWD/Script.pvf` passed 375 tests,
  skipped the two expected archive-present negative tests and failed none (`Dfo.Pvf.Tests`
  60, Application 197, Infrastructure 88 plus two skips, Networking 30).
- CK-009: complete `git diff --check` PASS.
- CK-010: the complete product/test diff was reviewed against RQ-001 through RQ-008 and the
  non-goals. All 16 legacy predicate categories have an explicit disposition; definition
  and subject contracts remain neutral; catalogue identity/order, conservative malformed
  handling, relationship rules and late publication have focused evidence. There is no
  Networking/protocol/lifecycle path in the diff, so no G6/G7 code was introduced.
- Dependency/diff evidence: no project or solution file changed; no `Microsoft.Data.Sqlite`
  or `Dfo.Pvf` reference appears in Application or Networking; the ignored real archive is
  present and remains excluded by `*.pvf`.
- Owned diff identity before this evidence append:
  - `CharacterEntryInventoryTests.cs`: one insertion; SHA-256
    `9dc8c24ff49e950e7e410ed256c35dcbfc7c3b1a2e7f5db35bf45eca5422f8c5`.
  - `SelectCharacterWorldEntryCoverage.md`: 49 insertions / 26 deletions; SHA-256
    `b4a03419fd7f8c9f586aeee7227acafca63854ab9c56b6c9290f9a684e375957`.

## Executor Self-check — TASK-04-R1-20260812

- [x] Consumed accepted TASK-03 and TASK-02 CD1/R2 outputs before closing the workflow.
- [x] Applied the test-value gate and adapted the existing behavior regression instead of
  adding a call-shape test.
- [x] Corrected row 6 and the associated archive statistics without claiming packet-capture
  confirmation.
- [x] Ran CK-007 through CK-010 with the real archive reachable and recorded decisive
  focused/full, dependency and diff evidence.
- [x] Modified only the two owned test/document paths and appended this single execution
  result/self-check; no product implementation, controller, Spec, Plan, result/review,
  another Task, Git history or ignored archive was changed.

## Independent Quick Review — TASK-04-QR1-20260812

- Verdict: `passed`.
- Reviewed contract: TASK-04 contract revision 1 with no amendments; T-004, RQ-007,
  RQ-008, AC-006, AC-007 and EV-004 through EV-006; TASK-02-CD1-20260812 dependency
  evidence; execution/self-check round TASK-04-R1-20260812.
- Owned diff identity:
  - `CharacterEntryInventoryTests.cs`: one insertion; SHA-256
    `9dc8c24ff49e950e7e410ed256c35dcbfc7c3b1a2e7f5db35bf45eca5422f8c5`.
  - `SelectCharacterWorldEntryCoverage.md`: 49 insertions / 26 deletions; SHA-256
    `b4a03419fd7f8c9f586aeee7227acafca63854ab9c56b6c9290f9a684e375957`.
- Evidence inspected:
  - The owned test diff, `EntryServices`, `FailingWorldContentEntryService`, and the
    production `SelectCharacterCommandHandler` path. The `content` theory case invokes the
    real handler, reaches `worldContent.LoadAsync`, propagates its failure, retains the exact
    previously active object and observes zero publications. `PublishCount` is external
    publication-state evidence at the active-character boundary, not a collaborator-call
    interaction proxy.
  - The coverage row 6 diff and TASK-02 CD1/R2 evidence. Both state that each supplied
    `[level]` block has one scalar and no NPC operand, the exact legacy loader produces zero
    mappings, production loads an empty exact level/NPC mapping, and training quests fail
    closed. The documented 2,740/2,719 catalogue counts and 341/306/15/6/156 retained-field
    counts match the accepted dependency evidence.
  - Fresh CK-007 with `PVF_ARCHIVE_PATH=$PWD/Script.pvf`: exit 0, 12 passed, zero failed or
    skipped. Fresh CK-008 with the same archive: exit 0, 375 passed, two expected
    archive-present skips and zero failures. Fresh CK-009 `git diff --check`: exit 0.
  - CK-010 bounded path/dependency inspection: no Networking, protocol or lifecycle path,
    project/solution file, or Application/Networking `Microsoft.Data.Sqlite` or `Dfo.Pvf`
    dependency appears in the diff; `Script.pvf` is present and ignored by `*.pvf`.
- Findings: no obvious blocking defect. The owned changes satisfy the RQ-007 publication
  regression and RQ-008 evidence reconciliation, remain inside the TASK-04 boundary, and
  introduce no G6/G7 scope. This quick-review pass authorizes only controller disposition
  for this Task Unit; it is not a Spec or final-delivery approval.
