---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: Runtime capture and complete plugin enablement

## Implementation Approach

First create and hash an isolated lab copy. Add a diagnostic-only Az build that stops at the production readiness boundary, records all required pre-mutation bytes plus current/resolved IAT state, and terminates. Run it twice from reset lab state, normalize and compare reports, then generate C policy data. Enable only evidence-backed mutations, add durable Host logging, redeploy production artifacts into a reset lab, and monitor bounded launches until Host initialization and the locally reachable startup state are proven.

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `D:\project\DNFClient-ClientHackLab-20260812` | Isolated disposable runtime lab; never the source client. | Read-only source client | Resettable capture/production launch environment |
| BD-002 | `ClientHack/src/capture/`, CMake capture target | Capture hash/readiness/runtime state without feature mutation. | Supported address map and IAT table | Diagnostic Az and parseable reports |
| BD-003 | `ClientHack/tools/`, `ClientHack/runtime/` | Normalize two reports and generate reviewed C signature/IAT policy data. | Two clean captures | Stable evidence, comparison, generated data |
| BD-004 | `ClientHack/src/az/`, `src/host/` | Apply evidence-backed IAT policy and durable startup logging. | BD-003 | Production Host capable of safe startup |
| BD-005 | `ClientHack/plugins/` | Replace protection/network placeholders and complete captured fixed patches/Hooks. | BD-003 and Host API | Four functioning required plugins |
| BD-006 | `ClientHack/tests/` | Extend regression coverage for capture, policy, mutations, ordering, and rollback failure. | BD-002 through BD-005 | Automated evidence |
| BD-007 | `ClientHack/README.md`, `ClientHack/docs/` | Record capture, deployment, verified boundary, and limitations. | Runtime results | Reproducible maintainer guidance |

## Dependencies

| ID | Dependency | Required by | Evidence |
|---|---|---|---|
| DP-001 | Isolated DNF reaches unpacked readiness with diagnostic Az | T-002, T-003, T-004 | Capture report or demonstrated blocker evidence |
| DP-002 | Two normalized capture reports agree for required state | T-003, T-004 | Comparison output |

## Ordered Tasks

### T-001: Prepare the isolated lab and diagnostic capture path

- Covers: RQ-001, RQ-002, RQ-008
- Boundaries: BD-001, BD-002, BD-004
- Outputs: Hashed lab copy, capture-only Az target, bounded launcher/monitor, durable capture and Host logs

Steps:
1. Record source hashes and copy the source client to the exact lab directory without altering the source.
2. Add a diagnostic Az mode that validates hash, waits for readiness, captures configured entries/IAT slots before production mutation, flushes the report, and terminates.
3. Add file logging and a bounded lab launch/termination harness with process identity checks.

Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-001 | Non-command: source/lab inventory and hashes | Lab initially matches source and source remains unchanged. |
| CK-002 | `cmake --build --preset win32-release --target AzCapture` | Diagnostic x86 DLL builds. |
| CK-003 | Non-command: first lab capture launch | Report is written after readiness and before plugin loading. |

### T-002: Capture twice and derive stable runtime policy

- Depends on: T-001, DP-001
- Covers: RQ-002, RQ-003, RQ-004, RQ-005
- Boundaries: BD-001, BD-002, BD-003
- Outputs: Two clean reports, normalized comparison, generated signature/IAT policy data

Steps:
1. Reset only lab DLL/config/capture state and perform two clean diagnostic launches.
2. Normalize DNF pointers as RVAs and external pointers as module/export identities.
3. Reject inconsistent required bytes; generate checked C data only from stable evidence.

Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-004 | Non-command: two capture files | Each contains every configured entry and all 406 IAT slots. |
| CK-005 | Capture comparison tool | Required entry bytes match and every IAT slot receives a stable supported classification. |

### T-003: Enable evidence-backed Host and plugin behavior

- Depends on: T-002, DP-002
- Covers: RQ-004, RQ-005, RQ-006, RQ-007, RQ-008, RQ-010
- Boundaries: BD-003, BD-004, BD-005, BD-006
- Outputs: Validated IAT repair, functioning protection/network plugins, complete tests

Steps:
1. Implement atomic IAT repair from captured classifications with already-resolved idempotence and rollback.
2. Convert three protection targets to validated patches or signed Hooks as evidence dictates.
3. Restore cipher passthrough, socket-write staging, and any required captured direct patches with exact state validation.
4. Extend production and model tests for generated policies, partial failure, ordering, and required initialization.

Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `ctest --preset win32-release --output-on-failure` | All core, Host, loader, capture, and policy tests pass. |
| CK-007 | Non-command: mutation manifest review | Every enabled mutation maps to captured expected state and Host ownership. |

### T-004: Deploy and verify production startup in a reset lab

- Depends on: T-003
- Covers: RQ-001, RQ-006, RQ-007, RQ-008, RQ-009, RQ-011
- Boundaries: BD-001, BD-004, BD-005, BD-006
- Outputs: Fresh production lab, successful ordered initialization log, bounded startup evidence

Steps:
1. Reset lab root DLLs/plugins from the source baseline and deploy Release production artifacts.
2. Launch under bounded monitoring, preserve Host log, process exit, and visible-window state.
3. Diagnose any failure from the earliest log/state divergence, fix one demonstrated cause at a time, and repeat from a reset lab until success.

Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-008 | Non-command: production Host log | Hash/readiness/IAT pass and all four exact plugins initialize in numeric order. |
| CK-009 | Non-command: bounded lab observation | No Host fatal warning/crash; client reaches its next locally available startup state. |
| CK-010 | Non-command: source-client hashes | Source client matches the pre-task baseline. |

### T-005: Final build, evidence, documentation, review, and verification

- Depends on: T-004
- Covers: RQ-010, RQ-011, RQ-012
- Boundaries: BD-006, BD-007
- Outputs: Clean Release evidence, runtime documentation, independently reviewable complete change

Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-011 | Clean Release build, CTest, and `dumpbin` inspection | Tests pass; stage contains only intended PE32 artifacts. |
| CK-012 | Non-command: complete evidence/documentation inspection | Capture provenance, runtime result, limitations, and source integrity are explicit. |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-004 | BD-001 | CK-001, CK-010 |
| RQ-002 | T-001, T-002 | BD-002 | CK-003, CK-004 |
| RQ-003 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-004 | T-002, T-003 | BD-003, BD-004 | CK-005, CK-006, CK-007 |
| RQ-005 | T-002, T-003 | BD-003, BD-005 | CK-005, CK-007 |
| RQ-006 | T-003, T-004 | BD-004, BD-005 | CK-006, CK-008 |
| RQ-007 | T-003, T-004 | BD-005 | CK-007, CK-008 |
| RQ-008 | T-001, T-003, T-004 | BD-002, BD-004 | CK-003, CK-008 |
| RQ-009 | T-004 | BD-001, BD-004 | CK-009 |
| RQ-010 | T-003, T-005 | BD-006 | CK-006, CK-011 |
| RQ-011 | T-004, T-005 | BD-001, BD-006 | CK-011 |
| RQ-012 | T-005 | BD-007 | CK-012 |

## Risks and Rollback

- If the diagnostic process hangs, terminate only the verified lab DNF process and retain logs.
- Before replacing any lab DLL, resolve and verify the target path remains under the exact lab root.
- Reset the lab by recopying only from the read-only source; never reverse-copy lab content.
- If two captures disagree, do not enable the affected mutation; diagnose timing or prior mutation first.
