---
kind: rks-verification
decision: manual_required
---

# Completion verification

## Claim

The branch contains a buildable Windows x86 pure-C ClientHack distribution with one active `Az.dll`, one passive `ijl15.dll`, a shared plugin ABI entry, four ordered required plugins, fail-closed identity/state checks, and a corrected startup sequence that initializes required plugins before releasing the game thread.

## Verified conditions

| Condition | Fresh evidence |
|---|---|
| Release build | Clean configure, clean target, and complete Release build succeeded without compiler errors. |
| Automated behavior | CTest passed 5/5, including capture consistency/order, IAT mixed/already-resolved policy, partial repair rollback, Host lifecycle, loader rollback, and required plugin metadata. |
| Stage shape | Stage contains PE32 x86 `Az.dll`, passive `ijl15.dll`, and four plugin DLLs; no staged dependency on `86JP.dll` or `GameGaurd.dll`. |
| Plugin architecture | `plugins/plugin_entry.c` is the only bundled `ClientHackPlugin_Query` implementation; numbered plugin directories contain their logic implementations. |
| Runtime startup | The six staged DLLs matched the isolated lab byte-for-byte. Host log recorded IAT policy acceptance (`342 repaired, 63 already resolved, 1 retained`), all four plugins initialized in numeric order, and client-thread release afterward. The client reached the game window and passed the former R6002 interval. |
| Source boundary | `D:\project\DNFClient` was not launched or modified; final hashes remain `F78F...C04C` (DNF.exe), `8B71...E6A` (Az.dll), and `11C6...14F4` (ijl15.dll). |

## Unverified Conditions

- The independent reviewer requires the two raw production startup-gate captures to be retained in the repository and external transient IAT pointers to be normalized across system ASLR/reboot. Those conditions remain open.
- The user explicitly accepted these review findings on 2026-08-13 and authorized merge/push without remediation.
