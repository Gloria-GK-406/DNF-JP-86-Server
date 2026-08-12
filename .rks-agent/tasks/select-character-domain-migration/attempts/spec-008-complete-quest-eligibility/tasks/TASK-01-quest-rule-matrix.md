# TASK-01 — Quest Rule Matrix

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Establish the exhaustive selectable-quest predicate/source/disposition matrix and update G5 coverage with code-reversed confidence.
- Referenced spec requirements: RQ-001, RQ-008; AC-007; EV-001, EV-005.
- Referenced plan sections: T-001; BD-001; CK-001, CK-002.
- Owned boundary: Quest eligibility analysis and `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` only.
- Permitted files or interfaces: `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`; append-only execution evidence in this Task file.
- Dependencies: None.
- Required dependency outputs: None.
- Acceptance conditions: Every predicate in the complete legacy selectable-quest loop is listed with definition source, character/server source, exact disposition and uncertainty; no packet-capture claim; no G6/G7 behavior.
- Task-specific authorization: Inherited from the root authorization to complete through G7 and this ready child contract.
- Required methods: Inspect legacy quest relation/prerequisite/training sources and representative real-archive fields; preserve existing accepted G5 decisions unless direct evidence corrects them.
- Required checks: CK-001 and CK-002 from the approved plan; `git diff --check` for owned changes.
- Required evidence: Exact source paths/symbols, final predicate count/categories, owned diff identity, check results and executor self-check.

## Execution Result — TASK-01-R1-20260812

- Result: PASS. `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` now contains one
  exhaustive 16-row predicate/source/disposition matrix for row 12 and retains the explicit
  code-reversed, not capture-confirmed confidence boundary.
- Legacy evidence cross-checked:
  - `Server/DfoServer/GameWorld/QuestRelationIndex.cs` —
    `ComputeAcceptableQuests`, `IsSelectableGrade`, `MatchesTargetCharacter`, `MatchesJob`.
  - `Server/DfoServer/GameWorld/QuestPrerequisiteDefinition.cs` —
    `QuestPrerequisiteDefinition.Evaluate`, `ParseCompletedQuestGroups`,
    `ParseRequiredAnswers`, `ParseCollisionQuestGroups`, `QuestPrerequisiteCatalog`.
  - `Server/DfoServer/GameWorld/QuestData.cs` — `LoadTrainingQuestNpcs` and
    `IsThereDailyTrainingQuestList`.
  - `Server/DfoServer/GameWorld/QuestCatalog.cs` — first-ID catalogue order and
    missing-definition behavior.
  - `Server/DfoServer/Network/Builders/Init/QuestListBodyBuilder.cs` — selected-character
    cleared flags and equipped-creature eligible-kind input.
- Predicate categories: catalogue identity/range/uniqueness, exposed-by-NPC, event,
  creature, expert job, training, grade, target character, level, job, job-change grow
  branch, cleared/repeatable, completed prerequisites, question-answer flags, character
  collision, and account collision.
- Real archive evidence: token-index scan of the supplied `Script.pvf` found 2,740 unique
  in-range list entries and no missing quest file; 353 exposure declarations (338 zero),
  306 event, 15 creature, six expert-job, 160 target-character, 1,375 prerequisite owners
  / 1,463 groups, 13 answer prerequisites, 98 collision owners / 99 groups, and zero account
  collision declarations. Relationship forms had 17 unknown prerequisite and three unknown
  collision references, but no non-integer, odd-pair, or self-reference form. The training
  list contains levels 1..70 plus the `-100` sentinel; no quest has exact training grade.
- Owned product diff: only `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`, 52
  insertions and 26 deletions; SHA-256
  `26db82e54b7578868c62208d41f79aa3d93218a8045effcf68704ceaa64f1b4b` before this
  append-only task evidence.
- Checks:
  - CK-001 PASS: every required category is found by the approved `rg` expression.
  - CK-002 PASS: rows 1..16 were manually traced in execution order against the complete
    legacy loop and its called prerequisite evaluator; no continue/deny branch is omitted.
  - `git diff --check -- Docs/Refactor/SelectCharacterWorldEntryCoverage.md` PASS.

## Executor Self-check — TASK-01-R1-20260812

- [x] Modified only the owned coverage document and this append-only execution evidence.
- [x] Preserved accepted non-quest G5 decisions and introduced no G6/G7 behavior.
- [x] Distinguished optional absence, malformed declaration, unavailable mandatory state,
  conservative exclusion, and whole-catalogue composition failure.
- [x] Made no packet-capture or client-confirmation claim.
- [x] Did not modify execution controllers, another Task, implementation code, tests, or Git
  history.

## Independent Quick Review — TASK-01-QR1-20260812

- Verdict: `changes_required`.
- Reviewed contract: Task Unit Contract revision 1; no amendments present.
- Owned diff identity: `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`, 52 insertions
  and 26 deletions, SHA-256
  `26db82e54b7578868c62208d41f79aa3d93218a8045effcf68704ceaa64f1b4b`.
- Commands/evidence inspected:
  - the sealed TASK-01 contract, execution result and executor self-check;
  - plan T-001/BD-001/CK-001/CK-002 and spec RQ-001/RQ-008;
  - the complete owned document diff and its recorded SHA-256/numstat identity;
  - `main:Server/DfoServer/GameWorld/QuestRelationIndex.cs`, specifically
    `ComputeAcceptableQuests`, `IsSelectableGrade`, `MatchesTargetCharacter`,
    `MatchesJob`, and `GetRequiredQuestAnswerFlagValue`;
  - `main:Server/DfoServer/GameWorld/QuestPrerequisiteDefinition.cs`, specifically
    `Evaluate`, `ParseCompletedQuestGroups`, `ParseCollisionQuestGroups`, and
    `ParseRequiredAnswers`;
  - `main:Server/DfoServer/GameWorld/QuestData.cs`, specifically
    `LoadTrainingQuestNpcs` and `IsThereDailyTrainingQuestList`;
  - fresh CK-001 category search and the owned document's `git diff --check`, both
    successful; manual CK-002 trace found the blocking mismatch below.
- Finding: `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` row 11 does not record the
  complete default job-change/grow-type predicate. The legacy `ComputeAcceptableQuests`
  default branch compares a declared quest grow type only when the character `growType >=
  0`; when character grow type is negative/unavailable, that branch does not exclude the
  quest. The matrix instead says that, outside job-change values `1`/`10`/`20`, a declared
  grow type is an exact match, and its only `-1` statement describes the missing quest
  definition value. This omits a directly evidenced condition and therefore does not meet
  the Task acceptance condition or CK-002's exact complete-loop cross-check.
- Required remediation: amend row 11 to state the character-side `growType >= 0` guard and
  the disposition when character grow type is negative/unavailable, while retaining the
  distinct masked behavior for job-change values `2`/`3`; then rerun CK-001, manually rerun
  CK-002 against the complete loop, run the owned `git diff --check`, and refresh the owned
  diff identity in a new execution/self-check round.

## Execution Result — TASK-01-R2-20260812

- Result: PASS after focused remediation of `TASK-01-QR1-20260812`.
- Row 11 now records the complete legacy grow predicate:
  - job-change values `2`/`3` always compare the declared quest value with
    `characterGrowType & 0xF`, including a negative character value;
  - values `1`/`10`/`20` retain their exemption;
  - the ordinary branch performs exact comparison only when `characterGrowType >= 0`, so
    a negative character value does not exclude there;
  - unavailable mandatory G5 character state fails composition instead of being encoded as
    that permissive legacy sentinel, while a missing quest grow declaration remains
    unrestricted.
- Refreshed owned product diff: only
  `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`, 52 insertions and 26 deletions;
  SHA-256 `a9159a8359f199806547575bda130ce12dbf595cf8c4e0b1d9d35a7e2768d656`.
- Checks:
  - CK-001 PASS: the approved category search still finds every required category.
  - CK-002 PASS: row 11 was retraced against
    `QuestRelationIndex.ComputeAcceptableQuests` lines 144..159, and the complete 16-row
    set still accounts for every loop/evaluator deny branch.
  - `git diff --check` PASS.

## Executor Self-check — TASK-01-R2-20260812

- [x] Changed only row 11 of the owned coverage document and appended this new unique round.
- [x] Did not alter the prior executor round or independent review round.
- [x] Preserved the masked `2`/`3` branch, the three exemption values, and both quest-side
  and character-side negative-value semantics without claiming capture confirmation.
- [x] Did not modify implementation, tests, controllers, another Task, or Git history.

## Independent Quick Review — TASK-01-QR2-20260812

- Verdict: `passed`.
- Reviewed contract: Task Unit Contract revision 1; no amendments present; remediation
  round `TASK-01-R2-20260812` and its matching executor self-check.
- Owned diff identity: `Docs/Refactor/SelectCharacterWorldEntryCoverage.md`, 52 insertions
  and 26 deletions; current document SHA-256
  `a9159a8359f199806547575bda130ce12dbf595cf8c4e0b1d9d35a7e2768d656`, matching R2.
- Commands/evidence inspected:
  - the sealed TASK-01 contract, prior QR1 finding, R2 execution result and R2 self-check;
  - the complete owned document diff and its fresh numstat/document-hash identity;
  - `main:Server/DfoServer/GameWorld/QuestRelationIndex.cs` lines 144..159 in
    `ComputeAcceptableQuests`, for the job-change/grow branches remediated in row 11;
  - fresh CK-001 category search and
    `git diff --check -- Docs/Refactor/SelectCharacterWorldEntryCoverage.md`, both PASS;
  - focused CK-002 trace of remediated row 11 against the cited legacy branch, PASS.
- Finding: QR1 is fully remediated. Row 11 now distinguishes the unconditional masked
  comparison for job-change values `2`/`3`, exemptions `1`/`10`/`20`, and the ordinary
  branch's character-side `growType >= 0` guard, while keeping missing mandatory G5 state
  fail-closed and missing quest grow type unrestricted. No obvious blocking defect is
  demonstrated in the bounded R2 inputs.
- Required remediation: none.
