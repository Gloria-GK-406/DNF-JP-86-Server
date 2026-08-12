---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Complete G5 Quest Eligibility

## Why and Outcome

The select-character result must carry the quests the selected character can actually be
offered, not a broad catalogue filtered by only level and class. Every eligibility decision
evidenced by the legacy quest index must be represented honestly before G6 sends the list;
required information that cannot be established may not silently turn into permission.

## Task Perspective

- Core outcome: `AvailableQuests` is the complete, deterministic answer for the selected character under the evidenced standalone-server rules.
- Necessary support: Quest definitions and the character subject carry every fact needed by those rules without leaking archive vocabulary or unrelated domain state.
- Peripheral assurance: A traceable rule classification, real-archive coverage, failure-before-publication regression and dependency inspection prove completeness and boundary integrity.

## Scope and Non-Goals

### In Scope

- Quest exposure, event, creature, expert-job, training, target-character, prerequisite,
  cleared-state, level, job and grow-type eligibility.
- The distinction between an absent optional restriction and missing mandatory eligibility
  data.
- Standalone-server classification of legacy rules whose original authority does not exist
  in this server.
- G5 coverage documentation for the resulting answer.

### Non-Goals

- Quest acceptance, progress, completion or reward mutation.
- Packet bodies, packet order or conditional sends.
- Session admission, town/PvP membership or other lifecycle transitions.
- General PVF catalogue expansion unrelated to deciding whether a quest is offerable.

## Constraints

- Archive spelling and token representation remain outside public content and application contracts.
- A missing mandatory predicate source narrows the answer or fails composition according to its evidenced semantics; it never broadens the answer.
- Empty means the full applicable catalogue was evaluated and no quest qualified.
- Existing non-quest G5 results and accepted G1-G4 domain ownership remain unchanged.

## Definitions

| Term | Meaning |
|---|---|
| Offerable quest | A selectable quest whose complete eligibility predicate evaluates true for the selected character at composition time. |
| Optional restriction | A quest field whose absence means the definition places no restriction of that kind. |
| Mandatory eligibility data | Definition or character state required to decide a declared restriction without guessing. |
| Standalone inapplicability | A legacy predicate whose governing feature is intentionally unavailable in this standalone server and whose safe observable result is explicitly classified rather than inferred. |

## External Behavior and Data Rules

- Quest order remains the authoritative catalogue order after ineligible entries are removed.
- Quest identifiers outside the client-representable selection range are never offered.
- A non-repeatable cleared quest is excluded; only the evidenced repeatable grades may return after completion.
- Prerequisite groups, collision exclusions and question-answer flag requirements are evaluated against the selected character's cleared state.
- A declared restriction for creature kind, expert job, target character or training availability requires matching character/server state; absence of that state does not count as a match.
- Job-change grow-type rules preserve their distinct masked and exemption semantics rather than being collapsed into ordinary exact-grow-type matching.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Every predicate used by the legacy selectable-quest path has one explicit disposition: applied to the result, proven irrelevant to this standalone entry answer, or rejected because its mandatory data is unavailable. |
| RQ-002 | Definitions that are hidden from NPC exposure, event-only, unsupported by the character's creature state, gated by an unmet expert job, invalid for training availability, or targeted at another character class are excluded. |
| RQ-003 | Level, job-family, target-character and job-change grow-type restrictions reproduce their distinct evidenced matching rules, including masked grow types and exemption values. |
| RQ-004 | Completed, repeatable, prerequisite-group, collision and question-answer requirements are evaluated against the selected character's complete cleared quest and flag state. |
| RQ-005 | Missing or malformed mandatory definition data never produces a broader offerable set, while absent optional restrictions retain unrestricted semantics. |
| RQ-006 | The available-quest answer preserves catalogue order and contains only client-representable unique identifiers. |
| RQ-007 | A quest content failure prevents the new selection from being published and leaves the prior active character intact. |
| RQ-008 | G5 coverage records the final predicate disposition and preserves code-reversed confidence without claiming packet-capture confirmation. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | A populated catalogue contains otherwise-valid quests that are hidden, event-only, creature-gated, expert-job-gated, training-gated and targeted at another class | The selected character's available quests are composed | Every failing definition is absent and an unrestricted valid definition remains. |
| AC-002 | RQ-003 | Definitions exercise ordinary job/grow matching, masked job-change matching and grow-type exemption values across matching and nonmatching characters | Eligibility is evaluated | Each definition follows its own rule and no branch is treated as ordinary exact matching by convenience. |
| AC-003 | RQ-004 | A character has cleared quests, answer flags and prerequisite/collision relationships containing both satisfied and unsatisfied alternatives | Eligibility is evaluated | Only repeatable or prerequisite-allowed quests without a collision are offered. |
| AC-004 | RQ-005 | One definition omits an optional restriction and another declares a restriction whose mandatory value is malformed or unavailable | The catalogue is loaded and evaluated | The optional definition remains eligible subject to other rules, while the undecidable restricted definition cannot widen the result. |
| AC-005 | RQ-006 | Catalogue order contains valid, invalid-range and duplicate quest identifiers | The result is composed | Valid unique identifiers retain first authoritative order and invalid-range entries are excluded. |
| AC-006 | RQ-007 | An account already has an active character and a new selection encounters a mandatory quest-content failure | The selection command runs | The failure propagates and the prior active character remains published. |
| AC-007 | RQ-008 | The implementation is complete | Coverage is reviewed | Every predicate is classified with its evidence and remaining protocol uncertainty is still labeled code-reversed. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-003, RQ-004 | AC-001, AC-002, AC-003 | Cross-checked eligibility matrix plus populated behavior evidence with positive and negative cases. |
| EV-002 | RQ-005 | AC-004 | Definition-source and service evidence distinguishing absent optional fields from malformed or unavailable mandatory restrictions. |
| EV-003 | RQ-006 | AC-005 | Stable-order, range and duplicate behavior evidence. |
| EV-004 | RQ-007 | AC-006 | Workflow failure regression showing publication state is unchanged. |
| EV-005 | RQ-008 | AC-007 | Reviewed G5 coverage documentation tied to legacy and real-archive evidence. |
| EV-006 | RQ-001, RQ-005, RQ-007 | AC-001, AC-004, AC-006 | Fresh complete-solution verification with the supplied archive reachable and dependency boundaries intact. |

## Assumptions

- The legacy selectable-quest path is the current behavioral authority until client testing provides contrary evidence.
- Runtime authorities absent from this standalone server are treated conservatively and documented; they are not simulated solely to make more quests appear.
