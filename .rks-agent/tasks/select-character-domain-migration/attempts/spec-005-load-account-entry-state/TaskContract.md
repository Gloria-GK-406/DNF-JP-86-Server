---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
depends_on: []
---

# TaskContract: Load Account Entry State

## Context

### Source Request

The authorized root migration requires cohesive services for every non-character entry domain, with AccountId as the business identity and SQLite hidden behind repositories.

### Current Situation

Accepted G1-G3 checkpoints provide selection consistency, inventory state and complete character-owned state. The G4 matrix rows still depend on account settings, wallet, entitlements, honor/adventure/growth, lottery/event progress, cross-character relations and one unresolved purchase-restriction list. `AccountEntryState` currently contains only AccountId.

## Goal

Provide a complete typed account-owned entry composition for the selected AccountId, loaded through cohesive services from normalized persistence, without copying character, static-content, world or session data into the account view.

## Scope

### In

- Account settings and input mode/quick-chat state.
- Wallet balances including Cera variants and Lucky Star; existing account virtual resources remain referenced through the accepted inventory view rather than duplicated.
- Premium/entitlement contracts and lottery/event progress.
- Honor, adventure and growth-capsule account progression.
- Cross-character support relation and roster-derived account portions.
- Neutral explicitly unresolved purchase-restriction compatibility entries.
- Cohesive service/repository boundaries and composition into selection before active publication.

### Out

- Account cargo implementation already accepted in G2.
- Character-owned usage/progression already accepted in G3.
- Static definitions, world/channel policy, session lifecycle, mutations and Networking projection.

## Constraints

- Account state is keyed by AccountId and cannot contain selected-character convenience fields.
- Applicable account collections are non-null even when empty; missing mandatory account rows or malformed values fail explicitly.
- Unresolved list semantics remain neutral and documented rather than promoted to commerce policy.
- SQLite implementation types remain outside Application and Networking.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-5.1 | Typed account views cover every G4 matrix row and character-independent conditional portion with traceable field/service mapping. | Exact G4 coverage review and populated/empty tests. |
| SC-5.2 | Settings, wallet, entitlement and progression services preserve normalized account data without character/static/world/session leakage. | Cross-slice repository/service tests and dependency review. |
| SC-5.3 | Missing/malformed/wrong-owner account state fails rather than becoming empty or defaults except where a demonstrated domain default exists. | Failure-path tests. |
| SC-5.4 | Selection composes account state before active publication and preserves prior state on load failure. | Workflow tests. |
| SC-5.5 | Existing behavior builds and all tests pass. | Fresh complete solution and diff checks. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Implement, review, verify and locally commit this contained G4 account-state checkpoint. |

## Requires Confirmation

- Push, merge, PR, cleanup and excluded domains remain outside this child.

## Assumptions

- Existing normalized account tables in legacy main are compatibility evidence; uncertain `Unknown730` entries remain neutral records.
