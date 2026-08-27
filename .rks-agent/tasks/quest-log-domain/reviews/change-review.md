---
kind: rks-change-review
decision: approved
---

# Change Review: 任务日志成为自己的领域

Two independent reviewers read the complete change against `589adf34`, split by subject rather than
by file count: one took the new domain and its persistence, the other took the call sites, the
removals and the documentation. Both built the tree and ran the full suite themselves.

## What both reviewers found clean

- **The migrated SQL is a move, not a rewrite.** `CarriedQuestSection` was diffed line by line
  against the baseline `QuestSection`: predicates, the `ON CONFLICT(character_id, slot)` clause, the
  `!= 1` row-count guard and the exception text are unchanged, and `SqliteOwnedCharacterSection`
  differs from the baseline `SqliteCharacterSection` only in its name.
- **One participant, not two.** All three stores build `StoreIdentity` from the same path through
  the same two calls; the facet key is unique; the ownership guard was carried over whole and now
  covers three sections.
- **Nothing still reaches the old partition**, and nothing that should have moved was left behind.
- **The wire refusal codes are unchanged**, checked value by value against the baseline.
- **The documentation's factual claims hold** -- that `character_invisible_flags` is the cleared
  carrier, that all three tables now have write paths, that the acceptance rules left the handler.

## Findings and what was done about them

| # | Finding | Verdict | Disposition |
|---|---|---|---|
| 1 | Entry-time correction located the row by quest number, not by the slot the snapshot named; two slots holding one quest would correct the wrong one, or fail entry | CONFIRMED, unreachable today | **Fixed.** `RecordProgressAsync` now takes the quest as last seen -- slot, number and progress -- and writes that slot. Covered by a test that seeds the two-slot state directly |
| 2 | `CompleteAsync(q, 0)` returned true while leaving the quest in neither list -- zero is how every reader spells "never finished" | CONFIRMED | **Fixed.** A zero mark is refused; test added |
| 3 | The rollback test passed by construction: the count was already zero before the rollback, and the read used a freshly built log | CONFIRMED | **Fixed.** One settled quest and one unsettled, both asked of the same log instance across the rollback |
| 4 | The delete statements of the cleared and tracked sections were never executed by any test | CONFIRMED | **Fixed.** A store-level test empties both; the shortened-selection test now settles first so the delete really runs |
| 5 | A cleared quest past the entry projection's flag array would be accepted by the store and make the character unenterable | PLAUSIBLE, latent | **Fixed.** Refused while staging, for the reason a creature's condition is |
| 6 | `TrackAsync` computed the stale tail from a row count, which only finds it while slots run 0,1,2 with no gaps | PLAUSIBLE | **Fixed.** The old slots are read and cleared by slot |
| 7 | Accepting a quest and then recording guarded progress on it inside one use case must fail, and one object now offers both verbs | PLAUSIBLE | **Documented** on `RecordProgressAsync`. The combination is contradictory -- nobody has "last seen" a row that does not exist yet -- so the fix is to say so, not to invent a rule |
| 8 | The claim written into `IQuestStore` that the two readers never disagree is false for the tracking selection: this store answers in stored order, the entry projection sorts by quest number | CONFIRMED | **Corrected.** The remark now says which sections agree, which does not, and that there is no evidence yet which order the client wants -- nothing has ever written that table |
| 9 | `AGENTS.md`'s domain-directory list did not gain `Quests/` | CONFIRMED | **Fixed** |
| 10 | The one genuinely new branch -- a quest taken on and then handed back when its items will not fit -- had no test anywhere | CONFIRMED | **Fixed.** Four handler tests over a real store and a real container; the hand-back case asserts no quest row and no item survive |
| 11 | The explicit "this command must be transactional" gate was lost on the refusal paths, which return before touching a store | PLAUSIBLE | **Fixed** and made stronger: the handler now checks `context.ExecutionKind` on the way in, so a mis-registration fails every request rather than only the ones that reach a write |
| 12 | `RecordingQuestLogs` re-implemented accept and complete, both wrongly, and neither was used | CONFIRMED | **Fixed.** The unused verbs refuse rather than answer, so nobody inherits a second unchecked copy of the judgement |
| 13 | A comment claiming nothing writes quests without an expectation was false at baseline too | INFO | **Corrected** in passing |

Two further notes were accepted as accurate and left alone: the residual difference on the
items-refused path (an ownership check and an empty delete now run where the baseline sent no
statement, unreachable while the character is in a live session), and the `ItemPathLiveTests`
failure seen by both reviewers, which is a missing `PVF_ARCHIVE_PATH` in a bare shell and not a
defect -- it passes with the variable set, before and after.

## After remediation

- Build: 0 warnings, 0 errors.
- Full suite: **1304 passed, 2 skipped, 0 failed** (1294 before the fixes; ten tests added).
- Wire and rows re-captured on the remediated build: see
  `../verification/wire-and-rows-unchanged.md`.

No unresolved blocking finding.
