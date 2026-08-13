---
kind: rks-spec-result
spec: ./spec.md
decision: satisfied
---

# Spec result: Normalize IAT startup-gate policy across system ASLR

## Outcome

Satisfied. The production IAT startup-gate policy matches no slot against any captured absolute address. Runtime diagnosis corrected the design mid-task: gate-time slot values are arbitrary packer-fill intermediates (not enumerable, not stable across boots because bundled DLLs like msvcp60 relocate even without DYNAMICBASE), so the policy was aligned with the reference S4A12 `RepairIAT` — every resolvable slot is repaired to its current `GetProcAddress` resolution; the DNF data-export slot is retained by module base + RVA; an unresolvable non-data-export slot is rejected with rollback.

## Requirement coverage

| ID | Status | Evidence |
|---|---|---|
| RQ-001 | met | `ch_iat_policy_evaluate` takes no captured value; `iat_repair.c` repairs each slot to its current resolved export; no absolute-address comparison remains. |
| RQ-002 | met | `iat_gate_policy.h` stores only `CH_IAT_GATE_ENTRY_COUNT`, `CH_IAT_GATE_DATA_EXPORT_INDEX`, `CH_IAT_GATE_DATA_EXPORT_RVA`; the 341-entry exact table is deleted. |
| RQ-003 | met | Fail-closed preserved at the meaningful boundary: unresolvable non-data-export slot -> REJECTED with rollback; repair position constrained to the fixed `iat_table.h` list. |
| RQ-004 | met | `actual == resolved` is idempotent RESOLVED; any other prior value is NEEDS_REPAIR; data-export retained. |
| RQ-005 | met | `test_production_iat_gate_policy` uses relocated resolved destinations unlike any removed exact value and asserts every resolvable slot is repaired; unresolvable/wrong-RVA rejected. |
| RQ-006 | met | Single `ch_iat_policy_evaluate` signature used consistently in `iat_state.c/h`, `iat_repair.c`, and `test_main.c`. |
| RQ-007 | met | Clean MSVC Win32 Release build, zero warnings. |
| RQ-008 | met | CTest 6/6 passed. |
| RQ-009 | met | Lab launch reached the game window (`地下城与勇士`, ~685 MB) with IAT policy accepted and five plugins initialized in order. |
| RQ-010 | met | `D:\project\DNFClient` untouched (hash and mtime unchanged). |

## Key correction during execution

The original spec assumed gate-time forwarder stubs were enumerable and stable. Runtime diagnosis disproved this: slot 0 held `0x07184EAE` while msvcp60 loaded at `0x67CE0000` (preferred base `0x10000000` collides and relocates per boot). The spec's RQ-001/RQ-003 and Definitions were updated, with user approval, to the S4A12 unconditional-repair semantic. This stayed inside the task boundary (same acceptance boundary: normalize the IAT policy across ASLR and prove a lab launch into the game window).

## Artifacts changed

- `ClientHack/include/clienthack/iat_state.h` — new `ch_iat_policy_evaluate` signature (no captured value, no module-range array).
- `ClientHack/src/core/iat_state.c` — 5-rule-free policy: resolved!=0 -> RESOLVED/NEEDS_REPAIR; data-export RVA -> RETAIN; else REJECTED.
- `ClientHack/src/az/iat_gate_policy.h` — exact table and sentinels removed; only entry-count and data-export constants remain.
- `ClientHack/src/az/iat_repair.c` — repairs each slot to its current resolved export; no captured-value selection; fail-closed on unresolvable export.
- `ClientHack/tests/test_main.c` — policy unit cases and relocated-base production-gate case updated to the new signature/semantics.
