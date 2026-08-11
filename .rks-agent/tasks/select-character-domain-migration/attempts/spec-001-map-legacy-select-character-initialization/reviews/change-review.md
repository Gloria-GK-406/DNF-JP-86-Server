---
kind: rks-review
verdict: approved
---

# Complete-result review: spec-001 migration matrix

The deliverable satisfies RQ-001 through RQ-005 and AC-001 through AC-005. No blocking findings remain.

Mechanical comparison confirms that the ordered table contains exactly 58 rows matching the 58 `Raw(...)`/`Item(...)` additions in `NewCharacterInitSequence.Build()`, including the five item-list variants and every repeated command/type occurrence (`Server/DfoServer/Game/SelectCharacter/NewCharacterInitSequence.cs:13-91`; `Docs/Refactor/SelectCharacterMigrationMatrix.md:23-90`). Each static row supplies wire identity, condition/meaning, source, owner and target, effect, confidence, and successor group.

The supplemental surface now covers the directly invoked conditional and trailing behavior from packet construction, the select-character handler, and immediate lifecycle continuation (`Docs/Refactor/SelectCharacterMigrationMatrix.md:92-117`). In particular, X-17 is source-fidelitous: `CloseSessionAsync` emits NOTI `00/021C` only after closing an owned open expert store; `includeOwner=true` sends to the owner and other eligible area sessions, whereas `includeOwner=false` sends only to the other area sessions (`Server/DfoServer/Network/Handlers/ExpertJobStoreHandler.cs:763-823,842-847`; `Server/DfoServer/Network/Core/PacketTypes.cs:546-547`). X-18 is likewise exact: `NotifyLeaveAsync` emits NOTI `00/0006` only when a session directory and positive selected character exist, with zero-or-more eligible area recipients (`Server/DfoServer/Network/Handlers/TownHandler.cs:339-347`).

L-01, L-05, and L-10 correctly reference X-17/X-18 and preserve their distinct invocation semantics: normal previous-character cleanup uses `includeOwner=true`; displaced and rollback expert-store cleanup use `includeOwner=false`; rollback town leave occurs only after exact-generation unregister succeeds and the player still identifies the character (`Server/DfoServer/Network/Handlers/CharacterSessionLifecycleCoordinator.cs:538-588,592-678,681-764`; `Docs/Refactor/SelectCharacterMigrationMatrix.md:125-136`). The remaining lifecycle rows account for preparation, registration, PvP handshake/publication, trailing lock refresh, quest synchronization, pet revival/state adjacency, and dungeon-rejoin offer/participant projection.

The matrix keeps uncertain and conflicting legacy semantics explicit, distinguishes applicable-empty from unavailable or inapplicable data, and separates reads, preparation mutations, and lifecycle effects (`Docs/Refactor/SelectCharacterMigrationMatrix.md:3-21,138-158`). G1-G7 cover every static, supplemental, and lifecycle row with dependency order and separated character, account, world/content, session, and Networking responsibilities; X-17/X-18 are specifically covered by G3/G5/G6/G7 as applicable (`Docs/Refactor/SelectCharacterMigrationMatrix.md:160-174`).
