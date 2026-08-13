---
kind: rks-task-contract
id: normalize-iat-gate-policy
status: ready
authorized_scope: |
  Modify ClientHack IAT gate policy so no slot is matched against an absolute
  captured process/system address; rebuild under MSVC; run CTest; deploy the
  staged distribution to an isolated DNFClient lab copy and verify it launches
  into the game window with all five required plugins initialized. Do not
  launch or mutate D:\project\DNFClient itself.
---

# TaskContract: Normalize IAT gate policy across system ASLR

## Source request

User instruction (2026-08-13): "本机是有msvc的，所以你要做的是将windows下的ASLR问题修复掉并且在DNFClient测试功能正常。"

Continues the open review finding from `rebuild-client-hack-host` (`reviews/change-review.md` item 2): the production startup-gate IAT policy compares most transient slots against absolute captured addresses, which can make a valid client fail closed after an OS reboot because system DLL bases change.

## Goal

The production startup-gate IAT policy must accept a valid supported DNF client across system ASLR relocations (different boots), while preserving the current fail-closed guarantee for genuinely wrong slot values. Concrete evidence: MSVC Release build succeeds, all CTest suites pass with relocated-base coverage, and an isolated lab copy launches into the game window with five required plugins initialized.

## Authorized scope

Permitted:
- Edit `ClientHack/src/az/iat_gate_policy.h`, `iat_repair.c`, `iat_table.h` usage, `include/clienthack/iat_state.h`, `src/core/iat_state.c`, and the CTest sources under `ClientHack/tests/` to remove absolute-address matching.
- Configure, build (MSVC, Release, Win32), and run CTest for the `ClientHack` project.
- Copy the staged distribution into one explicitly named isolated lab copy of the client and launch it there; read its Host log.
- Create task-local artifacts under `.rks-agent/tasks/normalize-iat-gate-policy/`.

Forbidden:
- Launch or mutate `D:\project\DNFClient`. All runtime work happens in an isolated lab copy.
- Modify, merge, push, or rewrite history in any remote. Local task commits only, on authorization.
- Change the supported executable hash gate, the unpacked-readiness boundary, the required-plugin set, or the capture-before-mutation ordering.

## Key facts discovered

- `DNF.exe` DllCharacteristics = 0x8100 (NX_COMPAT | TERMINAL_SERVER_AWARE); **DYNAMICBASE is NOT set**. DNF ImageBase is fixed at 0x00400000. So DNF-owned and DNF-bundled-DLL addresses are inherently stable; the ASLR risk applies only to OS/system DLL exports referenced through the IAT.
- `iat_gate_policy.h` currently encodes 341 "exact" absolute values. Classification by high byte: 311 are 0x07xxxxxx (msvcp60 region, DNF-bundled, stable); 30 are 0x74xxxxxx/0x77xxxxxx (kernel32/system DLLs, subject to system ASLR). Index 341 is already module-RVA normalized; index 375 is a stable msvcr71 compatibility entry; 342..405 were already equal to resolved destinations.
- `iat_table.h` provides `module_name` + `function_name` for all 406 slots, which is the identity basis for normalization.
- The prior review was explicitly accepted by the user on 2026-08-13 without remediation; this task performs the remediation now.

## Decision required by user (resolved)

- Normalization scope: **all 341 slots normalized uniformly** (drop the absolute-address exact table; accept each slot by module+export identity).
- Acceptance verification: **MSVC build + CTest pass + isolated lab launch into the game window**.

## Out of scope

- Server-side or protocol changes.
- Re-enabling the not-yet-signed candidate hooks (0x01CF9700 etc.); they remain capture-only.
- Any change to the `S4A12ClientPatch` reference repo.
