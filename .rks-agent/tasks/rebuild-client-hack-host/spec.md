---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: Captured-state completion of the pure-C client Host

## Why and Outcome

The static Host framework is safe but cannot start because the supported executable is packed and the required original runtime states exist only after unpacking. The completed result must derive those states from repeatable isolated launches, bind enabled mutations to that evidence, and demonstrate that the isolated client starts with all required plugins initialized while the source client remains unchanged.

## Task Perspective

- Core outcome: A validated Az/plugin distribution that starts the supported isolated client copy with protection, compatibility, networking, and window plugins initialized.
- Necessary support: Pre-mutation capture mode, repeatability comparison, generated runtime signatures/IAT policy, deterministic lab deployment, logs, and rollback safety.
- Peripheral assurance: Clean build, unit/integration tests, PE inspection, documentation, and source-client integrity proof.

## Scope and Non-Goals

The system may copy and launch only the isolated lab. It may terminate lab processes, replace lab DLLs, and write capture/log files there. It must not mutate or launch the source client. External authentication or game-server availability is not redefined as a client-patch requirement; the Host must nevertheless reach its own successful initialization and the furthest locally reachable client state.

## Definitions

| Term | Meaning |
|---|---|
| Clean capture launch | A lab launch restored from the same source-client baseline with diagnostic Az and no feature-plugin mutation before capture. |
| Stable runtime state | Bytes or classifications that agree across at least two clean capture launches after relocation normalization. |
| Enabled mutation | An IAT replacement, fixed write, or Hook that production Host/plugin code can execute. |
| Startup success | Hash accepted, readiness reached, required IAT policy applied, all four required plugins initialized, and no Host fatal warning/crash before the next external dependency. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The source client directory remains read-only and unlaunched; all runtime work occurs in one explicitly named isolated lab copy. |
| RQ-002 | Diagnostic Az validates the supported file hash, waits for the same unpacked readiness boundary as production, captures evidence before IAT/plugin mutation, writes a parseable report, and terminates the lab process. |
| RQ-003 | At least two clean captures are compared after normalizing process-specific addresses; inconsistent required state prevents production enablement. |
| RQ-004 | Every production IAT repair accepts an evidence-backed source classification or the already-resolved destination and supports rollback on partial failure. |
| RQ-005 | Every enabled fixed patch declares captured expected bytes plus replacement bytes; every enabled Hook declares a captured entry signature before MinHook runs. |
| RQ-006 | `0010_DisableGameGuard`, `0020_ClientCompatibility`, `0100_NetworkHook`, and `0900_PreventMinimize` are Host-required, initialize in numeric order, and contain no unconditional fixed-address write. |
| RQ-007 | Protection and network plugins implement their intended behavior for the captured supported build rather than unconditional placeholder failure. |
| RQ-008 | Production Host records a durable lab log covering hash, readiness, IAT outcome, candidate order, descriptor validation, plugin initialization, and fatal reason. |
| RQ-009 | A fresh lab deployment starts without a Host compatibility warning or Host-caused crash and reaches the furthest locally available startup state. |
| RQ-010 | Automated tests cover captured-state parsing/comparison, IAT policy, patch/Hook validation, transaction rollback, required-plugin behavior, and capture-before-mutation gating. |
| RQ-011 | Release staging remains root `Az.dll` plus passive `ijl15.dll` and four plugins below `plugins`, all PE32 x86 C artifacts. |
| RQ-012 | Maintainer documentation records the lab path, capture provenance, supported signatures/policies, build/deploy flow, runtime result, and remaining external limitations. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | A fresh isolated copy and diagnostic Az | The lab DNF is launched | A pre-mutation capture report is written and the lab process terminates without loading feature plugins. |
| AC-002 | RQ-003 | Two clean capture reports | They are compared | Required entry bytes and normalized IAT states agree or production enablement stops with a concrete mismatch. |
| AC-003 | RQ-004, RQ-005 | Captured original, already-applied, or unrelated runtime state | Production mutation is requested | Original is safely changed, already-applied is idempotent, and unrelated state is rejected and rolled back. |
| AC-004 | RQ-006, RQ-007, RQ-008 | Production artifacts deployed to a fresh lab | DNF reaches readiness | The log shows the four exact required plugins initialized in numeric order with no placeholder failure. |
| AC-005 | RQ-009 | A fresh production lab deployment | DNF is launched under bounded monitoring | No Host fatal warning/crash occurs and the process reaches the next locally available startup state. |
| AC-006 | RQ-010, RQ-011 | A clean Release build | Tests and PE inspection run | All automated suites pass and the staged tree contains only the intended x86 artifacts. |
| AC-007 | RQ-001, RQ-012 | Runtime work is complete | Source and documentation are inspected | Source hashes are unchanged and the evidence/limitations are reproducible from documentation. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-003 | AC-001, AC-002 | Lab inventory, capture reports, comparison output, and source hashes. |
| EV-002 | RQ-004, RQ-005, RQ-010 | AC-003, AC-006 | Generated policy/signature data plus automated mutation and rollback tests. |
| EV-003 | RQ-006, RQ-007, RQ-008, RQ-009 | AC-004, AC-005 | Production Host log and bounded process/window observations from a fresh lab launch. |
| EV-004 | RQ-011, RQ-012 | AC-006, AC-007 | Clean Release build, PE/stage inspection, documentation review, and integrity comparison. |

## Assumptions

- The unpacker reaches the known readiness signature before a working external game server is required.
- Process-specific system DLL addresses can be normalized by module/export identity; DNF-relative addresses can be normalized by module RVA.
- When runtime evidence disproves a reference patch or signature, captured behavior is authoritative.
