# TASK-03 — Handler Lifecycle Orchestration

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Move ordered SelectCharacter coordination and sending into the protocol handler, split business work into cohesive sequential command phases, and remove sender ownership from command handlers without changing lifecycle outcomes.
- Referenced spec requirements: RQ-001, RQ-003, RQ-005, RQ-006, RQ-007.
- Referenced plan sections: T-002; T-004; BD-003; BD-007; BD-008.
- Owned boundary: SelectCharacter admission/preparation, static synchronization, refill, publication/PvP, quests, pet revival, rejoin, failure compensation and connection-close workflow.
- Permitted files or interfaces: `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/**`; relevant SelectCharacter application/abstraction services; `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`; SelectCharacter lifecycle and handler tests. Concrete message encoder bodies accepted in TASK-02 may only receive compatibility fixes demonstrated by this integration.
- Dependencies: TASK-01, TASK-02.
- Required dependency outputs: Corrected dispatch rule, transcript oracle, registered concrete state messages.
- Acceptance conditions: The handler holds one selected-session transition boundary across sequential command dispatch and sends; sends `01/0004` before state synchronization; command handlers contain no `ICommandBus`, sender or socket orchestration; phase results contain domain/recipient facts rather than frames; stale-generation, required-send, concurrent replacement and cleanup behavior remain equivalent; no command handler recursively dispatches.
- Task-specific authorization: Covered by the root task authorization; no wire or persistence semantic changes.
- Required methods: Use existing lifecycle tests as behavior oracles; add focused tests for handler sequencing only where the externally observable order/failure effect is not already covered; use static checks for structural prohibitions.
- Required checks: CK-004, CK-005, CK-009, CK-010, CK-011 from `plan.md`; focused Application and Protocol tests; `git diff --check`.
- Required evidence: Command-phase inventory, handler send/dispatch order, lifecycle test results, source scans for forbidden dependencies and owned diff paths.

## Execution Result — Round 1

- Executor: root agent.
- Handler ownership: `SelectCharacterHandler` now owns the single `SelectedSessionTransitionGate` lease across all sequential command dispatches, required owner sends, best-effort target delivery planning, compensation decisions and post-lease connection closes.
- Command-phase inventory: `PrepareSelectedCharacterSessionCommand`; `ApplySelectedCharacterDailyRefillCommand`; `PublishSelectedCharacterSessionCommand`; conditional `MarkSelectedCharacterPvpReadyCommand`; `SynchronizeSelectedCharacterQuestsCommand`; `ReviveSelectedCharacterPetCommand`; `PlanSelectedCharacterRejoinCommand`; and `CompensateSelectedCharacterSessionCommand`.
- Phase boundary: command handlers invoke only their cohesive lifecycle/application services and return selected-session identity, current domain state, optional synchronization facts or explicit target/close facts. No command handler receives `ICommandBus`, `INetworkMessageSender` or `INetworkConnectionCloser`, and no command file sends network output.
- Send order: the handler asks `ISelectedCharacterStateMessageFactory` to construct and validate every required static message before acceptance, then sends the concrete list in factory order (`01/0004` first), sends pre-publication facts, dispatches daily refill, dispatches publication/PvP, sends trailing locks, dispatches quest and pet changes, and finally dispatches rejoin planning.
- Failure behavior: admission refusal remains an ordered X-08 delivery followed by the original refusal; preparation-phase failures carry explicit close facts while preserving the original exception; later failures dispatch exact compensation and close only after releasing the lifecycle lease; close failures remain best effort and cannot mask the workflow failure.
- Removed compatibility seam: deleted `SelectCharacterCommand`/monolithic handler, `SelectCharacterResponse`, its multi-frame encoder and composer. Removed the now-unused server catalogue member for the aggregate response.
- Regression-test migration: the 16 existing lifecycle scenarios now drive the real protocol handler through a recording command bus and assert the exact phase sequence. The lifecycle fixture replaces only the validated static-message factory because its intentionally minimal domain state never encoded the old aggregate response; TASK-02's complete 58-frame real-encoder oracle remains the wire proof.
- Systematic-debugging evidence: the first migrated lifecycle run reproduced 10 failures at `SelectedCharacterStateMessageFactory.AddInventorySnapshots` because `CreateLifecycleResultAsync` intentionally supplies no Main/Avatar/Cargo/Pet snapshot inputs. The old `RecordingSender` had only inspected `SelectCharacterResponse` and never encoded it. Introducing the validated factory seam preserved strict production prevalidation while keeping lifecycle tests scoped to orchestration; the exact focused rerun then passed 16/16.
- Check CK-004/CK-010: focused `SelectCharacterLifecycle` exited 0; 16 passed, 0 failed, 0 skipped.
- Check CK-009: focused Protocol SelectCharacter exited 0; 36 passed, 0 failed, 0 skipped.
- Broader checks: full Application tests exited 0; 256 passed, 10 skipped, 0 failed. Full Protocol tests exited 0; 88 passed, 0 failed, 0 skipped. Solution build exited 0 with 0 warnings and 0 errors.
- Static checks CK-005/CK-011: command-file scan found no command bus or sender APIs; obsolete aggregate response/composer, descriptor sequence and `EncodeNotification` scan returned no matches; `git diff --check` exited 0.

## Executor Self-check — Round 1

- RQ-001/RQ-006: the validated factory prevents late missing-state discovery after acceptance, and the handler sends its already validated concrete list in the accepted ACK-first order.
- RQ-005: one inbound handler visibly dispatches six ordinary phases on the non-PvP success path and the conditional PvP/compensation phases when applicable; every dispatch returns before the next begins. Existing command-bus protection still rejects dispatch while a command handler is executing.
- RQ-007: all 16 accepted, rejected, stale/concurrent, replacement, send-failure, rollback and post-lease close scenarios remain green against the handler-owned workflow.
- TASK-02 A1 is discharged: no aggregate SelectCharacter response, multi-frame encoder or composer remains.
- Residual boundary: supplemental typed projections and SelectCharacter-specific fan-out delivery remain intentionally unchanged for TASK-04, which owns their semantic-message conversion and transport cleanup.

## Independent Quick Review — IQR-01

- Reviewer: `/root/task03_quick_review` (fresh independent context).
- Verdict: `pass`; no blocking findings.
- Confirmed evidence: one transition lease covers every sequential phase dispatch, required send and compensation; close is lease-external best effort; the complete state list is prevalidated and sent ACK-first; command files contain no bus, sender, closer or socket APIs; old aggregate response/composer/command are absent.
- Confirmed staging boundary: supplemental projection facts and their special delivery remain only as the explicit TASK-04 dependency output, not as hidden command-handler orchestration.
- Fresh checks observed by reviewer: lifecycle 16/16, Protocol SelectCharacter 36/36, solution build 0 warnings/errors and `git diff --check` clean.
- Non-blocking observation remediated: corrected the stale `EntryServices.cs` XML reference from deleted `SelectCharacterCommandHandler` to `SelectCharacterPreparationService`.
