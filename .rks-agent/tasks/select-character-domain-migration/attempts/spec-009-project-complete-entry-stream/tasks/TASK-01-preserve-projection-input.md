# TASK-01 — Preserve Complete Projection Input

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Preserve every owner-correct protocol-relevant fact currently discarded between normalized sources and `SelectCharacterResult`, including avatar detail, public subtype wire facts, skill header/tails and SP slots, option availability, collection status, expert state, premium usage slots, complete mailbox entries and account roster.
- Referenced spec requirements: RQ-002, RQ-005, RQ-006; AC-001, AC-005, AC-006.
- Referenced plan sections: T-001; BD-001, BD-002, BD-003, BD-004, BD-007; CK-001, CK-002.
- Owned boundary: Owner Data/Abstractions/Infrastructure/Application entry-state models, mapping and focused tests required to preserve the listed facts through the completed selection result.
- Permitted files or interfaces: `Server/src/Dfo.Server.Data/{Characters,Inventory,Accounts,Game}/*.cs`; `Server/src/Dfo.Server.Abstractions/{Characters,Inventory,Accounts}/*.cs`; `Server/src/Dfo.Server.Infrastructure/{Characters,Inventory,Accounts}/*.cs`; `Server/src/Dfo.Server.Application/{Characters,Accounts,Commands/Game}/*.cs`; directly affected DI registrations and tests under matching Infrastructure/Application test domains.
- Dependencies: none.
- Required dependency outputs: accepted G1-G5 entry views at baseline `501c2ca330fc6f506261dc9016e82327dccc73fe`.
- Acceptance conditions: Populated protocol-relevant facts survive source-to-result composition; loaded-empty, inapplicable and unavailable states remain distinguishable; required unavailable/malformed state fails before active publication; no raw frame, command/type number, repository handle, PVF type or transport object enters the owner models.
- Task-specific authorization: Implement and test this bounded input-preservation slice in the parent-managed worktree; do not commit, push, merge, alter SDD controller state, or modify later Task files.
- Required methods: Apply the test-value gate per behavior; use RED/GREEN for durable source-to-result distinctions; use compilation/static inspection for structural-only interface changes; preserve existing normalized schema compatibility honestly and do not fabricate defaults.
- Required checks: CK-001 and CK-002 from the approved plan; full solution build; `git diff --check`; dependency scan for SQLite/PVF leakage.
- Required evidence: Exact RED/GREEN commands and decisive output for behavioral cycles, changed paths and source ownership explanation, focused/full check results, residual risks, owned diff identity, and executor self-check appended to this file.

## Execution Result

- Result: implemented the owner-correct source-to-result preservation slice for character avatar detail, public identity/manage-level facts, option availability, explicit premium-usage slots, complete mailbox rows/attachments, and the account roster. `SelectCharacterCommandHandler` now obtains and validates the roster before loading the remaining entry views and still publishes last.
- Test-value gate: accepted. These values are client-visible initialization inputs and the pre-change repository/application mappings silently collapsed or discarded them; the regression oracle asserts normalized non-zero source values at the owner port and the completed application result without coupling to SQL text or packet codecs.
- RED: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly' -v:minimal` failed 0/1 at `Assert.Single`: persisted premium usage produced an empty typed slot collection.
- GREEN: the same command passed 1/1 after mapping normalized counter `lottery_double_reward_used` to explicit slot 1. The same focused fixture also proves public identity/manage level and complete mailbox/attachment preservation.
- CK-001: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~CharacterEntry|FullyQualifiedName~InventoryEntry|FullyQualifiedName~AccountEntry' -v:minimal` passed 37/37.
- CK-002: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~SelectCharacter|FullyQualifiedName~CharacterEntry' -v:minimal` passed 14/14.
- Full build: `dotnet build Server/86JP.Server.slnx --no-restore -v:minimal` succeeded with 0 warnings and 0 errors.
- Full test: `dotnet test Server/86JP.Server.slnx --no-restore -v:minimal` passed: PVF 60/60, Networking 30/30, Application 199/199, Infrastructure 89 passed with 2 documented missing-archive skips.
- Hygiene: `git diff --check` passed. `! rg -n 'Microsoft.Data.Sqlite|Dfo.Pvf' Server/src/Dfo.Server.Application Server/src/Dfo.Server.Data Server/src/Dfo.Server.Abstractions` passed, so owner/application models contain no SQLite or PVF leakage.
- Owned paths: `Server/src/Dfo.Server.{Data,Abstractions,Application,Infrastructure}/{Characters,Inventory,Game}/**` and directly affected Character/Infrastructure tests listed by `git diff --numstat`; owned product/test diff is 401 insertions and 59 deletions across 13 files.
- Owned diff identity: SHA-256 `d1f971e787b6811f74edb4cc4f52cd948da418d98c5415ccb58da616a05e5a5d` over the unstaged owned product/test diff, excluding parent/controller artifacts and this execution record.
- Residual risks: skill page header/tails remain codec/static-content derivations and collection-box availability remains a World/Content projection; neither was fabricated as nullable owner state in this Task. Avatar detail is loaded for character-owned avatar items (item kind 8) and required/malformed rows fail; account-cargo avatar-detail ownership remains for downstream confirmation if that container can carry kind 8.

## Executor Self-check

- Stayed within the Task permitted Data/Abstractions/Infrastructure/Application and matching test paths; did not alter controller state, review/result artifacts, later Task files, Networking, docs, commits, remotes, or branches.
- Reverted an initially considered nullable skill header/tail representation after determining it would manufacture an unavailable owner fact rather than preserve a normalized source.
- Confirmed active-character publication remains the final state mutation and roster absence/wrong ownership fails before publication.
- Reviewed the owned diff for raw frame/command/type-number, repository-handle, SQLite and PVF leakage; none entered owner models.
- No commit or push was performed.

## Independent Quick Review — IQR-001

- Verdict: `changes_required`
- Reviewed Task contract revision: 1; no amendments present.
- Owned diff identity: SHA-256 `d1f971e787b6811f74edb4cc4f52cd948da418d98c5415ccb58da616a05e5a5d` over the 13 declared unstaged product/test paths (401 insertions, 59 deletions), independently reproduced.
- Commands/evidence inspected: current Task file; owned diff and `git diff --numstat`; plan T-001, BD-001/002/003/004/007 and CK-001/002; spec RQ-002/005/006 and AC-001/005/006; executor-cited CK-001 (37/37 passed), CK-002 (14/14 passed), `git diff --check` (passed), and the SQLite/PVF leakage scan (passed).
- Finding F-001: Existing normalized SQLite databases are not migrated for several newly required columns. `SqliteCharacterEntryStateRepository` now selects `s1.manage_level` (`Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryStateRepository.cs:87`) and mailbox fields including `sender_character_id`, `sender_name`, `mail_type`, `source_protocol`, `title`, `body`, `gold`, `received_gold_flag`, and `read_flag`, but `EnsureSchema` only invokes `EnsureCharacterColumns` and then executes `CREATE TABLE IF NOT EXISTS` declarations (`.../SqliteCharacterEntryStateRepository.cs:605-609,831-897`). Those declarations do not add columns to pre-existing `character_subtype1_fields`, `mailbox_messages`, or `mailbox_recipients` tables, so an existing baseline database reaches `no such column` before source-to-result composition. The focused tests construct a brand-new empty database (`Server/tests/Dfo.Server.Infrastructure.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs:240-241`), so the passing CK-001 does not cover this upgrade path. This violates the required honest normalized-schema compatibility and BD-003/T-001 preservation boundary.
- Required remediation: add idempotent existing-table column migration for every newly queried column (at minimum the three tables above), and add a focused test that creates the pre-change normalized schema before repository initialization and proves load/composition succeeds with the intended compatibility semantics. Re-run CK-001 and the declared hygiene checks, then provide a new owned-diff identity for the next fresh review round.

## Execution Result R2

- Result: fixed IQR-001 F-001 only. Repository startup now introspects and idempotently adds every newly queried column to pre-existing `character_subtype1_fields`, `mailbox_messages`, and `mailbox_recipients` tables after the base schema exists.
- Compatibility semantics: added columns use the same neutral `NOT NULL DEFAULT` definitions as a newly created normalized schema. This preserves old rows as legacy-empty public/mail metadata; it does not mask missing required profile rows, item blobs, ownership, or other malformed state, which continue to fail during load.
- Test-value gate: accepted. Startup of an existing normalized database is durable observable behavior and the prior greenfield-only fixtures could not detect `CREATE TABLE IF NOT EXISTS` failing to evolve an existing table.
- RED: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~CharacterEntryState_UpgradesPreProjectionNormalizedSchemaIdempotently' -v:minimal` failed 0/1 with SQLite `no such column: s1.manage_level` while loading a deliberately pre-projection schema.
- GREEN: the same command passed 1/1 after explicit migrations. The test initializes the repository repeatedly before loading, proving the migration is idempotent, and then verifies subtype and mailbox composition from the upgraded database.
- CK-001: `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~CharacterEntry|FullyQualifiedName~InventoryEntry|FullyQualifiedName~AccountEntry' -v:minimal` passed 38/38.
- CK-002: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~SelectCharacter|FullyQualifiedName~CharacterEntry' -v:minimal` passed 14/14.
- Full build: `dotnet build Server/86JP.Server.slnx --no-restore -v:minimal` succeeded with 0 warnings and 0 errors.
- Full test: `dotnet test Server/86JP.Server.slnx --no-restore -v:minimal` passed: PVF 60/60, Networking 30/30, Application 199/199, Infrastructure 90 passed with 2 documented missing-archive skips.
- Hygiene: `git diff --check` passed. `! rg -n 'Microsoft.Data.Sqlite|Dfo.Pvf' Server/src/Dfo.Server.Application Server/src/Dfo.Server.Data Server/src/Dfo.Server.Abstractions` passed.
- Owned diff identity R2: SHA-256 `0383a41bd10833e613e1f238c13120bdb12e4cc9872151650369fbc95e720e99` over the 13 owned product/test paths (519 insertions, 59 deletions), excluding parent/controller artifacts and this Task history.

## Executor Self-check R2

- Changed only `SqliteCharacterEntryStateRepository` and its matching Infrastructure test beyond the reviewed R1 owned diff; did not modify the independent review, controller state, later Tasks, Networking, docs, commits, remotes, or branches.
- Confirmed all new query columns have explicit migrations: `manage_level`; mailbox message sender/name/title/body/gold/type/source; mailbox recipient read/received-gold flags.
- Confirmed repeated repository initialization does not duplicate or fail migrations, while absent required normalized rows still follow the existing rejection path.
- Re-ran focused, full build/test, whitespace and dependency-boundary checks after the final product change.
- No commit or push was performed.

## Independent Quick Review — IQR-002

- Verdict: `passed`
- Reviewed Task contract revision: 1; no amendments present; reviewed latest Execution Result R2 and Executor Self-check R2.
- Owned diff identity: SHA-256 `0383a41bd10833e613e1f238c13120bdb12e4cc9872151650369fbc95e720e99` independently reproduced over the 13 declared owned product/test paths (519 insertions, 59 deletions), excluding parent/controller artifacts and Task history.
- Commands/evidence inspected: current Task file; current owned diff and path/numstat boundary; plan T-001, BD-001/002/003/004/007 and CK-001/002; F-001 remediation at `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryStateRepository.cs:602-653`; focused old-schema/idempotence evidence at `Server/tests/Dfo.Server.Infrastructure.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs:179-247`; independently rerun focused migration test (1/1), CK-001 (38/38), CK-002 (14/14), full solution build (0 warnings/errors), full solution test (PVF 60/60, Networking 30/30, Application 199/199, Infrastructure 90 passed with 2 documented skips), `git diff --check`, and SQLite/PVF leakage scan.
- Findings: no obvious blocking defect demonstrated in the bounded R2 inputs. F-001 is remediated: startup now adds the missing `manage_level`, mailbox-message and mailbox-recipient columns only when absent using the same neutral defaults as fresh schema, and the regression constructs the pre-projection tables, initializes repeatedly, and proves load/composition succeeds. The current diff remains within BD-001/002/003/004/007 and satisfies the directly referenced T-001/CK-001/CK-002 boundary.
- Required remediation: none.
