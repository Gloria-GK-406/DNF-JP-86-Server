---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Character Entry State

## Why and Outcome

The selection workflow needs one complete character-owned result assembled from cohesive business views, not a packet-shaped mega-snapshot. Consumers receive all character progress and optional features required by G3 while account, static-content, world and session data remain separate.

## Task Perspective

- Core outcome: A selected CharacterId yields complete, typed character-owned entry views for later mixed-domain composition and pure Networking projection.
- Necessary support: Exact ownership, explicit feature applicability, neutral unresolved fields, storage isolation and failure-before-publication keep the result honest.
- Peripheral assurance: Matrix coverage review, representative integration tests, dependency inspection and full regression prove completeness.

## Scope and Non-Goals

### In Scope

- Core/public/detail, fatigue, position, appearance, settings and progression views.
- Skills, skill points, quests and optional class features.
- Pet runtime, collections, expert job, dungeon/PvP/mail/daily/activity state.
- Repository-backed cohesive services and selection composition.

### Non-Goals

- Inventory already accepted by Spec 003.
- Account, content/world, admission/lobby/session and Networking projection.
- Entry-time mutations or fabricated meanings for unresolved legacy fields.

## Constraints

- `null` is only for inapplicable optional features; applicable loaded-empty state uses non-null empty collections.
- A missing applicable source, malformed value, ownership mismatch or incomplete composite is an error.
- Character views contain no account wallet/settings/progression, static catalog definitions or session data.
- Packet ordering and command identities do not shape the service APIs.

## G3 Coverage

| Slice | Matrix coverage | Character-owned outcome |
|---|---|---|
| Core/public | rows 1, 8-9, 54, 57; X-02-X-03 | Identity/detail, fatigue, position, appearance and visibility views. |
| Skills/classes | rows 10-11, 27; X-01, X-06 character portion | Skill pages/points plus nullable Dark Knight and Knight Shield features. |
| Quests/dungeon | rows 12-13, 18; X-12-X-14 | Quest progress/flags/notifications and dungeon progress, excluding definitions/admission. |
| Settings/progression | rows 16, 19-20, 30, 39-46, 49-51 | Character options/hotkeys, growth/cube/gold/fatigue/daily and neutral unresolved progress. |
| Pets/collections | rows 7, 21-26, 32-33, 58; X-17 character portion | Creature runtime, title/achievement/collection progress and optional equipped-pet entry data. |
| Profession/PvP/mail | rows 37, 47-48 | Expert profession, mailbox summary and PvP mission progress. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Loading a selected character returns cohesive core/public, fatigue, position, appearance, settings and progression views covering the declared matrix slice. |
| RQ-002 | Skills, quests, dungeon, daily/activity and optional class features preserve ordering, applicable-empty data and explicit inapplicability. |
| RQ-003 | Pet runtime, collections, expert profession, mailbox and PvP progress are complete character-owned views without static definitions or account overlays. |
| RQ-004 | Exact ownership and required-data validation prevent partial results; selection composes the complete character entry view before active publication. |
| RQ-005 | Persistence remains behind repository-driven cohesive services and existing behavior remains compatible. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-003 | An ordinary character has populated core and subdomain state | Entry state is loaded | Every applicable character view contains deterministically ordered stored data and no foreign-domain overlay. |
| AC-002 | RQ-002 | A non-Dark-Knight/non-Knight character has applicable empty skills/quests/collections | Entry state is loaded | Applicable collections are empty and both class-only features are null. |
| AC-003 | RQ-002 | An eligible class has stored empty or populated optional feature state | Entry state is loaded | The feature is non-null and faithfully represents loaded empty or populated data. |
| AC-004 | RQ-004 | A required slice is malformed, unavailable or owned by another character | Selection is attempted | Loading fails before active-character publication and no partial composite is returned. |
| AC-005 | RQ-005 | The complete G3 slice is integrated | Focused and complete checks run | Service/repository boundaries and all regression behavior pass. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-003 | AC-001, AC-002, AC-003 | Coverage cross-check plus ordinary and class-conditional service/integration tests. |
| EV-002 | RQ-004 | AC-004 | Ownership/malformed/failure-before-publication tests. |
| EV-003 | RQ-005 | AC-005 | Dependency inspection, complete review and fresh solution tests. |

## Assumptions

- Neutral compatibility records may preserve raw bounded blobs or unknown numeric fields when the matrix explicitly marks their meaning unresolved, but their ownership, presence and size remain validated.
