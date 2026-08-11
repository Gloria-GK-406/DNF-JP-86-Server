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

# TaskContract: Load World and Content Entry State

## Context

### Source Request

The authorized migration requires the non-character/account services used by select-character, while static content, channel/world policy and session-adjacent views remain outside character state.

### Current Situation

Accepted G1-G4 checkpoints now provide consistent selection plus complete inventory, character and account views. G5 matrix rows still depend on quest/activity/collection/pet definitions, server events/constants, channel admission, social/town state and PvP lobby/session-adjacent read views.

## Goal

Provide complete typed G5 world/content/session-adjacent entry views and channel admission result through cohesive services, with honest constants and unresolved meanings, and compose them before active publication without persistence or networking leakage.

## Scope

### In

- Static quest, activity, collection and pet-content definitions required to combine accepted G3 progress.
- Server event/activity state and explicitly code-confirmed empty/fixed protocol capabilities represented honestly.
- Game-channel resolution/admission for the listener target using a business channel identity after Networking resolution.
- Social/town initial state and PvP lobby/session-adjacent query views required by X-09/X-10 and lifecycle preparation, without sending or mutation.
- Typed `GameWorldEntryState`/session-adjacent result, cohesive services, configuration/static repositories and selection composition before publication.

### Out

- Packet encoding/order/target sends, lobby membership mutation, town enter/leave mutation, daily refills and other G6/G7 behavior.
- Reclassifying unresolved constants as character/account facts.

## Constraints

- Network connections/listener ports do not flow through business commands; Networking/Application resolves a configured `ChannelId` before command dispatch.
- Static definitions and world/session views remain outside character/account state.
- Confirmed empty standalone capabilities are explicit loaded constants; unavailable required content is an error, not empty.
- SQLite/file/config implementations remain behind abstractions and Networking performs no content loading.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-6.1 | Every G5 matrix identity has a typed field/service/explicit constant with traceable coverage and no fabricated character/account state. | Exact coverage and independent review. |
| SC-6.2 | Channel target is resolved before command dispatch and admission rejects before active publication. | Handler/command/workflow tests. |
| SC-6.3 | Content/world/social/PvP query services preserve loaded-empty versus unavailable semantics and expose no network/persistence implementations. | Populated/empty/failure tests and dependency review. |
| SC-6.4 | Selection composes complete world/session-adjacent state only after all required loads and before publication. | Workflow tests. |
| SC-6.5 | Existing solution behavior remains passing. | Fresh full tests and diff checks. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Root authorization: “对，好。来试着开始任务吧。有一些纰漏正常，放心干。毕竟原来的代码写的本来就是一坨。做完了告诉我结果”. | Implement, review, verify and locally commit this contained G5 checkpoint. |

## Requires Confirmation

- Push, merge, PR, cleanup, packet capture and excluded mutations/projection remain outside this child.

## Assumptions

- Current configuration/static legacy sources are sufficient to represent standalone-server content; protocol capture uncertainty remains documented.
