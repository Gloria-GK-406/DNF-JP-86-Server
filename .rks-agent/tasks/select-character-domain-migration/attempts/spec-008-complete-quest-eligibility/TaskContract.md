---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
  - SC-6
depends_on: []
---

# TaskContract: Complete G5 Quest Eligibility

## Context

### Source Request

The operator resumed the select-character migration and authorized completing every
remaining group through G7 when no external blocker exists: “这里有没有阻塞点？如果没有那就直接一次性完成到G7，我们用客户端实机跑一下看看就知道了。”

### Current Situation

The accepted G5 result computes `AvailableQuests` before publication, but its coverage
document records that the answer omits legacy eligibility predicates for NPC exposure,
event state, expert-job requirements, creature evolution, target character, prerequisite
chains and job-change grow-type handling. It can therefore offer quests the legacy server
would reject. Real `Script.pvf` is available and the accepted zero-domain PVF reader plus
quest source already expose the correct infrastructure boundary, so this is an in-repository
fidelity gap rather than an external block.

## Goal

Make the G5 available-quest answer apply every eligibility rule evidenced by the legacy
select-character quest index that is meaningful for this standalone server, while keeping
PVF representation inside Infrastructure and making any deliberately unsupported rule
explicit instead of silently accepting it.

## Scope

### In

- Cross-check the current quest definition source and `WorldContentEntryService` against the
  legacy quest-index eligibility path.
- Extend neutral quest definition and character subject data only where a demonstrated
  eligibility rule requires it.
- Parse the required PVF fields through Infrastructure without exposing token or archive
  types through Data, Abstractions or Application.
- Apply deterministic eligibility rules before `AvailableQuests` reaches
  `SelectCharacterResult`.
- Preserve repeatable-grade, completed-quest, level, job and grow-type behavior already
  accepted unless legacy evidence demonstrates a correction.
- Update G5 coverage documentation to distinguish applied, inapplicable and genuinely
  unresolved predicates.

### Out

- Any Networking packet encoding or G6 ordering.
- Runtime session registry writers, quest acceptance/progress mutation, daily refill or any
  G7 lifecycle behavior.
- Broader PVF item/equipment catalog work unrelated to quest eligibility.
- Fabricating event, expert-job, creature or prerequisite state when a required source is
  absent.

## Constraints

- Application and Networking may not reference `Dfo.Pvf` or archive/token types.
- Missing mandatory quest eligibility data must fail explicitly or make the quest
  ineligible according to demonstrated legacy semantics; it must not silently widen the
  result.
- Empty `AvailableQuests` means the complete applicable catalogue was evaluated and no
  quest qualified.
- Character, account and world/content ownership remain separated in the composed result.
- Existing accepted behavior outside quest eligibility remains unchanged.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-8.1 | Every legacy eligibility predicate is mapped to an applied rule, a proven standalone-server inapplicability, or an explicitly unresolved failure boundary. | Reviewed rule matrix cross-checked against the legacy quest index and current PVF fields. |
| SC-8.2 | `AvailableQuests` excludes quests that fail any modeled predicate and retains valid repeatable/completed, level, job and grow-type behavior. | Focused behavior tests containing both accepted and rejected populated definitions. |
| SC-8.3 | Required PVF fields are parsed into neutral storage contracts with strict malformed/missing-source behavior and no PVF type leakage. | Infrastructure tests using the real archive plus dependency inspection. |
| SC-8.4 | Select-character composition still completes before publication and a quest-source failure preserves the prior active character. | Application workflow regression evidence. |
| SC-8.5 | The complete solution and existing PVF-backed behavior remain passing. | Fresh focused and solution verification with the real archive reachable. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “这里有没有阻塞点？如果没有那就直接一次性完成到G7，我们用客户端实机跑一下看看就知道了。” together with the existing root authorization for the complete select-character migration. | Implement, review, verify and locally commit this bounded G5 fidelity checkpoint in the resumed root task worktree. |

## Requires Confirmation

- Push, merge, pull request creation, client execution, external-server traffic, branch or
  worktree cleanup still require separate direction at the point they are needed.

## Assumptions

- Legacy code is the current protocol/behavior evidence; later client testing may correct
  code-reversed semantics without making fabricated state acceptable now.
- The supplied 86JP `Script.pvf` contains the quest fields referenced by the legacy index.
