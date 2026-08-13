---
kind: rks-verification
decision: accepted
---

# Completion verification

## Claim

The ClientHack IAT startup-gate policy no longer matches any slot against a captured absolute address. Every resolvable slot is repaired to its current `GetProcAddress` resolution (idempotent if already equal); the DNF data-export slot is retained by module base + RVA; an unresolvable non-data-export slot is rejected with rollback. A clean MSVC Win32 Release build succeeds, all six CTest suites pass including a relocated-base case, and an isolated lab copy launches into the game window with all five required plugins initialized.

## Verified conditions

| Condition | Fresh evidence |
|---|---|
| Release build | Clean MSVC (v18.4.3, toolset 14.50) Win32 Release build of Az.dll, ijl15.dll, all five plugin DLLs, and both test EXEs completed with zero compiler errors and zero warnings. |
| Automated behavior | `ctest -C Release` passed 6/6: capture_evidence, clienthack_tests (incl. updated `test_iat_policy_and_transaction` and relocated-base `test_production_iat_gate_policy`), endpoint_redirect_tests, host_tests, loader_tests, rollback_failure_tests. |
| IAT policy source | `g_ch_iat_gate_exact[]` and the exact/extra-exact sentinel machinery removed from `iat_gate_policy.h`; `ch_iat_policy_evaluate` no longer takes or compares against any captured value; `iat_repair.c` repairs every slot to its current resolved export. |
| Relocated-base coverage | `test_production_iat_gate_policy` uses resolved destinations (`0x6F120000u+`) unlike any value in the removed exact table and asserts every resolvable slot is repaired to the current resolved regardless of the arbitrary pack-time value, plus wrong-data-export-RVA and unresolvable rejections. |
| Runtime startup | Isolated lab `DNFClient-ClientHackLab-20260812` launched with the fresh stage. Host log: `IAT policy accepted: 342 repaired, 63 already resolved, 1 retained`; all five plugins initialized in numeric order (0010, 0020, 0100, 0110_ServerRedirect [disabled by config], 0900); `client thread released after startup initialization`. |
| Game window reached | `tasklist /V` reported the process Running with window title `地下城与勇士`, working set ~685 MB, well past the former R6002 interval. |
| Source boundary | `D:\project\DNFClient\DNF.exe` SHA-256 remains `F78F0AEA...C04C`; mtime unchanged (2024-12-01). Lab work occurred only in `DNFClient-ClientHackLab-20260812`. |

## Diagnosis that corrected the design

First lab launch rejected slot 0. Diagnostic logging showed gate-time slot 0 held `0x07184EAE` (a stale msvcp60 stub address from a prior boot) while this boot loaded msvcp60 at `0x67CE0000`, and the msvcp60 preferred base (`0x10000000`, no DYNAMICBASE) still collides and relocates differently each boot. The forwarder-stub values are therefore neither enumerable nor stable across boots. Cross-checked against the reference `S4A12/Az/az.cpp::RepairIAT`, which unconditionally writes each slot's `GetProcAddress` result and never inspects the prior value. The user approved matching that semantic; the spec's RQ-001/RQ-003 were updated to reflect it. The arbitrary pack-time value is not a security boundary; fail-closed is preserved at the resolvable-position + GetProcAddress-must-succeed boundary.

## Unverified conditions

None material. The prior open review finding (external transient IAT pointers normalized across system ASLR/reboot) is resolved: no production slot stores or compares against any captured absolute address, and the relocated-base CTest plus the successful lab launch on a boot whose msvcp60/kernel32 bases differ from the capture boot demonstrate the fix.
