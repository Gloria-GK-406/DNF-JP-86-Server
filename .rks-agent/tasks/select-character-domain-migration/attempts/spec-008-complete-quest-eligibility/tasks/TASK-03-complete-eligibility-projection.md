# TASK-03 — Complete Eligibility Projection

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Evaluate every modeled quest predicate against a narrow selected-character subject and produce the correct ordered offerable list.
- Referenced spec requirements: RQ-002, RQ-003, RQ-004, RQ-005, RQ-006; AC-001 through AC-005; EV-001 through EV-003.
- Referenced plan sections: T-003; BD-004, BD-005, BD-006, BD-008; CK-005, CK-006.
- Owned boundary: World content subject, quest eligibility service, select-character subject construction and focused Application rule/support tests.
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/World/IWorldEntryServices.cs`; `Server/src/Dfo.Server.Application/World/WorldContentEntryService.cs`; `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs`; `Server/tests/Dfo.Server.Application.Tests/World/WorldContentRuleTests.cs`; directly affected Application test support/fixtures, including `WorldContentArchiveTests.cs` only when its real-archive behavioral assertion is invalidated by the completed predicate; append-only execution evidence in this Task file.
- Dependencies: TASK-02.
- Required dependency outputs: Accepted complete neutral quest definitions.
- Acceptance conditions: Every exclusion and grow/prerequisite branch is behavior-covered; catalogue order and uniqueness are preserved; subject stays narrow; no repository, SQLite or PVF dependency enters Application/Networking.
- Task-specific authorization: Inherited from the root authorization and this ready child contract.
- Required methods: Behavior-first TDD for each predicate cluster; structural wiring changes use compilation/static verification rather than interaction-proxy tests.
- Required checks: CK-005, CK-006 and `git diff --check` for owned changes.
- Required evidence: RED/GREEN results, owned diff identity, focused checks and executor self-check.

## Execution Result — TASK-03-R1-20260812

- Result: PASS. `WorldContentEntryService` now evaluates the complete neutral eligibility
  contract in catalogue order: client range/first identity, exposure, event, creature,
  conservative expert-job and account-collision decisions, training, grade, target and job
  families, level, all grow branches, cleared/repeatable state, prerequisite alternatives,
  answer flags and character collisions.
- Narrow subject: `WorldContentSubject` replaces the lossy cleared-ID set with the complete
  non-zero cleared-flag map and adds only equipped-creature level beside the already-owned
  equipment identifier. `SelectCharacterCommandHandler` projects both from its already
  loaded `CharacterEntryState`/inventory result. It adds no repository, archive or
  persistence access. Active quest IDs are intentionally absent because the evidenced
  select-character caller supplies no active set; declared account collisions and expert
  restrictions fail closed because their required authoritative state is unavailable.
- Dependency adaptation: TASK-02's accepted nullable/list neutral contract initially left
  Application unable to compile (nullable grade/level/grow and list-valued jobs, plus the new
  training source member). The owned service and focused stubs were adapted without altering
  TASK-02. The complete solution now builds with zero warnings and zero errors.
- TDD value: offerability, stable order/range/uniqueness and fail-closed restrictions are
  consumer-visible results. Plausible regressions would silently offer invalid quests and
  escape existing level/job-only tests. The new tests assert returned quest IDs only, not
  collaborators or constructor shape, and remain valid under an internal rule refactor.
- RED/GREEN behavior cycles:
  - Restriction cluster RED: focused
    `Declared_entry_restrictions_require_their_complete_matching_state` exited 1; expected
    `(1,8,9,10)` but the old service returned hidden, event, creature, expert, target and
    training failures too. GREEN: the same command exited 0, 1/1 passed.
  - Optional/malformed grade RED: focused
    `An_absent_grade_is_unrestricted_but_an_unrecognized_declaration_is_rejected` exited 1
    because both IDs were offered. GREEN: 1/1 passed.
  - Grow branch RED: focused
    `Job_change_quests_preserve_masked_exempt_and_ordinary_grow_branches` exited 1; expected
    `(1,2,4,5,6,7)` but the ordinary equality implementation returned `(1,3,7)`. GREEN:
    1/1 passed.
  - Progress cluster RED: focused
    `Prerequisites_answers_and_collisions_use_the_complete_cleared_flag_map` exited 1 because
    unmet alternatives, wrong answers and collisions were still offered. GREEN: 1/1 passed.
  - Identity RED: focused
    `Available_quests_keep_first_authoritative_client_range_order` exited 1 because IDs
    `0`, `30000` and the duplicate `7` remained. GREEN: 1/1 passed in first catalogue order.
- Creature evidence correction: direct legacy inspection of
  `PetCreatureEvolutionRuntimeService.LoadEligiblePetCreatureEvolutionQuestKinds` showed
  that an allowed kind requires equipment-to-current-creature resolution, a declared quest
  evolution and the current creature level meeting the creature definition's evolution
  threshold. It does not equate the evolution quest ID with the candidate quest ID and does
  not apply the candidate quest's creature-level operand at offer time. The GREEN fixture
  deliberately uses a different evolution quest ID and a higher candidate creature level
  so either invented condition would fail.
- Checks:
  - CK-005 PASS: 52 `WorldContentRuleTests` passed, zero failed/skipped.
  - CK-006 PASS: no `Microsoft.Data.Sqlite` or `Dfo.Pvf` match in Application/Networking.
  - Owned `git diff --check` PASS.
  - Complete solution build PASS: zero warnings, zero errors.
  - Controller-directed archive evidence rework PASS: the old level-40-only swordman/mage
    assertion failed after the complete filters, and a bounded level-only search established
    that no level differs at grow type zero. The faithful replacement searches supported
    levels `1..99` and first-grow values `0..15` and requires at least one subject whose real
    offered sets differ; it passed against `Script.pvf`, so the check still proves real job
    restrictions affect entry without assuming one arbitrary level/grow combination.
  - Fresh complete solution test with `Script.pvf` PASS: 374 passed, two expected
    archive-present skips, zero failed.
- Owned product/test diff identity before this evidence append:
  - `IWorldEntryServices.cs`: 13 insertions / 2 deletions; SHA-256
    `db48baf6f25d11ce6de474ba9c753eee0ca3ffa00f167a68e0120628ee0e24b6`.
  - `WorldContentEntryService.cs`: 138 insertions / 13 deletions; SHA-256
    `cbfe80907b0c153a1b388f2872303d368ca965b895aaf3956b11272ef137ecee`.
  - `SelectCharacterCommand.cs`: 7 insertions / 2 deletions; SHA-256
    `92a6e66fb8209e024a2c18eb62184c83625b290ae8790e87b05e94f187cf1555`.
  - `WorldContentRuleTests.cs`: 165 insertions / 13 deletions; SHA-256
    `15d110cc61fb4e5ae46c84368c88d3c9b6a28509b3143d563a4e825a7b10dc8a`.
  - Directly affected `WorldContentArchiveTests.cs` subject fixture and real-archive job
    evidence: 27 insertions / 8 deletions; SHA-256
    `34de87c603b50597f8f89af640f40582c09175c25fbc4ef411ef3624df4390b7`.

## Executor Self-check — TASK-03-R1-20260812

- [x] Modified only TASK-03's four owned product/rule-test paths, the directly affected
  Application archive subject fixture, and appended this unique result/self-check.
- [x] Consumed TASK-02's accepted neutral definitions without modifying its product paths,
  Task evidence or interpretation.
- [x] Covered every applied predicate cluster plus order, range and first-ID uniqueness with
  behavior assertions; used compilation/static checks rather than interaction proxies for
  command wiring.
- [x] Kept the subject narrow and projected only already-loaded cleared flags and equipped
  creature level; introduced no repository, SQLite, PVF, G6 or G7 dependency.
- [x] Preserved catalogue order, repeatable semantics and fail-closed handling for missing
  expert/account authority; did not substitute default permission.
- [x] Did not modify controller state, Spec, Plan, result/review artifacts, another Task,
  Infrastructure, Git history or the ignored archive.

## Contract Amendment — TASK-03-A1-20260812

- The full Application run demonstrated that the existing real-archive assertion “job 0
  and job 3 differ at level 40” is no longer true after the complete eligibility predicate;
  it was an incidental sample, not a promised level-specific behavior.
- This Task may update only that directly affected real-archive test to retain its original
  faithful outcome: prove that at least one supported character level produces different
  offerable sets for different job families. It may not weaken the test to mere nonempty
  results or remove job sensitivity evidence.
- Disposition: `task-rework`; this preserves the approved Spec, Plan and Task boundary and
  is required to restore the full suite before Task completion.

## Controller Disposition — TASK-03-CD1-20260812

- Disposition: `task-rework`; the quick review's local `blocked` report does not establish a
  terminal Task or Spec state because the missing target-equipment fact is derivable from
  the already accepted creature/equipment PVF source in this repository.
- Direct evidence: legacy `PetCreatureEvolutionRuntimeService.TryLoadEquippedQuestState`
  requires `EvolutionItemTemplateId > 0`; the catalogue resolves it from equipment output
  or the evolution target creature's equipment mapping. The current rule checks only a
  positive evolution target creature ID and is therefore broader.
- Boundary amendment: TASK-03 may also change
  `Server/src/Dfo.Server.Abstractions/Content/CreatureContent.cs`,
  `Server/src/Dfo.Server.Infrastructure/Content/PvfCreatureDefinitionSource.cs`, their
  directly affected focused tests and Task-owned stubs solely to expose and consume a
  neutral target-creature-to-equipment resolution fact. It may not add general item
  metadata, equipment mutation or lifecycle behavior.
- Required remediation: add a faithful RED in which evolution target equipment is missing,
  prove the quest is rejected, then GREEN by resolving the target through the content
  abstraction. Preserve all prior R1 evidence and rerun affected creature/source, CK-005,
  CK-006, full solution and diff checks before a new R2 self-check.

## Independent Quick Review — TASK-03-QR1-20260812

- Verdict: `blocked`.
- Reviewed contract: Task Unit Contract revision 1; amendment `TASK-03-A1-20260812`;
  execution/self-check round `TASK-03-R1-20260812`.
- Owned diff identity: `IWorldEntryServices.cs`, 13 insertions / 2 deletions, SHA-256
  `db48baf6f25d11ce6de474ba9c753eee0ca3ffa00f167a68e0120628ee0e24b6`;
  `WorldContentEntryService.cs`, 138 insertions / 13 deletions, SHA-256
  `cbfe80907b0c153a1b388f2872303d368ca965b895aaf3956b11272ef137ecee`;
  `SelectCharacterCommand.cs`, 7 insertions / 2 deletions, SHA-256
  `92a6e66fb8209e024a2c18eb62184c83625b290ae8790e87b05e94f187cf1555`;
  `WorldContentRuleTests.cs`, 165 insertions / 13 deletions, SHA-256
  `15d110cc61fb4e5ae46c84368c88d3c9b6a28509b3143d563a4e825a7b10dc8a`;
  directly affected `WorldContentArchiveTests.cs`, 27 insertions / 8 deletions, SHA-256
  `34de87c603b50597f8f89af640f40582c09175c25fbc4ef411ef3624df4390b7`.
- Commands/evidence inspected:
  - the sealed TASK-03 contract, A1, R1 result and matching executor self-check; plan T-003,
    BD-004/BD-005/BD-006/BD-008 and CK-005/CK-006; spec RQ-002 through RQ-006,
    AC-001 through AC-005 and EV-001 through EV-003;
  - TASK-01's accepted R2 predicate matrix and TASK-02's accepted R2 neutral quest
    definitions;
  - the complete five-file owned diff plus fresh numstat/SHA-256 identity;
  - direct legacy `ClearQuestListBodyBuilder`/`QuestListBodyBuilder` flag-slot projection,
    `QuestRelationIndex.ComputeAcceptableQuests`,
    `QuestPrerequisiteDefinition.Evaluate`, and
    `PetCreatureEvolutionRuntimeService.TryLoadEquippedQuestState` with
    `PetCreatureEvolutionCatalog.ResolveEvolutionItemTemplateId`;
  - fresh CK-005: 52 passed, zero failed/skipped; fresh CK-006: no
    `Microsoft.Data.Sqlite`/`Dfo.Pvf` match in Application or Networking; fresh owned
    `git diff --check`: PASS;
  - fresh archive-amendment regression with `Script.pvf`: one passed, zero failed/skipped.
- Finding: the flags, progress, ordinary/masked/exempt grow, grade/job/target,
  prerequisite OR/AND, answer, collision, order/range/first-duplicate and A1 archive
  behaviors show no other obvious blocking mismatch. In particular, indexing the
  30,000-byte visibility array by its zero-based slot reproduces the legacy quest-ID key,
  preserves every non-zero flag value, and retains the complete cleared-ID set as the map
  keys. The subject adds only already-loaded flag and equipped-creature-level facts and no
  persistence/archive dependency.
- Blocking finding: creature eligible-kind calculation is broader than the direct legacy
  rule. Legacy `TryLoadEquippedQuestState` requires a positive resolved
  `EvolutionItemTemplateId` in addition to current equipment-to-creature resolution, a
  declared evolution quest, a positive evolution threshold and sufficient current creature
  level. `WorldContentEntryService.cs:129-142` returns true for any evolution with a positive
  target creature ID already present in the neutral record, positive quest/level values and
  sufficient level; it never proves that the evolution target resolves to an equipment
  template. The focused fixture at `WorldContentRuleTests.cs:35-39` supplies only creature
  10 with evolution target 11 and no target creature/equipment mapping, yet line 49 requires
  quest 10 to be offered. That fixture therefore enshrines a case the legacy eligible-kind
  loader rejects. This violates RQ-002/RQ-005 and AC-001/AC-004 by allowing a malformed or
  unresolved creature evolution to widen the offered set.
- Required remediation: provide an authoritative neutral fact for successful evolution
  target-equipment resolution, reject a creature restriction when that fact is absent, and
  add positive/negative behavior evidence while retaining the already-correct distinctions
  that evolution quest ID need not equal candidate quest ID and the candidate quest's own
  creature-level operand is not an offer-time threshold. This cannot be completed exactly
  through the currently permitted TASK-03 interfaces because `CreatureEvolution` and
  `ICreatureDefinitionSource` expose no resolved target-equipment identity or viability.

## Quick Review Blocker Report — TASK-03-QB1-20260812

- Reason: exact legacy creature eligible-kind eligibility requires target-equipment
  resolution state that the current neutral creature contract does not expose, and adding
  that state crosses TASK-03's sealed permitted-file boundary.
- Direct evidence: legacy `PetCreatureEvolutionRuntimeService.cs:138-142` rejects when
  `EvolutionItemTemplateId <= 0`; legacy `PetCreatureEvolution.cs:256-272` resolves that ID
  only through a valid equipment output or an equipment mapping for the evolution creature;
  the owned service and focused fixture demonstrate that neither condition is currently
  required.
- Affected Task acceptance: complete creature exclusion coverage and exact conservative
  mandatory-state handling under RQ-002/RQ-005, AC-001/AC-004 and T-003 step 3.
- Dependency impact: TASK-02's accepted neutral quest-definition output remains usable and
  is not contradicted. A controller-approved boundary amendment or a prerequisite neutral
  creature-contract/source result is required before TASK-03 can finish; T-004 must not
  consume TASK-03 as accepted meanwhile.
- Owned-diff identity: the five hashes and numstats recorded in `TASK-03-QR1-20260812` match
  R1 exactly; no product or test file was changed by this review.
- Attempted actions: inspected all bounded inputs and direct legacy branches, reran CK-005,
  CK-006, the owned diff check and A1's real-archive regression; all checks passed but none
  exercises unresolved evolution-target equipment.
- Known alternatives: amend the neutral creature definition/source to expose resolved
  evolution-target equipment viability, or authorize an upstream component to compute the
  exact eligible-kind set and pass that narrow result to world content. Merely checking a
  positive target creature ID, equating the evolution quest ID with the candidate quest, or
  applying the candidate quest's creature-level operand would not reproduce the legacy
  rule.

## Execution Result — TASK-03-R2-20260812

- Result: PASS after the controller's `TASK-03-CD1-20260812` task-rework. Creature
  eligible-kind evaluation now requires all direct legacy viability facts: current
  equipment resolves to the current creature, the creature declares a positive quest and
  evolution threshold, its current level reaches that threshold, and the evolution item is
  resolvable either from the current equipment's explicit output or equipment mapped to the
  target creature.
- Neutral boundary: `ICreatureDefinitionSource.CanResolveEvolutionEquipmentAsync` returns
  only the evolution viability answer for current equipment/target creature IDs. The
  contract exposes no item record, general metadata, inventory access or mutation.
  `PvfCreatureDefinitionSource` derives the answer from its existing creature-equipment
  index plus the neutral `[output index]` relation while the Application remains free of
  archive vocabulary.
- Faithful RED:
  - Command: `dotnet test
    Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj
    --no-restore --filter
    FullyQualifiedName~WorldContentRuleTests.Creature_evolution_quest_requires_the_target_creatures_equipment_mapping`.
  - Exit: 1. The unresolved case returned quest 10 even though current creature 10's
    positive target 11 had no equipment mapping; `Assert.Empty` failed. The same fixture's
    resolved branch maps a separate target equipment to creature 11 and requires quest 10.
- GREEN:
  - The same focused command exited 0 with 1/1 passed. The earlier restriction-cluster
    fixture was corrected to reject its unresolved target rather than enshrine the broader
    R1 behavior.
  - Real-source focused command with `Script.pvf`, filtered to
    `PvfCreatureDefinitionSourceTests`, exited 0 with 7/7 passed. New evidence finds an
    actual evolution target resolvable through the production creature-equipment index and
    proves an undefined target is not resolvable.
- Checks:
  - CK-005 PASS: 53 `WorldContentRuleTests` passed, zero failed/skipped.
  - CK-006 PASS: no `Microsoft.Data.Sqlite` or `Dfo.Pvf` match in Application/Networking.
  - Fresh complete solution with `Script.pvf` PASS: 374 passed, two expected
    archive-present skips, zero failed.
  - Complete `git diff --check` PASS.
- Current owned product/test diff identity before this R2 evidence append:
  - `CreatureContent.cs`: 11 insertions; SHA-256
    `c8f75a407e31fc7110f6e07aa2fcf33d5bf8b265c3be1c4aa510b652397eaf7b`.
  - `IWorldEntryServices.cs`: 13 insertions / 2 deletions; SHA-256
    `db48baf6f25d11ce6de474ba9c753eee0ca3ffa00f167a68e0120628ee0e24b6`.
  - `SelectCharacterCommand.cs`: 7 insertions / 2 deletions; SHA-256
    `92a6e66fb8209e024a2c18eb62184c83625b290ae8790e87b05e94f187cf1555`.
  - `WorldContentEntryService.cs`: 145 insertions / 13 deletions; SHA-256
    `682480789197497334cf82b10d4e023107f1f67887e98b5f01531ab4052a28e1`.
  - `PvfCreatureDefinitionSource.cs`: 59 insertions / 12 deletions; SHA-256
    `5a8b735dcfa23f89b5c46203c1e92cd70fe49b134d198b0add9cf55eb8029ed0`.
  - `WorldContentArchiveTests.cs`: 27 insertions / 8 deletions; SHA-256
    `34de87c603b50597f8f89af640f40582c09175c25fbc4ef411ef3624df4390b7`.
  - `WorldContentRuleTests.cs`: 202 insertions / 13 deletions; SHA-256
    `b019a4e393bf9707ba523048d0d5e6ae56ea089926be5066516d8568d5ae65b3`.
  - `PvfCreatureDefinitionSourceTests.cs`: 31 insertions; SHA-256
    `ea9ab0b27da86f67ba9a3baea88cd75352644fbc25cf3cabdcf877cc5e6a7bdd`.

## Executor Self-check — TASK-03-R2-20260812

- [x] Preserved R1, A1, CD1, QR1 and QB1 verbatim and appended this distinct R2 result and
  matching self-check.
- [x] Remediated only QB1's demonstrated target-equipment viability gap within CD1's added
  creature contract/source/test paths and existing TASK-03 rule/stub ownership.
- [x] The negative and positive branches observe returned quest IDs and target resolution,
  not collaborator calls; current evolution quest ID still differs from candidate quest ID
  and candidate creature-level operand remains outside offer-time eligibility.
- [x] Implemented both legacy resolution paths—validated explicit equipment output and
  target-creature equipment mapping—without adding general item metadata or mutation.
- [x] Re-ran affected real-source tests, CK-005, CK-006, the real-PVF complete solution and
  complete diff check successfully.
- [x] Did not modify TASK-02, other Tasks, controller state, Spec, Plan, result/review
  artifacts, Git history or the ignored archive.

## Independent Quick Review — TASK-03-QR2-20260812

- Verdict: `changes_required`.
- Reviewed contract: Task Unit Contract revision 1; amendment `TASK-03-A1-20260812`;
  controller disposition `TASK-03-CD1-20260812`; prior QR1/QB1; remediation
  execution/self-check round `TASK-03-R2-20260812`.
- Owned diff identity: `CreatureContent.cs`, 11 insertions, SHA-256
  `c8f75a407e31fc7110f6e07aa2fcf33d5bf8b265c3be1c4aa510b652397eaf7b`;
  `IWorldEntryServices.cs`, 13 insertions / 2 deletions, SHA-256
  `db48baf6f25d11ce6de474ba9c753eee0ca3ffa00f167a68e0120628ee0e24b6`;
  `SelectCharacterCommand.cs`, 7 insertions / 2 deletions, SHA-256
  `92a6e66fb8209e024a2c18eb62184c83625b290ae8790e87b05e94f187cf1555`;
  `WorldContentEntryService.cs`, 145 insertions / 13 deletions, SHA-256
  `682480789197497334cf82b10d4e023107f1f67887e98b5f01531ab4052a28e1`;
  `PvfCreatureDefinitionSource.cs`, 59 insertions / 12 deletions, SHA-256
  `5a8b735dcfa23f89b5c46203c1e92cd70fe49b134d198b0add9cf55eb8029ed0`;
  `WorldContentArchiveTests.cs`, 27 insertions / 8 deletions, SHA-256
  `34de87c603b50597f8f89af640f40582c09175c25fbc4ef411ef3624df4390b7`;
  `WorldContentRuleTests.cs`, 202 insertions / 13 deletions, SHA-256
  `b019a4e393bf9707ba523048d0d5e6ae56ea089926be5066516d8568d5ae65b3`;
  `PvfCreatureDefinitionSourceTests.cs`, 31 insertions, SHA-256
  `ea9ab0b27da86f67ba9a3baea88cd75352644fbc25cf3cabdcf877cc5e6a7bdd`.
- Commands/evidence inspected:
  - the complete current TASK-03 through R2, the directly referenced RQ-002 through RQ-006,
    AC-001 through AC-005, T-003 and CK-005/CK-006;
  - the complete current neutral `CreatureContent.cs`,
    `PvfCreatureDefinitionSource.cs` and its focused test file, plus the complete relevant
    world-rule/service diff;
  - direct legacy `PetCreatureEvolutionRuntimeService.TryLoadEquippedQuestState` and
    `PetCreatureEvolutionCatalog.ResolveEvolutionItemTemplateId`, including explicit-output
    priority and target-creature equipment fallback;
  - fresh CK-005: 53 passed, zero failed/skipped; fresh real-source creature tests with
    `Script.pvf`: 7 passed, zero failed/skipped; fresh CK-006: no dependency match; fresh
    owned `git diff --check`: PASS. The recorded R2 complete-solution result was inspected
    but not redundantly rerun.
- Finding: QB1's product logic is corrected. `PvfCreatureDefinitionSource.cs:62-70`
  faithfully accepts a validated explicit output different from the current equipment,
  while lines 72-73 fall back to equipment mapped to the target creature; otherwise the
  answer is false. `WorldContentEntryService.cs:134-149` consumes only that boolean after
  current equipment/creature, declared quest, positive threshold and level checks. The
  abstraction exposes only current equipment ID, target creature ID and a viability boolean,
  so no PVF token/type, general item metadata, inventory access or mutation leaks into
  Application. The unchanged hashes for `IWorldEntryServices.cs` and
  `SelectCharacterCommand.cs` prove the R1 visibility-flag projection did not change, so it
  was not re-reviewed.
- Blocking finding: R2 has no behavior evidence for the explicit-output resolution path.
  Every call in the added real-source test at
  `PvfCreatureDefinitionSourceTests.cs:107-127` passes
  `currentEquipmentItemId: 0`, so it can only exercise the target-creature fallback at
  `PvfCreatureDefinitionSource.cs:72-73` or the unresolved false result; it cannot enter the
  explicit-output branch at lines 62-70. The Application stub at
  `WorldContentRuleTests.cs:705-710` likewise models only target-creature mapping. Therefore
  unresolved rejection and target-creature fallback are behavior-covered, but the R2
  self-check's two-path claim and CD1's exact legacy remediation are not fully supported by
  the required behavior-first evidence.
- Required remediation: add focused behavior evidence in which the current creature
  equipment has a valid distinct `[output index]` pointing to creature equipment while the
  supplied target creature is deliberately unmapped; assert that resolution succeeds via
  output alone. Retain the existing target-creature fallback and unresolved-negative cases,
  rerun the affected source tests, CK-005/CK-006 and diff checks, and refresh the owned diff
  identity in a new execution/self-check round.

## Execution Result — TASK-03-R3-20260812

- Result: PASS; QR2's only finding was an evidence gap. The real-source focused suite now
  proves the explicit-output branch through the public neutral API while preserving R2's
  target-creature fallback and unresolved false evidence.
- New behavior evidence: the test enumerates authoritative equipment list identities and
  calls `CanResolveEvolutionEquipmentAsync` with `targetCreatureId: int.MaxValue`, which is
  deliberately absent from the creature-equipment map. A true result therefore cannot use
  the target fallback and proves that the current equipment has a distinct valid output
  resolving to known creature equipment. The assertion requires at least one such real
  archive item and does not inspect a private helper or collaborator call.
- TDD deviation protocol:
  - Attempted RED command: `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test
    Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj
    --no-restore --filter
    FullyQualifiedName~PvfCreatureDefinitionSourceTests.Distinct_equipment_output_resolves_even_when_the_target_creature_is_unmapped`.
  - Exit: 0, 1/1 passed on first execution. Classification: the R2 product behavior already
    implemented the explicit-output path exactly; QR2 demonstrated missing coverage rather
    than missing behavior. This is not reported as a successful RED, and no product change
    was made for R3.
- Checks:
  - All real-source `PvfCreatureDefinitionSourceTests`: 8 passed, zero failed/skipped.
  - CK-005: 53 `WorldContentRuleTests` passed, zero failed/skipped.
  - CK-006: no `Microsoft.Data.Sqlite` or `Dfo.Pvf` match in Application/Networking.
  - Fresh complete solution with `Script.pvf`: 375 passed, two expected archive-present
    skips, zero failed.
  - Complete `git diff --check`: PASS.
- Refreshed owned identity: all seven R2 product/other-test files retain their exact R2
  hashes. Only `PvfCreatureDefinitionSourceTests.cs` changed, now 58 insertions and SHA-256
  `75906d303fc44ee5346b0d082ebbc2098f7f676609d37167d240d5beeb846406`.

## Executor Self-check — TASK-03-R3-20260812

- [x] Preserved every prior result, disposition and review round and appended a distinct R3
  result/self-check.
- [x] Added one public-behavior real-archive test for QR2's explicit-output evidence gap;
  retained the existing target-fallback and unresolved-negative tests.
- [x] Applied the TDD deviation protocol honestly after the attempted RED passed immediately
  because the behavior already existed; made no unnecessary product change.
- [x] Re-ran the focused creature suite, CK-005, CK-006, complete real-PVF solution and diff
  check successfully.
- [x] Did not modify TASK-02, another Task, controller state, Spec, Plan, result/review
  artifacts, product code, Git history or the ignored archive during R3.

## Independent Quick Review — TASK-03-QR3-20260812

- Verdict: `passed`.
- Reviewed contract: Task Unit Contract revision 1; amendment `TASK-03-A1-20260812`;
  controller disposition `TASK-03-CD1-20260812`; QR1/QB1 and QR2 findings; remediation
  execution/self-check round `TASK-03-R3-20260812`.
- Owned diff identity: `CreatureContent.cs`, 11 insertions, SHA-256
  `c8f75a407e31fc7110f6e07aa2fcf33d5bf8b265c3be1c4aa510b652397eaf7b`;
  `IWorldEntryServices.cs`, 13 insertions / 2 deletions, SHA-256
  `db48baf6f25d11ce6de474ba9c753eee0ca3ffa00f167a68e0120628ee0e24b6`;
  `SelectCharacterCommand.cs`, 7 insertions / 2 deletions, SHA-256
  `92a6e66fb8209e024a2c18eb62184c83625b290ae8790e87b05e94f187cf1555`;
  `WorldContentEntryService.cs`, 145 insertions / 13 deletions, SHA-256
  `682480789197497334cf82b10d4e023107f1f67887e98b5f01531ab4052a28e1`;
  `PvfCreatureDefinitionSource.cs`, 59 insertions / 12 deletions, SHA-256
  `5a8b735dcfa23f89b5c46203c1e92cd70fe49b134d198b0add9cf55eb8029ed0`;
  `WorldContentArchiveTests.cs`, 27 insertions / 8 deletions, SHA-256
  `34de87c603b50597f8f89af640f40582c09175c25fbc4ef411ef3624df4390b7`;
  `WorldContentRuleTests.cs`, 202 insertions / 13 deletions, SHA-256
  `b019a4e393bf9707ba523048d0d5e6ae56ea089926be5066516d8568d5ae65b3`;
  `PvfCreatureDefinitionSourceTests.cs`, 58 insertions, SHA-256
  `75906d303fc44ee5346b0d082ebbc2098f7f676609d37167d240d5beeb846406`.
- Commands/evidence inspected:
  - the complete current TASK-03 through R3 and QR2's exact required remediation;
  - the complete focused test diff and the public neutral resolver implementation at
    `PvfCreatureDefinitionSource.cs:55-73`;
  - fresh real-source `PvfCreatureDefinitionSourceTests` with `Script.pvf`: 8 passed, zero
    failed/skipped; fresh CK-005: 53 passed, zero failed/skipped; fresh CK-006: no dependency
    match; fresh complete solution with `Script.pvf`: 375 passed, two expected skips, zero
    failed; fresh owned `git diff --check`: PASS;
  - fresh numstat and SHA-256 identity for all eight owned product/test files, matching R3.
- Finding: QR2 is fully remediated. The pre-existing negative call at
  `PvfCreatureDefinitionSourceTests.cs:124-127` proves `targetCreatureId: int.MaxValue`
  cannot resolve through the target-creature equipment fallback. The new test at lines
  131-154 enumerates authoritative equipment-list IDs and passes that same unmapped target
  to the public neutral resolver. Consequently a true answer can only enter
  `PvfCreatureDefinitionSource.cs:62-70`, which additionally requires a positive output,
  output distinct from the current equipment, and output present in the validated creature
  equipment index. This is behavior evidence for the exact direct-legacy output branch,
  while R2's target fallback and unresolved rejection remain covered.
- Finding: the attempted RED is classified truthfully. It passed on first execution because
  R2 had already implemented the behavior and QR2 requested only missing evidence; R3 does
  not call that a successful RED and changes only the focused test. The seven prior hashes
  remain exactly R2's, and only the focused source-test hash/numstat changed.
- Required remediation: none. No obvious blocking defect remains in the bounded R3 inputs;
  this verdict permits only TASK-03 executor/controller acceptance and is not final Spec
  approval.

## Execution Result — TASK-03-R4-20260812

- Result: PASS after final change-review remediation. A mixed job declaration containing
  both `Any` and a concrete family no longer becomes globally unrestricted. Only the exact
  one-element `Any` declaration is unrestricted; every mixed declaration must contain the
  selected character's family, matching legacy's exact-string `[all]` exemption.
- Neutral/source disposition: the accepted `JobRequirements` list already preserves the
  necessary distinction: exact `[all]` projects `[Any]`, while mixed
  `[all] [swordman]` projects `[Any, Swordman]`. No neutral-contract or Infrastructure
  source change was necessary; remediation is confined to Application interpretation and
  its behavior fixture.
- RED:
  - Command: `dotnet test
    Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj
    --no-restore --filter
    FullyQualifiedName~WorldContentRuleTests.Mixed_all_and_specific_job_declaration_retains_the_specific_family_restriction`.
  - Exit: 1. Swordman was correctly offered quest 1, but Mage was also offered it and the
    negative `Assert.Empty` failed.
- GREEN: the same focused command exited 0, 1/1 passed after restricting the `Any`
  exemption to a list whose only element is `Any`. The test proves the positive Swordman
  and negative Mage branches from the returned quest list.
- Checks:
  - Accepted quest-source focused suite with `Script.pvf`: 19 passed, one expected
    archive-present skip, zero failed; no source regression.
  - CK-005: 54 `WorldContentRuleTests` passed, zero failed/skipped.
  - CK-006: no `Microsoft.Data.Sqlite` or `Dfo.Pvf` match in Application/Networking.
  - Fresh complete solution with `Script.pvf`: 376 passed, two expected archive-present
    skips, zero failed.
  - Complete `git diff --check`: PASS.
- Refreshed owned identity: six unchanged R3 files retain their hashes. Changed files are:
  - `WorldContentEntryService.cs`: 146 insertions / 13 deletions; SHA-256
    `74a83bb4e9453405ca810be374573414fec046a91e0d8b12168686c6a7d705ae`.
  - `WorldContentRuleTests.cs`: 218 insertions / 13 deletions; SHA-256
    `710942872533ef2a670a878bf30b5a4006216b1da09b0538fd317cd89999a1fd`.
  - TASK-02's three accepted files retain exact hashes
    `4745015179fe7b1565807ff866eb06677bfdb48f800528c546e0ca62e2bc12db`,
    `2cb0ca7fa2cd6523f19efcc89473348a532f373246876e6e41e6887dc1f6eac1`
    and `c29ac720e8a403e2d0f9a8d5ba0cfc4a1c0111d18f03af0e60bf2ff37b39b639`.

## Executor Self-check — TASK-03-R4-20260812

- [x] Appended this distinct final-review remediation round without altering the change
  review, prior Task rounds, controller state or another Task.
- [x] Added positive and negative consumer behavior for mixed `Any`/specific jobs and made
  the minimum Application predicate correction.
- [x] Preserved exact `Any`, optional job absence, specific-family and unmapped-job behavior;
  changed neither the accepted neutral list nor its PVF projection.
- [x] Re-ran the focused quest source, CK-005, CK-006, complete real-PVF solution and diff
  check successfully.
- [x] Introduced no archive/persistence dependency, G6/G7 behavior, Git mutation or ignored
  archive change.

## Execution Result — TASK-03-R5-20260812

- Result: PASS after final re-review remediation. Job restriction provenance is now an
  explicit neutral `QuestJobRestriction` instead of an inferred enum list. Optional absence
  is unrestricted; only the exact normalized single declaration `[all]` has
  `IsExactAll == true`; concrete-family restrictions carry only modeled families; invalid
  non-exact empty restrictions fail closed in Application.
- Archive-boundary behavior: `PvfQuestDefinitionSource` tokenizes the complete declared job
  text without substring/`Distinct` inference. Mixed `[all] [swordman]` retains Swordman as
  a concrete restriction, `[all] [all]` has no concrete family outside exact-all and is
  rejected, and `[all] [unknown]` is rejected for its unknown token. Thus neither duplicate
  nor ignored vocabulary can collapse into exact-all permission.
- TDD RED:
  - Command: `dotnet test
    Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj
    --no-restore --filter
    FullyQualifiedName~PvfQuestDefinitionSourceTests.Job_restriction_preserves_exact_all_and_rejects_malformed_declarations`.
  - Exit: 1. The provenance-losing projection threw no exception for `[all] [all]`, so the
    malformed declaration could still appear as exact-all. The same test also requires
    `[all] [unknown]` rejection plus positive exact-all, mixed-family and known-family
    projections.
- GREEN:
  - The same Infrastructure command exited 0 with 1/1 passed after exact token parsing.
  - Application focused command covering the mixed and complete neutral-state cases exited
    0 with 2/2 passed. Observable results prove optional and exact-all admission,
    Swordman-only mixed admission, invalid rejection, and Mage-family positive/negative
    behavior.
- Checks:
  - Complete `PvfQuestDefinitionSourceTests` with `Script.pvf`: 20 passed, one expected
    archive-present skip, zero failed.
  - CK-005: 55 `WorldContentRuleTests` passed, zero failed/skipped.
  - CK-006: no `Microsoft.Data.Sqlite` or `Dfo.Pvf` match in Application/Networking.
  - Fresh complete solution with `Script.pvf`: 378 passed, two expected archive-present
    skips, zero failed.
  - Complete `git diff --check`: PASS; no stale `JobRequirements` reference remains.
- Refreshed R5 changed-file identity:
  - `QuestContent.cs`: 59 insertions / 8 deletions; SHA-256
    `6cb71432621197a2f5f27e9e32233f819a0172d2c67fe31cd6d3986a1cb44150`.
  - `PvfQuestDefinitionSource.cs`: 619 insertions / 127 deletions; SHA-256
    `d0e4223f0e8da1fe3fa135130c3768811b91e06cf20fa4cab37d0f253bfc9cd1`.
  - `PvfQuestDefinitionSourceTests.cs`: 200 insertions / 10 deletions; SHA-256
    `5f1cdef0a2854a4fa9058ab98a751c70602f386d1fd36cc9fc4fb15736962ee5`.
  - `WorldContentEntryService.cs`: 146 insertions / 13 deletions; SHA-256
    `8322ef7201f3ca154e18b94e91760f7d16a9ebb7fb12f5df9b0e59e946e010af`.
  - `WorldContentRuleTests.cs`: 245 insertions / 13 deletions; SHA-256
    `b76a43d9f36a06a2a29f2e3c1317a8c59c2da0c509fc41b3dc780c5fc9d9de15`.
  - The other six owned product/test files retain their prior hashes exactly.

## Executor Self-check — TASK-03-R5-20260812

- [x] Appended this distinct re-review remediation after R4 without modifying any review,
  prior Task round, controller state, another Task or Git history.
- [x] Replaced provenance inference with one neutral job restriction whose exact-all and
  validity semantics are directly consumable without archive vocabulary.
- [x] Covered optional absence, exact single all, all+family, all+all, all+unknown, known
  family positive/negative and invalid neutral state across focused Infrastructure and
  Application behavior tests.
- [x] Rejected malformed declarations at the archive boundary and retained a fail-closed
  Application validity check; no duplicate or unknown token can broaden availability.
- [x] Re-ran quest-source tests, CK-005, CK-006, the full real-PVF solution and diff check
  successfully; introduced no persistence/archive leakage, G6/G7 behavior or ignored-file
  mutation.
