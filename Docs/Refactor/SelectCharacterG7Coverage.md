# SELECT_CHARACTER G7 lifecycle coverage

> Status: implemented and refactor-audited on 2026-08-15. All packet identities,
> lifecycle ordering and business meanings remain reverse-engineered from server code;
> they are not confirmed by an 86JP client or packet capture.

The Protocol `SelectCharacterHandler` is the visible workflow coordinator. It validates the
authenticated connection, holds one selected-session transition lease, sequentially dispatches
cohesive Application commands, and sends concrete messages. Command handlers own domain work but
contain neither a command bus nor a sender. Networking receives only messages, explicit targets
and connection IDs.

## Supplemental lifecycle ledger

| ID | Trigger and owner | Mutation / source | Delivery and failure | Exact rollback responsibility |
|---|---|---|---|---|
| X-07 | Entry inventory preparation after registration | PVF daily-refill rules; one SQLite transaction; reloadable in-memory lease | Only committed groups, required owner delivery in container order | Transaction rollback; reload lease; exact lease release during workflow compensation |
| X-09 | Free-duel `PlanPvp` | Current exact ready-session identities and complete room facts | Required owner identity roster before room snapshot; failure prevents ready state | Exact PvP readiness clear and session cleanup |
| X-10 | Same PvP plan | Complete rooms, then newcomer identity and explicit ready-peer targets | Owner rooms complete before `MarkPvpReady`; peers are best effort and isolated | Required owner failure rolls back; peer failure does not roll back owner |
| X-11 | Every accepted continuation after PvP phase | Prepared character/account equipment-lock result | Required owner trailing duplicate | Failure invokes full exact compensation and closes that connection |
| X-12 | Changed item-seeking quest trigger | PVF seeking operands, current inventory and owner-validated SQLite CAS | Required owner; absent when no committed change | No fabricated update; exact session compensation on send failure |
| X-13 | Equipped slot-24 creature transitions from dead to alive | Owner-validated SQLite mutation sets state to exactly 1 | Required owner revival identity; absent for already-alive/missing pet | Exact session compensation on required delivery failure |
| X-14 | Same committed revival | Domain state retains creature key and state | Codec emits immediately after X-13 and cannot emit it independently | Same required batch/compensation as X-13 |
| X-15 | — | **removed 2026-08-18** | Never reachable: no candidate was ever written | This game has no rejoin; see `Docs/领域结构重构设计.md` section 7 |
| X-16 | — | **removed 2026-08-18** | Unreachable without X-15 | Same |
| X-17 | Cleanup of a session owning an expert store | Capture area targets before exact close | Owner is required only for previous-character cleanup; peers are explicit best effort | All cleanup mutations run even when delivery/cancellation fails |
| X-18 | Same captured town-presence cleanup plan | Exact presence withdrawal after target resolution | One leave body to each eligible peer; owner is not a town peer | Stale cleanup cannot withdraw replacement identity |

## Legacy lifecycle constraints

| ID | Successor owner and implemented disposition |
|---|---|
| L-01 | `SelectedSessionCleanupService` resolves X-17/X-18 before independently closing store, withdrawing presence and clearing exact registries. |
| L-02 | Entry mutation ports preserve owner-validated character preparation; missing mutation capability fails instead of reporting success. |
| L-03 | `SelectCharacterEntryInventoryService` prepares/reloads inventory while the separate `IEntryInventoryLeaseRegistry` owns exact session release. |
| L-04 | Preparation resolves channel placement/current appearance; command persists position only when channel policy requires it. |
| L-05 | Directory cleans the previous exact identity, registers replacing, then cleans any distinct displaced identity without character-key deletion. |
| L-06 | Preparation derives current skill protocol facts from PVF before the required static stream; free-duel applicability remains session-owned. |
| L-07 | Static and X-02–X-07 are required before publication; PvP direct precedes ready/peer publication; X-11 follows. |
| L-08 | Changed-only quest synchronization occurs after X-11 and emits X-12 only for committed changes. |
| L-09 | Pet transition is the last staged phase since rejoin planning was removed; X-13/X-14 preserve codec-owned adjacency/order. |
| L-10 | Any exception after registration attempts every exact cleanup action and closes only the failed connection; admission refusal stops after required X-08. |

## Identity, ordering and rollback boundary

- `AccountId` remains the business identity. Connection ID and the pre-bind online
  generation exist only in `SelectedSessionIdentity` and exact session registries.
- `PublishSelectedCharacterSessionCommand` publishes the current runtime only after static
  and pre-publication required delivery. Exact cleanup unbinds only that session identity.
- Disconnect captures the selected identity by connection, performs non-cancellable exact
  cleanup, then removes the same online connection. A late old disconnect cannot remove a
  replacement session or a newer authenticated generation.
- Required owner sends propagate failure. `TouchSocketNetworkMessageSender` isolates each
  already-addressed peer batch; Networking never discovers sessions, content or storage and
  contains no SelectCharacter-specific branch.

This is implementation coverage, not real-client compatibility evidence. Client execution
and packet capture are still required before any inferred protocol meaning can be promoted.
