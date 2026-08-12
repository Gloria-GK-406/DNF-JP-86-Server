# TASK-02 — Encode All Static Body Families

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Implement deterministic ACK, inventory, user-info, character, account and protocol-constant body projections sufficient for static rows 1 through 58.
- Referenced spec requirements: RQ-002, RQ-003, RQ-005, RQ-006; AC-001, AC-002, AC-003, AC-005, AC-006.
- Referenced plan sections: T-002; BD-001, BD-005, BD-007; CK-003.
- Owned boundary: Select-character body codecs, supporting neutral projection values and focused Networking behavior vectors; no sequence or sender integration yet.
- Permitted files or interfaces: `Server/src/Dfo.Server.Networking/Framing/SelectCharacter*.cs`, `GameProtocol.cs`; directly required neutral Data projection contracts; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacter*`.
- Dependencies: TASK-01.
- Required dependency outputs: Complete owner-correct typed inputs accepted from TASK-01.
- Acceptance conditions: Every static row body has one deterministic codec; required unrepresentable values fail; high-risk distinctions and applicable empty states are covered; codecs query no service or repository and perform no mutation.
- Task-specific authorization: Implement and test only the static body families; do not compose the final ordered stream, alter transport delivery, commit or push.
- Required methods: Behavior-first legacy-vector tests where a faithful oracle exists; exact little-endian shape; retain unresolved compatibility names rather than inventing domain meaning.
- Required checks: CK-003; Networking project tests; `git diff --check`; codec dependency scan.
- Required evidence: RED/GREEN cycles, body identities/lengths/key offsets, changed paths, checks, risks, owned diff identity and executor self-check appended to this file.

## Contract Amendment A-001 — Neutral Static Projection Values

- Trigger: TASK-01 intentionally left values that are derived only for the wire projection out of owner storage state: four skill-point protocol mirrors and collection-box expiry/status. Legacy evidence confirms subtype-0's guild name is a fixed empty compatibility value, while equipped-creature display and selected appearance are derived from the accepted inventory/G3 pet inputs rather than requiring another stored model.
- Same-Spec rework authorization: within TASK-02's existing “directly required neutral Data projection contracts” boundary, add optional neutral compatibility records to the existing character entry models. Codecs must reject a missing required record; they may not turn absence into zeros. No repository, Application service, PVF object, wire command/type, sequence, transport or G7 lifecycle state may enter these records.
- Acceptance effect: behavior vectors supply non-zero representative values and prove their exact offsets; unavailable required records fail deterministically. Later composition work must provide the neutral records before a static stream can be emitted.
- Deferred boundary: X-04 account-roster appearance is supplemental rather than part of rows 1-58. TASK-04 must add and require that account-owned typed input; this Task neither encodes nor silently drops it.
- Files added to owned boundary: `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs` only, already covered by BD-001 and the Task permitted neutral Data clause.

## Contract Amendment A-002 — Row-9 Special-Reward Content Fact

- Trigger: IQR-002 proved that an exact codec and typed collection were insufficient while production populated both collections as literals. Legacy evidence classifies only completed quest IDs whose QST contains `[special reward status]`; projecting every clear flag would broaden semantics and duplicate unrelated quest state.
- Same-Spec boundary extension: add one neutral `QuestDefinition.HasSpecialRewardStatus` content fact, project it from tag presence in `PvfQuestDefinitionSource`, and let `CharacterEntryStateService` filter the repository's already normalized non-zero clear flags through `IQuestDefinitionSource`. This preserves the existing outbound Data model and keeps PVF/repository access out of Networking.
- PvP loaded-empty decision: the retired legacy migration states `character_pvp_results` had no writer and was empty before dropping it. The SQLite provider therefore uses one named/commented loaded-empty projection until a new authoritative PvP persistence source exists; no table or replacement semantics are invented.
- Added necessary paths: `Server/src/Dfo.Server.Abstractions/Content/QuestContent.cs`, `Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs`, existing Character Application/Infrastructure paths, and their focused tests.

## Execution Result ER-001

- Status: complete.
- Implemented cohesive static projections for ACK; the five item-list containers plus account cargo; all repeated `NOTI_2` user-info bodies; skill, quest, progression, feature, account, mailbox, premium-service and protocol-constant body families used by rows 1-58. This Task deliberately exposes body codecs only; row order, framing and sending remain outside its boundary.
- Owner-correct derived values: selected appearance is rebuilt from current equipment; equipped-creature name/alive comes from the matching G3 pet and `Field04 > 0`; main-list virtual currency/cube slots are merged from character/account inputs in the observed 0-2/items/354-359 order; skill and collection projection mirrors are required rather than zero-filled.
- Mailbox: detail-only messages emit the observed seed summary; gold and every populated attachment emit summary records before details; attachment item-core is decoded from the accepted 82-byte typed value; official sender compatibility and bounded sender/body strings are preserved; missing avatar detail fails.
- Required-unavailable behavior: missing skill mirror, collection protocol state, avatar detail, malformed virtual slots, malformed mailbox item-core/detail and invalid fixed-size bitmaps/decks throw `InvalidDataException`.
- RED evidence:
  - Initial CK-003 failed at compile with four `CS0103` errors because `SelectCharacterStaticPayloadCodec` did not exist.
  - Focused compatibility vectors then failed with `OverflowException` for premium type 581 and `Expected 0 / Actual 1` for equipped-pet alive state; both were corrected against legacy byte projection and pet `Field04`.
  - The populated inventory vector failed `Expected 10 / Actual 1` before the nine observed virtual main-list rows were included.
- GREEN and verification evidence (fresh final run):
  - `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacterPayload"` — passed 14/14.
  - `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore` — passed 44/44.
  - `git diff --check` — passed.
  - `rg -n "Repository|I[A-Za-z]+Service|DbConnection|Sqlite|PVF|Pvf"` across both codec files — no matches.
- Behavior vectors cover ACK key offsets and premium width, common/avatar/virtual inventory shapes, all 52 static notification occurrences, seven exact constant-body lengths, premium-service length, option presence, missing projection mirrors/details, pet alive semantics, and mailbox gold/attachment identity.
- Owned product paths:
  - `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs` (only the protocol-mirror compatibility records/hunks authorized by A-001)
  - `Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticPayloadCodec.cs`
  - `Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticBodyFamilies.cs`
  - `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterPayloadCodecTests.cs`
- Owned product diff identity: Git blob hash `3d58028a23f30e8c9eca1c7d15bd962759f433c3` over the unstaged diff of the four product paths above, captured after the final checks and before appending this result.
- Deferred required input: X-04 roster appearance is not a static row body and remains an explicit TASK-04 required input under A-001; it is not silently dropped here.
- Residual risk: several bodies preserve legacy-observed compatibility constants whose semantic meaning remains unknown; mailbox avatar details continue to use the already accepted typed row's JSON payload and deliberately reject malformed/missing content. Exact live-client acceptance remains the later integration boundary.

## Executor Self-check SC-001

- Scope: PASS — changes are limited to static body codecs, focused Networking vectors and A-001 neutral Data records; no sequence, sender, service, repository or lifecycle code was added.
- Requirements: PASS — all rows 1-58 have a deterministic body path, duplicates accept an explicit occurrence, and required unavailable inputs fail rather than inventing zero state.
- Architecture: PASS — codecs depend only on accepted Data values and perform no I/O or mutation.
- Tests: PASS — real REDs preceded the principal codec and compatibility behavior; fresh focused and full Networking suites are green.
- Hygiene: PASS — no commit/push, `git diff --check` is clean, dependency scan is empty, and pre-existing/shared TASK-01/controller-owned work was not edited except the explicitly permitted neutral Data hunks.

## Independent Quick Review — IQR-001

- Verdict: `changes_required`
- Reviewed Task contract revision: 1 plus Amendment A-001; reviewed ER-001 and SC-001.
- Owned diff identity: declared Git blob hash `3d58028a23f30e8c9eca1c7d15bd962759f433c3`; independently reproduced by hashing `git diff` over the four declared paths, but that command includes only the tracked `CharacterModels.cs` patch (72 insertions, 9 deletions) and excludes the three untracked codec/test files, so it is not a complete identity of the reviewed owned product diff.
- Commands/evidence inspected: current Task file; owned path/status/diff and declared hash reproduction; plan T-002, BD-001/005/007 and CK-003; accepted TASK-01 required output/IQR-002; authoritative legacy static matrix row 9 and legacy `UserInfoSubtype1Builder`; current subtype-1 model/codec and focused vectors; independently rerun CK-003 (14/14 passed), full Networking tests (44/44 passed), `git diff --check` (passed), and codec dependency scan (no matches).
- Finding F-001: static row 9 (`NOTI 00/0002`, subtype 1) is not a faithful deterministic body projection for populated legacy state. The legacy body writes a `u8` PvP-result count followed by `u32 + u16 + u16` per result, then a `u8` manage level, then a `u32` special-reward-quest count followed by `u32` IDs (`main:Server/DfoServer/Network/Builders/Init/UserInfoSubtype1Builder.cs:86-98`). The successor writes literal zero counts for both sections (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticBodyFamilies.cs:560`) and `CharacterSubtype1EntryState` exposes neither required collection (`Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:109-145`). Thus populated values cannot be represented and are silently projected as false empty state, violating T-002 step 2, the Task's every-static-row/high-risk/unavailable-value acceptance, and A-001's prohibition on turning missing required projection data into zeros. The green all-family test only asserts that the returned body is non-null (`Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterPayloadCodecTests.cs:68-93`), so CK-003 does not detect the missing legacy sections.
- Finding F-002: ER-001's owned-diff identity does not cover the implementation under review. The three new codec/test files are untracked, while `git diff -- <four paths>` hashes only the tracked Data patch; the reproduced `3d580...` therefore cannot identify changes to the body codecs or behavior vectors. This makes the required owned-diff evidence incomplete even though the path boundary itself is locally consistent.
- Required remediation: preserve or explicitly supply the two row-9 collections through the accepted typed input boundary, encode their exact legacy widths/order with checked counts, reject unavailable required state instead of emitting zero, and add a non-zero exact-offset legacy vector. Then rerun CK-003/full Networking/hygiene checks and report an owned-diff identity that includes untracked files before the next fresh review round.

## Execution Result ER-002 — IQR-001 Remediation

- Status: complete; supersedes ER-001 only for F-001/F-002 remediation evidence while preserving its implementation/history.
- F-001 remediation: added normalized `CharacterPvpResultStorageRow` and `SpecialRewardQuestIds` inputs, preserved both through `CharacterProfileEntryService` into owner-correct `CharacterSubtype1EntryState`, and removed row 9's literal empty counts. The subtype-1 codec now emits the legacy sequence `u8 count + N × (u32,u16,u16)`, `u8 manageLevel`, `u32 count + N × u32`, then the existing tail. Null required collections fail explicitly and PvP count overflow is checked.
- RED: `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~Subtype1PreservesPvpResultsAndSpecialRewardQuestIds"` failed with `Expected [2,68,51,34,17,...] / Actual [0,0,0,0,0,...]` before codec implementation. A preceding compile-only assertion-overload error was corrected and is not claimed as behavioral RED.
- GREEN and fresh final checks:
  - CK-003 `--filter "FullyQualifiedName~SelectCharacterPayload"` — passed 15/15.
  - Full Networking project — passed 45/45.
  - Full Application project — passed 199/199.
  - Full Infrastructure project — passed 90, skipped 2, failed 0.
  - `git diff --check` — passed.
  - codec dependency scan — no matches.
- F-002 remediation: built a temporary alternate Git index from HEAD, applied intent-to-add for all three untracked TASK-02 product files in that isolated index, and hashed `git diff --binary HEAD` across the complete eight-path owned/product-and-required-input set. Complete owned diff identity: `7ebe428ca1bebb04dfccffdc31ef5209d25ec7c7` (1682 insertions, 49 deletions). This identity includes the previously omitted untracked codec and test contents without staging the shared worktree.
- Complete hash path set: `ICharacterEntryStateServices.cs`, `CharacterEntryStateServices.cs`, `CharacterModels.cs`, `SqliteCharacterEntryStateRepository.cs`, both `SelectCharacterStatic*Codec/BodyFamilies.cs` files, `CharacterEntryStateTests.cs`, and `SelectCharacterPayloadCodecTests.cs`.
- Scope note: the four normalized-input paths are the minimum same-Spec rework required by IQR F-001; they do not add a service/repository query to the codec, wire types to owner models, or G7 lifecycle behavior.

## Executor Self-check SC-002

- F-001: PASS — non-empty representative collections survive normalized input → typed state → exact row-9 bytes, and unavailable required collections cannot masquerade as empty.
- F-002: PASS — the new complete binary diff hash demonstrably includes untracked product files through an isolated alternate index; the shared index was not modified.
- Regression: PASS — focused and full Networking, Application and Infrastructure checks are fresh and green; hygiene and dependency scans pass.
- Boundary: PASS — no controller, review, result, sequence, sender, transport, commit or push change was made.

## Independent Quick Review — IQR-002

- Verdict: `changes_required`
- Reviewed Task contract revision: 1 plus Amendment A-001; reviewed latest ER-002 and SC-002.
- Owned diff identity: complete Git blob hash `7ebe428ca1bebb04dfccffdc31ef5209d25ec7c7` independently reproduced over the declared eight-path binary diff using an isolated alternate index (1682 insertions, 49 deletions). The reproduced diff includes the complete contents of all three untracked codec/test files; F-002 is remediated.
- Commands/evidence inspected: current Task file; declared eight-path owned diff/status/hash; plan T-002, BD-001/005/007 and CK-003; accepted TASK-01 output/IQR-002; legacy `UserInfoSubtype1Builder` and `SqliteSubtype1Repository`; current subtype-1 storage/model/service/SQLite provider/codec and focused vectors; independently rerun CK-003 (15/15 passed), full Networking (45/45), Application (199/199), Infrastructure (90 passed, 2 expected skips), `git diff --check` (passed), and codec dependency scan (no matches).
- Finding F-003 (F-001 remains incomplete): row 9's codec shape is now exact for supplied populated values: it emits `u8 count + N × (u32,u16,u16)`, `u8 manageLevel`, then `u32 count + N × u32`, and the non-zero suffix vector verifies the order, widths and little-endian bytes (`Server/src/Dfo.Server.Networking/Framing/SelectCharacterStaticBodyFamilies.cs:560-570`; `Server/tests/Dfo.Server.Networking.Tests/Framing/SelectCharacterPayloadCodecTests.cs:235-257`). However, the sole production `SqliteCharacterEntryStateRepository` still constructs both required collections as literal empty arrays (`Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryStateRepository.cs:130-140`). This demonstrably discards the legacy-populated special-reward quest IDs, which were derived from non-zero `character_invisible_falgs` rows (`main:Server/DfoServer/Game/CharacterData/SqliteSubtype1Repository.cs:179-196`). The Application test injects non-zero lists through a fake storage record and the Networking test starts from a hand-built result, so neither covers the real normalized-input path. Therefore ER-002/SC-002's claim that populated collections survive normalized input to exact row-9 bytes is false for production input, and required data can still masquerade as loaded-empty, violating T-002 step 2 and the original F-001 remediation boundary.
- Required remediation: replace the production provider's literal empty projection with owner-correct supplied values for every available row-9 collection (preserving special-reward quest classification upstream or via another accepted typed input, without adding service/PVF queries to the codec), and add a non-zero production source-to-result regression that feeds the row-9 vector. If PvP results are legitimately loaded-empty because no normalized source exists, make that source-state decision explicit rather than using one unconditional literal for both collections. Re-run CK-003 and the declared Application/Infrastructure/hygiene checks and report a new complete owned-diff identity.

## Execution Result ER-003 — IQR-002 Remediation

- Status: complete; supersedes ER-002 only for F-003 source evidence.
- Production source correction: removed the unconditional paired empty projection. SQLite continues to load non-zero completed-quest flags from `character_invisible_falgs`; `CharacterEntryStateService` now filters those normalized IDs through `IQuestDefinitionSource` and populates subtype-1 `SpecialRewardQuestIds` only when the authoritative `QuestDefinition.HasSpecialRewardStatus` fact is true.
- Content fact: A-002 adds the narrow neutral boolean and `PvfQuestDefinitionSource` derives it solely from presence of `[special reward status]`. The real archive vector proves quest 13502 is true and 13501 is false; no clear ID is broadened into special-reward state.
- PvP source decision: `character_pvp_results` is not recreated. The legacy replay-removal migration explicitly records it had no writer and was empty before dropping it. The production repository now calls a named/commented `LoadRetiredPvpResults()` compatibility projection, and both repository and end-to-end vectors assert loaded-empty until an authoritative new persistence source exists.
- Behavioral RED: after correcting an unrelated fixture `skill_tree_index=-1` overflow, `SelectCharacterPayload_Subtype1PreservesRealSqliteSpecialRewardSource` failed `Expected [13502] / Actual []` against the production SQLite repository/Application path. It is now green and additionally verifies the encoded row-9 ID bytes.
- Fresh GREEN/check evidence:
  - CK-003 `SelectCharacterPayload` — passed 16/16.
  - Full Networking — passed 46/46.
  - Full Application — passed 199/199.
  - Full Infrastructure — passed 90, skipped 2, failed 0.
  - Real `Script.pvf` focused archive vector — passed 1/1, including 13502/13501 classification.
  - `git diff --check` — passed; Networking codec dependency scan — no matches.
- Complete owned diff identity: `ee4d5ab96a0dea80e1600fdb84af8b58062c2765`, generated with an isolated alternate Git index and `git diff --binary HEAD` across all 12 owned/required-input paths, including the three untracked codec/test files (1903 insertions, 59 deletions). Shared index remained untouched.
- Complete paths: Character and Content Abstractions; Character Application/Data/SQLite Infrastructure; PVF quest source; both Networking codec files; Application character, Infrastructure character/content, and Networking payload tests.

## Executor Self-check SC-003

- F-003 production path: PASS — a real populated SQLite clear flag reaches owner typed state only through an affirmative content classification and reaches exact row-9 bytes.
- No semantic broadening: PASS — ordinary clear flags are excluded; real Script.pvf verifies the positive/negative pair.
- PvP honesty: PASS — loaded-empty is isolated and documented from direct migration evidence rather than conflated with special-reward input or backed by an invented table.
- Architecture: PASS — Networking remains pure; content lookup occurs in Application orchestration over normalized repository state.
- Verification/hygiene: PASS — fresh focused/full suites, real archive vector, diff check and dependency scan pass; no review/controller/sequence/sender/transport/commit/push mutation.

## Independent Quick Review — IQR-003-20260812

- Verdict: `passed`
- Reviewed Task contract revision: 1 plus Amendments A-001 and A-002; reviewed latest ER-003 and SC-003 only for F-003 remediation.
- Owned diff identity: independently reproduced complete Git blob hash `ee4d5ab96a0dea80e1600fdb84af8b58062c2765` from an isolated alternate index over the declared 12-path binary diff (12 files, 1903 insertions, 59 deletions). The identity includes all three untracked Networking codec/test files and every A-002/F-003 owned source and test path without changing the shared index.
- Commands/evidence inspected: current TASK-02; plan T-002, BD-001/005/007 and CK-003; the declared 12-path owned diff; production SQLite clear-flag load, `IQuestDefinitionSource` classification, typed subtype-1 projection and row-9 codec; focused SQLite/Application/Networking and real-PVF vectors; replay-removal commit `abe02d1` and its predecessor database/schema evidence; independently rerun CK-003 (16/16), full Networking (46/46), Application (199/199), Infrastructure (90 passed, 2 expected skips), real-archive quest vector (1/1), `git diff --check` (passed), and codec dependency scan (no matches).
- Findings: no obvious blocking defect in the bounded inputs. F-003 is remediated: non-zero `character_invisible_falgs` rows remain normalized SQLite quest facts, `CharacterEntryStateService` admits only definitions with `HasSpecialRewardStatus`, `PvfQuestDefinitionSource` derives that fact from `[special reward status]`, the typed list reaches exact row-9 bytes, and the positive/negative archive pair prevents broadening ordinary clear flags. The production chain uses the real source through `IQuestDefinitionSource`; the row-9 source-to-byte vector exercises the same SQLite/Application/codec chain with a focused source double while the separate real-archive vector verifies the production classifier.
- PvP loaded-empty evidence: predecessor source at `abe02d1^` contains only the table definition and one SELECT for `character_pvp_results`, no INSERT/UPDATE/DELETE writer; the predecessor checked-in SQLite table contains zero rows; commit `abe02d1` explicitly retires the writerless empty table. The named `LoadRetiredPvpResults()` projection and focused repository/end-to-end empty assertions therefore represent an evidenced loaded-empty state rather than invented persistence semantics.
- Scope: this `passed` verdict authorizes only TASK-02 acceptance logic; it is not complete-change or Spec approval.
