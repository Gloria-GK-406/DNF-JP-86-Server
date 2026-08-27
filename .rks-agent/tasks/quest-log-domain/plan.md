---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 任务日志成为自己的领域

## Implementation Approach

Spec 的核心成果是**一个按角色取得、自己作判定的任务桶**；必要支撑是把任务状态迁出人物存储、
让已通关可记录、把两个写入方改经桶、理清读取路径；外围保障是线级与落库的等价证明、测试与文档。

最小实现路径是**照抄仓内已经建好的同构物：背包**。不需要发明形状，四件对应物都在：

| 背包 | 位置 | 任务要建的对应物 |
|---|---|---|
| `IItemContainer`（带动词、不缓存状态） | `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` | `IQuestLog` |
| `ICharacterContainerDomainService.FindContainer` | `Server/src/Dfo.Server.Abstractions/Inventory/ICharacterContainerDomainService.cs` | 按角色取桶的查找服务 |
| `IItemStore` / `SqliteItemStore`（facet） | `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs` | `IQuestStore` / `SqliteQuestStore` |
| `AmbientStoreAccess` 触达存储 | `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs` | 同一条触达路径 |

最早的核心任务是 T-002（自有存储）与 T-003（桶本身）——但 **T-001 必须在它们之前**，
且只有这一个前置：Spec 的 RQ-017／RQ-018／RQ-019 要求「改动前后逐字节一致」，
而「改动前」的字节一旦开始改代码就再也取不到。这是不可逆信息，不是保险。

次序的其余部分由「不留半迁中间态」决定：先让新家能住人（T-002、T-003），
再把两个写入方搬过去（T-004、T-005），确认无人再用旧分区之后才拆掉它（T-006）。
颠倒 T-005 与 T-006 会得到一个编译不过或写入丢失的中间状态。

**执行发生在上下文压缩之后的新会话**，因此本 Plan 自足：路径、基线取法、判据命令都写在里面，
不依赖对先前对话的记忆。三条已经裁决、不要在执行时重新讨论的取舍：

1. **不为在做的任务快照目标与奖励**——只存引用、进度、玩家的选择。理由：客户端读同一份 `.qst`，
   快照只会让两侧不一致。
2. **任务行迁出人物存储**（而不是留在原处加外壳）——用户已明确选定。
3. **不补齐「已通关且不可重复」「前置未满足」两条受理判据**——它们要等任务完成协议落地才有判据；
   本任务只让领域能表达已通关，不改变受理结论。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Quests/` | 新增：任务桶接口、任务实例 record、查找服务接口、领取结果与拒绝理由 | 背包同构物的形状 | `IQuestLog`、查找服务、值类型 |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Persistence/` 中任务存储契约 | 新增：任务自有存储的会话词汇 | `IStoreSession` 通用词汇 | 任务存储接口 |
| BD-003 | `Server/src/Dfo.Server.Persistence/Quests/` | 新增：任务存储的 SQLite 实现，作为既有参与者的又一个 facet | 连接字符串、`ISqliteStoreFacet` | `SqliteQuestStore` 及其会话 |
| BD-004 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 修改：已通关任务的结构；确认在做的任务与追踪选择的结构 | 现有 schema | 建库语句与结构清单 |
| BD-005 | `Server/src/Dfo.Server.Application/Quests/` | 新增：任务桶实现与查找服务实现 | BD-001、BD-002 | 领域实现 |
| BD-006 | `Server/src/Dfo.Server.Protocol/Game/QuestAcceptance/AcceptQuestCommand.cs` | 修改：受理判定移出，只剩编排 | BD-005 | 变薄的命令 handler |
| BD-007 | `Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs` | 修改：入场后修正任务进度改经任务桶 | BD-005 | 改经桶的写入方 |
| BD-008 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterStore.cs` | 修改：移除人物存储会话上的任务分区 | 无人再用该分区 | 不含任务的人物词汇 |
| BD-009 | `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterStore.cs` | 修改：随 BD-008 移除任务分区实现 | BD-008 | 对应实现 |
| BD-010 | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs` 与 `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs` 中任务相关部分 | 修改：入场投影的任务读取与任务存储的关系理清 | BD-003 | 单一读取路径，或一份说明 |
| BD-011 | `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Persistence/PersistenceModule.cs`、`Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 修改：注册新增的存储与领域服务 | BD-003、BD-005 | 组合根接线 |
| BD-012 | `Server/tests/Dfo.Server.Application.Tests/`、`Server/tests/Dfo.Server.Persistence.Tests/`、`Server/tests/Dfo.Server.Protocol.Tests/` 中任务相关测试 | 新增与修改：桶动作、领取判定、事务可见性与回滚、任务与物品同存亡 | BD-005、BD-003 | 自动化测试 |
| BD-013 | `Docs/ProtocolScaffoldDissonances.md`、`protocol/game/client-to-server/0x001F.md` | 修改：反映改动后的形状 | 改动结果 | 更新后的文档 |

## Ordered Tasks

### T-001: 取到「改动前」的基线，且证明它可复现

- Covers: RQ-017, RQ-018, RQ-019
- Boundaries: BD-012
- Inputs: 当前实现；`scratchpad/questprobe.py`、`entryprobe.py`、`cmpentry.py`
- Outputs: 领取应答与可接任务表的字节、入场批次逐帧记录、落库的槽位与进度值，全部存档

#### Steps

1. 用当前构建起服务端（`PVF_ARCHIVE_PATH` 指向 `D:\project\DNF\DNFClient-A21-protocol\Script.pvf`，
   `NETWORKING__ENABLED=true`，监听 7001/10011），库先清空。
2. 跑 `questprobe.py`，存下两次领取的应答与其后 `NOTI 0x0015` 的完整字节。
3. 跑 `entryprobe.py` 取入场批次，用 `cmpentry.py` 与参照会话比对，存下比对结果。
4. 从 `Data/dfo-server.db` 读出 `character_active_quests` 的全部行，存档。
5. 把三份存档放进任务文件夹的 `verification/` 下，命名指明是改动前基线。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `python questprobe.py 10011 0` | 输出 `matches the reference`；应答与其后的可接任务表字节已存档 |
| CK-002 | Non-command: 查看存档的入场批次比对结果 | 66 帧对 66 帧，差异帧集合已记录 |
| CK-003 | Non-command: 查看存档的活动任务行 | 槽位与进度值已记录 |

### T-002: 任务有自己的存储，且与物品同存亡

- Depends on: T-001
- Covers: RQ-003, RQ-014
- Boundaries: BD-002, BD-003, BD-004, BD-011, BD-012
- Inputs: `SqliteItemStore` 的 facet 写法；`SqliteDatabaseParticipant`
- Outputs: 任务自有存储；已通关任务的结构；同参与者的证据

#### Steps

1. 定义任务存储的会话词汇：在做的任务（按槽位寻址）、已通关的任务、追踪选择。
2. 按 `SqliteItemStore` 的样子实现 SQLite 侧：`StoreIdentity` 用同一个连接字符串，
   经 `transaction.Enlist` 取参与者后 `Facet(...)` 取自己的面。
3. 在 schema 中加入已通关任务的结构；确认在做的任务与 `character_quest_notify_selections` 现状可用。
4. 在组合根注册新存储。
5. 写测试：同一用例内既改任务又改物品，提交后两侧都在、回滚后两侧都不在。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1` | 全通过，含任务与物品同存亡一例 |
| CK-005 | Non-command: 检视任务存储实现 | `StoreIdentity` 与人物、物品相同；未新增可失败参与者 |

### T-003: 任务桶存在，作答并改变该角色的任务

- Depends on: T-002
- Covers: RQ-001, RQ-002, RQ-003, RQ-008, RQ-009, RQ-010, RQ-011, RQ-012, RQ-013
- Boundaries: BD-001, BD-005, BD-011, BD-012
- Inputs: BD-002 的存储词汇；`IItemContainer` 的动词形状
- Outputs: `IQuestLog`、任务实例 record、查找服务、领域测试

#### Steps

1. 定义 `IQuestLog`：作答在做的任务与已通关的任务；动作为领取、推进进度、完成、放弃、
   追踪与取消追踪。任务实例是不可变 record（任务标识、槽位、进度值，以及玩家的选择），
   **不含目标与奖励的内容快照**。
2. 定义按角色取桶的查找服务，形状对齐 `ICharacterContainerDomainService.FindContainer`。
3. 实现桶：**不持有状态**，每次作答都读穿到存储（对齐 `ICharacter` 注释给出的理由：
   缓存会与回滚过的用例失配）。经 `AmbientStoreAccess` 触达。
4. 在组合根注册。
5. 写测试：先写后读、用例内可见、回滚后消失、各动作各一例。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -m:1` | 全通过，含桶各动作与可见性、回滚两例 |
| CK-007 | Non-command: 检视桶的实现 | 无字段缓存任务状态 |

### T-004: 受理判定属于桶，不属于协议层

- Depends on: T-003
- Covers: RQ-004, RQ-005, RQ-006, RQ-007, RQ-021
- Boundaries: BD-001, BD-005, BD-006, BD-012
- Inputs: 现有 `AcceptQuestCommandHandler` 中的查重与占槽
- Outputs: 桶上的领取动作与领取结果；变薄的命令 handler

#### Steps

1. 把「同一任务是否已在做」「是否还有空槽」「取最低空槽」三条判定搬进桶的领取动作，
   槽位上限保持三十。
2. 领取结果给出成功（含槽位与初始进度）或失败（含可区分理由）；理由沿用现有拒绝码语义，
   **不改变对同一输入的结论**。
3. 命令 handler 只剩：取桶 → 取任务内容 → 让桶领 → 按内容发放随领取交付的道具 → 回包。
   发放仍在同一事务内，由 `UnitOfWorkBehavior` 保证全有或全无。
4. 补测试：成功、已在做、无空槽、槽位选取确定性。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet test tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -m:1` | 全通过 |
| CK-009 | Non-command: 检视命令 handler | 不含查重、占槽与槽位上限 |
| CK-010 | Non-command: 领取判定的测试清单 | 成功、已在做、无空槽、槽位确定性各有一例 |

### T-005: 入场后修正任务进度改经任务桶

- Depends on: T-003
- Covers: RQ-016
- Boundaries: BD-007, BD-012
- Inputs: `SelectCharacterPostEntryServices` 现有写法
- Outputs: 改经桶的写入方

#### Steps

1. 把该处对任务分区的读与写改为经由任务桶的作答与推进进度。
2. 保持其既有语义：只在计算出的进度与快照不同时才写，写入仍带「我看到的是什么」的守卫。
3. 跑覆盖该路径的既有测试。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `dotnet test tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -m:1` | 全通过，含入场后修正一例 |

### T-006: 人物领域不再谈论任务

- Depends on: T-004, T-005
- Covers: RQ-015
- Boundaries: BD-008, BD-009
- Inputs: 已无写入方使用旧分区
- Outputs: 不含任务的人物存储词汇

#### Steps

1. 确认全仓已无处引用人物存储会话上的任务分区。
2. 从人物存储会话接口与其 SQLite 实现中移除该分区。
3. 全量构建。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet build 86JP.Server.slnx -v q --nologo -m:1` | 0 错误 0 警告 |
| CK-013 | Non-command: 在人物存储会话接口中查找任务分区 | 不存在 |

### T-007: 同一份任务状态只有一个说得清的读法

- Depends on: T-006
- Covers: RQ-020
- Boundaries: BD-010, BD-013
- Inputs: 入场投影现有的任务读取
- Outputs: 单一读取路径，或一份写下来的结论

#### Steps

1. 判定入场投影的任务读取能否改为经由任务存储；能则改，读取路径合一。
2. 不能则保留两条，并按背包对同类双读的写法，在代码注释或文档中交代为何是两条、
   以及两者靠什么保持一致。
3. 跑入场相关测试。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | Non-command: 追查任务状态的读取路径 | 只有一条，或存在写下来的多条理由与一致性说明 |
| CK-015 | `dotnet test tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1` | 全通过 |

### T-008: 证明玩家侧什么都没变

- Depends on: T-007
- Covers: RQ-017, RQ-018, RQ-019
- Boundaries: BD-012
- Inputs: T-001 的三份基线存档
- Outputs: 改动后与基线的逐字节比对结果

#### Steps

1. 清库、用改动后的构建重起服务端，条件与 T-001 相同。
2. 重跑 `questprobe.py`，与 T-001 存档的应答与可接任务表字节逐字节比对。
3. 重跑 `entryprobe.py` 与 `cmpentry.py`，与 T-001 的逐帧比对结果比对。
4. 读出活动任务行，与 T-001 的槽位与进度值比对。
5. 三份比对结果存进 `verification/`。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | `python questprobe.py 10011 0` | 输出 `matches the reference`，且应答与可接任务表字节与 T-001 存档相同 |
| CK-017 | Non-command: 入场批次比对 | 与 T-001 的差异帧集合相同 |
| CK-018 | Non-command: 活动任务行比对 | 槽位与进度值与 T-001 相同 |
| CK-019 | `dotnet test 86JP.Server.slnx -m:1` | 全通过 |

### T-009: 文档反映改动后的形状

- Depends on: T-008
- Covers: RQ-022
- Boundaries: BD-013
- Inputs: 改动结果
- Outputs: 更新后的文档

#### Steps

1. 更新 `protocol/game/client-to-server/0x001F.md` 的服务端实现映射一节。
2. 更新 `Docs/ProtocolScaffoldDissonances.md` 中受影响的条目：任务读取路径若已合一则划掉，
   二十二条的收口前提若因已通关表出现而改变则据实修正。
3. 检查文档无 BOM、无 CRLF、相对链接可解析。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-020 | Non-command: 查阅更新后的文档 | 描述与改动后的实现一致 |
| CK-021 | Non-command: 文档卫生检查（BOM、CRLF、相对链接） | 无 BOM、无 CRLF、链接全部可解析 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-002 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-003 | T-002, T-003 | BD-002, BD-003, BD-004, BD-005 | CK-004, CK-006 |
| RQ-004 | T-004 | BD-001, BD-005 | CK-008, CK-010 |
| RQ-005 | T-004 | BD-005 | CK-010 |
| RQ-006 | T-004 | BD-005 | CK-010 |
| RQ-007 | T-004 | BD-005 | CK-010 |
| RQ-008 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-009 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-010 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-011 | T-003 | BD-001, BD-005 | CK-006 |
| RQ-012 | T-003 | BD-005 | CK-006 |
| RQ-013 | T-003 | BD-005 | CK-006 |
| RQ-014 | T-002 | BD-003 | CK-004, CK-005 |
| RQ-015 | T-006 | BD-008, BD-009 | CK-012, CK-013 |
| RQ-016 | T-005 | BD-007 | CK-011 |
| RQ-017 | T-001, T-008 | BD-012 | CK-001, CK-016 |
| RQ-018 | T-001, T-008 | BD-012 | CK-003, CK-018 |
| RQ-019 | T-001, T-008 | BD-012 | CK-002, CK-017 |
| RQ-020 | T-007 | BD-010, BD-013 | CK-014, CK-015 |
| RQ-021 | T-004 | BD-006 | CK-009 |
| RQ-022 | T-009 | BD-013 | CK-020, CK-021 |

## Risks and Rollback

- Risk: T-006 先于 T-004／T-005 执行会得到编译不过或写入丢失的中间态。缓解：依赖关系已声明，
  T-006 依赖二者。
- Risk: 新会话按用户原话把目标与奖励快照进任务实例，与已裁决的取舍相反。
  缓解：该取舍写在 Implementation Approach 的三条不可重议清单里，并作为 Spec 的非目标。
- Risk: T-001 的基线未取就开始改代码，则 RQ-017／018／019 永久失去判据。
  缓解：T-001 是唯一的前置任务，其余任务全部依赖它或依赖其后继。
- Rollback: 任一任务失败时，把工作树恢复到已接受基线；本任务未产生任何外部或远端变更，
  数据库为开发库可重建。

## Assumptions

- 执行开始时工作树有一个可证明的已接受基线；当前尚未提交的领取任务实现如何入基线由用户决定。
- 任务状态与物品状态同库，因而共用一个事务参与者；这是 T-002 的做法与 RQ-014 的前提。
- `scratchpad/` 下的 `questprobe.py`、`entryprobe.py`、`cmpentry.py` 在执行时仍可用；
  若不可用，T-001 与 T-008 需先重建等价探针，判据不变。
