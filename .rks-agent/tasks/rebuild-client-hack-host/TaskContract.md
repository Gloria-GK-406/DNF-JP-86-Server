---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: Complete the pure-C client Hack runtime

## Context

### Source Request

“可以，你来通过启动复制副本来采集最后所需的东西。直到完成插件功能完全实现为止。”

### Current Situation

The first pure-C Host/stub implementation is buildable and statically verified, but it deliberately fails closed because the packed executable contains no raw code for the required runtime Hook entries and does not reveal acceptable unpacked IAT source states. The source client still has overlapping custom IJL and Az/GameGuard components. The user has now authorized copying that client into an isolated lab, launching only the copy, collecting the missing runtime evidence, and continuing until the plugin functionality and startup are verified.

## Goal

Deliver a buildable, testable, pure-C client patch project that uses `Az.dll` as the only active root Host, safely completes the migrated compatibility, protection-bypass, networking, and window plugins from captured evidence, and successfully starts the supported DNF client from an isolated copy without modifying or launching the source client directory.

## Scope

### In

- Preserve the existing independent `ClientHack` Win32 C build, passive IJL stub, versioned plugin ABI, hash gate, and ordered plugin staging.
- Create an isolated copy of `D:\project\DNFClient` outside the source client directory and use it for all deployment, runtime capture, and launch verification.
- Capture unpacked Hook-entry bytes, IAT slot states, and missing fixed-address originals before feature plugins mutate them; retain reproducible evidence in the project.
- Implement validated IAT repair and enable protection/network behavior only for the captured supported state.
- Reimplement separately staged pure-C plugins for GameGuard/protection bypass, five legacy compatibility changes, plaintext/network behavior, and prevention of minimizing other windows.
- Launch the isolated copy as many times as needed to verify Host startup, required-plugin ordering, patch/Hook activation, rollback behavior, and observable progress into the client startup flow.
- Retain tests and documentation for the supported identity, captured provenance, deployment, and remaining external-service limitations.

### Out

- Do not overwrite, delete, deploy into, or launch anything from the source `D:\project\DNFClient`; it remains a read-only copy source and hash baseline.
- Do not obtain or redistribute an original Intel IJL binary.
- Do not modify server or network-protocol behavior in this task.
- Do not add hot reload/unload while Hooks may be active.

## Constraints

- Runtime implementation and bundled plugins must remain C and target Windows x86.
- `Az.dll` is the sole active root Host; `ijl15.dll` remains compatibility-only.
- No feature plugin may initialize before the supported on-disk executable hash is accepted.
- Every enabled fixed-address write and Hook must validate captured expected state or an explicitly supported already-applied state.
- Runtime capture must occur before feature mutation and be repeatable across at least two clean isolated launches.
- Build output and lab deployment must remain outside the source client directory.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | The independent project builds as Win32 C and stages root `Az.dll`, passive `ijl15.dll`, and ordered plugins. | Clean Release build, test output, staged inventory, and PE inspection. |
| SC-2 | The supported DNF hash is accepted before plugin loading; mismatch warns and terminates. | Automated gate tests and production-path inspection. |
| SC-3 | Two clean lab launches capture consistent unpacked entry bytes, IAT classifications, and required patch originals before mutation. | Capture files and comparison report. |
| SC-4 | Every enabled IAT repair, patch, and Hook accepts only captured original/already-applied state and remains rollback-owned by the Host. | Generated signature data, source review, and lifecycle tests. |
| SC-5 | The four required bundled plugins are present, ordered, identity-checked, and successfully initialize for the captured supported state. | Lab Host log and automated required-plugin tests. |
| SC-6 | The isolated client starts without a Host compatibility warning or crash and reaches the furthest locally available startup state. | Fresh lab process/log/window evidence with timeout and exit state; external login/server limits are recorded separately. |
| SC-7 | The source client remains unchanged. | Before/after hashes for source `DNF.exe`, `Az.dll`, and `ijl15.dll`. |
| SC-8 | Documentation explains the capture, supported runtime signatures, build/deploy flow, and verified runtime boundary. | Documentation inspection. |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | User authorized: “可以，你来通过启动复制副本来采集最后所需的东西。直到完成插件功能完全实现为止。” | Copy the existing client into an isolated lab, launch and instrument only that copy, capture required runtime evidence, implement remaining pure-C plugin behavior, and verify startup to the locally reachable state while leaving the source client untouched. |

## Requires Confirmation

- Any replacement, deletion, launch, or deployment inside the source `D:\project\DNFClient` requires a separate explicit instruction.

## Assumptions

- The supported executable remains SHA-256 `F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C` with preferred image base `0x00400000`.
- The isolated client can reach the unpacked startup point without a working external game server; progress beyond that point may depend on external services.
- Runtime evidence, not the unlicensed reference implementation, is authoritative for expected bytes and accepted IAT source states.
