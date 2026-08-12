---
kind: rks-verification
decision: pass
---

# Completion Evidence: Complete G5 Quest Eligibility

## Claim

The G5 `AvailableQuests` answer now applies the complete evidenced standalone-server
eligibility predicate, fails closed on malformed or unavailable mandatory restrictions,
preserves late publication, and remains isolated from Networking and lifecycle work.

## Condition Mapping

| Condition | Fresh evidence |
|---|---|
| SC-8.1 / RQ-001 | The reviewed 16-row predicate matrix covers identity, exposure, event, creature, expert, training, grade, target, level, job, grow, cleared/repeatable, prerequisites, answer flags, character collision and account collision. Complete independent review is approved. |
| SC-8.2 / RQ-002-RQ-006 | Application rule suite passes 55 focused cases; exact-all, mixed job declarations, target invalidity, creature target equipment, training, prerequisites, flags, collisions, grow branches, order/range/duplicates all have positive and negative evidence. |
| SC-8.3 / RQ-001/RQ-005/RQ-006 | Quest source suite passes 20 cases with one expected archive-present skip; creature source suite passes 8. Real PVF yields 2,719 validated definitions from 2,740 list rows; malformed owners are excluded conservatively. Dependency scan finds no PVF/SQLite leakage. |
| SC-8.4 / RQ-007 | Select-character workflow focused suite passes 12 cases; mandatory content failure preserves the old active character and publishes the replacement zero times. |
| SC-8.5 / RQ-008 | Fresh full solution result with `Script.pvf`: 378 passed, 2 expected archive-present negative-path skips, 0 failed. `git diff --check` passes and no G6/G7 implementation path is changed. |

## Fresh Commands and Results

- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`: exit 0; PVF 60, Application 199, Infrastructure 89 plus 2 expected skips, Networking 30; total 378 passed, 2 skipped, 0 failed.
- `git diff --check`: exit 0.
- Dependency scan for `Dfo.Pvf`, `PvfToken`, `PvfArchive`, and `Microsoft.Data.Sqlite` in Abstractions/Data/Application/Networking: no matches.
- Independent complete change review: `approved`.

## Skips

The two skipped Infrastructure tests intentionally exercise missing-archive failure. They
skip when the supplied archive is reachable; positive real-archive coverage ran and passed.
