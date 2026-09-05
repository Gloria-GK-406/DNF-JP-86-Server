---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 物品有自己的身份，槽只说它在哪

## Implementation Approach

Spec 的核心成果是**一件有身份的物品在任何位置变动之后仍是同一件，且它的可变状态随它走**。
支撑条件是槽对两类东西同一种形状、分类规则显式且每次写入都走、投影仍产出今天的字节。
外围保障是孤儿可查询、变化清单、既有机检。

**最小可行做法，三个已量出来的事实决定了它：**

1. **世界侧一张表，不是三张。** 三类有身份物品除「身份 + 82 字节 core」外各自需要的列是
   装备 0 列、时装 6 列、契约兽 8 列，**列名重合度为零**。设计文档「三张表各自列稠密」的理由
   因此不成立——三者真正共用的恰是身份与 core，而那就是装备所需的全部。裁定 M-2 对同一问题
   已有判词：SQLite 里变长记录中的 NULL 不占空间，不构成保留第二张表的理由。
   加上入场要一次性读整个库存，**一张表一次查询**优于三张表三次 join。
2. **`items` 就地变成世界表。** 它今天是 `(item_uid, owner, list, slot, core, avatar×6)`；
   加上契约兽 8 列，就是世界表。**位置另立一张槽表**，
   记 `(owner_scope, owner_id, list_type, slot_index, kind, id, count)`。
   位置四列在结算移走之前仍被读写，所以它们留到 T-011 才退役，不在 T-002。
   有身份的物品 `id` 指向 `items.item_uid`、`count` 恒为 1；
   无身份的东西 `id` 是模板号、`count` 是数量、**不在 `items` 里留行**。
3. **今天的结算办法可以原样保留，只是换了作用对象。** 现有 `DELETE`+`INSERT` 是为了绕开
   `UNIQUE(owner, list, slot)` 在批量结算中途的冲突。槽表仍有这个唯一键，
   但**槽行不再承载身份与载荷**——销毁重建一个槽行不损失任何东西。
   世界行则从生到灭只被 `INSERT` 一次、`DELETE` 一次。原本最难的机械点因此消解。

**最早的核心任务是 T-002**（世界表与槽表落地）。T-001 排在它之前，因为它不是新工作而是
**取证**：确认既有的入场字节守卫覆盖本次要碰的帧，并记下哪些哈希对应物品帧。
没有这一步，后面任何一次「逐值不变」的声称都没有当场可核的锚。

**契约兽的对外标识用单一窄空间。** 有身份的物品只在**被创建时**取得标识，而角色可持有的数量
由格子数封顶；创建次数远不足以耗尽 32 位有符号范围。分配处加一道守卫，
使服务器无法产出超范围的标识（RQ-008），而不是另设第二个标识——两个标识本身就是缺陷模子。

**命名不引入新的 `store` 读法。** `IItemDomainStore` 仍是事务结算的真源，
本轮它同时结算世界行与槽行；容器仍只管槽。世界侧不新增以 `DomainStore` 结尾的类型。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs` | 只读取证：确认既有帧哈希守卫覆盖物品与契约兽帧 | 现有判据 | 覆盖清单，记入 T-001 的取证 |
| BD-002 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | T-002 为 `items` 加入契约兽 8 列并新增槽表声明；T-011 再退役位置四列 | 今日 schema | 世界表与槽表的声明 |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` | 行网关契约随两张表的形状调整 | 今日行记录 | 世界行与槽行的行网关契约 |
| BD-004 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` | 读写两张表；对账扫描改以世界身份为准 | 行网关契约 | 读写实现 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/Content/` 与 `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` | 分类答案由档案给出：一件模板有没有身份、属于哪一类 | 档案 | 分类规则的唯一来源 |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs` 与 `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` | 结算写世界行与槽行；世界行不因移动而销毁 | 分类规则、行网关 | 新的结算语义 |
| BD-007 | `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs` | 投影携带稳定身份；槽的形状对两类东西统一 | 存储读出 | 领域与投影模型 |
| BD-008 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 与 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | 容器交出稳定身份；动词直接作用于主语，无凭证往返 | 领域模型、store | 容器契约与实现 |
| BD-009 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` 与 `InventoryEntryServices.cs` | 入场装配读两张表；对账扫描保持正确 | store、模型 | 入场库存投影 |
| BD-010 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemCore.cs` | core 的编解码随时装列迁出而调整 | core 定义 | 存储编解码 |
| BD-011 | `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`、`SqliteCharacterDomainStore.cs`、`Server/src/Dfo.Server.Abstractions/Characters/ICharacterDomainStore.cs` | 契约兽实例状态从 `character_creatures` 迁入世界表；保持清单顺序 | 世界表 | 契约兽状态的新住所 |
| BD-012 | `Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs` 与 `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs` | 契约兽可被装回其装备槽 | 档案槽位表 | 授权的行为变化一 |
| BD-013 | `AGENTS.md` | 记下建模、授权的变化、本轮没做的部分 | 完成后的实际状态 | 仓库约定 |
| BD-014 | `Server/tests/Dfo.Server.Persistence.Tests/`、`Server/tests/Dfo.Server.Application.Tests/`、`Server/tests/Dfo.Server.Protocol.Tests/` | 新增本 Spec 所需判据 | 各任务产出 | 判据 |
| BD-015 | `.rks-agent/tasks/items-own-their-identity/` 下的成果记录 | 行为变化清单与本轮结论 | 各任务证据 | 变化清单 |

## Ordered Tasks

### T-001: 确认逐值不变的字节锚已经存在并覆盖本次要碰的帧

- Covers: RQ-011
- Boundaries: BD-001
- Inputs: 既有入场帧哈希守卫
- Outputs: 覆盖清单：哪些哈希对应物品清单帧、契约兽清单帧、增量帧

#### Steps

1. 读既有帧哈希守卫，记下它逐帧比对的方式与哈希条数。
2. 指认清单中对应物品清单帧、契约兽清单帧、增量帧的条目。
3. 若增量路径未被该守卫覆盖，记下由哪一处既有判据覆盖它。
4. 把覆盖清单写进任务记录，作为后续每次「逐值不变」声称的锚。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `Non-command: 指认帧哈希守卫中对应物品清单、契约兽清单与增量帧的条目` | 每类帧至少一条对应条目，且条目位置可当场核 |
| CK-002 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 2131 通过 / 2 跳过 / 0 失败 |

### T-002: 世界表与槽表落地，尚无消费者

- Depends on: T-001
- Covers: RQ-001, RQ-004
- Boundaries: BD-002, BD-003, BD-004
- Inputs: 今日 schema 与行网关
- Outputs: 两张表的声明与读写通路

#### Steps

1. 在 schema 中把 `items` 扩成世界表：加入契约兽所需的 8 列。**位置四列此时保留原处**——结算今天仍在读写它们，而结算属 T-004；它们在 T-011 退役。
2. 声明槽表：`(owner_scope, owner_id, list_type, slot_index, kind, id, count)`，
   保留 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`。
3. 调整行网关契约与实现，使两张表都可读可写。
4. 为世界身份的分配加一道守卫，使其无法超出 32 位有符号范围。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `Non-command: 检查槽表对有身份与无身份两类内容使用同一组列` | 两类只在 `kind`/`id`/`count` 的取值上不同，列集合相同 |
| CK-004 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj --nologo` | 新增的读写判据通过 |
| CK-005 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 2131 通过 / 2 跳过 / 0 失败 |

### T-003: 分类规则归档案，并证明它在存储值与档案冲突时取档案

- Depends on: T-002
- Covers: RQ-006
- Boundaries: BD-005, BD-014
- Inputs: 档案内容源
- Outputs: 「一件模板有没有身份、属于哪一类」的单一答案来源

#### Steps

1. 让内容源回答分类问题，作为规则服务的一部分；它不解析身份、不读库。
2. 把分类规则接进每一条写入路径，使其成为可指认的单一处。
3. 新增判据：构造一件存储中记录的种类值与档案解释不一致的物品，
   断言分类结果取档案的解释。
4. **变异验证**：把分类改回读存储中的种类值，确认恰好该判据变红；还原后确认恢复全绿，
   并记录还原前后被改文件的哈希一致。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `Non-command: 指认分类规则的唯一生效处，并列出每一条写入路径如何经过它` | 单一处，且每条写入路径都经过 |
| CK-007 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 分类判据通过，全量不低于基线 |
| CK-008 | `Non-command: 变异验证——把分类改回读存储种类值，记录失败的判据名；还原后复跑` | 恰好分类判据变红；还原后全绿，文件哈希与变异前一致 |

### T-004: 结算写两张表，世界行不因移动而销毁

- Depends on: T-003
- Covers: RQ-001, RQ-002
- Boundaries: BD-006
- Inputs: 分类规则、两张表的行网关
- Outputs: 新的结算语义

#### Steps

1. 结算时按分类决定：有身份的物品在世界表落一行并在槽表引用它；无身份的东西只落槽行。
2. 位置改变只重建槽行；世界行从生到灭只被创建一次、删除一次。
3. 保留今日「谓词与唯一键同名」的槽行清理办法，因为槽行不再承载载荷。
4. 新增判据：同一件有身份的物品在移动前后身份值不变。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `Non-command: 检查移动路径不删除也不重建任何世界行` | 移动只触及槽行 |
| CK-010 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 身份不变判据通过，全量不低于基线 |

### T-005: 领域层与容器交出稳定身份，动词直接作用于主语

- Depends on: T-004
- Covers: RQ-002, RQ-003, RQ-004, RQ-005, RQ-016
- Boundaries: BD-007, BD-008, BD-014
- Inputs: 新的结算语义
- Outputs: 领域模型与容器契约

#### Steps

1. 让投影与容器交出跨写入不变的身份，取代今日那个每次写入都改变的存储标识。
2. 槽的领域表达对两类东西统一：类型、是什么、几个。
3. 检查每一个会改变物品状态的动词：它直接作用于主语，不把读到的状态带出再写回。
4. 新增机检：不存在以无身份东西的标识作为区分依据的生产代码。
5. 新增判据：一叠消耗品拆分后两格各自可用，且新格不携带任何继承自原堆的区分性标识。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `Non-command: 逐个检查改变物品状态的动词的取数方式` | 无凭证往返；每个动词直接作用于主语 |
| CK-012 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 拆堆判据与无身份机检通过，全量不低于基线 |

### T-006: 时装的六列迁入世界表

- Depends on: T-005
- Covers: RQ-003
- Boundaries: BD-007, BD-010, BD-009
- Inputs: 世界表
- Outputs: 时装细节随身份走

#### Steps

1. 把时装的六列从今日所在处迁到世界表，作为该身份的可变状态。
2. 保留今日线上对时装的两条强制：时装必须带细节、非时装不得带细节。
3. 保留入场装配对时装细节的既有校验中属于取值而非接线的那几条。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 入场帧哈希守卫仍绿，全量不低于基线 |
| CK-014 | `Non-command: 检查时装细节随身份而非随槽` | 移动一件时装后其细节逐字段不变 |

### T-007: 契约兽实例状态迁入世界表，清单顺序与标识宽度不变

- Depends on: T-006
- Covers: RQ-003, RQ-008
- Boundaries: BD-011, BD-014
- Inputs: 世界表、身份分配守卫
- Outputs: 契约兽实例状态的新住所

#### Steps

1. 把契约兽实例状态的 8 列迁入世界表，作为该身份的可变状态。
2. 保持契约兽清单帧的条目顺序与今日一致；该顺序今天由存储中的序号列决定，
   迁移后须有等价的稳定全序。
3. 保持清单帧的条目计数上限语义不变。
4. 新增判据：分配一个契约兽标识并写入清单帧，断言其可用 32 位有符号整数表示且写出不抛。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 契约兽清单帧哈希守卫仍绿，标识宽度判据通过 |
| CK-016 | `Non-command: 检查契约兽清单帧的条目顺序在迁移前后一致` | 顺序等价，且其依据可指认 |

### T-008: 无人引用的有身份物品可被查询

- Depends on: T-007
- Covers: RQ-007
- Boundaries: BD-004, BD-014
- Inputs: 两张表
- Outputs: 孤儿查询通路

#### Steps

1. 提供一条能列出「存在于世界表但没有任何槽引用」的读通路。
2. 新增判据：造出一件无人引用的物品，断言查询能列出它。
3. 在任务记录中如实写明：本轮**不**清扫它们，清扫属于后续成果。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-017 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 孤儿查询判据通过 |
| CK-018 | `Non-command: 复核今天是否存在能销毁有身份物品的生产通路` | 若存在，停下并提交阻塞报告；若不存在，如实记录该结论与其依据 |

### T-009: 契约兽取下之后能装回

- Depends on: T-008
- Covers: RQ-009
- Boundaries: BD-012, BD-014
- Inputs: 档案槽位表
- Outputs: 授权的行为变化一

#### Steps

1. 让契约兽这一类物品能解析到它今天所用的那个装备槽位。
2. 不改变该槽位的数值，也不改变其它槽位的归属。
3. 新增判据：取下再装回成功，且装回后身份不变。
4. **变异验证**：撤回该修复，确认恰好该判据变红；还原后确认恢复全绿。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 取下再装回判据通过，全量不低于基线 |
| CK-020 | `Non-command: 变异验证——撤回该修复并记录失败的判据名；还原后复跑` | 恰好该判据变红；还原后全绿 |

### T-010: 契约兽的种类判定与档案一致

- Depends on: T-009
- Covers: RQ-010
- Boundaries: BD-005, BD-014
- Inputs: 分类规则
- Outputs: 授权的行为变化二

#### Steps

1. 让契约兽物品的种类判定取档案对同一件物品的解释。
2. 新增判据：向存储与档案分别询问同一件物品的种类，断言一致。
3. 逐条记录这次改动使哪些既有判据的期望值发生变化，以及每一处为何属于被授权的行为变化。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-021 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 种类一致判据通过，全量不低于基线 |
| CK-022 | `Non-command: 列出因本任务而改变期望值的既有判据，逐条给出理由` | 每条都能归到 RQ-010，且清单之外没有其它期望值改动 |

### T-011: 退役旧形状，并证明词汇与机检未被放宽

- Depends on: T-010
- Covers: RQ-011, RQ-013, RQ-014
- Boundaries: BD-002, BD-004, BD-006, BD-011, BD-014
- Inputs: 前述任务的产出
- Outputs: 旧列与旧表退役

#### Steps

1. **先补完迁移，再删。** 位置四列自 T-004 起**并非无人读**——这句原文已被 TASK-08 证伪：
   `InventoryItemTables.cs:35`、`:57`、`:237` 三条 `SELECT … list_type, slot_index … FROM items` 仍在读它们，
   而 `item_slots` 生产上**无人读**、且有两条写入方完全绕过它
   （`CharacterRosterService.CreateAsync:214`、`InventoryEntryAssembly.SaveAsync:181/185`）。
   所以本任务的真实次序是：**(a) 让全部写入方都写槽行 → (b) 让位置读取改读槽表 → (c) 才能删那四列**。
   删已无人读的其余旧表（如 `character_creatures` 的声明）不受此限。
2. 逐个核对领域层中以 `DomainStore` 结尾的类型，确认每一个都是事务结算的真源。
3. 比对两处注入面机检的判定谓词与豁免清单，确认差异为空。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-023 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 入场帧哈希守卫仍绿，全量不低于基线 |
| CK-024 | `Non-command: 逐个核对以 DomainStore 结尾的类型` | 每一个都是事务结算的真源 |
| CK-025 | `git diff -- '*CommandHandlerArchitectureTests.cs' '*NotificationHandlerArchitectureTests.cs'` | 差异为空，或仅含不放宽判定谓词与豁免清单的改动 |

### T-012: 仓库约定与行为变化清单

- Depends on: T-011
- Covers: RQ-012, RQ-015
- Boundaries: BD-013, BD-015
- Inputs: 全部已完成任务的证据
- Outputs: 仓库约定的新小节与行为变化清单

#### Steps

1. 在仓库约定中记下这套建模：有身份的物品与位置分开、槽对两类东西同一种形状、
   分类由档案回答。
2. 如实写下本轮**没有**做的部分：不清扫孤儿、不启用无归属物品的用法、
   不做契约兽领域本身、不裁契约兽槽位、不动那五个含义未查清的列、旧库作废重建。
3. 汇总行为变化清单：仅 RQ-009 与 RQ-010 两处，每条指到具体代码位置。
4. 清单中的每个数字须当场复算，并按内容而非行号索引。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-026 | `Non-command: 对照代码阅读仓库约定新小节，逐个数字与陈述复核` | 记载与代码一致 |
| CK-027 | `Non-command: 逐条回到行为变化清单声称的位置核对` | 每条属实，且清单之外没有其它行为变化 |
| CK-028 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于基线计数，0 失败 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-002, T-004 | BD-002, BD-003, BD-004, BD-006 | CK-003, CK-009 |
| RQ-002 | T-004, T-005 | BD-006, BD-007, BD-008 | CK-010, CK-012 |
| RQ-003 | T-005, T-006, T-007 | BD-007, BD-010, BD-011 | CK-012, CK-014, CK-015 |
| RQ-004 | T-002, T-005 | BD-002, BD-007, BD-008 | CK-003, CK-012 |
| RQ-005 | T-005 | BD-008, BD-014 | CK-012 |
| RQ-006 | T-003 | BD-005, BD-014 | CK-006, CK-007, CK-008 |
| RQ-007 | T-008 | BD-004, BD-014 | CK-017, CK-018 |
| RQ-008 | T-007 | BD-002, BD-011, BD-014 | CK-015 |
| RQ-009 | T-009 | BD-012, BD-014 | CK-019, CK-020 |
| RQ-010 | T-010 | BD-005, BD-014 | CK-021, CK-022 |
| RQ-011 | T-001, T-011 | BD-001, BD-002, BD-004, BD-011 | CK-001, CK-002, CK-023 |
| RQ-012 | T-012 | BD-015 | CK-027 |
| RQ-013 | T-011 | BD-006 | CK-024 |
| RQ-014 | T-011 | BD-014 | CK-025 |
| RQ-015 | T-012 | BD-013 | CK-026 |
| RQ-016 | T-005 | BD-008 | CK-011 |

## Risks and Rollback

- Risk: **分类分错不报错。** 存储中记录的种类值与档案解释不一致，而投影照样拼得出线上字节，
  所以全量判据全绿不构成证据。缓解：T-003 把分类收成单一处并做变异验证（CK-008），
  T-010 再从另一侧钉住一致性（CK-021）。
- Risk: **契约兽清单帧的条目顺序今天由存储中的序号列决定，而该列在迁移后可能消失。**
  缓解：CK-016 单独核对顺序等价，且帧哈希守卫会在顺序改变时变红。
- Risk: **入场库存的对账扫描今天以每次写入都会改变的存储标识为准。** 改为稳定身份之后，
  该扫描的语义必须仍然正确，否则会误删。缓解：T-004 与 T-009 之间全量判据每次必须全绿，
  且 CK-018 复核销毁通路。
- Rollback: 任一任务未通过其检查时，只回滚该任务自己的改动到上一个全绿状态；
  不跨任务回滚，不触碰已声明的持久产物。

## Assumptions

- 本轮不会产生无人引用的物品：今天没有任何生产通路销毁一件有身份的物品。
  CK-018 在 T-008 复核这一点；**若被证伪，停下提交阻塞报告，不要把清理器塞进本轮**。
- 既有数据库作废重建，故不需要迁移机制，也不需要为旧形状保留兼容读路径。
- 契约兽沿用今天所用的那个装备槽位；本轮不裁它是否应当换一个。
- Task Unit 不建 Git 检查点、不提交。接受提交由工作区控制器在完整评审与新鲜验证之后做一次。
