---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Complete G5 Quest Eligibility

## Implementation Approach

Start with a rule/source matrix because the core answer cannot be implemented truthfully
until each legacy predicate is paired with both its definition input and character/server
input. Then extend the neutral content contract and real-archive projection, apply the rules
in the existing world-content service, and finally wire the additional selected-character
facts into composition. Tests drive observable exclusions and the failure-before-publication
boundary; documentation records any predicate proven irrelevant rather than pretending it
was implemented.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Docs/Refactor/SelectCharacterWorldEntryCoverage.md` | Replace the known quest-filter gap with a predicate/source/disposition matrix and current confidence. | Legacy quest relation/index behavior and accepted G5 coverage. | Traceable G5 eligibility evidence. |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Content/QuestContent.cs` | Add neutral definition values needed to decide offerability, including relationships and restriction presence. | PVF quest/prerequisite declarations. | Archive-independent typed quest definitions. |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs` | Parse and validate the required definition and relationship fields from the supplied archive. | `Script.pvf`, neutral content contract. | Complete ordered quest catalogue or explicit source failure. |
| BD-004 | `Server/src/Dfo.Server.Abstractions/World/IWorldEntryServices.cs` | Carry the selected character facts needed by eligibility without carrying repositories or archive vocabulary. | Character/account entry state already composed by G1-G4. | Complete `WorldContentSubject`. |
| BD-005 | `Server/src/Dfo.Server.Application/World/WorldContentEntryService.cs` | Evaluate the full deterministic offerability predicate while preserving catalogue order. | Typed quest catalogue and complete subject. | Correct `AvailableQuestEntryState`. |
| BD-006 | `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs` | Supply the additional quest subject facts before publication. | Active character and character-entry result. | Fully populated content question with unchanged late publication. |
| BD-007 | `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfQuestDefinitionSourceTests.cs` | Protect real-archive parsing, classification and malformed-source behavior. | Supplied archive and focused fixtures. | Infrastructure evidence for RQ-001/RQ-005/RQ-006. |
| BD-008 | `Server/tests/Dfo.Server.Application.Tests/World/WorldContentRuleTests.cs` | Protect positive and negative eligibility combinations and order. | Typed populated definitions and subjects. | Business-rule evidence for RQ-002-RQ-006. |
| BD-009 | `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs` | Protect failure-before-publication with the expanded content question. | Select-character workflow fixture. | Publication regression evidence for RQ-007. |

## Ordered Tasks

### T-001: Establish the complete rule and data disposition

- Covers: RQ-001, RQ-008
- Boundaries: BD-001
- Inputs: Legacy selectable-quest implementation, current G5 coverage, real archive vocabulary.
- Outputs: One exact predicate list with definition source, character/server source and applied/inapplicable/failure disposition.

#### Steps

1. Cross-check the legacy eligibility loop, prerequisite evaluator, training-quest list and quest-file fields.
2. Inspect representative real-archive definitions for every retained predicate and count missing/malformed forms.
3. Record the resulting disposition without claiming packet-capture confirmation.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `rg -n "exposed|event|creature|expert|training|target|prerequisite|collision|question|job.change" Docs/Refactor/SelectCharacterWorldEntryCoverage.md` | Every legacy predicate category has a documented disposition. |
| CK-002 | Non-command: cross-check the documented predicate set against the complete legacy selectable-quest loop. | No predicate is omitted or classified without evidence. |

### T-002: Complete neutral quest definitions at the archive boundary

- Depends on: T-001
- Covers: RQ-001, RQ-005, RQ-006
- Boundaries: BD-002, BD-003, BD-007
- Inputs: Predicate matrix, supplied archive, existing quest content contract and source.
- Outputs: Complete validated neutral quest definitions in authoritative order.

#### Steps

1. Add focused RED cases for required restriction fields, prerequisites, invalid IDs and absent optional restrictions.
2. Extend neutral values only for predicates in the matrix.
3. Parse and validate quest/list/relationship declarations, preserving first authoritative order and explicit optionality.
4. Run real-archive vocabulary and populated projection checks.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~PvfQuestDefinitionSourceTests` | All focused definition-source cases pass with the real archive reachable. |
| CK-004 | `rg -n "Dfo.Pvf|PvfToken|PvfArchive" Server/src/Dfo.Server.Abstractions Server/src/Dfo.Server.Data Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking` | No archive implementation type leaks beyond Infrastructure. |

### T-003: Evaluate complete character eligibility

- Depends on: T-002
- Covers: RQ-002, RQ-003, RQ-004, RQ-005, RQ-006
- Boundaries: BD-004, BD-005, BD-006, BD-008
- Inputs: Complete definitions, active character, cleared quest/flag, expert-job and creature facts.
- Outputs: Ordered, unique and fully filtered `AvailableQuests` result.

#### Steps

1. Add RED behavior cases for every exclusion category, grow-type branch, prerequisite alternative, collision and question-answer flag.
2. Extend the world content subject with only the required selected-character facts.
3. Implement the complete predicate in catalogue order, conservatively handling declared restrictions without state.
4. Wire the subject from the already-loaded domain results without new repository access.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~WorldContentRuleTests` | All positive, negative, order, range and duplicate eligibility cases pass. |
| CK-006 | `rg -n "Microsoft.Data.Sqlite|Dfo.Pvf" Server/src/Dfo.Server.Application Server/src/Dfo.Server.Networking` | Application and Networking remain free of persistence/archive dependencies. |

### T-004: Prove workflow preservation and close the checkpoint

- Depends on: T-003
- Covers: RQ-007, RQ-008
- Boundaries: BD-001, BD-006, BD-009
- Inputs: Completed eligibility implementation and select-character workflow.
- Outputs: Publication regression, final documentation and complete verification evidence.

#### Steps

1. Add or adapt the failure-before-publication case for a mandatory quest content failure.
2. Reconcile the final implementation against the predicate matrix and update any evidence-backed disposition wording.
3. Run focused and full verification with the supplied archive, then inspect the complete diff and dependencies.

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~CharacterEntryInventoryTests` | Content failure leaves the prior active character intact and all workflow cases pass. |
| CK-008 | `dotnet test Server/86JP.Server.slnx --no-restore` | The complete solution passes with no failed test and the expected archive-present skips only. |
| CK-009 | `git diff --check` | The complete change has no whitespace errors. |
| CK-010 | Non-command: review the complete diff against RQ-001 through RQ-008 and every non-goal. | No predicate, boundary, failure rule or documentation obligation is missing and no G6/G7 scope is introduced. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-002 | BD-001, BD-002, BD-003, BD-007 | CK-001, CK-002, CK-003, CK-004 |
| RQ-002 | T-003 | BD-004, BD-005, BD-006, BD-008 | CK-005 |
| RQ-003 | T-003 | BD-004, BD-005, BD-006, BD-008 | CK-005 |
| RQ-004 | T-003 | BD-002, BD-004, BD-005, BD-006, BD-008 | CK-005 |
| RQ-005 | T-002, T-003 | BD-002, BD-003, BD-004, BD-005, BD-007, BD-008 | CK-003, CK-004, CK-005, CK-006 |
| RQ-006 | T-002, T-003 | BD-002, BD-003, BD-005, BD-007, BD-008 | CK-003, CK-005 |
| RQ-007 | T-004 | BD-006, BD-009 | CK-007, CK-008 |
| RQ-008 | T-001, T-004 | BD-001 | CK-001, CK-002, CK-010 |

## Risks and Rollback

- Risk: Legacy parsing tolerated missing quest files and malformed prerequisite definitions by silently skipping them; reproducing that tolerance would widen the result. The plan requires explicit classification and negative coverage before acceptance.
- Risk: Adding all character state to the content subject would recreate a context object. Only facts consumed by evidenced predicates may cross the boundary.
- Rollback: Until accepted, restore only this attempt's classified product/test changes to baseline `94616f4` while preserving the root controller and sealed child evidence according to the parent workflow.

## Assumptions

- The supplied archive remains available at the task worktree root for real-source checks.
- Existing character entry state contains the authoritative expert-job and cleared quest flag values needed by the legacy predicate.
