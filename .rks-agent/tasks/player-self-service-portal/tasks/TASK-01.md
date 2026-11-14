# TASK-01 — 表结构 v7 与玩家后台持久化

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付表结构 v7（三张新表、`gm_audit` 两个新列）、DbMigrate 步骤 `[6]`、玩家后台设置 / 会话 / 操作记录的持久化接口与 SQLite 实现、审计记录的操作者扩展与按操作者筛选，以及它们的测试。对应 Plan T-002 与 SD-6、SD-7（Data / Abstractions / Persistence 三层）、SD-9（审计存储部分：`ActorKind`、`AccountId`、操作者筛选），SD-1 中重放判定与每日用量所需的查询；覆盖 RQ-050、RQ-020（存储与默认值）、RQ-024（存储），AC-050。
- Owned boundary:
  - `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`（v7 声明与 `Version = 7`）
  - `Server/src/Dfo.Server.Data/PlayerPortal/`（新建，record 与枚举）
  - `Server/src/Dfo.Server.Data/Administration/GmAdministration.cs`（`GmAuditRecord`、`GmAuditFilter` 的增量）
  - `Server/src/Dfo.Server.Abstractions/PlayerPortal/`（新建，持久化接口）
  - `Server/src/Dfo.Server.Persistence/PlayerPortal/`（新建，SQLite 实现）
  - `Server/src/Dfo.Server.Persistence/Administration/SqliteGmAdministrationPersistence.cs`（审计读写两列与操作者筛选）
  - `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`（注册一行）
  - `Tool/DbMigrate/PlayerPortalUpgrade.cs`（新建）、`Tool/DbMigrate/Migrator.cs`（登记 `[6]`）
  - `Tool/DbMigrate.Tests/`（新增用例）
  - `Server/tests/Dfo.Server.Persistence.Tests/`（新增玩家后台持久化测试；`Administration/GmAuditTransactionTests.cs` 补用例；`Schema/SchemaOwnershipTests.cs` 更新表数与 `gm_audit` 形状断言）
  - `Document/DatabaseSchemaMigration.md`（补"版本 6 → 7"一条）
  - 不改 Application、GmWeb、Host 的生产代码；GM 现有审计写入点一行不动（靠新参数的缺省值保持 `gm`）。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 零警告。
  - `Tool/DbMigrate.Tests` 全部通过（含既有 `A_migrated_database_has_exactly_the_shape_the_server_builds` 与新增 v6→v7 用例）。
  - `Dfo.Server.Persistence.Tests` 中新增的玩家后台测试、`GmAuditTransactionTests`、`SchemaOwnershipTests` 通过。
  - 回归：`Dfo.Server.Host.Tests` 的 `Gm*` 测试，`Dfo.Server.Application.Tests` 中名字含 `Gm` 的测试。
- Result review: required — 本单元做数据迁移（错了会在用户库副本和线上库上留下与新建库不一致或丢默认值的结构，迁移后旧服务端无法回退），并定下 T-003–T-005 全部依赖的持久化接口与 record；接口形状或用量查询语义有偏差，会被后续三个单元层层建在上面，结构一致性测试只能发现"形状不一致"，发现不了"接口少了一个查询条件"或"用量统计口径错"。

## Detailed Plan

### 1. 表结构 v7（`CharacterDatabaseSchema`）

`Version` 改为 7。在 `TableDeclarations` 中：

- `gm_audit` 声明末尾追加两列（顺序固定，迁移步骤以同样顺序 `ADD COLUMN`）：
  `...,reason TEXT NOT NULL,actor_kind TEXT NOT NULL DEFAULT 'gm',account_id INTEGER)`。
- 新增三张表（`CREATE TABLE IF NOT EXISTS`，放在 GM 表附近即可，顺序无意义）：
  - `player_portal_settings(id INTEGER PRIMARY KEY CHECK(id=1), enabled INTEGER NOT NULL CHECK(enabled IN(0,1)), cera_enabled INTEGER NOT NULL CHECK(cera_enabled IN(0,1)), mail_enabled INTEGER NOT NULL CHECK(mail_enabled IN(0,1)), cera_per_grant INTEGER NOT NULL CHECK(cera_per_grant>=0), cera_per_day INTEGER NOT NULL CHECK(cera_per_day>=0), token_cera_per_grant INTEGER NOT NULL CHECK(token_cera_per_grant>=0), token_cera_per_day INTEGER NOT NULL CHECK(token_cera_per_day>=0), mail_gold_per_mail INTEGER NOT NULL CHECK(mail_gold_per_mail>=0), mails_per_day INTEGER NOT NULL CHECK(mails_per_day>=0), revision INTEGER NOT NULL CHECK(revision>=1))`
  - `player_portal_sessions(token_hash TEXT PRIMARY KEY, account_id INTEGER NOT NULL, created_at INTEGER NOT NULL, expires_at INTEGER NOT NULL)`
  - `player_portal_operations(account_id INTEGER NOT NULL, request_id TEXT NOT NULL, kind TEXT NOT NULL CHECK(kind IN('cera','mail')), currency TEXT, amount INTEGER NOT NULL CHECK(amount>=0), payload_hash TEXT NOT NULL, receipt_json TEXT NOT NULL, period_day TEXT NOT NULL, committed_at INTEGER NOT NULL, PRIMARY KEY(account_id,request_id))`
- `IndexDeclarations` 追加：
  - `ix_player_portal_sessions_account ON player_portal_sessions(account_id)`（按账号踢）
  - `ix_player_portal_sessions_expiry ON player_portal_sessions(expires_at)`（列有效会话、清过期，与 `ix_gm_sessions_expiry` 同理）
  - `ix_player_portal_operations_usage ON player_portal_operations(account_id,kind,period_day)`（SD-6 规定的每日用量索引）
  - `ix_player_portal_operations_time ON player_portal_operations(account_id,committed_at DESC,request_id DESC)`（记录分页，与 `ix_gm_cera_operations_time` 同理）

本地决定（后续单元按此使用，不再改）：

- **设置缺省不落行。** 与 `daily_refill_settings` 相同：表里没有行即为缺省值（总开关关、两个子开关开、六项限额 0），读出 `Revision = 0`；首次保存以期望版本 0 插入、版本变 1。这样新建库与迁移后的库数据完全一致，迁移步骤无需插入行，RQ-020 的"迁移后默认关闭"由"迁移不写行"直接成立。
- **只存成功的操作。** `player_portal_operations` 只写成功入账 / 投递的请求（重放只看成功记录、每日用量只计成功，SD-1 与 RQ-022）。失败只进审计。记录页（RQ-013）的"结果"取自回执内容，由 T-004/T-005 决定回执里放什么。
- **字段口径。** `kind`：`cera` 或 `mail`；`currency`：`cera` 时为 `Cera` / `TokenCera`，`mail` 时为 NULL；`amount`：`cera` 时为数量，`mail` 时为金币数。`request_id` 存 `Guid.ToString("N")`（与 GM 操作表一致）；`period_day` 存 `IDailyResetRuleService.PeriodDay` 的 `yyyy-MM-dd`（与 `ItemArrivalRuleService` 的日期写法一致）；时间存 Unix 毫秒。
- **回执不在本层定型。** 持久化只存取 `receipt_json` 字符串；回执类型与序列化由 T-004/T-005 的 Application 代码负责，避免本单元提前定下点券 / 邮件回执的形状。
- **玩家操作者的名字列。** 审计不另加登录名列：玩家操作的登录名（或脱敏占位）写入现有 `administrator_name`（即"操作者名称"），`administrator_id` 为空，`actor_kind='player'`，`account_id` 为账号 ID。本单元只提供存取能力，写入规则由 T-003 实现。
- 不加外键（与现有 `gm_*` 表一致；账号不会被物理删除）。

### 2. Data 层

新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalModels.cs`，命名空间 `Dfo.Server.Data.PlayerPortal`：

- `PlayerPortalLimits(long CeraPerGrant, long CeraPerDay, long TokenCeraPerGrant, long TokenCeraPerDay, long MailGoldPerMail, long MailsPerDay)`，含 `static None`（全 0）。
- `PlayerPortalSettings(bool Enabled, bool CeraEnabled, bool MailEnabled, PlayerPortalLimits Limits, long Revision)`，含 `static Default`（关 / 开 / 开 / `Limits.None` / `Revision 0`）。
- `PlayerPortalSession(string TokenHash, int AccountId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)`。
- `PlayerPortalSessionEntry(int AccountId, string LoginName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)`（管理员列表用，不含 hash）。
- `enum PlayerPortalOperationKind { Cera, Mail }`，存取时与 `cera` / `mail` 互转（转换放在 Persistence 内）。
- `PlayerPortalOperation(int AccountId, Guid RequestId, PlayerPortalOperationKind Kind, string? Currency, long Amount, string PayloadHash, string ReceiptJson, DateOnly PeriodDay, DateTimeOffset CommittedAt)`。
- `PlayerPortalUsage(long Count, long Amount)`（当日成功笔数与数量合计）。
- `PlayerPortalOperationPage(IReadOnlyList<PlayerPortalOperation> Items, long Total, int Page, int PageSize)`。

账号 ID 用 `int`，与 `GmCeraAccountIdentity` 等现有 GM 记录一致。

审计扩展在原文件 `Server/src/Dfo.Server.Data/Administration/GmAdministration.cs`：

- 新增 `public static class GmAuditActors { public const string Gm = "gm"; public const string Player = "player"; }`。
- `GmAuditRecord` 末尾追加 `string ActorKind = GmAuditActors.Gm, int? AccountId = null`。现有所有 `new(...)` 位置参数调用不变、语义仍为 `gm`。
- `GmAuditFilter` 末尾追加 `string? Actor = null`；新增 `static IReadOnlySet<string> Actors`（`gm`、`player`）；`IsValid` 同时校验 `Actor`。GmWeb 的 `actor` 查询参数解析、新动作名（`PlayerLogin` 等）加入 `Actions` 都属 T-003，本单元不动 `Actions`。

副作用说明：GM 审计接口直接序列化 `GmAuditRecord`，加字段后响应会多出 `actorKind`、`accountId`，这正是 SD-9 要求的增量（RQ-051 允许）；本单元以 GM 回归测试确认原字段不变，增量字段的 HTTP 断言留给 T-003。

### 3. Abstractions 接口

新建 `Server/src/Dfo.Server.Abstractions/PlayerPortal/IPlayerPortalPersistence.cs`（命名空间 `Dfo.Server.Abstractions.PlayerPortal`），单一接口，写方法都要求处于命令事务内（与 GM 持久化相同，由 `SqliteDatabase` 强制）：

```csharp
public interface IPlayerPortalPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken token);                         // 写意图，同 GmCera/DailyRefill
    ValueTask<PlayerPortalSettings> ReadSettingsAsync(CancellationToken token);    // 无行时返回 Default
    ValueTask<bool> SaveSettingsAsync(PlayerPortalSettings settings, long expectedRevision, CancellationToken token); // 版本不符返回 false
    ValueTask SaveSessionAsync(PlayerPortalSession session, CancellationToken token);
    ValueTask<PlayerPortalSession?> FindSessionAsync(string tokenHash, CancellationToken token);
    ValueTask<bool> DeleteSessionAsync(string tokenHash, CancellationToken token);
    ValueTask<int> DeleteAccountSessionsAsync(int accountId, CancellationToken token);
    ValueTask<int> DeleteAllSessionsAsync(CancellationToken token);
    ValueTask<int> DeleteExpiredSessionsAsync(DateTimeOffset now, CancellationToken token);
    ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> ListActiveSessionsAsync(DateTimeOffset now, CancellationToken token);
    ValueTask<PlayerPortalOperation?> FindOperationAsync(int accountId, Guid requestId, CancellationToken token);
    ValueTask SaveOperationAsync(PlayerPortalOperation operation, CancellationToken token);
    ValueTask<PlayerPortalUsage> UsageAsync(int accountId, PlayerPortalOperationKind kind, string? currency, DateOnly periodDay, CancellationToken token);
    ValueTask<PlayerPortalOperationPage> OperationsAsync(int accountId, PlayerPortalOperationKind? kind, int page, int pageSize, CancellationToken token);
}
```

语义：

- `SaveSettingsAsync`：照抄 `SqliteDailyRefillSettingsPersistence.SaveAsync` 的两句式（期望版本 0 时 `INSERT ... ON CONFLICT(id) DO NOTHING`，否则 `UPDATE ... WHERE revision=@revision`），保存后 `revision` 为期望值 + 1；忽略传入记录自身的 `Revision` 字段，只看 `expectedRevision`。
- `ListActiveSessionsAsync`：`expires_at > now`，`LEFT JOIN accounts` 取 `m_id`（取不到时为空串），按 `created_at DESC, account_id` 排序，每个会话一行。
- `UsageAsync`：`WHERE account_id=@a AND kind=@k AND period_day=@d AND (@c IS NULL OR currency=@c)`，返回 `COUNT(*)` 与 `COALESCE(SUM(amount),0)`。点券每日上限用 `Amount`（按币种传 `currency`），邮件每日封数用 `Count`（`currency` 传 null）。点券与代币券的每日上限各自独立（RQ-020/RQ-022），点券类调用方**必须**传入币种，按 account + kind + currency + period_day 汇总，不得以 null 把两种币种合计；测试须覆盖同账号同日两种币种互不计入。
- `OperationsAsync`：只含该账号，`kind` 为 null 时不筛；`ORDER BY committed_at DESC, request_id DESC`，`LIMIT/OFFSET` 与 GM 相同写法，同时返回总数。
- `SaveOperationAsync`：主键冲突直接抛（重放由调用方先 `FindOperationAsync` 判定，不在此吞掉）。

审计不加新接口：继续用 `IGmAdministrationPersistence.AppendAuditAsync` / `QueryAuditAsync`，只改实现（第 4 节）。这样 Application 测试中实现该接口的假对象不需改动。

### 4. Persistence 实现与注册

- 新建 `Server/src/Dfo.Server.Persistence/PlayerPortal/SqlitePlayerPortalPersistence.cs`：`internal sealed class SqlitePlayerPortalPersistence(SqliteDatabase database) : IPlayerPortalPersistence`，写法仿 `SqliteGmCeraPersistence`（`ExecuteAsync`、`ReadFirstAsync`、`ReadAsync` 组合；只拼接固定 SQL 片段，值一律参数化）。`AcquireWriteAsync` 用 `UPDATE player_portal_settings SET revision=revision WHERE 0;`。
- `PersistenceModule` 在 GM 持久化注册旁加：`services.TryAddSingleton<IPlayerPortalPersistence, SqlitePlayerPortalPersistence>();`。
- `SqliteGmAdministrationPersistence`：
  - `AppendAuditAsync` 的 INSERT 增加 `actor_kind`、`account_id` 两个参数（`AccountId` 为 null 时写 `DBNull`）。
  - `QueryAuditAsync`：`filter.Actor` 非空时加 `actor_kind=@actor`；SELECT 增加两列并填入记录。其余筛选与排序不变。

### 5. DbMigrate 步骤 `[6]`

新建 `Tool/DbMigrate/PlayerPortalUpgrade.cs`（`internal static class PlayerPortalUpgrade`，参照 `CharacterRelationsUpgrade` 与 `AdventureGroupColumnRetirement`），一个事务内：

1. 三张新表与四个新索引的语句，**从 v7 声明复制**（不读声明常量，理由同 `CharacterRelationsUpgrade` 的注释），全部 `IF NOT EXISTS`。
2. `Shape.HasColumn(connection, transaction, "gm_audit", "actor_kind")` 为假时 `ALTER TABLE gm_audit ADD COLUMN actor_kind TEXT NOT NULL DEFAULT 'gm';`——既有审计行由列缺省值统一归为 `gm`。
3. 同法检查并添加 `account_id INTEGER`。
4. 不写 `player_portal_settings` 任何行（缺省即关闭，见第 1 节）。

加列前查列是必要的：版本 0 的旧库先经 `LegacyUpgrade` 按**当前**声明建缺失表，若旧库里本来没有 `gm_audit`，这一步就已建出带新列的表，随后走到 `[6]` 时再无条件 `ADD COLUMN` 会报错。

`Migrator.Steps` 登记 `[6] = PlayerPortalUpgrade.Apply`。`Document/DatabaseSchemaMigration.md` 在"版本 5 → 6"后追加一条"版本 6 → 7（`PlayerPortalUpgrade.cs`）：新增玩家后台设置、玩家会话、玩家操作记录三张表及其索引；`gm_audit` 增加 `actor_kind`（缺省 `gm`，既有审计归为管理员操作）与 `account_id`；不写设置行，玩家后台缺省关闭；其它数据不变。迁移后 0.3.x 服务端无法再打开该库。"——措辞按该文档现有风格。

### 6. 测试

**`Tool/DbMigrate.Tests`**（新文件 `PlayerPortalMigrationTests.cs`，沿用 `SchemaVersionTests` 的临时库、`Execute` / `Scalar` / `ShapeOf` 写法；`ShapeOf` 若需共享，复制或提为同程序集内的 internal 帮助方法，择简）：

- `Version_six_upgrade_adds_the_player_portal_and_keeps_everything_else`：
  - 用 `Unversioned.Open` 建出当前形状后降为 v6：`DROP` 三张新表（索引随表删除）、`ALTER TABLE gm_audit DROP COLUMN account_id`、`DROP COLUMN actor_kind`、`PRAGMA user_version=6`。
  - 写入 v6 数据：账号（含 `cera`、`token_cera`）、两个角色（一个 `delete_flag=1`）、一封带附件的邮件、两条 `gm_audit`（不同 `action`/`outcome`、带 `administrator_id`/`administrator_name`）、一条 `gm_cera_operations`。
  - 迁移前记下这些表的行数与内容快照（`SELECT * ... ORDER BY` 拼成字符串即可）。
  - `Migrator.Migrate(path, backup:false)` 后断言：`(From,To)=(6,7)`；形状与新建库 `ShapeOf` 完全一致；`player_portal_settings`、`player_portal_sessions`、`player_portal_operations` 均为 0 行；经 `SqlitePlayerPortalPersistence.ReadSettingsAsync` 读出等于 `PlayerPortalSettings.Default`（`Enabled=false`、子开关 true、限额全 0、版本 0）；两条审计 `actor_kind='gm'`、`account_id IS NULL`，原有列值不变；其它表快照与迁移前一致；`Unversioned.Open` 能打开。
  - 再跑一次 `Migrator.Migrate`：`From==To`、`Backup` 为 null，形状与快照仍不变。
- `Version_six_step_is_safe_on_a_database_that_already_has_the_columns`：在已是 v7 形状的库上直接调 `PlayerPortalUpgrade.Apply` 两次，不抛、形状不变（覆盖 Legacy 路径先建好新列的情况）。
- 既有 `A_migrated_database_has_exactly_the_shape_the_server_builds`（v0 → 7）必须仍通过，它同时验证 Legacy 路径与 `[6]` 的衔接。

**`Server/tests/Dfo.Server.Persistence.Tests`**：

- 新文件 `PlayerPortal/SqlitePlayerPortalPersistenceTests.cs`，临时库与事务写法仿 `GmAuditTransactionTests`（`TestGameplay.RunAsync`、`TestRequired.TransactionScope()`、`scope.Begin()`，结束清池删目录）：
  - 设置：新库读出 `Default`；期望版本 0 保存成功、读回版本 1 与所存值；再用版本 0 保存返回 false 且值不变；用版本 1 保存成功、版本 2。
  - 会话：保存两个账号共三个会话（其一已过期）；按 hash 查到 / 查不到；`ListActiveSessionsAsync` 只含未过期两条且带登录名；`DeleteAccountSessionsAsync` 只删该账号并返回条数；`DeleteExpiredSessionsAsync` 只删过期；`DeleteAllSessionsAsync` 清空。
  - 操作记录：保存后按 (账号, 请求编号) 查回各字段一致（含 `PeriodDay`、`Currency` 为 null 的邮件记录）；同主键再存抛异常；不同账号同请求编号可共存；`UsageAsync` 只统计同账号、同类型、同币种、同日（准备跨日、跨币种、跨账号的干扰行），邮件按 `Count`；`OperationsAsync` 按时间倒序、分页总数正确、按类型筛选、不含其他账号。
  - 写方法在无事务时抛 `InvalidOperationException`（取一个方法验证即可，与 `AuditCannotWriteOutsideAnOwnedTransaction` 同理）。
- `Administration/GmAuditTransactionTests.cs` 补一个用例：不带操作者参数写入的记录读回 `ActorKind="gm"`、`AccountId=null`；带 `player` 与账号 ID 的记录读回一致；`new GmAuditFilter(Actor: "player")` 只返回玩家记录且总数正确，`Actor: "gm"` 只返回管理员记录；`GmAuditFilter.IsValid` 拒绝未知 `Actor`。
- `Schema/SchemaOwnershipTests.cs`：表数 116 → 119；`gm_audit` 的形状断言改为含 `account_id:INTEGER`、`actor_kind:TEXT` 的新排序串；为三张新表各加一条 `Assert.Contains`。

### 7. 执行顺序与检查方法

1. Data record 与审计扩展 → Abstractions 接口 → 声明 v7 → Persistence 实现与注册 → 审计实现改动。
2. DbMigrate 步骤与登记 → 迁移文档一条。
3. 测试（先 DbMigrate.Tests，再 Persistence.Tests）。
4. 检查（长命令后台运行）：
   - `dotnet build Server/86JP.Server.slnx`，确认 0 警告 0 错误。
   - `dotnet test Tool/DbMigrate.Tests`（全部）。
   - `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter "FullyQualifiedName~PlayerPortal|FullyQualifiedName~GmAudit|FullyQualifiedName~SchemaOwnership"`。
   - 回归：`dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~.Gm"`；`dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~Gm"`。
   - 若 `DbMigrate.Tests` 不在上述项目路径，按 `Server/86JP.Server.slnx` 中的实际路径运行。

## Result

- Outcome: done（执行第 1 轮，Revision 1；未提交，改动为工作区中下列文件）
- Changed:
  - 表结构 v7：`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`（`Version = 7`；`gm_audit` 末尾加 `actor_kind TEXT NOT NULL DEFAULT 'gm'`、`account_id INTEGER`；新增 `player_portal_settings`、`player_portal_sessions`、`player_portal_operations` 三表与四个索引）。
  - Data：新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalModels.cs`（`PlayerPortalLimits`、`PlayerPortalSettings.Default`、`PlayerPortalSession`、`PlayerPortalSessionEntry`、`PlayerPortalOperationKind`、`PlayerPortalOperation`、`PlayerPortalUsage`、`PlayerPortalOperationPage`）；`Server/src/Dfo.Server.Data/Administration/GmAdministration.cs` 加 `GmAuditActors`、`GmAuditRecord` 末尾可选参数 `ActorKind = "gm"`、`AccountId = null`，`GmAuditFilter` 加 `Actor` 与 `Actors`，`IsValid` 校验之。
  - Abstractions：新建 `Server/src/Dfo.Server.Abstractions/PlayerPortal/IPlayerPortalPersistence.cs`（按 Detailed Plan 第 3 节签名）。
  - Persistence：新建 `Server/src/Dfo.Server.Persistence/PlayerPortal/SqlitePlayerPortalPersistence.cs`；`PersistenceModule.cs` 注册；`Administration/SqliteGmAdministrationPersistence.cs` 审计读写两列并按 `actor_kind` 筛选。
  - 迁移：新建 `Tool/DbMigrate/PlayerPortalUpgrade.cs`，`Tool/DbMigrate/Migrator.cs` 登记 `[6]`；`Document/DatabaseSchemaMigration.md` 补"版本 6 → 7"。
  - 测试：新建 `Tool/DbMigrate.Tests/PlayerPortalMigrationTests.cs`（2 例）、`Server/tests/Dfo.Server.Persistence.Tests/PlayerPortal/SqlitePlayerPortalPersistenceTests.cs`（6 例）；`GmAuditTransactionTests.cs` 加 `AuditRecordsSayWhoActedAndFilterByActor`；`SchemaOwnershipTests.cs` 表数 116 → 119、`gm_audit` 新形状与三张新表断言。
  - GM 现有审计写入点、Application、GmWeb、Host 生产代码未改。
- Why:
  - 用量查询按 account + kind + currency + period_day 汇总：点券与代币券每日上限独立（RQ-020/RQ-022），接口注释写明点券必须传币种，测试固定同账号同日两种币种互不计入。
  - 迁移测试降 v6 时直接 `DROP COLUMN` 当前形状的两列，再与新建库比形状；另以对已有新列的库连跑两次 `PlayerPortalUpgrade.Apply` 覆盖 Legacy 路径先建好新列的情形。
- Checks:
  - `dotnet build Server/86JP.Server.slnx`：0 警告 0 错误。
  - `dotnet test Tool/DbMigrate.Tests --no-build`：30 通过 / 0 失败（含既有 v0→当前形状一致性用例与新增 2 例）。
  - `dotnet test Server/tests/Dfo.Server.Persistence.Tests --no-build --filter "FullyQualifiedName~PlayerPortal|FullyQualifiedName~GmAudit|FullyQualifiedName~SchemaOwnership"`：14 通过 / 0 失败。
  - 回归 `dotnet test Server/tests/Dfo.Server.Host.Tests --no-build --filter "FullyQualifiedName~.Gm|FullyQualifiedName~HttpAuditedFailureBoundary"`：141 通过 / 0 失败 / 11 跳过。
  - 回归 `dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter "FullyQualifiedName~Gm"`：18 通过 / 0 失败 / 11 跳过。
  - 两处跳过都是测试代码里原本就标了跳过的用例，需要真实 PVF / A21 客户端资源或属于库存类用例，与本单元无关。
- Remaining（不阻塞本单元，归后续单元）：
  - GM 审计接口响应现已自然多出 `actorKind`、`accountId`，HTTP 断言留给 T-003。
  - `actor` 查询参数解析与新动作名加入 `GmAuditFilter.Actions` 也在 T-003。

## Result Review

Independent Quick Review R1 — 审查对象：执行第 1 轮、契约 Revision 1、工作区未提交改动（基线 `b5942637d`；`git status` 中 8 个修改文件与 7 个新增路径，均在 Owned boundary 内）。依据：Task Unit + Code 准则；plan.md T-002 / SD-1 / SD-6 / SD-7 / SD-9；spec.md RQ-020、RQ-024、RQ-050、AC-050。

```yaml
kind: rks-review
verdict: approved
```

核查与证据：

- 迁移幂等与一致性：`PlayerPortalUpgrade` 建表 / 索引全为 `IF NOT EXISTS`，两列先 `Shape.HasColumn` 再 `ADD COLUMN`，同一事务。逐条比对复制语句与 v7 声明，3 张表与 4 个索引文本完全一致（形状比对不含 CHECK 约束，故另做了文本比对）。加列顺序 `actor_kind`→`account_id` 与声明一致，缺省值 `'gm'` 进入 `dflt_value` 被形状测试覆盖。旧库已有新列（Legacy 路径）由 `Version_six_step_leaves_a_database_that_already_has_the_columns_as_it_was` 连跑两次覆盖；另跑 Application 中会把库降到 `user_version=0` 再迁移的 `OutsideTransactionTests` / `TutorialCompletionTests` / `JoustIntegrationTests`，均通过。
- 缺省语义：不写设置行，`ReadSettingsAsync` 无行返回 `Default`（关 / 开 / 开 / 全 0 / 版本 0），迁移测试经真实持久化读出断言；保存沿用 `daily_refill_settings` 的两句式，只有恰好一句生效时返回 true，测试覆盖了版本 0 重复插入与过期版本两种拒绝。
- 用量口径：`UsageAsync` 按 account + kind + period_day + 可选 currency 汇总；测试含同账号同日 `Cera` 与 `TokenCera` 互不计入，以及跨日、跨账号、邮件按 `Count` 的干扰行。
- 后续单元的接口是否够用：重放（`FindOperationAsync` 带 `PayloadHash` / `ReceiptJson`）、每日用量、记录分页 + 类型筛选 + 只含本账号、踢单账号 / 全部、列有效会话（带登录名）、清过期会话、写意图 `AcquireWriteAsync` 均已具备，与 SD-1 判定顺序和 RQ-013 / RQ-043 对得上。
- GM 审计写入语义：`GmAuditRecord` 新参数带默认值，现有构造点未改，写入 `actor_kind='gm'`、`account_id=NULL`；读取与 `Actor` 筛选有测试；`GmWeb` 仍按 `(action, outcome)` 构造筛选，原筛选含义不变。
- 复跑检查：`dotnet build Server/86JP.Server.slnx` 0 警告 0 错误；`DbMigrate.Tests` 30/30；`Dfo.Server.Persistence.Tests` 全量 472/472（比 Result 中的过滤集更宽）；上述 Application 迁移相关用例 51 通过、28 为既有跳过。

非阻塞的遗留风险（留给 T-003 / T-004，本单元不必改）：

- `gm_audit.actor_kind` 没有 CHECK 约束，`GmAuditDomainService.AppendAsync` 也不校验 `ActorKind` 与 `AccountId` 是否搭配（例如 `player` 却没有账号 ID）。T-003 写玩家审计时应在写入点或领域服务中保证这一点。
- `UsageAsync(kind: Cera, currency: null)` 会把两种币种合计，目前只靠接口注释约束；T-004 的点券限额判定必须传币种，并用测试固定下来。
