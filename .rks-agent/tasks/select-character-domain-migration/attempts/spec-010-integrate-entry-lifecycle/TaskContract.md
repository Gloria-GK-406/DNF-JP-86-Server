---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-4
  - SC-6
depends_on: []
---

# TaskContract: Integrate the Select Character Lifecycle

## Context

### Source Request

The operator authorized uninterrupted completion through G7: “继续任务，完成G7后再和我说”.
The accepted G1-G5 slices now supply business state and accepted G6 projects the complete
58-row static stream plus typed X-01-X-18 seams.

### Current Situation

The live message handler still runs a read/composition command, publishes an active
character, then sends the static response once. The runtime registries are query-capable
but do not carry connection generations or recipient connections, and no workflow owns the
legacy entry preparation, exact replacement, post-entry mutations, target publication or
rollback represented by X-07, X-09-X-18 and L-01-L-10.

## Goal

Execute Select Character as one account-exclusive, generation-safe application lifecycle
that prepares and delivers the complete entry stream, performs only successful post-entry
mutations, publishes the current character/session at the correct boundary, and
compensates a failed registered selection without deleting a newer generation.

## Scope

### In

- Carry the authenticated connection identity and expected generation from the network
  message boundary into the Select Character workflow while retaining `AccountId` as the
  only business identity used by character, inventory, account and quest services.
- Validate admission and wire user identity before cleanup, registration or mutation;
  rejected entry emits the ordered X-08 response and stops without side effects.
- Clean a connection's previous character and any displaced selected generation with
  exact-generation semantics, including X-17/X-18 target projections and persistence or
  runtime detachment owned by narrow services.
- Prepare the selected character through cohesive character, inventory, world and session
  services; deliver the accepted G6 static stream and X-01-X-06 before publication.
- Implement the G7-triggered X-07 and X-09-X-18 behavior, including transactional daily
  refill, PvP direct-then-target publication, trailing locks, changed-only quest sync,
  adjacent pet revival packets, ordered dungeon-rejoin packets, and explicit target sets.
- Publish and remove selected sessions and active-character state using exact session
  identity so stale disconnect or rollback cannot clear a replacement.
- Compensate failures after registration, isolate best-effort compensation/target failures,
  and close the failed connection while preserving any newer selected generation.
- Preserve the accepted typed G6 framing boundary and update the migration documentation
  with the implemented G7 status and remaining real-client uncertainty.

### Out

- Unrelated game commands after entry, full dungeon gameplay, general PvP room mutation,
  general inventory mutation APIs, or pet simulation beyond select-character entry.
- Packet capture, external server traffic, or real-client execution.
- Push, merge, pull request, branch deletion or worktree cleanup.

## Constraints

- The Select Character handler coordinates cohesive domain/application services; it does
  not move account, content, inventory, quest, PvP or session state into a character entity
  and does not dispatch nested commands.
- Character/equipment collaboration uses narrow facts or lifecycle interfaces rather than
  exposing a full character service to the inventory domain.
- Networking only frames and sends completed typed messages/projections. It never discovers
  recipients, queries registries or repositories, reads PVF, or performs lifecycle mutation.
- Connection and generation facts are confined to session lifecycle boundaries; downstream
  business services receive `AccountId` and owner-specific identifiers, not `OnlineUserId`.
- Required direct delivery failure is a workflow failure; explicitly targeted broadcasts
  are best effort and cannot roll back a successfully prepared owner solely because another
  recipient disconnected.
- Missing applicable data or mutation capability is an error, never a fabricated empty or
  successful transition.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-10.1 | Admission or invalid wire identity rejects before cleanup, registration, mutation or publication; X-08 is delivered in its required order for admission refusal. | Event-ledger and side-effect verification for rejection and invalid-ID paths. |
| SC-10.2 | Previous-character leave, same-account replacement and displaced-session cleanup use exact connection/generation ownership; stale disconnect and stale rollback cannot clear the current replacement. | Concurrent/replacement behavior evidence with exact-remove and active-state assertions. |
| SC-10.3 | Successful entry preserves the documented lifecycle order from preparation through static/X delivery, publication, PvP, locks, quest, pet and rejoin behavior. | One integrated ordered event ledger plus focused conditional-path evidence. |
| SC-10.4 | Daily refill, quest seeking sync and pet revival mutate persisted/leased state only when their conditions succeed; failure or no-change paths emit no false update and retain a reloadable state. | Peripheral adapter/service behavior evidence over populated, no-op and failure cases, including real PVF rule loading where applicable. |
| SC-10.5 | PvP, town, expert-store and dungeon-rejoin projections use current explicitly resolved connections, preserve required direct order and participant order, and tolerate disconnected best-effort targets. | Registry and delivery behavior evidence for direct failure, target failure, ordering and invalid/expired candidates. |
| SC-10.6 | Any failure after selected-session registration runs generation-safe compensation, attempts all independent cleanup, closes the failed connection, and does not fabricate restoration of an already-left previous runtime. | Fault-injection evidence across preparation, required send and compensation exceptions. |
| SC-10.7 | Architecture remains domain-separated: the workflow coordinates narrow services, business layers do not construct frames, Networking has no repository/PVF/business-query dependency, and no nested command dispatch is introduced. | Complete code review, dependency inspection and existing command-boundary regression evidence. |
| SC-10.8 | The complete solution and Host start successfully with the supplied PVF, and G7 documentation distinguishes code-reversed implementation from client-confirmed behavior. | Fresh full solution, Host startup smoke evidence, diff/dependency checks and reviewed documentation. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | “继续任务，完成G7后再和我说” under the ready root Select Character migration authorization. | Implement, review, verify and locally commit the integrated G7 lifecycle in the existing parent-managed worktree. |

## Requires Confirmation

- Push, merge, pull request, external client execution, packet capture, remote traffic and
  branch/worktree cleanup remain outside this child.

## Assumptions

- The accepted migration matrix and legacy server implementation remain the current
  lifecycle authority; client behavior is a later validation step.
- The supplied `Script.pvf` and normalized SQLite schema are available to implement the
  two remaining content-driven entry mutations without inventing rules.
