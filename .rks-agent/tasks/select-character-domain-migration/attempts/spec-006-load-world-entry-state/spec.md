---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: World and Content Entry State

## Why and Outcome

Selection must receive all G5 static definitions, world policy and session-adjacent read views as a separate completed result so Networking never queries content or world services while encoding entry packets.

## Task Perspective

- Core: complete typed G5 result and admission decision for a resolved ChannelId.
- Support: explicit constants, unavailable errors, ownership isolation and pre-publication rejection.
- Assurance: exact coverage, focused tests, dependency review and full regression.

## Scope and Non-Goals

In scope are G5 quest/activity/collection/pet definitions, events/constants, channel admission, social/town and PvP query views. Out of scope are sends, target resolution, membership/town mutation, refills and packet composition.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Static content views provide every definition required to combine G3 progress, with unavailable required sources failing explicitly. |
| RQ-002 | Server events, schedules, social state and unresolved fixed capabilities remain typed world results or documented protocol constants, never character/account fields. |
| RQ-003 | A listener resolves to ChannelId before command dispatch; channel admission produces an accepted typed view or rejects before publication. |
| RQ-004 | PvP/town/session-adjacent queries are loaded read views with explicit empty state and no membership/send mutation. |
| RQ-005 | Complete G5 state is composed before publication and dependency/regression boundaries remain passing. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | Configured content/world sources exist | Selection loads | Complete ordered definitions and honest constants appear outside character/account state. |
| AC-002 | RQ-003 | Listener maps to allowed or rejected channel | Message is handled | Business command receives ChannelId; rejected admission publishes nothing. |
| AC-003 | RQ-004 | Social/PvP/town query has no current entries | Selection loads | Explicit empty read views are returned without mutation. |
| AC-004 | RQ-001-RQ-005 | Required source is missing/malformed | Selection runs | Load fails before publication. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001-RQ-004 | AC-001-AC-003 | Exact G5 coverage and populated/empty focused tests. |
| EV-002 | RQ-003, RQ-005 | AC-002, AC-004 | Handler/workflow failure tests. |
| EV-003 | RQ-005 | AC-004 | Dependency review and full tests. |
