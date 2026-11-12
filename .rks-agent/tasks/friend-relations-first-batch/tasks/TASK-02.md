# TASK-02 — 角色关系领域服务、存储与结构 v5

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-002、SD-1 – SD-4。交付关系契约、唯一领域实现与规则、SQLite 存储、结构版本 4→5 及迁移、按 ID 批量读取与按名精确查找角色身份的只读能力、被动性架构用例。覆盖 RQ-010 – RQ-020、RQ-060；验收 AC-010 – AC-015、AC-060。
- Owned boundary:
  - 新增：`Server/src/Dfo.Server.Abstractions/Relations/`、`Server/src/Dfo.Server.Application/Relations/`、`Server/src/Dfo.Server.Persistence/Relations/`。
  - 角色领域中新增的批量身份读取与按名查找：Abstractions 契约、Application 实现、Persistence 查询，以及现有 Characters 注册处。
  - 修改：`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`（表、索引、`Version`=5）、`PersistenceModule.cs`、`ApplicationModule.cs`（一行 `AddRelations()`），以及 `Tool/DbMigrate/`（`Steps[4]` 与步骤文件）、`Tool/DbMigrate.Tests/SchemaVersionTests.cs`。
  - 测试：`Server/tests/Dfo.Server.Application.Tests/Relations/`、`Server/tests/Dfo.Server.Persistence.Tests/Relations/`，以及架构用例（放在现有架构测试所在项目）。
  - `Document/DatabaseSchemaMigration.md` 补一行 4→5。
  - 不碰任何 Protocol 代码。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 通过；
  - `dotnet test` 覆盖 `Dfo.Server.Application.Tests`、`Dfo.Server.Persistence.Tests`、`Tool/DbMigrate.Tests`，以及架构测试所在项目，全部通过；
  - 新增用例覆盖 AC-011 – AC-015、AC-060 与被动性用例；
  - 现有 `SchemaOwnershipTests`、`RegistrationArchitectureTests`、`RequiredDependencyArchitectureTests` 通过。
- Result review: required — 它定义了 T-003 – T-006 都要消费的共享契约与数据格式，并引入不可轻易回退的结构迁移；接口形状或语义不对会被后续单元建在上面。

## Detailed Plan

**测试先行**（test-driven-development）：先写 Application 规则测试与存储往返测试，再实现。

**1. 契约**（`Abstractions/Relations/CharacterRelations.cs`，可按需拆文件）
- `enum CharacterRelationKind : byte { Friend = 1, Blacklist = 2 }`。数值即存储值，以后新增最近联系人、师徒时往后加（RQ-019）。
- `sealed record CharacterRelation(CharacterId Target, DateTimeOffset Since)`。
- `enum CharacterRelationAddOutcome { Added, AlreadyPresent, Self, TargetMissing, Full }`；`enum CharacterRelationRemoveOutcome { Removed, Missing }`。重复添加的协议含义（好友算成功、黑名单回 74）由协议层映射，领域只报告事实。
- `sealed record CharacterRelationPurge(IReadOnlyList<CharacterId> FriendObservers)`：删角色时，删除前把该角色加为好友的 owner。
- `ICharacterRelationQueryDomainService`：
  - `ListAsync(owner, kind)`：按加入时间、再按 target 排序，只含有效角色；
  - `ObserversOfAsync(target, kind)`：反查 owner，只含有效角色；
  - `ExistsAsync(owner, target, kind)`；
  - `int Limit(CharacterRelationKind kind)`。
- `ICharacterRelationDomainService`：
  - `AddAsync(owner, target, kind)`；
  - `RemoveAsync(owner, target, kind)`；
  - `PurgeAsync(character)`。
  - 三者都要求当前可写事务，否则抛错，写法同 `SouthernDaleQuestDomainService`。
- `ICharacterRelationDomainStore`：store 只做数据操作，不含规则。
  - `ListAsync`、`OwnersOfAsync`：两者都联 `characters` 过滤 `delete_flag=0`；
  - `ExistsAsync`、`CountAsync(owner, kind)`；
  - `InsertAsync(owner, target, kind, at)`：插入，返回是否新增；
  - `DeleteAsync`；
  - `PurgeAsync(character)`：返回删除前作为 Friend owner 的集合，再删两个方向的所有关系。

**2. 角色身份只读能力**
- 在角色领域新增只读的 `*DomainService` 契约（名称在实现时按 Characters 目录的命名习惯定）：
  - 按名精确查找有效角色（语义同 `SqliteMailboxRepository.FindRecipientAsync`：`COALESCE(name_bytes, CAST(name AS BLOB))` 精确匹配，`delete_flag=0`）；
  - 按 ID 集合批量读取有效角色的名字字节、等级、职业、转职；
  - 返回同一个公开身份 record。
- 1:1 对话与邮件现有的按名查找不在本单元改动，避免越界。

**3. 领域实现**（`Application/Relations/CharacterRelationDomainService.cs`）

一个类同时实现读写两个接口，注册时两个接口解析到同一单例（`RegistrationArchitectureTests` 要求每个服务类型只登记一种实现）。规则：
- owner == target → `Self`；
- target 不是有效角色 → `TargetMissing`，经第 2 项的批量读取判定；
- 已存在 → `AlreadyPresent`；
- 数量 ≥ `Limit` → `Full`；
- 否则插入 → `Added`。

上限：两种关系当前都取 64（常量集中一处）。TASK-01 的结论若不同，只改这一处。

构造依赖只有 store、角色身份只读服务、`IDomainTransactionAccessor`、`TimeProvider`。

**4. 存储与结构**
- `CharacterDatabaseSchema` 的 `TableDeclarations` 追加：
  ```
  CREATE TABLE IF NOT EXISTS character_relations(
    owner_id INTEGER NOT NULL REFERENCES characters(character_id),
    target_id INTEGER NOT NULL REFERENCES characters(character_id),
    kind INTEGER NOT NULL CHECK(kind BETWEEN 1 AND 255),
    created_at INTEGER NOT NULL,
    PRIMARY KEY(owner_id,kind,target_id),
    CHECK(owner_id<>target_id));
  CREATE INDEX IF NOT EXISTS ix_character_relations_target ON character_relations(target_id,kind);
  ```
- `Version = 5`。
- `SqliteCharacterRelationDomainStore`（internal），在 `PersistenceModule` 用 `TryAddSingleton` 注册。
- 迁移：`Tool/DbMigrate/Migrator.cs` 登记 `[4] = AddCharacterRelations`，在一个事务里执行与上面完全相同的建表、建索引语句。`SchemaVersionTests` 新增一个 4→5 用例：既有角色数据不变，新表可插入。结构一致性用例自然覆盖形状。

**5. 被动性架构用例**

用反射检查两个关系服务实现类的构造参数，只允许第 3 项列出的依赖类型；出现 `IEventService`、`INotificationBus`、`ICommandProjections`、发件箱或发送端时失败。附一个违规形状的反例，按仓库「新增机制须有拒绝违规的反例」的惯例。

**6. 注册**

新建 `Application/Relations/RelationsRegistration.cs`，提供 `AddRelations()`；`ApplicationModule` 加一行调用。

**7. 测试**
- Application：真实 SQLite 临时库（照 `SouthernDaleTests` 的夹具做法），覆盖：
  - AC-011：添加离线目标、重复添加、自己、不存在、已删除、上限；
  - AC-012：删除、反向关系独立；
  - AC-013：黑名单的同类分支，以及好友与黑名单互不影响；
  - AC-014：Purge 两个方向，返回观察者，删除后读不到；
  - 无可写事务时写操作抛错。
- Persistence：往返、重开库后一致（AC-015）、`delete_flag=1` 的目标不出现在 `ListAsync`／`OwnersOfAsync` 中。
- DbMigrate：AC-060。

## Result

- Outcome: done
- Round: 1, worker implementation. The owned change is the uncommitted diff listed under Changed.
- Changed:
  - Contracts: `Abstractions/Relations/CharacterRelations.cs` — kind, relation, add/remove outcomes, purge, query and write domain services, store.
  - Read-only character identity: `Abstractions/Characters/ICharacterIdentityQueryDomainService.cs`, holding `CharacterPublicIdentity`, `ICharacterIdentityQueryDomainService` (`FindByNameAsync`, batch `ReadAsync`) and `ICharacterIdentityRepository`.
  - Implementations:
    - `Application/Relations/CharacterRelationDomainService.cs` — one singleton behind both interfaces, with the limit 64 kept in one place.
    - `RelationsRegistration.cs`.
    - `Application/Characters/CharacterIdentityQueryDomainService.cs`.
    - `Persistence/Relations/SqliteCharacterRelationDomainStore.cs`.
    - `Persistence/Characters/SqliteCharacterIdentityRepository.cs`.
  - Schema version 5: the `character_relations` table and its target index; `Tool/DbMigrate/CharacterRelationsUpgrade.cs` registered as `[4]`; one more line in `Document/DatabaseSchemaMigration.md`.
  - Registration: `ApplicationModule` (`AddRelations()`), `CharactersRegistration`, `PersistenceModule`.
  - Tests:
    - `Application.Tests/Relations/` — rules, identity, and the passivity architecture test with counterexamples.
    - `Persistence.Tests/Relations/` and `Persistence.Tests/Characters/CharacterIdentityQueryTests.cs`.
    - `Tool/DbMigrate.Tests/SchemaVersionTests.cs` — the 4→5 case.
    - `Persistence.Tests/Schema/SchemaOwnershipTests.cs` — table count 116, plus a shape assertion.
- Why:
  - **Limit**: 64 is confirmed by `research/client-evidence.md` (RQ-002, `cmp eax,0x40`).
  - **Invalid owner**: an owner that is not a live character throws instead of returning an outcome. The owner is the caller's own selected character, so that case is a caller error.
  - **Passivity reachability check**: it treats `IDomainTransactionAccessor` as a leaf. Every write service reaches the use-case outbox through it, which is the caller's sending path, not one the relation service owns. A separate assertion proves that the path does exist when the walk does not stop there.
  - **Name lookup**: lifted into the character domain (`FindByNameAsync`). The existing name lookups in mail and 1:1 chat are left untouched.
- Checks:
  - `dotnet build Server/86JP.Server.slnx`: 0 warnings, 0 errors.
  - `dotnet test Tool/DbMigrate.Tests`: 27/27.
  - `Dfo.Server.Persistence.Tests`: 466 passed.
  - `Dfo.Server.Application.Tests`: 5930 passed, 594 skipped (existing skips; none of the new cases are skipped).
  - `Dfo.Server.Host.Tests`: 230 passed, 14 skipped.
  - Logs are in the session scratchpad `*.log`.
- Boundary note: `SchemaOwnershipTests.cs` was outside the planned owned boundary. It asserts the table count, so it must change with any new table.
- Quick review: approved (independent context). Two non-blocking points were applied before this commit:
  - both foreign keys of `character_relations` now carry `ON DELETE CASCADE`, in the schema and the 4→5 step alike;
  - the limit comment now cites `research/client-evidence.md` RQ-002.
  - Re-checked after the fixes: build 0 errors; DbMigrate.Tests 27/27; Persistence.Tests 466/466; Application.Tests Relations filter 31/31.
- Remaining:
  - Two name lookups now exist, the mail one and the character-domain one. Pointing mail and 1:1 chat at the character-domain one is a follow-up and is not in scope.
  - A future GM write path must check the owner before calling, because an invalid owner throws.
