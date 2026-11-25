# TASK-03 — 战绩持久化、`character_pvp` 拆分与 v11 迁移

## Task Unit Contract

- Revision: 1
- Outcome and references: 数据库结构 v11；战绩、对局、对局结果、迁移审计四类持久化；`character_pvp` 拆分且全部消费方迁移到位；`IPvpRecordDomainStore`（幂等结算、按游戏日计数、读取战绩）；`Tool/DbMigrate` 10→11 步骤与测试；`Document/DatabaseSchemaMigration.md` 版本说明。Plan T-002；Spec RQ-054（存储侧）、RQ-055（同源）、RQ-060；AC-054（存储侧）、AC-055（同源部分）、AC-060。
- Owned boundary: `Server/src/Dfo.Server.Persistence/**`（Schema、Characters、新 `Pvp/`、`PersistenceModule`）、`Server/src/Dfo.Server.Abstractions/Persistence/CharacterBodyTables.cs` 与 `Abstractions/Pvp/` 中新增的 Store 契约、`Server/src/Dfo.Server.Application/Characters/`（`CharacterRosterService`、`CharacterEntryAssembly`、`CharacterEntryStateServices`、`SelectCharacterPersistenceServices` 中涉及 `character_pvp` 的部分）、`Server/src/Dfo.Server.Data/**` 中随之删除的旧字段、`Tool/DbMigrate/**`、`Tool/DbMigrate.Tests/**`、`Document/DatabaseSchemaMigration.md`、相关测试项目中引用这些类型的测试。不触碰房间、规则服务实现、协议。
- Required checks: 解决方案构建；`Tool/DbMigrate.Tests` 全部（含 `SchemaVersionTests` 迁移后结构与新建库一致）；`Dfo.Server.Persistence.Tests` 全部；Application.Tests 中 Characters、Sessions、Execution 过滤；新增 Store 测试；Networking `SelectionLifecycleWholeCommandTests`；与 `verification/baseline-failed.txt` 对比无新增失败。
- Dependencies: TASK-02
- Result review: required — 数据迁移难以回退；`character_pvp` 缺行或越界会让角色无法进游戏；结算幂等是后续结算正确性的根。

## Detailed Plan

表结构（v11，列名可按仓库风格微调，语义固定）：
- `character_pvp_record(character_id PK FK→characters ON DELETE CASCADE, grade, rating_grade, grade_experience INTEGER NULL, win, lose, draw, win_streak, peak_win_streak)`。`grade_experience` 为 NULL 表示"取当前段下限"（迁移不依赖 PVF，规则服务 `LowerBound` 在结算时补值）。
- `pvp_matches(match_id TEXT PK, room_id TEXT, listener_port, channel_env, battle_mode, outcome_kind /*0 win,1 draw,2 cancelled*/, winner_team_or_seat, game_day TEXT, completed_at)`。
- `pvp_match_results(match_id FK, character_id, seat, team, outcome, kills, deaths, fled, counts_toward_daily, previous_grade, grade, experience_change, experience, win_streak, PK(match_id, character_id))`，并为 `(character_id, game_day)` 计数提供索引（经 join 或冗余 `game_day` 列，自行选择，测试证明计数正确）。
- 决斗技能初始化标记：先查仓库是否已有决斗技能状态表（`character_pvp_skill_state` 或技能表的 mode 列）；有则并入，没有则新建 `character_pvp_skill_state(character_id PK, skills_initialized)`。`SelectCharacterPersistenceServices.PvpSkillProfileService` 改读写它。
- `character_pvp_legacy(character_id PK, rank_point, stat_a, win_streak, lose_streak)`：只保存迁移时旧列不全为 0 的行。
- 删除 `character_pvp` 表。

迁移步骤 `Steps[10]`：复制 v11 建表 SQL（不引用声明）；`character_pvp` → 战绩表（grade、rating_grade，经验 NULL，胜负平与连胜 0）、技能状态表、审计表；建角时也要插入战绩与技能状态必要行（`CharacterRosterService` 原插入 `character_pvp` 处）。迁移测试：构造 v10 库（含各旧列非 0 值）迁移后断言各去向与段位、技能初始化不变，以及迁移后结构与新建库一致。

消费方：`ICharacterPvpTable`/`CharacterPvpRow` 按新表拆为战绩读取（入场用的段位）与技能状态读写；`CharacterEntryAssembly` 只读段位与评级；`CharacterPvpStandingStorageRecord` 中已无编码方的 RankPoint/StatA/WinStreak/LoseStreak 字段与其范围校验一并删除（先 grep 证明无消费方）。

`IPvpRecordDomainStore`（Abstractions/Pvp，实现 Persistence/Pvp，注册 PersistenceModule）：
- `ReadAsync(CharacterId)` → 战绩（段位、评级、可空经验、胜负平、连胜、最高连胜）；批量重载可选。
- `DailyCountsAsync(CharacterId, DateOnly gameDay)` → 当日计入每日计数的胜/负场数。
- `SettleAsync(PvpMatchSettlementRecord)` → `Created(results)` 或 `AlreadySettled(existing results)`：同一 `match_id` 已存在则回读、不重写；写入对局、各人结果并更新战绩（含连胜：胜 +1、负或平清零，最高连胜取大）。须在调用方事务内（无事务写即抛，沿用 `SqliteDatabase`）。
- Store 测试：首次 Created、二次 AlreadySettled 且结果相同、事务回滚后无残留、按游戏日计数、连胜更新。

文档：`Document/DatabaseSchemaMigration.md` 增加 10→11 一行。

## Result

- Outcome: done
- Changed:
  - Schema v11 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`：删 `character_pvp`；新增 `character_pvp_record`、`character_pvp_skill_state`、`character_pvp_legacy`、`pvp_matches`、`pvp_match_results` 与索引 `ix_pvp_match_results_character`。
  - 契约 `Server/src/Dfo.Server.Abstractions/Pvp/PvpRecords.cs`（`PvpRecord`、`PvpDailyCounts`、`PvpMatchOutcomeKind`、`PvpFighterSettlement`、`PvpMatchSettlementRecord`、`PvpFighterResult`、`PvpSettleOutcome.Created/AlreadySettled`、`IPvpRecordDomainStore`）；实现 `Server/src/Dfo.Server.Persistence/Pvp/SqlitePvpRecordDomainStore.cs`，`PersistenceModule` 注册。
  - `ICharacterPvpTable`/`CharacterPvpRow` 拆为 `ICharacterPvpRecordTable`（`FindGradesAsync`、`InsertIfAbsentAsync`）与 `ICharacterPvpSkillStateTable`（`FindAsync`、`InsertIfAbsentAsync`、`MarkSkillsInitializedAsync`）；消费方 `CharacterEntryAssembly`（只读段位/评级）、`CharacterRosterService`（建角插两行）、`PvpSkillProfileService`（读写技能状态）。
  - `CharacterPvpStandingStorageRecord` 删 `RankPoint/StatA/WinStreak/LoseStreak`，`RequireProfileInRange` 删对应三处校验。
  - 迁移 `Tool/DbMigrate/PvpRecordSplitUpgrade.cs`、`Migrator.Steps[10]`；测试 `Tool/DbMigrate.Tests/PvpRecordMigrationTests.cs`；`PlayerPortalMigrationTests` 删硬编码 `Version==10` 断言。
  - Store 测试 `Server/tests/Dfo.Server.Persistence.Tests/Pvp/SqlitePvpRecordDomainStoreTests.cs`；`CharacterBodyShapeTests`、`CharacterBodyCreationTests`、`SchemaOwnershipTests`（表数 123→127）、`EntryLoadCharacterizationTests`、`SqliteCharacterEntryStateRepositoryTests`、`SkillCarrierTests`、`CharacterSide`、`ClassificationRefusalTests` 及 20 个测试种子 SQL 改到新表。
  - `Document/DatabaseSchemaMigration.md` 加 10→11 一行。
- Why:
  - grep 证明旧四字段无编码方：`Server/src` 中 `RankPoint/StatA/WinStreak/LoseStreak` 只出现在 `CharacterEntryAssembly` 的构造与范围校验（`ClientRankPoint` 是地下城结算的另一概念）；`Results` 仍保留（同样无编码方、恒为空，属 TASK-06 选角快照的决定，未越界删除）。
  - 与既有裁定冲突：`TableOwnershipRuling` 曾把 `character_pvp_skill_state` 并入 `character_pvp`，`CharacterBodyShapeTests.FoldedAway` 断言它不存在。Spec RQ-060 要求拆分且 `skills_initialized` 不进战绩，仓库也没有可并入的技能状态表（`character_skills.mode` 是逐技能行），所以按 TASK 新建该表，保留"建角即有行、旗标代替行存在"的语义；测试改为 `character_pvp` 进 FoldedAway，级联外键断言覆盖两张拆出的表。
  - `room_id` 用 INTEGER（房间号是 `ushort`）；`pvp_matches` 增 `records_result` 列（取消对局 CHECK 不得记录）；每日计数不冗余 `game_day`，经 join + `ix_pvp_match_results_character`，测试证明计数正确；`pvp_match_results.character_id` 不加外键（历史结果不随角色级联删除）；审计表不加外键。
  - 迁移不补造缺行：v10 中无 `character_pvp` 行的角色迁移后也无战绩行（与迁移前一样进不了游戏，不发明段位）；迁移经验为 NULL，读作段位下限，由规则服务 `LowerBound` 在结算时补值（迁移工具不读 PVF）。迁移对 legacy（v0）库也成立：legacy 步按当前声明建新表，第 10 步再搬 `character_pvp`。
  - `SettleAsync`：先 `INSERT ... ON CONFLICT(match_id) DO NOTHING`，未插入即回读结果返回 `AlreadySettled`；只在 `RecordsResult` 时更新战绩（胜 +1 连胜、负/平清零、最高连胜取大、胜负平计数、段位/经验取 Decision）；`previous_grade` 取库内当前段位；结果按 seat、character_id 排序，首结与重复返回一致；无事务写由 `SqliteDatabase` 抛出。
- Checks（`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`）：
  - `dotnet build Server/86JP.Server.slnx`：成功，无警告。
  - `Tool/DbMigrate.Tests` 全部 33 通过（含 `SchemaVersionTests` 结构一致、新增 2 项迁移测试）。
  - `Dfo.Server.Persistence.Tests` 全部 489 通过（含新增 Store 测试 8 项）。首跑 `SchemaOwnershipTests` 表数断言失败，更新后通过。
  - Application.Tests 全量：6870 通过 / 26 失败，全部在基线清单内（含 Characters、Sessions、Execution）。
  - Protocol.Tests 全量 4 失败、Networking.Tests 中 `ChannelControlMessagesIntegrationTests` 3 失败：原因是测试 bin 目录残留的默认库 `bin/Debug/net10.0/Data/dfo-server.db` 仍是 v10（被新服务端正确拒绝）；把这两个残留库改名为 `*.v10-stale` 后重跑全部通过。其余 Networking 失败（14）、Host 1、Infrastructure 17 均在基线清单内。`SelectionLifecycleWholeCommandTests` 2 项通过。
- Remaining:
  - AC-060"经验初值为段位下限"在存储侧表现为 NULL，真正取值发生在结算/0x0030 读取时调用 `LowerBound`；TASK-05/06 消费 `PvpRecord.Experience` 时必须处理 null。
  - `CharacterPvpStandingStorageRecord.Results` 无编码方、恒为空；TASK-06 做 0x0030 快照时决定替换或删除。
  - 版本号出现之前、仍带旧版 `character_pvp_skill_state`（裁定前形状）的库未被处理：legacy 步会保留旧表，第 10 步插入会失败。现网与本地均为 v10，不受影响。
  - 本地其他工作区/测试 bin 下的 v10 默认库在新版本下会被拒绝，需迁移或删除；测试服部署需先跑 DbMigrate 迁到 v11。

## Result Review

- Verdict: approved（独立快速审查）。自跑 DbMigrate.Tests 33 项、Persistence.Tests 489 项全过；无换行噪音；两个改名的测试残留库被 `.gitignore` 忽略。
- 移交：N1 `Docs/Refactor/TableOwnershipRuling.md` 关于 `character_pvp_skill_state` 并入 `character_pvp` 的记载已被 v11 取代，由 T-007 文档单元补修正案；N3 经验为空须取 `LowerBound`（含 `0x0030`）、N4 不记录频道须传与当前战绩一致的决定，写入 TASK-05 合同。
