---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Normalize the IAT startup-gate policy across system ASLR

## Why and Outcome

The current production IAT policy (`iat_gate_policy.h`) compares 341 transient slots against absolute captured addresses. 30 of those reference kernel32/system DLL exports whose bases change across OS reboots due to system ASLR, so a valid client can fail closed after a reboot even though nothing is wrong. The completed result must accept the supported client purely by module/export identity and DNF-module RVA, must reject genuinely wrong slot values, and must launch the supported isolated client copy into the game window with all five required plugins initialized.

## Task Perspective

- Core outcome: A Host whose IAT gate never depends on a captured absolute process/system address, verified by a relocated-base test and by an isolated lab launch into the game window.
- Necessary support: Updated `ch_iat_policy_evaluate` semantics, removal of the absolute exact-address table, idempotent handling of initial/partial/fully-resolved slot states, transaction rollback preserved, and CTest coverage that exercises relocated module bases.
- Peripheral assurance: Clean MSVC Release build, unchanged stage shape, unchanged source-client directory, and a maintained capture/generation audit trail.

## Scope and Non-Goals

The system may edit the IAT policy implementation, its callers, the policy data, and the CTest suites. It may build under MSVC, run CTest, deploy the staged tree to one explicitly named isolated client copy, launch it, and read its log. It must not launch or mutate `D:\project\DNFClient`, change the supported hash/readiness/plugin set, or enable any unsigned hook. Re-enabling capture-only candidate hooks is out of scope.

## Definitions

| Term | Meaning |
|---|---|
| Normalized identity | A slot is repaired to the current `GetProcAddress(module, function)` resolution for that slot's `iat_table.h` identity, without comparing the slot's prior value against any captured absolute address. |
| Pack-time intermediate value | Runtime diagnosis proved that at the production startup gate an IAT slot can hold an arbitrary packer-fill intermediate value (observed: slot 0 held `0x07184EAE`, a stale msvcp60 stub address from a prior boot, while this boot loaded msvcp60 at `0x67CE0000`). These values are not enumerable and not stable across boots; they are therefore not a security boundary. |
| Unconditional repair | Matches the reference implementation S4A12 `RepairIAT`: for every slot whose `resolved` export is non-zero, write `resolved` into the slot (idempotent if already equal). The slot's prior value is not matched against any captured state. |
| Fail-closed boundary | Repair position must be one of the fixed `iat_table.h` slot RVAs (ensured structurally), and `GetProcAddress(module, function)` must succeed. A slot whose export cannot be resolved is the only rejection case (except the DNF data-export slot, retained by module RVA). |
| Startup success | Hash accepted, readiness reached, normalized IAT policy applied, all five required plugins initialized in numeric order, and no Host fatal warning/crash before the next external dependency. |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | No production IAT slot is matched against an absolute captured process or system address. Every slot whose export resolves is repaired to the current resolved destination (idempotent if already equal); the DNF data-export slot is retained by module base + RVA. The slot's prior pack-time value is never compared to a captured constant. |
| RQ-002 | `iat_gate_policy.h` no longer stores the 341-entry absolute-address exact table (`g_ch_iat_gate_exact`) or the special exact sentinel logic keyed to absolute addresses. Only the entry count and the data-export index/RVA constants remain. |
| RQ-003 | Fail-closed is preserved at the meaningful boundary: a slot whose `GetProcAddress` resolution fails (returns NULL/0) and is not the declared DNF data-export slot is REJECTED and aborts repair with rollback. The repair position is constrained to the fixed `iat_table.h` slot list, so no out-of-table address can be mutated. |
| RQ-004 | The three legitimate transient states continue to pass idempotently: initial (captured-zero / unresolved data-export), partially-resolved, and fully-resolved. |
| RQ-005 | CTest adds a relocated-base case: when the resolved destination differs from any value that ever appeared in the removed exact table, every previously-exact slot is still accepted (repaired to the current resolved). The policy still rejects a slot whose export cannot be resolved. This must run on Windows MSVC. |
| RQ-006 | `ch_iat_policy_evaluate` signature/semantics changes are reflected consistently across `iat_repair.c`, `iat_state.c`, `iat_state.h`, and every CTest caller; no caller retains the old absolute-compare assumption. |
| RQ-007 | MSVC Win32 Release build completes without compiler errors or warnings introduced by this change. |
| RQ-008 | All CTest suites pass, including capture consistency/order, IAT mixed/already-resolved/rollback, Host lifecycle, loader rollback, required-plugin metadata (now five), endpoint-redirect, and the new relocated-base IAT case. |
| RQ-009 | An isolated lab copy of the client, deployed from the fresh stage, launches and reaches the game window; the Host log records IAT policy acceptance and the five required plugins initialized in numeric order, with no fatal warning/crash. |
| RQ-010 | `D:\project\DNFClient` remains unlaunched and byte-unchanged; the staged tree contains only the intended PE32 x86 artifacts (Az.dll, passive ijl15.dll, ClientHack.ini, five plugin DLLs). |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | The policy source | Inspected | No slot stores or compares against an absolute captured process/system address; identity and module-RVA are the only acceptance bases. |
| AC-002 | RQ-003, RQ-004 | A slot whose export resolves to a non-zero address, a slot already equal to resolved, and a slot whose export cannot be resolved | Evaluated by the policy | Resolvable slots are repaired to the current resolved (idempotent if equal); the data-export slot is retained; an unresolvable non-data-export slot is REJECTED with rollback. |
| AC-003 | RQ-005, RQ-008 | A clean MSVC Release build | CTest runs including the relocated-base case | All suites pass and the relocated-base case proves identity acceptance. |
| AC-004 | RQ-007, RQ-010 | The staged tree and source client | Build and integrity inspection | Clean Release build; staged tree contains only intended artifacts; source client hashes unchanged. |
| AC-005 | RQ-009 | A fresh isolated lab deployment | DNF is launched under bounded monitoring | No Host fatal warning/crash; the log shows IAT policy accepted and five plugins initialized in order; the process reaches the game window. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002, RQ-006 | AC-001 | Source diff of policy/data/callers showing identity-only acceptance and removal of the absolute exact table. |
| EV-002 | RQ-003, RQ-004, RQ-005, RQ-008 | AC-002, AC-003 | CTest output (passing) including the new relocated-base case and the existing rollback/mixed cases. |
| EV-003 | RQ-007, RQ-010 | AC-004 | Clean build log and stage/source integrity inspection. |
| EV-004 | RQ-009 | AC-005 | Host log from an isolated lab launch and bounded window/process observation. |

## Assumptions

- `iat_table.h` module_name/function_name identities are correct and complete for all 406 slots (verified: 406 entries).
- System DLL exports resolve to a single current address per boot via `GetProcAddress`, so identity comparison is well-defined.
- The supported executable reaches the unpacked readiness boundary without change after the policy edit.
- DNF.exe and its bundled DLLs (msvcp60, msvcr71) remain without DYNAMICBASE, so DNF-owned addresses stay stable and identity/RVA acceptance is equivalent to the prior absolute match for them.
