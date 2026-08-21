---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 九个仓储退回单表读写并接入用例边界

## Implementation Approach

Spec 的核心成果是"领域层自己编排取数"。它的必要支撑是那些读写落在同一次用例的同一个连接与同一个事务里——没有这一条，按表拆分只会把今天 26 次独立提交放大成上百次，把一次撕裂的读放大成几十次。

所以最早的核心任务不是拆仓储，而是**让用例边界能容纳读**。今天 `SqliteDatabaseParticipant` 只在有暂存写时才 `BEGIN`，读走的是 autocommit——同一条连接也不给一致快照。这是 RQ-003 的直接障碍，且它先于一切拆分。

紧随其后的是**让写路径真的有边界**。全仓只有五条命令标了 `Transactional`，九个仓储的写多数发生在边界之外。先拆后补边界，会在中间留下"一次业务操作散成几十次提交"的窗口，且每个测试都会绿着穿过去。

拆分本身按仓储分批，每批独立可验证。入场装载那 33 张表单独成任务，并且**硬性要求先钉住今天的输出**：`SelectCharacterAcceptedTranscriptTests` 的 SHA256 金样覆盖的是最终帧，不是 `CharacterEntryStorageRecord` 本身，组装改序或漏搬造成的中间记录差异不一定被现有断言覆盖。

单表仓储的实现形状：每张表一个具名类型，各自持有自己的 SQL；共用的读写原语（读整型列表、读单行若干列、按键 upsert、按键删除）以显式表名与列名参数化，不引入反射或特性映射。这既满足"按表建立"，又不把同一段样板抄五十遍——用户要样板化正是为了后续能自动化，而不是为了手抄。

领域侧的编排以每张表一个注入点会造成三十余个构造参数，因此沿用本仓库既有形状：像 `ICharacterStoreSession` 用属性暴露各断面那样，用一个按聚合分组的表集合暴露各单表仓储。分组对象不含任何 SQL，只是取用点。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs` | 事务改为首次命令即开启（延迟事务），使读与写共处同一事务；`PrepareAsync` 不再重复开启 | 现有参与者 | 覆盖读的用例事务 |
| BD-002 | `Server/src/Dfo.Server.Persistence/Database/` 新增 | 按连接串的数据库取用点：入伙当前事务并交出本次用例的命令；无环境事务时开一次性连接供读取 | BD-001 | 仓储取用连接的唯一入口 |
| BD-003 | `Server/src/Dfo.Server.Persistence/Database/` 新增 | 单表读写原语：以显式表名列名参数化的读列表、读行、upsert、删除 | BD-002 | 单表仓储的共用底座 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Persistence/` 新增 | 单表仓储契约与按聚合分组的取用点契约 | 现有存储行类型 | 领域层可注入的单表读写契约 |
| BD-005 | `Server/src/Dfo.Server.Persistence/Accounts/` | 两个账号仓储拆成单表仓储 | BD-002, BD-003, BD-004 | 账号侧单表仓储 |
| BD-006 | `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterRepository.cs`, `SqliteCharacterPvpSkillProfileRepository.cs`, `SqliteDungeonPersistentEffectRecoveryRepository.cs`, `SqliteExpertSkillContractReconciliationRepository.cs` | 四个非入场仓储拆成单表仓储；移除表结构元数据探查 | BD-002, BD-003, BD-004 | 人物侧非入场单表仓储 |
| BD-007 | `Server/src/Dfo.Server.Persistence/Inventory/SqliteInventoryEntryRepository.cs`, `SqliteEntryInventoryMutationRepository.cs` | 两个背包仓储拆成单表仓储 | BD-002, BD-003, BD-004 | 背包侧单表仓储 |
| BD-008 | `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterEntryStateRepository.cs` | 33 张表拆成单表仓储，该类消失 | BD-002, BD-003, BD-004, T-003 的表征结论 | 入场装载的单表仓储 |
| BD-009 | `Server/src/Dfo.Server.Application/Characters/` 新增 | 入场装载的跨表组装，单一文件单一公开类型 | BD-008 | 领域层的入场装载编排 |
| BD-010 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`, `Server/src/Dfo.Server.Application/` 中无命令的持久化接缝 | 写路径进入用例边界：改动持久状态的命令标为 `Transactional`；无命令的接缝自开边界 | 现有命令注册 | 写路径的用例边界 |
| BD-011 | `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`, `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`, `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 注册单表仓储、分组取用点与领域编排 | BD-004..BD-009 | 可解析的组合根 |
| BD-014 | `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs`, `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs`, `AGENTS.md` | 会话结束的持久写与调用者的用例解耦；同步执行方式的记述 | BD-010 | 独立结算的会话结束写入 |
| BD-013 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 建库时设定日志模式，使读者与写者互不阻塞 | BD-001 | 可并存的读快照与写入 |
| BD-012 | `Server/tests/` | 随类型与依赖变化的机械调整，以及本 Spec 新增的证据用例 | 各任务产出 | 覆盖与证据 |

## Ordered Tasks

### T-001: 用例事务覆盖读

- Covers: RQ-002, RQ-003
- Boundaries: BD-001, BD-002, BD-013, BD-012
- Inputs: 现有 `SqliteDatabaseParticipant` 与 `DomainTransactionScope`
- Outputs: 首次命令即开启的用例事务；仓储取用连接的唯一入口

#### Steps

1. 先写失败用例：一次用例内跨两张表读取，读之间由另一条连接提交一处改动，断言后一次读仍见旧值。今天必红。
2. 再写失败用例：无环境事务时读取应放行而非抛出。
3. 把参与者的事务开启从 `PrepareAsync` 提前到首次 `CommandAsync`，`PrepareAsync` 遇已开启则不重复开启；无暂存写时的提交路径仍须结算已开启的读事务。
4. 新增按连接串的数据库取用点：有环境事务时入伙并交出本次用例的命令；无环境事务时开一次性连接、用毕即弃。
5. 复跑上述两条用例转绿。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj` | 新增的快照一致性用例与边界外读取用例通过 |
| CK-002 | Non-command: 把事务开启改回只在 `PrepareAsync` 发生 | 快照一致性用例转红 |
| CK-003 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 1011 通过 / 2 跳过 / 0 失败 |

### T-002: 写路径进入用例边界

- Depends on: T-001
- Covers: RQ-004
- Boundaries: BD-010, BD-014, BD-012
- Inputs: 现有命令注册与持久化接缝
- Outputs: 每条改动持久状态的路径都在一个用例边界内

#### Steps

1. 清点九个仓储的写方法及其调用链，定位每条写路径所属的命令或接缝。
2. 把改动持久状态的命令标为 `Transactional`。
3. 对无命令的接缝（例如断线清理），在该接缝处自开边界。
4. 写失败用例：一次用例改动两张不同的表，令其中一处在写出阶段失败，断言两处均不可见。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | Non-command: 清点九个仓储写方法的调用链 | 每条写路径都落在一个用例边界内，无遗漏项 |
| CK-005 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj` | 跨表全有或全无用例通过 |
| CK-006 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 |

### T-003: 钉住入场装载今天的输出

- Depends on: T-001
- Covers: RQ-008
- Boundaries: BD-012
- Inputs: 现有 `ICharacterEntryStateRepository.LoadAsync`
- Outputs: 覆盖 `CharacterEntryStorageRecord` 完整输出的表征用例

#### Steps

1. 以一条内容尽量饱满的角色数据为输入，取得今天 `LoadAsync` 的完整输出。
2. 写表征用例，逐字段断言该输出，覆盖五个子记录的全部字段与集合顺序。
3. 证伪：改动任一子记录的一个字段或集合顺序，该用例应转红。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj` | 表征用例通过 |
| CK-008 | Non-command: 改动任一子记录的一个字段值与一处集合顺序 | 两次改动各使表征用例转红 |

### T-004: 单表仓储形状与账号两侧

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-007
- Boundaries: BD-003, BD-004, BD-005, BD-011, BD-012
- Inputs: T-001 的取用点
- Outputs: 单表读写原语、单表仓储契约、账号侧单表仓储

#### Steps

1. 建立单表读写原语，以显式表名与列名参数化，不使用反射或特性映射。
2. 建立单表仓储契约与按聚合分组的取用点契约。
3. 拆 `SqliteAccountRepository` 与 `SqliteAccountEntryStateRepository`，账号侧跨表组装移入其调用的领域服务。
4. 更新组合根与随之变化的测试。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | Non-command: 清点账号侧每个仓储语句提及的表名 | 每个恰好一张 |
| CK-010 | Non-command: 清点账号侧仓储中新建连接与开启事务的处数 | 均为零 |
| CK-011 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 |

### T-005: 人物侧四个非入场仓储

- Depends on: T-004
- Covers: RQ-001, RQ-002, RQ-006
- Boundaries: BD-006, BD-011, BD-012
- Inputs: T-004 的原语与契约
- Outputs: 人物侧非入场单表仓储

#### Steps

1. 拆 `SqliteCharacterRepository`、`SqliteCharacterPvpSkillProfileRepository`、`SqliteDungeonPersistentEffectRecoveryRepository`、`SqliteExpertSkillContractReconciliationRepository`。
2. 移除 `TableExistsAsync` 及其对 `sqlite_master` 的查询。
3. 各自的跨表编排移入其调用的领域服务。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | Non-command: 清点这四个仓储拆出的每个仓储语句提及的表名 | 每个恰好一张 |
| CK-013 | Non-command: 在 `Server/src/Dfo.Server.Persistence/` 内检索 `sqlite_master` | 零命中 |
| CK-014 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 |

### T-006: 背包两个仓储

- Depends on: T-005
- Covers: RQ-001, RQ-002
- Boundaries: BD-007, BD-011, BD-012
- Inputs: T-004 的原语与契约
- Outputs: 背包侧单表仓储

#### Steps

1. 拆 `SqliteInventoryEntryRepository`（14 张表）与 `SqliteEntryInventoryMutationRepository`（3 张表）。
2. 其跨表编排移入其调用的领域服务。
3. 保持以字节形式存储的列的编解码位置不变。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | Non-command: 清点背包侧拆出的每个仓储语句提及的表名 | 每个恰好一张 |
| CK-016 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 |

### T-007: 入场装载拆分与组装上移

- Depends on: T-003, T-006
- Covers: RQ-001, RQ-002, RQ-005, RQ-007
- Boundaries: BD-008, BD-009, BD-011, BD-012
- Inputs: T-003 的表征用例、T-004 的原语与契约
- Outputs: 33 个单表仓储；领域层的入场装载编排

#### Steps

1. 把 33 张表各拆为一个单表仓储。
2. 把组装移入领域层，落在单一文件的单一公开类型内。
3. `SqliteCharacterEntryStateRepository` 随之消失。
4. 全程以 T-003 的表征用例作为不变性判据。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-017 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj` | T-003 的表征用例在搬家前后均通过 |
| CK-018 | Non-command: 清点入场装载所用每个仓储语句提及的表名 | 每个恰好一张 |
| CK-019 | Non-command: 检索把多张表的行拼成入场记录的代码 | 位于领域层，定义处唯一，持久化实现中零命中 |
| CK-020 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 |

### T-008: 行为保持证据

- Depends on: T-007
- Covers: RQ-007, RQ-008
- Boundaries: BD-012
- Inputs: 全部前序产出
- Outputs: 行为保持的完整证据

#### Steps

1. 清点全库新建连接、开启事务、表结构元数据查询、表与列声明的处数。
2. 确认协议编解码用例与整条帧序列金样未被改写且全部通过。
3. 以真实数据库对真实服务器走完道具路径并逐字节断言。
4. 汇总全量结果与基线的对照。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-021 | Non-command: 清点表与列声明处 | 仍只有既有唯一归属者，表数与列数与基线一致 |
| CK-022 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj` | 协议用例全部通过，断言未被改写 |
| CK-023 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 1011 通过 / 2 跳过 / 0 失败，增量全部为新增用例 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-004, T-005, T-006, T-007 | BD-005, BD-006, BD-007, BD-008 | CK-009, CK-012, CK-015, CK-018 |
| RQ-002 | T-001, T-004, T-005, T-006, T-007 | BD-001, BD-002, BD-005, BD-006, BD-007, BD-008 | CK-001, CK-010 |
| RQ-003 | T-001 | BD-001, BD-002, BD-013 | CK-001, CK-002 |
| RQ-004 | T-002 | BD-010, BD-014 | CK-004, CK-005 |
| RQ-005 | T-007 | BD-009 | CK-019 |
| RQ-006 | T-005 | BD-006 | CK-013 |
| RQ-007 | T-004, T-007, T-008 | BD-003, BD-008, BD-012 | CK-021 |
| RQ-008 | T-003, T-008 | BD-012 | CK-007, CK-008, CK-022, CK-023 |

## Risks and Rollback

- Risk: 用例事务从首次读起就开启，一次长用例会持有读锁直到结算，与另一条连接的写产生锁竞争。本项目并发水平低且只有一个写进程，判定可接受；若 T-001 的全量出现新的超时或忙等失败，改为仅在需要一致快照的读路径上开启。
- Risk: 把更多命令标为 `Transactional` 会改变其原子性。这是 RQ-004 所要的改变，但会使原先允许的部分成功变为全体回滚。逐条确认其调用方不依赖部分成功语义。
- Rollback: 各任务的产出彼此不重叠，可按任务单元逐个还原至基线。

## Assumptions

- 九个仓储触及的每张表都能被单独读写。若某张表的写必须与另一张表在同一语句内完成，该仓储脱离本 Spec 的接受边界，交回根控制器。
