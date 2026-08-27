---
kind: rks-verification
decision: pass
---

# Verification: 任务日志成为自己的领域

**Claim under test.** Every success condition of the TaskContract (revision 1) is met by the tree
as it now stands: quests are their own domain reached through a per-character log, their rows have
left the character store for a store of their own under the same transaction participant, the
acceptance rules have left the protocol handler, and nothing a player can observe has changed.

Evidence below was taken against the remediated tree, after the review fixes, not carried over from
the implementation stage.

| Condition | Evidence |
|---|---|
| SC-1 — a per-character quest log answers both lists and holds no state | `QuestLogTests`, 20 cases over the real store. `TheLogSeesWhatTheUseCaseHasDecidedBeforeAnythingIsSettled` reads back a staged quest while the database still holds none; `AUseCaseTakenBackTakesItsQuestWithItAndLeavesTheRest` asks the **same log instance** across a rollback and gets the settled quest only |
| SC-2 — quest rows have left the character store; same participant | `grep -c Quests ICharacterStore.cs` → 0; `grep -c "QuestSection\|character_active_quests" SqliteCharacterStore.cs` → 0. `CharacterStoreSettlementTests.TheCharacterAndItsItemsTakePartAsOneParticipant` asserts `Items.StoreIdentity == Quests.StoreIdentity`; the commit and rollback cases change a quest and an item in one use case and settle them together |
| SC-3 — finished quests can be recorded and read back | `WhatWasFinishedAndWhatIsBeingWatchedComeBackOut` (write, settle, read) and `WhatWasFinishedAndWhatIsBeingWatchedCanBeTakenAwayAgain` (the delete side). The structure is `character_invisible_flags`, which already existed and already fed the entry projection's cleared-quest list; it had a read path and no write path, and now has both |
| SC-4 — the acceptance judgement is the domain's | `SlotCount = 30` lives in `QuestLog`; the handler contains no duplicate check, no free-slot search and no limit. Judged by `QuestLogTests`: accepted into the lowest free slot, refused as already carried, refused as full, and the lowest-free-slot rule proven by giving a quest up and watching the next one take the hole |
| SC-5 — both writers go through the log | The two are `AcceptQuestCommandHandler` and `SelectCharacterQuestEntryService`; both now take `ICharacterQuestLogDomainService` and neither names a store. `AcceptQuestCommandHandlerTests` (4 cases) and `SelectCharacterEntryLifecycleServiceTests.QuestSynchronization_*` still pass, the latter now asserting the slot and the guard rather than only the value |
| SC-6 — the read paths are reconciled | Two remain, and the conclusion is written where both can be found: `IQuestStore`'s remark names the entry projection, says why no use case is exposed to the split, and says which section the two do **not** agree on — the tracking selection's order, which nothing has ever written and for which no evidence exists either way. `ICharacterActiveQuestTable` points back at it |
| SC-7 — wire and rows unchanged | `verification/wire-and-rows-unchanged.md`. Acceptance bytes and quest rows identical to the baseline; entry batch identical in 73 of 76 frames, the other 3 differing only where a **same-build control run** differs by the same offsets — the server timestamp and three item instance ids |
| SC-8 — the suite passes | `dotnet test 86JP.Server.slnx` → **1304 passed, 2 skipped, 0 failed**; `dotnet build` → 0 warnings, 0 errors |
| SC-9 — the affected documentation is updated | `AGENTS.md` (three facets, the session shapes, the domain directory list), `Docs/ProtocolScaffoldDissonances.md` (§16 closed, §22 corrected — the cleared-quest carrier exists, what is missing is content), `protocol/game/client-to-server/0x001F.md` (server mapping and the two unevaluable checks), and the dungeon-admission trace annotated rather than rewritten |

## Scope of what the wire capture proves, and what it does not

The captures exercise entry and acceptance. They do **not** exercise the entry-time progress
correction: that fires only when a quest declaring seeking items is already carried with a progress
value the bag disagrees with, and the captured character accepts its quests after entering. RQ-016
is therefore verified by test rather than on the wire, and this is stated rather than glossed
because the acceptance criterion asks for the corrected value to be unchanged, not for a frame.

Two acceptance checks the reference server makes remain unimplemented — a cleared non-repeatable
quest, and unmet prerequisites. They were out of scope by contract, they stay out, and the reason
is recorded in `Docs/ProtocolScaffoldDissonances.md` §22: the state they need now has both a
carrier and a write path, but nothing finishes a quest yet, so the checks would run against a set
that is empty for every character.

## Verified conditions: 9 of 9

No condition is unverified. No check was skipped or unavailable.
