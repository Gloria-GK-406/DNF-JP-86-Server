---
kind: rks-review
verdict: changes_required
---

# Blocking Findings

1. The new production startup-gate IAT policy is not backed by retained, reproducibly compared evidence in the project. `ClientHack/src/az/iat_gate_policy.h:6-18` says its 406-slot policy came from two 2026-08-13 gate captures, but `ClientHack/runtime/captures/` contains only the earlier zero-source `capture-1.jsonl` and `capture-2.jsonl`; neither contains the 341 exact gate values, the index-341 module-relative value, or the index-375 compatibility entry used by production. `ClientHack/tools/compare-captures.ps1:48-80` and the new `clienthack_capture_evidence` CTest consequently validate only those zero-source reports, not the evidence from which the production gate policy was generated. This leaves SC-3, RQ-003, EV-001, and the plan's BD-003/CK-005 evidence boundary unsatisfied. Retain the two raw startup-gate captures under `runtime`, add a normalized comparison/generation check, and make CTest prove that `iat_gate_policy.h` matches that evidence.

2. Most startup-gate transient entries remain encoded and compared as absolute process/system addresses rather than normalized identities. `ClientHack/src/az/iat_gate_policy.h:18-61` contains absolute values such as `0x74F7D802` and `0x772145F5` for kernel imports, while `ClientHack/src/az/iat_repair.c:95-107` exact-compares them; only index 341 is normalized as module RVA. Two launches in the same Windows boot can share system DLL ASLR bases, so equality of the two external lab files does not make these addresses stable for the supported DNF build. This contradicts RQ-003 and the approved plan step T-002.2 requiring external pointers to be normalized by module/export identity, and can make a valid client fail closed after an OS reboot. Normalize every external transient value to module/export or module-RVA identity (and DNF-owned values to DNF RVA), then exercise relocated bases in the production-policy test.
