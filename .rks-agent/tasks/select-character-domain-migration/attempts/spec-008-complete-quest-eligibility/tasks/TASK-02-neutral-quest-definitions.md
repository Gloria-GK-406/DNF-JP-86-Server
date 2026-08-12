# TASK-02 — Neutral Quest Definitions

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Extend the neutral quest contract and PVF source so every matrix predicate has validated definition data in authoritative order.
- Referenced spec requirements: RQ-001, RQ-005, RQ-006; AC-004, AC-005; EV-002, EV-003.
- Referenced plan sections: T-002; BD-002, BD-003, BD-007; CK-003, CK-004.
- Owned boundary: Quest definition contracts, the Infrastructure PVF quest source and its focused tests.
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/Content/QuestContent.cs`; `Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs`; directly required Infrastructure content helpers; `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfQuestDefinitionSourceTests.cs`; append-only execution evidence in this Task file.
- Dependencies: TASK-01.
- Required dependency outputs: Accepted predicate/source/disposition matrix.
- Acceptance conditions: Required fields and relationships are neutral and typed; optional absence is explicit; malformed mandatory data does not widen the catalogue; IDs are client-range unique in first authoritative order; no PVF types leak.
- Task-specific authorization: Inherited from the root authorization and this ready child contract.
- Required methods: TDD for observable parsing/validation behavior; real-archive coverage; no tests for constructor shape alone.
- Required checks: CK-003, CK-004 and `git diff --check` for owned changes.
- Required evidence: RED/GREEN commands and decisive output, archive field observations, owned diff identity, checks and executor self-check.

## Controller Disposition — TASK-02-CD1-20260812

- Disposition: `task-rework`; the approved Spec remains valid and no external input is
  missing.
- Direct evidence: the supplied `TrainingQuest.lst` contains one scalar inside each
  `[level]` block and no NPC operands. A temporary execution of legacy `PvfLib.GetFileContent`
  against the same archive plus the exact legacy `LoadTrainingQuestNpcs` algorithm produced
  zero level-to-NPC mappings.
- Required interpretation: preserve the evidenced predicate — a training quest requires an
  exact level/NPC mapping — but model the production source as the honestly loaded empty
  mapping. Therefore any training quest is ineligible on this archive. Do not reinterpret
  level scalars as NPC identifiers or create level-only eligibility.
- Scope effect: no Task boundary expansion. TASK-02 still owns the neutral mapping source;
  TASK-04 already owns final coverage-document reconciliation and must correct TASK-01 row 6
  plus its archive statistics from this accepted downstream evidence.
- Execution may continue from the current RED/GREEN cycle; all other predicates and checks
  remain required.

## Execution Result — TASK-02-R1-20260812

- Result: PASS after the controller's `TASK-02-CD1-20260812` interpretation. The neutral
  content contract now distinguishes absent optional restrictions with nullable typed
  values and carries exposure, event, creature, expert-job, target-character, job-change,
  prerequisite alternatives, question answers, character/account collisions and exact
  training level/NPC mappings without archive vocabulary.
- TDD value: the protected outcome is the validated neutral catalogue consumed by G5;
  realistic regressions include silently treating missing/malformed restrictions as
  permission, flattening prerequisite alternatives and returning duplicate/out-of-range
  IDs. The focused tests observe returned definitions/order and rejection decisions, not
  constructors or collaborator identity, and remain valid if the parser is reorganized.
- RED evidence:
  - Command: `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test
    Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj
    --no-restore --filter FullyQualifiedName~PvfQuestDefinitionSourceTests`.
  - Exit: 1. Decisive behavior failures: the real archive expected 353 exposure
    declarations but projected 0; relationship normalization expected a populated result
    but returned null. Fourteen other cases passed and the archive-present missing-file
    negative case skipped.
  - Identity-cycle RED command: the same project filtered to
    `List_identity_keeps_first_client_range_occurrence_in_authoritative_order`; exit 1,
    because IDs 0/-1/30000 and the second ID 7 were all returned instead of the expected
    first authoritative `(7,3,9)` sequence.
- GREEN evidence:
  - Final CK-003 command: the same real-archive focused project/filter command above.
  - Exit: 0; 17 passed, 1 expected skip, 0 failed. The identity-only GREEN also passed 1/1.
- Real-archive observations: `quest.lst` has 2,740 unique in-range file-backed entries; the
  validated catalogue contains 2,719 after eight combined/non-selectable grade declarations
  and thirteen prerequisite owners with no valid alternative are conservatively rejected.
  Retained definitions contain 341 explicit exposure values, 306 event declarations, 15
  creature restrictions, six expert-job restrictions and 156 target-character
  restrictions; answer and collision relationships are populated and validated. The exact
  legacy parser run recorded in CD1 produces zero training level/NPC mappings, and the new
  source truthfully returns an empty mapping rather than treating level scalars as NPCs.
- Validation and failure behavior: list failures and an all-invalid catalogue throw;
  missing quest files and malformed owner definitions omit only that owner; prerequisite
  occurrences retain AND-within/OR-between semantics, unknown prerequisite alternatives are
  discarded, unknown collisions are ignored, and non-positive/self/odd/negative mandatory
  relationship values reject their owner.
- Checks:
  - CK-004 PASS: the approved archive-type search returned no match outside Infrastructure.
  - `git diff --check` PASS for the complete current worktree diff.
- Owned product diff identity before this evidence append:
  - `QuestContent.cs`: 37 insertions / 8 deletions; SHA-256
    `6e95ed7403503268bc1f0e171c1a47f3711d2bbfea22954b662c434c528653ad`.
  - `PvfQuestDefinitionSource.cs`: 558 insertions / 128 deletions; SHA-256
    `0d2e2b1b078b96024acbdcbcea0e6ae3baa7167820853dd396d320801354731d`.
  - `PvfQuestDefinitionSourceTests.cs`: 141 insertions / 10 deletions; SHA-256
    `6c5073f311d48810e9c6a31369d193a1f8231fbbc0eee7fa61c1385a13542805`.

## Executor Self-check — TASK-02-R1-20260812

- [x] Modified only the three owned product/test paths and appended this single execution
  round after the controller disposition.
- [x] Consumed TASK-01's accepted predicate matrix and did not enter Application eligibility
  rules, command composition, G6 or G7.
- [x] Kept archive tokens/types inside Infrastructure and exposed only neutral typed values.
- [x] Preserved optional absence, conservative malformed-owner rejection, first-ID order,
  client range and relationship alternative semantics.
- [x] Did not fabricate training NPC mappings after the real-archive contradiction; the
  exact level/NPC contract remains and this archive returns empty as directed by CD1.
- [x] Did not modify controllers, Spec, Plan, result/review artifacts, another Task, Git
  history or the ignored `Script.pvf`.

## Independent Quick Review — TASK-02-QR1-20260812

- Verdict: `changes_required`.
- Reviewed contract: Task Unit Contract revision 1; controller disposition
  `TASK-02-CD1-20260812`; execution/self-check round `TASK-02-R1-20260812`; no contract
  amendments present.
- Owned diff identity: `QuestContent.cs`, 37 insertions / 8 deletions, SHA-256
  `6e95ed7403503268bc1f0e171c1a47f3711d2bbfea22954b662c434c528653ad`;
  `PvfQuestDefinitionSource.cs`, 558 insertions / 128 deletions, SHA-256
  `0d2e2b1b078b96024acbdcbcea0e6ae3baa7167820853dd396d320801354731d`;
  `PvfQuestDefinitionSourceTests.cs`, 141 insertions / 10 deletions, SHA-256
  `6c5073f311d48810e9c6a31369d193a1f8231fbbc0eee7fa61c1385a13542805`.
- Commands/evidence inspected:
  - the sealed TASK-02 contract, CD1, R1 result and matching executor self-check;
  - plan T-002/BD-002/BD-003/BD-007/CK-003/CK-004, spec RQ-001/RQ-005/RQ-006
    with AC-004/AC-005 and EV-002/EV-003, and TASK-01's accepted 16-row matrix;
  - the complete owned diff and fresh numstat/SHA-256 identity;
  - fresh CK-003 with the supplied archive: 17 passed, one expected skip, zero failed;
  - fresh CK-004: no archive-type match outside Infrastructure; fresh owned
    `git diff --check`: PASS.
- Finding 1: malformed training declarations can widen availability. In
  `PvfQuestDefinitionSource.ReadTrainingAvailability`, lines 435..454, every non-integer
  token inside a `[level]` block is silently skipped. A block containing a valid level,
  then a malformed non-integer operand, then a positive integer is consequently accepted
  as a level/NPC mapping. That contradicts matrix row 6 (`missing/malformed training data
  is no match`) and RQ-005. CD1's real archive correctly produces the evidenced empty
  mapping, but the focused suite proves only that archive case and has no malformed-list
  regression.
- Finding 2: a declared target-character section with no family string is treated as
  optional absence. In `PvfQuestDefinitionSource.Project`, lines 252..255 process only
  string tokens and do not remember that `[target character]` was declared; therefore a
  numeric-only/malformed declaration leaves `targetCharacterText` null and projects an
  unrestricted definition at lines 323..325. Valid target sections may contain ignored
  numeric grow tuples, but matrix row 8 requires a declared target with no modelled family
  to be rejected, while only true section absence is optional. No focused test covers this
  declared-without-family distinction.
- Required remediation: add focused RED cases for both malformed declarations; make
  training parsing reject/ignore the malformed mapping without fabricating permission and
  distinguish an absent target section from a declared section that yields no modelled
  family; retain CD1's empty real-archive training mapping and valid target grow-tuple
  behavior. Then rerun CK-003 with `Script.pvf`, CK-004 and the owned `git diff --check`,
  and refresh the owned diff identity in a new execution/self-check round.

## Execution Result — TASK-02-R2-20260812

- Result: PASS after focused remediation of both `TASK-02-QR1-20260812` findings.
- RED evidence:
  - Command: `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test
    Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj
    --no-restore --filter
    'FullyQualifiedName~PvfQuestDefinitionSourceTests.Malformed_training_block_does_not_produce_a_mapping|FullyQualifiedName~PvfQuestDefinitionSourceTests.Declared_target_without_a_modelled_family_is_not_optional_absence'`.
  - Exit: 1; both behavior tests failed for the reviewed reasons. The malformed training
    input produced three mappings instead of only the valid block, and a declared target
    section with no family projected null instead of a retained invalid restriction.
- GREEN evidence: the same two-test command exited 0 with 2 passed, 0 failed. The minimal
  behavior changes reject an entire training block when any operand is non-integer,
  non-positive or its level is outside `1..70`; an absent target tag remains null, while a
  declared target tag now produces a neutral `QuestTargetCharacterRestriction` whose empty
  family list explicitly reports `IsValid == false` for the next-layer exclusion.
- Real-archive preservation: CK-003 with the supplied `Script.pvf` exited 0 with 19 passed,
  one expected archive-present skip and zero failed. Its training mapping remains honestly
  empty, while all 156 retained target declarations have a recognised family; numeric grow
  operands inside valid target sections remain ignored rather than treated as families.
- Checks: CK-004 PASS with no archive implementation type outside Infrastructure; complete
  and owned `git diff --check` PASS.
- Refreshed owned product diff identity before this evidence append:
  - `QuestContent.cs`: 47 insertions / 8 deletions; SHA-256
    `4745015179fe7b1565807ff866eb06677bfdb48f800528c546e0ca62e2bc12db`.
  - `PvfQuestDefinitionSource.cs`: 594 insertions / 127 deletions; SHA-256
    `2cb0ca7fa2cd6523f19efcc89473348a532f373246876e6e41e6887dc1f6eac1`.
  - `PvfQuestDefinitionSourceTests.cs`: 175 insertions / 10 deletions; SHA-256
    `c29ac720e8a403e2d0f9a8d5ba0cfc4a1c0111d18f03af0e60bf2ff37b39b639`.

## Executor Self-check — TASK-02-R2-20260812

- [x] Added only the two faithful behavior regressions requested by QR1 and the minimum
  neutral/source changes that make their consumer-visible result fail closed.
- [x] Kept exact training level/NPC semantics and CD1's real-archive empty result; did not
  reinterpret a level scalar as an NPC or fail the whole catalogue for one malformed block.
- [x] Distinguished true target-tag absence from a declared invalid restriction without
  exposing PVF tokens or prematurely applying the Application rule.
- [x] Preserved all R1 ordering, range, relationship and malformed-owner behavior and reran
  CK-003, CK-004 and diff checks.
- [x] Appended this unique R2 round without altering R1, QR1, controller artifacts, another
  Task, product boundaries, Git history or the ignored archive.

## Independent Quick Review — TASK-02-QR2-20260812

- Verdict: `passed`.
- Reviewed contract: Task Unit Contract revision 1; controller disposition
  `TASK-02-CD1-20260812`; QR1 findings; remediation execution/self-check round
  `TASK-02-R2-20260812`; no contract amendments present.
- Owned diff identity: `QuestContent.cs`, 47 insertions / 8 deletions, SHA-256
  `4745015179fe7b1565807ff866eb06677bfdb48f800528c546e0ca62e2bc12db`;
  `PvfQuestDefinitionSource.cs`, 594 insertions / 127 deletions, SHA-256
  `2cb0ca7fa2cd6523f19efcc89473348a532f373246876e6e41e6887dc1f6eac1`;
  `PvfQuestDefinitionSourceTests.cs`, 175 insertions / 10 deletions, SHA-256
  `c29ac720e8a403e2d0f9a8d5ba0cfc4a1c0111d18f03af0e60bf2ff37b39b639`.
- Commands/evidence inspected:
  - the sealed TASK-02 contract, CD1, QR1 findings, R2 result and matching self-check;
  - the complete owned diff and fresh numstat/SHA-256 identity;
  - fresh two-test QR1 remediation filter: 2 passed, 0 failed;
  - fresh CK-003 with the supplied archive: 19 passed, one expected skip, zero failed;
  - fresh CK-004: no archive-type match outside Infrastructure; fresh owned
    `git diff --check`: PASS.
- Finding: QR1 is fully remediated. Training declarations are accumulated and validated as
  whole blocks; any non-integer operand, non-positive NPC or level outside `1..70` discards
  that block, so no malformed tail can fabricate a mapping. True target-tag absence remains
  null, while a declared restriction without a recognised family remains an explicit
  neutral invalid state (`IsValid == false`) for fail-closed consumption; valid family
  strings retain their ignored numeric grow operands. The real archive still projects
  2,719 validated definitions from 2,740 raw entries, all 156 retained target restrictions
  are valid, and the CD1 training mapping remains empty. No obvious blocking defect or
  regression is demonstrated in the bounded R2 inputs.
- Required remediation: none.
