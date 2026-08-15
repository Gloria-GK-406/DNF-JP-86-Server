# TASK-04 — Generic Delivery and Supplemental Synchronization

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Replace SelectCharacter-specific supplemental projection/composer transport semantics with concrete reusable messages and protocol-neutral explicit-recipient delivery while preserving required-direct and best-effort behavior.
- Referenced spec requirements: RQ-003, RQ-004, RQ-006, RQ-007, RQ-008, RQ-010.
- Referenced plan sections: T-005; BD-004; BD-005; BD-006; BD-008.
- Owned boundary: Supplemental X-02–X-18, inventory deltas, admission rejection, PvP direct/publication, trailing locks, accepted quests, pet revival, dungeon rejoin and cleanup notices; Networking delivery exception removal.
- Permitted files or interfaces: `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/**`; supplemental-related SelectCharacter files; `Server/src/Dfo.Server.Data/Game/SelectCharacterSupplementalModels.cs`; `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`; `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`; related Application services, ProtocolModule and focused tests.
- Dependencies: TASK-03.
- Required dependency outputs: Handler-driven workflow and concrete static message infrastructure.
- Acceptance conditions: Networking contains no SelectCharacter-specific composer or projection delivery contract; protocol/domain code supplies concrete messages and explicit recipients; required-direct failure propagates; each secondary recipient is isolated best-effort; paired/batched semantic events preserve adjacency and order; `00/000E` remains reusable; packet-ready SelectCharacter projection types are removed or renamed/narrowed to domain facts.
- Task-specific authorization: Covered by the root task authorization; endpoint behavior and recipient failure semantics must not change.
- Required methods: Preserve existing projection byte and delivery tests as behavior oracles, migrating their fixtures to concrete messages; use source scans for semantic leakage.
- Required checks: CK-012, CK-013, CK-014 plus relevant CK-007 and CK-010 from `plan.md`; `git diff --check`.
- Required evidence: Generic delivery contract description, before/after supplemental identity map, focused Networking/Application/Protocol test results, leakage scan and owned diff paths.

## Execution Result — Round 1

- Generic delivery: `ExplicitMessageDelivery` contains ordered required-direct concrete messages and explicit best-effort targets. `TouchSocketNetworkMessageSender` resolves every message through the normal CLR encoder registry; required failures propagate, owner targets are excluded, and each secondary target is isolated after its first failure.
- Transport boundary: removed `ISelectCharacterProjectionComposer`, `SelectCharacterProjectionDelivery`, `SendProjectionAsync` and the Networking composer callback. Source scans of Networking and `Abstractions/Networking` contain no `SelectCharacter` or `Projection` semantics.
- Supplemental conversion: X-02–X-18 are represented by 14 concrete `SupplementalSynchronizationMessage` types, including fixed adjacent rejection, pet-revival and dungeon-rejoin batches. Reflection coverage requires every static and supplemental message type to have exactly one Game encoder registration.
- Domain inputs: removed `ISelectCharacterSupplementalProjection` and all packet-ready `SelectCharacter*Projection` records. Inventory, quest, pet, roster, PvP, rejoin and cleanup outputs are domain/workflow facts. PvP participant state contains only the character/public/equipment/pet inputs required by its message and no longer carries `SelectCharacterResult`.
- Application boundary: cleanup services no longer inject a sender. They resolve domain notification plans; Protocol maps them to concrete expert-store/town messages and invokes generic explicit-recipient delivery.
- Cleanup failure correction: independent review found that an exact cleanup failure could suppress an already-resolved notification plan. `SelectedSessionCleanupFailure` now carries that plan; preparation, compensation and disconnect paths still attempt it while retaining the original cleanup/workflow failure. Two new focused scenarios lock plan preservation and attempted disconnect delivery.
- Focused checks: Application lifecycle/session/explicit-delivery suites passed 33 tests; Protocol SelectCharacter/state-synchronization suites passed 36; full Networking passed 45. Solution build passed with 0 warnings/errors and `git diff --check` was clean.
- Static checks: state-synchronization Protocol/Data sources contain no `SelectCharacterResult`; command files contain no bus/sender API; old projection interfaces/composer/API and SelectCharacter-specific transport terms are absent.

## Executor Self-check — Round 1

- RQ-003/RQ-004: concrete message identity selects encoding; no application result or central supplemental CLR-type switch crosses into the encoder boundary.
- RQ-007: required-direct, peer isolation and exact cleanup behavior remain explicit, including notification attempts when cleanup itself partially fails.
- RQ-008: `InventoryDeltaMessage` (`00/000E`) remains independently constructible from committed inventory facts; the full `00/000D` snapshot remains a distinct concrete message.
- RQ-010: Networking owns only endpoint/message transport and generic recipient policy; Protocol owns every wire family and Application owns mutations/target discovery.

## Independent Quick Review — IQR-01 / IQR-02

- Reviewer: `/root/task03_quick_review` in fresh read-only turns.
- Initial verdict: `changes_required` because cleanup could resolve notifications, fail a later exact action and throw before exposing the plan to Protocol.
- Remediation evidence: introduced `SelectedSessionCleanupFailure.NotificationPlan`; phase, compensation and disconnect consumers attempt the plan in mutually exclusive success/failure branches; added service- and observer-level regression tests.
- Final verdict: `pass`; no blocking findings. Reviewer confirmed no duplicate delivery, no masking of the primary workflow failure, protocol-neutral transport, registration completeness and clean leakage scans.
