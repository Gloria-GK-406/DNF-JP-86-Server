---
kind: rks-spec-result
state: completed
---

# Spec Result: Load Account Entry State

Implemented complete G4 AccountId-owned entry state across normalized SQLite persistence, raw repository contracts, four cohesive Application slices and selection composition.

The result separates settings, wallet/entitlements, progression/events and account relations/neutral compatibility. It preserves signed premium identifiers such as Devil Contract 580-587, models only the one account-owned hotkey prefix, reuses the accepted `AccountInventoryEntryState` without duplicating cargo or cube resources, and keeps CharacterId only in explicit roster/relation views.

Unknown730 and the PC-room-like scalar are promoted from character storage only when all active roster values agree; conflicts fail rather than becoming fabricated account defaults. Missing/malformed/wrong-owner data fails before active publication.

Independent re-review approved the remediated change. Fresh verification passed 15/15 focused tests, 118/118 full tests, exact G4 coverage and dependency/diff checks.

G5 world/content/session-adjacent services, G6 Networking projection and G7 integrated mutation/lifecycle remain.
