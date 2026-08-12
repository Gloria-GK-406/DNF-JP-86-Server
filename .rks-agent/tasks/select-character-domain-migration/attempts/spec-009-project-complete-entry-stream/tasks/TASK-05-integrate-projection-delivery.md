# TASK-05 — Integrate Explicit Projection Delivery

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Deliver ordered static, required-direct and best-effort-target projection batches to explicit connection recipients without target discovery or reverse business calls.
- Referenced spec requirements: RQ-001, RQ-004, RQ-006; AC-001, AC-004, AC-006.
- Referenced plan sections: T-005; BD-006, BD-007; CK-006, CK-007.
- Owned boundary: Server network messages, sender abstraction/runtime implementation and focused transport behavior tests.
- Permitted files or interfaces: `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`; `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`; `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`; directly affected Application sender callers/test doubles; focused Networking/Application tests.
- Dependencies: TASK-04.
- Required dependency outputs: Accepted static and supplemental projections.
- Acceptance conditions: Per-recipient order is stable; required direct failure propagates; disconnected best-effort targets are isolated; owner exclusion is explicit; sender discovers no business/session state and Networking gains no repository/PVF/SQLite dependency.
- Task-specific authorization: Integrate delivery contracts and transport behavior only; do not implement G7 trigger logic or lifecycle mutation; do not commit or push.
- Required methods: Behavior-first sender batch semantics where observable; structural checks for dependency direction.
- Required checks: CK-006 and CK-007; Networking and directly affected Application tests; `git diff --check`.
- Required evidence: RED/GREEN or justified test-value decisions, recipient/order evidence, changed paths, dependency scan, checks, owned diff identity and executor self-check appended to this file.

## Task Contract Amendment A-001 — CK-007 Architectural Intent

- Reason: the literal plan CK-007 pattern also matches existing Application repository abstractions (`ICharacterRepository`, `IAccountRepository` and entry repository ports), although the approved architecture explicitly permits Application to drive repository ports and the Task acceptance forbids repository/PVF/SQLite dependencies in Networking plus concrete SQLite/PVF dependencies in Application.
- Bounded correction: verify `Microsoft.Data.Sqlite|Dfo.Pvf|I[A-Za-z]+Repository` has zero matches in Networking; verify `Microsoft.Data.Sqlite|Dfo.Pvf` has zero matches in Application. Existing Application repository abstractions remain allowed and are not a TASK-05 product change.
- Coverage and authorization are unchanged: this amendment corrects only the mechanically over-broad validation expression and does not weaken Networking isolation or permit any concrete persistence/content dependency in Application.

## Execution Result ER-001

- Status: complete.
- Test-value gate: passed. Required-direct order/failure and optional-target failure isolation are transport-observable semantics; regression tests materially protect against partial success being reported as required success and against one disconnected recipient blocking unrelated recipients.
- Behavioral RED: CK-006 initially failed compilation with `CS0246` for the absent `SelectCharacterProjectionDelivery`/`ExplicitProjectionTarget` contracts and `CS0117` for the absent `DeliverProjectionAsync` delivery seam.
- Implemented contracts:
  - `SelectCharacterProjectionDelivery` carries one ordered required-direct frame list and an ordered list of already-addressed best-effort targets.
  - Each `ExplicitProjectionTarget` contains only a `NetworkConnectionId` and its ordered frames; no business identity, session registry or query handle is present.
  - `INetworkMessageSender.SendProjectionAsync` provides the typed delivery operation while retaining compatibility for current sender test doubles/callers through the existing `SendAsync` implementation contract.
- Delivery behavior:
  - Required-direct frames are sent sequentially to the supplied connection; the first failure propagates and prevents later direct and target sends.
  - Best-effort targets are processed in supplied order and frames remain sequential per target. A failure stops only that target's remaining frames and processing continues with the next explicit target.
  - A target equal to the required-direct/owner connection is explicitly skipped, preventing accidental owner duplication. Caller cancellation still propagates.
  - The runtime resolves only the supplied connection IDs to transport sessions; it never enumerates connections or discovers recipients/business/session state.
- Fresh checks:
  - CK-006 `--filter "FullyQualifiedName~SelectCharacter|FullyQualifiedName~TouchSocketNetworkMessageSender"` — passed 32/32.
  - Full Networking project — passed 61/61.
  - Directly affected Application project — passed 199/199; existing test doubles compile unchanged.
  - Corrected CK-007 per A-001 — Networking repository/PVF/SQLite scan: zero matches; Application concrete SQLite/PVF scan: zero matches.
  - Delivery ownership/discovery scan over the three product files — no registry enumeration, online-user/session business service, repository, SQLite or PVF dependency.
  - Full solution with `PVF_ARCHIVE_PATH="$PWD/Script.pvf"` — passed: PVF 60/60, Application 199/199, Networking 61/61, Infrastructure 90/92 with the two documented archive-negative tests skipped.
  - `git diff --check` — passed.
- Owned paths:
  - `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`
  - `Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs`
  - `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`
  - `Server/tests/Dfo.Server.Networking.Tests/Integration/TouchSocketNetworkMessageSenderProjectionTests.cs`
- Complete owned diff identity: `59722c5f806bd580d59f8a7e7231eeb1f18090ce`, generated from the full binary diff of all four owned paths using an isolated alternate index including the untracked test (208 insertions); the shared index was not changed.
- Boundary: no G7 trigger, recipient discovery, lifecycle transition/mutation, rollback, controller/review/result, commit or push was added.

## Executor Self-check SC-001

- Required delivery: PASS — stable sequential order and failure propagation are behavior-covered.
- Best effort: PASS — per-target order is stable; disconnected/failing targets are isolated; cancellation is not swallowed.
- Addressing: PASS — every optional recipient is an explicit `NetworkConnectionId`; owner exclusion is deterministic and tested.
- Architecture: PASS — sender resolves supplied transport IDs only and contains no reverse Application/domain calls, enumeration or persistence/content dependency.
- Compatibility: PASS — existing `INetworkMessageSender` doubles and callers compile and all affected Application tests pass.
- Verification/hygiene: PASS — focused, project, full-solution, corrected architecture scans and diff check are fresh and green; owned hash includes the new test.
- Ownership: PASS — only permitted product/test paths and this Task's appended amendment/result were changed.

## Independent Quick Review — IQR-001-20260812

- Verdict: `passed`
- Reviewed Task contract revision/amendments: revision 1 plus A-001; reviewed ER-001 and SC-001 against plan T-005, BD-006/BD-007, CK-006 and corrected CK-007.
- Owned diff identity: independently reproduced complete Git blob hash `59722c5f806bd580d59f8a7e7231eeb1f18090ce` from an isolated alternate index over the four declared owned paths, including the untracked focused test without changing the shared index (208 insertions).
- Commands/evidence inspected: current TASK-05; exact four-path owned diff; plan T-005, BD-006/BD-007, CK-006/CK-007 and Task amendment A-001; accepted TASK-04 ER-002/SC-002/IQR-002 output; independently rerun CK-006 (32/32 passed), full Networking (61/61 passed), full Application (199/199 passed), corrected CK-007 scans (zero prohibited matches), owned-path discovery/dependency scan (zero matches), owned-path `git diff --check` (passed), and complete owned-diff identity calculation.
- Findings: no obvious blocking defect. Required-direct frames are awaited sequentially and the first failure escapes before later direct or target sends (`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs:76-79`); focused tests freeze both overall/per-target order and this stop/propagation behavior (`Server/tests/Dfo.Server.Networking.Tests/Integration/TouchSocketNetworkMessageSenderProjectionTests.cs:9-60`). Each supplied best-effort target is isolated by breaking only its inner frame loop and continuing to the next explicit target, while an owner-equal target is deterministically skipped (`TouchSocketNetworkMessageSender.cs:81-103`); the focused failure vector covers both a mid-batch target failure and a disconnected explicit target without blocking the following target (`TouchSocketNetworkMessageSenderProjectionTests.cs:63-93`). Caller cancellation is not swallowed because an `OperationCanceledException` observed with the supplied token canceled is rethrown before the best-effort catch-all (`TouchSocketNetworkMessageSender.cs:92-100`).
- Boundary/dependency disposition: every optional recipient is carried as an explicit `NetworkConnectionId`; runtime lookup uses only the supplied identifier and contains no target enumeration, business/session discovery, reverse Application/domain call, repository, SQLite or PVF dependency (`Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs:139-153`; `TouchSocketNetworkMessageSender.cs:106-121`). The default sender seam preserves existing callers/test doubles while routing the typed delivery through the existing implementation (`Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs:13-25`). No G7 trigger or lifecycle mutation is present in the owned diff.
- Required remediation: none for this Task Unit. This quick-review pass permits TASK-05 acceptance logic only and is not approval of the complete Spec or delivery.
