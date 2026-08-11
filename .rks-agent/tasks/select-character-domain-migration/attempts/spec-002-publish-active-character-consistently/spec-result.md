---
kind: rks-spec-result
state: completed
---

# Spec Result: Publish Active Character Consistently

Implemented one `IActiveCharacterService` boundary keyed by `AccountId`. `UserService` now owns both the online aggregate's current CharacterId and the corresponding `ActiveCharacterState`, publishing them under one synchronization boundary and recording the owning `OnlineUserId` for safe disconnect cleanup.

`CharacterEntryService` loads the exact account slot before publication, so load failure preserves prior accepted state. `CharacterService` now contains only repository-backed roster and exact-slot loading behavior. Dependency injection maps `IActiveCharacterService` to the same singleton `UserService` used for online-user state.

Stable behavior coverage proves initial and replacement publication, failed-selection preservation, stale-disconnect preservation, and accepted-owner cleanup. Independent review approved the complete change, and fresh verification passed 73/73 solution tests plus structural and diff checks.

This result advances root SC-4 by completing consistent AccountId-indexed selection publication and contributes the online active-state boundary needed by SC-3. Later domain data services and Networking projection remain outside this Spec.
