---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-5
  - SC-6
depends_on: []
---

# TaskContract: Project Complete Select Character Entry Stream

## Context

### Source Request

The operator explicitly authorized continuing without another checkpoint report until G7
is complete: “继续任务，完成G7后再和我说”. The root task already authorizes the complete
select-character migration and its accepted G1-G5 checkpoints now provide the business
result G6 consumes.

### Current Situation

Networking emits only a partially populated SELECT_CHARACTER acknowledgement. The legacy
entry stream contains 58 ordered static occurrences plus conditional and trailing X-rows.
The migration matrix assigns encoding, ordering, duplication, conditions and target shape to
G6 while forbidding Networking from loading data or performing lifecycle mutation.

## Goal

Encode the complete code-reversed select-character entry packet stream from typed completed
business/projection inputs, preserving exact command/type occurrence order, conditional
omission and one-to-many frame shape without repository, domain-service or mutation calls in
Networking.

## Scope

### In

- Complete the mixed-domain selection acknowledgement from `SelectCharacterResult`.
- Encode static rows 2 through 58 in the exact legacy sequence, including item-list rows,
  duplicate command/type occurrences and fixed protocol constants.
- Define typed projection inputs for conditional/trailing X-01 through X-18 packets when
  their data or targets are supplied by the application lifecycle.
- Keep packet byte construction, order, multiplicity and conditional omission in Networking.
- Preserve code-reversed confidence and explicit unresolved field names; do not invent
  missing business data or silently emit placeholder rows for required unavailable state.
- Add golden/order/condition verification proportionate to stable wire behavior.

### Out

- Session registration, registry mutation, inventory lease/refill mutation, position or
  appearance persistence, quest/pet transitions, rollback and target discovery (G7).
- Packet capture or external client execution.
- Repository or content queries from Networking.
- Unrelated game commands after entry.

## Constraints

- Networking consumes completed typed results/projections only and may not call business,
  repository, PVF or SQLite services.
- Static row order and duplicate occurrences match the migration matrix exactly.
- Conditional rows are omitted when genuinely inapplicable and fail when required typed
  data is unavailable; absence is not encoded as a fabricated empty business value.
- Target resolution and lifecycle side effects remain outside body encoders.
- Protocol assertions remain labeled code-reversed until client/capture confirmation.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-9.1 | Every static matrix row 1-58 has one body projection and appears in exact command/type/occurrence order. | Mechanical identity/order comparison plus representative byte evidence. |
| SC-9.2 | The ACK and mixed-domain packets consume the correct character/account/world limbs with no required field left as a convenience zero when typed state exists. | Focused populated projections and complete code review. |
| SC-9.3 | Item lists, class-conditional packets, pet welcome, supplemental, duplicate and trailing projections preserve their documented conditions and multiplicity. | Positive/negative condition and occurrence verification. |
| SC-9.4 | X-01-X-18 have typed body/projection seams sufficient for G7 to provide data and targets without Networking querying services or mutating state. | Contract and dependency inspection plus focused encoder evidence. |
| SC-9.5 | Networking has no repository, Infrastructure, PVF, SQLite or domain-service dependency and existing framing behavior remains valid. | Dependency scan and full solution verification. |
| SC-9.6 | Protocol documentation records exact implemented coverage and unresolved code-reversed semantics honestly. | Reviewed matrix/coverage update. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “继续任务，完成G7后再和我说” and the ready root authorization for the complete select-character migration. | Implement, review, verify and locally commit the complete G6 projection checkpoint in the existing root worktree. |

## Requires Confirmation

- Push, merge, pull request, external client execution, remote traffic and branch/worktree
  cleanup remain outside this child.

## Assumptions

- Legacy builders and the reviewed migration matrix are the current wire authority.
- G1-G5 typed results contain every static business value; where G7 supplies runtime data,
  G6 defines a projection seam rather than fabricating it.
