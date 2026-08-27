---
kind: rks-spec-result
outcome: completed
---

# Result: 任务日志成为自己的领域

Quests are their own domain. A character's quests -- what is being carried, what has been finished,
what the player asked to be told about -- are reached through one log obtained per character, and
that log decides for itself whether a quest may be taken on and where it goes. The rows left the
character store for a store of their own, which is another facet of the same database participant,
so a use case can still take a quest on and hand over what it gives as one outcome.

Nothing a player can observe changed.

## What was built

| Boundary | What is there now |
|---|---|
| `Abstractions/Quests/IQuestLog.cs` | the log, its verbs, and the two refusals it can give |
| `Abstractions/Quests/IQuestStore.cs` | the store contract and its three sections; where the two readers of these rows are reconciled |
| `Persistence/Quests/SqliteQuestStore.cs` | the rows, as another facet of the character database |
| `Persistence/Database/SqliteOwnedCharacterSection.cs` | the staged-section base, lifted out of the character store so both stores can use it |
| `Application/Quests/QuestLog.cs` | the log itself, reading through on every question, and the service that hands one out |
| `Protocol/.../AcceptQuestCommand.cs` | a handler that decodes, orchestrates and answers, and judges nothing |
| `Application/Characters/SelectCharacterPostEntryServices.cs` | the entry-time correction, now naming the slot it read |

`ICharacterStoreSession` no longer mentions quests.

## What was decided along the way

- **Finished quests already had a carrier.** `character_invisible_flags` is the cleared-quest table
  and always was -- the entry projection reads its non-zero rows as `ClearedQuestIds`. What it
  lacked was a write path. Adding a second table would have created exactly the duplicate-reader
  problem this task existed to remove, so the existing one gained the write path instead. This is
  the third reroute condition in the routing decision firing: the evidence for SC-3 is a write path
  over an existing structure, not a new structure.
- **The two readers stay two, and now say so precisely.** A first attempt claimed they never
  disagree; review showed that is false for the tracking selection, where this store answers in
  stored order and the entry projection sorts by quest number. Nothing has ever written that table,
  so nobody can yet tell which the client wants. The disagreement is recorded rather than resolved.
- **The three settled decisions carried in the plan were kept**: no snapshot of objectives or
  rewards on a carried quest, the rows moved rather than grew a shell, and the two unevaluable
  acceptance checks were not added.

## Evidence

- `reviews/change-review.md` — two independent reviewers, thirteen findings, all dispositioned.
- `verification/completion-evidence.md` — nine of nine success conditions, fresh.
- `verification/wire-and-rows-unchanged.md` — before, after, and a same-build control.

Build 0/0. Suite 1304 passed, 2 skipped, 0 failed.

## What this leaves for whoever comes next

- Quest completion has a domain verb and nowhere to be called from. When `FINISH_QUEST` lands, the
  two acceptance checks recorded in `Docs/ProtocolScaffoldDissonances.md` §22 become answerable for
  the first time, because the cleared set will finally be non-empty.
- Quest tracking has a domain verb and an unsettled question about the order the client draws it in.
